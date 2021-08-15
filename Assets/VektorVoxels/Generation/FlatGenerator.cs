using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Generation {
    /// <summary>
    /// Generates a flat world with grass at the top, a thin layer of dirt, then stone, and finally bedrock.
    /// Roughly equivalent to Minecraft's SuperFlat world type.
    /// Layers are processed in order from the bottom of the chunk.
    /// i.e Grass should be the last layer.
    /// </summary>
    public class FlatGenerator : ITerrainGenerator {
        private int _seaLevel;
        private VoxelLayer[] _layers;

        public FlatGenerator(int seaLevel, VoxelLayer[] layers) {
            _seaLevel = seaLevel;
            _layers = layers;
        }
        
        /// <summary>
        /// Gets a flat generator instance with the default config.
        /// - Bedrock: 1
        /// - Stone: 27
        /// - Dirt: 3
        /// - Grass: 1
        /// </summary>
        /// <returns></returns>
        public static FlatGenerator Default() {
            // Grab voxel definitions.
            var bedrock = VoxelTable.GetVoxelDefinition("bedrock");
            var stone = VoxelTable.GetVoxelDefinition("stone");
            var dirt = VoxelTable.GetVoxelDefinition("dirt");
            var grass = VoxelTable.GetVoxelDefinition("grass");
            
            var layers = new [] {
                new VoxelLayer(bedrock.Id, 1),
                new VoxelLayer(stone.Id, 27),
                new VoxelLayer(dirt.Id, 3),
                new VoxelLayer(grass.Id, 1),
            };

            return new FlatGenerator(32, layers);
        }

        public void ProcessChunk(in Chunk chunk) {
            Debug.Assert(chunk != null, "Received a null chunk reference!");
            var d = WorldManager.Instance.ChunkSize;
            var layerY = 0;
            foreach (var layer in _layers) {
                // Get the voxel data instance for the current layer.
                var voxel = VoxelTable.GetVoxelDefinition(layer.VoxelId).GetDataInstance();
                
                // Set voxels for this layer.
                for (var y = 0; y < layer.Thickness; y++) {
                    for (var z = 0; z < d.x; z++) {
                        for (var x = 0; x < d.x; x++) {
                            var vi = VoxelUtility.VoxelIndex(x, layerY + y, z, d);
                            chunk.VoxelData[vi] = voxel;
                        }
                    }
                }
                
                // Update starting Y index for the next layer.
                layerY += layer.Thickness;
            }
        }
    }
}