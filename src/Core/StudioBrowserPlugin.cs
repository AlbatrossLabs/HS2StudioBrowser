using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using HS2StudioBrowser.Game;
using HS2StudioBrowser.UI;
using KKAPI.Studio;
using UnityEngine;

namespace HS2StudioBrowser
{
    public enum SortBy
    {
        Name,
        FileName,
        Newest,
        Oldest,
    }

    /// <summary>Order of catalog items (animations).</summary>
    public enum ItemSort
    {
        StudioOrder,
        Name,
    }

    /// <summary>
    /// The shared plugin behaviour: settings, hotkey, toolbar button, opening and closing with Studio's own list,
    /// standing in for that list, and stepping aside while Camera Sweep picks files with it. A browser plugin
    /// derives from this, names itself with [BepInPlugin], and supplies its <see cref="BrowserKind"/> and icon.
    /// </summary>
    public abstract class StudioBrowserPlugin : BaseUnityPlugin
    {
        public ConfigEntry<KeyboardShortcut> WindowHotkey;
        public ConfigEntry<bool> OpenWithStudioList;
        public ConfigEntry<bool> ReplaceStudioList;
        public ConfigEntry<int> ThumbnailWidth;
        public ConfigEntry<bool> IncludeSubfolders;
        public ConfigEntry<bool> ShowFileNames;
        public ConfigEntry<SortBy> Sort;
        public ConfigEntry<bool> RenameFileToo;
        public ConfigEntry<bool> ConfirmRecycle;
        public ConfigEntry<int> CacheSize;
        public ConfigEntry<bool> CheckMods;
        public ConfigEntry<bool> ShowFolders;
        public ConfigEntry<bool> ShowDetails;
        public ConfigEntry<bool> StayOpenAfterAdd;
        public ConfigEntry<string> OpenFolders;
        public ConfigEntry<string> WindowRect;
        public ConfigEntry<string> LastFolderFemale;
        public ConfigEntry<string> LastFolderMale;

        // Catalog browsers (animations) only.
        public ConfigEntry<ItemSort> ItemSortBy;
        public ConfigEntry<bool> PlayOnClick;
        public ConfigEntry<bool> GridView;
        public ConfigEntry<bool> NamesInStudio;
        public ConfigEntry<bool> HideMarked;
        public ConfigEntry<int> ItemThumbSize;

        public StudioListKind Kind { get; private set; }

        /// <summary>Is the browser window showing?</summary>
        public bool WindowVisible => _window != null && _window.Visible;

        /// <summary>Hides the browser window (as if the user closed it, without closing Studio's list).</summary>
        public void HideWindow()
        {
            _window?.Hide();
            _autoOpened = false;
        }

        private IBrowserWindow _window;
        private Harmony _harmony;
        private bool _listWasOpen;
        private bool _autoOpened;
        private bool _hiddenForPick;

        protected abstract StudioListKind CreateKind();
        protected abstract Texture2D CreateIcon();
        protected abstract KeyboardShortcut DefaultHotkey { get; }

        /// <summary>Where Studio's list is, for the settings' wording: "Manipulate > character > Costume".</summary>
        protected abstract string StudioListWhere { get; }

        /// <summary>An older plugin's settings file to carry over on first run (file name in BepInEx\config), or null.</summary>
        protected virtual string LegacyConfigFile => null;

        /// <summary>An older favorites file to carry over on first run (file name in BepInEx\config), or null.</summary>
        protected virtual string LegacyFavoritesFile => null;

        /// <summary>Recently used items (catalog browsers): BepInEx\config\HS2StudioBrowser.&lt;id&gt;.recent.txt.</summary>
        public string RecentFile => Path.Combine(Paths.ConfigPath, $"HS2StudioBrowser.{Kind.Id}.recent.txt");

        /// <summary>Tags: BepInEx\config\HS2StudioBrowser.&lt;id&gt;.tags.txt; the tag list: .tagset.txt.</summary>
        public string TagsFile => Path.Combine(Paths.ConfigPath, $"HS2StudioBrowser.{Kind.Id}.tags.txt");
        public string TagSetFile => Path.Combine(Paths.ConfigPath, $"HS2StudioBrowser.{Kind.Id}.tagset.txt");
        public static string SharedTagSetFile => Path.Combine(Paths.ConfigPath, "HS2StudioBrowser.shared.tagset.txt");

        /// <summary>The kind's tag list, with the shared groups added.</summary>
        internal Files.TagSet CreateTagSet()
        {
            if (Kind.UsesSharedTags) TagSets.MigrateShared(SharedTagSetFile);
            return new Files.TagSet(TagSetFile, Kind.DefaultTagSet, Kind.OlderTagSets,
                Kind.UsesSharedTags ? new Files.TagSet(SharedTagSetFile, TagSets.Shared) : null);
        }

        /// <summary>Your names for catalog items, and items marked for deletion.</summary>
        public string NamesFile => Path.Combine(Paths.ConfigPath, $"HS2StudioBrowser.{Kind.Id}.names.txt");
        public string MarkedFile => Path.Combine(Paths.ConfigPath, $"HS2StudioBrowser.{Kind.Id}.marked.txt");

        public string FavoritesFile => Path.Combine(Paths.ConfigPath, $"HS2StudioBrowser.{Kind.Id}.favorites.txt");

        /// <summary>Takes the value saved under a setting's old name (now an orphan in the .cfg) and drops the old line.</summary>
        private void CarryOver<T>(ConfigEntry<T> entry, string section, string oldKey)
        {
            try
            {
                var orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config, null) as IDictionary<ConfigDefinition, string>;
                var def = new ConfigDefinition(section, oldKey);
                if (orphans == null || !orphans.TryGetValue(def, out var value)) return;
                entry.SetSerializedValue(value);
                orphans.Remove(def);
                Config.Save();
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug($"Couldn't carry over \"{oldKey}\": {ex.Message}");
            }
        }

        protected virtual void Awake()
        {
            Kind = CreateKind();
            Migrate();

            var k = Kind;
            WindowHotkey = Config.Bind("General", "Toggle window", DefaultHotkey,
                $"Shows or hides the {k.Title}. Its icon on Studio's left toolbar does the same.");
            OpenWithStudioList = Config.Bind("General", $"Open with the {k.StudioListName}", true,
                $"Show the browser whenever Studio's {k.StudioListName} is open ({StudioListWhere}), and hide it again when the list closes.");
            ReplaceStudioList = Config.Bind("General", $"Replace the {k.StudioListName}", true,
                $"Opening the {k.StudioListName} shows only the {k.Title}: Studio's own list (and Browser Folders' window for it) stays hidden, and closing the browser closes the list. " +
                $"Turn off to see both side by side. While Camera Sweep is picking {k.Nouns}, Studio's list is shown as normal.");
            WindowRect = Config.Bind("Window", "Position", "", new ConfigDescription("Saved window position and size.", null, "Advanced"));
            if (k.AddsToScene)
            {
                StayOpenAfterAdd = Config.Bind("General", "Stay open after adding", true,
                    $"Adding a {k.Noun} from the browser keeps Studio's Add menu (and so the browser) open, even with Studio's option to close the menu after adding on. " +
                    "Off: Studio's option decides, as in its own list. Adding from Studio's own lists is never changed.");
                k.KeepAddMenuOpen = () => StayOpenAfterAdd.Value;
            }
            ShowDetails = Config.Bind("View", "Show details panel", true,
                $"Show the selected {k.Noun}'s picture, name and tags on the right, where tags can be set by clicking them (the chevron at its top folds it away). It folds itself away when the window is too narrow for it.");
            foreach (var old in k.OldStudioListNames)
            {
                CarryOver(OpenWithStudioList, "General", $"Open with the {old}");
                CarryOver(ReplaceStudioList, "General", $"Replace the {old}");
            }

            if (k is BrowserKind bk)
            {
                ConfirmRecycle = Config.Bind("General", "Ask before Recycle Bin", true,
                    $"Ask before sending {k.Nouns} or folders to the Recycle Bin. Nothing is ever deleted outright.");
                RenameFileToo = Config.Bind("General", $"Rename the file with the {k.Noun}", true,
                    $"In the Rename box, keep the file name in step with the {k.Noun} name as you type (you can still edit it separately).");

                ThumbnailWidth = Config.Bind("View", "Thumbnail width", bk.DefaultThumbWidth,
                    new ConfigDescription($"Width of each {k.Noun} picture in pixels (also the slider in the window).", new AcceptableValueRange<int>(60, bk.MaxThumbWidth)));
                IncludeSubfolders = Config.Bind("View", "Include subfolders", false,
                    $"Also show {k.Nouns} from the folders inside the selected one.");
                ShowFileNames = Config.Bind("View", "Show file names", true,
                    $"Show the file name under the {k.Noun} name when they differ.");
                Sort = Config.Bind("View", "Sort by", bk.DefaultSort, $"Order of the {k.Nouns}.");
                CacheSize = Config.Bind("View", "Thumbnails kept in memory", 300,
                    new ConfigDescription("Pictures kept loaded while the window is open (about 0.35 MB each). All are freed when it closes.",
                        new AcceptableValueRange<int>(50, 2000)));
                CheckMods = Config.Bind("View", "Check for missing zipmods", true,
                    $"Mark {k.Nouns} that use zipmods you don't have (red badge), with a filter and a list of what's missing. Checked in the background; needs Sideloader. Changes take effect on restart.");
                ShowFolders = Config.Bind("View", "Show folder panel", true, "Show the folder tree on the left (the chevron at its top folds it away).");

                OpenFolders = Config.Bind("Window", "Open folders", "", new ConfigDescription("Folders expanded in the tree.", null, "Advanced"));
                LastFolderFemale = Config.Bind("Window", "Last female folder", "", new ConfigDescription("", null, "Advanced"));
                LastFolderMale = Config.Bind("Window", "Last male folder", "", new ConfigDescription("", null, "Advanced"));
            }
            else
            {
                var ik = (ItemKind)k;
                var verb = ik.Verb.ToLowerInvariant();
                PlayOnClick = Config.Bind("General", $"{ik.Verb} on click", ik.DefaultRunOnClick,
                    ik.RunOnStep
                        ? $"Clicking a {k.Noun} (or stepping to one with the arrow keys) {verb}s it right away, as Studio's list does. Off: double-click or Enter {verb}s."
                        : $"Clicking a {k.Noun} {verb}s it right away, as Studio's list does. Off: double-click or Enter {verb}s. The arrow keys only move through the list.");
                ItemSortBy = Config.Bind("View", "Sort by", ItemSort.StudioOrder, $"Order of the {k.Nouns}: as Studio lists them, or by name. Favorites always come first.");
                ShowFolders = Config.Bind("View", "Show group panel", true, "Show the groups and categories on the left (the chevron at its top folds it away).");
                OpenFolders = Config.Bind("Window", "Open groups", "", new ConfigDescription("Groups expanded in the panel.", null, "Advanced"));
                NamesInStudio = Config.Bind("General", "Use my names in Studio", true,
                    $"Names you give {k.Nouns} here (F2) also show in Studio's own lists while the game runs. The zipmods are never edited; off shows the original names there.");
                HideMarked = Config.Bind("View", "Hide marked for deletion", true,
                    $"{k.Nouns.Substring(0, 1).ToUpper() + k.Nouns.Substring(1)} marked for deletion (Del) only show under *Marked for deletion*. Off: they show everywhere, marked in red.");
                if (ik.HasPictures)
                {
                    GridView = Config.Bind("View", "Show as grid", true, $"Show the {k.Nouns} as a grid of pictures (off: a list of names).");
                    ItemThumbSize = Config.Bind("View", "Picture size", 96,
                        new ConfigDescription($"Size of each {k.Noun}'s picture in the grid, in pixels (also the slider in the window).", new AcceptableValueRange<int>(56, 200)));
                }
            }

            _harmony = new Harmony(Info.Metadata.GUID);
            InputGuard.Install();
            if (k is BrowserKind fileKind) _window = new BrowserWindow(this, fileKind);
            else if (k is ItemKind itemKind) _window = new ItemWindow(this, itemKind);
            else throw new InvalidOperationException($"{k.GetType().Name} is neither a BrowserKind nor an ItemKind.");
        }

        /// <summary>First run after a rename of the plugin: copy the old settings and favorites over.</summary>
        private void Migrate()
        {
            try
            {
                if (LegacyConfigFile != null && !File.Exists(Config.ConfigFilePath))
                {
                    var old = Path.Combine(Paths.ConfigPath, LegacyConfigFile);
                    if (File.Exists(old))
                    {
                        File.Copy(old, Config.ConfigFilePath);
                        Config.Reload();
                        Logger.LogInfo($"Settings carried over from {LegacyConfigFile}");
                    }
                }
                if (LegacyFavoritesFile != null && !File.Exists(FavoritesFile))
                {
                    var old = Path.Combine(Paths.ConfigPath, LegacyFavoritesFile);
                    if (File.Exists(old))
                    {
                        File.Copy(old, FavoritesFile);
                        Logger.LogInfo($"Favorites carried over from {LegacyFavoritesFile}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Couldn't carry over old settings: " + ex.Message);
            }
        }

        protected virtual void Start()
        {
            // Every plugin is loaded by now, so hooks into other plugins can find their classes.
            Kind.Install(_harmony);
            ToolbarButton.Add(this, Kind.Id, Kind.Title, CreateIcon(), () => _window.Toggle(StudioTargets.Characters()));
        }

        protected virtual void Update()
        {
            if (!StudioAPI.StudioLoaded) return;

            if (WindowHotkey.Value.IsDown())
            {
                _window.Toggle(StudioTargets.Characters());
                _autoOpened = false;
            }

            AddMenuHold.Tick();
            var open = Kind.StudioListOpen(out var listSex);
            var picking = CameraSweepBridge.Picking();
            var replace = ReplaceStudioList.Value && !picking;

            // Camera Sweep picks files with Studio's own lists: step aside while it does.
            if (picking && _window.Visible && open)
            {
                _window.Hide();
                _hiddenForPick = true;
            }
            else if (!picking && _hiddenForPick)
            {
                _hiddenForPick = false;
                if (open)
                {
                    _window.Show(listSex);
                    _autoOpened = true;
                }
            }

            if ((OpenWithStudioList.Value || ReplaceStudioList.Value) && !picking)
            {
                if (open && !_listWasOpen && !_window.Visible)
                {
                    _window.Show(listSex);
                    _autoOpened = true;
                }
                else if (!open && _listWasOpen && _autoOpened)
                {
                    _window.Hide();
                    _autoOpened = false;
                }
                else if (open && _window.Visible)
                {
                    _window.FollowSex(listSex);
                }
            }
            _listWasOpen = open;

            Kind.OnUpdate(replace && open);
            Kind.SetStudioListHidden(replace && open && _window.Visible);

            _window.Update();
        }

        protected virtual void OnGUI()
        {
            if (!StudioAPI.StudioLoaded) return;
            _window.Draw();
        }

        protected virtual void OnDestroy()
        {
            _window?.Dispose();
            Kind?.Restore();
        }

        /// <summary>The user closed the window: if it was standing in for Studio's list, close the list too.</summary>
        internal void ClosedByUser()
        {
            _autoOpened = false;
            if (ReplaceStudioList.Value && !CameraSweepBridge.Picking() && Kind.StudioListOpen(out _))
                Kind.CloseStudioList();
        }
    }
}
