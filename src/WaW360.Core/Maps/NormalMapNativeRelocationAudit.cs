using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4NativeRelocationFastFileAudit(
    string Role,
    string Source,
    string WritePlanFile,
    int PhysicalRecordsAudited,
    int KnownPointerFieldsAudited,
    int InlineOrNullPointers,
    int PackedPointers,
    int AliasPointers,
    int InvalidPointers,
    IReadOnlyDictionary<string, int> PackedByBlock,
    bool NestedPointerAuditPending,
    bool Verified);

public sealed class NormalMapNativeRelocationAuditResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4NativeRelocationFastFileAudit> FastFiles { get; init; }
    public required int PhysicalRecordsAudited { get; init; }
    public required int KnownPointerFieldsAudited { get; init; }
    public required int PackedPointers { get; init; }
    public required int AliasPointers { get; init; }
    public required int InvalidPointers { get; init; }
    public required int RelocationObligations { get; init; }
    public required bool NestedPointerAuditPending { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5b / alpha 10a: relocation + rebasing preflight.
///
/// Audits every packed pointer in the known top-level pointer fields for the four
/// Stage-4-proven native asset classes, plus every packed XAsset-table alias. Every
/// pointer must decode into one of T4's seven blocks, land within the source block
/// allocation, and round-trip through the new encoder byte-exactly.
///
/// This is intentionally not the final rebaser yet. Nested XModel/Weapon/FX pointer
/// ownership still needs block-cursor simulation before final .ff emission is unlocked.
/// </summary>
public static class NormalMapNativeRelocationAudit
{
    private sealed class PlanDocumentModel
    {
        public string Source { get; set; } = string.Empty;
        public List<PlanAssetModel> Assets { get; set; } = new();
    }

    private sealed class PlanAssetModel
    {
        public int XAssetIndex { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public int XboxTypeId { get; set; }
        public T4AssetReferenceKind ReferenceKind { get; set; }
        public uint SourceRawHeader { get; set; }
        public long? SourceBodyOffset { get; set; }
        public string? ProofContainer { get; set; }
        public long? ProofBlobOffset { get; set; }
        public int BlobLength { get; set; }
        public T4ZoneBlock DefaultBlock { get; set; }
        public bool RequiresSplitBlockTraversal { get; set; }
    }

    private sealed record PointerField(int Offset, string Name);

    public static async Task<NormalMapNativeRelocationAuditResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var writerJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10a_native_writer", $"{map.Name}_stage5_alpha10a_native_writer.json");
        if (!File.Exists(writerJson))
            throw new InvalidDataException("Stage 5b requires the Stage 5a native write plan. Run Stage 5a first.");

        var writerResult = JsonSerializer.Deserialize<NormalMapNativeWriterFoundationResult>(
            await File.ReadAllTextAsync(writerJson, cancellationToken),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Could not read Stage 5a native writer result.");
        if (!writerResult.Verified)
            throw new InvalidDataException("Stage 5a native writer result is not verified; relocation audit refuses to continue.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10a_relocation");
        Directory.CreateDirectory(workspace);
        var reports = new List<T4NativeRelocationFastFileAudit>();
        var detailLines = new List<string>();

        foreach (var ff in writerResult.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = JsonSerializer.Deserialize<PlanDocumentModel>(
                await File.ReadAllTextAsync(ff.PlanFile, cancellationToken),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException($"Could not read native write plan '{ff.PlanFile}'.");

            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            var header = cached.ZoneHeader;
            var packedByBlock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var physical = 0;
            var audited = 0;
            var inlineOrNull = 0;
            var packed = 0;
            var aliases = 0;
            var invalid = 0;
            var nestedPending = false;

            detailLines.Add($"[{ff.Role}] {ff.Source}");

            foreach (var asset in plan.Assets.OrderBy(a => a.XAssetIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (asset.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias)
                {
                    aliases++;
                    audited++;
                    if (!AuditPackedPointer(asset.SourceRawHeader, header, out var blockName, out var description))
                    {
                        invalid++;
                        detailLines.Add($"  INVALID alias XAsset#{asset.XAssetIndex} {asset.TypeName}: 0x{asset.SourceRawHeader:X8} {description}");
                    }
                    else
                    {
                        packed++;
                        Increment(packedByBlock, blockName!);
                        detailLines.Add($"  alias XAsset#{asset.XAssetIndex} {asset.TypeName}: {description}");
                    }
                    continue;
                }

                if (asset.ReferenceKind is not (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
                    continue;
                if (string.IsNullOrWhiteSpace(asset.ProofContainer) || asset.ProofBlobOffset is null || asset.BlobLength <= 0)
                    throw new InvalidDataException($"Physical planned XAsset #{asset.XAssetIndex} {asset.TypeName} has no proof blob descriptor.");

                physical++;
                nestedPending |= asset.RequiresSplitBlockTraversal;
                var fields = GetTopLevelPointerFields(asset.TypeName);
                var needed = fields.Count == 0 ? 0 : fields.Max(f => f.Offset) + 4;
                if (asset.BlobLength < needed)
                    throw new InvalidDataException($"{asset.TypeName} XAsset #{asset.XAssetIndex} proof blob is too small for its fixed pointer fields.");

                var prefix = await ReadBlobPrefixAsync(asset.ProofContainer!, asset.ProofBlobOffset.Value, needed, cancellationToken);
                foreach (var field in fields)
                {
                    audited++;
                    var raw = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(field.Offset, 4));
                    var decoded = T4ZonePointer.Decode(raw);
                    if (decoded.Kind is T4ZonePointerKind.Null or T4ZonePointerKind.Following or T4ZonePointerKind.Insert)
                    {
                        inlineOrNull++;
                        continue;
                    }

                    if (!AuditPackedPointer(raw, header, out var blockName, out var description))
                    {
                        invalid++;
                        detailLines.Add($"  INVALID XAsset#{asset.XAssetIndex} {asset.TypeName}.{field.Name} @+0x{field.Offset:X}: 0x{raw:X8} {description}");
                        continue;
                    }

                    packed++;
                    Increment(packedByBlock, blockName!);
                    detailLines.Add($"  packed XAsset#{asset.XAssetIndex} {asset.TypeName}.{field.Name} @+0x{field.Offset:X}: {description}");
                }
            }

            if (invalid != 0)
                throw new InvalidDataException($"Stage 5b found {invalid:N0} invalid packed pointer(s) in '{Path.GetFileName(ff.Source)}'. No native output is allowed.");

            detailLines.Add($"  audited={audited:N0}; packed={packed:N0}; aliases={aliases:N0}; inline/null={inlineOrNull:N0}; nestedPending={nestedPending}");
            detailLines.Add("");

            reports.Add(new T4NativeRelocationFastFileAudit(
                ff.Role,
                ff.Source,
                ff.PlanFile,
                physical,
                audited,
                inlineOrNull,
                packed,
                aliases,
                invalid,
                packedByBlock,
                nestedPending,
                true));
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10a_relocation.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10a_relocation.json");
        var result = new NormalMapNativeRelocationAuditResult
        {
            MapName = map.Name,
            Workspace = workspace,
            FastFiles = reports,
            PhysicalRecordsAudited = reports.Sum(x => x.PhysicalRecordsAudited),
            KnownPointerFieldsAudited = reports.Sum(x => x.KnownPointerFieldsAudited),
            PackedPointers = reports.Sum(x => x.PackedPointers),
            AliasPointers = reports.Sum(x => x.AliasPointers),
            InvalidPointers = reports.Sum(x => x.InvalidPointers),
            RelocationObligations = reports.Sum(x => x.PackedPointers),
            NestedPointerAuditPending = reports.Any(x => x.NestedPointerAuditPending),
            Verified = reports.All(x => x.Verified) && reports.Sum(x => x.InvalidPointers) == 0,
            HardwareReady = false,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, detailLines), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static IReadOnlyList<PointerField> GetTopLevelPointerFields(string type)
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

    private static bool AuditPackedPointer(uint raw, ZoneHeaderSummary header, out string? blockName, out string description)
    {
        blockName = null;
        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null)
        {
            description = $"does not decode as a packed T4 block pointer ({decoded})";
            return false;
        }

        var block = decoded.Block.Value;
        var blockIndex = (int)block;
        if (blockIndex < 0 || blockIndex >= header.BlockSizes.Count)
        {
            description = $"decodes to unknown block {blockIndex}";
            return false;
        }
        if (decoded.Offset >= header.BlockSizes[blockIndex])
        {
            description = $"decodes to {block}+0x{decoded.Offset:X8}, outside source block size 0x{header.BlockSizes[blockIndex]:X8}";
            return false;
        }

        var encoded = T4ZonePointer.Encode(block, decoded.Offset);
        if (encoded != raw)
        {
            description = $"encode/decode round-trip mismatch: 0x{raw:X8} -> {block}+0x{decoded.Offset:X8} -> 0x{encoded:X8}";
            return false;
        }

        blockName = block.ToString();
        description = $"0x{raw:X8} = {block}+0x{decoded.Offset:X8} (source block size 0x{header.BlockSizes[blockIndex]:X8}); encode/decode PASS";
        return true;
    }

    private static async Task<byte[]> ReadBlobPrefixAsync(string path, long offset, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        if (offset < 0 || offset > fs.Length - length)
            throw new InvalidDataException($"Proof blob prefix 0x{offset:X}+0x{length:X} exceeds '{path}'.");
        fs.Seek(offset, SeekOrigin.Begin);
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await fs.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            total += read;
        }
        return buffer;
    }

    private static void Increment(Dictionary<string, int> map, string key) =>
        map[key] = map.TryGetValue(key, out var count) ? count + 1 : 1;

    private static string Render(NormalMapNativeRelocationAuditResult r, IReadOnlyList<string> details)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5b alpha 10a relocation/rebasing preflight");
        sb.AppendLine("================================================================");
        sb.AppendLine("READ/PLAN ONLY: no hardware fastfile is emitted by this stage.");
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine($"Hardware ready: {r.HardwareReady} (expected FALSE until nested block-cursor rebasing + remaining serializers are integrated)");
        sb.AppendLine();
        sb.AppendLine("TOTALS");
        sb.AppendLine($"  physical typed records audited: {r.PhysicalRecordsAudited:N0}");
        sb.AppendLine($"  known pointer fields audited:   {r.KnownPointerFieldsAudited:N0}");
        sb.AppendLine($"  packed pointer obligations:     {r.PackedPointers:N0}");
        sb.AppendLine($"  XAsset-table alias pointers:    {r.AliasPointers:N0}");
        sb.AppendLine($"  invalid packed pointers:        {r.InvalidPointers:N0}");
        sb.AppendLine($"  nested pointer audit pending:   {r.NestedPointerAuditPending}");
        sb.AppendLine();
        sb.AppendLine("DETAIL");
        foreach (var line in details) sb.AppendLine(line);
        sb.AppendLine("Alpha 10a conclusion");
        sb.AppendLine("  Known top-level packed pointers and XAsset aliases are now validated against the real T4 block table and round-trip through the encoder.");
        sb.AppendLine("  Next relocation iteration must simulate nested TEMP/VIRTUAL allocation while streaming the proof payloads, then rewrite packed references to their new block offsets.");
        return sb.ToString();
    }
}
