using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VektorVoxels.Chunks;
using VektorVoxels.Data;
using VektorVoxels.Lighting;
using VektorVoxels.Meshing;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Jobs {
    /// <summary>
    /// Schedules and manages Burst-compiled meshing jobs.
    /// Handles texture rect preparation and mesh data output.
    /// </summary>
    public class MeshingJobScheduler : IDisposable {
        // Persistent lookup tables
        private NativeArray<float3> _faceVertices;
        private NativeArray<float3> _faceNormals;
        private NativeArray<int3> _neighborOffsets;
        private NativeArray<int3> _lightNeighborOffsets;
        private NativeArray<VoxelTextureRects> _textureRects;

        // Reusable output buffers (cleared between jobs)
        private NativeList<VertexNative> _vertices;
        private NativeList<uint> _opaqueIndices;
        private NativeList<uint> _alphaIndices;

        // Pending jobs
        private struct PendingMeshJob {
            public Vector2Int ChunkId;
            public JobHandle Handle;
            public Action<Vector2Int> Callback;
            public Mesh.MeshDataArray MeshDataArray;
        }

        private List<PendingMeshJob> _pendingJobs;
        private bool _initialized;

        // Vertex buffer layout: Position, Normal, UV, SunLight (Color32), BlockLight (Color32), TileRepeat
        private static readonly VertexAttributeDescriptor[] VertexBufferParams = {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2)
        };

        public bool IsInitialized => _initialized;

        /// <summary>
        /// Initializes the scheduler with lookup tables from VoxelTable.
        /// </summary>
        public void Initialize() {
            if (_initialized) return;

            // Initialize face vertices table
            _faceVertices = new NativeArray<float3>(MeshConstants.FaceVertices.Length, Allocator.Persistent);
            for (int i = 0; i < MeshConstants.FaceVertices.Length; i++) {
                _faceVertices[i] = MeshConstants.FaceVertices[i];
            }

            // Initialize face normals table
            _faceNormals = new NativeArray<float3>(MeshConstants.FaceNormals.Length, Allocator.Persistent);
            for (int i = 0; i < MeshConstants.FaceNormals.Length; i++) {
                _faceNormals[i] = MeshConstants.FaceNormals[i];
            }

            // Initialize neighbor offsets
            _neighborOffsets = new NativeArray<int3>(MeshConstants.NeighborOffsets.Length, Allocator.Persistent);
            for (int i = 0; i < MeshConstants.NeighborOffsets.Length; i++) {
                _neighborOffsets[i] = MeshConstants.NeighborOffsets[i];
            }

            // Initialize light neighbor offsets
            _lightNeighborOffsets = new NativeArray<int3>(MeshConstants.LightNeighborOffsets.Length, Allocator.Persistent);
            for (int i = 0; i < MeshConstants.LightNeighborOffsets.Length; i++) {
                _lightNeighborOffsets[i] = MeshConstants.LightNeighborOffsets[i];
            }

            // Build texture rects from VoxelTable
            BuildTextureRects();

            // Initialize output buffers with reasonable capacity
            // Max vertices: 16*256*16 * 6 faces * 4 verts = 25,165,824 (worst case)
            // Typical: much less due to occlusion
            _vertices = new NativeList<VertexNative>(65536, Allocator.Persistent);
            _opaqueIndices = new NativeList<uint>(98304, Allocator.Persistent);
            _alphaIndices = new NativeList<uint>(16384, Allocator.Persistent);

            _pendingJobs = new List<PendingMeshJob>();
            _initialized = true;

            Debug.Log("[MeshingJobScheduler] Initialized");
        }

        /// <summary>
        /// Builds the texture rect lookup table from VoxelTable.
        /// </summary>
        private void BuildTextureRects() {
            var voxelCount = VoxelTable.VoxelCount;
            _textureRects = new NativeArray<VoxelTextureRects>(voxelCount, Allocator.Persistent);

            for (int i = 0; i < voxelCount; i++) {
                var def = VoxelTable.Voxels[i];
                var rects = new VoxelTextureRects();

                // Convert Unity Rect to float4 (x, y, width, height)
                rects.North = RectToFloat4(def.TextureRects[0]);
                rects.East = RectToFloat4(def.TextureRects[1]);
                rects.South = RectToFloat4(def.TextureRects[2]);
                rects.West = RectToFloat4(def.TextureRects[3]);
                rects.Top = RectToFloat4(def.TextureRects[4]);
                rects.Bottom = RectToFloat4(def.TextureRects[5]);

                _textureRects[i] = rects;
            }
        }

        private static float4 RectToFloat4(Rect rect) {
            return new float4(rect.x, rect.y, rect.width, rect.height);
        }

        /// <summary>
        /// Executes mesh generation for a chunk synchronously.
        /// Returns true if successful.
        /// </summary>
        public bool ExecuteMeshing(Vector2Int chunkId, bool useSmoothLighting, ref Mesh.MeshDataArray meshDataArray) {
            if (!_initialized) {
                Debug.LogError("[MeshingJobScheduler] Not initialized");
                return false;
            }

            var store = VoxelWorld.Instance?.ChunkDataStore;
            if (store == null || store.IsDisposed) return false;

            var nativeId = new int2(chunkId.x, chunkId.y);
            if (!store.IsAllocated(nativeId)) return false;

            var chunkData = store.GetChunk(nativeId);

            // Gather neighbor light data
            var neighborFlags = NeighborDataFlags.None;
            var emptyArray = new NativeArray<VoxelColor>(0, Allocator.TempJob);

            // North neighbor (+Z)
            var northId = new int2(chunkId.x, chunkId.y + 1);
            NativeArray<VoxelColor> northSun = emptyArray, northBlock = emptyArray;
            if (store.IsAllocated(northId)) {
                var northData = store.GetChunk(northId);
                northSun = northData.SunLight;
                northBlock = northData.BlockLight;
                neighborFlags |= NeighborDataFlags.North;
            }

            // South neighbor (-Z)
            var southId = new int2(chunkId.x, chunkId.y - 1);
            NativeArray<VoxelColor> southSun = emptyArray, southBlock = emptyArray;
            if (store.IsAllocated(southId)) {
                var southData = store.GetChunk(southId);
                southSun = southData.SunLight;
                southBlock = southData.BlockLight;
                neighborFlags |= NeighborDataFlags.South;
            }

            // East neighbor (+X)
            var eastId = new int2(chunkId.x + 1, chunkId.y);
            NativeArray<VoxelColor> eastSun = emptyArray, eastBlock = emptyArray;
            if (store.IsAllocated(eastId)) {
                var eastData = store.GetChunk(eastId);
                eastSun = eastData.SunLight;
                eastBlock = eastData.BlockLight;
                neighborFlags |= NeighborDataFlags.East;
            }

            // West neighbor (-X)
            var westId = new int2(chunkId.x - 1, chunkId.y);
            NativeArray<VoxelColor> westSun = emptyArray, westBlock = emptyArray;
            if (store.IsAllocated(westId)) {
                var westData = store.GetChunk(westId);
                westSun = westData.SunLight;
                westBlock = westData.BlockLight;
                neighborFlags |= NeighborDataFlags.West;
            }

            // Clear output buffers
            _vertices.Clear();
            _opaqueIndices.Clear();
            _alphaIndices.Clear();

            // Create and run job
            var job = new VisualMeshingJob {
                Voxels = chunkData.Voxels,
                SunLight = chunkData.SunLight,
                BlockLight = chunkData.BlockLight,
                TextureRects = _textureRects,
                UseSmoothLighting = useSmoothLighting,
                NeighborNorthSunLight = northSun,
                NeighborNorthBlockLight = northBlock,
                NeighborSouthSunLight = southSun,
                NeighborSouthBlockLight = southBlock,
                NeighborEastSunLight = eastSun,
                NeighborEastBlockLight = eastBlock,
                NeighborWestSunLight = westSun,
                NeighborWestBlockLight = westBlock,
                NeighborFlags = neighborFlags,
                Vertices = _vertices,
                OpaqueIndices = _opaqueIndices,
                AlphaIndices = _alphaIndices,
                FaceVerticesTable = _faceVertices,
                FaceNormalsTable = _faceNormals,
                NeighborOffsetsTable = _neighborOffsets,
                LightNeighborOffsetsTable = _lightNeighborOffsets
            };

            // Run synchronously on main thread (avoids allocator restrictions)
            job.Run();

            // Dispose temp array
            emptyArray.Dispose();

            // Apply to mesh data array
            ApplyToMeshData(ref meshDataArray);

            return true;
        }

        /// <summary>
        /// Applies the generated vertex/index data to a MeshDataArray.
        /// </summary>
        private void ApplyToMeshData(ref Mesh.MeshDataArray meshDataArray) {
            var data = meshDataArray[0];

            var vertexCount = _vertices.Length;
            var opaqueCount = _opaqueIndices.Length;
            var alphaCount = _alphaIndices.Length;

            data.SetVertexBufferParams(vertexCount, VertexBufferParams);
            data.SetIndexBufferParams(opaqueCount + alphaCount, IndexFormat.UInt32);

            // Copy vertex data
            if (vertexCount > 0) {
                var vertexBuffer = data.GetVertexData<VertexNative>();
                NativeArray<VertexNative>.Copy(_vertices.AsArray(), vertexBuffer, vertexCount);
            }

            // Copy index data
            if (opaqueCount + alphaCount > 0) {
                var indexBuffer = data.GetIndexData<uint>();

                if (opaqueCount > 0) {
                    NativeArray<uint>.Copy(_opaqueIndices.AsArray(), 0, indexBuffer, 0, opaqueCount);
                }
                if (alphaCount > 0) {
                    NativeArray<uint>.Copy(_alphaIndices.AsArray(), 0, indexBuffer, opaqueCount, alphaCount);
                }
            }

            // Set submeshes
            data.subMeshCount = 2;
            data.SetSubMesh(0, new SubMeshDescriptor(0, opaqueCount, MeshTopology.Triangles));
            data.SetSubMesh(1, new SubMeshDescriptor(opaqueCount, alphaCount, MeshTopology.Triangles));
        }

        /// <summary>
        /// Syncs light data from managed chunk arrays to native arrays before meshing.
        /// Call after lighting completes and before ExecuteMeshing.
        /// </summary>
        public void SyncLightFromChunk(Vector2Int chunkId) {
            var store = VoxelWorld.Instance?.ChunkDataStore;
            if (store == null || store.IsDisposed) return;

            if (!VoxelWorld.Instance.IsChunkInBounds(chunkId)) return;
            if (!VoxelWorld.Instance.IsChunkLoaded(chunkId)) return;

            var chunk = VoxelWorld.Instance.Chunks[chunkId.x, chunkId.y];
            if (chunk == null) return;

            var nativeId = new int2(chunkId.x, chunkId.y);
            if (!store.IsAllocated(nativeId)) return;

            store.CopySunLightFrom(nativeId, chunk.SunLight);
            store.CopyBlockLightFrom(nativeId, chunk.BlockLight);
        }

        /// <summary>
        /// Updates the scheduler. Call once per frame to check for completed jobs.
        /// </summary>
        public void Update() {
            if (!_initialized || _pendingJobs == null) return;

            for (int i = _pendingJobs.Count - 1; i >= 0; i--) {
                var pending = _pendingJobs[i];
                if (pending.Handle.IsCompleted) {
                    pending.Handle.Complete();
                    pending.Callback?.Invoke(pending.ChunkId);
                    _pendingJobs.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Disposes all native containers.
        /// </summary>
        public void Dispose() {
            if (!_initialized) return;

            // Complete any pending jobs
            if (_pendingJobs != null) {
                foreach (var pending in _pendingJobs) {
                    pending.Handle.Complete();
                }
                _pendingJobs.Clear();
            }

            if (_faceVertices.IsCreated) _faceVertices.Dispose();
            if (_faceNormals.IsCreated) _faceNormals.Dispose();
            if (_neighborOffsets.IsCreated) _neighborOffsets.Dispose();
            if (_lightNeighborOffsets.IsCreated) _lightNeighborOffsets.Dispose();
            if (_textureRects.IsCreated) _textureRects.Dispose();
            if (_vertices.IsCreated) _vertices.Dispose();
            if (_opaqueIndices.IsCreated) _opaqueIndices.Dispose();
            if (_alphaIndices.IsCreated) _alphaIndices.Dispose();

            _initialized = false;
        }
    }
}
