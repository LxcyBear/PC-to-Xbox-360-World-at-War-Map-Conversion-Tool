using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record LinkerV2TechniqueClosureEntry(
    string SourceRole,
    string SourceFastFile,
    int AssetIndex,
    string Name,
    int NonNullConsoleTechniqueSlots,
    int InlineConsoleTechniqueSlots,
    int PackedConsoleTechniqueSlots,
    int NonNullDroppedPcSlots,
    string Disposition,
    string Evidence);

public sealed class NormalMapLinkerV2TechniqueClosureResult
{
    public required string MapName { get; init; }
    public required int DeclaredTechniqueSets { get; init; }
    public required int DeterministicallyWalkedTechniqueSets { get; init; }
    public required int DeclaredMaterials { get; init; }
    public required int DeterministicallyWalkedMaterials { get; init; }
    public required int MaterialTechniqueReferences { get; init; }
    public required int PortableNullGraphTechniqueSets { get; init; }
    public required int XboxDonorRequiredTechniqueSets { get; init; }
    public required int BlockedTechniqueSets { get; init; }
    public required int NonNullDroppedPcTechniqueSlots { get; init; }
    public required bool ClassificationComplete { get; init; }
    public required bool FullTechniqueClosureReady { get; init; }
    public required IReadOnlyList<LinkerV2TechniqueClosureEntry> Entries { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 6 production TechniqueSet dependency classifier.
///
/// PC shader/pass graphs are never copied to Xbox. This stage reuses the deterministic
/// Material/TechniqueSet source walker that already exists in the converter and classifies
/// every physical PC TechniqueSet as:
///   PORTABLE_NULL_GRAPH   - no console technique graph is referenced;
///   XBOX_DONOR_REQUIRED  - one or more real technique slots need an Xbox/Xenos donor;
///   BLOCKED_*            - the source graph is not yet deterministically understood.
///
/// This is intentionally stricter than the old null-graph smoke serializer. A synthetic
/// TechniqueSet can still be useful for diagnostics, but it can never satisfy production
/// typed closure.
/// </summary>
public static class NormalMapLinkerV2TechniqueClosure
{
    public static async Task<NormalMapLinkerV2TechniqueClosureResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null,
        CancellationToken cancellationToken = default)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_technique_closure");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_technique_closure.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_technique_closure.json");

        var primary = source.FastFiles
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .OrderBy(x => LinkerV2ProofEvidence.RoleRank(x.Role))
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entries = new List<LinkerV2TechniqueClosureEntry>();
        var blockers = new List<string>();
        var notes = new List<string>
        {
            "Production rule: PC Direct3D technique/pass/shader graphs are never serialized into an Xbox candidate.",
            "A non-null PC technique slot therefore requires an Xbox-native TechniqueSet/pixel-shader donor or a separately proven Xbox-native reconstruction.",
            "The old T4TechniqueSetNullGraphSerializer remains diagnostic-only and cannot satisfy this gate."
        };

        var declaredTechsets = 0;
        var walkedTechsets = 0;
        var declaredMaterials = 0;
        var walkedMaterials = 0;
        var materialTechniqueRefs = 0;
        var portable = 0;
        var donorRequired = 0;
        var blocked = 0;
        var nonNullDropped = 0;

        foreach (var container in primary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(container.Path, cancellationToken);
            var doc = cached.Document;

            var physicalTechsets = doc.Assets
                .Where(a => a.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase) &&
                            a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                .OrderBy(a => a.Index)
                .ToList();
            var physicalMaterials = doc.Assets
                .Where(a => a.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase) &&
                            a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                .OrderBy(a => a.Index)
                .ToList();

            declaredTechsets += physicalTechsets.Count;
            declaredMaterials += physicalMaterials.Count;
            if (physicalTechsets.Count == 0 && physicalMaterials.Count == 0)
                continue;

            T4MaterialTechniqueLoadScan scan;
            try
            {
                scan = T4MaterialTechniqueLoadSerializer.Build(doc, cached.Bytes, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var root in physicalTechsets)
                {
                    entries.Add(new LinkerV2TechniqueClosureEntry(
                        container.Role, container.Path, root.Index, $"<unresolved:{root.Index}>",
                        0, 0, 0, 0,
                        "BLOCKED_SOURCE_WALKER",
                        $"deterministic Material/TechniqueSet walker threw {ex.GetType().Name}: {ex.Message}"));
                    blocked++;
                }
                blockers.Add($"{container.FileName}: deterministic Material/TechniqueSet source walk failed: {ex.Message}");
                continue;
            }

            walkedTechsets += scan.TechniqueSetsByIndex.Count;
            walkedMaterials += scan.MaterialsByIndex.Count;
            materialTechniqueRefs += scan.MaterialsByIndex.Values.Count(x => x.SourceTechniquePointer != 0);

            foreach (var root in physicalTechsets)
            {
                if (!scan.TechniqueSetsByIndex.TryGetValue(root.Index, out var tech))
                {
                    entries.Add(new LinkerV2TechniqueClosureEntry(
                        container.Role, container.Path, root.Index, $"<unresolved:{root.Index}>",
                        0, 0, 0, 0,
                        "BLOCKED_SOURCE_WALKER",
                        "physical TechniqueSet root was not mapped to one deterministic source payload"));
                    blocked++;
                    continue;
                }

                nonNullDropped += tech.NonNullDroppedPcSlots;
                string disposition;
                string evidence;

                if (tech.NonNullDroppedPcSlots != 0)
                {
                    disposition = "BLOCKED_PC_ONLY_SLOTS";
                    evidence = $"{tech.NonNullDroppedPcSlots} non-null PC-only/editor technique slot(s) would be dropped by the 59 -> 51 Xbox table";
                    blocked++;
                }
                else if (tech.NonNullConsoleTechniqueSlots == 0 &&
                         tech.BlockReason is null &&
                         tech.XboxPayload is not null)
                {
                    disposition = "PORTABLE_NULL_GRAPH";
                    evidence = "all 51 Xbox-visible technique pointers are null; top-level Xbox TechniqueSet payload is deterministic";
                    portable++;
                }
                else if (tech.NonNullConsoleTechniqueSlots > 0)
                {
                    disposition = "XBOX_DONOR_REQUIRED";
                    evidence = $"{tech.NonNullConsoleTechniqueSlots} Xbox-visible technique slot(s) are non-null " +
                               $"(inline={tech.InlineConsoleTechniqueSlots}, packed={tech.PackedConsoleTechniqueSlots}); PC shader graph must not be copied";
                    donorRequired++;
                }
                else
                {
                    disposition = "BLOCKED_SERIALIZER";
                    evidence = tech.BlockReason ?? "TechniqueSet could not produce a deterministic Xbox top-level payload";
                    blocked++;
                }

                entries.Add(new LinkerV2TechniqueClosureEntry(
                    container.Role,
                    container.Path,
                    root.Index,
                    tech.Name,
                    tech.NonNullConsoleTechniqueSlots,
                    tech.InlineConsoleTechniqueSlots,
                    tech.PackedConsoleTechniqueSlots,
                    tech.NonNullDroppedPcSlots,
                    disposition,
                    evidence));
            }

            if (scan.MaterialsByIndex.Count != physicalMaterials.Count)
                blockers.Add($"{container.FileName}: deterministic Material walk mapped {scan.MaterialsByIndex.Count:N0}/{physicalMaterials.Count:N0} physical Material root(s).");
            if (scan.TechniqueSetsByIndex.Count != physicalTechsets.Count)
                blockers.Add($"{container.FileName}: deterministic TechniqueSet walk mapped {scan.TechniqueSetsByIndex.Count:N0}/{physicalTechsets.Count:N0} physical TechniqueSet root(s).");
        }

        var classificationComplete =
            entries.Count == declaredTechsets &&
            walkedTechsets == declaredTechsets &&
            entries.All(x => !x.Disposition.Equals("BLOCKED_SOURCE_WALKER", StringComparison.OrdinalIgnoreCase));

        if (donorRequired != 0)
            blockers.Add($"{donorRequired:N0} TechniqueSet root(s) require Xbox-native donor resolution before production serialization.");
        if (blocked != 0)
            blockers.Add($"{blocked:N0} TechniqueSet root(s) remain blocked by incomplete deterministic source/platform mapping.");
        if (nonNullDropped != 0)
            blockers.Add($"{nonNullDropped:N0} non-null PC-only technique slot(s) need explicit Xbox policy; silently dropping them is forbidden.");

        var fullReady = classificationComplete && donorRequired == 0 && blocked == 0 && nonNullDropped == 0;
        if (fullReady)
            notes.Add($"TECHNIQUE CLOSURE PASS: all {declaredTechsets:N0} physical TechniqueSet root(s) are production-safe without copying PC shader bytecode.");
        else if (classificationComplete)
            notes.Add($"TECHNIQUE CLASSIFICATION PASS: all {declaredTechsets:N0} physical TechniqueSet root(s) were classified; donor/materialization work is now explicit instead of a hardcoded unknown.");

        var result = new NormalMapLinkerV2TechniqueClosureResult
        {
            MapName = map.Name,
            DeclaredTechniqueSets = declaredTechsets,
            DeterministicallyWalkedTechniqueSets = walkedTechsets,
            DeclaredMaterials = declaredMaterials,
            DeterministicallyWalkedMaterials = walkedMaterials,
            MaterialTechniqueReferences = materialTechniqueRefs,
            PortableNullGraphTechniqueSets = portable,
            XboxDonorRequiredTechniqueSets = donorRequired,
            BlockedTechniqueSets = blocked,
            NonNullDroppedPcTechniqueSlots = nonNullDropped,
            ClassificationComplete = classificationComplete,
            FullTechniqueClosureReady = fullReady,
            Entries = entries.OrderBy(x => LinkerV2ProofEvidence.RoleRank(x.SourceRole))
                .ThenBy(x => x.SourceFastFile, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.AssetIndex)
                .ToList(),
            Notes = notes,
            Blockers = blockers,
            Verified = classificationComplete,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private static string Render(NormalMapLinkerV2TechniqueClosureResult r)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 6 production TechniqueSet dependency closure");
        sb.AppendLine(new string('=', 104));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"techsets: declared={r.DeclaredTechniqueSets:N0}; walked={r.DeterministicallyWalkedTechniqueSets:N0}; portableNull={r.PortableNullGraphTechniqueSets:N0}; xboxDonorRequired={r.XboxDonorRequiredTechniqueSets:N0}; blocked={r.BlockedTechniqueSets:N0}");
        sb.AppendLine($"materials: declared={r.DeclaredMaterials:N0}; walked={r.DeterministicallyWalkedMaterials:N0}; techniqueRefs={r.MaterialTechniqueReferences:N0}");
        sb.AppendLine($"nonNullDroppedPcSlots={r.NonNullDroppedPcTechniqueSlots:N0}; classificationComplete={r.ClassificationComplete}; fullTechniqueClosureReady={r.FullTechniqueClosureReady}; verified={r.Verified}");
        sb.AppendLine();
        sb.AppendLine("TECHNIQUESETS");
        foreach (var e in r.Entries.Take(1024))
            sb.AppendLine($"  [{e.SourceRole}] {Path.GetFileName(e.SourceFastFile)}#{e.AssetIndex} {e.Name} | {e.Disposition} | slots={e.NonNullConsoleTechniqueSlots} inline={e.InlineConsoleTechniqueSlots} packed={e.PackedConsoleTechniqueSlots} droppedPc={e.NonNullDroppedPcSlots} | {e.Evidence}");
        if (r.Entries.Count > 1024)
            sb.AppendLine($"  ... {r.Entries.Count - 1024:N0} additional TechniqueSet row(s) omitted from TXT; JSON contains all entries.");
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        foreach (var blocker in r.Blockers) sb.AppendLine("BLOCKER: " + blocker);
        return sb.ToString();
    }
}
