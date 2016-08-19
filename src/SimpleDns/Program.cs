using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Pipeliner;
using Pipeliner.Builder;
using SimpleDns.Internal;
using SimpleDns.Pipeline;

namespace SimpleDns {
    public static class Program {
        public static void Main(string[] args) {
            var responseFactory = new DnsResponseFactory(new[] {
                new ResourceRecord(IPAddress.Parse("192.168.56.104"), 5000, new Regex("(?:webdev|dev\\.io)$"))
            });

            var pipeline = new PipelineBuilder<ISocketContext>()
                .UseMiddleware<LocalDnsMiddleware>(responseFactory)
                .Build(DumpPacket);

            RunServer(pipeline).Wait();
        }

        public static async Task RunServer(IPipeline<ISocketContext> pipeline) {
            var localAddress = new IPEndPoint(IPAddress.Any, 53);
            var client = (EndPoint)(new IPEndPoint(IPAddress.Any, 0));
            var buffer = new byte[512];

            using (var master = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp)) {
                master.Bind(localAddress);

                while (true) {
                    var len = master.ReceiveFrom(buffer, ref client);
                    var packet = new ArraySlice<byte>(buffer, 0, len);

                    var context = new UdpSocketContext(master, client, packet);
                    await pipeline.Run(context);
                }
            }
        }

        public static Task DumpPacket(ISocketContext context) {
            var buffer = new char[64];
            int displayOffset = 16 * 3,
                remaining = context.Data.Length,
                b = 0, c = 0;
                
            Console.WriteLine("=== PACKET ===");

            while (remaining > 0) {
                for(int i = 0; i < 16; ++i) {
                    if (remaining == 0) {
                        buffer[i * 3] = ' ';
                        buffer[i * 3 + 1] = ' ';
                        buffer[i * 3 + 2] = ' ';
                        buffer[displayOffset + i] = ' ';
                    }
                    else {
                        c = context.Data[context.Data.Length - remaining];
                        b = c >> 4;
                        buffer[i * 3] = (char)(55 + b + (((b-10)>>31)&-7));
                        b = c & 0xF;
                        buffer[i * 3 + 1] = (char)(55 + b + (((b-10)>>31)&-7));
                        buffer[i * 3 + 2] = ' ';

                        buffer[displayOffset + i] = char.IsControl((char)c) ? '.' : (char)c;
                        --remaining;
                    }
                }

                Console.WriteLine(new string(buffer));
                Console.WriteLine();
            }

            return Task.FromResult(0);
        }
    }
}