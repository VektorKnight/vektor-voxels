using System.Runtime.CompilerServices;
using UnityEngine;

namespace VektorVoxels.Voxels {
    public static class VoxelUtility {
        /// <summary>
        /// Determines if a voxel coordinate is within the local grid.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InLocalGrid(Vector3Int p, Vector2Int d) {
            return p.x >= 0 && p.x < d.x &&
                   p.y >= 0 && p.y < d.y &&
                   p.z >= 0 && p.z < d.x;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int VoxelIndex(in Vector3Int p, Vector2Int d) {
            return p.x + d.x * (p.y + d.y * p.z);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int VoxelIndex(int x, int y, int z, Vector2Int d) {
            return x + d.x * (y + d.y * z);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HeightIndex(int x, int z, int sx) {
            return x + z * sx;
        }
    }
}