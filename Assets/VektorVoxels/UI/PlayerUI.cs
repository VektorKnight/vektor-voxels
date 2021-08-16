using System;
using UnityEngine;
using UnityEngine.UI;
using VektorVoxels.Interaction;

namespace VektorVoxels.UI {
    [RequireComponent(typeof(Canvas))]
    public class PlayerUI  : MonoBehaviour {
        [SerializeField] private Image _compass;

        [SerializeField] private BasicPlayer _player;

        private void Update() {
            _compass.rectTransform.rotation = Quaternion.Euler(0, 0, _player.transform.rotation.eulerAngles.y);
        }
    }
}