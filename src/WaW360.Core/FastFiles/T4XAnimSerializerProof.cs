using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4XAnimProofAsset(
    string Name,
    long SourceOffset,
    int SourceSerializedBytes,
    int XboxSerializedBytes,
    bool HasDelta,
    ushort NumFrames,
    byte BoneCount,
    byte NotifyCount);

public sealed record T4XAnimDiagnosticCandidate(string Name, int Offset, int End);
public sealed record T4RejectedInlineXAnimCandidate(
    string Name,
    int Offset,
    string Reason,
    ushort FrameCount,
    byte BoneCount,
    byte NotifyCount,
    float FrameRate,
    float Frequency,
    uint NamePointer);

public sealed class T4XAnimProofResult
{
    public required string SourceFastFile { get; init; }
    public required string OutputFile { get; init; }
    public required int DeclaredXAnimCount { get; init; }
    public required int ResolvedXAnimCount { get; init; }
    public required IReadOnlyList<T4XAnimProofAsset> Assets { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool Verified { get; init; }
}

/// <summary>
/// Stage 4 alpha 9j: typed T4 XAnimParts PC -> Xbox 360 serializer proof with run-bounded packed-name recovery.
///
/// This emits a proof container, NOT a game fastfile. Every XAnimParts fixed field,
/// script-string array, notify, delta transform/quaternion payload, scalar data array
/// and indices array is re-emitted in Xbox big-endian form using T4's serialized order.
/// The proof is then parsed back as big-endian and must land exactly on every record end.
///
/// T4 serialized order (OpenAssetTools ZoneCode reference):
/// name, names, notify, deltaPart, dataByte, dataShort, dataInt,
/// randomDataShort, randomDataByte, randomDataInt, indices.
/// </summary>
public static class T4XAnimSerializerProof
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("W360A4X\0");
    private const uint Following = 0xFFFFFFFFu;
    private const uint Insert = 0xFFFFFFFEu;
    // PC WaW serializes the desktop XAnimParts root as 0x58 bytes.
    // TU7 Xbox 360 was hardware-proven on 2026-10-25 to Load_Stream(..., 0x5C).
    // Keep the two layouts explicit: source parsing MUST remain PC/0x58 while
    // native emission expands the bone-count region and shifts all owned pointers.
    private const int PcFixedSize = 0x58;
    internal const int XboxFixedSize = 0x5C;
    private const uint XboxProofVersion = 5;

    public static async Task<T4XAnimProofResult> BuildProofAsync(
        string sourceFastFile,
        string outputFile,
        string? weaponOwnedProofFile = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFastFile))
            throw new FileNotFoundException("Fastfile not found.", sourceFastFile);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "xanim_a9e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "zone.bin");

        try
        {
            var cachedZone = await FastFileAnalysisCache.GetZoneAsync(sourceFastFile, cancellationToken);
            if (cachedZone.Platform != WaWPlatform.PC || cachedZone.ZoneHeader.Endianness != Endian.Little)
                throw new InvalidDataException("Alpha 9i XAnim proof expects a PC/little-endian WaW source fastfile.");

            var zone = cachedZone.Bytes;
            var document = cachedZone.Document;

            var declared = document.Assets.Count(a => a.TypeName.Equals("xanim", StringComparison.OrdinalIgnoreCase));
            var rawOffsets = FindXAnimOffsets(zone, Endian.Little);
            IReadOnlyList<int> offsets = rawOffsets;
            var warnings = new List<string>();

            if (declared == 0)
                throw new InvalidDataException("Source fastfile declares no XAnim assets.");

            if (rawOffsets.Count != declared)
            {
                var aligned = T4MixedAssetOrderProbe.SelectAlignedXAnims(
                    zone, document, weaponOwnedProofFile, cancellationToken);

                if (aligned.ExpectedXAnim != declared)
                    throw new InvalidDataException(
                        $"XAnim XAsset accounting mismatch: table declares {declared:N0}, mixed physical order expects {aligned.ExpectedXAnim:N0} inline/shared XAnim bodies.");

                if (aligned.MissingXAnim != 0 || aligned.MatchedXAnim != declared)
                {
                    var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputFile))!;
                    var stem = Path.GetFileNameWithoutExtension(outputFile);
                    var expandedReport = Path.Combine(outputDir, stem + ".alpha9i_expanded_anchor_forensics.txt");
                    T4ExpandedAnchorProbeResult? expanded = null;
                    try
                    {
                        expanded = T4ExpandedAssetAnchorProbe.AnalyzeAndWrite(
                            zone, document, "xanim", aligned.Offsets, null, weaponOwnedProofFile, expandedReport, cancellationToken);
                    }
                    catch (Exception expandedEx)
                    {
                        expandedReport = $"<expanded-anchor report failed: {expandedEx.Message}>";
                    }

                    var gapReport = Path.Combine(outputDir, stem + ".alpha9i_focused_microscope.txt");
                    try
                    {
                        var diagnosticWindows = expanded?.MicroscopeWindows ?? aligned.MissingWindows;
                        T4AssetGapMicroscope.WriteReport(
                            zone, document, sourceFastFile, "xanim", aligned.MissingXAnim, diagnosticWindows, gapReport, cancellationToken,
                            includeGlobalRejectedScan: false);
                    }
                    catch (Exception microscopeEx)
                    {
                        gapReport = $"<focused microscope report failed: {microscopeEx.Message}>";
                    }

                    throw new InvalidDataException(
                        $"XAnim aligned discovery incomplete: XAssetList declares {declared:N0}, inline-name typed scan found {rawOffsets.Count:N0}, " +
                        $"mixed XAsset-order alignment matched {aligned.MatchedXAnim:N0}, missing {aligned.MissingXAnim:N0}. " +
                        $"Packed-name bridge candidates={aligned.PackedNameCandidates:N0}, matchedPacked={aligned.MatchedPackedName:N0}. " +
                        $"Alpha 9j still fails closed after run-bounded packed-name recovery; expanded anchors remain diagnostic only. Expanded anchors: {expandedReport}. Focused microscope: {gapReport}");
                }

                offsets = aligned.Offsets;
                warnings.Add(
                    $"Alpha 9j run-bounded packed-name bridge selected {aligned.MatchedXAnim:N0}/{declared:N0} authoritative XAnim bodies; " +
                    $"{aligned.MatchedPackedName:N0} use packed/offset top-level name pointers ({aligned.PackedNameCandidates:N0} bounded candidate(s)). " +
                    $"Weapon anchor={(aligned.UsedWeaponProof ? "yes" : "no")}. Relocation is required before native FF emission.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
            var assets = new List<T4XAnimProofAsset>(offsets.Count);

            await using (var fs = File.Create(outputFile))
            {
                await fs.WriteAsync(Magic, cancellationToken);
                WriteU32(fs, XboxProofVersion);
                WriteU32(fs, checked((uint)offsets.Count));

                foreach (var sourceOffset in offsets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var packedTopLevelName = !IsInline(ReadU32(zone, sourceOffset, Endian.Little));
                    var measured = Measure(zone, sourceOffset, Endian.Little, requireInlinePayloads: true, requireTopLevelName: !packedTopLevelName);
                    using var assetStream = new MemoryStream(Math.Max(256, measured.EndOffset - sourceOffset + (XboxFixedSize - PcFixedSize)));
                    var converted = WriteXboxAsset(zone, sourceOffset, assetStream, allowPackedTopLevelName: packedTopLevelName);
                    var blob = assetStream.ToArray();

                    var expectedXboxLength = checked(measured.EndOffset - sourceOffset + (XboxFixedSize - PcFixedSize));
                    if (blob.Length != expectedXboxLength)
                        throw new InvalidDataException(
                            $"XAnim '{measured.Name}' Xbox layout length mismatch: PC={measured.EndOffset - sourceOffset:N0}, expected Xbox={expectedXboxLength:N0}, actual Xbox={blob.Length:N0}.");

                    var verify = MeasureXboxSerialized(blob, 0, requireInlinePayloads: true, requireTopLevelName: !packedTopLevelName);
                    if (verify.EndOffset != blob.Length || !verify.Name.Equals(measured.Name, StringComparison.Ordinal))
                        throw new InvalidDataException($"Big-endian Xbox 0x5C XAnim proof read-back failed for '{measured.Name}'.");

                    WriteU32(fs, checked((uint)sourceOffset));
                    WriteU32(fs, checked((uint)blob.Length));
                    await fs.WriteAsync(blob, cancellationToken);

                    assets.Add(new T4XAnimProofAsset(
                        measured.Name,
                        sourceOffset,
                        measured.EndOffset - sourceOffset,
                        blob.Length,
                        measured.HasDelta,
                        measured.NumFrames,
                        measured.BoneCount,
                        measured.NotifyCount));
                }
            }

            var verified = VerifyContainer(outputFile, assets);
            if (!verified)
                throw new InvalidDataException("Stage 4 alpha 9j XAnim proof container failed read-back verification.");

            return new T4XAnimProofResult
            {
                SourceFastFile = sourceFastFile,
                OutputFile = outputFile,
                DeclaredXAnimCount = declared,
                ResolvedXAnimCount = offsets.Count,
                Assets = assets,
                Warnings = warnings,
                Verified = true
            };
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private sealed record XAnimMeasure(
        string Name,
        int EndOffset,
        bool HasDelta,
        ushort NumFrames,
        byte BoneCount,
        byte NotifyCount);

    internal static T4XAnimDiagnosticCandidate MeasureDiagnosticCandidateAt(
        byte[] zone,
        int offset,
        Endian endian,
        bool allowPackedTopLevelName = false)
    {
        var measured = Measure(
            zone, offset, endian,
            requireInlinePayloads: true,
            requireTopLevelName: !allowPackedTopLevelName);
        return new T4XAnimDiagnosticCandidate(measured.Name, offset, measured.EndOffset);
    }

    public static IReadOnlyList<T4XAnimDiagnosticCandidate> DiscoverDiagnosticCandidates(byte[] zone, Endian endian)
    {
        var result = new List<T4XAnimDiagnosticCandidate>();
        foreach (var offset in FindXAnimOffsets(zone, endian))
        {
            try
            {
                var m = Measure(zone, offset, endian, requireInlinePayloads: true);
                result.Add(new T4XAnimDiagnosticCandidate(m.Name, offset, m.EndOffset));
            }
            catch (InvalidDataException) { }
        }
        return result;
    }


    internal static IReadOnlyList<T4RejectedInlineXAnimCandidate> DiscoverRejectedInlineNameCandidates(
        byte[] zone,
        Endian endian,
        ZoneHeaderSummary header,
        int rangeStart,
        int rangeEnd,
        CancellationToken cancellationToken = default)
    {
        var accepted = FindXAnimOffsets(zone, endian).ToHashSet();
        var result = new List<T4RejectedInlineXAnimCandidate>();
        var start = Math.Max(0, rangeStart);
        var end = Math.Min(zone.Length, rangeEnd);
        if (end - start < PcFixedSize + 5) return result;

        for (var offset = start; offset <= end - PcFixedSize - 5; offset++)
        {
            if ((offset & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            var namePtr = ReadU32(zone, offset, endian);
            if (!IsInline(namePtr)) continue;
            if (!TryReadAssetName(zone, offset + PcFixedSize, out var name)) continue;
            if (accepted.Contains(offset)) continue;

            var numFrames = ReadU16(zone, offset + 14, endian);
            var boneCount = zone[offset + 27];
            var notifyCount = zone[offset + 28];
            var framerate = ReadF32(zone, offset + 40, endian);
            var frequency = ReadF32(zone, offset + 44, endian);

            string reason;
            if (numFrames > 10000)
                reason = $"precheck: frame count {numFrames} > 10000";
            else if (zone[offset + 16] > 1)
                reason = $"precheck: byte+16={zone[offset + 16]} > 1";
            else if (zone[offset + 17] > 1)
                reason = $"precheck: byte+17={zone[offset + 17]} > 1";
            else if (zone[offset + 30] > 1)
                reason = $"precheck: byte+30={zone[offset + 30]} > 1";
            else if (!FiniteRange(framerate, 0f, 1000f))
                reason = $"precheck: framerate={framerate:G9} outside 0..1000";
            else if (!FiniteRange(frequency, 0f, 1000f))
                reason = $"precheck: frequency={frequency:G9} outside 0..1000";
            else
            {
                reason = string.Empty;
                for (var p = 48; p <= 84; p += 4)
                {
                    var value = ReadU32(zone, offset + p, endian);
                    if (!IsSerializedPointer(value))
                    {
                        reason = $"precheck: pointer +0x{p:X}=0x{value:X8} invalid";
                        break;
                    }
                    if (value != 0 && !IsInline(value) && !PointerWithinHeader(value, header))
                    {
                        reason = $"precheck: pointer +0x{p:X}=0x{value:X8} out-of-bounds";
                        break;
                    }
                }

                if (reason.Length == 0)
                {
                    try
                    {
                        var measured = Measure(zone, offset, endian, requireInlinePayloads: true);
                        reason = $"strict Measure succeeds to 0x{measured.EndOffset:X8}; excluded by an earlier scanner condition not reproduced";
                    }
                    catch (InvalidDataException ex)
                    {
                        reason = "measure: " + ex.Message;
                    }
                }
            }

            result.Add(new T4RejectedInlineXAnimCandidate(
                name, offset, reason, numFrames, boneCount, notifyCount, framerate, frequency, namePtr));
        }

        return result.OrderBy(x => x.Offset).ToList();
    }

    internal static IReadOnlyList<T4XAnimDiagnosticCandidate> DiscoverPackedNameCandidates(
        byte[] zone,
        Endian endian,
        ZoneHeaderSummary header,
        int rangeStart,
        int rangeEnd,
        CancellationToken cancellationToken = default)
    {
        var result = new List<T4XAnimDiagnosticCandidate>();
        var start = Math.Max(0, rangeStart);
        var end = Math.Min(zone.Length, rangeEnd);
        if (end - start < PcFixedSize) return result;

        for (var offset = start; offset <= end - PcFixedSize; offset++)
        {
            if ((offset & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            var namePtr = ReadU32(zone, offset, endian);
            if (namePtr == 0 || IsInline(namePtr) || !PointerWithinHeader(namePtr, header)) continue;

            var numFrames = ReadU16(zone, offset + 14, endian);
            if (numFrames > 10000) continue;
            if (zone[offset + 16] > 1 || zone[offset + 17] > 1 || zone[offset + 30] > 1) continue;

            var framerate = ReadF32(zone, offset + 40, endian);
            var frequency = ReadF32(zone, offset + 44, endian);
            if (!FiniteRange(framerate, 0f, 1000f) || !FiniteRange(frequency, 0f, 1000f)) continue;

            var ownedPointersOkay = true;
            for (var p = 48; p <= 84; p += 4)
            {
                var value = ReadU32(zone, offset + p, endian);
                if (value != 0 && !IsInline(value))
                {
                    ownedPointersOkay = false;
                    break;
                }
            }
            if (!ownedPointersOkay) continue;

            try
            {
                var measured = Measure(zone, offset, endian, requireInlinePayloads: true, requireTopLevelName: false);
                if (measured.EndOffset <= end)
                    result.Add(new T4XAnimDiagnosticCandidate(measured.Name, offset, measured.EndOffset));
            }
            catch (InvalidDataException)
            {
                // Not a complete packed-name top-level XAnim structure.
            }
        }

        return result;
    }

    private static bool PointerWithinHeader(uint value, ZoneHeaderSummary header)
    {
        var decoded = T4ZonePointer.Decode(value);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null) return false;
        var block = (int)decoded.Block.Value;
        return block >= 0 && block < header.BlockSizes.Count && decoded.Offset < header.BlockSizes[block];
    }

    private static IReadOnlyList<int> FindXAnimOffsets(byte[] zone, Endian endian)
    {
        var offsets = new List<int>();
        for (var offset = 0; offset <= zone.Length - PcFixedSize - 5; offset++)
        {
            if (!IsInline(ReadU32(zone, offset, endian))) continue;

            var numFrames = ReadU16(zone, offset + 14, endian);
            if (numFrames > 10000) continue;
            if (zone[offset + 16] > 1 || zone[offset + 17] > 1 || zone[offset + 30] > 1) continue;

            var framerate = ReadF32(zone, offset + 40, endian);
            var frequency = ReadF32(zone, offset + 44, endian);
            if (!FiniteRange(framerate, 0f, 1000f) || !FiniteRange(frequency, 0f, 1000f)) continue;

            var pointersOkay = true;
            for (var p = 48; p <= 84; p += 4)
            {
                if (!IsSerializedPointer(ReadU32(zone, offset + p, endian)))
                {
                    pointersOkay = false;
                    break;
                }
            }
            if (!pointersOkay) continue;

            if (!TryReadAssetName(zone, offset + PcFixedSize, out _)) continue;

            try
            {
                var measured = Measure(zone, offset, endian, requireInlinePayloads: true);
                if (measured.EndOffset <= zone.Length)
                    offsets.Add(offset);
            }
            catch (InvalidDataException)
            {
                // Not a complete typed XAnim structure.
            }
        }
        return offsets;
    }

    private static XAnimMeasure Measure(byte[] data, int start, Endian endian, bool requireInlinePayloads, bool requireTopLevelName = true)
    {
        Ensure(data, start, PcFixedSize, "XAnimParts fixed body");
        var namePtr = ReadU32(data, start, endian);
        var dataByteCount = ReadU16(data, start + 4, endian);
        var dataShortCount = ReadU16(data, start + 6, endian);
        var dataIntCount = ReadU16(data, start + 8, endian);
        var randomDataByteCount = ReadU16(data, start + 10, endian);
        var randomDataIntCount = ReadU16(data, start + 12, endian);
        var numFrames = ReadU16(data, start + 14, endian);
        var boneCount = data[start + 27]; // PART_TYPE_ALL / boneCount[9]
        var notifyCount = data[start + 28];
        var randomDataShortCount = ReadU32(data, start + 32, endian);
        var indexCount = ReadU32(data, start + 36, endian);

        var ptrNames = ReadU32(data, start + 48, endian);
        var ptrDataByte = ReadU32(data, start + 52, endian);
        var ptrDataShort = ReadU32(data, start + 56, endian);
        var ptrDataInt = ReadU32(data, start + 60, endian);
        var ptrRandomShort = ReadU32(data, start + 64, endian);
        var ptrRandomByte = ReadU32(data, start + 68, endian);
        var ptrRandomInt = ReadU32(data, start + 72, endian);
        var ptrIndices = ReadU32(data, start + 76, endian);
        var ptrNotify = ReadU32(data, start + 80, endian);
        var ptrDelta = ReadU32(data, start + 84, endian);

        if (requireTopLevelName && !IsInline(namePtr))
            throw new InvalidDataException($"XAnim at 0x{start:X} does not serialize its top-level name inline.");
        if (namePtr != 0 && !IsSerializedPointer(namePtr))
            throw new InvalidDataException($"XAnim at 0x{start:X} contains invalid name pointer 0x{namePtr:X8}.");

        var ownedPointers = new[] { ptrNames, ptrDataByte, ptrDataShort, ptrDataInt, ptrRandomShort, ptrRandomByte, ptrRandomInt, ptrIndices, ptrNotify, ptrDelta };
        if (requireInlinePayloads && ownedPointers.Any(p => p != 0 && !IsInline(p)))
            throw new InvalidDataException($"XAnim at 0x{start:X} contains a non-inline owned payload pointer.");

        var cursor = checked(start + PcFixedSize);
        string name;
        if (IsInline(namePtr))
        {
            if (!TryReadAssetName(data, cursor, out name))
                throw new InvalidDataException($"XAnim at 0x{start:X}: invalid inline name.");
            cursor = checked(cursor + Encoding.ASCII.GetByteCount(name) + 1);
        }
        else if (namePtr != 0)
        {
            name = $"<packed@0x{namePtr:X8}>";
        }
        else
        {
            name = "<null>";
        }

        // Top-level reorder: name, names, notify, deltaPart, bulk arrays, indices.
        if (ptrNames != 0) cursor = Advance(data, cursor, checked((long)boneCount * 2), "XAnim.names");
        if (ptrNotify != 0) cursor = Advance(data, cursor, checked((long)notifyCount * 8), "XAnim.notify");

        if (ptrDelta != 0)
        {
            Ensure(data, cursor, 8, "XAnim.deltaPart");
            var transPtr = ReadU32(data, cursor, endian);
            var quatPtr = ReadU32(data, cursor + 4, endian);
            if (requireInlinePayloads && ((transPtr != 0 && !IsInline(transPtr)) || (quatPtr != 0 && !IsInline(quatPtr))))
                throw new InvalidDataException($"XAnim '{name}': deltaPart contains a non-inline pointer.");
            cursor += 8;

            if (transPtr != 0)
                cursor = MeasureTrans(data, cursor, endian, numFrames, requireInlinePayloads, name);
            if (quatPtr != 0)
                cursor = MeasureQuat(data, cursor, endian, numFrames, requireInlinePayloads, name);
        }

        if (ptrDataByte != 0) cursor = Advance(data, cursor, dataByteCount, "XAnim.dataByte");
        if (ptrDataShort != 0) cursor = Advance(data, cursor, checked((long)dataShortCount * 2), "XAnim.dataShort");
        if (ptrDataInt != 0) cursor = Advance(data, cursor, checked((long)dataIntCount * 4), "XAnim.dataInt");
        if (ptrRandomShort != 0) cursor = Advance(data, cursor, checked((long)randomDataShortCount * 2), "XAnim.randomDataShort");
        if (ptrRandomByte != 0) cursor = Advance(data, cursor, randomDataByteCount, "XAnim.randomDataByte");
        if (ptrRandomInt != 0) cursor = Advance(data, cursor, checked((long)randomDataIntCount * 4), "XAnim.randomDataInt");
        if (ptrIndices != 0)
        {
            var width = numFrames < 256 ? 1 : 2;
            cursor = Advance(data, cursor, checked((long)indexCount * width), "XAnim.indices");
        }

        return new XAnimMeasure(name, cursor, ptrDelta != 0, numFrames, boneCount, notifyCount);
    }

    private static int MeasureTrans(byte[] data, int cursor, Endian endian, ushort numFrames, bool requireInlinePayloads, string name)
    {
        Ensure(data, cursor, 4, "XAnimPartTrans header");
        var size = ReadU16(data, cursor, endian);
        var smallTrans = data[cursor + 2] != 0;
        if (size == 0)
            return Advance(data, cursor, 16, "XAnimPartTrans.frame0");

        Ensure(data, cursor, 32, "XAnimPartTrans.frames prefix");
        var framesPtr = ReadU32(data, cursor + 28, endian);
        if (requireInlinePayloads && framesPtr != 0 && !IsInline(framesPtr))
            throw new InvalidDataException($"XAnim '{name}': trans frames pointer is non-inline.");
        cursor += 32;
        var count = checked((int)size + 1);
        cursor = Advance(data, cursor, checked((long)count * (numFrames < 256 ? 1 : 2)), "XAnimPartTrans.indices");
        if (framesPtr != 0)
            cursor = Advance(data, cursor, checked((long)count * (smallTrans ? 3 : 6)), "XAnimPartTrans.frames");
        return cursor;
    }

    private static int MeasureQuat(byte[] data, int cursor, Endian endian, ushort numFrames, bool requireInlinePayloads, string name)
    {
        Ensure(data, cursor, 8, "XAnimDeltaPartQuat");
        var size = ReadU16(data, cursor, endian);
        if (size == 0)
            return cursor + 8;

        var framesPtr = ReadU32(data, cursor + 4, endian);
        if (requireInlinePayloads && framesPtr != 0 && !IsInline(framesPtr))
            throw new InvalidDataException($"XAnim '{name}': quat frames pointer is non-inline.");
        cursor += 8;
        var count = checked((int)size + 1);
        cursor = Advance(data, cursor, checked((long)count * (numFrames < 256 ? 1 : 2)), "XAnimDeltaPartQuat.indices");
        if (framesPtr != 0)
            cursor = Advance(data, cursor, checked((long)count * 4), "XAnimDeltaPartQuat.frames");
        return cursor;
    }


    /// <summary>
    /// Parses the native TU7 Xbox 360 XAnimParts serialization proven on hardware.
    ///
    /// PC root (0x58):
    ///   +0x12 boneCount[10], total at +0x1B, notify +0x1C, pointers +0x30..+0x54.
    ///
    /// Xbox root (0x5C):
    ///   +0x12 boneCount[12], total at +0x1D, notify +0x1E, pointers +0x34..+0x58.
    ///
    /// PC has no values for the two Xbox-only part buckets, so conversion emits
    /// zero for Xbox boneCount[9] and [10], then moves PC PART_TYPE_ALL to [11].
    /// </summary>
    private static XAnimMeasure MeasureXboxSerialized(
        byte[] data,
        int start,
        bool requireInlinePayloads,
        bool requireTopLevelName = true)
    {
        Ensure(data, start, XboxFixedSize, "Xbox XAnimParts fixed body");

        var namePtr = ReadU32(data, start, Endian.Big);
        var dataByteCount = ReadU16(data, start + 0x04, Endian.Big);
        var dataShortCount = ReadU16(data, start + 0x06, Endian.Big);
        var dataIntCount = ReadU16(data, start + 0x08, Endian.Big);
        var randomDataByteCount = ReadU16(data, start + 0x0A, Endian.Big);
        var randomDataIntCount = ReadU16(data, start + 0x0C, Endian.Big);
        var numFrames = ReadU16(data, start + 0x0E, Endian.Big);
        var boneCount = data[start + 0x1D];
        var notifyCount = data[start + 0x1E];
        var randomDataShortCount = ReadU32(data, start + 0x24, Endian.Big);
        var indexCount = ReadU32(data, start + 0x28, Endian.Big);

        // These two slots do not exist in the PC T4 layout. They are deliberately
        // zero until a source platform exposes semantic values for them.
        if (data[start + 0x1B] != 0 || data[start + 0x1C] != 0)
            throw new InvalidDataException($"Xbox XAnim at 0x{start:X} has non-zero PC-unmapped boneCount slots.");

        var ptrNames = ReadU32(data, start + 0x34, Endian.Big);
        var ptrDataByte = ReadU32(data, start + 0x38, Endian.Big);
        var ptrDataShort = ReadU32(data, start + 0x3C, Endian.Big);
        var ptrDataInt = ReadU32(data, start + 0x40, Endian.Big);
        var ptrRandomShort = ReadU32(data, start + 0x44, Endian.Big);
        var ptrRandomByte = ReadU32(data, start + 0x48, Endian.Big);
        var ptrRandomInt = ReadU32(data, start + 0x4C, Endian.Big);
        var ptrIndices = ReadU32(data, start + 0x50, Endian.Big);
        var ptrNotify = ReadU32(data, start + 0x54, Endian.Big);
        var ptrDelta = ReadU32(data, start + 0x58, Endian.Big);

        if (requireTopLevelName && !IsInline(namePtr))
            throw new InvalidDataException($"Xbox XAnim at 0x{start:X} does not serialize its top-level name inline.");
        if (namePtr != 0 && !IsSerializedPointer(namePtr))
            throw new InvalidDataException($"Xbox XAnim at 0x{start:X} contains invalid name pointer 0x{namePtr:X8}.");

        var ownedPointers = new[] { ptrNames, ptrDataByte, ptrDataShort, ptrDataInt, ptrRandomShort, ptrRandomByte, ptrRandomInt, ptrIndices, ptrNotify, ptrDelta };
        if (requireInlinePayloads && ownedPointers.Any(p => p != 0 && !IsInline(p)))
            throw new InvalidDataException($"Xbox XAnim at 0x{start:X} contains a non-inline owned payload pointer.");

        var cursor = checked(start + XboxFixedSize);
        string name;
        if (IsInline(namePtr))
        {
            if (!TryReadAssetName(data, cursor, out name))
                throw new InvalidDataException($"Xbox XAnim at 0x{start:X}: invalid inline name.");
            cursor = checked(cursor + Encoding.ASCII.GetByteCount(name) + 1);
        }
        else if (namePtr != 0)
        {
            name = $"<packed@0x{namePtr:X8}>";
        }
        else
        {
            name = "<null>";
        }

        if (ptrNames != 0) cursor = Advance(data, cursor, checked((long)boneCount * 2), "Xbox XAnim.names");
        if (ptrNotify != 0) cursor = Advance(data, cursor, checked((long)notifyCount * 8), "Xbox XAnim.notify");

        if (ptrDelta != 0)
        {
            Ensure(data, cursor, 8, "Xbox XAnim.deltaPart");
            var transPtr = ReadU32(data, cursor, Endian.Big);
            var quatPtr = ReadU32(data, cursor + 4, Endian.Big);
            if (requireInlinePayloads && ((transPtr != 0 && !IsInline(transPtr)) || (quatPtr != 0 && !IsInline(quatPtr))))
                throw new InvalidDataException($"Xbox XAnim '{name}': deltaPart contains a non-inline pointer.");
            cursor += 8;

            if (transPtr != 0)
                cursor = MeasureTrans(data, cursor, Endian.Big, numFrames, requireInlinePayloads, name);
            if (quatPtr != 0)
                cursor = MeasureQuat(data, cursor, Endian.Big, numFrames, requireInlinePayloads, name);
        }

        if (ptrDataByte != 0) cursor = Advance(data, cursor, dataByteCount, "Xbox XAnim.dataByte");
        if (ptrDataShort != 0) cursor = Advance(data, cursor, checked((long)dataShortCount * 2), "Xbox XAnim.dataShort");
        if (ptrDataInt != 0) cursor = Advance(data, cursor, checked((long)dataIntCount * 4), "Xbox XAnim.dataInt");
        if (ptrRandomShort != 0) cursor = Advance(data, cursor, checked((long)randomDataShortCount * 2), "Xbox XAnim.randomDataShort");
        if (ptrRandomByte != 0) cursor = Advance(data, cursor, randomDataByteCount, "Xbox XAnim.randomDataByte");
        if (ptrRandomInt != 0) cursor = Advance(data, cursor, checked((long)randomDataIntCount * 4), "Xbox XAnim.randomDataInt");
        if (ptrIndices != 0)
        {
            var width = numFrames < 256 ? 1 : 2;
            cursor = Advance(data, cursor, checked((long)indexCount * width), "Xbox XAnim.indices");
        }

        return new XAnimMeasure(name, cursor, ptrDelta != 0, numFrames, boneCount, notifyCount);
    }

    internal static void ValidateXboxNativeBlob(byte[] blob, string? expectedName = null)
    {
        if (blob.Length < XboxFixedSize)
            throw new InvalidDataException($"Xbox XAnim blob is only {blob.Length:N0} bytes; fixed root requires 0x{XboxFixedSize:X}.");

        var packedTopLevelName = !IsInline(ReadU32(blob, 0, Endian.Big));
        var measured = MeasureXboxSerialized(blob, 0, requireInlinePayloads: true, requireTopLevelName: !packedTopLevelName);
        if (measured.EndOffset != blob.Length)
            throw new InvalidDataException($"Xbox XAnim '{measured.Name}' ends at 0x{measured.EndOffset:X}, blob length is 0x{blob.Length:X}.");

        if (!packedTopLevelName && !string.IsNullOrWhiteSpace(expectedName) &&
            !measured.Name.Equals(expectedName, StringComparison.Ordinal))
            throw new InvalidDataException($"Xbox XAnim name mismatch: expected '{expectedName}', serialized '{measured.Name}'.");
    }

    private static XAnimMeasure WriteXboxAsset(byte[] src, int start, Stream dst, bool allowPackedTopLevelName = false)
    {
        var measured = Measure(src, start, Endian.Little, requireInlinePayloads: true, requireTopLevelName: !allowPackedTopLevelName);
        var cursor = start;

        // Native TU7 Xbox XAnimParts root (0x5C).
        // Hardware proof:
        //   Load_Stream(varXAnimParts, 0x5C)
        //   names* at +0x34 uses boneCount total byte +0x1D
        //   notify* at +0x54 uses notifyCount byte +0x1E
        //   deltaPart* lives at +0x58.
        WriteU32(dst, ReadU32(src, cursor, Endian.Little)); // name
        for (var p = 4; p <= 14; p += 2) WriteU16(dst, ReadU16(src, cursor + p, Endian.Little));

        dst.Write(src.AsSpan(cursor + 0x10, 2)); // bLoop, bDelta
        dst.Write(src.AsSpan(cursor + 0x12, 9)); // PC part buckets 0..8
        dst.WriteByte(0);                        // Xbox-only part bucket 9
        dst.WriteByte(0);                        // Xbox-only part bucket 10
        dst.WriteByte(src[cursor + 0x1B]);       // PC PART_TYPE_ALL -> Xbox boneCount[11]
        dst.WriteByte(src[cursor + 0x1C]);       // notifyCount
        dst.WriteByte(src[cursor + 0x1D]);       // assetType
        dst.WriteByte(src[cursor + 0x1E]);       // isDefault
        dst.WriteByte(0);                        // Xbox alignment padding
        dst.WriteByte(0);
        dst.WriteByte(0);

        WriteU32(dst, ReadU32(src, cursor + 0x20, Endian.Little)); // randomDataShortCount -> +0x24
        WriteU32(dst, ReadU32(src, cursor + 0x24, Endian.Little)); // indexCount           -> +0x28
        WriteU32(dst, ReadU32(src, cursor + 0x28, Endian.Little)); // framerate            -> +0x2C
        WriteU32(dst, ReadU32(src, cursor + 0x2C, Endian.Little)); // frequency            -> +0x30
        for (var p = 0x30; p <= 0x54; p += 4)
            WriteU32(dst, ReadU32(src, cursor + p, Endian.Little)); // pointers shift +4

        var numFrames = ReadU16(src, cursor + 14, Endian.Little);
        var boneCount = src[cursor + 27];
        var notifyCount = src[cursor + 28];
        var dataByteCount = ReadU16(src, cursor + 4, Endian.Little);
        var dataShortCount = ReadU16(src, cursor + 6, Endian.Little);
        var dataIntCount = ReadU16(src, cursor + 8, Endian.Little);
        var randomDataByteCount = ReadU16(src, cursor + 10, Endian.Little);
        var randomDataIntCount = ReadU16(src, cursor + 12, Endian.Little);
        var randomDataShortCount = ReadU32(src, cursor + 32, Endian.Little);
        var indexCount = ReadU32(src, cursor + 36, Endian.Little);
        var ptrNames = ReadU32(src, cursor + 48, Endian.Little);
        var ptrDataByte = ReadU32(src, cursor + 52, Endian.Little);
        var ptrDataShort = ReadU32(src, cursor + 56, Endian.Little);
        var ptrDataInt = ReadU32(src, cursor + 60, Endian.Little);
        var ptrRandomShort = ReadU32(src, cursor + 64, Endian.Little);
        var ptrRandomByte = ReadU32(src, cursor + 68, Endian.Little);
        var ptrRandomInt = ReadU32(src, cursor + 72, Endian.Little);
        var ptrIndices = ReadU32(src, cursor + 76, Endian.Little);
        var ptrNotify = ReadU32(src, cursor + 80, Endian.Little);
        var ptrDelta = ReadU32(src, cursor + 84, Endian.Little);
        cursor += PcFixedSize;

        if (IsInline(ReadU32(src, start, Endian.Little)))
        {
            var nameBytes = Encoding.ASCII.GetBytes(measured.Name + "\0");
            dst.Write(nameBytes);
            cursor += nameBytes.Length;
        }

        if (ptrNames != 0)
        {
            for (var i = 0; i < boneCount; i++)
            {
                WriteU16(dst, ReadU16(src, cursor, Endian.Little));
                cursor += 2;
            }
        }

        if (ptrNotify != 0)
        {
            for (var i = 0; i < notifyCount; i++)
            {
                WriteU16(dst, ReadU16(src, cursor, Endian.Little));
                dst.Write(src.AsSpan(cursor + 2, 2)); // native padding
                WriteU32(dst, ReadU32(src, cursor + 4, Endian.Little)); // float bits
                cursor += 8;
            }
        }

        if (ptrDelta != 0)
        {
            var transPtr = ReadU32(src, cursor, Endian.Little);
            var quatPtr = ReadU32(src, cursor + 4, Endian.Little);
            WriteU32(dst, transPtr);
            WriteU32(dst, quatPtr);
            cursor += 8;

            if (transPtr != 0)
                cursor = WriteTrans(src, cursor, dst, numFrames);
            if (quatPtr != 0)
                cursor = WriteQuat(src, cursor, dst, numFrames);
        }

        if (ptrDataByte != 0) CopyRaw(src, ref cursor, dst, dataByteCount);
        if (ptrDataShort != 0) CopyU16Array(src, ref cursor, dst, dataShortCount);
        if (ptrDataInt != 0) CopyU32Array(src, ref cursor, dst, dataIntCount);
        if (ptrRandomShort != 0) CopyU16Array(src, ref cursor, dst, checked((int)randomDataShortCount));
        if (ptrRandomByte != 0) CopyRaw(src, ref cursor, dst, randomDataByteCount);
        if (ptrRandomInt != 0) CopyU32Array(src, ref cursor, dst, randomDataIntCount);
        if (ptrIndices != 0)
        {
            if (numFrames < 256) CopyRaw(src, ref cursor, dst, checked((int)indexCount));
            else CopyU16Array(src, ref cursor, dst, checked((int)indexCount));
        }

        if (cursor != measured.EndOffset)
            throw new InvalidDataException($"XAnim '{measured.Name}': writer cursor 0x{cursor:X} did not match measured end 0x{measured.EndOffset:X}.");
        return measured;
    }

    private static int WriteTrans(byte[] src, int cursor, Stream dst, ushort numFrames)
    {
        var size = ReadU16(src, cursor, Endian.Little);
        var smallTrans = src[cursor + 2] != 0;
        WriteU16(dst, size);
        dst.WriteByte(src[cursor + 2]);
        dst.WriteByte(src[cursor + 3]);

        if (size == 0)
        {
            for (var p = 4; p < 16; p += 4) WriteU32(dst, ReadU32(src, cursor + p, Endian.Little));
            return cursor + 16;
        }

        for (var p = 4; p < 28; p += 4) WriteU32(dst, ReadU32(src, cursor + p, Endian.Little));
        var framesPtr = ReadU32(src, cursor + 28, Endian.Little);
        WriteU32(dst, framesPtr);
        cursor += 32;

        var count = checked((int)size + 1);
        if (numFrames < 256) CopyRaw(src, ref cursor, dst, count);
        else CopyU16Array(src, ref cursor, dst, count);

        if (framesPtr != 0)
        {
            if (smallTrans)
            {
                CopyRaw(src, ref cursor, dst, checked(count * 3));
            }
            else
            {
                CopyU16Array(src, ref cursor, dst, checked(count * 3));
            }
        }
        return cursor;
    }

    private static int WriteQuat(byte[] src, int cursor, Stream dst, ushort numFrames)
    {
        var size = ReadU16(src, cursor, Endian.Little);
        WriteU16(dst, size);
        dst.Write(src.AsSpan(cursor + 2, 2));

        if (size == 0)
        {
            WriteU16(dst, ReadU16(src, cursor + 4, Endian.Little));
            WriteU16(dst, ReadU16(src, cursor + 6, Endian.Little));
            return cursor + 8;
        }

        var framesPtr = ReadU32(src, cursor + 4, Endian.Little);
        WriteU32(dst, framesPtr);
        cursor += 8;
        var count = checked((int)size + 1);
        if (numFrames < 256) CopyRaw(src, ref cursor, dst, count);
        else CopyU16Array(src, ref cursor, dst, count);

        if (framesPtr != 0)
            CopyU16Array(src, ref cursor, dst, checked(count * 2));
        return cursor;
    }

    private static bool VerifyContainer(string path, IReadOnlyList<T4XAnimProofAsset> expected)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 16 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != XboxProofVersion) return false;
        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12, 4));
        if (count != (uint)expected.Count) return false;

        var cursor = 16;
        for (var i = 0; i < expected.Count; i++)
        {
            if (cursor + 8 > bytes.Length) return false;
            _ = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor, 4));
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor + 4, 4)));
            cursor += 8;
            if (length <= 0 || cursor + length > bytes.Length) return false;
            var blob = bytes.AsSpan(cursor, length).ToArray();
            var packedTopLevelName = !IsInline(ReadU32(blob, 0, Endian.Big));
            var measured = MeasureXboxSerialized(blob, 0, requireInlinePayloads: true, requireTopLevelName: !packedTopLevelName);
            if (measured.EndOffset != length || !measured.Name.Equals(expected[i].Name, StringComparison.Ordinal)) return false;
            cursor += length;
        }
        return cursor == bytes.Length;
    }

    private static void CopyRaw(byte[] src, ref int cursor, Stream dst, int count)
    {
        Ensure(src, cursor, count, "raw XAnim array");
        dst.Write(src.AsSpan(cursor, count));
        cursor += count;
    }

    private static void CopyU16Array(byte[] src, ref int cursor, Stream dst, int count)
    {
        Ensure(src, cursor, checked(count * 2), "u16 XAnim array");
        for (var i = 0; i < count; i++)
        {
            WriteU16(dst, ReadU16(src, cursor, Endian.Little));
            cursor += 2;
        }
    }

    private static void CopyU32Array(byte[] src, ref int cursor, Stream dst, int count)
    {
        Ensure(src, cursor, checked(count * 4), "u32 XAnim array");
        for (var i = 0; i < count; i++)
        {
            WriteU32(dst, ReadU32(src, cursor, Endian.Little));
            cursor += 4;
        }
    }

    private static int Advance(byte[] data, int cursor, long count, string label)
    {
        if (count < 0 || count > int.MaxValue) throw new InvalidDataException($"{label}: invalid byte count {count}.");
        Ensure(data, cursor, (int)count, label);
        return checked(cursor + (int)count);
    }

    private static void Ensure(byte[] data, int offset, int count, string label)
    {
        if (offset < 0 || count < 0 || (long)offset + count > data.Length)
            throw new InvalidDataException($"{label} exceeds serialized zone bounds at 0x{offset:X} (+0x{count:X}).");
    }

    private static bool TryReadAssetName(byte[] data, int offset, out string name)
    {
        name = string.Empty;
        if (offset < 0 || offset >= data.Length) return false;
        var end = offset;
        var max = Math.Min(data.Length, offset + 256);
        while (end < max && data[end] != 0)
        {
            var b = data[end];
            if (b < 0x20 || b > 0x7E) return false;
            end++;
        }
        if (end >= max || end == offset || data[end] != 0 || end - offset < 4) return false;
        name = Encoding.ASCII.GetString(data, offset, end - offset);
        if (name.Any(char.IsWhiteSpace) || !name.Any(char.IsLetter)) return false;
        return true;
    }

    private static bool IsInline(uint value) => value is Following or Insert;
    private static bool IsSerializedPointer(uint value)
    {
        if (value == 0 || IsInline(value)) return true;
        var decoded = T4ZonePointer.Decode(value);
        return decoded.Kind == T4ZonePointerKind.Offset && decoded.Block is not null;
    }
    private static bool FiniteRange(float value, float min, float max) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;

    private static ushort ReadU16(byte[] data, int offset, Endian endian)
    {
        var span = data.AsSpan(offset, 2);
        return endian == Endian.Big ? BinaryPrimitives.ReadUInt16BigEndian(span) : BinaryPrimitives.ReadUInt16LittleEndian(span);
    }

    private static uint ReadU32(byte[] data, int offset, Endian endian)
    {
        var span = data.AsSpan(offset, 4);
        return endian == Endian.Big ? BinaryPrimitives.ReadUInt32BigEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    private static float ReadF32(byte[] data, int offset, Endian endian) =>
        BitConverter.Int32BitsToSingle(unchecked((int)ReadU32(data, offset, endian)));

    private static void WriteU16(Stream stream, ushort value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, value);
        stream.Write(b);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }
}
