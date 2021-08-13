namespace VektorVoxels.Chunks {
    public struct HeightData {
        public int Value;
        public bool Dirty;

        public HeightData(int value, bool dirty) {
            Value = value;
            Dirty = dirty;
        }
    }
}