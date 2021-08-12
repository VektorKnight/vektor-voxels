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
        
        // World data.
        private VoxelData[] _voxelData;
        private Color16[] _sunLight;
        private Color16[] _blockLight;
        private HeightData[] _heightMap;
        
        // Mesh data.
        private ChunkState _state;
        private bool _waitingForJob;
        
        // Useful accessors.
        public VoxelData[] VoxelData => _voxelData;
        public Color16[] BlockLight => _blockLight;
        public Color16[] SunLight => _sunLight;
        public ChunkState State => _state;

        public void Initialize() {
            // Reference required components.
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _meshCollider = GetComponent<MeshCollider>();

            _meshRenderer.sharedMaterials = new[] {
                _opaqueMaterial,
                _alphaMaterial
            };
            
            _meshCollider.convex = false;
            
            
            // Mesher and lightmapper.
            _mesher = new CubicMesher();
            _lightMapper = new LightMapper();
            _mesh = new Mesh() {
                name = $"ChunkMesh-{GetInstanceID()}",
                indexFormat = IndexFormat.UInt32
            };

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
                        _heightMap[VoxelUtility.HeightIndex(x, z, dimensions.x)] = new HeightData(1, true);
                    }
                }

                GlobalThreadPool.QueueOnMain(OnGenerationComplete);
            });
        }

        public void OnGenerationComplete() {
            // Queue light first pass on the threadpool.
            _waitingForJob = true;
            GlobalThreadPool.QueueWorkItem(() => {
                var sw = new Stopwatch();
                sw.Start();
                _lightMapper.InitializeSunLightFirstPass(_voxelData, _heightMap, _sunLight, WorldManager.Instance.ChunkSize);
                _lightMapper.PropagateSunLight(_voxelData, _sunLight, WorldManager.Instance.ChunkSize);
                sw.Stop();
                Debug.Log($"Light Pass 1 (Sun): {sw.ElapsedMilliseconds}ms");

                sw.Reset();
                _lightMapper.InitializeBlockLightFirstPass(_voxelData, WorldManager.Instance.ChunkSize);
                _lightMapper.PropagateBlockLight(_voxelData, _blockLight, WorldManager.Instance.ChunkSize);
                sw.Stop();
                Debug.Log($"Light Pass 1 (Block): {sw.ElapsedMilliseconds}ms");
                
                GlobalThreadPool.QueueOnMain(OnLightFirstPassComplete);
            });
            
            _state = ChunkState.LightFirstPass;
        }

        public void OnLightFirstPassComplete() {
            // TODO: Eventually need to wait for neighbors before executing the mesh pass.
            _waitingForJob = true;
            GlobalThreadPool.QueueWorkItem(() => {
                _mesher.GenerateMeshData(_voxelData, _blockLight, _sunLight, WorldManager.Instance.ChunkSize, WorldManager.Instance.UseSmoothLighting);
                GlobalThreadPool.QueueOnMain(OnMeshPassComplete);
            });

            _state = ChunkState.Meshing;
        }

        public void OnLightLastPassComplete() {
            
        }

        public void OnMeshPassComplete() {
            _waitingForJob = false;
            _mesher.SetMeshData(ref _mesh);
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
                    break;
                }
                case ChunkState.LightFinalPass: {
                    break;
                }
                case ChunkState.Meshing: {
                    break;
                }
                case ChunkState.Ready: {
                    if (Input.GetKeyDown(KeyCode.Space)) {
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