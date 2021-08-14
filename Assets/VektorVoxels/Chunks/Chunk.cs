using System;
using System.Collections.Concurrent;
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
        private bool _partialLoad;
        private LightPass _lightPass;
        
        // Jobs.
        private GenerationJob _generationJob;
        private LightJob _lightJobPass1;
        private LightJob _lightJobPass2;
        private LightJob _lightJobPass3;
        private MeshJob _meshJob;
        
        // Thread safety.
        private ReaderWriterLockSlim _threadLock;
        private ConcurrentQueue<ChunkEvent> _eventQueue;

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
            var dataSize = dimensions.x * dimensions.y * dimensions.x;
            _voxelData = new VoxelData[dataSize];
            _sunLight = new Color16[dataSize];
            _blockLight = new Color16[dataSize];
            _heightMap = new HeightData[dimensions.x * dimensions.x];
            
            // Create job instances.
            _generationJob = new GenerationJob(this, OnGenerationComplete);
            _lightJobPass1 = new LightJob(this, default, LightPass.First, _lightMapper, () => {
                OnLightPassComplete(LightPass.First);
            });
            _lightJobPass2 = new LightJob(this, default, LightPass.Second, _lightMapper, () => {
                OnLightPassComplete(LightPass.Second);
            });
            _lightJobPass2 = new LightJob(this, default, LightPass.Third, _lightMapper, () => {
                OnLightPassComplete(LightPass.Third);
            });
            _meshJob = new MeshJob(this, default, _mesher, OnMeshPassComplete);
            
            // Thread safety.
            _threadLock = new ReaderWriterLockSlim();
            _eventQueue = new ConcurrentQueue<ChunkEvent>();
            
            // Register with world events.
            WorldManager.OnWorldEvent += WorldEventHandler;
            
            // Queue generation pass.
            QueueGenerationPass();
        }

        private void WorldEventHandler(WorldEvent e) {
            _threadLock.EnterReadLock();
            switch (e) {
                case WorldEvent.LoadRegionChanged:
                    var inView = WorldManager.Instance.IsChunkInView(_chunkId);

                    if (!inView) {
                        _eventQueue.Enqueue(ChunkEvent.Unload);
                        _threadLock.ExitReadLock();
                        return;
                    }
                    else if (_state == ChunkState.Inactive) {
                        _eventQueue.Enqueue(ChunkEvent.Reload);
                        _threadLock.ExitReadLock();
                        return;
                    }
                    
                    if (_partialLoad) {
                        _partialLoad = false;
                        _eventQueue.Enqueue(ChunkEvent.Reload);
                    }
                    break;
                case WorldEvent.ViewDistanceChanged:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e), e, null);
            }
            _threadLock.ExitReadLock();
        }

        private void ChunkEventHandler(ChunkEvent e) {
            switch (e) {
                case ChunkEvent.Unload:
                    _state = ChunkState.Inactive;
                    _meshRenderer.enabled = false;
                    break;
                case ChunkEvent.Reload:
                    OnGenerationComplete();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e), e, null);
            }
        }

        private void QueueGenerationPass() {
            _waitingForJob = true;
            GlobalThreadPool.QueueWorkItem(_generationJob);
            _state = ChunkState.TerrainGeneration;
        }

        private void QueueLightPass(LightPass pass) {
            switch (pass) {
                case LightPass.None: {
                    break;
                }
                case LightPass.First: {
                    GlobalThreadPool.QueueWorkItem(_lightJobPass1);
                    break;
                }
                case LightPass.Second: {
                    _lightJobPass2.Neighbors = new NeighborSet(_neighborBuffer, _neighborFlags);
                    GlobalThreadPool.QueueWorkItem(_lightJobPass2);
                    break;
                }
                case LightPass.Third: {
                    _lightJobPass3.Neighbors = new NeighborSet(_neighborBuffer, _neighborFlags);
                    GlobalThreadPool.QueueWorkItem(_lightJobPass3);
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
            _meshJob.Neighbors = new NeighborSet(_neighborBuffer, _neighborFlags);
            GlobalThreadPool.QueueWorkItem(_meshJob);

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
            _meshCollider.enabled = true;
            _meshCollider.sharedMesh = _mesh;
            _state = ChunkState.Ready;
        }

        private void CheckForNeighborState() {
            _neighborFlags = NeighborFlags.None;
            _partialLoad = false;
            for (var i = 0; i < 4; i++) {
                _neighborBuffer[i] = null;
                var neighborId = _chunkId + _chunkNeighbors[i];


                if (!WorldManager.Instance.IsChunkInView(neighborId)) {
                    _partialLoad = true;
                    continue;
                }
                
                if (!WorldManager.Instance.IsChunkInBounds(neighborId)) {
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
                    //if (Input.GetKeyDown(KeyCode.Space)) {
                        //_meshRenderer.enabled = false;
                        //OnGenerationComplete();
                    //}
                    break;
                }
                case ChunkState.Lighting:
                    break;
                case ChunkState.Inactive:
                    break;
                default: {
                    throw new ArgumentOutOfRangeException();
                }
            }

            if (!_waitingForJob && _state == ChunkState.Ready || _state == ChunkState.Inactive) {
                if (_threadLock.CurrentReadCount != 0) return;
                // Process event queue.
                _threadLock.EnterWriteLock();
                while (_eventQueue.TryDequeue(out var e)) {
                    ChunkEventHandler(e);
                }
                _threadLock.ExitWriteLock();
            }
        }

        private void OnDrawGizmos() {
            if (_state == ChunkState.Inactive) return;
            Gizmos.color = _state switch {
                ChunkState.Uninitialized => Color.red,
                ChunkState.TerrainGeneration => Color.yellow,
                ChunkState.Lighting => Color.magenta,
                ChunkState.WaitingForNeighbors => Color.green,
                ChunkState.Meshing => Color.cyan,
                ChunkState.Ready => Color.blue,
                ChunkState.Inactive => Color.clear,
                _ => throw new ArgumentOutOfRangeException()
            };
            
            Gizmos.DrawWireCube(transform.position + new Vector3(8, 0, 8), Vector3.one * 16);
        }
    }
}