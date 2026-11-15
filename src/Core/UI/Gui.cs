using UnityEngine;

namespace HS2StudioBrowser.UI
{
    internal static class Gui
    {
        /// <summary>
        /// Call right after a click that adds controls to the window (unfolding a section, ticking a
        /// variation, adding a row). IMGUI lays the window out once per frame; drawing more controls than
        /// were laid out in the same click event makes Unity log a layout error. Ending this event lets
        /// the next frame lay out the new controls cleanly.
        /// </summary>
        public static void Relayout()
        {
            var t = Event.current?.type;
            if (t != null && t != EventType.Layout && t != EventType.Repaint) GUIUtility.ExitGUI();
        }
    }
}
