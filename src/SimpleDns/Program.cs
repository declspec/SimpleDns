using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.CommandLineUtils;
using Pipeliner;
using Pipeliner.Builder;
using SimpleDns.Internal;
using SimpleDns.Network;
using SimpleDns.Pipeline;

namespace SimpleDns {
    public class ServerOptions {
        public string HostsFile;
        public int MaximumConnections;
        public IPEndPoint DnsServer;
        public IPEndPoint LocalServer;
    }

    public static class Program {
        private const int UdpPacketSize = 512;

        private static void PrintUsage() {
            Console.WriteLine("usage: simpledns -h <hostfile> -s <dns (ip:port)> [-l <local (ip:port)>[, -c <max-connections>]]");
        }

        private static CommandLineApplication ConfigureApplication(CommandLineApplication app, Func<ServerOptions, int> continuation) {
            var fileArgument = app.Argument("[hostfile]", "Path to your hosts file containing custom DNS records (must be prefixed with #@)");
            var connOption = app.Option("-c|--connections", "Maximum number of concurrent connections to allow, defaults to 10.", CommandOptionType.SingleValue);
            var serverOption = app.Option("-s|--server", "IP endpoint of the backing DNS server, defaults to 8.8.8.8:53", CommandOptionType.SingleValue);
            var localOption = app.Option("-a|--address", "Local endpoint to bind to, defaults to 127.0.0.1:53", CommandOptionType.SingleValue);

            app.HelpOption("-h|-?|--help");

            app.OnExecute(() => {
                if (string.IsNullOrEmpty(fileArgument.Value)) {
                    app.ShowHelp();
                    return 1;
                }

                var opts = new ServerOptions() { HostsFile = fileArgument.Value };

                if (!TryParseEndPoint(serverOption.Value() ?? "8.8.8.8:53", out opts.DnsServer))
                    throw new CommandParsingException(app, "invalid format for --server; must be in ip:port format");

                if (!TryParseEndPoint(localOption.Value() ?? "127.0.0.1:53", out opts.LocalServer))
                    throw new CommandParsingException(app, "invalid format for --address; must be in ip:port format");

                if (!int.TryParse(connOption.Value() ?? "10", out opts.MaximumConnections))
                    throw new CommandParsingException(app, "invalid value for --connections; must be a positive, non-zero integer");

                return continuation(opts);
            });

            return app;
        }

        public static int Main(string[] args) {
            var app = new CommandLineApplication() {
                Name = "simpledns",
                FullName = "Simple DNS",
                Description = "Acts as a simple DNS proxy server that also allows you to configure your own DNS records; a hosts file on steroids.",
            };

            try {
                return ConfigureApplication(app, opts => {
                    IPipeline<ISocketContext> pipeline;

                    try {
                        var records = ReadResourceRecords(opts.HostsFile).ToList();
                        var responseFactory = new DnsResponseFactory(records);

                        pipeline = new PipelineBuilder<ISocketContext>()
                            //.UseMiddleware(new PacketDumpMiddleware(Console.Out))
                            .UseMiddleware(new DnsMiddleware(responseFactory))
                            .UseMiddleware(new UdpProxyMiddleware(opts.DnsServer))
                            .Build(async context => await context.End());
                    }
                    catch(IOException o) {
                        Console.Error.WriteLine("error: failed to read from hosts file ({0})", o.Message);
                        return 1;
                    }

                    var task = RunServer(opts, pipeline, CancellationToken.None);
                    Console.WriteLine("info: dns started");
                    task.Wait();

                    return 0;
                }).Execute(args);
            }
            catch(CommandParsingException ex) {
                Console.Error.WriteLine("error: {0}", ex.Message);
                return 0;
            }
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
                                    Console.WriteLine("error: {0}", ex.ToString());
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
    }
}