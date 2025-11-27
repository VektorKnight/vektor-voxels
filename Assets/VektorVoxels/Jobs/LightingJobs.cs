using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VektorVoxels.Chunks;
using VektorVoxels.Lighting;
using VektorVoxels.Voxels;

namespace VektorVoxels.Jobs {
    /// <summary>
    /// Burst-compatible light propagation node.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LightNodeNative {
        public int3 Position;
        public VoxelColor Value;

        public LightNodeNative(int3 position, VoxelColor value) {
            Position = position;
            Value = value;
        }

        public LightNodeNative(int x, int y, int z, VoxelColor value) {
            Position = new int3(x, y, z);
            Value = value;
        }
    }

    /// <summary>
    /// Constants for light propagation matching LightMapper.
    /// </summary>
    public static class LightConstants {
        /// <summary>
        /// Multiplicative attenuation factor (220/256 ≈ 0.859 per step).
        /// </summary>
        public const int LIGHT_MULTIPLIER = 220;

        /// <summary>
        /// Threshold below which light stops propagating.
        /// </summary>
        public const int LIGHT_THRESHOLD = 16;

        /// <summary>
        /// Chunk dimensions.
        /// </summary>
        public const int WIDTH = 16;
        public const int HEIGHT = 256;
        public const int DEPTH = 16;
        public const int VOXEL_COUNT = WIDTH * HEIGHT * DEPTH;
        public const int COLUMN_COUNT = WIDTH * DEPTH;

        /// <summary>
        /// 6 cardinal directions for neighbor checks.
        /// </summary>
        public static readonly int3[] Directions = {
            new int3(0, 1, 0),   // Up
            new int3(0, -1, 0),  // Down
            new int3(1, 0, 0),   // East (+X)
            new int3(-1, 0, 0),  // West (-X)
            new int3(0, 0, 1),   // North (+Z)
            new int3(0, 0, -1)   // South (-Z)
        };
    }

    /// <summary>
    /// Parallel job that initializes sunlight columns.
    /// Sets full light (255,255,255) above heightmap and identifies cavern openings.
    /// Processes 256 columns in parallel (one per thread).
    /// </summary>
    [BurstCompile]
    public struct SunlightColumnJob : IJobParallelFor {
        /// <summary>
        /// Voxel data for opacity checks.
        /// </summary>
        [ReadOnly] public NativeArray<VoxelData> Voxels;

        /// <summary>
        /// Height map for column heights.
        /// </summary>
        [ReadOnly] public NativeArray<HeightData> HeightMap;

        /// <summary>
        /// Output sunlight array.
        /// </summary>
        [NativeDisableParallelForRestriction]
        public NativeArray<VoxelColor> SunLight;

        /// <summary>
        /// Output queue for cavern propagation seeds.
        /// </summary>
        public NativeQueue<LightNodeNative>.ParallelWriter CavernSeeds;

        private static readonly VoxelColor FullLight = new VoxelColor(255, 255, 255);
        private static readonly VoxelColor NoLight = new VoxelColor(0, 0, 0);

        public void Execute(int index) {
            int x = index % LightConstants.WIDTH;
            int z = index / LightConstants.WIDTH;

            var height = HeightMap[index].Value;

            // Set full light above heightmap
            for (int y = height + 1; y < LightConstants.HEIGHT; y++) {
                int vi = VoxelIndex(x, y, z);
                SunLight[vi] = FullLight;
            }

            // Set black below heightmap (propagation will fill if needed)
            for (int y = 0; y <= height; y++) {
                int vi = VoxelIndex(x, y, z);
                SunLight[vi] = NoLight;
            }

            // Check if surface block is translucent and place seed above for entry-tinting
            // Seed must be outside the glass so propagation enters it and applies tinting.
            int heightVi = VoxelIndex(x, height, z);
            var heightVoxel = Voxels[heightVi];
            if (!heightVoxel.IsEmpty() && (heightVoxel.Flags & VoxelFlags.AlphaRender) != 0) {
                int aboveY = height + 1;
                if (aboveY < LightConstants.HEIGHT) {
                    int aboveIdx = VoxelIndex(x, aboveY, z);
                    SunLight[aboveIdx] = NoLight;
                    CavernSeeds.Enqueue(new LightNodeNative(x, aboveY, z, FullLight));
                }
            }

            // Find max height among cardinal neighbors for cavern detection
            int regionMax = height;
            if (x > 0) {
                int nh = HeightMap[HeightIndex(x - 1, z)].Value;
                if (nh > regionMax) regionMax = nh;
            }
            if (x < LightConstants.WIDTH - 1) {
                int nh = HeightMap[HeightIndex(x + 1, z)].Value;
                if (nh > regionMax) regionMax = nh;
            }
            if (z > 0) {
                int nh = HeightMap[HeightIndex(x, z - 1)].Value;
                if (nh > regionMax) regionMax = nh;
            }
            if (z < LightConstants.DEPTH - 1) {
                int nh = HeightMap[HeightIndex(x, z + 1)].Value;
                if (nh > regionMax) regionMax = nh;
            }

            // Skip if at world top
            if (height >= LightConstants.HEIGHT - 1) return;

            // Check for cavern openings from our surface up to regionMax
            for (int y = height + 1; y <= regionMax; y++) {
                // Check 4 cardinal neighbors, passing home coords for consistent seeding
                CheckCavernOpening(x, z, x - 1, y, z);
                CheckCavernOpening(x, z, x + 1, y, z);
                CheckCavernOpening(x, z, x, y, z - 1);
                CheckCavernOpening(x, z, x, y, z + 1);
            }
        }

        private void CheckCavernOpening(int homeX, int homeZ, int nx, int y, int nz) {
            // Bounds check neighbor
            if (nx < 0 || nx >= LightConstants.WIDTH || nz < 0 || nz >= LightConstants.DEPTH) return;

            int neighborHeight = HeightMap[HeightIndex(nx, nz)].Value;

            // If this Y is below neighbor's surface, there's a cavern opening
            if (y < neighborHeight) {
                int neighborIdx = VoxelIndex(nx, y, nz);
                // Only proceed if neighbor is non-opaque (air or translucent)
                if (!Voxels[neighborIdx].IsOpaque()) {
                    // Seed in OUR air column (not neighbor's position which might be glass)
                    // Clear first to enable propagation, then seed
                    // Propagation will flow into neighbor and apply entry-tinting if translucent
                    int homeIdx = VoxelIndex(homeX, y, homeZ);
                    SunLight[homeIdx] = NoLight;
                    CavernSeeds.Enqueue(new LightNodeNative(homeX, y, homeZ, FullLight));
                }
            }
        }

        private static int VoxelIndex(int x, int y, int z) {
            return x + LightConstants.WIDTH * (y + LightConstants.HEIGHT * z);
        }

        private static int HeightIndex(int x, int z) {
            return x + z * LightConstants.WIDTH;
        }
    }

    /// <summary>
    /// Parallel job that finds block light sources.
    /// Scans all voxels and queues light sources for propagation.
    /// </summary>
    [BurstCompile]
    public struct BlockLightSourceJob : IJobParallelFor {
        /// <summary>
        /// Voxel data to scan for light sources.
        /// </summary>
        [ReadOnly] public NativeArray<VoxelData> Voxels;

        /// <summary>
        /// Output block light array (cleared to black).
        /// </summary>
        [NativeDisableParallelForRestriction]
        [WriteOnly] public NativeArray<VoxelColor> BlockLight;

        /// <summary>
        /// Output queue for light source seeds.
        /// </summary>
        public NativeQueue<LightNodeNative>.ParallelWriter LightSources;

        private static readonly VoxelColor NoLight = new VoxelColor(0, 0, 0);

        public void Execute(int index) {
            // Clear this voxel's light
            BlockLight[index] = NoLight;

            var voxel = Voxels[index];

            // Check if light source
            if ((voxel.Flags & VoxelFlags.LightSource) != 0) {
                // Convert flat index back to 3D coordinates
                int x = index % LightConstants.WIDTH;
                int temp = index / LightConstants.WIDTH;
                int y = temp % LightConstants.HEIGHT;
                int z = temp / LightConstants.HEIGHT;

                LightSources.Enqueue(new LightNodeNative(x, y, z, voxel.ColorData));
            }
        }
    }

    /// <summary>
    /// Single-threaded Burst job that propagates light using wavefront BFS.
    /// Takes seed nodes and propagates until queue is empty.
    /// </summary>
    [BurstCompile]
    public struct LightPropagationJob : IJob {
        /// <summary>
        /// Voxel data for opacity and color filtering.
        /// </summary>
        [ReadOnly] public NativeArray<VoxelData> Voxels;

        /// <summary>
        /// Light map to propagate into.
        /// </summary>
        public NativeArray<VoxelColor> LightMap;

        /// <summary>
        /// Queue of nodes to propagate from.
        /// </summary>
        public NativeQueue<LightNodeNative> PropagationQueue;

        public void Execute() {
            // Process all nodes in queue
            while (PropagationQueue.Count > 0) {
                var node = PropagationQueue.Dequeue();
                PropagateFromNode(node);
            }
        }

        private void PropagateFromNode(LightNodeNative node) {
            int cpi = VoxelIndex(node.Position);

            // Bounds check
            if (!InBounds(node.Position)) return;

            var current = LightMap[cpi];

            // Decompose colors
            node.Value.Decompose(out int nr, out int ng, out int nb, out _);
            current.Decompose(out int cr, out int cg, out int cb, out _);

            // Check if this node improves any channel
            bool improves = nr > cr || ng > cg || nb > cb;
            if (!improves) return;

            // Update lightmap with max values
            LightMap[cpi] = VoxelColor.Max(current, node.Value);

            // Stop if below threshold
            if (nr <= LightConstants.LIGHT_THRESHOLD &&
                ng <= LightConstants.LIGHT_THRESHOLD &&
                nb <= LightConstants.LIGHT_THRESHOLD) {
                return;
            }

            // Check if source is a translucent block - apply exit tinting
            var sourceVoxel = Voxels[cpi];
            int sr = nr, sg = ng, sb = nb;
            if (!sourceVoxel.IsEmpty() && (sourceVoxel.Flags & VoxelFlags.AlphaRender) != 0) {
                sourceVoxel.ColorData.Decompose(out int mr, out int mg, out int mb, out _);
                sr = (nr * mr) >> 8;
                sg = (ng * mg) >> 8;
                sb = (nb * mb) >> 8;
            }

            // Propagate to 6 neighbors (with potentially tinted light)
            PropagateToNeighbor(node.Position.x + 1, node.Position.y, node.Position.z, sr, sg, sb);
            PropagateToNeighbor(node.Position.x - 1, node.Position.y, node.Position.z, sr, sg, sb);
            PropagateToNeighbor(node.Position.x, node.Position.y + 1, node.Position.z, sr, sg, sb);
            PropagateToNeighbor(node.Position.x, node.Position.y - 1, node.Position.z, sr, sg, sb);
            PropagateToNeighbor(node.Position.x, node.Position.y, node.Position.z + 1, sr, sg, sb);
            PropagateToNeighbor(node.Position.x, node.Position.y, node.Position.z - 1, sr, sg, sb);
        }

        private void PropagateToNeighbor(int nx, int ny, int nz, int sr, int sg, int sb) {
            // Bounds check
            if (nx < 0 || nx >= LightConstants.WIDTH ||
                ny < 0 || ny >= LightConstants.HEIGHT ||
                nz < 0 || nz >= LightConstants.DEPTH) {
                return;
            }

            int npi = VoxelIndex(nx, ny, nz);
            var neighbor = Voxels[npi];

            // Skip opaque
            if (neighbor.IsOpaque()) return;

            // Multiplicative attenuation
            int dr = (sr * LightConstants.LIGHT_MULTIPLIER) >> 8;
            int dg = (sg * LightConstants.LIGHT_MULTIPLIER) >> 8;
            int db = (sb * LightConstants.LIGHT_MULTIPLIER) >> 8;

            // Apply voxel tint (colored glass)
            if (!neighbor.IsEmpty()) {
                neighbor.ColorData.Decompose(out int mr, out int mg, out int mb, out _);
                dr = (dr * mr) >> 8;
                dg = (dg * mg) >> 8;
                db = (db * mb) >> 8;
            }

            // Skip if zero
            if (dr + dg + db == 0) return;

            // Check if improves existing light
            var neighborLight = LightMap[npi];
            neighborLight.Decompose(out int nlr, out int nlg, out int nlb, out _);
            if (nlr >= dr && nlg >= dg && nlb >= db) return;

            // Queue for propagation
            PropagationQueue.Enqueue(new LightNodeNative(nx, ny, nz, new VoxelColor(dr, dg, db)));
        }

        private static int VoxelIndex(int3 pos) {
            return pos.x + LightConstants.WIDTH * (pos.y + LightConstants.HEIGHT * pos.z);
        }

        private static int VoxelIndex(int x, int y, int z) {
            return x + LightConstants.WIDTH * (y + LightConstants.HEIGHT * z);
        }

        private static bool InBounds(int3 pos) {
            return pos.x >= 0 && pos.x < LightConstants.WIDTH &&
                   pos.y >= 0 && pos.y < LightConstants.HEIGHT &&
                   pos.z >= 0 && pos.z < LightConstants.DEPTH;
        }
    }

    /// <summary>
    /// Job that removes light and tracks positions needing re-propagation.
    /// Implements cascade-clearing to fix the ghost light bug.
    /// </summary>
    [BurstCompile]
    public struct LightRemovalJob : IJob {
        /// <summary>
        /// Voxel data for opacity checks.
        /// </summary>
        [ReadOnly] public NativeArray<VoxelData> Voxels;

        /// <summary>
        /// Light map to clear.
        /// </summary>
        public NativeArray<VoxelColor> LightMap;

        /// <summary>
        /// Queue of positions to clear (input).
        /// </summary>
        public NativeQueue<int3> RemovalQueue;

        /// <summary>
        /// Queue of nodes to re-propagate (output).
        /// </summary>
        public NativeQueue<LightNodeNative> RefillQueue;

        private static readonly VoxelColor NoLight = new VoxelColor(0, 0, 0);

        public void Execute() {
            // Phase 1: Clear light cascading outward
            while (RemovalQueue.Count > 0) {
                var pos = RemovalQueue.Dequeue();

                if (!InBounds(pos)) continue;

                int idx = VoxelIndex(pos);
                var currentLight = LightMap[idx];

                // Skip if already dark
                currentLight.Decompose(out int cr, out int cg, out int cb, out _);
                if (cr == 0 && cg == 0 && cb == 0) continue;

                // Clear this position
                LightMap[idx] = NoLight;

                // Check 6 neighbors
                CheckNeighborForRemoval(pos.x + 1, pos.y, pos.z, cr, cg, cb);
                CheckNeighborForRemoval(pos.x - 1, pos.y, pos.z, cr, cg, cb);
                CheckNeighborForRemoval(pos.x, pos.y + 1, pos.z, cr, cg, cb);
                CheckNeighborForRemoval(pos.x, pos.y - 1, pos.z, cr, cg, cb);
                CheckNeighborForRemoval(pos.x, pos.y, pos.z + 1, cr, cg, cb);
                CheckNeighborForRemoval(pos.x, pos.y, pos.z - 1, cr, cg, cb);
            }
        }

        private void CheckNeighborForRemoval(int nx, int ny, int nz, int sourceR, int sourceG, int sourceB) {
            // Bounds check
            if (nx < 0 || nx >= LightConstants.WIDTH ||
                ny < 0 || ny >= LightConstants.HEIGHT ||
                nz < 0 || nz >= LightConstants.DEPTH) {
                return;
            }

            int npi = VoxelIndex(new int3(nx, ny, nz));
            var neighborLight = LightMap[npi];
            neighborLight.Decompose(out int nr, out int ng, out int nb, out _);

            // If neighbor is darker, it likely came from us - cascade clear
            // Use threshold to account for attenuation
            int attenuatedR = (sourceR * LightConstants.LIGHT_MULTIPLIER) >> 8;
            int attenuatedG = (sourceG * LightConstants.LIGHT_MULTIPLIER) >> 8;
            int attenuatedB = (sourceB * LightConstants.LIGHT_MULTIPLIER) >> 8;

            bool cameFromUs = nr <= attenuatedR && ng <= attenuatedG && nb <= attenuatedB;
            bool hasLight = nr > 0 || ng > 0 || nb > 0;

            if (cameFromUs && hasLight) {
                // This light came from us, clear it
                RemovalQueue.Enqueue(new int3(nx, ny, nz));
            }
            else if (hasLight && (nr > attenuatedR || ng > attenuatedG || nb > attenuatedB)) {
                // This light came from elsewhere, re-propagate from here
                RefillQueue.Enqueue(new LightNodeNative(nx, ny, nz, neighborLight));
            }
        }

        private static int VoxelIndex(int3 pos) {
            return pos.x + LightConstants.WIDTH * (pos.y + LightConstants.HEIGHT * pos.z);
        }

        private static bool InBounds(int3 pos) {
            return pos.x >= 0 && pos.x < LightConstants.WIDTH &&
                   pos.y >= 0 && pos.y < LightConstants.HEIGHT &&
                   pos.z >= 0 && pos.z < LightConstants.DEPTH;
        }
    }

    /// <summary>
    /// Flags indicating which neighbors are available for border propagation.
    /// </summary>
    [System.Flags]
    public enum NeighborFlagsNative : byte {
        None = 0,
        North = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3,
        All = North | East | South | West
    }

    /// <summary>
    /// Job that reads light values from neighbor chunk borders and creates
    /// propagation seeds where neighbor light exceeds current chunk light.
    /// Used for cross-chunk light propagation.
    /// </summary>
    [BurstCompile]
    public struct BorderSeedJob : IJob {
        /// <summary>
        /// Current chunk's voxel data for opacity checks.
        /// </summary>
        [ReadOnly] public NativeArray<VoxelData> Voxels;

        /// <summary>
        /// Current chunk's light map (sun or block).
        /// </summary>
        [ReadOnly] public NativeArray<VoxelColor> CurrentLight;

        /// <summary>
        /// North neighbor's light map (their Z=0 borders our Z=15).
        /// </summary>
        [ReadOnly] public NativeArray<VoxelColor> NeighborNorthLight;

        /// <summary>
        /// East neighbor's light map (their X=0 borders our X=15).
        /// </summary>
        [ReadOnly] public NativeArray<VoxelColor> NeighborEastLight;

        /// <summary>
        /// South neighbor's light map (their Z=15 borders our Z=0).
        /// </summary>
        [ReadOnly] public NativeArray<VoxelColor> NeighborSouthLight;

        /// <summary>
        /// West neighbor's light map (their X=15 borders our X=0).
        /// </summary>
        [ReadOnly] public NativeArray<VoxelColor> NeighborWestLight;

        /// <summary>
        /// Which neighbors are available.
        /// </summary>
        public NeighborFlagsNative NeighborFlags;

        /// <summary>
        /// Output queue for propagation seeds.
        /// </summary>
        public NativeQueue<LightNodeNative> Seeds;

        private const int LAST_IDX = LightConstants.WIDTH - 1;

        public void Execute() {
            // Process each border
            if ((NeighborFlags & NeighborFlagsNative.North) != 0) {
                ProcessNorthBorder();
            }
            if ((NeighborFlags & NeighborFlagsNative.East) != 0) {
                ProcessEastBorder();
            }
            if ((NeighborFlags & NeighborFlagsNative.South) != 0) {
                ProcessSouthBorder();
            }
            if ((NeighborFlags & NeighborFlagsNative.West) != 0) {
                ProcessWestBorder();
            }
        }

        private void ProcessNorthBorder() {
            // Our Z=15 border, neighbor's Z=0 border
            for (int x = 0; x < LightConstants.WIDTH; x++) {
                for (int y = 0; y < LightConstants.HEIGHT; y++) {
                    int homeIdx = VoxelIndex(x, y, LAST_IDX);
                    int neighborIdx = VoxelIndex(x, y, 0);

                    ProcessBorderVoxel(homeIdx, NeighborNorthLight[neighborIdx], x, y, LAST_IDX);
                }
            }
        }

        private void ProcessEastBorder() {
            // Our X=15 border, neighbor's X=0 border
            for (int z = 0; z < LightConstants.DEPTH; z++) {
                for (int y = 0; y < LightConstants.HEIGHT; y++) {
                    int homeIdx = VoxelIndex(LAST_IDX, y, z);
                    int neighborIdx = VoxelIndex(0, y, z);

                    ProcessBorderVoxel(homeIdx, NeighborEastLight[neighborIdx], LAST_IDX, y, z);
                }
            }
        }

        private void ProcessSouthBorder() {
            // Our Z=0 border, neighbor's Z=15 border
            for (int x = 0; x < LightConstants.WIDTH; x++) {
                for (int y = 0; y < LightConstants.HEIGHT; y++) {
                    int homeIdx = VoxelIndex(x, y, 0);
                    int neighborIdx = VoxelIndex(x, y, LAST_IDX);

                    ProcessBorderVoxel(homeIdx, NeighborSouthLight[neighborIdx], x, y, 0);
                }
            }
        }

        private void ProcessWestBorder() {
            // Our X=0 border, neighbor's X=15 border
            for (int z = 0; z < LightConstants.DEPTH; z++) {
                for (int y = 0; y < LightConstants.HEIGHT; y++) {
                    int homeIdx = VoxelIndex(0, y, z);
                    int neighborIdx = VoxelIndex(LAST_IDX, y, z);

                    ProcessBorderVoxel(homeIdx, NeighborWestLight[neighborIdx], 0, y, z);
                }
            }
        }

        private void ProcessBorderVoxel(int homeIdx, VoxelColor neighborLight, int x, int y, int z) {
            // Skip if home voxel is opaque
            if (Voxels[homeIdx].IsOpaque()) return;

            // Decompose light values
            neighborLight.Decompose(out int nr, out int ng, out int nb, out _);

            // Skip if neighbor light is below threshold
            if (nr <= LightConstants.LIGHT_THRESHOLD &&
                ng <= LightConstants.LIGHT_THRESHOLD &&
                nb <= LightConstants.LIGHT_THRESHOLD) {
                return;
            }

            var homeLight = CurrentLight[homeIdx];
            homeLight.Decompose(out int hr, out int hg, out int hb, out _);

            // Skip if home light is already >= neighbor on all channels
            if (hr >= nr && hg >= ng && hb >= nb) return;

            // Apply attenuation (light crosses one voxel boundary)
            int dr = (nr * LightConstants.LIGHT_MULTIPLIER) >> 8;
            int dg = (ng * LightConstants.LIGHT_MULTIPLIER) >> 8;
            int db = (nb * LightConstants.LIGHT_MULTIPLIER) >> 8;

            // Skip if attenuated to zero
            if (dr + dg + db == 0) return;

            // Create seed for propagation
            Seeds.Enqueue(new LightNodeNative(x, y, z, new VoxelColor(dr, dg, db)));
        }

        private static int VoxelIndex(int x, int y, int z) {
            return x + LightConstants.WIDTH * (y + LightConstants.HEIGHT * z);
        }
    }
}
