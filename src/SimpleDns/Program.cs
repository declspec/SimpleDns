using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
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
        public int Timeout;
        public IPEndPoint DnsServer;
        public IPEndPoint LocalServer;
    }

    public static class Program {
        private const int UdpPacketSize = 512;

        public static int Main(string[] args) {
            var app = new CommandLineApplication() {
                Name = "simpledns",
                FullName = "Simple DNS",
                Description = "Acts as a simple DNS proxy server that also allows you to configure your own DNS records; a hosts file on steroids.",
            };

            try {
                var program = ConfigureApplication(app, opts => {
                    IPipeline<ISocketContext> pipeline;

                    try {
                        var responseFactory = new DnsResponseFactory();

                        pipeline = new PipelineBuilder<ISocketContext>()
                            .UseMiddleware(new DnsQueryParserMiddleware())
                            .UseMiddleware(new DnsQueryLoggingMiddleware(Console.Out))
                            .UseMiddleware(new LocalDnsMiddleware(opts.HostsFile, responseFactory))
                            .UseMiddleware(new UdpProxyMiddleware(opts.DnsServer))
                            .Build(async context => await context.End());
                    }
                    catch(IOException o) {
                        Console.Error.WriteLine("error: failed to read from hosts file ({0})", o.Message);
                        return 1;
                    }

                    var task = RunServer(opts, pipeline, CancellationToken.None);
                    Console.WriteLine("info: dns started");
                    Task.WaitAll(task);

                    return 0;
                });

                return program.Execute(args);
            }
            catch(CommandParsingException ex) {
                Console.Error.WriteLine("error: {0}", ex.Message);
                return 1;
            }
        }

        private static CommandLineApplication ConfigureApplication(CommandLineApplication app, Func<ServerOptions, int> continuation) {
            var fileArgument = app.Argument("[hostfile]", "Path to your hosts file containing custom DNS records (must be prefixed with #@)");
            var connOption = app.Option("-c|--max-connections", "Maximum number of concurrent connections to allow, defaults to 10.", CommandOptionType.SingleValue);
            var timeoutOption = app.Option("-t|--timeout", "Time, in milliseconds, to wait for the remote DNS server before timing out. A value of <= 0 indicates no timeout.", CommandOptionType.SingleValue);
            var serverOption = app.Option("-s|--server", "IP endpoint of the backing DNS server, defaults to 8.8.8.8:53", CommandOptionType.SingleValue);
            var localOption = app.Option("-a|--address", "Local endpoint to bind to, defaults to 127.0.0.1:53", CommandOptionType.SingleValue);
  
            app.HelpOption("-h|-?|--help");

            app.OnExecute(() => {
                if (string.IsNullOrEmpty(fileArgument.Value)) {
                    app.ShowHelp();
                    return 1;
                }

                var opts = new ServerOptions() { HostsFile = fileArgument.Value };

                if (!TryParseEndPoint(serverOption.Value() ?? "8.8.8.8", 53, out opts.DnsServer))
                    throw new CommandParsingException(app, "invalid format for --server; must be in ip:port format");

                if (!TryParseEndPoint(localOption.Value() ?? "127.0.0.1", 53, out opts.LocalServer))
                    throw new CommandParsingException(app, "invalid format for --address; must be in ip:port format");

                if (!int.TryParse(connOption.Value() ?? "10", out opts.MaximumConnections) || opts.MaximumConnections <= 0)
                    throw new CommandParsingException(app, "invalid value for --max-connections; must be a positive, non-zero integer");

                if (!int.TryParse(timeoutOption.Value() ?? "0", out opts.Timeout))
                    throw new CommandParsingException(app, "Invalid value for --timeout; must be a positive, non-zero integer");

                return continuation(opts);
            });

            return app;
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

                while(!token.IsCancellationRequested) {
                    var wrapper = await pool.Acquire(token);
                    wrapper.Wrap(master);
                    wrapper.EventArgs.ResetBuffer();

                    try {
                        // Wait for a request to come through
                        var client = (EndPoint)(new IPEndPoint(IPAddress.Any, 0));
                        wrapper.EventArgs.RemoteEndPoint = client;
                        await wrapper.ReceiveFromAsync();

                        // Create an expiring CancellationToken to use
                        var expiringToken = CreateExpiringCancellationToken(token, opts.Timeout);

                        // Create the context and dispatch the request.
                        var data = new ArraySlice<byte>(wrapper.EventArgs.Buffer, wrapper.EventArgs.Offset, wrapper.EventArgs.BytesTransferred);
                        var context = new UdpSocketContext(wrapper, wrapper.EventArgs.RemoteEndPoint, data, expiringToken);

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

        private static CancellationToken CreateExpiringCancellationToken(CancellationToken source, int timeout) {
            if (timeout <= 0)
                return source;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(source);
            cts.CancelAfter(timeout);
            return cts.Token;
        }

        private static bool TryParseEndPoint(string str, int defaultPort, out IPEndPoint ep) {
            ep = new IPEndPoint(IPAddress.Any, 0);
            var parts = str.Split(':');

            if (!IPAddress.TryParse(parts[0], out IPAddress ip) || (parts.Length == 2 && !int.TryParse(parts[1], out defaultPort)))
                return false;

            ep = new IPEndPoint(ip, defaultPort);
            return true;
        }
    }
}