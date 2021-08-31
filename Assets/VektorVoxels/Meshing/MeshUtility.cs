using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Lighting;
using VektorVoxels.Voxels;

namespace VektorVoxels.Meshing {
    public static class MeshUtility {
        /// <summary>
        /// Fetches data from a neighbor depending on position and if the neighbor was provided.
        /// WARNING: This function might give you a stroke.
        /// </summary>
        public static void GetNeighborData(in Vector3Int p, in Vector2Int d, in NeighborSet n, out VoxelData v, out LightData l) {
            Chunk neighbor;
            int nvi;
            bool exists;
            
            // Handle out of bounds Y values.
            if (p.y < 0 || p.y >= d.y) {
                v = VoxelData.Null();
                l = new LightData(Color16.White(), Color16.Clear());
                return;
            }
            
            // Determine which neighbor the voxel lies in.
            var offsetFlags = NeighborOffset.None;
            offsetFlags |= p.z >= d.x ? NeighborOffset.North : NeighborOffset.None;
            offsetFlags |= p.x >= d.x ? NeighborOffset.East : NeighborOffset.None;
            offsetFlags |= p.z < 0 ? NeighborOffset.South : NeighborOffset.None;
            offsetFlags |= p.x < 0 ? NeighborOffset.West : NeighborOffset.None;

            switch (offsetFlags) {
                case NeighborOffset.North:
                    exists = (n.Flags & NeighborFlags.North) != 0;
                    neighbor = n.North;
                    nvi = VoxelUtility.VoxelIndex(p.x, p.y, 0, d);
                    break;
                case NeighborOffset.East:
                    exists = (n.Flags & NeighborFlags.East) != 0;
                    neighbor = n.East;
                    nvi = VoxelUtility.VoxelIndex(0, p.y, p.z, d);
                    break;
                case NeighborOffset.South:
                    exists = (n.Flags & NeighborFlags.South) != 0;
                    neighbor = n.South;
                    nvi = VoxelUtility.VoxelIndex(p.x, p.y, d.x - 1, d);
                    break;
                case NeighborOffset.West:
                    exists = (n.Flags & NeighborFlags.West) != 0;
                    neighbor = n.West;
                    nvi = VoxelUtility.VoxelIndex(d.x - 1, p.y, p.z, d);
                    break;
                case NeighborOffset.NorthEast:
                    exists = (n.Flags & NeighborFlags.NorthEast) != 0;
                    neighbor = n.NorthEast;
                    nvi = VoxelUtility.VoxelIndex(0, p.y, 0, d);
                    break;
                case NeighborOffset.SouthEast:
                    exists = (n.Flags & NeighborFlags.SouthEast) != 0;
                    neighbor = n.SouthEast;
                    nvi = VoxelUtility.VoxelIndex(0, p.y, d.x - 1, d);
                    break;
                case NeighborOffset.SouthWest:
                    exists = (n.Flags & NeighborFlags.SouthWest) != 0;
                    neighbor = n.SouthWest;
                    nvi = VoxelUtility.VoxelIndex(d.x - 1, p.y, d.x - 1, d);
                    break;
                case NeighborOffset.NorthWest:
                    exists = (n.Flags & NeighborFlags.NorthWest) != 0;
                    neighbor = n.NorthWest;
                    nvi = VoxelUtility.VoxelIndex(d.x - 1, p.y, 0, d);
                    break;
                case NeighborOffset.None:
                    exists = false;
                    neighbor = null;
                    nvi = 0;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (exists) {
                v = neighbor.VoxelData[nvi];
                l = new LightData(neighbor.SunLight[nvi], neighbor.BlockLight[nvi]);
            }
            else {
                v = VoxelData.Null();
                l = new LightData(Color16.White(), Color16.Clear());
            }
        }
        
        /// <summary>
        /// Averages the 4 light values needed for a vertex.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color32 CalculateVertexLight(Color16 c0, Color16 c1, Color16 c2, Color16 c3) {
            // Decompose each color into individual channels.
            // This is necessary since working with Color16 directly involves a lot of bitwise ops.
            c0.Decompose(out var c0r, out var c0g, out var c0b, out var c0a);
            c1.Decompose(out var c1r, out var c1g, out var c1b, out var c1a);
            c2.Decompose(out var c2r, out var c2g, out var c2b, out var c2a);
            c3.Decompose(out var c3r, out var c3g, out var c3b, out var c3a);
            
            // Average each channel.
            var r = (c0r + c1r + c2r + c3r) >> 2;
            var g = (c0g + c1g + c2g + c3g) >> 2;
            var b = (c0b + c1b + c2b + c3b) >> 2;
            var a = (c0a + c1a + c2a + c3a) >> 2;
            
            // Scale to color 32 by multiplying by 17.
            return new Color32(
                (byte)(r * 17),
                (byte)(g * 17),
                (byte)(b * 17),
                (byte)(a * 17)
            );
        }
    }
}