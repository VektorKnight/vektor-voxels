using System;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using VektorVoxels.Lighting;

namespace VektorVoxels.Voxels {
    /// <summary>
    /// Represents a single voxel stored voxel instance.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct VoxelData : IEquatable<VoxelData> {
        public readonly ushort Id;
        public readonly VoxelFlags Flags;
        public readonly FacingDirection Orientation;
        public readonly VoxelColor ColorData;
        
        /// <summary>
        /// Whether or not the voxel is null or air.
        /// </summary>
        public bool IsNull => Id == 0;

        /// <summary>
        /// A null voxel or "air" in some games.
        /// Not considered for meshing, collision, or anything else really.
        /// </summary>
        private static readonly VoxelData NULL_VOXEL = new VoxelData(0, VoxelFlags.NoCollision, 0, VoxelColor.White());
        public static VoxelData Empty() => NULL_VOXEL;

        public VoxelData(ushort id, VoxelFlags flags, FacingDirection orientation, VoxelColor colorData) {
            Id = id;
            Flags = flags;
            Orientation = orientation;
            ColorData = colorData;
        }

        /// <summary>
        /// Checks if this voxel data instance has the specified flag.
        /// </summary>
        [Pure]
        public bool HasFlag(VoxelFlags flag) {
            return (Flags & flag) != 0;
        }
        
        [Pure]
        public bool Equals(VoxelData other) {
            return Id == other.Id;
        }
        
        [Pure]
        public override bool Equals(object obj) {
            return obj is VoxelData other && Equals(other);
        }

        [Pure]
        public bool IsEmpty() {
            return Id == 0;
        }

        [Pure]
        public bool IsOpaque() {
            return !IsEmpty() && (Flags & VoxelFlags.AlphaRender) == 0;
        }
        
        [Pure]
        public bool IsLightSource() {
            return !IsEmpty() && (Flags.HasFlag(VoxelFlags.LightSource));
        }
        
        [Pure]
        public override int GetHashCode() {
            return (int)Id;
        }
    }
}