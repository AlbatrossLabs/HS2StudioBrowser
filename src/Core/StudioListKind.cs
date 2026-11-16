using System;
using HarmonyLib;

namespace HS2StudioBrowser
{
    /// <summary>
    /// What every browser has in common, files or not: its names, and how Studio's own list for it is found,
    /// hidden and closed. <see cref="BrowserKind"/> adds cards on disk (outfits, characters, scenes, poses);
    /// <see cref="ItemKind"/> adds entries from the game's lists (animations).
    /// </summary>
    public abstract class StudioListKind
    {
        /// <summary>Short id, used for the favorites file and window ids: "outfits", "animations".</summary>
        public abstract string Id { get; }

        /// <summary>Window title and toolbar tooltip: "Outfit Browser".</summary>
        public abstract string Title { get; }

        public abstract string Noun { get; }    // "outfit"
        public abstract string Nouns { get; }   // "outfits"

        /// <summary>Studio's own list this browser can stand in for: "Costume list".</summary>
        public abstract string StudioListName { get; }
        /// <summary>Whether the kind adds things to the scene from Studio's Add menu (items, characters): offers "Stay open after adding".</summary>
        public virtual bool AddsToScene => false;

        /// <summary>Set by the plugin: whether adding should keep Studio's Add menu open (its "Stay open after adding").</summary>
        public Func<bool> KeepAddMenuOpen = () => false;

        /// <summary>Names the list had in earlier versions, so settings saved under them carry over.</summary>
        public virtual string[] OldStudioListNames => new string[0];

        /// <summary>True while Studio's list for this kind is on screen, with the sex it shows (-1 if none).</summary>
        public abstract bool StudioListOpen(out int sex);

        /// <summary>Hides Studio's list (or shows it again) without closing it.</summary>
        public abstract void SetStudioListHidden(bool hidden);

        /// <summary>Closes Studio's list (when the browser standing in for it is closed).</summary>
        public abstract void CloseStudioList();

        /// <summary>
        /// The tag wheel's starting list, written to BepInEx\config\HS2StudioBrowser.&lt;id&gt;.tagset.txt the first
        /// time (then edited there): one group per line, "Name: value, value", "(one)" after the name for a
        /// group where only one value applies.
        /// </summary>
        public virtual string DefaultTagSet => TagSets.Header + "Color: Black, White, Gray, Red, Pink, Orange, Yellow, Green, Blue, Purple, Brown\nUse: Favorite, WIP, Client, Archive\n";

        /// <summary>Earlier starting lists: a .tagset.txt that still holds one of these, unedited, is updated to the current one.</summary>
        public virtual string[] OlderTagSets => null;

        /// <summary>Whether the shared groups (Project, Status, Use) show in this browser's wheel.</summary>
        public virtual bool UsesSharedTags => true;

        /// <summary>Called from the plugin's Start, after every plugin has loaded (hooks into other plugins go here).</summary>
        public virtual void Install(Harmony harmony) { }

        /// <summary>Called every frame with whether the browser is standing in for Studio's list right now.</summary>
        public virtual void OnUpdate(bool replacing) { }

        /// <summary>Puts anything changed back (plugin unloading).</summary>
        public virtual void Restore() { }
    }
}
