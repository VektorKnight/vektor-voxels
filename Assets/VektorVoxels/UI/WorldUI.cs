using System.Collections.Generic;
using UnityEngine;
using VektorVoxels.World;

namespace VektorVoxels.UI {
    /// <summary>
    /// IMGUI interface for world save/load operations.
    /// Toggle with F5 key.
    /// </summary>
    public class WorldUI : MonoBehaviour {
        private bool _showUI;
        private string _newWorldName = "New World";
        private string _newWorldSeed = "12345";
        private string[] _availableWorlds;
        private Vector2 _scrollPosition;
        private string _statusMessage = "";
        private float _statusTimer;

        /// <summary>
        /// Returns true if any UI that should block player input is open.
        /// </summary>
        public static bool IsUIOpen { get; private set; }

        private void Start() {
            // Show UI on start and wait for world selection
            _showUI = true;
            IsUIOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshWorldList();
        }

        private void Update() {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F5)) {
                // Only allow closing if a world is loaded
                var world = VoxelWorld.Instance;
                if (world != null && world.IsWorldLoaded) {
                    _showUI = !_showUI;
                    IsUIOpen = _showUI;

                    if (_showUI) {
                        RefreshWorldList();
                        Cursor.lockState = CursorLockMode.None;
                        Cursor.visible = true;
                    }
                    else {
                        Cursor.lockState = CursorLockMode.Locked;
                        Cursor.visible = false;
                    }
                }
            }

            if (_statusTimer > 0) {
                _statusTimer -= Time.deltaTime;
            }
        }

        private void OnGUI() {
            if (!_showUI) return;

            var world = VoxelWorld.Instance;
            if (world == null) return;

            // Center the window
            var windowWidth = 300f;
            var windowHeight = 450f;
            var windowX = (Screen.width - windowWidth) / 2f;
            var windowY = (Screen.height - windowHeight) / 2f;

            // Main window
            GUILayout.BeginArea(new Rect(windowX, windowY, windowWidth, windowHeight), GUI.skin.box);
            GUILayout.Label("World Manager", GUI.skin.box);

            // Current world status
            GUILayout.Space(5);
            if (world.IsWorldLoaded) {
                GUILayout.Label($"Current: {world.Persistence.CurrentWorldName}");
                var dirtyCount = world.GetDirtyChunkCount();
                GUILayout.Label($"Unsaved chunks: {dirtyCount}");

                if (GUILayout.Button("Save Now")) {
                    world.SaveWorld();
                    SetStatus("World saved!");
                }
            }
            else {
                GUILayout.Label("No world loaded");
                GUILayout.Label("(Chunks won't persist)");
            }

            GUILayout.Space(10);

            // Create new world
            GUILayout.Label("Create New World", GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", GUILayout.Width(50));
            _newWorldName = GUILayout.TextField(_newWorldName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed:", GUILayout.Width(50));
            _newWorldSeed = GUILayout.TextField(_newWorldSeed);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Create World")) {
                if (int.TryParse(_newWorldSeed, out var seed)) {
                    if (world.CreateWorld(_newWorldName, seed)) {
                        SetStatus($"Created world: {_newWorldName}");
                        RefreshWorldList();
                        CloseUI();
                    }
                    else {
                        SetStatus("Failed to create world");
                    }
                }
                else {
                    SetStatus("Invalid seed number");
                }
            }

            GUILayout.Space(10);

            // Load existing world
            GUILayout.Label("Load World", GUI.skin.box);

            if (_availableWorlds == null || _availableWorlds.Length == 0) {
                GUILayout.Label("No worlds found");
            }
            else {
                _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(150));

                foreach (var worldName in _availableWorlds) {
                    GUILayout.BeginHorizontal();

                    if (GUILayout.Button(worldName, GUILayout.ExpandWidth(true))) {
                        if (world.LoadWorld(worldName, out var missingVoxels)) {
                            SetStatus($"Loaded: {worldName}");
                            CloseUI();
                        }
                        else if (missingVoxels != null && missingVoxels.Count > 0) {
                            SetStatus($"Missing voxels: {string.Join(", ", missingVoxels)}");
                        }
                        else {
                            SetStatus("Failed to load world");
                        }
                    }

                    if (GUILayout.Button("X", GUILayout.Width(25))) {
                        if (world.Persistence.DeleteWorld(worldName)) {
                            SetStatus($"Deleted: {worldName}");
                            RefreshWorldList();
                        }
                    }

                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            }

            if (GUILayout.Button("Refresh List")) {
                RefreshWorldList();
            }

            // Status message
            if (_statusTimer > 0 && !string.IsNullOrEmpty(_statusMessage)) {
                GUILayout.Space(5);
                GUILayout.Label(_statusMessage, GUI.skin.box);
            }

            GUILayout.Space(5);
            if (world.IsWorldLoaded) {
                GUILayout.Label("Press F5 to close", GUI.skin.label);
            }
            else {
                GUILayout.Label("Create or load a world to begin", GUI.skin.label);
            }

            GUILayout.EndArea();
        }

        private void RefreshWorldList() {
            var world = VoxelWorld.Instance;
            if (world != null) {
                _availableWorlds = world.GetAvailableWorlds();
            }
        }

        private void SetStatus(string message) {
            _statusMessage = message;
            _statusTimer = 3f;
        }

        private void CloseUI() {
            _showUI = false;
            IsUIOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
