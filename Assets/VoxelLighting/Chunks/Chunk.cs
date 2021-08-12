using System;
using UnityEngine;
using VoxelLighting.Lighting;
using VoxelLighting.Meshing;
using VoxelLighting.Threading;
using VoxelLighting.Voxels;
using VoxelLighting.World;

namespace VoxelLighting.Chunks {
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
        
        // World data.
        private VoxelData[] _voxelData;
        private Color16[] _blockLight;
        private Color16[] _sunLight;
        private byte[] _heightMap;
        
        // Mesh data.
        private Mesh _mesh;
        private ChunkState _state;
        private bool _waitingForJob;
        
        // Useful accessors.
        public VoxelData[] VoxelData => _voxelData;
        public Color16[] BlockLight => _blockLight;
        public Color16[] SunLight => _sunLight;
        public ChunkState State => _state;

        public void Initialize() {
            
        }

        public void OnGenerationComplete() {
            // Queue light first pass on the threadpool.
            _waitingForJob = true;
            GlobalThreadPool.Instance.EnqueueWorkItem(() => {
                _lightMapper.InitializeSunLightFirstPass(in _voxelData, in _heightMap, in _sunLight, WorldManager.Instance.ChunkSize);
                _lightMapper.PropagateSunLight(in _voxelData, in _sunLight, WorldManager.Instance.ChunkSize);
                
                _lightMapper.InitializeBlockLightFirstPass(_voxelData, WorldManager.Instance.ChunkSize);
                _lightMapper.PropagateBlockLight(_voxelData, _blockLight, WorldManager.Instance.ChunkSize);
                
                GlobalThreadPool.Instance.QueueOnMain(OnLightFirstPassComplete);
            });
            
            _state = ChunkState.LightFirstPass;
        }

        public void OnLightFirstPassComplete() {
            // TODO: Eventually need to wait for neighbors before executing the mesh pass.
            _waitingForJob = true;
            GlobalThreadPool.Instance.EnqueueWorkItem(() => {
                _mesher.GenerateMeshData(_voxelData, _blockLight, _sunLight, WorldManager.Instance.ChunkSize, WorldManager.Instance.UseSmoothLighting);
                
                GlobalThreadPool.Instance.QueueOnMain(OnMeshPassComplete);
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
                    break;
                }
                default: {
                    throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}