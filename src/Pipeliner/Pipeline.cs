using System.Threading.Tasks;
using Pipeliner.Builder;

namespace Pipeliner {
    public interface IPipeline<TContext> {
        Task Run(TContext context);
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