using System;
using System.Collections.Generic;
using HS2StudioBrowser.Files;
using System.IO;
using System.Text;

namespace HS2StudioBrowser.Outfits
{
    /// <summary>
    /// Reads and changes the outfit name stored inside an HS2 coordinate (outfit) card without loading the
    /// clothes. A card is: PNG image, int product number, "【AIS_Clothes】", version string, int language,
    /// outfit name (the name Studio's Costume list shows), then the clothes/accessory data, then any plugin
    /// data (ExtensibleSaveFormat appends it after the game's data). Renaming rewrites only the name, so
    /// the picture, the clothes and every plugin's data stay byte-for-byte the same.
    /// </summary>
    internal static class CoordinateCard
    {
        public const string Tag = "【AIS_Clothes】";

        public sealed class Header
        {
            public string Name;
            public long NameStart;
            public long NameEnd;
        }

        /// <summary>Clothing slots in the game's order (ChaFileDefine.ClothesKind).</summary>
        public static readonly string[] Slots = { "Top", "Bottom", "Bra", "Panties", "Gloves", "Pantyhose", "Socks", "Shoes" };

        /// <summary>
        /// Which clothing slots the outfit fills (id other than 0) and how many accessory slots it uses
        /// (type other than 350, "none"), from the card's clothes and accessory MessagePack blocks.
        /// Null if it can't be read. Accessories past the game's 20 slots (MoreAccessories) aren't counted.
        /// </summary>
        public static KeyValuePair<List<string>, int>? ReadParts(string path)
        {
            var header = ReadHeader(path);
            if (header == null) return null;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs, Encoding.UTF8))
            {
                fs.Position = header.NameEnd;
                var count = br.ReadInt32();
                if (count <= 8 || count > fs.Length - fs.Position) return null;
                var data = br.ReadBytes(count);
                var clothesLen = BitConverter.ToInt32(data, 0);
                if (clothesLen <= 0 || 4 + clothesLen + 4 > data.Length) return null;
                var clothes = new byte[clothesLen];
                Buffer.BlockCopy(data, 4, clothes, 0, clothesLen);
                var accLen = BitConverter.ToInt32(data, 4 + clothesLen);
                byte[] acc = null;
                if (accLen > 0 && 8 + clothesLen + accLen <= data.Length)
                {
                    acc = new byte[accLen];
                    Buffer.BlockCopy(data, 8 + clothesLen, acc, 0, accLen);
                }

                var parts = new List<string>();
                var slots = MsgPackLite.FindInMap(clothes, 0, "parts");
                var list = slots != null ? MsgPackLite.Elements(clothes, slots.Start) : null;
                if (list == null) return null;
                for (var i = 0; i < list.Count && i < Slots.Length; i++)
                {
                    var id = MsgPackLite.FindInMap(clothes, list[i], "id");
                    if (id == null) continue;
                    var p = id.Start;
                    if (MsgPackLite.Int(clothes, ref p) != 0) parts.Add(Slots[i]);
                }

                var accessories = 0;
                if (acc != null)
                {
                    var ap = MsgPackLite.FindInMap(acc, 0, "parts");
                    var al = ap != null ? MsgPackLite.Elements(acc, ap.Start) : null;
                    if (al != null)
                        foreach (var start in al)
                        {
                            var type = MsgPackLite.FindInMap(acc, start, "type");
                            if (type == null) continue;
                            var p = type.Start;
                            if (MsgPackLite.Int(acc, ref p) != 350) accessories++;
                        }
                }
                return new KeyValuePair<List<string>, int>(parts, accessories);
            }
        }

        /// <summary>The outfit name, or null if the file isn't an outfit card.</summary>
        public static string ReadName(string path) => ReadHeader(path)?.Name;

        public static Header ReadHeader(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var br = new BinaryReader(fs, Encoding.UTF8))
                {
                    PngFile.SkipPng(br);
                    if (fs.Length - fs.Position < 8) return null;
                    var product = br.ReadInt32();
                    if (product > 100) return null;
                    if (br.ReadString() != Tag) return null;
                    br.ReadString(); // version
                    br.ReadInt32();  // language
                    var start = fs.Position;
                    var name = br.ReadString();
                    return new Header { Name = name, NameStart = start, NameEnd = fs.Position };
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Changes the outfit name inside the card. Writes a new copy next to the file, checks it reads back,
        /// then swaps it in. The file keeps its modified date, so Studio's date sort doesn't move it.
        /// </summary>
        public static void SetName(string path, string newName)
        {
            if (newName == null) throw new ArgumentNullException(nameof(newName));
            var header = ReadHeader(path) ?? throw new InvalidDataException("not an HS2 outfit card");
            if (header.Name == newName) return;

            var bytes = File.ReadAllBytes(path);
            var modified = File.GetLastWriteTimeUtc(path);
            var tmp = path + ".obtmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                using (var bw = new BinaryWriter(fs, new UTF8Encoding(false)))
                {
                    bw.Write(bytes, 0, (int)header.NameStart);
                    bw.Write(newName);
                    bw.Write(bytes, (int)header.NameEnd, bytes.Length - (int)header.NameEnd);
                }

                var check = ReadHeader(tmp);
                if (check == null || check.Name != newName)
                    throw new IOException("the renamed copy didn't read back; the original was left alone");

                File.SetLastWriteTimeUtc(tmp, modified);
                Swap(tmp, path);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            }
        }

        /// <summary>The plugin-data block (ExtendedSave) after the outfit's own data, or null if there is none.</summary>
        public static byte[] ReadPluginData(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs, Encoding.UTF8))
            {
                PngFile.SkipPng(br);
                if (br.ReadInt32() > 100) return null;
                if (br.ReadString() != Tag) return null;
                br.ReadString();  // version
                br.ReadInt32();   // language
                br.ReadString();  // outfit name
                var n = br.ReadInt32();
                if (n < 0 || n > fs.Length - fs.Position) return null;
                fs.Seek(n, SeekOrigin.Current);
                if (fs.Length - fs.Position < 9) return null;
                var marker = br.ReadString();
                br.ReadInt32();   // data version
                var len = br.ReadInt32();
                if (marker != "KKEx" || len <= 0 || len > fs.Length - fs.Position) return null;
                return br.ReadBytes(len);
            }
        }

        private static void Swap(string tmp, string path)
        {
            try
            {
                File.Replace(tmp, path, null);
                return;
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException || ex is NotImplementedException || ex is IOException)
            {
                // Fall through to delete + move.
            }

            File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
