using UnityEngine;

namespace VektorVoxels.VoxelPhysics {
    /// <summary>
    /// Result of a swept collision test against the voxel grid.
    /// </summary>
    public struct CollisionResult {
        /// <summary>
        /// Final position after collision resolution.
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// Adjusted velocity after collision response.
        /// </summary>
        public Vector3 Velocity;

        /// <summary>
        /// True if any collision occurred.
        /// </summary>
        public bool Hit;

        /// <summary>
        /// True if grounded (collision below with upward-facing normal).
        /// </summary>
        public bool Grounded;

        /// <summary>
        /// True if hit ceiling (collision above).
        /// </summary>
        public bool HitCeiling;

        /// <summary>
        /// True if hit wall (horizontal collision).
        /// </summary>
        public bool HitWall;

        /// <summary>
        /// Ground surface normal (only valid if Grounded).
        /// </summary>
        public Vector3 GroundNormal;
    }
}
