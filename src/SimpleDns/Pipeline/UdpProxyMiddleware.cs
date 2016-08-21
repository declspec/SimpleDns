using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Pipeliner;
using SimpleDns.Internal;

namespace SimpleDns.Pipeline {
    public class UdpProxyMiddleware : IPipelineMiddleware<ISocketContext> {
        private const int MaxSockets = 10;
        private static readonly EndPoint LocalEndPoint = new IPEndPoint(IPAddress.Any, 0);

        private readonly EndPoint _target;
        private readonly SparsePool<Socket> _sockets;

        public UdpProxyMiddleware(EndPoint target) {
            _target = target;
            _sockets = new SparsePool<Socket>(MaxSockets, GetSocket);
        }

        public async Task Handle(ISocketContext context, PipelineDelegate<ISocketContext> next) {
            var socket = await _sockets.Acquire();

            try {
                // TODO: Work on fault tolerance as well as timeouts on the receive.
                context.SocketWrapper.Wrap(socket);

                var args = context.SocketWrapper.EventArgs;
                args.RemoteEndPoint = _target;

                args.SetBufferLength(context.Data.Length);
                await context.SocketWrapper.SendToAsync();

                args.ResetBuffer();
                await context.SocketWrapper.ReceiveFromAsync();

                var response = new ArraySlice<byte>(args.Buffer, args.Offset, args.BytesTransferred);
                await context.End(response);
            }
            finally {
                _sockets.Release(socket);
            }
        }

        private static Socket GetSocket(int n) {
            var socket = new Socket(LocalEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(LocalEndPoint);
            return socket;
        }
    }
}