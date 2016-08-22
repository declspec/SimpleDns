using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SimpleDns.Internal {
    public class SparsePool<T> where T : class {
        private readonly Stack<T> _available;
        private readonly Stack<int> _freeIndexes;
        private readonly List<T> _resources;
        private readonly Queue<TaskCompletionSource<T>> _waiting;
        private readonly Func<int, T> _factory;
        private readonly int _capacity;

        public SparsePool(int capacity, Func<int, T> factory) {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            
            _capacity = capacity;
            _factory = factory;
            _resources = new List<T>(capacity);
            _available = new Stack<T>(capacity);
            _waiting = new Queue<TaskCompletionSource<T>>();
            _freeIndexes = new Stack<int>();
        }
    
        public async Task<T> Acquire() {
            TaskCompletionSource<T> tcs = null;

            lock(_available) {
                if (_available.Count > 0 && _waiting.Count == 0)
                    return _available.Pop();
                else if (_waiting.Count == 0 && (_freeIndexes.Count > 0 || _resources.Count < _capacity)) {
                    var pos = _freeIndexes.Count > 0
                        ? _freeIndexes.Pop()
                        : _resources.Count;

                    var resource = _factory.Invoke(pos);
                    if (resource == null)
                        throw new NullReferenceException("resource factory returned null");

                    if (pos < _resources.Count)
                        _resources[pos] = resource;
                    else 
                        _resources.Add(resource);

                    return resource;
                }
             
                tcs = new TaskCompletionSource<T>();
                _waiting.Enqueue(tcs);
            }

            return await tcs.Task;
        }
    
        public void Release(T resource) {
            lock(_available) {
                if (_waiting.Count == 0) 
                    _available.Push(resource);
                else {
                    var tcs = _waiting.Dequeue();
                    tcs.SetResult(resource);
                }
            }
        }

        // Drop a resource out of the pool. This will allow another
        // resource to be created and take its place
        public void Remove(T resource) {
            lock(_available) {
                var idx = _resources.IndexOf(resource);
                if (idx >= 0) {
                    _resources[idx] = null;
                    _freeIndexes.Push(idx);
                }
            }
        }
    }
}