using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace SimpleDns.Network {
    // Full credit to Stephen Toub for this
    // see: https://blogs.msdn.microsoft.com/pfxteam/2011/12/15/awaiting-socket-operations/
    public sealed class AsyncSocketWrapper : INotifyCompletion
    {
        private static readonly Action SENTINEL = () => { };

        public SocketAsyncEventArgsEx EventArgs { get; }
        public Socket Socket { get { return _socket; } }
        public bool IsCompleted { get { return _completed; }}

        private bool _completed;
        private Action _continuation;
        private Socket _socket;
       
        public AsyncSocketWrapper(SocketAsyncEventArgsEx eventArgs) {
            if (eventArgs == null)
                throw new ArgumentNullException(nameof(eventArgs));

            EventArgs = eventArgs;
            EventArgs.Completed += OnEventCompleted;

            _completed = false;
            _continuation = null;
        }

        public void Wrap(Socket socket) {
            _socket = socket;
        }

        public AsyncSocketWrapper Unwrap() {
            _socket = null;
            return this;        
        }

        public AsyncSocketWrapper ReceiveAsync() {
            Reset();
            if (!_socket.ReceiveAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketWrapper SendAsync() {
            Reset();
            if (!_socket.SendAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketWrapper AcceptAsync() {
            Reset();
            if (!_socket.AcceptAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketWrapper ConnectAsync() {
            Reset();
            if (!_socket.AcceptAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketWrapper ReceiveFromAsync() {
            Reset();
            if (!_socket.ReceiveFromAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketWrapper SendToAsync() {
            Reset();
            if (!_socket.SendToAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketWrapper GetAwaiter() {
            return this;
        }

        public void GetResult() {
            if (EventArgs.SocketError != SocketError.Success)
                throw new SocketException((int)EventArgs.SocketError);
        }

        private void Reset() {
            _completed = false;
            _continuation = null;

            if (_socket == null)
                throw new InvalidOperationException("Must first wrap a socket before attempting this operation");
        }

        public void OnCompleted(Action continuation)
        {
            if (_continuation == SENTINEL || Interlocked.CompareExchange(ref _continuation, continuation, null) == SENTINEL)
                Task.Run(continuation);
        }

        private void OnEventCompleted(object sender, SocketAsyncEventArgs args) {
            var previous = _continuation ?? Interlocked.CompareExchange(ref _continuation, SENTINEL, null);
            if (previous != null)
                previous.Invoke();
        }
    }
}