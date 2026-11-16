using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.Game;
using HS2StudioBrowser.UI;
using Studio;

namespace HS2StudioBrowser.Characters
{
    /// <summary>Character cards in UserData\chara, added to the scene or swapped in, standing in for Studio's character lists.</summary>
    internal sealed class CharacterKind : BrowserKind
    {
        private readonly List<CardAction> _actions;
        private static bool _hideBrowserFolders;

        public CharacterKind()
        {
            _actions = new List<CardAction>
            {
                new CardAction
                {
                    Label = "Change", MenuLabel = "Change selected character", Width = 80,
                    Tooltip = "Swap the selected character(s) in the workspace for this one, keeping their place and pose",
                    CanRun = sex => Selected(sex).Count > 0,
                    Run = Change,
                },
                new CardAction
                {
                    Label = "Load", MenuLabel = "Load (add to scene)", Primary = true, Width = 80,
                    Tooltip = "Add this character to the scene (double-click or Enter does the same)",
                    Run = Add,
                },
            };
        }

        public override string DefaultTagSet => TagSets.Characters;
        public override string[] OlderTagSets => new[] { TagSets.CharactersV1 };

        private static readonly List<TagFacet> Auto = new List<TagFacet>
        {
            AutoFacet.Make("Height", true, "Short", "Average", "Tall"),
            AutoFacet.Make("Bust", true, "Small", "Medium", "Large"),
        };

        public override IList<TagFacet> AutoFacets => Auto;

        /// <summary>Height and bust from the body sliders (the maker's 0-100, in thirds); bust for female characters only.</summary>
        public override IEnumerable<string> ReadAutoTags(string path)
        {
            var b = CharaCard.ReadBody(path);
            if (b == null) yield break;
            if (!double.IsNaN(b.Height)) yield return "Height:" + (b.Height < HeightLow ? "Short" : b.Height > HeightHigh ? "Tall" : "Average");
            if (b.Sex == 1 && !double.IsNaN(b.Bust)) yield return "Bust:" + (b.Bust < BustLow ? "Small" : b.Bust > BustHigh ? "Large" : "Medium");
        }

        internal const double HeightLow = 0.35, HeightHigh = 0.65, BustLow = 0.35, BustHigh = 0.65;
        public override string Id => "characters";
        public override string Title => "Character Browser";
        public override string Noun => "character";
        public override string Nouns => "characters";
        public override string UserDataFolder => "chara";
        public override string StudioListName => "Character list";
        public override string NameLabel => "Character name " + Theme.Hint("(what Studio's character list shows)");

        public override string ReadName(string path) => CharaCard.ReadName(path);
        public override void WriteName(string path, string newName) => CharaCard.SetName(path, newName);
        public override byte[] ReadPluginData(string path) => CharaCard.ReadPluginData(path);

        public override IList<CardAction> Actions => _actions;

        private static List<OCIChar> Selected(int sex) => StudioTargets.Characters().Where(c => c.oiCharInfo.sex == sex).ToList();

        public override bool AddsToScene => true;

        private ActionResult Add(string path, int sex)
        {
            if (!File.Exists(path)) return ActionResult.Fail("That file is gone.");
            try
            {
                AddMenuHold.Run(KeepAddMenuOpen(), () =>
                {
                    if (sex == 0) Studio.Studio.Instance.AddMale(path);
                    else Studio.Studio.Instance.AddFemale(path);
                    return 0;
                });
                return ActionResult.Ok($"Added {CharaCard.ReadName(path) ?? Path.GetFileNameWithoutExtension(path)} to the scene.");
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Adding {Path.GetFileName(path)} failed: {ex}");
                return ActionResult.Fail("Couldn't add the character: " + ex.Message);
            }
        }

        private static ActionResult Change(string path, int sex)
        {
            if (!File.Exists(path)) return ActionResult.Fail("That file is gone.");
            var targets = Selected(sex);
            if (targets.Count == 0) return ActionResult.Fail($"Select a {(sex == 0 ? "male" : "female")} character in the workspace to change.");
            var done = 0;
            var errors = new List<string>();
            foreach (var oci in targets)
            {
                try
                {
                    oci.ChangeChara(path);
                    done++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{StudioTargets.NameOf(oci)}: {ex.Message}");
                    Log.Source.LogWarning($"Changing {StudioTargets.NameOf(oci)} to {Path.GetFileName(path)} failed: {ex}");
                }
            }
            var name = CharaCard.ReadName(path) ?? Path.GetFileNameWithoutExtension(path);
            var msg = done == 1 ? $"Changed to {name}." : $"Changed {done} characters to {name}.";
            if (errors.Count > 0) return ActionResult.Fail(msg + " Failed: " + string.Join("; ", errors.ToArray()));
            return ActionResult.Ok(msg);
        }

        public override string TargetLine(int sex)
        {
            var all = StudioTargets.Characters();
            var same = all.Where(c => c.oiCharInfo.sex == sex).ToList();
            var load = Theme.Hint("Load adds a new character");
            if (same.Count == 1) return load + Theme.Hint("  ·  Change replaces ") + StudioTargets.NameOf(same[0]);
            if (same.Count > 1) return load + Theme.Hint("  ·  Change replaces ") + $"{same.Count} characters";
            if (all.Count > 0) return load + Theme.Hint($"  ·  select a {(sex == 1 ? "female" : "male")} character to Change");
            return load + Theme.Hint("  ·  select one in the workspace to Change it");
        }

        public override bool StudioListOpen(out int sex)
        {
            var list = CharaLists.Open();
            sex = list != null ? CharaLists.SexOf(list) : -1;
            return list != null;
        }

        public override void SetStudioListHidden(bool hidden) => CharaLists.SetHidden(hidden ? CharaLists.Open() : null);
        public override void CloseStudioList() => CharaLists.Close(CharaLists.Open());
        public override void SyncStudioList(IList<ListChange> changes) => CharaLists.Sync(changes);

        public override void Install(Harmony harmony)
        {
            // Browser Folders' "Character folder" window, when that part of it is turned on.
            try
            {
                var t = AccessTools.TypeByName("BrowserFolders.StudioCharaFolders");
                var onGui = t != null ? AccessTools.Method(t, "OnGui") : null;
                if (onGui != null) harmony.Patch(onGui, prefix: new HarmonyMethod(typeof(CharacterKind), nameof(BrowserFoldersOnGuiPrefix)));
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Browser Folders hook skipped: " + ex.Message);
            }
        }

        private static bool BrowserFoldersOnGuiPrefix() => !_hideBrowserFolders;

        public override void OnUpdate(bool replacing) => _hideBrowserFolders = replacing;

        public override void Restore()
        {
            _hideBrowserFolders = false;
            CharaLists.Restore();
        }
    }
}
