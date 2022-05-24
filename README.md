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

## Known Issues
 - Due to the lack of a saving system, chunks are never unloaded from memory.
 - The player controller is honestly just horrible.
 - PhysX really hates updates to static mesh geometry and can cause stutters during chunk loading on some systems.
 - Light colors that are a combination of RGB values may fade out strangely.
 
## Closing Thoughts
I've decided that this project is no longer worth maintaining as it satisfied most of the goals I initially set out to achieve. I've also determined that voxel-based games are just not viable in engines such as Unity/Unreal without significantly limiting scope and/or significant changes/workarounds within the engines themselves. If you're looking for examples on how to generate cubic voxel meshes with per-side textures, smooth lighting, and AO, this project should serve as a good example. As a bonus, the lighting system is full RGB. Most of the precision issues could be eliminated by just using regular 8-bit color instead of the 4-bit format I chose to save memory.

## Credits
Textures used are from the Pixel Perfection texture pack by XSSheep.
You can find the Minecraft Forum post here: 
[Pixel Perfection](https://www.minecraftforum.net/forums/mapping-and-modding-java-edition/resource-packs/1242533-pixel-perfection-now-with-polar-bears-1-11).
