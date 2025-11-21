using UnityEngine;

namespace VektorVoxels.Lighting {
    public readonly struct LightNode {
        public readonly Vector3Int Position;
        public readonly LightColor Value;

        public LightNode(Vector3Int position, LightColor value) {
            Position = position;
            Value = value;
        }
    }
}