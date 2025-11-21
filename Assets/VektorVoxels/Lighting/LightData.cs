using System.Runtime.InteropServices;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Combined sun and block light at a single voxel.
    /// Each LightColor uses 8 bits per channel (0-255 intensity).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LightData {
        public LightColor Sun;
        public LightColor Block;

        public LightData(LightColor sun, LightColor block) {
            Sun = sun;
            Block = block;
        }
    }
}