using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        
        // Event/update queues.
        private ConcurrentQueue<ChunkEvent> _eventQueue;
        private List<VoxelUpdate> _voxelUpdates;
        
        // Mesh data.
        private ChunkState _state;
        private bool _waitingForJob;
        private bool _partialLoad;
        private LightPass _lightPass;

        // Job callbacks.
        private long _jobSetCounter;
        private Action _generationCallback;
        private Action _lightCallback1, _lightCallback2, _lightCallback3;
        private Action _meshCallback;
        
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
        
        // Threading.
        public ReaderWriterLockSlim ThreadLock => _threadLock;
        public long JobCounter => Interlocked.Read(ref _jobSetCounter);

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
            
            // Job completion callbacks.
            _generationCallback = OnGenerationPassComplete;
            _lightCallback1 = () => OnLightPassComplete(LightPass.First);
            _lightCallback2 = () => OnLightPassComplete(LightPass.Second);
            _lightCallback3 = () => OnLightPassComplete(LightPass.Third);
            _meshCallback = OnMeshPassComplete;

            // Thread safety.
            _threadLock = new ReaderWriterLockSlim();
            _eventQueue = new ConcurrentQueue<ChunkEvent>();
            
            // Register with world events.
            WorldManager.OnWorldEvent += WorldEventHandler;
            
            // Queue generation pass.
            QueueGenerationPass();
        }
        
        /// <summary>
        /// Processes events from the world manager.
        /// </summary>
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
                    
                    if (_partialLoad && _state == ChunkState.Ready) {
                        _partialLoad = false;
                        _eventQueue.Enqueue(ChunkEvent.Reload);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e), e, null);
            }
            _threadLock.ExitReadLock();
        }
        
        /// <summary>
        /// Processes chunk events.
        /// </summary>
        private void ProcessChunkEvent(ChunkEvent e) {
            switch (e) {
                case ChunkEvent.Unload:
                    _state = ChunkState.Inactive;
                    _meshRenderer.enabled = false;
                    break;
                case ChunkEvent.Reload:
                    OnGenerationPassComplete();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e), e, null);
            }
        }
        
        /// <summary>
        /// Queues the terrain generation pass on this chunk.
        /// </summary>
        private void QueueGenerationPass() {
            Debug.Assert(!_waitingForJob, "Generation pass queued while another job was running.");
            _waitingForJob = true;
            
            // First job in the chain so increment the counter.
            Interlocked.Increment(ref _jobSetCounter);
            
            GlobalThreadPool.QueueWorkItem(new GenerationJob(_jobSetCounter, this, _generationCallback));
            _state = ChunkState.TerrainGeneration;
        }
        
        /// <summary>
        /// Queues a light pass on this chunk.
        /// </summary>
        private void QueueLightPass(LightPass pass) {
            switch (pass) {
                case LightPass.None: {
                    break;
                }
                case LightPass.First: {
                    GlobalThreadPool.QueueWorkItem(
                        new LightJob(
                            _jobSetCounter, 
                            this, 
                            new NeighborSet(_neighborBuffer, _neighborFlags),
                            _lightMapper,
                            LightPass.First, _lightCallback1
                        )
                    );
                    break;
                }
                case LightPass.Second: {
                    GlobalThreadPool.QueueWorkItem(
                        new LightJob(
                            _jobSetCounter, 
                            this, 
                            new NeighborSet(_neighborBuffer, _neighborFlags),
                            _lightMapper,
                            LightPass.Second, _lightCallback2
                        )
                    );
                    break;
                }
                case LightPass.Third: {
                    GlobalThreadPool.QueueWorkItem(
                        new LightJob(
                            _jobSetCounter, 
                            this, 
                            new NeighborSet(_neighborBuffer, _neighborFlags),
                            _lightMapper,
                            LightPass.Third, _lightCallback3
                        )
                    );
                    break;
                }
                default: {
                    throw new ArgumentOutOfRangeException(nameof(pass), pass, null);
                }
            }
            
            _state = ChunkState.Lighting;
        }
        
        private void QueueMeshPass() {
            GlobalThreadPool.QueueWorkItem(
                new MeshJob(
                    _jobSetCounter, 
                    this, 
                    new NeighborSet(_neighborBuffer, _neighborFlags), 
                    _mesher, 
                    _meshCallback
                )
            );
            _state = ChunkState.Meshing;
        }
        
        private void OnGenerationPassComplete() {
            _lightPass = LightPass.None;
            QueueLightPass(LightPass.First);
        }

        private void OnLightPassComplete(LightPass pass) {
            Debug.Assert(pass > _lightPass);
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
                    ProcessChunkEvent(e);
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