using VektorVoxels.Chunks;

namespace VektorVoxels.Generation {
    public interface ITerrainGenerator {
        /// <summary>
        /// Populates a given chunk with voxel data.
        /// The chunk is expected to be empty before calling this.
        /// This function is expected to be thread-safe as generation is executed on the pool.
        /// </summary>
        void ProcessChunk(in Chunk chunk);
    }
}