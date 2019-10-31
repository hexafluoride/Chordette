using System;
using System.Collections.Generic;
using System.Text;

namespace Chordette
{
    public class HandlerDictionary<K, V> : Dictionary<K, List<V>>
    {
        public HandlerDictionary()
        {
        }

        public void Add(K key, V value)
        {
            if (!ContainsKey(key))
                this[key] = new List<V>();

            this[key].Add(value);
        }

        public bool Remove(K key, V value)
        {
            if (!ContainsKey(key))
                return false;

            return this[key].Remove(value);
        }
    }
}
