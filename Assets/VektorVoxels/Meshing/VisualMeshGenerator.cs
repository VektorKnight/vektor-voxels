// Copyright 2021, Derek de la Peza (aka VektorKnight)
// The following code is subject to the license terms defined in LICENSE.md

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using UnityEngine;
using UnityEngine.Rendering;
using VektorVoxels.Chunks;
using VektorVoxels.Lighting;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Meshing {
    /// <summary>
    /// Performs Minecraft-style cubic meshing of a voxel grid.
    /// </summary>
    public class VisualMeshGenerator {
        public const int ATLAS_SIZE = 256;
        public const int TEXTURE_SIZE = 16;
        public const float TEX_UV_WIDTH = 1f / (ATLAS_SIZE / TEXTURE_SIZE);
        
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

        public VisualMeshGenerator() {
            _vertices = new List<Vertex>();
            _trianglesA = new List<int>();
            _trianglesB = new List<int>();

            _vertexWorkBuffer = new Vertex[4];
            _uvWorkBuffer = new Vector2[4];
            _lightWorkBuffer = new LightData[8];
            _triangleWorkBuffer = new int[6];
        }
        
        /// <summary>
        /// Averages the 4 light values needed for a vertex.
        /// </summary>
        private static Color32 CalculateVertexLight(Color16 c0, Color16 c1, Color16 c2, Color16 c3) {
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
        
        /// <summary>
        /// Fetches data from a neighbor depending on position and if the neighbor was provided.
        /// This function might give you a stroke.
        /// </summary>
        private static void GetNeighborData(in Vector3Int p, in Vector2Int d, in NeighborSet n, out VoxelData v, out LightData l) {
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
        /// Acquires read locks on any valid neighbors.
        /// </summary>
        private void AcquireNeighborLocks(in NeighborSet neighbors) {
            if ((neighbors.Flags & NeighborFlags.North) != 0) {
                neighbors.North.ThreadLock.EnterReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.East) != 0) {
                neighbors.East.ThreadLock.EnterReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.South) != 0) {
                neighbors.South.ThreadLock.EnterReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.West) != 0) {
                neighbors.West.ThreadLock.EnterReadLock();
            }
        }
        
        /// <summary>
        /// Releases read locks on any valid neighbors.
        /// </summary>
        private void ReleaseNeighborLocks(in NeighborSet neighbors) {
            if ((neighbors.Flags & NeighborFlags.North) != 0) {
                neighbors.North.ThreadLock.ExitReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.East) != 0) {
                neighbors.East.ThreadLock.ExitReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.South) != 0) {
                neighbors.South.ThreadLock.ExitReadLock();
            }
            
            if ((neighbors.Flags & NeighborFlags.West) != 0) {
                neighbors.West.ThreadLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Generates mesh data for a given voxel grid.
        /// Can be called on a separate thread if needed.
        /// Call SetMeshData() once this method completes to apply the new mesh data to a given mesh.
        /// </summary>
        public void GenerateMeshData(in Chunk chunk, NeighborSet neighbors) {
            // Acquire read lock on all provided neighbors.
            AcquireNeighborLocks(in neighbors);
            
            var voxelGrid = chunk.VoxelData;
            var sunLight = chunk.SunLight;
            var blockLight = chunk.BlockLight;
            var d = WorldManager.Instance.ChunkSize;
            var smoothLight = WorldManager.Instance.UseSmoothLighting;
            
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
                        var voxel = voxelGrid[VoxelUtility.VoxelIndex(in vp, d)];

                        // Skip if voxel is null.
                        if (voxel.Id == 0) continue;
                        
                        var voxelDef = VoxelTable.GetVoxelDefinition(voxel.Id);
                        
                        // Iterate each neighbor and determine if a face should be added.
                        // TODO: Handle data from neighboring chunks.
                        for (var i = 0; i < 6; i++) {
                            // Grab neighbor voxel depending on locality.
                            // Voxels outside the current grid will just be null.
                            var np = MeshTables.VoxelNeighbors[i] + vp;
                            var npi = VoxelUtility.VoxelIndex(in np, d);
                            
                            VoxelData neighbor;
                            LightData light;

                            if (VoxelUtility.InLocalGrid(np, d)) {
                                neighbor = voxelGrid[npi];
                                light = new LightData(sunLight[npi], blockLight[npi]);
                            }
                            else {
                                GetNeighborData(in np, in d, in neighbors, out neighbor, out light);
                            }
                            
                            // Only need to populate the light work buffer for smooth lighting.
                            if (smoothLight) {
                                for (var l = 0; l < 8; l++) {
                                    var lnp = MeshTables.LightNeighbors[i][l] + np;
                                    var lni = VoxelUtility.VoxelIndex(in lnp, d);

                                    LightData smoothLightData;
                                    if (VoxelUtility.InLocalGrid(lnp, d)) {
                                        smoothLightData = new LightData(sunLight[lni], blockLight[lni]);
                                    }
                                    else {
                                        GetNeighborData(in lnp, in d, in neighbors, out _, out smoothLightData);
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
                            
                            // Generate UV coordinates from voxel table.
                            //var origin = i == 4 || i == 5
                                //? VoxelTable.GetVoxelDefinition(voxel.Id).AtlasA * TEX_UV_WIDTH
                                //: VoxelTable.GetVoxelDefinition(voxel.Id).AtlasB * TEX_UV_WIDTH;

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
                                        0 => CalculateVertexLight(light.Sun, _lightWorkBuffer[4].Sun,
                                            _lightWorkBuffer[5].Sun, _lightWorkBuffer[6].Sun),
                                        1 => CalculateVertexLight(light.Sun, _lightWorkBuffer[6].Sun,
                                            _lightWorkBuffer[7].Sun, _lightWorkBuffer[0].Sun),
                                        2 => CalculateVertexLight(light.Sun, _lightWorkBuffer[0].Sun,
                                            _lightWorkBuffer[1].Sun, _lightWorkBuffer[2].Sun),
                                        3 => CalculateVertexLight(light.Sun, _lightWorkBuffer[2].Sun,
                                            _lightWorkBuffer[3].Sun, _lightWorkBuffer[4].Sun),
                                        _ => Color16.Clear().ToColor32()
                                    };
                                    vertexLightBlock = v switch {
                                        0 => CalculateVertexLight(light.Block, _lightWorkBuffer[4].Block,
                                            _lightWorkBuffer[5].Block, _lightWorkBuffer[6].Block),
                                        1 => CalculateVertexLight(light.Block, _lightWorkBuffer[6].Block,
                                            _lightWorkBuffer[7].Block, _lightWorkBuffer[0].Block),
                                        2 => CalculateVertexLight(light.Block, _lightWorkBuffer[0].Block,
                                            _lightWorkBuffer[1].Block, _lightWorkBuffer[2].Block),
                                        3 => CalculateVertexLight(light.Block, _lightWorkBuffer[2].Block,
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
            ReleaseNeighborLocks(in neighbors);
        }
        
        /// <summary>
        /// Sets generated mesh data to a given mesh.
        /// </summary>
        public void SetMeshData(ref Mesh mesh) {
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
        }
    }
}