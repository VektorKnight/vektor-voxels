# Vektor Voxels

A cubic voxel framework for Unity focused on exploring efficient meshing, full RGB lighting, and concurrent processing. This isn't meant to be a Minecraft clone—it's an exploratory project diving into the algorithms and techniques that make voxel engines tick, with some of my own optimizations thrown in.

## Features

### Full RGB Lighting System
The lighting engine supports complete RGB color with both emission and tinting. Light propagates through translucent materials and blends naturally, giving you colored shadows and atmospheric effects. Sunlight floods down from the sky while block lights spread in all directions with proper falloff.

### Multithreaded Chunk Pipeline
Chunks progress through a state machine (terrain generation → lighting → meshing) with all heavy lifting done on background threads. A custom thread pool with async/await support keeps the main thread responsive while processing multiple chunks simultaneously.

### Optimized Cubic Meshing
The mesher generates efficient geometry with per-side textures, block orientations, and smooth lighting with ambient occlusion. Vertex data is tightly packed and lighting is baked directly into the mesh for fast rendering.

### Additional Highlights
- Procedural terrain generation with layered voxels (bedrock, stone, dirt, grass)
- Custom shaders integrating voxel lighting with Unity's rendering pipeline
- DDA-based voxel raycasting for accurate block placement and breaking
- Support for custom mesh shapes via the `ICustomMesh` interface

## Getting Started

1. Clone the repository to your local system
2. Open the project in Unity 2021 or newer (developed with Unity 6000.2.2f1)
3. Open `Scenes/SampleScene` and hit Play
4. Walk around with WASD, place blocks with right-click, break with left-click

## Project Status

This project has reached its goals and is no longer actively maintained. It successfully demonstrates:
- Cubic voxel meshing with per-side textures and smooth lighting
- Full RGB light propagation with emission and color tinting
- Concurrent chunk processing without blocking the main thread

If you're exploring voxel engine development or looking for reference implementations of these algorithms, feel free to dig through the code. The lighting precision issues mentioned below could largely be fixed by switching from 4-bit to 8-bit color channels—I just went with the more compact format to save memory.

## Known Limitations

- No save system—chunks stay in memory and world state isn't persisted
- The player controller is pretty rough around the edges
- PhysX can stutter when updating chunk collision meshes on some systems
- Some RGB color combinations fade out unevenly due to 4-bit precision

## Architecture Overview

The codebase is organized into namespaces under `VektorVoxels/`:
- **Chunks** - Chunk lifecycle, state management, and thread-safe data access
- **Generation** - Terrain generators implementing `ITerrainGenerator`
- **Lighting** - Sunlight and block light propagation with flood-fill
- **Meshing** - Visual and collision mesh generation
- **Threading** - Custom thread pool and job system with callbacks
- **Voxels** - Data structures and voxel definitions
- **VoxelPhysics** - DDA raycasting for voxel traces

See `CLAUDE.md` for deeper architectural documentation.

## Credits

Textures are from the Pixel Perfection texture pack by XSSheep.
[Pixel Perfection on Minecraft Forum](https://www.minecraftforum.net/forums/mapping-and-modding-java-edition/resource-packs/1242533-pixel-perfection-now-with-polar-bears-1-11)
