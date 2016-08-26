using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Pipeliner;
using Pipeliner.Builder;
using SimpleDns.Internal;
using SimpleDns.Network;
using SimpleDns.Pipeline;

namespace SimpleDns {
    public class ServerOptions {
        public string HostsFile { get; }
        public int MaximumConnections { get; }
        public IPEndPoint DnsServer { get; }
        public IPEndPoint LocalServer { get; }

        public ServerOptions(string hostsfile, int maxConnections, IPEndPoint dns, IPEndPoint local) {
            HostsFile = hostsfile;
            MaximumConnections = maxConnections;
            DnsServer = dns;
            LocalServer = local;
        }
    }

    public static class Program {
        private const int UdpPacketSize = 512;

        private static void PrintUsage() {
            Console.WriteLine("usage: simpledns -h <hostfile> -s <dns (ip:port)> [-l <local (ip:port)>[, -c <max-connections>]]");
        }

        public static void Main(string[] args) {
            if (args.Length == 0) {
                PrintUsage();
                return;
            }

            ServerOptions options;
            IList<ResourceRecord> records;

            try {
                options = BuildOptions(args);
                records = ReadResourceRecords(options.HostsFile).ToList();
            }
            catch(IOException o) {
                Console.WriteLine("error: failed to read from hosts file ({0})", o.Message);
                return;
            }
            catch(FormatException f) {
                Console.WriteLine("error: {0}", f.Message);
                return;
            }

            var responseFactory = new DnsResponseFactory(records);

            var pipeline = new PipelineBuilder<ISocketContext>()
                //.UseMiddleware(new PacketDumpMiddleware(Console.Out))
                .UseMiddleware(new DnsMiddleware(responseFactory))
                .UseMiddleware(new UdpProxyMiddleware(new IPEndPoint(IPAddress.Parse("134.7.134.7"), 53)))
                .Build(async context => await context.End());

            var task = RunServer(options, pipeline, CancellationToken.None);
            Console.WriteLine("info: started with {0} custom records.", records.Count);
            task.Wait();
        }

        private static async Task RunServer(ServerOptions opts, IPipeline<ISocketContext> pipeline, CancellationToken token) {
            // Define a sparse pool of async socket wrappers
            // from a fixed-size buffer to reduce allocations and memory fragmentation
            var buffer = new byte[UdpPacketSize * opts.MaximumConnections];

            var pool = new AsyncObjectPool<AsyncSocketWrapper>(opts.MaximumConnections, n => {
                var args = new SocketAsyncEventArgsEx(buffer, UdpPacketSize * n, UdpPacketSize);
                return new AsyncSocketWrapper(args);
            });

            using (var master = new Socket(opts.LocalServer.AddressFamily, SocketType.Dgram, ProtocolType.Udp)) {
                master.Bind(opts.LocalServer);

                while(true) {
                    var wrapper = await pool.Acquire(token);
                    wrapper.Wrap(master);
                    wrapper.EventArgs.ResetBuffer();

                    try {
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
                        var ignore = pipeline.Run(context).ContinueWith(task => {
                            pool.Free(context.SocketWrapper);

                            if (task.IsFaulted) {
                                foreach(var ex in task.Exception.InnerExceptions)
                                    Console.WriteLine("error: {0}", ex.Message);
                            }
                        });
                    }
                    catch(Exception e) {
                        Console.WriteLine("error: {0}", e.Message);
                        pool.Free(wrapper);
                    }
                }
            }
        }

        private static ServerOptions BuildOptions(string[] args) {
            var results = ParseOptions(args, 'h', 's', 'l', 'c');
            if (string.IsNullOrEmpty(results['h']))
                throw new FormatException("please specify the path to the hosts file to use");

            if (string.IsNullOrEmpty(results['s']))
                throw new FormatException("please specify the ip:port combination of the DNS server to forward requests to");

            IPEndPoint serverAddr;
            IPEndPoint localAddr;
            int maxConnections;

            if (!TryParseEndPoint(results['s'], out serverAddr))
                throw new FormatException("dns server address must be in the form 'ip:port' (i.e 127.0.0.1:53)");

            if (!TryParseEndPoint(results['l'] ?? "0.0.0.0:53", out localAddr))
                throw new FormatException("local address must be in the form 'ip:port' (i.e 127.0.0.1:53)");

            if (!int.TryParse(results['c'] ?? "10", out maxConnections) || maxConnections <= 0)
                throw new FormatException("maximum connections must be a valid non-zero integer");

            return new ServerOptions(results['h'], maxConnections, serverAddr, localAddr);
        }

        private static IEnumerable<ResourceRecord> ReadResourceRecords(string file) {
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
        }

        public static Dictionary<char, string> ParseOptions(string[] args, params char[] opts) {
            var results = opts.ToDictionary(c => c, c => default(string));

            var argc = args.Length;
            var argv = 0;

            while(argc > 0) {
                if (args[argv][0] != '-' || args[argv].Length < 2)
                    continue;

                var opt = args[argv][1];
                foreach(var o in opts) {
                    if (o == opt) {
                        if (argc < 2)
                            throw new FormatException(string.Format("missing required value for the -{0} option", o));

                        results[o] = args[argv+1];
                        ++argv; --argc;
                        break;
                    }
                }

                ++argv; --argc;
            }

            return results;
        }
    }
}