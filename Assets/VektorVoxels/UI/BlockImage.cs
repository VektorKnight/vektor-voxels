using UnityEngine;
using UnityEngine.UI;
using VektorVoxels.Meshing;
using VektorVoxels.Voxels;

namespace VektorVoxels.UI {
    public sealed class BlockImage : MonoBehaviour {
        [SerializeField] private RawImage _image;
        [SerializeField] private Text _label;

        public void SetVoxelDefinition(VoxelDefinition definition) {
            _image.uvRect = definition.GetTextureRect(BlockSide.North);
            _label.text = definition.FriendlyName;
        }

        public void SetLabelState(bool state) {
            _label.enabled = state;
        }
    }
}