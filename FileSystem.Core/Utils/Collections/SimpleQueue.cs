namespace FileSystem.Core.Utils.Collections
{
    public class SimpleQueue<T>
    {
        private T[] _items;
        private int _head;
        private int _tail;
        public int Count { get; private set; }

        public SimpleQueue(int capacity = 4)
        {
            _items = new T[Math.Max(capacity, 4)];
            _head = 0;
            _tail = 0;
            Count = 0;
        }

        public void Enqueue(T item)
        {
            if (Count == _items.Length) Resize(_items.Length * 2);

            _items[_tail++] = item;

            if (_tail == _items.Length)
            {
                _tail = 0;
            }

            Count++;
        }

        public T Dequeue()
        {
            if (Count == 0) throw new InvalidOperationException("Queue is empty");

            var v = _items[_head];
            _items[_head] = default!;
            _head++;

            if (_head == _items.Length)
            {
                _head = 0;
            }

            Count--;

            return v;
        }

        private void Resize(int newSize)
        {
            var n = new T[newSize];
            for (int i = 0; i < Count; i++)
            {
                n[i] = _items[(_head + i) % _items.Length];
            }

            _items = n;
            _head = 0;
            _tail = Count;
        }
    }
}
