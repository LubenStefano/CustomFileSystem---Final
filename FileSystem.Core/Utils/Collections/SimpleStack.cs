namespace FileSystem.Core.Utils.Collections
{
    public class SimpleStack<T>
    {
        private T[] _items;
        public int Count { get; private set; }

        public SimpleStack(int capacity = 4)
        {
            _items = new T[Math.Max(capacity, 4)];
            Count = 0;
        }

        public void Push(T item)
        {
            if (Count == _items.Length) Resize(_items.Length * 2);

            _items[Count++] = item;
        }

        public T Pop()
        {
            if (Count == 0) throw new InvalidOperationException("Stack is empty");

            var idx = --Count;
            var v = _items[idx];

            _items[idx] = default!;

            return v;
        }

        public T Peek()
        {
            if (Count == 0) throw new InvalidOperationException("Stack is empty");

            return _items[Count - 1];
        }

        public void Clear()
        {
            for (int i = 0; i < Count; i++)
            {
                _items[i] = default!;
            }

            Count = 0;
        }

        private void Resize(int size)
        {
            var n = new T[size];

            Array.Copy(_items, n, Count);

            _items = n;
        }
    }
}
