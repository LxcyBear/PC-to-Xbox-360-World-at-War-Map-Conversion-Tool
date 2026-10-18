using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4NativeReadinessFastFile(
    string Role,
    string Source,
    string ZoneImageFile,
    int AssetCount,
    int UnhandledNonNullAssets,
    int ScriptStringAliasesPending,
    int PackedSharedTargetsPending,
    bool NestedPointerRebasePending,
    IReadOnlyDictionary<string, int> BlockersByType,
    bool HardwareReady);

public sealed class NormalMapNativeReadinessInventoryResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4NativeReadinessFastFile> FastFiles { get; init; }
    public required int UnhandledNonNullAssets { get; init; }
    public required int ScriptStringAliasesPending { get; init; }
    public required int PackedSharedTargetsPending { get; init; }
    public required bool NestedPointerRebasePending { get; init; }
    public required IReadOnlyDictionary<string, int> BlockersByType { get; init; }
    public required int HardwareReadyFastFiles { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5g / alpha 10d: exact hardware-readiness blocker inventory.
///
/// This stage does not guess at missing serializers.  It reconciles the independently
/// read-back Stage-5f native zone scaffold against the original XAsset table and records
/// every non-null asset class that is still intentionally suppressed, plus unresolved
/// script-string aliases, packed/shared targets, and nested-pointer rebasing obligations.
/// It is the single fail-closed gate used by the Stage-5h FF emitter.
/// </summary>
public static class NormalMapNativeReadinessInventory
{
    private static readonly HashSet<string> TrackedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "xanim", "xmodel", "weapon", "fx"
    };

    public static async Task<NormalMapNativeReadinessInventoryResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var fJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10c_zone_reconstruction", $"{map.Name}_stage5_alpha10c_zone_reconstruction.json");
        var bJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10a_relocation", $"{map.Name}_stage5_alpha10a_relocation.json");
        if (!File.Exists(fJson) || !File.Exists(bJson))
            throw new InvalidDataException("Stage 5g requires verified Stage 5b + Stage 5f results.");

        var f = JsonSerializer.Deserialize<NormalMapNativeZoneReconstructionResult>(
            await File.ReadAllTextAsync(fJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5f result.");
        if (!f.Verified || f.ReadBackErrors != 0)
            throw new InvalidDataException("Stage 5f is not fully verified; readiness inventory refused.");
        var b = JsonSerializer.Deserialize<NormalMapNativeRelocationAuditResult>(
            await File.ReadAllTextAsync(bJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5b result.");
        if (!b.Verified || b.InvalidPointers != 0)
            throw new InvalidDataException("Stage 5b is not fully verified; readiness inventory refused.");
        var preflightBySourceRole = b.FastFiles.ToDictionary(
            x => SourceRoleKey(x.Source, x.Role), StringComparer.OrdinalIgnoreCase);

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10d_readiness");
        Directory.CreateDirectory(workspace);
        var rows = new List<T4NativeReadinessFastFile>();
        var detail = new List<string>();
        var aggregate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var ff in f.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(ff.ZoneImageFile))
                throw new InvalidDataException($"Stage 5g cannot find Stage-5f zone image '{ff.ZoneImageFile}'.");

            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            var blockers = cached.Document.Assets
                .Where(a => !TrackedTypes.Contains(a.TypeName) && a.ReferenceKind != T4AssetReferenceKind.Null)
                .GroupBy(a => a.TypeName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var unhandled = blockers.Values.Sum();
            if (unhandled != ff.UnhandledAssetsSuppressed)
                throw new InvalidDataException(
                    $"Stage 5g blocker accounting mismatch for '{Path.GetFileName(ff.Source)}': Stage 5f suppressed {ff.UnhandledAssetsSuppressed:N0}, source table has {unhandled:N0} non-null unhandled assets.");

            foreach (var kv in blockers)
                aggregate[kv.Key] = aggregate.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;

            if (!preflightBySourceRole.TryGetValue(SourceRoleKey(ff.Source, ff.Role), out var preflight))
                throw new InvalidDataException($"Stage 5g cannot find Stage-5b preflight for '{ff.Source}' role '{ff.Role}'.");
            // Use the actual per-fastfile split-block requirement from Stage 5b. XAnim-only
            // files can legitimately be nested-clean even while another file in the same map
            // still contains XModel/Weapon/FX split-block obligations.
            var nestedPending = preflight.NestedPointerAuditPending;
            var ready = unhandled == 0 &&
                        ff.ScriptStringAliasesPending == 0 &&
                        ff.PackedSharedTargetsPending == 0 &&
                        !nestedPending;

            rows.Add(new T4NativeReadinessFastFile(
                ff.Role, ff.Source, ff.ZoneImageFile, ff.AssetCount,
                unhandled, ff.ScriptStringAliasesPending, ff.PackedSharedTargetsPending,
                nestedPending, blockers, ready));

            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: assets={ff.AssetCount:N0}; unhandledNonNull={unhandled:N0}; scriptAliasPending={ff.ScriptStringAliasesPending:N0}; sharedTargetsPending={ff.PackedSharedTargetsPending:N0}; nestedPending={nestedPending}; hardwareReady={ready}");
            foreach (var kv in blockers.Take(16))
                detail.Add($"  {kv.Key}: {kv.Value:N0}");
            if (blockers.Count > 16)
                detail.Add($"  ... {blockers.Count - 16:N0} more asset class(es)");
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10d_readiness.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10d_readiness.json");
        var result = new NormalMapNativeReadinessInventoryResult
        {
            MapName = map.Name,
            Workspace = workspace,
            FastFiles = rows,
            UnhandledNonNullAssets = rows.Sum(x => x.UnhandledNonNullAssets),
            ScriptStringAliasesPending = rows.Sum(x => x.ScriptStringAliasesPending),
            PackedSharedTargetsPending = rows.Sum(x => x.PackedSharedTargetsPending),
            NestedPointerRebasePending = rows.Any(x => x.NestedPointerRebasePending),
            BlockersByType = aggregate.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
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

    private static string Render(NormalMapNativeReadinessInventoryResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5g alpha 10d native hardware-readiness inventory");
        sb.AppendLine(new string('=', 84));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Unhandled non-null XAssets: {r.UnhandledNonNullAssets:N0}");
        sb.AppendLine($"Script-string aliases pending: {r.ScriptStringAliasesPending:N0}");
        sb.AppendLine($"Packed/shared targets pending: {r.PackedSharedTargetsPending:N0}");
        sb.AppendLine($"Nested pointer rebasing pending: {r.NestedPointerRebasePending}");
        sb.AppendLine($"Hardware-ready fastfiles: {r.HardwareReadyFastFiles:N0}/{r.FastFiles.Count:N0}");
        sb.AppendLine($"Verified accounting: {r.Verified}");
        sb.AppendLine($"Hardware ready: {r.HardwareReady}");
        sb.AppendLine();
        sb.AppendLine("AGGREGATE UNHANDLED ASSET CLASSES");
        foreach (var kv in r.BlockersByType) sb.AppendLine($"  {kv.Key,-24} {kv.Value,8:N0}");
        sb.AppendLine();
        sb.AppendLine("PER FASTFILE");
        foreach (var line in detail) sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine("Gate rule: a real .ff may be emitted only when unhandledNonNull=0, scriptAliasPending=0, sharedTargetsPending=0, and nestedPending=False for that fastfile.");
        return sb.ToString();
    }

    private static string SourceRoleKey(string source, string role) => Path.GetFullPath(source) + "|" + role;
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
