using UnityEngine;
using VoxelLighting.Lighting;

namespace VoxelLighting.Voxels {
    /// <summary>
    /// Hard-coded voxel definitions cause I'm lazy.
    /// (Maybe an automatic system would be less work overall, oh well)
    /// </summary>
    public static class VoxelTable {
        public static VoxelDefinition ById(uint id) {
            return Voxels[id - 1];
        }
        
        public static readonly VoxelDefinition[] Voxels = {
            new VoxelDefinition(
                1, 
                "Grass", 
                VoxelFlags.None, 
                Color16.Clear(), 
                new Vector2(0, 0), 
                new Vector2(1, 0)
            ),
            new VoxelDefinition(
                2, 
                "Dirt", 
                VoxelFlags.None, 
                Color16.Clear(), 
                new Vector2(2, 0), 
                new Vector2(2, 0)
            ),
            new VoxelDefinition(
                3, 
                "Gravel", 
                VoxelFlags.None, 
                Color16.Clear(), 
                new Vector2(3, 0), 
                new Vector2(3, 0)
            ),
            new VoxelDefinition(
                4, 
                "Sand", 
                VoxelFlags.None, 
                Color16.Clear(), 
                new Vector2(4, 0), 
                new Vector2(4, 0)
            ),
            new VoxelDefinition(
                5, 
                "Stone", 
                VoxelFlags.None, 
                Color16.Clear(), 
                new Vector2(5, 0), 
                new Vector2(5, 0)
            ),
            new VoxelDefinition(
                6, 
                "Bedrock", 
                VoxelFlags.Unbreakable, 
                Color16.Clear(), 
                new Vector2(6, 0), 
                new Vector2(6, 0)
            ),
            new VoxelDefinition(
                7, 
                "Glass", 
                VoxelFlags.AlphaRender, 
                Color16.Clear(), 
                new Vector2(0, 6), 
                new Vector2(0, 6)
            ),
            new VoxelDefinition(
                8, 
                "Glass Red", 
                VoxelFlags.AlphaRender, 
                new Color16(15, 0, 0, 0).ToAttenuation(), 
                new Vector2(3, 6), 
                new Vector2(3, 6)
            ),
            new VoxelDefinition(
                9, 
                "Glass Green", 
                VoxelFlags.AlphaRender, 
                new Color16(0, 15, 0, 0).ToAttenuation(), 
                new Vector2(6, 6), 
                new Vector2(6, 6)
            ),
            new VoxelDefinition(
                10, 
                "Glass Blue", 
                VoxelFlags.AlphaRender, 
                new Color16(0, 0, 15, 0).ToAttenuation(), 
                new Vector2(9, 6), 
                new Vector2(9, 6)
            ),
            new VoxelDefinition(
                11, 
                "Glowstone", 
                VoxelFlags.LightSource, 
                new Color16(15, 15, 15, 0), 
                new Vector2(0, 7), 
                new Vector2(0, 7)
            ),
        };
}
}