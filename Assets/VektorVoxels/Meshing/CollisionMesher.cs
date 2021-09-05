using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using VektorVoxels.Chunks;
using VektorVoxels.Lighting;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Meshing {
    /// <summary>
    /// Generates the collision-only mesh for a given chunk.
    /// </summary>
    public sealed class CollisionMesher {
        // Thread-local instance.
        private static readonly ThreadLocal<CollisionMesher> _threadLocal = new ThreadLocal<CollisionMesher>(() => new CollisionMesher());
        public static CollisionMesher LocalThreadInstance => _threadLocal.Value;
        
        // Resulting mesh data.
        private readonly List<Vector3> _vertices;
        private readonly List<Vector3> _normals;
        private readonly List<int> _triangles;
        
        // Local work buffers to reduce calls to List.Add/AddRange.
        private readonly Vector3[] _vertexWorkBuffer;
        private readonly Vector3[] _normalWorkBuffer;
        private readonly int[] _triangleWorkBuffer;
        
        private static readonly VertexAttributeDescriptor[] _vertexBufferParams = {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1)
        };

        public CollisionMesher() {
            _vertices = new List<Vector3>();
            _normals = new List<Vector3>();
            _triangles = new List<int>();

            _vertexWorkBuffer = new Vector3[4];
            _normalWorkBuffer = new Vector3[4];
            _triangleWorkBuffer = new int[6];
        }

        public void GenerateMeshData(in Chunk chunk, NeighborSet neighbors) {
            // Clear work buffers.
            _vertices.Clear();
            _normals.Clear();
            _triangles.Clear();
            
            // Acquire read locks on provided neighbors.
            neighbors.AcquireReadLocks();
            
            // Shortcut some references.
            var voxels = chunk.VoxelData;
            var d = VoxelWorld.CHUNK_SIZE;
            
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
                        var voxel = voxels[VoxelUtility.VoxelIndex(vp, d)];

                        // Skip if voxel is null.
                        if (voxel.IsNull) continue;

                        // Iterate each neighbor and determine if a face should be added.
                        for (var i = 0; i < 6; i++) {
                            // Grab neighbor voxel depending on locality.
                            // Voxels outside the current grid will just be null.
                            var np = MeshTables.VoxelNeighbors[i] + vp;
                            var npi = VoxelUtility.VoxelIndex(np, d);
                            
                            VoxelData neighbor;

                            if (VoxelUtility.InLocalGrid(np, d)) {
                                neighbor = voxels[npi];
                            }
                            else {
                                MeshUtility.GetNeighborData(in np, in d, in neighbors, out neighbor, out _);
                            }
                            
                            // Skip face if neighbor has collision.
                            if (!neighbor.IsNull && !neighbor.HasFlag(VoxelFlags.NoCollision)) {
                                continue;
                            }

                            // Transform and add face vertex data.
                            // Vertex count pre-copy is stored for triangles later.
                            var vc = _vertices.Count;
                            for (var v = 0; v < 4; v++) {
                                var pos = MeshTables.Vertices[i][v];
                                pos.x += x; pos.y += y; pos.z += z;
                                
                                // Construct vertex data.
                                _vertexWorkBuffer[v] = pos;
                                _normalWorkBuffer[v] = MeshTables.Normals[i];
                            }
                            
                            // Copy vertex work buffer into mesh vertex data.
                            _vertices.AddRange(_vertexWorkBuffer);
                            _normals.AddRange(_normalWorkBuffer);

                            // Transform and add triangle data.
                            for (var t = 0; t < 6; t++) {
                                _triangleWorkBuffer[t] = MeshTables.Triangles[t] + vc;
                            }

                            // Add triangles.
                            _triangles.AddRange(_triangleWorkBuffer);
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
            data.SetIndexBufferParams(_triangles.Count, IndexFormat.UInt32);
            
            // Write in the vertex buffer data.
            var vertexBuffer = data.GetVertexData<Vector3>();
            var normalBuffer = data.GetVertexData<Vector3>(1);
            for (var i = 0; i < _vertices.Count; i++) {
                vertexBuffer[i] = _vertices[i];
                normalBuffer[i] = _normals[i];
            }

            // Configure sub-meshes and write index buffer data.
            // Sub-mesh index data is packed linearly.
            var indexBuffer = data.GetIndexData<uint>();
            for (var i = 0; i < _triangles.Count; i++) {
                indexBuffer[i] = (uint)_triangles[i];
            }

            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor(0, _triangles.Count, MeshTopology.Triangles));
        }
    }
}