using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Net.Sockets;

namespace SimpleDns.Internal {
    public class DnsResponseFactory {
        private const int DNS_HEADER_SIZE = 0x0C;
        private const int DNS_ANSWER_SIZE = 0x0C;

        private IList<ResourceRecord> Records { get; }

        public DnsResponseFactory(IEnumerable<ResourceRecord> records) {
            Records = new List<ResourceRecord>(records);
        }

        public byte[] GetResponse(ArraySlice<byte> datagram) {
            // Ensure that we have at *least* the length of a dns header
            if (datagram.Length < DNS_HEADER_SIZE)
                return null;

            var flags = ToUInt16(datagram, 2);
            var qcount = ToUInt16(datagram, 4);

            if (!IsValidQuery(flags, qcount))
                return null;

            return Lookup(datagram);
        }

        private ResourceRecord FindAnswer(Question question) {
            return Records.FirstOrDefault(r => r.Name.IsMatch(question.Name));
        }

        private byte[] Lookup(ArraySlice<byte> datagram) {
            var offset = DNS_HEADER_SIZE;
            var label = new StringBuilder(256);

            while(offset < datagram.Length) {
                var len = datagram[offset++];
                if (len == 0)
                    break;

                // Domain sections don't include the dot separator, so it needs to label
                // manually inserted between each label
                if (label.Length > 0)
                    label.Append('.');
                label.Append(Encoding.UTF8.GetString(datagram.Array, datagram.Offset + offset, len));

                offset += len;
            }

            var question = new Question(
                label.ToString(), 
                ToUInt16(datagram, offset), 
                ToUInt16(datagram, offset + 2)
            );

            if (!IsValidQuestion(question))
                return null; // We do not know how to handle this question/don't care       

            Console.WriteLine("resolving: {0}", question.Name);

            var record = FindAnswer(question);
            var raw = new ArraySlice<byte>(datagram.Array, datagram.Offset, offset + 4);

            return record != null ? CreateAnswer(record, raw) : null;
        }

        private static byte[] CreateAnswer(ResourceRecord record, ArraySlice<byte> rawQuestion) {
            var rtype = (byte)(record.HostIp.AddressFamily == AddressFamily.InterNetwork ? 0x01 : 0x1C);
            var rdata = record.HostIp.GetAddressBytes();

            var message = new byte[rawQuestion.Length + DNS_ANSWER_SIZE + rdata.Length];
            
            message[0] = rawQuestion[0]; // ID lowbyte
            message[1] = rawQuestion[1]; // ID highbyte
            message[2] = 0x80; // QR flag
            message[5] = 0x01; // Question Count = 1
            message[7] = 0x01; // Answer Count = 1

            // Copy the rest of the question verbatim into the buffer;
            Buffer.BlockCopy(
                rawQuestion.Array, rawQuestion.Offset + DNS_HEADER_SIZE, 
                message, DNS_HEADER_SIZE, 
                rawQuestion.Length - DNS_HEADER_SIZE
            );
            
            var answer = new byte[DNS_ANSWER_SIZE] {
                0xC0, 0x0C, // offset to the question (0xC0, 0x0C means the question starts at offset 12)
                0x00, rtype, // Response type (u16)
                0x00, 0x01, // Class (IN = internet)
                (byte)(record.Ttl >> 24), (byte)(record.Ttl >> 16), (byte)(record.Ttl >> 8), (byte)record.Ttl, // TTL
                (byte)(rdata.Length >> 8), (byte)rdata.Length, // Data length 
            };

            // Copy the answer straight after the original query and add the rdata
            Buffer.BlockCopy(answer, 0, message, rawQuestion.Length, answer.Length);
            Buffer.BlockCopy(rdata, 0, message, rawQuestion.Length + DNS_ANSWER_SIZE, rdata.Length);

            return message;
        }

        private static bool IsValidQuery(UInt16 flags, UInt16 qcount) {
            return qcount > 0 
                && (flags & 0x8000) == 0 // 'QR' flag is not set
                && (flags & 0x7800) == 0; // Regular query type
        }

        private static bool IsValidQuestion(Question question) {
            return question.Type == 0x01 || question.Type == 0x1C;
        }

        private static UInt16 ToUInt16(ArraySlice<byte> buffer, int offset) {
            return (UInt16)((buffer[offset] << 8) | buffer[offset + 1]);
        }

        private struct Question {
            public string Name { get; }
            public UInt16 Type { get; }
            public UInt16 Class { get; }

            public Question(string name, UInt16 type, UInt16 @class) {
                Name = name;
                Type = type;
                Class = @class;
            }
        }
    }
}