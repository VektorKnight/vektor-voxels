using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VektorVoxels.Input;
using VektorVoxels.VoxelPhysics;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Interaction {
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public class VektorPlayer : MonoBehaviour, IPlayer {
        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 5.0f;
        [SerializeField] private float _aerialControlForce = 5f;
        [SerializeField] private float _jumpForce = 60f;
        [SerializeField] private float _stopTime = 0.1f;

        [Header("Aiming")]
        [SerializeField] private float _lookSensitivity = 10.0f;
        [SerializeField] private float _lookSmoothTime = 0.02f;

        [Header("Terrain Interaction")]
        [SerializeField] private Vector3 _terrainCheckOrigin = new Vector3(0f, -0.5f, 0f);
        [SerializeField] private float _terrainCheckRadius = 0.25f;
        [SerializeField] private float _terrainCheckDistance = 0.1f;
        [SerializeField] private LayerMask _terrainMask;

        [Header("Voxel Interaction")]
        [SerializeField] private float _maxDistance = 10f;
        [SerializeField] private Transform _selectionCube;

        [Header("Camera")]
        [SerializeField] private Vector3 _cameraOffset = new Vector3(0f, 0.4f, 0f);
        [SerializeField] private Camera _camera;

        private PlayerControls _playerControls;
        private Rigidbody _rigidBody;

        private Vector3 _moveInput;
        private Vector2 _lookInput;
        private Vector3 _moveVelocity;

        private Vector2 _lookVelocity;
        private Vector2 _lookSmooth;
        private Vector3 _desiredLook;

        private Vector3 _groundNormal;
        private bool _grounded;
        
        public Vector3 Position => transform.position;
        public Quaternion Rotation => Quaternion.Euler(_desiredLook);
        public Vector3 RotationEuler => _desiredLook;
        public Vector3 Velocity => _rigidBody.linearVelocity;
        public bool Grounded => _grounded;

        private void Awake() {
            _playerControls = new PlayerControls();
            _playerControls.Enable();
            _playerControls.Gameplay.Jump.performed += OnJumpInput;
            
            _rigidBody = GetComponent<Rigidbody>();

            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update() {
            // Poll latest movement input.
            var moveInput = _playerControls.Gameplay.Move.ReadValue<Vector2>();
            _moveInput.x = moveInput.x;
            _moveInput.z = moveInput.y;
            _moveInput = _moveInput.normalized;

            // Poll and smooth latest look input.
            _lookInput = _playerControls.Gameplay.Look.ReadValue<Vector2>() * _lookSensitivity * Time.deltaTime;
            _lookSmooth = Vector2.SmoothDamp(_lookSmooth, _lookInput, ref _lookVelocity, _lookSmoothTime);
            
            // Update desired look vector and clamp pitch.
            _desiredLook.x -= _lookInput.y;
            _desiredLook.y += _lookInput.x;
            _desiredLook.x = Mathf.Clamp(_desiredLook.x, -89f, 89f);

            _moveInput = Quaternion.Euler(0f, _desiredLook.y, 0f) * _moveInput;
            
            // Update camera position and apply look rotation.
            _camera.transform.SetPositionAndRotation(transform.position + _cameraOffset, Quaternion.Euler(_desiredLook.x, _desiredLook.y, 0f));
        }

        private void FixedUpdate() {
            // Check for grounded state.
            if (Physics.SphereCast(transform.position + _terrainCheckOrigin, _terrainCheckRadius, Vector3.down, out var hit, _terrainCheckDistance, _terrainMask)) {
                _groundNormal = hit.normal;
                _grounded = true;
            }
            else {
                _groundNormal = Vector3.up;
                _grounded = false;
            }
            
            // Just smooth damp to the desired velocities when grounded.
            // I know this probably isn't "correct" but it works and nothing has exploded yet.
            // Regular forces are used when air-bourne.
            if (_grounded) {
                if (_moveInput.magnitude > 0f) {
                    _rigidBody.linearVelocity = Vector3.SmoothDamp(
                        _rigidBody.linearVelocity,
                        _moveInput * _moveSpeed,
                        ref _moveVelocity,
                        _stopTime,
                        float.MaxValue,
                        Time.fixedDeltaTime
                    );
                }
                else {
                    _rigidBody.linearVelocity = Vector3.SmoothDamp(
                        _rigidBody.linearVelocity,
                        Vector3.zero,
                        ref _moveVelocity,
                        _stopTime,
                        float.MaxValue,
                        Time.fixedDeltaTime
                    );
                }
            }
            else {
                _rigidBody.AddForce(_moveInput * _aerialControlForce, ForceMode.Acceleration);   
            }
            
            // Check for voxel intersection.
            if (!VoxelWorld.Instance.TryGetChunk(transform.position, out var chunk)) {
                return;
            }
            
            var ray = _camera.ViewportPointToRay(Vector3.one * 0.5f);
            if (VoxelTrace.TraceRay(ray, chunk, 10f, out var result)) {
                Debug.DrawRay(ray.origin, ray.direction, Color.yellow);
                _selectionCube.transform.position = result.World + new Vector3(0.5f, 0.5f, 0.5f);
            }
        }

        private void OnJumpInput(InputAction.CallbackContext context) {
            if (!_grounded) return;
            
            _rigidBody.AddRelativeForce(Vector3.up * _jumpForce, ForceMode.Impulse);
        }

        public void SetHandVoxel(VoxelDefinition definition) {
            throw new System.NotImplementedException();
        }

        public void Teleport(Vector3 position) {
            throw new System.NotImplementedException();
        }
    }
}