using System;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using VektorVoxels.Lighting;

namespace VektorVoxels.Voxels {
    /// <summary>
    /// Represents a single voxel stored voxel data instance.
    /// Additional data that is both global and infrequently accessed is stored in the Voxel Attribute Table.
    /// There may be more optimal data layouts but memory optimization is not a major focus of this project beyond basic
    /// locality for meshing/lighting and alignment to make things easier on the GPU.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct VoxelData : IEquatable<VoxelData> {
        // 32 Bits
        public readonly uint Id;            // Id of the voxel (textures, other data).
        
        // 32 Bits
        public readonly VoxelFlags Flags;   // Voxel configuration flags.
        public readonly Color16 ColorData;  // Color data for lighting.
        
        /// <summary>
        /// A null voxel or "air" in some games.
        /// Not considered for meshing, collision, or anything else really.
        /// </summary>
        public static VoxelData Null() => new VoxelData(0, 0, Color16.Clear());

        public VoxelData(uint id, VoxelFlags flags, Color16 colorData) {
            Id = id;
            Flags = flags;
            ColorData = colorData;
        }
        
        [Pure]
        public bool HasFlag(VoxelFlags flag) {
            return (Flags & flag) != 0;
        }

        public bool Equals(VoxelData other) {
            return Id == other.Id;
        }

        public override bool Equals(object obj) {
            return obj is VoxelData other && Equals(other);
        }

        public override int GetHashCode() {
            return (int)Id;
        }
    }
}