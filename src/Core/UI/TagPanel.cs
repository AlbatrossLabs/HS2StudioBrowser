using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2StudioBrowser.Files;
using UnityEngine;

namespace HS2StudioBrowser.UI
{
    /// <summary>
    /// The details panel on the right of a browser: the selected card or item (picture, name, where it's
    /// from), its tags as chips grouped like the tag wheel (click to put on or take off, for everything
    /// selected), and its automatic tags. The wheel and this panel edit the same tags.
    /// </summary>
    public sealed class TagPanel
    {
        private Vector2 _scroll;
        private readonly HashSet<string> _closed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Flow> _flows = new Dictionary<string, Flow>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Color, Texture2D> _swatches = new Dictionary<Color, Texture2D>();
        private GUIStyle _title, _sub, _group, _auto, _link;

        /// <summary>How wide the panel is in a window <paramref name="windowWidth"/> wide.</summary>
        public static float WidthFor(float windowWidth) => Mathf.Clamp(windowWidth * 0.27f, 200f, 290f);

        public sealed class Selection
        {
            public IList<string> Targets;   // keys or paths the chips apply to
            public string Title, Subtitle;  // rich text
            public Texture Picture;
            public IEnumerable<string> Auto; // automatic tags ("Group:Value"), read only
            public string Noun;
        }

        /// <param name="changed">Called after a tag was put on or taken off.</param>
        /// <param name="hide">Called by the chevron that folds the panel away.</param>
        public void Draw(float width, TagSet set, TagStore store, Selection sel, Action changed, Action hide) =>
            Draw(width, set, store, sel, changed, hide, null, null);

        /// <param name="folders">Your folders, drawn first as chips (null: none).</param>
        /// <param name="newFolder">Called by "+ new folder" (null: no link).</param>
        public void Draw(float width, TagSet set, TagStore store, Selection sel, Action changed, Action hide, TagFacet folders, Action newFolder)
        {
            EnsureStyles();
            GUILayout.BeginVertical(Theme.Skin.box, GUILayout.Width(width), GUILayout.ExpandHeight(true));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(Styles.PanelRight, "Hide the details panel"), Styles.TreeArrow, GUILayout.Width(22), GUILayout.Height(20)))
                hide();
            GUILayout.Label("Details", _group, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            var inner = width - 34; // box padding and the scroll bar
            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);

            if (sel == null || sel.Targets == null || sel.Targets.Count == 0)
            {
                GUILayout.Label(Theme.Hint($"Select a {sel?.Noun ?? "card"} to see its tags and set them here.\n\nClick a tag below to put it on or take it off; with several selected it applies to all of them."), Theme.Rich, GUILayout.Width(inner));
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                return;
            }

            // The card or item.
            if (sel.Picture != null && sel.Picture.width > 0)
            {
                var h = Mathf.Min(inner * sel.Picture.height / sel.Picture.width, 180f);
                var r = GUILayoutUtility.GetRect(inner, h, GUILayout.Width(inner), GUILayout.Height(h));
                if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, sel.Picture, ScaleMode.ScaleToFit);
                GUILayout.Space(4);
            }
            if (!string.IsNullOrEmpty(sel.Title)) GUILayout.Label(sel.Title, _title, GUILayout.Width(inner));
            if (!string.IsNullOrEmpty(sel.Subtitle)) GUILayout.Label(sel.Subtitle, _sub, GUILayout.Width(inner));
            GUILayout.Space(6);

            var n = sel.Targets.Count;

            // Your folders first, if the browser has them.
            if (folders != null)
            {
                GUILayout.BeginHorizontal(GUILayout.Width(inner));
                GUILayout.Label("<color=#F5C451>★</color> Favorites folders", _group, GUILayout.ExpandWidth(true));
                if (newFolder != null && GUILayout.Button(new GUIContent("+ new folder", "Make a folder inside Favorites and put this in it"), _link, GUILayout.ExpandWidth(false))) newFolder();
                GUILayout.EndHorizontal();
                if (folders.Values.Count == 0)
                    GUILayout.Label(Theme.Hint("No folders yet. Make one here, or with + next to Favorites on the left; then click a folder to put this in it (it becomes a favorite), or drag it onto the folder."), Theme.Rich, GUILayout.Width(inner));
                else
                {
                    var flow = FlowFor("\u0001folders");
                    flow.Begin(inner);
                    foreach (var v in folders.Values)
                    {
                        var tag = folders.Tag(v);
                        var with = sel.Targets.Count(t => store.Has(t, tag));
                        var state = with == 0 ? 0 : with == n ? 2 : 1;
                        var content = new GUIContent(state == 1 ? $"<color={Theme.NeonHex}>{v}</color>" : v,
                            state == 2 ? "In this folder: click to take it out (it stays a favorite)" : "Click to put it in this folder (and make it a favorite)");
                        flow.Next(Flow.Size(content, Theme.Choice));
                        var on = GUILayout.Toggle(state == 2, content, Theme.Choice, GUILayout.ExpandWidth(false));
                        if (on != (state == 2))
                        {
                            store.Set(sel.Targets, tag, state != 2, folders);
                            changed();
                        }
                    }
                    flow.End();
                }
                GUILayout.Space(8);
            }

            // Tags, group by group.
            set.Refresh();
            foreach (var f in set.Facets)
            {
                var onCount = sel.Targets.Count(t => store.Get(t).Any(x => x.StartsWith(f.Name + ":", StringComparison.OrdinalIgnoreCase)));
                var open = !_closed.Contains(f.Name);
                GUILayout.BeginHorizontal(GUILayout.Width(inner));
                var head = $"{(open ? "▾" : "▸")} {f.Name}" + (f.Single ? Theme.Hint("  one") : "")
                    + (!open && onCount > 0 ? Theme.Hint("  ·  " + string.Join(", ", f.Values.Where(v => sel.Targets.Any(t => store.Has(t, f.Tag(v)))).ToArray())) : "");
                if (GUILayout.Button(head, _group, GUILayout.ExpandWidth(true)))
                {
                    if (open) _closed.Add(f.Name);
                    else _closed.Remove(f.Name);
                }
                if (onCount > 0 && GUILayout.Button(new GUIContent("clear", $"Take every {f.Name} tag off"), _link, GUILayout.ExpandWidth(false)))
                {
                    store.ClearFacet(sel.Targets, f);
                    changed();
                }
                GUILayout.EndHorizontal();
                if (!open) continue;

                var flow = FlowFor(f.Name);
                flow.Begin(inner);
                foreach (var v in f.Values)
                {
                    var tag = f.Tag(v);
                    var with = sel.Targets.Count(t => store.Has(t, tag));
                    var state = with == 0 ? 0 : with == n ? 2 : 1;
                    var content = new GUIContent(state == 1 ? $"<color={Theme.NeonHex}>{v}</color>" : v, Swatch(v),
                        state == 2 ? "Click to take it off" : state == 1 ? $"On {with} of {n} selected: click to put it on all of them" : "Click to put it on");
                    flow.Next(Flow.Size(content, Theme.Choice));
                    var on = GUILayout.Toggle(state == 2, content, Theme.Choice, GUILayout.ExpandWidth(false));
                    if (on != (state == 2))
                    {
                        store.Set(sel.Targets, tag, state != 2, f);
                        changed();
                    }
                }
                flow.End();
                GUILayout.Space(4);
            }

            // Automatic tags: read from the card or item, shown for reference.
            var auto = sel.Auto?.ToList();
            if (auto != null && auto.Count > 0)
            {
                GUILayout.Space(4);
                GUILayout.Label(Theme.Hint("Automatic"), _group, GUILayout.Width(inner));
                var flow = FlowFor("\u0001auto");
                flow.Begin(inner);
                foreach (var t in auto)
                {
                    var c = new GUIContent(Theme.Hint(t.Replace(":", ": ")), "Worked out from the file itself; can't be changed here");
                    flow.Next(Flow.Size(c, _auto));
                    GUILayout.Label(c, _auto, GUILayout.ExpandWidth(false));
                }
                flow.End();
            }

            GUILayout.Space(8);
            GUILayout.BeginHorizontal(GUILayout.Width(inner));
            if (GUILayout.Button(new GUIContent("edit list", "Open this browser's tag list in your text editor"), _link, GUILayout.ExpandWidth(false))) Open(set.FilePath);
            if (set.SharedPath != null && GUILayout.Button(new GUIContent("edit shared", "Open the groups every browser shares (Project, Status, Use)"), _link, GUILayout.ExpandWidth(false))) Open(set.SharedPath);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        /// <summary>The folded-away panel: a thin column with a chevron to bring it back.</summary>
        public static void DrawFolded(Action show)
        {
            GUILayout.BeginVertical(GUILayout.Width(24), GUILayout.ExpandHeight(true));
            if (GUILayout.Button(new GUIContent(Styles.PanelLeft, "Show the details panel (tags)"), Styles.TreeArrow, GUILayout.Width(22), GUILayout.Height(22)))
                show();
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        private Flow FlowFor(string key)
        {
            if (!_flows.TryGetValue(key, out var f)) _flows[key] = f = new Flow();
            return f;
        }

        private Texture2D Swatch(string value)
        {
            var c = TagRadial.ColorOf(value);
            if (!c.HasValue) return null;
            if (_swatches.TryGetValue(c.Value, out var t) && t != null) return t;
            t = new Texture2D(10, 10, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var px = new Color[100];
            for (var i = 0; i < 100; i++)
            {
                var x = i % 10;
                var y = i / 10;
                px[i] = x == 0 || y == 0 || x == 9 || y == 9 ? new Color(1, 1, 1, 0.35f) : c.Value;
            }
            t.SetPixels(px);
            t.Apply();
            return _swatches[c.Value] = t;
        }

        private static void Open(string file)
        {
            try { Process.Start(file); }
            catch (Exception ex) { Log.Source.LogWarning("Couldn't open the tag list: " + ex.Message); }
        }

        private void EnsureStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(Theme.Skin.label) { richText = true, wordWrap = true, fontStyle = FontStyle.Bold };
            _sub = new GUIStyle(Theme.Rich) { fontSize = Mathf.Max(10, Theme.Skin.label.fontSize - 1) };
            _group = new GUIStyle(Theme.RichSingle) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _auto = new GUIStyle(Theme.Choice) { richText = true };
            _link = new GUIStyle(Theme.RichSingle) { normal = { textColor = Theme.Col(Theme.MutedHex) }, hover = { textColor = Theme.Col(Theme.NeonHex) } };
        }
    }
}
