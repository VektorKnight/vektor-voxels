using System;
using System.Diagnostics;
using AudioTerrain;
using UnityEngine;
using UnityEngine.UI;
using VektorVoxels.World;

namespace VektorVoxels.UI {
    [RequireComponent(typeof(Canvas))]
    public class DebugUI : MonoBehaviour {
        [SerializeField] private Text _upperLeft;
        [SerializeField] private Text _upperRight;
        [SerializeField] private RawImage _image;
        [SerializeField] private Transform _player;
        [SerializeField] private Vector3Int _profilerSize = new Vector3Int(256, 64, 2);
        
        private Canvas _canvas;
        
        // profiler
        private float _maxTime = 1f / 30;
        private FloatRingBuffer _frameTimes;
        private float[] _frameData;
        private Color32[] _frameColors;
        private Texture2D _profilerTexture;

        private void Awake() {
            _canvas = GetComponent<Canvas>();

            _upperRight.text = $"{SystemInfo.processorType.Trim()}\n" +
                               $"{SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceType}\n" +
                               $"{SystemInfo.operatingSystem}\n" +
                               $"Unity {Application.unityVersion}";

            _frameTimes = new FloatRingBuffer(_profilerSize.x);
            _frameData = new float[_profilerSize.x];
            _frameColors = new Color32[_profilerSize.x * _profilerSize.y];
            _profilerTexture = new Texture2D(_profilerSize.x, _profilerSize.y, TextureFormat.ARGB32, false) {
                filterMode = FilterMode.Point
            };
            
            _image.texture = _profilerTexture;
            _image.rectTransform.sizeDelta = new Vector2(_profilerSize.x, _profilerSize.y) * _profilerSize.z;
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.F3)) {
                _canvas.enabled = !_canvas.enabled;
            }
            
            _frameTimes.Push(Time.deltaTime);

            if (!_canvas.enabled) {
                return;
            }
            
            _frameTimes.ExportData(_frameData);
            
            // Generate the texture.
            for (var x = 0; x < _profilerSize.x; x++) {
                var yMax = Mathf.RoundToInt((_frameData[x] / _maxTime) * _profilerSize.y);
                for (var y = 0; y < _profilerSize.y; y++) {
                    _frameColors[x + y * _profilerSize.x] = y < yMax ? new Color32(255, 255, 0, 255) : new Color32(0, 0, 0, 128);
                }
            }

            _profilerTexture.SetPixels32(_frameColors);
            _profilerTexture.Apply();

            var position = _player.position;
            var chunk = WorldManager.Instance.WorldToChunkPos(position);
            
            _upperLeft.text = $"{Application.productName} | {Application.version}\n" +
                              $"FPS: {1f / Time.deltaTime:n0}\n" +
                              $"W: {_player.transform.position}\n" +
                              $"C: {chunk}";
        }
    }
}