using UnityEngine;
using VektorVoxels.Interaction;
using VektorVoxels.Voxels;

namespace VektorVoxels.UI {
    /// <summary>
    /// IMGUI-based display showing the voxel the player is currently looking at.
    /// Displays at the top of the screen with texture and name.
    /// </summary>
    public class LookingAtUI : MonoBehaviour {
        [Header("References")]
        [SerializeField] private VektorPlayer _player;
        [SerializeField] private Texture2D _atlas;

        [Header("Layout")]
        [SerializeField] private int _slotSize = 48;
        [SerializeField] private int _topMargin = 20;

        [Header("Colors")]
        [SerializeField] private Color _slotColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        [SerializeField] private Color _borderColor = new Color(0.1f, 0.1f, 0.1f, 1f);

        private GUIStyle _labelStyle;
        private Texture2D _whiteTexture;

        private void Start() {
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }

        private void OnDestroy() {
            if (_whiteTexture != null) {
                Destroy(_whiteTexture);
            }
        }

        private void OnGUI() {
            if (_player == null || _atlas == null) return;

            var voxel = _player.LookingAtVoxel;
            if (voxel == null) return;

            if (_labelStyle == null) {
                _labelStyle = new GUIStyle(GUI.skin.label) {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };
            }

            var startX = (Screen.width - _slotSize) / 2f;
            var startY = _topMargin;

            var slotRect = new Rect(startX, startY, _slotSize, _slotSize);

            // Draw slot background
            DrawSlotBackground(slotRect);

            // Draw voxel texture
            DrawVoxelTexture(slotRect, voxel);

            // Draw voxel name below
            var nameRect = new Rect(0, startY + _slotSize + 5, Screen.width, 25);
            GUI.Label(nameRect, voxel.FriendlyName, _labelStyle);
        }

        private void DrawSlotBackground(Rect rect) {
            // Draw border
            GUI.color = _borderColor;
            GUI.DrawTexture(rect, _whiteTexture);

            // Draw inner background
            var innerRect = new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4);
            GUI.color = _slotColor;
            GUI.DrawTexture(innerRect, _whiteTexture);

            GUI.color = Color.white;
        }

        private void DrawVoxelTexture(Rect slotRect, VoxelDefinition voxel) {
            var uvRect = voxel.GetTextureRect(FacingDirection.North);

            // Flip vertically for IMGUI
            uvRect.y = uvRect.y + uvRect.height;
            uvRect.height = -uvRect.height;

            var texRect = new Rect(
                slotRect.x + 6,
                slotRect.y + 6,
                slotRect.width - 12,
                slotRect.height - 12
            );

            GUI.DrawTextureWithTexCoords(texRect, _atlas, uvRect);
        }
    }
}
