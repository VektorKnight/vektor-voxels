using VektorVoxels.Chunks;
using VektorVoxels.Voxels;

namespace VektorVoxels.Generation {
    /// <summary>
    /// Uses perlin noise to give some semblance of terrain.
    /// </summary>
    public class PerlinGenerator : ITerrainGenerator {
        private readonly VoxelLayer[] _layers;
        private readonly int _maxHeight;

        public PerlinGenerator(VoxelLayer[] layers) {
            _layers = layers;
            
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
                new VoxelLayer(bedrock.Id, 1),
                new VoxelLayer(stone.Id, 27),
                new VoxelLayer(dirt.Id, 3),
                new VoxelLayer(grass.Id, 1),
            };

            return new PerlinGenerator(layers);
        }
        
        public void ProcessChunk(in Chunk chunk) {
            
        }
    }
}