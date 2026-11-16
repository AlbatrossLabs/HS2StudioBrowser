using HS2StudioBrowser.Game;
using System.Collections.Generic;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.UI;

namespace HS2StudioBrowser.Items
{
    /// <summary>Studio's items (game and zipmods), added to the scene; stands in for the Add › Item list.</summary>
    internal sealed class ItemBrowserKind : ItemKind
    {
        private readonly List<CardAction> _actions;
        private readonly Dictionary<string, ItemTable.Ids> _ids = new Dictionary<string, ItemTable.Ids>();
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();

        public ItemBrowserKind()
        {
            _actions = new List<CardAction>
            {
                new CardAction
                {
                    Label = "Add inside", Width = 90,
                    Tooltip = "Add this item inside the folder or object selected in the workspace (as if dragged onto it)",
                    CanRun = _ => ItemPanel.SelectedNode() != null,
                    Run = AddInside,
                },
                new CardAction
                {
                    Label = "Add", Primary = true, Width = 80,
                    Tooltip = "Add this item to the scene, as Studio's Item list does (double-click or Enter does the same)",
                    Run = Add,
                },
            };
        }

        public override string DefaultTagSet => TagSets.Items;
        public override string[] OlderTagSets => new[] { TagSets.ItemsV1 };

        private static readonly List<TagFacet> Auto = new List<TagFacet>
        {
            AutoFacet.Make("Source", true, "Base game", "Mod"),
            AutoFacet.Make("Colors", true, "None", "1", "2", "3", "4"),
        };

        public override IList<TagFacet> AutoFacets => Auto;

        // ---------------------------------------------------------------- pictures

        private readonly ItemThumbs _thumbs = new ItemThumbs();

        public override bool HasPictures => true;

        public override UnityEngine.Texture2D Picture(CatalogItem item) =>
            item.Tag is ItemTable.Ids ids ? _thumbs.Get(item.Key, ids) : null;

        public override bool PictureMissing(CatalogItem item) => _thumbs.Missing(item.Key);
        public override void TickPictures() => _thumbs.Tick();
        public override void OnHidden() => _thumbs.Clear();

        public override string PictureStatus
        {
            get
            {
                var parts = new List<string>();
                if (_thumbs.FromQab > 0) parts.Add($"{_thumbs.FromQab} pictures from Quick Access Box");
                if (_thumbs.FromPacks > 0) parts.Add($"{_thumbs.FromPacks} from thumbnail packs");
                if (_thumbs.FromCache > 0) parts.Add($"{_thumbs.FromCache} saved");
                if (_thumbs.Rendered > 0) parts.Add($"{_thumbs.Rendered} made");
                if (_thumbs.Pending > 0) parts.Add($"{_thumbs.Pending} to go");
                return parts.Count == 0 ? null : string.Join(", ", parts.ToArray());
            }
        }

        public override IEnumerable<string> AutoTags(CatalogItem item)
        {
            if (!(item.Tag is ItemTable.Ids ids)) yield break;
            yield return "Source:" + (ids.Mod ? "Mod" : "Base game");
            yield return "Colors:" + (ids.Colors <= 0 ? "None" : ids.Colors.ToString());
        }
        public override string[] OwnListPrefixes => new[] { "Item" };

        public override void ShowNameInStudio(CatalogItem item, string name)
        {
            if (!(item.Tag is ItemTable.Ids ids)) return;
            var t = Studio.Info.Instance.dicItemLoadInfo;
            if (t.TryGetValue(ids.Group, out var cats) && cats.TryGetValue(ids.Category, out var items) && items.TryGetValue(ids.No, out var info))
                StudioNames.Apply(item.Key, info, name);
        }

        public override string Id => "items";
        public override string Title => "Item Browser";
        public override string Noun => "item";
        public override string Nouns => "items";
        public override string StudioListName => "Item list";
        public override string Verb => "Add";
        public override bool DefaultRunOnClick => false;
        public override bool RunOnStep => false;
        public override IList<CardAction> Actions => _actions;

        public override List<ItemGroup> BuildCatalog()
        {
            var catalog = ItemTable.Build();
            _ids.Clear();
            _names.Clear();
            foreach (var g in catalog)
            foreach (var c in g.Categories)
            foreach (var i in c.Items)
            {
                if (i.Tag is ItemTable.Ids ids && !_ids.ContainsKey(i.Key))
                {
                    _ids[i.Key] = ids;
                    _names[i.Key] = i.Name;
                }
            }
            return catalog;
        }

        public override string TargetLine()
        {
            var node = ItemPanel.SelectedNode();
            return node != null
                ? Theme.Hint("Add puts it in the scene  ·  Add inside puts it in ") + ItemPanel.NameOf(node)
                : Theme.Hint("Add puts it in the scene  ·  select a folder or object to Add inside it");
        }

        private ActionResult Add(string key, int sex)
        {
            if (!_ids.TryGetValue(key, out var ids)) return ActionResult.Fail("That item is gone; press Refresh.");
            var name = _names.TryGetValue(key, out var n) ? n : "item";
            try
            {
                return ItemPanel.Add(ids.Group, ids.Category, ids.No)
                    ? ActionResult.Ok($"Added {name}")
                    : ActionResult.Fail($"Studio couldn't add {name} (its zipmod may be missing a file).");
            }
            catch (System.Exception ex)
            {
                Log.Source.LogWarning($"Adding {name} failed: {ex}");
                return ActionResult.Fail("Adding failed: " + ex.Message);
            }
        }

        private ActionResult AddInside(string key, int sex)
        {
            if (!_ids.TryGetValue(key, out var ids)) return ActionResult.Fail("That item is gone; press Refresh.");
            var parent = ItemPanel.SelectedNode();
            if (parent == null) return ActionResult.Fail("Select one folder or object in the workspace first.");
            var name = _names.TryGetValue(key, out var n) ? n : "item";
            try
            {
                var item = ItemPanel.AddInside(ids.Group, ids.Category, ids.No, parent);
                if (item == null) return ActionResult.Fail($"Studio couldn't add {name}.");
                var inside = item.treeNodeObject != null && item.treeNodeObject.parent == parent;
                return inside
                    ? ActionResult.Ok($"Added {name} inside {ItemPanel.NameOf(parent)}")
                    : ActionResult.Ok($"Added {name} (it can't go inside {ItemPanel.NameOf(parent)}, so it's at the top level)");
            }
            catch (System.Exception ex)
            {
                Log.Source.LogWarning($"Adding {name} failed: {ex}");
                return ActionResult.Fail("Adding failed: " + ex.Message);
            }
        }

        public override bool StudioListOpen(out int sex)
        {
            sex = -1;
            return ItemPanel.Open;
        }

        public override void SetStudioListHidden(bool hidden) => ItemPanel.SetHidden(hidden);
        public override void CloseStudioList() => ItemPanel.Close();
        public override void Restore() => ItemPanel.Restore();
    }
}
