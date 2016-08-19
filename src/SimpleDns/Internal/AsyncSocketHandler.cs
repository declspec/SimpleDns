using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace SimpleDns.Internal {
    // Full credit to Stephen Toub for this
    // see: https://blogs.msdn.microsoft.com/pfxteam/2011/12/15/awaiting-socket-operations/
    public sealed class AsyncSocketHandler : INotifyCompletion
    {
        private static readonly Action SENTINEL = () => { };

        public SocketAsyncEventArgs EventArgs { get; }
        public bool IsCompleted { get { return _completed; }}

        private bool _completed;
        private Action _continuation;
       
        public AsyncSocketHandler(SocketAsyncEventArgs eventArgs) {
            if (eventArgs == null)
                throw new ArgumentNullException(nameof(eventArgs));

            EventArgs = eventArgs;
            EventArgs.Completed += OnEventCompleted;

            _completed = false;
            _continuation = null;
        }

        public AsyncSocketHandler ReceiveAsync(Socket socket) {
            Reset();
            if (!socket.ReceiveAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketHandler SendAsync(Socket socket) {
            Reset();
            if (!socket.SendAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketHandler AcceptAsync(Socket socket) {
            Reset();
            if (!socket.AcceptAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketHandler ConnectAsync(Socket socket) {
            Reset();
            if (!socket.AcceptAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketHandler ReceiveFromAsync(Socket socket) {
            Reset();
            if (!socket.ReceiveFromAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketHandler SendToAsync(Socket socket) {
            Reset();
            if (!socket.SendToAsync(EventArgs))
                _completed = true;
            return this;
        }

        public AsyncSocketHandler GetAwaiter() {
            return this;
        }

        public void GetResult() {
            if (EventArgs.SocketError != SocketError.Success)
                throw new SocketException((int)EventArgs.SocketError);
        }

        private void Reset() {
            _completed = false;
            _continuation = null;
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