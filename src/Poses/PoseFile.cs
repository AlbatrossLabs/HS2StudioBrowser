using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HS2StudioBrowser.Poses
{
    /// <summary>
    /// Studio pose files. A .dat is Studio's own format: the string "【pose】", an int version, an int sex, the
    /// pose's name, then the bones (and ExtendedSave's extra data at the end). A Pose Tools .png is a picture
    /// followed by exactly the same bytes. Strings are BinaryWriter strings: a 7-bit length, then UTF-8.
    /// </summary>
    internal static class PoseFile
    {
        public const string Tag = "【pose】";
        private static readonly byte[] TagBytes = Encoding.UTF8.GetBytes(Tag);
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public sealed class Layout
        {
            public long DataStart;   // where "【pose】" begins (0 for a .dat, the picture's length for a .png)
            public int Version;
            public int Sex;
            public long NameStart;   // the name's length prefix
            public long NameEnd;
            public string Name;
        }

        public static bool IsDat(string path) => string.Equals(Path.GetExtension(path), ".dat", StringComparison.OrdinalIgnoreCase);

        /// <summary>The pose's name, or null if the file isn't a pose.</summary>
        public static string ReadName(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096))
                    return ReadLayout(fs)?.Name;
            }
            catch
            {
                return null;
            }
        }

        public static Layout ReadLayout(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096))
                return ReadLayout(fs);
        }

        /// <summary>Reads where the pose data and its name are, or null if this isn't a pose.</summary>
        public static Layout ReadLayout(Stream s)
        {
            var start = PngLength(s);
            if (start < 0) return null;
            s.Position = start;
            var len = Read7Bit(s);
            if (len != TagBytes.Length) return null;
            var tag = new byte[len];
            if (s.Read(tag, 0, len) != len) return null;
            for (var i = 0; i < len; i++) if (tag[i] != TagBytes[i]) return null;

            var l = new Layout { DataStart = start };
            l.Version = ReadInt(s);
            l.Sex = ReadInt(s);
            l.NameStart = s.Position;
            var nameLen = Read7Bit(s);
            if (nameLen < 0 || nameLen > 4096 || s.Position + nameLen > s.Length) return null;
            var nb = new byte[nameLen];
            if (s.Read(nb, 0, nameLen) != nameLen) return null;
            l.Name = Encoding.UTF8.GetString(nb);
            l.NameEnd = s.Position;
            return l;
        }

        /// <summary>
        /// For the duplicate finder: the pose's data (the base animation and every IK / FK bone, after the name,
        /// so the picture, name and plugin extras don't count) hashed for exact matches, plus the bone values
        /// for near matches between poses on the same animation with the same bones.
        /// </summary>
        public static Files.DupeKey DuplicateKey(string path)
        {
            var bytes = File.ReadAllBytes(path);
            Layout l;
            using (var ms = new MemoryStream(bytes, false)) l = ReadLayout(ms);
            if (l == null) return null;
            var p = (int)l.NameEnd;
            int Int() { var v = BitConverter.ToInt32(bytes, p); p += 4; return v; }
            float Float() { var v = BitConverter.ToSingle(bytes, p); p += 4; return v; }
            try
            {
                var group = Int();
                var category = Int();
                var no = Int();
                if (l.Version >= 101) p += 4;       // normalizedTime
                p += 1 + 5;                         // enableIK, activeIK[5]
                var bones = new SortedDictionary<int, float[]>();
                var ik = Int();
                if (ik < 0 || ik > 512) return null;
                var ikKeys = new List<int>();
                for (var i = 0; i < ik; i++)
                {
                    var k = Int();
                    var v = new float[9];
                    for (var j = 0; j < 9; j++) v[j] = Float();
                    bones[k] = v;
                    ikKeys.Add(k);
                }
                p += 1 + 7;                         // enableFK, activeFK[7]
                var fk = Int();
                if (fk < 0 || fk > 1024) return null;
                var fkBones = new SortedDictionary<int, float[]>();
                for (var i = 0; i < fk; i++)
                {
                    var k = Int();
                    var v = new float[9];
                    for (var j = 0; j < 9; j++) v[j] = Float();
                    fkBones[k] = v;
                }
                p += 4;                             // expression[4]
                if (p > bytes.Length) return null;

                ikKeys.Sort();
                var values = new List<float>();
                var angle = new List<bool>();
                foreach (var set in new[] { bones, fkBones })
                foreach (var kv in set)
                    for (var j = 0; j < 9; j++)
                    {
                        values.Add(kv.Value[j]);
                        angle.Add(j >= 3 && j < 6);
                    }
                return new Files.DupeKey
                {
                    Exact = Files.DupeKey.Hash(bytes, (int)l.NameEnd, p),
                    Bucket = $"{group}/{category}/{no}|ik:{string.Join(",", ikKeys.Select(x => x.ToString()).ToArray())}|fk:{string.Join(",", fkBones.Keys.Select(x => x.ToString()).ToArray())}",
                    Values = values.ToArray(),
                    IsAngle = angle.ToArray(),
                };
            }
            catch (ArgumentException)
            {
                return null; // cut short
            }
        }

        /// <summary>
        /// What the pose drives: IK, FK on the body, FK on the hands (and hands only, with nothing else),
        /// from the enable and per-part flags after the name. Empty if it can't be read.
        /// </summary>
        public static List<string> ReadRig(string path)
        {
            var rig = new List<string>();
            var bytes = File.ReadAllBytes(path);
            Layout l;
            using (var ms = new MemoryStream(bytes, false)) l = ReadLayout(ms);
            if (l == null) return rig;
            var p = (int)l.NameEnd + 12;           // group, category, no
            if (l.Version >= 101) p += 4;          // normalizedTime
            if (p + 6 > bytes.Length) return rig;
            var ik = bytes[p] != 0;
            p += 1 + 5;
            var ikCount = BitConverter.ToInt32(bytes, p);
            if (ikCount < 0 || ikCount > 512) return rig;
            p += 4 + ikCount * 40;
            if (p + 8 > bytes.Length) return rig;
            var fk = bytes[p] != 0;
            var parts = new bool[7];
            for (var i = 0; i < 7; i++) parts[i] = bytes[p + 1 + i] != 0;
            var body = fk && (parts[3] || parts[0] || parts[1] || parts[2] || parts[6]);   // body, hair, neck, breast, skirt
            var hands = fk && (parts[4] || parts[5]);
            if (ik) rig.Add("IK");
            if (fk && parts[3]) rig.Add("FK body");
            if (hands) rig.Add("Hands");
            if (hands && !ik && !body) rig.Add("Hands only");
            return rig;
        }

        /// <summary>Changes the name stored in the pose, leaving the rest byte for byte. Throws on failure.</summary>
        public static void SetName(string path, string newName)
        {
            var bytes = File.ReadAllBytes(path);
            Layout l;
            using (var ms = new MemoryStream(bytes, false)) l = ReadLayout(ms);
            if (l == null) throw new IOException("Not a pose file.");
            if (l.Name == newName) return;

            var name = EncodeString(newName);
            var output = new byte[bytes.Length - (l.NameEnd - l.NameStart) + name.Length];
            Buffer.BlockCopy(bytes, 0, output, 0, (int)l.NameStart);
            Buffer.BlockCopy(name, 0, output, (int)l.NameStart, name.Length);
            Buffer.BlockCopy(bytes, (int)l.NameEnd, output, (int)l.NameStart + name.Length, bytes.Length - (int)l.NameEnd);

            using (var ms = new MemoryStream(output, false))
            {
                var check = ReadLayout(ms);
                if (check == null || check.Name != newName || output.Length - check.NameEnd != bytes.Length - l.NameEnd)
                    throw new IOException("The renamed pose didn't read back correctly; nothing was changed.");
            }
            Swap(path, output);
        }

        /// <summary>
        /// Puts <paramref name="png"/> in front of the pose data. A .png gets its picture replaced in place
        /// (returns the same path); a .dat is written as a new .png next to it (returns that path, the .dat is
        /// left for the caller). Throws on failure.
        /// </summary>
        public static string WriteWithPicture(string path, byte[] png)
        {
            using (var ms = new MemoryStream(png, false))
                if (PngLength(ms) != png.Length) throw new IOException("The picture isn't a PNG.");

            var bytes = File.ReadAllBytes(path);
            Layout l;
            using (var ms = new MemoryStream(bytes, false)) l = ReadLayout(ms);
            if (l == null) throw new IOException("Not a pose file.");

            var data = bytes.Length - (int)l.DataStart;
            var output = new byte[png.Length + data];
            Buffer.BlockCopy(png, 0, output, 0, png.Length);
            Buffer.BlockCopy(bytes, (int)l.DataStart, output, png.Length, data);

            using (var ms = new MemoryStream(output, false))
            {
                var check = ReadLayout(ms);
                if (check == null || check.Name != l.Name || check.DataStart != png.Length)
                    throw new IOException("The new picture didn't read back correctly; nothing was changed.");
            }

            if (!IsDat(path))
            {
                Swap(path, output);
                return path;
            }

            var dir = Path.GetDirectoryName(path);
            var target = Files.FileOps.FreePath(dir, Path.GetFileNameWithoutExtension(path) + ".png");
            var tmp = target + ".sbtmp";
            File.WriteAllBytes(tmp, output);
            File.Move(tmp, target);
            try { File.SetLastWriteTime(target, File.GetLastWriteTime(path)); } catch { /* cosmetic */ }
            return target;
        }

        /// <summary>Length of the picture at the front of a .png pose: 0 when there is none (a .dat), -1 when it is broken.</summary>
        public static long PngLength(Stream s)
        {
            var start = s.Position;
            var sig = new byte[8];
            if (s.Read(sig, 0, 8) != 8) { s.Position = start; return 0; }
            for (var i = 0; i < 8; i++)
                if (sig[i] != PngSignature[i]) { s.Position = start; return 0; }

            var head = new byte[8];
            while (true)
            {
                if (s.Read(head, 0, 8) != 8) return -1;
                var len = (head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3];
                if (len < 0 || s.Position + len + 4 > s.Length) return -1;
                s.Position += len + 4; // data + CRC
                if (head[4] == 'I' && head[5] == 'E' && head[6] == 'N' && head[7] == 'D') break;
            }
            var end = s.Position - start;
            s.Position = start;
            return end;
        }

        // ------------------------------------------------------------------ helpers

        private static void Swap(string path, byte[] output)
        {
            var modified = File.GetLastWriteTime(path);
            var tmp = path + ".sbtmp";
            File.WriteAllBytes(tmp, output);
            var back = path + ".sbbak";
            try
            {
                File.Replace(tmp, path, back);
                File.Delete(back);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            try { File.SetLastWriteTime(path, modified); } catch { /* cosmetic */ }
        }

        private static int ReadInt(Stream s)
        {
            var b = new byte[4];
            if (s.Read(b, 0, 4) != 4) throw new EndOfStreamException();
            return BitConverter.ToInt32(b, 0);
        }

        private static int Read7Bit(Stream s)
        {
            int value = 0, shift = 0;
            for (var i = 0; i < 5; i++)
            {
                var b = s.ReadByte();
                if (b < 0) return -1;
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
                shift += 7;
            }
            return -1;
        }

        private static byte[] EncodeString(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            var ms = new MemoryStream();
            var v = (uint)bytes.Length;
            while (v >= 0x80)
            {
                ms.WriteByte((byte)(v | 0x80));
                v >>= 7;
            }
            ms.WriteByte((byte)v);
            ms.Write(bytes, 0, bytes.Length);
            return ms.ToArray();
        }
    }
}
