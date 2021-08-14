using System;
using UnityEngine;
using VektorVoxels.Chunks;

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
        [SerializeField] private Vector2Int _maxChunks = new Vector2Int(16, 16);
        [SerializeField] private bool _useSmoothLighting = true;
        [SerializeField] private Chunk _chunkPrefab;

        [Header("World Config")] 
        [SerializeField] private WorldType _worldType = WorldType.Flat;
        [SerializeField] [Range(0, 128)] private int _seaLevel = 32;
        
        // Notice: This will only work if the world bounds are known.
        private Chunk[,] _chunks;
        
        public Vector2Int ChunkSize => _chunkSize;
        public Vector2Int MaxChunks => _maxChunks;
        public bool UseSmoothLighting => _useSmoothLighting;
        public WorldType WorldType => _worldType;
        public int SeaLevel => _seaLevel;

        public Chunk[,] Chunks => _chunks;

        public bool ChunkInBounds(Vector2Int id) {
            return id.x >= 0 && id.x < _maxChunks.x &&
                   id.y >= 0 && id.y < _maxChunks.y;
        }

        public bool IsChunkLoaded(Vector2Int id) {
            return _chunks[id.x, id.y] != null;
        }

        public Vector2Int ChunkPosFromId(Vector2Int id) {
            return new Vector2Int(id.x - (_maxChunks.x >> 2), id.y - (_maxChunks.y >> 2));
        }

        public Vector2Int ChunkIdFromPos(Vector2Int pos) {
            return new Vector2Int(pos.x + (_maxChunks.x >> 2), pos.y + (_maxChunks.y >> 2));
        }

        public Vector2Int WorldToChunkPos(in Vector3 pos) {
            return new Vector2Int(
                Mathf.FloorToInt(pos.x) / _chunkSize.x,
                Mathf.FloorToInt(pos.z) / _chunkSize.x
            );
        }

        private void Awake() {
            if (Instance != null) {
                Debug.LogWarning("Duplicate world manager instance detected! \n" +
                                 "Please ensure only one instance is present per-scene.");
                return;
            }

            Instance = this;

            _chunks = new Chunk[_maxChunks.x, _maxChunks.y];

            for (var z = 0; z < _maxChunks.y; z++) {
                for (var x = 0; x < _maxChunks.x; x++) {
                    var chunkPos = new Vector3Int(x - _maxChunks.x / 2, 0, z - _maxChunks.y / 2);
                    chunkPos *= _chunkSize.x;
                    
                    var chunk = Instantiate(_chunkPrefab, chunkPos, Quaternion.identity);
                    chunk.Initialize(new Vector2Int(x, z));
                    _chunks[x, z] = chunk;
                }
            }
        }
    }
}