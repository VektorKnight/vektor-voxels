using System;
using UnityEngine;
using UnityEngine.UI;
using VektorVoxels.Interaction;
using VektorVoxels.Voxels;

namespace VektorVoxels.UI {
    [RequireComponent(typeof(Canvas))]
    public class PlayerUI  : MonoBehaviour {
        [SerializeField] private Image _compass;

        [Header("Block Selector")] 
        [SerializeField] private BlockImage[] _blockImages = new BlockImage[7];
        [SerializeField] private BasicPlayer _player;

        private int _selectionOffset;

        private void Awake() {
            CycleHotBar(-3);
        }

        private int RoundRobin(int x, int xMin, int xMax) {
            if (x < xMin)
                x = xMax - (xMin - x) % (xMax - xMin);
            else
                x = xMin + (x - xMin) % (xMax - xMin);

            return x;
        }

        private void CycleHotBar(int offset) {
            _selectionOffset += offset;
            for (var i = 0; i < _blockImages.Length; i++) {
                var voxelId = RoundRobin(_selectionOffset + i, 0, VoxelTable.VoxelCount);
                _blockImages[i].SetVoxelDefinition(VoxelTable.GetVoxelDefinition((uint)voxelId + 1));
                _blockImages[i].SetLabelState(i == 3);
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
        }
    }
}