namespace FileSystem.Core.Utils.Collections
{
    public class SimpleList<T>
    {
        private T[] _items;
        public int Count { get; private set; }

        public SimpleList(int capacity = 4)
        {
            _items = new T[Math.Max(capacity, 4)];
            Count = 0;
        }

        public void Add(T item)
        {
            if (Count == _items.Length) Resize(_items.Length * 2);

            _items[Count++] = item;
        }

        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));

                return _items[index];
            }
            set
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));

                _items[index] = value;
            }
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));

            for (int i = index; i < Count - 1; i++)
            {
                _items[i] = _items[i + 1];
            }

            Count--;

            _items[Count] = default!;
        }

        public bool Remove(T item)
        {
            int idx = IndexOf(item);

            if (idx >= 0) { RemoveAt(idx); return true; }

            return false;
        }

        public int IndexOf(T item)
        {
            var comp = EqualityComparer<T>.Default;

            for (int i = 0; i < Count; i++)
            {
                if (comp.Equals(_items[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        public void Clear()
        {
            for (int i = 0; i < Count; i++)
            {
                _items[i] = default!;
            }

            Count = 0;
        }

        public T[] ToArray()
        {
            var arr = new T[Count];

            Array.Copy(_items, 0, arr, 0, Count);

            return arr;
        }

        private void Resize(int newSize)
        {
            var n = new T[newSize];

            Array.Copy(_items, n, Count);
            
            _items = n;
        }
    }
}
