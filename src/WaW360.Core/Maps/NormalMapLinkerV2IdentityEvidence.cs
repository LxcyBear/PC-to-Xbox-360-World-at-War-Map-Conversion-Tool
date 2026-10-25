using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record LinkerV2IdentityFamilySummary(
    string AssetType,
    int SourceRoots,
    int ProofBackedRoots,
    int NamedRoots,
    int UnnamedProofRoots,
    int ProofFilesPresent,
    int ProofFilesMissing,
    int ExactNameCollisionGroups,
    int RootsInExactNameCollisions);

public sealed record LinkerV2IdentityCollision(
    string AssetType,
    string Name,
    int RootCount,
    IReadOnlyList<string> Sources,
    string Status);

public sealed class NormalMapLinkerV2IdentityEvidenceResult
{
    public required string MapName { get; init; }
    public required int PrimaryRoots { get; init; }
    public required int ProofBackedRoots { get; init; }
    public required int NamedProofRoots { get; init; }
    public required int UnnamedProofRoots { get; init; }
    public required int ExactNameCollisionGroups { get; init; }
    public required int RootsInExactNameCollisions { get; init; }
    public required int MissingProofFiles { get; init; }
    public required IReadOnlyList<LinkerV2IdentityFamilySummary> Families { get; init; }
    public required IReadOnlyList<LinkerV2IdentityCollision> CollisionSamples { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required bool Verified { get; init; }
    public required string LedgerFile { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 4 concrete identity evidence ledger.
///
/// The XAsset table remains authoritative for root accounting. For XAnim/XModel/Weapon/FX this
/// stage optionally reads the already VERIFIED Stage-4 serializer proof containers and recovers
/// concrete root names without performing any new candidate scan. This is a migration/evidence
/// bridge only: proof-backed names expose real cross-container collisions and precedence work,
/// but final production identity still needs deterministic source-stream walkers for every type.
/// </summary>
public static class NormalMapLinkerV2IdentityEvidence
{
    private sealed record Row(
        string Role,
        string SourceFastFile,
        int XAssetIndex,
        string AssetType,
        uint RawHeader,
        string ProofFile,
        long SourceOffset,
        string? Name,
        string IdentityState,
        int PrecedenceRank);

    public static async Task<NormalMapLinkerV2IdentityEvidenceResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null,
        CancellationToken cancellationToken = default)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_identity_evidence");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_identity_evidence.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_identity_evidence.json");
        var ledger = Path.Combine(outDir, $"{map.Name}_linker_v2_identity_ledger.tsv");

        var primary = source.FastFiles
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .OrderBy(x => LinkerV2ProofEvidence.RoleRank(x.Role))
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var materialized = await LinkerV2ProofEvidence.EnsureTrackedProofsAsync(
            map, outputRoot, source, cancellationToken);

        var rows = new List<Row>();
        var missingProofFiles = 0;
        var familyRoots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var familyProofPresent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var familyProofMissing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var blockers = new List<string>();
        foreach (var family in materialized.FailedFamilies)
            blockers.Add($"Fast Mode typed proof materialization failed for {family}; identity remains unresolved for that family.");

        foreach (var container in primary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(container.Path, cancellationToken);
            var doc = cached.Document;

            foreach (var g in doc.Assets.GroupBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase))
                familyRoots[g.Key] = familyRoots.TryGetValue(g.Key, out var n) ? n + g.Count() : g.Count();

            foreach (var type in LinkerV2ProofEvidence.TrackedTypes)
            {
                var roots = doc.Assets
                    .Where(a => a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                                a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                    .OrderBy(a => a.Index)
                    .ToList();
                if (roots.Count == 0) continue;

                var proofPath = LinkerV2ProofEvidence.GetProofPath(outputRoot, map.Name, container.Path, container.Role, type);
                if (!File.Exists(proofPath))
                {
                    missingProofFiles++;
                    familyProofMissing[type] = familyProofMissing.TryGetValue(type, out var m) ? m + 1 : 1;
                    continue;
                }

                T4ProofContainerIndex proof;
                try
                {
                    proof = await T4NativeProofContainerReader.IndexAsync(proofPath, type, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    blockers.Add($"Proof evidence unreadable for {Path.GetFileName(container.Path)} {type}: {ex.Message}");
                    continue;
                }

                familyProofPresent[type] = familyProofPresent.TryGetValue(type, out var p) ? p + 1 : 1;
                var records = proof.Records.OrderBy(r => r.SourceOffset).ToList();
                if (records.Count != roots.Count)
                {
                    blockers.Add($"Proof/root count mismatch for {Path.GetFileName(container.Path)} {type}: roots={roots.Count:N0}, proof={records.Count:N0}. Evidence for this family/container is ignored.");
                    continue;
                }

                for (var i = 0; i < roots.Count; i++)
                {
                    var name = await LinkerV2ProofEvidence.TryReadNameAsync(records[i], cancellationToken);
                    rows.Add(new Row(
                        container.Role,
                        container.Path,
                        roots[i].Index,
                        type,
                        roots[i].RawHeader,
                        proofPath,
                        records[i].SourceOffset,
                        name,
                        string.IsNullOrWhiteSpace(name) ? "PROOF_BACKED_NAME_UNRESOLVED" : "PROOF_BACKED_EXACT_NAME",
                        LinkerV2ProofEvidence.RoleRank(container.Role)));
                }
            }
        }

        var named = rows.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
        var collisions = named
            .GroupBy(r => (Type: r.AssetType.ToLowerInvariant(), Name: r.Name!), new TypeNameComparer())
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Type, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Name, StringComparer.Ordinal)
            .ToList();

        var collisionRows = collisions.Take(80).Select(g => new LinkerV2IdentityCollision(
            g.Key.Type,
            g.Key.Name,
            g.Count(),
            g.OrderBy(x => x.PrecedenceRank).ThenBy(x => x.XAssetIndex)
                .Select(x => $"{x.Role}:{Path.GetFileName(x.SourceFastFile)}#{x.XAssetIndex}")
                .ToArray(),
            "IDENTITY_COLLISION_PROVEN__TYPE_SPECIFIC_OVERRIDE_OR_ALIAS_POLICY_REQUIRED"))
            .ToList();

        var familySummaries = familyRoots
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv =>
            {
                var proofRows = rows.Where(r => r.AssetType.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                var namedRows = proofRows.Count(r => !string.IsNullOrWhiteSpace(r.Name));
                var groups = collisions.Where(g => g.Key.Type.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                return new LinkerV2IdentityFamilySummary(
                    kv.Key,
                    kv.Value,
                    proofRows.Count,
                    namedRows,
                    proofRows.Count - namedRows,
                    familyProofPresent.TryGetValue(kv.Key, out var present) ? present : 0,
                    familyProofMissing.TryGetValue(kv.Key, out var missing) ? missing : 0,
                    groups.Count,
                    groups.Sum(g => g.Count()));
            })
            .ToList();

        await WriteLedgerAsync(ledger, rows, cancellationToken);

        var notes = new List<string>
        {
            "The XAsset table is still the authoritative root count. Proof containers are read-only migration evidence; Fast Mode may materialize a missing verified proof with the existing fail-closed typed serializer before reading it.",
            $"Fast Mode proof materialization: generated={string.Join(",", materialized.GeneratedFamilies.DefaultIfEmpty("<none>"))}; failed={string.Join(",", materialized.FailedFamilies.DefaultIfEmpty("<none>"))}.",
        };
        notes.AddRange(materialized.Notes);
        notes.AddRange(new[]
        {
            "Proof generation remains an internal evidence step only; no proof container is a game fastfile.",
            "Exact-name collisions are evidence that concrete merge/override/alias policy is needed. Alpha 7 does NOT delete a root merely because two proof names match.",
            "Families without proof-backed names remain intentionally unresolved until their deterministic source-stream walkers are migrated into Linker V2.",
            "The full per-root evidence ledger is written as TSV so the one-file diagnostic stays compact."
        });


        var result = new NormalMapLinkerV2IdentityEvidenceResult
        {
            MapName = map.Name,
            PrimaryRoots = source.PrimaryGraphSourceRoots,
            ProofBackedRoots = rows.Count,
            NamedProofRoots = named.Count,
            UnnamedProofRoots = rows.Count - named.Count,
            ExactNameCollisionGroups = collisions.Count,
            RootsInExactNameCollisions = collisions.Sum(g => g.Count()),
            MissingProofFiles = missingProofFiles,
            Families = familySummaries,
            CollisionSamples = collisionRows,
            Notes = notes,
            Blockers = blockers,
            Verified = blockers.All(x => !x.StartsWith("Proof/root count mismatch", StringComparison.OrdinalIgnoreCase)) && rows.Count <= source.PrimaryGraphSourceRoots,
            LedgerFile = ledger,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private sealed class TypeNameComparer : IEqualityComparer<(string Type, string Name)>
    {
        public bool Equals((string Type, string Name) x, (string Type, string Name) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Type, y.Type) && StringComparer.Ordinal.Equals(x.Name, y.Name);
        public int GetHashCode((string Type, string Name) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Type), StringComparer.Ordinal.GetHashCode(obj.Name));
    }

    private static async Task WriteLedgerAsync(string path, IReadOnlyList<Row> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder(Math.Max(1024, rows.Count * 96));
        sb.AppendLine("role\tfile\txassetIndex\ttype\trawHeader\tsourceOffset\tidentityState\tname\tproofFile");
        foreach (var r in rows.OrderBy(x => x.PrecedenceRank).ThenBy(x => x.SourceFastFile, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.XAssetIndex))
        {
            static string Clean(string? s) => (s ?? string.Empty).Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
            sb.Append(r.Role).Append('\t')
              .Append(Path.GetFileName(r.SourceFastFile)).Append('\t')
              .Append(r.XAssetIndex).Append('\t')
              .Append(r.AssetType).Append('\t')
              .Append("0x").Append(r.RawHeader.ToString("X8")).Append('\t')
              .Append("0x").Append(r.SourceOffset.ToString("X")).Append('\t')
              .Append(r.IdentityState).Append('\t')
              .Append(Clean(r.Name)).Append('\t')
              .Append(Clean(r.ProofFile)).AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    private static string Render(NormalMapLinkerV2IdentityEvidenceResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 7 self-materializing concrete identity evidence ledger");
        sb.AppendLine(new string('=', 96));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"primaryRoots={r.PrimaryRoots:N0}; proofBacked={r.ProofBackedRoots:N0}; named={r.NamedProofRoots:N0}; unnamedProof={r.UnnamedProofRoots:N0}; missingProofFiles={r.MissingProofFiles:N0}");
        sb.AppendLine($"exactNameCollisionGroups={r.ExactNameCollisionGroups:N0}; rootsInCollisions={r.RootsInExactNameCollisions:N0}; verified={r.Verified}");
        sb.AppendLine($"ledger={r.LedgerFile}");
        sb.AppendLine();
        sb.AppendLine("FAMILY COVERAGE");
        foreach (var f in r.Families)
            sb.AppendLine($"  {f.AssetType,-18} roots={f.SourceRoots,6:N0} proof={f.ProofBackedRoots,6:N0} named={f.NamedRoots,6:N0} unnamed={f.UnnamedProofRoots,5:N0} proofFiles={f.ProofFilesPresent:N0}/{f.ProofFilesPresent + f.ProofFilesMissing:N0} collisions={f.ExactNameCollisionGroups:N0}");
        if (r.CollisionSamples.Count != 0)
        {
            sb.AppendLine();
            sb.AppendLine("EXACT-NAME COLLISION SAMPLES (EVIDENCE ONLY; NO ROOTS REMOVED)");
            foreach (var c in r.CollisionSamples)
                sb.AppendLine($"  {c.AssetType}:{c.Name} x{c.RootCount:N0} <- {string.Join(", ", c.Sources)} | {c.Status}");
        }
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        foreach (var blocker in r.Blockers) sb.AppendLine("BLOCKER: " + blocker);
        return sb.ToString();
    }
}
