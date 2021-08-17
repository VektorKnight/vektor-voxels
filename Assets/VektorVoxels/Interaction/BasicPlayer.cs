using System;
using UnityEngine;
using VektorVoxels.Chunks;
using VektorVoxels.Voxels;
using VektorVoxels.World;

namespace VektorVoxels.Interaction {
    [RequireComponent(typeof(CharacterController))]
    public class BasicPlayer : MonoBehaviour {
        [Header("Movement")] 
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _thrustMultiplier = 2f;
        [SerializeField] private float _jumpSpeed = 10f;
        [SerializeField] private float _terminalSpeed = 50f;

        [Header("Mouse")] 
        [SerializeField] private float _mouseSensitivity = 5.0f;
        [SerializeField] private float _mouseSmoothTime = 0.02f;
        [SerializeField] private Camera _mainCamera;

        [Header("Editing")]
        [SerializeField] private LayerMask _selectionMask;
        [SerializeField] private Transform _selector;
        
        private Vector2 _mouseRaw;
        private Vector2 _mouseSmooth;
        private Vector2 _mouseVel;
        private Vector2 _desiredRotation;
        private Vector3 _velocity;
        private CharacterController _controller;

        private void Awake() {
            _controller = GetComponent<CharacterController>();
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update() {
            _velocity += Physics.gravity * Time.deltaTime;
            _velocity.x = Input.GetAxis("Horizontal") * _moveSpeed;
            _velocity.z = Input.GetAxis("Vertical") * _moveSpeed;
            
            if (_controller.isGrounded) {
                _velocity.y = Mathf.Clamp(_velocity.y, -0.01f, 0.01f);
            }

            if (_controller.isGrounded && Input.GetKeyDown(KeyCode.Space)) {
                _velocity.y = _jumpSpeed;
            }

            _velocity.y = Mathf.Clamp(_velocity.y, -_terminalSpeed, _terminalSpeed);
            _controller.Move(Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f) * _velocity * Time.deltaTime);
            
            if (Input.GetKeyDown(KeyCode.Mouse0)) {
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (Input.GetKeyDown(KeyCode.Escape)) {
                Cursor.lockState = CursorLockMode.None;
            }
            
            _mouseRaw = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            _mouseSmooth = Vector2.SmoothDamp(_mouseSmooth, _mouseRaw, ref _mouseVel, _mouseSmoothTime);
            
            _desiredRotation.y += _mouseSmooth.x * _mouseSensitivity;
            _desiredRotation.x -= _mouseSmooth.y * _mouseSensitivity;

            _desiredRotation.x = Mathf.Clamp(_desiredRotation.x, -89f, 89f);
            
            transform.rotation = Quaternion.Euler(0f, _desiredRotation.y, 0f);
            
            _mainCamera.transform.localRotation = Quaternion.Euler(_desiredRotation.x, 0f, 0f);
            
            var selectionRay = _mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (Physics.Raycast(selectionRay, out RaycastHit hit, 10f, _selectionMask)) {
                Debug.DrawLine(selectionRay.origin, hit.point, Color.cyan);
                _selector.transform.position = new Vector3(
                    Mathf.FloorToInt(hit.point.x) + 0.5f,
                    Mathf.FloorToInt(hit.point.y) - 0.5f,
                    Mathf.FloorToInt(hit.point.z) + 0.5f
                );

                if (Input.GetKeyDown(KeyCode.Mouse0)) {
                    if (WorldManager.Instance.TryGetChunk(hit.point, out var chunk)) {
                        var glowstone = VoxelTable.GetVoxelDefinition("lightstone_green");
                        var local = chunk.WorldToLocal(hit.point);
                        chunk.QueueVoxelUpdate(new VoxelUpdate(local, glowstone.GetDataInstance()));
                    }
                }
            }
            else {
                _selector.transform.position = Vector3.down * 1000;
            }
        }

        private void FixedUpdate() {
            
        }
    }
}