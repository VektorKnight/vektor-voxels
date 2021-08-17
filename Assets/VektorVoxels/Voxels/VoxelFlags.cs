using System;

namespace VektorVoxels.Voxels {
    /// <summary>
    /// Configuration flags for a particular voxel.
    /// </summary>
    [Flags]
    public enum VoxelFlags : byte {
        None = 0,
        AlphaRender = 1,    // Will be rendered with alpha (transparency).
        NoCollision = 2,    // Will not be used for solid collision.
        LightSource = 4,    // Will be considered for lightmapping.
        AllowRotation = 8,  // Voxel can be placed in any orientation.
    }
}