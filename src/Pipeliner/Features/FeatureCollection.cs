using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Taken from https://github.com/aspnet/HttpAbstractions
namespace Pipeliner.Features {
    /// <summary>
    /// Represents a collection of Pipeline features.
    /// </summary>
    public interface IFeatureCollection : IEnumerable<KeyValuePair<Type, object>> {
        /// <summary>
        /// Indicates if the collection can be modified.
        /// </summary>
        bool IsReadOnly { get; }

        /// <summary>
        /// Incremented for each modification and can be used to verify cached results.
        /// </summary>
        int Revision { get; }

        /// <summary>
        /// Gets or sets a given feature. Setting a null value removes the feature.
        /// </summary>
        /// <param name="key"></param>
        /// <returns>The requested feature, or null if it is not present.</returns>
        object this[Type key] { get; set; }

        /// <summary>
        /// Retrieves the requested feature from the collection.
        /// </summary>
        /// <typeparam name="TFeature">The feature key.</typeparam>
        /// <returns>The requested feature, or null if it is not present.</returns>
        TFeature Get<TFeature>();

        /// <summary>
        /// Checks if the requested feature is available in the collection
        /// </summary>
        /// <typeparam name="TFeature">The feature key.</typeparam>
        /// <returns>True if the requested features is available, false otherwise</returns>
        bool IsAvailable<TFeature>();

        /// <summary>
        /// Sets the given feature in the collection.
        /// </summary>
        /// <typeparam name="TFeature">The feature key.</typeparam>
        /// <param name="instance">The feature value.</param>
        void Set<TFeature>(TFeature instance);
    }

    public class FeatureCollection : IFeatureCollection {
        private static readonly KeyComparer FeatureKeyComparer = new KeyComparer();
        private readonly IFeatureCollection _defaults;
        private IDictionary<Type, object> _features;
        private volatile int _containerRevision;

        public virtual int Revision { get { return _containerRevision + (_defaults?.Revision ?? 0); } }
        public bool IsReadOnly { get { return false; } }

        public FeatureCollection() {
        }

        public FeatureCollection(IFeatureCollection defaults) {
            _defaults = defaults;
        }

        public bool IsAvailable<TFeature>() {
            return Get<TFeature>() != null;
        }

        public object this[Type key] {
            get {
                if (key == null)
                    throw new ArgumentNullException(nameof(key));

                object result;
                return _features != null && _features.TryGetValue(key, out result) ? result : _defaults?[key];
            }
            set {
                if (key == null)
                    throw new ArgumentNullException(nameof(key));

                if (value == null) {
                    if (_features != null && _features.Remove(key))
                        _containerRevision++;
                    return;
                }

                if (_features == null)
                    _features = new Dictionary<Type, object>();

                _features[key] = value;
                _containerRevision++;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() {
            if (_features != null) {
                foreach (var pair in _features) {
                    yield return pair;
                }
            }

            if (_defaults != null) {
                // Don't return features masked by the wrapper.
                var features = _features == null ? _defaults : _defaults.Except(_features, FeatureKeyComparer);
                
                foreach (var pair in features)
                    yield return pair;
            }
        }

        public TFeature Get<TFeature>() {
            return (TFeature)this[typeof(TFeature)];
        }

        public void Set<TFeature>(TFeature instance) {
            this[typeof(TFeature)] = instance;
        }

        private class KeyComparer : IEqualityComparer<KeyValuePair<Type, object>> {
            public bool Equals(KeyValuePair<Type, object> x, KeyValuePair<Type, object> y) {
                return x.Key.Equals(y.Key);
            }

            public int GetHashCode(KeyValuePair<Type, object> obj) {
                return obj.Key.GetHashCode();
            }
        }
    }
}