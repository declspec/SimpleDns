using System;
using System.Threading.Tasks;
using Pipeliner;
using SimpleDns.Internal;
using System.Text;
using SimpleDns.src.SimpleDns.Pipeline.Features;

namespace SimpleDns.Pipeline {
    public class DnsQueryParserMiddleware : IPipelineMiddleware<ISocketContext> {
        private const int DNS_HEADER_SIZE = 0x0C;

        public Task Handle(ISocketContext context, PipelineDelegate<ISocketContext> next) {
            var datagram = context.Data;

            // Ensure that we have at *least* the length of a dns header
            if (datagram.Length < DNS_HEADER_SIZE)
                return next.Invoke(context);

            var flags = ToUInt16(datagram, 2);
            var qcount = ToUInt16(datagram, 4);

            if (!IsValidQuery(flags, qcount))
                return next.Invoke(context);

            var offset = DNS_HEADER_SIZE;
            var label = new StringBuilder(256);

            while (offset < datagram.Length) {
                var len = datagram[offset++];
                if (len == 0)
                    break;

                // Domain sections don't include the dot separator
                // so manually insert one between each label
                if (label.Length > 0)
                    label.Append('.');
                label.Append(Encoding.UTF8.GetString(datagram.Array, datagram.Offset + offset, len));

                offset += len;
            }

            context.Features.Set<IDnsQueryFeature>(new DnsQueryFeature(
                label.ToString(),
                ToUInt16(datagram, offset),
                ToUInt16(datagram, offset + 2),
                offset + 4
            ));

            return next.Invoke(context);
        }

        private static UInt16 ToUInt16(ArraySlice<byte> buffer, int offset) {
            return (UInt16)((buffer[offset] << 8) | buffer[offset + 1]);
        }

        private static bool IsValidQuery(UInt16 flags, UInt16 qcount) {
            return qcount > 0
                && (flags & 0x8000) == 0 // 'QR' flag is not set
                && (flags & 0x7800) == 0; // Regular query type
        }
    }
}
