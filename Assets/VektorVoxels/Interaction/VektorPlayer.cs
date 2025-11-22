using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VektorVoxels.Input;
using VektorVoxels.VoxelPhysics;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Interaction {
    [RequireComponent(typeof(VoxelBody))]
    public class VektorPlayer : MonoBehaviour, IPlayer {
        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 5.0f;
        [SerializeField] private float _aerialControlForce = 25f;
        [SerializeField] private float _jumpVelocity = 8f;
        [SerializeField] private float _acceleration = 50f;
        [SerializeField] private float _deceleration = 40f;

        [Header("Aiming")]
        [SerializeField] private float _lookSensitivity = 10.0f;
        [SerializeField] private float _lookSmoothTime = 0.02f;

        [Header("Physics Feel")]
        [SerializeField] private float _fallGravityMultiplier = 2.5f;
        [SerializeField] private float _lowJumpGravityMultiplier = 2f;

        [Header("Voxel Interaction")]
        [SerializeField] private float _maxDistance = 10f;
        [SerializeField] private SelectionIndicator _selectionCube;

        // Hotbar state
        private int _selectedSlot;
        private VoxelDefinition _selectedVoxel;

        // Looking-at state
        private VoxelDefinition _lookingAtVoxel;

        public int SelectedSlot => _selectedSlot;
        public VoxelDefinition SelectedVoxel => _selectedVoxel;
        public VoxelDefinition LookingAtVoxel => _lookingAtVoxel;

        [Header("Camera")]
        [SerializeField] private Vector3 _cameraOffset = new Vector3(0f, 0.4f, 0f);
        [SerializeField] private Camera _camera;

        private PlayerControls _playerControls;
        private VoxelBody _voxelBody;

        private Vector3 _moveInput;
        private Vector2 _lookInput;

        private Vector2 _lookVelocity;
        private Vector2 _lookSmooth;
        private Vector3 _desiredLook;

        private bool _wantsBreak;
        private bool _wantsPlace;
        private bool _jumpHeld;

        // Crosshair
        [Header("Crosshair")]
        [SerializeField] private int _crosshairSize = 12;
        [SerializeField] private int _crosshairThickness = 2;
        [SerializeField] private Color _crosshairColor = Color.white;
        private Texture2D _crosshairTexture;

        public Vector3 Position => transform.position;
        public Quaternion Rotation => Quaternion.Euler(_desiredLook);
        public Vector3 RotationEuler => _desiredLook;
        public Vector3 Velocity => _voxelBody.Velocity;
        public bool Grounded => _voxelBody.Grounded;

        private void Awake() {
            _playerControls = new PlayerControls();
            _playerControls.Enable();
            _playerControls.Gameplay.Jump.performed += OnJumpInput;

            _voxelBody = GetComponent<VoxelBody>();

            Cursor.lockState = CursorLockMode.Locked;

            _playerControls.Gameplay.Break.performed += OnBreak;
            _playerControls.Gameplay.Place.performed += OnPlace;

            // Initialize hotbar with first voxel
            _selectedSlot = 0;
            UpdateSelectedVoxel();

            // Create crosshair texture
            _crosshairTexture = new Texture2D(1, 1);
            _crosshairTexture.SetPixel(0, 0, Color.white);
            _crosshairTexture.Apply();
        }

        private void OnDestroy() {
            if (_crosshairTexture != null) {
                Destroy(_crosshairTexture);
            }
        }

        private void OnGUI() {
            if (_crosshairTexture == null) return;

            GUI.color = _crosshairColor;
            var centerX = Screen.width / 2f;
            var centerY = Screen.height / 2f;

            // Horizontal line
            GUI.DrawTexture(new Rect(
                centerX - _crosshairSize / 2f,
                centerY - _crosshairThickness / 2f,
                _crosshairSize,
                _crosshairThickness
            ), _crosshairTexture);

            // Vertical line
            GUI.DrawTexture(new Rect(
                centerX - _crosshairThickness / 2f,
                centerY - _crosshairSize / 2f,
                _crosshairThickness,
                _crosshairSize
            ), _crosshairTexture);

            GUI.color = Color.white;
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

            // Track jump held state for variable jump height
            _jumpHeld = _playerControls.Gameplay.Jump.IsPressed();

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
            var velocity = _voxelBody.Velocity;
            var dt = Time.fixedDeltaTime;

            // Apply extra gravity when falling or releasing jump early for snappier feel.
            if (!_voxelBody.Grounded) {
                if (velocity.y < 0) {
                    // Falling - apply extra gravity for faster descent
                    _voxelBody.AddForce(Vector3.up * Physics.gravity.y * (_fallGravityMultiplier - 1));
                }
                else if (velocity.y > 0 && !_jumpHeld) {
                    // Rising but jump released - cut jump short for variable jump height
                    _voxelBody.AddForce(Vector3.up * Physics.gravity.y * (_lowJumpGravityMultiplier - 1));
                }
            }

            // Ground movement
            if (_voxelBody.Grounded) {
                var targetVelocity = _moveInput * _moveSpeed;
                var currentHorizontal = new Vector3(velocity.x, 0, velocity.z);
                var targetHorizontal = new Vector3(targetVelocity.x, 0, targetVelocity.z);

                Vector3 newHorizontal;
                if (_moveInput.magnitude > 0.1f) {
                    // Accelerate toward target
                    newHorizontal = Vector3.MoveTowards(currentHorizontal, targetHorizontal, _acceleration * dt);
                } else {
                    // Decelerate to stop
                    newHorizontal = Vector3.MoveTowards(currentHorizontal, Vector3.zero, _deceleration * dt);
                }

                _voxelBody.Velocity = new Vector3(newHorizontal.x, velocity.y, newHorizontal.z);
            }
            else {
                // Air control - add force but don't override
                _voxelBody.AddForce(_moveInput * _aerialControlForce);
            }

            // Check for voxel intersection.
            var ray = new Ray(_camera.transform.position, _camera.transform.forward);
            if (VoxelWorld.Instance.TraceRay(ray, 10f, out var result)) {
                Debug.DrawRay(ray.origin, ray.direction, Color.yellow);
                
                _selectionCube.SetPositionAndFacing(result.World + new Vector3(0.5f, 0.5f, 0.5f), result.Normal);

                // Update looked-at voxel
                var voxelData = result.Voxel;
                _lookingAtVoxel = voxelData.Id > 0 ? VoxelTable.GetVoxelDefinition(voxelData.Id) : null;

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
            else {
                _lookingAtVoxel = null;
            }
        }

        private void OnJumpInput(InputAction.CallbackContext context) {
            if (!_voxelBody.Grounded) return;

            var velocity = _voxelBody.Velocity;
            velocity.y = _jumpVelocity;
            _voxelBody.Velocity = velocity;
        }

        public void SetHandVoxel(VoxelDefinition definition) {
            throw new System.NotImplementedException();
        }

        public void Teleport(Vector3 position) {
            transform.position = position;
            _voxelBody.Velocity = Vector3.zero;
        }
    }
}
