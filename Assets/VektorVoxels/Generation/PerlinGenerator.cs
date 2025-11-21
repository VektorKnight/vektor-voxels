using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Generation {
    /// <summary>
    /// Terrain generator using 2D Perlin noise as heightmap.
    /// Samples continuous noise space for seamless chunk transitions.
    /// Fills voxels in layers from top to bottom (grass -> dirt -> stone -> bedrock).
    /// </summary>
    public class PerlinGenerator : ITerrainGenerator {
        private readonly VoxelLayer[] _layers;
        private readonly int _maxHeight;
        private readonly float _noiseScale;

        /// <param name="layers">Layer stack from top to bottom. Heights are cumulative.</param>
        /// <param name="noiseScale">Noise frequency. Lower = larger features (0.01-0.1 recommended).</param>
        public PerlinGenerator(VoxelLayer[] layers, float noiseScale = 0.25f) {
            _layers = layers;
            _noiseScale = noiseScale;
            
            // Determine max height as the sum of all layers.
            var maxHeight = 0;
            foreach (var layer in layers) {
                maxHeight += layer.Thickness;
            }
            _maxHeight = maxHeight - 1;
            
        }
        
        /// <summary>
        /// Creates a perlin generator instance with the default layer config.
        /// - Bedrock: 1
        /// - Stone: 27
        /// - Dirt: 3
        /// - Grass: 1
        /// </summary>
        public static PerlinGenerator Default() {
            // Grab voxel definitions.
            var bedrock = VoxelTable.GetVoxelDefinition("bedrock");
            var stone = VoxelTable.GetVoxelDefinition("stone");
            var dirt = VoxelTable.GetVoxelDefinition("dirt");
            var grass = VoxelTable.GetVoxelDefinition("grass");

            var layers = new [] {
                new VoxelLayer(grass.Id, 1),
                new VoxelLayer(dirt.Id, 3),
                new VoxelLayer(stone.Id, 27),
                new VoxelLayer(bedrock.Id, 1)
            };

            return new PerlinGenerator(layers, 0.02f);
        }
        
        public void ProcessChunk(in Chunk chunk) {
            var d = VoxelWorld.CHUNK_SIZE;
            var offset = chunk.ChunkId * d.x;
            for (var z = 0; z < d.x; z++) {
                for (var x = 0; x < d.x; x++) {
                    // Grab perlin sample for the current coordinate.
                    var perlin = Mathf.Clamp01(Mathf.PerlinNoise((x + offset.x) * _noiseScale, (z + offset.y) * _noiseScale));
                    var height = Mathf.RoundToInt(perlin * _maxHeight);
                    
                    // Write heightmap values.
                    //chunk.HeightMap[VoxelUtility.HeightIndex(x, z, d.x)] = new HeightData((byte)(height - 1), true);

                    var layerY = height;
                    foreach (var layer in _layers) {
                        // Get the voxel data instance for the current layer.
                        var voxel = VoxelTable.GetVoxelDefinition(layer.VoxelId).GetDataInstance();

                        if (layerY < 0) {
                            break;
                        }
                
                        // Set voxels for this layer.
                        for (var y = 0; y < layer.Thickness; y++) {
                            var vi = VoxelUtility.VoxelIndex(x, layerY - y, z, d);

                            if (layerY - y < 0) {
                                continue;
                            }
                            
                            chunk.VoxelData[vi] = voxel;
                        }
                
                        // Update starting Y index for the next layer.
                        layerY -= layer.Thickness;
                    }
                }
            }
            
            chunk.RebuildHeightMap();
        }
    }
}