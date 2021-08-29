using AudioTerrain;
using UnityEngine;

namespace VektorVoxels.Debugging {
    /// <summary>
    /// Generates a low-res frame profiler like Minecraft.
    /// </summary>
    public sealed class PixelProfiler {
        /// <summary>
        /// Since texture data is updated directly, the layout is ARGB.
        /// </summary>
        private static readonly Color32[] _defaultColors = {
            new Color32(0, 255, 64, 255),
            new Color32(255, 128, 0, 255),
            new Color32(255, 63, 0, 255)
        };
        
        private Vector2Int _profilerSize;
        private FloatRingBuffer _frameTimes;
        private float[] _frameData;
        private float _maxTime;

        private Color32[] _colors;

        private Texture2D _profilerTexture;

        public Vector2Int Size => _profilerSize;
        public Texture2D Texture => _profilerTexture;

        public PixelProfiler(Vector2Int profilerSize, float maxTime = 1f / 15, Color32[] colors = null) {
            _profilerSize = profilerSize;
            _maxTime = maxTime;

            _frameTimes = new FloatRingBuffer(profilerSize.x);
            _frameData = new float[profilerSize.x];

            _colors = colors ?? _defaultColors;
            
            _profilerTexture = new Texture2D(_profilerSize.x, _profilerSize.y, TextureFormat.RGBA32, false) {
                filterMode = FilterMode.Point
            };
        }

        public void PushFrameTime(float deltaTime) {
            _frameTimes.Push(deltaTime);
            _frameTimes.ExportData(_frameData);
            
            var pixels = _profilerTexture.GetRawTextureData<Color32>();
            for (var x = 0; x < _profilerSize.x; x++) {
                var p = _frameData[x] / _maxTime;
                var color = _colors[Mathf.FloorToInt(p * (_colors.Length - 1))];
                var yMax = Mathf.RoundToInt(p * _profilerSize.y);
                
                for (var y = 0; y < _profilerSize.y; y++) {
                    pixels[x + y * _profilerSize.x] = y < yMax ? color : new Color32(0, 0, 0, 191);
                }
            }
            _profilerTexture.Apply();
        }
    }
}