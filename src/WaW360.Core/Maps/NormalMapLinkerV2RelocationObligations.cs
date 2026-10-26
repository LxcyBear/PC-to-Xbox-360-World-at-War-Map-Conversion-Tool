using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record LinkerV2RelocationFamilySummary(
    string AssetType,
    int ProofAssets,
    int KnownPointerFields,
    int NullPointers,
    int FollowingPointers,
    int InsertPointers,
    int PackedPointerReferences,
    int UniquePackedTargets,
    int InvalidPackedPointers,
    IReadOnlyDictionary<string, int> PackedTargetsByBlock);

public sealed record LinkerV2PackedTargetSample(
    string AssetType,
    string Field,
    string SourceFastFile,
    int ProofRecordIndex,
    uint Raw,
    string Block,
    uint Offset,
    int ReferenceCount);

public sealed record LinkerV2RelocationFieldSummary(
    string AssetType,
    string Field,
    int PackedReferences,
    int UniquePackedTargets,
    IReadOnlyDictionary<string, int> PackedTargetsByBlock);

public sealed class NormalMapLinkerV2RelocationObligationsResult
{
    public required string MapName { get; init; }
    public required int ProofAssetsAudited { get; init; }
    public required int KnownPointerFieldsAudited { get; init; }
    public required int NullPointers { get; init; }
    public required int FollowingPointers { get; init; }
    public required int InsertPointers { get; init; }
    public required int PackedPointerReferences { get; init; }
    public required int UniquePackedTargets { get; init; }
    public required int InvalidPackedPointers { get; init; }
    public required int MissingProofFiles { get; init; }
    public required int SourceTopLevelAliasRoots { get; init; }
    public required bool PackedEncodingVerified { get; init; }
    public required bool SharedTargetOwnershipResolved { get; init; }
    public required bool NestedPointerWalkComplete { get; init; }
    public required IReadOnlyList<LinkerV2RelocationFamilySummary> Families { get; init; }
    public required IReadOnlyList<LinkerV2RelocationFieldSummary> Fields { get; init; }
    public required IReadOnlyList<LinkerV2PackedTargetSample> PackedTargetSamples { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 4 relocation-obligation inventory.
///
/// Reads only already verified Xbox proof blobs and known typed pointer fields. Packed source
/// pointers are decoded against the authoritative PC XFile block table and round-tripped through
/// T4ZonePointer.Encode. This proves pointer encoding/range semantics and quantifies shared-target
/// work without assigning fake destination addresses or nulling escaping references.
///
/// Nested child structs are deliberately not recursively scanned here; that remains an explicit
/// blocker. The point of this stage is to turn "thousands of scary pointers" into a deterministic
/// per-family/per-block obligation ledger that the real block allocator can consume next.
/// </summary>
public static class NormalMapLinkerV2RelocationObligations
{
    private sealed record PointerField(int Offset, string Name);
    private sealed record PackedHit(
        string Type,
        string Field,
        string SourceFastFile,
        int RecordIndex,
        uint Raw,
        T4ZoneBlock Block,
        uint Offset);

    public static async Task<NormalMapLinkerV2RelocationObligationsResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null,
        CancellationToken cancellationToken = default)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_relocation");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_relocation.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_relocation.json");

        var primary = source.FastFiles
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .OrderBy(x => LinkerV2ProofEvidence.RoleRank(x.Role))
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hits = new List<PackedHit>();
        var familyCounters = new Dictionary<string, Counter>(StringComparer.OrdinalIgnoreCase);
        var blockers = new List<string>();
        var missingProofFiles = 0;
        var invalid = 0;
        var sourceAliases = 0;

        foreach (var container in primary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(container.Path, cancellationToken);
            var doc = cached.Document;
            sourceAliases += doc.Assets.Count(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias);

            foreach (var type in LinkerV2ProofEvidence.TrackedTypes)
            {
                var physical = doc.Assets.Count(a =>
                    a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                    a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared);
                if (physical == 0) continue;

                if (!familyCounters.TryGetValue(type, out var counter))
                    familyCounters[type] = counter = new Counter();

                var proofPath = LinkerV2ProofEvidence.GetProofPath(outputRoot, map.Name, container.Path, container.Role, type);
                if (!File.Exists(proofPath))
                {
                    missingProofFiles++;
                    counter.MissingProofFiles++;
                    continue;
                }

                T4ProofContainerIndex proof;
                try
                {
                    proof = await T4NativeProofContainerReader.IndexAsync(proofPath, type, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    blockers.Add($"Relocation proof unreadable for {Path.GetFileName(container.Path)} {type}: {ex.Message}");
                    continue;
                }

                if (proof.Records.Count != physical)
                {
                    blockers.Add($"Relocation proof/root mismatch for {Path.GetFileName(container.Path)} {type}: roots={physical:N0}, proof={proof.Records.Count:N0}.");
                    continue;
                }

                var fields = GetPointerFields(type);
                if (fields.Count == 0) continue;
                var prefixBytes = fields.Max(f => f.Offset) + 4;
                var recordIndex = 0;
                foreach (var record in proof.Records.OrderBy(r => r.SourceOffset))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    counter.ProofAssets++;
                    var prefix = await T4NativeProofContainerReader.ReadBlobPrefixAsync(record, prefixBytes, cancellationToken);
                    if (prefix.Length < prefixBytes)
                    {
                        blockers.Add($"{type} proof record #{recordIndex:N0} in {Path.GetFileName(container.Path)} is smaller than the known pointer-field prefix ({prefix.Length:N0} < {prefixBytes:N0}).");
                        recordIndex++;
                        continue;
                    }

                    foreach (var field in fields)
                    {
                        counter.Fields++;
                        var raw = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(field.Offset, 4));
                        var decoded = T4ZonePointer.Decode(raw);
                        switch (decoded.Kind)
                        {
                            case T4ZonePointerKind.Null:
                                counter.Null++;
                                break;
                            case T4ZonePointerKind.Following:
                                counter.Following++;
                                break;
                            case T4ZonePointerKind.Insert:
                                counter.Insert++;
                                break;
                            case T4ZonePointerKind.Offset:
                            {
                                counter.PackedRefs++;
                                if (decoded.Block is null)
                                {
                                    counter.Invalid++;
                                    invalid++;
                                    break;
                                }
                                var blockIndex = (int)decoded.Block.Value;
                                var inRange = blockIndex >= 0 && blockIndex < doc.Header.BlockSizes.Count &&
                                              decoded.Offset < doc.Header.BlockSizes[blockIndex];
                                var roundTrip = false;
                                if (inRange)
                                {
                                    try { roundTrip = T4ZonePointer.Encode(decoded.Block.Value, decoded.Offset) == raw; }
                                    catch { roundTrip = false; }
                                }
                                if (!inRange || !roundTrip)
                                {
                                    counter.Invalid++;
                                    invalid++;
                                    break;
                                }
                                hits.Add(new PackedHit(type, field.Name, container.Path, recordIndex, raw, decoded.Block.Value, decoded.Offset));
                                counter.IncrementBlock(decoded.Block.Value.ToString());
                                break;
                            }
                        }
                    }
                    recordIndex++;
                }
            }
        }

        var uniqueTargets = hits
            .GroupBy(h => (h.SourceFastFile, h.Raw), new SourceRawComparer())
            .ToList();

        var fieldSummaries = hits
            .GroupBy(h => (h.Type, h.Field), new TypeFieldComparer())
            .Select(g =>
            {
                var unique = g
                    .GroupBy(h => (h.SourceFastFile, h.Raw), new SourceRawComparer())
                    .Count();
                var byBlock = g
                    .GroupBy(h => h.Block.ToString(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
                return new LinkerV2RelocationFieldSummary(
                    g.Key.Type,
                    g.Key.Field,
                    g.Count(),
                    unique,
                    byBlock);
            })
            .OrderByDescending(x => x.PackedReferences)
            .ThenBy(x => x.AssetType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Field, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var samples = uniqueTargets
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.SourceFastFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Raw)
            .Take(100)
            .Select(g =>
            {
                var h = g.First();
                return new LinkerV2PackedTargetSample(
                    h.Type,
                    h.Field,
                    Path.GetFileName(h.SourceFastFile),
                    h.RecordIndex,
                    h.Raw,
                    h.Block.ToString(),
                    h.Offset,
                    g.Count());
            })
            .ToList();

        var familySummaries = familyCounters
            .OrderByDescending(x => x.Value.PackedRefs)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv =>
            {
                var uniqueForType = hits.Where(h => h.Type.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(h => (h.SourceFastFile, h.Raw), new SourceRawComparer()).Count();
                return new LinkerV2RelocationFamilySummary(
                    kv.Key,
                    kv.Value.ProofAssets,
                    kv.Value.Fields,
                    kv.Value.Null,
                    kv.Value.Following,
                    kv.Value.Insert,
                    kv.Value.PackedRefs,
                    uniqueForType,
                    kv.Value.Invalid,
                    new Dictionary<string, int>(kv.Value.Blocks, StringComparer.OrdinalIgnoreCase));
            })
            .ToList();

        var packedEncodingVerified = invalid == 0;
        var sharedResolved = uniqueTargets.Count == 0;
        var nestedComplete = false; // Alpha 4 intentionally audits top-level typed pointer fields only.
        if (uniqueTargets.Count != 0)
            blockers.Add($"{uniqueTargets.Count:N0} unique packed/shared target(s) are valid source block addresses but still need concrete ownership + destination allocation/rebasing.");
        blockers.Add("Nested pointer traversal is not complete yet; Alpha 4 audits known top-level fields only and never pretends child structs are relocation-complete.");

        var notes = new List<string>
        {
            "No packed pointer is nulled or replaced. Valid source packed pointers remain explicit relocation obligations.",
            "Packed pointers are validated against each source XFile's real seven-block table and must encode/decode byte-exactly.",
            "Proof containers are consumed read-only; no broad candidate scan runs in this stage.",
            "Source XAsset alias roots are counted separately from nested packed pointer targets."
        };

        var result = new NormalMapLinkerV2RelocationObligationsResult
        {
            MapName = map.Name,
            ProofAssetsAudited = familyCounters.Values.Sum(x => x.ProofAssets),
            KnownPointerFieldsAudited = familyCounters.Values.Sum(x => x.Fields),
            NullPointers = familyCounters.Values.Sum(x => x.Null),
            FollowingPointers = familyCounters.Values.Sum(x => x.Following),
            InsertPointers = familyCounters.Values.Sum(x => x.Insert),
            PackedPointerReferences = familyCounters.Values.Sum(x => x.PackedRefs),
            UniquePackedTargets = uniqueTargets.Count,
            InvalidPackedPointers = invalid,
            MissingProofFiles = missingProofFiles,
            SourceTopLevelAliasRoots = sourceAliases,
            PackedEncodingVerified = packedEncodingVerified,
            SharedTargetOwnershipResolved = sharedResolved,
            NestedPointerWalkComplete = nestedComplete,
            Families = familySummaries,
            Fields = fieldSummaries,
            PackedTargetSamples = samples,
            Notes = notes,
            Blockers = blockers,
            Verified = packedEncodingVerified && blockers.All(x => !x.StartsWith("Relocation proof/root mismatch", StringComparison.OrdinalIgnoreCase)),
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private sealed class Counter
    {
        public int ProofAssets;
        public int Fields;
        public int Null;
        public int Following;
        public int Insert;
        public int PackedRefs;
        public int Invalid;
        public int MissingProofFiles;
        public Dictionary<string, int> Blocks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public void IncrementBlock(string block) => Blocks[block] = Blocks.TryGetValue(block, out var n) ? n + 1 : 1;
    }

    private sealed class TypeFieldComparer : IEqualityComparer<(string Type, string Field)>
    {
        public bool Equals((string Type, string Field) x, (string Type, string Field) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Type, y.Type) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Field, y.Field);
        public int GetHashCode((string Type, string Field) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Type),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Field));
    }

    private sealed class SourceRawComparer : IEqualityComparer<(string SourceFastFile, uint Raw)>
    {
        public bool Equals((string SourceFastFile, uint Raw) x, (string SourceFastFile, uint Raw) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.SourceFastFile, y.SourceFastFile) && x.Raw == y.Raw;
        public int GetHashCode((string SourceFastFile, uint Raw) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SourceFastFile), obj.Raw);
    }

    private static IReadOnlyList<PointerField> GetPointerFields(string type)
    {
        if (type.Equals("xanim", StringComparison.OrdinalIgnoreCase))
            return new[]
            {
                new PointerField(0, "name"), new PointerField(52, "names"), new PointerField(56, "dataByte"),
                new PointerField(60, "dataShort"), new PointerField(64, "dataInt"), new PointerField(68, "randomDataShort"),
                new PointerField(72, "randomDataByte"), new PointerField(76, "randomDataInt"), new PointerField(80, "indices"),
                new PointerField(84, "notify"), new PointerField(88, "deltaPart")
            };

        if (type.Equals("xmodel", StringComparison.OrdinalIgnoreCase))
            return new[]
            {
                new PointerField(0, "name"), new PointerField(8, "boneNames"), new PointerField(12, "parentList"),
                new PointerField(16, "quats"), new PointerField(20, "trans"), new PointerField(24, "partClassification"),
                new PointerField(28, "baseMat"), new PointerField(32, "surfs"), new PointerField(36, "materialHandles"),
                new PointerField(152, "collSurfs"), new PointerField(164, "boneInfo"), new PointerField(212, "physPreset"),
                new PointerField(216, "physGeoms"), new PointerField(220, "collmap"), new PointerField(224, "physConstraints")
            };

        if (type.Equals("fx", StringComparison.OrdinalIgnoreCase))
            return new[] { new PointerField(0, "name"), new PointerField(32, "elemDefs") };

        if (type.Equals("weapon", StringComparison.OrdinalIgnoreCase))
            return WeaponPointerFields;

        return Array.Empty<PointerField>();
    }

    private static readonly IReadOnlyList<PointerField> WeaponPointerFields = BuildWeaponPointerFields();

    private static IReadOnlyList<PointerField> BuildWeaponPointerFields()
    {
        var fields = new List<PointerField>
        {
            new(0, "szInternalName"), new(4, "szDisplayName"), new(8, "szOverlayName")
        };
        for (var i = 0; i < 16; i++) fields.Add(new PointerField(12 + i * 4, $"gunXModel[{i}]"));
        fields.Add(new PointerField(76, "handXModel"));
        for (var i = 0; i < 35; i++) fields.Add(new PointerField(80 + i * 4, $"szXAnims[{i}]"));
        fields.Add(new PointerField(220, "szModeName"));
        fields.Add(new PointerField(380, "viewFlashEffect"));
        fields.Add(new PointerField(384, "worldFlashEffect"));
        for (var o = 388; o <= 620; o += 4) fields.Add(new PointerField(o, $"sound@0x{o:X}"));
        fields.Add(new PointerField(624, "bounceSound"));
        foreach (var (o, n) in new[]
        {
            (628,"standMountedWeapdef"),(632,"crouchMountedWeapdef"),(636,"proneMountedWeapdef"),
            (652,"viewShellEjectEffect"),(656,"worldShellEjectEffect"),(660,"viewLastShotEjectEffect"),(664,"worldLastShotEjectEffect"),
            (668,"reticleCenter"),(672,"reticleSide")
        }) fields.Add(new PointerField(o, n));
        for (var i = 0; i < 16; i++) fields.Add(new PointerField(900 + i * 4, $"worldModel[{i}]"));
        foreach (var (o, n) in new[]
        {
            (964,"worldClipModel"),(968,"rocketModel"),(972,"knifeModel"),(976,"worldKnifeModel"),(980,"mountedModel"),
            (984,"hudIcon"),(992,"ammoCounterIcon"),(1008,"szAmmoName"),(1016,"szClipName"),(1040,"szSharedAmmoCapName"),
            (1308,"overlayMaterial"),(1312,"overlayMaterialLowRes"),(1552,"killIcon"),(1564,"dpadIcon"),(1592,"szAltWeaponName"),
            (1664,"projectileModel"),(1672,"projExplosionEffect"),(1680,"projDudEffect"),(1684,"projExplosionSound"),
            (1688,"projDudSound"),(1692,"mortarShellSound"),(1696,"tankShellSound"),(1976,"projTrailEffect"),
            (2008,"projIgnitionEffect"),(2012,"projIgnitionSound"),(2176,"aiVsAiAccuracyGraphName"),
            (2180,"aiVsPlayerAccuracyGraphName"),(2184,"aiVsAiAccuracyGraphKnots"),(2188,"aiVsPlayerAccuracyGraphKnots"),
            (2192,"originalAiVsAiAccuracyGraphKnots"),(2196,"originalAiVsPlayerAccuracyGraphKnots"),
            (2288,"szUseHintString"),(2292,"dropHintString"),(2312,"szScript"),(2428,"fireRumble"),(2432,"meleeImpactRumble"),
            (2452,"flameTableFirstPerson"),(2456,"flameTableThirdPerson"),(2460,"flameTableFirstPersonPtr"),
            (2464,"flameTableThirdPersonPtr"),(2468,"tagFx_preparationEffect"),(2472,"tagFlash_preparationEffect")
        }) fields.Add(new PointerField(o, n));
        return fields.OrderBy(f => f.Offset).ToArray();
    }

    private static string Render(NormalMapLinkerV2RelocationObligationsResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 4 packed/shared relocation obligation ledger");
        sb.AppendLine(new string('=', 100));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"proofAssets={r.ProofAssetsAudited:N0}; knownFields={r.KnownPointerFieldsAudited:N0}; null={r.NullPointers:N0}; following={r.FollowingPointers:N0}; insert={r.InsertPointers:N0}");
        sb.AppendLine($"packedRefs={r.PackedPointerReferences:N0}; uniquePackedTargets={r.UniquePackedTargets:N0}; invalid={r.InvalidPackedPointers:N0}; topLevelAliasRoots={r.SourceTopLevelAliasRoots:N0}; missingProofFiles={r.MissingProofFiles:N0}");
        sb.AppendLine($"packedEncodingVerified={r.PackedEncodingVerified}; sharedTargetOwnershipResolved={r.SharedTargetOwnershipResolved}; nestedPointerWalkComplete={r.NestedPointerWalkComplete}; verified={r.Verified}");
        sb.AppendLine();
        sb.AppendLine("FAMILY POINTER ACCOUNTING");
        foreach (var f in r.Families)
        {
            var blocks = f.PackedTargetsByBlock.Count == 0 ? "<none>" : string.Join(",", f.PackedTargetsByBlock.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value:N0}"));
            sb.AppendLine($"  {f.AssetType,-10} assets={f.ProofAssets,5:N0} fields={f.KnownPointerFields,7:N0} follow={f.FollowingPointers,7:N0} packedRefs={f.PackedPointerReferences,7:N0} uniqueTargets={f.UniquePackedTargets,6:N0} invalid={f.InvalidPackedPointers,4:N0} blocks={blocks}");
        }
        if (r.Fields.Count != 0)
        {
            sb.AppendLine();
            sb.AppendLine("PACKED REFERENCES BY TYPED FIELD");
            foreach (var f in r.Fields.Take(80))
            {
                var blocks = f.PackedTargetsByBlock.Count == 0
                    ? "<none>"
                    : string.Join(",", f.PackedTargetsByBlock.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value:N0}"));
                sb.AppendLine($"  {f.AssetType,-10} {f.Field,-34} refs={f.PackedReferences,6:N0} unique={f.UniquePackedTargets,6:N0} blocks={blocks}");
            }
        }

        if (r.PackedTargetSamples.Count != 0)
        {
            sb.AppendLine();
            sb.AppendLine("PACKED TARGET SAMPLES");
            foreach (var s in r.PackedTargetSamples.Take(40))
                sb.AppendLine($"  {s.SourceFastFile} {s.AssetType}#{s.ProofRecordIndex:N0}.{s.Field}: 0x{s.Raw:X8} -> {s.Block}+0x{s.Offset:X8}; refs={s.ReferenceCount:N0}");
        }
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        foreach (var blocker in r.Blockers) sb.AppendLine("BLOCKER: " + blocker);
        return sb.ToString();
    }
}
