using System.Runtime.InteropServices;
using UnityEngine;

namespace VoxelLighting.Meshing {
    /// <summary>
    /// Vertex layout for a voxel mesh.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;
        public Color32 SunLight;
        public Color32 BlockLight;

        public Vertex(Vector3 position, Vector3 normal, Vector2 texCoord, Color32 sunLight, Color32 blockLight) {
            Position = position;
            Normal = normal;
            TexCoord = texCoord;
            SunLight = sunLight;
            BlockLight = blockLight;
        }
    }
}