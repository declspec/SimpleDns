using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using Pipeliner;
using Pipeliner.Builder;
using SimpleDns.Internal;
using SimpleDns.Network;
using SimpleDns.Pipeline;

namespace SimpleDns {
    public static class Program {
        private const int MaximumConnections = 10;
        private const int UdpPacketSize = 512;

        public static void Main(string[] args) {
            // TODO: Actually build a proper resource record generator
            var responseFactory = new DnsResponseFactory(new[] {
                new ResourceRecord(IPAddress.Parse("192.168.56.104"), 5000, new Regex("(?:webdev|dev\\.io)$"))
            });

            var pipeline = new PipelineBuilder<ISocketContext>()
                .UseMiddleware(new PacketDumpMiddleware(Console.Out))
                .UseMiddleware(new DnsMiddleware(responseFactory))
                .UseMiddleware(new UdpProxyMiddleware(new IPEndPoint(IPAddress.Parse("134.7.134.7"), 53)))
                .Build(async context => await context.End());

            RunServer(pipeline, CancellationToken.None).Wait();
        }

        private static async Task RunServer(IPipeline<ISocketContext> pipeline, CancellationToken token) {
            // Define a sparse pool of async socket wrappers
            // from a fixed-size buffer to reduce allocations and memory fragmentation
            var buffer = new byte[UdpPacketSize * MaximumConnections];

            var pool = new AsyncObjectPool<AsyncSocketWrapper>(MaximumConnections, n => {
                var args = new SocketAsyncEventArgsEx(buffer, UdpPacketSize * n, UdpPacketSize);
                return new AsyncSocketWrapper(args);
            });

            var localAddress = new IPEndPoint(IPAddress.Any, 53);

            using (var master = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp)) {
                master.Bind(localAddress);

                while(true) {
                    var wrapper = await pool.Acquire(token);
                    wrapper.Wrap(master);
                    wrapper.EventArgs.ResetBuffer();

                    // Wait for a request to come through
                    var client = (EndPoint)(new IPEndPoint(IPAddress.Any, 0));
                    wrapper.EventArgs.RemoteEndPoint = client;
                    await wrapper.ReceiveFromAsync();

                    // Allocate 2 seconds for each request and cancel them if the master token is cancelled
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    cts.CancelAfter(2000);

                    // Create the context and dispatch the request.
                    var data = new ArraySlice<byte>(wrapper.EventArgs.Buffer, wrapper.EventArgs.Offset, wrapper.EventArgs.BytesTransferred);
                    var context = new UdpSocketContext(wrapper, wrapper.EventArgs.RemoteEndPoint, data, cts.Token);

                    // Don't wait for the task to complete, 'Acquire' will block if too many requests come in.
                    pipeline.Run(context).ContinueWith(_ => pool.Free(context.SocketWrapper));
                }
            }
        }
/*
        private static Options GetOptions(string[] args) {
            var results = OptionParser.Parse(args, 'h', 's', 'l');
            if (string.IsNullOrEmpty(results['h']))
                throw new FormatException("please specify the path to the hosts file to use");
            
            if (string.IsNullOrEmpty(results['s']))
                throw new FormatException("please specify the ip:port combination of the DNS server to forward requests to");
            
            IPEndPoint serverAddr;
            IPEndPoint localAddr;

            if (!TryParseEndPoint(results['s'], out serverAddr))
                throw new FormatException("dns server address must be in the form 'ip:port' (i.e 127.0.0.1:53)");
            
            if (!TryParseEndPoint(results['l'] ?? "127.0.0.1:53", out localAddr))
                throw new FormatException("local address must be in the form 'ip:port' (i.e 127.0.0.1:53)");
            
            return new Options(results['h'], serverAddr, localAddr);
        }

        private static IEnumerable<ResourceRecord> GetResourceRecords(string file) {
            using(var fs = new FileStream(file, FileMode.Open, FileAccess.Read))
            using(var sr = new StreamReader(fs)) {
                string line = null;
                while((line = sr.ReadLine()) != null) {
                    if (!line.StartsWith("#@"))
                        continue;
                    
                    line = line.Substring(2).Trim();
                    ResourceRecord record = null;

                    try {
                        record = ResourceRecord.Parse(line);
                    }
                    catch(FormatException f) {
                        Console.WriteLine("warning: skipping '{0}' ({1})", line, f.Message);
                    }

                    if (record != null)
                        yield return record;
                }
            }
        }

        private static bool TryParseEndPoint(string str, out IPEndPoint ep) {
            ep = new IPEndPoint(IPAddress.Any, 0);
            var parts = str.Split(':');
            
            if (parts.Length != 2)
                return false;
            
            IPAddress ip;
            int port;

            if (!IPAddress.TryParse(parts[0], out ip) || !int.TryParse(parts[1], out port))
                return false;
            
            ep = new IPEndPoint(ip, port);
            return true;
        }*/
    }
}