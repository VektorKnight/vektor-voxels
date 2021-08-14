using System;
using UnityEngine;
using UnityEngine.UI;

namespace VektorVoxels.UI {
    [RequireComponent(typeof(Canvas))]
    public class DebugUI : MonoBehaviour {
        [SerializeField] private Text _upperLeft;

        private Canvas _canvas;

        private void Awake() {
            _canvas = GetComponent<Canvas>();

            _upperLeft.text = $"{Application.productName}\n" +
                              $"FPS: {1f / Time.deltaTime:n0}";
        }

        private void Update() {
            _upperLeft.text = $"{Application.productName}\n" +
                              $"FPS: {1f / Time.deltaTime:n0}";
        }
    }
}