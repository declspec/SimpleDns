using Pipeliner;
using SimpleDns.Internal;
using SimpleDns.src.SimpleDns.Pipeline.Features;
using System.IO;
using System.Threading.Tasks;

namespace SimpleDns.Pipeline {
    public class DnsQueryLoggingMiddleware : IPipelineMiddleware<ISocketContext>
    {
        private readonly TextWriter _output;

        public DnsQueryLoggingMiddleware(TextWriter output) {
            _output = output;
        }

        public Task Handle(ISocketContext context, PipelineDelegate<ISocketContext> next) {
            var query = context.Features.Get<IDnsQueryFeature>();
            if (query != null)
                _output.WriteLine(query.Name);
            else {
                _output.WriteLine("failed to parse request:");
                Dump(context.Data);
            }

            return next.Invoke(context);
        }

        private void Dump(ArraySlice<byte> data) {
            var buffer = new char[64];
            int displayOffset = 16 * 3,
                remaining = data.Length,
                b = 0, c = 0;

            while (remaining > 0) {
                for (int i = 0; i < 16; ++i) {
                    if (remaining == 0) {
                        buffer[i * 3] = buffer[i * 3 + 1] = buffer[i * 3 + 2] = ' ';
                        buffer[displayOffset + i] = ' ';
                    }
                    else {
                        c = data[data.Length - remaining];
                        b = c >> 4;
                        buffer[i * 3] = (char)(55 + b + (((b - 10) >> 31) & -7));
                        b = c & 0xF;
                        buffer[i * 3 + 1] = (char)(55 + b + (((b - 10) >> 31) & -7));
                        buffer[i * 3 + 2] = ' ';

                        buffer[displayOffset + i] = char.IsControl((char)c) ? '.' : (char)c;
                        --remaining;
                    }
                }

                _output.WriteLine(new string(buffer));
            }
        }
    }
}
