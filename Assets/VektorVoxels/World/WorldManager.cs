using System;
using UnityEngine;
using VektorVoxels.Chunks;

namespace VektorVoxels.World {
    /// <summary>
    /// Mostly handles chunks and loading/unloading.
    /// </summary>
    public class WorldManager : MonoBehaviour {
        public static WorldManager Instance { get; private set; }

        [Header("Chunk Config")] 
        [SerializeField] private Vector3Int _chunkSize = new Vector3Int(16, 256, 16);
        [SerializeField] private Vector2Int _maxChunks = new Vector2Int(16, 16);
        [SerializeField] private bool _useSmoothLighting = true;
        [SerializeField] private Chunk _chunkPrefab;

        [Header("World Config")] 
        [SerializeField] private WorldType _worldType = WorldType.Flat;
        [SerializeField] [Range(0, 128)] private int _seaLevel = 32;
        
        public Vector3Int ChunkSize => _chunkSize;
        public Vector2Int MaxChunks => _maxChunks;
        public bool UseSmoothLighting => _useSmoothLighting;
        public WorldType WorldType => _worldType;
        public int SeaLevel => _seaLevel;

        private void Awake() {
            if (Instance != null) {
                Debug.LogWarning("Duplicate world manager instance detected! \n" +
                                 "Please ensure only one instance is present per-scene.");
                return;
            }

            Instance = this;

            for (var z = 0; z < _maxChunks.y; z++) {
                for (var x = 0; x < _maxChunks.x; x++) {
                    var chunkPos = new Vector3Int(x - _maxChunks.x / 2, 0, z - _maxChunks.y / 2);
                    chunkPos *= _chunkSize.x;
                    var chunk = Instantiate(_chunkPrefab, chunkPos, Quaternion.identity);
                    chunk.Initialize();
                }
            }
        }
    }
}