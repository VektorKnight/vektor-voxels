namespace VektorVoxels.Generation {
    public struct VoxelLayer {
        public uint VoxelId;
        public int Thickness;

        public VoxelLayer(uint voxelId, int thickness) {
            VoxelId = voxelId;
            Thickness = thickness;
        }
    }
}