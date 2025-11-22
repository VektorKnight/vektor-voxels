using UnityEngine;
using VektorVoxels.Interaction;
using VektorVoxels.Voxels;

namespace VektorVoxels.UI {
    /// <summary>
    /// Simple IMGUI-based hotbar similar to Minecraft's creative mode.
    /// Displays 9 slots with voxel textures and highlights the selected slot.
    /// </summary>
    public class HotbarUI : MonoBehaviour {
        [Header("References")]
        [SerializeField] private VektorPlayer _player;
        [SerializeField] private Texture2D _atlas;

        [Header("Layout")]
        [SerializeField] private int _slotSize = 48;
        [SerializeField] private int _slotPadding = 4;
        [SerializeField] private int _bottomMargin = 20;

        [Header("Colors")]
        [SerializeField] private Color _slotColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        [SerializeField] private Color _selectedColor = new Color(0.8f, 0.8f, 0.8f, 0.9f);
        [SerializeField] private Color _borderColor = new Color(0.1f, 0.1f, 0.1f, 1f);

        [SerializeField] private int _visibleSlots = 9;

        private GUIStyle _labelStyle;
        private GUIStyle _smallLabelStyle;
        private Texture2D _whiteTexture;

        private void Start() {
            // Create a 1x1 white texture for drawing colored boxes
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();

            // Ensure odd number of slots so selection is centered
            if (_visibleSlots % 2 == 0) _visibleSlots++;
        }

        private void OnDestroy() {
            if (_whiteTexture != null) {
                Destroy(_whiteTexture);
            }
        }

        private void OnGUI() {
            if (_player == null || _atlas == null) return;

            // Initialize label style on first use
            if (_labelStyle == null) {
                _labelStyle = new GUIStyle(GUI.skin.label) {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };
            }

            var voxelCount = VoxelTable.VoxelCount;
            if (voxelCount == 0) return;

            var slotsToShow = Mathf.Min(_visibleSlots, voxelCount);
            var totalWidth = slotsToShow * _slotSize + (slotsToShow - 1) * _slotPadding;
            var startX = (Screen.width - totalWidth) / 2f;
            var startY = Screen.height - _slotSize - _bottomMargin;

            var centerSlot = slotsToShow / 2;
            var selectedSlot = _player.SelectedSlot;

            // Draw slots as carousel - selected is always in center
            for (int i = 0; i < slotsToShow; i++) {
                var slotX = startX + i * (_slotSize + _slotPadding);
                var slotRect = new Rect(slotX, startY, _slotSize, _slotSize);
                var isCenter = i == centerSlot;

                // Calculate which voxel to show (wrap around)
                var offset = i - centerSlot;
                var voxelIndex = WrapIndex(selectedSlot + offset, voxelCount);

                // Draw slot background
                DrawSlotBackground(slotRect, isCenter);

                // Draw voxel texture
                var voxel = VoxelTable.GetVoxelDefinition((uint)(voxelIndex + 1));
                DrawVoxelTexture(slotRect, voxel);
            }

            // Draw selected voxel name above hotbar
            if (_player.SelectedVoxel != null) {
                var nameRect = new Rect(0, startY - 30, Screen.width, 25);
                GUI.Label(nameRect, _player.SelectedVoxel.FriendlyName, _labelStyle);
            }
        }

        private int WrapIndex(int index, int count) {
            return ((index % count) + count) % count;
        }

        private void DrawSlotBackground(Rect rect, bool selected) {
            var bgColor = selected ? _selectedColor : _slotColor;

            // Draw border
            GUI.color = _borderColor;
            GUI.DrawTexture(rect, _whiteTexture);

            // Draw inner background
            var innerRect = new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4);
            GUI.color = bgColor;
            GUI.DrawTexture(innerRect, _whiteTexture);

            GUI.color = Color.white;
        }

        private void DrawVoxelTexture(Rect slotRect, VoxelDefinition voxel) {
            // Get UV rect for the voxel's north face (or first texture)
            var uvRect = voxel.GetTextureRect(FacingDirection.North);

            // Flip vertically for IMGUI (top-left origin vs UV bottom-left origin)
            uvRect.y = uvRect.y + uvRect.height;
            uvRect.height = -uvRect.height;

            // Inset the texture a bit from the slot edges
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
