# Vektor Voxels

Cubic voxel research project built in Unity to explore Minecraft-like voxel engines, multithreading, and RGB lighting. This should as a decent-enough reference for any curiosities around cubic voxel engines and how to approach them.
In order to keep up with the industry as well as learn some new principles, the most recent work to bring it to a solid stopping point was done with AI assistance.

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

## Getting Started

1. Clone the repository to your local system
2. Open the project in Unity 2021 or newer (developed with Unity 6000.2.2f1)
3. Open `Scenes/SampleScene` and hit Play
4. Walk around with WASD, place blocks with right-click, break with left-click

## Project Status

I have decided to continue development on this as time and interest allows. The goal is not to produce a production-worthy game that would by any means try to compete with Minecraft or potentially Hytale. The core of this project is still research and general passion. The secondary goal is a robust example of a voxel engine implemented in Unity.

Note that AI development tools are being used in this project. This is partially responsible for the project being revived at all. Proper consideration is being given around optimal usage and auditing work done by the agent. Degradations in code quality, architecture, or documentation are not expected.

## Known Limitations

- The world can be saved but is still limited in size as a robust virtual-memory, cache, and streaming system for chunks has yet to be implemented (pain and suffering).
- The player controller is very basic but functional.
- The extremely minimal voxel collision means normal Unity objects will not respond to the world.
  - This was necessary to get around PhysX stuttering when loading in new chunks rapidly.
  - May explore re-integration in the future or an alternative.
- Greedy meshing is implemented but does not function with smooth lighting and may be fundamentally incompatible without moving lighting to the GPU somehow.
- There is currently no way to customize settings of any kind including input bindings or mouse sensitivity.
  - Priority is somewhat low on this as the focus for now is solidifying the foundational systems.

## Architecture Overview

The codebase is organized into namespaces under `VektorVoxels/`:
- **Chunks** - Chunk lifecycle, state management, and thread-safe data access
- **Generation** - Terrain generators implementing `ITerrainGenerator`
- **Lighting** - Sunlight and block light propagation with flood-fill
- **Meshing** - Visual and collision mesh generation
- **Threading** - Custom thread pool and job system with callbacks
- **Voxels** - Data structures and voxel definitions
- **VoxelPhysics** - DDA raycasting for voxel traces

## Credits

Textures are from the Pixel Perfection texture pack by XSSheep.
[Pixel Perfection on Minecraft Forum](https://www.minecraftforum.net/forums/mapping-and-modding-java-edition/resource-packs/1242533-pixel-perfection-now-with-polar-bears-1-11)
