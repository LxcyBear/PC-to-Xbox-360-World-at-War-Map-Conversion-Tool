using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4StockDonorMatch(string Type, string Name, IReadOnlyList<string> DonorZones);

public sealed record T4CriticalFileNames(
    string Role,
    string Source,
    int DeclaredXAnim,
    int ResolvedXAnimNames,
    int DeclaredXModel,
    int ObservedXModelNames,
    IReadOnlyList<string> XAnimNames,
    IReadOnlyList<string> XModelNames,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapStockResolutionAuditResult
{
    public required string MapName { get; init; }
    public required string Profile { get; init; }
    public required string? XboxGameFolder { get; init; }
    public required IReadOnlyList<T4CriticalFileNames> FastFiles { get; init; }
    public required IReadOnlyList<string> IwdWeaponNames { get; init; }
    public required IReadOnlyList<string> XboxZonesScanned { get; init; }
    public required IReadOnlyList<T4StockDonorMatch> XAnimDonors { get; init; }
    public required IReadOnlyList<T4StockDonorMatch> XModelDonors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 4 alpha 3: resolve critical asset names before writing more serializers.
/// Exact-name donor matches are evidence only. This stage never replaces an asset and never
/// emits a game fastfile; byte/graph equivalence is still required before a donor is used.
/// </summary>
public static class NormalMapStockResolutionAudit
{
    public static async Task<NormalMapStockResolutionAuditResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        string? xboxGameFolder,
        CancellationToken cancellationToken = default)
    {
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        if (!analysis.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{map.Name}' classified as {analysis.Profile}. Alpha 3 NORMAL donor audit refuses mod-profile rules.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage4_alpha3_stock_resolution");
        Directory.CreateDirectory(workspace);

        var files = new List<T4CriticalFileNames>();
        var allXAnim = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allXModel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var ff in analysis.FastFiles.Where(x => x.Readable && (x.AssetTypes.ContainsKey("xanim") || x.AssetTypes.ContainsKey("xmodel"))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scan = await T4CriticalAssetNameScanner.ScanFastFileAsync(ff.Path, cancellationToken);
            foreach (var name in scan.XAnimNames) allXAnim.Add(NormalizeReferenceName(name));
            foreach (var name in scan.XModelNames) allXModel.Add(NormalizeReferenceName(name));
            warnings.AddRange(scan.Warnings.Select(w => $"{Path.GetFileName(ff.Path)}: {w}"));

            files.Add(new T4CriticalFileNames(
                ff.Role,
                ff.Path,
                scan.DeclaredXAnim,
                scan.XAnimNames.Count,
                scan.DeclaredXModel,
                scan.XModelNames.Count,
                scan.XAnimNames,
                scan.XModelNames,
                scan.Warnings));
        }

        var weaponNames = ReadIwdWeaponNames(map);

        var donorIndexXAnim = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var donorIndexXModel = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var zonesScanned = new List<string>();

        if (!string.IsNullOrWhiteSpace(xboxGameFolder) && Directory.Exists(xboxGameFolder))
        {
            var donorFiles = SelectXboxDonorFastFiles(xboxGameFolder).ToList();
            if (donorFiles.Count == 0)
            {
                warnings.Add("Xbox Game Folder is configured, but no common/patch/zombie donor fastfiles were found under it.");
            }
            else
            {
                foreach (var donor in donorFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var scan = await T4CriticalAssetNameScanner.ScanFastFileAsync(donor, cancellationToken);
                        zonesScanned.Add(donor);
                        foreach (var name in scan.XAnimNames)
                            AddDonor(donorIndexXAnim, NormalizeReferenceName(name), donor);
                        foreach (var name in scan.XModelNames)
                            AddDonor(donorIndexXModel, NormalizeReferenceName(name), donor);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        warnings.Add($"Xbox donor scan skipped {donor}: {ex.Message}");
                    }
                }
            }
        }
        else
        {
            warnings.Add("Xbox Game Folder is not configured or does not exist. PC critical names were resolved, but exact Xbox donor matching was skipped. Set it on the Settings tab and rerun Alpha 3.");
        }

        var xanimMatches = BuildMatches("xanim", allXAnim, donorIndexXAnim);
        var xmodelMatches = BuildMatches("xmodel", allXModel, donorIndexXModel);

        var txt = Path.Combine(workspace, $"{map.Name}_stage4_alpha3_stock_resolution.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage4_alpha3_stock_resolution.json");
        var result = new NormalMapStockResolutionAuditResult
        {
            MapName = map.Name,
            Profile = analysis.Profile,
            XboxGameFolder = xboxGameFolder,
            FastFiles = files,
            IwdWeaponNames = weaponNames,
            XboxZonesScanned = zonesScanned,
            XAnimDonors = xanimMatches,
            XModelDonors = xmodelMatches,
            Warnings = warnings,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, allXAnim.Count, allXModel.Count), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static IReadOnlyList<string> ReadIwdWeaponNames(MapCandidate map)
    {
        var output = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var iwdFiles = map.SourceFolders
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.iwd", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var iwd in iwdFiles)
        {
            try
            {
                using var zip = ZipFile.OpenRead(iwd);
                foreach (var entry in zip.Entries)
                {
                    var path = entry.FullName.Replace('\\', '/');
                    const string prefix = "weapons/sp/";
                    if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = path[prefix.Length..].Trim('/');
                    if (name.Length > 0 && !name.Contains('/')) output.Add(name);
                }
            }
            catch { }
        }
        return output.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> SelectXboxDonorFastFiles(string root)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*.ff", SearchOption.AllDirectories); }
        catch { yield break; }

        foreach (var file in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file).ToLowerInvariant();
            if (name is "common.ff" or "code_post_gfx.ff" or "patch.ff" ||
                name.Contains("nazi_zombie") || name.Contains("zombie") ||
                name.Contains("dlc1") || name.Contains("dlc2") || name.Contains("dlc3") || name.Contains("dlc4"))
                yield return file;
        }
    }

    private static string NormalizeReferenceName(string name) =>
        name.StartsWith(",", StringComparison.Ordinal) ? name[1..] : name;

    private static void AddDonor(Dictionary<string, HashSet<string>> index, string name, string donor)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!index.TryGetValue(name, out var zones))
        {
            zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            index[name] = zones;
        }
        zones.Add(donor);
    }

    private static IReadOnlyList<T4StockDonorMatch> BuildMatches(
        string type,
        IEnumerable<string> needed,
        Dictionary<string, HashSet<string>> donorIndex)
    {
        var output = new List<T4StockDonorMatch>();
        foreach (var name in needed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!donorIndex.TryGetValue(name, out var zones)) continue;
            output.Add(new T4StockDonorMatch(type, name, zones.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()));
        }
        return output;
    }

    private static string Render(NormalMapStockResolutionAuditResult r, int uniqueXAnim, int uniqueXModel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 3 stock-donor resolution");
        sb.AppendLine("========================================================");
        sb.AppendLine("READ-ONLY: no map or Xbox fastfile is modified.");
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Profile: {r.Profile}");
        sb.AppendLine($"Unique PC XAnim names observed:  {uniqueXAnim:N0}");
        sb.AppendLine($"Unique PC XModel names observed: {uniqueXModel:N0}");
        sb.AppendLine($"IWD weapon source names:         {r.IwdWeaponNames.Count:N0}");
        sb.AppendLine();

        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"[{ff.Role}] {ff.Source}");
            sb.AppendLine($"  xanim  declared={ff.DeclaredXAnim:N0}, resolved names={ff.ResolvedXAnimNames:N0}");
            sb.AppendLine($"  xmodel declared={ff.DeclaredXModel:N0}, observed names={ff.ObservedXModelNames:N0}");
            foreach (var warning in ff.Warnings) sb.AppendLine("  NOTE: " + warning);
        }
        sb.AppendLine();

        sb.AppendLine("XBOX DONOR SCAN");
        sb.AppendLine($"  Xbox Game Folder: {r.XboxGameFolder ?? "<not configured>"}");
        sb.AppendLine($"  donor zones scanned: {r.XboxZonesScanned.Count:N0}");
        sb.AppendLine($"  exact XAnim name matches:  {r.XAnimDonors.Count:N0} / {uniqueXAnim:N0}");
        sb.AppendLine($"  exact XModel name matches: {r.XModelDonors.Count:N0} / {uniqueXModel:N0}");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: an exact name match is a DONOR CANDIDATE, not permission to replace the map asset. Alpha 4 will still prove source equivalence/dependencies before using a console donor.");
        sb.AppendLine();

        sb.AppendLine("XAnim donor candidates");
        foreach (var match in r.XAnimDonors)
            sb.AppendLine($"  {match.Name}  <-  {string.Join("; ", match.DonorZones.Select(Path.GetFileName))}");
        sb.AppendLine();
        sb.AppendLine("XModel donor candidates");
        foreach (var match in r.XModelDonors)
            sb.AppendLine($"  {match.Name}  <-  {string.Join("; ", match.DonorZones.Select(Path.GetFileName))}");
        sb.AppendLine();
        sb.AppendLine("IWD weapon source names");
        foreach (var name in r.IwdWeaponNames) sb.AppendLine("  " + name);
        sb.AppendLine();
        sb.AppendLine("WARNINGS");
        if (r.Warnings.Count == 0) sb.AppendLine("  (none)");
        foreach (var warning in r.Warnings) sb.AppendLine("  - " + warning);
        return sb.ToString();
    }
}
