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
        
        /// <summary>
        /// Determines if a height-map coordinate is within the local rect.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InLocalRect(Vector2Int p, int d) {
            return p.x >= 0 && p.x < d &&
                   p.y >= 0 && p.y < d;
        }
        
        /// <summary>
        /// Determines if a height-map coordinate is within the local rect.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InLocalRect(int x, int y, int d) {
            return x >= 0 && x < d &&
                   y >= 0 && y < d;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int VoxelIndex(Vector3Int p, Vector2Int d) {
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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HeightIndex(Vector2Int p, int sx) {
            return p.x + p.y * sx;
        }
    }
}