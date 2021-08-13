using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using VektorVoxels.Lighting;
using VektorVoxels.Meshing;
using VektorVoxels.Threading;
using VektorVoxels.Voxels;
using VektorVoxels.World;
using Debug = UnityEngine.Debug;

namespace VektorVoxels.Chunks {
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public sealed class Chunk : MonoBehaviour {
        private static readonly Vector2Int[] _chunkNeighbors = {
            Vector2Int.up, 
            Vector2Int.right, 
            Vector2Int.down, 
            Vector2Int.left,
        };
        
        [Header("Config")] 
        [SerializeField] private Material _opaqueMaterial;
        [SerializeField] private Material _alphaMaterial;
        
        // Required components.
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private MeshCollider _meshCollider;
        
        // Lightmapper and mesher instances.
        // TODO: Pool these.
        private LightMapper _lightMapper;
        private CubicMesher _mesher;
        private Mesh _mesh;
        private Chunk[] _neighborBuffer;
        private NeighborFlags _neighborFlags;
        
        // World data.
        private Vector2Int _chunkId;
        private VoxelData[] _voxelData;
        private Color16[] _sunLight;
        private Color16[] _blockLight;
        private HeightData[] _heightMap;
        
        // Mesh data.
        private ChunkState _state;
        private bool _waitingForJob;
        
        // Useful accessors.
        public Vector2Int ChunkId => _chunkId;
        public VoxelData[] VoxelData => _voxelData;
        public HeightData[] HeightMap => _heightMap;
        public Color16[] BlockLight => _blockLight;
        public Color16[] SunLight => _sunLight;
        public ChunkState State => _state;

        public void Initialize(Vector2Int id) {
            _chunkId = id;
            
            // Reference required components.
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _meshCollider = GetComponent<MeshCollider>();

            _meshRenderer.sharedMaterials = new[] {
                _opaqueMaterial,
                _alphaMaterial
            };
            _meshRenderer.enabled = false;
            
            _meshCollider.convex = false;

            // Mesher and lightmapper.
            _mesher = new CubicMesher();
            _lightMapper = new LightMapper();
            _mesh = new Mesh() {
                name = $"ChunkMesh-{GetInstanceID()}",
                indexFormat = IndexFormat.UInt32
            };
            _neighborBuffer = new Chunk[4];

            _meshFilter.mesh = _mesh;
            _meshCollider.sharedMesh = _mesh;
            
            // World data.
            var dimensions = WorldManager.Instance.ChunkSize;
            var dataSize = dimensions.x * dimensions.y * dimensions.z;
            _voxelData = new VoxelData[dataSize];
            _sunLight = new Color16[dataSize];
            _blockLight = new Color16[dataSize];
            _heightMap = new HeightData[dimensions.x * dimensions.z];
            
            // Generate voxel data.
            GlobalThreadPool.QueueWorkItem(() => {
                for (var z = 0; z < dimensions.z; z++) {
                    for (var x = 0; x < dimensions.x; x++) {
                        _voxelData[VoxelUtility.VoxelIndex(x, 0, z, in dimensions)] = VoxelTable.ById(1).GetDataInstance();
                        _heightMap[VoxelUtility.HeightIndex(x, z, dimensions.x)] = new HeightData(0, true);
                    }
                }

                _voxelData[VoxelUtility.VoxelIndex(0, 1, 0, in dimensions)] = VoxelTable.ById(11).GetDataInstance();
                _heightMap[VoxelUtility.HeightIndex(0, 0, dimensions.x)] = new HeightData(1, true);

                GlobalThreadPool.QueueOnMain(OnGenerationComplete);
            });
        }

        public void OnGenerationComplete() {
            // Queue light first pass on the threadpool.
            _waitingForJob = true;
            GlobalThreadPool.QueueWorkItem(() => {
                var sw = new Stopwatch();
                sw.Start();
                if (_chunkId.x % 2 == 0) {
                    _lightMapper.InitializeSunLightFirstPass(this);
                    _lightMapper.PropagateSunLight(this);
                }
                sw.Stop();
                Debug.Log($"Light Pass 1 (Sun): {sw.ElapsedMilliseconds}ms");

                sw.Reset();
                _lightMapper.InitializeBlockLightFirstPass(this);
                _lightMapper.PropagateBlockLight(this);
                sw.Stop();
                Debug.Log($"Light Pass 1 (Block): {sw.ElapsedMilliseconds}ms");
                
                GlobalThreadPool.QueueOnMain(OnLightFirstPassComplete);
            });
            
            _state = ChunkState.LightFirstPass;
        }

        public void OnLightFirstPassComplete() {
            _waitingForJob = false;
            _state = ChunkState.WaitingForNeighbors;
        }

        public void OnLightLastPassComplete() {
            _waitingForJob = true;
            GlobalThreadPool.QueueWorkItem(() => {
                var sw = new Stopwatch();
                sw.Start();
                _lightMapper.InitializeNeighborLightPass(this, new NeighborSet(_neighborBuffer), _neighborFlags);
                _lightMapper.PropagateSunLight(this);
                _lightMapper.PropagateBlockLight(this);
                sw.Stop();
                Debug.Log($"Light Pass 3 (Combined): {sw.ElapsedMilliseconds}ms");
                _mesher.GenerateMeshData(this, new NeighborSet(_neighborBuffer), _neighborFlags);
                GlobalThreadPool.QueueOnMain(OnMeshPassComplete);
            });

            _state = ChunkState.Meshing;
        }

        public void OnMeshPassComplete() {
            _waitingForJob = false;
            _mesher.SetMeshData(ref _mesh);
            _meshRenderer.enabled = true;
            _state = ChunkState.Ready;
        }

        private void Update() {
            switch (_state) {
                case ChunkState.Uninitialized: {
                    break;
                }
                case ChunkState.TerrainGeneration: {
                    break;
                }
                case ChunkState.LightFirstPass: {
                    break;
                }
                case ChunkState.WaitingForNeighbors: {
                    _neighborFlags = NeighborFlags.None;
                    for (var i = 0; i < 4; i++) {
                        _neighborBuffer[i] = null;
                        var neighborId = _chunkId + _chunkNeighbors[i];

                        if (!WorldManager.Instance.ChunkInBounds(neighborId)) {
                            continue;
                        }

                        if (!WorldManager.Instance.IsChunkLoaded(neighborId)) {
                            return;
                        }
                        
                        var neighbor = WorldManager.Instance.Chunks[neighborId.x, neighborId.y];

                        if (neighbor.State < ChunkState.WaitingForNeighbors) {
                            return;
                        }

                        _neighborBuffer[i] = neighbor;
                        _neighborFlags |= (NeighborFlags)(1 << i);
                    }
                    
                    GlobalThreadPool.QueueWorkItem(() => {
                        var sw = new Stopwatch();
                        sw.Start();
                        _lightMapper.InitializeNeighborLightPass(this, new NeighborSet(_neighborBuffer), _neighborFlags);
                        _lightMapper.PropagateSunLight(this);
                        _lightMapper.PropagateBlockLight(this);
                        sw.Stop();
                        Debug.Log($"Light Pass 2 (Combined): {sw.ElapsedMilliseconds}ms");
                        
                        GlobalThreadPool.QueueOnMain(OnLightLastPassComplete);
                    });

                    _state = ChunkState.LightSecondPass;
                    
                    break;
                }
                case ChunkState.LightSecondPass: {
                    break;
                }
                case ChunkState.Meshing: {
                    break;
                }
                case ChunkState.Ready: {
                    if (Input.GetKeyDown(KeyCode.Space)) {
                        _meshRenderer.enabled = false;
                        OnGenerationComplete();
                    }
                    break;
                }
                default: {
                    throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}