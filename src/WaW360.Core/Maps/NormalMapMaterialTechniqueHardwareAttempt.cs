using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4MaterialTechniqueHardwareAttemptFastFile(
    string Role,
    string Source,
    bool Candidate,
    int DeclaredMaterial,
    int ConvertedMaterial,
    int DeclaredTechniqueSet,
    int ConvertedTechniqueSet,
    int TechniqueGraphSlotsPending,
    int DroppedPcTechniqueSlotsNonNull,
    int OtherBlockers,
    string? CandidateZone,
    bool ZoneReadBackVerified,
    string? ProbeFile,
    bool ProbeRoundTripVerified,
    string? HardwareFastFile,
    string Status);

public sealed class NormalMapMaterialTechniqueHardwareAttemptResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4MaterialTechniqueHardwareAttemptFastFile> FastFiles { get; init; }
    public required int CandidateFastFiles { get; init; }
    public required int FullyConvertedFastFiles { get; init; }
    public required int TechniqueGraphSlotsPending { get; init; }
    public required int HardwareFastFilesEmitted { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5p / alpha 10j: first strict Material + TechniqueSet hardware-candidate attempt.
///
/// This stage deliberately targets only Stage-5j Material/Techset-only fastfiles. It will
/// emit a real .ff only if every non-null asset in that fastfile is physically represented,
/// all PC-only technique slots are null, no unconverted technique/pass/shader graph remains,
/// every Material->TechniqueSet packed reference is proven against the source TEMP offset,
/// the rebuilt Xbox zone parses back, and the final IWffu100/Xbox-block-deflate wrapper
/// decompresses byte-for-byte to that zone.
/// </summary>
public static class NormalMapMaterialTechniqueHardwareAttempt
{
    public static async Task<NormalMapMaterialTechniqueHardwareAttemptResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var jJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10e_readiness", $"{map.Name}_stage5_alpha10e_readiness.json");
        if (!File.Exists(jJson))
            throw new InvalidDataException("Stage 5p requires the verified Stage 5j candidate inventory.");

        var gate = JsonSerializer.Deserialize<NormalMapFoundationReadinessInventoryResult>(
            await File.ReadAllTextAsync(jJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5j readiness inventory.");
        if (!gate.Verified)
            throw new InvalidDataException("Stage 5j accounting is not verified; alpha 10j hardware retry refused.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10j_material_techset_block_coordinate_retry");
        Directory.CreateDirectory(workspace);
        var localZoneRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WaW360Converter", "native_zones_10j", Sanitize(map.Name));
        var localProbeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WaW360Converter", "ff_probes_10j", Sanitize(map.Name));
        Directory.CreateDirectory(localZoneRoot);
        Directory.CreateDirectory(localProbeRoot);

        var rows = new List<T4MaterialTechniqueHardwareAttemptFastFile>();
        var detail = new List<string>();

        foreach (var ff in gate.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ff.MaterialTechniqueCandidate)
            {
                rows.Add(new T4MaterialTechniqueHardwareAttemptFastFile(
                    ff.Role, ff.Source, false, 0, 0, 0, 0, 0, 0, ff.UnhandledNonNullAssets,
                    null, false, null, false, null, "not a Stage-5j Material/Techset-only candidate"));
                continue;
            }

            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            var doc = cached.Document;
            var zone = cached.Bytes;
            var simple = T4SimpleNativeAssetSerializer.Build(doc, zone);
            var mt = T4MaterialTechniqueLoadSerializer.Build(doc, zone, cancellationToken);

            var nonNullMaterials = doc.Assets.Where(a => a.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase) && a.ReferenceKind != T4AssetReferenceKind.Null).ToList();
            var nonNullTechsets = doc.Assets.Where(a => a.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase) && a.ReferenceKind != T4AssetReferenceKind.Null).ToList();
            var physicalMaterials = nonNullMaterials.Where(IsPhysical).ToList();
            var physicalTechsets = nonNullTechsets.Where(IsPhysical).ToList();
            var materialAliases = nonNullMaterials.Count - physicalMaterials.Count;
            var techAliases = nonNullTechsets.Count - physicalTechsets.Count;

            var convertedMaterials = mt.MaterialsByIndex.Count;
            var convertedTechsets = mt.TechniqueSetsByIndex.Values.Count(x => x.XboxPayload is not null);
            var graphPending = mt.TechniqueSetsByIndex.Values.Sum(x => x.NonNullConsoleTechniqueSlots);
            var droppedNonNull = mt.TechniqueSetsByIndex.Values.Sum(x => x.NonNullDroppedPcSlots);

            var otherBlockers = doc.Assets
                .Where(a => a.ReferenceKind != T4AssetReferenceKind.Null)
                .Where(a => !a.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase) &&
                            !a.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase))
                .Count(a => !simple.AssetsByIndex.ContainsKey(a.Index));

            var blockers = new List<string>();
            if (materialAliases != 0) blockers.Add($"{materialAliases} Material alias XAsset(s) are not resolved by the 10j subset");
            if (techAliases != 0) blockers.Add($"{techAliases} TechniqueSet alias XAsset(s) are not resolved by the 10j subset");
            if (convertedMaterials != physicalMaterials.Count)
                blockers.Add($"Material conversion coverage {convertedMaterials}/{physicalMaterials.Count}");
            if (convertedTechsets != physicalTechsets.Count)
                blockers.Add($"TechniqueSet conversion coverage {convertedTechsets}/{physicalTechsets.Count}");
            if (graphPending != 0)
                blockers.Add($"{graphPending} Xbox-valid technique slot(s) still own/reference a pass/shader graph");
            if (droppedNonNull != 0)
                blockers.Add($"{droppedNonNull} PC-only technique slot(s) 51..58 are non-null");
            if (otherBlockers != 0) blockers.Add($"{otherBlockers} non-Material/Techset blocker(s) remain");
            if (ff.ScriptStringAliasesPending != 0) blockers.Add($"{ff.ScriptStringAliasesPending} script-string alias(es) remain");
            if (ff.PackedSharedTargetsPending != 0) blockers.Add($"{ff.PackedSharedTargetsPending} tracked shared target(s) remain");
            if (ff.NestedPointerRebasePending) blockers.Add("tracked nested pointer rebasing remains pending");
            if (!mt.CoordinateProofVerified || mt.ProvenTechniqueSetTempOffset is null)
                blockers.Add("source TEMP coordinate proof is not verified");
            blockers.AddRange(mt.Warnings.Where(w => !w.StartsWith("INFO: ", StringComparison.Ordinal)).Take(12));

            string? candidateZone = null;
            string? probe = null;
            string? hardwareFf = null;
            var zoneVerified = false;
            var probeVerified = false;

            // The subset can only produce a candidate if every source physical Material and
            // TechniqueSet has a complete Xbox payload and there are no aliases/other blockers.
            if (blockers.Count == 0)
            {
                try
                {
                    var prepared = PreparePayloads(doc, zone, simple, mt);
                    candidateZone = Path.Combine(localZoneRoot, Path.GetFileNameWithoutExtension(ff.Source) + "_" + Sanitize(ff.Role) + ".n5pzone");
                    await EmitCandidateZoneAsync(candidateZone, doc, prepared, cancellationToken);
                    await VerifyCandidateZoneAsync(candidateZone, doc, prepared, cancellationToken);
                    zoneVerified = true;

                    var stem = Path.GetFileNameWithoutExtension(ff.Source) + "_" + Sanitize(ff.Role);
                    probe = Path.Combine(localProbeRoot, stem + ".ffprobe");
                    await FastFileRepacker.RepackZoneFileAsync(ff.Source, candidateZone, probe, WaWCompressionKind.XboxBlockDeflate, cancellationToken);
                    var verifyZone = Path.Combine(localProbeRoot, stem + ".verify.zone");
                    try
                    {
                        var decomp = await FastFileDecompressor.DecompressAutoAsync(probe, verifyZone, cancellationToken);
                        if (decomp.Platform != WaWPlatform.Xbox360 || decomp.Compression != WaWCompressionKind.XboxBlockDeflate)
                            throw new InvalidDataException("10j FF probe did not identify as Xbox360/XboxBlockDeflate.");
                        if (!await FilesEqualAsync(candidateZone, verifyZone, cancellationToken))
                            throw new InvalidDataException("10j FF probe did not decompress byte-for-byte to its candidate zone.");
                        probeVerified = true;
                    }
                    finally
                    {
                        try { File.Delete(verifyZone); } catch { }
                    }

                    var hardwareRoot = Path.Combine(outputRoot, map.Name, "hardware_candidates");
                    Directory.CreateDirectory(hardwareRoot);
                    hardwareFf = Path.Combine(hardwareRoot, Path.GetFileName(ff.Source));
                    File.Copy(probe, hardwareFf, overwrite: true);
                }
                catch (Exception ex) when (ex is InvalidDataException or OverflowException)
                {
                    blockers.Add("candidate emission refused: " + ex.Message);
                    candidateZone = null;
                    probe = null;
                    hardwareFf = null;
                    zoneVerified = false;
                    probeVerified = false;
                }
            }

            var status = hardwareFf is not null
                ? "HARDWARE CANDIDATE EMITTED"
                : blockers.Count == 0 ? "candidate gate did not complete" : string.Join("; ", blockers.Take(8));

            rows.Add(new T4MaterialTechniqueHardwareAttemptFastFile(
                ff.Role, ff.Source, true,
                nonNullMaterials.Count, convertedMaterials,
                nonNullTechsets.Count, convertedTechsets,
                graphPending, droppedNonNull, otherBlockers,
                candidateZone, zoneVerified, probe, probeVerified, hardwareFf, status));

            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: material={convertedMaterials}/{nonNullMaterials.Count}; techset={convertedTechsets}/{nonNullTechsets.Count}; techniqueGraphSlots={graphPending}; droppedPcSlotsNonNull={droppedNonNull}; otherBlockers={otherBlockers}; zoneVerified={zoneVerified}; ffRoundTrip={probeVerified}; hardwareFF={(hardwareFf ?? "NO")}");
            foreach (var w in mt.Warnings.Take(12)) detail.Add("  NOTE: " + w);
            if (hardwareFf is null)
                foreach (var b in blockers.Take(12)) detail.Add("  BLOCK: " + b);
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10j_material_techset_block_coordinate_retry.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10j_material_techset_block_coordinate_retry.json");
        var result = new NormalMapMaterialTechniqueHardwareAttemptResult
        {
            MapName = map.Name,
            Workspace = workspace,
            FastFiles = rows,
            CandidateFastFiles = rows.Count(x => x.Candidate),
            FullyConvertedFastFiles = rows.Count(x => x.CandidateZone is not null && x.ZoneReadBackVerified),
            TechniqueGraphSlotsPending = rows.Sum(x => x.TechniqueGraphSlotsPending),
            HardwareFastFilesEmitted = rows.Count(x => x.HardwareFastFile is not null),
            Verified = true,
            HardwareReady = rows.Any(x => x.HardwareFastFile is not null),
            TextReportFile = txt,
            JsonReportFile = json
        };
        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private sealed record PreparedPayloads(
        IReadOnlyDictionary<int, byte[]> PayloadByAssetIndex,
        IReadOnlyDictionary<int, uint> DestinationOffsetByAssetIndex,
        IReadOnlyList<uint> BlockSizes,
        uint FirstPhysicalTempOffset);

    private static PreparedPayloads PreparePayloads(
        T4ZoneDocument doc,
        byte[] sourceZone,
        T4SimpleNativeAssetScan simple,
        T4MaterialTechniqueLoadScan mt)
    {
        if (!mt.CoordinateProofVerified || mt.ProvenTechniqueSetTempOffset is null)
            throw new InvalidDataException("alpha 10j requires a source-proven TEMP coordinate from unanimous Material.techniqueSet pointers");
        if (doc.ScriptStringCount != 0)
            throw new InvalidDataException("alpha 10j micro-load hardware path currently requires zero script strings");

        var physical = doc.Assets.Where(IsPhysical).OrderBy(a => a.Index).ToList();
        var physicalTechsets = physical.Where(a => a.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase)).ToList();
        if (physicalTechsets.Count != 1 || physical.Count == 0 || physical[0].Index != physicalTechsets[0].Index)
            throw new InvalidDataException("alpha 10j coordinate proof requires the sole physical TechniqueSet to be the first physical XAsset");

        var payloads = new Dictionary<int, byte[]>();
        foreach (var kv in simple.AssetsByIndex) payloads[kv.Key] = kv.Value.Payload;
        foreach (var kv in mt.TechniqueSetsByIndex)
        {
            if (kv.Value.XboxPayload is null)
                throw new InvalidDataException($"TechniqueSet XAsset#{kv.Key} still owns/references an unconverted technique graph.");
            payloads[kv.Key] = kv.Value.XboxPayload;
        }

        var firstTemp = mt.ProvenTechniqueSetTempOffset.Value;
        var offsets = new Dictionary<int, uint>();
        uint cursor = firstTemp;
        foreach (var asset in physical)
        {
            cursor = Align(cursor, 4);
            offsets[asset.Index] = cursor;
            if (asset.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase))
            {
                if (!mt.MaterialsByIndex.TryGetValue(asset.Index, out var material))
                    throw new InvalidDataException($"No complete Material payload for XAsset#{asset.Index}.");
                cursor = checked(cursor + (uint)material.XboxPayloadTemplate.Length);
            }
            else if (payloads.TryGetValue(asset.Index, out var p))
            {
                cursor = checked(cursor + (uint)p.Length);
            }
            else
            {
                throw new InvalidDataException($"Non-null physical XAsset#{asset.Index} {asset.TypeName} has no alpha-10j payload.");
            }
        }

        var techAsset = physicalTechsets[0];
        foreach (var kv in mt.MaterialsByIndex)
        {
            var material = kv.Value;
            uint destTech = 0;
            if (material.SourceTechniquePointer != 0)
            {
                var decoded = T4ZonePointer.Decode(material.SourceTechniquePointer);
                if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block != T4ZoneBlock.Temp || decoded.Offset != firstTemp)
                    throw new InvalidDataException($"Material '{material.Name}' TechniqueSet pointer {decoded} disagrees with proven source TEMP+0x{firstTemp:X8}; rebasing refused.");
                destTech = T4ZonePointer.Encode(T4ZoneBlock.Temp, offsets[techAsset.Index]);
            }
            payloads[kv.Key] = T4MaterialTechniqueLoadSerializer.PatchMaterialTechniquePointer(material.XboxPayloadTemplate, destTech);
        }

        // Preserve every source block allocation except TEMP.  Only Material/TechniqueSet fixed
        // layouts shrink in this micro-load path; RawFile bytes remain length-identical.
        long tempDelta = 0;
        foreach (var asset in physical)
        {
            if (!mt.SourcePhysicalSpansByAssetIndex.TryGetValue(asset.Index, out var span))
                throw new InvalidDataException($"Missing source physical span for XAsset#{asset.Index} {asset.TypeName}");
            var sourceLength = checked(span.SourceEnd - span.SourceOffset);
            int destLength;
            if (asset.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase))
                destLength = mt.MaterialsByIndex[asset.Index].XboxPayloadTemplate.Length;
            else
                destLength = payloads[asset.Index].Length;
            if (asset.TypeName.Equals("rawfile", StringComparison.OrdinalIgnoreCase) && destLength != sourceLength)
                throw new InvalidDataException($"RawFile XAsset#{asset.Index} changed serialized length {sourceLength}->{destLength}; block-preserving 10j path refuses it");
            if (asset.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase) || asset.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase))
                tempDelta += destLength - sourceLength;
        }

        var blockSizes = doc.Header.BlockSizes.ToArray();
        var newTemp = checked((long)doc.Header.TempBlockSize + tempDelta);
        if (newTemp <= 0 || newTemp > uint.MaxValue)
            throw new InvalidDataException($"alpha 10j computed invalid TEMP size {newTemp}");
        blockSizes[(int)T4ZoneBlock.Temp] = checked((uint)newTemp);

        if (cursor > blockSizes[(int)T4ZoneBlock.Temp])
            throw new InvalidDataException($"alpha 10j destination TEMP cursor 0x{cursor:X} exceeds planned TEMP block 0x{blockSizes[(int)T4ZoneBlock.Temp]:X}");

        return new PreparedPayloads(payloads, offsets, blockSizes, firstTemp);
    }

    private static async Task EmitCandidateZoneAsync(string path, T4ZoneDocument doc, PreparedPayloads prepared, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        WriteU32(fs, 0);
        WriteU32(fs, 0);
        for (var i = 0; i < 7; i++) WriteU32(fs, prepared.BlockSizes[i]);

        WriteU32(fs, checked((uint)doc.ScriptStringCount));
        WriteU32(fs, doc.ScriptStringCount == 0 ? 0u : T4ZonePointer.Following);
        WriteU32(fs, checked((uint)doc.AssetCount));
        WriteU32(fs, doc.AssetCount == 0 ? 0u : T4ZonePointer.Following);

        var inlineStrings = new List<byte[]>();
        foreach (var s in doc.ScriptStrings)
        {
            if (s.Inline && s.Value is not null)
            {
                WriteU32(fs, T4ZonePointer.Following);
                inlineStrings.Add(Encoding.UTF8.GetBytes(s.Value + "\0"));
            }
            else if (s.RawPointer == 0) WriteU32(fs, 0);
            else throw new InvalidDataException("alpha 10j candidate contains unresolved script-string alias");
        }
        foreach (var bytes in inlineStrings) await fs.WriteAsync(bytes, ct);

        foreach (var asset in doc.Assets.OrderBy(a => a.Index))
        {
            WriteU32(fs, checked((uint)T4XboxAssetTypeMap.GetXboxTypeId(asset.TypeName)));
            if (asset.ReferenceKind == T4AssetReferenceKind.Null) WriteU32(fs, 0);
            else if (IsPhysical(asset))
            {
                if (!prepared.PayloadByAssetIndex.ContainsKey(asset.Index))
                    throw new InvalidDataException($"candidate XAsset#{asset.Index} {asset.TypeName} has no payload");
                WriteU32(fs, asset.ReferenceKind == T4AssetReferenceKind.InlineShared ? T4ZonePointer.Following : T4ZonePointer.Insert);
            }
            else throw new InvalidDataException($"candidate still contains unresolved alias XAsset#{asset.Index} {asset.TypeName} raw=0x{asset.RawHeader:X8}");
        }

        var payloadStart = checked((long)(doc.AssetTableOffset + doc.AssetCount * 8L));
        if (fs.Position != payloadStart)
            throw new InvalidDataException($"alpha 10j serialized prefix mismatch: planned payload start 0x{payloadStart:X}, stream 0x{fs.Position:X}");

        uint logicalCursor = prepared.FirstPhysicalTempOffset;
        foreach (var asset in doc.Assets.OrderBy(a => a.Index))
        {
            if (!IsPhysical(asset)) continue;
            if (!prepared.PayloadByAssetIndex.TryGetValue(asset.Index, out var payload))
                throw new InvalidDataException($"missing prepared payload for XAsset#{asset.Index}");
            var aligned = Align(logicalCursor, 4);
            while (logicalCursor < aligned) { fs.WriteByte(0); logicalCursor++; }
            if (!prepared.DestinationOffsetByAssetIndex.TryGetValue(asset.Index, out var expected) || expected != logicalCursor)
                throw new InvalidDataException($"destination TEMP offset mismatch for XAsset#{asset.Index}: planned 0x{expected:X}, stream 0x{logicalCursor:X}");
            await fs.WriteAsync(payload, ct);
            logicalCursor = checked(logicalCursor + (uint)payload.Length);
        }

        await fs.FlushAsync(ct);
        if (fs.Length > uint.MaxValue) throw new InvalidDataException("10j candidate zone exceeds 32-bit T4 size field");
        fs.Seek(0, SeekOrigin.Begin);
        WriteU32(fs, checked((uint)fs.Length));
        await fs.FlushAsync(ct);
    }

    private static async Task VerifyCandidateZoneAsync(string path, T4ZoneDocument source, PreparedPayloads prepared, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var parsed = T4ZoneParser.ParseZoneBytes(path, bytes, WaWPlatform.Xbox360, WaWCompressionKind.XboxBlockDeflate, Endian.Big);
        if (parsed.AssetCount != source.AssetCount || parsed.ScriptStringCount != source.ScriptStringCount)
            throw new InvalidDataException("10j candidate zone header/list counts changed on parse-back");
        for (var i = 0; i < source.Assets.Count; i++)
        {
            if (parsed.Assets[i].TypeId != T4XboxAssetTypeMap.GetXboxTypeId(source.Assets[i].TypeName))
                throw new InvalidDataException($"10j XAsset type remap mismatch at index {i}");
            if (source.Assets[i].ReferenceKind == T4AssetReferenceKind.Null)
            {
                if (parsed.Assets[i].RawHeader != 0) throw new InvalidDataException($"10j null XAsset#{i} became non-null");
            }
            else if (!IsPhysical(source.Assets[i]))
                throw new InvalidDataException($"10j parse-back encountered unsupported alias XAsset#{i}");
        }

        // Locate the payload tail by replaying script strings + XAsset table exactly as the parser did.
        for (var b = 0; b < 7; b++)
            if (parsed.Header.BlockSizes[b] != prepared.BlockSizes[b])
                throw new InvalidDataException($"10j block size changed on parse-back for {(T4ZoneBlock)b}: parsed=0x{parsed.Header.BlockSizes[b]:X}, planned=0x{prepared.BlockSizes[b]:X}");
        var payloadStart = checked((int)(parsed.AssetTableOffset + parsed.AssetCount * 8L));
        var pos = payloadStart;
        uint logical = prepared.FirstPhysicalTempOffset;
        foreach (var asset in source.Assets.OrderBy(a => a.Index))
        {
            if (!IsPhysical(asset)) continue;
            var aligned = Align(logical, 4);
            var pad = checked((int)(aligned - logical));
            for (var p = 0; p < pad; p++) if (bytes[pos + p] != 0) throw new InvalidDataException("10j non-zero top-level alignment padding");
            pos += pad;
            logical = aligned;
            var expected = prepared.PayloadByAssetIndex[asset.Index];
            if (pos > bytes.Length - expected.Length || !bytes.AsSpan(pos, expected.Length).SequenceEqual(expected))
                throw new InvalidDataException($"10j payload read-back mismatch for XAsset#{asset.Index} {asset.TypeName}");
            pos += expected.Length;
            logical = checked(logical + (uint)expected.Length);
        }
    }

    private static bool IsPhysical(T4AssetEntry a) =>
        a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared;

    private static uint Align(uint value, uint alignment) =>
        alignment <= 1 ? value : checked((value + alignment - 1) & ~(alignment - 1));

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }

    private static async Task<bool> FilesEqualAsync(string a, string b, CancellationToken ct)
    {
        var ai = new FileInfo(a); var bi = new FileInfo(b);
        if (ai.Length != bi.Length) return false;
        await using var x = File.OpenRead(a); await using var y = File.OpenRead(b);
        var xb = new byte[1024 * 1024]; var yb = new byte[xb.Length];
        while (true)
        {
            var xn = await x.ReadAsync(xb.AsMemory(), ct); var yn = await y.ReadAsync(yb.AsMemory(), ct);
            if (xn != yn) return false; if (xn == 0) return true;
            if (!xb.AsSpan(0, xn).SequenceEqual(yb.AsSpan(0, yn))) return false;
        }
    }

    private static string Render(NormalMapMaterialTechniqueHardwareAttemptResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5p alpha 10j block-coordinate-proof Material/TechniqueSet hardware-candidate retry");
        sb.AppendLine(new string('=', 96));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Stage-5j Material/Techset-only candidates: {r.CandidateFastFiles:N0}");
        sb.AppendLine($"Fully converted + zone-readback candidates: {r.FullyConvertedFastFiles:N0}");
        sb.AppendLine($"Technique graph slots still pending: {r.TechniqueGraphSlotsPending:N0}");
        sb.AppendLine($"REAL Xbox hardware .ff emitted: {r.HardwareFastFilesEmitted:N0}");
        sb.AppendLine($"Verified fail-closed accounting: {r.Verified}");
        sb.AppendLine($"At least one hardware candidate ready: {r.HardwareReady}");
        sb.AppendLine();
        sb.AppendLine("10j derives the source TEMP coordinate from unanimous Material.techniqueSet pointers and preserves the source block directory and NEVER copies PC D3D shader/pass graphs into an Xbox candidate. A real .ff is emitted only for the strict no-technique-graph subset after native-zone parse-back and Xbox FF decompression round-trip.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }

    private static string Sanitize(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
