using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HS2StudioBrowser.Files;

namespace HS2StudioBrowser.Scenes
{
    /// <summary>
    /// Byte-level reading of Studio scene files, with no game or Sideloader types (so it runs anywhere, worker
    /// threads included): a 320 x 180 PNG, then the scene (version string, objects, characters embedded as full
    /// character data, map, lighting...), then ExtendedSave's plugin data at the very end ("KKEx", version, length).
    /// </summary>
    internal static class SceneBytes
    {
        private static readonly byte[] KKExMarker = { 4, (byte)'K', (byte)'K', (byte)'E', (byte)'x' };

        /// <summary>The file name (without .png) if the file is a Studio scene, otherwise null.</summary>
        public static string ReadName(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var br = new BinaryReader(fs, Encoding.UTF8))
                {
                    PngFile.SkipPng(br);
                    if (fs.Length - fs.Position < 6) return null;
                    var first = br.ReadByte();
                    if (first < 3 || first > 16) return null;  // a short version string such as "1.1.1"
                    fs.Seek(-1, SeekOrigin.Current);
                    var v = br.ReadString();
                    Version version;
                    try { version = new Version(v); }
                    catch { return null; }
                    if (version.Major > 9) return null;
                    return Path.GetFileNameWithoutExtension(path);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>ExtendedSave's block at the end of the file: the last "KKEx" whose length runs exactly to the end.</summary>
        public static byte[] PluginDataIn(byte[] b)
        {
            for (var i = LastIndexOf(b, KKExMarker, b.Length - 1); i >= 0; i = LastIndexOf(b, KKExMarker, i - 1))
            {
                if (i + 13 > b.Length) continue;
                var len = BitConverter.ToInt32(b, i + 9);
                if (len > 0 && i + 13 + (long)len == b.Length)
                {
                    var data = new byte[len];
                    Array.Copy(b, i + 13, data, 0, len);
                    return data;
                }
            }
            return null;
        }

        /// <summary>How many female and male characters the scene holds (each embedded character's Parameter "sex").</summary>
        public static KeyValuePair<int, int> CastOf(byte[] b)
        {
            int f = 0, m = 0;
            using (var ms = new MemoryStream(b, false))
            {
                var tag = CharaCard.TagBytes;
                for (var i = IndexOf(b, tag, 0); i >= 0; i = IndexOf(b, tag, i + tag.Length))
                {
                    try
                    {
                        if (i < 4 || BitConverter.ToInt32(b, i - 4) > 100) continue;
                        ms.Position = i + tag.Length;
                        var layout = CharaCard.ReadLayoutAfterTag(ms);
                        if (layout == null) continue;
                        var sex = CharaCard.SexIn(CharaCard.ReadBlock(ms, layout, "Parameter"));
                        if (sex == 1) f++;
                        else if (sex == 0) m++;
                    }
                    catch
                    {
                        // Not a character after all: skip it.
                    }
                }
            }
            return new KeyValuePair<int, int>(f, m);
        }

        /// <summary>
        /// The cast as the folder names people use: F, FF, FFF, M, MM, MMM; mixed ones with the larger side
        /// first and M first on a tie (MF, FFM, MMF); "4+" for four or more, "None" for an empty scene.
        /// </summary>
        public static string CastLabel(int f, int m)
        {
            var n = f + m;
            if (n == 0) return "None";
            if (n >= 4) return "4+";
            if (m == 0) return new string('F', f);
            if (f == 0) return new string('M', m);
            return f > m ? new string('F', f) + new string('M', m) : new string('M', m) + new string('F', f);
        }

        /// <summary>Characters embedded in a scene: (name, plugin-data block) for each, found by their card tag.</summary>
        public static List<KeyValuePair<string, byte[]>> EmbeddedCharacters(byte[] b)
        {
            var list = new List<KeyValuePair<string, byte[]>>();
            using (var ms = new MemoryStream(b, false))
            {
                var tag = CharaCard.TagBytes;
                for (var i = IndexOf(b, tag, 0); i >= 0; i = IndexOf(b, tag, i + tag.Length))
                {
                    try
                    {
                        if (i < 4 || BitConverter.ToInt32(b, i - 4) > 100) continue; // product number before the tag
                        ms.Position = i + tag.Length;
                        var layout = CharaCard.ReadLayoutAfterTag(ms);
                        if (layout == null) continue;
                        var name = CharaCard.NameIn(CharaCard.ReadBlock(ms, layout, "Parameter"));
                        list.Add(new KeyValuePair<string, byte[]>(name, CharaCard.ReadBlock(ms, layout, "KKEx")));
                    }
                    catch
                    {
                        // Not a character after all, or a damaged one: skip it.
                    }
                }
            }
            return list;
        }

        private static int IndexOf(byte[] hay, byte[] needle, int start)
        {
            var first = needle[0];
            for (var i = Math.Max(0, start); i <= hay.Length - needle.Length; i++)
            {
                if (hay[i] != first) continue;
                var j = 1;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        private static int LastIndexOf(byte[] hay, byte[] needle, int from)
        {
            var first = needle[0];
            for (var i = Math.Min(from, hay.Length - needle.Length); i >= 0; i--)
            {
                if (hay[i] != first) continue;
                var j = 1;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }
}
