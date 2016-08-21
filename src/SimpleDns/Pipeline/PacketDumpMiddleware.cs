using System.IO;
using System.Threading.Tasks;
using Pipeliner;

namespace SimpleDns.Pipeline {
    public class PacketDumpMiddleware : IPipelineMiddleware<ISocketContext> {
        private readonly TextWriter _writer;

        public PacketDumpMiddleware(TextWriter output) {
            _writer = output;
        }

        public async Task Handle(ISocketContext context, PipelineDelegate<ISocketContext> next) {
            var buffer = new char[64];
            int displayOffset = 16 * 3,
                remaining = context.Data.Length,
                b = 0, c = 0;
                
            _writer.WriteLine("\ninfo: incoming packet\n");

            while (remaining > 0) {
                for(int i = 0; i < 16; ++i) {
                    if (remaining == 0) {
                        buffer[i * 3] = buffer[i * 3 + 1] = buffer[i * 3 + 2] = ' ';
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

                _writer.WriteLine(new string(buffer));
            }

            _writer.WriteLine();
            await next.Invoke(context);
        }
    }
}