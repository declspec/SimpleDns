using System;
using System.Threading.Tasks;
using SimpleDns.Internal;
using Pipeliner;
using System.Collections.Generic;
using System.IO;
using SimpleDns.src.SimpleDns.Pipeline.Features;
using System.Linq;
using System.Threading;

namespace SimpleDns.Pipeline {
    public class LocalDnsMiddleware : IPipelineMiddleware<ISocketContext> {
        private const int ReloadingState = 1;
        private const int IdleState = 0;

        private readonly string _localFile;
        private readonly DnsResponseFactory _responseFactory;

        private IList<ResourceRecord> _cachedRecords;
        private FileSystemWatcher _watcher;
        private DateTime _lastUpdated;
        private int _state;

        public LocalDnsMiddleware(string localFile, DnsResponseFactory responseFactory) {
            _localFile = localFile;
            _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));

            _cachedRecords = ReadResourceRecords(_localFile).ToList();
            _lastUpdated = DateTime.UtcNow;

            _watcher = new FileSystemWatcher(Path.GetDirectoryName(localFile), Path.GetFileName(localFile)) {
                NotifyFilter = NotifyFilters.LastWrite,
                IncludeSubdirectories = false
            };

            _watcher.Changed += new FileSystemEventHandler(OnLocalFileChanged);
            _watcher.EnableRaisingEvents = true;
        }

        public Task Handle(ISocketContext context, PipelineDelegate<ISocketContext> next) {
            var query = context.Features.Get<IDnsQueryFeature>();

            // Ensure the query is available.
            if (query == null)
                return next.Invoke(context);

            // Check if there's a matching record
            var record = _cachedRecords.FirstOrDefault(r => r.Name.IsMatch(query.Name));
            if (record == null)
                return next.Invoke(context);

            var rawQuery = new ArraySlice<byte>(context.Data.Array, context.Data.Offset, query.Size);
            var response = _responseFactory.CreateResponse(record, rawQuery);

            return context.End(new ArraySlice<byte>(response));
        }

        private void OnLocalFileChanged(object sender, FileSystemEventArgs e) {
            // Attempt to take a lock on _state, if it's already Reloading then
            // skip this event as the file is going to be reloaded anyway
            if (Interlocked.CompareExchange(ref _state, ReloadingState, IdleState) == ReloadingState)
                return; 

            try {
                // Sometimes programs will fire multiple 'change' events during a save
                // By remembering the last updated time we can filter out duplicates
                if (_lastUpdated >= File.GetLastWriteTimeUtc(e.FullPath))
                    return;

                var reloaded = false;

                // FileSystemWatcher can sometimes fire events while the event originator
                // still has a lock on the file (i.e a write-lock), to get around this
                // we attempt to read the file every half second until it succeeds.
                // All further file changes will be ignored until this one has been processed.
                while (!reloaded) {
                    try {
                        var newRecords = ReadResourceRecords(e.FullPath).ToList();
                        Interlocked.Exchange(ref _cachedRecords, newRecords);
                        reloaded = true;
                    }
                    catch (IOException) {
                        Thread.Sleep(500);
                    }
                }

                // Set the last updated time
                _lastUpdated = DateTime.UtcNow;
            }
            finally {
                // No need for Interlocked as we now own _state
                _state = IdleState;
            }
        }

        private static IEnumerable<ResourceRecord> ReadResourceRecords(string file) {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read))
            using (var sr = new StreamReader(fs)) {
                string line = null;
                ResourceRecord record = null;

                while ((line = sr.ReadLine()) != null) {
                    if (!line.StartsWith("#@"))
                        continue;

                    line = line.Substring(2).Trim();

                    if (ResourceRecord.TryParse(line, out record))
                        yield return record;
                    // TODO: Add warnings about lines that failed to parse 
                }
            }
        }
    }
}