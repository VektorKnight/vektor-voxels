using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Generation;
using VektorVoxels.Lighting;
using VektorVoxels.Meshing;
using VektorVoxels.Threading;
using VektorVoxels.Threading.Jobs;
using VektorVoxels.Voxels;
using Random = UnityEngine.Random;

namespace VektorVoxels.World {
    /// <summary>
    /// Mostly handles chunks and loading/unloading.
    /// Designed to handle a finite world where all chunks are resident in memory.
    /// This is just enough to satisfy the purposes of a demo.
    ///
    /// Technically any chunk size is "supported" but is largely untested.
    /// Anything larger than 4 in each dimension should work fine and it is recommended to keep X == Z.
    ///
    /// Due to Unity's 32-bit transform, you should never define a world larger than ~20km in X or Z.
    /// </summary>
    public class WorldManager : MonoBehaviour {
        public static WorldManager Instance { get; private set; }
        
        /// <summary>
        /// Width and height of chunks.
        /// Messing with this value may require additional code updates.
        /// </summary>
        public static readonly Vector2Int CHUNK_SIZE = new Vector2Int(16, 256);

        [Header("Chunk Config")] 
        [SerializeField] private Vector2Int _maxChunks = new Vector2Int(64, 64);
        [SerializeField] private bool _useSmoothLighting = true;
        [SerializeField] private Chunk _chunkPrefab;

        [Header("World Config")]
        [SerializeField] private WorldType _worldType = WorldType.Flat;
        [SerializeField] [Range(0, 128)] private int _seaLevel = 32;
        [SerializeField] [Range(1, 32)] private int _viewDistance = 10;
        [SerializeField] private Transform _loadTransform;

        [Header("Performance")] 
        [SerializeField] private int _chunksPerTick = 4;

        private ITerrainGenerator _generator;
        private Chunk[,] _chunks;
        private LoadRect _loadRect;
        private List<Chunk> _loadedChunks;
        private List<Chunk> _chunksToLoad;
        private Queue<Chunk> _loadQueue;

        // Events.
        public delegate void WorldEventHandler(WorldEvent e);
        public static event WorldEventHandler OnWorldEvent;
        
        public Vector2Int MaxChunks => _maxChunks;
        public bool UseSmoothLighting => _useSmoothLighting;
        public WorldType WorldType => _worldType;
        public int SeaLevel => _seaLevel;
        public int ViewDistance => _viewDistance;

        public ITerrainGenerator Generator => _generator;
        public Chunk[,] Chunks => _chunks;
        public LoadRect LoadRect => _loadRect;

        public int ChunksPerTick => _chunksPerTick;

        /// <summary>
        /// Checks if a chunk ID is within the bounds of the world.
        /// </summary>
        public bool IsChunkInBounds(Vector2Int id) {
            return id.x >= 0 && id.x < _maxChunks.x &&
                   id.y >= 0 && id.y < _maxChunks.y;
        }
        
        /// <summary>
        /// Checks if a chunk with the given ID is loaded.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public bool IsChunkLoaded(Vector2Int id) {
            var chunk = _chunks[id.x, id.y];
            return chunk != null;
        }
        
        /// <summary>
        /// Checks if a chunk ID is within the current view distance.
        /// </summary>
        public bool IsChunkInView(Vector2Int id) {
            var chunkPos = ChunkPosFromId(id);
            return _loadRect.ContainsChunk(chunkPos);
        }
        
        /// <summary>
        /// Gets a chunk position from its ID.
        /// </summary>
        public Vector2Int ChunkPosFromId(Vector2Int id) {
            return new Vector2Int(id.x - (_maxChunks.x >> 1), id.y - (_maxChunks.y >> 1));
        }
        
        /// <summary>
        /// Gets a chunk ID from its position.
        /// </summary>
        public Vector2Int ChunkIdFromPos(Vector2Int pos) {
            return new Vector2Int(pos.x + _maxChunks.x / 2, pos.y + _maxChunks.y / 2);
        }
        
        public Vector2Int WorldToChunkPos(Vector3 pos) {
            return new Vector2Int(
                Mathf.FloorToInt(pos.x / CHUNK_SIZE.x),
                Mathf.FloorToInt(pos.z / CHUNK_SIZE.x)
            );
        }

        public bool TryGetChunk(Vector3 world, out Chunk chunk) {
            var id = ChunkIdFromPos(WorldToChunkPos(world));

            chunk = _chunks[id.x, id.y];
            return IsChunkLoaded(id);
        }
        
        public bool TryQueueVoxelUpdate(Vector3 position, VoxelData data) {
            // Fail if chunk is not available.
            if (!TryGetChunk(position, out var chunk)) return false;
            
            chunk.QueueVoxelUpdate(new VoxelUpdate(
                chunk.WorldToVoxel(position),
                data
            ));
            
            return true;
        }

        private async void Awake() {
            if (Instance != null) {
                Debug.LogWarning("Duplicate world manager instance detected! \n" +
                                 "Please ensure only one instance is present per-scene.");
                return;
            }

            Instance = this;
            
            // Limit max framerate to 360 cause coil whine is annoying.
            Application.targetFrameRate = 360;

            _generator = PerlinGenerator.Default();
            _chunks = new Chunk[_maxChunks.x, _maxChunks.y];
            _loadRect = new LoadRect(Vector2Int.zero, _viewDistance);
            _loadedChunks = new List<Chunk>();
            _chunksToLoad = new List<Chunk>();
            _loadQueue = new Queue<Chunk>();
            
            // Configure thread pool throttled queue.
            GlobalThreadPool.ThrottledUpdatesPerTick = _chunksPerTick;

            var test = new ExampleJob();
            var result = await test.Dispatch();
            Debug.Log(result);
        }
        
        /// <summary>
        /// Used for sorting chunks.
        /// Might be called extremely often so component-wise math was used.
        /// Comparison is done on the Square Magnitude of the vectors since we don't need actual distance.
        /// Just a number to compare with.
        /// </summary>
        private int CompareChunks(Chunk a, Chunk b) {
            var origin = _loadRect.Position;
            var wA = a.WorldPosition;
            var wB = b.WorldPosition;

            var aX = origin.x - wA.x;
            var aZ = origin.y - wA.y;
            
            var bX = origin.x - wB.x;
            var bZ = origin.y - wB.y;

            var dA = aX * aX + aZ * aZ;
            var dB = bX * bX + bZ * bZ;

            if (dA > dB) {
                return 1;
            }

            if (dA == dB) {
                return 0;
            }

            return -1;
        }

        private void FixedUpdate() {
            if (_loadTransform == null) {
                return;
            }
            
            // Update load rect origin.
            var loadPosition = _loadTransform.position;
            var loadOrigin = new Vector2Int(
                Mathf.RoundToInt(loadPosition.x / CHUNK_SIZE.x),
                Mathf.RoundToInt(loadPosition.z / CHUNK_SIZE.x)
            );
            
            // Figure out if the rect has moved.
            // If so, invoke the world event.
            var loadPrev = _loadRect;
            _loadRect = new LoadRect(loadOrigin, _viewDistance);
            if (!_loadRect.Equals(loadPrev)) {
                OnWorldEvent?.Invoke(WorldEvent.LoadRegionChanged);
            }
            
            // Load any new chunks.
            _chunksToLoad.Clear();
            for (var z = loadOrigin.y - _viewDistance; z < loadOrigin.y + _viewDistance; z++) {
                for (var x = loadOrigin.x - _viewDistance; x < loadOrigin.x + _viewDistance; x++) {
                    var chunkId = ChunkIdFromPos(new Vector2Int(x, z));
                    var chunkPos = ChunkPosFromId(chunkId) * CHUNK_SIZE.x;

                    if (!IsChunkInBounds(chunkId) || IsChunkLoaded(chunkId)) continue;

                    var chunk = Instantiate(_chunkPrefab, new Vector3(chunkPos.x, 0, chunkPos.y), Quaternion.identity);
                    chunk.transform.SetParent(transform);
                    chunk.name = $"Chunk[{chunkId.x},{chunkId.y}]";
                    chunk.SetIdAndPosition(chunkId, ChunkPosFromId(chunkId));
                    
                    // Set global table reference.
                    _chunks[chunkId.x, chunkId.y] = chunk;
                    
                    // Add the chunk to the load list.
                    _chunksToLoad.Add(chunk);
                }
            }
            
            // Sort the list of chunks waiting to be loaded.
            _chunksToLoad.Sort(CompareChunks);
            foreach (var chunk in _chunksToLoad) {
                if (_loadQueue.Contains(chunk)) continue;
                _loadQueue.Enqueue(chunk);
            }
            
            // Initialize chunks waiting to be loaded.
            var count = _chunksPerTick;
            while (_loadQueue.Count > 0) {
                if (count <= 0) {
                    break;
                }
                
                var chunk = _loadQueue.Dequeue();
                chunk.Initialize();
                _loadedChunks.Add(chunk);
                count--;
            }
        }

        private void Update() {
            // Sort the loaded chunks then tick them in order.
            _loadedChunks.Sort(CompareChunks);
            foreach (var chunk in _loadedChunks) {
                chunk.OnTick();
            }
        }

        private void LateUpdate() {
            foreach (var chunk in _loadedChunks) {
                chunk.OnLateTick();
            }
        }

        private void OnDrawGizmos() {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(new Vector3(_loadRect.Position.x, 0, _loadRect.Position.y) * CHUNK_SIZE.x, new Vector3(_viewDistance * 2, 1, _viewDistance * 2) * CHUNK_SIZE.x);
            
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_maxChunks.x, 16, _maxChunks.y) * CHUNK_SIZE.x);
        }
    }
}