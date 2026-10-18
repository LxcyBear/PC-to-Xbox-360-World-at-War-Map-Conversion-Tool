using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record LinkerV2DestinationSourceBlockSummary(
    string Role,
    string SourceFastFile,
    IReadOnlyDictionary<string, uint> SourceBlockSizes);

public sealed class NormalMapLinkerV2DestinationBlockPlanResult
{
    public required string MapName { get; init; }
    public required IReadOnlyList<LinkerV2DestinationSourceBlockSummary> Sources { get; init; }
    public required IReadOnlyDictionary<string, uint> StructuralCapacities { get; init; }
    public required IReadOnlyDictionary<string, uint>? GoldenBlockSizes { get; init; }
    public required bool UsesGoldenForAllocation { get; init; }
    public required bool SourceCapacityPlanReady { get; init; }
    public required bool ProductionOwnershipComplete { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 5 destination block-capacity plan.
///
/// This fixes the old smoke writer's most suspicious header behavior: it no longer writes the
/// same whole-zone capacity into all seven XFile blocks. For a STRUCTURAL smoke candidate the
/// allocator conservatively sums the real source XFile capacities by semantic block, while
/// runtime payload blocks intentionally remain empty unless a V2 serializer actually emits runtime
/// bytes. Golden block tables are validation-only and are never copied into the plan.
///
/// Production shared-object placement is a stricter problem and remains separate: this stage only
/// establishes a non-fabricated per-block capacity contract suitable for self-contained structural
/// payloads.
/// </summary>
public static class NormalMapLinkerV2DestinationBlockPlan
{
    public static async Task<NormalMapLinkerV2DestinationBlockPlanResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null,
        CancellationToken cancellationToken = default)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_block_plan");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_block_plan.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_block_plan.json");

        var primary = source.FastFiles
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .OrderBy(x => LinkerV2ProofEvidence.RoleRank(x.Role))
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totals64 = Enum.GetValues<T4ZoneBlock>().ToDictionary(x => x, _ => 0UL);
        var rows = new List<LinkerV2DestinationSourceBlockSummary>();
        var blockers = new List<string>();

        foreach (var ff in primary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Path, cancellationToken);
            var dict = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in Enum.GetValues<T4ZoneBlock>())
            {
                var size = cached.Document.Header.BlockSizes[(int)block];
                dict[block.ToString()] = size;
                totals64[block] += size;
            }
            rows.Add(new LinkerV2DestinationSourceBlockSummary(ff.Role, ff.Path, dict));
        }

        // Structural V2 payloads currently contain no image/audio runtime arena. Runtime itself is
        // reservation-only and may remain conservatively sized; LargeRuntime/PhysicalRuntime imply
        // serialized runtime bytes and therefore MUST be zero until the emitter appends them.
        totals64[T4ZoneBlock.LargeRuntime] = 0;
        totals64[T4ZoneBlock.PhysicalRuntime] = 0;

        var capacities = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in Enum.GetValues<T4ZoneBlock>())
        {
            var total = totals64[block];
            if (total > uint.MaxValue)
            {
                blockers.Add($"Summed source capacity for {block} exceeds the 32-bit T4 block field ({total:N0} bytes).");
                capacities[block.ToString()] = uint.MaxValue;
            }
            else
            {
                capacities[block.ToString()] = (uint)total;
            }
        }

        var golden = LinkerV2GoldenCatalog.Find(source.CanonicalBaseName);
        var goldenBlocks = golden?.MainBlockSizes.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>
        {
            "Structural capacities are exact sums of the real source XFile seven-block tables; no working-converter block table is copied and no blanket alignment is imposed (real Xbox Virtual capacities need not be 16-byte multiples).",
            "LargeRuntime and PhysicalRuntime are forced to zero for the silent/self-contained structural emitter because it appends no runtime arena bytes.",
            "Runtime is reservation-only and may remain non-zero without adding serialized bytes; this matches the shape observed in real Xbox zones.",
            "Production shared-target ownership/placement remains a stricter gate than this structural capacity plan."
        };
        if (goldenBlocks is not null)
            notes.Add("Golden block sizes are recorded only for post-plan validation/diffing. They do not influence StructuralCapacities.");

        var ready = blockers.Count == 0 && capacities.Count == 7;
        var result = new NormalMapLinkerV2DestinationBlockPlanResult
        {
            MapName = map.Name,
            Sources = rows,
            StructuralCapacities = capacities,
            GoldenBlockSizes = goldenBlocks,
            UsesGoldenForAllocation = false,
            SourceCapacityPlanReady = ready,
            ProductionOwnershipComplete = false,
            Notes = notes,
            Blockers = blockers,
            Verified = ready,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private static string Render(NormalMapLinkerV2DestinationBlockPlanResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 5 destination block-capacity plan");
        sb.AppendLine(new string('=', 98));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"sourceCapacityPlanReady={r.SourceCapacityPlanReady}; productionOwnershipComplete={r.ProductionOwnershipComplete}; usesGoldenForAllocation={r.UsesGoldenForAllocation}; verified={r.Verified}");
        sb.AppendLine();
        sb.AppendLine("STRUCTURAL DESTINATION CAPACITIES");
        foreach (var block in Enum.GetValues<T4ZoneBlock>())
            sb.AppendLine($"  {block,-18} {r.StructuralCapacities.GetValueOrDefault(block.ToString()),14:N0} B  0x{r.StructuralCapacities.GetValueOrDefault(block.ToString()):X8}");
        if (r.GoldenBlockSizes is not null)
        {
            sb.AppendLine();
            sb.AppendLine("GOLDEN VALIDATION ONLY (NOT USED FOR ALLOCATION)");
            foreach (var block in Enum.GetValues<T4ZoneBlock>())
                sb.AppendLine($"  {block,-18} {r.GoldenBlockSizes.GetValueOrDefault(block.ToString()),14:N0} B");
        }
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        foreach (var blocker in r.Blockers) sb.AppendLine("BLOCKER: " + blocker);
        return sb.ToString();
    }
}
