using System;
using System.Linq;
using System.Collections.Generic;

namespace Pipeliner.Builder {
    public interface IPipelineBuilder<TContext> {
        IPipelineBuilder<TContext> Use(MiddlewareProviderDelegate<TContext> provider);
        IPipelineBuilder<TContext> Use(IPipelineMiddlewareProvider<TContext> provider);  
        IPipelineBuilder<TContext> Use<TProvider>(params object[] args) where TProvider : IPipelineMiddlewareProvider<TContext>;

        IPipelineBuilder<TContext> UseMiddleware(MiddlewareDelegate<TContext> middleware);
        IPipelineBuilder<TContext> UseMiddleware(IPipelineMiddleware<TContext> middleware);
        IPipelineBuilder<TContext> UseMiddleware<TMiddleware>(params object[] args) where TMiddleware : IPipelineMiddleware<TContext>;

        IPipeline<TContext> Build(PipelineDelegate<TContext> final);
    }

    public class PipelineBuilder<TContext> : IPipelineBuilder<TContext> {
        private readonly IList<MiddlewareProviderDelegate<TContext>> _providers;

        public PipelineBuilder() {
            _providers = new List<MiddlewareProviderDelegate<TContext>>();
        }

        public IPipelineBuilder<TContext> Use(MiddlewareProviderDelegate<TContext> provider) {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            _providers.Add(provider);
            return this;
        }

        public IPipelineBuilder<TContext> Use(IPipelineMiddlewareProvider<TContext> provider) {
            if (provider == null)
                throw new ArgumentNullException("provider");

            _providers.Add(provider.Provide);
            return this;
        }

        public IPipelineBuilder<TContext> Use<TProvider>(params object[] args) where TProvider : IPipelineMiddlewareProvider<TContext> {
            var instance = (IPipelineMiddlewareProvider<TContext>)Activator.CreateInstance(typeof(TProvider), args);
            return Use(instance);
        }

        public IPipelineBuilder<TContext> UseMiddleware(MiddlewareDelegate<TContext> middleware) {
            if (middleware == null)
                throw new ArgumentNullException(nameof(middleware));
            
            return Use(next => async context => await middleware.Invoke(context, next));
        }

        public IPipelineBuilder<TContext> UseMiddleware(IPipelineMiddleware<TContext> middleware) {
            if (middleware == null)
                throw new ArgumentNullException(nameof(middleware));

            return UseMiddleware(middleware.Handle);
        }

        public IPipelineBuilder<TContext> UseMiddleware<TMiddleware>(params object[] args) where TMiddleware : IPipelineMiddleware<TContext> {
            var instance = (IPipelineMiddleware<TContext>)Activator.CreateInstance(typeof(TMiddleware), args);
            return UseMiddleware(instance);
        }

        public IPipeline<TContext> Build(PipelineDelegate<TContext> final) {
            if (final == null)
                throw new ArgumentNullException("final");

            return new Pipeline<TContext>(_providers.Reverse().Aggregate(final, (app, provider) => provider(app)));
        }
    }
}