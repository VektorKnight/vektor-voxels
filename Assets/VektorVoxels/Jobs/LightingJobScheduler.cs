using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Data;
using VektorVoxels.Lighting;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Jobs {
    /// <summary>
    /// Schedules and manages Burst-compiled lighting jobs.
    /// Handles sunlight initialization, block light sources, and propagation.
    ///
    /// Phase 1 (internal lighting):
    /// 1. SunlightColumnJob - Sets light above heightmap, finds cavern entries
    /// 2. BlockLightSourceJob - Finds light source blocks
    /// 3. LightPropagationJob - Propagates from all seeds
    ///
    /// Phase 2+ (neighbor lighting) still uses legacy system for now.
    /// </summary>
    public class LightingJobScheduler : IDisposable {
        // Reusable queues (allocated once, cleared between uses)
        private NativeQueue<LightNodeNative> _sunlightSeeds;
        private NativeQueue<LightNodeNative> _blockLightSeeds;
        private NativeQueue<LightNodeNative> _propagationQueue;
        private NativeQueue<int3> _removalQueue;
        private NativeQueue<LightNodeNative> _refillQueue;

        private bool _initialized;

        /// <summary>
        /// Whether the scheduler is ready.
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// Initializes the scheduler with reusable native containers.
        /// </summary>
        public void Initialize() {
            if (_initialized) return;

            // Allocate queues with reasonable initial capacity
            _sunlightSeeds = new NativeQueue<LightNodeNative>(Allocator.Persistent);
            _blockLightSeeds = new NativeQueue<LightNodeNative>(Allocator.Persistent);
            _propagationQueue = new NativeQueue<LightNodeNative>(Allocator.Persistent);
            _removalQueue = new NativeQueue<int3>(Allocator.Persistent);
            _refillQueue = new NativeQueue<LightNodeNative>(Allocator.Persistent);

            _initialized = true;
            Debug.Log("[LightingJobScheduler] Initialized");
        }

        /// <summary>
        /// Executes the first lighting pass (internal lighting) for a chunk.
        /// This is synchronous - blocks until complete.
        /// </summary>
        /// <param name="chunkId">Chunk ID to process</param>
        /// <returns>True if successful</returns>
        public bool ExecuteFirstPass(Vector2Int chunkId) {
            if (!_initialized) {
                Debug.LogError("[LightingJobScheduler] Not initialized");
                return false;
            }

            var store = VoxelWorld.Instance?.ChunkDataStore;
            if (store == null || store.IsDisposed) return false;

            var nativeId = new int2(chunkId.x, chunkId.y);
            if (!store.IsAllocated(nativeId)) return false;

            var chunkData = store.GetChunk(nativeId);

            // Clear queues
            _sunlightSeeds.Clear();
            _blockLightSeeds.Clear();
            _propagationQueue.Clear();

            // === Phase 1: Initialize sunlight columns ===
            var sunlightJob = new SunlightColumnJob {
                Voxels = chunkData.Voxels,
                HeightMap = chunkData.HeightMap,
                SunLight = chunkData.SunLight,
                CavernSeeds = _sunlightSeeds.AsParallelWriter()
            };
            var sunlightHandle = sunlightJob.Schedule(LightConstants.COLUMN_COUNT, 16);

            // === Phase 2: Find block light sources ===
            var blockLightJob = new BlockLightSourceJob {
                Voxels = chunkData.Voxels,
                BlockLight = chunkData.BlockLight,
                LightSources = _blockLightSeeds.AsParallelWriter()
            };
            var blockLightHandle = blockLightJob.Schedule(LightConstants.VOXEL_COUNT, 256);

            // Wait for both initialization jobs
            JobHandle.CombineDependencies(sunlightHandle, blockLightHandle).Complete();

            // === Phase 3: Propagate sunlight from cavern seeds ===
            // Transfer seeds to propagation queue
            while (_sunlightSeeds.Count > 0) {
                _propagationQueue.Enqueue(_sunlightSeeds.Dequeue());
            }

            if (_propagationQueue.Count > 0) {
                var sunPropagateJob = new LightPropagationJob {
                    Voxels = chunkData.Voxels,
                    LightMap = chunkData.SunLight,
                    PropagationQueue = _propagationQueue
                };
                sunPropagateJob.Schedule().Complete();
            }

            // === Phase 4: Propagate block light from sources ===
            _propagationQueue.Clear();
            while (_blockLightSeeds.Count > 0) {
                _propagationQueue.Enqueue(_blockLightSeeds.Dequeue());
            }

            if (_propagationQueue.Count > 0) {
                var blockPropagateJob = new LightPropagationJob {
                    Voxels = chunkData.Voxels,
                    LightMap = chunkData.BlockLight,
                    PropagationQueue = _propagationQueue
                };
                blockPropagateJob.Schedule().Complete();
            }

            // Update the chunk data in store
            store.UpdateChunk(nativeId, chunkData);

            return true;
        }

        /// <summary>
        /// Removes light from a position and re-propagates.
        /// Used when a light source is destroyed.
        /// </summary>
        /// <param name="chunkId">Chunk containing the light</param>
        /// <param name="localPos">Local position within chunk</param>
        /// <param name="isSunlight">True for sunlight, false for block light</param>
        public bool RemoveLight(Vector2Int chunkId, int3 localPos, bool isSunlight) {
            if (!_initialized) return false;

            var store = VoxelWorld.Instance?.ChunkDataStore;
            if (store == null || store.IsDisposed) return false;

            var nativeId = new int2(chunkId.x, chunkId.y);
            if (!store.IsAllocated(nativeId)) return false;

            var chunkData = store.GetChunk(nativeId);

            _removalQueue.Clear();
            _refillQueue.Clear();
            _propagationQueue.Clear();

            // Queue the initial removal position
            _removalQueue.Enqueue(localPos);

            // Execute removal
            var removalJob = new LightRemovalJob {
                Voxels = chunkData.Voxels,
                LightMap = isSunlight ? chunkData.SunLight : chunkData.BlockLight,
                RemovalQueue = _removalQueue,
                RefillQueue = _refillQueue
            };
            removalJob.Schedule().Complete();

            // Re-propagate from refill queue
            while (_refillQueue.Count > 0) {
                _propagationQueue.Enqueue(_refillQueue.Dequeue());
            }

            if (_propagationQueue.Count > 0) {
                var propagateJob = new LightPropagationJob {
                    Voxels = chunkData.Voxels,
                    LightMap = isSunlight ? chunkData.SunLight : chunkData.BlockLight,
                    PropagationQueue = _propagationQueue
                };
                propagateJob.Schedule().Complete();
            }

            store.UpdateChunk(nativeId, chunkData);
            return true;
        }

        /// <summary>
        /// Adds light at a position and propagates.
        /// Used when a light source is placed.
        /// </summary>
        public bool AddLight(Vector2Int chunkId, int3 localPos, VoxelColor lightColor, bool isSunlight) {
            if (!_initialized) return false;

            var store = VoxelWorld.Instance?.ChunkDataStore;
            if (store == null || store.IsDisposed) return false;

            var nativeId = new int2(chunkId.x, chunkId.y);
            if (!store.IsAllocated(nativeId)) return false;

            var chunkData = store.GetChunk(nativeId);

            _propagationQueue.Clear();
            _propagationQueue.Enqueue(new LightNodeNative(localPos, lightColor));

            var propagateJob = new LightPropagationJob {
                Voxels = chunkData.Voxels,
                LightMap = isSunlight ? chunkData.SunLight : chunkData.BlockLight,
                PropagationQueue = _propagationQueue
            };
            propagateJob.Schedule().Complete();

            store.UpdateChunk(nativeId, chunkData);
            return true;
        }

        /// <summary>
        /// Syncs lighting data from native arrays to managed chunk arrays.
        /// </summary>
        public void SyncToManagedChunk(Vector2Int chunkId) {
            var store = VoxelWorld.Instance?.ChunkDataStore;
            if (store == null || store.IsDisposed) return;

            if (!VoxelWorld.Instance.IsChunkInBounds(chunkId)) return;
            if (!VoxelWorld.Instance.IsChunkLoaded(chunkId)) return;

            var chunk = VoxelWorld.Instance.Chunks[chunkId.x, chunkId.y];
            if (chunk == null) return;

            var nativeId = new int2(chunkId.x, chunkId.y);
            store.CopySunLightTo(nativeId, chunk.SunLight);
            store.CopyBlockLightTo(nativeId, chunk.BlockLight);
        }

        /// <summary>
        /// Disposes all native containers.
        /// </summary>
        public void Dispose() {
            if (!_initialized) return;

            if (_sunlightSeeds.IsCreated) _sunlightSeeds.Dispose();
            if (_blockLightSeeds.IsCreated) _blockLightSeeds.Dispose();
            if (_propagationQueue.IsCreated) _propagationQueue.Dispose();
            if (_removalQueue.IsCreated) _removalQueue.Dispose();
            if (_refillQueue.IsCreated) _refillQueue.Dispose();

            _initialized = false;
        }
    }
}
