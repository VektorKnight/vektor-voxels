using UnityEngine;
using UnityEngine.Rendering;

namespace VektorVoxels.Meshing {
    /// <summary>
    /// Static lookup tables for voxel mesh generation. Contains face vertices, triangle indices,
    /// normals, and light sampling offsets. Face order is N/E/S/W/Up/Down (Z+/X+/Z-/X-/Y+/Y-).
    /// LightNeighbors defines the 8 sampling positions for smooth lighting per face.
    /// </summary>
    public static class MeshTables {
        // Texture atlas configuration: 256x256 total, 16x16 per tile = 16 tiles per row.
        public const int ATLAS_SIZE = 256;
        public const int TEXTURE_SIZE = 16;
        public const float TEX_UV_WIDTH = 1f / (ATLAS_SIZE / TEXTURE_SIZE);

        // Face vertices (North is Z+).
        // Clockwise always starting from the bottom left.
        public static readonly Vector3[][] Vertices = {
            new [] {
                new Vector3(1, 0, 1),
                new Vector3(1, 1, 1),
                new Vector3(0, 1, 1),
                new Vector3(0, 0, 1),
            },

            new [] {
                new Vector3(1, 0, 0),
                new Vector3(1, 1, 0),
                new Vector3(1, 1, 1),
                new Vector3(1, 0, 1),
            },
            
            new [] {
                new Vector3(0, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(1, 1, 0),
                new Vector3(1, 0, 0),
            },

            new [] {
                new Vector3(0, 0, 1),
                new Vector3(0, 1, 1),
                new Vector3(0, 1, 0),
                new Vector3(0, 0, 0),
            },

            new [] {
                new Vector3(0, 1, 0),
                new Vector3(0, 1, 1),
                new Vector3(1, 1, 1),
                new Vector3(1, 1, 0),
            },

            new [] {
                new Vector3(0, 0, 1),
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(1, 0, 1)
            }
        };


        // Face triangles.
        public static readonly int[] Triangles = {
            0, 1, 2,
            0, 2, 3,
        };
        
        // Alternate triangle config for ambient occlusion.
        public static readonly int[] TrianglesAlt = {
            1, 2, 3,
            0, 1, 3
        };

        /*public static readonly Vector2[] UVs = {
            new Vector2(0, 0),
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(1, 0)
        };*/

        public static readonly Vector3[] Normals = {
            Vector3.forward,
            Vector3.right,
            Vector3.back,
            Vector3.left,
            Vector3.up, 
            Vector3.down
        };
        
        // Processing order of faces (N, E, S, W, T, B)
        public static readonly Vector3Int[] VoxelNeighbors = {
            new Vector3Int(0, 0, 1),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, -1),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0)
        };
        
        // Clockwise starting from relative north.
        public static readonly Vector3Int[][] LightNeighbors = {
            new [] {
                new Vector3Int(0, 1, 0),    // N
                new Vector3Int(-1, 1, 0),   // NE
                new Vector3Int(-1, 0, 0),   // E
                new Vector3Int(-1, -1, 0),  // SE
                new Vector3Int(0, -1, 0),   // S
                new Vector3Int(1, -1, 0),   // SW
                new Vector3Int(1, 0, 0),    // W
                new Vector3Int(1, 1, 0)     // NW
            },

            new [] {
                new Vector3Int(0, 1, 0),     // N
                new Vector3Int(0, 1, 1),     // NE
                new Vector3Int(0, 0, 1),     // E
                new Vector3Int(0, -1, 1),    // SE
                new Vector3Int(0, -1, 0),    // S
                new Vector3Int(0, -1, -1),   // SW
                new Vector3Int(0, 0, -1),    // W
                new Vector3Int(0, 1, -1)     // NW
            },
            
            new [] {
                new Vector3Int(0, 1, 0),     // N
                new Vector3Int(1, 1, 0),     // NE
                new Vector3Int(1, 0, 0),     // E
                new Vector3Int(1, -1, 0),    // SE
                new Vector3Int(0, -1, 0),    // S
                new Vector3Int(-1, -1, 0),   // SW
                new Vector3Int(-1, 0, 0),    // W
                new Vector3Int(-1, 1, 0)     // NW
            },
            
            new [] {
                new Vector3Int(0, 1, 0),     // N
                new Vector3Int(0, 1, -1),     // NE
                new Vector3Int(0, 0, -1),     // E
                new Vector3Int(0, -1, -1),    // SE
                new Vector3Int(0, -1, 0),    // S
                new Vector3Int(0, -1, 1),   // SW
                new Vector3Int(0, 0, 1),    // W
                new Vector3Int(0, 1, 1)     // NW
            },
            
            new [] {
                new Vector3Int(0, 0, 1),     // N
                new Vector3Int(1, 0, 1),     // NE
                new Vector3Int(1, 0, 0),     // E
                new Vector3Int(1, 0, -1),    // SE
                new Vector3Int(0, 0, -1),    // S
                new Vector3Int(-1, 0, -1),   // SW
                new Vector3Int(-1, 0, 0),    // W
                new Vector3Int(-1, 0, 1)     // NW
            },
            
            new [] {
                new Vector3Int(0, 0, -1),     // N
                new Vector3Int(1, 0, -1),     // NE
                new Vector3Int(1, 0, 0),     // E
                new Vector3Int(1, 0, 1),    // SE
                new Vector3Int(0, 0, 1),    // S
                new Vector3Int(-1, 0, 1),   // SW
                new Vector3Int(-1, 0, 0),    // W
                new Vector3Int(-1, 0, -1)     // NW
            },
        };
    }
}