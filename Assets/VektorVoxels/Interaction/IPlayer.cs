using UnityEngine;
using VektorVoxels.Voxels;

namespace VektorVoxels.Interaction {
    public interface IPlayer {
        /// <summary>
        /// World position of the player.
        /// </summary>
        Vector3 Position { get; }
        
        /// <summary>
        /// Current euler rotation of the player.
        /// </summary>
        Vector3 Rotation { get; }

        /// <summary>
        /// Sets the current voxel in the player's hand.
        /// </summary>
        void SetHandVoxel(VoxelDefinition definition);
        
        /// <summary>
        /// Teleport the player to a specified world-space position.
        /// </summary>
        void Teleport(Vector3 position);
    }
}