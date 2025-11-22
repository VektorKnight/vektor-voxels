using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using VektorVoxels.Voxels;

namespace VektorVoxels.Editor {
    [CustomEditor(typeof(VoxelDatabaseAsset))]
    public class VoxelDatabaseEditor : UnityEditor.Editor {
        private ReorderableList _voxelList;
        private VoxelDatabaseAsset _database;
        private Vector2 _scrollPosition;
        private int _selectedIndex = -1;
        private List<string> _cachedIssues;
        private bool _needsValidation = true;

        private static readonly string[] FaceNames = { "North", "East", "South", "West", "Top", "Bottom" };

        private void OnEnable() {
            _database = (VoxelDatabaseAsset)target;

            _voxelList = new ReorderableList(serializedObject,
                serializedObject.FindProperty("Voxels"),
                true, true, true, true);

            _voxelList.drawHeaderCallback = rect => {
                EditorGUI.LabelField(rect, $"Voxels ({_database.Voxels.Count})");
            };

            _voxelList.drawElementCallback = (rect, index, isActive, isFocused) => {
                var element = _voxelList.serializedProperty.GetArrayElementAtIndex(index);
                var internalName = element.FindPropertyRelative("InternalName");

                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;

                // Draw texture preview
                var previewRect = new Rect(rect.x, rect.y, 32, 32);
                DrawTexturePreview(previewRect, index);

                // Draw name
                var labelRect = new Rect(rect.x + 36, rect.y + 8, rect.width - 36, rect.height);
                EditorGUI.LabelField(labelRect, $"{index + 1}. {internalName.stringValue}");
            };

            _voxelList.elementHeightCallback = index => 36;

            _voxelList.onSelectCallback = list => {
                _selectedIndex = list.index;
            };

            _voxelList.onAddCallback = list => {
                var index = list.serializedProperty.arraySize;
                list.serializedProperty.arraySize++;
                list.index = index;

                var element = list.serializedProperty.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("InternalName").stringValue = "new_voxel";
                element.FindPropertyRelative("UseSingleTexture").boolValue = true;
                element.FindPropertyRelative("FaceTextures").arraySize = 6;

                _selectedIndex = index;
                _needsValidation = true;
            };

            _voxelList.onRemoveCallback = list => {
                ReorderableList.defaultBehaviours.DoRemoveButton(list);
                // Reset selection if it's now out of bounds
                if (_selectedIndex >= list.serializedProperty.arraySize) {
                    _selectedIndex = list.serializedProperty.arraySize - 1;
                }
                _needsValidation = true;
            };

            _voxelList.onReorderCallback = list => {
                _needsValidation = true;
            };
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            // Atlas settings
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("TextureAtlas"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("TileSize"));
            if (EditorGUI.EndChangeCheck()) {
                _needsValidation = true;
            }

            EditorGUILayout.Space(10);

            // Validation (cached)
            if (_needsValidation || _cachedIssues == null) {
                _cachedIssues = _database.Validate();
                _needsValidation = false;
            }
            if (_cachedIssues.Count > 0) {
                EditorGUILayout.HelpBox(string.Join("\n", _cachedIssues), MessageType.Warning);
            }

            EditorGUILayout.Space(10);

            // Voxel list
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.MaxHeight(300));
            _voxelList.DoLayoutList();
            EditorGUILayout.EndScrollView();

            // Clamp selection to valid range
            if (_selectedIndex >= _database.Voxels.Count) {
                _selectedIndex = _database.Voxels.Count - 1;
            }

            // Selected voxel details
            if (_selectedIndex >= 0 && _selectedIndex < _database.Voxels.Count) {
                EditorGUILayout.Space(10);
                EditorGUI.BeginChangeCheck();
                DrawSelectedVoxelDetails();
                if (EditorGUI.EndChangeCheck()) {
                    _needsValidation = true;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSelectedVoxelDetails() {
            var element = _voxelList.serializedProperty.GetArrayElementAtIndex(_selectedIndex);

            EditorGUILayout.LabelField("Selected Voxel", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(element.FindPropertyRelative("InternalName"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("Flags"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("Orientation"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("LightColor"));

            EditorGUILayout.Space(5);

            var useSingle = element.FindPropertyRelative("UseSingleTexture");
            EditorGUILayout.PropertyField(useSingle);

            if (useSingle.boolValue) {
                // Single texture picker
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(element.FindPropertyRelative("SingleTexture"));

                // Clickable preview
                if (_database.TextureAtlas != null) {
                    var singleTex = element.FindPropertyRelative("SingleTexture");
                    var coords = singleTex.vector2IntValue;
                    if (DrawClickableTilePreview(coords.x, coords.y, 48)) {
                        TexturePickerWindow.Show(_database, _selectedIndex, true);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            else {
                // Per-face textures
                var faceTextures = element.FindPropertyRelative("FaceTextures");
                if (faceTextures.arraySize != 6) {
                    faceTextures.arraySize = 6;
                }

                EditorGUILayout.LabelField("Face Textures", EditorStyles.miniBoldLabel);

                for (int i = 0; i < 6; i++) {
                    EditorGUILayout.BeginHorizontal();
                    var faceProp = faceTextures.GetArrayElementAtIndex(i);
                    EditorGUILayout.PropertyField(faceProp, new GUIContent(FaceNames[i]));

                    if (_database.TextureAtlas != null) {
                        var coords = faceProp.vector2IntValue;
                        if (DrawClickableTilePreview(coords.x, coords.y, 32)) {
                            TexturePickerWindow.Show(_database, _selectedIndex, false, i);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUI.indentLevel--;

            // Texture atlas picker
            if (_database.TextureAtlas != null) {
                EditorGUILayout.Space(10);
                if (GUILayout.Button("Open Texture Picker")) {
                    TexturePickerWindow.Show(_database, _selectedIndex, useSingle.boolValue);
                }
            }
        }

        private void DrawTexturePreview(Rect rect, int voxelIndex) {
            if (_database.TextureAtlas == null || voxelIndex >= _database.Voxels.Count) return;

            var voxel = _database.Voxels[voxelIndex];
            var coords = voxel.UseSingleTexture ? voxel.SingleTexture : voxel.FaceTextures[4]; // Top face

            var tilesPerRow = _database.TilesPerRow;
            var tilesPerCol = _database.TilesPerColumn;

            if (tilesPerRow == 0 || tilesPerCol == 0) return;

            var uvRect = new Rect(
                (float)coords.x / tilesPerRow,
                1f - (float)(coords.y + 1) / tilesPerCol,
                1f / tilesPerRow,
                1f / tilesPerCol
            );

            GUI.DrawTextureWithTexCoords(rect, _database.TextureAtlas, uvRect);
        }

        private void DrawTilePreview(int tileX, int tileY, float size) {
            var tilesPerRow = _database.TilesPerRow;
            var tilesPerCol = _database.TilesPerColumn;

            if (tilesPerRow == 0 || tilesPerCol == 0) return;

            var uvRect = new Rect(
                (float)tileX / tilesPerRow,
                1f - (float)(tileY + 1) / tilesPerCol,
                1f / tilesPerRow,
                1f / tilesPerCol
            );

            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            GUI.DrawTextureWithTexCoords(rect, _database.TextureAtlas, uvRect);
        }

        private bool DrawClickableTilePreview(int tileX, int tileY, float size) {
            var tilesPerRow = _database.TilesPerRow;
            var tilesPerCol = _database.TilesPerColumn;

            if (tilesPerRow == 0 || tilesPerCol == 0) return false;

            var uvRect = new Rect(
                (float)tileX / tilesPerRow,
                1f - (float)(tileY + 1) / tilesPerCol,
                1f / tilesPerRow,
                1f / tilesPerCol
            );

            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            GUI.DrawTextureWithTexCoords(rect, _database.TextureAtlas, uvRect);

            // Draw border to indicate clickable
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), Color.gray);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Color.gray);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), Color.gray);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), Color.gray);

            // Handle click
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition)) {
                Event.current.Use();
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Popup window for picking textures from the atlas.
    /// </summary>
    public class TexturePickerWindow : EditorWindow {
        private VoxelDatabaseAsset _database;
        private int _voxelIndex;
        private bool _singleTexture;
        private int _selectedFace;
        private Vector2 _scrollPos;

        public static void Show(VoxelDatabaseAsset database, int voxelIndex, bool singleTexture, int initialFace = 0) {
            var window = GetWindow<TexturePickerWindow>("Texture Picker");
            window._database = database;
            window._voxelIndex = voxelIndex;
            window._singleTexture = singleTexture;
            window._selectedFace = initialFace;
            window.minSize = new Vector2(300, 300);
            window.Show();
        }

        private void OnGUI() {
            if (_database == null || _database.TextureAtlas == null) {
                EditorGUILayout.HelpBox("No texture atlas assigned", MessageType.Warning);
                return;
            }

            if (!_singleTexture) {
                _selectedFace = GUILayout.Toolbar(_selectedFace,
                    new[] { "North", "East", "South", "West", "Top", "Bottom" });
            }

            EditorGUILayout.LabelField("Click a tile to select it", EditorStyles.miniLabel);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            var tilesPerRow = _database.TilesPerRow;
            var tilesPerCol = _database.TilesPerColumn;
            var tileSize = 32;

            for (int y = 0; y < tilesPerCol; y++) {
                EditorGUILayout.BeginHorizontal();
                for (int x = 0; x < tilesPerRow; x++) {
                    var uvRect = new Rect(
                        (float)x / tilesPerRow,
                        1f - (float)(y + 1) / tilesPerCol,
                        1f / tilesPerRow,
                        1f / tilesPerCol
                    );

                    var rect = GUILayoutUtility.GetRect(tileSize, tileSize,
                        GUILayout.Width(tileSize), GUILayout.Height(tileSize));

                    GUI.DrawTextureWithTexCoords(rect, _database.TextureAtlas, uvRect);

                    if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) {
                        SetTexture(x, y);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void SetTexture(int x, int y) {
            if (_voxelIndex >= _database.Voxels.Count) return;

            Undo.RecordObject(_database, "Set Voxel Texture");

            var voxel = _database.Voxels[_voxelIndex];
            if (_singleTexture) {
                voxel.SingleTexture = new Vector2Int(x, y);
            }
            else {
                voxel.FaceTextures[_selectedFace] = new Vector2Int(x, y);
            }

            EditorUtility.SetDirty(_database);
        }
    }
}
