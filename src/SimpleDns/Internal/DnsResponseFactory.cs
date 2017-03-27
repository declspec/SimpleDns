using System;
using System.Net.Sockets;

namespace SimpleDns.Internal {
    public class DnsResponseFactory {
        private const int DNS_HEADER_SIZE = 0x0C;
        private const int DNS_ANSWER_SIZE = 0x0C;

        public DnsResponseFactory() {

        }

        public byte[] CreateResponse(ResourceRecord record, ArraySlice<byte> question) {
            var rtype = (byte)(record.HostIp.AddressFamily == AddressFamily.InterNetwork ? 0x01 : 0x1C);
            var rdata = record.HostIp.GetAddressBytes();

            var message = new byte[question.Length + DNS_ANSWER_SIZE + rdata.Length];
            
            message[0] = question[0]; // ID lowbyte
            message[1] = question[1]; // ID highbyte
            message[2] = 0x80; // QR flag
            message[5] = 0x01; // Question Count = 1
            message[7] = 0x01; // Answer Count = 1

            // Copy the rest of the question verbatim into the buffer;
            Buffer.BlockCopy(
                question.Array, question.Offset + DNS_HEADER_SIZE, 
                message, DNS_HEADER_SIZE,
                question.Length - DNS_HEADER_SIZE
            );
            
            var answer = new byte[DNS_ANSWER_SIZE] {
                0xC0, 0x0C, // offset to the question (0xC0, 0x0C means the question starts at offset 12)
                0x00, rtype, // Response type (u16)
                0x00, 0x01, // Class (IN = internet)
                (byte)(record.Ttl >> 24), (byte)(record.Ttl >> 16), (byte)(record.Ttl >> 8), (byte)record.Ttl, // TTL
                (byte)(rdata.Length >> 8), (byte)rdata.Length, // Data length 
            };

            // Copy the answer straight after the original query and add the rdata
            Buffer.BlockCopy(answer, 0, message, question.Length, answer.Length);
            Buffer.BlockCopy(rdata, 0, message, question.Length + DNS_ANSWER_SIZE, rdata.Length);

            return message;
        }
    }
}