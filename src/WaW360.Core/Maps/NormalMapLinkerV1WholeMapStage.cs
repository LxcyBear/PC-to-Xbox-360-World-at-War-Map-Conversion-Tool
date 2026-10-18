using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4WholeMapRootRun(
    string TypeName,
    int TypeId,
    int StartAssetIndex,
    int Count,
    string ReferenceKinds);

public sealed record T4WholeMapFastFilePlan(
    string Role,
    string SourceFastFile,
    int AssetCount,
    int PhysicalRootAssets,
    int AliasRootAssets,
    int NullRootAssets,
    int TypeRunCount,
    IReadOnlyDictionary<string, int> AssetsByType,
    IReadOnlyList<T4WholeMapRootRun> RootRuns,
    string RootSequenceSha256,
    bool XAssetTableWalkVerified,
    bool DependencyClosureComplete,
    IReadOnlyList<string> Blockers);

public sealed class NormalMapLinkerV1WholeMapResult
{
    public required string MapName { get; init; }
    public required int FastFileCount { get; init; }
    public required int TotalRootAssets { get; init; }
    public required int PhysicalRootAssets { get; init; }
    public required int AliasRootAssets { get; init; }
    public required int NullRootAssets { get; init; }
    public required int RootRunCount { get; init; }
    public required bool ExactRootWalkVerified { get; init; }
    public required bool DependencyClosureComplete { get; init; }
    public required IReadOnlyList<T4WholeMapFastFilePlan> FastFiles { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V1 alpha 4 foundation: authoritative whole-map XAsset root-table walk.
/// This stage deliberately does not scan payload bytes. Every row comes directly from
/// the parsed XAssetList in serialized order. Unknown asset identities remain explicit
/// blockers rather than being guessed from nearby byte patterns.
/// </summary>
public static class NormalMapLinkerV1WholeMapStage
{
    public static async Task<NormalMapLinkerV1WholeMapResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        var inputs = analysis.InputFiles
            .Where(x => x.Path.EndsWith(".ff", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => RoleOrder(x.Role))
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dir = Path.Combine(outputRoot, map.Name, "linker_v1_alpha4_wholemap");
        Directory.CreateDirectory(dir);
        var txt = Path.Combine(dir, $"{map.Name}_linker_v1_alpha4_wholemap_roots.txt");
        var json = Path.Combine(dir, $"{map.Name}_linker_v1_alpha4_wholemap_roots.json");
        var rows = new List<T4WholeMapFastFilePlan>();
        var warnings = new List<string>();

        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(input.Path, cancellationToken);
            var doc = cached.Document;
            var ordered = doc.Assets.OrderBy(a => a.Index).ToList();
            if (ordered.Count != doc.AssetCount || ordered.Where((a, i) => a.Index != i).Any())
                throw new InvalidDataException($"Whole-map XAsset table for '{input.Path}' is not a contiguous 0..N-1 sequence.");

            var physical = ordered.Count(a => a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared);
            var aliases = ordered.Count(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias);
            var nulls = ordered.Count(a => a.ReferenceKind == T4AssetReferenceKind.Null);
            var runs = BuildRuns(ordered);
            var sequenceHash = HashRootSequence(ordered);
            var blockers = new List<string>();

            if (aliases != 0)
                blockers.Add($"{aliases:N0} top-level packed/alias XAsset row(s) require deterministic target resolution before dependency closure is complete.");
            if (nulls != 0)
                blockers.Add($"{nulls:N0} null XAsset row(s) are preserved in the authoritative root plan and require role-specific handling.");
            if (physical != 0)
                blockers.Add($"Asset identity/owned-pointer closure for {physical:N0} physical root(s) is the next linker pass; alpha 4 refuses broad candidate scans.");

            rows.Add(new T4WholeMapFastFilePlan(
                input.Role,
                input.Path,
                doc.AssetCount,
                physical,
                aliases,
                nulls,
                runs.Count,
                doc.AssetCountsByType,
                runs,
                sequenceHash,
                XAssetTableWalkVerified: true,
                DependencyClosureComplete: doc.AssetCount == 0,
                blockers));
        }

        if (inputs.Count == 0)
            warnings.Add("No fastfiles were discovered; whole-map linker root planning is a no-op.");

        var result = new NormalMapLinkerV1WholeMapResult
        {
            MapName = map.Name,
            FastFileCount = rows.Count,
            TotalRootAssets = rows.Sum(x => x.AssetCount),
            PhysicalRootAssets = rows.Sum(x => x.PhysicalRootAssets),
            AliasRootAssets = rows.Sum(x => x.AliasRootAssets),
            NullRootAssets = rows.Sum(x => x.NullRootAssets),
            RootRunCount = rows.Sum(x => x.TypeRunCount),
            ExactRootWalkVerified = rows.All(x => x.XAssetTableWalkVerified),
            DependencyClosureComplete = rows.Count != 0 && rows.All(x => x.DependencyClosureComplete),
            FastFiles = rows,
            Warnings = warnings,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static List<T4WholeMapRootRun> BuildRuns(IReadOnlyList<T4AssetEntry> assets)
    {
        var runs = new List<T4WholeMapRootRun>();
        if (assets.Count == 0) return runs;

        var start = 0;
        for (var i = 1; i <= assets.Count; i++)
        {
            var close = i == assets.Count || assets[i].TypeId != assets[start].TypeId;
            if (!close) continue;
            var slice = assets.Skip(start).Take(i - start).ToList();
            var kinds = string.Join(",", slice.GroupBy(x => x.ReferenceKind)
                .OrderBy(x => x.Key.ToString(), StringComparer.Ordinal)
                .Select(g => $"{g.Key}:{g.Count()}"));
            runs.Add(new T4WholeMapRootRun(
                assets[start].TypeName,
                assets[start].TypeId,
                assets[start].Index,
                i - start,
                kinds));
            start = i;
        }
        return runs;
    }

    private static string HashRootSequence(IReadOnlyList<T4AssetEntry> assets)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        foreach (var a in assets)
        {
            writer.Write(a.Index);
            writer.Write(a.TypeId);
            writer.Write(a.TypeName);
            writer.Write(a.RawHeader);
            writer.Write((int)a.ReferenceKind);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
    }

    private static int RoleOrder(string role) => role switch
    {
        "map.ff" => 0,
        "map_patch.ff" => 1,
        "mod.ff" => 2,
        "load.ff" => 3,
        _ => 9
    };

    private static string Render(NormalMapLinkerV1WholeMapResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Linker V1 alpha 4 whole-map deterministic XAsset root plan");
        sb.AppendLine(new string('=', 87));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Fastfiles: {r.FastFileCount:N0}");
        sb.AppendLine($"Root XAssets: {r.TotalRootAssets:N0} (physical={r.PhysicalRootAssets:N0}, alias={r.AliasRootAssets:N0}, null={r.NullRootAssets:N0})");
        sb.AppendLine($"Contiguous type runs: {r.RootRunCount:N0}");
        sb.AppendLine($"Exact XAsset-table walk verified: {r.ExactRootWalkVerified}");
        sb.AppendLine($"Dependency closure complete: {r.DependencyClosureComplete}");
        sb.AppendLine();
        sb.AppendLine("POLICY");
        sb.AppendLine("  This plan is derived only from the authoritative XAssetList order. No broad payload candidate scan is used.");
        sb.AppendLine("  Alpha 4 records unresolved identity/ownership as blockers instead of inventing names or offsets.");
        sb.AppendLine("  The next whole-map linker pass resolves each physical root by type and walks owned dependencies from that root.");

        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine();
            sb.AppendLine($"[{ff.Role}] {Path.GetFileName(ff.SourceFastFile)}");
            sb.AppendLine($"  assets={ff.AssetCount:N0}; physical={ff.PhysicalRootAssets:N0}; alias={ff.AliasRootAssets:N0}; null={ff.NullRootAssets:N0}; runs={ff.TypeRunCount:N0}");
            sb.AppendLine($"  rootSequenceSha256={ff.RootSequenceSha256}");
            sb.AppendLine("  types: " + string.Join(", ", ff.AssetsByType.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => $"{x.Key}={x.Value:N0}")));
            foreach (var run in ff.RootRuns.Take(128))
                sb.AppendLine($"  run asset#{run.StartAssetIndex:N0} +{run.Count:N0}: {run.TypeName}({run.TypeId}) [{run.ReferenceKinds}]");
            if (ff.RootRuns.Count > 128) sb.AppendLine($"  ... {ff.RootRuns.Count - 128:N0} more run(s)");
            foreach (var blocker in ff.Blockers) sb.AppendLine("  BLOCKER: " + blocker);
        }
        foreach (var warning in r.Warnings) sb.AppendLine("WARNING: " + warning);
        return sb.ToString();
    }
}
