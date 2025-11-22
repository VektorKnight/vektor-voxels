using UnityEngine;

namespace VektorVoxels.World {
    /// <summary>
    /// Chunk dimensions with explicit naming to avoid Vector2Int confusion.
    /// Width applies to both X and Z (square chunks), Height is Y.
    /// </summary>
    public readonly struct ChunkDimensions {
        public readonly int Width;  // X and Z size
        public readonly int Height; // Y size

        public ChunkDimensions(int width, int height) {
            Width = width;
            Height = height;
        }

        public ChunkDimensions(Vector2Int size) {
            Width = size.x;
            Height = size.y;
        }

        /// <summary>
        /// Total voxel count in chunk.
        /// </summary>
        public int Volume => Width * Height * Width;

        /// <summary>
        /// Check if local position is within chunk bounds.
        /// </summary>
        public bool Contains(Vector3Int localPos) {
            return localPos.x >= 0 && localPos.x < Width &&
                   localPos.y >= 0 && localPos.y < Height &&
                   localPos.z >= 0 && localPos.z < Width;
        }

        /// <summary>
        /// Calculate flat array index for local position.
        /// </summary>
        public int GetIndex(Vector3Int localPos) {
            return localPos.x + (localPos.y * Width) + (localPos.z * Width * Height);
        }

        public static implicit operator ChunkDimensions(Vector2Int v) => new ChunkDimensions(v);
    }
}
