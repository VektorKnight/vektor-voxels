using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using VektorVoxels.World;

namespace VektorVoxels.UI {
    [RequireComponent(typeof(Canvas))]
    public class DebugUI : MonoBehaviour {
        [SerializeField] private Text _upperLeft;
        [SerializeField] private Text _upperRight;
        [SerializeField] private Transform _player;
        
        private Canvas _canvas;

        private void Awake() {
            _canvas = GetComponent<Canvas>();

            _upperRight.text = $"{SystemInfo.processorType.Trim()}\n" +
                               $"{SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceType}\n" +
                               $"{SystemInfo.operatingSystem}\n" +
                               $"Unity {Application.unityVersion}";
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.F3)) {
                _canvas.enabled = !_canvas.enabled;
            }

            if (!_canvas.enabled) {
                return;
            }

            var position = _player.position;
            var chunk = WorldManager.Instance.WorldToChunkPos(position);
            
            _upperLeft.text = $"{Application.productName} | {Application.version}\n" +
                              $"FPS: {1f / Time.deltaTime:n0}\n" +
                              $"W: {_player.transform.position}\n" +
                              $"C: {chunk}";
        }
    }
}