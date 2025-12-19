using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Data;
using VektorVoxels.Generation;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Jobs {
    /// <summary>
    /// Schedules and manages Burst-compiled terrain generation jobs.
    /// Coordinates between the Chunk managed arrays and NativeArrays for Unity Jobs.
    ///
    /// Lifecycle:
    /// 1. Initialize with layer configuration (converts to NativeArrays)
    /// 2. ScheduleGeneration() queues a job for a chunk
    /// 3. Update() checks for completed jobs and syncs data
    /// 4. Dispose() cleans up NativeArrays
    /// </summary>
    public class TerrainJobScheduler : IDisposable {
        // Configuration (persistent for lifetime of scheduler)
        private NativeArray<TerrainLayerData> _layerData;
        private int _maxHeight;
        private float _noiseScale;
        private bool _initialized;

        // Active jobs
        private readonly List<ScheduledTerrainJob> _activeJobs = new List<ScheduledTerrainJob>();
        private readonly List<ScheduledTerrainJob> _completedJobs = new List<ScheduledTerrainJob>();

        /// <summary>
        /// Whether the scheduler is ready to accept jobs.
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// Number of jobs currently in flight.
        /// </summary>
        public int ActiveJobCount => _activeJobs.Count;

        /// <summary>
        /// Initializes the scheduler with layer configuration.
        /// Must be called from main thread before scheduling any jobs.
        /// </summary>
        /// <param name="layers">Voxel layers from top (grass) to bottom (bedrock)</param>
        /// <param name="noiseScale">Noise frequency (default 0.02)</param>
        public void Initialize(VoxelLayer[] layers, float noiseScale = 0.02f) {
            if (_initialized) {
                Debug.LogWarning("[TerrainJobScheduler] Already initialized. Call Dispose() first.");
                return;
            }

            _noiseScale = noiseScale;
            _maxHeight = 0;

            // Convert VoxelLayer[] to TerrainLayerData[] with pre-resolved VoxelData
            _layerData = new NativeArray<TerrainLayerData>(layers.Length, Allocator.Persistent);
            for (int i = 0; i < layers.Length; i++) {
                var layer = layers[i];
                var voxelDef = VoxelTable.GetVoxelDefinition(layer.VoxelId);
                var voxelData = voxelDef.GetDataInstance();

                _layerData[i] = new TerrainLayerData(voxelData, layer.Thickness);
                _maxHeight += layer.Thickness;
            }
            _maxHeight -= 1; // Match PerlinGenerator behavior

            _initialized = true;
            Debug.Log($"[TerrainJobScheduler] Initialized with {layers.Length} layers, maxHeight={_maxHeight}");
        }

        /// <summary>
        /// Schedules terrain generation for a chunk using the native data store.
        /// </summary>
        /// <param name="chunkId">Chunk ID (grid coordinates)</param>
        /// <param name="onComplete">Callback when generation completes (called on main thread)</param>
        /// <returns>True if job was scheduled, false if scheduler not ready</returns>
        public bool ScheduleGeneration(Vector2Int chunkId, Action<Vector2Int> onComplete = null) {
            if (!_initialized) {
                Debug.LogError("[TerrainJobScheduler] Cannot schedule - not initialized");
                return false;
            }

            var store = VoxelWorld.Instance?.ChunkDataStore;
            if (store == null) {
                Debug.LogError("[TerrainJobScheduler] Cannot schedule - ChunkDataStore not available");
                return false;
            }

            var nativeId = new int2(chunkId.x, chunkId.y);
            if (!store.IsAllocated(nativeId)) {
                Debug.LogError($"[TerrainJobScheduler] Cannot schedule - chunk {chunkId} not allocated");
                return false;
            }

            // Get chunk data from store
            var chunkData = store.GetChunk(nativeId);

            // Calculate world offset for noise sampling
            var chunkPos = VoxelWorld.Instance.ChunkPosFromId(chunkId);
            var worldOffset = new int2(chunkPos.x * 16, chunkPos.y * 16);

            // Schedule the job
            var job = new TerrainGenerationJob {
                ChunkWorldOffset = worldOffset,
                NoiseScale = _noiseScale,
                MaxHeight = _maxHeight,
                Layers = _layerData,
                Voxels = chunkData.Voxels,
                HeightMap = chunkData.HeightMap
            };

            // Process all 256 columns (16x16)
            var handle = job.Schedule(256, 16);

            _activeJobs.Add(new ScheduledTerrainJob {
                ChunkId = chunkId,
                Handle = handle,
                OnComplete = onComplete
            });

            return true;
        }

        /// <summary>
        /// Checks for completed jobs and processes callbacks.
        /// Call from main thread Update().
        /// </summary>
        public void Update() {
            if (!_initialized) return;

            _completedJobs.Clear();

            // Check which jobs have completed
            for (int i = _activeJobs.Count - 1; i >= 0; i--) {
                var scheduledJob = _activeJobs[i];
                if (scheduledJob.Handle.IsCompleted) {
                    // Complete the job (blocks until done, but should be instant if IsCompleted)
                    scheduledJob.Handle.Complete();
                    _completedJobs.Add(scheduledJob);
                    _activeJobs.RemoveAt(i);
                }
            }

            // Process completed jobs
            foreach (var completed in _completedJobs) {
                // Sync native data back to managed arrays in Chunk
                SyncToManagedChunk(completed.ChunkId);

                // Invoke callback
                completed.OnComplete?.Invoke(completed.ChunkId);
            }
        }

        /// <summary>
        /// Blocks until all active jobs complete.
        /// Use sparingly - prefer async completion via Update().
        /// </summary>
        public void CompleteAll() {
            foreach (var job in _activeJobs) {
                job.Handle.Complete();
                SyncToManagedChunk(job.ChunkId);
                job.OnComplete?.Invoke(job.ChunkId);
            }
            _activeJobs.Clear();
        }

        /// <summary>
        /// Syncs generated data from NativeArrays to the Chunk's managed arrays.
        /// </summary>
        private void SyncToManagedChunk(Vector2Int chunkId) {
            var store = VoxelWorld.Instance?.ChunkDataStore;
            if (store == null) return;

            // Get the managed Chunk
            if (!VoxelWorld.Instance.IsChunkInBounds(chunkId)) return;
            if (!VoxelWorld.Instance.IsChunkLoaded(chunkId)) return;

            var chunk = VoxelWorld.Instance.Chunks[chunkId.x, chunkId.y];
            if (chunk == null) return;

            var nativeId = new int2(chunkId.x, chunkId.y);

            // Copy native data to managed arrays
            store.CopyVoxelsTo(nativeId, chunk.VoxelData);
            store.CopyHeightMapTo(nativeId, chunk.HeightMap);
        }

        /// <summary>
        /// Disposes all NativeArrays. Call when scheduler is no longer needed.
        /// </summary>
        public void Dispose() {
            // Complete any remaining jobs
            CompleteAll();

            if (_layerData.IsCreated) {
                _layerData.Dispose();
            }

            _initialized = false;
        }

        /// <summary>
        /// Tracks an in-flight terrain generation job.
        /// </summary>
        private struct ScheduledTerrainJob {
            public Vector2Int ChunkId;
            public JobHandle Handle;
            public Action<Vector2Int> OnComplete;
        }
    }
}
