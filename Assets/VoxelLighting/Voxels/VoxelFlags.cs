using System;

namespace VoxelLighting.Voxels {
    /// <summary>
    /// Configuration flags for a particular voxel.
    /// </summary>
    [Flags]
    public enum VoxelFlags : ushort {
        None = 0,
        AlphaRender = 1,    // Will be rendered with alpha (transparency).
        NoCollision = 2,    // Will not be used for solid collision.
        LightSource = 4,    // Will be considered for lightmapping.
        Unbreakable = 8,    // Cannot be placed or broken.
    }
}