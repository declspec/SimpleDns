using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SimpleDns.Internal;
using SimpleDns.Network;

namespace SimpleDns.Pipeline {
    public interface ISocketContext {
        ProtocolType Protocol { get; }
        ArraySlice<byte> Data { get; }
        AsyncSocketWrapper SocketWrapper { get; }
        CancellationToken CancellationToken { get; }

        Task End(ArraySlice<byte> response);
        Task End();
    }

    public class UdpSocketContext : ISocketContext {
        public ProtocolType Protocol { get { return ProtocolType.Udp; }}
        public ArraySlice<byte> Data { get; }
        public AsyncSocketWrapper SocketWrapper { get; }
        public CancellationToken CancellationToken { get; }

        private readonly EndPoint _client;
        private readonly Socket _socket;        
        
        public UdpSocketContext(AsyncSocketWrapper socketWrapper, EndPoint client, ArraySlice<byte> data, CancellationToken token) {
            Data = data;
            SocketWrapper = socketWrapper;
            CancellationToken = token;

            // Store the original socket for later use
            _socket = SocketWrapper.Socket;
            _client = client;
        }
        
        public async Task End(ArraySlice<byte> response) {
            // Configure the wrapper
            SocketWrapper.Wrap(_socket);
            
            var args = SocketWrapper.EventArgs;
            args.RemoteEndPoint = _client;

            // Copy the response into the buffer
            if (response.Array != args.Buffer || response.Offset != args.Offset)
                Buffer.BlockCopy(response.Array, response.Offset, args.Buffer, args.Offset, response.Length);

            args.SetBufferLength(response.Length);
            await SocketWrapper.SendToAsync();
        }

        public Task End() {
            return Task.FromResult(0);
        }
    }
}
