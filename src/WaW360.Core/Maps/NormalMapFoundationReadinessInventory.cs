using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4FoundationReadinessFastFile(
    string Role,
    string Source,
    string ZoneImageFile,
    int AssetCount,
    int FoundationAssetsPromoted,
    int UnhandledNonNullAssets,
    int ScriptStringAliasesPending,
    int PackedSharedTargetsPending,
    bool NestedPointerRebasePending,
    IReadOnlyDictionary<string, int> BlockersByType,
    bool MaterialTechniqueCandidate,
    bool HardwareReady);

public sealed class NormalMapFoundationReadinessInventoryResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4FoundationReadinessFastFile> FastFiles { get; init; }
    public required int FoundationAssetsPromoted { get; init; }
    public required int UnhandledNonNullAssets { get; init; }
    public required int ScriptStringAliasesPending { get; init; }
    public required int PackedSharedTargetsPending { get; init; }
    public required bool NestedPointerRebasePending { get; init; }
    public required IReadOnlyDictionary<string, int> BlockersByType { get; init; }
    public required int MaterialTechniqueCandidateFastFiles { get; init; }
    public required int HardwareReadyFastFiles { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5j / alpha 10e: recomputes the hardware-readiness gate after Stage 5i promotes
/// RawFile, LocalizeEntry, and conservative StringTable payloads into the native zone.
/// Only exact XAsset indices actually emitted by the foundation serializer are removed
/// from the blocker set; aliases or ambiguous scans stay blocked.
///
/// The stage also highlights tiny fastfiles whose only remaining blockers are Material
/// and MaterialTechniqueSet. Those are the highest-value next targets because they have
/// no shared-target/nested-pointer obligations left outside that serializer family.
/// </summary>
public static class NormalMapFoundationReadinessInventory
{
    private static readonly HashSet<string> TrackedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "xanim", "xmodel", "weapon", "fx"
    };

    public static async Task<NormalMapFoundationReadinessInventoryResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var iJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10e_foundation_promotion", $"{map.Name}_stage5_alpha10e_foundation_promotion.json");
        var bJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10a_relocation", $"{map.Name}_stage5_alpha10a_relocation.json");
        if (!File.Exists(iJson) || !File.Exists(bJson))
            throw new InvalidDataException("Stage 5j requires verified Stage 5b + Stage 5i results.");

        var promoted = JsonSerializer.Deserialize<NormalMapFoundationAssetPromotionResult>(
            await File.ReadAllTextAsync(iJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5i result.");
        if (!promoted.Verified || promoted.ReadBackErrors != 0)
            throw new InvalidDataException("Stage 5i is not fully verified; foundation-aware readiness inventory refused.");

        var preflight = JsonSerializer.Deserialize<NormalMapNativeRelocationAuditResult>(
            await File.ReadAllTextAsync(bJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5b result.");
        if (!preflight.Verified || preflight.InvalidPointers != 0)
            throw new InvalidDataException("Stage 5b is not fully verified; foundation-aware readiness inventory refused.");

        var preflightBySourceRole = preflight.FastFiles.ToDictionary(
            x => SourceRoleKey(x.Source, x.Role), StringComparer.OrdinalIgnoreCase);

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10e_readiness");
        Directory.CreateDirectory(workspace);
        var rows = new List<T4FoundationReadinessFastFile>();
        var detail = new List<string>();
        var aggregate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var ff in promoted.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(ff.ZoneImageFile))
                throw new InvalidDataException($"Stage 5j cannot find Stage-5i zone image '{ff.ZoneImageFile}'.");

            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            var simple = T4SimpleNativeAssetSerializer.Build(cached.Document, cached.Bytes);
            var emittedSimple = simple.AssetsByIndex.Keys.ToHashSet();

            var blockers = cached.Document.Assets
                .Where(a => a.ReferenceKind != T4AssetReferenceKind.Null)
                .Where(a => !TrackedTypes.Contains(a.TypeName))
                .Where(a => !emittedSimple.Contains(a.Index))
                .GroupBy(a => a.TypeName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var unhandled = blockers.Values.Sum();
            if (unhandled != ff.UnhandledAssetsSuppressed)
                throw new InvalidDataException(
                    $"Stage 5j blocker accounting mismatch for '{Path.GetFileName(ff.Source)}': Stage 5i suppressed {ff.UnhandledAssetsSuppressed:N0}, exact source-index accounting has {unhandled:N0} blocker(s).");

            foreach (var kv in blockers)
                aggregate[kv.Key] = aggregate.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;

            if (!preflightBySourceRole.TryGetValue(SourceRoleKey(ff.Source, ff.Role), out var b))
                throw new InvalidDataException($"Stage 5j cannot find Stage-5b preflight for '{ff.Source}' role '{ff.Role}'.");

            var nestedPending = b.NestedPointerAuditPending;
            var ready = unhandled == 0 &&
                        ff.ScriptStringAliasesPending == 0 &&
                        ff.PackedSharedTargetsPending == 0 &&
                        !nestedPending;

            var materialTechniqueOnly = unhandled > 0 &&
                                        ff.ScriptStringAliasesPending == 0 &&
                                        ff.PackedSharedTargetsPending == 0 &&
                                        !nestedPending &&
                                        blockers.Keys.All(k =>
                                            k.Equals("material", StringComparison.OrdinalIgnoreCase) ||
                                            k.Equals("techset", StringComparison.OrdinalIgnoreCase));

            rows.Add(new T4FoundationReadinessFastFile(
                ff.Role, ff.Source, ff.ZoneImageFile, ff.AssetCount, ff.FoundationPhysicalPayloads,
                unhandled, ff.ScriptStringAliasesPending, ff.PackedSharedTargetsPending,
                nestedPending, blockers, materialTechniqueOnly, ready));

            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: assets={ff.AssetCount:N0}; foundationPromoted={ff.FoundationPhysicalPayloads:N0}; unhandledNonNull={unhandled:N0}; scriptAliasPending={ff.ScriptStringAliasesPending:N0}; sharedTargetsPending={ff.PackedSharedTargetsPending:N0}; nestedPending={nestedPending}; materialTechCandidate={materialTechniqueOnly}; hardwareReady={ready}");
            foreach (var kv in blockers.Take(16))
                detail.Add($"  {kv.Key}: {kv.Value:N0}");
            if (blockers.Count > 16)
                detail.Add($"  ... {blockers.Count - 16:N0} more asset class(es)");
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10e_readiness.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10e_readiness.json");
        var result = new NormalMapFoundationReadinessInventoryResult
        {
            MapName = map.Name,
            Workspace = workspace,
            FastFiles = rows,
            FoundationAssetsPromoted = rows.Sum(x => x.FoundationAssetsPromoted),
            UnhandledNonNullAssets = rows.Sum(x => x.UnhandledNonNullAssets),
            ScriptStringAliasesPending = rows.Sum(x => x.ScriptStringAliasesPending),
            PackedSharedTargetsPending = rows.Sum(x => x.PackedSharedTargetsPending),
            NestedPointerRebasePending = rows.Any(x => x.NestedPointerRebasePending),
            BlockersByType = aggregate.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            MaterialTechniqueCandidateFastFiles = rows.Count(x => x.MaterialTechniqueCandidate),
            HardwareReadyFastFiles = rows.Count(x => x.HardwareReady),
            Verified = true,
            HardwareReady = rows.Count != 0 && rows.All(x => x.HardwareReady),
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static string Render(NormalMapFoundationReadinessInventoryResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5j alpha 10e foundation-aware hardware readiness");
        sb.AppendLine(new string('=', 86));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Foundation assets promoted: {r.FoundationAssetsPromoted:N0}");
        sb.AppendLine($"Unhandled non-null XAssets remaining: {r.UnhandledNonNullAssets:N0}");
        sb.AppendLine($"Script-string aliases pending: {r.ScriptStringAliasesPending:N0}");
        sb.AppendLine($"Packed/shared targets pending: {r.PackedSharedTargetsPending:N0}");
        sb.AppendLine($"Nested pointer rebasing pending: {r.NestedPointerRebasePending}");
        sb.AppendLine($"Material/Techset-only candidate fastfiles: {r.MaterialTechniqueCandidateFastFiles:N0}");
        sb.AppendLine($"Hardware-ready fastfiles: {r.HardwareReadyFastFiles:N0}/{r.FastFiles.Count:N0}");
        sb.AppendLine($"Verified accounting: {r.Verified}");
        sb.AppendLine($"Hardware ready: {r.HardwareReady}");
        sb.AppendLine();
        sb.AppendLine("AGGREGATE REMAINING BLOCKER CLASSES");
        foreach (var kv in r.BlockersByType) sb.AppendLine($"  {kv.Key,-24} {kv.Value,8:N0}");
        if (r.BlockersByType.Count == 0) sb.AppendLine("  (none)");
        sb.AppendLine();
        sb.AppendLine("PER FASTFILE");
        foreach (var line in detail) sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine("Candidate rule: Material/Techset-only means the fastfile has zero script aliases, zero tracked shared-target obligations, zero tracked nested-pointer obligations, and every remaining non-null blocker belongs to Material or MaterialTechniqueSet.");
        sb.AppendLine("Hardware gate rule remains strict: unhandledNonNull=0, scriptAliasPending=0, sharedTargetsPending=0, nestedPending=False.");
        return sb.ToString();
    }

    private static string SourceRoleKey(string source, string role) => Path.GetFullPath(source) + "|" + role;
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
