using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Lighting;
using VektorVoxels.Voxels;

namespace VektorVoxels.Jobs {
    /// <summary>
    /// Burst-compatible texture rect data for a single voxel type.
    /// Contains 6 rects (one per face) stored as float4 (x, y, width, height).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelTextureRects {
        public float4 North;  // Face 0: +Z
        public float4 East;   // Face 1: +X
        public float4 South;  // Face 2: -Z
        public float4 West;   // Face 3: -X
        public float4 Top;    // Face 4: +Y
        public float4 Bottom; // Face 5: -Y

        public float4 GetFace(int faceIndex) {
            return faceIndex switch {
                0 => North,
                1 => East,
                2 => South,
                3 => West,
                4 => Top,
                5 => Bottom,
                _ => float4.zero
            };
        }
    }

    /// <summary>
    /// Burst-compatible vertex matching the existing Vertex struct layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VertexNative {
        public float3 Position;
        public float3 Normal;
        public float2 TexCoord;
        public uint SunLight;   // Color32 packed as uint (R|G|B|A)
        public uint BlockLight; // Color32 packed as uint
        public float2 TileRepeat;

        public VertexNative(float3 position, float3 normal, float2 texCoord, uint sunLight, uint blockLight) {
            Position = position;
            Normal = normal;
            TexCoord = texCoord;
            SunLight = sunLight;
            BlockLight = blockLight;
            TileRepeat = float2.zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint PackColor(byte r, byte g, byte b, byte a = 255) {
            return (uint)(r | (g << 8) | (b << 16) | (a << 24));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint PackColor(VoxelColor color) {
            color.Decompose(out int r, out int g, out int b, out int a);
            return PackColor((byte)r, (byte)g, (byte)b, (byte)a);
        }
    }

    /// <summary>
    /// Static mesh generation constants and lookup tables.
    /// </summary>
    public static class MeshConstants {
        public const int WIDTH = 16;
        public const int HEIGHT = 256;
        public const int DEPTH = 16;

        // Face vertices - 4 vertices per face, 6 faces
        // Stored as flat array: face * 4 + vertex
        public static readonly float3[] FaceVertices = {
            // Face 0: North (+Z)
            new float3(1, 0, 1), new float3(1, 1, 1), new float3(0, 1, 1), new float3(0, 0, 1),
            // Face 1: East (+X)
            new float3(1, 0, 0), new float3(1, 1, 0), new float3(1, 1, 1), new float3(1, 0, 1),
            // Face 2: South (-Z)
            new float3(0, 0, 0), new float3(0, 1, 0), new float3(1, 1, 0), new float3(1, 0, 0),
            // Face 3: West (-X)
            new float3(0, 0, 1), new float3(0, 1, 1), new float3(0, 1, 0), new float3(0, 0, 0),
            // Face 4: Top (+Y)
            new float3(0, 1, 0), new float3(0, 1, 1), new float3(1, 1, 1), new float3(1, 1, 0),
            // Face 5: Bottom (-Y)
            new float3(0, 0, 1), new float3(0, 0, 0), new float3(1, 0, 0), new float3(1, 0, 1)
        };

        // Face normals
        public static readonly float3[] FaceNormals = {
            new float3(0, 0, 1),   // North
            new float3(1, 0, 0),   // East
            new float3(0, 0, -1),  // South
            new float3(-1, 0, 0),  // West
            new float3(0, 1, 0),   // Top
            new float3(0, -1, 0)   // Bottom
        };

        // Neighbor offsets for face visibility checks
        public static readonly int3[] NeighborOffsets = {
            new int3(0, 0, 1),   // North
            new int3(1, 0, 0),   // East
            new int3(0, 0, -1),  // South
            new int3(-1, 0, 0),  // West
            new int3(0, 1, 0),   // Top
            new int3(0, -1, 0)   // Bottom
        };

        // Light neighbor offsets for smooth lighting (8 samples per face)
        // Indexed: face * 8 + sample
        public static readonly int3[] LightNeighborOffsets = {
            // Face 0: North (+Z)
            new int3(0, 1, 0), new int3(-1, 1, 0), new int3(-1, 0, 0), new int3(-1, -1, 0),
            new int3(0, -1, 0), new int3(1, -1, 0), new int3(1, 0, 0), new int3(1, 1, 0),
            // Face 1: East (+X)
            new int3(0, 1, 0), new int3(0, 1, 1), new int3(0, 0, 1), new int3(0, -1, 1),
            new int3(0, -1, 0), new int3(0, -1, -1), new int3(0, 0, -1), new int3(0, 1, -1),
            // Face 2: South (-Z)
            new int3(0, 1, 0), new int3(1, 1, 0), new int3(1, 0, 0), new int3(1, -1, 0),
            new int3(0, -1, 0), new int3(-1, -1, 0), new int3(-1, 0, 0), new int3(-1, 1, 0),
            // Face 3: West (-X)
            new int3(0, 1, 0), new int3(0, 1, -1), new int3(0, 0, -1), new int3(0, -1, -1),
            new int3(0, -1, 0), new int3(0, -1, 1), new int3(0, 0, 1), new int3(0, 1, 1),
            // Face 4: Top (+Y)
            new int3(0, 0, 1), new int3(1, 0, 1), new int3(1, 0, 0), new int3(1, 0, -1),
            new int3(0, 0, -1), new int3(-1, 0, -1), new int3(-1, 0, 0), new int3(-1, 0, 1),
            // Face 5: Bottom (-Y)
            new int3(0, 0, -1), new int3(1, 0, -1), new int3(1, 0, 0), new int3(1, 0, 1),
            new int3(0, 0, 1), new int3(-1, 0, 1), new int3(-1, 0, 0), new int3(-1, 0, -1)
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int VoxelIndex(int x, int y, int z) {
            return x + WIDTH * (y + HEIGHT * z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int VoxelIndex(int3 pos) {
            return pos.x + WIDTH * (pos.y + HEIGHT * pos.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InBounds(int3 pos) {
            return pos.x >= 0 && pos.x < WIDTH &&
                   pos.y >= 0 && pos.y < HEIGHT &&
                   pos.z >= 0 && pos.z < DEPTH;
        }
    }

    /// <summary>
    /// Flags indicating which neighbor chunks have valid data.
    /// </summary>
    [Flags]
    public enum NeighborDataFlags {
        None = 0,
        North = 1 << 0,  // +Z
        East = 1 << 1,   // +X
        South = 1 << 2,  // -Z
        West = 1 << 3,   // -X
    }

    /// <summary>
    /// Burst-compiled mesh generation job.
    /// Generates vertex and index data for a chunk's visual mesh.
    /// </summary>
    [BurstCompile]
    public struct VisualMeshingJob : IJob {
        // Input data
        [ReadOnly] public NativeArray<VoxelData> Voxels;
        [ReadOnly] public NativeArray<VoxelColor> SunLight;
        [ReadOnly] public NativeArray<VoxelColor> BlockLight;
        [ReadOnly] public NativeArray<VoxelTextureRects> TextureRects; // Indexed by voxelId - 1
        [ReadOnly] public bool UseSmoothLighting;

        // Neighbor chunk light data for boundary sampling
        [ReadOnly] public NativeArray<VoxelColor> NeighborNorthSunLight;   // +Z neighbor
        [ReadOnly] public NativeArray<VoxelColor> NeighborNorthBlockLight;
        [ReadOnly] public NativeArray<VoxelColor> NeighborEastSunLight;    // +X neighbor
        [ReadOnly] public NativeArray<VoxelColor> NeighborEastBlockLight;
        [ReadOnly] public NativeArray<VoxelColor> NeighborSouthSunLight;   // -Z neighbor
        [ReadOnly] public NativeArray<VoxelColor> NeighborSouthBlockLight;
        [ReadOnly] public NativeArray<VoxelColor> NeighborWestSunLight;    // -X neighbor
        [ReadOnly] public NativeArray<VoxelColor> NeighborWestBlockLight;
        [ReadOnly] public NeighborDataFlags NeighborFlags;

        // Output data
        public NativeList<VertexNative> Vertices;
        public NativeList<uint> OpaqueIndices;
        public NativeList<uint> AlphaIndices;

        // Precomputed tables (passed in to avoid static access in Burst)
        [ReadOnly] public NativeArray<float3> FaceVerticesTable;
        [ReadOnly] public NativeArray<float3> FaceNormalsTable;
        [ReadOnly] public NativeArray<int3> NeighborOffsetsTable;
        [ReadOnly] public NativeArray<int3> LightNeighborOffsetsTable;

        public void Execute() {
            Vertices.Clear();
            OpaqueIndices.Clear();
            AlphaIndices.Clear();

            // Process each voxel
            for (int z = 0; z < MeshConstants.DEPTH; z++) {
                for (int y = 0; y < MeshConstants.HEIGHT; y++) {
                    for (int x = 0; x < MeshConstants.WIDTH; x++) {
                        var voxelIdx = MeshConstants.VoxelIndex(x, y, z);
                        var voxel = Voxels[voxelIdx];

                        // Skip air/null voxels
                        if (voxel.Id == 0) continue;

                        var voxelPos = new int3(x, y, z);

                        // Check each face
                        for (int face = 0; face < 6; face++) {
                            ProcessFace(voxelPos, voxel, face);
                        }
                    }
                }
            }
        }

        private void ProcessFace(int3 voxelPos, VoxelData voxel, int face) {
            var neighborOffset = NeighborOffsetsTable[face];
            var neighborPos = voxelPos + neighborOffset;

            // Get neighbor voxel and light (sample from neighbor chunks if out of bounds)
            VoxelData neighbor;
            VoxelColor neighborSun;
            VoxelColor neighborBlock;

            if (MeshConstants.InBounds(neighborPos)) {
                var neighborIdx = MeshConstants.VoxelIndex(neighborPos);
                neighbor = Voxels[neighborIdx];
                neighborSun = SunLight[neighborIdx];
                neighborBlock = BlockLight[neighborIdx];
            }
            else {
                // Out of bounds - treat as air, sample light from neighbor chunk if available
                neighbor = VoxelData.Empty();
                SampleLight(neighborPos, out neighborSun, out neighborBlock);
            }

            // Skip if neighbor is opaque (not air and not alpha)
            if (neighbor.Id != 0 && (neighbor.Flags & VoxelFlags.AlphaRender) == 0) {
                return;
            }

            // Skip if same voxel type
            if (voxel.Id == neighbor.Id) {
                return;
            }

            // Get texture rect for this face
            var texRects = TextureRects[(int)voxel.Id - 1];
            var rect = texRects.GetFace(face);

            // Calculate UVs
            var uv0 = new float2(rect.x, rect.y + rect.w); // bottom-left
            var uv1 = new float2(rect.x, rect.y);          // top-left
            var uv2 = new float2(rect.x + rect.z, rect.y); // top-right
            var uv3 = new float2(rect.x + rect.z, rect.y + rect.w); // bottom-right

            var normal = FaceNormalsTable[face];
            var isAlpha = (voxel.Flags & VoxelFlags.AlphaRender) != 0;
            var isLightSource = (voxel.Flags & VoxelFlags.LightSource) != 0;

            // Calculate vertex lighting
            uint sunLight0, sunLight1, sunLight2, sunLight3;
            uint blockLight0, blockLight1, blockLight2, blockLight3;

            if (UseSmoothLighting) {
                CalculateSmoothLighting(neighborPos, face,
                    out sunLight0, out sunLight1, out sunLight2, out sunLight3,
                    out blockLight0, out blockLight1, out blockLight2, out blockLight3);
            }
            else {
                var flatSun = VertexNative.PackColor(neighborSun);
                var flatBlock = isLightSource ?
                    VertexNative.PackColor(voxel.ColorData) :
                    VertexNative.PackColor(neighborBlock);

                sunLight0 = sunLight1 = sunLight2 = sunLight3 = flatSun;
                blockLight0 = blockLight1 = blockLight2 = blockLight3 = flatBlock;
            }

            // Override block light for light sources
            if (isLightSource) {
                var lightColor = VertexNative.PackColor(voxel.ColorData);
                blockLight0 = blockLight1 = blockLight2 = blockLight3 = lightColor;
            }

            // Add vertices
            var vertexBase = (uint)Vertices.Length;
            var faceVertexBase = face * 4;

            for (int v = 0; v < 4; v++) {
                var localPos = FaceVerticesTable[faceVertexBase + v];
                var worldPos = localPos + new float3(voxelPos.x, voxelPos.y, voxelPos.z);

                var uv = v switch {
                    0 => uv0,
                    1 => uv1,
                    2 => uv2,
                    _ => uv3
                };

                var sunLight = v switch {
                    0 => sunLight0,
                    1 => sunLight1,
                    2 => sunLight2,
                    _ => sunLight3
                };

                var blockLight = v switch {
                    0 => blockLight0,
                    1 => blockLight1,
                    2 => blockLight2,
                    _ => blockLight3
                };

                Vertices.Add(new VertexNative(worldPos, normal, uv, sunLight, blockLight));
            }

            // Add triangle indices
            var indices = isAlpha ? AlphaIndices : OpaqueIndices;
            indices.Add(vertexBase);
            indices.Add(vertexBase + 1);
            indices.Add(vertexBase + 2);
            indices.Add(vertexBase);
            indices.Add(vertexBase + 2);
            indices.Add(vertexBase + 3);
        }

        private void CalculateSmoothLighting(int3 neighborPos, int face,
            out uint sun0, out uint sun1, out uint sun2, out uint sun3,
            out uint block0, out uint block1, out uint block2, out uint block3) {

            // Sample 8 neighbors for this face
            var lightBase = face * 8;

            // Get center light (sample from neighbor if out of bounds)
            SampleLight(neighborPos, out var centerSun, out var centerBlock);

            // Sample all 8 neighbors
            var sunSamples = new NativeArray<VoxelColor>(8, Allocator.Temp);
            var blockSamples = new NativeArray<VoxelColor>(8, Allocator.Temp);

            for (int i = 0; i < 8; i++) {
                var samplePos = neighborPos + LightNeighborOffsetsTable[lightBase + i];
                SampleLight(samplePos, out var sunSample, out var blockSample);
                sunSamples[i] = sunSample;
                blockSamples[i] = blockSample;
            }

            // Calculate averaged vertex colors
            // Vertex 0: samples 4, 5, 6
            sun0 = AverageLight(centerSun, sunSamples[4], sunSamples[5], sunSamples[6]);
            block0 = AverageLight(centerBlock, blockSamples[4], blockSamples[5], blockSamples[6]);

            // Vertex 1: samples 6, 7, 0
            sun1 = AverageLight(centerSun, sunSamples[6], sunSamples[7], sunSamples[0]);
            block1 = AverageLight(centerBlock, blockSamples[6], blockSamples[7], blockSamples[0]);

            // Vertex 2: samples 0, 1, 2
            sun2 = AverageLight(centerSun, sunSamples[0], sunSamples[1], sunSamples[2]);
            block2 = AverageLight(centerBlock, blockSamples[0], blockSamples[1], blockSamples[2]);

            // Vertex 3: samples 2, 3, 4
            sun3 = AverageLight(centerSun, sunSamples[2], sunSamples[3], sunSamples[4]);
            block3 = AverageLight(centerBlock, blockSamples[2], blockSamples[3], blockSamples[4]);

            sunSamples.Dispose();
            blockSamples.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint AverageLight(VoxelColor center, VoxelColor a, VoxelColor b, VoxelColor c) {
            center.Decompose(out int cr, out int cg, out int cb, out _);
            a.Decompose(out int ar, out int ag, out int ab, out _);
            b.Decompose(out int br, out int bg, out int bb, out _);
            c.Decompose(out int ccr, out int ccg, out int ccb, out _);

            var r = (byte)((cr + ar + br + ccr) >> 2);
            var g = (byte)((cg + ag + bg + ccg) >> 2);
            var bb2 = (byte)((cb + ab + bb + ccb) >> 2);

            return VertexNative.PackColor(r, g, bb2, 255);
        }

        /// <summary>
        /// Samples light at a position, handling neighbor chunk boundaries.
        /// When neighbor data isn't available, uses edge light from current chunk for continuity.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SampleLight(int3 pos, out VoxelColor sun, out VoxelColor block) {
            // In bounds - sample directly
            if (MeshConstants.InBounds(pos)) {
                var idx = MeshConstants.VoxelIndex(pos);
                sun = SunLight[idx];
                block = BlockLight[idx];
                return;
            }

            // Out of bounds in Y - return black (top/bottom of world)
            if (pos.y < 0 || pos.y >= MeshConstants.HEIGHT) {
                sun = new VoxelColor(0, 0, 0);
                block = new VoxelColor(0, 0, 0);
                return;
            }

            // Check which neighbor to sample from
            // +Z neighbor (North)
            if (pos.z >= MeshConstants.DEPTH && (NeighborFlags & NeighborDataFlags.North) != 0) {
                var wrappedPos = new int3(pos.x, pos.y, pos.z - MeshConstants.DEPTH);
                if (wrappedPos.x >= 0 && wrappedPos.x < MeshConstants.WIDTH) {
                    var idx = MeshConstants.VoxelIndex(wrappedPos);
                    sun = NeighborNorthSunLight[idx];
                    block = NeighborNorthBlockLight[idx];
                    return;
                }
            }

            // -Z neighbor (South)
            if (pos.z < 0 && (NeighborFlags & NeighborDataFlags.South) != 0) {
                var wrappedPos = new int3(pos.x, pos.y, pos.z + MeshConstants.DEPTH);
                if (wrappedPos.x >= 0 && wrappedPos.x < MeshConstants.WIDTH) {
                    var idx = MeshConstants.VoxelIndex(wrappedPos);
                    sun = NeighborSouthSunLight[idx];
                    block = NeighborSouthBlockLight[idx];
                    return;
                }
            }

            // +X neighbor (East)
            if (pos.x >= MeshConstants.WIDTH && (NeighborFlags & NeighborDataFlags.East) != 0) {
                var wrappedPos = new int3(pos.x - MeshConstants.WIDTH, pos.y, pos.z);
                if (wrappedPos.z >= 0 && wrappedPos.z < MeshConstants.DEPTH) {
                    var idx = MeshConstants.VoxelIndex(wrappedPos);
                    sun = NeighborEastSunLight[idx];
                    block = NeighborEastBlockLight[idx];
                    return;
                }
            }

            // -X neighbor (West)
            if (pos.x < 0 && (NeighborFlags & NeighborDataFlags.West) != 0) {
                var wrappedPos = new int3(pos.x + MeshConstants.WIDTH, pos.y, pos.z);
                if (wrappedPos.z >= 0 && wrappedPos.z < MeshConstants.DEPTH) {
                    var idx = MeshConstants.VoxelIndex(wrappedPos);
                    sun = NeighborWestSunLight[idx];
                    block = NeighborWestBlockLight[idx];
                    return;
                }
            }

            // No neighbor data available - use edge light from current chunk for continuity.
            // Clamp position to valid range and sample from our border.
            var clampedPos = new int3(
                math.clamp(pos.x, 0, MeshConstants.WIDTH - 1),
                pos.y,
                math.clamp(pos.z, 0, MeshConstants.DEPTH - 1)
            );
            var clampedIdx = MeshConstants.VoxelIndex(clampedPos);
            sun = SunLight[clampedIdx];
            block = BlockLight[clampedIdx];
        }
    }
}
