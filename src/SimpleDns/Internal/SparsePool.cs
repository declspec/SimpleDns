using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SimpleDns.Internal {
    public class SparsePool<T> where T : class {
        private readonly Stack<T> _available;
        private readonly Stack<int> _freeIndexes;
        private readonly List<T> _resources;
        private readonly LinkedList<TaskCompletionSource<T>> _waiting;
        private readonly Func<int, T> _factory;
        private readonly int _capacity;

        public SparsePool(int capacity, Func<int, T> factory) {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            
            _capacity = capacity;
            _factory = factory;
            
            _resources = new List<T>();
            _available = new Stack<T>();
            _waiting = new LinkedList<TaskCompletionSource<T>>();
            _freeIndexes = new Stack<int>();
        }
    
        public async Task<T> Acquire(CancellationToken token) {
            token.ThrowIfCancellationRequested();
            TaskCompletionSource<T> tcs = null;

            lock(_resources) {
                if (_waiting.Count == 0) {
                    if (_available.Count > 0)
                        return _available.Pop();
                    else {
                        var resource = GetNewResource();
                        if (resource != null)
                            return resource;      
                    }
                }

                tcs = new TaskCompletionSource<T>();
                _waiting.AddLast(tcs);
            }

            if (!token.CanBeCanceled)
                return await tcs.Task.ConfigureAwait(false);

            // Need to clean up the task when the provided
            // token is cancelled.
            var cancellationHandler = new Action(() => {
                if (tcs.Task.IsCompleted)
                    return;

                lock(_resources) {
                    _waiting.Remove(tcs);
                    tcs.TrySetCanceled();
                }
            });

            using (token.Register(cancellationHandler, false))
                return await tcs.Task.ConfigureAwait(false);
        }
    
        public void Release(T resource) {
            lock(_resources) {
                // Could be worth throwing an exception here.
                if (!_resources.Contains(resource))
                    return;

                if (_waiting.Count == 0) 
                    _available.Push(resource);
                else {
                    var tcs = _waiting.First.Value;
                    _waiting.RemoveFirst();
                    tcs.TrySetResult(resource);
                }
            }
        }

        // Drop a resource out of the pool. This will allow another
        // resource to be created and take its place
        // NOTE: Does not do any sort of destruction on the resource.
        public void Remove(T resource) {
            lock(_resources) {
                var idx = _resources.IndexOf(resource);

                if (idx >= 0) {
                    _resources[idx] = null;
                    _freeIndexes.Push(idx);

                    // Since we've removed a resource from the pool we can create a new
                    // one if there are any pending 'Acquire' waiters.
                    if (_waiting.Count > 0 && (resource = GetNewResource()) != null) {
                        var tcs = _waiting.First.Value;
                        _waiting.RemoveFirst();
                        tcs.SetResult(resource);
                    }
                }
            }
        }

        // NOTE: Should be called from within a lock
        private T GetNewResource() {
            if (_freeIndexes.Count == 0 && _resources.Count >= _capacity)
                return null; // Can't get a new resource at this time

            var pos = _freeIndexes.Count > 0
                ? _freeIndexes.Pop()
                : _resources.Count;

            T resource = _factory.Invoke(pos);
            if (resource == null)
                throw new NullReferenceException("resource factory returned null");

            if (pos < _resources.Count)
                _resources[pos] = resource;
            else 
                _resources.Add(resource);

            return resource;
        }
    }
}