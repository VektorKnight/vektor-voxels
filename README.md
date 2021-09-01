# Vektor Voxels
A work-in-progress cubic voxel framework for the Unity game engine with a focus on concurrency and optimized meshing/lighting algorithms. The primary goal of this project is not to recreate Minecraft in Unity but to explore the algorithms involved with some additions/optimizations.

## Goals
 - Fast and efficient cubic voxel meshing with per-side textures, orientations, and custom meshes.
 - Fast and efficient voxel lighting with full RGB support with blending and tinting through translucent materials.
 - Generic job system with a custom thread-pool supporting callbacks and C# async/await.
 - Highly-concurrent architecture to keep the main thread free of heavy work by executing meshing/lighting on job threads.
 
## Getting Started
 1. Clone the project to your local system.
 2. Open the project in Unity 2021 or newer.
 3. Build some voxels with RGB lighting.

## Functional Features
 - World generation and chunk loading.
 - Multi-threaded job system to process chunk updates.
 - Voxel meshing algorithm with smooth lighting and per-side textures.
 - Full-color voxel lighting engine (some minor bugs with sunlight).
 - Custom shaders to integrate voxel lighting with Unity's rendering pipeline.
 - Basic player that can walk around and place/break voxels.

## Planned Features
 - Saving/loading of worlds.
 - Non-solid voxels like water, lava, etc.
 - Gravity-enabled voxels like sand/gravel.
 - Tile-entity system for functional blocks.

## Known Issues
 - Due to the lack of a saving system, chunks are never unloaded from memory.
 - The player controller is honestly just horrible.
 - PhysX really hates updates to static mesh geometry and can cause stutters during chunk loading on some systems.
 - Light colors that are a combination of RGB values may fade out strangely.
 
## Disclaimer
This project is still under heavy development and some systems are far from polished. If you choose to use this code in your own project, I recommend doing so as a reference only till the first release as things are likely to change considerably.

## Credits
Textures used are from the Pixel Perfection texture pack by XSSheep.
You can find the Minecraft Forum post here: 
[Pixel Perfection](https://www.minecraftforum.net/forums/mapping-and-modding-java-edition/resource-packs/1242533-pixel-perfection-now-with-polar-bears-1-11).
