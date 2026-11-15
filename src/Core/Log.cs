using BepInEx.Logging;

namespace HS2StudioBrowser
{
    /// <summary>Log source for the shared code (each plugin also has its own Logger).</summary>
    public static class Log
    {
        public static readonly ManualLogSource Source = Logger.CreateLogSource("Studio Browser");
    }
}
