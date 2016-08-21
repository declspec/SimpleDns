using System.Net.Sockets;

namespace SimpleDns.Network {
    // Basic override of the default SocketAsyncEventArgs class
    // to allow for a fixed-buffer event args which knows about its
    // own limitations so it can be 'reset' from anywhere.
    // For example this allows anyone to reset the buffer to its max
    // size prior to a read.
    public class SocketAsyncEventArgsEx : SocketAsyncEventArgs {
        private readonly int _bufferSize;
        private readonly int _offset;

        public int Capacity { get { return _bufferSize; } }

        public SocketAsyncEventArgsEx(byte[] buffer, int offset, int count) : base() {
            base.SetBuffer(buffer, offset, count);

            // Store the original buffer size
            _bufferSize = count;
            _offset = offset;
        }

        public void ResetBuffer() {
            base.SetBuffer(_offset, _bufferSize);
        }

        public void SetBufferLength(int count) {
            base.SetBuffer(Offset, count);
        }
    }
}