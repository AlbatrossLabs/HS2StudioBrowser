using System;
using System.Linq;
using HarmonyLib;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Maps
{
    /// <summary>
    /// Studio's Map list (Add › Map) and the loaded map: finding the list, hiding it (a CanvasGroup, so it
    /// stays open but neither shows nor takes clicks), closing it through the Add menu, loading a map the
    /// way the list does, and the map's Option / Light switches and position.
    /// </summary>
    internal static class MapPanel
    {
        private static MapList _list;
        private static GameObject _panel;   // what the Add menu's Map button shows and hides
        private static float _nextFind;
        private static CanvasGroup _group;
        private static bool _addedGroup;

        /// <summary>
        /// Studio's own map list (Studio._mapList), and the panel the Add menu toggles for it: the Add menu's
        /// entry whose object is the list or one of its parents.
        /// </summary>
        private static void Find()
        {
            if (_list != null && _panel != null) return;
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            try
            {
                if (Studio.Studio.Instance == null) return;
                _list = AccessTools.Field(typeof(Studio.Studio), "_mapList")?.GetValue(Studio.Studio.Instance) as MapList;
                if (_list == null)
                    _list = Resources.FindObjectsOfTypeAll<MapList>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
                if (_list == null) return;

                // The MapList component sits on the Map button in the Add menu; the list itself (what the button opens)
                // is wherever its rows go (transformRoot). The panel is the Add menu entry holding those rows (the
                // deepest one, so not the Add menu itself), or else the scroll view around them.
                var rows = AccessTools.Field(typeof(MapList), "transformRoot")?.GetValue(_list) as Transform;
                _panel = null;
                var depth = -1;
                if (rows != null)
                {
                    foreach (var ctrl in Resources.FindObjectsOfTypeAll<AddButtonCtrl>())
                    {
                        if (ctrl == null || !ctrl.gameObject.scene.IsValid()) continue;
                        if (!(AccessTools.Field(typeof(AddButtonCtrl), "commonInfo")?.GetValue(ctrl) is Array infos)) continue;
                        foreach (var info in infos)
                        {
                            var obj = info == null ? null : AccessTools.Field(info.GetType(), "obj")?.GetValue(info) as GameObject;
                            if (obj == null || !rows.IsChildOf(obj.transform) || _list.transform.IsChildOf(obj.transform)) continue;
                            var d = Depth(obj.transform);
                            if (d > depth)
                            {
                                depth = d;
                                _panel = obj;
                            }
                        }
                    }
                    if (_panel == null)
                        for (var t = rows; t != null; t = t.parent)
                        {
                            if (_list.transform.IsChildOf(t)) break;
                            if (t.GetComponent<UnityEngine.UI.ScrollRect>() != null) _panel = t.gameObject;
                        }
                    if (_panel == null && !_list.transform.IsChildOf(rows)) _panel = rows.gameObject;
                }
                MapShots.Note($"Map rows: {(rows == null ? "(none)" : PathOf(rows))}");
                if (_panel == null)
                {
                    MapShots.Note("Couldn't tell which panel shows Studio's Map list; the Map browser opens from its toolbar button and hotkey only.");
                    _nextFind = Time.unscaledTime + 30f;
                    return;
                }
                MapShots.Note($"Map list: {PathOf(_list.transform)}; panel the Add menu toggles: {PathOf(_panel.transform)} (open now: {_panel.activeInHierarchy})");
            }
            catch (Exception ex)
            {
                MapShots.Note("Couldn't find Studio's Map list: " + ex.Message);
            }
        }

        private static int Depth(Transform t)
        {
            var d = 0;
            for (; t != null; t = t.parent) d++;
            return d;
        }

        private static string PathOf(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (; t != null; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts.ToArray());
        }

        public static bool Open
        {
            get
            {
                Find();
                return _panel != null && _panel.activeInHierarchy;
            }
        }

        public static void SetHidden(bool hide)
        {
            Find();
            if (_panel == null) return;
            if (_group == null)
            {
                if (!hide) return;
                _group = _panel.GetComponent<CanvasGroup>();
                if (_group == null)
                {
                    _group = _panel.AddComponent<CanvasGroup>();
                    _addedGroup = true;
                }
            }
            _group.alpha = hide ? 0f : 1f;
            _group.blocksRaycasts = !hide;
            _group.interactable = !hide;
        }

        public static void Restore()
        {
            SetHidden(false);
            if (_addedGroup && _group != null) UnityEngine.Object.Destroy(_group);
            _group = null;
            _addedGroup = false;
        }

        /// <summary>Closes the Map list the way its Add-menu button does.</summary>
        public static void Close()
        {
            Find();
            SetHidden(false);
            if (_panel == null) return;
            try
            {
                foreach (var ctrl in Resources.FindObjectsOfTypeAll<AddButtonCtrl>())
                {
                    if (ctrl == null || !ctrl.gameObject.scene.IsValid()) continue;
                    if (!(AccessTools.Field(typeof(AddButtonCtrl), "commonInfo")?.GetValue(ctrl) is Array infos)) continue;
                    for (var i = 0; i < infos.Length; i++)
                    {
                        var info = infos.GetValue(i);
                        var obj = info == null ? null : AccessTools.Field(info.GetType(), "obj")?.GetValue(info) as GameObject;
                        if (obj != _panel) continue;
                        if (obj.activeSelf) ctrl.OnClick(i);
                        return;
                    }
                }
                _panel.SetActive(false);
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't close the Map list through the Add menu: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- the loaded map

        /// <summary>The loaded map's number (-1: none).</summary>
        public static int Current
        {
            get
            {
                try { return Singleton<Studio.Map>.Instance != null ? Singleton<Studio.Map>.Instance.no : -1; }
                catch { return -1; }
            }
        }

        public static bool Loading
        {
            get
            {
                try { return Singleton<Studio.Map>.Instance != null && Singleton<Studio.Map>.Instance.isLoading; }
                catch { return false; }
            }
        }

        /// <summary>Loads a map (or, with -1, clears it) as Studio's list does; <paramref name="closeMenu"/> as Studio's "close after adding".</summary>
        public static void Load(int no, bool closeMenu)
        {
            Studio.Studio.Instance.AddMap(no, closeMenu, false, false);
            Find();
            // Keep Studio's own list showing the right one, for when the browser steps aside.
            try
            {
                if (_list != null) AccessTools.Method(typeof(MapList), "UpdateInfo")?.Invoke(_list, null);
            }
            catch { /* only the highlight */ }
        }

        public static bool HasOption => Singleton<Studio.Map>.Instance != null && Singleton<Studio.Map>.Instance.IsOption;
        public static bool HasLight => Singleton<Studio.Map>.Instance != null && Singleton<Studio.Map>.Instance.IsLight;

        public static bool OptionOn => Studio.Studio.Instance.sceneInfo.mapInfo.option;
        public static bool LightOn => Studio.Studio.Instance.sceneInfo.mapInfo.light;

        public static void SetOption(bool on)
        {
            Studio.Studio.Instance.sceneInfo.mapInfo.option = on;
            Singleton<Studio.Map>.Instance.VisibleOption = on;
            RefreshStudioPanel();
        }

        public static void SetLight(bool on)
        {
            Studio.Studio.Instance.sceneInfo.mapInfo.light = on;
            Singleton<Studio.Map>.Instance.VisibleLight = on;
            RefreshStudioPanel();
        }

        public static bool Moved
        {
            get
            {
                var ca = Studio.Studio.Instance.sceneInfo.mapInfo.ca;
                return ca.pos != Vector3.zero || ca.rot != Vector3.zero;
            }
        }

        /// <summary>Puts the map back where it loads (position and rotation zero).</summary>
        public static void ResetPlace()
        {
            var ca = Studio.Studio.Instance.sceneInfo.mapInfo.ca;
            ca.pos = Vector3.zero;
            ca.rot = Vector3.zero;
            var root = Singleton<Studio.Map>.Instance.MapRoot;
            if (root != null)
            {
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
            }
            RefreshStudioPanel();
        }

        /// <summary>Keeps Studio's own map panel (position fields, switches) in step.</summary>
        private static void RefreshStudioPanel()
        {
            try
            {
                foreach (var ctrl in Resources.FindObjectsOfTypeAll<MapCtrl>())
                    if (ctrl != null && ctrl.gameObject.scene.IsValid()) ctrl.UpdateUI();
            }
            catch { /* only the display */ }
        }
    }
}
