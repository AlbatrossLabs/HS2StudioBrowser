using System;
using System.Collections.Generic;
using Studio;

namespace HS2StudioBrowser.UI
{
    /// <summary>What the plugin needs from a browser window (cards on disk, or catalog items).</summary>
    internal interface IBrowserWindow : IDisposable
    {
        bool Visible { get; }
        void Toggle(List<OCIChar> targets);
        void Show(int sex);
        void Hide();
        void FollowSex(int sex);
        void Update();
        void Draw();
        /// <summary>Files changed outside the browser (a scene saved): re-read them if the window is open.</summary>
        void FilesChanged();
    }
}
