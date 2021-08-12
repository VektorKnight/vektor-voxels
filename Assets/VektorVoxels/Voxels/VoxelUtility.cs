using System.Runtime.CompilerServices;
using UnityEngine;

namespace VektorVoxels.Voxels {
    public static class VoxelUtility {
        /// <summary>
        /// Determines if a voxel coordinate is within the local grid.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InLocalGrid(int x, int y, int z, int sx, int sy, int sz) {
            return x >= 0 && x < sx &&
                   y >= 0 && y < sy &&
                   z >= 0 && z < sz;
        }
        
        /// <summary>
        /// Determines if a voxel coordinate is within the local grid.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InLocalGrid(in Vector3Int p, in Vector3Int d) {
            return p.x >= 0 && p.x < d.x &&
                   p.y >= 0 && p.y < d.y &&
                   p.z >= 0 && p.z < d.z;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int VoxelIndex(in Vector3Int p, in Vector3Int d) {
            return p.x + d.x * (p.y + d.y * p.z);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int VoxelIndex(int x, int y, int z, in Vector3Int d) {
            return x + d.x * (y + d.y * z);
        }

        public static int HeightIndex(int x, int z, int sx) {
            return x + z * sx;
        }
    }
}