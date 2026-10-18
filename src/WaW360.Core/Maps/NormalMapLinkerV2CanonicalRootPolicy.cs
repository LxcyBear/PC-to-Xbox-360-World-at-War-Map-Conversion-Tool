using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record LinkerV2CanonicalRoot(
    int CanonicalSourceOrdinal,
    string SourceRole,
    string SourceFastFile,
    int SourceAssetIndex,
    string PcTypeName,
    string XboxTypeName,
    int XboxTypeId,
    T4AssetReferenceKind SourceReferenceKind,
    uint SourceRawHeader,
    string IdentityKey,
    string Policy);

public sealed class NormalMapLinkerV2CanonicalRootPolicyResult
{
    public required string MapName { get; init; }
    public required int SourceRoots { get; init; }
    public required int CanonicalProvenanceRoots { get; init; }
    public required int PlatformTypeTransforms { get; init; }
    public required int StableSameNameCollisionGroupsRetained { get; init; }
    public required int RootsInStableSameNameCollisionGroupsRetained { get; init; }
    public required IReadOnlyDictionary<string, int> CanonicalCountsAfterPlatformMapping { get; init; }
    public required IReadOnlyList<LinkerV2CanonicalRoot> Roots { get; init; }
    public required bool ProvenanceIdentityComplete { get; init; }
    public required bool SemanticClosureComplete { get; init; }
    public required string RootSequenceSha256 { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 5 canonical ROOT identity policy.
///
/// Root identity is provenance first: (container role/order, source FF, authoritative XAsset index).
/// A same-name root in map.ff and mod.ff is NOT automatically a duplicate. The working Simpsons
/// oracle proves this matters: XModel/FX/Weapon family counts remain unchanged even though Alpha 4
/// found exact same-name roots in those families. Names are dependency lookup evidence, not a
/// universal root-dedup key.
///
/// The only platform type rewrite performed here is the T4 clipMap_t -> Xbox AssetClipMapPvs
/// mapping. No root is removed or promoted in this stage.
/// </summary>
public static class NormalMapLinkerV2CanonicalRootPolicy
{
    public static async Task<NormalMapLinkerV2CanonicalRootPolicyResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null,
        NormalMapLinkerV2IdentityEvidenceResult? identityOverride = null,
        CancellationToken cancellationToken = default)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var identity = identityOverride ?? await NormalMapLinkerV2IdentityEvidence.RunAsync(map, outputRoot, source, cancellationToken);
        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_root_policy");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_root_policy.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_root_policy.json");

        var primary = source.FastFiles
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .OrderBy(x => LinkerV2ProofEvidence.RoleRank(x.Role))
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var roots = new List<LinkerV2CanonicalRoot>(source.PrimaryGraphSourceRoots);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var platformTransforms = 0;
        var ordinal = 0;

        foreach (var container in primary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(container.Path, cancellationToken);
            foreach (var asset in cached.Document.Assets.OrderBy(x => x.Index))
            {
                // PC T4's clipMap_t is loaded through AssetClipMapPvs on console. Keep the source
                // type in provenance but emit the Xbox type as clipmap_pvs.
                var xboxTypeName = asset.TypeName.Equals("clipmap", StringComparison.OrdinalIgnoreCase)
                    ? "clipmap_pvs"
                    : asset.TypeName;
                if (!xboxTypeName.Equals(asset.TypeName, StringComparison.OrdinalIgnoreCase)) platformTransforms++;

                if (!T4XboxAssetTypeMap.TryGetXboxTypeId(xboxTypeName, out var xboxTypeId))
                    throw new InvalidDataException($"No Xbox T4 type mapping exists for source root {container.FileName}#{asset.Index} '{asset.TypeName}' -> '{xboxTypeName}'.");

                var identityKey = $"{container.Role}|{Path.GetFileName(container.Path)}|{asset.Index}";
                var policy = xboxTypeName.Equals(asset.TypeName, StringComparison.OrdinalIgnoreCase)
                    ? "PRESERVE_PROVENANCE_ROOT"
                    : $"PLATFORM_TYPE_REWRITE:{asset.TypeName}->{xboxTypeName}";
                roots.Add(new LinkerV2CanonicalRoot(
                    ordinal++, container.Role, container.Path, asset.Index, asset.TypeName, xboxTypeName,
                    xboxTypeId, asset.ReferenceKind, asset.RawHeader, identityKey, policy));
                counts[xboxTypeName] = counts.GetValueOrDefault(xboxTypeName) + 1;
            }
        }

        var canonicalizeFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "techset", "menulist", "clipmap", "clipmap_pvs" };
        var stableCollisionGroups = 0;
        var stableCollisionRoots = 0;
        foreach (var family in identity.Families)
        {
            if (family.ExactNameCollisionGroups == 0 || canonicalizeFamilies.Contains(family.AssetType)) continue;
            stableCollisionGroups += family.ExactNameCollisionGroups;
            stableCollisionRoots += family.RootsInExactNameCollisions;
        }

        var notes = new List<string>
        {
            "ROOT IDENTITY POLICY: every authoritative source XAsset root receives a unique provenance identity (role + source FF + source XAsset index).",
            "Exact same-name roots are retained unless that asset type's own typed linker semantics prove an override/dedup rule. Same-name is never a universal dedup key.",
            "Primary source order is deterministic: map.ff -> map_patch.ff -> mod.ff -> auxiliary/extra inputs.",
            "PC clipmap is mapped to Xbox clipmap_pvs as a platform type transform; it is not treated as delete-one/add-one magic.",
            $"Alpha-4 same-name evidence retained {stableCollisionGroups:N0} stable-family collision group(s) / {stableCollisionRoots:N0} root occurrence(s) rather than incorrectly deleting them."
        };

        var blockers = new List<string>();
        var convergence = await NormalMapLinkerV2CanonicalConvergence.RunAsync(map, outputRoot, source, cancellationToken);
        var semanticClosure = convergence.CanonicalizeFamilies == 0 && convergence.PromoteFamilies == 0;
        if (!semanticClosure)
            blockers.Add($"Typed semantic closure still has {convergence.CanonicalizeFamilies:N0} canonicalize family/families and {convergence.PromoteFamilies:N0} promotion family/families. Provenance identity is complete, but semantic closure is a separate gate.");

        var sequenceHash = HashRootSequence(roots);
        var result = new NormalMapLinkerV2CanonicalRootPolicyResult
        {
            MapName = map.Name,
            SourceRoots = source.PrimaryGraphSourceRoots,
            CanonicalProvenanceRoots = roots.Count,
            PlatformTypeTransforms = platformTransforms,
            StableSameNameCollisionGroupsRetained = stableCollisionGroups,
            RootsInStableSameNameCollisionGroupsRetained = stableCollisionRoots,
            CanonicalCountsAfterPlatformMapping = counts,
            Roots = roots,
            ProvenanceIdentityComplete = roots.Count == source.PrimaryGraphSourceRoots && roots.Select(x => x.IdentityKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() == roots.Count,
            SemanticClosureComplete = semanticClosure,
            RootSequenceSha256 = sequenceHash,
            Notes = notes,
            Blockers = blockers,
            Verified = roots.Count == source.PrimaryGraphSourceRoots && roots.All(x => x.XboxTypeId >= 0),
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private static string HashRootSequence(IEnumerable<LinkerV2CanonicalRoot> roots)
    {
        using var sha = SHA256.Create();
        using var ms = new MemoryStream();
        foreach (var root in roots)
        {
            var text = $"{root.SourceRole}\0{Path.GetFileName(root.SourceFastFile)}\0{root.SourceAssetIndex}\0{root.XboxTypeId}\0";
            var bytes = Encoding.UTF8.GetBytes(text);
            ms.Write(bytes, 0, bytes.Length);
        }
        ms.Position = 0;
        return Convert.ToHexString(sha.ComputeHash(ms)).ToLowerInvariant();
    }

    private static string Render(NormalMapLinkerV2CanonicalRootPolicyResult r)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 5 canonical provenance-root policy");
        sb.AppendLine(new string('=', 100));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"sourceRoots={r.SourceRoots:N0}; provenanceRoots={r.CanonicalProvenanceRoots:N0}; platformTypeTransforms={r.PlatformTypeTransforms:N0}");
        sb.AppendLine($"provenanceIdentityComplete={r.ProvenanceIdentityComplete}; semanticClosureComplete={r.SemanticClosureComplete}; verified={r.Verified}");
        sb.AppendLine($"rootSequenceSha256={r.RootSequenceSha256}");
        sb.AppendLine($"stable same-name collision groups retained={r.StableSameNameCollisionGroupsRetained:N0}; root occurrences={r.RootsInStableSameNameCollisionGroupsRetained:N0}");
        sb.AppendLine();
        sb.AppendLine("COUNTS AFTER PLATFORM TYPE MAPPING (NO DEDUP/PROMOTION YET)");
        foreach (var kv in r.CanonicalCountsAfterPlatformMapping.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
            sb.AppendLine($"  {kv.Key,-20} {kv.Value,7:N0}");
        sb.AppendLine();
        sb.AppendLine("ROOT ORDER SAMPLE");
        foreach (var root in r.Roots.Take(40))
            sb.AppendLine($"  #{root.CanonicalSourceOrdinal,5} [{root.SourceRole}] {Path.GetFileName(root.SourceFastFile)}#{root.SourceAssetIndex} {root.PcTypeName}->{root.XboxTypeName} xboxType={root.XboxTypeId} {root.SourceReferenceKind} | {root.Policy}");
        if (r.Roots.Count > 40) sb.AppendLine($"  ... {r.Roots.Count - 40:N0} more root(s)");
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        foreach (var blocker in r.Blockers) sb.AppendLine("BLOCKER: " + blocker);
        return sb.ToString();
    }
}
