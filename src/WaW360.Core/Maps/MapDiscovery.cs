using System.IO.Compression;

namespace WaW360.Core.Maps;

public static class MapDiscovery
{
    public static string DefaultPcMapsFolder
    {
        get
        {
            // Prefer the user's OneDrive desktop when Windows exposes it. This matches
            // the current working map root while still falling back to the shell Desktop.
            var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(oneDrive))
            {
                var oneDriveRoot = Path.Combine(oneDrive, "Desktop", "My Xbox", "World at War", "PC Maps");
                if (Directory.Exists(oneDriveRoot))
                    return oneDriveRoot;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "My Xbox",
                "World at War",
                "PC Maps");
        }
    }

    public static string LegacyPcMapsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "World at War",
        "PC Maps");

    public static string DefaultModsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Activision",
        "CoDWaW",
        "mods");

    public static string DefaultUsermapsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Activision",
        "CoDWaW",
        "usermaps");

    public static string ResolveConfiguredPcMapsFolder(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return DefaultPcMapsFolder;

        try
        {
            var configuredFull = Path.GetFullPath(configured.Trim());
            var legacyFull = Path.GetFullPath(LegacyPcMapsFolder);
            if (string.Equals(configuredFull, legacyFull, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(DefaultPcMapsFolder))
                return DefaultPcMapsFolder;
        }
        catch
        {
            // Preserve the user's configured text if normalization fails.
        }

        return configured.Trim();
    }

    public static IReadOnlyList<string> GetListingRoots(string? primaryRoot)
    {
        var roots = new List<string>();
        AddDistinctRoot(roots, ResolveConfiguredPcMapsFolder(primaryRoot));
        AddDistinctRoot(roots, DefaultModsFolder);
        return roots;
    }

    public static IReadOnlyList<MapCandidate> DiscoverSourceFolder(string sourceRoot) =>
        DiscoverSourceFolders(new[] { sourceRoot });

    public static IReadOnlyList<MapCandidate> DiscoverSourceFolders(IEnumerable<string> sourceRoots)
    {
        var maps = new Dictionary<string, MapCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in sourceRoots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var sourceRoot = root.Trim();
            if (!Directory.Exists(sourceRoot))
                continue;

            DiscoverInto(maps, sourceRoot);
        }

        foreach (var map in maps.Values)
            DetectQuickProfile(map);

        return maps.Values
            .Where(m => m.MapFastFile is not null || m.ModFastFile is not null || m.IwdCount > 0)
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void DiscoverInto(IDictionary<string, MapCandidate> maps, string sourceRoot)
    {
        // Allow loose .ff files directly in a configured source folder for quick testing.
        foreach (var ff in Directory.EnumerateFiles(sourceRoot, "*.ff", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(ff);
            if (name.EndsWith("_patch", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("_load", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("mod", StringComparison.OrdinalIgnoreCase))
                continue;

            var map = GetOrCreate(maps, name);
            AddSourceFolder(map, sourceRoot);
            map.UsermapFolder ??= sourceRoot;
            map.MapFastFile ??= ff;
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceRoot))
        {
            var name = Path.GetFileName(dir);
            var map = GetOrCreate(maps, name);

            // Most PC custom maps are either flat map folders, CoDWaW mods folders,
            // or contain familiar usermaps/mods-style content one useful level down.
            PopulateFromFolder(map, dir, name);

            var nestedUsermap = Path.Combine(dir, "usermaps", name);
            if (Directory.Exists(nestedUsermap))
                PopulateFromFolder(map, nestedUsermap, name);

            var nestedMod = Path.Combine(dir, "mods", name);
            if (Directory.Exists(nestedMod))
                PopulateFromFolder(map, nestedMod, name);
        }
    }

    // Retained for users who want to scan the stock PC install layout directly.
    public static IReadOnlyList<MapCandidate> DiscoverDefault() =>
        Discover(DefaultModsFolder, DefaultUsermapsFolder);

    public static IReadOnlyList<MapCandidate> Discover(string modsRoot, string usermapsRoot) =>
        DiscoverSourceFolders(new[] { modsRoot, usermapsRoot });

    private static void PopulateFromFolder(MapCandidate map, string dir, string mapName)
    {
        AddSourceFolder(map, dir);

        map.UsermapFolder ??= dir;
        if (IsUnderRoot(dir, DefaultModsFolder))
            map.ModFolder ??= dir;

        map.MapFastFile ??= FindFile(dir, mapName + ".ff")
                           ?? FindFirst(dir, "*.ff", exclude: new[] { "mod.ff", "*_patch.ff", "*_load.ff" });
        map.ModFastFile ??= FindFile(dir, "mod.ff");
        map.PatchFastFile ??= FindFirst(dir, "*_patch.ff");

        // Avoid double-counting IWDs when the same physical directory is encountered
        // from more than one root/shape.
        map.IwdCount = CountUniqueIwds(map.SourceFolders);
    }

    private static void AddSourceFolder(MapCandidate map, string dir)
    {
        var fullDir = Path.GetFullPath(dir);
        if (!map.SourceFolders.Any(x => string.Equals(x, fullDir, StringComparison.OrdinalIgnoreCase)))
            map.SourceFolders.Add(fullDir);
    }

    private static int CountUniqueIwds(IEnumerable<string> folders)
    {
        var iwds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.iwd", SearchOption.TopDirectoryOnly))
                    iwds.Add(Path.GetFullPath(file));
            }
            catch
            {
                // Discovery should remain usable even if one folder becomes inaccessible.
            }
        }
        return iwds.Count;
    }

    private static void DetectQuickProfile(MapCandidate map)
    {
        var signals = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        AddProfileSignal(map.Name, "map name", signals);
        AddProfileSignal(map.MapFastFile, "map FF", signals);
        AddProfileSignal(map.ModFastFile, "mod FF", signals);
        AddProfileSignal(map.PatchFastFile, "patch FF", signals);

        foreach (var folder in map.SourceFolders)
        {
            AddProfileSignal(folder, "source path", signals);
            if (!Directory.Exists(folder)) continue;

            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
                {
                    AddProfileSignal(path, "file/folder name", signals);
                    if (signals.Any(s => s.StartsWith("UGX", StringComparison.OrdinalIgnoreCase)) &&
                        signals.Any(s => s.StartsWith("ZCT/ZCM", StringComparison.OrdinalIgnoreCase)))
                        break;
                }
            }
            catch
            {
                // A quick listing detector should never make the map disappear just because
                // one nested source folder is inaccessible.
            }

            try
            {
                foreach (var iwd in Directory.EnumerateFiles(folder, "*.iwd", SearchOption.TopDirectoryOnly))
                {
                    AddProfileSignal(iwd, "IWD filename", signals);
                    using var zip = ZipFile.OpenRead(iwd);
                    foreach (var entry in zip.Entries)
                    {
                        AddProfileSignal(entry.FullName, $"IWD {Path.GetFileName(iwd)} entry", signals);
                        if (signals.Any(s => s.StartsWith("UGX", StringComparison.OrdinalIgnoreCase)) &&
                            signals.Any(s => s.StartsWith("ZCT/ZCM", StringComparison.OrdinalIgnoreCase)))
                            break;
                    }
                }
            }
            catch
            {
                // Broken/non-ZIP IWDs are left for the deeper analyzer to report.
            }
        }

        map.ProfileSignals.Clear();
        map.ProfileSignals.AddRange(signals);
        var hasUgx = signals.Any(s => s.StartsWith("UGX", StringComparison.OrdinalIgnoreCase));
        var hasZct = signals.Any(s => s.StartsWith("ZCT/ZCM", StringComparison.OrdinalIgnoreCase));
        map.Profile = hasUgx && hasZct ? "UGX + ZCT"
            : hasUgx ? "UGX"
            : hasZct ? "ZCT"
            : "Normal";
    }

    private static void AddProfileSignal(string? value, string source, ISet<string> signals)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var lower = value.ToLowerInvariant();
        if (lower.Contains("ugx", StringComparison.Ordinal))
            signals.Add($"UGX marker in {source}: {TrimSignal(value)}");
        if (lower.Contains("zct", StringComparison.Ordinal) || lower.Contains("zcm", StringComparison.Ordinal))
            signals.Add($"ZCT/ZCM marker in {source}: {TrimSignal(value)}");
    }

    private static string TrimSignal(string value)
    {
        var s = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length <= 140 ? s : s[..140] + "...";
    }

    private static bool IsUnderRoot(string path, string root)
    {
        try
        {
            var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void AddDistinctRoot(ICollection<string> roots, string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        string normalized;
        try { normalized = Path.GetFullPath(root); }
        catch { normalized = root.Trim(); }
        if (!roots.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
            roots.Add(normalized);
    }

    private static MapCandidate GetOrCreate(IDictionary<string, MapCandidate> maps, string name)
    {
        if (!maps.TryGetValue(name, out var map))
        {
            map = new MapCandidate { Name = name };
            maps[name] = map;
        }
        return map;
    }

    private static string? FindFile(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        return File.Exists(path) ? path : null;
    }

    private static string? FindFirst(string dir, string pattern, IEnumerable<string>? exclude = null)
    {
        var exclusions = (exclude ?? Array.Empty<string>()).ToArray();
        foreach (var file in Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (exclusions.Any(e => WildcardMatch(name, e))) continue;
            return file;
        }
        return null;
    }

    private static bool WildcardMatch(string value, string pattern)
    {
        if (!pattern.Contains('*')) return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        var parts = pattern.Split('*');
        return value.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) &&
               value.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase);
    }
}
