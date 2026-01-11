namespace FileSystem.Core.Utils.Collections
{
    public class SimpleHashTableInt<TValue>
    {
        private SimpleList<Entry>[] _buckets;
        private int _count;

        private class Entry
        {
            public int Key;
            public TValue Value = default!;
        }

        public SimpleHashTableInt(int capacity = 16)
        {
            var size = 1;
            while (size < capacity)
            {
                size <<= 1;
            }

            _buckets = new SimpleList<Entry>[size];

            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i] = new SimpleList<Entry>(2);
            }
            _count = 0;
        }

        private int BucketIndex(int key)
        {
            var h = HashUtils.HashInt(key);
            return (h & 0x7fffffff) & (_buckets.Length - 1);
        }

        public void Put(int key, TValue value)
        {
            var idx = BucketIndex(key);
            var bucket = _buckets[idx];
            for (int i = 0; i < bucket.Count; i++)
            {
                var e = bucket[i];
                if (e.Key == key)
                {
                    e.Value = value;
                    return;
                }
            }
            var ne = new Entry { Key = key, Value = value };
            bucket.Add(ne);
            _count++;
        }

        public bool TryGet(int key, out TValue value)
        {
            var idx = BucketIndex(key);
            var bucket = _buckets[idx];
            for (int i = 0; i < bucket.Count; i++)
            {
                var e = bucket[i];
                if (e.Key == key)
                {
                    value = e.Value;
                    return true;
                }
            }
            value = default!;
            return false;
        }

        public bool ContainsKey(int key)
        {
            var idx = BucketIndex(key);
            var bucket = _buckets[idx];
            for (int i = 0; i < bucket.Count; i++)
            {
                if (bucket[i].Key == key)
                {
                    return true;
                }
            }
            return false;
        }

        public bool Remove(int key)
        {
            var idx = BucketIndex(key);
            var bucket = _buckets[idx];
            for (int i = 0; i < bucket.Count; i++)
            {
                if (bucket[i].Key == key)
                {
                    bucket.RemoveAt(i);
                    _count--;
                    return true;
                }
            }
            return false;
        }
    }
}
