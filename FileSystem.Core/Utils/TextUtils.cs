namespace FileSystem.Core.Utils
{
    public static class TextUtils
    {
        public static Collections.SimpleList<string> Split(string input, char separator, bool removeEmpty = false)
        {
            var parts = new Collections.SimpleList<string>();

            if (input == null) return parts;

            int start = 0;
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == separator)
                {
                    int len = i - start;
                    if (len > 0 || !removeEmpty)
                    {
                        parts.Add(Substring(input, start, len));
                    }
                    start = i + 1;
                }
            }

            if (start <= input.Length)
            {
                int len = input.Length - start;
                if (len > 0 || !removeEmpty)
                {
                    parts.Add(Substring(input, start, len));
                }
            }

            return parts;
        }

        public static int IndexOfAny(string input, char[] any)
        {
            if (IsNullOrEmpty(input) || any == null || any.Length == 0) return -1;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                for (int j = 0; j < any.Length; j++)
                {
                    if (c == any[j]) return i;
                }
            }

            return -1;
        }

        public static bool IsNullOrEmpty(string? s)
        {
            return s == null || s.Length == 0;
        }

        public static bool IsNullOrWhiteSpace(string? s)
        {
            if (s == null) return true;

            for (int i = 0; i < s.Length; i++)
            {
                if (!IsWhiteSpace(s[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static string Trim(string s)
        {
            if (s == null) return "";

            int start = 0;
            int end = s.Length - 1;

            while (start <= end && IsWhiteSpace(s[start]))
            {
                start++;
            }

            while (end >= start && IsWhiteSpace(s[end]))
            {
                end--;
            }

            return Substring(s, start, end - start + 1);
        }

        private static bool IsWhiteSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\f' || c == '\v';
        }

        public static bool StartsWith(string s, string prefix)
        {
            if (s == null || prefix == null || prefix.Length > s.Length) return false;

            for (int i = 0; i < prefix.Length; i++)
            {
                if (s[i] != prefix[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static string Substring(string s, int start, int length)
        {
            if (s == null || length <= 0 || start >= s.Length) return "";

            if (start < 0) start = 0;

            int max = s.Length - start;
            int len = length > max ? max : length;
            var buf = new char[len];

            for (int i = 0; i < len; i++)
            {
                buf[i] = s[start + i];
            }

            return new string(buf);
        }

        public static int CompareIgnoreCase(string a, string b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int la = a.Length;
            int lb = b.Length;
            int l = la < lb ? la : lb;

            for (int i = 0; i < l; i++)
            {
                char ca = ToLowerAscii(a[i]);
                char cb = ToLowerAscii(b[i]);

                if (ca < cb) return -1;
                if (ca > cb) return 1;
            }

            if (la < lb) return -1;
            if (la > lb) return 1;

            return 0;
        }

        private static char ToLowerAscii(char c)
        {
            if (c >= 'A' && c <= 'Z') return (char)(c - 'A' + 'a');
            return c;
        }

        public static string ToLower(string s)
        {
            if (s == null) return "";

            var buf = new char[s.Length];

            for (int i = 0; i < s.Length; i++)
            {
                buf[i] = ToLowerAscii(s[i]);
            }

            return new string(buf);
        }
        public static string ReplaceChar(string s, char oldChar, char newChar)
        {
            if (s == null) return "";

            var buf = new char[s.Length];

            for (int i = 0; i < s.Length; i++)
            {
                buf[i] = s[i] == oldChar ? newChar : s[i];
            }

            return new string(buf);
        }

        public static bool IsPathRooted(string? path)
        {
            if (IsNullOrEmpty(path)) return false;

            if (StartsWith(path!, "\\\\")) return true;

            if (StartsWith(path!, "/") || StartsWith(path!, "\\")) return true;

            if (path!.Length >= 2 && path[1] == ':') return true;

            return false;
        }

        public static string GetDirectoryName(string? path)
        {
            if (IsNullOrEmpty(path)) return "";

            int lastSlash = -1;

            for (int i = path!.Length - 1; i >= 0; i--)
            {
                char c = path[i];
                if (c == '/' || c == '\\')
                {
                    lastSlash = i;
                    break;
                }
            }

            if (lastSlash <= 0) return "";

            return Substring(path, 0, lastSlash);
        }

        public static string CombinePaths(string a, string b)
        {
            if (IsNullOrEmpty(a)) return b ?? "";
            if (IsNullOrEmpty(b)) return a ?? "";
            if (IsPathRooted(b)) return b!;

            char sep = '/';
            for (int i = 0; i < a!.Length; i++)
            {
                if (a[i] == '\\')
                {
                    sep = '\\';
                    break;
                }
                if (a[i] == '/')
                {
                    sep = '/';
                    break;
                }
            }

            bool aEnds = a[a.Length - 1] == sep || a[a.Length - 1] == '/' || a[a.Length - 1] == '\\';

            if (aEnds) return a + b;

            return a + sep + b;
        }

        public static string TrimEnd(string s, char ch)
        {
            if (s == null) return "";

            int end = s.Length - 1;

            while (end >= 0 && s[end] == ch)
            {
                end--;
            }

            return Substring(s, 0, end + 1);
        }

        public static bool EqualsOrdinal(string a, string b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null || a.Length != b.Length) return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
