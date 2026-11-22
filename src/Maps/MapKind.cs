using System;
using System.Collections.Generic;
using System.Linq;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.Game;
using HS2StudioBrowser.UI;
using UnityEngine;

namespace HS2StudioBrowser.Maps
{
    /// <summary>Studio's maps (game and zipmods), loaded into the scene; stands in for the Add › Map list.</summary>
    internal sealed class MapKind : ItemKind
    {
        private readonly MapBrowserPlugin _plugin;
        private readonly List<CardAction> _actions;
        private readonly MapShots _shots = new MapShots();
        private readonly Dictionary<string, MapTable.Ids> _ids = new Dictionary<string, MapTable.Ids>();
        private readonly Dictionary<int, string> _keyOfNo = new Dictionary<int, string>();
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();

        // Picture-on-load: watch the loaded map; shoot a moment after it finishes loading.
        private int _lastNo = int.MinValue;
        private bool _wasLoading;
        private int _shotNo = -1;
        private float _shotAt = -1f;
        private string _lastMessage;

        public MapKind(MapBrowserPlugin plugin)
        {
            _plugin = plugin;
            _actions = new List<CardAction>
            {
                new CardAction
                {
                    Label = "No map", Width = 70,
                    Tooltip = "Clear the scene's map (as Studio's list's top entry does)",
                    CanRun = _ => MapPanel.Current >= 0,
                    Run = (key, sex) => Unload(),
                },
                new CardAction
                {
                    Label = "Picture", Width = 70,
                    Tooltip = "Give the map that's loaded now a picture of exactly what's on screen (menus and characters left out). Frame the camera first; replaces its picture",
                    CanRun = _ => MapPanel.Current >= 0 && !MapPanel.Loading,
                    Run = (key, sex) => PictureNow(),
                },
                new CardAction
                {
                    Label = "Load", Primary = true, Width = 80,
                    Tooltip = "Load this map into the scene, replacing the current one (double-click or Enter does the same)",
                    Run = Load,
                },
            };
        }

        public override string Id => "maps";
        public override string Title => "Map Browser";
        public override string Noun => "map";
        public override string Nouns => "maps";
        public override string StudioListName => "Map list";
        public override string GroupsLabel => "Sources";
        public override string Verb => "Load";
        public override bool DefaultRunOnClick => false;
        public override bool RunOnStep => false;
        public override bool AddsToScene => true;
        public override IList<CardAction> Actions => _actions;
        public override string DefaultTagSet => TagSets.Maps;
        public override string[] OwnListPrefixes => new[] { "Map" };

        public override List<ItemGroup> BuildCatalog()
        {
            var catalog = MapTable.Build();
            _ids.Clear();
            _keyOfNo.Clear();
            _names.Clear();
            foreach (var g in catalog)
            foreach (var c in g.Categories)
            foreach (var i in c.Items)
                if (i.Tag is MapTable.Ids ids && !_ids.ContainsKey(i.Key))
                {
                    _ids[i.Key] = ids;
                    _keyOfNo[ids.No] = i.Key;
                    _names[i.Key] = i.Name;
                }
            return catalog;
        }

        private string NameOf(int no)
        {
            if (_keyOfNo.TryGetValue(no, out var key) && _names.TryGetValue(key, out var n)) return n;
            return Studio.Info.Instance.dicMapLoadInfo.TryGetValue(no, out var info) ? info.name : "map";
        }

        private string KeyOf(int no) => _keyOfNo.TryGetValue(no, out var key) ? key : MapTable.KeyFor(no, out _);

        public override string CurrentKey => MapPanel.Current >= 0 ? KeyOf(MapPanel.Current) : null;

        public override string TargetLine()
        {
            var no = MapPanel.Current;
            if (MapPanel.Loading) return Theme.Hint("Loading ") + NameOf(no) + Theme.Hint("...");
            return no < 0
                ? Theme.Hint("Load puts the map in the scene  ·  no map loaded")
                : Theme.Hint("Load replaces the map  ·  loaded now: ") + NameOf(no);
        }

        public override string Badge(CatalogItem item) =>
            item.Tag is MapTable.Ids ids && ids.No == MapPanel.Current ? "loaded" : null;

        // ---------------------------------------------------------------- actions

        private ActionResult Load(string key, int sex)
        {
            if (!_ids.TryGetValue(key, out var ids)) return ActionResult.Fail("That map is gone; press Refresh.");
            var name = _names.TryGetValue(key, out var n) ? n : "map";
            try
            {
                var close = !KeepAddMenuOpen() && Studio.Studio.optionSystem.autoHide;
                MapPanel.Load(ids.No, close);
                return ActionResult.Ok(_shots.Has(key)
                    ? $"Loading {name}"
                    : $"Loading {name}  ·  frame a view you like and press Picture to give it one");
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Loading {name} failed: {ex}");
                return ActionResult.Fail("Loading failed: " + ex.Message);
            }
        }

        private ActionResult Unload()
        {
            try
            {
                MapPanel.Load(-1, false);
                return ActionResult.Ok("Map cleared");
            }
            catch (Exception ex)
            {
                return ActionResult.Fail("Couldn't clear the map: " + ex.Message);
            }
        }

        private ActionResult PictureNow()
        {
            var no = MapPanel.Current;
            var key = KeyOf(no);
            if (key == null) return ActionResult.Fail("Load a map first.");
            var name = NameOf(no);
            _plugin.Shoot(png =>
            {
                if (png == null)
                {
                    _lastMessage = $"Couldn't picture {name} (details in BepInEx\\cache\\HS2StudioBrowser.maps.log)";
                    return;
                }
                try
                {
                    _shots.Save(key, png);
                    _lastMessage = $"New picture for {name}";
                }
                catch (Exception ex)
                {
                    _lastMessage = $"Couldn't save the picture of {name}: {ex.Message}";
                }
            });
            return ActionResult.Ok($"Taking a picture of {name}...");
        }

        // ---------------------------------------------------------------- what the loaded map has

        public override void OnUpdate(bool replacing)
        {
            int no;
            bool loading;
            try
            {
                if (Studio.Studio.Instance == null) return;
                no = MapPanel.Current;
                loading = MapPanel.Loading;
            }
            catch { return; }

            // A moment after a map finishes loading, note its extras (options, lights, water) for the automatic tags.
            if (no != _lastNo || (_wasLoading && !loading))
            {
                if (no >= 0 && !loading)
                {
                    _shotNo = no;
                    _shotAt = Time.unscaledTime + 1f;
                }
                _lastNo = no;
            }
            _wasLoading = loading;

            if (_shotAt > 0f && Time.unscaledTime >= _shotAt)
            {
                _shotAt = -1f;
                if (no != _shotNo || loading) return;
                var key = KeyOf(no);
                if (key != null) Learn(key);
            }
        }

        /// <summary>Notes what the loaded map has, for the automatic tags.</summary>
        private void Learn(string key)
        {
            try
            {
                var extras = new List<string>();
                if (MapPanel.HasOption) extras.Add("Options");
                if (MapPanel.HasLight) extras.Add("Lights");
                var comp = HarmonyLib.AccessTools.Property(typeof(Studio.Map), "MapComponent")?.GetValue(Singleton<Studio.Map>.Instance, null) as Studio.MapComponent;
                if (comp != null && ((comp.renderersSea != null && comp.renderersSea.Length > 0) || comp.objSeaParent != null)) extras.Add("Water");
                _shots.Remember(key, extras);
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't read the map's extras: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- pictures

        public override bool HasPictures => true;
        public override float PictureAspect => 16f / 9f;
        public override Texture2D Picture(CatalogItem item) => _shots.Get(item.Key);
        public override bool PictureMissing(CatalogItem item) => !_shots.Has(item.Key);
        public override void TickPictures() => _shots.Tick();
        public override void OnHidden() => _shots.Clear();
        public override int AutoVersion => _shots.Version;

        public override string PictureStatus
        {
            get
            {
                var total = _ids.Count;
                var missing = _ids.Keys.Count(k => !_shots.Has(k));
                var s = missing == 0 ? "every map has a picture" : $"{missing} of {total} without a picture: load one, frame the view and press Picture";
                if (_lastMessage != null) s = _lastMessage + "  ·  " + s;
                return s;
            }
        }

        // ---------------------------------------------------------------- automatic tags

        private static readonly List<TagFacet> Auto = new List<TagFacet>
        {
            AutoFacet.Make("Source", true, "Base game", "Mod"),
            AutoFacet.Make("Extras", false, "Options", "Lights", "Water", "None"),
            AutoFacet.Make("Picture", true, "Has picture", "No picture"),
        };

        public override IList<TagFacet> AutoFacets => Auto;

        public override IEnumerable<string> AutoTags(CatalogItem item)
        {
            if (!(item.Tag is MapTable.Ids ids)) yield break;
            yield return "Source:" + (ids.Mod ? "Mod" : "Base game");
            var extras = _shots.Extras(item.Key);
            if (extras != null)
            {
                if (extras.Length == 0) yield return "Extras:None";
                foreach (var e in extras) yield return "Extras:" + e;
            }
            yield return "Picture:" + (_shots.Has(item.Key) ? "Has picture" : "No picture");
        }

        // ---------------------------------------------------------------- the map controls strip

        public override bool HasControls => true;

        public override void DrawControls(float width)
        {
            var no = MapPanel.Current;
            GUILayout.BeginHorizontal();
            if (no < 0)
            {
                GUILayout.Label(Theme.Hint("No map loaded. Load one to set its options, lights and position here."), Theme.RichSingle);
                GUILayout.EndHorizontal();
                return;
            }
            if (MapPanel.Loading)
            {
                GUILayout.Label(Theme.Hint($"Loading {NameOf(no)}..."), Theme.RichSingle);
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.Label($"<b>{NameOf(no)}</b>", Theme.RichSingle, GUILayout.ExpandWidth(false));
            GUILayout.Space(12);
            var was = GUI.enabled;
            try
            {
                GUI.enabled = was && MapPanel.HasOption;
                var opt = GUILayout.Toggle(MapPanel.HasOption && MapPanel.OptionOn, new GUIContent(" Options", MapPanel.HasOption ? "Show the map's optional objects (props it can show or hide)" : "This map has no optional objects"), GUILayout.ExpandWidth(false));
                if (MapPanel.HasOption && opt != MapPanel.OptionOn) MapPanel.SetOption(opt);
                GUILayout.Space(8);
                GUI.enabled = was && MapPanel.HasLight;
                var light = GUILayout.Toggle(MapPanel.HasLight && MapPanel.LightOn, new GUIContent(" Lights", MapPanel.HasLight ? "Turn the map's own lights on or off" : "This map has no lights of its own"), GUILayout.ExpandWidth(false));
                if (MapPanel.HasLight && light != MapPanel.LightOn) MapPanel.SetLight(light);
                GUILayout.FlexibleSpace();
                GUI.enabled = was && MapPanel.Moved;
                if (GUILayout.Button(new GUIContent("Reset position", "Put the map back where it loads (it was moved or turned in Studio's map settings)"), GUILayout.Width(110)))
                    MapPanel.ResetPlace();
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Map controls: " + ex.Message);
            }
            finally
            {
                GUI.enabled = was;
            }
            GUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- Studio's list

        public override void ShowNameInStudio(CatalogItem item, string name)
        {
            if (item.Tag is MapTable.Ids ids && Studio.Info.Instance.dicMapLoadInfo.TryGetValue(ids.No, out var info))
                StudioNames.Apply(item.Key, info, name);
        }

        public override bool StudioListOpen(out int sex)
        {
            sex = -1;
            return MapPanel.Open;
        }

        public override void SetStudioListHidden(bool hidden) => MapPanel.SetHidden(hidden);
        public override void CloseStudioList() => MapPanel.Close();
        public override void Restore() => MapPanel.Restore();
    }
}
