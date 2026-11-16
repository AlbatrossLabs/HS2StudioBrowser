using System;
using UnityEngine;

namespace HS2StudioBrowser.Game
{
    /// <summary>
    /// Keeps Studio's Add menu open while the browser adds something. Studio closes the menu after adding
    /// when its "auto hide" option is on (Studio.AddItem / AddFemale / AddMale read it); the option is
    /// turned off around the add and put back a few frames later, in case Studio reads it a frame late.
    /// </summary>
    public static class AddMenuHold
    {
        private static bool? _saved;
        private static int _restoreAt;

        /// <summary>Runs <paramref name="add"/> with the menu kept open when <paramref name="keepOpen"/>.</summary>
        public static T Run<T>(bool keepOpen, Func<T> add)
        {
            if (!keepOpen) return add();
            try
            {
                if (_saved == null) _saved = Studio.Studio.optionSystem.autoHide;
                Studio.Studio.optionSystem.autoHide = false;
                _restoreAt = Time.frameCount + 3;
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't hold the Add menu open: " + ex.Message);
            }
            return add();
        }

        /// <summary>Puts Studio's option back (called every frame).</summary>
        public static void Tick()
        {
            if (_saved == null || Time.frameCount < _restoreAt) return;
            try { Studio.Studio.optionSystem.autoHide = _saved.Value; }
            catch { /* Studio is gone */ }
            _saved = null;
        }
    }
}
