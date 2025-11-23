using System.Runtime.InteropServices;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Combined sun and block light at a single voxel.
    /// Each LightColor uses RGB565 format (16-bit, 32/64/32 levels per channel).
    /// Total: 4 bytes per voxel.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LightData {
        public VoxelColor Sun;
        public VoxelColor Block;

        public LightData(VoxelColor sun, VoxelColor block) {
            Sun = sun;
            Block = block;
        }
    }
}