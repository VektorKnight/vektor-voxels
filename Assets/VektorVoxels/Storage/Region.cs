using System.Collections.Generic;
using VektorVoxels.World.Chunks;

namespace VektorVoxels.Storage {
    public sealed class Region {
        /// <summary>
        /// X/Y size of a region in chunks.
        /// </summary>
        public const int SIZE = 32;
        
        private readonly uint _id;
        private readonly Dictionary<uint, ChunkData> _chunks;
    }
}