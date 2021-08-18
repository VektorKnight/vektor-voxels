﻿using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Generation {
    /// <summary>
    /// Uses perlin noise to give some semblance of terrain.
    /// </summary>
    public class PerlinGenerator : ITerrainGenerator {
        private readonly VoxelLayer[] _layers;
        private readonly int _maxHeight;
        private readonly float _noiseScale;

        public PerlinGenerator(VoxelLayer[] layers, float noiseScale = 0.25f) {
            _layers = layers;
            _noiseScale = noiseScale;
            
            // Determine max height as the sum of all layers.
            var maxHeight = 0;
            foreach (var layer in layers) {
                maxHeight += layer.Thickness;
            }
            _maxHeight = maxHeight;
            
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
                new VoxelLayer(bedrock.Id, 1),
            };

            return new PerlinGenerator(layers, 0.02f);
        }
        
        public void ProcessChunk(in Chunk chunk) {
            var d = WorldManager.Instance.ChunkSize;
            var offset = chunk.ChunkId * d.x;
            for (var z = 0; z < d.x; z++) {
                for (var x = 0; x < d.x; x++) {
                    // Grab perlin sample for the current coordinate.
                    var perlin = Mathf.Clamp01(Mathf.PerlinNoise((x + offset.x) * _noiseScale, (z + offset.y) * _noiseScale));
                    var height = Mathf.RoundToInt(perlin * _maxHeight);
                    
                    // Write heightmap values.
                    chunk.HeightMap[VoxelUtility.HeightIndex(x, z, d.x)] = new HeightData((uint)height, true);

                    var layerY = height - 1;
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
        }
    }
}