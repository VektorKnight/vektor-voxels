using System;

namespace VektorVoxels.Voxels {
    /// <summary>
    /// Configuration flags for a particular voxel.
    /// </summary>
    [Flags]
    public enum VoxelFlags : byte {
        None          = 0x0,     // Sad mundane voxel noises.
        AlphaRender   = 0x1,     // Will be rendered with alpha (Usually glass, water, etc).
        NoCollision   = 0x2,     // Will not be used for solid collision.
        LightSource   = 0x4,     // Will be considered for lightmapping.
        AllowRotation = 0x8,     // Voxel can be placed in any orientation.
        CustomMesh    = 0x10,    // Voxel uses custom mesh data instead of the default cube.
        ClipRender    = 0x20,    // Voxel will be rendered with alpha-clip (usually foliage).
    }
}