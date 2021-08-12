using UnityEngine;

namespace VektorVoxels.Lighting {
    public readonly struct LightNode {
        public readonly Vector3Int Position;
        public readonly Color16 Value;

        public LightNode(Vector3Int position, Color16 value) {
            Position = position;
            Value = value;
        }
    }
}