using System;
using System.Threading.Tasks;
using SimpleDns.Internal;
using Pipeliner;
using System.Collections.Generic;
using System.IO;
using SimpleDns.src.SimpleDns.Pipeline.Features;
using System.Linq;

namespace SimpleDns.Pipeline {
    public class LocalDnsMiddleware : IPipelineMiddleware<ISocketContext> {
        private const int CacheLifetime = 60 * 5; // 5 minutes

        private readonly string _localFile;
        private readonly DnsResponseFactory _responseFactory;

        private IList<ResourceRecord> _cachedRecords;
        private DateTime _lastUpdated;

        public LocalDnsMiddleware(string localFile, DnsResponseFactory responseFactory) {
            _localFile = localFile;
            _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        }

        public Task Handle(ISocketContext context, PipelineDelegate<ISocketContext> next) {
            var query = context.Features.Get<IDnsQueryFeature>();

            // Ensure the query is available.
            if (query == null)
                return next.Invoke(context);

            // Ensure the responses are up-to-date
            var now = DateTime.UtcNow;
            if (_cachedRecords == null || now.Subtract(_lastUpdated).TotalSeconds > CacheLifetime) {
                _cachedRecords = ReadResourceRecords(_localFile).ToList();
                _lastUpdated = now;
            }

            // Check if there's a matching record
            var record = _cachedRecords.FirstOrDefault(r => r.Name.IsMatch(query.Name));
            if (record == null)
                return next.Invoke(context);

            var rawQuery = new ArraySlice<byte>(context.Data.Array, context.Data.Offset, query.Size);
            var response = _responseFactory.CreateResponse(record, rawQuery);

            return context.End(new ArraySlice<byte>(response));
        }

        private static IEnumerable<ResourceRecord> ReadResourceRecords(string file) {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read))
            using (var sr = new StreamReader(fs)) {
                string line = null;
                while ((line = sr.ReadLine()) != null) {
                    if (!line.StartsWith("#@"))
                        continue;

                    line = line.Substring(2).Trim();
                    ResourceRecord record = null;

                    try {
                        record = ResourceRecord.Parse(line);
                    }
                    catch (FormatException f) {
                        Console.WriteLine("warning: skipping '{0}' ({1})", line, f.Message);
                    }

                    if (record != null)
                        yield return record;
                }
            }
        }
    }
}