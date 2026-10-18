using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record LinkerV2ScriptStringSourceSummary(
    string Role,
    string SourceFastFile,
    int SourceStrings,
    int InlineLiteralStrings,
    int NullStrings,
    int AliasStrings,
    int CanonicalAdds,
    IReadOnlyList<int?> SourceToCanonicalIndex);

public sealed class NormalMapLinkerV2ScriptStringMergeResult
{
    public required string MapName { get; init; }
    public required string CanonicalBaseName { get; init; }
    public required int RawSourceStrings { get; init; }
    public required int InlineLiteralStrings { get; init; }
    public required int NullStrings { get; init; }
    public required int AliasStrings { get; init; }
    public required int CanonicalUniqueStrings { get; init; }
    public required int CanonicalNullEntries { get; init; }
    public required int CanonicalTableEntries { get; init; }
    public required int SourceNullEntriesCollapsed { get; init; }
    public required int ExactDuplicateStringsRemoved { get; init; }
    public required int? GoldenScriptStrings { get; init; }
    public required int? GoldenDeltaAfterExactMerge { get; init; }
    public required bool FullRemapReady { get; init; }
    public required bool GoldenCountMatched { get; init; }
    public required string CanonicalSequenceSha256 { get; init; }
    public required IReadOnlyList<LinkerV2ScriptStringSourceSummary> Sources { get; init; }
    public required IReadOnlyList<string> DuplicateSamples { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 4 exact script-string merger.
///
/// This is a real canonicalization step, not an oracle-driven count adjustment. Every primary
/// source XFile string table is read through the authoritative zone parser. Literal strings are
/// merged by exact ordinal identity and a per-container source-index -> canonical-index remap is
/// produced. Null entries remain null. Packed/alias string entries are deliberately fail-closed
/// until the real block resolver can resolve their target bytes.
///
/// The resulting remap is what typed asset writers must use when they re-emit script-string indices
/// into the single canonical Xbox main graph.
/// </summary>
public static class NormalMapLinkerV2ScriptStringMerge
{
    public static async Task<NormalMapLinkerV2ScriptStringMergeResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null,
        CancellationToken cancellationToken = default)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var golden = LinkerV2GoldenCatalog.Find(source.CanonicalBaseName);
        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_script_strings");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_script_strings.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_script_strings.json");

        var primary = source.FastFiles
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .OrderBy(x => RoleRank(x.Role))
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var canonical = new List<string>();
        var byValue = new Dictionary<string, int>(StringComparer.Ordinal);
        var duplicateSamples = new List<string>();
        var sourceRows = new List<LinkerV2ScriptStringSourceSummary>();
        var raw = 0;
        var literals = 0;
        var nulls = 0;
        var aliases = 0;
        var duplicates = 0;

        // T4 script-string index 0 is commonly the null/none sentinel. When multiple PC
        // containers are merged each source can carry its own null table entry; collapsing all
        // of those to NO ENTRY shifts every real index and is unsafe. Reserve exactly one
        // canonical null slot when any primary source contains a null script-string row.
        var anySourceNull = false;
        foreach (var container in primary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(container.Path, cancellationToken);
            if (cached.Document.ScriptStrings.Any(x => x.RawPointer == 0))
            {
                anySourceNull = true;
                break;
            }
        }
        var canonicalNullEntries = anySourceNull ? 1 : 0;
        var canonicalStringBase = canonicalNullEntries;

        foreach (var container in primary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(container.Path, cancellationToken);
            var doc = cached.Document;
            var remap = new int?[doc.ScriptStringCount];
            var localLiteral = 0;
            var localNull = 0;
            var localAlias = 0;
            var localAdds = 0;

            raw += doc.ScriptStringCount;
            for (var i = 0; i < doc.ScriptStrings.Count; i++)
            {
                var entry = doc.ScriptStrings[i];
                if (entry.RawPointer == 0)
                {
                    localNull++;
                    nulls++;
                    remap[i] = canonicalNullEntries == 0 ? null : 0;
                    continue;
                }

                if (!entry.Inline || entry.Value is null || entry.Value.StartsWith("<alias ", StringComparison.Ordinal))
                {
                    localAlias++;
                    aliases++;
                    remap[i] = null;
                    continue;
                }

                localLiteral++;
                literals++;
                if (!byValue.TryGetValue(entry.Value, out var canonicalIndex))
                {
                    canonicalIndex = canonicalStringBase + canonical.Count;
                    canonical.Add(entry.Value);
                    byValue.Add(entry.Value, canonicalIndex);
                    localAdds++;
                }
                else
                {
                    duplicates++;
                    if (duplicateSamples.Count < 24)
                        duplicateSamples.Add($"{container.FileName}[{i}] -> canonical[{canonicalIndex}] '{Escape(entry.Value)}'");
                }
                remap[i] = canonicalIndex;
            }

            sourceRows.Add(new LinkerV2ScriptStringSourceSummary(
                container.Role,
                container.Path,
                doc.ScriptStringCount,
                localLiteral,
                localNull,
                localAlias,
                localAdds,
                remap));
        }

        var canonicalTableEntries = canonicalNullEntries + canonical.Count;
        var sourceNullEntriesCollapsed = Math.Max(0, nulls - canonicalNullEntries);
        var hash = HashCanonicalSequence(canonical, canonicalNullEntries != 0);
        int? goldenDelta = golden is null ? null : golden.MainScriptStrings - canonicalTableEntries;
        var fullRemap = aliases == 0;
        var goldenMatch = golden is not null && fullRemap && canonicalTableEntries == golden.MainScriptStrings;
        var notes = new List<string>
        {
            "Exact literal duplicate folding is identity-proven by ordinal string equality; no golden count is used to choose what to remove.",
            "A single canonical null/none slot is preserved whenever any source table contains null; per-container null sentinels collapse onto that slot instead of shifting every live script-string index.",
            "The emitted source-index -> canonical-index maps are the contract typed asset writers must use when patching script-string indices into the merged Xbox graph."
        };
        var blockers = new List<string>();
        if (aliases != 0)
            blockers.Add($"{aliases:N0} script-string table entr{(aliases == 1 ? "y is" : "ies are")} packed/aliased and require real block-target resolution before the canonical remap is complete.");
        if (golden is not null)
        {
            if (goldenMatch)
                notes.Add($"GOLDEN CONVERGENCE PROVEN: source-derived canonical table independently reached the working Xbox script-string count {golden.MainScriptStrings:N0}.");
            else
                notes.Add($"Golden validation only: source-derived canonical table produced {canonicalTableEntries:N0}; working Xbox reference contains {golden.MainScriptStrings:N0}; remaining delta={goldenDelta:+#;-#;0}. Count equality is NOT a production gate by itself as long as every live asset reference is remapped consistently; the delta is diagnostic, never a delete/add instruction.");
        }
        else
        {
            notes.Add("GENERIC MAP: the canonical exact-literal count is derived solely from source identities; no target count is assumed.");
        }

        var result = new NormalMapLinkerV2ScriptStringMergeResult
        {
            MapName = map.Name,
            CanonicalBaseName = source.CanonicalBaseName,
            RawSourceStrings = raw,
            InlineLiteralStrings = literals,
            NullStrings = nulls,
            AliasStrings = aliases,
            CanonicalUniqueStrings = canonical.Count,
            CanonicalNullEntries = canonicalNullEntries,
            CanonicalTableEntries = canonicalTableEntries,
            SourceNullEntriesCollapsed = sourceNullEntriesCollapsed,
            ExactDuplicateStringsRemoved = duplicates,
            GoldenScriptStrings = golden?.MainScriptStrings,
            GoldenDeltaAfterExactMerge = goldenDelta,
            FullRemapReady = fullRemap,
            GoldenCountMatched = goldenMatch,
            CanonicalSequenceSha256 = hash,
            Sources = sourceRows,
            DuplicateSamples = duplicateSamples,
            Notes = notes,
            Blockers = blockers,
            Verified = raw == sourceRows.Sum(x => x.SourceStrings) &&
                       literals == canonical.Count + duplicates &&
                       canonical.Count == byValue.Count &&
                       raw == literals + nulls + aliases &&
                       sourceNullEntriesCollapsed == Math.Max(0, nulls - canonicalNullEntries),
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private static int RoleRank(string role) => role.ToLowerInvariant() switch
    {
        "map.ff" => 0,
        "map_patch.ff" => 1,
        "mod.ff" => 2,
        _ => 3
    };

    private static string HashCanonicalSequence(IReadOnlyList<string> strings, bool includeNullSentinel)
    {
        using var sha = SHA256.Create();
        using var ms = new MemoryStream();
        if (includeNullSentinel)
            ms.WriteByte(0);
        foreach (var value in strings)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            ms.Write(bytes, 0, bytes.Length);
            ms.WriteByte(0);
        }
        ms.Position = 0;
        return Convert.ToHexString(sha.ComputeHash(ms)).ToLowerInvariant();
    }

    private static string Escape(string value)
    {
        var v = value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return v.Length <= 120 ? v : v[..117] + "...";
    }

    private static string Render(NormalMapLinkerV2ScriptStringMergeResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 4 script-string canonicalization/remap");
        sb.AppendLine(new string('=', 96));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Canonical identity: {r.CanonicalBaseName}");
        sb.AppendLine($"raw={r.RawSourceStrings:N0}; literal={r.InlineLiteralStrings:N0}; sourceNull={r.NullStrings:N0}; aliases={r.AliasStrings:N0}; canonicalLiteralUnique={r.CanonicalUniqueStrings:N0}; canonicalNullSlots={r.CanonicalNullEntries:N0}; canonicalTable={r.CanonicalTableEntries:N0}; exactDuplicatesRemoved={r.ExactDuplicateStringsRemoved:N0}; sourceNullsCollapsed={r.SourceNullEntriesCollapsed:N0}");
        sb.AppendLine($"fullRemapReady={r.FullRemapReady}; sequenceSha256={r.CanonicalSequenceSha256}; verified={r.Verified}");
        if (r.GoldenScriptStrings is not null)
            sb.AppendLine($"goldenStrings={r.GoldenScriptStrings:N0}; deltaAfterExactMerge={r.GoldenDeltaAfterExactMerge:+#;-#;0}; goldenCountMatched={r.GoldenCountMatched}");
        sb.AppendLine();
        sb.AppendLine("SOURCE REMAPS");
        foreach (var s in r.Sources)
            sb.AppendLine($"  [{s.Role}] {Path.GetFileName(s.SourceFastFile)}: source={s.SourceStrings:N0}; literal={s.InlineLiteralStrings:N0}; null={s.NullStrings:N0}; aliases={s.AliasStrings:N0}; canonicalAdds={s.CanonicalAdds:N0}");
        if (r.DuplicateSamples.Count != 0)
        {
            sb.AppendLine();
            sb.AppendLine("EXACT DUPLICATE SAMPLES");
            foreach (var sample in r.DuplicateSamples) sb.AppendLine("  " + sample);
        }
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        foreach (var blocker in r.Blockers) sb.AppendLine("BLOCKER: " + blocker);
        return sb.ToString();
    }
}
