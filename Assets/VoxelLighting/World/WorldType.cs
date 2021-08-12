﻿using System;

namespace VoxelLighting.World {
    [Serializable]
    public enum WorldType {
        Flat,   // Completely flat, like Minecraft's super-flat.
        Basic   // Very basic perlin-based terrain generation.
    }
}