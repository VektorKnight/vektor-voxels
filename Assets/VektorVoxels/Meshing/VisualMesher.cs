// Copyright 2021, Derek de la Peza (aka VektorKnight)
// The following code is subject to the license terms defined in LICENSE.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using VektorVoxels.Chunks;
using VektorVoxels.Config;
using VektorVoxels.Lighting;
using VektorVoxels.Voxels;
using VektorVoxels.World;
using Debug = UnityEngine.Debug;

namespace VektorVoxels.Meshing {
    /// <summary>
    /// Performs Minecraft-style cubic meshing of a voxel grid.
    /// </summary>
    public class VisualMesher {
        public const int ATLAS_SIZE = 256;
        public const int TEXTURE_SIZE = 16;
        public const float TEX_UV_WIDTH = 1f / (ATLAS_SIZE / TEXTURE_SIZE);
        
        // Thread-local instance.
        private static readonly ThreadLocal<VisualMesher> _threadLocal = new ThreadLocal<VisualMesher>(() => new VisualMesher());
        public static VisualMesher LocalThreadInstance => _threadLocal.Value;

        // Resulting mesh data.
        private readonly List<Vertex> _vertices;
        private readonly List<int> _trianglesA;
        private readonly List<int> _trianglesB;
        
        // Local work buffers to reduce calls to List.Add/AddRange.
        private readonly Vertex[] _vertexWorkBuffer;
        private readonly Vector2[] _uvWorkBuffer;
        private readonly LightData[] _lightWorkBuffer;
        private readonly int[] _triangleWorkBuffer;

        // A custom vertex layout is used for voxel meshes.
        // Sun and block light are mapped to TexCoord1 and TexCoord2 as Color32s (Unorm8x4).
        // Vertex colors could eventually be used for biome blending or other color effects.
        private static readonly VertexAttributeDescriptor[] _vertexBufferParams = {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.UNorm8, 4)
        };

        public VisualMesher() {
            _vertices = new List<Vertex>();
            _trianglesA = new List<int>();
            _trianglesB = new List<int>();

            _vertexWorkBuffer = new Vertex[4];
            _uvWorkBuffer = new Vector2[4];
            _lightWorkBuffer = new LightData[8];
            _triangleWorkBuffer = new int[6];
        }

        /// <summary>
        /// Generates mesh data for a given voxel grid.
        /// Can be called on a separate thread if needed.
        /// Call SetMeshData() once this method completes to apply the new mesh data to a given mesh.
        /// </summary>
        public void GenerateMeshData(in Chunk chunk, NeighborSet neighbors) {
            // Acquire read lock on all provided neighbors.
            neighbors.AcquireReadLocks();
            
            var voxelGrid = chunk.VoxelData;
            var sunLight = chunk.SunLight;
            var blockLight = chunk.BlockLight;
            var d = VoxelWorld.CHUNK_SIZE;
            var smoothLight = VoxelWorld.Instance.UseSmoothLighting;
            
            // Clear work buffers.
            _vertices.Clear();
            _trianglesA.Clear();
            _trianglesB.Clear();
            
            // Process each voxel and add the appropriate faces.
            // Note: variable naming may seem a bit weird here.
            // To clarify, anything ending in 'p' is a 3D index/position and anything ending in 'i' is a 1D index.
            // Ex: np, npi -> 'Neighbor Position', 'Neighbor Position Index'.
            var vp = Vector3Int.zero;
            for (var y = 0; y < d.y; y++) {
                for (var z = 0; z < d.x; z++) {
                    for (var x = 0; x < d.x; x++) {
                        // Grab current voxel and light data.
                        vp.x = x; vp.y = y; vp.z = z;
                        var voxel = voxelGrid[VoxelUtility.VoxelIndex(vp, d)];

                        // Skip if voxel is null.
                        if (voxel.Id == 0) continue;
                        
                        // Grab the voxel definition.
                        var voxelDef = VoxelTable.GetVoxelDefinition(voxel.Id);
                        
                        // Iterate each neighbor and determine if a face should be added.
                        for (var i = 0; i < 6; i++) {
                            // Grab neighbor voxel depending on locality.
                            // Voxels outside the current grid will just be null.
                            var np = MeshTables.VoxelNeighbors[i] + vp;
                            var npi = VoxelUtility.VoxelIndex(np, d);
                            
                            VoxelData neighbor;
                            LightData light;

                            if (VoxelUtility.InLocalGrid(np, d)) {
                                neighbor = voxelGrid[npi];
                                light = new LightData(sunLight[npi], blockLight[npi]);
                            }
                            else {
                                MeshUtility.GetNeighborData(in np, in d, in neighbors, out neighbor, out light);
                            }
                            
                            // Only need to populate the light work buffer for smooth lighting.
                            if (smoothLight) {
                                for (var l = 0; l < 8; l++) {
                                    var lnp = MeshTables.LightNeighbors[i][l] + np;
                                    var lni = VoxelUtility.VoxelIndex(lnp, d);

                                    LightData smoothLightData;
                                    if (VoxelUtility.InLocalGrid(lnp, d)) {
                                        smoothLightData = new LightData(sunLight[lni], blockLight[lni]);
                                    }
                                    else {
                                        MeshUtility.GetNeighborData(in lnp, in d, in neighbors, out _, out smoothLightData);
                                    }

                                    _lightWorkBuffer[l] = smoothLightData;
                                }
                            }

                            // Skip the face if this voxel is opaque and the neighbor is opaque.
                            if (neighbor.Id != 0 && (neighbor.Flags & VoxelFlags.AlphaRender) == 0) {
                                continue;
                            }
                            
                            // Skip face if this voxel and the neighbor are the same.
                            if (voxel.Id == neighbor.Id) {
                                continue;
                            }
                            
                            // Grab the texture rect corresponding to the current face and populate the UV buffer.
                            var rect = voxelDef.TextureRects[i];
                            _uvWorkBuffer[0] = new Vector2(rect.position.x, rect.yMax);
                            _uvWorkBuffer[1] = rect.position;
                            _uvWorkBuffer[2] = new Vector2(rect.xMax, rect.position.y);
                            _uvWorkBuffer[3] = rect.max;
                            
                            // Transform and add face vertex data.
                            // Vertex count pre-copy is stored for triangles later.
                            var vc = _vertices.Count;
                            for (var v = 0; v < 4; v++) {
                                var pos = MeshTables.Vertices[i][v];
                                pos.x += x; pos.y += y; pos.z += z;
                                
                                // Generate vertex light values.
                                Color32 vertexLightSun, vertexLightBlock;
                                
                                // Smooth lighting averages the neighboring light values for each vertex.
                                // Refer to the smooth lighting diagram for an explanation.
                                // This also gives us AO for free :D
                                // TODO: Could probably avoid two switch statements here.
                                if (smoothLight) {
                                    vertexLightSun = v switch {
                                        0 => MeshUtility.CalculateVertexLight(light.Sun, _lightWorkBuffer[4].Sun,
                                            _lightWorkBuffer[5].Sun, _lightWorkBuffer[6].Sun),
                                        1 => MeshUtility.CalculateVertexLight(light.Sun, _lightWorkBuffer[6].Sun,
                                            _lightWorkBuffer[7].Sun, _lightWorkBuffer[0].Sun),
                                        2 => MeshUtility.CalculateVertexLight(light.Sun, _lightWorkBuffer[0].Sun,
                                            _lightWorkBuffer[1].Sun, _lightWorkBuffer[2].Sun),
                                        3 => MeshUtility.CalculateVertexLight(light.Sun, _lightWorkBuffer[2].Sun,
                                            _lightWorkBuffer[3].Sun, _lightWorkBuffer[4].Sun),
                                        _ => Color16.Clear().ToColor32()
                                    };
                                    vertexLightBlock = v switch {
                                        0 => MeshUtility.CalculateVertexLight(light.Block, _lightWorkBuffer[4].Block,
                                            _lightWorkBuffer[5].Block, _lightWorkBuffer[6].Block),
                                        1 => MeshUtility.CalculateVertexLight(light.Block, _lightWorkBuffer[6].Block,
                                            _lightWorkBuffer[7].Block, _lightWorkBuffer[0].Block),
                                        2 => MeshUtility.CalculateVertexLight(light.Block, _lightWorkBuffer[0].Block,
                                            _lightWorkBuffer[1].Block, _lightWorkBuffer[2].Block),
                                        3 => MeshUtility.CalculateVertexLight(light.Block, _lightWorkBuffer[2].Block,
                                            _lightWorkBuffer[3].Block, _lightWorkBuffer[4].Block),
                                        _ => Color16.Clear().ToColor32()
                                    };
                                }
                                else {
                                    vertexLightSun = light.Sun.ToColor32();
                                    vertexLightBlock = light.Block.ToColor32();
                                }

                                // Construct vertex data.
                                _vertexWorkBuffer[v] = new Vertex(
                                    pos, 
                                    MeshTables.Normals[i],
                                    _uvWorkBuffer[v],
                                    vertexLightSun,
                                    (voxel.Flags & VoxelFlags.LightSource) != 0 
                                        ? voxel.ColorData.ToColor32() 
                                        : vertexLightBlock
                                );
                            }
                            
                            // Copy vertex work buffer into mesh vertex data.
                            _vertices.AddRange(_vertexWorkBuffer);

                            // Transform and add triangle data.
                            for (var t = 0; t < 6; t++) {
                                _triangleWorkBuffer[t] = MeshTables.Triangles[t] + vc;
                            }

                            // Add triangle to opaque or alpha layer depending on flags.
                            if ((voxel.Flags & VoxelFlags.AlphaRender) == 0) {
                                _trianglesA.AddRange(_triangleWorkBuffer);
                            }
                            else {
                                _trianglesB.AddRange(_triangleWorkBuffer);
                            }
                        }
                    }
                }
            }
            
            // Release neighbor locks.
            neighbors.ReleaseReadLocks();
        }
        
        /// <summary>
        /// Applies the recently generated mesh data to a provided mesh data array.
        /// </summary>
        public void ApplyMeshData(ref Mesh.MeshDataArray meshData) {
            var data = meshData[0];
            
            data.SetVertexBufferParams(_vertices.Count, _vertexBufferParams);
            data.SetIndexBufferParams(_trianglesA.Count + _trianglesB.Count, IndexFormat.UInt32);
            
            // Write in the vertex buffer data.
            var vertexBuffer = data.GetVertexData<Vertex>();
            for (var i = 0; i < _vertices.Count; i++) {
                vertexBuffer[i] = _vertices[i];
            }
            
            // Configure sub-meshes and write index buffer data.
            // Sub-mesh index data is packed linearly.
            var indexBuffer = data.GetIndexData<uint>();
            for (var i = 0; i < _trianglesA.Count; i++) {
                indexBuffer[i] = (uint)_trianglesA[i];
            }
            for (var i = 0; i < _trianglesB.Count; i++) {
                indexBuffer[i + _trianglesA.Count] = (uint)_trianglesB[i];
            }
            
            data.subMeshCount = 2;
            data.SetSubMesh(0, new SubMeshDescriptor(0, _trianglesA.Count, MeshTopology.Triangles));
            data.SetSubMesh(1, new SubMeshDescriptor(_trianglesA.Count, _trianglesB.Count, MeshTopology.Triangles));
        }
        
        /// <summary>
        /// Sets generated mesh data to a given mesh.
        /// </summary>
        /*public void SetMeshData(ref Mesh mesh) {
            if (mesh == null) {
                mesh = new Mesh() {
                    indexFormat = IndexFormat.UInt32
                };
            }

            // Configure and set vertex buffer.
            mesh.SetVertexBufferParams(_vertices.Count, _vertexBufferParams);
            mesh.SetVertexBufferData(_vertices, 0, 0, _vertices.Count);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(_trianglesA, 0, false);
            mesh.SetTriangles(_trianglesB, 1);
        }*/
    }
}