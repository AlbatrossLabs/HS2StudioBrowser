using System.Collections.Generic;
using System.Linq;
using KKAPI.Studio;
using Studio;

namespace HS2StudioBrowser.Game
{
    /// <summary>Characters selected in Studio's workspace.</summary>
    public static class StudioTargets
    {
        public static List<OCIChar> Characters()
        {
            try { return StudioAPI.GetSelectedCharacters().Where(c => c != null && c.charInfo != null).ToList(); }
            catch { return new List<OCIChar>(); }
        }

        public static string NameOf(OCIChar c)
        {
            try { return c.treeNodeObject?.textName ?? c.charInfo.chaFile.parameter.fullname; }
            catch { return "character"; }
        }
    }
}
