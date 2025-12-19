using System;
using Unity.Collections;
using Unity.Mathematics;
using VektorVoxels.Chunks;
using VektorVoxels.Lighting;
using VektorVoxels.Voxels;

namespace VektorVoxels.Data {
    /// <summary>
    /// Central storage for all chunk data using NativeContainers for Unity Jobs.
    /// Works alongside the managed Chunk arrays, with data synced between systems.
    ///
    /// Memory budget (64x64 world, all chunks loaded):
    /// - Per chunk: ~768 KB
    /// - Max theoretical: 768 KB * 4096 = 3 GB (never all loaded at once)
    /// - Typical (view distance 10): ~400 chunks = 307 MB
    /// </summary>
    public class ChunkDataStore : IDisposable {
        /// <summary>
        /// Maximum world size in chunks per axis.
        /// </summary>
        public readonly int2 MaxChunks;

        /// <summary>
        /// All chunk data indexed by flat array index.
        /// Index = chunkId.x + chunkId.y * MaxChunks.x
        /// </summary>
        private NativeArray<ChunkData> _chunks;

        /// <summary>
        /// Tracks which chunk slots are currently allocated.
        /// </summary>
        private NativeArray<bool> _allocated;

        /// <summary>
        /// Lookup from chunk ID to flat index.
        /// Redundant with direct calculation but useful for job safety.
        /// </summary>
        private NativeHashMap<int2, int> _chunkLookup;

        /// <summary>
        /// Chunks needing terrain generation (indices into _chunks).
        /// </summary>
        public NativeList<int> ChunksNeedingGeneration;

        /// <summary>
        /// Chunks needing lighting (indices into _chunks).
        /// </summary>
        public NativeList<int> ChunksNeedingLighting;

        /// <summary>
        /// Chunks needing meshing (indices into _chunks).
        /// </summary>
        public NativeList<int> ChunksNeedingMeshing;

        /// <summary>
        /// Number of currently allocated chunks.
        /// </summary>
        public int AllocatedCount { get; private set; }

        /// <summary>
        /// Whether this store has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Creates a new ChunkDataStore for a world of the given size.
        /// </summary>
        public ChunkDataStore(int2 maxChunks) {
            MaxChunks = maxChunks;
            var totalSlots = maxChunks.x * maxChunks.y;

            _chunks = new NativeArray<ChunkData>(totalSlots, Allocator.Persistent);
            _allocated = new NativeArray<bool>(totalSlots, Allocator.Persistent);
            _chunkLookup = new NativeHashMap<int2, int>(totalSlots, Allocator.Persistent);

            ChunksNeedingGeneration = new NativeList<int>(256, Allocator.Persistent);
            ChunksNeedingLighting = new NativeList<int>(256, Allocator.Persistent);
            ChunksNeedingMeshing = new NativeList<int>(256, Allocator.Persistent);

            AllocatedCount = 0;
        }

        /// <summary>
        /// Converts a chunk ID to flat array index.
        /// </summary>
        public int ChunkIdToIndex(int2 chunkId) {
            return chunkId.x + chunkId.y * MaxChunks.x;
        }

        /// <summary>
        /// Converts a flat array index to chunk ID.
        /// </summary>
        public int2 IndexToChunkId(int index) {
            return new int2(index % MaxChunks.x, index / MaxChunks.x);
        }

        /// <summary>
        /// Checks if a chunk ID is within world bounds.
        /// </summary>
        public bool IsInBounds(int2 chunkId) {
            return chunkId.x >= 0 && chunkId.x < MaxChunks.x &&
                   chunkId.y >= 0 && chunkId.y < MaxChunks.y;
        }

        /// <summary>
        /// Checks if a chunk is allocated at the given ID.
        /// </summary>
        public bool IsAllocated(int2 chunkId) {
            if (!IsInBounds(chunkId)) return false;
            return _allocated[ChunkIdToIndex(chunkId)];
        }

        /// <summary>
        /// Allocates a new chunk at the given ID.
        /// Returns true if successful, false if already allocated or out of bounds.
        /// </summary>
        public bool AllocateChunk(int2 chunkId) {
            if (!IsInBounds(chunkId)) return false;

            var index = ChunkIdToIndex(chunkId);
            if (_allocated[index]) return false;

            _chunks[index] = ChunkData.Create(chunkId);
            _allocated[index] = true;
            _chunkLookup.Add(chunkId, index);
            AllocatedCount++;

            return true;
        }

        /// <summary>
        /// Deallocates the chunk at the given ID.
        /// Returns true if successful, false if not allocated or out of bounds.
        /// </summary>
        public bool DeallocateChunk(int2 chunkId) {
            if (!IsInBounds(chunkId)) return false;

            var index = ChunkIdToIndex(chunkId);
            if (!_allocated[index]) return false;

            var chunk = _chunks[index];
            chunk.Dispose();
            _chunks[index] = default;
            _allocated[index] = false;
            _chunkLookup.Remove(chunkId);
            AllocatedCount--;

            return true;
        }

        /// <summary>
        /// Gets the chunk data at the given ID.
        /// Throws if not allocated.
        /// </summary>
        public ChunkData GetChunk(int2 chunkId) {
            if (!IsInBounds(chunkId))
                throw new ArgumentOutOfRangeException(nameof(chunkId), "Chunk ID out of bounds");

            var index = ChunkIdToIndex(chunkId);
            if (!_allocated[index])
                throw new InvalidOperationException($"Chunk {chunkId} is not allocated");

            return _chunks[index];
        }

        /// <summary>
        /// Gets the chunk data at the given flat index.
        /// </summary>
        public ChunkData GetChunkByIndex(int index) {
            return _chunks[index];
        }

        /// <summary>
        /// Tries to get the chunk data at the given ID.
        /// Returns false if not allocated.
        /// </summary>
        public bool TryGetChunk(int2 chunkId, out ChunkData chunk) {
            if (!IsInBounds(chunkId) || !_allocated[ChunkIdToIndex(chunkId)]) {
                chunk = default;
                return false;
            }

            chunk = _chunks[ChunkIdToIndex(chunkId)];
            return true;
        }

        /// <summary>
        /// Updates a chunk in the store. Use after modifying a local copy.
        /// </summary>
        public void UpdateChunk(int2 chunkId, ChunkData chunk) {
            if (!IsInBounds(chunkId))
                throw new ArgumentOutOfRangeException(nameof(chunkId));

            var index = ChunkIdToIndex(chunkId);
            if (!_allocated[index])
                throw new InvalidOperationException($"Chunk {chunkId} is not allocated");

            _chunks[index] = chunk;
        }

        /// <summary>
        /// Copies voxel data from a managed array into the chunk's NativeArray.
        /// Syncs data between Chunk managed arrays and NativeArrays for Unity Jobs.
        /// </summary>
        public void CopyVoxelsFrom(int2 chunkId, VoxelData[] source) {
            if (!IsAllocated(chunkId)) return;

            var index = ChunkIdToIndex(chunkId);
            _chunks[index].Voxels.CopyFrom(source);
        }

        /// <summary>
        /// Copies voxel data from the chunk's NativeArray to a managed array.
        /// Syncs data between NativeArrays and Chunk managed arrays.
        /// </summary>
        public void CopyVoxelsTo(int2 chunkId, VoxelData[] destination) {
            if (!IsAllocated(chunkId)) return;

            var index = ChunkIdToIndex(chunkId);
            _chunks[index].Voxels.CopyTo(destination);
        }

        /// <summary>
        /// Copies sun light data from a managed array into the chunk's NativeArray.
        /// </summary>
        public void CopySunLightFrom(int2 chunkId, VoxelColor[] source) {
            if (!IsAllocated(chunkId)) return;

            var index = ChunkIdToIndex(chunkId);
            _chunks[index].SunLight.CopyFrom(source);
        }

        /// <summary>
        /// Copies sun light data from the chunk's NativeArray to a managed array.
        /// </summary>
        public void CopySunLightTo(int2 chunkId, VoxelColor[] destination) {
            if (!IsAllocated(chunkId)) return;

            var index = ChunkIdToIndex(chunkId);
            _chunks[index].SunLight.CopyTo(destination);
        }

        /// <summary>
        /// Copies block light data from a managed array into the chunk's NativeArray.
        /// </summary>
        public void CopyBlockLightFrom(int2 chunkId, VoxelColor[] source) {
            if (!IsAllocated(chunkId)) return;

            var index = ChunkIdToIndex(chunkId);
            _chunks[index].BlockLight.CopyFrom(source);
        }

        /// <summary>
        /// Copies block light data from the chunk's NativeArray to a managed array.
        /// </summary>
        public void CopyBlockLightTo(int2 chunkId, VoxelColor[] destination) {
            if (!IsAllocated(chunkId)) return;

            var index = ChunkIdToIndex(chunkId);
            _chunks[index].BlockLight.CopyTo(destination);
        }

        /// <summary>
        /// Copies height map data from a managed array into the chunk's NativeArray.
        /// </summary>
        public void CopyHeightMapFrom(int2 chunkId, HeightData[] source) {
            if (!IsAllocated(chunkId)) return;

            var index = ChunkIdToIndex(chunkId);
            _chunks[index].HeightMap.CopyFrom(source);
        }

        /// <summary>
        /// Copies height map data from the chunk's NativeArray to a managed array.
        /// </summary>
        public void CopyHeightMapTo(int2 chunkId, HeightData[] destination) {
            if (!IsAllocated(chunkId)) return;

            var index = ChunkIdToIndex(chunkId);
            _chunks[index].HeightMap.CopyTo(destination);
        }

        /// <summary>
        /// Gets the raw NativeArray of chunks for job access.
        /// Use with caution - jobs must use appropriate read/write attributes.
        /// </summary>
        public NativeArray<ChunkData> GetChunksForJobs() {
            return _chunks;
        }

        /// <summary>
        /// Gets the chunk lookup map for job access.
        /// </summary>
        public NativeHashMap<int2, int> GetLookupForJobs() {
            return _chunkLookup;
        }

        /// <summary>
        /// Clears all work lists. Call at start of each frame before populating.
        /// </summary>
        public void ClearWorkLists() {
            ChunksNeedingGeneration.Clear();
            ChunksNeedingLighting.Clear();
            ChunksNeedingMeshing.Clear();
        }

        /// <summary>
        /// Disposes all NativeContainers. Must be called when store is no longer needed.
        /// </summary>
        public void Dispose() {
            if (IsDisposed) return;
            IsDisposed = true;

            // Dispose each allocated chunk's internal arrays
            for (int i = 0; i < _chunks.Length; i++) {
                if (_allocated[i]) {
                    var chunk = _chunks[i];
                    chunk.Dispose();
                }
            }

            // Dispose container arrays
            if (_chunks.IsCreated) _chunks.Dispose();
            if (_allocated.IsCreated) _allocated.Dispose();
            if (_chunkLookup.IsCreated) _chunkLookup.Dispose();
            if (ChunksNeedingGeneration.IsCreated) ChunksNeedingGeneration.Dispose();
            if (ChunksNeedingLighting.IsCreated) ChunksNeedingLighting.Dispose();
            if (ChunksNeedingMeshing.IsCreated) ChunksNeedingMeshing.Dispose();
        }
    }
}
