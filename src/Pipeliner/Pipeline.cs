using Pipeliner.Features;
using System.Threading.Tasks;

namespace Pipeliner {
    public delegate Task PipelineDelegate<TContext>(TContext context);
    public delegate Task MiddlewareDelegate<TContext>(TContext context, PipelineDelegate<TContext> next);
    public delegate PipelineDelegate<TContext> MiddlewareProviderDelegate<TContext>(PipelineDelegate<TContext> next);

    public interface IPipeline<TContext> {
        Task Run(TContext context);
    }

    public interface IPipelineMiddleware<TContext> {
        Task Handle(TContext context, PipelineDelegate<TContext> next);
    }

    public interface IPipelineMiddlewareProvider<TContext> {
        PipelineDelegate<TContext> Provide(PipelineDelegate<TContext> next);
    }

    public class Pipeline<TContext> : IPipeline<TContext> {
        private readonly PipelineDelegate<TContext> _pipeline;

        public Pipeline(PipelineDelegate<TContext> pipeline) {
            _pipeline = pipeline;
        }

        public Task Run(TContext context) {
            return _pipeline.Invoke(context);
        }
    }
}