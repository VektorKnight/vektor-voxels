# Save System Plan

## Overview

Implement world persistence with automatic saving, ID remapping for voxel database changes, and RLE compression.

## Requirements

- Save/load worlds from disk
- Resolve voxel database changes via ID→InternalName mapping
- Only save critical voxel state (lighting regenerated on load)
- RLE compression to minimize disk space
- Automatic interval-based saving of modified chunks

## Data Format

### Per-Voxel Storage

3 bytes per voxel:
- `Id` (ushort, 2 bytes)
- `Orientation` (byte, 1 byte)

Flags restored from VoxelDefinition at load time.
ColorData restored from VoxelDefinition at load time.

Uncompressed: 16×256×16 × 3 = 192KB per chunk

### RLE Compression

Encode Y-columns with run-length encoding:
```
[Id:2][Orientation:1][Count:1] = 4 bytes per run
```

Expected compressed size: <20KB per typical terrain chunk.

### File Structure

```
worlds/
  {world-name}/
    world.json          # World metadata + ID mapping
    chunks/
      chunk_{x}_{z}.bin # Individual compressed chunk files
```

### world.json Schema

```json
{
  "name": "World Name",
  "seed": 12345,
  "createdAt": "2025-11-22T10:00:00Z",
  "lastSaved": "2025-11-22T12:30:00Z",
  "voxelMapping": {
    "1": "stone",
    "2": "dirt",
    "3": "grass",
    "4": "bedrock"
  }
}
```

### Chunk Binary Format

```
Header:
  [Version:1]           # Format version for future compatibility
  [ChunkX:4][ChunkZ:4]  # Chunk coordinates (int32)

Data (per Y-column, 16×16 = 256 columns):
  [RunCount:1]          # Number of runs in this column
  [Run0...RunN]         # RLE-encoded runs bottom to top

Run:
  [Id:2][Orientation:1][Count:1]
```

## Implementation

### Core Classes

1. **WorldSaveData** - Serializable world.json structure
2. **ChunkSerializer** - RLE encode/decode chunk data
3. **WorldPersistence** - Coordinates saving/loading, manages file I/O
4. **VoxelIdRemapper** - Builds saved→current ID mapping from InternalNames

### Chunk State Machine Modification

Current: `Uninitialized → TerrainGeneration → Lighting → Meshing → Ready`

Modified: `Uninitialized → DataLoad → Lighting → Meshing → Ready`

Where `DataLoad` either:
- Loads from disk (saved world)
- Runs terrain generation (new world)

### Dirty Tracking

Add to `Chunk`:
```csharp
private bool _isDirty;
public bool IsDirty => _isDirty;
public void MarkDirty() => _isDirty = true;
public void ClearDirty() => _isDirty = false;
```

Set `_isDirty = true` on any voxel modification. Clear after successful save.

### Auto-Save System

- Configurable interval (default 30-60 seconds)
- Only saves chunks where `IsDirty == true`
- Async via `GlobalThreadPool` to avoid main thread blocking
- Queue dirty chunks and process in batches

### Load Flow

1. Parse world.json
2. Build ID remap dictionary: `Dictionary<ushort, ushort>`
   - For each saved ID, lookup InternalName
   - Find current ID for that InternalName
   - If missing, abort with error listing missing voxels
3. Load chunk files on demand (same as current chunk loading)
4. Apply ID remapping during deserialization
5. Proceed to lighting pass

### Error Handling

Missing voxels on load:
```
Error: Cannot load world "My World"
Missing voxel definitions:
  - "copper_ore" (saved ID: 15)
  - "crystal_block" (saved ID: 23)
```

## Future Considerations

- **Per-voxel ColorData**: If per-instance tinting is added, include `HasCustomColor` flag and conditionally save ColorData
- **Region files**: If chunk count grows significantly, consider packing chunks into region files (32×32 chunks per region)
- **Delta saves**: Only save changed columns within a chunk for faster incremental saves

## UI (IMGUI)

After infrastructure complete:
- World list with load/delete options
- New world creation with name/seed input
- Save indicator showing dirty chunk count
- Manual save button
