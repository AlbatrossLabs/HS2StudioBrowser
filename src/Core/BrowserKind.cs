using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using HS2StudioBrowser.Game;

namespace HS2StudioBrowser
{
    /// <summary>
    /// Everything that differs between browsers (outfits, characters...): where the cards live, how a card's
    /// name is read and changed, what Load does, and how Studio's own list is found, hidden and kept in step.
    /// The shared window, folder tree, thumbnails, favorites, file operations and missing-zipmod check work
    /// through this. One per plugin.
    /// </summary>
    public abstract class BrowserKind : StudioListKind
    {
        /// <summary>Folder under UserData that holds female\ and male\: "coordinate", "chara".</summary>
        public abstract string UserDataFolder { get; }

        /// <summary>Label of the name box in Rename: "Outfit name (what Studio's Costume list shows)".</summary>
        public abstract string NameLabel { get; }

        /// <summary>Height / width of the card pictures (HS2 outfit and character cards are 252 x 352, scenes 320 x 180).</summary>
        public virtual float ThumbAspect => 352f / 252f;

        /// <summary>False when cards aren't split into female\ and male\ (scenes): no Female / Male switch, the root is the folder itself.</summary>
        public virtual bool HasSex => true;

        /// <summary>False when the card stores no name of its own (scenes): Rename changes only the file name.</summary>
        public virtual bool HasStoredName => true;

        /// <summary>Starting thumbnail width, and the slider's maximum.</summary>
        public virtual int DefaultThumbWidth => 110;
        public virtual int MaxThumbWidth => 260;

        /// <summary>Starting sort order.</summary>
        public virtual SortBy DefaultSort => SortBy.Name;

        /// <summary>Which files are cards (read at every scan, so it can follow a setting): "*.png".</summary>
        public virtual string[] FilePatterns => new[] { "*.png" };

        /// <summary>False for cards that carry no picture (a placeholder is drawn instead of loading one).</summary>
        public virtual bool HasPicture(string path) => true;

        /// <summary>What the placeholder says for a card without a picture.</summary>
        public virtual string NoPictureText(string path) => "no picture";

        /// <summary>False when this kind's cards carry nothing the missing-zipmod check can read.</summary>
        public virtual bool ChecksMods => true;

        /// <summary>Extra controls at the end of the top bar (inside the window). Return true to re-read the folder.</summary>
        public virtual bool DrawTopBarExtras() => false;

        /// <summary>True while the kind wants every subfolder's cards shown (e.g. a "needs a picture" view).</summary>
        public virtual bool ForceSubfolders => false;
        /// <summary>Width of what <see cref="DrawTopBarExtras"/> draws (so the bar can wrap before it).</summary>
        public virtual float TopBarExtrasWidth => 0;
        /// <summary>Width of what <see cref="DrawFooterExtras"/> draws.</summary>
        public virtual float FooterExtrasWidth => 0;

        /// <summary>Extra controls in the footer, between the target line and the action buttons.</summary>
        public virtual void DrawFooterExtras() { }

        private readonly List<KeyValuePair<string, string>> _replaced = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Tells the window an action swapped a card for another file (say name.dat became name.png), so
        /// favorites, the selection and the picture follow. Any thread.
        /// </summary>
        protected void ReportReplaced(string oldPath, string newPath)
        {
            lock (_replaced) _replaced.Add(new KeyValuePair<string, string>(oldPath, newPath));
        }

        /// <summary>Replacements reported since the last call (window, main thread).</summary>
        public List<KeyValuePair<string, string>> TakeReplaced()
        {
            lock (_replaced)
            {
                if (_replaced.Count == 0) return null;
                var list = new List<KeyValuePair<string, string>>(_replaced);
                _replaced.Clear();
                return list;
            }
        }

        private volatile bool _rescan;

        /// <summary>Asks the window to re-read the open folder (say after saving a new card into it). Any thread.</summary>
        protected void RequestRescan() => _rescan = true;

        /// <summary>True once after <see cref="RequestRescan"/> (window, main thread).</summary>
        public bool TakeRescan()
        {
            if (!_rescan) return false;
            _rescan = false;
            return true;
        }

        /// <summary>UserData\&lt;UserDataFolder&gt;, full path.</summary>
        public string Folder => Path.GetFullPath(Path.Combine(Path.Combine(Paths.GameRootPath, "UserData"), UserDataFolder));

        public virtual string Root(int sex) => HasSex ? Path.Combine(Folder, sex == 0 ? "male" : "female") : Folder;

        // ---------------------------------------------------------------- cards (called on worker threads)

        /// <summary>The name stored in the card, or null if the file isn't this kind of card.</summary>
        public abstract string ReadName(string path);

        /// <summary>Changes the name stored in the card, leaving everything else as it was. Throws on failure.</summary>
        public abstract void WriteName(string path, string newName);

        /// <summary>
        /// What makes two cards the same, for the duplicate finder (worker thread). Default: the card's data after
        /// its picture, so copies with different thumbnails still match. Null for a file that can't be read.
        /// </summary>
        public virtual Files.DupeKey DuplicateKey(string path) => Files.DupeKey.AfterPicture(path);

        /// <summary>
        /// Groups the kind works out from each card (an outfit's clothing slots, a scene's cast...). Offered in
        /// the filter wheel and matched by search; never stored or set by hand. Null for none.
        /// </summary>
        public virtual IList<Files.TagFacet> AutoFacets => null;

        /// <summary>The card's automatic tags ("Parts:Top"), read from the file on a worker thread.</summary>
        public virtual IEnumerable<string> ReadAutoTags(string path) => null;

        /// <summary>Bump when <see cref="ReadAutoTags"/> changes, so cached results are read again.</summary>
        public virtual int AutoTagsLogic => 1;

        /// <summary>Whether moving through the grid with the arrow keys runs the primary action on each card (poses: try each one on).</summary>
        public virtual bool RunOnStep => false;

        /// <summary>Tolerance for near-identical cards (0: exact matches only). Poses use degrees.</summary>
        public virtual float NearTolerance => 0f;

        /// <summary>The card's plugin-data block (ExtendedSave), for the missing-zipmod check. Null if none.</summary>
        public virtual byte[] ReadPluginData(string path) => null;

        /// <summary>
        /// Items in the card that need zipmods you don't have (empty if none), or null if it couldn't be read.
        /// Default: Sideloader's list in the card's plugin data. Runs on a worker thread.
        /// </summary>
        public virtual List<MissingItem> FindMissing(string path) => ModCheck.Check(path, ReadPluginData);

        // ---------------------------------------------------------------- actions (main thread)

        /// <summary>What can be done with the selected card. The first Primary action runs on double-click and Enter.</summary>
        public abstract IList<CardAction> Actions { get; }

        /// <summary>Footer text saying what the actions will apply to (rich text), e.g. "Loads onto Aiko".</summary>
        public abstract string TargetLine(int sex);

        // ---------------------------------------------------------------- Studio's own list (main thread)

        /// <summary>Mirrors file changes made in the browser into Studio's list, in memory where possible.</summary>
        public abstract void SyncStudioList(IList<ListChange> changes);

        /// <summary>The folder open in the browser changed (main thread).</summary>
        public virtual void OnFolderChanged(string folder) { }

    }

    /// <summary>A thing to do with the selected card: a footer button and a right-click menu entry.</summary>
    public sealed class CardAction
    {
        public string Label;          // footer button
        public string MenuLabel;      // right-click entry (defaults to Label)
        public string Tooltip;
        public string Key;            // shown in the menu, e.g. "Enter"
        public bool Primary;          // highlighted; double-click and Enter run it
        public float Width = 96;
        public Func<int, bool> CanRun;                 // sex shown -> enabled
        public Func<string, int, ActionResult> Run;    // (card path, sex shown) -> status line
    }

    public struct ActionResult
    {
        public string Message;
        public bool Error;

        public static ActionResult Ok(string message) => new ActionResult { Message = message };
        public static ActionResult Fail(string message) => new ActionResult { Message = message, Error = true };
    }

    /// <summary>One file change to mirror into Studio's list.</summary>
    public sealed class ListChange
    {
        public string OldPath;   // file as Studio knows it
        public string NewPath;   // set for renamed / moved / added files
        public string NewName;   // set when the card's name changed (or for an added file)
        public bool Removed;
        public bool Added;
    }
}
