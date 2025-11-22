using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.World;

namespace VektorVoxels.VoxelPhysics {
    /// <summary>
    /// Custom physics body for voxel collision.
    /// Replaces Rigidbody for entities that collide with voxel terrain.
    /// Runs in FixedUpdate for consistent timestep.
    /// </summary>
    public class VoxelBody : MonoBehaviour {
        [Header("Collision Shape")]
        [SerializeField] private Vector3 _size = new Vector3(0.6f, 1.8f, 0.6f);
        [SerializeField] private Vector3 _offset = Vector3.zero;

        [Header("Physics")]
        [SerializeField] private float _gravityScale = 1f;
        [SerializeField] private float _groundDrag = 8f;
        [SerializeField] private float _airDrag = 0.1f;

        [Header("Step Up")]
        [SerializeField] private bool _enableStepUp = true;
        [SerializeField] private float _maxStepHeight = 0.6f;

        private Vector3 _velocity;
        private bool _grounded;
        private bool _hitCeiling;
        private bool _hitWall;
        private Vector3 _groundNormal;

        // Interpolation
        private Vector3 _previousPosition;
        private Vector3 _currentPosition;

        /// <summary>
        /// Current velocity in world units per second.
        /// </summary>
        public Vector3 Velocity {
            get => _velocity;
            set => _velocity = value;
        }

        /// <summary>
        /// True if standing on solid ground.
        /// </summary>
        public bool Grounded => _grounded;

        /// <summary>
        /// True if hit ceiling this frame.
        /// </summary>
        public bool HitCeiling => _hitCeiling;

        /// <summary>
        /// True if hit wall this frame.
        /// </summary>
        public bool HitWall => _hitWall;

        /// <summary>
        /// Normal of ground surface (only valid if Grounded).
        /// </summary>
        public Vector3 GroundNormal => _groundNormal;

        /// <summary>
        /// Get current collision bounds in world space.
        /// </summary>
        public Bounds GetBounds() {
            return new Bounds(_currentPosition + _offset, _size);
        }

        private void Start() {
            _currentPosition = transform.position;
            _previousPosition = _currentPosition;
        }

        private void Update() {
            // Interpolate visual position between physics steps
            float t = (Time.time - Time.fixedTime) / Time.fixedDeltaTime;
            transform.position = Vector3.Lerp(_previousPosition, _currentPosition, t);
        }

        private void FixedUpdate() {
            // Wait for world and chunk to be ready before running physics
            var world = VoxelWorld.Instance;
            if (world == null) return;

            var chunkPos = world.WorldToChunkPos(Vector3Int.FloorToInt(_currentPosition));
            var chunkId = world.ChunkIdFromPos(chunkPos);
            var maxChunks = world.MaxChunks;
            if (chunkId.x >= 0 && chunkId.x < maxChunks.x &&
                chunkId.y >= 0 && chunkId.y < maxChunks.y) {
                var chunk = world.Chunks[chunkId.x, chunkId.y];
                if (chunk == null || chunk.State < ChunkState.Lighting) {
                    return; // Wait for chunk to be ready
                }
            }

            // Store previous position for interpolation
            _previousPosition = _currentPosition;

            var dt = Time.fixedDeltaTime;

            // Apply gravity
            _velocity += Physics.gravity * _gravityScale * dt;

            // Apply drag
            var drag = _grounded ? _groundDrag : _airDrag;
            var horizontalVel = new Vector3(_velocity.x, 0, _velocity.z);
            horizontalVel = Vector3.MoveTowards(horizontalVel, Vector3.zero, drag * dt);
            _velocity.x = horizontalVel.x;
            _velocity.z = horizontalVel.z;

            // Calculate movement for this frame
            var movement = _velocity * dt;

            // Try step-up if enabled and moving horizontally
            if (_enableStepUp && _grounded && (movement.x != 0 || movement.z != 0)) {
                if (TryStepUp(ref movement)) {
                    // Step-up succeeded, movement was modified
                }
            }

            // Perform collision detection and resolution
            var bounds = GetBounds();
            var result = VoxelCollider.SweepAABB(bounds, movement);

            // Apply results
            _currentPosition = result.Position - _offset;
            _velocity = result.Velocity / dt; // Convert back to units/sec
            _grounded = result.Grounded;
            _hitCeiling = result.HitCeiling;
            _hitWall = result.HitWall;
            _groundNormal = result.GroundNormal;

            // Cancel vertical velocity on collision
            if (_grounded && _velocity.y < 0) {
                _velocity.y = 0;
            }
            if (_hitCeiling && _velocity.y > 0) {
                _velocity.y = 0;
            }
        }

        private bool TryStepUp(ref Vector3 movement) {
            var bounds = GetBounds();
            var horizontalMovement = new Vector3(movement.x, 0, movement.z);

            if (horizontalMovement.sqrMagnitude < 0.0001f) return false;

            // Check if we'd hit a wall with normal movement
            var testBounds = bounds;
            testBounds.center += horizontalMovement;

            var solidVoxels = new System.Collections.Generic.List<Vector3Int>(8);
            VoxelCollider.GetSolidVoxelsInBounds(testBounds, solidVoxels);

            if (solidVoxels.Count == 0) return false; // No obstacle

            // Try stepping up
            var stepUpBounds = bounds;
            stepUpBounds.center += Vector3.up * _maxStepHeight;

            // Check if space above is clear
            var aboveBounds = bounds;
            aboveBounds.center += Vector3.up * _maxStepHeight * 0.5f;
            aboveBounds.size = new Vector3(bounds.size.x, _maxStepHeight, bounds.size.z);

            solidVoxels.Clear();
            VoxelCollider.GetSolidVoxelsInBounds(aboveBounds, solidVoxels);
            if (solidVoxels.Count > 0) return false; // Can't step up, blocked above

            // Check if we can move forward at stepped-up height
            var forwardBounds = stepUpBounds;
            forwardBounds.center += horizontalMovement;

            solidVoxels.Clear();
            VoxelCollider.GetSolidVoxelsInBounds(forwardBounds, solidVoxels);
            if (solidVoxels.Count > 0) return false; // Still blocked at step height

            // Step up succeeded - modify movement to include vertical component
            // We'll step up, move forward, then let gravity bring us back down
            movement.y += _maxStepHeight;
            return true;
        }

        /// <summary>
        /// Add instantaneous force (like jump impulse).
        /// </summary>
        public void AddImpulse(Vector3 impulse) {
            _velocity += impulse;
        }

        /// <summary>
        /// Add continuous force (like movement acceleration).
        /// </summary>
        public void AddForce(Vector3 force) {
            _velocity += force * Time.fixedDeltaTime;
        }

        private void OnDrawGizmosSelected() {
            Gizmos.color = _grounded ? Color.green : Color.yellow;
            Gizmos.DrawWireCube(transform.position + _offset, _size);
        }
    }
}
