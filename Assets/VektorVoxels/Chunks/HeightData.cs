namespace VektorVoxels.Chunks {
    public struct HeightData {
        public byte Value;
        public bool Dirty;

        public HeightData(byte value, bool dirty) {
            Value = value;
            Dirty = dirty;
        }

        public void SetDirty() {
            Dirty = true;
        }
    }
}