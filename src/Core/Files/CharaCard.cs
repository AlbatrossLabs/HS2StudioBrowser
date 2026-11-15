using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HS2StudioBrowser.Files
{
    /// <summary>
    /// Reads and changes the name inside an HS2 character card without loading the character. A card is: PNG
    /// image, int product number, "【AIS_Chara】", version, int language, user ID, data ID, then a block header
    /// (MessagePack list of {name, version, pos, size}), an int64 data length, and the blocks themselves
    /// (Custom, Coordinate, Parameter, GameInfo, Status, Parameter2, GameInfo2, and KKEx for plugin data).
    /// The name is "fullname" in the Parameter block. Renaming rewrites that one value, then shifts the
    /// offsets of the blocks stored after it; every other byte, the picture and all plugin data stay the same.
    /// </summary>
    public static class CharaCard
    {
        public const string Tag = "【AIS_Chara】";

        public sealed class BlockInfo
        {
            public string Name;
            public long Pos, Size;
            public MsgPackLite.Span PosSpan, SizeSpan; // where the values sit in the header bytes
        }

        public sealed class Layout
        {
            public long CountAt;         // file offset of the int32 header length
            public byte[] Header;        // the MessagePack block header
            public long DataStart;       // file offset of block data (after the int64 length)
            public long DataLength;
            public List<BlockInfo> Blocks = new List<BlockInfo>();

            public BlockInfo Find(string name) => Blocks.Find(b => b.Name == name);
        }

        public static Layout ReadLayout(Stream fs)
        {
            var br = new BinaryReader(fs, Encoding.UTF8);
            PngFile.SkipPng(br);
            if (fs.Length - fs.Position < 8) return null;
            if (br.ReadInt32() > 100) return null;
            if (br.ReadString() != Tag) return null;
            return ReadLayoutAfterTag(fs);
        }

        /// <summary>The bytes of the tag as BinaryWriter stores it (length byte + UTF-8), for finding characters embedded in scenes.</summary>
        public static readonly byte[] TagBytes = MakeTagBytes();

        private static byte[] MakeTagBytes()
        {
            var t = Encoding.UTF8.GetBytes(Tag);
            var b = new byte[t.Length + 1];
            b[0] = (byte)t.Length;
            Array.Copy(t, 0, b, 1, t.Length);
            return b;
        }

        /// <summary>Reads the layout of a character stored right after its tag (cards, and characters embedded in scenes).</summary>
        public static Layout ReadLayoutAfterTag(Stream fs)
        {
            var br = new BinaryReader(fs, Encoding.UTF8);
            br.ReadString();  // version
            br.ReadInt32();   // language
            br.ReadString();  // user ID
            br.ReadString();  // data ID
            var layout = new Layout { CountAt = fs.Position };
            var count = br.ReadInt32();
            if (count <= 0 || count > fs.Length - fs.Position) return null;
            layout.Header = br.ReadBytes(count);
            layout.DataLength = br.ReadInt64();
            layout.DataStart = fs.Position;

            var h = layout.Header;
            var pos = 0;
            var list = MsgPackLite.FindInMap(h, 0, "lstInfo");
            if (list == null) return null;
            pos = list.Start;
            if (!MsgPackLite.ArrayHeader(h, ref pos, out var n)) return null;
            for (var i = 0; i < n; i++)
            {
                if (!MsgPackLite.MapHeader(h, ref pos, out var fields)) return null;
                var info = new BlockInfo();
                for (var f = 0; f < fields; f++)
                {
                    var key = MsgPackLite.String(h, ref pos);
                    var start = pos;
                    switch (key)
                    {
                        case "name": info.Name = MsgPackLite.String(h, ref pos); break;
                        case "pos": info.Pos = MsgPackLite.Int(h, ref pos); info.PosSpan = new MsgPackLite.Span(start, pos); break;
                        case "size": info.Size = MsgPackLite.Int(h, ref pos); info.SizeSpan = new MsgPackLite.Span(start, pos); break;
                        default: pos = MsgPackLite.Skip(h, pos); break;
                    }
                }
                layout.Blocks.Add(info);
            }
            return layout;
        }

        public static byte[] ReadBlock(Stream fs, Layout layout, string name)
        {
            var b = layout.Find(name);
            if (b == null || b.Size <= 0 || layout.DataStart + b.Pos + b.Size > fs.Length) return null;
            fs.Seek(layout.DataStart + b.Pos, SeekOrigin.Begin);
            var data = new byte[b.Size];
            var read = 0;
            while (read < data.Length)
            {
                var r = fs.Read(data, read, data.Length - read);
                if (r <= 0) return null;
                read += r;
            }
            return data;
        }

        /// <summary>The character's sex from a Parameter block: 0 male, 1 female, -1 unknown.</summary>
        public static int SexIn(byte[] param)
        {
            if (param == null) return -1;
            var span = MsgPackLite.FindInMap(param, 0, "sex");
            if (span == null) return -1;
            var p = span.Start;
            try { return (int)MsgPackLite.Int(param, ref p); }
            catch { return -1; }
        }

        public sealed class BodyInfo
        {
            public int Sex = -1;
            public double Height = double.NaN;  // body slider "height" (0..1 in the maker's 0..100)
            public double Bust = double.NaN;    // body slider "bust size"
        }

        /// <summary>
        /// Sex, and the height and bust-size body sliders (shapeValueBody[0] and [1]) from the Custom block
        /// (face, body and hair, each as an int length plus MessagePack). Null if it can't be read.
        /// </summary>
        public static BodyInfo ReadBody(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var layout = ReadLayout(fs);
                if (layout == null) return null;
                var info = new BodyInfo { Sex = SexIn(ReadBlock(fs, layout, "Parameter")) };
                var custom = ReadBlock(fs, layout, "Custom");
                if (custom == null || custom.Length < 8) return info;
                var faceLen = BitConverter.ToInt32(custom, 0);
                var at = 4 + faceLen;
                if (faceLen < 0 || at + 4 > custom.Length) return info;
                var bodyLen = BitConverter.ToInt32(custom, at);
                if (bodyLen <= 0 || at + 4 + bodyLen > custom.Length) return info;
                var body = new byte[bodyLen];
                Buffer.BlockCopy(custom, at + 4, body, 0, bodyLen);
                var span = MsgPackLite.FindInMap(body, 0, "shapeValueBody");
                var values = span != null ? MsgPackLite.Numbers(body, span.Start) : null;
                if (values != null && values.Count > 1)
                {
                    info.Height = values[0];
                    info.Bust = values[1];
                }
                return info;
            }
        }

        /// <summary>The "fullname" in a Parameter block, or "" if absent.</summary>
        public static string NameIn(byte[] param)
        {
            if (param == null) return "";
            var span = MsgPackLite.FindInMap(param, 0, "fullname");
            if (span == null) return "";
            var p = span.Start;
            return MsgPackLite.String(param, ref p) ?? "";
        }

        /// <summary>The character's name, or null if the file isn't an HS2 character card.</summary>
        public static string ReadName(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var layout = ReadLayout(fs);
                    if (layout == null) return null;
                    var param = ReadBlock(fs, layout, "Parameter");
                    if (param == null) return null;
                    var span = MsgPackLite.FindInMap(param, 0, "fullname");
                    if (span == null) return "";
                    var p = span.Start;
                    return MsgPackLite.String(param, ref p) ?? "";
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The plugin-data block (KKEx), or null if there is none.</summary>
        public static byte[] ReadPluginData(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var layout = ReadLayout(fs);
                return layout == null ? null : ReadBlock(fs, layout, "KKEx");
            }
        }

        /// <summary>
        /// Changes the character's name. Writes a new copy next to the file, checks that it reads back with the new
        /// name and every other block unchanged, then swaps it in. The file keeps its modified date.
        /// </summary>
        public static void SetName(string path, string newName)
        {
            if (newName == null) throw new ArgumentNullException(nameof(newName));
            var bytes = File.ReadAllBytes(path);
            Layout layout;
            using (var ms = new MemoryStream(bytes, false)) layout = ReadLayout(ms);
            if (layout == null) throw new InvalidDataException("not an HS2 character card");
            var param = layout.Find("Parameter") ?? throw new InvalidDataException("the card has no Parameter block");

            var paramBytes = new byte[param.Size];
            Array.Copy(bytes, layout.DataStart + param.Pos, paramBytes, 0, param.Size);
            var nameSpan = MsgPackLite.FindInMap(paramBytes, 0, "fullname") ?? throw new InvalidDataException("the card has no name field");
            var p0 = nameSpan.Start;
            if (MsgPackLite.String(paramBytes, ref p0) == newName) return;

            var newParam = MsgPackLite.Splice(paramBytes, new[] { new KeyValuePair<MsgPackLite.Span, byte[]>(nameSpan, MsgPackLite.EncodeString(newName)) });
            var delta = newParam.Length - paramBytes.Length;

            // Header: the Parameter block's size, and the position of every block stored after it.
            var edits = new List<KeyValuePair<MsgPackLite.Span, byte[]>>();
            foreach (var b in layout.Blocks)
            {
                if (b == param) edits.Add(new KeyValuePair<MsgPackLite.Span, byte[]>(b.SizeSpan, MsgPackLite.EncodeInt(newParam.Length)));
                else if (b.Pos > param.Pos && b.PosSpan != null) edits.Add(new KeyValuePair<MsgPackLite.Span, byte[]>(b.PosSpan, MsgPackLite.EncodeInt(b.Pos + delta)));
            }
            var newHeader = MsgPackLite.Splice(layout.Header, edits);

            var paramAt = layout.DataStart + param.Pos;
            var paramEnd = paramAt + param.Size;
            var modified = File.GetLastWriteTimeUtc(path);
            var tmp = path + ".sbtmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(bytes, 0, (int)layout.CountAt);
                    bw.Write(newHeader.Length);
                    bw.Write(newHeader);
                    bw.Write(layout.DataLength + delta);
                    bw.Write(bytes, (int)layout.DataStart, (int)(paramAt - layout.DataStart));
                    bw.Write(newParam);
                    bw.Write(bytes, (int)paramEnd, bytes.Length - (int)paramEnd);
                }

                Verify(tmp, newName, bytes, layout);
                File.SetLastWriteTimeUtc(tmp, modified);
                Swap(tmp, path);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            }
        }

        /// <summary>The new copy must have the new name and every other block byte-for-byte as before.</summary>
        private static void Verify(string tmp, string newName, byte[] original, Layout before)
        {
            if (ReadName(tmp) != newName) throw new IOException("the renamed copy didn't read back; the original was left alone");
            using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.Read))
            {
                var after = ReadLayout(fs) ?? throw new IOException("the renamed copy didn't read back; the original was left alone");
                foreach (var b in before.Blocks)
                {
                    if (b.Name == "Parameter") continue;
                    var now = ReadBlock(fs, after, b.Name);
                    if (now == null || now.Length != b.Size) throw new IOException($"block {b.Name} changed size; the original was left alone");
                    for (var i = 0; i < now.Length; i++)
                        if (now[i] != original[before.DataStart + b.Pos + i])
                            throw new IOException($"block {b.Name} changed; the original was left alone");
                }
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
