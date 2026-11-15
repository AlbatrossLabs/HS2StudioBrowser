using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace HS2StudioBrowser.Files
{
    public sealed class CardEntry
    {
        public string Path;
        public string FileName;   // without the extension
        public string Name;       // name stored in the card (what Studio's list shows)
        public DateTime Modified;
        public string Folder;     // folder the file is in
    }

    public sealed class FolderNode
    {
        public string Path;
        public string Name;
        public FolderNode Parent;
        public readonly List<FolderNode> Children = new List<FolderNode>();
        public int Depth;
        public int FileCount;     // cards directly in this folder
        public int TotalCount;    // cards in this folder and every folder inside it
    }

    /// <summary>
    /// The folder tree under UserData\<kind>\female (or male) and the cards in the open folder.
    /// Scanning runs on a worker thread: only each card's header is read (for its name), and names
    /// are cached by path + date + size, so re-scans after a rename or move are quick.
    /// </summary>
    public sealed class CardIndex
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, CachedName> _names = new Dictionary<string, CachedName>(StringComparer.OrdinalIgnoreCase);
        private int _generation;
        private readonly Func<string, string> _readName;

        /// <param name="readName">Reads the name stored in a card; null means "not this kind of card". Runs on a worker thread.</param>
        public CardIndex(Func<string, string> readName)
        {
            _readName = readName;
        }
        private ScanResult _pending;

        public string Root { get; private set; }
        public FolderNode Tree { get; private set; }
        public List<CardEntry> Entries { get; private set; } = new List<CardEntry>();
        public bool Scanning { get; private set; }
        public int Version { get; private set; }   // bumps when Tree/Entries change

        private struct CachedName
        {
            public DateTime Modified;
            public long Size;
            public string Name; // null = not this kind of card
        }

        private sealed class ScanResult
        {
            public int Generation;
            public FolderNode Tree;
            public List<CardEntry> Entries;
        }

        /// <summary>Starts a scan of <paramref name="root"/>'s tree and the cards in <paramref name="folder"/>.</summary>
        public void Scan(string root, string folder, bool includeSubfolders, string[] patterns = null)
        {
            if (patterns == null || patterns.Length == 0) patterns = new[] { "*.png" };
            Root = root;
            var gen = Interlocked.Increment(ref _generation);
            Scanning = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                ScanResult result;
                try
                {
                    Directory.CreateDirectory(root);
                    var tree = BuildTree(root, patterns);
                    var entries = ReadEntries(folder, includeSubfolders, patterns);
                    result = new ScanResult { Generation = gen, Tree = tree, Entries = entries };
                }
                catch (Exception ex)
                {
                    Log.Source.LogWarning("Card scan failed: " + ex.Message);
                    result = new ScanResult { Generation = gen, Tree = new FolderNode { Path = root, Name = System.IO.Path.GetFileName(root) }, Entries = new List<CardEntry>() };
                }
                lock (_lock) _pending = result;
            });
        }

        /// <summary>Call every frame on the main thread; applies a finished scan. True if something changed.</summary>
        public bool Poll()
        {
            ScanResult done;
            lock (_lock)
            {
                done = _pending;
                _pending = null;
            }
            if (done == null || done.Generation != _generation) return false;
            Tree = done.Tree;
            Entries = done.Entries;
            Scanning = false;
            Version++;
            return true;
        }

        public FolderNode Find(string path)
        {
            if (Tree == null || path == null) return null;
            var stack = new Stack<FolderNode>();
            stack.Push(Tree);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (FileOps.Same(n.Path, path)) return n;
                foreach (var c in n.Children) stack.Push(c);
            }
            return null;
        }

        public IEnumerable<FolderNode> AllFolders()
        {
            if (Tree == null) yield break;
            var stack = new Stack<FolderNode>();
            stack.Push(Tree);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                yield return n;
                for (var i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
            }
        }

        /// <summary>Tells the cache a card's name changed so the next scan doesn't need to read it again.</summary>
        public void Forget(string path)
        {
            lock (_names) _names.Remove(path);
        }

        // ------------------------------------------------------------ worker thread

        private static FolderNode BuildTree(string root, string[] patterns)
        {
            var node = new FolderNode { Path = FileOps.Normalize(root), Name = System.IO.Path.GetFileName(FileOps.Normalize(root)) };
            Fill(node, 0, patterns);
            Total(node);
            return node;
        }

        /// <summary>Files matching any of the patterns (each file once).</summary>
        public static string[] Files(string folder, string[] patterns, SearchOption option)
        {
            if (patterns.Length == 1) return Directory.GetFiles(folder, patterns[0], option);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var pattern in patterns)
                foreach (var f in Directory.GetFiles(folder, pattern, option))
                    if (set.Add(f)) list.Add(f);
            return list.ToArray();
        }

        private static int Total(FolderNode node)
        {
            var n = node.FileCount;
            foreach (var c in node.Children) n += Total(c);
            node.TotalCount = n;
            return n;
        }

        private static void Fill(FolderNode node, int depth, string[] patterns)
        {
            node.Depth = depth;
            try { node.FileCount = Files(node.Path, patterns, SearchOption.TopDirectoryOnly).Length; }
            catch { node.FileCount = 0; }
            if (depth > 24) return;
            string[] dirs;
            try { dirs = Directory.GetDirectories(node.Path); }
            catch { return; }
            foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var name = System.IO.Path.GetFileName(d);
                if (name.StartsWith(".")) continue;
                var child = new FolderNode { Path = d, Name = name, Parent = node };
                Fill(child, depth + 1, patterns);
                node.Children.Add(child);
            }
        }

        private List<CardEntry> ReadEntries(string folder, bool includeSubfolders, string[] patterns)
        {
            var list = new List<CardEntry>();
            if (folder == null || !Directory.Exists(folder)) return list;
            string[] files;
            try
            {
                files = Files(folder, patterns, includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Couldn't list {folder}: {ex.Message}");
                return list;
            }

            foreach (var f in files)
            {
                try
                {
                    var info = new FileInfo(f);
                    var name = NameOf(f, info);
                    if (name == null) continue; // not this kind of card (another card type, a screenshot...)
                    list.Add(new CardEntry
                    {
                        Path = f,
                        FileName = System.IO.Path.GetFileNameWithoutExtension(f),
                        Name = name,
                        Modified = info.LastWriteTime,
                        Folder = System.IO.Path.GetDirectoryName(f),
                    });
                }
                catch
                {
                    // File vanished mid-scan; skip it.
                }
            }
            return list;
        }

        private string NameOf(string path, FileInfo info)
        {
            lock (_names)
            {
                if (_names.TryGetValue(path, out var c) && c.Modified == info.LastWriteTimeUtc && c.Size == info.Length)
                    return c.Name;
            }
            var name = _readName(path);
            lock (_names) _names[path] = new CachedName { Modified = info.LastWriteTimeUtc, Size = info.Length, Name = name };
            return name;
        }
    }
}
