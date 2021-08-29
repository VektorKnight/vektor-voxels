using System.Collections;
using UnityEngine;

namespace VektorVoxels.Voxels {
    /// <summary>
    /// Interface for custom voxel mesh data.
    /// </summary>
    public interface ICustomMesh {
        Vector3[] Vertices { get; }
        Vector3[] Normals { get; }
        Vector2[] TexCoords { get; }
        int[] Triangles { get; }
    }
}