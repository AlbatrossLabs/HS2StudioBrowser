using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.Game;
using HS2StudioBrowser.UI;

namespace HS2StudioBrowser.Outfits
{
    /// <summary>Outfit (coordinate) cards in UserData\coordinate, loaded onto characters, standing in for Studio's Costume list.</summary>
    internal sealed class OutfitKind : BrowserKind
    {
        private readonly List<CardAction> _actions;

        public OutfitKind()
        {
            _actions = new List<CardAction>
            {
                new CardAction
                {
                    Label = "Clothes only", MenuLabel = "Load clothes only", Width = 96,
                    Tooltip = "Keep their accessories, take the clothes from this outfit",
                    CanRun = HasTarget, Run = (path, sex) => Load(path, sex, LoadMode.ClothesOnly),
                },
                new CardAction
                {
                    Label = "Accessories only", MenuLabel = "Load accessories only", Width = 118,
                    Tooltip = "Keep their clothes, take the accessories from this outfit",
                    CanRun = HasTarget, Run = (path, sex) => Load(path, sex, LoadMode.AccessoriesOnly),
                },
                new CardAction
                {
                    Label = "Load", Primary = true, Width = 80,
                    Tooltip = "Load the whole outfit (double-click or Enter does the same)",
                    CanRun = HasTarget, Run = (path, sex) => Load(path, sex, LoadMode.Full),
                },
            };
        }

        public override string DefaultTagSet => TagSets.Outfits;
        public override string[] OlderTagSets => new[] { TagSets.OutfitsV1 };

        private static readonly List<TagFacet> Auto = new List<TagFacet>
        {
            AutoFacet.Make("Parts", false, CoordinateCard.Slots),
            AutoFacet.Make("Accessories", true, "None", "1-5", "6-15", "16+"),
        };

        public override IList<TagFacet> AutoFacets => Auto;

        public override IEnumerable<string> ReadAutoTags(string path)
        {
            var r = CoordinateCard.ReadParts(path);
            if (r == null) yield break;
            foreach (var part in r.Value.Key) yield return "Parts:" + part;
            var n = r.Value.Value;
            yield return "Accessories:" + (n == 0 ? "None" : n <= 5 ? "1-5" : n <= 15 ? "6-15" : "16+");
        }
        public override string Id => "outfits";
        public override string Title => "Outfit Browser";
        public override string Noun => "outfit";
        public override string Nouns => "outfits";
        public override string UserDataFolder => "coordinate";
        public override string StudioListName => "Costume list";
        public override string NameLabel => "Outfit name " + Theme.Hint("(what Studio's Costume list shows)");

        public override string ReadName(string path) => CoordinateCard.ReadName(path);
        public override void WriteName(string path, string newName) => CoordinateCard.SetName(path, newName);
        public override byte[] ReadPluginData(string path) => CoordinateCard.ReadPluginData(path);

        public override IList<CardAction> Actions => _actions;

        private static bool HasTarget(int sex) => CostumeList.TargetCharacters().Any(t => t.oiCharInfo.sex == sex);

        private static ActionResult Load(string path, int sex, LoadMode mode)
        {
            var msg = OutfitLoader.Load(path, sex, CostumeList.TargetCharacters(), mode);
            return new ActionResult { Message = msg, Error = msg.StartsWith("Nothing") || msg.Contains("Failed") };
        }

        public override string TargetLine(int sex)
        {
            var targets = CostumeList.TargetCharacters();
            var matching = targets.Count(t => t.oiCharInfo.sex == sex);
            if (targets.Count == 0) return Theme.Warn("Select a character to load outfits onto");
            if (matching == 0) return Theme.Warn($"Selected character is {(sex == 1 ? "male" : "female")}: switch to {(sex == 1 ? "Male" : "Female")} above");
            if (targets.Count == 1) return Theme.Hint("Loads onto ") + StudioTargets.NameOf(targets[0]);
            return Theme.Hint("Loads onto ") + $"{matching} characters";
        }

        public override bool StudioListOpen(out int sex)
        {
            sex = -1;
            if (!CostumeList.CostumeListVisible()) return false;
            var c = CostumeList.PanelCharacter();
            sex = c != null ? c.oiCharInfo.sex : -1;
            return true;
        }

        public override void SetStudioListHidden(bool hidden) => CostumeOverride.SetStudioListHidden(hidden);
        public override void CloseStudioList() => CostumeList.CloseCostumePanel();

        public override void SyncStudioList(IList<ListChange> changes)
        {
            if (!CostumeList.SyncCostumeList(changes)) _refreshWhenVisible = true;
        }

        private bool _refreshWhenVisible;

        public override void Install(Harmony harmony) => CostumeOverride.Install(harmony);

        public override void OnUpdate(bool replacing)
        {
            CostumeOverride.HideBrowserFolders = replacing;
            if (_refreshWhenVisible && CostumeList.CostumeListVisible())
            {
                _refreshWhenVisible = false;
                CostumeList.RefreshCostumeList();
            }
        }

        public override void Restore() => CostumeOverride.Restore();
    }
}
