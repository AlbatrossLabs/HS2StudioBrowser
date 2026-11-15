using System.Collections.Generic;

namespace HS2StudioBrowser
{
    /// <summary>
    /// A browser over entries from the game's own lists rather than files (animations; maps later): a catalog
    /// of groups, categories and items, what can be done with an item, and Studio's list it stands in for.
    /// Favorites, recently used, search and stepping through items are handled by the shared window.
    /// </summary>
    public abstract class ItemKind : StudioListKind
    {
        /// <summary>Reads the catalog (main thread; on first open and on Refresh). Empty groups and categories are dropped by the window.</summary>
        public abstract List<ItemGroup> BuildCatalog();

        /// <summary>What can be done with the selected item; Run gets the item's key. The first Primary action runs on click (if set), double-click and Enter.</summary>
        public abstract IList<CardAction> Actions { get; }

        /// <summary>Footer text saying what the actions apply to (rich text), e.g. "Plays on Aiko".</summary>
        public abstract string TargetLine();

        /// <summary>Label for the catalog's top level in the panel header: "Groups".</summary>
        public virtual string GroupsLabel => "Groups";

        /// <summary>What the primary action does, for labels: "Play", "Add".</summary>
        public virtual string Verb => "Play";

        /// <summary>Whether a click runs the primary action by default (the setting starts from this).</summary>
        public virtual bool DefaultRunOnClick => true;

        /// <summary>Whether stepping with the arrow keys runs it too (playing yes; adding to the scene no).</summary>
        public virtual bool RunOnStep => true;

        /// <summary>Bumps when <see cref="AutoTags"/> results change (so filtered views are rebuilt).</summary>
        public virtual int AutoVersion => 0;

        /// <summary>Whether items have pictures (then the window offers a grid view).</summary>
        public virtual bool HasPictures => false;

        /// <summary>The item's picture if ready, else null (and it's fetched or made; called every frame it's on screen).</summary>
        public virtual UnityEngine.Texture2D Picture(CatalogItem item) => null;

        /// <summary>True when the item has no picture to show at all.</summary>
        public virtual bool PictureMissing(CatalogItem item) => false;

        /// <summary>Main thread, every frame while the window is open: fetch or make the pictures asked for.</summary>
        public virtual void TickPictures() { }

        /// <summary>A short note on where pictures come from, for the footer; null for none.</summary>
        public virtual string PictureStatus => null;

        /// <summary>The window was closed (free pictures).</summary>
        public virtual void OnHidden() { }

        /// <summary>Whether the kind draws a controls strip under the list (animation speed, pattern...).</summary>
        public virtual bool HasControls => false;

        /// <summary>Draws the controls strip (GUILayout, inside the window), full width, between the list and the footer.</summary>
        public virtual void DrawControls() { }

        /// <summary>A short note shown on the item's row (rich text), e.g. its length; null for none. Called while drawing.</summary>
        public virtual string Badge(CatalogItem item) => null;

        /// <summary>
        /// Groups of tags the kind works out by itself (e.g. Length: Pose / Short / Long). Offered in the
        /// filter wheel and matched by search, never stored or set by hand. Null for none.
        /// </summary>
        public virtual IList<Files.TagFacet> AutoFacets => null;

        /// <summary>The item's automatic tags ("Length:Pose"), from <see cref="AutoFacets"/>.</summary>
        public virtual IEnumerable<string> AutoTags(CatalogItem item) { yield break; }
    }

    public sealed class ItemGroup
    {
        public string Key;
        public string Name;
        public readonly List<ItemCategory> Categories = new List<ItemCategory>();
    }

    public sealed class ItemCategory
    {
        public string Key;
        public string Name;
        public ItemGroup Group;
        public readonly List<CatalogItem> Items = new List<CatalogItem>();
    }

    public sealed class CatalogItem
    {
        /// <summary>Stable across sessions and installs (favorites and recents are stored by it).</summary>
        public string Key;
        public string Name;
        /// <summary>Where it comes from, shown muted: a zipmod's name, "base game".</summary>
        public string Source;
        public ItemCategory Category;
        /// <summary>The kind's own data (ids to load it with).</summary>
        public object Tag;
        /// <summary>Position in Studio's own order.</summary>
        public int Order;
    }
}
