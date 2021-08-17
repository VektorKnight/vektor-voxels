using System;
using UnityEngine;
using UnityEngine.UI;
using VektorVoxels.Interaction;
using VektorVoxels.Voxels;

namespace VektorVoxels.UI {
    [RequireComponent(typeof(Canvas))]
    public class PlayerUI : MonoBehaviour {
        [SerializeField] private Image _compass;

        [Header("Block Selector")] 
        [SerializeField] private BlockImage[] _blockImages = new BlockImage[7];
        [SerializeField] private BasicPlayer _player;

        private int _selectionOffset;

        private void Awake() {
            CycleHotBar(-3);
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
                    _blockImages[i].SetLabelState(i == 3);
                }
                catch (Exception e) {
                    Debug.Log($"{voxelId} | {_selectionOffset + i}");
                    throw;
                }
                
                
            }
        }

        private void Update() {
            _compass.rectTransform.rotation = Quaternion.Euler(0, 0, _player.transform.rotation.eulerAngles.y);

            if (Input.GetKeyDown(KeyCode.LeftBracket)) {
                CycleHotBar(-1);
            }
            else if (Input.GetKeyDown(KeyCode.RightBracket)) {
                CycleHotBar(1);
            }

            CycleHotBar(Mathf.RoundToInt(Input.mouseScrollDelta.y));
        }
    }
}