using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4MixedOrderFastFileProbe(
    string Role,
    string Source,
    string ReportFile,
    int ExpectedAssets,
    int ObservedCandidates,
    int MissingFx,
    int MissingXModel,
    int SkippedXModel,
    bool UsedWeaponProof);

public sealed class NormalMapMixedAssetOrderProbeResult
{
    public required string MapName { get; init; }
    public required string Profile { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4MixedOrderFastFileProbe> FastFiles { get; init; }
    public required int MissingFx { get; init; }
    public required int MissingXModel { get; init; }
    public required int SkippedXModel { get; init; }
    public required int TightFxWindows { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

public static class NormalMapMixedAssetOrderProbe
{
    public static async Task<NormalMapMixedAssetOrderProbeResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        if (!analysis.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{map.Name}' classified as {analysis.Profile}. Alpha 9a mixed-order probe currently targets Normal maps.");

        var candidates = analysis.FastFiles
            .Where(ff => ff.Readable && ff.AssetTypes.TryGetValue("fx", out var count) && count > 0)
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidDataException($"'{map.Name}' has no readable fastfiles declaring FX assets for the mixed-order probe.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage4_alpha9a_mixed_order");
        Directory.CreateDirectory(workspace);
        var rows = new List<T4MixedOrderFastFileProbe>();
        var fullResults = new List<T4MixedAssetOrderProbeResult>();

        foreach (var ff in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stem = Path.GetFileNameWithoutExtension(ff.Path) + $"_{SanitizeRole(ff.Role)}";
            var xanimProof = Path.Combine(outputRoot, map.Name, "stage4_alpha4_xanim", stem + ".xbox_xanim_proof.bin");
            var weaponProof = Path.Combine(outputRoot, map.Name, "stage4_alpha7b_weapon_owned", stem + ".xbox_weapon_owned_proof.bin");
            var report = Path.Combine(workspace, stem + ".mixed_xasset_order.txt");
            var result = await T4MixedAssetOrderProbe.RunAsync(ff.Path, xanimProof, weaponProof, report, cancellationToken);
            fullResults.Add(result);
            rows.Add(new T4MixedOrderFastFileProbe(
                ff.Role,
                ff.Path,
                report,
                result.ExpectedAssets,
                result.ObservedCandidates,
                Get(result.MissingByType, "fx"),
                Get(result.MissingByType, "xmodel"),
                Get(result.SkippedByType, "xmodel"),
                File.Exists(weaponProof)));
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage4_alpha9a_mixed_order.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage4_alpha9a_mixed_order.json");
        var resultAll = new NormalMapMixedAssetOrderProbeResult
        {
            MapName = map.Name,
            Profile = analysis.Profile,
            Workspace = workspace,
            FastFiles = rows,
            MissingFx = rows.Sum(r => r.MissingFx),
            MissingXModel = rows.Sum(r => r.MissingXModel),
            SkippedXModel = rows.Sum(r => r.SkippedXModel),
            TightFxWindows = fullResults.Sum(r => r.MissingFxWindows.Count(w => w.WindowStart.HasValue && w.WindowEnd.HasValue && w.WindowEnd.Value >= w.WindowStart.Value)),
            TextReportFile = txt,
            JsonReportFile = json
        };
        await File.WriteAllTextAsync(txt, Render(resultAll), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(resultAll, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return resultAll;
    }

    private static int Get(IReadOnlyDictionary<string, int> d, string k) => d.TryGetValue(k, out var n) ? n : 0;
    private static string SanitizeRole(string role) => new string(role.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    private static string Render(NormalMapMixedAssetOrderProbeResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 9a mixed XAsset-order probe");
        sb.AppendLine("===========================================================");
        sb.AppendLine("DIAGNOSTIC ONLY: no new asset bodies are accepted by this stage.");
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Profile: {r.Profile}");
        sb.AppendLine();
        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"[{ff.Role}] {ff.Source}");
            sb.AppendLine($"  tracked expected/candidates: {ff.ExpectedAssets:N0}/{ff.ObservedCandidates:N0}");
            sb.AppendLine($"  missing FX after mixed alignment: {ff.MissingFx:N0}");
            sb.AppendLine($"  missing/skipped XModel: {ff.MissingXModel:N0}/{ff.SkippedXModel:N0}");
            sb.AppendLine($"  Weapon proof anchor: {(ff.UsedWeaponProof ? "yes" : "NO")}");
            sb.AppendLine($"  detail: {ff.ReportFile}");
            sb.AppendLine();
        }
        sb.AppendLine("TOTALS");
        sb.AppendLine($"  missing FX slots: {r.MissingFx:N0}");
        sb.AppendLine($"  missing XModel slots: {r.MissingXModel:N0}");
        sb.AppendLine($"  skipped XModel candidates: {r.SkippedXModel:N0}");
        sb.AppendLine($"  bounded missing-FX windows: {r.TightFxWindows:N0}");
        return sb.ToString();
    }
}
