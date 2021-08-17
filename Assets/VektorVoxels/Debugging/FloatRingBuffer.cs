using System.Runtime.CompilerServices;

namespace AudioTerrain {
    /// <summary>
    /// Super basic ring buffer of floating point values.
    /// Useful as a sort of filter or history buffer.
    /// </summary>
    public sealed class FloatRingBuffer {
        private int _writeIndex;
        
        /// <summary>
        /// Direct access to this buffer's data.
        /// </summary>
        public readonly float[] Data;

        private readonly int _length;

        /// <summary>
        /// Length of the internal buffer.
        /// </summary>
        public int Length => _length;

        public int WriteIndex => _writeIndex;

        public float this[int index] {
            get {
                return Data[InternalIndex(index)];
            }
        }

        public FloatRingBuffer(int length, float init = 0f) {
            Data = new float[length];
            _length = length;
            _writeIndex = 0;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int InternalIndex(int index) {
            return (_writeIndex + index) % _length;
        }
        
        /// <summary>
        /// Pushes a value into the ring buffer.
        /// </summary>
        public void Push(float value) {
            Data[_writeIndex] = value;
            _writeIndex = (_writeIndex + 1) % Data.Length;
        }

        public void PushRange(float[] values) {
            for (var i = 0; i < values.Length; i++) {
                Data[_writeIndex] = values[i];
                _writeIndex = (_writeIndex + 1) % _length;
            }
        }
        
        /// <summary>
        /// Flushes all values from the buffer setting them to zero.
        /// </summary>
        public void Flush() {
            for (var i = 0; i < Data.Length; i++) {
                Data[i] = 0f;
            }
        }
        
        /// <summary>
        /// Floods the buffer with a given value.
        /// </summary>
        public void Flood(float value) {
            for (var i = 0; i < Data.Length; i++) {
                Data[i] = value;
            }
        }
        
        /// <summary>
        /// Returns the sum of all values in the buffer.
        /// </summary>
        /// <returns></returns>
        public float Sum() {
            var sum = 0f;
            for (var i = 0; i < Data.Length; i++) {
                sum += Data[i];
            }

            return sum; 
        }
        
        /// <summary>
        /// Returns the average of all values in the buffer.
        /// </summary>
        /// <returns></returns>
        public float Average() {
            return Sum() / Data.Length;
        }
        
        /// <summary>
        /// Exports all data from this buffer into a destination.
        /// </summary>
        /// <returns>Number of elements written.</returns>
        public int ExportData(float[] destination) {
            for (var i = 0; i < Data.Length; i++) {
                destination[i] = Data[(_writeIndex + i) % Data.Length];
            }

            return Data.Length;
        }
    }
}