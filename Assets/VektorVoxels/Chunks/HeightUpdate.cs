using UnityEngine;

namespace VektorVoxels.Chunks {
    public readonly struct HeightUpdate {
        public readonly Vector2Int Position;
        public readonly bool FromNeighbor;

        public HeightUpdate(Vector2Int position, bool fromNeighbor) {
            Position = position;
            FromNeighbor = fromNeighbor;
        }
    }
}