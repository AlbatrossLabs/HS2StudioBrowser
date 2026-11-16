using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.Game;
using HS2StudioBrowser.UI;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Poses
{
    /// <summary>
    /// Studio poses in UserData\studio\pose: Pose Tools .png poses (with a picture) and, optionally, Studio's
    /// own .dat poses (without one). Loads onto the selected characters, saves new poses into the open folder,
    /// and can give a pose a picture from the current view. Stands in for Studio's Pose list.
    /// </summary>
    internal sealed class PoseKind : BrowserKind
    {
        private readonly PoseBrowserPlugin _plugin;
        private readonly List<CardAction> _actions;
        private string _folder;
        private string _saveName = "";
        private string _note;
        private bool _noteError;
        private float _noteUntil;
        private bool _busy;

        // The pose this browser last put on characters, so Picture can capture it as it stands (with any
        // camera or bone tweaks) instead of loading it again.
        private string _lastLoaded;

        // Each character's pose from before the first pose the browser put on it (so Undo can put it back).
        // Forgotten when the browser closes or after Undo.
        private readonly Dictionary<OCIChar, PauseCtrl.FileInfo> _before = new Dictionary<OCIChar, PauseCtrl.FileInfo>();
        private bool _wasVisible;
        private List<OCIChar> _lastTargets = new List<OCIChar>();

        private static readonly string[] PngOnly = { "*.png" };
        private static readonly string[] PngAndDat = { "*.png", "*.dat" };
        private static readonly string[] DatOnly = { "*.dat" };

        /// <summary>"No picture" in the top bar: only poses without a picture (.dat), from every subfolder.</summary>
        private bool _onlyNoPicture;

        public PoseKind(PoseBrowserPlugin plugin)
        {
            _plugin = plugin;
            _actions = new List<CardAction>
            {
                new CardAction
                {
                    Label = "Picture", MenuLabel = "Make picture from the view", Width = 72,
                    Tooltip = "Puts this pose on the selected character and saves what Studio's camera sees as its picture. A .dat becomes a .png (the .dat goes to the Recycle Bin).",
                    CanRun = _ => !_busy,
                    Run = Picture,
                },
                new CardAction
                {
                    Label = "Undo", MenuLabel = "Put back the pose from before", Width = 56, Key = "Backspace",
                    Tooltip = "Put the characters back in the pose they had before you started trying poses here (Backspace)",
                    CanRun = _ => _before.Count > 0,
                    Run = (p, s) => Undo(),
                },
                new CardAction
                {
                    Label = "Load", Primary = true, Width = 80,
                    Tooltip = "Put this pose on the selected characters (double-click or Enter does the same)",
                    CanRun = _ => !_busy,
                    Run = Load,
                },
            };
        }

        public override string DefaultTagSet => TagSets.Poses;
        public override string[] OlderTagSets => new[] { TagSets.PosesV1 };

        private static readonly List<TagFacet> Auto = new List<TagFacet>
        {
            AutoFacet.Make("Picture", true, "Has picture", "No picture"),
            AutoFacet.Make("Rig", false, "IK", "FK body", "Hands", "Hands only"),
        };

        public override IList<TagFacet> AutoFacets => Auto;

        public override IEnumerable<string> ReadAutoTags(string path)
        {
            yield return "Picture:" + (PoseFile.IsDat(path) ? "No picture" : "Has picture");
            foreach (var r in PoseFile.ReadRig(path)) yield return "Rig:" + r;
        }
        public override string Id => "poses";
        public override string Title => "Pose Browser";
        public override string Noun => "pose";
        public override string Nouns => "poses";
        public override string UserDataFolder => Path.Combine("studio", "pose");
        public override string StudioListName => "Pose list";
        public override string NameLabel => "Pose name (what Studio's Pose list shows)";
        public override float ThumbAspect => 180f / 320f;
        public override bool HasSex => false;
        public override int DefaultThumbWidth => 160;
        public override int MaxThumbWidth => 360;
        public override bool ChecksMods => false;

        public override string[] FilePatterns => _onlyNoPicture ? DatOnly : _plugin.ShowDat.Value ? PngAndDat : PngOnly;
        public override bool ForceSubfolders => _onlyNoPicture;
        public override bool HasPicture(string path) => !PoseFile.IsDat(path);
        public override string NoPictureText(string path) => ".dat  ·  no picture";

        public override string ReadName(string path) => PoseFile.ReadName(path);
        public override DupeKey DuplicateKey(string path) => PoseFile.DuplicateKey(path);
        public override float NearTolerance => _plugin.NearTolerance.Value;
        public override void WriteName(string path, string newName) => PoseFile.SetName(path, newName);

        public override IList<CardAction> Actions => _actions;

        // ---------------------------------------------------------------- targets

        /// <summary>Characters selected in the workspace; if none, the one whose Pose list is open.</summary>
        private static List<OCIChar> Targets()
        {
            var list = StudioTargets.Characters();
            if (list.Count == 0)
            {
                var c = PoseList.Character;
                if (c != null && c.charInfo != null) list.Add(c);
            }
            return list;
        }

        private static string Describe(List<OCIChar> targets) =>
            targets.Count == 1 ? StudioTargets.NameOf(targets[0]) : $"{targets.Count} characters";

        public override string TargetLine(int sex)
        {
            if (_note != null && Time.unscaledTime < _noteUntil) return _noteError ? Theme.Error(_note) : _note;
            if (_busy) return Theme.Warn("Making the picture...");
            var t = Targets();
            return t.Count > 0 ? Theme.Hint("Poses ") + Describe(t) : Theme.Warn("Select a character in the workspace to pose");
        }

        private void Note(string text, bool error = false)
        {
            _note = text;
            _noteError = error;
            _noteUntil = Time.unscaledTime + 4f;
        }

        // ---------------------------------------------------------------- Load

        private ActionResult Load(string path, int sex)
        {
            if (!File.Exists(path)) return ActionResult.Fail("That file is gone.");
            var targets = Targets();
            if (targets.Count == 0) return ActionResult.Fail("Select a character in the workspace first.");
            var done = Apply(path, targets);
            if (done == 0) return ActionResult.Fail("Studio couldn't load that pose.");
            return ActionResult.Ok($"Posed {Describe(targets)}: {PoseFile.ReadName(path) ?? Path.GetFileNameWithoutExtension(path)}");
        }

        private int Apply(string path, List<OCIChar> targets)
        {
            var done = 0;
            foreach (var c in targets)
            {
                try
                {
                    if (!_before.ContainsKey(c)) _before[c] = new PauseCtrl.FileInfo(c);
                    if (PauseCtrl.Load(c, path)) done++;
                }
                catch (Exception ex)
                {
                    Log.Source.LogWarning($"Loading pose {Path.GetFileName(path)} onto {StudioTargets.NameOf(c)} failed: {ex}");
                }
            }
            if (done > 0)
            {
                _lastLoaded = path;
                _lastTargets = targets;
            }
            return done;
        }

        public override bool RunOnStep => _plugin.ApplyOnStep.Value;

        private ActionResult Undo()
        {
            if (_before.Count == 0) return ActionResult.Fail("Nothing to put back yet.");
            var n = 0;
            foreach (var kv in _before.ToList())
            {
                try
                {
                    if (kv.Key?.charInfo == null) continue; // deleted since
                    kv.Value.Apply(kv.Key);
                    n++;
                }
                catch (Exception ex)
                {
                    Log.Source.LogWarning("Putting the pose back failed: " + ex.Message);
                }
            }
            _before.Clear();
            _lastLoaded = null;
            return n > 0 ? ActionResult.Ok(n == 1 ? "Put the pose back." : $"Put {n} characters' poses back.") : ActionResult.Fail("Those characters are gone.");
        }

        public override void OnUpdate(bool replacing)
        {
            // Closing the browser keeps the pose you ended on: forget the "before".
            var visible = _plugin.WindowVisible;
            if (_wasVisible && !visible) _before.Clear();
            _wasVisible = visible;
        }

        // ---------------------------------------------------------------- Picture

        private ActionResult Picture(string path, int sex)
        {
            if (!File.Exists(path)) return ActionResult.Fail("That file is gone.");
            if (_busy) return ActionResult.Fail("Still making the last picture.");
            var targets = Targets();
            var posed = _lastLoaded != null && FileOps.Same(_lastLoaded, path) && _lastTargets.Count > 0 && _lastTargets.All(c => c != null && c.charInfo != null);
            if (!posed && targets.Count == 0) return ActionResult.Fail("Select a character in the workspace to pose for the picture.");
            _plugin.StartCoroutine(PictureRoutine(path, posed ? null : targets));
            return ActionResult.Ok("Making the picture...");
        }

        private IEnumerator PictureRoutine(string path, List<OCIChar> loadOnto)
        {
            _busy = true;
            if (loadOnto != null)
            {
                if (Apply(path, loadOnto) == 0)
                {
                    _busy = false;
                    Note("Studio couldn't load that pose.", true);
                    yield break;
                }
                // Let FK / IK settle the bones before the shot.
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
            }

            try
            {
                var png = Shot();
                var newPath = PoseFile.WriteWithPicture(path, png);
                if (!FileOps.Same(newPath, path))
                {
                    if (!FileOps.Recycle(new[] { path }))
                        Note($"Saved {Path.GetFileName(newPath)}, but the .dat couldn't go to the Recycle Bin.", true);
                    else Note("Saved the picture as " + Path.GetFileName(newPath));
                    if (_lastLoaded != null && FileOps.Same(_lastLoaded, path)) _lastLoaded = newPath;
                }
                else Note("New picture saved for " + Path.GetFileName(path));
                ReportReplaced(path, newPath);
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Making a picture for {Path.GetFileName(path)} failed: {ex}");
                Note("Couldn't save the picture: " + ex.Message, true);
            }
            finally
            {
                _busy = false;
            }
        }

        // ---------------------------------------------------------------- Save (footer)

        public override void DrawFooterExtras()
        {
            var e = Event.current;
            var focused = GUI.GetNameOfFocusedControl() == "sb_pose_save";
            if (focused && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                e.Use();
                Save();
            }

            GUILayout.Label(Theme.Hint("Save as"), Theme.RichSingle, GUILayout.ExpandWidth(false));
            GUI.SetNextControlName("sb_pose_save");
            _saveName = GUILayout.TextField(_saveName ?? "", GUILayout.Width(120));
            var was = GUI.enabled;
            GUI.enabled = was && _saveName.Trim().Length > 0;
            if (GUILayout.Button(new GUIContent("Save", "Save the selected character's pose into the open folder, with a picture of the current view"), GUILayout.Width(52)))
                Save();
            GUI.enabled = was;
            GUILayout.Space(14);
        }

        private void Save()
        {
            var name = (_saveName ?? "").Trim();
            if (name.Length == 0) return;
            var targets = Targets();
            if (targets.Count == 0)
            {
                Note("Select the character whose pose to save.", true);
                return;
            }
            if (targets.Count > 1)
            {
                Note("Select just one character to save its pose.", true);
                return;
            }

            var folder = _folder != null && Directory.Exists(_folder) ? _folder : Folder;
            if (!PoseList.SetToolsFolder(folder))
                Log.Source.LogWarning("Couldn't point Pose Tools at the open folder; the pose saves where Pose Tools is.");
            try
            {
                PauseCtrl.Save(targets[0], name);
                Note($"Saved \"{name}\"");
                _saveName = "";
                GUIUtility.keyboardControl = 0;
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning("Saving the pose failed: " + ex);
                Note("Saving failed: " + ex.Message, true);
            }
            RequestRescan();
            PoseList.Refresh();
        }

        // ---------------------------------------------------------------- top bar

        public override float TopBarExtrasWidth => 6 + Flow.Size(".dat", Theme.Skin.toggle) + Flow.Size("No picture", Theme.Skin.toggle) + Flow.Size("Try on", Theme.Skin.toggle);
        public override float FooterExtrasWidth => Flow.Size(Theme.Hint("Save as"), Theme.RichSingle) + Flow.Fixed(120, Theme.Skin.textField) + Flow.Fixed(52) + 14;

        /// <summary>
        /// A picture of what Studio's camera sees, at the chosen size; with Smooth edges, shot at twice the
        /// size and halved (each pixel the average of four), which cleans up jagged edges.
        /// </summary>
        private byte[] Shot()
        {
            PoseBrowserPlugin.Dimensions(_plugin.PictureSizeSetting.Value, out var w, out var h);
            var scale = _plugin.SharperPictures.Value ? 2 : 1;
            var png = Studio.Studio.Instance.gameScreenShot.CreatePngScreen(w * scale, h * scale, false, false);
            if (scale == 1 || png == null) return png;

            Texture2D big = null, small = null;
            RenderTexture rt = null;
            var was = RenderTexture.active;
            try
            {
                big = new Texture2D(2, 2, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                if (!big.LoadImage(png)) return png;
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(big, rt);
                RenderTexture.active = rt;
                small = new Texture2D(w, h, TextureFormat.RGB24, false);
                small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                small.Apply();
                return small.EncodeToPNG();
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning("Couldn't scale the picture down, saving it full size: " + ex.Message);
                return png;
            }
            finally
            {
                RenderTexture.active = was;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (big != null) UnityEngine.Object.Destroy(big);
                if (small != null) UnityEngine.Object.Destroy(small);
            }
        }

        public override bool DrawTopBarExtras()
        {
            GUILayout.Space(6);
            var dat = GUILayout.Toggle(_plugin.ShowDat.Value, new GUIContent(".dat", "Also show .dat poses (Studio's own format, without a picture). Picture gives one a picture and makes it a .png."), GUILayout.ExpandWidth(false));
            var only = GUILayout.Toggle(_onlyNoPicture, new GUIContent("No picture",
                "Show only the poses without a picture (.dat), from this folder and every folder in it (the folder counts show how many each has). " +
                "Select one, frame the camera and press Picture: it gets its picture, leaves this list, and the next one is selected. Arrow keys try each pose on."), GUILayout.ExpandWidth(false));
            var step = GUILayout.Toggle(_plugin.ApplyOnStep.Value, new GUIContent("Try on", "Arrow keys put each pose on the selected characters as you move to it; Backspace puts back the pose from before"), GUILayout.ExpandWidth(false));
            if (step != _plugin.ApplyOnStep.Value) _plugin.ApplyOnStep.Value = step;
            var rescan = false;
            if (only != _onlyNoPicture)
            {
                _onlyNoPicture = only;
                rescan = true;
            }
            if (dat != _plugin.ShowDat.Value)
            {
                _plugin.ShowDat.Value = dat;
                rescan = true;
            }
            return rescan;
        }

        // ---------------------------------------------------------------- Studio's Pose list

        public override bool StudioListOpen(out int sex)
        {
            sex = -1;
            return PoseList.Open;
        }

        public override void SetStudioListHidden(bool hidden) => PoseList.SetHidden(hidden);
        public override void CloseStudioList() => PoseList.Close();
        public override void SyncStudioList(IList<ListChange> changes) => PoseList.Refresh();

        /// <summary>Keeps Pose Tools' folder (its list and where saves go) on the folder open here.</summary>
        public override void OnFolderChanged(string folder)
        {
            _folder = folder;
            if (_plugin.SyncSaveFolder.Value) PoseList.SetToolsFolder(folder);
        }

        public override void Restore() => PoseList.Restore();
    }
}
