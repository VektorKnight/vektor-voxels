using System;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Deprecated: Use LightColor instead.
    /// 4-bit per channel RGBA color (16 levels per channel).
    /// </summary>
    [System.Obsolete("Use LightColor (RGB565) instead for better precision with same memory footprint.")]
    public readonly struct Color16 : IFormattable {
        private readonly ushort _packed;
        
        // Individual channel props.
        public int R => _packed & 0xF;
        public int G => (_packed >> 4) & 0xF;
        public int B => (_packed >> 8) & 0xF;
        public int A => (_packed >> 12) & 0xF;
        
        // Shortcut values.
        public static Color16 Clear() => new Color16(0, 0, 0, 0);
        public static Color16 White() => new Color16(15, 15, 15, 15);
        public static Color16 Black() => new Color16(0, 0, 0, 15);
        public static Color16 Red() => new Color16(15, 0, 0, 15);
        public static Color16 Green() => new Color16(0, 15, 0, 15);
        public static Color16 Blue() => new Color16(0, 0, 15, 15);
        
        /// <summary>
        /// Construct a Color16 from individual components.
        /// Only positive values between [0, 15] are supported.
        /// </summary>
        public Color16 (int r, int g, int b, int a) {
            var packed =
                (r & 0xF) |
                ((g & 0xF) << 4) |
                ((b & 0xF) << 8) |
                ((a & 0xF) << 12);

            _packed = (ushort)packed;
        }
        
        /// <summary>
        /// Decomposes the color into its individual components.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Decompose(out int r, out int g, out int b, out int a) {
            r = _packed & 0xF;
            g = (_packed >> 4) & 0xF;
            b = (_packed >> 8) & 0xF;
            a = (_packed >> 12) & 0xF;
        }

        /// <summary>
        /// Performs a component-wise Max between two colors and returns the result.
        /// </summary>
        public static Color16 Max(in Color16 a, in Color16 b) {
            a.Decompose(out var r0, out var g0, out var b0, out var a0);
            b.Decompose(out var r1, out var g1, out var b1, out var a1);
            
            return new Color16(
                r0 > r1 ? r0 : r1,
                g0 > g1 ? g0 : g1,
                b0 > b1 ? b0 : b1,
                a0 > a1 ? a0 : a1
            );
        }

        /// <summary>
        /// Converts this Color16 to a normal Unity Color.
        /// </summary>
        [Pure]
        public Color ToColor() {
            Decompose(out var r, out var g, out var b, out var a);
            return new Color((float)r / 15, (float)g / 15, (float)b / 15, (float)a / 15);
        }
        
        /// <summary>
        /// Converts this Color16 to a Unity Color32 or RGBA8888.
        /// </summary>
        [Pure]
        public Color32 ToColor32() {
            Decompose(out var r, out var g, out var b, out var a);
            
            // Converting to a 8-bit color just involves multiplying by 17 or (255 / 15).
            return new Color32((byte)(r * 17), (byte)(g * 17), (byte)(b * 17), (byte)(a * 17));
        }
        
        /// <summary>
        /// Converts this Color16 to an attenuation value.
        /// Useful for converting a tint color for user-friendliness.
        /// </summary>
        public Color16 ToAttenuation() {
            Decompose(out var r, out var g, out var b, out var a);
            return new Color16(15 - r, 15 - g, 15 - b, a);
        }

        /// <summary>
        /// Creates a Color16 from a regular Color.
        /// </summary>
        public static Color16 FromColor(Color c) {
            var r = (int)(c.r * 15);
            var g = (int)(c.g * 15);
            var b = (int)(c.b * 15);
            var a = (int)(c.a * 15);

            return new Color16(r, g, b, a);
        }

        public string ToString(string format, IFormatProvider formatProvider) {
            Decompose(out var r, out var g, out var b, out var a);
            return $"R: {r}, G: {g}, B: {b}, A: {a}";
        }
    }
}