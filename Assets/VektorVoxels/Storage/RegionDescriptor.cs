using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace VektorVoxels.Storage {
    [Serializable]
    public readonly struct RegionDescriptor {
        public readonly uint Id;
        public readonly KeyValuePair<uint, uint>[] ChunkOffsets;
        
        [JsonConstructor]
        public RegionDescriptor(uint id) : this() {
            Id = id;
            ChunkOffsets = new KeyValuePair<uint, uint>[32];
        }
    }
}