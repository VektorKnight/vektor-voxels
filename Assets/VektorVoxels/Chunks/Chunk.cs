using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using VektorVoxels.Config;
using VektorVoxels.Generation;
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
        private Mesh _mesh;
        private Mesh.MeshDataArray _latestMesh;
        private bool _latestMeshUsed = true;
        private Chunk[] _neighborBuffer;
        private NeighborFlags _neighborFlags;
        
        // World data.
        private Vector2Int _chunkId;
        private Vector2Int _chunkPos;
        private Vector2Int _worldPos;
        private VoxelData[] _voxelData;
        private Color16[] _sunLight;
        private Color16[] _blockLight;
        private HeightData[] _heightMap;
        
        // Event/update queues.
        private ConcurrentQueue<ChunkEvent> _eventQueue;
        private List<VoxelUpdate> _voxelUpdates;
        private List<HeightUpdate> _heightUpdates;
        
        // Mesh data.
        private ChunkState _state = ChunkState.Uninitialized;
        private bool _waitingForJob;
        private bool _partialLoad;
        private bool _waitingForReload, _waitingForUnload;
        private bool _isDirty;
        
        private LightPass _lightPass;

        // Job callbacks.
        private long _jobSetCounter;
        private Action _generationCallback;
        private Action _lightCallback1, _lightCallback2, _lightCallback3;
        private Action _meshCallback;
        
        // Thread safety.
        private ReaderWriterLockSlim _threadLock;
        
        private static readonly Vector2Int[] _chunkNeighbors = {
            new Vector2Int(0, 1),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0),
            
            new Vector2Int(1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(-1, -1),
            new Vector2Int(-1, 1)
        };
        
        // Useful accessors.
        public Vector2Int ChunkId => _chunkId;
        public Vector2Int ChunkPos => _chunkPos;
        public Vector2Int WorldPosition => _worldPos;
        public VoxelData[] VoxelData => _voxelData;
        public HeightData[] HeightMap => _heightMap;
        public Color16[] BlockLight => _blockLight;
        public Color16[] SunLight => _sunLight;
        public ChunkState State => _state;
        public LightPass LightPass => _lightPass;
        
        // Threading.
        public ReaderWriterLockSlim ThreadLock => _threadLock;
        public long JobCounter => Interlocked.Read(ref _jobSetCounter);

        public void SetIdAndPosition(Vector2Int id, Vector2Int pos) {
            if (_state != ChunkState.Uninitialized) {
                Debug.LogError("Cannot set chunk ID after initialization.");
                return;
            }
            
            _chunkId = id;
            _chunkPos = pos;
            _worldPos = pos * WorldManager.CHUNK_SIZE.x;
        }

        /// <summary>
        /// Initializes this chunk and gets it ready for use.
        /// Should only be called by the WorldManager instance.
        /// </summary>
        public void Initialize() {
            if (_state != ChunkState.Uninitialized) {
                Debug.LogError("Chunk initializer called on an already initialized chunk.");
                return;
            }

            // Reference required components.
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _meshCollider = GetComponent<MeshCollider>();

            _meshRenderer.sharedMaterials = new[] {
                _opaqueMaterial,
                _alphaMaterial
            };
            
            _meshRenderer.forceRenderingOff = true;
            _meshCollider.convex = false;
            
            _mesh = new Mesh() {
                name = $"ChunkMesh-{GetInstanceID()}",
                indexFormat = IndexFormat.UInt32
            };
            
            _neighborBuffer = new Chunk[8];
            _meshFilter.mesh = _mesh;
            _meshCollider.sharedMesh = _mesh;
            
            // World data.
            var dimensions = WorldManager.CHUNK_SIZE;
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
            _voxelUpdates = new List<VoxelUpdate>();
            _heightUpdates = new List<HeightUpdate>();
            
            // Register with world events.
            WorldManager.OnWorldEvent += WorldEventHandler;
            
            // Queue generation pass.
            QueueGenerationPass();
        }
        
        /// <summary>
        /// Transforms a world coordinate to local voxel grid space.
        /// The coordinate might lie outside of this chunk's local grid.
        /// Be sure to verify the coordinate with VoxelUtility.InLocalGrid() before using it.
        /// </summary>
        public Vector3Int WorldToLocal(Vector3 world) {
            return new Vector3Int(
                Mathf.FloorToInt(world.x - _worldPos.x),
                Mathf.FloorToInt(world.y),
                Mathf.FloorToInt(world.z - _worldPos.y)
            );
        }
        
        /// <summary>
        /// Transforms a 2D height-map coordinate (X,Z) to local height-map space.
        /// The coordinate might lie outside of this chunk's local rect.
        /// Be sure to verify the coordinate with VoxelUtility.InLocalRect() before using it.
        /// </summary>
        public Vector2Int WorldToLocal(Vector2 world) {
            return new Vector2Int(
                Mathf.FloorToInt(world.x - _worldPos.x),
                Mathf.FloorToInt(world.y - _worldPos.y)
            );
        }
        
        /// <summary>
        /// Transforms a local voxel grid coordinate to world space.
        /// </summary>
        public Vector3 LocalToWorld(Vector3Int local) {
            return new Vector3(
                local.x + _worldPos.x,
                local.y,
                local.z + _worldPos.y
            );
        }
        
        /// <summary>
        /// Transforms a local height-map coordinate to 2D world space (X,Z).
        /// </summary>
        public Vector2 LocalToWorld(Vector2Int local) {
            return new Vector2(
                local.x + _worldPos.x,
                local.y + _worldPos.y
            );
        }
        
        /// <summary>
        /// Queues a voxel update on this chunk at the specified position.
        /// If the position is not within this chunk's local grid, an exception will be thrown.
        /// </summary>
        public void QueueVoxelUpdate(VoxelUpdate update) {
            _voxelUpdates.Add(update);
        }
        
        /// <summary>
        /// Queues a height-map update on this chunk at the specified position.
        /// </summary>
        private void QueueHeightUpdate(HeightUpdate update) {
            _heightUpdates.Add(update);
        }
        
        /// <summary>
        /// Processes events from the world manager.
        /// </summary>
        private void WorldEventHandler(WorldEvent e) {
            switch (e) {
                case WorldEvent.LoadRegionChanged:
                    var inView = WorldManager.Instance.IsChunkInView(_chunkId);

                    if (!inView) {
                        _waitingForUnload = true;
                        _waitingForReload = false;
                        return;
                    }
                    else if (_state == ChunkState.Inactive) {
                        _waitingForUnload = false;
                        _waitingForReload = true;
                        return;
                    }
                    
                    if (_partialLoad && _state == ChunkState.Ready) {
                        _waitingForUnload = false;
                        _waitingForReload = true;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e), e, null);
            }
        }
        
        /// <summary>
        /// Processes chunk events.
        /// </summary>
        private void ProcessChunkEvent(ChunkEvent e) {
            switch (e) {
                case ChunkEvent.Unload:
                    Unload();
                    break;
                case ChunkEvent.Reload:
                    Reload();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e), e, null);
            }
        }
        
        /// <summary>
        /// Queues the terrain generation pass on this chunk.
        /// </summary>
        private void QueueGenerationPass() {
            _waitingForJob = true;
            GlobalThreadPool.DispatchJob(new GenerationJob(_jobSetCounter, this, _generationCallback));
            _state = ChunkState.TerrainGeneration;
        }
        
        /// <summary>
        /// Reloads this chunk.
        /// Executes lighting passes 1-3 then a mesh pass.
        /// Just re-enables the mesh renderer if not dirty or in a partial load state.
        /// </summary>
        private void Reload(bool force = false) {
            Debug.Assert(!_waitingForJob, "Reload started while another job was running.");

            _waitingForReload = false;

            if (force || _isDirty || _partialLoad) {
                // First job in the chain so increment the counter.
                Interlocked.Increment(ref _jobSetCounter);
                _waitingForJob = true;
                _lightPass = LightPass.None;
                QueueLightPass(LightPass.First);
            }
            else {
                _meshRenderer.forceRenderingOff = false;
                _state = ChunkState.Ready;
            }
        }
        
        /// <summary>
        /// Unloads this chunk from rendering.
        /// </summary>
        private void Unload() {
            _waitingForUnload = false;
            _state = ChunkState.Inactive;
            //_meshRenderer.enabled = false;
            _meshRenderer.forceRenderingOff = true;
        }
        
        /// <summary>
        /// Queues a light pass on this chunk.
        /// </summary>
        private void QueueLightPass(LightPass pass) {
            switch (pass) {
                case LightPass.First: {
                    GlobalThreadPool.DispatchJob(
                        new LightJob(
                            _jobSetCounter, 
                            this, 
                            new NeighborSet(_neighborBuffer, _neighborFlags),
                            LightPass.First, _lightCallback1
                        )
                    );
                    break;
                }
                case LightPass.Second: {
                    GlobalThreadPool.DispatchJob(
                        new LightJob(
                            _jobSetCounter, 
                            this, 
                            new NeighborSet(_neighborBuffer, _neighborFlags),
                            LightPass.Second, _lightCallback2
                        )
                    );
                    break;
                }
                case LightPass.Third: {
                    GlobalThreadPool.DispatchJob(
                        new LightJob(
                            _jobSetCounter, 
                            this, 
                            new NeighborSet(_neighborBuffer, _neighborFlags),
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
            //_latestMesh.Dispose();
            if (!_latestMeshUsed) {
                Debug.LogError("Overlapping jobs!");
                _latestMesh.Dispose();
                Debug.Break();
            }
            _latestMesh = Mesh.AllocateWritableMeshData(1);
            _latestMeshUsed = false;
            GlobalThreadPool.DispatchJob(
                new MeshJob(
                    _jobSetCounter, 
                    this, 
                    new NeighborSet(_neighborBuffer, _neighborFlags),
                    _latestMesh,
                    _meshCallback
                )
            );
            _state = ChunkState.Meshing;
        }
        
        /// <summary>
        /// Called when the generation pass has completed..
        /// </summary>
        private void OnGenerationPassComplete() {
            _isDirty = true;
            _lightPass = LightPass.None;
            QueueLightPass(LightPass.First);
        }
        
        /// <summary>
        /// Called when a lighting pass has completed.
        /// </summary>
        private void OnLightPassComplete(LightPass pass) {
            Debug.Assert(pass > _lightPass);
            _lightPass = pass;
            _state = ChunkState.WaitingForNeighbors;
        }

        public void SetLatestMeshData(Mesh.MeshDataArray data) {
            //_latestMesh.Dispose();
            _latestMesh = data;
        }
        
        /// <summary>
        /// Called when a mesh pass has completed.
        /// </summary>
        private void OnMeshPassComplete() {
            _waitingForJob = false;
            Mesh.ApplyAndDisposeWritableMeshData(_latestMesh, _mesh);
            _latestMeshUsed = true;
            _mesh.RecalculateBounds();
            _meshRenderer.forceRenderingOff = false;

            if (_isDirty) {
                _meshCollider.sharedMesh = _mesh;
                _isDirty = false;
            }

            // Clear flags.
            _state = ChunkState.Ready;
        }
        
        /// <summary>
        /// Checks neighbors and adds them to the buffer if active.
        /// </summary>
        private void CheckForNeighborState() {
            _neighborFlags = NeighborFlags.None;
            _partialLoad = false;
            for (var i = 0; i < 8; i++) {
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
        
        /// <summary>
        /// 
        /// </summary>
        private void UpdateAvailableNeighbors() {
            _neighborFlags = NeighborFlags.None;
            for (var i = 0; i < 8; i++) {
                _neighborBuffer[i] = null;
                var neighborId = _chunkId + _chunkNeighbors[i];

                if (!WorldManager.Instance.IsChunkInView(neighborId)) {
                    continue;
                }

                if (!WorldManager.Instance.IsChunkInBounds(neighborId)) {
                    continue;
                }

                if (!WorldManager.Instance.IsChunkLoaded(neighborId)) {
                    return;
                }

                var neighbor = WorldManager.Instance.Chunks[neighborId.x, neighborId.y];
                neighbor._waitingForReload = true;
                neighbor._isDirty = true;
            }
        }
        
        /// <summary>
        /// Updates a height-map column by iterating from the top of the chunk and stopping at the first voxel hit.
        /// </summary>
        private void UpdateHeightMapColumn(Vector2Int pos) {
            var d = WorldManager.CHUNK_SIZE;
            var hi = VoxelUtility.HeightIndex(pos, d.x);

            for (var y = d.y - 1; y >= 0; y--) {
                var vi = VoxelUtility.VoxelIndex(pos.x, y, pos.y, d);
                var voxel = _voxelData[vi];

                if (voxel.IsNull) continue;
                
                if (y == 0) {
                    _heightMap[hi] = new HeightData(0, true);
                }
                else {
                    _heightMap[hi] = new HeightData((byte)y, true);
                    return;
                }
            }
        }
        
        /// <summary>
        /// Update a 3x3 section of heightmap data.
        /// This is required for any voxel update so that sunlight can propagate correctly.
        /// </summary>
        private void UpdateHeightMapRegion(Vector2Int center) {
            var d = WorldManager.CHUNK_SIZE.x;
            for (var y = -1; y < 1; y++) {
                for (var x = -1; x < 1; x++) {
                    if (!VoxelUtility.InLocalRect(center.x + x, center.y + y, d)) {
                        continue;
                    }

                    UpdateHeightMapColumn(new Vector2Int(center.x + x,center.y + y));
                }
            }
        }
        
        /// <summary>
        /// Queues height updates on all available neighbors.
        /// If the update lies outside the neighbor's local grid, it will just be ignored.
        /// TODO: This is kinda lazy, maybe check if we even need to poke the neighbor.
        /// </summary>
        private void UpdateNeighborHeightMaps(Vector2Int center) {
            for (var i = 0; i < 8; i++) {
                var neighborId = _chunkId + _chunkNeighbors[i];

                if (!WorldManager.Instance.IsChunkInView(neighborId)) {
                    continue;
                }

                if (!WorldManager.Instance.IsChunkInBounds(neighborId)) {
                    continue;
                }

                if (!WorldManager.Instance.IsChunkLoaded(neighborId)) {
                    continue;
                }

                var neighbor = WorldManager.Instance.Chunks[neighborId.x, neighborId.y];
                var neighborPos = neighbor.WorldToLocal(LocalToWorld(center));
                neighbor.QueueHeightUpdate(new HeightUpdate(neighborPos, true));
            }
        }
        
        /// <summary>
        /// Designed to be called from the World Manager.
        /// Same purpose as Unity Update() but with finer control over timing.
        /// </summary>
        public void OnTick() {
            if (_state == ChunkState.WaitingForNeighbors) {
                CheckForNeighborState();
                return;
            }
            
            // Process chunk event queue once the chunk is ready/inactive and no threads have an active lock.
            if (!_waitingForJob && _state == ChunkState.Ready || _state == ChunkState.Inactive) {
                if (_threadLock.IsReadLockHeld || _threadLock.IsWriteLockHeld) return;
                
                // Process event queue.
                //_threadLock.EnterWriteLock();
                while (_eventQueue.TryDequeue(out var e)) {
                    ProcessChunkEvent(e);
                }
                
                // Process voxel and height updates.
                if (_voxelUpdates.Count > 0 || _heightUpdates.Count > 0) {
                    var d = WorldManager.CHUNK_SIZE;
                    
                    // Process voxel updates and queue height updates.
                    foreach (var update in _voxelUpdates) {
                        if (!VoxelUtility.InLocalGrid(update.Position, WorldManager.CHUNK_SIZE)) {
                            Debug.Log(update.Position);
                            Debug.Log(_chunkPos);
                            continue;
                        }

                        _voxelData[VoxelUtility.VoxelIndex(update.Position, d)] = update.Data;
                        _heightUpdates.Add(new HeightUpdate(new Vector2Int(update.Position.x, update.Position.z), false));
                    }
                    _voxelUpdates.Clear();
                    
                    // Process height updates.
                    foreach (var update in _heightUpdates) {
                        UpdateHeightMapRegion(update.Position);

                        if (!update.FromNeighbor) {
                            UpdateNeighborHeightMaps(update.Position);
                        }
                    }
                    _heightUpdates.Clear();
                    
                    // Flag chunk as dirty and inform neighbors to update.
                    _isDirty = true;
                    UpdateAvailableNeighbors();
                }
                //_threadLock.ExitWriteLock();
            }
        }

        public void OnLateTick() {
            if ((_waitingForJob || _state != ChunkState.Ready) && _state != ChunkState.Inactive) return;
            if (_threadLock.IsReadLockHeld || _threadLock.IsWriteLockHeld) return;
            
            if (Input.GetKeyDown(KeyCode.F5)) {
                Reload(true);
            }

            // Handle flags.
            if (_waitingForReload || _isDirty) {
                Reload();
            }

            if (_waitingForUnload) {
                Unload();
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