using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace San9AutoDomestic.Core.Collections
{
    /// <summary>
    /// A defensive dictionary snapshot that exposes no mutation interface and
    /// remains compatible with the .NET Framework 4.0 reference surface.
    /// </summary>
    public sealed class ReadOnlyMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        private readonly Dictionary<TKey, TValue> _items;
        private readonly ReadOnlyCollection<TKey> _keys;
        private readonly ReadOnlyCollection<TValue> _values;

        public ReadOnlyMap(IDictionary<TKey, TValue> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            _items = new Dictionary<TKey, TValue>(source);
            _keys = new ReadOnlyCollection<TKey>(new List<TKey>(_items.Keys));
            _values = new ReadOnlyCollection<TValue>(new List<TValue>(_items.Values));
        }

        public int Count
        {
            get { return _items.Count; }
        }

        public TValue this[TKey key]
        {
            get { return _items[key]; }
        }

        public ReadOnlyCollection<TKey> Keys
        {
            get { return _keys; }
        }

        public ReadOnlyCollection<TValue> Values
        {
            get { return _values; }
        }

        public bool ContainsKey(TKey key)
        {
            return _items.ContainsKey(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return _items.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
