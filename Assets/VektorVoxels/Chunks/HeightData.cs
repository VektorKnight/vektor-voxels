namespace VektorVoxels.Chunks {
    public struct HeightData {
        public uint Value;
        public bool Dirty;

        public HeightData(uint value, bool dirty) {
            Value = value;
            Dirty = dirty;
        }
    }
}