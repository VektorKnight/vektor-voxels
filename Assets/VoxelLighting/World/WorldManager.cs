using UnityEngine;

namespace VoxelLighting.World {
    /// <summary>
    /// Mostly handles chunks and loading/unloading.
    /// </summary>
    public class WorldManager : MonoBehaviour {
        public static WorldManager Instance { get; private set; }

        [Header("Chunk Config")] 
        [SerializeField] private Vector3Int _chunkSize = new Vector3Int(16, 256, 16);
        [SerializeField] private Vector2Int _maxChunks = new Vector2Int(64, 64);
        [SerializeField] private bool _useSmoothLighting = true;

        [Header("World Config")] 
        [SerializeField] private WorldType _worldType = WorldType.Flat;
        [SerializeField] [Range(0, 128)] private int _seaLevel = 32;
        
        public Vector3Int ChunkSize => _chunkSize;
        public Vector2Int MaxChunks => _maxChunks;
        public bool UseSmoothLighting => _useSmoothLighting;
        public WorldType WorldType => _worldType;
        public int SeaLevel => _seaLevel;
    }
}