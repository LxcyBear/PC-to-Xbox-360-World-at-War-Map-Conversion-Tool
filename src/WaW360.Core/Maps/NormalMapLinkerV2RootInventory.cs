using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record LinkerV2RootRun(
    string TypeName,
    int TypeId,
    int StartAssetIndex,
    int Count,
    string ReferenceKinds);

public sealed record LinkerV2RootFastFile(
    string Role,
    string SourceFastFile,
    string GraphParticipation,
    int AssetCount,
    int PhysicalRoots,
    int InlineSharedRoots,
    int InsertRoots,
    int PackedAliasRoots,
    int NullRoots,
    int TypeRunCount,
    string RootSequenceSha256,
    IReadOnlyDictionary<string, int> AssetsByType,
    IReadOnlyList<LinkerV2RootRun> Runs);

public sealed class NormalMapLinkerV2RootInventoryResult
{
    public required string MapName { get; init; }
    public required int FastFileCount { get; init; }
    public required int TotalRoots { get; init; }
    public required int PrimaryGraphRoots { get; init; }
    public required int LoadCandidateRoots { get; init; }
    public required int PhysicalRoots { get; init; }
    public required int PackedAliasRoots { get; init; }
    public required int NullRoots { get; init; }
    public required bool ExactRootWalk { get; init; }
    public required IReadOnlyList<LinkerV2RootFastFile> FastFiles { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 authoritative source-root inventory.
///
/// This stage only trusts the parsed XAsset table. It performs no payload candidate scanning and
/// makes no identity guesses. The root sequence/family/reference-kind fingerprint is retained so
/// later V2 identity/override closure can prove that every input root was consumed exactly once.
/// </summary>
public static class NormalMapLinkerV2RootInventory
{
    public static async Task<NormalMapLinkerV2RootInventoryResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var dir = Path.Combine(outputRoot, map.Name, "linker_v2_root_inventory");
        Directory.CreateDirectory(dir);
        var txt = Path.Combine(dir, $"{map.Name}_linker_v2_root_inventory.txt");
        var json = Path.Combine(dir, $"{map.Name}_linker_v2_root_inventory.json");

        var rows = new List<LinkerV2RootFastFile>();
        foreach (var ff in source.FastFiles.OrderBy(RoleOrder).ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Path, cancellationToken);
            var doc = cached.Document;
            var ordered = doc.Assets.OrderBy(x => x.Index).ToList();
            if (ordered.Count != doc.AssetCount || ordered.Where((x, i) => x.Index != i).Any())
                throw new InvalidDataException($"Linker V2 source root table for '{ff.Path}' is not a contiguous 0..N-1 sequence.");

            var runs = BuildRuns(ordered);
            rows.Add(new LinkerV2RootFastFile(
                ff.Role,
                ff.Path,
                ff.GraphParticipation,
                doc.AssetCount,
                ordered.Count(x => x.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared),
                ordered.Count(x => x.ReferenceKind == T4AssetReferenceKind.InlineShared),
                ordered.Count(x => x.ReferenceKind == T4AssetReferenceKind.Inline),
                ordered.Count(x => x.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias),
                ordered.Count(x => x.ReferenceKind == T4AssetReferenceKind.Null),
                runs.Count,
                HashRootSequence(ordered),
                new Dictionary<string, int>(doc.AssetCountsByType, StringComparer.OrdinalIgnoreCase),
                runs));
        }

        var result = new NormalMapLinkerV2RootInventoryResult
        {
            MapName = map.Name,
            FastFileCount = rows.Count,
            TotalRoots = rows.Sum(x => x.AssetCount),
            PrimaryGraphRoots = rows.Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE").Sum(x => x.AssetCount),
            LoadCandidateRoots = rows.Where(x => x.GraphParticipation == "LOAD_CANDIDATE").Sum(x => x.AssetCount),
            PhysicalRoots = rows.Sum(x => x.PhysicalRoots),
            PackedAliasRoots = rows.Sum(x => x.PackedAliasRoots),
            NullRoots = rows.Sum(x => x.NullRoots),
            ExactRootWalk = true,
            FastFiles = rows,
            Notes = new[]
            {
                "Every row in this report comes directly from the XAsset table; no broad candidate scan is used.",
                "Root-table equality is an input-accounting invariant only. Final Xbox root counts may grow through typed dependency promotion or shrink through proven identity/override deduplication.",
                "Packed/alias source roots are preserved as explicit identities for the future relocation graph; Linker V2 does not replace them with NULL to force self-contained payloads."
            },
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private static List<LinkerV2RootRun> BuildRuns(IReadOnlyList<T4AssetEntry> assets)
    {
        var runs = new List<LinkerV2RootRun>();
        var i = 0;
        while (i < assets.Count)
        {
            var first = assets[i];
            var j = i + 1;
            while (j < assets.Count && assets[j].TypeId == first.TypeId) j++;
            var refKinds = assets.Skip(i).Take(j - i)
                .GroupBy(x => x.ReferenceKind)
                .OrderBy(x => x.Key)
                .Select(x => $"{x.Key}:{x.Count():N0}");
            runs.Add(new LinkerV2RootRun(first.TypeName, first.TypeId, first.Index, j - i, string.Join(",", refKinds)));
            i = j;
        }
        return runs;
    }

    private static string HashRootSequence(IReadOnlyList<T4AssetEntry> assets)
    {
        using var sha = SHA256.Create();
        foreach (var a in assets)
        {
            var row = Encoding.UTF8.GetBytes($"{a.Index}|{a.TypeId}|{a.TypeName}|{a.RawHeader:X8}|{a.ReferenceKind}\n");
            sha.TransformBlock(row, 0, row.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static int RoleOrder(LinkerV2SourceContainer ff) => ff.Role switch
    {
        "map.ff" => 0,
        "map_patch.ff" => 1,
        "mod.ff" => 2,
        "extra.ff" => 3,
        "load.ff" => 4,
        _ => 9
    };

    private static string Render(NormalMapLinkerV2RootInventoryResult r)
    {
        var sb = new StringBuilder(128 * 1024);
        sb.AppendLine("WaW360Converter — LINKER V2 authoritative source-root inventory");
        sb.AppendLine(new string('=', 94));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Fastfiles={r.FastFileCount:N0}; roots={r.TotalRoots:N0}; primaryGraphRoots={r.PrimaryGraphRoots:N0}; loadCandidateRoots={r.LoadCandidateRoots:N0}");
        sb.AppendLine($"Physical={r.PhysicalRoots:N0}; packedAlias={r.PackedAliasRoots:N0}; null={r.NullRoots:N0}; exactRootWalk={r.ExactRootWalk}");
        sb.AppendLine();
        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"[{ff.Role}] {Path.GetFileName(ff.SourceFastFile)} | {ff.GraphParticipation}");
            sb.AppendLine($"  assets={ff.AssetCount:N0}; physical={ff.PhysicalRoots:N0}; shared={ff.InlineSharedRoots:N0}; insert={ff.InsertRoots:N0}; packedAlias={ff.PackedAliasRoots:N0}; null={ff.NullRoots:N0}; runs={ff.TypeRunCount:N0}");
            sb.AppendLine($"  rootSequenceSha256={ff.RootSequenceSha256}");
            sb.AppendLine("  types: " + string.Join(", ", ff.AssetsByType.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => $"{x.Key}={x.Value:N0}")));
            foreach (var run in ff.Runs)
                sb.AppendLine($"  run asset#{run.StartAssetIndex:N0} +{run.Count:N0}: {run.TypeName}({run.TypeId}) [{run.ReferenceKinds}]");
        }
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        return sb.ToString();
    }
}
