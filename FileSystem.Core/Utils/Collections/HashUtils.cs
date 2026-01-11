namespace FileSystem.Core.Utils.Collections
{
    public static class HashUtils
    {
        public static int HashInt(int v)
        {
            unchecked
            {
                uint x = (uint)v;

                x = ((x >> 16) ^ x) * 0x45d9f3b;
                x = ((x >> 16) ^ x) * 0x45d9f3b;
                x = (x >> 16) ^ x;
                
                return (int)x;
            }
        }
    }
}
