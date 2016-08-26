using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SimpleDns.Internal {
    public class AsyncObjectPool<T> where T : class {
        private const int OBJECT_BORROWED = 1;
        private const int OBJECT_AVAILABLE = 0;

        private readonly PoolItem[] _pool;
        private readonly Func<int, T> _factory;
        private readonly LinkedList<TaskCompletionSource<T>> _waiting;
    
        public AsyncObjectPool(int capacity, Func<int, T> factory) {
            _pool = new PoolItem[capacity];
            _waiting = new LinkedList<TaskCompletionSource<T>>();
            _factory = factory;
        }

        public async Task<T> Acquire(CancellationToken token) {
            var localPool = _pool;

            for(int i = 0; i < localPool.Length; ++i) {
                if (Interlocked.CompareExchange(ref localPool[i].Borrowed, OBJECT_BORROWED, OBJECT_AVAILABLE) == OBJECT_AVAILABLE) {
                    return localPool[i].Value != null
                        ? localPool[i].Value
                        : (localPool[i].Value = _factory(i));
                }
            }

            var tcs = new TaskCompletionSource<T>();

            lock(_waiting) 
                _waiting.AddLast(tcs);

            if (!token.CanBeCanceled)
                return await tcs.Task.ConfigureAwait(false);

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
            if (_waiting.Count == 0) {
                for(int i = 0; i < _pool.Length; ++i) {
                    // Each object should only be owned by one user at a time,
                    // which means this read shouldn't need to be interlocked
                    if (_pool[i].Value == obj) {
                        Interlocked.Exchange(ref _pool[i].Borrowed, OBJECT_AVAILABLE);
                        break;
                    }
                }
            }
            else {
                TaskCompletionSource<T> tcs = null;

                lock(_waiting) {
                    // Double-read 'Count' as we may have lost a contentious lock
                    // with only a single item in the list since our last read.
                    if (_waiting.Count > 0) {
                        tcs = _waiting.First.Value;
                        _waiting.RemoveFirst();
                    }
                }

                if (tcs != null)
                    tcs.SetResult(obj);
            }
        }

        public void Remove(T obj) {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            // Same semantics as 'Free' in that only one PoolItem can own this resource
            // at a time so there should be no need for interlocked reads
            int i = 0;
            for (; i < _pool.Length; ++i) {
                if (_pool[i].Value == obj)
                    break;
            }

             // Was the item even in the pool?
            if (i >= _pool.Length)
                return;

            TaskCompletionSource<T> tcs = null;

            // If there are any pending 'waiters' we can allocate one a new instance
            // and resolve it right away. 

            // PERF: Can remove this if you don't care too much about the order that 
            //  waiters are resolved. Without this, the PoolItem will be claimed by the
            //  next call to 'Acquire' and the waiter will have to wait for a resource from
            //  'Free' instead. There is the potential for a deadlock if there are waiters,
            //  no further calls to Acquire occur, and every single current object is removed rather than freed.
            if (_waiting.Count > 0) {
                lock(_waiting) {
                    // Double-read 'Count' as we may have lost a contentious lock
                    // with only a single item in the list since our last read.
                    if (_waiting.Count > 0) {
                        tcs = _waiting.First.Value;
                        _waiting.RemoveFirst();
                    }
                }
            }

            if (tcs != null) 
                tcs.SetResult(_pool[i].Value = _factory(i));
            else {
                // Noone needs an object right now, just free the item slot
                _pool[i].Value = null;
                Interlocked.Exchange(ref _pool[i].Borrowed, OBJECT_AVAILABLE);
            }
        }

        private struct PoolItem {
            public T Value;
            public int Borrowed;
        }
    }
}
