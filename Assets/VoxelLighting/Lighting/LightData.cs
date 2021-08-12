using System.Runtime.InteropServices;

namespace VoxelLighting.Lighting {
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