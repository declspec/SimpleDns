using System;
using System.Threading;
using System.Collections.Generic;

namespace SimpleDns.Internal {
    // Pretty much yanked out of the ObjectPool implementation in Roslyn
    public class ObjectPool<T> where T : class {
        private readonly PoolItem[] _pool;
        private readonly Func<int, T> _factory;
        private readonly bool _strict;
        private T _first;
        
        public ObjectPool(int capacity, Func<int, T> factory) 
            : this(capacity, false, factory) { }

        public ObjectPool(int capacity, bool strict, Func<int, T> factory) {
            _first = null;
            _pool = new PoolItem[capacity - 1];
            _factory = factory;
        }

        public T Acquire() {
            T instance = _first;
            return instance == null || instance != Interlocked.CompareExchange(ref _first, null, instance)
                ? AcquireSlow()
                : instance;
        }

        private T AcquireSlow() {
            var localPool = _pool;
            var instance = default(T);

            for(int i = 0; i < localPool.Length; ++i) {
                if ((instance = Interlocked.CompareExchange(ref localPool[i].Value, null, localPool[i].Value)) != null)
                    return instance;
            }

            if (_strict)
                throw new InvalidOperationException("No available items in the pool");

            return _factory(-1);
        }

        public void Free(T obj) {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            if (Interlocked.CompareExchange(ref _first, obj, null) != null)
                FreeSlow(obj);
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