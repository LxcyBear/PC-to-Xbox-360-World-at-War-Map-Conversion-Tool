using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4NativeDestinationRelocation(
    uint SourceRaw,
    T4ZoneBlock SourceBlock,
    uint SourceOffset,
    uint DestinationRaw,
    T4ZoneBlock DestinationBlock,
    uint DestinationOffset,
    int ReferenceCount);

public sealed record T4NativeDestinationAsset(
    int XAssetIndex,
    string TypeName,
    int XboxTypeId,
    T4AssetReferenceKind ReferenceKind,
    uint SourceRawHeader,
    string? ProofContainer,
    long? ProofBlobOffset,
    int BlobLength,
    T4ZoneBlock DestinationBlock,
    uint DestinationOffset,
    int PatchedPackedPointers,
    int InlineOrNullPointers);

public sealed record T4NativeDestinationFastFile(
    string Role,
    string Source,
    string SourceSha256,
    string LayoutManifest,
    string PatchManifest,
    int PhysicalAssets,
    int AliasAssets,
    int UniquePackedTargets,
    int PatchedPackedPointers,
    int InlineOrNullPointers,
    IReadOnlyDictionary<string, uint> DestinationBlockSizes,
    bool NestedPayloadMaterializationPending,
    bool Verified);

public sealed class NormalMapNativeDestinationLayoutResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4NativeDestinationFastFile> FastFiles { get; init; }
    public required int PhysicalAssets { get; init; }
    public required int AliasAssets { get; init; }
    public required int UniquePackedTargets { get; init; }
    public required int PatchedPackedPointers { get; init; }
    public required bool NestedPayloadMaterializationPending { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5c / alpha 10b: destination block layout + real pointer patch planning.
///
/// Assigns deterministic destination addresses to every Stage-5-tracked physical asset,
/// builds a one-to-one source-packed-target -> destination-packed-target relocation map,
/// and applies that map to the actual known pointer fields from the verified big-endian
/// proof blobs. The patched prefixes are written to a compact patch manifest and are
/// independently decoded/verified before PASS.
///
/// This stage intentionally does NOT claim that nested split-block payloads have been
/// materialized yet. Those remain an explicit blocker for hardwareReady.
/// </summary>
public static class NormalMapNativeDestinationLayout
{
    private sealed class PlanDocumentModel
    {
        public string Source { get; set; } = string.Empty;
        public string SourceSha256 { get; set; } = string.Empty;
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
    private sealed record PointerOccurrence(int XAssetIndex, string TypeName, int FieldOffset, string FieldName, uint Raw);
    private sealed record PrefixPatch(int XAssetIndex, string TypeName, int PrefixLength, string PrefixBase64, int PatchedCount);

    public static async Task<NormalMapNativeDestinationLayoutResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var writerJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10a_native_writer", $"{map.Name}_stage5_alpha10a_native_writer.json");
        var relocationJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10a_relocation", $"{map.Name}_stage5_alpha10a_relocation.json");
        if (!File.Exists(writerJson) || !File.Exists(relocationJson))
            throw new InvalidDataException("Stage 5c requires verified Stage 5a + 5b results.");

        var writer = JsonSerializer.Deserialize<NormalMapNativeWriterFoundationResult>(
            await File.ReadAllTextAsync(writerJson, cancellationToken),
            JsonOptions()) ?? throw new InvalidDataException("Could not read Stage 5a result.");
        var preflight = JsonSerializer.Deserialize<NormalMapNativeRelocationAuditResult>(
            await File.ReadAllTextAsync(relocationJson, cancellationToken),
            JsonOptions()) ?? throw new InvalidDataException("Could not read Stage 5b result.");
        if (!writer.Verified || !preflight.Verified || preflight.InvalidPointers != 0)
            throw new InvalidDataException("Stage 5a/5b is not fully verified; Stage 5c refuses to create a destination layout.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10b_destination_layout");
        Directory.CreateDirectory(workspace);
        var ffResults = new List<T4NativeDestinationFastFile>();
        var reportLines = new List<string>();

        foreach (var ff in writer.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = JsonSerializer.Deserialize<PlanDocumentModel>(
                await File.ReadAllTextAsync(ff.PlanFile, cancellationToken), JsonOptions())
                ?? throw new InvalidDataException($"Could not read write plan '{ff.PlanFile}'.");

            var sourceCache = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            if (!string.Equals(sourceCache.SourceSha256, ff.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Source SHA-256 changed for '{ff.Source}'. Refusing stale Stage 5 relocation state.");

            // Destination cursors.  Top-level tracked structures live in TEMP.  We reserve
            // their full verified proof-blob spans in this alpha so addresses are stable and
            // non-overlapping while nested split-block materialization is brought online.
            var cursors = Enum.GetValues<T4ZoneBlock>().ToDictionary(b => b, _ => 0u);
            var destinationAssets = new List<T4NativeDestinationAsset>();
            var occurrences = new List<PointerOccurrence>();
            var physicalPlans = new List<(PlanAssetModel Asset, uint DestOffset, byte[] Prefix)>();
            var aliases = 0;
            var inlineOrNullTotal = 0;

            foreach (var asset in plan.Assets.OrderBy(a => a.XAssetIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (asset.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias)
                {
                    aliases++;
                    var d = T4ZonePointer.Decode(asset.SourceRawHeader);
                    if (d.Kind != T4ZonePointerKind.Offset || d.Block is null)
                        throw new InvalidDataException($"Alias XAsset#{asset.XAssetIndex} {asset.TypeName} is not a packed pointer.");
                    occurrences.Add(new PointerOccurrence(asset.XAssetIndex, asset.TypeName, -1, "xasset.alias", asset.SourceRawHeader));
                    continue;
                }
                if (asset.ReferenceKind is not (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
                    continue;
                if (string.IsNullOrWhiteSpace(asset.ProofContainer) || asset.ProofBlobOffset is null || asset.BlobLength <= 0)
                    throw new InvalidDataException($"Physical XAsset#{asset.XAssetIndex} {asset.TypeName} is missing its verified proof blob.");

                var block = asset.DefaultBlock;
                var destOffset = Align(cursors[block], 4);
                var end = checked(destOffset + (uint)asset.BlobLength);
                if (end > 0x20000000u)
                    throw new InvalidDataException($"Destination {block} block exceeds T4's 29-bit packed-offset address space while placing XAsset#{asset.XAssetIndex} {asset.TypeName}.");
                _ = T4ZonePointer.Encode(block, destOffset);
                cursors[block] = end;

                var fields = GetTopLevelPointerFields(asset.TypeName);
                var prefixLength = fields.Count == 0 ? 0 : fields.Max(f => f.Offset) + 4;
                if (prefixLength > asset.BlobLength)
                    throw new InvalidDataException($"{asset.TypeName} XAsset#{asset.XAssetIndex} blob is smaller than its pointer-field prefix.");
                var prefix = prefixLength == 0
                    ? Array.Empty<byte>()
                    : await ReadBlobPrefixAsync(asset.ProofContainer!, asset.ProofBlobOffset.Value, prefixLength, cancellationToken);

                var inlineOrNull = 0;
                foreach (var field in fields)
                {
                    var raw = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(field.Offset, 4));
                    var decoded = T4ZonePointer.Decode(raw);
                    if (decoded.Kind is T4ZonePointerKind.Null or T4ZonePointerKind.Following or T4ZonePointerKind.Insert)
                    {
                        inlineOrNull++;
                        continue;
                    }
                    if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null)
                        throw new InvalidDataException($"XAsset#{asset.XAssetIndex} {asset.TypeName}.{field.Name} has invalid pointer 0x{raw:X8}.");
                    occurrences.Add(new PointerOccurrence(asset.XAssetIndex, asset.TypeName, field.Offset, field.Name, raw));
                }
                inlineOrNullTotal += inlineOrNull;
                physicalPlans.Add((asset, destOffset, prefix));
            }

            // Give every unique source packed target a deterministic destination symbol in
            // the SAME T4 block.  These are real encoded destination addresses and are used
            // to exercise the patch engine against the actual verified BE asset bytes.
            // A 16-byte reservation per target deliberately prevents accidental aliasing.
            var grouped = occurrences
                .GroupBy(o => o.Raw)
                .OrderBy(g => T4ZonePointer.Decode(g.Key).Block)
                .ThenBy(g => T4ZonePointer.Decode(g.Key).Offset)
                .ToArray();
            var relocationMap = new Dictionary<uint, T4NativeDestinationRelocation>();
            foreach (var group in grouped)
            {
                var sourceRaw = group.Key;
                var decoded = T4ZonePointer.Decode(sourceRaw);
                if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null)
                    throw new InvalidDataException($"Relocation source 0x{sourceRaw:X8} is not a packed T4 pointer.");
                var block = decoded.Block.Value;
                var destOffset = Align(cursors[block], 16);
                var targetEnd = checked(destOffset + 16u);
                if (targetEnd > 0x20000000u)
                    throw new InvalidDataException($"Destination {block} relocation arena exceeds T4's 29-bit packed-offset address space.");
                cursors[block] = targetEnd;
                var destRaw = T4ZonePointer.Encode(block, destOffset);
                var roundTrip = T4ZonePointer.Decode(destRaw);
                if (roundTrip.Kind != T4ZonePointerKind.Offset || roundTrip.Block != block || roundTrip.Offset != destOffset)
                    throw new InvalidDataException($"Destination pointer round-trip failed for {block}+0x{destOffset:X8}.");
                relocationMap[sourceRaw] = new T4NativeDestinationRelocation(
                    sourceRaw, block, decoded.Offset, destRaw, block, destOffset, group.Count());
            }

            var prefixPatches = new List<PrefixPatch>();
            var patchedTotal = 0;
            foreach (var item in physicalPlans)
            {
                var asset = item.Asset;
                var prefix = item.Prefix.ToArray();
                var patched = 0;
                foreach (var field in GetTopLevelPointerFields(asset.TypeName))
                {
                    var raw = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(field.Offset, 4));
                    if (!relocationMap.TryGetValue(raw, out var reloc)) continue;
                    BinaryPrimitives.WriteUInt32BigEndian(prefix.AsSpan(field.Offset, 4), reloc.DestinationRaw);
                    var verifyRaw = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(field.Offset, 4));
                    var verify = T4ZonePointer.Decode(verifyRaw);
                    if (verify.Kind != T4ZonePointerKind.Offset || verify.Block != reloc.DestinationBlock || verify.Offset != reloc.DestinationOffset)
                        throw new InvalidDataException($"Patched pointer verification failed for XAsset#{asset.XAssetIndex} {asset.TypeName}.{field.Name}.");
                    patched++;
                    patchedTotal++;
                }
                prefixPatches.Add(new PrefixPatch(asset.XAssetIndex, asset.TypeName, prefix.Length, Convert.ToBase64String(prefix), patched));
                destinationAssets.Add(new T4NativeDestinationAsset(
                    asset.XAssetIndex, asset.TypeName, asset.XboxTypeId, asset.ReferenceKind, asset.SourceRawHeader,
                    asset.ProofContainer, asset.ProofBlobOffset, asset.BlobLength, asset.DefaultBlock, item.DestOffset,
                    patched, GetTopLevelPointerFields(asset.TypeName).Count - patched));
            }

            // Alias entries are patched through the exact same relocation dictionary.
            foreach (var asset in plan.Assets.Where(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias).OrderBy(a => a.XAssetIndex))
            {
                if (!relocationMap.TryGetValue(asset.SourceRawHeader, out var reloc))
                    throw new InvalidDataException($"No destination relocation exists for alias XAsset#{asset.XAssetIndex}.");
                destinationAssets.Add(new T4NativeDestinationAsset(
                    asset.XAssetIndex, asset.TypeName, asset.XboxTypeId, asset.ReferenceKind, asset.SourceRawHeader,
                    null, null, 0, reloc.DestinationBlock, reloc.DestinationOffset, 1, 0));
                patchedTotal++;
            }

            var stem = Path.GetFileNameWithoutExtension(ff.Source) + "_" + Sanitize(ff.Role);
            var layoutPath = Path.Combine(workspace, stem + ".destination_layout.json");
            var patchPath = Path.Combine(workspace, stem + ".pointer_patches.json");
            var blockSizes = cursors.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            var layoutDoc = new
            {
                format = "WaW360Converter Stage 5c destination layout",
                version = 1,
                source = ff.Source,
                sourceSha256 = ff.SourceSha256,
                policy = new
                {
                    topLevelTrackedBlock = "TEMP",
                    targetBlockPreserved = true,
                    targetReservationBytes = 16,
                    nestedPayloadMaterializationPending = true,
                    note = "Packed targets receive real destination T4 addresses now; nested target payload materialization is deliberately deferred and keeps hardwareReady false."
                },
                destinationBlockSizes = blockSizes,
                assets = destinationAssets.OrderBy(a => a.XAssetIndex).ToArray(),
                relocations = relocationMap.Values.OrderBy(r => r.DestinationBlock).ThenBy(r => r.DestinationOffset).ToArray()
            };
            await File.WriteAllTextAsync(layoutPath, JsonSerializer.Serialize(layoutDoc, PrettyJson()), Encoding.UTF8, cancellationToken);
            await File.WriteAllTextAsync(patchPath, JsonSerializer.Serialize(prefixPatches, PrettyJson()), Encoding.UTF8, cancellationToken);

            // Read both manifests back from disk before PASS.
            using (var parsedLayout = JsonDocument.Parse(await File.ReadAllTextAsync(layoutPath, cancellationToken)))
            {
                var relocationCount = parsedLayout.RootElement.GetProperty("relocations").GetArrayLength();
                if (relocationCount != relocationMap.Count)
                    throw new InvalidDataException($"Stage 5c layout read-back mismatch: wrote {relocationMap.Count:N0} relocations, read {relocationCount:N0}.");
            }
            var parsedPatches = JsonSerializer.Deserialize<List<PrefixPatch>>(await File.ReadAllTextAsync(patchPath, cancellationToken), JsonOptions())
                ?? throw new InvalidDataException("Stage 5c could not read back its pointer patch manifest.");
            if (parsedPatches.Sum(p => p.PatchedCount) != patchedTotal - aliases)
                throw new InvalidDataException("Stage 5c pointer-patch read-back count mismatch.");

            reportLines.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: physical={physicalPlans.Count:N0}; aliases={aliases:N0}; uniqueTargets={relocationMap.Count:N0}; patched={patchedTotal:N0}");
            foreach (var kv in blockSizes.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                reportLines.Add($"  {kv.Key}: plannedSize=0x{kv.Value:X8} ({kv.Value:N0} bytes)");

            ffResults.Add(new T4NativeDestinationFastFile(
                ff.Role, ff.Source, ff.SourceSha256, layoutPath, patchPath,
                physicalPlans.Count, aliases, relocationMap.Count, patchedTotal, inlineOrNullTotal,
                blockSizes, true, true));
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10b_destination_layout.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10b_destination_layout.json");
        var result = new NormalMapNativeDestinationLayoutResult
        {
            MapName = map.Name,
            Workspace = workspace,
            FastFiles = ffResults,
            PhysicalAssets = ffResults.Sum(x => x.PhysicalAssets),
            AliasAssets = ffResults.Sum(x => x.AliasAssets),
            UniquePackedTargets = ffResults.Sum(x => x.UniquePackedTargets),
            PatchedPackedPointers = ffResults.Sum(x => x.PatchedPackedPointers),
            NestedPayloadMaterializationPending = true,
            Verified = ffResults.All(x => x.Verified),
            HardwareReady = false,
            TextReportFile = txt,
            JsonReportFile = json
        };
        await File.WriteAllTextAsync(txt, Render(result, reportLines), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    internal static IReadOnlyList<(int Offset, string Name)> PointerFieldsFor(string type) =>
        GetTopLevelPointerFields(type).Select(x => (x.Offset, x.Name)).ToArray();

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
                new PointerField(0,"name"),new PointerField(8,"boneNames"),new PointerField(12,"parentList"),new PointerField(16,"quats"),
                new PointerField(20,"trans"),new PointerField(24,"partClassification"),new PointerField(28,"baseMat"),new PointerField(32,"surfs"),
                new PointerField(36,"materialHandles"),new PointerField(152,"collSurfs"),new PointerField(164,"boneInfo"),new PointerField(212,"physPreset"),
                new PointerField(216,"physGeoms"),new PointerField(220,"collmap"),new PointerField(224,"physConstraints")
            };
        if (type.Equals("fx", StringComparison.OrdinalIgnoreCase))
            return new[] { new PointerField(0,"name"), new PointerField(32,"elemDefs") };
        if (type.Equals("weapon", StringComparison.OrdinalIgnoreCase)) return WeaponPointerFields;
        return Array.Empty<PointerField>();
    }

    private static readonly IReadOnlyList<PointerField> WeaponPointerFields = BuildWeaponPointerFields();
    private static IReadOnlyList<PointerField> BuildWeaponPointerFields()
    {
        var f = new List<PointerField> { new(0,"szInternalName"), new(4,"szDisplayName"), new(8,"szOverlayName") };
        for (var i=0;i<16;i++) f.Add(new PointerField(12+i*4,$"gunXModel[{i}]"));
        f.Add(new PointerField(76,"handXModel"));
        for (var i=0;i<35;i++) f.Add(new PointerField(80+i*4,$"szXAnims[{i}]"));
        f.Add(new PointerField(220,"szModeName"));
        f.Add(new PointerField(380,"viewFlashEffect")); f.Add(new PointerField(384,"worldFlashEffect"));
        for (var o=388;o<=620;o+=4) f.Add(new PointerField(o,$"sound@0x{o:X}"));
        f.Add(new PointerField(624,"bounceSound"));
        foreach (var p in new (int,string)[] { (628,"standMountedWeapdef"),(632,"crouchMountedWeapdef"),(636,"proneMountedWeapdef"),(652,"viewShellEjectEffect"),(656,"worldShellEjectEffect"),(660,"viewLastShotEjectEffect"),(664,"worldLastShotEjectEffect"),(668,"reticleCenter"),(672,"reticleSide") }) f.Add(new PointerField(p.Item1,p.Item2));
        for (var i=0;i<16;i++) f.Add(new PointerField(900+i*4,$"worldModel[{i}]"));
        foreach (var p in new (int,string)[] { (964,"worldClipModel"),(968,"rocketModel"),(972,"knifeModel"),(976,"worldKnifeModel"),(980,"mountedModel"),(984,"hudIcon"),(992,"ammoCounterIcon"),(1008,"szAmmoName"),(1016,"szClipName"),(1040,"szSharedAmmoCapName"),(1308,"overlayMaterial"),(1312,"overlayMaterialLowRes"),(1552,"killIcon"),(1564,"dpadIcon"),(1592,"szAltWeaponName"),(1664,"projectileModel"),(1672,"projExplosionEffect"),(1680,"projDudEffect"),(1684,"projExplosionSound"),(1688,"projDudSound"),(1692,"mortarShellSound"),(1696,"tankShellSound"),(1976,"projTrailEffect"),(2008,"projIgnitionEffect"),(2012,"projIgnitionSound"),(2176,"aiVsAiAccuracyGraphName"),(2180,"aiVsPlayerAccuracyGraphName"),(2184,"aiVsAiAccuracyGraphKnots"),(2188,"aiVsPlayerAccuracyGraphKnots"),(2192,"originalAiVsAiAccuracyGraphKnots"),(2196,"originalAiVsPlayerAccuracyGraphKnots"),(2288,"szUseHintString"),(2292,"dropHintString"),(2312,"szScript"),(2428,"fireRumble"),(2432,"meleeImpactRumble"),(2452,"flameTableFirstPerson"),(2456,"flameTableThirdPerson"),(2460,"flameTableFirstPersonPtr"),(2464,"flameTableThirdPersonPtr"),(2468,"tagFx_preparationEffect"),(2472,"tagFlash_preparationEffect") }) f.Add(new PointerField(p.Item1,p.Item2));
        return f.OrderBy(x=>x.Offset).ToArray();
    }

    private static async Task<byte[]> ReadBlobPrefixAsync(string path, long offset, int count, CancellationToken ct)
    {
        var b = new byte[count];
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64*1024, FileOptions.Asynchronous|FileOptions.RandomAccess);
        fs.Seek(offset, SeekOrigin.Begin);
        var total=0;
        while(total<count)
        {
            var n=await fs.ReadAsync(b.AsMemory(total,count-total),ct);
            if(n==0) throw new EndOfStreamException();
            total+=n;
        }
        return b;
    }

    private static uint Align(uint value, uint alignment) => checked((value + alignment - 1u) & ~(alignment - 1u));
    private static string Sanitize(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };

    private static string Render(NormalMapNativeDestinationLayoutResult r, IReadOnlyList<string> details)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5c alpha 10b destination layout + pointer rebase");
        sb.AppendLine(new string('=', 76));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Physical tracked assets: {r.PhysicalAssets:N0}");
        sb.AppendLine($"Alias assets: {r.AliasAssets:N0}");
        sb.AppendLine($"Unique packed targets: {r.UniquePackedTargets:N0}");
        sb.AppendLine($"Patched packed pointer occurrences: {r.PatchedPackedPointers:N0}");
        sb.AppendLine($"Nested payload materialization pending: {r.NestedPayloadMaterializationPending}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine($"Hardware ready: {r.HardwareReady}");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: alpha 10b assigns and patches real destination T4 packed addresses, but nested target payload bytes are not yet materialized into those destination reservations. This is a relocation-engine proof, not a game-ready .ff.");
        sb.AppendLine();
        foreach (var line in details) sb.AppendLine(line);
        return sb.ToString();
    }
}
