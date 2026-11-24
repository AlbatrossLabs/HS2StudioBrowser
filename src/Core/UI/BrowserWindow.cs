using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.Game;
using KKAPI.Utilities;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.UI
{
    /// <summary>
    /// The browser: folder tree on the left, card thumbnails on the right, the kind's actions at the bottom.
    /// Click selects (Ctrl / Shift for more), double-click runs the main action, right-click opens a menu, drag cards
    /// (or folders) onto a folder in the tree to move them. File changes run on the next Update, outside
    /// IMGUI's event passes, then the lists are re-read.
    /// </summary>
    internal sealed class BrowserWindow : IBrowserWindow
    {
        private readonly int WindowId, MenuId, DialogId; // per browser, so several can be open
        private const float TreeWidth = 190f;
        private const float Gap = 6f;
        private const float HeaderH = 22f;
        private const float StarSize = 22f;

        private readonly StudioBrowserPlugin _p;
        private readonly BrowserKind _kind;
        private readonly CardIndex _index;
        private readonly ThumbnailCache _thumbs = new ThumbnailCache();
        private readonly Favorites _favs;
        private readonly TagStore _tags;
        private readonly TagSet _tagSet;
        private readonly TagRadial _radial;
        private readonly HashSet<string> _filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _filterScanned;   // the last scan took in subfolders because a tag filter was on
        private readonly AutoTagIndex _auto;
        private Dictionary<string, int> _autoCounts;
        private readonly DuplicateFinder _dupes;
        private bool _dupeMode;
        private bool _dupeScanned;     // the last scan took in subfolders for the duplicate check
        private int _dupeRunKey;
        private readonly HashSet<string> _keepers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly MissingModChecker _mods;      // null when Sideloader isn't installed or the check is off
        private bool _onlyMissing;
        private int _modsVersion;               // snapshot per frame, so the view rebuilds at most once a frame
        private int _missingCount;              // cards in the view (before the missing-only filter) with missing mods
        private string _modRequestKey;
        private string _hoverTip;
        private int _favCount; // favorites at the start of the current view

        private bool _visible;
        private Rect _rect;
        private bool _rectLoaded;
        private int _sex = 1;
        private int _lastFollowSex = -2;
        private string _folder;
        private readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string _search = "";
        private List<CardEntry> _view = new List<CardEntry>();
        private string _viewKey;
        private readonly Dictionary<string, DateTime> _modified = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _anchor;

        private Vector2 _treeScroll, _gridScroll;
        // Grid layout from the last draw, for moving with the arrow keys.
        private int _gridCols = 1;
        private float _gridRowH = 100f, _gridYA, _gridYB, _gridAreaH = 300f;
        private Rect _gridRect = new Rect(0, 0, 100, 100);
        private Rect _treeViewScreen;
        private readonly List<KeyValuePair<Rect, FolderNode>> _treeRows = new List<KeyValuePair<Rect, FolderNode>>();

        // Press / drag
        private string _pressPath;
        private FolderNode _pressFolder;
        private bool _pressWasSelected;
        private Vector2 _pressScreen;
        private bool _dragging;
        private List<string> _dragPaths;
        private FolderNode _dropTarget;

        // Favorites and your folders inside it (the top of the tree): which one is open (null: a disk folder;
        // "": all favorites), the rows for dropping cards on, and the one under a drag.
        private readonly FolderList _favFolders;
        private string _favScope;
        private bool _favOpen = true;
        private readonly List<KeyValuePair<Rect, string>> _favRows = new List<KeyValuePair<Rect, string>>();
        private string _favDrop;
        private int _dragControl;

        // Resize
        private bool _resizing;
        private Vector2 _resizeStart, _resizeOrig;

        // Context menu
        private bool _menuOpen;
        private Rect _menuRect;
        private Vector2 _menuScreen;
        private bool _menuPlace;
        private string _menuTitle;
        private readonly List<MenuItem> _menu = new List<MenuItem>();

        // Dialog
        private Dialog _dialog;

        // Work queued from GUI events, run in Update.
        private readonly List<Action> _pending = new List<Action>();

        private string _status;
        private bool _statusError;
        private float _statusUntil;

        private float _nextPoll;
        private DateTime _folderStamp;

        public BrowserWindow(StudioBrowserPlugin plugin, BrowserKind kind)
        {
            _p = plugin;
            _kind = kind;
            var id = 0x0B570000;
            foreach (var c in kind.Id) id = id * 31 + c;
            WindowId = id & 0x7FFFFFF0;
            MenuId = WindowId + 1;
            DialogId = WindowId + 2;
            _index = new CardIndex(kind.ReadName);
            _favs = new Favorites(kind.Folder, _p.FavoritesFile);
            _tags = new TagStore(kind.Folder, _p.TagsFile);
            _tagSet = _p.CreateTagSet();
            _radial = new TagRadial(WindowId + 3);
            _favFolders = new FolderList(plugin.FoldersFile);
            _dupes = new DuplicateFinder(kind.DuplicateKey);
            if (kind.AutoFacets != null)
                _auto = new AutoTagIndex(kind.ReadAutoTags, kind.Folder,
                    Path.Combine(BepInEx.Paths.CachePath, $"HS2StudioBrowser.{kind.Id}.autotags.txt"), kind.AutoTagsLogic);
            if (kind.ChecksMods && _p.CheckMods.Value && BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.bepis.bepinex.sideloader"))
                _mods = new MissingModChecker(kind.FindMissing);
            LoadExpanded();
        }

        private string CoordinateRoot => _kind.Folder;
        private string N => _kind.Noun;
        private string Ns => _kind.Nouns;

        private void LoadExpanded()
        {
            foreach (var rel in (_p.OpenFolders.Value ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try { _expanded.Add(FileOps.Normalize(Path.Combine(CoordinateRoot, rel.Replace('/', Path.DirectorySeparatorChar)))); }
                catch { /* bad entry */ }
            }
        }

        private void SaveExpanded()
        {
            var root = FileOps.Normalize(CoordinateRoot);
            var rels = _expanded.Where(x => FileOps.IsInside(x, root) && !FileOps.Same(x, root) && Directory.Exists(x))
                .Select(x => FileOps.Normalize(x).Substring(root.Length + 1).Replace('\\', '/'))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            _p.OpenFolders.Value = string.Join("|", rels);
        }

        private void SetExpanded(string path, bool open)
        {
            if (open) _expanded.Add(path);
            else _expanded.Remove(path);
            SaveExpanded();
        }

        public bool Visible => _visible;

        public void FilesChanged()
        {
            if (_visible) Later(Rescan);
        }

        private bool Modal => _dialog != null;

        // Screen size in GUI units (differs from pixels when an IMGUI scaling plugin sets GUI.matrix).
        private static float GuiWidth => Screen.width / Mathf.Max(0.01f, GUI.matrix.m00);
        private static float GuiHeight => Screen.height / Mathf.Max(0.01f, GUI.matrix.m11);

        private string Root(int sex) => _kind.Root(sex);

        // ================================================================ open / close

        public void Toggle(List<OCIChar> targets)
        {
            if (_visible)
            {
                Hide();
                _p.ClosedByUser();
            }
            else Show(targets != null && targets.Count > 0 ? targets[0].oiCharInfo.sex : -1);
        }

        public void Show(int sex)
        {
            if (!_rectLoaded) LoadRect();
            if (sex == 0 || sex == 1)
            {
                _lastFollowSex = sex;
                if (sex != _sex || _folder == null) SetSex(sex, false);
            }
            if (_folder == null) SetSex(_sex, false);
            _visible = true;
            Rescan();
            _kind.OnFolderChanged(_folder);
        }

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            InputGuard.Set(this, false);
            _radial.Close();
            CloseMenu();
            _dialog = null;
            EndDrag();
            SaveRect();
            _thumbs.Clear();
        }

        /// <summary>Switches female/male when the character in Studio's panel changes sex.</summary>
        public void FollowSex(int sex)
        {
            if (sex == _lastFollowSex) return;
            _lastFollowSex = sex;
            if (sex != _sex && (sex == 0 || sex == 1))
            {
                SetSex(sex, true);
                Rescan();
            }
        }

        public void Dispose()
        {
            SaveRect();
            _thumbs.Dispose();
            _mods?.Dispose();
        }

        private void SetSex(int sex, bool remember)
        {
            if (remember && _folder != null) RememberFolder();
            _sex = sex;
            var last = (_sex == 0 ? _p.LastFolderMale : _p.LastFolderFemale).Value;
            var root = Root(_sex);
            _folder = !string.IsNullOrEmpty(last) && Directory.Exists(last) && FileOps.IsInside(last, root) ? last : root;
            ExpandTo(_folder);
            _selected.Clear();
            _anchor = null;
            _gridScroll = Vector2.zero;
        }

        private void RememberFolder()
        {
            (_sex == 0 ? _p.LastFolderMale : _p.LastFolderFemale).Value = _folder;
        }

        private void LoadRect()
        {
            _rectLoaded = true;
            var parts = (_p.WindowRect.Value ?? "").Split(',');
            if (parts.Length == 4 && parts.All(x => float.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
            {
                var v = parts.Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                _rect = new Rect(v[0], v[1], v[2], v[3]);
            }
            else
            {
                _rect = new Rect(Screen.width * 0.36f, Screen.height * 0.1f, Screen.width * 0.42f, Screen.height * 0.74f);
            }
            ClampRect();
        }

        private void SaveRect()
        {
            if (!_rectLoaded) return;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            _p.WindowRect.Value = string.Join(",", new[] { _rect.x, _rect.y, _rect.width, _rect.height }.Select(f => Mathf.Round(f).ToString(c)).ToArray());
            if (_folder != null) RememberFolder();
        }

        private void ClampRect()
        {
            _rect.width = Mathf.Clamp(_rect.width, 560f, Mathf.Max(560f, GuiWidth));
            _rect.height = Mathf.Clamp(_rect.height, 380f, Mathf.Max(380f, GuiHeight));
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, GuiWidth - _rect.width));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, GuiHeight - _rect.height));
        }

        // ================================================================ per frame

        public void Update()
        {
            if (_pending.Count > 0)
            {
                var work = _pending.ToArray();
                _pending.Clear();
                foreach (var a in work)
                {
                    try { a(); }
                    catch (Exception ex)
                    {
                        SetStatus(ex.Message, true);
                        Log.Source.LogWarning(ex);
                    }
                }
            }

            if (!_visible)
            {
                InputGuard.Set(this, false);
                return;
            }

            if (_index.Poll()) AfterScan();
            if (_dupes.Poll()) _viewKey = null;
            if (_auto != null && _auto.Poll())
            {
                _autoCounts = null;
                if (_filter.Count > 0 || !string.IsNullOrEmpty(_search)) _viewKey = null;
            }

            if (_kind.TakeRescan()) Changed();
            var replaced = _kind.TakeReplaced();
            if (replaced != null)
            {
                var before = View();
                _reselectAt = replaced.Select(kv => before.FindIndex(x => FileOps.Same(x.Path, kv.Key))).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
                foreach (var kv in replaced)
                {
                    _thumbs.Invalidate(kv.Key);
                    _thumbs.Invalidate(kv.Value);
                    _index.Forget(kv.Key);
                    _index.Forget(kv.Value);
                    if (!FileOps.Same(kv.Key, kv.Value) && _selected.Remove(kv.Key)) _selected.Add(kv.Value);
                    if (_anchor != null && FileOps.Same(_anchor, kv.Key)) _anchor = kv.Value;
                }
                _favs.Moved(replaced.Where(kv => !FileOps.Same(kv.Key, kv.Value)));
                _tags.Moved(replaced.Where(kv => !FileOps.Same(kv.Key, kv.Value)));
                Changed(replaced.Select(kv => new ListChange { OldPath = kv.Key, NewPath = kv.Value }).ToList());
            }

            if (_mods != null)
            {
                if (ModCheck.ListCtrl == null)
                {
                    try { ModCheck.ListCtrl = Manager.Character.Instance != null ? Manager.Character.Instance.chaListCtrl : null; }
                    catch { /* not ready yet */ }
                }
                _modsVersion = _mods.Version;
                var key = _index.Version + "|" + _folder;
                if (key != _modRequestKey)
                {
                    _modRequestKey = key;
                    _mods.Request(_index.Entries.Select(x => new KeyValuePair<string, DateTime>(x.Path, x.Modified)).ToList());
                }
            }

            _thumbs.Capacity = _p.CacheSize.Value;
            _thumbs.Tick(path => _modified.TryGetValue(path, out var m) ? m : DateTime.MinValue);

            // Notice cards saved, added or removed outside the browser.
            if (Time.unscaledTime >= _nextPoll && !_index.Scanning)
            {
                _nextPoll = Time.unscaledTime + 1.5f;
                try
                {
                    var stamp = Directory.Exists(_folder) ? Directory.GetLastWriteTimeUtc(_folder) : DateTime.MinValue;
                    if (stamp != _folderStamp)
                    {
                        if (_folderStamp != default(DateTime)) Rescan();
                        _folderStamp = stamp;
                    }
                }
                catch { /* folder vanished; next scan sorts it out */ }
            }
        }

        private void Rescan()
        {
            if (_folder == null) return;
            if (!Directory.Exists(_folder)) _folder = Root(_sex);
            try { _folderStamp = Directory.Exists(_folder) ? Directory.GetLastWriteTimeUtc(_folder) : DateTime.MinValue; }
            catch { _folderStamp = DateTime.MinValue; }
            _scannedWithSubfolders = IncludeSubfoldersNow();
            _filterScanned = _filter.Count > 0;
            _dupeScanned = _dupeMode;
            _index.Scan(Root(_sex), _folder, _scannedWithSubfolders, _kind.FilePatterns);
        }

        private bool _scannedWithSubfolders;
        private int _reselectAt = -1;

        /// <summary>
        /// Subfolders are shown when ticked, and also for a folder whose cards are all in its subfolders
        /// (otherwise it would open empty).
        /// </summary>
        private bool IncludeSubfoldersNow()
        {
            if (_p.IncludeSubfolders.Value || _filter.Count > 0 || _dupeMode || _kind.ForceSubfolders || _favScope != null) return true;
            var node = _index.Find(_folder);
            return node != null && node.FileCount == 0 && node.TotalCount > 0;
        }

        private void AfterScan()
        {
            _viewKey = null;
            _modified.Clear();
            foreach (var e in _index.Entries) _modified[e.Path] = e.Modified;
            _selected.RemoveWhere(p => !_modified.ContainsKey(p));
            if (_anchor != null && !_modified.ContainsKey(_anchor)) _anchor = null;
            // A card replaced by the kind left this view (a pose that got its picture, in "No picture"):
            // select the one that took its place, so the next one is ready to go.
            if (_reselectAt >= 0)
            {
                var at = _reselectAt;
                _reselectAt = -1;
                if (_selected.Count == 0)
                {
                    var v = View();
                    if (v.Count > 0) SelectOnly(v[Mathf.Min(at, v.Count - 1)].Path);
                }
            }
            if (_index.Find(_folder) == null && _index.Tree != null)
            {
                _folder = _index.Tree.Path;
                Rescan();
            }
            else if (!_scannedWithSubfolders && IncludeSubfoldersNow()) Rescan(); // all its cards are in subfolders
            else if (_dupeMode) RunDuplicateCheck();
            _auto?.Request(_index.Entries);
        }

        /// <summary>The card's own tags plus the ones read from it (clothing slots, cast...).</summary>
        private HashSet<string> AllTags(CardEntry e)
        {
            var own = _tags.Get(e.Path);
            if (_auto == null) return own;
            var auto = _auto.Get(e.Path, e.Modified);
            if (auto.Length == 0) return own;
            var all = new HashSet<string>(own, StringComparer.OrdinalIgnoreCase);
            foreach (var t in auto) all.Add(t);
            return all;
        }

        /// <summary>How many cards carry a tag: own tags across the store, automatic ones across the cards read so far.</summary>
        private int CountOf(string tag)
        {
            if (_auto == null || !_kind.AutoFacets.Any(f => tag.StartsWith(f.Name + ":", StringComparison.OrdinalIgnoreCase))) return _tags.Count(tag);
            if (_autoCounts == null)
            {
                _autoCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in _index.Entries)
                foreach (var t in _auto.Get(e.Path, e.Modified))
                    _autoCounts[t] = _autoCounts.TryGetValue(t, out var n) ? n + 1 : 1;
            }
            return _autoCounts.TryGetValue(tag, out var c) ? c : 0;
        }

        private void RunDuplicateCheck()
        {
            if (!_dupeScanned) return;
            var key = _index.Version;
            if (key == _dupeRunKey && !_dupes.Busy) return;
            _dupeRunKey = key;
            _dupes.Run(_index.Entries.Select(x => new KeyValuePair<string, DateTime>(x.Path, x.Modified)).ToList(), _kind.NearTolerance);
        }

        private void SetDuplicateMode(bool on)
        {
            if (on == _dupeMode) return;
            _dupeMode = on;
            _viewKey = null;
            _gridScroll = Vector2.zero;
            _dupeRunKey = -1;
            Rescan();
        }

        /// <summary>Which copy in a duplicate set to keep: a favorite, then the most tags, then one with a picture, then the oldest.</summary>
        private List<CardEntry> KeepOrder(IEnumerable<CardEntry> set) =>
            set.OrderBy(e => _favs.Contains(e.Path) ? 0 : 1)
               .ThenByDescending(e => _tags.Get(e.Path).Count)
               .ThenBy(e => _kind.HasPicture(e.Path) ? 0 : 1)
               .ThenBy(e => e.Modified)
               .ThenBy(e => e.Path.Length)
               .ToList();

        /// <summary>Selects every copy but the one to keep in each set.</summary>
        private void SelectExtraCopies()
        {
            _selected.Clear();
            _anchor = null;
            var n = 0;
            foreach (var set in View().GroupBy(e => _dupes.SetOf.TryGetValue(e.Path, out var id) ? id : 0).Where(g => g.Key > 0))
                foreach (var e in KeepOrder(set).Skip(1))
                {
                    _selected.Add(e.Path);
                    n++;
                }
            SetStatus(n == 0 ? "Nothing to select." : $"Selected {n} extra {(n == 1 ? "copy" : "copies")} (one of each kept). Press Del to send them to the Recycle Bin; their stars and tags move to the copy that's kept.");
        }

        /// <summary>Before duplicates go to the Recycle Bin: give their stars and tags to the copy that stays.</summary>
        private void MergeIntoKeepers(List<string> leaving)
        {
            if (!_dupeMode || leaving.Count == 0) return;
            var gone = new HashSet<string>(leaving, StringComparer.OrdinalIgnoreCase);
            foreach (var set in _index.Entries.Where(e => _dupes.SetOf.ContainsKey(e.Path)).GroupBy(e => _dupes.SetOf[e.Path]))
            {
                var keep = KeepOrder(set.Where(e => !gone.Contains(e.Path))).FirstOrDefault();
                if (keep == null) continue;
                foreach (var e in set.Where(x => gone.Contains(x.Path)))
                {
                    if (_favs.Contains(e.Path)) _favs.Set(new[] { keep.Path }, true);
                    foreach (var t in _tags.Get(e.Path).ToList())
                        if (!_tags.Has(keep.Path, t)) _tags.Set(new[] { keep.Path }, t, true, null);
                }
            }
        }

        /// <summary>Re-read after a change made here, and mirror it into Studio's own list.</summary>
        private void Changed(List<ListChange> changes = null)
        {
            Rescan();
            if (changes != null && changes.Count > 0) _kind.SyncStudioList(changes);
        }

        /// <summary>Every card under a folder, and where it will be once the folder is at <paramref name="newFolder"/>.</summary>
        private List<ListChange> FolderChanges(string folder, string newFolder, bool removed)
        {
            var list = new List<ListChange>();
            try
            {
                var root = FileOps.Normalize(folder);
                foreach (var f in CardIndex.Files(root, _kind.FilePatterns, SearchOption.AllDirectories))
                {
                    var rel = FileOps.Normalize(f).Substring(root.Length);
                    list.Add(removed
                        ? new ListChange { OldPath = f, Removed = true }
                        : new ListChange { OldPath = f, NewPath = FileOps.Normalize(newFolder) + rel });
                }
            }
            catch { /* best effort */ }
            return list;
        }

        private List<CardEntry> View()
        {
            var key = $"{_index.Version}|{_search}|{_p.Sort.Value}|{_favs.Version}|{_onlyMissing}|{_modsVersion}|{_tags.Version}|{string.Join("|", _filter.ToArray())}|{_dupeMode}|{_dupes.Version}|{_favScope}|{_favFolders.Version}";
            if (key == _viewKey) return _view;
            _viewKey = key;

            IEnumerable<CardEntry> q = _index.Entries;
            var terms = (_search ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length > 0)
                q = q.Where(e => terms.All(t =>
                    e.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.FileName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    AllTags(e).Any(tag => TagFilter.ValueOf(tag).IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)));
            if (_filter.Count > 0) q = q.Where(e => TagFilter.Matches(AllTags(e), _filter));
            if (_favScope != null)
            {
                var folderTag = _favScope.Length > 0 ? FolderList.Tag(_favScope) : null;
                q = q.Where(e => _favs.Contains(e.Path) && (folderTag == null || _tags.Has(e.Path, folderTag)));
            }

            if (_mods != null)
            {
                var searched = q.ToList();
                _missingCount = searched.Count(x => _mods.HasMissing(x.Path, x.Modified));
                q = searched;
                if (_onlyMissing) q = searched.Where(x => _mods.HasMissing(x.Path, x.Modified));
            }

            if (_dupeMode)
            {
                // Duplicates: only cards with a copy, set by set (largest first), the copy to keep first in each.
                _keepers.Clear();
                _view = new List<CardEntry>();
                foreach (var set in q.Where(e => _dupes.SetOf.ContainsKey(e.Path)).GroupBy(e => _dupes.SetOf[e.Path]).OrderBy(g => g.Key))
                {
                    var copies = KeepOrder(set);
                    if (copies.Count < 2) continue; // its copies are filtered out of this view
                    _keepers.Add(copies[0].Path);
                    _view.AddRange(copies);
                }
                _favCount = 0;
                return _view;
            }

            // Favorites first, then the rest; each part in the chosen order.
            var ordered = q.OrderBy(e => _favs.Contains(e.Path) ? 0 : 1);
            switch (_p.Sort.Value)
            {
                case SortBy.FileName: ordered = ordered.ThenBy(e => e.FileName, NaturalComparer.Instance); break;
                case SortBy.Newest: ordered = ordered.ThenByDescending(e => e.Modified); break;
                case SortBy.Oldest: ordered = ordered.ThenBy(e => e.Modified); break;
                default: ordered = ordered.ThenBy(e => e.Name, NaturalComparer.Instance).ThenBy(e => e.FileName, NaturalComparer.Instance); break;
            }
            _view = ordered.ToList();
            _favCount = _view.TakeWhile(e => _favs.Contains(e.Path)).Count();
            return _view;
        }

        private void SetStatus(string text, bool error = false)
        {
            _status = text;
            _statusError = error;
            _statusUntil = Time.unscaledTime + (error ? 10f : 5f);
        }

        private void Later(Action a) => _pending.Add(a);

        // ================================================================ drawing

        public void Draw()
        {
            if (!_visible) return;

            var e = Event.current;
            if (_menuOpen && e.type == EventType.MouseDown && !_menuRect.Contains(e.mousePosition)) CloseMenu();
            if (e.type == EventType.Repaint)
                InputGuard.Set(this, _dialog != null || _menuOpen || _dragging || _rect.Contains(e.mousePosition));

            var skin = GUI.skin;
            GUI.skin = Theme.Skin;
            try
            {
                ClampRect();
                var title = _kind.Title + (_favScope != null ? "  ·  Favorites" + (_favScope.Length > 0 ? "  ›  " + _favScope : "")
                    : _index.Tree != null ? "  ·  " + FolderLabel(_folder) : "");
                _rect = GUI.Window(WindowId, _rect, DrawWindow, title);

                if (_menuOpen)
                {
                    if (_menuPlace)
                    {
                        // Screen point -> GUI space (they differ when an IMGUI scaling plugin is active).
                        var p = GUIUtility.ScreenToGUIPoint(_menuScreen);
                        _menuRect = new Rect(p.x + 2, p.y + 2, 210, 10);
                        _menuPlace = false;
                    }
                    _menuRect = GUILayout.Window(MenuId, _menuRect, DrawMenu, GUIContent.none, Styles.Menu, GUILayout.Width(210));
                    _menuRect.x = Mathf.Clamp(_menuRect.x, 0, Mathf.Max(0, GuiWidth - _menuRect.width));
                    _menuRect.y = Mathf.Clamp(_menuRect.y, 0, Mathf.Max(0, GuiHeight - _menuRect.height));
                    GUI.BringWindowToFront(MenuId);
                }

                if (_dialog != null)
                {
                    var dw = _dialog.Width;
                    if (_dialog.Rect.width <= 0)
                        _dialog.Rect = new Rect(_rect.x + (_rect.width - dw) / 2, _rect.y + _rect.height * 0.2f, dw, 10);
                    _dialog.Rect = GUILayout.Window(DialogId, _dialog.Rect, DrawDialog, _dialog.Title, GUILayout.Width(dw));
                    GUI.BringWindowToFront(DialogId);
                    if (_dialog != null && _dialog.FocusWindow)
                    {
                        GUI.FocusWindow(DialogId);
                        _dialog.FocusWindow = false;
                    }
                }

                _radial.Draw(_tagSet, _tags, _filter, TagsChanged, _kind.AutoFacets, CountOf);
            }
            finally
            {
                GUI.skin = skin;
            }
        }

        private string FolderLabel(string folder)
        {
            var root = Root(_sex);
            if (folder == null) return "";
            var top = _kind.HasSex ? (_sex == 0 ? "male" : "female") : Path.GetFileName(FileOps.Normalize(root));
            if (FileOps.Same(folder, root)) return top;
            return top + folder.Substring(FileOps.Normalize(root).Length).Replace('\\', '/');
        }

        private void DrawWindow(int id)
        {
            var e = Event.current;
            _dragControl = GUIUtility.GetControlID(FocusType.Passive);
            if (_menuOpen && e.type == EventType.MouseDown && e.button == 0) CloseMenu();
            if (e.type == EventType.Repaint) _hoverTip = null;

            if (GUI.Button(new Rect(_rect.width - 30, 7, 22, 20), "x"))
            {
                Later(() =>
                {
                    Hide();
                    _p.ClosedByUser();
                });
            }

            var wasEnabled = GUI.enabled;
            if (Modal) GUI.enabled = false;

            GUILayout.BeginArea(new Rect(10, 32, _rect.width - 20, _rect.height - 42));
            DrawTopBar();
            DrawFilterBar();
            GUILayout.Space(4);
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawTree();
            GUILayout.Space(6);
            DrawGrid();
            GUILayout.Space(6);
            DrawDetails();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            DrawFooter();
            GUILayout.EndArea();

            GUI.enabled = wasEnabled;

            HandleDrag(e);
            if (!Modal) HandleKeys(e);
            HandleResize(e);

            GUI.DragWindow(new Rect(0, 0, _rect.width - 34, 28));
            IMGUIUtils.EatInputInRect(_rect);
        }

        // ---------------------------------------------------------------- details panel (right)

        private void DrawDetails()
        {
            var w = TagPanel.WidthFor(_rect.width);
            var tree = _p.ShowFolders.Value ? TreeWidth : 24;
            if (!_p.ShowDetails.Value || _rect.width - 20 - tree - w - 12 < 240)
            {
                TagPanel.DrawFolded(() => _p.ShowDetails.Value = true);
                return;
            }
            var sel = new TagPanel.Selection { Noun = N };
            var p = Primary();
            if (p != null && _selected.Count > 0)
            {
                var paths = SelectedInOrder();
                if (paths.Count == 0) paths = new List<string> { p.Path };
                var showFile = !string.IsNullOrEmpty(p.Name) && !string.Equals(p.Name, p.FileName, StringComparison.OrdinalIgnoreCase);
                var root = _kind.Root(_sex);
                var rel = p.Folder != null && root != null && p.Folder.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? p.Folder.Substring(root.Length).TrimStart('\\', '/') : null;
                sel.Targets = paths;
                sel.Title = paths.Count > 1 ? $"{paths.Count} {Ns} selected" : string.IsNullOrEmpty(p.Name) ? p.FileName : p.Name;
                sel.Subtitle = Theme.Hint(paths.Count > 1 ? $"tags you click go on all {paths.Count}; showing {(string.IsNullOrEmpty(p.Name) ? p.FileName : p.Name)}"
                    : (showFile ? Path.GetFileName(p.Path) : Path.GetExtension(p.Path).TrimStart('.')) + (string.IsNullOrEmpty(rel) ? "" : "\n" + rel));
                sel.Picture = _kind.HasPicture(p.Path) ? _thumbs.Get(p.Path, p.Modified) : null;
                sel.Auto = paths.Count == 1 && _auto != null ? _auto.Get(p.Path, p.Modified) : null;
            }
            var targets = sel.Targets;
            var selPaths = targets?.ToList();
            _details.Draw(w, _tagSet, _tags, sel, () =>
                {
                    if (targets != null) StarFolderMembers(targets);
                    TagsChanged();
                }, () => _p.ShowDetails.Value = false,
                _favFolders.Facet, selPaths != null && selPaths.Count > 0 ? () => Later(() => AskFavFolder(null, selPaths)) : (Action)null);
        }

        // ---------------------------------------------------------------- tags

        /// <summary>Tags or the tag filter changed (from the wheel or the filter bar).</summary>
        private void TagsChanged()
        {
            _viewKey = null;
            if ((_filter.Count > 0) != _filterScanned) Rescan();
        }

        /// <summary>Opens the tag wheel for the selected cards at the pointer.</summary>
        private void OpenTagWheel(Vector2 screen)
        {
            var paths = SelectedInOrder();
            if (paths.Count == 0)
            {
                SetStatus($"Select the {Ns} to tag first.");
                return;
            }
            _radial.ShowTag(screen, paths);
        }

        /// <summary>The active tag filter as removable chips, under the top bar.</summary>
        private void DrawFilterBar()
        {
            if (_filter.Count == 0) return;
            GUILayout.Space(3);
            var f = _filterFlow;
            f.Begin(BarWidth);
            f.Next(Flow.Size(Theme.Hint("Tagged"), Theme.RichSingle));
            GUILayout.Label(Theme.Hint("Tagged"), Theme.RichSingle, GUILayout.ExpandWidth(false));
            foreach (var tag in _filter.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList())
            {
                var t = tag;
                var chip = new GUIContent(t.Replace(":", ": ") + "  x", "Remove this from the filter");
                f.Next(Flow.Size(chip, Theme.Choice));
                if (GUILayout.Button(chip, Theme.Choice, GUILayout.ExpandWidth(false)))
                    Later(() => { _filter.Remove(t); TagsChanged(); });
            }
            f.Next(Flow.Fixed(50));
            if (GUILayout.Button(new GUIContent("Clear", "Show everything again"), GUILayout.Width(50)))
                Later(() => { _filter.Clear(); TagsChanged(); });
            var reading = _auto != null && _auto.Pending > 0 ? $"  ·  reading {_auto.Pending} cards for automatic tags" : "";
            var hint = Theme.Hint("in this folder and its subfolders" + reading);
            f.Next(Mathf.Min(Flow.Size(hint, Theme.RichSingle), BarWidth));
            GUILayout.Label(hint, Theme.RichSingle, GUILayout.ExpandWidth(true));
            f.End();
        }

        /// <summary>"Black · Swimwear · Suggestive" for the footer.</summary>
        private string TagLine(string path)
        {
            var tags = _tags.Get(path);
            var own = tags.Count == 0 ? null : string.Join(" · ", tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).Select(TagFilter.ValueOf).ToArray());
            var entry = _auto != null ? _index.Entries.FirstOrDefault(x => FileOps.Same(x.Path, path)) : null;
            var auto = entry != null ? _auto.Get(entry.Path, entry.Modified) : new string[0];
            var autoText = auto.Length == 0 ? null : Theme.Hint(string.Join(" · ", auto.Select(TagFilter.ValueOf).ToArray()));
            if (own == null) return autoText;
            return autoText == null ? own : own + "   " + autoText;
        }

        // ---------------------------------------------------------------- top bar

        // Bars that wrap onto more rows when the window is narrow.
        private readonly Flow _topFlow = new Flow(), _filterFlow = new Flow(), _footFlow = new Flow();
        private readonly TagPanel _details = new TagPanel();
        private float BarWidth => _rect.width - 20;

        private void DrawTopBar()
        {
            var f = _topFlow;
            var skin = Theme.Skin;
            f.Begin(BarWidth);

            if (_kind.HasSex)
            {
                f.Next(Flow.Fixed(66, Theme.Choice) + Flow.Fixed(54, Theme.Choice) + 10);
                if (GUILayout.Toggle(_sex == 1, "Female", Theme.Choice, GUILayout.Width(66)) && _sex != 1) Later(() => { SetSex(1, true); Rescan(); });
                if (GUILayout.Toggle(_sex == 0, "Male", Theme.Choice, GUILayout.Width(54)) && _sex != 0) Later(() => { SetSex(0, true); Rescan(); });
                GUILayout.Space(10);
            }
            f.Next(Flow.Size("Search", Styles.Small) + Flow.Fixed(150, skin.textField) + (_search?.Length > 0 ? Flow.Fixed(22) : 0));
            GUILayout.Label("Search", Styles.Small, GUILayout.ExpandWidth(false));
            GUI.SetNextControlName("ob_search");
            _search = GUILayout.TextField(_search ?? "", GUILayout.Width(150));
            if (_search.Length > 0 && GUILayout.Button("x", GUILayout.Width(22))) _search = "";

            f.Spring();

            f.Next(Flow.Size("Sort", Styles.Small) + Flow.Fixed(50, Theme.Choice) + Flow.Fixed(40, Theme.Choice) + Flow.Fixed(58, Theme.Choice) + Flow.Fixed(52, Theme.Choice) + 8, true);
            GUILayout.Label("Sort", Styles.Small, GUILayout.ExpandWidth(false));
            SortChoice(SortBy.Name, "Name", 50);
            SortChoice(SortBy.FileName, "File", 40);
            SortChoice(SortBy.Newest, "Newest", 58);
            SortChoice(SortBy.Oldest, "Oldest", 52);

            GUILayout.Space(8);
            f.Next(Flow.Size("Subfolders", skin.toggle), true);
            var sub = GUILayout.Toggle(_p.IncludeSubfolders.Value, "Subfolders", GUILayout.ExpandWidth(false));
            if (sub != _p.IncludeSubfolders.Value)
            {
                _p.IncludeSubfolders.Value = sub;
                Later(Rescan);
            }

            if (_kind.TopBarExtrasWidth > 0) f.Next(_kind.TopBarExtrasWidth, true);
            if (_kind.DrawTopBarExtras()) Later(Rescan);

            GUILayout.Space(6);
            f.Next(6 + Flow.Fixed(_filter.Count > 0 ? 68 : 48), true);
            var tagLabel = _filter.Count > 0 ? $"Tags ({_filter.Count})" : "Tags";
            if (GUILayout.Button(new GUIContent(tagLabel, $"Show only {Ns} with certain tags (opens the tag wheel)"), _filter.Count > 0 ? Theme.Primary : Theme.Skin.button, GUILayout.Width(_filter.Count > 0 ? 68 : 48)))
            {
                var at = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                Later(() => _radial.ShowFilter(at));
            }

            GUILayout.Space(4);
            var dupeLabel = !_dupeMode ? "Duplicates" : _dupes.Busy ? "Checking..." : $"Duplicates ({_dupes.SetCount})";
            f.Next(4 + Flow.Size("Duplicates (999)", skin.toggle), true);
            var dupe = GUILayout.Toggle(_dupeMode, new GUIContent(dupeLabel, $"Show only {Ns} that have a copy in this folder or its subfolders{(_kind.NearTolerance > 0 ? " (or a near-identical one)" : "")}"), GUILayout.ExpandWidth(false));
            if (dupe != _dupeMode) Later(() => SetDuplicateMode(dupe));

            if (_mods != null)
            {
                GUILayout.Space(6);
                var label = _mods.Pending > 0 ? "Missing mods..." : _missingCount > 0 ? $"Missing mods ({_missingCount})" : "Missing mods";
                f.Next(6 + Flow.Size("Missing mods (999)", skin.toggle), true);
                var only = GUILayout.Toggle(_onlyMissing, new GUIContent(label, $"Show only {Ns} that use zipmods you don't have"), GUILayout.ExpandWidth(false));
                if (only != _onlyMissing)
                {
                    _onlyMissing = only;
                    _viewKey = null;
                    _gridScroll = Vector2.zero;
                }
            }

            f.Next(8 + Flow.Fixed(80, skin.horizontalSlider) + 6 + Flow.Fixed(64), true);
            GUILayout.Space(8);
            var w = GUILayout.HorizontalSlider(_p.ThumbnailWidth.Value, 60, _kind.MaxThumbWidth, GUILayout.Width(80));
            var wi = Mathf.RoundToInt(w / 5f) * 5;
            if (wi != _p.ThumbnailWidth.Value) _p.ThumbnailWidth.Value = wi;

            GUILayout.Space(6);
            if (GUILayout.Button(new GUIContent("Refresh", "Re-read the folders and pictures"), GUILayout.Width(64)))
                Later(() =>
                {
                    _thumbs.Clear();
                    Rescan();
                });

            f.End();
        }

        private void SortChoice(SortBy value, string label, float width)
        {
            if (GUILayout.Toggle(_p.Sort.Value == value, label, Theme.Choice, GUILayout.Width(width)) && _p.Sort.Value != value)
                _p.Sort.Value = value;
        }

        // ---------------------------------------------------------------- folder tree

        private void DrawTree()
        {
            var e = Event.current;

            if (!_p.ShowFolders.Value)
            {
                // Folded away: a slim strip with a button to bring the folders back.
                if (e.type != EventType.Layout)
                {
                    _treeRows.Clear();
                    _treeViewScreen = Rect.zero;
                }
                GUILayout.BeginVertical(GUILayout.Width(24), GUILayout.ExpandHeight(true));
                if (GUILayout.Button(new GUIContent(Styles.PanelRight, "Show folders"), Styles.TreeArrow, GUILayout.Width(22), GUILayout.Height(22)))
                    _p.ShowFolders.Value = true;
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginVertical(GUILayout.Width(TreeWidth), GUILayout.ExpandHeight(true));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Folders", Styles.SectionHeader, GUILayout.Height(20));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent(Styles.PanelLeft, $"Hide folders (more room for {Ns})"), Styles.TreeArrow, GUILayout.Width(22), GUILayout.Height(20)))
                _p.ShowFolders.Value = false;
            GUILayout.EndHorizontal();

            _treeScroll = GUILayout.BeginScrollView(_treeScroll, false, false, GUIStyle.none, Theme.Skin.verticalScrollbar, Theme.Skin.box, GUILayout.ExpandHeight(true));
            if (e.type != EventType.Layout)
            {
                _treeRows.Clear();
                _favRows.Clear();
            }
            DrawFavorites();
            if (_index.Tree != null) DrawNode(_index.Tree);
            else GUILayout.Label(_index.Scanning ? "Reading folders..." : "", Styles.Small);
            GUILayout.EndScrollView();
            if (e.type != EventType.Layout) _treeViewScreen = ToScreen(GUILayoutUtility.GetLastRect());

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("+ Folder", "New folder inside the open one")))
                Later(() => AskNewFolder(_folder));
            if (GUILayout.Button(new GUIContent("Explorer", "Open this folder in Windows Explorer")))
                FileOps.ShowInExplorer(_folder);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawNode(FolderNode node)
        {
            var e = Event.current;
            var isRoot = node.Parent == null;
            var hasKids = node.Children.Count > 0;
            var open = isRoot || _expanded.Contains(node.Path);

            GUILayout.BeginHorizontal();
            GUILayout.Space(node.Depth * 12f);

            var arrow = GUILayoutUtility.GetRect(16, 20, GUILayout.Width(16), GUILayout.Height(20));
            var name = isRoot ? (_kind.HasSex ? $"All {(_sex == 0 ? "male" : "female")} {Ns}" : $"All {Ns}") : node.Name;
            // The count is everything inside, subfolders included.
            var content = new GUIContent($"{name}  <color={Theme.MutedHex}><size=10>{node.TotalCount}</size></color>");
            var row = GUILayoutUtility.GetRect(content, Styles.TreeRow, GUILayout.ExpandWidth(true), GUILayout.Height(20));
            GUILayout.EndHorizontal();

            var inView = _treeViewScreen.Contains(GUIUtility.GUIToScreenPoint(e.mousePosition));

            if (hasKids && !isRoot)
            {
                if (e.type == EventType.Repaint)
                {
                    var hi = arrow.Contains(e.mousePosition) && inView;
                    var icon = open ? (hi ? Styles.ChevronDownHi : Styles.ChevronDown) : (hi ? Styles.ChevronRightHi : Styles.ChevronRight);
                    GUI.DrawTexture(new Rect(arrow.x, arrow.y + 2, 16, 16), icon, ScaleMode.ScaleToFit, true);
                }
                if (!Modal && e.type == EventType.MouseDown && e.button == 0 && inView && arrow.Contains(e.mousePosition))
                {
                    SetExpanded(node.Path, !open);
                    e.Use();
                }
            }

            if (!Modal && inView && row.Contains(e.mousePosition) && e.type == EventType.MouseDown)
            {
                GUIUtility.keyboardControl = 0;
                if (e.button == 0)
                {
                    if (e.clickCount == 2 && hasKids && !isRoot)
                    {
                        SetExpanded(node.Path, !open);
                    }
                    else if (!FileOps.Same(node.Path, _folder))
                    {
                        var path = node.Path;
                        Later(() => OpenFolder(path));
                    }
                    if (!isRoot)
                    {
                        _pressFolder = node;
                        _pressPath = null;
                        _pressScreen = GUIUtility.GUIToScreenPoint(e.mousePosition);
                        GUIUtility.hotControl = _dragControl;
                    }
                }
                else if (e.button == 1)
                {
                    OpenFolderMenu(node, GUIUtility.GUIToScreenPoint(e.mousePosition));
                }
                e.Use();
            }

            if (e.type == EventType.Repaint)
            {
                var current = _favScope == null && FileOps.Same(node.Path, _folder);
                var drop = _dragging && _dropTarget == node;
                var style = current || drop ? Styles.TreeRowOn : Styles.TreeRow;
                style.Draw(row, content, row.Contains(e.mousePosition) && inView, false, false, false);
            }

            if (e.type != EventType.Layout) _treeRows.Add(new KeyValuePair<Rect, FolderNode>(ToScreen(row), node));

            if (open)
                foreach (var c in node.Children)
                    DrawNode(c);
        }

        private void OpenFolder(string path)
        {
            if (_folder != null && FileOps.Same(path, _folder) && _favScope == null) return;
            _favScope = null;
            _folder = path;
            ExpandTo(path);
            RememberFolder();
            _selected.Clear();
            _anchor = null;
            _gridScroll = Vector2.zero;
            _index.Entries.Clear();
            _viewKey = null;
            Rescan();
            _kind.OnFolderChanged(_folder);
        }

        private void ExpandTo(string path)
        {
            var root = FileOps.Normalize(Root(_sex));
            var p = FileOps.Normalize(path);
            while (p.Length > root.Length && FileOps.IsInside(p, root))
            {
                var parent = Path.GetDirectoryName(p);
                if (parent == null) break;
                _expanded.Add(parent);
                p = FileOps.Normalize(parent);
            }
        }

        private static Rect ToScreen(Rect r)
        {
            var p = GUIUtility.GUIToScreenPoint(r.position);
            return new Rect(p.x, p.y, r.width, r.height);
        }

        // ---------------------------------------------------------------- thumbnail grid

        private void DrawGrid()
        {
            var e = Event.current;
            var area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (e.type != EventType.Layout) _gridRect = area;
            area = _gridRect;

            if (e.type == EventType.Repaint) Theme.Skin.box.Draw(area, false, false, false, false);

            var view = View();
            var tw = (float)_p.ThumbnailWidth.Value;
            var th = Mathf.Round(tw * _kind.ThumbAspect);
            var showFile = _p.ShowFileNames.Value;
            var labelH = showFile ? 42f : 30f;
            var cellW = tw + 8f;
            var cellH = th + 8f + labelH;
            var innerW = area.width - 14f;
            var cols = Mathf.Max(1, (int)((innerW - Gap) / (cellW + Gap)));
            // Spread spare width between the columns.
            var spare = innerW - Gap - cols * (cellW + Gap);
            var gapX = Gap + (cols > 1 ? Mathf.Max(0, spare) / cols : 0);
            // Two blocks when there are favorites: "Favorites" rows first, then the rest.
            var fav = _favCount;
            var rest = view.Count - fav;
            var rowH = cellH + Gap;
            var rowsA = (fav + cols - 1) / cols;
            var rowsB = (rest + cols - 1) / cols;
            var yA = Gap + (fav > 0 ? HeaderH : 0);
            var yB = fav > 0 ? yA + rowsA * rowH + (rest > 0 ? HeaderH : 0) : Gap;
            var bottom = rest > 0 ? yB + rowsB * rowH : yA + rowsA * rowH;
            var content = new Rect(0, 0, innerW, Mathf.Max(area.height - 2, bottom));
            _gridCols = cols;
            _gridRowH = rowH;
            _gridYA = yA;
            _gridYB = yB;
            if (e.type == EventType.Repaint) _gridAreaH = area.height;

            _gridScroll = GUI.BeginScrollView(area, _gridScroll, content, false, false, GUIStyle.none, Theme.Skin.verticalScrollbar);
            var visible = new Rect(_gridScroll.x, _gridScroll.y, area.width, area.height);
            var mouseIn = visible.Contains(e.mousePosition);

            if (view.Count == 0 && e.type == EventType.Repaint)
            {
                string msg;
                if (_index.Scanning) msg = $"Reading {Ns}...";
                else if (_dupeMode && _dupes.Busy) msg = $"Comparing {_dupes.Checked} of {_dupes.Total} {Ns}...";
                else if (_dupeMode) msg = $"No duplicates in this folder and its subfolders.";
                else if (_search.Length > 0) msg = $"No {N} matches \"{_search}\".";
                else if (_index.Tree != null && _index.Find(_folder)?.Children.Count > 0 && !_p.IncludeSubfolders.Value) msg = $"No {Ns} in this folder. Open a folder on the left, or tick Subfolders.";
                else msg = $"No {Ns} in this folder.";
                Styles.Placeholder.Draw(new Rect(visible.x, visible.y, visible.width, 60), msg, false, false, false, false);
            }

            if (e.type == EventType.Repaint && fav > 0)
            {
                Styles.SectionHeader.Draw(new Rect(Gap, Gap, innerW - 2 * Gap, HeaderH - 4), $"Favorites  <color={Theme.MutedHex}>{fav}</color>", false, false, false, false);
                if (rest > 0)
                {
                    var hy = yB - HeaderH;
                    var c0 = GUI.color;
                    GUI.color = Theme.Col(Theme.BorderHex);
                    GUI.DrawTexture(new Rect(Gap, hy + 2, innerW - 2 * Gap, 1), Styles.White);
                    GUI.color = c0;
                    Styles.SectionHeader.Draw(new Rect(Gap, hy + 2, innerW - 2 * Gap, HeaderH - 4), $"Other {Ns}  <color={Theme.MutedHex}>{rest}</color>", false, false, false, false);
                }
            }

            var handled = false;
            DrawBlock(e, view, 0, fav, yA);
            DrawBlock(e, view, fav, rest, yB);

            void DrawBlock(Event ev, List<CardEntry> items, int offset, int count, float top)
            {
                if (count <= 0) return;
                var blockRows = (count + cols - 1) / cols;
                var firstRow = Mathf.Max(0, (int)((_gridScroll.y - top) / rowH));
                var lastRow = Mathf.Min(blockRows - 1, (int)((_gridScroll.y + area.height - top) / rowH));
                for (var r = firstRow; r <= lastRow; r++)
                for (var c = 0; c < cols; c++)
                {
                    var j = r * cols + c;
                    if (j >= count) break;
                    var entry = items[offset + j];
                    var cell = new Rect(Gap + c * (cellW + gapX), top + r * rowH, cellW, cellH);
                    var over = mouseIn && cell.Contains(ev.mousePosition);
                    if (!handled && over) handled = CellEvent(ev, entry, items, cell);
                    if (ev.type == EventType.Repaint) DrawCell(entry, cell, tw, th, over, showFile);
                }
            }

            // Clicks on empty space.
            if (!Modal && !handled && mouseIn && e.type == EventType.MouseDown)
            {
                GUIUtility.keyboardControl = 0;
                if (e.button == 0 && !e.control && !e.shift)
                {
                    _selected.Clear();
                    _anchor = null;
                }
                else if (e.button == 1) OpenGridMenu(GUIUtility.GUIToScreenPoint(e.mousePosition));
                e.Use();
            }

            GUI.EndScrollView(true);
        }

        private void DrawCell(CardEntry entry, Rect cell, float tw, float th, bool hover, bool showFile)
        {
            var selected = _selected.Contains(entry.Path);
            var bg = selected ? Styles.CellOn : hover ? Styles.CellHover : Styles.Cell;
            bg.Draw(cell, false, false, false, false);

            var pic = new Rect(cell.x + 4, cell.y + 4, tw, th);
            var tex = _kind.HasPicture(entry.Path) ? _thumbs.Get(entry.Path, entry.Modified) : null;
            if (tex != null) GUI.DrawTexture(pic, tex, ScaleMode.ScaleToFit, true);
            else if (!_kind.HasPicture(entry.Path)) Styles.Placeholder.Draw(pic, _kind.NoPictureText(entry.Path), false, false, false, false);
            else Styles.Placeholder.Draw(pic, _thumbs.Failed(entry.Path) ? "no picture" : "...", false, false, false, false);

            // Missing zipmods: red badge top-right; hovering the card names them in the footer.
            if (_mods != null)
            {
                var miss = _mods.Get(entry.Path, entry.Modified);
                if (miss != null && miss.Count > 0)
                {
                    GUI.DrawTexture(new Rect(pic.xMax - StarSize - 2, pic.y + 2, StarSize, StarSize), Styles.MissingBadge, ScaleMode.ScaleToFit, true);
                    if (hover) _hoverTip = Theme.Error(MissingSummary(miss)) + Theme.Hint("   ·   right-click › Missing mods");
                }
            }

            // Color tags as small swatches along the bottom of the picture; other tags as a count.
            var tags = _tags.Get(entry.Path);
            if (tags.Count > 0)
            {
                var x = pic.x + 4;
                var others = 0;
                foreach (var t in tags)
                {
                    var col = TagRadial.ColorOf(TagFilter.ValueOf(t));
                    if (col.HasValue && x < pic.xMax - 30)
                    {
                        GUI.DrawTexture(new Rect(x, pic.yMax - 15, 11, 11), TagRadial.Dot(col.Value), ScaleMode.StretchToFill, true);
                        x += 13;
                    }
                    else others++;
                }
                if (others > 0) Styles.Small.Draw(new Rect(x + 1, pic.yMax - 18, 40, 16), $"<color={Theme.NeonHex}>#{others}</color>", false, false, false, false);
            }

            // Duplicates: the set number, and which copy is kept.
            if (_dupeMode && _dupes.SetOf.TryGetValue(entry.Path, out var setNo))
            {
                var keep = _keepers.Contains(entry.Path);
                var label = $"set {setNo}" + (_dupes.Near.Contains(entry.Path) ? " · similar" : "") + (keep ? $"  <color={Theme.NeonHex}>keep</color>" : "");
                var r = new Rect(pic.x + 2, pic.y + pic.height - 38, pic.width - 4, 18);
                var c0 = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.6f);
                GUI.DrawTexture(new Rect(r.x, r.y, Mathf.Min(r.width, Styles.Small.CalcSize(new GUIContent(label)).x + 6), r.height), Styles.White);
                GUI.color = c0;
                Styles.Small.Draw(new Rect(r.x + 3, r.y, r.width, r.height), label, false, false, false, false);
            }

            // Star: always on favorites, on hover for the rest (click it to toggle).
            var star = StarRect(cell);
            var isFav = _favs.Contains(entry.Path);
            if (isFav || hover)
            {
                var onStar = hover && star.Contains(Event.current.mousePosition);
                var icon = isFav ? Styles.StarOn : onStar ? Styles.StarHover : Styles.StarOff;
                GUI.DrawTexture(star, icon, ScaleMode.ScaleToFit, true);
            }

            var nameRect = new Rect(cell.x + 2, pic.yMax + 3, cell.width - 4, 28);
            var name = string.IsNullOrEmpty(entry.Name) ? "(no name)" : entry.Name;
            Styles.CardName.Draw(nameRect, name, false, false, false, false);
            if (showFile && !string.Equals(entry.FileName, entry.Name, StringComparison.OrdinalIgnoreCase))
                Styles.CardFile.Draw(new Rect(cell.x + 2, nameRect.y + 27, cell.width - 4, 14), entry.FileName, false, false, false, false);
        }

        private static Rect StarRect(Rect cell) => new Rect(cell.x + 6, cell.y + 6, StarSize, StarSize);

        /// <summary>Mouse down on a card. Returns true if it took the event.</summary>
        private bool CellEvent(Event e, CardEntry entry, List<CardEntry> view, Rect cell)
        {
            if (Modal || e.type != EventType.MouseDown) return false;
            GUIUtility.keyboardControl = 0;
            var path = entry.Path;

            if (e.button == 0 && StarRect(cell).Contains(e.mousePosition))
            {
                // The star toggles just this card, whatever else is selected.
                var on = !_favs.Contains(path);
                Later(() => SetFavorite(new List<string> { path }, on));
                e.Use();
                return true;
            }

            if (e.button == 0)
            {
                if (e.clickCount == 2)
                {
                    SelectOnly(path);
                    Later(RunPrimary);
                    e.Use();
                    return true;
                }

                if (e.shift && _anchor != null) SelectRange(view, _anchor, path, e.control);
                else if (e.control)
                {
                    if (!_selected.Remove(path)) _selected.Add(path);
                    _anchor = path;
                }
                else
                {
                    _pressWasSelected = _selected.Contains(path);
                    if (!_pressWasSelected) SelectOnly(path);
                    _anchor = path;
                }

                _pressPath = path;
                _pressFolder = null;
                _pressScreen = GUIUtility.GUIToScreenPoint(e.mousePosition);
                GUIUtility.hotControl = _dragControl;
                e.Use();
                return true;
            }

            if (e.button == 1)
            {
                if (!_selected.Contains(path)) SelectOnly(path);
                _anchor = path;
                OpenCardMenu(GUIUtility.GUIToScreenPoint(e.mousePosition));
                e.Use();
                return true;
            }
            return false;
        }

        private void SelectOnly(string path)
        {
            _selected.Clear();
            _selected.Add(path);
            _anchor = path;
        }

        private void SelectRange(List<CardEntry> view, string from, string to, bool add)
        {
            var a = view.FindIndex(x => FileOps.Same(x.Path, from));
            var b = view.FindIndex(x => FileOps.Same(x.Path, to));
            if (a < 0 || b < 0)
            {
                SelectOnly(to);
                return;
            }
            if (!add) _selected.Clear();
            for (var i = Mathf.Min(a, b); i <= Mathf.Max(a, b); i++) _selected.Add(view[i].Path);
        }

        /// <summary>Selected cards in the order they're shown.</summary>
        private List<string> SelectedInOrder() => View().Where(x => _selected.Contains(x.Path)).Select(x => x.Path).ToList();

        private CardEntry Primary()
        {
            var view = View();
            if (_anchor != null && _selected.Contains(_anchor))
            {
                var a = view.FirstOrDefault(x => FileOps.Same(x.Path, _anchor));
                if (a != null) return a;
            }
            return view.FirstOrDefault(x => _selected.Contains(x.Path));
        }

        // ---------------------------------------------------------------- footer

        private void DrawFooter()
        {
            var view = View();
            var f = _footFlow;
            f.Begin(BarWidth);

            var info = $"{view.Count} {(view.Count == 1 ? N : Ns)}";
            if (_favCount > 0) info += $"  ·  {_favCount} favorite{(_favCount == 1 ? "" : "s")}";
            if (_mods != null && _missingCount > 0 && !_onlyMissing) info += $"  ·  {_missingCount} missing mods";
            if (_selected.Count > 0) info += $"  ·  {_selected.Count} selected";
            f.Next(Flow.Size(info, Styles.Small) + 10);
            GUILayout.Label(info, Styles.Small, GUILayout.ExpandWidth(false));
            GUILayout.Space(10);

            var target = _kind.TargetLine(_sex);
            f.Next(Mathf.Min(Flow.Size(target, Theme.RichSingle), BarWidth));
            GUILayout.Label(target, Theme.RichSingle, GUILayout.ExpandWidth(true));
            if (_kind.FooterExtrasWidth > 0) f.Next(_kind.FooterExtrasWidth, true);
            _kind.DrawFooterExtras();

            f.Next(_kind.Actions.Sum(a => Flow.Fixed(a.Width)), true);

            // The kind's actions, primary last (rightmost), as in a dialog.
            var was = GUI.enabled;
            foreach (var a in _kind.Actions.OrderBy(x => x.Primary ? 1 : 0))
            {
                GUI.enabled = was && !Modal && _selected.Count > 0 && (a.CanRun == null || a.CanRun(_sex));
                var act = a;
                if (GUILayout.Button(new GUIContent(a.Label, a.Tooltip), a.Primary ? Theme.Primary : Theme.Skin.button, GUILayout.Width(a.Width)))
                    Later(() => RunAction(act));
            }
            GUI.enabled = was;

            f.End();

            var line = "";
            if (_status != null && Time.unscaledTime < _statusUntil) line = _statusError ? Theme.Error(_status) : _status;
            else if (_hoverTip != null) line = _hoverTip;
            else
            {
                var p = Primary();
                var pm = p != null && _mods != null ? _mods.Get(p.Path, p.Modified) : null;
                if (pm != null && pm.Count > 0) line = Theme.Error(MissingSummary(pm)) + Theme.Hint("   ·   right-click › Missing mods");
                else if (p != null)
                {
                    var tl = TagLine(p.Path);
                    line = Theme.Hint(Path.GetFileName(p.Path)) + (tl != null ? "   ·   " + tl : "")
                        + (_selected.Count == 1 ? Theme.Hint(tl != null ? "   ·   T to tag" : "   ·   T to tag, right-click for rename, move and more") : Theme.Hint("   ·   T tags all selected"));
                }
                else if (_dupeMode && !_dupes.Busy && _dupes.SetCount > 0) line = Theme.Hint($"{_dupes.SetCount} sets of duplicates  ·  right-click empty space › Select extra copies, then Del (stars and tags move to the copy that's kept)");
                else if (!string.IsNullOrEmpty(GUI.tooltip)) line = Theme.Hint(GUI.tooltip);
            }
            GUILayout.Label(line, Styles.Status, GUILayout.Height(18));
        }

        private void RunAction(CardAction action)
        {
            var p = Primary();
            if (p == null || action?.Run == null) return;
            if (action.CanRun != null && !action.CanRun(_sex)) return;
            var result = action.Run(p.Path, _sex);
            if (!string.IsNullOrEmpty(result.Message)) SetStatus(result.Message, result.Error);
        }

        private void RunPrimary() => RunAction(_kind.Actions.FirstOrDefault(a => a.Primary) ?? _kind.Actions.FirstOrDefault());

        private enum Step { Left, Right, Up, Down, PageUp, PageDown, First, Last }

        /// <summary>
        /// Moves the selection through the grid as it's laid out (Up / Down by row, across the Favorites and
        /// the rest), scrolls it into view, and for kinds that want it (poses) runs the primary action.
        /// </summary>
        private void StepGrid(Step step)
        {
            var view = View();
            if (view.Count == 0) return;
            var cols = Mathf.Max(1, _gridCols);
            var fav = _favCount;
            var rest = view.Count - fav;
            var current = Primary();
            var idx = current != null ? view.FindIndex(x => FileOps.Same(x.Path, current.Path)) : -1;
            int next;
            if (idx < 0) next = step == Step.Last || step == Step.Up || step == Step.Left ? view.Count - 1 : 0;
            else
            {
                next = idx;
                var rows = step == Step.PageUp || step == Step.PageDown ? Mathf.Max(1, Mathf.FloorToInt(_gridAreaH / _gridRowH) - 1) : 1;
                switch (step)
                {
                    case Step.Left: next = Mathf.Max(0, idx - 1); break;
                    case Step.Right: next = Mathf.Min(view.Count - 1, idx + 1); break;
                    case Step.First: next = 0; break;
                    case Step.Last: next = view.Count - 1; break;
                    case Step.Up:
                    case Step.PageUp:
                        for (var i = 0; i < rows; i++) next = RowStep(next, -1, cols, fav, rest);
                        break;
                    default:
                        for (var i = 0; i < rows; i++) next = RowStep(next, 1, cols, fav, rest);
                        break;
                }
            }
            if (next == idx) return;

            var entry = view[next];
            _selected.Clear();
            _selected.Add(entry.Path);
            _anchor = entry.Path;

            // Keep it on screen.
            var y = next < fav ? _gridYA + (next / cols) * _gridRowH : _gridYB + ((next - fav) / cols) * _gridRowH;
            if (y < _gridScroll.y) _gridScroll.y = Mathf.Max(0, y - Gap);
            else if (y + _gridRowH > _gridScroll.y + _gridAreaH) _gridScroll.y = y + _gridRowH - _gridAreaH + Gap;

            if (_kind.RunOnStep) RunPrimary();
        }

        /// <summary>One row up or down from <paramref name="i"/>, keeping the column, crossing between the Favorites block and the rest.</summary>
        private static int RowStep(int i, int dir, int cols, int fav, int rest)
        {
            var inFav = i < fav;
            var j = inFav ? i : i - fav;
            var count = inFav ? fav : rest;
            var col = j % cols;
            var row = j / cols;
            var lastRow = (count - 1) / cols;
            if (dir < 0)
            {
                if (row > 0) return i - cols;
                if (!inFav && fav > 0) return Math.Min(((fav - 1) / cols) * cols + col, fav - 1);
                return i;
            }
            if (row < lastRow) return (inFav ? 0 : fav) + Math.Min(j + cols, count - 1);
            if (inFav && rest > 0) return fav + Math.Min(col, rest - 1);
            return i;
        }

        // ---------------------------------------------------------------- drag & drop, keys, resize

        private void HandleDrag(Event e)
        {
            if (_pressPath == null && _pressFolder == null) return;

            var screen = GUIUtility.GUIToScreenPoint(e.mousePosition);
            var type = e.GetTypeForControl(_dragControl);

            if (type == EventType.MouseDrag)
            {
                if (!_dragging && (screen - _pressScreen).magnitude > 6f)
                {
                    _dragging = true;
                    if (_pressPath != null) _dragPaths = _selected.Contains(_pressPath) ? SelectedInOrder() : new List<string> { _pressPath };
                }
                if (_dragging)
                {
                    _dropTarget = RowAt(screen);
                    if (_pressFolder != null && _dropTarget != null &&
                        (FileOps.IsInside(_dropTarget.Path, _pressFolder.Path) || FileOps.Same(_dropTarget.Path, _pressFolder.Parent?.Path)))
                        _dropTarget = null;
                    _favDrop = _pressFolder == null ? FavRowAt(screen) : null;
                    if (_favDrop != null) _dropTarget = null;
                }
                e.Use();
            }
            else if (type == EventType.MouseUp)
            {
                if (_dragging && _favDrop != null && _dragPaths != null)
                {
                    var paths = _dragPaths;
                    var folder = _favDrop;
                    Later(() => AddToFavorites(paths, folder));
                }
                else if (_dragging && _dropTarget != null)
                {
                    var target = _dropTarget.Path;
                    if (_pressFolder != null)
                    {
                        var folder = _pressFolder.Path;
                        Later(() => DoMoveFolder(folder, target));
                    }
                    else
                    {
                        var paths = _dragPaths;
                        Later(() => DoMove(paths, target));
                    }
                }
                else if (!_dragging && _pressPath != null && _pressWasSelected && !e.control && !e.shift)
                {
                    SelectOnly(_pressPath);
                }
                EndDrag();
                e.Use();
            }
            else if (type == EventType.Repaint && _dragging)
            {
                string label;
                if (_pressFolder != null) label = "Folder: " + _pressFolder.Name;
                else if (_dragPaths.Count == 1) label = Path.GetFileNameWithoutExtension(_dragPaths[0]);
                else label = $"{_dragPaths.Count} {Ns}";
                label += _favDrop != null ? "  →  ★ " + (_favDrop.Length > 0 ? _favDrop : "Favorites")
                    : _dropTarget != null ? "  →  " + (_dropTarget.Parent == null ? FolderLabel(_dropTarget.Path) : _dropTarget.Name) : "   (drop on a folder)";
                var size = Styles.Ghost.CalcSize(new GUIContent(label));
                Styles.Ghost.Draw(new Rect(e.mousePosition.x + 16, e.mousePosition.y + 6, size.x, size.y), label, false, false, false, false);
            }
        }

        private void EndDrag()
        {
            _pressPath = null;
            _pressFolder = null;
            _dragging = false;
            _dragPaths = null;
            _dropTarget = null;
            _favDrop = null;
            if (GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
        }

        private string FavRowAt(Vector2 screen)
        {
            if (!_treeViewScreen.Contains(screen)) return null;
            foreach (var kv in _favRows)
                if (kv.Key.Contains(screen))
                    return kv.Value;
            return null;
        }

        // ---------------------------------------------------------------- favorites and their folders

        /// <summary>Favorites (every starred card here and in every folder inside) and your folders under it.</summary>
        private void DrawFavorites()
        {
            var root = Root(_sex);
            var favs = _favs.Paths.Where(p => FileOps.IsInside(p, root)).ToList();
            var star = "<color=#F5C451>★</color>";
            FavRow($"{star} Favorites  <color={Theme.MutedHex}><size=10>{favs.Count}</size></color>", 0, _favScope == "", _favFolders.Names.Count > 0 ? _favOpen : (bool?)null,
                () => OpenFavorites(""), () => _favOpen = !_favOpen, null, "", true);
            if (_favOpen)
                foreach (var f in _favFolders.Names)
                {
                    var name = f;
                    var tag = FolderList.Tag(name);
                    var count = favs.Count(p => _tags.Has(p, tag));
                    FavRow($"{name}  <color={Theme.MutedHex}><size=10>{count}</size></color>", 1,
                        _favScope != null && string.Equals(_favScope, name, StringComparison.OrdinalIgnoreCase), null,
                        () => OpenFavorites(name), null, () => AskFavFolder(name, null), name, false);
                }
            GUILayout.Space(4);
        }

        private void FavRow(string text, int depth, bool current, bool? open, Action click, Action toggle, Action right, string dropName, bool plus)
        {
            var e = Event.current;
            GUILayout.BeginHorizontal();
            GUILayout.Space(depth * 12f);
            var arrow = GUILayoutUtility.GetRect(16, 20, GUILayout.Width(16), GUILayout.Height(20));
            var content = new GUIContent(text);
            var row = GUILayoutUtility.GetRect(content, Styles.TreeRow, GUILayout.ExpandWidth(true), GUILayout.Height(20));
            Rect add = default;
            if (plus) add = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20), GUILayout.Height(20));
            GUILayout.EndHorizontal();

            var inView = _treeViewScreen.Contains(GUIUtility.GUIToScreenPoint(e.mousePosition));
            if (open.HasValue && e.type == EventType.Repaint)
            {
                var hi = arrow.Contains(e.mousePosition) && inView;
                var icon = open.Value ? (hi ? Styles.ChevronDownHi : Styles.ChevronDown) : (hi ? Styles.ChevronRightHi : Styles.ChevronRight);
                GUI.DrawTexture(new Rect(arrow.x, arrow.y + 2, 16, 16), icon, ScaleMode.ScaleToFit, true);
            }
            if (plus && GUI.Button(add, new GUIContent("+", $"New favorites folder (put favorite {Ns} in it from the details panel, or drag them onto it)"), Styles.TreeArrow))
                Later(() => AskFavFolder(null, null));

            if (!Modal && inView && e.type == EventType.MouseDown)
            {
                if (open.HasValue && e.button == 0 && arrow.Contains(e.mousePosition))
                {
                    Later(toggle);
                    e.Use();
                }
                else if (row.Contains(e.mousePosition))
                {
                    GUIUtility.keyboardControl = 0;
                    if (e.button == 0) Later(click);
                    else if (e.button == 1 && right != null) Later(right);
                    e.Use();
                }
            }

            if (e.type == EventType.Repaint)
            {
                var drop = _dragging && _favDrop != null && string.Equals(_favDrop, dropName, StringComparison.OrdinalIgnoreCase);
                (current || drop ? Styles.TreeRowOn : Styles.TreeRow).Draw(row, content, row.Contains(e.mousePosition) && inView, false, false, false);
            }
            if (e.type != EventType.Layout) _favRows.Add(new KeyValuePair<Rect, string>(ToScreen(row), dropName));
        }

        /// <summary>Shows the starred cards (all, or one of your folders) from every folder.</summary>
        private void OpenFavorites(string folder)
        {
            _favScope = folder;
            _folder = Root(_sex);
            _selected.Clear();
            _anchor = null;
            _gridScroll = Vector2.zero;
            _index.Entries.Clear();
            _viewKey = null;
            Rescan();
        }

        /// <summary>Stars the cards and, with a folder name, puts them in that folder.</summary>
        private void AddToFavorites(List<string> paths, string folder)
        {
            if (paths == null || paths.Count == 0) return;
            _favs.Set(paths, true);
            if (!string.IsNullOrEmpty(folder)) _tags.Set(paths, FolderList.Tag(folder), true, _favFolders.Facet);
            _viewKey = null;
            SetStatus((paths.Count == 1 ? Path.GetFileNameWithoutExtension(paths[0]) : $"{paths.Count} {Ns}")
                      + (string.IsNullOrEmpty(folder) ? " added to Favorites" : $" added to Favorites › {folder}"));
        }

        /// <summary>Anything put in one of your folders becomes a favorite too.</summary>
        private void StarFolderMembers(IEnumerable<string> paths)
        {
            var add = paths.Where(p => !_favs.Contains(p) && _tags.Get(p).Any(t => t.StartsWith(FolderList.Group + ":", StringComparison.OrdinalIgnoreCase))).ToList();
            if (add.Count > 0) _favs.Set(add, true);
        }

        /// <param name="existing">Folder to rename or delete (null: a new one).</param>
        /// <param name="add">For a new folder: cards to put in it.</param>
        private void AskFavFolder(string existing, List<string> add)
        {
            _dialog = new Dialog
            {
                Kind = DialogKind.FavFolder,
                Title = existing == null ? "New favorites folder" : "Favorites folder",
                Path = existing,
                Paths = add,
                Text1 = existing ?? "",
                Original1 = existing ?? "",
            };
        }

        private void ApplyFavFolder(Dialog d)
        {
            var name = FolderList.Clean(d.Text1);
            if (d.Path == null)
            {
                if (!_favFolders.Add(name))
                {
                    throw new InvalidOperationException(name.Length == 0 ? "Give the folder a name." : $"There's already a folder called {name}.");
                }
                _favOpen = true;
                if (d.Paths != null && d.Paths.Count > 0) AddToFavorites(d.Paths, name);
                else SetStatus($"New folder: Favorites › {name}");
            }
            else if (!string.Equals(d.Path, name, StringComparison.Ordinal))
            {
                if (!_favFolders.Rename(d.Path, name))
                {
                    throw new InvalidOperationException(name.Length == 0 ? "Give the folder a name." : $"There's already a folder called {name}.");
                }
                var oldTag = FolderList.Tag(d.Path);
                var members = _favs.Paths.Where(p => _tags.Has(p, oldTag)).ToList();
                _tags.Set(members, FolderList.Tag(name), true, _favFolders.Facet);
                _tags.Set(members, oldTag, false, null);
                if (_favScope != null && string.Equals(_favScope, d.Path, StringComparison.OrdinalIgnoreCase)) _favScope = name;
                SetStatus($"Renamed folder to {name}");
            }
            _dialog = null;
            _viewKey = null;
        }

        private void DeleteFavFolder(string name)
        {
            var tag = FolderList.Tag(name);
            _tags.Set(_favs.Paths.Where(p => _tags.Has(p, tag)).ToList(), tag, false, null);
            _favFolders.Remove(name);
            if (_favScope != null && string.Equals(_favScope, name, StringComparison.OrdinalIgnoreCase)) OpenFavorites("");
            SetStatus($"Deleted folder {name} (the {Ns} stay favorites)");
            _dialog = null;
            _viewKey = null;
        }

        private FolderNode RowAt(Vector2 screen)
        {
            if (!_treeViewScreen.Contains(screen)) return null;
            foreach (var kv in _treeRows)
                if (kv.Key.Contains(screen))
                    return kv.Value;
            return null;
        }

        private void HandleKeys(Event e)
        {
            if (e.type != EventType.KeyDown) return;
            if (GUIUtility.keyboardControl != 0) return; // typing in a field
            if (!new Rect(0, 0, _rect.width, _rect.height).Contains(e.mousePosition)) return;

            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    if (_menuOpen) CloseMenu();
                    else _selected.Clear();
                    e.Use();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_selected.Count > 0) Later(RunPrimary);
                    e.Use();
                    break;
                case KeyCode.F2:
                    if (_selected.Count == 1) Later(AskRename);
                    e.Use();
                    break;
                case KeyCode.LeftArrow: Later(() => StepGrid(Step.Left)); e.Use(); break;
                case KeyCode.RightArrow: Later(() => StepGrid(Step.Right)); e.Use(); break;
                case KeyCode.UpArrow: Later(() => StepGrid(Step.Up)); e.Use(); break;
                case KeyCode.DownArrow: Later(() => StepGrid(Step.Down)); e.Use(); break;
                case KeyCode.PageUp: Later(() => StepGrid(Step.PageUp)); e.Use(); break;
                case KeyCode.PageDown: Later(() => StepGrid(Step.PageDown)); e.Use(); break;
                case KeyCode.Home: Later(() => StepGrid(Step.First)); e.Use(); break;
                case KeyCode.End: Later(() => StepGrid(Step.Last)); e.Use(); break;
                case KeyCode.Backspace:
                    {
                        // A kind's undo-style action (poses: put back the pose from before trying them).
                        var undo = _kind.Actions.FirstOrDefault(a => a.Key == "Backspace");
                        if (undo?.Run != null && (undo.CanRun == null || undo.CanRun(_sex)))
                            Later(() =>
                            {
                                var r = undo.Run(Primary()?.Path, _sex);
                                if (!string.IsNullOrEmpty(r.Message)) SetStatus(r.Message, r.Error);
                            });
                        e.Use();
                    }
                    break;
                case KeyCode.T:
                    {
                        var at = GUIUtility.GUIToScreenPoint(e.mousePosition);
                        Later(() => OpenTagWheel(at));
                        e.Use();
                    }
                    break;
                case KeyCode.Delete:
                    if (_selected.Count > 0) Later(() => AskRecycle(SelectedInOrder(), null));
                    e.Use();
                    break;
                case KeyCode.A:
                    if (e.control)
                    {
                        foreach (var x in View()) _selected.Add(x.Path);
                        e.Use();
                    }
                    break;
                case KeyCode.F:
                    if (e.control)
                    {
                        GUI.FocusControl("ob_search");
                        e.Use();
                    }
                    break;
            }
        }

        private void HandleResize(Event e)
        {
            var handle = new Rect(_rect.width - 16, _rect.height - 16, 16, 16);
            if (e.type == EventType.Repaint) Styles.Small.Draw(handle, "//", false, false, false, false);
            var id = GUIUtility.GetControlID(FocusType.Passive);
            var screen = GUIUtility.GUIToScreenPoint(e.mousePosition);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && handle.Contains(e.mousePosition))
                    {
                        _resizing = true;
                        _resizeStart = screen;
                        _resizeOrig = new Vector2(_rect.width, _rect.height);
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_resizing && GUIUtility.hotControl == id)
                    {
                        _rect.width = Mathf.Max(560, _resizeOrig.x + (screen.x - _resizeStart.x));
                        _rect.height = Mathf.Max(380, _resizeOrig.y + (screen.y - _resizeStart.y));
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (_resizing && GUIUtility.hotControl == id)
                    {
                        _resizing = false;
                        GUIUtility.hotControl = 0;
                        SaveRect();
                        e.Use();
                    }
                    break;
            }
        }

        // ================================================================ context menus

        private sealed class MenuItem
        {
            public string Label;
            public string Key;
            public Action Do;
            public bool Enabled = true;
            public bool Separator;
        }

        private void Item(string label, Action act, bool enabled = true, string key = null) =>
            _menu.Add(new MenuItem { Label = label, Do = act, Enabled = enabled, Key = key });

        private void Separator() => _menu.Add(new MenuItem { Separator = true });

        private void OpenMenu(string title, Vector2 screen)
        {
            _menuTitle = title;
            _menuScreen = screen;
            _menuPlace = true;
            _menuOpen = true;
        }

        private void CloseMenu()
        {
            _menuOpen = false;
            _menu.Clear();
        }

        private void OpenCardMenu(Vector2 screen)
        {
            _menu.Clear();
            var paths = SelectedInOrder();
            var one = paths.Count == 1;
            var p = Primary();
            foreach (var a in _kind.Actions)
            {
                var act = a;
                Item(a.MenuLabel ?? a.Label, () => RunAction(act), p != null && (a.CanRun == null || a.CanRun(_sex)), a.Primary ? "Enter" : a.Key);
            }
            Separator();
            var allFav = paths.Count > 0 && paths.All(_favs.Contains);
            if (allFav) Item(one ? "Remove from favorites" : $"Remove {paths.Count} from favorites", () => SetFavorite(paths, false));
            else Item(one ? "Add to favorites" : $"Add {paths.Count} to favorites", () => SetFavorite(paths, true));
            var menuAt = screen;
            Item(one ? "Tags..." : $"Tag {paths.Count} {Ns}...", () => _radial.ShowTag(menuAt, paths), true, "T");
            if (_mods != null)
            {
                var broken = paths.Where(x => _modified.TryGetValue(x, out var m) && _mods.HasMissing(x, m)).ToList();
                if (broken.Count > 0)
                {
                    var n = broken.Count == 1 ? _mods.Get(broken[0], _modified[broken[0]]).Select(x => x.Guid).Distinct().Count() : 0;
                    Item(one ? $"Missing mods ({n})..." : $"Missing mods in {broken.Count} {Ns}...", () => ShowMissing(broken));
                }
            }
            Item("Rename...", AskRename, one, "F2");
            Item(one ? "Move to..." : $"Move {paths.Count} {Ns} to...", () => AskMove(paths, null));
            Item("Duplicate", () =>
            {
                var copy = FileOps.Duplicate(paths[0]);
                SelectOnly(copy);
                SetStatus("Copied to " + Path.GetFileName(copy));
                Changed(new List<ListChange> { new ListChange { NewPath = copy, NewName = _kind.ReadName(copy), Added = true } });
            }, one);
            Item("Show in Explorer", () => FileOps.ShowInExplorer(paths[0]), one);
            Separator();
            Item(one ? "Send to Recycle Bin" : $"Send {paths.Count} to Recycle Bin", () => AskRecycle(paths, null), true, "Del");

            OpenMenu(one ? (p?.Name ?? "") : $"{paths.Count} {Ns}", screen);
        }

        private void OpenGridMenu(Vector2 screen)
        {
            _menu.Clear();
            Item("New folder here...", () => AskNewFolder(_folder));
            Item("Select all", () => { foreach (var x in View()) _selected.Add(x.Path); }, View().Count > 0, "Ctrl+A");
            if (_dupeMode) Item("Select extra copies (keep one of each)", SelectExtraCopies, !_dupes.Busy && _dupes.SetCount > 0);
            if (_mods != null)
                Item($"Select {Ns} with missing mods", () =>
                {
                    _selected.Clear();
                    foreach (var x in View().Where(x => _mods.HasMissing(x.Path, x.Modified))) _selected.Add(x.Path);
                }, _missingCount > 0);
            Item("Refresh", () => { _thumbs.Clear(); Rescan(); });
            Item("Open folder in Explorer", () => FileOps.ShowInExplorer(_folder));
            OpenMenu(FolderLabel(_folder), screen);
        }

        private void OpenFolderMenu(FolderNode node, Vector2 screen)
        {
            _menu.Clear();
            var isRoot = node.Parent == null;
            var path = node.Path;
            Item("Open", () => OpenFolder(path), !FileOps.Same(path, _folder));
            if (node.Children.Count > 0)
            {
                Item("Expand all", () =>
                {
                    foreach (var f in Descendants(node)) _expanded.Add(f.Path);
                    SaveExpanded();
                });
                Item("Collapse all", () =>
                {
                    foreach (var f in Descendants(node)) _expanded.Remove(f.Path);
                    SaveExpanded();
                });
                Separator();
            }
            Item("New folder inside...", () => AskNewFolder(path));
            Item("Rename...", () => AskRenameFolder(path), !isRoot);
            Item("Move to...", () => AskMove(null, path), !isRoot);
            Item("Show in Explorer", () => FileOps.ShowInExplorer(path));
            if (_selected.Count > 0 && !FileOps.Same(path, _folder))
            {
                Separator();
                var sel = SelectedInOrder();
                Item($"Move {sel.Count} selected {(sel.Count == 1 ? N : Ns)} here", () => DoMove(sel, path));
            }
            Separator();
            Item("Send folder to Recycle Bin", () => AskRecycle(null, path), !isRoot);
            OpenMenu(isRoot ? FolderLabel(path) : node.Name, screen);
        }

        private void DrawMenu(int id)
        {
            if (!string.IsNullOrEmpty(_menuTitle)) GUILayout.Label(_menuTitle, Styles.MenuHeader);
            foreach (var item in _menu.ToArray())
            {
                if (item.Separator)
                {
                    var r = GUILayoutUtility.GetRect(1, 7, GUILayout.ExpandWidth(true));
                    if (Event.current.type == EventType.Repaint)
                    {
                        var c = GUI.color;
                        GUI.color = Theme.Col(Theme.BorderHex);
                        GUI.DrawTexture(new Rect(r.x + 4, r.y + 3, r.width - 8, 1), Styles.White);
                        GUI.color = c;
                    }
                    continue;
                }

                var was = GUI.enabled;
                GUI.enabled = item.Enabled;
                var label = item.Key != null ? $"{item.Label}<color={Theme.MutedHex}><size=10>   {item.Key}</size></color>" : item.Label;
                if (GUILayout.Button(label, Styles.MenuItem))
                {
                    var act = item.Do;
                    CloseMenu();
                    Later(act);
                }
                GUI.enabled = was;
            }
            IMGUIUtils.EatInputInRect(_menuRect);
        }

        // ================================================================ dialogs

        private enum DialogKind
        {
            Rename,
            NewFolder,
            RenameFolder,
            Move,
            Recycle,
            Missing,
            FavFolder,
        }

        private sealed class Dialog
        {
            public DialogKind Kind;
            public string Title;
            public Rect Rect;
            public bool FocusWindow = true;
            public bool FocusField = true;
            public string Path;             // card or folder the dialog is about
            public List<string> Paths;      // cards to move / recycle
            public string Text1 = "", Text2 = "";
            public string Original1 = "", Original2 = "";
            public bool Linked;
            public string Error;
            public string Pick;             // Move: chosen folder
            public Vector2 Scroll;
            public string NewFolderName = "";
            public List<MissingRow> Rows;   // Missing
            public float Width = 400;
        }

        private void AskRename()
        {
            var p = Primary();
            if (p == null) return;
            var linked = _p.RenameFileToo.Value;
            _dialog = new Dialog
            {
                Kind = DialogKind.Rename,
                Title = "Rename " + N,
                Path = p.Path,
                Text1 = p.Name ?? "",
                Original1 = p.Name ?? "",
                Text2 = p.FileName,
                Original2 = p.FileName,
                Linked = linked,
            };
        }

        private void AskNewFolder(string parent)
        {
            _dialog = new Dialog { Kind = DialogKind.NewFolder, Title = "New folder", Path = parent, Text1 = "New folder" };
        }

        private void AskRenameFolder(string folder)
        {
            var name = Path.GetFileName(FileOps.Normalize(folder));
            _dialog = new Dialog { Kind = DialogKind.RenameFolder, Title = "Rename folder", Path = folder, Text1 = name, Original1 = name };
        }

        /// <summary>Move cards (<paramref name="paths"/>) or one folder (<paramref name="folder"/>).</summary>
        private void AskMove(List<string> paths, string folder)
        {
            _dialog = new Dialog
            {
                Kind = DialogKind.Move,
                Title = folder != null ? "Move folder" : paths.Count == 1 ? "Move " + N : $"Move {paths.Count} {Ns}",
                Paths = paths,
                Path = folder,
                Pick = null,
                FocusField = false,
            };
        }

        private void AskRecycle(List<string> paths, string folder)
        {
            if (!_p.ConfirmRecycle.Value)
            {
                DoRecycle(paths, folder);
                return;
            }
            _dialog = new Dialog
            {
                Kind = DialogKind.Recycle,
                Title = "Send to Recycle Bin",
                Paths = paths,
                Path = folder,
                FocusField = false,
            };
        }

        private void DrawDialog(int id)
        {
            var d = _dialog;
            if (d == null) return;
            var e = Event.current;

            // Enter / Esc work even while a text field has focus.
            var submit = false;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    submit = true;
                    e.Use();
                }
                else if (e.character == '\n')
                {
                    e.Use(); // the second half of an Enter press
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    Later(() => _dialog = null);
                    e.Use();
                }
            }

            switch (d.Kind)
            {
                case DialogKind.Rename: submit = DrawRename(d, submit); break;
                case DialogKind.NewFolder:
                case DialogKind.RenameFolder: submit = DrawFolderName(d, submit); break;
                case DialogKind.FavFolder: submit = DrawFavFolder(d, submit); break;
                case DialogKind.Move: submit = DrawMove(d, submit); break;
                case DialogKind.Recycle: submit = DrawRecycle(d, submit); break;
                case DialogKind.Missing: submit = DrawMissing(d, submit); break;
            }

            if (!string.IsNullOrEmpty(d.Error))
                GUILayout.Label(Theme.Error(d.Error), Theme.Rich);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(d.Kind == DialogKind.Missing ? "Close" : "Cancel", GUILayout.Width(80))) Later(() => _dialog = null);
            var ok = d.Kind == DialogKind.FavFolder ? (d.Path == null ? "Create" : "Save")
                : d.Kind == DialogKind.Rename ? "Rename" : d.Kind == DialogKind.NewFolder ? "Create"
                : d.Kind == DialogKind.RenameFolder ? "Rename" : d.Kind == DialogKind.Move ? "Move here"
                : d.Kind == DialogKind.Missing ? "Copy list" : "Recycle";
            var was = GUI.enabled;
            GUI.enabled = d.Kind != DialogKind.Move || d.Pick != null;
            if (GUILayout.Button(ok, Theme.Primary, GUILayout.Width(96))) submit = true;
            GUI.enabled = was;
            GUILayout.EndHorizontal();

            if (submit) Later(() => Apply(d));

            if (d.FocusField && e.type == EventType.Repaint)
            {
                GUI.FocusControl("ob_dlg_1");
                d.FocusField = false;
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 26));
            IMGUIUtils.EatInputInRect(d.Rect);
        }

        private bool DrawRename(Dialog d, bool submit)
        {
            if (_kind.HasStoredName)
            {
                GUILayout.Label(_kind.NameLabel, Theme.Rich);
                GUI.SetNextControlName("ob_dlg_1");
                var t1 = GUILayout.TextField(d.Text1);
                if (t1 != d.Text1)
                {
                    d.Text1 = t1;
                    d.Error = null;
                    if (d.Linked) d.Text2 = FileOps.Sanitize(t1);
                }
                GUILayout.Space(6);
            }

            GUILayout.Label("File name", Theme.Rich);
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(_kind.HasStoredName ? "ob_dlg_2" : "ob_dlg_1");
            var t2 = GUILayout.TextField(d.Text2);
            if (t2 != d.Text2)
            {
                d.Text2 = t2;
                d.Error = null;
                d.Linked = false;
            }
            GUILayout.Label(Path.GetExtension(d.Path ?? ".png"), Styles.Small, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            if (!_kind.HasStoredName) return submit;

            var linked = GUILayout.Toggle(d.Linked, $" File name follows the {N} name");
            if (linked != d.Linked)
            {
                d.Linked = linked;
                if (linked) d.Text2 = FileOps.Sanitize(d.Text1);
            }
            return submit;
        }

        private bool DrawFavFolder(Dialog d, bool submit)
        {
            GUILayout.Label(d.Path == null ? "Name for the new folder inside Favorites" : $"Rename {Theme.Hint(d.Path)}, or delete it (the {Ns} in it stay favorites)", Theme.Rich);
            GUI.SetNextControlName("ob_dlg_1");
            var t = GUILayout.TextField(d.Text1);
            if (t != d.Text1)
            {
                d.Text1 = t;
                d.Error = null;
            }
            if (d.Path != null)
            {
                GUILayout.Space(4);
                var name = d.Path;
                if (GUILayout.Button(new GUIContent("Delete folder", $"Delete the folder; its {Ns} stay favorites"), GUILayout.Width(110))) Later(() => DeleteFavFolder(name));
            }
            return submit;
        }

        private bool DrawFolderName(Dialog d, bool submit)
        {
            if (d.Kind == DialogKind.NewFolder)
                GUILayout.Label("New folder in " + Theme.Hint(FolderLabel(d.Path)), Theme.Rich);
            else
                GUILayout.Label("Folder name", Theme.Rich);
            GUI.SetNextControlName("ob_dlg_1");
            var t = GUILayout.TextField(d.Text1);
            if (t != d.Text1)
            {
                d.Text1 = t;
                d.Error = null;
            }
            return submit;
        }

        private bool DrawMove(Dialog d, bool submit)
        {
            if (d.Path != null) GUILayout.Label("Move " + Theme.Hint(Path.GetFileName(FileOps.Normalize(d.Path))) + " into:", Theme.Rich);
            else if (d.Paths.Count == 1) GUILayout.Label("Move " + Theme.Hint(Path.GetFileName(d.Paths[0])) + " to:", Theme.Rich);
            else GUILayout.Label($"Move {d.Paths.Count} {Ns} to:", Theme.Rich);

            d.Scroll = GUILayout.BeginScrollView(d.Scroll, false, false, GUIStyle.none, Theme.Skin.verticalScrollbar, Theme.Skin.box, GUILayout.Height(240));
            foreach (var node in _index.AllFolders())
            {
                var blocked = d.Path != null && (FileOps.IsInside(node.Path, d.Path) || FileOps.Same(node.Path, Path.GetDirectoryName(FileOps.Normalize(d.Path))));
                var here = d.Paths != null && d.Paths.All(x => FileOps.Same(Path.GetDirectoryName(x), node.Path));
                var name = node.Parent == null ? FolderLabel(node.Path) : node.Name;
                GUILayout.BeginHorizontal();
                GUILayout.Space(node.Depth * 12f);
                var was = GUI.enabled;
                GUI.enabled = was && !blocked && !here;
                var picked = d.Pick != null && FileOps.Same(d.Pick, node.Path);
                if (GUILayout.Button(name + (here ? Theme.Hint("  (already here)") : ""), picked ? Styles.TreeRowOn : Styles.TreeRow, GUILayout.Height(20)))
                {
                    if (picked && Event.current.clickCount == 2) submit = true;
                    d.Pick = node.Path;
                    d.Error = null;
                }
                GUI.enabled = was;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.Label("New folder", Styles.Small, GUILayout.ExpandWidth(false));
            GUI.SetNextControlName("ob_dlg_1");
            d.NewFolderName = GUILayout.TextField(d.NewFolderName);
            var was2 = GUI.enabled;
            GUI.enabled = was2 && d.NewFolderName.Trim().Length > 0;
            if (GUILayout.Button(new GUIContent("Create", "Create it inside the picked folder (or the top folder)"), GUILayout.Width(64)))
            {
                var parent = d.Pick ?? Root(_sex);
                var name = d.NewFolderName;
                Later(() =>
                {
                    try
                    {
                        d.Pick = FileOps.CreateFolder(parent, name);
                        d.NewFolderName = "";
                        d.Error = null;
                        ExpandTo(d.Pick);
                        Changed();
                    }
                    catch (Exception ex) { d.Error = ex.Message; }
                });
            }
            GUI.enabled = was2;
            GUILayout.EndHorizontal();

            // Enter in the new-folder field creates the folder, not the move.
            if (submit && GUI.GetNameOfFocusedControl() == "ob_dlg_1" && d.NewFolderName.Trim().Length > 0)
            {
                var parent = d.Pick ?? Root(_sex);
                var name = d.NewFolderName;
                Later(() =>
                {
                    try
                    {
                        d.Pick = FileOps.CreateFolder(parent, name);
                        d.NewFolderName = "";
                        ExpandTo(d.Pick);
                        Changed();
                    }
                    catch (Exception ex) { d.Error = ex.Message; }
                });
                return false;
            }
            return submit && d.Pick != null;
        }

        private bool DrawRecycle(Dialog d, bool submit)
        {
            if (d.Path != null)
            {
                GUILayout.Label($"Send the folder {Theme.Hint(Path.GetFileName(FileOps.Normalize(d.Path)))} and everything in it to the Recycle Bin?", Theme.Rich);
            }
            else
            {
                GUILayout.Label(d.Paths.Count == 1 ? $"Send this {N} to the Recycle Bin?" : $"Send these {d.Paths.Count} {Ns} to the Recycle Bin?", Theme.Rich);
                foreach (var p in d.Paths.Take(6)) GUILayout.Label("   " + Path.GetFileName(p), Styles.Small);
                if (d.Paths.Count > 6) GUILayout.Label($"   ...and {d.Paths.Count - 6} more", Styles.Small);
            }
            GUILayout.Label(Theme.Hint("You can restore them from the Recycle Bin."), Theme.Rich);
            return submit;
        }

        private void Apply(Dialog d)
        {
            if (_dialog != d) return;
            try
            {
                switch (d.Kind)
                {
                    case DialogKind.Rename: DoRename(d); break;
                    case DialogKind.FavFolder: ApplyFavFolder(d); break;
                    case DialogKind.NewFolder:
                    {
                        var made = FileOps.CreateFolder(d.Path, d.Text1);
                        ExpandTo(made);
                        _expanded.Add(d.Path);
                        SetStatus("Created " + Path.GetFileName(made));
                        Changed();
                        break;
                    }
                    case DialogKind.RenameFolder:
                    {
                        var target = Path.Combine(Path.GetDirectoryName(FileOps.Normalize(d.Path)), (d.Text1 ?? "").Trim());
                        var changes = FolderChanges(d.Path, target, false);
                        var moved = FileOps.RenameFolder(d.Path, d.Text1);
                        _favs.FolderMoved(d.Path, moved);
                        _tags.FolderMoved(d.Path, moved);
                        FollowFolder(d.Path, moved);
                        SetStatus("Renamed folder to " + Path.GetFileName(moved));
                        Changed(changes);
                        break;
                    }
                    case DialogKind.Move:
                        if (d.Path != null) DoMoveFolder(d.Path, d.Pick);
                        else DoMove(d.Paths, d.Pick);
                        break;
                    case DialogKind.Recycle:
                        DoRecycle(d.Paths, d.Path);
                        break;
                    case DialogKind.Missing:
                        GUIUtility.systemCopyBuffer = MissingText(d);
                        SetStatus($"Copied {d.Rows.Count} zipmod{(d.Rows.Count == 1 ? "" : "s")} to the clipboard (GUID | name | author | website)");
                        break;
                }
                _dialog = null;
            }
            catch (Exception ex)
            {
                d.Error = ex.Message;
            }
        }

        // ================================================================ file actions

        private static IEnumerable<FolderNode> Descendants(FolderNode node)
        {
            var stack = new Stack<FolderNode>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                yield return n;
                foreach (var c in n.Children) stack.Push(c);
            }
        }

        private static string MissingSummary(List<MissingItem> missing)
        {
            var mods = missing.GroupBy(x => x.Guid).Select(g => g.First().Label).ToList();
            var text = "Missing zipmods: " + string.Join(", ", mods.Take(3).ToArray());
            if (mods.Count > 3) text += $" (+{mods.Count - 3} more)";
            return text;
        }

        private sealed class MissingRow
        {
            public string Guid, Label, Author, Website;
            public bool Installed;
            public int Outfits;
            public List<string> Parts = new List<string>();
        }

        private void ShowMissing(List<string> paths)
        {
            var rows = new Dictionary<string, MissingRow>();
            foreach (var path in paths)
            {
                if (!_modified.TryGetValue(path, out var m)) continue;
                var miss = _mods.Get(path, m);
                if (miss == null) continue;
                foreach (var g in miss.GroupBy(x => x.Guid))
                {
                    if (!rows.TryGetValue(g.Key, out var row))
                    {
                        var f = g.First();
                        rows[g.Key] = row = new MissingRow { Guid = f.Guid, Label = f.Label, Author = f.Author, Website = f.Website, Installed = f.ModInstalled };
                    }
                    row.Outfits++;
                    foreach (var part in g.Select(x => x.Part ?? ModCheck.PartName(x.Property)))
                        if (!row.Parts.Contains(part)) row.Parts.Add(part);
                }
            }
            _dialog = new Dialog
            {
                Kind = DialogKind.Missing,
                Title = paths.Count == 1 ? "Missing zipmods" : $"Missing zipmods in {paths.Count} {Ns}",
                Paths = paths,
                Rows = rows.Values.OrderByDescending(r => r.Outfits).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList(),
                Width = 540,
                FocusField = false,
            };
        }

        private bool DrawMissing(Dialog d, bool submit)
        {
            var notInstalled = d.Rows.Count(r => !r.Installed);
            var head = d.Rows.Count == 1 ? "1 zipmod" : d.Rows.Count + " zipmods";
            GUILayout.Label(head + (d.Paths.Count > 1 ? $" missing across {d.Paths.Count} {Ns}." : " missing.")
                + (notInstalled < d.Rows.Count ? Theme.Hint($"  {d.Rows.Count - notInstalled} installed but outdated (the item isn't in your version).") : ""), Theme.Rich);

            d.Scroll = GUILayout.BeginScrollView(d.Scroll, false, false, GUIStyle.none, Theme.Skin.verticalScrollbar, Theme.Skin.box, GUILayout.Height(Mathf.Min(320, 30 + d.Rows.Count * 52)));
            foreach (var r in d.Rows)
            {
                var by = string.IsNullOrEmpty(r.Author) ? "" : Theme.Hint("  by " + r.Author);
                GUILayout.Label($"<b>{r.Label}</b>{by}", Theme.Rich);
                var state = r.Installed ? Theme.Warn("installed, outdated") : Theme.Error("not installed");
                var used = d.Paths.Count > 1 ? Theme.Hint($"  ·  {r.Outfits} {(r.Outfits == 1 ? N : Ns)}") : "";
                GUILayout.Label($"{state}{used}{Theme.Hint("  ·  " + string.Join(", ", r.Parts.ToArray()))}", Theme.Rich);
                var line = r.Guid + (string.IsNullOrEmpty(r.Website) ? "" : "   " + r.Website);
                GUILayout.Label(Theme.Hint(line), Theme.Rich);
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();
            return submit;
        }

        private string MissingText(Dialog d)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in d.Rows)
            {
                sb.Append(r.Guid);
                if (!string.IsNullOrEmpty(r.Label) && r.Label != r.Guid) sb.Append(" | ").Append(r.Label);
                if (!string.IsNullOrEmpty(r.Author)) sb.Append(" | ").Append(r.Author);
                if (!string.IsNullOrEmpty(r.Website)) sb.Append(" | ").Append(r.Website);
                sb.Append(r.Installed ? " | installed, outdated" : " | not installed");
                if (d.Paths.Count > 1) sb.Append(" | ").Append(r.Outfits).Append(" ").Append(Ns);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private void SetFavorite(List<string> paths, bool favorite)
        {
            var n = _favs.Set(paths, favorite);
            if (n == 0) return;
            SetStatus(favorite
                ? (n == 1 ? "Added to favorites" : $"Added {n} {Ns} to favorites")
                : (n == 1 ? "Removed from favorites" : $"Removed {n} {Ns} from favorites"));
        }

        private void DoRename(Dialog d)
        {
            var path = d.Path;
            var name = d.Text1 ?? "";
            var stem = (d.Text2 ?? "").Trim();
            if (stem.Length == 0) stem = d.Original2;
            if (!FileOps.IsValidName(stem, out var problem)) throw new IOException(problem);

            var nameChanged = name != d.Original1;
            var fileChanged = stem != d.Original2;
            if (!nameChanged && !fileChanged)
            {
                _dialog = null;
                return;
            }

            var change = new ListChange { OldPath = path };
            if (nameChanged)
            {
                _kind.WriteName(path, name);
                _index.Forget(path);
                change.NewName = name;
            }
            if (fileChanged)
            {
                var newPath = FileOps.RenameFile(path, stem);
                _thumbs.Invalidate(path);
                _index.Forget(path);
                _favs.Moved(new[] { new KeyValuePair<string, string>(path, newPath) });
                _tags.Moved(new[] { new KeyValuePair<string, string>(path, newPath) });
                path = newPath;
                change.NewPath = newPath;
            }

            SelectOnly(path);
            SetStatus(nameChanged && fileChanged ? $"Renamed to \"{name}\" ({Path.GetFileName(path)})"
                : nameChanged ? $"Renamed to \"{name}\"" : "File renamed to " + Path.GetFileName(path));
            Changed(new List<ListChange> { change });
        }

        private void DoMove(List<string> paths, string dest)
        {
            if (paths == null || paths.Count == 0 || dest == null) return;
            var errors = new List<string>();
            var moved = FileOps.MoveFiles(paths, dest, errors);
            _favs.Moved(moved);
            _tags.Moved(moved);
            foreach (var p in moved.Keys)
            {
                _thumbs.Invalidate(p);
                _selected.Remove(p);
            }
            var where = FileOps.Same(dest, Root(_sex)) ? FolderLabel(dest) : Path.GetFileName(FileOps.Normalize(dest));
            if (errors.Count > 0) SetStatus($"Moved {moved.Count}, {errors.Count} failed: {errors[0]}", true);
            else SetStatus(moved.Count == 1 ? $"Moved {Path.GetFileNameWithoutExtension(moved.Values.First())} to {where}" : $"Moved {moved.Count} {Ns} to {where}");
            Changed(moved.Select(kv => new ListChange { OldPath = kv.Key, NewPath = kv.Value }).ToList());
        }

        private void DoMoveFolder(string folder, string dest)
        {
            if (folder == null || dest == null) return;
            var changes = FolderChanges(folder, Path.Combine(dest, Path.GetFileName(FileOps.Normalize(folder))), false);
            var moved = FileOps.MoveFolder(folder, dest);
            _favs.FolderMoved(folder, moved);
            _tags.FolderMoved(folder, moved);
            FollowFolder(folder, moved);
            ExpandTo(moved);
            SetStatus($"Moved folder {Path.GetFileName(moved)} into {(FileOps.Same(dest, Root(_sex)) ? FolderLabel(dest) : Path.GetFileName(FileOps.Normalize(dest)))}");
            Changed(changes);
        }

        /// <summary>If the open folder was renamed or moved (or is inside one that was), keep it open.</summary>
        private void FollowFolder(string oldPath, string newPath)
        {
            if (_folder == null) return;
            if (FileOps.IsInside(_folder, oldPath))
            {
                var rest = FileOps.Normalize(_folder).Substring(FileOps.Normalize(oldPath).Length);
                _folder = FileOps.Normalize(newPath) + rest;
                RememberFolder();
            }
            var expanded = _expanded.Where(x => FileOps.IsInside(x, oldPath)).ToList();
            foreach (var x in expanded)
            {
                _expanded.Remove(x);
                _expanded.Add(FileOps.Normalize(newPath) + FileOps.Normalize(x).Substring(FileOps.Normalize(oldPath).Length));
            }
            if (expanded.Count > 0) SaveExpanded();
            _selected.Clear();
        }

        private void DoRecycle(List<string> paths, string folder)
        {
            var list = folder != null ? new List<string> { folder } : paths ?? new List<string>();
            if (list.Count == 0) return;
            var changes = folder != null ? FolderChanges(folder, null, true) : list.Select(p => new ListChange { OldPath = p, Removed = true }).ToList();
            if (folder == null) MergeIntoKeepers(list);
            if (folder != null && FileOps.IsInside(_folder, folder))
            {
                _folder = Path.GetDirectoryName(FileOps.Normalize(folder));
                RememberFolder();
            }
            if (!FileOps.Recycle(list))
            {
                SetStatus("Windows didn't move everything to the Recycle Bin.", true);
            }
            else
            {
                SetStatus(folder != null ? $"Sent folder {Path.GetFileName(FileOps.Normalize(folder))} to the Recycle Bin"
                    : list.Count == 1 ? $"Sent {Path.GetFileName(list[0])} to the Recycle Bin" : $"Sent {list.Count} {Ns} to the Recycle Bin");
            }
            _favs.Removed(list.Where(p => !File.Exists(p) && !Directory.Exists(p)));
            _tags.Removed(list.Where(p => !File.Exists(p) && !Directory.Exists(p)));
            foreach (var p in list)
            {
                _thumbs.Invalidate(p);
                _selected.Remove(p);
            }
            Changed(changes.Where(c => !File.Exists(c.OldPath)).ToList());
        }
    }

    /// <summary>Sorts "Outfit 2" before "Outfit 10".</summary>
    internal sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new NaturalComparer();

        public int Compare(string a, string b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    var si = i;
                    var sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    var na = a.Substring(si, i - si).TrimStart('0');
                    var nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    var c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    var c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
