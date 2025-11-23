using UnityEngine;

namespace VektorVoxels.Lighting {
    public readonly struct LightNode {
        public readonly Vector3Int Position;
        public readonly VoxelColor Value;

        public LightNode(Vector3Int position, VoxelColor value) {
            Position = position;
            Value = value;
        }
    }
}