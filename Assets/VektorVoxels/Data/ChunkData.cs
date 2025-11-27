using System;
using Unity.Collections;
using Unity.Mathematics;
using VektorVoxels.Chunks;
using VektorVoxels.Lighting;
using VektorVoxels.Voxels;

namespace VektorVoxels.Data {
    /// <summary>
    /// Burst-compatible chunk data container using NativeArrays.
    /// Replaces managed arrays in Chunk.cs for job system compatibility.
    ///
    /// Memory layout per chunk:
    /// - Voxels:     65,536 * 8 bytes  = 512 KB
    /// - SunLight:   65,536 * 2 bytes  = 128 KB
    /// - BlockLight: 65,536 * 2 bytes  = 128 KB
    /// - HeightMap:  256 * 2 bytes     = 512 bytes
    /// Total: ~768 KB per chunk
    /// </summary>
    public struct ChunkData : IDisposable {
        /// <summary>
        /// Chunk identifier in world grid coordinates.
        /// </summary>
        public int2 ChunkId;

        /// <summary>
        /// Voxel data array. 16x256x16 = 65,536 elements.
        /// Index formula: x + z * 16 + y * 256
        /// </summary>
        public NativeArray<VoxelData> Voxels;

        /// <summary>
        /// Sunlight values per voxel. Same indexing as Voxels.
        /// </summary>
        public NativeArray<VoxelColor> SunLight;

        /// <summary>
        /// Block light values per voxel. Same indexing as Voxels.
        /// </summary>
        public NativeArray<VoxelColor> BlockLight;

        /// <summary>
        /// Height map storing highest solid Y per column. 16x16 = 256 elements.
        /// Index formula: x + z * 16
        /// </summary>
        public NativeArray<HeightData> HeightMap;

        /// <summary>
        /// Current state in the chunk lifecycle.
        /// </summary>
        public ChunkState State;

        /// <summary>
        /// Current lighting pass (None, First, Second, Third).
        /// </summary>
        public LightPass CurrentPass;

        /// <summary>
        /// Whether this chunk has been modified since last save.
        /// </summary>
        public bool PersistenceDirty;

        /// <summary>
        /// Whether this chunk needs remeshing.
        /// </summary>
        public bool MeshDirty;

        /// <summary>
        /// Whether chunk was loaded with missing neighbors (edge of view).
        /// </summary>
        public bool PartialLoad;

        /// <summary>
        /// Standard chunk dimensions: 16 wide, 256 tall, 16 deep.
        /// </summary>
        public const int WIDTH = 16;
        public const int HEIGHT = 256;
        public const int DEPTH = 16;
        public const int VOXEL_COUNT = WIDTH * HEIGHT * DEPTH;  // 65,536
        public const int COLUMN_COUNT = WIDTH * DEPTH;          // 256

        /// <summary>
        /// Creates a new ChunkData with allocated NativeArrays.
        /// </summary>
        /// <param name="chunkId">The chunk's position in world grid.</param>
        /// <param name="allocator">Memory allocator (usually Persistent for long-lived data).</param>
        public static ChunkData Create(int2 chunkId, Allocator allocator = Allocator.Persistent) {
            var data = new ChunkData {
                ChunkId = chunkId,
                Voxels = new NativeArray<VoxelData>(VOXEL_COUNT, allocator),
                SunLight = new NativeArray<VoxelColor>(VOXEL_COUNT, allocator),
                BlockLight = new NativeArray<VoxelColor>(VOXEL_COUNT, allocator),
                HeightMap = new NativeArray<HeightData>(COLUMN_COUNT, allocator),
                State = ChunkState.Uninitialized,
                CurrentPass = LightPass.None,
                PersistenceDirty = false,
                MeshDirty = false,
                PartialLoad = false
            };

            // Initialize voxels to empty (air)
            var empty = VoxelData.Empty();
            for (int i = 0; i < VOXEL_COUNT; i++) {
                data.Voxels[i] = empty;
            }

            return data;
        }

        /// <summary>
        /// Converts 3D coordinates to flat voxel array index.
        /// Layout: X + Width * (Y + Height * Z) - matches VoxelUtility.VoxelIndex
        /// </summary>
        public static int VoxelIndex(int x, int y, int z) {
            return x + WIDTH * (y + HEIGHT * z);
        }

        /// <summary>
        /// Converts 3D coordinates to flat voxel array index.
        /// Layout: X + Width * (Y + Height * Z) - matches VoxelUtility.VoxelIndex
        /// </summary>
        public static int VoxelIndex(int3 pos) {
            return pos.x + WIDTH * (pos.y + HEIGHT * pos.z);
        }

        /// <summary>
        /// Converts 2D coordinates to flat heightmap array index.
        /// </summary>
        public static int HeightIndex(int x, int z) {
            return x + z * WIDTH;
        }

        /// <summary>
        /// Converts 2D coordinates to flat heightmap array index.
        /// </summary>
        public static int HeightIndex(int2 pos) {
            return pos.x + pos.y * WIDTH;
        }

        /// <summary>
        /// Checks if coordinates are within chunk bounds.
        /// </summary>
        public static bool InBounds(int x, int y, int z) {
            return x >= 0 && x < WIDTH &&
                   y >= 0 && y < HEIGHT &&
                   z >= 0 && z < DEPTH;
        }

        /// <summary>
        /// Checks if coordinates are within chunk bounds.
        /// </summary>
        public static bool InBounds(int3 pos) {
            return InBounds(pos.x, pos.y, pos.z);
        }

        /// <summary>
        /// Returns true if all NativeArrays are allocated.
        /// </summary>
        public bool IsCreated => Voxels.IsCreated &&
                                  SunLight.IsCreated &&
                                  BlockLight.IsCreated &&
                                  HeightMap.IsCreated;

        /// <summary>
        /// Disposes all NativeArrays. Must be called when chunk is no longer needed.
        /// </summary>
        public void Dispose() {
            if (Voxels.IsCreated) Voxels.Dispose();
            if (SunLight.IsCreated) SunLight.Dispose();
            if (BlockLight.IsCreated) BlockLight.Dispose();
            if (HeightMap.IsCreated) HeightMap.Dispose();
        }
    }
}
