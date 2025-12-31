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
    /// Phase 2+ (neighbor lighting) uses BorderSeedJob for cross-chunk propagation.
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
        /// When true, uses 2 border passes instead of 3.
        /// Pass 3 is only needed when light from multiple sources converges through
        /// an intermediate chunk. In practice, 2 passes work for most scenarios.
        /// Set to false for strict correctness, true for ~33% faster lighting.
        /// </summary>
        public bool UseTwoPassLighting = false;

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
        /// Executes full lighting for multiple chunks in coordinated passes.
        /// This eliminates race conditions by ensuring all chunks complete each pass
        /// before any chunk starts the next pass.
        /// </summary>
        /// <param name="chunkIds">List of chunk IDs needing lighting</param>
        public void ExecuteFullLighting(List<int2> chunkIds) {
            if (!_initialized || chunkIds == null || chunkIds.Count == 0) return;

            // Pass 1: Internal lighting for all chunks
            foreach (var chunkId in chunkIds) {
                ExecuteFirstPass(new Vector2Int(chunkId.x, chunkId.y));
            }

            // Pass 2: Border propagation (reads pass 1 neighbor data)
            foreach (var chunkId in chunkIds) {
                ExecuteBorderPass(chunkId);
            }

            // Pass 3: Border propagation again (convergence for multi-source scenarios)
            // Can be skipped when UseTwoPassLighting is true for ~33% faster lighting.
            if (!UseTwoPassLighting) {
                foreach (var chunkId in chunkIds) {
                    ExecuteBorderPass(chunkId);
                }
            }

            // Sync all to managed arrays
            foreach (var chunkId in chunkIds) {
                SyncToManagedChunk(new Vector2Int(chunkId.x, chunkId.y));
            }
        }

        /// <summary>
        /// Executes border propagation pass for a single chunk.
        /// Reads light values from neighbor borders and propagates into this chunk.
        /// </summary>
        private void ExecuteBorderPass(int2 chunkId) {
            var store = VoxelWorld.Instance?.ChunkDataStore;
            if (store == null || store.IsDisposed) return;
            if (!store.IsAllocated(chunkId)) return;

            var chunkData = store.GetChunk(chunkId);

            // Get neighbor flags and data
            var neighborFlags = GetNeighborFlags(chunkId, store);
            if (neighborFlags == NeighborFlagsNative.None) return;

            // Process sunlight borders
            ExecuteBorderPassForLightType(chunkId, chunkData, store, neighborFlags, true);

            // Process block light borders
            ExecuteBorderPassForLightType(chunkId, chunkData, store, neighborFlags, false);

            // Update chunk in store
            store.UpdateChunk(chunkId, chunkData);
        }

        private void ExecuteBorderPassForLightType(int2 chunkId, ChunkData chunkData,
            ChunkDataStore store, NeighborFlagsNative neighborFlags, bool isSunlight) {

            _propagationQueue.Clear();

            var currentLight = isSunlight ? chunkData.SunLight : chunkData.BlockLight;

            // Get neighbor light arrays. Use currentLight as placeholder for missing neighbors
            // (the job checks flags before accessing, but Unity validates all arrays at schedule time).
            var northLight = GetNeighborLightOrPlaceholder(chunkId, 0, 1, store, isSunlight, currentLight);
            var eastLight = GetNeighborLightOrPlaceholder(chunkId, 1, 0, store, isSunlight, currentLight);
            var southLight = GetNeighborLightOrPlaceholder(chunkId, 0, -1, store, isSunlight, currentLight);
            var westLight = GetNeighborLightOrPlaceholder(chunkId, -1, 0, store, isSunlight, currentLight);

            // Run border seed job
            var borderJob = new BorderSeedJob {
                Voxels = chunkData.Voxels,
                CurrentLight = currentLight,
                NeighborNorthLight = northLight,
                NeighborEastLight = eastLight,
                NeighborSouthLight = southLight,
                NeighborWestLight = westLight,
                NeighborFlags = neighborFlags,
                Seeds = _propagationQueue
            };
            borderJob.Schedule().Complete();

            // Propagate seeds if any were found
            if (_propagationQueue.Count > 0) {
                var propagateJob = new LightPropagationJob {
                    Voxels = chunkData.Voxels,
                    LightMap = currentLight,
                    PropagationQueue = _propagationQueue
                };
                propagateJob.Schedule().Complete();
            }
        }

        /// <summary>
        /// Gets neighbor flags indicating which neighbors are available.
        /// </summary>
        private NeighborFlagsNative GetNeighborFlags(int2 chunkId, ChunkDataStore store) {
            var flags = NeighborFlagsNative.None;

            // North (+Z)
            var northId = new int2(chunkId.x, chunkId.y + 1);
            if (store.IsAllocated(northId)) flags |= NeighborFlagsNative.North;

            // East (+X)
            var eastId = new int2(chunkId.x + 1, chunkId.y);
            if (store.IsAllocated(eastId)) flags |= NeighborFlagsNative.East;

            // South (-Z)
            var southId = new int2(chunkId.x, chunkId.y - 1);
            if (store.IsAllocated(southId)) flags |= NeighborFlagsNative.South;

            // West (-X)
            var westId = new int2(chunkId.x - 1, chunkId.y);
            if (store.IsAllocated(westId)) flags |= NeighborFlagsNative.West;

            return flags;
        }

        /// <summary>
        /// Gets a neighbor's light array, or a placeholder if not available.
        /// Unity Jobs validates all arrays at schedule time, so we can't pass default/uninitialized arrays.
        /// The placeholder won't be read because BorderSeedJob checks NeighborFlags first.
        /// </summary>
        private NativeArray<VoxelColor> GetNeighborLightOrPlaceholder(int2 chunkId, int dx, int dz,
            ChunkDataStore store, bool isSunlight, NativeArray<VoxelColor> placeholder) {

            var neighborId = new int2(chunkId.x + dx, chunkId.y + dz);

            if (!store.IsAllocated(neighborId)) {
                // Return placeholder - BorderSeedJob will skip via flags anyway
                return placeholder;
            }

            var neighborData = store.GetChunk(neighborId);
            return isSunlight ? neighborData.SunLight : neighborData.BlockLight;
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
