using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record NormalMapInputFile(
    string Role,
    string Path,
    long SizeBytes);

public sealed record NormalMapFastFileAnalysis(
    string Role,
    string Path,
    bool Readable,
    string Platform,
    string Compression,
    int ScriptStringCount,
    int AssetCount,
    IReadOnlyDictionary<string, int> AssetTypes,
    IReadOnlyList<string> ResolvedNamedAssets,
    string? Error);

public sealed record NormalMapIwdAnalysis(
    string Path,
    int EntryCount,
    IReadOnlyDictionary<string, int> Categories,
    IReadOnlyDictionary<string, int> Extensions,
    IReadOnlyList<string> ImportantEntries,
    IReadOnlyList<string> UnsupportedSourceEntries,
    string? Error);

public sealed class NormalMapAnalysisResult
{
    public required string MapName { get; init; }
    public required string Profile { get; init; }
    public required IReadOnlyList<string> ProfileSignals { get; init; }
    public required IReadOnlyList<NormalMapInputFile> InputFiles { get; init; }
    public required IReadOnlyList<NormalMapFastFileAnalysis> FastFiles { get; init; }
    public required IReadOnlyList<NormalMapIwdAnalysis> Iwds { get; init; }
    public required IReadOnlyDictionary<string, int> TotalAssetTypes { get; init; }
    public required IReadOnlyDictionary<string, int> TotalIwdCategories { get; init; }
    public required IReadOnlyList<string> ConversionPlan { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 4 alpha 1b. Responsive read-only inventory/analyzer for a normal PC World at War custom map.
/// It deliberately does not rebuild or modify any fastfile. Its purpose is to establish the
/// exact source set and asset mix before PC -> Xbox serialization begins.
/// </summary>
public static class NormalMapAnalyzer
{
    private static readonly HashSet<string> UnsupportedLooseExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".map", ".menu", ".inc", ".h", ".gdt", ".psd", ".ttf", ".png", ".jpg", ".jpeg", ".bmp"
    };

    private static readonly ConcurrentDictionary<string, Task<NormalMapAnalysisResult>> SharedAnalysis = new(StringComparer.OrdinalIgnoreCase);

    public static void ClearMemoryCache() => SharedAnalysis.Clear();

    public static async Task<NormalMapAnalysisResult> AnalyzeAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        var fingerprint = await BatchStageCache.ComputeMapFingerprintAsync(map, cancellationToken);
        var key = Path.GetFullPath(outputRoot) + "|" + map.Name + "|" + fingerprint;
        var task = SharedAnalysis.GetOrAdd(key, _ => AnalyzeCoreAsync(map, outputRoot, CancellationToken.None));
        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        catch
        {
            SharedAnalysis.TryRemove(key, out _);
            throw;
        }
    }

    private static async Task<NormalMapAnalysisResult> AnalyzeCoreAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        if (string.IsNullOrWhiteSpace(map.Name)) throw new InvalidDataException("Map candidate has no name.");

        var outputDir = Path.Combine(outputRoot, map.Name);
        Directory.CreateDirectory(outputDir);

        var inputFiles = DiscoverInputFiles(map);
        var fastFiles = new List<NormalMapFastFileAnalysis>();
        var iwds = new List<NormalMapIwdAnalysis>();
        var warnings = new List<string>();
        var profileSignals = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in inputFiles.Where(f => f.Path.EndsWith(".ff", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            fastFiles.Add(await AnalyzeFastFileAsync(file.Role, file.Path, profileSignals, cancellationToken));
        }

        foreach (var file in inputFiles.Where(f => f.Path.EndsWith(".iwd", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            iwds.Add(AnalyzeIwd(file.Path, profileSignals));
        }

        AddFolderSignals(map, profileSignals);
        var profile = ClassifyProfile(profileSignals);
        if (!profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Profile signals suggest {profile}. Stage 4 alpha 1b is using NORMAL-MAP rules only; no UGX/ZCT override behavior is applied.");

        var totalAssetTypes = SumDictionaries(fastFiles.Where(x => x.Readable).Select(x => x.AssetTypes));
        var totalIwdCategories = SumDictionaries(iwds.Select(x => x.Categories));

        if (map.MapFastFile is null)
            warnings.Add("No <map>.ff was detected. A normal map conversion cannot proceed to an Xbox map fastfile without a primary map FF.");
        if (fastFiles.Any(x => !x.Readable))
            warnings.Add("At least one source fastfile could not be parsed. It must be understood before a full conversion can be considered complete.");
        if (iwds.Any(x => x.UnsupportedSourceEntries.Count > 0))
            warnings.Add("One or more IWDs contain source-only files. These are inventoried but are not assumed to be runtime Xbox assets.");

        var plan = BuildConversionPlan(map, fastFiles, iwds, totalAssetTypes);

        var textPath = Path.Combine(outputDir, $"{map.Name}_stage4_normal_analysis.txt");
        var jsonPath = Path.Combine(outputDir, $"{map.Name}_stage4_normal_analysis.json");

        var result = new NormalMapAnalysisResult
        {
            MapName = map.Name,
            Profile = profile,
            ProfileSignals = profileSignals.ToList(),
            InputFiles = inputFiles,
            FastFiles = fastFiles,
            Iwds = iwds,
            TotalAssetTypes = totalAssetTypes,
            TotalIwdCategories = totalIwdCategories,
            ConversionPlan = plan,
            Warnings = warnings,
            TextReportFile = textPath,
            JsonReportFile = jsonPath
        };

        await File.WriteAllTextAsync(textPath, RenderTextReport(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(jsonPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8,
            cancellationToken);

        return result;
    }

    private static List<NormalMapInputFile> DiscoverInputFiles(MapCandidate map)
    {
        var files = new List<NormalMapInputFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string role, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var full = Path.GetFullPath(path);
            if (!seen.Add(full)) return;
            files.Add(new NormalMapInputFile(role, full, new FileInfo(full).Length));
        }

        Add("map.ff", map.MapFastFile);
        Add("mod.ff", map.ModFastFile);
        Add("map_patch.ff", map.PatchFastFile);

        foreach (var dir in CandidateFolders(map))
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var iwd in Directory.EnumerateFiles(dir, "*.iwd", SearchOption.TopDirectoryOnly))
                Add("iwd", iwd);

            // Some maps carry extra load/patch FFs next to the primary files. Inventory them too.
            foreach (var ff in Directory.EnumerateFiles(dir, "*.ff", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(ff);
                if (seen.Contains(Path.GetFullPath(ff))) continue;
                var role = name.EndsWith("_load.ff", StringComparison.OrdinalIgnoreCase) ? "load.ff" :
                           name.EndsWith("_patch.ff", StringComparison.OrdinalIgnoreCase) ? "map_patch.ff" :
                           name.Equals("mod.ff", StringComparison.OrdinalIgnoreCase) ? "mod.ff" : "extra.ff";
                Add(role, ff);
            }
        }

        return files
            .OrderBy(f => RoleOrder(f.Role))
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> CandidateFolders(MapCandidate map)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in map.SourceFolders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            var full = Path.GetFullPath(folder);
            if (seen.Add(full)) yield return full;
        }

        foreach (var folder in new[] { map.UsermapFolder, map.ModFolder })
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            var full = Path.GetFullPath(folder);
            if (seen.Add(full)) yield return full;
        }
    }

    private static int RoleOrder(string role) => role switch
    {
        "map.ff" => 0,
        "map_patch.ff" => 1,
        "mod.ff" => 2,
        "load.ff" => 3,
        "extra.ff" => 4,
        "iwd" => 5,
        _ => 9
    };

    private static async Task<NormalMapFastFileAnalysis> AnalyzeFastFileAsync(
        string role,
        string path,
        ISet<string> profileSignals,
        CancellationToken cancellationToken)
    {
        try
        {
            var doc = await T4ZoneParser.ParseFastFileAsync(path, cancellationToken);
            var grouped = doc.Assets
                .GroupBy(a => a.TypeName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            // Stage 4 alpha 1b intentionally stops at the proven XAssetList + script-string inventory.
            // The older deep payload resolver performs a broad byte scan and can take long enough to make
            // WinForms appear hung on large/custom zones. Payload-name resolution belongs in the conversion
            // stage where we can walk assets by type instead of scanning the whole decompressed zone.
            var named = new List<string>();

            DetectProfileSignals(doc.ScriptStrings.Select(s => s.Value ?? string.Empty), profileSignals, $"{role} script string");

            return new NormalMapFastFileAnalysis(
                role,
                path,
                true,
                doc.Platform.ToString(),
                doc.Compression.ToString(),
                doc.ScriptStringCount,
                doc.AssetCount,
                grouped,
                named,
                null);
        }
        catch (Exception ex)
        {
            return new NormalMapFastFileAnalysis(
                role,
                path,
                false,
                "Unknown",
                "Unknown",
                0,
                0,
                new Dictionary<string, int>(),
                Array.Empty<string>(),
                ex.Message);
        }
    }

    private static NormalMapIwdAnalysis AnalyzeIwd(string path, ISet<string> profileSignals)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var categories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var extensions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var important = new List<string>();
            var unsupported = new List<string>();

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var normalized = entry.FullName.Replace('\\', '/');
                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                Increment(extensions, string.IsNullOrWhiteSpace(ext) ? "<none>" : ext.ToLowerInvariant());

                var category = ClassifyIwdEntry(normalized, ext);
                Increment(categories, category);

                if (category is "texture" or "sound" or "script/raw" or "weapon" or "localizedstring" or "stringtable")
                    important.Add(normalized);

                if (UnsupportedLooseExtensions.Contains(ext))
                    unsupported.Add(normalized);

                DetectProfileSignals(new[] { normalized }, profileSignals, $"IWD {Path.GetFileName(path)}");
            }

            return new NormalMapIwdAnalysis(
                path,
                archive.Entries.Count(e => !string.IsNullOrEmpty(e.Name)),
                categories.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                extensions.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                important.Take(500).ToList(),
                unsupported.Take(500).ToList(),
                null);
        }
        catch (Exception ex)
        {
            return new NormalMapIwdAnalysis(
                path,
                0,
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                ex.Message);
        }
    }

    private static string ClassifyIwdEntry(string normalizedPath, string extension)
    {
        var p = normalizedPath.ToLowerInvariant();
        if (p.StartsWith("images/") && extension.Equals(".iwi", StringComparison.OrdinalIgnoreCase)) return "texture";
        if (p.StartsWith("sound/") && extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)) return "sound";
        if (p.StartsWith("weapons/sp/") || p.StartsWith("weapons/")) return "weapon";
        if (p.StartsWith("localizedstrings/") && extension.Equals(".str", StringComparison.OrdinalIgnoreCase)) return "localizedstring";
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)) return "stringtable";
        if (extension is ".gsc" or ".csc" or ".atr" or ".cfg" or ".txt") return "script/raw";
        if (UnsupportedLooseExtensions.Contains(extension)) return "source-only";
        return "other";
    }

    private static void AddFolderSignals(MapCandidate map, ISet<string> signals)
    {
        DetectProfileSignals(new[]
        {
            map.Name,
            map.MapFastFile ?? string.Empty,
            map.ModFastFile ?? string.Empty,
            map.PatchFastFile ?? string.Empty,
            map.ModFolder ?? string.Empty,
            map.UsermapFolder ?? string.Empty
        }, signals, "path/name");
    }

    private static void DetectProfileSignals(IEnumerable<string> values, ISet<string> signals, string source)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var s = value.ToLowerInvariant();
            if (s.Contains("ugx")) signals.Add($"UGX marker in {source}: {TrimSignal(value)}");
            if (s.Contains("zct") || s.Contains("zcm")) signals.Add($"ZCT/ZCM marker in {source}: {TrimSignal(value)}");
        }
    }

    private static string TrimSignal(string value)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= 140 ? value : value[..140] + "...";
    }

    private static string ClassifyProfile(IEnumerable<string> signals)
    {
        var list = signals.ToList();
        var hasUgx = list.Any(s => s.StartsWith("UGX", StringComparison.OrdinalIgnoreCase));
        var hasZct = list.Any(s => s.StartsWith("ZCT/ZCM", StringComparison.OrdinalIgnoreCase));
        if (hasUgx && hasZct) return "Mixed/Unknown mod profile";
        if (hasUgx) return "UGX candidate";
        if (hasZct) return "ZCT/ZCM candidate";
        return "Normal";
    }

    private static Dictionary<string, int> SumDictionaries(IEnumerable<IReadOnlyDictionary<string, int>> dictionaries)
    {
        var total = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var dict in dictionaries)
            foreach (var (key, value) in dict)
                total[key] = total.TryGetValue(key, out var current) ? current + value : value;
        return total.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void Increment(IDictionary<string, int> dict, string key) =>
        dict[key] = dict.TryGetValue(key, out var value) ? value + 1 : 1;

    private static List<string> BuildConversionPlan(
        MapCandidate map,
        IReadOnlyList<NormalMapFastFileAnalysis> fastFiles,
        IReadOnlyList<NormalMapIwdAnalysis> iwds,
        IReadOnlyDictionary<string, int> assets)
    {
        var plan = new List<string>
        {
            "NORMAL MAP PROFILE: map-owned content wins. Do not apply UGX/ZCT override/namespace rules unless a later profile stage explicitly selects them.",
            "Convert the primary PC map FF to Xbox 360 T4 layout (big-endian/Xbox block serialization) while preserving the map's own asset graph.",
            "Keep only exact stock references that are proven present in Xbox always-loaded common.ff, code_post_gfx.ff or patch.ff; make every other dependency local to the map FF.",
            "IWD textures (.iwi) are source material for image/material dependencies; Xbox runtime cannot load them loose.",
            "IWD weapon files are compiled only when the map actually references those weapons and no compiled map-owned weapon already exists.",
            "Scripts/rawfiles, localized strings and string tables referenced by the map must be packed into the output FF.",
            "Custom loaded sounds are converted to streamed Xbox XMA/SDNS files under usermaps/<map>/sounds; streamed sound is the only supported loose runtime dependency.",
            "After writing, reopen the Xbox FF with the same parser and fail closed on any unresolved pointer/reference or malformed asset."
        };

        if (fastFiles.Any(f => f.Role == "mod.ff"))
            plan.Add("A mod.ff is present. For a NORMAL map, preserve it as map-owned content; do not let it silently replace unrelated stock resident assets without dependency proof.");
        if (fastFiles.Any(f => f.Role == "map_patch.ff"))
            plan.Add("A map patch FF is present. It must be converted and applied after the base map with PC-equivalent override precedence.");
        if (iwds.Count > 0)
            plan.Add($"Inspect {iwds.Count} IWD archive(s) as dependency sources; do not copy the IWD itself to Xbox as a substitute for packed runtime assets.");
        if (assets.TryGetValue("image", out var images) && images > 0)
            plan.Add($"Image conversion required for {images:N0} image asset declaration(s), including Xbox tiling/mipmap/fetch-constant rules where payloads are local.");
        if (assets.TryGetValue("material", out var materials) && materials > 0)
            plan.Add($"Material conversion required for {materials:N0} material declaration(s); PC shader/techset references need Xbox-compatible material templates.");
        if (assets.TryGetValue("xmodel", out var models) && models > 0)
            plan.Add($"Model conversion required for {models:N0} xmodel declaration(s), preserving collision and viewmodel/viewhands detail rules.");
        if (assets.TryGetValue("weapon", out var weapons) && weapons > 0)
            plan.Add($"Weapon conversion required for {weapons:N0} weapon declaration(s); dependencies must remain map-local unless exact Xbox stock assets are proven equivalent.");

        return plan;
    }

    private static string RenderTextReport(NormalMapAnalysisResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 1b NORMAL MAP ANALYSIS");
        sb.AppendLine("=====================================================");
        sb.AppendLine("READ-ONLY: this stage inventories the source map and does not create or modify a fastfile.");
        sb.AppendLine();
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Profile: {r.Profile}");
        if (r.ProfileSignals.Count > 0)
        {
            sb.AppendLine("Profile signals:");
            foreach (var signal in r.ProfileSignals) sb.AppendLine("  - " + signal);
        }
        sb.AppendLine();

        sb.AppendLine("INPUT FILES");
        foreach (var f in r.InputFiles)
            sb.AppendLine($"  {f.Role,-13} {f.SizeBytes,12:N0}  {f.Path}");
        sb.AppendLine();

        sb.AppendLine("FASTFILES");
        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"  [{ff.Role}] {ff.Path}");
            if (!ff.Readable)
            {
                sb.AppendLine($"    ERROR: {ff.Error}");
                continue;
            }
            sb.AppendLine($"    {ff.Platform}/{ff.Compression}; script strings={ff.ScriptStringCount:N0}; assets={ff.AssetCount:N0}");
            foreach (var kv in ff.AssetTypes)
                sb.AppendLine($"      {kv.Key,-22} {kv.Value,7:N0}");
            if (ff.ResolvedNamedAssets.Count > 0)
            {
                sb.AppendLine("    Conservative named payloads:");
                foreach (var name in ff.ResolvedNamedAssets.Take(80)) sb.AppendLine("      " + name);
                if (ff.ResolvedNamedAssets.Count > 80) sb.AppendLine($"      ... {ff.ResolvedNamedAssets.Count - 80:N0} more");
            }
        }
        sb.AppendLine();

        sb.AppendLine("IWD ARCHIVES");
        foreach (var iwd in r.Iwds)
        {
            sb.AppendLine($"  {iwd.Path}");
            if (iwd.Error is not null)
            {
                sb.AppendLine($"    ERROR: {iwd.Error}");
                continue;
            }
            sb.AppendLine($"    entries={iwd.EntryCount:N0}");
            foreach (var kv in iwd.Categories) sb.AppendLine($"      {kv.Key,-18} {kv.Value,7:N0}");
            if (iwd.UnsupportedSourceEntries.Count > 0)
                sb.AppendLine($"    source-only entries inventoried: {iwd.UnsupportedSourceEntries.Count:N0}");
        }
        sb.AppendLine();

        sb.AppendLine("TOTAL FF ASSET TYPES");
        foreach (var kv in r.TotalAssetTypes) sb.AppendLine($"  {kv.Key,-22} {kv.Value,7:N0}");
        sb.AppendLine();

        sb.AppendLine("TOTAL IWD CATEGORIES");
        foreach (var kv in r.TotalIwdCategories) sb.AppendLine($"  {kv.Key,-18} {kv.Value,7:N0}");
        sb.AppendLine();

        sb.AppendLine("NORMAL-MAP CONVERSION PLAN");
        for (var i = 0; i < r.ConversionPlan.Count; i++)
            sb.AppendLine($"  {i + 1}. {r.ConversionPlan[i]}");
        sb.AppendLine();

        if (r.Warnings.Count > 0)
        {
            sb.AppendLine("WARNINGS");
            foreach (var warning in r.Warnings) sb.AppendLine("  - " + warning);
            sb.AppendLine();
        }

        sb.AppendLine("Stage 4 alpha 1b conclusion");
        sb.AppendLine("  No FF was written. This manifest is the evidence input for the first normal-map PC -> Xbox build stage.");
        return sb.ToString();
    }
}
