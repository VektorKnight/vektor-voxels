using System;
using System.Diagnostics;
using AudioTerrain;
using UnityEngine;
using UnityEngine.UI;
using VektorVoxels.Debugging;
using VektorVoxels.World;
using Debug = UnityEngine.Debug;

namespace VektorVoxels.UI {
    [RequireComponent(typeof(Canvas))]
    public class DebugUI : MonoBehaviour {
        [SerializeField] private Text _upperLeft;
        [SerializeField] private Text _upperRight;
        [SerializeField] private RawImage _image;
        [SerializeField] private Transform _player;
        [SerializeField] private Vector2Int _profilerSize = new Vector2Int(256, 64);
        
        private Canvas _canvas;
        
        // profiler
        private PixelProfiler _profiler;

        private void Awake() {
            _canvas = GetComponent<Canvas>();

            _upperRight.text = $"{SystemInfo.processorType.Trim()}\n" +
                               //$"{SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceType}\n" +
                               $"{SystemInfo.operatingSystem}\n" +
                               $"Unity {Application.unityVersion}";

            _profiler = new PixelProfiler(_profilerSize);
            
            _image.texture = _profiler.Texture;
            _image.rectTransform.sizeDelta = new Vector2(_profilerSize.x, _profilerSize.y) * 2;
        }

        private void Update() {
            //if (Input.GetKeyDown(KeyCode.F3)) {
                //_canvas.enabled = !_canvas.enabled;
            //}
            
            _profiler.PushFrameTime(Time.deltaTime);

            if (!_canvas.enabled) {
                return;
            }

            var position = _player.position;
            var chunk = VoxelWorld.Instance.WorldToChunkPos(position);
            
            _upperLeft.text = $"{Application.productName} | {Application.version}\n" +
                              $"FPS: {1f / Time.deltaTime:n0}\n" +
                              $"Chunks/Tick: {VoxelWorld.Instance.ChunksPerTick}\n" +
                              $"View: {VoxelWorld.Instance.ViewDistance}\n" +
                              $"World: {_player.transform.position}\n" +
                              $"Chunk: {chunk}";
        }
    }
}