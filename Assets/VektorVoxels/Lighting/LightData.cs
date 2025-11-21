using System.Runtime.InteropServices;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Combined sun and block light at a single voxel.
    /// Each Color16 uses 4 bits per channel (0-15 intensity).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LightData {
        public Color16 Sun;
        public Color16 Block;

        public LightData(Color16 sun, Color16 block) {
            Sun = sun;
            Block = block;
        }
    }
}