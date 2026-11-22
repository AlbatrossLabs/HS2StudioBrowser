using System;
using System.Collections.Generic;
using System.Linq;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.Game;
using KKAPI.Utilities;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.UI
{
    /// <summary>
    /// The browser window for catalog items (animations): groups and categories on the left, with Favorites
    /// and Recent above them; a list on the right with a star per row; search across everything; the arrow
    /// keys step through the list and (with Play on click) play as they go.
    /// </summary>
    internal sealed class ItemWindow : IBrowserWindow
    {
        private const float RowH = 24f;
        private const float TreeWidth = 230f;
        private const float StarSize = 16f;

        private enum Scope { Favorites, Recent, Marked, Folder, Group, Category }

        private readonly StudioBrowserPlugin _p;
        private readonly ItemKind _kind;
        private readonly int WindowId;
        private readonly KeyList _favs;
        private readonly KeyList _recent;
        private readonly TagStore _tags;
        private readonly TagSet _tagSet;
        private readonly TagRadial _radial;
        private readonly HashSet<string> _filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly NameStore _names;
        private readonly KeyList _marked;
        private readonly FolderList _folders;

        // Folders: the new / rename box, and dragging an item onto a folder on the left.
        private bool _folderBox;
        private string _folderOld, _folderText, _folderAddKey;
        private bool _folderFocus;
        private string _pressKey, _dragKey;
        private bool _favOpen = true;
        private Vector2 _pressAt;
        private readonly Dictionary<string, Rect> _folderRows = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

        // Rename box and deletion report (small windows over the browser).
        private string _renameKey, _renameText;
        private bool _renameFocus;
        private bool _reportOpen;
        private Vector2 _reportScroll;
        private List<ReportRow> _report;
        private int DialogId => WindowId + 5;

        // Bars that wrap onto more rows when the window is narrow.
        private readonly Flow _topFlow = new Flow(), _filterFlow = new Flow(), _footFlow = new Flow();
        private readonly TagPanel _details = new TagPanel();
        private float BarWidth => _rect.width - 20;
        private readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Action> _pending = new List<Action>();

        private List<ItemGroup> _catalog;
        private readonly Dictionary<string, CatalogItem> _byKey = new Dictionary<string, CatalogItem>(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemCategory> _categories = new Dictionary<string, ItemCategory>(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemGroup> _groups = new Dictionary<string, ItemGroup>(StringComparer.Ordinal);
        private int _total;

        private Rect _rect;
        private bool _rectLoaded;
        private bool _visible;
        private bool _resizing;
        private Vector2 _resizeStart, _resizeOrig;

        private Scope _scope = Scope.Category;
        private string _scopeKey;          // group or category key
        private string _search = "";
        private string _selected;          // item key
        private Vector2 _treeScroll, _listScroll;
        private Rect _treeViewScreen;      // the group panel's visible area, in screen space (rows outside it ignore the mouse)
        private float _listViewH = 300f;
        private bool _scrollToSelected;

        private List<CatalogItem> _view = new List<CatalogItem>();
        private string _viewKey;
        private int _favInView;

        private string _status;
        private bool _statusError;
        private float _statusUntil;

        private GUIStyle _muted, _mutedRight;

        public ItemWindow(StudioBrowserPlugin plugin, ItemKind kind)
        {
            _p = plugin;
            _kind = kind;
            var id = 0x0B570000;
            foreach (var c in kind.Id) id = id * 31 + c;
            WindowId = id & 0x7FFFFFF0;
            _favs = new KeyList(plugin.FavoritesFile);
            _recent = new KeyList(plugin.RecentFile, 40);
            _tags = new TagStore(null, plugin.TagsFile);
            _tagSet = plugin.CreateTagSet();
            _radial = new TagRadial(WindowId + 3);
            _names = new NameStore(plugin.NamesFile);
            _marked = new KeyList(plugin.MarkedFile);
            _folders = new FolderList(plugin.FoldersFile);
            foreach (var k in (_p.OpenFolders.Value ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)) _expanded.Add(k);
        }

        public bool Visible => _visible;

        private static float GuiWidth => Screen.width / Mathf.Max(0.01f, GUI.matrix.m00);
        private static float GuiHeight => Screen.height / Mathf.Max(0.01f, GUI.matrix.m11);

        // ================================================================ open / close

        public void Toggle(List<OCIChar> targets)
        {
            if (_visible)
            {
                Hide();
                _p.ClosedByUser();
            }
            else Show(-1);
        }

        public void Show(int sex)
        {
            if (!_rectLoaded) LoadRect();
            if (_catalog == null) Rebuild();
            _visible = true;
            // Open on what Studio is using now (the loaded map), in its own group if the current view doesn't show it.
            var current = _kind.CurrentKey;
            if (current != null && _byKey.TryGetValue(current, out var item))
            {
                if (!View().Any(i => i.Key == current)) Open(Scope.Category, item.Category.Key);
                _selected = current;
            }
            _scrollToSelected = true;
        }

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            InputGuard.Set(this, false);
            _radial.Close();
            _renameKey = null;
            _reportOpen = false;
            _folderBox = false;
            _dragKey = _pressKey = null;
            _kind.OnHidden();
            SaveRect();
        }

        public void FollowSex(int sex) { }

        public void Dispose() => SaveRect();

        private void Rebuild()
        {
            List<ItemGroup> catalog;
            try { catalog = _kind.BuildCatalog() ?? new List<ItemGroup>(); }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Couldn't read the {_kind.Nouns}: {ex}");
                catalog = new List<ItemGroup>();
            }

            _byKey.Clear();
            _categories.Clear();
            _groups.Clear();
            _total = 0;
            foreach (var g in catalog)
            {
                g.Categories.RemoveAll(c => c.Items.Count == 0);
                foreach (var c in g.Categories)
                {
                    c.Group = g;
                    _categories[c.Key] = c;
                    foreach (var i in c.Items)
                    {
                        i.Category = c;
                        if (!_byKey.ContainsKey(i.Key)) _byKey[i.Key] = i;
                        _total++;
                    }
                }
            }
            catalog.RemoveAll(g => g.Categories.Count == 0);
            foreach (var g in catalog) _groups[g.Key] = g;
            _catalog = catalog;
            _viewKey = null;
            if (_folders.Names.Count > 0) StarFolderMembers(_byKey.Keys.ToList());
            if (_p.NamesInStudio.Value)
                foreach (var kv in _names.All)
                    if (_byKey.TryGetValue(kv.Key, out var named)) TryShowName(named, kv.Value);

            // Keep the open scope if it still exists, else start on Favorites (or the first category).
            var valid = _scope == Scope.Favorites || _scope == Scope.Recent || _scope == Scope.Marked
                || (_scope == Scope.Folder && _folders.Contains(_scopeKey))
                || (_scope == Scope.Group && _scopeKey != null && _groups.ContainsKey(_scopeKey))
                || (_scope == Scope.Category && _scopeKey != null && _categories.ContainsKey(_scopeKey));
            if (!valid)
            {
                if (_favs.Count > 0) _scope = Scope.Favorites;
                else
                {
                    _scope = Scope.Category;
                    _scopeKey = _catalog.FirstOrDefault()?.Categories.FirstOrDefault()?.Key;
                }
            }
        }

        private void LoadRect()
        {
            _rectLoaded = true;
            var parts = (_p.WindowRect.Value ?? "").Split(',');
            var c = System.Globalization.CultureInfo.InvariantCulture;
            if (parts.Length == 4 && parts.All(x => float.TryParse(x, System.Globalization.NumberStyles.Float, c, out _)))
            {
                var v = parts.Select(x => float.Parse(x, c)).ToArray();
                _rect = new Rect(v[0], v[1], v[2], v[3]);
            }
            else _rect = new Rect(Screen.width * 0.40f, Screen.height * 0.12f, Mathf.Min(760f, Screen.width * 0.42f), Screen.height * 0.70f);
            ClampRect();
        }

        private void SaveRect()
        {
            if (!_rectLoaded) return;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            _p.WindowRect.Value = string.Join(",", new[] { _rect.x, _rect.y, _rect.width, _rect.height }.Select(f => Mathf.Round(f).ToString(c)).ToArray());
        }

        private void ClampRect()
        {
            _rect.width = Mathf.Clamp(_rect.width, 520f, Mathf.Max(520f, GuiWidth));
            _rect.height = Mathf.Clamp(_rect.height, 360f, Mathf.Max(360f, GuiHeight));
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, GuiWidth - _rect.width));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, GuiHeight - _rect.height));
        }

        private void SetExpanded(string groupKey, bool open)
        {
            if (open) _expanded.Add(groupKey);
            else _expanded.Remove(groupKey);
            _p.OpenFolders.Value = string.Join("|", _expanded.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }

        // ================================================================ per frame

        public void Update()
        {
            if (!_visible)
            {
                _pending.Clear();
                InputGuard.Set(this, false);
                return;
            }
            if (Grid) _kind.TickPictures();
            if (_pending.Count == 0) return;
            var run = _pending.ToArray();
            _pending.Clear();
            foreach (var a in run)
            {
                try { a(); }
                catch (Exception ex) { Log.Source.LogWarning("Browser action failed: " + ex); }
            }
        }

        private void Later(Action a) => _pending.Add(a);

        private void SetStatus(string text, bool error = false)
        {
            _status = text;
            _statusError = error;
            _statusUntil = Time.unscaledTime + (error ? 10f : 5f);
        }

        // ================================================================ the list being shown

        private bool Searching => !string.IsNullOrEmpty(_search) && _search.Trim().Length > 0;

        /// <summary>True when the view mixes categories, so each row says where it's from.</summary>
        private bool ShowsPath => Searching || _filter.Count > 0 || _scope != Scope.Category;

        /// <summary>Pictures in a grid (kinds with pictures, when the grid is on).</summary>
        private bool Grid => _kind.HasPictures && _p.GridView != null && _p.GridView.Value;
        private int _gridCols = 1;

        private List<CatalogItem> View()
        {
            // Recently used keeps its order while it's on screen (so stepping through it doesn't reshuffle it); it catches up when reopened.
            var recentVersion = _scope == Scope.Recent && !Searching ? 0 : _recent.Version;
            var key = $"{_scope}|{_scopeKey}|{_search}|{_p.ItemSortBy.Value}|{_favs.Version}|{recentVersion}|{_catalog?.Count}|{_tags.Version}|{string.Join("|", _filter.ToArray())}|{(_filter.Count > 0 || Searching ? _kind.AutoVersion : 0)}|{_names.Version}|{_marked.Version}|{_folders.Version}|{_p.HideMarked.Value}";
            if (key == _viewKey) return _view;
            _viewKey = key;

            IEnumerable<CatalogItem> q;
            var recentOrder = false;
            if (_catalog == null) q = Enumerable.Empty<CatalogItem>();
            else if (Searching || _filter.Count > 0)
            {
                // Search and the tag filter look through everything.
                var words = (_search ?? "").Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                q = _catalog.SelectMany(g => g.Categories).SelectMany(c => c.Items)
                    .Where(i =>
                    {
                        var tags = AllTags(i);
                        if (!TagFilter.Matches(tags, _filter)) return false;
                        if (words.Length == 0) return true;
                        var hay = (i.Name + " " + _names.Get(i.Key) + " " + i.Source + " " + i.Category.Name + " " + i.Category.Group.Name + " " + string.Join(" ", tags.Select(TagFilter.ValueOf).ToArray())).ToLowerInvariant();
                        return words.All(w => hay.Contains(w));
                    });
            }
            else switch (_scope)
            {
                case Scope.Favorites:
                    q = _favs.Items.Select(k => _byKey.TryGetValue(k, out var i) ? i : null).Where(i => i != null);
                    break;
                case Scope.Recent:
                    q = _recent.Items.Select(k => _byKey.TryGetValue(k, out var i) ? i : null).Where(i => i != null);
                    recentOrder = true;
                    break;
                case Scope.Marked:
                    q = _marked.Items.Select(k => _byKey.TryGetValue(k, out var i) ? i : null).Where(i => i != null);
                    break;
                case Scope.Folder:
                    var folderTag = FolderList.Tag(_scopeKey ?? "");
                    q = _byKey.Values.Where(i => _tags.Has(i.Key, folderTag));
                    break;
                case Scope.Group:
                    q = _groups.TryGetValue(_scopeKey ?? "", out var g) ? g.Categories.SelectMany(c => c.Items) : Enumerable.Empty<CatalogItem>();
                    break;
                default:
                    q = _categories.TryGetValue(_scopeKey ?? "", out var cat) ? cat.Items : Enumerable.Empty<CatalogItem>();
                    break;
            }

            // Marked items live under "Marked for deletion" only (unless shown everywhere).
            if (_scope != Scope.Marked && _p.HideMarked.Value && _marked.Count > 0) q = q.Where(i => !_marked.Contains(i.Key));
            var list = q.ToList();
            if (!recentOrder)
            {
                // Favorites first (except in Favorites itself, where everything is one), then the chosen order.
                var byName = _p.ItemSortBy.Value == ItemSort.Name;
                var index = new Dictionary<CatalogItem, int>();
                for (var n = 0; n < list.Count; n++) index[list[n]] = n;
                IOrderedEnumerable<CatalogItem> o = list.OrderBy(i => _scope != Scope.Favorites && _favs.Contains(i.Key) ? 0 : 1);
                o = byName ? o.ThenBy(i => DisplayName(i), NaturalComparer.Instance) : o.ThenBy(i => index[i]);
                list = o.ToList();
            }
            _view = list;
            _favInView = _scope == Scope.Favorites || recentOrder ? 0 : _view.TakeWhile(i => _favs.Contains(i.Key)).Count();
            return _view;
        }

        private string ScopeLabel()
        {
            if (Searching) return $"Search: \"{_search.Trim()}\"";
            if (_filter.Count > 0) return "Tagged: " + TagFilter.Describe(_filter);
            switch (_scope)
            {
                case Scope.Favorites: return "Favorites";
                case Scope.Recent: return "Recently used";
                case Scope.Marked: return "Marked for deletion";
                case Scope.Folder: return _scopeKey ?? "";
                case Scope.Group: return _groups.TryGetValue(_scopeKey ?? "", out var g) ? g.Name : "";
                default: return _categories.TryGetValue(_scopeKey ?? "", out var c) ? c.Group.Name + "  ›  " + c.Name : "";
            }
        }

        private void Open(Scope scope, string key)
        {
            _scope = scope;
            _scopeKey = key;
            _search = "";
            _viewKey = null;
            _listScroll = Vector2.zero;
            GUIUtility.keyboardControl = 0;
        }

        // ================================================================ actions

        private CardAction PrimaryAction => _kind.Actions.FirstOrDefault(a => a.Primary) ?? _kind.Actions.FirstOrDefault();

        private void Run(CardAction action, string key)
        {
            if (action?.Run == null || key == null || !_byKey.ContainsKey(key)) return;
            if (action.CanRun != null && !action.CanRun(-1)) return;
            var result = action.Run(key, -1);
            if (!string.IsNullOrEmpty(result.Message)) SetStatus(result.Message, result.Error);
            if (!result.Error && action == PrimaryAction) _recent.Touch(key);
        }

        private void Select(string key, bool play)
        {
            _selected = key;
            _scrollToSelected = true;
            if (play) Later(() => Run(PrimaryAction, key));
        }

        private void StepRow(int dir) => Step(Grid ? dir * Mathf.Max(1, _gridCols) : dir);

        private void Step(int delta)
        {
            var view = View();
            if (view.Count == 0) return;
            var at = _selected == null ? -1 : view.FindIndex(i => i.Key == _selected);
            var next = at < 0 ? (delta > 0 ? 0 : view.Count - 1) : Mathf.Clamp(at + delta, 0, view.Count - 1);
            if (next == at) return;
            Select(view[next].Key, _p.PlayOnClick.Value && _kind.RunOnStep);
        }

        // ================================================================ drawing

        public void Draw()
        {
            if (!_visible) return;
            var e = Event.current;
            if (e.type == EventType.Repaint) InputGuard.Set(this, _rect.Contains(e.mousePosition));

            var skin = GUI.skin;
            GUI.skin = Theme.Skin;
            try
            {
                if (_muted == null)
                {
                    _muted = new GUIStyle(Styles.Small) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, richText = true };
                    _mutedRight = new GUIStyle(_muted) { alignment = TextAnchor.MiddleRight };
                }
                ClampRect();
                _rect = GUI.Window(WindowId, _rect, DrawWindow, _kind.Title + "  ·  " + ScopeLabel());
                _radial.Draw(_tagSet, _tags, _filter, () => _viewKey = null, _kind.AutoFacets, CountOf);
                DrawDialogs();
            }
            finally
            {
                GUI.skin = skin;
            }
        }

        private void DrawWindow(int id)
        {
            var e = Event.current;

            if (GUI.Button(new Rect(_rect.width - 30, 7, 22, 20), "x"))
                Later(() =>
                {
                    Hide();
                    _p.ClosedByUser();
                });

            HandleKeys(e);
            HandleDrag(e);

            GUILayout.BeginArea(new Rect(10, 32, _rect.width - 20, _rect.height - 42));
            DrawTopBar();
            DrawFilterBar();
            GUILayout.Space(4);
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawTree();
            GUILayout.Space(6);
            DrawList();
            GUILayout.Space(6);
            DrawDetails();
            GUILayout.EndHorizontal();
            if (_kind.HasControls)
            {
                GUILayout.Space(4);
                _kind.DrawControls(BarWidth);
            }
            GUILayout.Space(4);
            DrawFooter();
            GUILayout.EndArea();

            HandleResize(e);
            GUI.DragWindow(new Rect(0, 0, _rect.width - 34, 28));
            IMGUIUtils.EatInputInRect(_rect);
        }

        private void DrawTopBar()
        {
            var f = _topFlow;
            var skin = Theme.Skin;
            f.Begin(BarWidth);
            f.Next(Flow.Size("Search", Styles.Small) + Flow.Fixed(200, skin.textField)
                + (_search?.Length > 0 ? Flow.Fixed(22) : 0) + (Searching ? Flow.Size(Theme.Hint($"in all {_total} {_kind.Nouns}"), Theme.RichSingle) : 0));
            GUILayout.Label("Search", Styles.Small, GUILayout.ExpandWidth(false));

            // Down or Enter in the search box goes to the list.
            var e = Event.current;
            if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "ib_search" &&
                (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                GUIUtility.keyboardControl = 0;
                var v = View();
                if (v.Count > 0) Select(v[0].Key, e.keyCode != KeyCode.DownArrow || (_p.PlayOnClick.Value && _kind.RunOnStep));
                e.Use();
            }

            GUI.SetNextControlName("ib_search");
            var s = GUILayout.TextField(_search ?? "", GUILayout.Width(200));
            if (s != _search)
            {
                _search = s;
                _listScroll = Vector2.zero;
            }
            if (_search.Length > 0 && GUILayout.Button("x", GUILayout.Width(22))) _search = "";
            if (Searching) GUILayout.Label(Theme.Hint($"in all {_total} {_kind.Nouns}"), Theme.RichSingle, GUILayout.ExpandWidth(false));

            f.Spring();
            if (_kind.HasPictures && _p.GridView != null)
            {
                f.Next(Flow.Size("Grid", skin.toggle) + (_p.GridView.Value ? Flow.Fixed(70, skin.horizontalSlider) : 0) + 8, true);
                var grid = GUILayout.Toggle(_p.GridView.Value, new GUIContent("Grid", $"Show the {_kind.Nouns} as pictures (off: a list of names)"), GUILayout.ExpandWidth(false));
                if (grid != _p.GridView.Value)
                {
                    _p.GridView.Value = grid;
                    _scrollToSelected = true;
                }
                if (grid)
                {
                    var size = Mathf.RoundToInt(GUILayout.HorizontalSlider(_p.ItemThumbSize.Value, 56, 200, GUILayout.Width(70)));
                    if (size != _p.ItemThumbSize.Value) _p.ItemThumbSize.Value = size;
                }
                GUILayout.Space(8);
            }
            var tagLabel = _filter.Count > 0 ? $"Tags ({_filter.Count})" : "Tags";
            f.Next(Flow.Fixed(_filter.Count > 0 ? 68 : 48) + 8, true);
            if (GUILayout.Button(new GUIContent(tagLabel, $"Show only {_kind.Nouns} with certain tags (opens the tag wheel)"), _filter.Count > 0 ? Theme.Primary : Theme.Skin.button, GUILayout.Width(_filter.Count > 0 ? 68 : 48)))
            {
                var at = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                Later(() => _radial.ShowFilter(at));
            }
            GUILayout.Space(8);
            f.Next(Flow.Size("Sort", Styles.Small) + Flow.Fixed(58, Theme.Choice) + Flow.Fixed(50, Theme.Choice) + 8, true);
            GUILayout.Label("Sort", Styles.Small, GUILayout.ExpandWidth(false));
            if (GUILayout.Toggle(_p.ItemSortBy.Value == ItemSort.StudioOrder, new GUIContent("Studio", "In the order Studio lists them"), Theme.Choice, GUILayout.Width(58)))
                _p.ItemSortBy.Value = ItemSort.StudioOrder;
            if (GUILayout.Toggle(_p.ItemSortBy.Value == ItemSort.Name, new GUIContent("Name", "By name"), Theme.Choice, GUILayout.Width(50)))
                _p.ItemSortBy.Value = ItemSort.Name;

            GUILayout.Space(8);
            var verb = _kind.Verb.ToLowerInvariant();
            f.Next(Flow.Size($"{_kind.Verb} on click", skin.toggle) + 6, true);
            var play = GUILayout.Toggle(_p.PlayOnClick.Value, new GUIContent($"{_kind.Verb} on click",
                _kind.RunOnStep ? $"Clicking a {_kind.Noun} or stepping with the arrow keys {verb}s it. Off: double-click or Enter {verb}s."
                    : $"Clicking a {_kind.Noun} {verb}s it. Off: double-click or Enter {verb}s. The arrow keys only move through the list."), GUILayout.ExpandWidth(false));
            if (play != _p.PlayOnClick.Value) _p.PlayOnClick.Value = play;

            GUILayout.Space(6);
            f.Next(Flow.Fixed(64), true);
            if (GUILayout.Button(new GUIContent("Refresh", $"Read the {_kind.Nouns} again"), GUILayout.Width(64)))
                Later(Rebuild);
            f.End();
        }

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
                    Later(() => _filter.Remove(t));
            }
            f.Next(Flow.Fixed(50));
            if (GUILayout.Button(new GUIContent("Clear", "Show everything again"), GUILayout.Width(50))) Later(() => _filter.Clear());
            var hint = Theme.Hint($"in all {_total} {_kind.Nouns}");
            f.Next(Flow.Size(hint, Theme.RichSingle));
            GUILayout.Label(hint, Theme.RichSingle, GUILayout.ExpandWidth(true));
            f.End();
        }

        /// <summary>The item's own tags plus the ones the kind works out (length...).</summary>
        private HashSet<string> AllTags(CatalogItem i)
        {
            var own = _tags.Get(i.Key);
            if (_kind.AutoFacets == null) return own;
            var all = new HashSet<string>(own, StringComparer.OrdinalIgnoreCase);
            foreach (var t in _kind.AutoTags(i)) all.Add(t);
            return all;
        }

        private Dictionary<string, int> _autoCounts;
        private int _autoCountsFrame = -1;

        /// <summary>How many items carry a tag (own tags from the store; automatic ones counted over the catalog, once a second).</summary>
        private int CountOf(string tag)
        {
            if (_kind.AutoFacets == null || !_kind.AutoFacets.Any(f => tag.StartsWith(f.Name + ":", StringComparison.OrdinalIgnoreCase))) return _tags.Count(tag);
            if (_autoCounts == null || Time.frameCount - _autoCountsFrame > 60)
            {
                _autoCountsFrame = Time.frameCount;
                _autoCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var i in _byKey.Values)
                foreach (var t in _kind.AutoTags(i))
                    _autoCounts[t] = _autoCounts.TryGetValue(t, out var n) ? n + 1 : 1;
            }
            return _autoCounts.TryGetValue(tag, out var c) ? c : 0;
        }

        private void OpenTagWheel(Vector2 screen, string key)
        {
            if (key == null)
            {
                SetStatus($"Select a {_kind.Noun} to tag first.");
                return;
            }
            _radial.ShowTag(screen, new List<string> { key });
        }

        // ---------------------------------------------------------------- details panel (right)

        private void DrawDetails()
        {
            var w = TagPanel.WidthFor(_rect.width);
            var tree = _p.ShowFolders.Value ? TreeWidth : 24;
            // Too narrow for it next to the list: fold it (without changing the setting).
            if (!_p.ShowDetails.Value || _rect.width - 20 - tree - w - 12 < 220)
            {
                TagPanel.DrawFolded(() => _p.ShowDetails.Value = true);
                return;
            }
            TagPanel.Selection sel = null;
            if (_selected != null && _byKey.TryGetValue(_selected, out var item))
            {
                var renamed = _names.Get(item.Key) != null;
                sel = new TagPanel.Selection
                {
                    Targets = new List<string> { item.Key },
                    Title = (_marked.Contains(item.Key) ? Theme.Error("Marked for deletion") + "\n" : "") + DisplayName(item),
                    Subtitle = Theme.Hint((renamed ? $"originally {item.Name}\n" : "") + $"{item.Category.Group.Name} › {item.Category.Name}" + (string.IsNullOrEmpty(item.Source) ? "" : "\n" + item.Source)),
                    Picture = _kind.HasPictures ? _kind.Picture(item) : null,
                    Auto = _kind.AutoFacets != null ? _kind.AutoTags(item) : null,
                    Noun = _kind.Noun,
                };
            }
            else sel = new TagPanel.Selection { Noun = _kind.Noun };
            var selKey = _selected;
            var targets = sel.Targets;
            _details.Draw(w, _tagSet, _tags, sel, () =>
                {
                    if (targets != null) StarFolderMembers(targets);
                    _viewKey = null;
                }, () => _p.ShowDetails.Value = false,
                _folders.Facet, selKey != null ? () => Later(() => OpenFolderBox(null, selKey)) : (Action)null);
        }

        // ---------------------------------------------------------------- left panel

        private void DrawTree()
        {
            if (!_p.ShowFolders.Value)
            {
                GUILayout.BeginVertical(GUILayout.Width(24), GUILayout.ExpandHeight(true));
                if (GUILayout.Button(new GUIContent(Styles.PanelRight, "Show groups"), Styles.TreeArrow, GUILayout.Width(22), GUILayout.Height(22)))
                    _p.ShowFolders.Value = true;
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginVertical(GUILayout.Width(TreeWidth), GUILayout.ExpandHeight(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(_kind.GroupsLabel, Styles.SectionHeader, GUILayout.Height(20));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent(Styles.PanelLeft, $"Hide groups (more room for {_kind.Nouns})"), Styles.TreeArrow, GUILayout.Width(22), GUILayout.Height(20)))
                _p.ShowFolders.Value = false;
            GUILayout.EndHorizontal();

            var e0 = Event.current;
            _treeScroll = GUILayout.BeginScrollView(_treeScroll, false, false, GUIStyle.none, Theme.Skin.verticalScrollbar, Theme.Skin.box, GUILayout.ExpandHeight(true));

            // Favorites, with your folders inside it.
            if (e0.type == EventType.Repaint) _folderRows.Clear();
            var favRow = TreeRow(_folders.Names.Count > 0 ? _favOpen : (bool?)null,
                $"<color=#F5C451>★</color> Favorites  <color={Theme.MutedHex}><size=10>{_favs.Count}</size></color>", 0, !Searching && _scope == Scope.Favorites,
                () => Open(Scope.Favorites, null), () => _favOpen = !_favOpen, null, _dragKey != null);
            if (e0.type == EventType.Repaint) _folderRows[""] = new Rect(GUIUtility.GUIToScreenPoint(favRow.position), favRow.size);
            if (_favOpen || _folders.Names.Count == 0)
            {
                foreach (var f in _folders.Names)
                {
                    var name = f;
                    var tag = FolderList.Tag(name);
                    var count = _byKey.Keys.Count(k => _tags.Has(k, tag));
                    var row = TreeRow(null, $"{name}  <color={Theme.MutedHex}><size=10>{count}</size></color>", 1,
                        !Searching && _scope == Scope.Folder && string.Equals(_scopeKey, name, StringComparison.OrdinalIgnoreCase),
                        () => Open(Scope.Folder, name), null, () => OpenFolderBox(name, null), _dragKey != null);
                    if (e0.type == EventType.Repaint) _folderRows[name] = new Rect(GUIUtility.GUIToScreenPoint(row.position), row.size);
                }
                TreeRow(null, Theme.Hint("+ new folder"), 1, false, () => OpenFolderBox(null, null), null);
            }
            TreeRow(null, $"Recently used  <color={Theme.MutedHex}><size=10>{_recent.Items.Count(k => _byKey.ContainsKey(k))}</size></color>", 0,
                !Searching && _scope == Scope.Recent, () => Open(Scope.Recent, null), null);
            var marked = _marked.Items.Count(k => _byKey.ContainsKey(k));
            if (marked > 0 || _scope == Scope.Marked)
                TreeRow(null, $"<color={Theme.ErrorHex}>Marked for deletion</color>  <color={Theme.MutedHex}><size=10>{marked}</size></color>", 0,
                    !Searching && _scope == Scope.Marked, () => Open(Scope.Marked, null), null);
            GUILayout.Space(4);

            if (_catalog != null)
            {
                foreach (var g in _catalog)
                {
                    var open = _expanded.Contains(g.Key);
                    var count = g.Categories.Sum(c => c.Items.Count);
                    var gk = g.Key;
                    TreeRow(open, $"{g.Name}  <color={Theme.MutedHex}><size=10>{count}</size></color>", 0,
                        !Searching && _scope == Scope.Group && _scopeKey == g.Key,
                        () =>
                        {
                            if (!_expanded.Contains(gk)) SetExpanded(gk, true);
                            Open(Scope.Group, gk);
                        },
                        () => SetExpanded(gk, !_expanded.Contains(gk)));
                    if (!open) continue;
                    foreach (var c in g.Categories)
                    {
                        var ck = c.Key;
                        TreeRow(null, $"{c.Name}  <color={Theme.MutedHex}><size=10>{c.Items.Count}</size></color>", 1,
                            !Searching && _scope == Scope.Category && _scopeKey == c.Key, () => Open(Scope.Category, ck), null);
                    }
                }
                if (_catalog.Count == 0) GUILayout.Label($"No {_kind.Nouns} found.", Styles.Small);
            }
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint)
            {
                var r = GUILayoutUtility.GetLastRect();
                var p = GUIUtility.GUIToScreenPoint(r.position);
                _treeViewScreen = new Rect(p.x, p.y, r.width, r.height);
            }
            GUILayout.EndVertical();
        }

        /// <param name="open">null: no chevron; true / false: an open / closed branch.</param>
        private Rect TreeRow(bool? open, string text, int depth, bool current, Action click, Action toggle, Action rightClick = null, bool dropTarget = false)
        {
            var e = Event.current;
            GUILayout.BeginHorizontal();
            GUILayout.Space(depth * 14f);
            var arrow = GUILayoutUtility.GetRect(16, 20, GUILayout.Width(16), GUILayout.Height(20));
            var content = new GUIContent(text);
            var row = GUILayoutUtility.GetRect(content, Styles.TreeRow, GUILayout.ExpandWidth(true), GUILayout.Height(20));
            GUILayout.EndHorizontal();

            var inView = _treeViewScreen.Contains(GUIUtility.GUIToScreenPoint(e.mousePosition));
            if (open.HasValue)
            {
                if (e.type == EventType.Repaint)
                {
                    var hi = inView && arrow.Contains(e.mousePosition);
                    var icon = open.Value ? (hi ? Styles.ChevronDownHi : Styles.ChevronDown) : (hi ? Styles.ChevronRightHi : Styles.ChevronRight);
                    GUI.DrawTexture(new Rect(arrow.x, arrow.y + 2, 16, 16), icon, ScaleMode.ScaleToFit, true);
                }
                if (inView && e.type == EventType.MouseDown && e.button == 0 && arrow.Contains(e.mousePosition))
                {
                    Later(toggle);
                    e.Use();
                }
            }

            if (inView && e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                if (e.clickCount == 2 && toggle != null) Later(toggle);
                else Later(click);
                e.Use();
            }
            if (inView && rightClick != null && e.type == EventType.MouseDown && e.button == 1 && row.Contains(e.mousePosition))
            {
                Later(rightClick);
                e.Use();
            }

            if (e.type == EventType.Repaint)
            {
                var over = inView && row.Contains(e.mousePosition);
                (current || (dropTarget && over) ? Styles.TreeRowOn : Styles.TreeRow).Draw(row, content, over, false, false, false);
            }
            return row;
        }

        // ---------------------------------------------------------------- list

        private void DrawList()
        {
            if (Grid)
            {
                DrawGrid();
                return;
            }
            var view = View();
            var e = Event.current;
            var area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.Box(area, GUIContent.none);
            if (e.type == EventType.Repaint) _listViewH = area.height;

            if (view.Count == 0)
            {
                string msg;
                if (Searching) msg = $"No {_kind.Nouns} match \"{_search.Trim()}\".";
                else if (_filter.Count > 0) msg = $"No {_kind.Nouns} have those tags. Right-click a {_kind.Noun} (or press T) to tag it.";
                else if (_scope == Scope.Favorites) msg = $"No favorites yet. Click the star next to a {_kind.Noun} (or press F) to add it here.";
                else if (_scope == Scope.Recent) msg = $"{_kind.Nouns.Substring(0, 1).ToUpper() + _kind.Nouns.Substring(1)} you {_kind.Verb.ToLowerInvariant()} show up here.";
                else msg = $"Pick a group or category on the left.";
                Styles.Placeholder.Draw(new Rect(area.x + 8, area.y + 8, area.width - 16, 60), msg, false, false, false, false);
                return;
            }

            // Section headers in front of the favorites and the rest when both are present.
            var headers = _favInView > 0 && _favInView < view.Count;
            var headerH = headers ? 22f : 0f;
            var contentH = view.Count * RowH + headerH * 2 + 4;
            var inner = new Rect(0, 0, area.width - 18, contentH);

            if (_scrollToSelected && _selected != null)
            {
                _scrollToSelected = false;
                var at = view.FindIndex(i => i.Key == _selected);
                if (at >= 0)
                {
                    var y = RowY(at, headers, headerH);
                    if (y < _listScroll.y) _listScroll.y = y - 4;
                    else if (y + RowH > _listScroll.y + _listViewH) _listScroll.y = y + RowH - _listViewH + 4;
                }
            }

            _listScroll = GUI.BeginScrollView(area, _listScroll, inner, false, true);
            var first = Mathf.Max(0, Mathf.FloorToInt((_listScroll.y - headerH * 2) / RowH) - 1);
            var last = Mathf.Min(view.Count - 1, Mathf.CeilToInt((_listScroll.y + area.height) / RowH) + 1);

            if (headers)
            {
                Styles.SectionHeader.Draw(new Rect(6, 2, inner.width - 12, headerH - 4), $"Favorites  <color={Theme.MutedHex}>{_favInView}</color>", false, false, false, false);
                var hy = RowY(_favInView, true, headerH) - headerH;
                GUI.DrawTexture(new Rect(6, hy + 2, inner.width - 12, 1), Styles.White);
                Styles.SectionHeader.Draw(new Rect(6, hy + 2, inner.width - 12, headerH - 4), $"Other {_kind.Nouns}  <color={Theme.MutedHex}>{view.Count - _favInView}</color>", false, false, false, false);
            }

            // Only the part on screen takes the mouse (rows just outside it would otherwise catch clicks on the footer).
            var visible = new Rect(_listScroll.x, _listScroll.y, area.width - 16, area.height);
            for (var n = first; n <= last; n++)
            {
                var item = view[n];
                var row = new Rect(2, RowY(n, headers, headerH), inner.width - 4, RowH - 2);
                DrawRow(item, row, e, visible.Contains(e.mousePosition));
            }
            GUI.EndScrollView(true);
        }

        private float RowY(int index, bool headers, float headerH)
        {
            if (!headers) return 2 + index * RowH;
            return 2 + headerH + index * RowH + (index >= _favInView ? headerH : 0);
        }

        private void DrawRow(CatalogItem item, Rect row, Event e, bool mouseInView)
        {
            var hover = mouseInView && row.Contains(e.mousePosition);
            var selected = item.Key == _selected;
            var star = new Rect(row.x + 4, row.y + (row.height - StarSize) / 2, StarSize, StarSize);
            var fav = _favs.Contains(item.Key);

            if (e.type == EventType.Repaint)
            {
                if (selected) Styles.TreeRowOn.Draw(row, GUIContent.none, hover, false, false, false);
                else if (hover) Styles.CellHover.Draw(row, GUIContent.none, false, false, false, false);

                var icon = fav ? Styles.StarOn : hover && star.Contains(e.mousePosition) ? Styles.StarHover : hover || selected ? Styles.StarOff : null;
                if (icon != null) GUI.DrawTexture(star, icon, ScaleMode.ScaleToFit, true);

                var badge = _kind.Badge(item);
                var right = (badge != null ? badge + "   " : "") + (item.Source ?? "");
                var rightW = Mathf.Min(row.width * 0.38f, _mutedRight.CalcSize(new GUIContent(right)).x + 8);
                var nameRect = new Rect(star.xMax + 6, row.y, row.width - (star.xMax + 6 - row.x) - rightW - 6, row.height);
                var name = NameMarkup(item);
                var tags = _tags.Get(item.Key);
                if (tags.Count > 0)
                {
                    var words = new List<string>();
                    foreach (var t in tags.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        var v = TagFilter.ValueOf(t);
                        var col = TagRadial.ColorOf(v);
                        words.Add(col.HasValue ? $"<color=#{ColorUtility.ToHtmlStringRGB(col.Value)}>●</color>{v}" : v);
                    }
                    name += $"   <color={Theme.NeonDimHex}><size=10>{string.Join("  ", words.ToArray())}</size></color>";
                }
                if (ShowsPath) name += $"   <color={Theme.MutedHex}><size=10>{item.Category.Group.Name} › {item.Category.Name}</size></color>";
                var st = selected ? Styles.TreeRowOn : Styles.TreeRow;
                st.Draw(nameRect, new GUIContent(name), false, false, false, false);
                if (right.Length > 0) _mutedRight.Draw(new Rect(row.xMax - rightW - 4, row.y, rightW, row.height), right, false, false, false, false);
            }

            ItemMouse(item, star, hover, e);
        }

        /// <summary>Clicks on a row or a grid cell: star, select / run, double-click, right-click for tags.</summary>
        private void ItemMouse(CatalogItem item, Rect star, bool hover, Event e)
        {
            if (e.type == EventType.MouseDown && hover)
            {
                GUIUtility.keyboardControl = 0;
                if (e.button == 0 && star.Contains(e.mousePosition))
                {
                    var key = item.Key;
                    Later(() => _favs.Set(key, !_favs.Contains(key)));
                }
                else if (e.button == 0)
                {
                    var key = item.Key;
                    _pressKey = key;
                    _pressAt = GUIUtility.GUIToScreenPoint(e.mousePosition);
                    // With run-on-click the first click already ran it; a double-click mustn't run it twice (two items added).
                    if (e.clickCount == 2) { if (!_p.PlayOnClick.Value) Later(() => Run(PrimaryAction, key)); }
                    else Select(key, _p.PlayOnClick.Value);
                    _scrollToSelected = false;
                }
                else if (e.button == 1)
                {
                    var key = item.Key;
                    _selected = key;
                    var at = GUIUtility.GUIToScreenPoint(e.mousePosition);
                    Later(() => OpenTagWheel(at, key));
                }
                e.Use();
            }
        }

        // ---------------------------------------------------------------- grid

        private void DrawGrid()
        {
            var view = View();
            var e = Event.current;
            var area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.Box(area, GUIContent.none);
            if (e.type == EventType.Repaint) _listViewH = area.height;
            if (view.Count == 0)
            {
                Styles.Placeholder.Draw(new Rect(area.x + 8, area.y + 8, area.width - 16, 60),
                    Searching ? $"No {_kind.Nouns} match \"{_search.Trim()}\"." : _scope == Scope.Favorites ? $"No favorites yet. Click the star on a {_kind.Noun} (or press F)." : $"No {_kind.Nouns} here.", false, false, false, false);
                return;
            }

            const float gap = 6f;
            const float labelH = 30f;
            var size = (float)_p.ItemThumbSize.Value;
            var cellW = size * _kind.PictureAspect + 8f;
            var cellH = size + 8f + labelH;
            var innerW = area.width - 16f;
            var cols = Mathf.Max(1, (int)((innerW - gap) / (cellW + gap)));
            _gridCols = cols;
            var spare = innerW - gap - cols * (cellW + gap);
            var gapX = gap + (cols > 1 ? Mathf.Max(0, spare) / cols : 0);
            var rowH = cellH + gap;
            var rows = (view.Count + cols - 1) / cols;
            var inner = new Rect(0, 0, innerW, gap + rows * rowH);

            if (_scrollToSelected && _selected != null)
            {
                _scrollToSelected = false;
                var at = view.FindIndex(i => i.Key == _selected);
                if (at >= 0)
                {
                    var y = gap + (at / cols) * rowH;
                    if (y < _listScroll.y) _listScroll.y = y - gap;
                    else if (y + rowH > _listScroll.y + area.height) _listScroll.y = y + rowH - area.height + gap;
                }
            }

            _listScroll = GUI.BeginScrollView(area, _listScroll, inner, false, true);
            var visible = new Rect(_listScroll.x, _listScroll.y, area.width - 16, area.height);
            var mouseIn = visible.Contains(e.mousePosition);
            var firstRow = Mathf.Max(0, (int)((_listScroll.y - gap) / rowH));
            var lastRow = Mathf.Min(rows - 1, (int)((_listScroll.y + area.height) / rowH));
            for (var r = firstRow; r <= lastRow; r++)
            for (var c = 0; c < cols; c++)
            {
                var i = r * cols + c;
                if (i >= view.Count) break;
                DrawCell(view[i], new Rect(gap + c * (cellW + gapX), gap + r * rowH, cellW, cellH), size, e, mouseIn);
            }
            GUI.EndScrollView(true);
        }

        private void DrawCell(CatalogItem item, Rect cell, float size, Event e, bool mouseIn)
        {
            var hover = mouseIn && cell.Contains(e.mousePosition);
            var selected = item.Key == _selected;
            var pic = new Rect(cell.x + 4, cell.y + 4, size * _kind.PictureAspect, size);
            var star = new Rect(pic.x + 3, pic.y + 3, StarSize, StarSize);
            var fav = _favs.Contains(item.Key);
            var tex = _kind.Picture(item);   // asks for it while on screen

            if (e.type == EventType.Repaint)
            {
                (selected ? Styles.CellOn : hover ? Styles.CellHover : Styles.Cell).Draw(cell, GUIContent.none, false, false, false, false);
                if (tex != null) GUI.DrawTexture(pic, tex, ScaleMode.ScaleToFit, true);
                else Styles.Placeholder.Draw(pic, _kind.PictureMissing(item) ? "no picture" : "...", false, false, false, false);

                var icon = fav ? Styles.StarOn : hover && star.Contains(e.mousePosition) ? Styles.StarHover : hover ? Styles.StarOff : null;
                if (icon != null) GUI.DrawTexture(star, icon, ScaleMode.ScaleToFit, true);

                var tags = _tags.Get(item.Key);
                if (tags.Count > 0)
                {
                    var x = pic.x + 4;
                    foreach (var t in tags)
                    {
                        var col = TagRadial.ColorOf(TagFilter.ValueOf(t));
                        if (!col.HasValue || x > pic.xMax - 14) continue;
                        GUI.DrawTexture(new Rect(x, pic.yMax - 15, 11, 11), TagRadial.Dot(col.Value), ScaleMode.StretchToFill, true);
                        x += 13;
                    }
                }
                var name = NameMarkup(item);
                Styles.CardName.Draw(new Rect(cell.x + 3, pic.yMax + 2, cell.width - 6, cell.height - size - 10), name, false, false, false, false);
            }
            ItemMouse(item, star, hover, e);
        }

        // ---------------------------------------------------------------- names, marking

        private string DisplayName(CatalogItem i) => _names.Get(i.Key) ?? (string.IsNullOrEmpty(i.Name) ? "(no name)" : i.Name);

        /// <summary>The name for a row or cell: yours if you gave one, in red when marked for deletion.</summary>
        private string NameMarkup(CatalogItem i)
        {
            var name = DisplayName(i);
            return _marked.Contains(i.Key) ? $"<color={Theme.ErrorHex}>{name}</color>" : name;
        }

        private void TryShowName(CatalogItem item, string name)
        {
            try { _kind.ShowNameInStudio(item, name); }
            catch (Exception ex) { Log.Source.LogDebug("Couldn't rename in Studio's list: " + ex.Message); }
        }

        private void ToggleMarked(string key)
        {
            if (!_byKey.TryGetValue(key, out var item)) return;
            var at = View().FindIndex(i => i.Key == key);
            var on = !_marked.Contains(key);
            _marked.Set(key, on);
            SetStatus(on ? $"Marked for deletion: {DisplayName(item)}  ·  find it under Marked for deletion (Del there unmarks it)" : $"Unmarked: {DisplayName(item)}");
            _viewKey = null;
            // When it leaves the list, select the one that took its place, so Del can go down a list.
            var view = View();
            if (at >= 0 && !view.Any(i => i.Key == key))
            {
                _selected = view.Count == 0 ? null : view[Mathf.Min(at, view.Count - 1)].Key;
                _scrollToSelected = true;
            }
        }

        private void OpenRename(string key)
        {
            if (!_byKey.TryGetValue(key, out var item)) return;
            _renameKey = key;
            _renameText = DisplayName(item);
            _renameFocus = true;
        }

        private void SaveRename(string name)
        {
            if (_renameKey == null || !_byKey.TryGetValue(_renameKey, out var item)) return;
            name = (name ?? "").Trim();
            var clear = name.Length == 0 || name == item.Name;
            _names.Set(_renameKey, clear ? null : name);
            if (_p.NamesInStudio.Value) TryShowName(item, clear ? null : name);
            SetStatus(clear ? $"Back to its own name: {item.Name}" : $"Renamed: {item.Name} → {name}");
            _renameKey = null;
            _viewKey = null;
        }

        private sealed class ReportRow
        {
            public string Guid, Title, File, Note;
            public int Marked, Total;
            public bool Removable;
            public Zipmods.Mod Mod;
        }

        /// <summary>Groups the marked items by zipmod: which mods are wholly marked and hold nothing else.</summary>
        private void OpenReport()
        {
            var all = _byKey.Values.ToList();
            var marked = _marked.Items.Select(k => _byKey.TryGetValue(k, out var i) ? i : null).Where(i => i != null).ToList();
            var rows = new List<ReportRow>();
            foreach (var g in marked.GroupBy(i => i.ModGuid ?? ""))
            {
                if (g.Key.Length == 0)
                {
                    rows.Add(new ReportRow { Title = "Base game", Marked = g.Count(), Total = all.Count(i => i.ModGuid == null), Note = "part of the game: can't be removed, only hidden here" });
                    continue;
                }
                var mod = Zipmods.Get(g.Key);
                var total = all.Count(i => i.ModGuid == g.Key);
                var row = new ReportRow { Guid = g.Key, Marked = g.Count(), Total = total, Mod = mod, Title = mod?.Name ?? g.First().Source ?? g.Key };
                if (mod == null) row.Note = "its file wasn't found in Sideloader's list";
                else
                {
                    row.File = mod.RelativeFile ?? mod.File;
                    var other = mod.OtherContent(_kind.OwnListPrefixes);
                    if (g.Count() < total) row.Note = $"only {g.Count()} of its {total} {_kind.Nouns} marked: the zipmod stays";
                    else if (other.Length > 0) row.Note = "all marked, but it also has " + other + ": the zipmod stays";
                    else
                    {
                        row.Removable = true;
                        row.Note = $"all {total} marked and nothing else in it: can be removed";
                    }
                }
                rows.Add(row);
            }
            _report = rows.OrderByDescending(r => r.Removable).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
            _reportScroll = Vector2.zero;
            _reportOpen = true;
        }

        private void MoveRemovable()
        {
            if (_report == null) return;
            var moved = 0;
            var failed = 0;
            foreach (var r in _report.Where(x => x.Removable && x.Mod != null && x.Note != "moved"))
            {
                try
                {
                    var to = Zipmods.MoveToDeleteFolder(r.Mod);
                    r.Note = "moved";
                    r.File = to;
                    moved++;
                }
                catch (Exception ex)
                {
                    r.Note = "couldn't move: " + ex.Message;
                    failed++;
                }
            }
            SetStatus(moved > 0
                ? $"Moved {moved} zipmod{(moved == 1 ? "" : "s")} to mods_to_delete. Restart the game for them to stop loading; delete that folder when you're sure."
                : failed > 0 ? "Nothing moved; see the report." : "Nothing to move.", moved == 0 && failed > 0);
        }

        // ---------------------------------------------------------------- folders

        /// <summary>Drag an item from the list or grid onto a folder on the left to put it there.</summary>
        private void HandleDrag(Event e)
        {
            if (e.rawType == EventType.MouseUp && e.button == 0)
            {
                if (_dragKey != null)
                {
                    var at = GUIUtility.GUIToScreenPoint(e.mousePosition);
                    var key = _dragKey;
                    var folder = _folderRows.FirstOrDefault(kv => kv.Value.Contains(at) && _treeViewScreen.Contains(at)).Key;
                    if (folder != null) Later(() => AddToFolder(key, folder));
                    e.Use();
                }
                _dragKey = _pressKey = null;
                return;
            }
            if (e.type == EventType.MouseDrag && e.button == 0 && _pressKey != null && _dragKey == null)
            {
                if ((GUIUtility.GUIToScreenPoint(e.mousePosition) - _pressAt).sqrMagnitude > 64f) _dragKey = _pressKey;
            }
            if (_dragKey != null && e.type == EventType.Repaint && _byKey.TryGetValue(_dragKey, out var item))
            {
                var label = new GUIContent(DisplayName(item) + Theme.Hint("  → drop on Favorites or one of its folders"));
                var size = Theme.RichSingle.CalcSize(label);
                var r = new Rect(e.mousePosition.x + 14, e.mousePosition.y + 6, size.x + 16, 22);
                GUI.Box(r, GUIContent.none);
                GUI.Label(new Rect(r.x + 8, r.y + 2, size.x, 18), label, Theme.RichSingle);
            }
            if (_dragKey != null && e.type == EventType.MouseDrag) e.Use();
        }

        /// <summary>Stars the item and, with a folder name, puts it in that folder too.</summary>
        private void AddToFolder(string key, string folder)
        {
            if (!_byKey.TryGetValue(key, out var item)) return;
            if (!_favs.Contains(key)) _favs.Set(key, true);
            if (!string.IsNullOrEmpty(folder)) _tags.Set(new[] { key }, FolderList.Tag(folder), true, _folders.Facet);
            _viewKey = null;
            SetStatus(string.IsNullOrEmpty(folder) ? $"Added {DisplayName(item)} to Favorites" : $"Added {DisplayName(item)} to Favorites › {folder}");
        }

        /// <summary>Anything put in one of your folders becomes a favorite too (also for folders made before they lived in Favorites).</summary>
        private void StarFolderMembers(IEnumerable<string> keys)
        {
            foreach (var k in keys)
                if (!_favs.Contains(k) && _tags.Get(k).Any(t => t.StartsWith(FolderList.Group + ":", StringComparison.OrdinalIgnoreCase)))
                    _favs.Set(k, true);
        }

        /// <param name="existing">The folder to rename or delete (null: a new one).</param>
        /// <param name="addKey">For a new folder: an item to put in it.</param>
        private void OpenFolderBox(string existing, string addKey)
        {
            _folderBox = true;
            _folderOld = existing;
            _folderText = existing ?? "";
            _folderAddKey = addKey;
            _folderFocus = true;
        }

        private void SaveFolder()
        {
            var name = FolderList.Clean(_folderText);
            if (_folderOld == null)
            {
                if (!_folders.Add(name))
                {
                    SetStatus(name.Length == 0 ? "Give the folder a name." : $"There's already a folder called {name}.", true);
                    return;
                }
                if (_folderAddKey != null) AddToFolder(_folderAddKey, name);
                else SetStatus($"New folder: Favorites › {name}  ·  click it in the details panel, or drag {_kind.Nouns} onto it");
            }
            else if (!string.Equals(_folderOld, name, StringComparison.Ordinal))
            {
                if (!_folders.Rename(_folderOld, name))
                {
                    SetStatus(name.Length == 0 ? "Give the folder a name." : $"There's already a folder called {name}.", true);
                    return;
                }
                var oldTag = FolderList.Tag(_folderOld);
                var keys = _byKey.Keys.Where(k => _tags.Has(k, oldTag)).ToList();
                _tags.Set(keys, FolderList.Tag(name), true, _folders.Facet);
                _tags.Set(keys, oldTag, false, null);
                if (_scope == Scope.Folder && string.Equals(_scopeKey, _folderOld, StringComparison.OrdinalIgnoreCase)) _scopeKey = name;
                SetStatus($"Renamed folder {_folderOld} to {name}");
            }
            _folderBox = false;
            _viewKey = null;
        }

        private void DeleteFolder()
        {
            if (_folderOld == null) return;
            var tag = FolderList.Tag(_folderOld);
            _tags.Set(_byKey.Keys.Where(k => _tags.Has(k, tag)).ToList(), tag, false, null);
            _folders.Remove(_folderOld);
            if (_scope == Scope.Folder && string.Equals(_scopeKey, _folderOld, StringComparison.OrdinalIgnoreCase)) Open(Scope.Favorites, null);
            SetStatus($"Deleted folder {_folderOld} (the {_kind.Nouns} in it stay favorites)");
            _folderBox = false;
            _viewKey = null;
        }

        private void DrawDialogs()
        {
            if (_folderBox)
            {
                var r = new Rect(_rect.x + (_rect.width - 380) / 2, _rect.y + 90, 380, 120);
                GUI.Window(DialogId + 2, r, id =>
                {
                    var e = Event.current;
                    GUILayout.Space(4);
                    GUILayout.Label(Theme.Hint(_folderOld == null ? "Name for the new folder inside Favorites" : $"Rename {_folderOld}, or delete it (its {_kind.Nouns} stay favorites)"), Theme.RichSingle);
                    if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { Later(SaveFolder); e.Use(); }
                    if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { _folderBox = false; e.Use(); return; }
                    GUI.SetNextControlName("ib_folder");
                    _folderText = GUILayout.TextField(_folderText ?? "");
                    if (_folderFocus && e.type == EventType.Repaint) { GUI.FocusControl("ib_folder"); _folderFocus = false; }
                    GUILayout.Space(6);
                    GUILayout.BeginHorizontal();
                    if (_folderOld != null && GUILayout.Button(new GUIContent("Delete folder", $"Delete the folder; the {_kind.Nouns} in it aren't touched"), GUILayout.Width(100))) Later(DeleteFolder);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Cancel", GUILayout.Width(70))) _folderBox = false;
                    if (GUILayout.Button(_folderOld == null ? "Create" : "Save", Theme.Primary, GUILayout.Width(70))) Later(SaveFolder);
                    GUILayout.EndHorizontal();
                    IMGUIUtils.EatInputInRect(new Rect(0, 0, r.width, r.height));
                }, _folderOld == null ? "New favorites folder" : "Favorites folder");
                GUI.BringWindowToFront(DialogId + 2);
            }

            if (_renameKey != null)
            {
                var r = new Rect(_rect.x + (_rect.width - 380) / 2, _rect.y + 90, 380, 120);
                GUI.Window(DialogId, r, id =>
                {
                    var e = Event.current;
                    _byKey.TryGetValue(_renameKey ?? "", out var item);
                    GUILayout.Space(4);
                    GUILayout.Label(Theme.Hint("Name shown in the browser" + (_p.NamesInStudio.Value ? " and Studio's lists" : "") + (item != null ? $"  ·  originally {item.Name}" : "")), Theme.RichSingle);
                    if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { var t = _renameText; Later(() => SaveRename(t)); e.Use(); }
                    if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { _renameKey = null; e.Use(); return; }
                    GUI.SetNextControlName("ib_rename");
                    _renameText = GUILayout.TextField(_renameText ?? "");
                    if (_renameFocus && e.type == EventType.Repaint) { GUI.FocusControl("ib_rename"); _renameFocus = false; }
                    GUILayout.Space(6);
                    GUILayout.BeginHorizontal();
                    if (item != null && GUILayout.Button(new GUIContent("Original name", "Use its own name again"), GUILayout.Width(110))) Later(() => SaveRename(""));
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Cancel", GUILayout.Width(70))) _renameKey = null;
                    if (GUILayout.Button("Save", Theme.Primary, GUILayout.Width(70))) { var t = _renameText; Later(() => SaveRename(t)); }
                    GUILayout.EndHorizontal();
                    IMGUIUtils.EatInputInRect(new Rect(0, 0, r.width, r.height));
                }, "Rename");
                GUI.BringWindowToFront(DialogId);
            }

            if (_reportOpen && _report != null)
            {
                var r = new Rect(_rect.x + 30, _rect.y + 60, Mathf.Max(420, _rect.width - 60), Mathf.Max(260, _rect.height - 120));
                GUI.Window(DialogId + 1, r, id =>
                {
                    var removable = _report.Count(x => x.Removable && x.Note != "moved");
                    GUILayout.Label(Theme.Hint($"{_marked.Count} marked {_kind.Nouns} from {_report.Count} sources. A zipmod can only go when every {_kind.Noun} in it is marked and it holds nothing else (clothes, maps, other studio lists): otherwise removing it would take those too. Moving puts it in mods_to_delete next to mods, so it stops loading after a restart and can be put back."), Theme.Rich);
                    _reportScroll = GUILayout.BeginScrollView(_reportScroll, GUILayout.ExpandHeight(true));
                    foreach (var row in _report)
                    {
                        GUILayout.BeginVertical(Theme.Skin.box);
                        var colour = row.Note == "moved" ? Theme.MutedHex : row.Removable ? Theme.NeonHex : Theme.TextHex;
                        GUILayout.Label($"<color={colour}><b>{row.Title}</b></color>   " + Theme.Hint($"{row.Marked} of {row.Total} marked"), Theme.RichSingle);
                        if (row.File != null) GUILayout.Label(Theme.Hint(row.File), Theme.RichSingle);
                        GUILayout.Label(row.Removable || row.Note == "moved" ? row.Note : Theme.Hint(row.Note), Theme.RichSingle);
                        GUILayout.EndVertical();
                    }
                    GUILayout.EndScrollView();
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(new GUIContent("Copy report", "Copy this list to the clipboard"), GUILayout.Width(100)))
                        GUIUtility.systemCopyBuffer = string.Join("\r\n", _report.Select(x => $"{x.Title}\t{x.Marked}/{x.Total}\t{x.File}\t{x.Note}").ToArray());
                    GUILayout.FlexibleSpace();
                    var was = GUI.enabled;
                    GUI.enabled = was && removable > 0;
                    if (GUILayout.Button(new GUIContent($"Move {removable} zipmod{(removable == 1 ? "" : "s")} to mods_to_delete", "Takes effect after a restart"), Theme.Primary, GUILayout.Width(240)))
                        Later(MoveRemovable);
                    GUI.enabled = was;
                    if (GUILayout.Button("Close", GUILayout.Width(70))) _reportOpen = false;
                    GUILayout.EndHorizontal();
                    IMGUIUtils.EatInputInRect(new Rect(0, 0, r.width, r.height));
                }, "Deletion report");
                GUI.BringWindowToFront(DialogId + 1);
            }
        }

        // ---------------------------------------------------------------- footer

        private void DrawFooter()
        {
            var view = View();
            var f = _footFlow;
            f.Begin(BarWidth);
            var info = $"{view.Count} {(view.Count == 1 ? _kind.Noun : _kind.Nouns)}";
            if (_favs.Count > 0) info += $"  ·  {_favs.Count} favorite{(_favs.Count == 1 ? "" : "s")}";
            f.Next(Flow.Size(info, Styles.Small) + 10);
            GUILayout.Label(info, Styles.Small, GUILayout.ExpandWidth(false));
            GUILayout.Space(10);
            var pics = Grid ? _kind.PictureStatus : null;
            var target = _kind.TargetLine() + (pics != null ? Theme.Hint("  ·  " + pics) : "");
            f.Next(Mathf.Min(Flow.Size(target, Theme.RichSingle), BarWidth));
            GUILayout.Label(target, Theme.RichSingle, GUILayout.ExpandWidth(true));

            var report = _scope == Scope.Marked && !Searching && _marked.Count > 0;
            f.Next((report ? Flow.Fixed(110) + 6 : 0) + Flow.Fixed(66) + Flow.Fixed(62) + 6 + _kind.Actions.Sum(a => Flow.Fixed(a.Width)), true);
            if (report)
            {
                if (GUILayout.Button(new GUIContent("Deletion report", "Which zipmods these come from, and which can be removed"), GUILayout.Width(110)))
                    Later(OpenReport);
                GUILayout.Space(6);
            }
            var was = GUI.enabled;
            GUI.enabled = was && _selected != null;
            if (GUILayout.Button(new GUIContent("Rename", $"Give this {_kind.Noun} your own name (F2)"), GUILayout.Width(66)))
            {
                var k = _selected;
                Later(() => OpenRename(k));
            }
            var isMarked = _selected != null && _marked.Contains(_selected);
            if (GUILayout.Button(new GUIContent(isMarked ? "Unmark" : "Mark", isMarked ? "Take it off the deletion list (Del)" : $"Mark this {_kind.Noun} for deletion: it moves to Marked for deletion (Del)"), GUILayout.Width(62)))
            {
                var k = _selected;
                Later(() => ToggleMarked(k));
            }
            GUILayout.Space(6);
            GUI.enabled = was;
            foreach (var a in _kind.Actions.OrderBy(x => x.Primary ? 1 : 0))
            {
                GUI.enabled = was && _selected != null && (a.CanRun == null || a.CanRun(-1));
                var act = a;
                var key = _selected;
                if (GUILayout.Button(new GUIContent(a.Label, a.Tooltip), a.Primary ? Theme.Primary : Theme.Skin.button, GUILayout.Width(a.Width)))
                    Later(() => Run(act, key));
            }
            GUI.enabled = was;
            f.End();

            string line;
            if (_status != null && Time.unscaledTime < _statusUntil) line = _statusError ? Theme.Error(_status) : _status;
            else if (_selected != null && _byKey.TryGetValue(_selected, out var sel))
                line = Theme.Hint($"{sel.Category.Group.Name} › {sel.Category.Name} › ") + DisplayName(sel)
                    + (_names.Get(sel.Key) != null ? Theme.Hint($"  (originally {sel.Name})") : "")
                    + (string.IsNullOrEmpty(sel.Source) ? "" : Theme.Hint("   ·   " + sel.Source))
                    + Theme.Hint("   ·   F2 rename, Del mark for deletion, F star, T tag");
            else line = !string.IsNullOrEmpty(GUI.tooltip) ? Theme.Hint(GUI.tooltip) : Theme.Hint((_p.PlayOnClick.Value ? $"Click a {_kind.Noun} to {_kind.Verb.ToLowerInvariant()} it" : $"Double-click a {_kind.Noun} (or Enter) to {_kind.Verb.ToLowerInvariant()} it") + "  ·  Up / Down step through the list  ·  the star (or F) adds a favorite  ·  right-click (or T) tags it");
            GUILayout.Label(line, Styles.Status, GUILayout.Height(18));
        }

        // ---------------------------------------------------------------- keys, resize

        private void HandleKeys(Event e)
        {
            if (e.type != EventType.KeyDown) return;
            if (GUIUtility.keyboardControl != 0) return; // typing in a field
            if (!new Rect(0, 0, _rect.width, _rect.height).Contains(e.mousePosition)) return;

            var page = Mathf.Max(1, Mathf.FloorToInt(_listViewH / RowH) - 1);
            switch (e.keyCode)
            {
                case KeyCode.F2:
                    if (_selected != null)
                    {
                        var key = _selected;
                        Later(() => OpenRename(key));
                    }
                    e.Use();
                    return;
                case KeyCode.Delete:
                    if (_selected != null)
                    {
                        var key = _selected;
                        Later(() => ToggleMarked(key));
                    }
                    e.Use();
                    return;
                case KeyCode.DownArrow: StepRow(1); e.Use(); break;
                case KeyCode.UpArrow: StepRow(-1); e.Use(); break;
                case KeyCode.RightArrow: Step(1); e.Use(); break;
                case KeyCode.LeftArrow: Step(-1); e.Use(); break;
                case KeyCode.PageDown: StepRow(page); e.Use(); break;
                case KeyCode.PageUp: StepRow(-page); e.Use(); break;
                case KeyCode.Home: Step(-100000); e.Use(); break;
                case KeyCode.End: Step(100000); e.Use(); break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_selected != null)
                    {
                        var key = _selected;
                        Later(() => Run(PrimaryAction, key));
                    }
                    e.Use();
                    break;
                case KeyCode.F:
                    if (e.control) GUI.FocusControl("ib_search");
                    else if (_selected != null)
                    {
                        var key = _selected;
                        Later(() => _favs.Set(key, !_favs.Contains(key)));
                    }
                    e.Use();
                    break;
                case KeyCode.T:
                    {
                        var at = GUIUtility.GUIToScreenPoint(e.mousePosition);
                        var key = _selected;
                        Later(() => OpenTagWheel(at, key));
                        e.Use();
                    }
                    break;
                case KeyCode.Escape:
                    if (Searching) _search = "";
                    else _selected = null;
                    e.Use();
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
                        _rect.width = Mathf.Max(520, _resizeOrig.x + (screen.x - _resizeStart.x));
                        _rect.height = Mathf.Max(360, _resizeOrig.y + (screen.y - _resizeStart.y));
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
    }
}
