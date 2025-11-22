using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using VektorVoxels.Chunks;
using VektorVoxels.Input;
using VektorVoxels.VoxelPhysics;
using VektorVoxels.Voxels;
using VektorVoxels.World;
using Random = System.Random;

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

        [Header("Physics Feel")]
        [SerializeField] private float _fallGravityMultiplier = 2.5f;
        [SerializeField] private float _lowJumpGravityMultiplier = 2f;

        [Header("Voxel Interaction")]
        [SerializeField] private float _maxDistance = 10f;
        [SerializeField] private Transform _selectionCube;

        // Hotbar state
        private int _selectedSlot;
        private VoxelDefinition _selectedVoxel;

        public int SelectedSlot => _selectedSlot;
        public VoxelDefinition SelectedVoxel => _selectedVoxel;

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

        private bool _wantsBreak;
        private bool _wantsPlace;
        
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

            _playerControls.Gameplay.Break.performed += OnBreak;
            _playerControls.Gameplay.Place.performed += OnPlace;

            // Initialize hotbar with first voxel
            _selectedSlot = 0;
            UpdateSelectedVoxel();
        }

        private void UpdateSelectedVoxel() {
            var voxelCount = VoxelTable.VoxelCount;
            if (voxelCount > 0) {
                // Wrap slot index
                _selectedSlot = ((_selectedSlot % voxelCount) + voxelCount) % voxelCount;
                _selectedVoxel = VoxelTable.GetVoxelDefinition((uint)(_selectedSlot + 1)); // IDs start at 1
                Debug.Log($"Selected: {_selectedVoxel.FriendlyName} (slot {_selectedSlot + 1}/{voxelCount})");
            }
        }

        private void OnBreak(InputAction.CallbackContext ctx) {
            _wantsBreak = true;
        }
        
        private void OnPlace(InputAction.CallbackContext ctx) {
            _wantsPlace = true;
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

            // Handle scroll wheel for hotbar selection
            var scroll = Mouse.current.scroll.ReadValue().y;
            if (scroll != 0) {
                _selectedSlot += scroll > 0 ? -1 : 1;
                UpdateSelectedVoxel();
            }

            // Handle number keys 1-9 for direct slot selection
            var keyboard = Keyboard.current;
            if (keyboard != null) {
                for (int i = 0; i < 9; i++) {
                    var key = (Key)(Key.Digit1 + i);
                    if (keyboard[key].wasPressedThisFrame) {
                        var maxSlot = Mathf.Min(9, VoxelTable.VoxelCount);
                        if (i < maxSlot) {
                            _selectedSlot = i;
                            UpdateSelectedVoxel();
                        }
                        break;
                    }
                }
            }
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

            // Apply extra gravity when falling or releasing jump early for snappier feel.
            if (!_grounded) {
                var velocity = _rigidBody.linearVelocity;
                if (velocity.y < 0) {
                    // Falling - apply extra gravity for faster descent
                    _rigidBody.AddForce(Vector3.up * Physics.gravity.y * (_fallGravityMultiplier - 1), ForceMode.Acceleration);
                }
                else if (velocity.y > 0 && !_playerControls.Gameplay.Jump.IsPressed()) {
                    // Rising but jump released - cut jump short for variable jump height
                    _rigidBody.AddForce(Vector3.up * Physics.gravity.y * (_lowJumpGravityMultiplier - 1), ForceMode.Acceleration);
                }
            }

            // Ground movement with snappier response.
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
            var ray = new Ray(_camera.transform.position, _camera.transform.forward);
            if (VoxelWorld.Instance.TraceRay(ray, 10f, out var result)) {
                Debug.DrawRay(ray.origin, ray.direction, Color.yellow);
                _selectionCube.transform.position = result.World + new Vector3(0.5f, 0.5f, 0.5f);

                if (_wantsBreak) {
                    VoxelWorld.Instance.TryQueueVoxelUpdate(result.World, VoxelData.Null());
                    _wantsBreak = false;
                }

                if (_wantsPlace && _selectedVoxel != null) {
                    var pos = result.World + result.Normal;
                    VoxelWorld.Instance.TryQueueVoxelUpdate(pos, _selectedVoxel.GetDataInstance());
                    _wantsPlace = false;
                }
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