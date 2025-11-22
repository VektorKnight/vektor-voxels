namespace VektorVoxels.Voxels {
    /// <summary>
    /// Cardinal and vertical directions for voxel faces and orientations.
    /// North = Z+, East = X+. Used for block orientation and texture lookups.
    /// </summary>
    public enum FacingDirection : byte {
        North,
        South,
        East,
        West,
        Up,
        Down
    }
}