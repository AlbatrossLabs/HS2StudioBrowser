using System.Collections.Generic;
using UnityEngine;

namespace HS2StudioBrowser.UI
{
    /// <summary>
    /// A row of controls that wraps: groups go left to right and a group that wouldn't fit starts a new
    /// row, so a narrow window gets two (or three) bars instead of cut-off buttons. Where rows break is
    /// decided in the Layout event and replayed in the others, so IMGUI always sees the same controls.
    /// </summary>
    public sealed class Flow
    {
        private readonly List<bool> _breaks = new List<bool>();
        private int _index;
        private bool _layout, _spring;
        private float _width, _x, _total, _lastTotal = -1;

        /// <summary>Starts the bar, <paramref name="width"/> pixels wide.</summary>
        public void Begin(float width)
        {
            _layout = Event.current.type == EventType.Layout;
            if (_layout)
            {
                _width = width;
                _breaks.Clear();
                _spring = _lastTotal >= 0 && _lastTotal <= width;
            }
            _index = 0;
            _x = 0;
            _total = 0;
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
        }

        /// <summary>
        /// Makes room for the next group, <paramref name="w"/> pixels (from <see cref="Size"/> / <see cref="Fixed"/>);
        /// starts a new row first if it doesn't fit. <paramref name="right"/>: on a new row, push it to the right.
        /// </summary>
        public void Next(float w, bool right = false)
        {
            _total += w;
            bool wrap;
            if (_layout || _index >= _breaks.Count)
            {
                wrap = _x > 0 && _x + w > _width;
                if (_layout) _breaks.Add(wrap);
            }
            else wrap = _breaks[_index];
            _index++;
            if (wrap)
            {
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                _x = 0;
                if (right) GUILayout.FlexibleSpace();
            }
            _x += w;
        }

        /// <summary>Pushes what follows to the right, but only while everything fits on one row.</summary>
        public void Spring()
        {
            if (_spring) GUILayout.FlexibleSpace();
        }

        /// <summary>True when the whole bar fit on one row (last frame).</summary>
        public bool OneRow => _spring;

        public void End()
        {
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint) _lastTotal = _total;
        }

        /// <summary>Width a control with this content takes in this style, margins included.</summary>
        public static float Size(GUIContent c, GUIStyle s) => s.CalcSize(c).x + s.margin.horizontal;
        public static float Size(string text, GUIStyle s) => Size(new GUIContent(text), s);

        /// <summary>Width a control of fixed width takes, margins included.</summary>
        public static float Fixed(float w, GUIStyle s = null) => w + (s ?? Theme.Skin.button).margin.horizontal;
    }
}
