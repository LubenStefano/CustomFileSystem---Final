namespace FileSystem.Core.Utils.Collections
{
    public class SimpleSetInt
    {
        private readonly SimpleHashTableInt<bool> _table;

        public SimpleSetInt(int capacity = 16)
        {
            _table = new SimpleHashTableInt<bool>(capacity);
        }

        public void Add(int v)
        {
            _table.Put(v, true);
        }

        public bool Contains(int v)
        {
            return _table.ContainsKey(v);
        }
    }
}
