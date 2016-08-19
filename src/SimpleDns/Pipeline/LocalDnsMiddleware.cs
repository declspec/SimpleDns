using System;
using System.Threading.Tasks;
using SimpleDns.Internal;
using Pipeliner.Builder;

namespace SimpleDns.Pipeline {
    public class LocalDnsMiddleware : IPipelineMiddleware<ISocketContext> {
        private readonly DnsResponseFactory _responseFactory;

        public LocalDnsMiddleware(DnsResponseFactory responseFactory) {
            if (responseFactory == null)
                throw new ArgumentNullException(nameof(responseFactory));

            _responseFactory = responseFactory;
        }

        public async Task Handle(ISocketContext context, PipelineDelegate<ISocketContext> next) {
            var response = _responseFactory.GetResponse(context.Data);
            await (response == null ? next.Invoke(context) : context.End(new ArraySlice<byte>(response)));
        }
    }
}