using System;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VektorVoxels.Lighting {
    /// <summary>
    /// Voxel light color packed into RGB565 format (16-bit).
    /// R: 5 bits (32 levels), G: 6 bits (64 levels), B: 5 bits (32 levels).
    /// API uses 0-255 range for compatibility, values are quantized on storage.
    ///
    /// Supports Minecraft-style light propagation with ~15 step attenuation:
    /// - Attenuation multiplier 220/256 ≈ 0.859x per step
    /// - From 255 to threshold (16) takes approximately 15 steps
    /// - Tinting support for colored translucent blocks
    /// </summary>
    public readonly struct VoxelColor : IFormattable {
        private readonly ushort _packed;

        /// <summary>
        /// Attenuation multiplier (220/256 ≈ 0.859).
        /// This gives ~15 propagation steps from max (255) to threshold (16).
        /// </summary>
        public const int ATTENUATION_MULTIPLIER = 220;

        /// <summary>
        /// Light values at or below this threshold stop propagating.
        /// </summary>
        public const int LIGHT_THRESHOLD = 16;

        /// <summary>
        /// Maximum light value (full brightness).
        /// </summary>
        public const int MAX_LIGHT = 255;

        /// <summary>
        /// Pre-computed attenuation lookup table.
        /// AttenuationLUT[i] = (i * 220) >> 8
        /// Avoids multiplication during propagation.
        /// </summary>
        private static readonly byte[] AttenuationLUT;

        /// <summary>
        /// Static constructor to initialize the attenuation LUT.
        /// </summary>
        static VoxelColor() {
            AttenuationLUT = new byte[256];
            for (int i = 0; i < 256; i++) {
                AttenuationLUT[i] = (byte)((i * ATTENUATION_MULTIPLIER) >> 8);
            }
        }

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

        /// <summary>
        /// Returns true if all channels are at or below the propagation threshold.
        /// </summary>
        public bool IsBelowThreshold {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                Decompose(out var r, out var g, out var b);
                return r <= LIGHT_THRESHOLD && g <= LIGHT_THRESHOLD && b <= LIGHT_THRESHOLD;
            }
        }

        /// <summary>
        /// Returns true if all channels are zero (no light).
        /// </summary>
        public bool IsBlack {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _packed == 0;
        }

        // Shortcut values.
        public static VoxelColor WHITE => new VoxelColor(255, 255, 255);
        public static VoxelColor BLACK => new VoxelColor(0, 0, 0);
        public static VoxelColor RED =>   new VoxelColor(255, 0, 0);
        public static VoxelColor GREEN => new VoxelColor(0, 255, 0);
        public static VoxelColor BLUE =>  new VoxelColor(0, 0, 255);

        public static VoxelColor White()    => WHITE;
        public static VoxelColor Black()    => BLACK;
        public static VoxelColor Red()      => RED;
        public static VoxelColor Green()    => GREEN;
        public static VoxelColor Blue()     => BLUE;

        /// <summary>
        /// Construct a VoxelColor from individual components.
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
        /// Construct a VoxelColor from a raw packed value.
        /// </summary>
        private VoxelColor(ushort packed) {
            _packed = packed;
        }

        /// <summary>
        /// Decomposes the color into its individual components (0-255 range).
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Decompose(out int r, out int g, out int b) {
            var r5 = _packed & 0x1F;
            var g6 = (_packed >> 5) & 0x3F;
            var b5 = (_packed >> 11) & 0x1F;

            r = (r5 << 3) | (r5 >> 2);
            g = (g6 << 2) | (g6 >> 4);
            b = (b5 << 3) | (b5 >> 2);
        }

        /// <summary>
        /// Decomposes the color into its individual components (0-255 range).
        /// Alpha is always returned as 255 for API compatibility.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Decompose(out int r, out int g, out int b, out int a) {
            Decompose(out r, out g, out b);
            a = 255;
        }

        /// <summary>
        /// Performs a component-wise Max between two colors and returns the result.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VoxelColor Max(in VoxelColor a, in VoxelColor b) {
            a.Decompose(out var r0, out var g0, out var b0);
            b.Decompose(out var r1, out var g1, out var b1);

            return new VoxelColor(
                r0 > r1 ? r0 : r1,
                g0 > g1 ? g0 : g1,
                b0 > b1 ? b0 : b1
            );
        }

        /// <summary>
        /// Compares each component of this voxel color to another.
        /// </summary>
        /// <returns>True if any channel is greater than the corresponding channel in other.</returns>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AnyChannelGreater(in VoxelColor other) {
            Decompose(out var r0, out var g0, out var b0);
            other.Decompose(out var r1, out var g1, out var b1);

            return r0 > r1 || g0 > g1 || b0 > b1;
        }

        /// <summary>
        /// Returns true if all channels of this color are >= the corresponding channels of other.
        /// Useful for checking if a light source is dominated by another.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool DominatesOrEquals(in VoxelColor other) {
            Decompose(out var r0, out var g0, out var b0);
            other.Decompose(out var r1, out var g1, out var b1);

            return r0 >= r1 && g0 >= g1 && b0 >= b1;
        }

        /// <summary>
        /// Attenuates the light by one propagation step using the LUT.
        /// After ~15 steps, light decays from 255 to below threshold.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VoxelColor Attenuate() {
            Decompose(out var r, out var g, out var b);
            return new VoxelColor(
                AttenuationLUT[r],
                AttenuationLUT[g],
                AttenuationLUT[b]
            );
        }

        /// <summary>
        /// Attenuates light and returns the component values directly.
        /// Avoids allocating a new VoxelColor when only values are needed.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AttenuateTo(out int r, out int g, out int b) {
            Decompose(out var sr, out var sg, out var sb);
            r = AttenuationLUT[sr];
            g = AttenuationLUT[sg];
            b = AttenuationLUT[sb];
        }

        /// <summary>
        /// Tints light by multiplying with another color (e.g., colored glass).
        /// Tint color of 255 = full pass-through, 0 = full block.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VoxelColor Tint(in VoxelColor tintColor) {
            Decompose(out var r, out var g, out var b);
            tintColor.Decompose(out var tr, out var tg, out var tb);

            return new VoxelColor(
                (r * tr) >> 8,
                (g * tg) >> 8,
                (b * tb) >> 8
            );
        }

        /// <summary>
        /// Attenuates and tints light in a single operation.
        /// More efficient than calling Attenuate() then Tint() separately.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VoxelColor AttenuateAndTint(in VoxelColor tintColor) {
            Decompose(out var r, out var g, out var b);
            tintColor.Decompose(out var tr, out var tg, out var tb);

            return new VoxelColor(
                (AttenuationLUT[r] * tr) >> 8,
                (AttenuationLUT[g] * tg) >> 8,
                (AttenuationLUT[b] * tb) >> 8
            );
        }

        /// <summary>
        /// Attenuates and tints light, returning component values directly.
        /// Avoids allocating a new VoxelColor when only values are needed.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AttenuateAndTintTo(in VoxelColor tintColor, out int r, out int g, out int b) {
            Decompose(out var sr, out var sg, out var sb);
            tintColor.Decompose(out var tr, out var tg, out var tb);

            r = (AttenuationLUT[sr] * tr) >> 8;
            g = (AttenuationLUT[sg] * tg) >> 8;
            b = (AttenuationLUT[sb] * tb) >> 8;
        }

        /// <summary>
        /// Returns the sum of all channels. Useful for quick zero-check.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ChannelSum() {
            Decompose(out var r, out var g, out var b);
            return r + g + b;
        }

        /// <summary>
        /// Converts this VoxelColor to a normal Unity Color.
        /// </summary>
        [Pure]
        public Color ToColor() {
            Decompose(out var r, out var g, out var b);
            return new Color((float)r / 255, (float)g / 255, (float)b / 255, 1f);
        }

        /// <summary>
        /// Converts this VoxelColor to a Unity Color32.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color32 ToColor32() {
            Decompose(out var r, out var g, out var b);
            return new Color32((byte)r, (byte)g, (byte)b, 255);
        }

        /// <summary>
        /// Creates a VoxelColor from a regular Color.
        /// </summary>
        public static VoxelColor FromColor(Color c) {
            var r = (int)(c.r * 255);
            var g = (int)(c.g * 255);
            var b = (int)(c.b * 255);

            return new VoxelColor(r, g, b);
        }

        /// <summary>
        /// Legacy method - use AnyChannelGreater instead.
        /// </summary>
        [Obsolete("Use AnyChannelGreater for clarity")]
        public bool Compare(VoxelColor other) => AnyChannelGreater(other);

        public string ToString(string format, IFormatProvider formatProvider) {
            Decompose(out var r, out var g, out var b);
            return $"R: {r}, G: {g}, B: {b}";
        }

        public override string ToString() {
            Decompose(out var r, out var g, out var b);
            return $"VoxelColor(R: {r}, G: {g}, B: {b})";
        }
    }
}
