using System;
using System.Threading.Tasks;
using Pipeliner;

namespace SimpleDns.Internal {
    public class FinallyMiddleware<TContext> : IPipelineMiddleware<TContext> {
        private readonly PipelineDelegate<TContext> _finally;
    
        public FinallyMiddleware(PipelineDelegate<TContext> handler) {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            _finally = handler;
        }

        public async Task Handle(TContext context, PipelineDelegate<TContext> next) {
            try { await next.Invoke(context); }
            finally { await _finally.Invoke(context); }
        }
    }
}