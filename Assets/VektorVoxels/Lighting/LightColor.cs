using System;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Voxel light color with 8-bit per channel precision (0-255 range).
    /// Provides smoother light gradients compared to 4-bit Color16.
    /// Packed into 32-bit uint for memory efficiency.
    /// </summary>
    public readonly struct LightColor : IFormattable {
        private readonly uint _packed;

        // Individual channel properties.
        public int R => (int)(_packed & 0xFF);
        public int G => (int)((_packed >> 8) & 0xFF);
        public int B => (int)((_packed >> 16) & 0xFF);
        public int A => (int)((_packed >> 24) & 0xFF);

        // Shortcut values.
        public static LightColor Clear() => new LightColor(0, 0, 0, 0);
        public static LightColor White() => new LightColor(255, 255, 255, 255);
        public static LightColor Black() => new LightColor(0, 0, 0, 255);
        public static LightColor Red() => new LightColor(255, 0, 0, 255);
        public static LightColor Green() => new LightColor(0, 255, 0, 255);
        public static LightColor Blue() => new LightColor(0, 0, 255, 255);

        /// <summary>
        /// Construct a LightColor from individual components.
        /// Values are clamped to [0, 255].
        /// </summary>
        public LightColor(int r, int g, int b, int a) {
            var packed =
                ((uint)(r & 0xFF)) |
                ((uint)(g & 0xFF) << 8) |
                ((uint)(b & 0xFF) << 16) |
                ((uint)(a & 0xFF) << 24);

            _packed = packed;
        }

        /// <summary>
        /// Decomposes the color into its individual components.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Decompose(out int r, out int g, out int b, out int a) {
            r = (int)(_packed & 0xFF);
            g = (int)((_packed >> 8) & 0xFF);
            b = (int)((_packed >> 16) & 0xFF);
            a = (int)((_packed >> 24) & 0xFF);
        }

        /// <summary>
        /// Performs a component-wise Max between two colors and returns the result.
        /// </summary>
        public static LightColor Max(in LightColor a, in LightColor b) {
            a.Decompose(out var r0, out var g0, out var b0, out var a0);
            b.Decompose(out var r1, out var g1, out var b1, out var a1);

            return new LightColor(
                r0 > r1 ? r0 : r1,
                g0 > g1 ? g0 : g1,
                b0 > b1 ? b0 : b1,
                a0 > a1 ? a0 : a1
            );
        }

        /// <summary>
        /// Converts this LightColor to a normal Unity Color.
        /// </summary>
        [Pure]
        public Color ToColor() {
            Decompose(out var r, out var g, out var b, out var a);
            return new Color((float)r / 255, (float)g / 255, (float)b / 255, (float)a / 255);
        }

        /// <summary>
        /// Converts this LightColor to a Unity Color32.
        /// Direct byte conversion (no scaling needed).
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color32 ToColor32() {
            Decompose(out var r, out var g, out var b, out var a);
            return new Color32((byte)r, (byte)g, (byte)b, (byte)a);
        }

        /// <summary>
        /// Converts this LightColor to an attenuation value.
        /// Useful for converting a tint color for user-friendliness.
        /// </summary>
        public LightColor ToAttenuation() {
            Decompose(out var r, out var g, out var b, out var a);
            return new LightColor(255 - r, 255 - g, 255 - b, a);
        }

        /// <summary>
        /// Creates a LightColor from a regular Color.
        /// </summary>
        public static LightColor FromColor(Color c) {
            var r = (int)(c.r * 255);
            var g = (int)(c.g * 255);
            var b = (int)(c.b * 255);
            var a = (int)(c.a * 255);

            return new LightColor(r, g, b, a);
        }

        /// <summary>
        /// Creates a LightColor from a Color16 (upscales 4-bit to 8-bit).
        /// </summary>
        public static LightColor FromColor16(Color16 c) {
            c.Decompose(out var r, out var g, out var b, out var a);
            // Scale 0-15 to 0-255 by multiplying by 17
            return new LightColor(r * 17, g * 17, b * 17, a * 17);
        }

        public string ToString(string format, IFormatProvider formatProvider) {
            Decompose(out var r, out var g, out var b, out var a);
            return $"R: {r}, G: {g}, B: {b}, A: {a}";
        }
    }
}
