using System;
using UnityEngine;
using UnityEngine.UI;
using VektorVoxels.Interaction;
using VektorVoxels.Voxels;

namespace VektorVoxels.UI {
    [RequireComponent(typeof(Canvas))]
    public class PlayerUI : MonoBehaviour {
        [Header("UI Objects")]
        [SerializeField] private Image _compass;
        [SerializeField] private Text _selectedName;
        [SerializeField] private BlockImage[] _blockImages = new BlockImage[7];
        
        private int _selectionOffset;
        private IPlayer _player;
        private bool _initialized;

        public void Initialize(IPlayer player) {
            _player = player;
            CycleHotBar(-3);
            _initialized = true;
        }

        private int WrapIndex(int value, int lower, int upper) {
            var range_size = upper - lower;

            var wrapped = value;

            if (value < lower) {
                wrapped += range_size * ((lower - value) / range_size + 1);
            }

            return lower + (wrapped - lower) % range_size;
        }

        private void CycleHotBar(int offset) {
            _selectionOffset += offset;
            
            for (var i = 0; i < _blockImages.Length; i++) {
                var voxelId = WrapIndex(_selectionOffset + i, 0, VoxelTable.VoxelCount);
                try {
                    _blockImages[i].SetVoxelDefinition(VoxelTable.Voxels[voxelId]);

                    if (i == 3) {
                        var selected = VoxelTable.Voxels[voxelId];
                        _selectedName.text = $"{selected.FriendlyName}";
                        _player.SetHandVoxel(selected);
                    }
                }
                catch (Exception e) {
                    Debug.Log($"{voxelId} | {_selectionOffset + i}");
                    throw;
                }
            }
        }

        private void Update() {
            if (!_initialized) return;
            
            _compass.rectTransform.rotation = Quaternion.Euler(0, 0, _player.RotationEuler.y);

            //if (Input.GetKeyDown(KeyCode.LeftBracket)) {
                //CycleHotBar(-1);
            //}
            //else if (Input.GetKeyDown(KeyCode.RightBracket)) {
                //CycleHotBar(1);
            //}

            //CycleHotBar(Mathf.RoundToInt(Input.mouseScrollDelta.y));
        }
    }
}