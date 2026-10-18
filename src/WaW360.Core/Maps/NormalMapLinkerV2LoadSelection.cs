using System.Text;
using System.Text.Json;
using WaW360.Core.Linker;

namespace WaW360.Core.Maps;

public sealed class NormalMapLinkerV2LoadSelectionResult
{
    public required string MapName { get; init; }
    public required string? SelectedSourceFastFile { get; init; }
    public required string? PlannedOutputFastFile { get; init; }
    public required int PcRoots { get; init; }
    public required int LinkedNodes { get; init; }
    public required int PromotedDependencies { get; init; }
    public required int BlockedDependencies { get; init; }
    public required bool ExactWalk { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> OutputOrder { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 production load selector. Unlike the old batch path, only the load FF whose
/// identity matches the canonical primary map base is walked as a final package candidate.
/// Other load FFs remain source-archive inputs and do not become output files by default.
/// </summary>
public static class NormalMapLinkerV2LoadSelection
{
    public static async Task<NormalMapLinkerV2LoadSelectionResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var dir = Path.Combine(outputRoot, map.Name, "linker_v2_load");
        Directory.CreateDirectory(dir);
        var txt = Path.Combine(dir, $"{map.Name}_linker_v2_load.txt");
        var json = Path.Combine(dir, $"{map.Name}_linker_v2_load.json");
        var notes = new List<string>();

        if (source.SelectedLoadSource is null)
        {
            notes.Add(source.FastFiles.Any(x => x.GraphParticipation == "LOAD_CANDIDATE")
                ? "Load candidates exist but canonical identity is ambiguous; fail-closed selection emitted no load graph."
                : "No load.ff exists for this map; this is valid for maps whose package has no separate load fastfile.");
            var none = new NormalMapLinkerV2LoadSelectionResult
            {
                MapName = map.Name,
                SelectedSourceFastFile = null,
                PlannedOutputFastFile = null,
                PcRoots = 0,
                LinkedNodes = 0,
                PromotedDependencies = 0,
                BlockedDependencies = 0,
                ExactWalk = true,
                Verified = !source.FastFiles.Any(x => x.GraphParticipation == "LOAD_CANDIDATE"),
                OutputOrder = Array.Empty<string>(),
                Notes = notes,
                TextReportFile = txt,
                JsonReportFile = json
            };
            await WriteAsync(none, cancellationToken);
            return none;
        }

        var graph = await T4LoadFastFileLinker.BuildAsync(source.SelectedLoadSource, cancellationToken);
        var order = graph.OutputOrder.Select(x =>
        {
            var deps = x.Dependencies.Count == 0 ? string.Empty : " | deps=" + string.Join(", ", x.Dependencies);
            var block = string.IsNullOrWhiteSpace(x.BlockReason) ? string.Empty : " | BLOCK=" + x.BlockReason;
            return $"#{x.OutputIndex} xboxType={x.XboxTypeId} {x.TypeName}:{x.Name} [{x.Origin}/{x.State}]{deps}{block}";
        }).ToList();
        var blocked = graph.OutputOrder.Count(x => !string.IsNullOrWhiteSpace(x.BlockReason));
        notes.Add("The existing seven-node Xbox load serializer/read-back is hardware-proven by Aztec; Linker V2 reuses that typed knowledge but changes source selection/package semantics.");
        if (source.GoldenOracleApplied && graph.LinkedAssetCount == 7)
            notes.Add("Golden-reference shape agrees: selected working Xbox load package uses seven canonical assets.");

        var result = new NormalMapLinkerV2LoadSelectionResult
        {
            MapName = map.Name,
            SelectedSourceFastFile = source.SelectedLoadSource,
            PlannedOutputFastFile = source.LoadOutputFastFile,
            PcRoots = graph.SourceAssetCount,
            LinkedNodes = graph.LinkedAssetCount,
            PromotedDependencies = graph.PromotedNestedAssetCount,
            BlockedDependencies = blocked,
            ExactWalk = graph.ExactSourceWalk,
            Verified = graph.Verified,
            OutputOrder = order,
            Notes = notes,
            TextReportFile = txt,
            JsonReportFile = json
        };
        await WriteAsync(result, cancellationToken);
        return result;
    }

    private static async Task WriteAsync(NormalMapLinkerV2LoadSelectionResult r, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 selected canonical load graph");
        sb.AppendLine(new string('=', 82));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Selected source: {r.SelectedSourceFastFile ?? "<none>"}");
        sb.AppendLine($"Planned output: {r.PlannedOutputFastFile ?? "<none>"}");
        sb.AppendLine($"PC roots={r.PcRoots:N0}; linked={r.LinkedNodes:N0}; promoted={r.PromotedDependencies:N0}; blockedDeps={r.BlockedDependencies:N0}; exactWalk={r.ExactWalk}; verified={r.Verified}");
        foreach (var row in r.OutputOrder) sb.AppendLine("  " + row);
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        await File.WriteAllTextAsync(r.TextReportFile, sb.ToString(), new UTF8Encoding(false), ct);
        await File.WriteAllTextAsync(r.JsonReportFile, JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), ct);
    }
}
