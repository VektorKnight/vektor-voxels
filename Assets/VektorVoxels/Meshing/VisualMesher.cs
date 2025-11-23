// Copyright 2021, Derek de la Peza (aka VektorKnight)
// The following code is subject to the license terms defined in LICENSE.md

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using VektorVoxels.Chunks;
using VektorVoxels.Config;
using VektorVoxels.Lighting;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Meshing {
    /// <summary>
    /// Performs Minecraft-style cubic meshing of a voxel grid.
    /// Thread-local instances accessed via LocalThreadInstance for job system.
    /// Outputs two submeshes: opaque (index 0) and alpha/translucent (index 1).
    /// </summary>
    public class VisualMesher {
        // Texture atlas: 256x256 pixels total, 16x16 per block texture = 16 textures per row.
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

        // Custom vertex layout for voxel meshes (48 bytes per vertex):
        // Position (12), Normal (12), UV (8), SunLight as Color32 (4), BlockLight as Color32 (4), TileRepeat (8).
        // Light packed as UNorm8x4 for GPU efficiency - each RGBA channel stores a light component.
        private static readonly VertexAttributeDescriptor[] _vertexBufferParams = {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2)
        };

        // Greedy meshing mask entry - stores face data for merging.
        private struct FaceMask {
            public ushort VoxelId;
            public VoxelFlags Flags;
            public LightData Light;
            public VoxelColor VoxelColor; // For light source voxels

            public bool CanMerge(in FaceMask other) {
                return VoxelId == other.VoxelId &&
                       Flags == other.Flags &&
                       Light.Sun.Equals(other.Light.Sun) &&
                       Light.Block.Equals(other.Light.Block);
            }
        }

        // Reusable mask buffer for greedy meshing (max slice size: 16x256 for XZ faces).
        private readonly FaceMask[] _greedyMask = new FaceMask[16 * 256];

        public VisualMesher() {
            _vertices = new List<Vertex>();
            _trianglesA = new List<int>();
            _trianglesB = new List<int>();
        }

        /// <summary>
        /// Calculates smooth vertex lighting by averaging neighboring light values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Color32 GetVertexLight(int vertexIndex, VoxelColor center, Span<LightData> neighbors) {
            return vertexIndex switch {
                0 => MeshUtility.CalculateVertexLight(center, neighbors[4].Sun, neighbors[5].Sun, neighbors[6].Sun),
                1 => MeshUtility.CalculateVertexLight(center, neighbors[6].Sun, neighbors[7].Sun, neighbors[0].Sun),
                2 => MeshUtility.CalculateVertexLight(center, neighbors[0].Sun, neighbors[1].Sun, neighbors[2].Sun),
                3 => MeshUtility.CalculateVertexLight(center, neighbors[2].Sun, neighbors[3].Sun, neighbors[4].Sun),
                _ => VoxelColor.Clear().ToColor32()
            };
        }

        /// <summary>
        /// Calculates smooth vertex block lighting by averaging neighboring light values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Color32 GetVertexBlockLight(int vertexIndex, VoxelColor center, Span<LightData> neighbors) {
            return vertexIndex switch {
                0 => MeshUtility.CalculateVertexLight(center, neighbors[4].Block, neighbors[5].Block, neighbors[6].Block),
                1 => MeshUtility.CalculateVertexLight(center, neighbors[6].Block, neighbors[7].Block, neighbors[0].Block),
                2 => MeshUtility.CalculateVertexLight(center, neighbors[0].Block, neighbors[1].Block, neighbors[2].Block),
                3 => MeshUtility.CalculateVertexLight(center, neighbors[2].Block, neighbors[3].Block, neighbors[4].Block),
                _ => VoxelColor.Clear().ToColor32()
            };
        }

        /// <summary>
        /// Populates the light work buffer with smooth lighting data from neighboring voxels.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PopulateSmoothLightBuffer(int faceIndex, Vector3Int np, Vector2Int d,
            VoxelColor[] sunLight, VoxelColor[] blockLight, in NeighborSet neighbors, Span<LightData> lightBuffer) {
            for (var l = 0; l < 8; l++) {
                var lnp = MeshTables.LightNeighbors[faceIndex][l] + np;
                var lni = VoxelUtility.VoxelIndex(lnp, d);

                if (VoxelUtility.InLocalGrid(lnp, d)) {
                    lightBuffer[l] = new LightData(sunLight[lni], blockLight[lni]);
                }
                else {
                    MeshUtility.GetNeighborData(in lnp, in d, in neighbors, out _, out var tempLight);
                    lightBuffer[l] = tempLight;
                }
            }
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

            // Clear output lists.
            _vertices.Clear();
            _trianglesA.Clear();
            _trianglesB.Clear();

            // Stack-allocated work buffers for hot path.
            Span<LightData> lightBuffer = stackalloc LightData[8];
            
            // Process each voxel and add the appropriate faces.
            // Note: variable naming may seem a bit weird here.
            // To clarify, anything ending in 'p' is a 3D index/position and anything ending in 'i' is a 1D index.
            // Ex: np, npi -> 'Neighbor Position', 'Neighbor Position Index'.
            var vp = Vector3Int.zero;
            for (var z = 0; z < d.x; z++) {
                for (var y = 0; y < d.y; y++) {
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
                                PopulateSmoothLightBuffer(i, np, d, sunLight, blockLight, in neighbors, lightBuffer);
                            }

                            // Skip the face if this voxel is opaque and the neighbor is opaque.
                            if (neighbor.Id != 0 && (neighbor.Flags & VoxelFlags.AlphaRender) == 0) {
                                continue;
                            }
                            
                            // Skip face if this voxel and the neighbor are the same.
                            if (voxel.Id == neighbor.Id) {
                                continue;
                            }
                            
                            // Grab the texture rect corresponding to the current face.
                            var rect = voxelDef.TextureRects[i];
                            var uv0 = new Vector2(rect.position.x, rect.yMax);
                            var uv1 = rect.position;
                            var uv2 = new Vector2(rect.xMax, rect.position.y);
                            var uv3 = rect.max;

                            // Vertex count pre-copy is stored for triangles later.
                            var vc = _vertices.Count;
                            var normal = MeshTables.Normals[i];
                            var isLightSource = (voxel.Flags & VoxelFlags.LightSource) != 0;
                            var voxelColor = voxel.ColorData.ToColor32();

                            // Pre-calculate flat lighting values if not using smooth lighting.
                            var flatSun = smoothLight ? default : light.Sun.ToColor32();
                            var flatBlock = smoothLight ? default : light.Block.ToColor32();

                            // Generate and add all 4 vertices for this face.
                            for (var v = 0; v < 4; v++) {
                                var pos = MeshTables.Vertices[i][v];
                                pos.x += x; pos.y += y; pos.z += z;

                                // Generate vertex light values.
                                // Smooth lighting averages the neighboring light values for each vertex.
                                // This also gives us AO for free :D
                                Color32 vertexLightSun, vertexLightBlock;
                                if (smoothLight) {
                                    vertexLightSun = GetVertexLight(v, light.Sun, lightBuffer);
                                    vertexLightBlock = GetVertexBlockLight(v, light.Block, lightBuffer);
                                }
                                else {
                                    vertexLightSun = flatSun;
                                    vertexLightBlock = flatBlock;
                                }

                                // Get UV for this vertex.
                                var uv = v switch {
                                    0 => uv0,
                                    1 => uv1,
                                    2 => uv2,
                                    _ => uv3
                                };

                                // Add vertex directly to list.
                                _vertices.Add(new Vertex(
                                    pos,
                                    normal,
                                    uv,
                                    vertexLightSun,
                                    isLightSource ? voxelColor : vertexLightBlock
                                ));
                            }

                            // Add triangle indices to opaque or alpha layer depending on flags.
                            // Triangle indices are computed inline to avoid work buffer overhead.
                            if ((voxel.Flags & VoxelFlags.AlphaRender) == 0) {
                                _trianglesA.Add(vc);
                                _trianglesA.Add(vc + 1);
                                _trianglesA.Add(vc + 2);
                                _trianglesA.Add(vc);
                                _trianglesA.Add(vc + 2);
                                _trianglesA.Add(vc + 3);
                            }
                            else {
                                _trianglesB.Add(vc);
                                _trianglesB.Add(vc + 1);
                                _trianglesB.Add(vc + 2);
                                _trianglesB.Add(vc);
                                _trianglesB.Add(vc + 2);
                                _trianglesB.Add(vc + 3);
                            }
                        }
                    }
                }
            }
            
            // Release neighbor locks.
            neighbors.ReleaseReadLocks();
        }

        /// <summary>
        /// Generates mesh data using greedy meshing for flat lighting mode.
        /// Merges adjacent faces with identical properties into larger quads.
        /// </summary>
        public void GenerateMeshDataGreedy(in Chunk chunk, NeighborSet neighbors) {
            neighbors.AcquireReadLocks();

            var voxelGrid = chunk.VoxelData;
            var sunLight = chunk.SunLight;
            var blockLight = chunk.BlockLight;
            var d = VoxelWorld.CHUNK_SIZE;

            _vertices.Clear();
            _trianglesA.Clear();
            _trianglesB.Clear();

            // Process each face direction.
            // 0: +Z, 1: +X, 2: -Z, 3: -X, 4: +Y, 5: -Y
            for (var face = 0; face < 6; face++) {
                var neighborDir = MeshTables.VoxelNeighbors[face];
                var normal = MeshTables.Normals[face];

                // Determine slice axis and dimensions based on face direction.
                int sliceAxis, uAxis, vAxis, sliceCount, uSize, vSize;
                GetSliceAxes(face, d, out sliceAxis, out uAxis, out vAxis, out sliceCount, out uSize, out vSize);

                // Process each slice along the perpendicular axis.
                for (var slice = 0; slice < sliceCount; slice++) {
                    // Build the face mask for this slice.
                    BuildFaceMask(voxelGrid, sunLight, blockLight, neighbors, d, face, neighborDir,
                        sliceAxis, uAxis, vAxis, slice, uSize, vSize);

                    // Greedy merge faces in this slice.
                    for (var v = 0; v < vSize; v++) {
                        for (var u = 0; u < uSize;) {
                            var maskIdx = v * uSize + u;
                            var current = _greedyMask[maskIdx];

                            // Skip empty faces.
                            if (current.VoxelId == 0) {
                                u++;
                                continue;
                            }

                            // Expand width while faces can merge.
                            var width = 1;
                            while (u + width < uSize) {
                                var nextIdx = v * uSize + u + width;
                                if (!current.CanMerge(in _greedyMask[nextIdx])) break;
                                width++;
                            }

                            // Expand height while entire row can merge.
                            var height = 1;
                            var canExpand = true;
                            while (v + height < vSize && canExpand) {
                                for (var w = 0; w < width; w++) {
                                    var checkIdx = (v + height) * uSize + u + w;
                                    if (!current.CanMerge(in _greedyMask[checkIdx])) {
                                        canExpand = false;
                                        break;
                                    }
                                }
                                if (canExpand) height++;
                            }

                            // Generate quad for this merged region.
                            AddGreedyQuad(face, sliceAxis, uAxis, vAxis, slice, u, v, width, height,
                                current, normal, d);

                            // Clear consumed faces from mask.
                            for (var cv = 0; cv < height; cv++) {
                                for (var cu = 0; cu < width; cu++) {
                                    _greedyMask[(v + cv) * uSize + u + cu].VoxelId = 0;
                                }
                            }

                            u += width;
                        }
                    }
                }
            }

            neighbors.ReleaseReadLocks();
        }

        /// <summary>
        /// Determines slice axes and dimensions for a given face direction.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetSliceAxes(int face, Vector2Int d, out int sliceAxis, out int uAxis, out int vAxis,
            out int sliceCount, out int uSize, out int vSize) {
            // d.x = chunk width (X and Z = 16), d.y = chunk height (Y = 256)
            // Axes: 0=X, 1=Y, 2=Z
            switch (face) {
                case 0: // +Z: slice along Z, face in XY plane
                case 2: // -Z
                    sliceAxis = 2; uAxis = 0; vAxis = 1;
                    sliceCount = d.x; uSize = d.x; vSize = d.y;
                    break;
                case 1: // +X: slice along X, face in ZY plane
                case 3: // -X
                    sliceAxis = 0; uAxis = 2; vAxis = 1;
                    sliceCount = d.x; uSize = d.x; vSize = d.y;
                    break;
                default: // 4: +Y, 5: -Y: slice along Y, face in XZ plane
                    sliceAxis = 1; uAxis = 0; vAxis = 2;
                    sliceCount = d.y; uSize = d.x; vSize = d.x;
                    break;
            }
        }

        /// <summary>
        /// Builds the face mask for a single slice.
        /// </summary>
        private void BuildFaceMask(VoxelData[] voxelGrid, VoxelColor[] sunLight, VoxelColor[] blockLight,
            in NeighborSet neighbors, Vector2Int d, int face, Vector3Int neighborDir,
            int sliceAxis, int uAxis, int vAxis, int slice, int uSize, int vSize) {

            var vp = Vector3Int.zero;
            var np = Vector3Int.zero;

            for (var v = 0; v < vSize; v++) {
                for (var u = 0; u < uSize; u++) {
                    var maskIdx = v * uSize + u;

                    // Build voxel position from slice coordinates.
                    vp[sliceAxis] = slice;
                    vp[uAxis] = u;
                    vp[vAxis] = v;

                    var voxelIdx = VoxelUtility.VoxelIndex(vp, d);
                    var voxel = voxelGrid[voxelIdx];

                    // Skip empty voxels.
                    if (voxel.Id == 0) {
                        _greedyMask[maskIdx].VoxelId = 0;
                        continue;
                    }

                    // Check neighbor.
                    np = vp + neighborDir;
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

                    // Check if face should be rendered.
                    var shouldRender = true;

                    // Skip if neighbor is opaque.
                    if (neighbor.Id != 0 && (neighbor.Flags & VoxelFlags.AlphaRender) == 0) {
                        shouldRender = false;
                    }
                    // Skip if same voxel type.
                    else if (voxel.Id == neighbor.Id) {
                        shouldRender = false;
                    }

                    if (shouldRender) {
                        _greedyMask[maskIdx].VoxelId = voxel.Id;
                        _greedyMask[maskIdx].Flags = voxel.Flags;
                        _greedyMask[maskIdx].Light = light;
                        _greedyMask[maskIdx].VoxelColor = voxel.ColorData;
                    }
                    else {
                        _greedyMask[maskIdx].VoxelId = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Adds a merged quad to the mesh.
        /// </summary>
        private void AddGreedyQuad(int face, int sliceAxis, int uAxis, int vAxis,
            int slice, int u, int v, int width, int height, in FaceMask mask, Vector3 normal, Vector2Int d) {

            var voxelDef = VoxelTable.GetVoxelDefinition(mask.VoxelId);
            var rect = voxelDef.TextureRects[face];
            var isAlpha = (mask.Flags & VoxelFlags.AlphaRender) != 0;

            // Calculate lighting.
            var sunColor = mask.Light.Sun.ToColor32();
            var isLightSource = (mask.Flags & VoxelFlags.LightSource) != 0;
            var blockColor = isLightSource ? mask.VoxelColor.ToColor32() : mask.Light.Block.ToColor32();

            // Calculate vertex positions based on face direction.
            // Must match MeshTables.Vertices winding order exactly.
            var p0 = Vector3.zero;
            var p1 = Vector3.zero;
            var p2 = Vector3.zero;
            var p3 = Vector3.zero;

            // Set slice position (offset by 1 for positive-facing faces).
            var slicePos = (face == 0 || face == 1 || face == 4) ? slice + 1 : slice;

            // Build corner positions matching MeshTables.Vertices winding.
            // Original vertices go: bottom-right, top-right, top-left, bottom-left (clockwise from bottom-left view)
            switch (face) {
                case 0: // +Z front: vertices at z=slicePos, x varies, y varies
                    // Original: (1,0,1), (1,1,1), (0,1,1), (0,0,1)
                    p0 = new Vector3(u + width, v, slicePos);
                    p1 = new Vector3(u + width, v + height, slicePos);
                    p2 = new Vector3(u, v + height, slicePos);
                    p3 = new Vector3(u, v, slicePos);
                    break;
                case 1: // +X right: vertices at x=slicePos, z varies, y varies
                    // Original: (1,0,0), (1,1,0), (1,1,1), (1,0,1)
                    p0 = new Vector3(slicePos, v, u);
                    p1 = new Vector3(slicePos, v + height, u);
                    p2 = new Vector3(slicePos, v + height, u + width);
                    p3 = new Vector3(slicePos, v, u + width);
                    break;
                case 2: // -Z back: vertices at z=slicePos, x varies, y varies
                    // Original: (0,0,0), (0,1,0), (1,1,0), (1,0,0)
                    p0 = new Vector3(u, v, slicePos);
                    p1 = new Vector3(u, v + height, slicePos);
                    p2 = new Vector3(u + width, v + height, slicePos);
                    p3 = new Vector3(u + width, v, slicePos);
                    break;
                case 3: // -X left: vertices at x=slicePos, z varies, y varies
                    // Original: (0,0,1), (0,1,1), (0,1,0), (0,0,0)
                    p0 = new Vector3(slicePos, v, u + width);
                    p1 = new Vector3(slicePos, v + height, u + width);
                    p2 = new Vector3(slicePos, v + height, u);
                    p3 = new Vector3(slicePos, v, u);
                    break;
                case 4: // +Y top: vertices at y=slicePos, x varies, z varies
                    // Original: (0,1,0), (0,1,1), (1,1,1), (1,1,0)
                    p0 = new Vector3(u, slicePos, v);
                    p1 = new Vector3(u, slicePos, v + height);
                    p2 = new Vector3(u + width, slicePos, v + height);
                    p3 = new Vector3(u + width, slicePos, v);
                    break;
                case 5: // -Y bottom: vertices at y=slicePos, x varies, z varies
                    // Original: (0,0,1), (0,0,0), (1,0,0), (1,0,1)
                    p0 = new Vector3(u, slicePos, v + height);
                    p1 = new Vector3(u, slicePos, v);
                    p2 = new Vector3(u + width, slicePos, v);
                    p3 = new Vector3(u + width, slicePos, v + height);
                    break;
            }

            // Calculate UVs for merged quad.
            // Original UV mapping: uv0=(minU, maxV), uv1=(minU, minV), uv2=(maxU, minV), uv3=(maxU, maxV)
            var uv0 = new Vector2(rect.x, rect.yMax);
            var uv1 = new Vector2(rect.x, rect.y);
            var uv2 = new Vector2(rect.xMax, rect.y);
            var uv3 = new Vector2(rect.xMax, rect.yMax);

            // Tile repeat counts for shader-based atlas tiling.
            var tileRepeat = new Vector2(width, height);

            // Add vertices.
            var vc = _vertices.Count;
            _vertices.Add(new Vertex(p0, normal, uv0, sunColor, blockColor, tileRepeat));
            _vertices.Add(new Vertex(p1, normal, uv1, sunColor, blockColor, tileRepeat));
            _vertices.Add(new Vertex(p2, normal, uv2, sunColor, blockColor, tileRepeat));
            _vertices.Add(new Vertex(p3, normal, uv3, sunColor, blockColor, tileRepeat));

            // Add triangles to appropriate list.
            var triangles = isAlpha ? _trianglesB : _trianglesA;
            triangles.Add(vc);
            triangles.Add(vc + 1);
            triangles.Add(vc + 2);
            triangles.Add(vc);
            triangles.Add(vc + 2);
            triangles.Add(vc + 3);
        }

        /// <summary>
        /// Applies the recently generated mesh data to a provided mesh data array.
        /// </summary>
        public void ApplyMeshData(ref Mesh.MeshDataArray meshData) {
            var data = meshData[0];

            var vertexCount = _vertices.Count;
            var triangleACount = _trianglesA.Count;
            var triangleBCount = _trianglesB.Count;

            data.SetVertexBufferParams(vertexCount, _vertexBufferParams);
            data.SetIndexBufferParams(triangleACount + triangleBCount, IndexFormat.UInt32);

            // Write vertex buffer data.
            var vertexBuffer = data.GetVertexData<Vertex>();
            for (var i = 0; i < vertexCount; i++) {
                vertexBuffer[i] = _vertices[i];
            }

            // Write index buffer data.
            var indexBuffer = data.GetIndexData<uint>();
            for (var i = 0; i < triangleACount; i++) {
                indexBuffer[i] = (uint)_trianglesA[i];
            }
            for (var i = 0; i < triangleBCount; i++) {
                indexBuffer[i + triangleACount] = (uint)_trianglesB[i];
            }

            data.subMeshCount = 2;
            data.SetSubMesh(0, new SubMeshDescriptor(0, triangleACount, MeshTopology.Triangles));
            data.SetSubMesh(1, new SubMeshDescriptor(triangleACount, triangleBCount, MeshTopology.Triangles));
        }
    }
}