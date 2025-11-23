using System;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Voxel light color packed into RGB565 format (16-bit).
    /// R: 5 bits (32 levels), G: 6 bits (64 levels), B: 5 bits (32 levels).
    /// API uses 0-255 range for compatibility, values are quantized on storage.
    /// </summary>
    public readonly struct VoxelColor : IFormattable {
        private readonly ushort _packed;

        // Individual channel properties (returned as 0-255 for API compatibility).
        public int R {
            get {
                var r5 = _packed & 0x1F;
                return (r5 << 3) | (r5 >> 2); // Expand 5-bit to 8-bit
            }
        }

        public int G {
            get {
                var g6 = (_packed >> 5) & 0x3F;
                return (g6 << 2) | (g6 >> 4); // Expand 6-bit to 8-bit
            }
        }

        public int B {
            get {
                var b5 = (_packed >> 11) & 0x1F;
                return (b5 << 3) | (b5 >> 2); // Expand 5-bit to 8-bit
            }
        }

        // Shortcut values.
        public static VoxelColor Clear() => new VoxelColor(0, 0, 0);
        public static VoxelColor White() => new VoxelColor(255, 255, 255);
        public static VoxelColor Black() => new VoxelColor(0, 0, 0);
        public static VoxelColor Red() => new VoxelColor(255, 0, 0);
        public static VoxelColor Green() => new VoxelColor(0, 255, 0);
        public static VoxelColor Blue() => new VoxelColor(0, 0, 255);

        /// <summary>
        /// Construct a LightColor from individual components.
        /// Values should be in [0, 255] range, will be quantized to RGB565.
        /// </summary>
        public VoxelColor(int r, int g, int b) {
            // Quantize 8-bit to 5/6-bit
            var r5 = (r >> 3) & 0x1F;
            var g6 = (g >> 2) & 0x3F;
            var b5 = (b >> 3) & 0x1F;

            _packed = (ushort)(r5 | (g6 << 5) | (b5 << 11));
        }

        /// <summary>
        /// Construct a LightColor from individual components.
        /// Alpha is ignored (kept for API compatibility).
        /// </summary>
        public VoxelColor(int r, int g, int b, int a) : this(r, g, b) { }

        /// <summary>
        /// Decomposes the color into its individual components (0-255 range).
        /// Alpha is always returned as 255.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Decompose(out int r, out int g, out int b, out int a) {
            var r5 = _packed & 0x1F;
            var g6 = (_packed >> 5) & 0x3F;
            var b5 = (_packed >> 11) & 0x1F;

            r = (r5 << 3) | (r5 >> 2);
            g = (g6 << 2) | (g6 >> 4);
            b = (b5 << 3) | (b5 >> 2);
            a = 255;
        }

        /// <summary>
        /// Performs a component-wise Max between two colors and returns the result.
        /// </summary>
        public static VoxelColor Max(in VoxelColor a, in VoxelColor b) {
            a.Decompose(out var r0, out var g0, out var b0, out _);
            b.Decompose(out var r1, out var g1, out var b1, out _);

            return new VoxelColor(
                r0 > r1 ? r0 : r1,
                g0 > g1 ? g0 : g1,
                b0 > b1 ? b0 : b1
            );
        }

        /// <summary>
        /// Converts this LightColor to a normal Unity Color.
        /// </summary>
        [Pure]
        public Color ToColor() {
            Decompose(out var r, out var g, out var b, out _);
            return new Color((float)r / 255, (float)g / 255, (float)b / 255, 1f);
        }

        /// <summary>
        /// Converts this LightColor to a Unity Color32.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color32 ToColor32() {
            Decompose(out var r, out var g, out var b, out _);
            return new Color32((byte)r, (byte)g, (byte)b, 255);
        }

        /// <summary>
        /// Converts this LightColor to an attenuation value.
        /// Deprecated: ColorData now stores pass-through multiplier directly.
        /// </summary>
        [System.Obsolete("ColorData now stores pass-through multiplier directly. No inversion needed.")]
        public VoxelColor ToAttenuation() {
            Decompose(out var r, out var g, out var b, out _);
            return new VoxelColor(255 - r, 255 - g, 255 - b);
        }

        /// <summary>
        /// Creates a LightColor from a regular Color.
        /// </summary>
        public static VoxelColor FromColor(Color c) {
            var r = (int)(c.r * 255);
            var g = (int)(c.g * 255);
            var b = (int)(c.b * 255);

            return new VoxelColor(r, g, b);
        }

        /// <summary>
        /// Creates a LightColor from a Color16 (upscales 4-bit to 8-bit).
        /// Deprecated: Color16 is obsolete, use LightColor directly.
        /// </summary>
        [System.Obsolete("Color16 is deprecated. Use LightColor directly.")]
        public static VoxelColor FromColor16(Color16 c) {
            c.Decompose(out var r, out var g, out var b, out _);
            // Scale 0-15 to 0-255 by multiplying by 17
            return new VoxelColor(r * 17, g * 17, b * 17);
        }

        public string ToString(string format, IFormatProvider formatProvider) {
            Decompose(out var r, out var g, out var b, out _);
            return $"R: {r}, G: {g}, B: {b}";
        }
    }
}
