using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace HS2StudioBrowser.Files
{
    /// <summary>File and folder operations on cards. Every method throws with a readable message on failure.</summary>
    public static class FileOps
    {
        // Windows' rules, spelled out so they hold whatever runtime reports.
        private static readonly char[] Invalid = "<>:\"/\\|?*".ToCharArray().Concat(Enumerable.Range(0, 32).Select(i => (char)i)).ToArray();

        /// <summary>Makes text safe as a file or folder name (bad characters become _).</summary>
        public static string Sanitize(string name)
        {
            if (name == null) return "";
            var chars = name.Trim().Select(c => Invalid.Contains(c) ? '_' : c).ToArray();
            var s = new string(chars).TrimEnd('.', ' ');
            return s;
        }

        public static bool IsValidName(string name, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) problem = "Enter a name.";
            else if (name.IndexOfAny(Invalid) >= 0) problem = "A file name can't contain \\ / : * ? \" < > |";
            else if (name.EndsWith(".") || name.EndsWith(" ")) problem = "A file name can't end with a dot or a space.";
            return problem == null;
        }

        /// <summary>A path in <paramref name="dir"/> for <paramref name="fileName"/> that isn't taken: "name (2).png" and so on.</summary>
        public static string FreePath(string dir, string fileName, string ignore = null)
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var candidate = Path.Combine(dir, fileName);
            for (var n = 2; Exists(candidate) && !Same(candidate, ignore); n++)
                candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            return candidate;
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        public static bool Same(string a, string b) =>
            a != null && b != null && string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        public static string Normalize(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        /// <summary>True if <paramref name="path"/> is <paramref name="folder"/> or inside it.</summary>
        public static bool IsInside(string path, string folder)
        {
            var p = Normalize(path) + Path.DirectorySeparatorChar;
            var f = Normalize(folder) + Path.DirectorySeparatorChar;
            return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Renames the file (same folder). Returns the new path. A name that's taken gets " (2)".</summary>
        public static string RenameFile(string path, string newStem)
        {
            if (!IsValidName(newStem, out var problem)) throw new IOException(problem);
            var dir = Path.GetDirectoryName(path);
            var ext = Path.GetExtension(path);
            var target = Path.Combine(dir, newStem + ext);
            if (Same(target, path)) return path;

            // Only the letter case changes: Windows needs a detour through a temporary name.
            if (string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
            {
                var tmp = path + ".obmove";
                File.Move(path, tmp);
                File.Move(tmp, target);
                return target;
            }

            target = FreePath(dir, newStem + ext);
            File.Move(path, target);
            return target;
        }

        /// <summary>Moves files into a folder. Returns old path → new path for the ones that moved.</summary>
        public static Dictionary<string, string> MoveFiles(IEnumerable<string> paths, string destDir, List<string> errors)
        {
            var moved = new Dictionary<string, string>();
            Directory.CreateDirectory(destDir);
            foreach (var path in paths)
            {
                try
                {
                    if (Same(Path.GetDirectoryName(path), destDir)) continue;
                    var target = FreePath(destDir, Path.GetFileName(path));
                    File.Move(path, target);
                    moved[path] = target;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
            return moved;
        }

        public static string Duplicate(string path)
        {
            var dir = Path.GetDirectoryName(path);
            var target = FreePath(dir, Path.GetFileNameWithoutExtension(path) + " (copy)" + Path.GetExtension(path));
            File.Copy(path, target);
            return target;
        }

        public static string CreateFolder(string parent, string name)
        {
            if (!IsValidName(name, out var problem)) throw new IOException(problem);
            var target = Path.Combine(parent, name.Trim());
            if (Directory.Exists(target)) throw new IOException($"\"{name}\" already exists here.");
            Directory.CreateDirectory(target);
            return target;
        }

        public static string RenameFolder(string folder, string newName)
        {
            if (!IsValidName(newName, out var problem)) throw new IOException(problem);
            var parent = Path.GetDirectoryName(Normalize(folder));
            var target = Path.Combine(parent, newName.Trim());
            if (Same(target, folder)) return folder;
            if (string.Equals(target, Normalize(folder), StringComparison.OrdinalIgnoreCase))
            {
                var tmp = Normalize(folder) + ".obmove";
                Directory.Move(folder, tmp);
                Directory.Move(tmp, target);
                return target;
            }
            if (Directory.Exists(target)) throw new IOException($"\"{newName}\" already exists here.");
            Directory.Move(folder, target);
            return target;
        }

        /// <summary>Moves a folder (with everything in it) into another folder.</summary>
        public static string MoveFolder(string folder, string destParent)
        {
            if (IsInside(destParent, folder)) throw new IOException("A folder can't go inside itself.");
            var target = Path.Combine(destParent, Path.GetFileName(Normalize(folder)));
            if (Same(target, folder)) return folder;
            if (Directory.Exists(target)) throw new IOException($"\"{Path.GetFileName(target)}\" already exists there.");
            Directory.Move(folder, target);
            return target;
        }

        public static void ShowInExplorer(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path.Replace('/', '\\')}\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path.Replace('/', '\\')}\"");
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning("Couldn't open Explorer: " + ex.Message);
            }
        }

        // ------------------------------------------------------------ recycle bin

        /// <summary>Sends files or folders to the Recycle Bin (never deletes outright). Returns false if Windows refused.</summary>
        public static bool Recycle(IList<string> paths)
        {
            if (paths.Count == 0) return true;
            var from = string.Join("\0", paths.Select(p => Path.GetFullPath(p).Replace('/', '\\')).ToArray()) + "\0\0";
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = from,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
            };
            var result = SHFileOperation(ref op);
            return result == 0 && !op.fAnyOperationsAborted;
        }

        private const uint FO_DELETE = 3;
        private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.U4)] public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
    }
}
