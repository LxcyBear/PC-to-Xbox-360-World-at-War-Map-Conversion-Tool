namespace WaW360.Core.Maps;

public sealed class MapCandidate
{
    public required string Name { get; init; }
    public string? ModFolder { get; set; }
    public string? UsermapFolder { get; set; }
    public string? MapFastFile { get; set; }
    public string? ModFastFile { get; set; }
    public string? PatchFastFile { get; set; }
    public int IwdCount { get; set; }
    public List<string> SourceFolders { get; } = new();
    public string Profile { get; set; } = "Normal";
    public List<string> ProfileSignals { get; } = new();

    public string DisplayLocation
    {
        get
        {
            if (SourceFolders.Count == 0)
                return UsermapFolder ?? ModFolder ?? string.Empty;
            if (SourceFolders.Count == 1)
                return SourceFolders[0];
            return SourceFolders[0] + $" (+{SourceFolders.Count - 1} source{(SourceFolders.Count == 2 ? string.Empty : "s")})";
        }
    }

    public string Tags
    {
        get
        {
            var tags = new List<string>();
            if (MapFastFile is not null) tags.Add("map.ff");
            if (ModFastFile is not null) tags.Add("mod.ff");
            if (PatchFastFile is not null) tags.Add("patch.ff");
            if (IwdCount > 0) tags.Add($"{IwdCount} IWD");
            return string.Join(", ", tags);
        }
    }
}
