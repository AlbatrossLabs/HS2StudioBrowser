using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HS2StudioBrowser.Files
{
    /// <summary>
    /// Just enough MessagePack to read a few fields out of a card and rewrite single values in place, leaving
    /// every other byte untouched. (The game's own serializer would re-encode whole blocks.) Thread-safe, no
    /// Unity or game types, so it runs on worker threads.
    /// </summary>
    public static class MsgPackLite
    {
        public sealed class Span
        {
            public int Start, End;
            public Span(int start, int end) { Start = start; End = end; }
        }

        /// <summary>Offset just past the value starting at <paramref name="pos"/>.</summary>
        public static int Skip(byte[] b, int pos)
        {
            var t = b[pos];
            if (t <= 0x7f || t >= 0xe0) return pos + 1;                       // fixint
            if (t >= 0x80 && t <= 0x8f) return SkipItems(b, pos + 1, (t & 0x0f) * 2); // fixmap
            if (t >= 0x90 && t <= 0x9f) return SkipItems(b, pos + 1, t & 0x0f);       // fixarray
            if (t >= 0xa0 && t <= 0xbf) return pos + 1 + (t & 0x1f);                  // fixstr
            switch (t)
            {
                case 0xc0: case 0xc2: case 0xc3: return pos + 1;
                case 0xc4: case 0xd9: return pos + 2 + b[pos + 1];
                case 0xc5: case 0xda: return pos + 3 + U16(b, pos + 1);
                case 0xc6: case 0xdb: return pos + 5 + (int)U32(b, pos + 1);
                case 0xc7: return pos + 3 + b[pos + 1];
                case 0xc8: return pos + 4 + U16(b, pos + 1);
                case 0xc9: return pos + 6 + (int)U32(b, pos + 1);
                case 0xca: return pos + 5;
                case 0xcb: return pos + 9;
                case 0xcc: case 0xd0: return pos + 2;
                case 0xcd: case 0xd1: return pos + 3;
                case 0xce: case 0xd2: return pos + 5;
                case 0xcf: case 0xd3: return pos + 9;
                case 0xd4: return pos + 3;
                case 0xd5: return pos + 4;
                case 0xd6: return pos + 6;
                case 0xd7: return pos + 10;
                case 0xd8: return pos + 18;
                case 0xdc: return SkipItems(b, pos + 3, U16(b, pos + 1));
                case 0xdd: return SkipItems(b, pos + 5, (int)U32(b, pos + 1));
                case 0xde: return SkipItems(b, pos + 3, U16(b, pos + 1) * 2);
                case 0xdf: return SkipItems(b, pos + 5, (int)U32(b, pos + 1) * 2);
            }
            throw new InvalidDataException($"unknown MessagePack type 0x{t:x2}");
        }

        private static int SkipItems(byte[] b, int pos, int n)
        {
            for (var i = 0; i < n; i++) pos = Skip(b, pos);
            return pos;
        }

        /// <summary>Reads a map header; false if the value at pos isn't a map.</summary>
        public static bool MapHeader(byte[] b, ref int pos, out int count)
        {
            var t = b[pos];
            if (t >= 0x80 && t <= 0x8f) { count = t & 0x0f; pos += 1; return true; }
            if (t == 0xde) { count = U16(b, pos + 1); pos += 3; return true; }
            if (t == 0xdf) { count = (int)U32(b, pos + 1); pos += 5; return true; }
            count = 0;
            return false;
        }

        public static bool ArrayHeader(byte[] b, ref int pos, out int count)
        {
            var t = b[pos];
            if (t >= 0x90 && t <= 0x9f) { count = t & 0x0f; pos += 1; return true; }
            if (t == 0xdc) { count = U16(b, pos + 1); pos += 3; return true; }
            if (t == 0xdd) { count = (int)U32(b, pos + 1); pos += 5; return true; }
            count = 0;
            return false;
        }

        /// <summary>The string at pos (advancing past it), or null if the value isn't a string.</summary>
        public static string String(byte[] b, ref int pos)
        {
            var t = b[pos];
            int len, head;
            if (t >= 0xa0 && t <= 0xbf) { len = t & 0x1f; head = 1; }
            else if (t == 0xd9) { len = b[pos + 1]; head = 2; }
            else if (t == 0xda) { len = U16(b, pos + 1); head = 3; }
            else if (t == 0xdb) { len = (int)U32(b, pos + 1); head = 5; }
            else if (t == 0xc0) { pos += 1; return null; }
            else { pos = Skip(b, pos); return null; }
            var s = Encoding.UTF8.GetString(b, pos + head, len);
            pos += head + len;
            return s;
        }

        /// <summary>The integer at pos (advancing past it). Throws if the value isn't an integer.</summary>
        public static long Int(byte[] b, ref int pos)
        {
            var t = b[pos];
            long v;
            switch (t)
            {
                case 0xcc: v = b[pos + 1]; pos += 2; return v;
                case 0xcd: v = U16(b, pos + 1); pos += 3; return v;
                case 0xce: v = U32(b, pos + 1); pos += 5; return v;
                case 0xcf: v = (long)U64(b, pos + 1); pos += 9; return v;
                case 0xd0: v = (sbyte)b[pos + 1]; pos += 2; return v;
                case 0xd1: v = (short)U16(b, pos + 1); pos += 3; return v;
                case 0xd2: v = (int)U32(b, pos + 1); pos += 5; return v;
                case 0xd3: v = (long)U64(b, pos + 1); pos += 9; return v;
            }
            if (t <= 0x7f) { pos += 1; return t; }
            if (t >= 0xe0) { pos += 1; return (sbyte)t; }
            throw new InvalidDataException($"expected an integer, found 0x{t:x2}");
        }

        /// <summary>Reads a number (float32, float64 or any integer) as a double.</summary>
        public static double Number(byte[] b, ref int pos)
        {
            var t = b[pos];
            if (t == 0xca)
            {
                var raw = new[] { b[pos + 4], b[pos + 3], b[pos + 2], b[pos + 1] };
                pos += 5;
                return BitConverter.ToSingle(raw, 0);
            }
            if (t == 0xcb)
            {
                var raw = new byte[8];
                for (var i = 0; i < 8; i++) raw[i] = b[pos + 8 - i];
                pos += 9;
                return BitConverter.ToDouble(raw, 0);
            }
            return Int(b, ref pos);
        }

        /// <summary>The numbers in the array at <paramref name="pos"/> (null if it isn't an array).</summary>
        public static List<double> Numbers(byte[] b, int pos)
        {
            if (!ArrayHeader(b, ref pos, out var n)) return null;
            var list = new List<double>(n);
            for (var i = 0; i < n; i++) list.Add(Number(b, ref pos));
            return list;
        }

        /// <summary>The start of each element of the array at <paramref name="pos"/> (null if it isn't an array).</summary>
        public static List<int> Elements(byte[] b, int pos)
        {
            if (!ArrayHeader(b, ref pos, out var n)) return null;
            var list = new List<int>(n);
            for (var i = 0; i < n; i++)
            {
                list.Add(pos);
                pos = Skip(b, pos);
            }
            return list;
        }

        /// <summary>Where the value for <paramref name="key"/> sits in the map at <paramref name="pos"/>, or null.</summary>
        public static Span FindInMap(byte[] b, int pos, string key)
        {
            if (!MapHeader(b, ref pos, out var n)) return null;
            for (var i = 0; i < n; i++)
            {
                var k = String(b, ref pos);
                var start = pos;
                pos = Skip(b, pos);
                if (k == key) return new Span(start, pos);
            }
            return null;
        }

        public static byte[] EncodeString(string s)
        {
            var data = Encoding.UTF8.GetBytes(s ?? "");
            var n = data.Length;
            using (var ms = new MemoryStream(n + 5))
            {
                if (n <= 31) ms.WriteByte((byte)(0xa0 | n));
                else if (n <= 0xff) { ms.WriteByte(0xd9); ms.WriteByte((byte)n); }
                else if (n <= 0xffff) { ms.WriteByte(0xda); ms.WriteByte((byte)(n >> 8)); ms.WriteByte((byte)n); }
                else { ms.WriteByte(0xdb); WriteBE(ms, (ulong)n, 4); }
                ms.Write(data, 0, n);
                return ms.ToArray();
            }
        }

        public static byte[] EncodeInt(long v)
        {
            using (var ms = new MemoryStream(9))
            {
                if (v >= 0)
                {
                    if (v <= 0x7f) ms.WriteByte((byte)v);
                    else if (v <= 0xff) { ms.WriteByte(0xcc); ms.WriteByte((byte)v); }
                    else if (v <= 0xffff) { ms.WriteByte(0xcd); WriteBE(ms, (ulong)v, 2); }
                    else if (v <= 0xffffffffL) { ms.WriteByte(0xce); WriteBE(ms, (ulong)v, 4); }
                    else { ms.WriteByte(0xcf); WriteBE(ms, (ulong)v, 8); }
                }
                else
                {
                    if (v >= -32) ms.WriteByte((byte)(sbyte)v);
                    else if (v >= sbyte.MinValue) { ms.WriteByte(0xd0); ms.WriteByte((byte)(sbyte)v); }
                    else if (v >= short.MinValue) { ms.WriteByte(0xd1); WriteBE(ms, (ulong)(ushort)(short)v, 2); }
                    else if (v >= int.MinValue) { ms.WriteByte(0xd2); WriteBE(ms, (ulong)(uint)(int)v, 4); }
                    else { ms.WriteByte(0xd3); WriteBE(ms, (ulong)v, 8); }
                }
                return ms.ToArray();
            }
        }

        /// <summary>Copies <paramref name="src"/> with each span replaced by its bytes (spans must not overlap).</summary>
        public static byte[] Splice(byte[] src, IList<KeyValuePair<Span, byte[]>> edits)
        {
            var sorted = new List<KeyValuePair<Span, byte[]>>(edits);
            sorted.Sort((a, c) => a.Key.Start.CompareTo(c.Key.Start));
            using (var ms = new MemoryStream(src.Length + 64))
            {
                var at = 0;
                foreach (var e in sorted)
                {
                    if (e.Key.Start < at) throw new ArgumentException("overlapping edits");
                    ms.Write(src, at, e.Key.Start - at);
                    ms.Write(e.Value, 0, e.Value.Length);
                    at = e.Key.End;
                }
                ms.Write(src, at, src.Length - at);
                return ms.ToArray();
            }
        }

        private static int U16(byte[] b, int p) => (b[p] << 8) | b[p + 1];
        private static uint U32(byte[] b, int p) => ((uint)b[p] << 24) | ((uint)b[p + 1] << 16) | ((uint)b[p + 2] << 8) | b[p + 3];
        private static ulong U64(byte[] b, int p) => ((ulong)U32(b, p) << 32) | U32(b, p + 4);

        private static void WriteBE(Stream s, ulong v, int bytes)
        {
            for (var i = bytes - 1; i >= 0; i--) s.WriteByte((byte)(v >> (8 * i)));
        }
    }
}
