namespace VektorVoxels.Lighting {
    /// <summary>
    /// Light propagation pass sequence.
    /// First: Initial sun/block light setup and intra-chunk propagation.
    /// Second/Third: Inter-chunk light spilling from loaded neighbors.
    /// Multiple neighbor passes allow light to propagate across chunk boundaries.
    /// </summary>
    public enum LightPass {
        None = 0,
        First = 1,
        Second = 2,
        Third = 3
    }
}