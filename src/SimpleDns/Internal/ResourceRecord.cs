using System;
using System.Net;
using System.Text.RegularExpressions;

namespace SimpleDns.Internal {
    public class ResourceRecord {
        private static Regex RecordPattern = new Regex(@"^(?<ip>[0-9a-fA-F:.]+)\s+(?<re>[^\s]+)(?:\s+\[(?<o>[^\]]+\])?", RegexOptions.Compiled);
        private const uint DefaultTtl = 1800;

        public IPAddress HostIp { get; }
        public UInt32 Ttl { get; }
        public Regex Name { get; }
        
        public ResourceRecord(IPAddress hostIp, UInt32 ttl, Regex name) {
            HostIp = hostIp;
            Ttl = ttl;
            Name = name;
        }

        public static ResourceRecord Parse(string str) {
            var parts = str.Split((string[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                throw new FormatException("Malformed resource record. Must contain at least an IP and a Host");
            
            IPAddress ip;
            Regex re;

            if (!IPAddress.TryParse(parts[0], out ip))
                throw new FormatException(string.Format("'{0}' is not in a valid IP Address format", parts[0]));
            
            try {
                re = new Regex(parts[1]);
            }
            catch(Exception) {
                throw new FormatException(string.Format("'{0}' is not a valid regular expression", parts[1]));
            }

            if (parts.Length < 3 || parts[2][0] == '#')
                return new ResourceRecord(ip, DefaultTtl, re);

            var optblock = parts[2];
            if (optblock[0] != '[' || optblock[optblock.Length - 1] != ']') 
                throw new FormatException(string.Format("'{0}' is not a valid option block", parts[2]));
            
            // Parse the options
            var ttl = DefaultTtl;

            foreach(var optstr in optblock.Substring(1, optblock.Length - 2).Split(',')) {
                var idx = optstr.IndexOf('=');
                var opt = idx > 0 ? optstr.Substring(0, idx) : optstr;
                var val = idx > 0 && idx < optstr.Length - 1 ? optstr.Substring(idx + 1) : null;

                if (opt == "ttl" && !uint.TryParse(val, out ttl))
                    throw new FormatException("Invalid value specified for the 'ttl' option; must be a valid positive integer");
            }

            return new ResourceRecord(ip, ttl, re);
        }

        public static bool TryParse(string str, out ResourceRecord record) {
            try {
                record = Parse(str);
                return true;
            }
            catch(FormatException) {
                record = null;
                return false;
            }
        }
    } 
}