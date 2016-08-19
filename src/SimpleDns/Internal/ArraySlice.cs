using System;

namespace SimpleDns.Internal {
    public struct ArraySlice<T> {
        private readonly T[] _array;
        private readonly int _offset;
        private readonly int _length;

        public T[] Array { get { return _array; } }
        public int Offset { get { return _offset; } }
        public int Length { get { return _length; } }

        public ArraySlice(T[] array) {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            _array = array;
            _offset = 0;
            _length = array.Length;
        }

        public ArraySlice(T[] array, int offset, int length) {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            if (offset < 0 || (offset + length) > array.Length)
                throw new ArgumentOutOfRangeException(offset < 0 ? nameof(offset): nameof(length));

            _array = array;
            _offset = offset;
            _length = length;
        }

        public T this[int index] {
            get { return _array[_offset + index]; }
        }

        public T[] ToArray() {
            var buffer = new T[_length];
            System.Array.Copy(_array, _offset, buffer, 0, _length);
            return buffer;
        }
    }
}