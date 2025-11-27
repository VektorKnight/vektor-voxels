using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VektorVoxels.Chunks;
using VektorVoxels.Voxels;

namespace VektorVoxels.Jobs {
    /// <summary>
    /// Burst-compiled terrain generation job.
    /// Processes all 256 columns in a chunk in parallel using Perlin noise.
    ///
    /// Usage:
    /// 1. Create TerrainLayerData array from VoxelTable (on main thread)
    /// 2. Schedule job with chunk's NativeArrays
    /// 3. After completion, heightmap is computed automatically
    /// </summary>
    [BurstCompile]
    public struct TerrainGenerationJob : IJobParallelFor {
        /// <summary>
        /// Chunk world offset for noise sampling (chunkId * 16).
        /// </summary>
        [ReadOnly] public int2 ChunkWorldOffset;

        /// <summary>
        /// Noise frequency. Lower = larger terrain features.
        /// Default 0.02 gives good results.
        /// </summary>
        [ReadOnly] public float NoiseScale;

        /// <summary>
        /// Maximum terrain height (sum of all layer thicknesses - 1).
        /// </summary>
        [ReadOnly] public int MaxHeight;

        /// <summary>
        /// Layer configuration. Ordered from top (grass) to bottom (bedrock).
        /// </summary>
        [ReadOnly] public NativeArray<TerrainLayerData> Layers;

        /// <summary>
        /// Output voxel data array. 65,536 elements (16x256x16).
        /// </summary>
        [NativeDisableParallelForRestriction]
        [WriteOnly] public NativeArray<VoxelData> Voxels;

        /// <summary>
        /// Output heightmap array. 256 elements (16x16).
        /// Stores highest solid Y per column.
        /// </summary>
        [NativeDisableParallelForRestriction]
        [WriteOnly] public NativeArray<HeightData> HeightMap;

        // Constants matching ChunkData
        private const int WIDTH = 16;
        private const int HEIGHT = 256;
        private const int DEPTH = 16;

        /// <summary>
        /// Execute is called once per column (256 total invocations per chunk).
        /// index = x + z * 16
        /// </summary>
        public void Execute(int index) {
            int x = index % WIDTH;
            int z = index / WIDTH;

            // Sample Perlin noise at world position
            float2 worldPos = new float2(
                (ChunkWorldOffset.x + x) * NoiseScale,
                (ChunkWorldOffset.y + z) * NoiseScale
            );

            // noise.cnoise returns [-1, 1], convert to [0, 1]
            float noiseValue = (noise.cnoise(worldPos) + 1f) * 0.5f;
            noiseValue = math.clamp(noiseValue, 0f, 1f);

            int surfaceHeight = (int)math.round(noiseValue * MaxHeight);

            // Write heightmap
            HeightMap[index] = new HeightData((byte)surfaceHeight, 1);

            // Fill column with layers from top to bottom
            int layerY = surfaceHeight;
            for (int layerIdx = 0; layerIdx < Layers.Length; layerIdx++) {
                var layer = Layers[layerIdx];

                if (layerY < 0) break;

                // Fill this layer's voxels
                for (int dy = 0; dy < layer.Thickness; dy++) {
                    int y = layerY - dy;
                    if (y < 0) continue;
                    if (y >= HEIGHT) continue;

                    int voxelIndex = VoxelIndex(x, y, z);
                    Voxels[voxelIndex] = layer.VoxelInstance;
                }

                layerY -= layer.Thickness;
            }
        }

        /// <summary>
        /// Converts 3D coordinates to flat voxel array index.
        /// Layout: X + Width * (Y + Height * Z) - matches VoxelUtility.VoxelIndex
        /// </summary>
        private static int VoxelIndex(int x, int y, int z) {
            return x + WIDTH * (y + HEIGHT * z);
        }
    }

    /// <summary>
    /// Pre-computed layer data for Burst job.
    /// Contains the actual VoxelData instance (resolved from VoxelTable on main thread).
    /// </summary>
    public struct TerrainLayerData {
        /// <summary>
        /// Pre-resolved VoxelData for this layer.
        /// </summary>
        public VoxelData VoxelInstance;

        /// <summary>
        /// Thickness of this layer in voxels.
        /// </summary>
        public int Thickness;

        public TerrainLayerData(VoxelData voxelInstance, int thickness) {
            VoxelInstance = voxelInstance;
            Thickness = thickness;
        }
    }
}
