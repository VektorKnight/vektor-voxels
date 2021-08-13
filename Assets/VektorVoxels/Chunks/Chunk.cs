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
using Random = UnityEngine.Random;

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
        private LightPass _lightPass;
        
        // Thread safety.
        private ReaderWriterLockSlim _threadLock;
        
        private static readonly Vector2Int[] _chunkNeighbors = {
            Vector2Int.up, 
            Vector2Int.right, 
            Vector2Int.down, 
            Vector2Int.left,
        };
        
        // Useful accessors.
        public Vector2Int ChunkId => _chunkId;
        public VoxelData[] VoxelData => _voxelData;
        public HeightData[] HeightMap => _heightMap;
        public Color16[] BlockLight => _blockLight;
        public Color16[] SunLight => _sunLight;
        public ChunkState State => _state;
        public LightPass LightPass => _lightPass;
        public ReaderWriterLockSlim ThreadLock => _threadLock;

        /// <summary>
        /// Initializes this chunk and gets it ready for use.
        /// Should only be called by the WorldManager instance.
        /// </summary>
        /// <param name="id"></param>
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
            
            // Thread safety.
            _threadLock = new ReaderWriterLockSlim();
            
            // Queue generation pass.
            QueueGenerationPass();
        }

        private void QueueGenerationPass() {
            _waitingForJob = true;
            GlobalThreadPool.QueueWorkItem(() => {
                // TODO: Actual generation code or class.
                _threadLock.EnterWriteLock();
                var dimensions = WorldManager.Instance.ChunkSize;
                for (var z = 0; z < dimensions.z; z++) {
                    for (var x = 0; x < dimensions.x; x++) {
                        _voxelData[VoxelUtility.VoxelIndex(x, 0, z, in dimensions)] = VoxelTable.ById(1).GetDataInstance();
                        _heightMap[VoxelUtility.HeightIndex(x, z, dimensions.x)] = new HeightData(0, true);
                    }
                }

                if (true) {
                    _voxelData[VoxelUtility.VoxelIndex(4, 1, 6, in dimensions)] = VoxelTable.ById((uint)(11 + (_chunkId.x % 2))).GetDataInstance();
                    _heightMap[VoxelUtility.HeightIndex(4, 6, dimensions.x)] = new HeightData(1, true);
                }
                _threadLock.ExitWriteLock();
                
                GlobalThreadPool.QueueOnMain(OnGenerationComplete);
            });
            _state = ChunkState.TerrainGeneration;
        }

        private void QueueLightPass(LightPass pass) {
            switch (pass) {
                case LightPass.None: {
                    break;
                }
                case LightPass.First: {
                    GlobalThreadPool.QueueWorkItem(() => {
                        _threadLock.EnterWriteLock();
                        var sw = new Stopwatch();
                        sw.Start();
                        _lightMapper.InitializeSunLightFirstPass(this);
                        _lightMapper.PropagateSunLight(this);
                        _lightMapper.InitializeBlockLightFirstPass(this);
                        _lightMapper.PropagateBlockLight(this);
                        sw.Stop();
                        Debug.Log($"Light Pass 1 (Combined): {sw.ElapsedMilliseconds}ms");
                        _threadLock.ExitWriteLock();
                        
                        GlobalThreadPool.QueueOnMain(() => OnLightPassComplete(LightPass.First));
                    });
                    break;
                }
                case LightPass.Second: {
                    GlobalThreadPool.QueueWorkItem(() => {
                        _threadLock.EnterWriteLock();
                        var sw = new Stopwatch();
                        sw.Start();
                        _lightMapper.InitializeNeighborLightPass(this, new NeighborSet(_neighborBuffer), _neighborFlags);
                        _lightMapper.PropagateSunLight(this);
                        _lightMapper.PropagateBlockLight(this);
                        sw.Stop();
                        Debug.Log($"Light Pass 2 (Combined): {sw.ElapsedMilliseconds}ms");
                        _threadLock.ExitWriteLock();
                        
                        GlobalThreadPool.QueueOnMain(() => OnLightPassComplete(LightPass.Second));
                    });
                    break;
                }
                case LightPass.Third: {
                    GlobalThreadPool.QueueWorkItem(() => {
                        _threadLock.EnterWriteLock();
                        var sw = new Stopwatch();
                        sw.Start();
                        _lightMapper.InitializeNeighborLightPass(this, new NeighborSet(_neighborBuffer), _neighborFlags);
                        _lightMapper.PropagateSunLight(this);
                        _lightMapper.PropagateBlockLight(this);
                        sw.Stop();
                        Debug.Log($"Light Pass 3 (Combined): {sw.ElapsedMilliseconds}ms");
                        _threadLock.ExitWriteLock();
                        
                        GlobalThreadPool.QueueOnMain(() => OnLightPassComplete(LightPass.Third));
                    });
                    break;
                }
                default: {
                    throw new ArgumentOutOfRangeException(nameof(pass), pass, null);
                }
            }
            
            _waitingForJob = true;
            _state = ChunkState.Lighting;
        }
        
        private void QueueMeshPass() {
            _waitingForJob = true;
            GlobalThreadPool.QueueWorkItem(() => {
                _threadLock.EnterReadLock();
                var sw = new Stopwatch();
                sw.Start();
                _mesher.GenerateMeshData(this, new NeighborSet(_neighborBuffer), _neighborFlags);
                sw.Stop();
                Debug.Log($"Mesh Pass: {sw.ElapsedMilliseconds}ms");
                _threadLock.ExitReadLock();
                
                GlobalThreadPool.QueueOnMain(OnMeshPassComplete);
            });

            _state = ChunkState.Meshing;
        }
        
        private void OnGenerationComplete() {
            // Queue first light first pass on the threadpool.
            _waitingForJob = false;
            _lightPass = LightPass.None;
            QueueLightPass(LightPass.First);
        }

        private void OnLightPassComplete(LightPass pass) {
            Debug.Assert(pass > _lightPass);
            
            _waitingForJob = false;
            _lightPass = pass;

            _state = ChunkState.WaitingForNeighbors;
        }

        private void OnMeshPassComplete() {
            _waitingForJob = false;
            _mesher.SetMeshData(ref _mesh);
            _meshRenderer.enabled = true;
            _state = ChunkState.Ready;
        }

        private void CheckForNeighborState() {
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

                if (neighbor.State < ChunkState.WaitingForNeighbors || neighbor.LightPass < _lightPass) {
                    return;
                }

                _neighborBuffer[i] = neighbor;
                _neighborFlags |= (NeighborFlags)(1 << i);
            }
            
            // Queue lighting or mesh passes based on state.
            switch (_lightPass) {
                case LightPass.None:
                    break;
                case LightPass.First:
                    QueueLightPass(LightPass.Second);
                    break;
                case LightPass.Second:
                    QueueLightPass(LightPass.Third);
                    break;
                case LightPass.Third:
                    QueueMeshPass();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void OnLightSecondPassComplete() {
            _waitingForJob = true;
            

            _state = ChunkState.Meshing;
        }

        private void Update() {
            switch (_state) {
                case ChunkState.Uninitialized: {
                    break;
                }
                case ChunkState.TerrainGeneration: {
                    break;
                }
                case ChunkState.WaitingForNeighbors: {
                    CheckForNeighborState();
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
                case ChunkState.Lighting:
                    break;
                default: {
                    throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}