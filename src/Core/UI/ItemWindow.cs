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

        private enum Scope { Favorites, Recent, Group, Category }

        private readonly StudioBrowserPlugin _p;
        private readonly ItemKind _kind;
        private readonly int WindowId;
        private readonly KeyList _favs;
        private readonly KeyList _recent;
        private readonly TagStore _tags;
        private readonly TagSet _tagSet;
        private readonly TagRadial _radial;
        private readonly HashSet<string> _filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            _scrollToSelected = true;
        }

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            InputGuard.Set(this, false);
            _radial.Close();
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

            // Keep the open scope if it still exists, else start on Favorites (or the first category).
            var valid = _scope == Scope.Favorites || _scope == Scope.Recent
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
            var key = $"{_scope}|{_scopeKey}|{_search}|{_p.ItemSortBy.Value}|{_favs.Version}|{recentVersion}|{_catalog?.Count}|{_tags.Version}|{string.Join("|", _filter.ToArray())}|{(_filter.Count > 0 || Searching ? _kind.AutoVersion : 0)}";
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
                        var hay = (i.Name + " " + i.Source + " " + i.Category.Name + " " + i.Category.Group.Name + " " + string.Join(" ", tags.Select(TagFilter.ValueOf).ToArray())).ToLowerInvariant();
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
                case Scope.Group:
                    q = _groups.TryGetValue(_scopeKey ?? "", out var g) ? g.Categories.SelectMany(c => c.Items) : Enumerable.Empty<CatalogItem>();
                    break;
                default:
                    q = _categories.TryGetValue(_scopeKey ?? "", out var cat) ? cat.Items : Enumerable.Empty<CatalogItem>();
                    break;
            }

            var list = q.ToList();
            if (!recentOrder)
            {
                // Favorites first (except in Favorites itself, where everything is one), then the chosen order.
                var byName = _p.ItemSortBy.Value == ItemSort.Name;
                var index = new Dictionary<CatalogItem, int>();
                for (var n = 0; n < list.Count; n++) index[list[n]] = n;
                IOrderedEnumerable<CatalogItem> o = list.OrderBy(i => _scope != Scope.Favorites && _favs.Contains(i.Key) ? 0 : 1);
                o = byName ? o.ThenBy(i => i.Name, NaturalComparer.Instance) : o.ThenBy(i => index[i]);
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

            GUILayout.BeginArea(new Rect(10, 32, _rect.width - 20, _rect.height - 42));
            DrawTopBar();
            DrawFilterBar();
            GUILayout.Space(4);
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawTree();
            GUILayout.Space(6);
            DrawList();
            GUILayout.EndHorizontal();
            if (_kind.HasControls)
            {
                GUILayout.Space(4);
                _kind.DrawControls();
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
            GUILayout.BeginHorizontal();
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

            GUILayout.FlexibleSpace();
            if (_kind.HasPictures && _p.GridView != null)
            {
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
            if (GUILayout.Button(new GUIContent(tagLabel, $"Show only {_kind.Nouns} with certain tags (opens the tag wheel)"), _filter.Count > 0 ? Theme.Primary : Theme.Skin.button, GUILayout.Width(_filter.Count > 0 ? 68 : 48)))
            {
                var at = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                Later(() => _radial.ShowFilter(at));
            }
            GUILayout.Space(8);
            GUILayout.Label("Sort", Styles.Small, GUILayout.ExpandWidth(false));
            if (GUILayout.Toggle(_p.ItemSortBy.Value == ItemSort.StudioOrder, new GUIContent("Studio", "In the order Studio lists them"), Theme.Choice, GUILayout.Width(58)))
                _p.ItemSortBy.Value = ItemSort.StudioOrder;
            if (GUILayout.Toggle(_p.ItemSortBy.Value == ItemSort.Name, new GUIContent("Name", "By name"), Theme.Choice, GUILayout.Width(50)))
                _p.ItemSortBy.Value = ItemSort.Name;

            GUILayout.Space(8);
            var verb = _kind.Verb.ToLowerInvariant();
            var play = GUILayout.Toggle(_p.PlayOnClick.Value, new GUIContent($"{_kind.Verb} on click",
                _kind.RunOnStep ? $"Clicking a {_kind.Noun} or stepping with the arrow keys {verb}s it. Off: double-click or Enter {verb}s."
                    : $"Clicking a {_kind.Noun} {verb}s it. Off: double-click or Enter {verb}s. The arrow keys only move through the list."), GUILayout.ExpandWidth(false));
            if (play != _p.PlayOnClick.Value) _p.PlayOnClick.Value = play;

            GUILayout.Space(6);
            if (GUILayout.Button(new GUIContent("Refresh", $"Read the {_kind.Nouns} again"), GUILayout.Width(64)))
                Later(Rebuild);
            GUILayout.EndHorizontal();
        }

        private void DrawFilterBar()
        {
            if (_filter.Count == 0) return;
            GUILayout.Space(3);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Theme.Hint("Tagged"), Theme.RichSingle, GUILayout.ExpandWidth(false));
            foreach (var tag in _filter.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList())
            {
                var t = tag;
                if (GUILayout.Button(new GUIContent(t.Replace(":", ": ") + "  x", "Remove this from the filter"), Theme.Choice, GUILayout.ExpandWidth(false)))
                    Later(() => _filter.Remove(t));
            }
            if (GUILayout.Button(new GUIContent("Clear", "Show everything again"), GUILayout.Width(50))) Later(() => _filter.Clear());
            GUILayout.Label(Theme.Hint($"in all {_total} {_kind.Nouns}"), Theme.RichSingle, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
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

            _treeScroll = GUILayout.BeginScrollView(_treeScroll, false, false, GUIStyle.none, Theme.Skin.verticalScrollbar, Theme.Skin.box, GUILayout.ExpandHeight(true));

            TreeRow(null, $"Favorites  <color={Theme.MutedHex}><size=10>{_favs.Count}</size></color>", 0, !Searching && _scope == Scope.Favorites,
                () => Open(Scope.Favorites, null), null);
            TreeRow(null, $"Recently used  <color={Theme.MutedHex}><size=10>{_recent.Items.Count(k => _byKey.ContainsKey(k))}</size></color>", 0,
                !Searching && _scope == Scope.Recent, () => Open(Scope.Recent, null), null);
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
        private void TreeRow(bool? open, string text, int depth, bool current, Action click, Action toggle)
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

            if (e.type == EventType.Repaint)
                (current ? Styles.TreeRowOn : Styles.TreeRow).Draw(row, content, inView && row.Contains(e.mousePosition), false, false, false);
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
                var name = string.IsNullOrEmpty(item.Name) ? "(no name)" : item.Name;
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
            var cellW = size + 8f;
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
            var pic = new Rect(cell.x + 4, cell.y + 4, size, size);
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
                var name = string.IsNullOrEmpty(item.Name) ? "(no name)" : item.Name;
                Styles.CardName.Draw(new Rect(cell.x + 3, pic.yMax + 2, cell.width - 6, cell.height - size - 10), name, false, false, false, false);
            }
            ItemMouse(item, star, hover, e);
        }

        // ---------------------------------------------------------------- footer

        private void DrawFooter()
        {
            var view = View();
            GUILayout.BeginHorizontal();
            var info = $"{view.Count} {(view.Count == 1 ? _kind.Noun : _kind.Nouns)}";
            if (_favs.Count > 0) info += $"  ·  {_favs.Count} favorite{(_favs.Count == 1 ? "" : "s")}";
            GUILayout.Label(info, Styles.Small, GUILayout.ExpandWidth(false));
            GUILayout.Space(10);
            var pics = Grid ? _kind.PictureStatus : null;
            GUILayout.Label(_kind.TargetLine() + (pics != null ? Theme.Hint("  ·  " + pics) : ""), Theme.RichSingle, GUILayout.ExpandWidth(true));

            var was = GUI.enabled;
            foreach (var a in _kind.Actions.OrderBy(x => x.Primary ? 1 : 0))
            {
                GUI.enabled = was && _selected != null && (a.CanRun == null || a.CanRun(-1));
                var act = a;
                var key = _selected;
                if (GUILayout.Button(new GUIContent(a.Label, a.Tooltip), a.Primary ? Theme.Primary : Theme.Skin.button, GUILayout.Width(a.Width)))
                    Later(() => Run(act, key));
            }
            GUI.enabled = was;
            GUILayout.EndHorizontal();

            string line;
            if (_status != null && Time.unscaledTime < _statusUntil) line = _statusError ? Theme.Error(_status) : _status;
            else if (_selected != null && _byKey.TryGetValue(_selected, out var sel))
                line = Theme.Hint($"{sel.Category.Group.Name} › {sel.Category.Name} › ") + sel.Name + (string.IsNullOrEmpty(sel.Source) ? "" : Theme.Hint("   ·   " + sel.Source))
                    + Theme.Hint("   ·   Up / Down to step, F to star, T to tag");
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
