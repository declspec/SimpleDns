using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using SimpleDns.Internal;

namespace SimpleDns.Pipeline {
    public interface ISocketContext {
        ProtocolType Protocol { get; }
        ArraySlice<byte> Data { get; }

        Task End(ArraySlice<byte> response);
        Task End();
    }

    public class UdpSocketContext : ISocketContext {
        public ProtocolType Protocol { get { return ProtocolType.Udp; }}
        public ArraySlice<byte> Data { get; }

        private readonly Socket _socket;
        private readonly EndPoint _client;
        
        public UdpSocketContext(Socket socket, EndPoint client, ArraySlice<byte> data) {
            Data = data;
            _socket = socket;
            _client = client;
        }
        
        public Task End(ArraySlice<byte> response) {
            _socket.SendTo(response.Array, response.Offset, response.Length, SocketFlags.None, _client);
            return Task.FromResult(0);
        }

        public Task End() {
            return Task.FromResult(0);
        }
    }
}
