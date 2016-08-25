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
            var pool = CreatePool();

            // create a 'finally' handler that will return the wrapper to the pool
            var releaser = new PipelineDelegate<ISocketContext>(context => { 
                pool.Release(context.SocketWrapper);
                return Task.FromResult(0);
            });

            // TODO: Actually build a proper resource record generator
            var responseFactory = new DnsResponseFactory(new[] {
                new ResourceRecord(IPAddress.Parse("192.168.56.104"), 5000, new Regex("(?:webdev|dev\\.io)$"))
            });

            var pipeline = new PipelineBuilder<ISocketContext>()
                .UseMiddleware(new FinallyMiddleware<ISocketContext>(releaser))
                .UseMiddleware(new PacketDumpMiddleware(Console.Out))
                .UseMiddleware(new DnsMiddleware(responseFactory))
                .UseMiddleware(new UdpProxyMiddleware(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53)))
                .Build(async context => await context.End());

            RunServer(pipeline, pool).Wait();
        }

        private static SparsePool<AsyncSocketWrapper> CreatePool() {
            // Define a sparse pool of async socket wrappers
            // from a fixed-size buffer to reduce allocations and memory fragmentation
            var buffer = new byte[UdpPacketSize * MaximumConnections];

            return new SparsePool<AsyncSocketWrapper>(MaximumConnections, n => {
                var args = new SocketAsyncEventArgsEx(buffer, UdpPacketSize * n, UdpPacketSize);
                return new AsyncSocketWrapper(args);
            });
        }

        public static async Task RunServer(IPipeline<ISocketContext> pipeline, SparsePool<AsyncSocketWrapper> pool) {
            var localAddress = new IPEndPoint(IPAddress.Any, 53);

            using (var master = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp)) {
                master.Bind(localAddress);

                while(true) {
                    // Get a wrapper from the pool and configure it, this will
                    // block until the pool has an available wrapper
                    var wrapper = await pool.Acquire(CancellationToken.None);
                    wrapper.Wrap(master);
                    wrapper.EventArgs.ResetBuffer();

                    // Wait for the next request
                    var client = (EndPoint)(new IPEndPoint(IPAddress.Any, 0));
                    wrapper.EventArgs.RemoteEndPoint = client;
                    await wrapper.ReceiveFromAsync();
                   
                    // Allocate 5 seconds for all requests
                    var cts = new CancellationTokenSource(2000);

                    // Create the context and dispatch the request
                    var data = new ArraySlice<byte>(wrapper.EventArgs.Buffer, wrapper.EventArgs.Offset, wrapper.EventArgs.BytesTransferred);
                    var context = new UdpSocketContext(wrapper, wrapper.EventArgs.RemoteEndPoint, data, cts.Token);

                    // No need for an await here, the `Pool.Acquire()` above will
                    // enforce blocking once too many concurrent requests stack up.
                    pipeline.Run(context);
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