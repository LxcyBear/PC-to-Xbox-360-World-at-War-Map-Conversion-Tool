using System.Text;
using System.Text.Json;

namespace WaW360.Core.Maps;

public sealed record LinkerV2FamilyConvergence(
    string AssetType,
    int SourceAggregateRoots,
    int? GoldenXboxRoots,
    int? Delta,
    string RequiredAction,
    bool CountStableAgainstOracle);

public sealed class NormalMapLinkerV2CanonicalConvergenceResult
{
    public required string MapName { get; init; }
    public required string CanonicalBaseName { get; init; }
    public required int SourceAggregateRoots { get; init; }
    public required int SourceAggregateScriptStrings { get; init; }
    public required bool GoldenOracleApplied { get; init; }
    public required string? GoldenOracleName { get; init; }
    public required int? GoldenMainRoots { get; init; }
    public required int? GoldenMainScriptStrings { get; init; }
    public required int? NetRootDelta { get; init; }
    public required int? AbsoluteFamilyEdits { get; init; }
    public required int? ScriptStringDelta { get; init; }
    public required int StableFamilies { get; init; }
    public required int CanonicalizeFamilies { get; init; }
    public required int PromoteFamilies { get; init; }
    public required IReadOnlyList<LinkerV2FamilyConvergence> Families { get; init; }
    public required IReadOnlyDictionary<string, uint>? GoldenBlockContract { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 4 canonical-convergence contract.
///
/// This is intentionally NOT a rewrite stage.  It converts the raw multi-container source
/// aggregate into an explicit production target contract.  When a known-working golden package
/// exists (Simpsons/Aztec), family-count deltas are validation oracles only: they tell the typed
/// linker which families must converge through proven identity/override resolution versus owned
/// dependency promotion.  They are never used to synthesize or delete an asset by count alone.
///
/// Unknown maps remain fully generic: every source family is carried forward as identity/closure
/// pending until typed walkers resolve concrete names and dependencies.
/// </summary>
public static class NormalMapLinkerV2CanonicalConvergence
{
    public static async Task<NormalMapLinkerV2CanonicalConvergenceResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null,
        CancellationToken cancellationToken = default)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var golden = LinkerV2GoldenCatalog.Find(source.CanonicalBaseName);

        var types = new HashSet<string>(source.PrimaryGraphRootCounts.Keys, StringComparer.OrdinalIgnoreCase);
        if (golden is not null)
            foreach (var type in golden.MainAssetCounts.Keys) types.Add(type);

        var families = new List<LinkerV2FamilyConvergence>();
        foreach (var type in types.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var sourceCount = source.PrimaryGraphRootCounts.TryGetValue(type, out var s) ? s : 0;
            if (golden is null)
            {
                families.Add(new LinkerV2FamilyConvergence(
                    type, sourceCount, null, null,
                    "IDENTITY_AND_TYPED_CLOSURE_PENDING",
                    false));
                continue;
            }

            var target = golden.MainAssetCounts.TryGetValue(type, out var g) ? g : 0;
            var delta = target - sourceCount;
            var action = delta switch
            {
                0 => "COUNT_STABLE__PRESERVE_IDENTITY_AND_DEPENDENCIES",
                < 0 => "PROVEN_IDENTITY_DEDUP_OVERRIDE_OR_CANONICALIZATION_REQUIRED",
                > 0 => "TYPED_DEPENDENCY_PROMOTION_REQUIRED"
            };
            families.Add(new LinkerV2FamilyConvergence(type, sourceCount, target, delta, action, delta == 0));
        }

        int? netRootDelta = golden is null ? null : golden.MainAssets - source.PrimaryGraphSourceRoots;
        int? absoluteEdits = golden is null ? null : families.Sum(x => Math.Abs(x.Delta ?? 0));
        int? stringDelta = golden is null ? null : golden.MainScriptStrings - source.PrimaryGraphSourceStrings;
        var blockers = new List<string>();
        var notes = new List<string>
        {
            "Counts are validation contracts only. Linker V2 must resolve concrete asset identity and typed dependencies before adding/removing any root.",
            "Container role is provenance. No family is copied or discarded merely because it came from map.ff, patch.ff, mod.ff or IWD."
        };

        if (golden is null)
        {
            blockers.Add("No golden oracle exists for this map; canonical target counts remain intentionally unknown until typed identity/closure walkers resolve the graph.");
            notes.Add("GENERIC MAP: source aggregate counts are an input invariant, not an Xbox target count.");
        }
        else
        {
            blockers.Add("Golden family deltas identify where convergence must occur, but they do not prove which concrete asset identities win or which dependencies are promoted.");
            notes.Add($"GOLDEN CONVERGENCE: raw primary roots={source.PrimaryGraphSourceRoots:N0}; working Xbox roots={golden.MainAssets:N0}; net delta={netRootDelta:+#;-#;0}; absolute family edits={absoluteEdits:N0}.");
            notes.Add($"GOLDEN STRINGS: raw source aggregate={source.PrimaryGraphSourceStrings:N0}; working Xbox={golden.MainScriptStrings:N0}; delta={stringDelta:+#;-#;0}.");
        }

        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_convergence");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_convergence.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_convergence.json");

        var result = new NormalMapLinkerV2CanonicalConvergenceResult
        {
            MapName = map.Name,
            CanonicalBaseName = source.CanonicalBaseName,
            SourceAggregateRoots = source.PrimaryGraphSourceRoots,
            SourceAggregateScriptStrings = source.PrimaryGraphSourceStrings,
            GoldenOracleApplied = golden is not null,
            GoldenOracleName = golden?.Name,
            GoldenMainRoots = golden?.MainAssets,
            GoldenMainScriptStrings = golden?.MainScriptStrings,
            NetRootDelta = netRootDelta,
            AbsoluteFamilyEdits = absoluteEdits,
            ScriptStringDelta = stringDelta,
            StableFamilies = families.Count(x => x.Delta == 0),
            CanonicalizeFamilies = families.Count(x => x.Delta < 0),
            PromoteFamilies = families.Count(x => x.Delta > 0),
            Families = families,
            GoldenBlockContract = golden is null ? null : new Dictionary<string, uint>(golden.MainBlockSizes, StringComparer.OrdinalIgnoreCase),
            Notes = notes,
            Blockers = blockers,
            Verified = true,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private static string Render(NormalMapLinkerV2CanonicalConvergenceResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 4 canonical convergence contract");
        sb.AppendLine(new string('=', 96));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Canonical identity: {r.CanonicalBaseName}");
        sb.AppendLine($"Source aggregate roots: {r.SourceAggregateRoots:N0}; source script strings: {r.SourceAggregateScriptStrings:N0}");
        if (r.GoldenOracleApplied)
        {
            sb.AppendLine($"Golden: {r.GoldenOracleName}");
            sb.AppendLine($"Golden roots: {r.GoldenMainRoots:N0}; net root delta: {r.NetRootDelta:+#;-#;0}; absolute family edits: {r.AbsoluteFamilyEdits:N0}");
            sb.AppendLine($"Golden script strings: {r.GoldenMainScriptStrings:N0}; delta: {r.ScriptStringDelta:+#;-#;0}");
            sb.AppendLine($"Family buckets: stable={r.StableFamilies:N0}; canonicalize/dedup={r.CanonicalizeFamilies:N0}; promote={r.PromoteFamilies:N0}");
        }
        else
        {
            sb.AppendLine("Golden: <none> (generic typed linker path)");
        }
        sb.AppendLine();
        sb.AppendLine("FAMILY CONVERGENCE CONTRACT");
        foreach (var f in r.Families.OrderByDescending(x => Math.Abs(x.Delta ?? 0)).ThenBy(x => x.AssetType, StringComparer.OrdinalIgnoreCase))
        {
            var target = f.GoldenXboxRoots is null ? "?" : f.GoldenXboxRoots.Value.ToString("N0");
            var delta = f.Delta is null ? "?" : f.Delta.Value.ToString("+#;-#;0");
            sb.AppendLine($"  {f.AssetType,-18} source={f.SourceAggregateRoots,6:N0} target={target,6} delta={delta,7}  {f.RequiredAction}");
        }
        if (r.GoldenBlockContract is not null)
        {
            sb.AppendLine();
            sb.AppendLine("GOLDEN XBOX BLOCK CONTRACT (VALIDATION ONLY; NEVER COPIED INTO UNKNOWN MAPS)");
            foreach (var kv in r.GoldenBlockContract)
                sb.AppendLine($"  {kv.Key,-16} {kv.Value,12:N0} B  0x{kv.Value:X8}");
        }
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        foreach (var blocker in r.Blockers) sb.AppendLine("BLOCKER: " + blocker);
        return sb.ToString();
    }
}
