using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.Game;
using HS2StudioBrowser.UI;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Animations
{
    /// <summary>Studio's animations (game and zipmods), played on the selected characters; stands in for the Anime panel's lists.</summary>
    internal sealed class AnimationKind : ItemKind
    {
        private readonly List<CardAction> _actions;
        private readonly Dictionary<string, AnimationTable.Ids> _ids = new Dictionary<string, AnimationTable.Ids>();
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        private readonly AnimationBrowserPlugin _plugin;

        private static readonly List<TagFacet> Auto = new List<TagFacet>
        {
            MakeFacet("Length", "Pose", "Under 2 s", "2-10 s", "Over 10 s"),
            MakeFacet("Playback", "Loop", "Once"),
            MakeFacet("Source", "Base game", "Mod"),
        };

        private static TagFacet MakeFacet(string name, params string[] values)
        {
            var f = new TagFacet { Name = name, Single = true };
            f.Values.AddRange(values);
            return f;
        }

        public AnimationKind(AnimationBrowserPlugin plugin)
        {
            _plugin = plugin;
            _actions = new List<CardAction>
            {
                new CardAction
                {
                    Label = "Play", Primary = true, Width = 80,
                    Tooltip = "Play this animation on the selected characters (click, double-click or Enter does the same)",
                    Run = Play,
                },
            };
        }

        public override string DefaultTagSet => TagSets.Animations;
        public override string[] OlderTagSets => new[] { TagSets.AnimationsV1, TagSets.AnimationsV2 };
        public override string[] OwnListPrefixes => new[] { "Anime" };

        public override void ShowNameInStudio(CatalogItem item, string name)
        {
            if (!(item.Tag is AnimationTable.Ids ids)) return;
            var t = AnimationTable.Full();
            if (t.TryGetValue(ids.Group, out var cats) && cats.TryGetValue(ids.Category, out var anims) && anims.TryGetValue(ids.No, out var info))
                StudioNames.Apply(item.Key, info, name);
        }

        public override string Id => "animations";
        public override string Title => "Animation Browser";
        public override string Noun => "animation";
        public override string Nouns => "animations";
        public override string StudioListName => "Anime list";
        public override IList<CardAction> Actions => _actions;

        public override List<ItemGroup> BuildCatalog()
        {
            var catalog = AnimationTable.Build();
            _ids.Clear();
            _names.Clear();
            foreach (var g in catalog)
            foreach (var c in g.Categories)
            foreach (var i in c.Items)
            {
                if (i.Tag is AnimationTable.Ids ids && !_ids.ContainsKey(i.Key))
                {
                    _ids[i.Key] = ids;
                    _names[i.Key] = i.Name;
                }
            }
            StartMeasuring();
            return catalog;
        }

        // ---------------------------------------------------------------- lengths

        private void StartMeasuring()
        {
            if (!_plugin.MeasureLengths.Value || AnimationLengths.Measuring) return;
            var table = AnimationTable.Full();
            var todo = new List<KeyValuePair<string, Info.AnimeLoadInfo>>();
            foreach (var kv in _ids)
            {
                if (AnimationLengths.Get(kv.Key) != null) continue;
                if (table.TryGetValue(kv.Value.Group, out var cats) && cats.TryGetValue(kv.Value.Category, out var anims) && anims.TryGetValue(kv.Value.No, out var info))
                    todo.Add(new KeyValuePair<string, Info.AnimeLoadInfo>(kv.Key, info));
            }
            if (todo.Count > 0) _plugin.StartCoroutine(AnimationLengths.MeasureAll(todo));
        }

        /// <summary>After an animation starts on a character, read its real length from that character (better than the probe's).</summary>
        private IEnumerator MeasureOnCharacter(string key, OCIChar c)
        {
            yield return null;
            yield return null;
            try
            {
                var m = AnimationLengths.Read(c?.charAnimeCtrl?.animator);
                if (m != null) AnimationLengths.Set(key, m);
            }
            catch { /* character gone */ }
        }

        public override int AutoVersion => AnimationLengths.Version;
        public override IList<TagFacet> AutoFacets => Auto;

        public override IEnumerable<string> AutoTags(CatalogItem item)
        {
            if (item.Tag is AnimationTable.Ids ids) yield return "Source:" + (ids.Mod ? "Mod" : "Base game");
            var m = AnimationLengths.Get(item.Key);
            if (m == null) yield break;
            yield return "Length:" + m.Bucket;
            if (!m.IsPose) yield return "Playback:" + (m.Loop ? "Loop" : "Once");
        }

        public override string Badge(CatalogItem item)
        {
            var m = AnimationLengths.Get(item.Key);
            if (m == null) return null;
            return m.IsPose ? $"<color={Theme.WarnHex}>pose</color>" : $"<color={Theme.TextHex}>{m.Text}</color>" + (m.Loop ? "" : $"<color={Theme.MutedHex}> · once</color>");
        }

        private readonly AnimeControls _controls = new AnimeControls();

        public override bool HasControls => _plugin.ShowControls.Value;
        public override void DrawControls() => _controls.Draw(_plugin);

        public override void OnUpdate(bool replacing)
        {
            AnimationLengths.Tick();
            AnimePanel.HideStudioControls = _plugin.ShowControls.Value;
        }

        public override string TargetLine()
        {
            var t = AnimePanel.Targets();
            var measuring = AnimationLengths.Measuring ? Theme.Hint($"  ·  measuring lengths {AnimationLengths.Done} / {AnimationLengths.Total}") : "";
            if (t.Count == 0) return Theme.Warn("Select a character in the workspace to animate") + measuring;
            if (t.Count == 1) return Theme.Hint("Plays on ") + StudioTargets.NameOf(t[0]) + measuring;
            var pairing = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(AnimePanel.PairingGuid);
            return Theme.Hint("Plays on ") + $"{t.Count} characters" + (pairing ? Theme.Hint("  ·  Animation Pairing picks each one's partner animation") : "") + measuring;
        }

        private ActionResult Play(string key, int sex)
        {
            if (!_ids.TryGetValue(key, out var ids)) return ActionResult.Fail("That animation is gone; press Refresh.");
            var targets = AnimePanel.Targets();
            if (targets.Count == 0) return ActionResult.Fail("Select a character in the workspace first.");
            var done = AnimePanel.Play(ids.Group, ids.Category, ids.No, targets);
            var name = _names.TryGetValue(key, out var n) ? n : "animation";
            if (done == 0) return ActionResult.Fail($"Studio couldn't play {name}.");
            _plugin.StartCoroutine(MeasureOnCharacter(key, targets[0]));
            var who = targets.Count == 1 ? StudioTargets.NameOf(targets[0]) : $"{done} of {targets.Count} characters";
            return ActionResult.Ok($"Playing {name} on {who}");
        }

        public override bool StudioListOpen(out int sex)
        {
            sex = -1;
            return AnimePanel.Open;
        }

        public override void SetStudioListHidden(bool hidden) => AnimePanel.SetHidden(hidden);
        public override void CloseStudioList() => AnimePanel.Close();
        public override void Restore() => AnimePanel.Restore();
    }
}
