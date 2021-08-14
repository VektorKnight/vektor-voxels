using System;
using System.Diagnostics.Contracts;
using UnityEngine;

namespace VektorVoxels.World {
    public readonly struct LoadRect : IEquatable<LoadRect> {
        public readonly Vector2Int Position;
        public readonly Vector2Int Min, Max;

        public LoadRect(Vector2Int position, int viewDistance) {
            Debug.Assert(viewDistance > 0, "View distance must be greater than zero.");
            
            Position = position;
            Min = new Vector2Int(position.x - viewDistance, position.y - viewDistance);
            Max = new Vector2Int(position.x + viewDistance, position.y + viewDistance);
        }

        /// <summary>
        /// Checks if a chunk position is within this load rect.
        /// </summary>
        [Pure]
        public bool ContainsChunk(Vector2Int pos) {
            return pos.x >= Min.x && pos.x < Max.x &&
                   pos.y >= Min.y && pos.y < Max.y;
        }
        
        [Pure]
        public bool Equals(LoadRect other) {
            return Position.Equals(other.Position) && Min.Equals(other.Min) && Max.Equals(other.Max);
        }
        
        [Pure]
        public override bool Equals(object obj) {
            return obj is LoadRect other && Equals(other);
        }
        
        [Pure]
        public override int GetHashCode() {
            unchecked {
                var hashCode = Position.GetHashCode();
                hashCode = (hashCode * 397) ^ Min.GetHashCode();
                hashCode = (hashCode * 397) ^ Max.GetHashCode();
                return hashCode;
            }
        }
    }
}