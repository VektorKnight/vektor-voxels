using UnityEngine;
using UnityEngine.Rendering;

namespace VektorVoxels.Meshing {
    /// <summary>
    /// Static lookup tables for voxel mesh generation. Contains face vertices, triangle indices,
    /// normals, and light sampling offsets. Face order is N/E/S/W/Up/Down (Z+/X+/Z-/X-/Y+/Y-).
    /// LightNeighbors defines the 8 sampling positions for smooth lighting per face.
    /// </summary>
    public static class MeshTables {
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
        
        /// <summary>
        /// Returns a cube composed of the mesh data defined previously for testing purposes.
        /// </summary>
        /*public static Mesh GetTestCube() {
            var mesh = new Mesh() {
                indexFormat = IndexFormat.UInt32
            };

            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var uv = new Vector2[24];
            var triangles = new int[36];

            for (var i = 0; i < 6; i++) {
                vertices[i * 4] = Vertices[i][0];
                vertices[i * 4 + 1] = Vertices[i][1];
                vertices[i * 4 + 2] = Vertices[i][2];
                vertices[i * 4 + 3] = Vertices[i][3];
                    
                normals[i * 4]     = Normals[i];
                normals[i * 4 + 1] = Normals[i];
                normals[i * 4 + 2] = Normals[i];
                normals[i * 4 + 3] = Normals[i];
                
                uv[i * 4]     = UVs[0];
                uv[i * 4 + 1] = UVs[1];
                uv[i * 4 + 2] = UVs[2];
                uv[i * 4 + 3] = UVs[3];

                triangles[i * 6] =     Triangles[0] + i * 4;
                triangles[i * 6 + 1] = Triangles[1] + i * 4;
                triangles[i * 6 + 2] = Triangles[2] + i * 4;
                triangles[i * 6 + 3] = Triangles[3] + i * 4;
                triangles[i * 6 + 4] = Triangles[4] + i * 4;
                triangles[i * 6 + 5] = Triangles[5] + i * 4;
                
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv);
                mesh.SetIndices(triangles, MeshTopology.Triangles, 0);
            }
            
            return mesh;
        }*/
    }
}