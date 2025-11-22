using System;
using System.Diagnostics;
using UnityEngine;
using VektorVoxels.Config;
using Debug = UnityEngine.Debug;

namespace VektorVoxels.Chunks {
    /// <summary>
    /// Immutable collection of a chunk's neighbors. Contains 4 cardinal (N/E/S/W) and
    /// 4 diagonal (NE/SE/SW/NW) references. Flags indicate which neighbors are valid.
    /// Used by lighting and meshing jobs to read border voxel data from adjacent chunks.
    /// </summary>
    public readonly struct NeighborSet {
        public readonly Chunk North;
        public readonly Chunk East;
        public readonly Chunk South;
        public readonly Chunk West;
        
        public readonly Chunk NorthEast;
        public readonly Chunk SouthEast;
        public readonly Chunk SouthWest;
        public readonly Chunk NorthWest;

        public readonly NeighborFlags Flags;

        public NeighborSet(in Chunk[] neighbors, NeighborFlags flags) {
            if (neighbors.Length < 8) {
                throw new ArgumentException("Neighbor buffer must have a length of 8");
            }
            
            North = neighbors[0];
            East = neighbors[1];
            South = neighbors[2];
            West = neighbors[3];

            NorthEast = neighbors[4];
            SouthEast = neighbors[5];
            SouthWest = neighbors[6];
            NorthWest = neighbors[7];
            
            Flags = flags;
        }
        
        /// <summary>
        /// Acquires read locks on any valid neighbors.
        /// </summary>
        public void AcquireReadLocks() {
            var success = true;
            
            if ((Flags & NeighborFlags.North) != 0) {
                if (!North.ThreadLock.TryEnterReadLock(GlobalConstants.JOB_LOCK_TIMEOUT_MS)) {
                    success = false;
                }
            }
            
            if ((Flags & NeighborFlags.East) != 0) {
                if (!East.ThreadLock.TryEnterReadLock(GlobalConstants.JOB_LOCK_TIMEOUT_MS)) {
                    success = false;
                }
            }
            
            if ((Flags & NeighborFlags.South) != 0) {
                if (!South.ThreadLock.TryEnterReadLock(GlobalConstants.JOB_LOCK_TIMEOUT_MS)) {
                    success = false;
                }
            }
            
            if ((Flags & NeighborFlags.West) != 0) {
                if (!West.ThreadLock.TryEnterReadLock(GlobalConstants.JOB_LOCK_TIMEOUT_MS)) {
                    success = false;
                }
            }

            if (!success) {
                Debug.LogError("Failed to acquire one or more neighbor locks!");
                if (Application.isEditor) {
                    Debug.Break();
                }
                else {
                    Process.GetCurrentProcess().Kill();
                }
            }
        }
        
        /// <summary>
        /// Releases read locks on any valid neighbors.
        /// </summary>
        public void ReleaseReadLocks() {
            if ((Flags & NeighborFlags.North) != 0) {
                North.ThreadLock.ExitReadLock();
            }
            
            if ((Flags & NeighborFlags.East) != 0) {
                East.ThreadLock.ExitReadLock();
            }
            
            if ((Flags & NeighborFlags.South) != 0) {
                South.ThreadLock.ExitReadLock();
            }
            
            if ((Flags & NeighborFlags.West) != 0) {
                West.ThreadLock.ExitReadLock();
            }
        }
    }
}