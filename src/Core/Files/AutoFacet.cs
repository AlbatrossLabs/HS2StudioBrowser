namespace HS2StudioBrowser.Files
{
    /// <summary>Builds the groups a kind fills in by itself (shown in the filter wheel).</summary>
    public static class AutoFacet
    {
        public static TagFacet Make(string name, bool single, params string[] values)
        {
            var f = new TagFacet { Name = name, Single = single };
            f.Values.AddRange(values);
            return f;
        }
    }
}
