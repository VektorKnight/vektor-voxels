using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Generation;
using VektorVoxels.Threading;
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

        [Header("Chunk Config")] [SerializeField] private Vector2Int _chunkSize = new Vector2Int(16, 256);
        [SerializeField] private Vector2Int _maxChunks = new Vector2Int(64, 64);
        [SerializeField] private bool _useSmoothLighting = true;
        [SerializeField] private Chunk _chunkPrefab;

        [Header("World Config")]
        [SerializeField] private WorldType _worldType = WorldType.Flat;
        [SerializeField] [Range(0, 128)] private int _seaLevel = 32;
        [SerializeField] [Range(1, 32)] private int _viewDistance = 10;
        [SerializeField] private Transform _loadTransform;

        private ITerrainGenerator _generator;
        private Chunk[,] _chunks;
        private LoadRect _loadRect;
        
        // Events.
        public delegate void WorldEventHandler(WorldEvent e);
        public static event WorldEventHandler OnWorldEvent;
        
        public Vector2Int ChunkSize => _chunkSize;
        public Vector2Int MaxChunks => _maxChunks;
        public bool UseSmoothLighting => _useSmoothLighting;
        public WorldType WorldType => _worldType;
        public int SeaLevel => _seaLevel;

        public ITerrainGenerator Generator => _generator;
        public Chunk[,] Chunks => _chunks;
        public LoadRect LoadRect => _loadRect;
        
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
            return _chunks[id.x, id.y] != null;
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
        
        public Vector2Int WorldToChunkPos(in Vector3 pos) {
            return new Vector2Int(
                Mathf.FloorToInt(pos.x / _chunkSize.x),
                Mathf.FloorToInt(pos.z / _chunkSize.x)
            );
        }

        public bool TryGetChunk(Vector3 world, out Chunk chunk) {
            var id = ChunkIdFromPos(WorldToChunkPos(in world));

            chunk = _chunks[id.x, id.y];
            return IsChunkLoaded(id);
        }

        private void Awake() {
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
        }

        private void Update() {
            if (_loadTransform == null) {
                return;
            }

            var loadPosition = _loadTransform.position;
            var loadOrigin = new Vector2Int(
                Mathf.RoundToInt(loadPosition.x / _chunkSize.x),
                Mathf.RoundToInt(loadPosition.z / _chunkSize.x)
            );

            var loadPrev = _loadRect;
            _loadRect = new LoadRect(loadOrigin, _viewDistance);

            if (!_loadRect.Equals(loadPrev)) {
                OnWorldEvent?.Invoke(WorldEvent.LoadRegionChanged);
            }

            // TODO: Loads chunks nearest the player first.
            for (var z = loadOrigin.y - _viewDistance; z < loadOrigin.y + _viewDistance; z++) {
                for (var x = loadOrigin.x - _viewDistance; x < loadOrigin.x + _viewDistance; x++) {
                    var chunkId = ChunkIdFromPos(new Vector2Int(x, z));
                    var chunkPos = ChunkPosFromId(chunkId) * _chunkSize.x;

                    if (!IsChunkInBounds(chunkId) || IsChunkLoaded(chunkId)) continue;

                    var chunk = Instantiate(_chunkPrefab, new Vector3(chunkPos.x, 0, chunkPos.y), Quaternion.identity);
                    chunk.transform.SetParent(transform);
                    chunk.Initialize(chunkId, ChunkPosFromId(chunkId));
                    //chunk.name = $"Chunk[{chunkId.x},{chunkId.y}]";
                    _chunks[chunkId.x, chunkId.y] = chunk;
                }
            }
        }

        private void OnDrawGizmos() {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(new Vector3(_loadRect.Position.x, 0, _loadRect.Position.y) * _chunkSize.x, new Vector3(_viewDistance * 2, 1, _viewDistance * 2) * _chunkSize.x);
            
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_maxChunks.x, 16, _maxChunks.y) * _chunkSize.x);
        }
    }
}