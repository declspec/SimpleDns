using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SimpleDns.Internal {
    public class AsyncObjectPool<T> where T : class {
        private readonly PoolItem[] _pool;
        private readonly Func<int, T> _factory;
        private readonly LinkedList<TaskCompletionSource<T>> _waiting;
        private T _first;
    
        public AsyncObjectPool(int capacity, Func<int, T> factory) {
            _first = null;
            _pool = new PoolItem[capacity - 1];
            _waiting = new LinkedList<TaskCompletionSource<T>>();
            _factory = factory;
        }

        public async Task<T> Acquire(CancellationToken token) {
            T instance = _first;
            if (instance != null && instance == Interlocked.CompareExchange(ref _first, null, instance))
                return instance;

            return await AcquireSlow(token);
        }

        private async Task<T> AcquireSlow(CancellationToken token) {
            var localPool = _pool;
            var instance = default(T);

            for(int i = 0; i < localPool.Length; ++i) {
                if ((instance = Interlocked.CompareExchange(ref localPool[i].Value, null, localPool[i].Value)) != null)
                    return instance;
            }

            var tcs = new TaskCompletionSource<T>();

            lock(_waiting) 
                _waiting.AddLast(tcs);

            if (!token.CanBeCanceled)
                return await tcs.Task;

            // Configure cancellation handling
            var cancellationHandler = new Action(() => {
                if (!tcs.Task.IsCompleted) {
                    lock(_waiting)
                        _waiting.Remove(tcs);
                    tcs.TrySetCanceled();
                }
            });

            using(token.Register(cancellationHandler))
                return await tcs.Task.ConfigureAwait(false);
        }

        public void Free(T obj) {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            // Deliberately avoid taking a lock on _waiting pre-emptively. Worst case
            // scenario is a recently added "waiter" gets skipped until the next
            // resource is free'd which is extremely unlikely.
            if (_waiting.Count > 0) {
                TaskCompletionSource<T> tcs;

                lock(_waiting) {
                    tcs = _waiting.First.Value;
                    _waiting.RemoveFirst();
                }

                tcs.TrySetResult(obj);
            }
            else {
                if (Interlocked.CompareExchange(ref _first, obj, null) != null)
                    FreeSlow(obj);
            }
        }

        private void FreeSlow(T obj) {
            var localPool = _pool;

            for(int i = 0; i < localPool.Length; ++i) {
                if (Interlocked.CompareExchange(ref localPool[i].Value, obj, null) == null)
                    break;
            }
        }

        private struct PoolItem {
            public T Value;
        }
    }

}