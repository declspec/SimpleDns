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
            context.CancellationToken.ThrowIfCancellationRequested();

            var socket = await _sockets.Acquire(context.CancellationToken);

            var cancellationHandler = new Action(() => {
                // If the token gets cancelled we dispose the underlying socket
                // which will force any async method to shutdown with an error.
                if (socket != null)
                    socket.Dispose();
                Console.WriteLine("cancelled");
            });

            using(context.CancellationToken.Register(cancellationHandler, false)) {
                try {
                    context.SocketWrapper.Wrap(socket);

                    var args = context.SocketWrapper.EventArgs;
                    args.RemoteEndPoint = _target;

                    args.SetBufferLength(context.Data.Length);
                    await context.SocketWrapper.SendToAsync();

                    args.ResetBuffer();
                    await context.SocketWrapper.ReceiveFromAsync();

                    // Release the socket back into the pool
                    _sockets.Release(socket);
                    socket = null;

                    var response = new ArraySlice<byte>(args.Buffer, args.Offset, args.BytesTransferred);
                    await context.End(response);
                }
                catch(SocketException) {
                    _sockets.Remove(socket);
                    socket = null;
                    throw;
                }
                finally {
                    if (socket != null)
                        _sockets.Release(socket);
                }
            }
        }

        private static Socket GetSocket(int n) {
            var socket = new Socket(LocalEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(LocalEndPoint);
            return socket;
        }
    }
}