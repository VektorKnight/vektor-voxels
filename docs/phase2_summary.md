# Phase 2: 8-Bit Lighting Upgrade - Learning Summary

## What Was Done

Upgraded the voxel lighting system from 4-bit per channel (0-15 intensity) to 8-bit per channel (0-255 intensity) for smoother light gradients and better visual quality.

### Files Modified

1. **Created:** `Assets/VektorVoxels/Lighting/LightColor.cs`
   - New 32-bit packed struct replacing Color16 for lighting
   - 8 bits per RGBA channel
   - Same API as Color16 with updated value ranges

2. **Modified:** `Assets/VektorVoxels/Lighting/LightNode.cs`
   - Changed `Color16 Value` to `LightColor Value`

3. **Modified:** `Assets/VektorVoxels/Lighting/LightData.cs`
   - Changed sun/block light from `Color16` to `LightColor`

4. **Modified:** `Assets/VektorVoxels/Chunks/Chunk.cs`
   - Changed `_sunLight` and `_blockLight` arrays from `Color16[]` to `LightColor[]`

5. **Modified:** `Assets/VektorVoxels/Lighting/LightMapper.cs`
   - Updated all light values from 0-15 to 0-255 range
   - Changed decrement from 1 to 17 per voxel
   - Changed threshold from <=1 to <=16
   - Updated attenuation scaling (multiply Color16 attenuation by 17)

6. **Modified:** `Assets/VektorVoxels/Meshing/MeshUtility.cs`
   - Updated `CalculateVertexLight` to use `LightColor`
   - Removed *17 scaling (no longer needed with 8-bit values)

7. **Modified:** `Assets/VektorVoxels/Meshing/VisualMesher.cs`
   - Updated default light values to use `LightColor`

## Why This Was Done

### Problem with 4-Bit Lighting

- Only 16 discrete intensity levels (0-15)
- Visible banding in smooth lighting gradients
- Noticeable "steps" in light falloff
- Limited precision for colored lights

### Benefits of 8-Bit Lighting

- 256 intensity levels per channel
- Smoother gradients in ambient occlusion
- Better color mixing for colored block lights
- More precise light falloff calculations
- Direct byte conversion to Color32 (no scaling needed)

## How It Works

### Scaling Strategy

To maintain similar propagation distance (~15 voxels) while using 8-bit values:

- **Old:** Light decrements by 1 per voxel (15 steps from max)
- **New:** Light decrements by 17 per voxel (255/15 ≈ 17)

This preserves the same visual falloff distance while providing 17x more precision within each step.

### Memory Impact

| Array Type | Before (4-bit) | After (8-bit) | Change |
|------------|----------------|---------------|--------|
| `_sunLight` per chunk | 131 KB | 262 KB | +131 KB |
| `_blockLight` per chunk | 131 KB | 262 KB | +131 KB |
| Total per chunk | 262 KB | 524 KB | +262 KB |
| 4096 chunks | 1.05 GB | 2.1 GB | +1.05 GB |

The memory increase is acceptable for the visual quality improvement.

### Attenuation Handling

Voxel `ColorData` (for attenuation/tint) remains as `Color16` (4-bit) since it defines block properties, not dynamic lighting. When applied during propagation, it's scaled by 17 to match 8-bit light values.

### Vertex Color Conversion

**Before:**
```csharp
// Average 4-bit values, then scale to 8-bit
var r = (c0r + c1r + c2r + c3r) >> 2;
return new Color32((byte)(r * 17), ...);
```

**After:**
```csharp
// Average 8-bit values directly
var r = (c0r + c1r + c2r + c3r) >> 2;
return new Color32((byte)r, ...);
```

## Key Decisions

1. **Decrement value of 17**: Chosen because 255/15 ≈ 17. This maintains backward compatibility with existing terrain generation that expects ~15 voxel propagation distance.

2. **Threshold of 16**: Stopping propagation at <=16 (not <=0) prevents unnecessary iterations for imperceptible light values.

3. **Keep Color16 for voxel data**: Voxel ColorData defines static properties and doesn't need 8-bit precision. Keeping it as Color16 saves memory in voxel arrays.

4. **FromColor16 conversion**: Block light sources still use 4-bit ColorData, so we upscale via `LightColor.FromColor16()` when initializing propagation.

## Testing Notes

After this change, the visual appearance should be:
- **Similar** overall brightness and falloff distance
- **Smoother** gradients in ambient occlusion
- **Better** color transitions for colored lights
- **No banding** in smooth lighting areas

If lighting appears different (too bright/dark), the decrement value (17) or threshold (16) may need tuning.

## Future Improvements

1. **Adjustable decrement**: Could expose the decrement value as a configurable parameter for different aesthetic styles (softer/harder falloff).

2. **Dynamic colored lights**: The increased precision enables better support for dynamic colored lights (torches, lamps, etc.).

3. **HDR support**: 8-bit provides a foundation for future HDR bloom effects where very bright lights can overflow and create glow.

## Related Files

- `Color16.cs` - Original 4-bit struct (still used for voxel ColorData)
- `LightColor.cs` - New 8-bit struct for lighting
- `architecture.md` - Updated with 8-bit lighting documentation
