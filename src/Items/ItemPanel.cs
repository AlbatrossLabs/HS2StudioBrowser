using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Items
{
    /// <summary>
    /// Studio's Item list (Add › Item): its group, category and item columns. Finding them, hiding them
    /// (CanvasGroups, so they stay open but neither show nor take clicks), closing the list through the Add
    /// menu, and adding an item the way Studio's list does, optionally inside the selected workspace node.
    /// </summary>
    internal static class ItemPanel
    {
        private static ItemGroupList _groups;
        private static ItemCategoryList _categories;
        private static ItemList _items;
        private static float _nextFind;
        private static readonly Dictionary<GameObject, CanvasGroup> Hidden = new Dictionary<GameObject, CanvasGroup>();
        private static readonly HashSet<CanvasGroup> Added = new HashSet<CanvasGroup>();

        private static void Find()
        {
            if (_groups != null && _categories != null && _items != null) return;
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            if (_groups == null) _groups = Resources.FindObjectsOfTypeAll<ItemGroupList>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
            if (_categories == null) _categories = Resources.FindObjectsOfTypeAll<ItemCategoryList>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
            if (_items == null) _items = Resources.FindObjectsOfTypeAll<ItemList>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
        }

        public static bool Open
        {
            get
            {
                Find();
                return _groups != null && _groups.gameObject.activeInHierarchy;
            }
        }

        public static void SetHidden(bool hide)
        {
            Find();
            foreach (var go in new MonoBehaviour[] { _groups, _categories, _items }.Where(x => x != null).Select(x => x.gameObject))
            {
                if (!Hidden.TryGetValue(go, out var g) || g == null)
                {
                    if (!hide) continue;
                    g = go.GetComponent<CanvasGroup>();
                    if (g == null)
                    {
                        g = go.AddComponent<CanvasGroup>();
                        Added.Add(g);
                    }
                    Hidden[go] = g;
                }
                g.alpha = hide ? 0f : 1f;
                g.blocksRaycasts = !hide;
                g.interactable = !hide;
            }
        }

        public static void Restore()
        {
            SetHidden(false);
            foreach (var g in Added) if (g != null) UnityEngine.Object.Destroy(g);
            Added.Clear();
            Hidden.Clear();
        }

        /// <summary>Closes the Item list the way its Add-menu button does (so the button's highlight follows).</summary>
        public static void Close()
        {
            Find();
            SetHidden(false);
            if (_groups == null) return;
            try
            {
                foreach (var ctrl in Resources.FindObjectsOfTypeAll<AddButtonCtrl>())
                {
                    if (ctrl == null || !ctrl.gameObject.scene.IsValid()) continue;
                    var infos = AccessTools.Field(typeof(AddButtonCtrl), "commonInfo")?.GetValue(ctrl) as Array;
                    if (infos == null) continue;
                    for (var i = 0; i < infos.Length; i++)
                    {
                        var info = infos.GetValue(i);
                        var obj = AccessTools.Field(info.GetType(), "obj")?.GetValue(info) as GameObject;
                        if (obj == null) continue;
                        if (obj == _groups.gameObject || _groups.transform.IsChildOf(obj.transform))
                        {
                            if (obj.activeSelf) ctrl.OnClick(i); // clicking the open one closes it
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't close the Item list through the Add menu: " + ex.Message);
            }
        }

        /// <summary>The workspace node new items can go inside: the one selected (if exactly one).</summary>
        public static TreeNodeObject SelectedNode()
        {
            try
            {
                var nodes = Studio.Studio.Instance.treeNodeCtrl.selectNodes;
                return nodes != null && nodes.Length == 1 ? nodes[0] : null;
            }
            catch { return null; }
        }

        public static string NameOf(TreeNodeObject node)
        {
            try { return node.textName; }
            catch { return "the selection"; }
        }

        /// <summary>Adds the item as Studio's list does (at the start position, selected if Studio's options say so).</summary>
        public static bool Add(int group, int category, int no)
        {
            var before = Studio.Studio.Instance.dicObjectCtrl.Count;
            Studio.Studio.Instance.AddItem(group, category, no);
            return Studio.Studio.Instance.dicObjectCtrl.Count > before;
        }

        /// <summary>Adds the item inside <paramref name="parent"/> (like dragging it onto that node in the workspace).</summary>
        public static OCIItem AddInside(int group, int category, int no, TreeNodeObject parent)
        {
            var studio = Studio.Studio.Instance;
            var item = AddObjectItem.Add(group, category, no);
            if (item == null) return null;
            Singleton<UndoRedoManager>.Instance.Clear();
            if (parent != null) studio.treeNodeCtrl.SetParent(item.treeNodeObject, parent);
            if (Studio.Studio.optionSystem.autoSelect) studio.treeNodeCtrl.SelectSingle(item.treeNodeObject);
            return item;
        }
    }
}
