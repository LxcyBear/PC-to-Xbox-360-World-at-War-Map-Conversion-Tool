using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4XModelProofAsset(
    string Name,
    long SourceOffset,
    int SourceSerializedBytes,
    int XboxSerializedBytes,
    byte BoneCount,
    byte SurfaceCount,
    int CollisionSurfaceCount,
    int InlineMaterials,
    int InlineImages,
    bool HasPhysPreset,
    bool HasPhysGeoms,
    bool HasCollmap);

public sealed record T4XModelDiagnosticCandidate(string Name, int Offset, int End);
public sealed record T4RejectedInlineXModelCandidate(
    string Name,
    int Offset,
    string Reason,
    byte BoneCount,
    byte RootBoneCount,
    byte SurfaceCount,
    byte LodRamp,
    ushort LodCount,
    short CollisionLod,
    uint NamePointer);

public sealed class T4XModelProofResult
{
    public required string SourceFastFile { get; init; }
    public required string OutputFile { get; init; }
    public required int DeclaredXModelCount { get; init; }
    public required int ResolvedXModelCount { get; init; }
    public required IReadOnlyList<T4XModelProofAsset> Assets { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool Verified { get; init; }
}

/// <summary>
/// Stage 4 alpha 9j: bounded-recovery, packed-name-aware, XAsset-order-filtered T4 XModel PC -> Xbox 360 serializer proof.
///
/// This emits an internal proof container, NOT a game fastfile. It walks the
/// serialized ownership graph for each top-level XModel and endian-converts the
/// complete owned payload without changing its physical byte length:
/// skeleton arrays, XSurface geometry, rigid collision trees, material/image
/// dependencies, collision surfaces, bone info and supported physics data.
///
/// Unsupported inline ownership forms fail closed. In particular this proof does
/// not guess inline MaterialTechniqueSet, water_t, or PhysConstraints layouts.
/// Packed/referenced versions of those dependencies remain valid references.
/// </summary>
public static class T4XModelSerializerProof
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("W360A5M\0");

    private const uint Following = 0xFFFFFFFFu;
    private const uint Insert = 0xFFFFFFFEu;

    private const int XModelFixedSize = 228;
    private const int XSurfaceFixedSize = 64;
    private const int XModelCollSurfSize = 44;
    private const int XBoneInfoSize = 40;
    private const int MaterialFixedSize = 112;
    private const int XboxMaterialFixedSize = 96;
    private const int XboxTechniqueSetFixedSize = 216;
    private const int XboxTechniqueCount = 51;
    private const int MaterialTextureDefSize = 16;
    private const int MaterialConstantDefSize = 32;
    private const int GfxStateBitsSize = 8;
    private const int GfxImageFixedSize = 36;
    private const int GfxImageLoadDefSize = 16;
    private const int PhysPresetSize = 48;
    private const int PhysGeomListSize = 20;
    private const int PhysGeomInfoSize = 68;
    private const int BrushWrapperSize = 96;
    private const int BrushSideSize = 12;
    private const int CPlaneSize = 20;

    private sealed class WalkStats
    {
        public int InlineMaterials;
        public int InlineImages;
        public bool HasPhysPreset;
        public bool HasPhysGeoms;
        public bool HasCollmap;
        public int SmokeTechniqueSets;
    }

    private sealed record XModelMeasure(
        string Name,
        int EndOffset,
        byte BoneCount,
        byte SurfaceCount,
        int CollisionSurfaceCount,
        WalkStats Stats);

    private sealed record PlannedInsertion(int Offset, byte[] Bytes);

    private sealed class WalkContext
    {
        public required byte[] Source { get; init; }
        public required Endian Endianness { get; init; }
        public byte[]? Destination { get; init; }
        public int RootStart { get; init; }
        public ZoneHeaderSummary? Header { get; init; }
        public required WalkStats Stats { get; init; }
        public bool RequireSelfContainedPointers { get; init; }
        public bool PatchOnly { get; init; }
        public bool PlanSmokeTechniqueSets { get; init; }
        public List<PlannedInsertion>? PlannedInsertions { get; init; }
    }

    public static async Task<T4XModelProofResult> BuildProofAsync(
        string sourceFastFile,
        string outputFile,
        string? xanimProofFile = null,
        string? weaponOwnedProofFile = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFastFile))
            throw new FileNotFoundException("Fastfile not found.", sourceFastFile);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "xmodel_a5", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "zone.bin");

        try
        {
            var cachedZone = await FastFileAnalysisCache.GetZoneAsync(sourceFastFile, cancellationToken);
            if (cachedZone.Platform != WaWPlatform.PC || cachedZone.ZoneHeader.Endianness != Endian.Little)
                throw new InvalidDataException("Alpha 9i XModel proof expects a PC/little-endian WaW source fastfile.");

            var zone = cachedZone.Bytes;
            var document = cachedZone.Document;

            var declared = document.Assets.Count(a => a.TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase));
            if (declared == 0)
                throw new InvalidDataException("Source fastfile declares no XModel assets.");

            var rawOffsets = FindTopLevelXModelOffsets(zone, Endian.Little, header: document.Header);
            IReadOnlyList<int> offsets = rawOffsets;
            var warnings = new List<string>();

            if (rawOffsets.Count != declared)
            {
                var aligned = T4MixedAssetOrderProbe.SelectAlignedXModels(
                    zone, document, xanimProofFile, weaponOwnedProofFile, cancellationToken);

                if (aligned.ExpectedXModel != declared)
                    throw new InvalidDataException(
                        $"XModel XAsset accounting mismatch: table declares {declared:N0}, mixed physical order expects {aligned.ExpectedXModel:N0} inline/shared XModel bodies.");

                if (aligned.MissingXModel != 0 || aligned.MatchedXModel != declared)
                {
                    var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputFile))!;
                    var stem = Path.GetFileNameWithoutExtension(outputFile);
                    var expandedReport = Path.Combine(outputDir, stem + ".alpha9i_expanded_anchor_forensics.txt");
                    T4ExpandedAnchorProbeResult? expanded = null;
                    try
                    {
                        expanded = T4ExpandedAssetAnchorProbe.AnalyzeAndWrite(
                            zone, document, "xmodel", aligned.Offsets, xanimProofFile, weaponOwnedProofFile, expandedReport, cancellationToken);
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
                            zone, document, sourceFastFile, "xmodel", aligned.MissingXModel, diagnosticWindows, gapReport, cancellationToken,
                            includeGlobalRejectedScan: false);
                    }
                    catch (Exception microscopeEx)
                    {
                        gapReport = $"<focused microscope report failed: {microscopeEx.Message}>";
                    }

                    throw new InvalidDataException(
                        $"XModel aligned discovery incomplete: XAssetList declares {declared:N0}, raw typed scan found {rawOffsets.Count:N0}, " +
                        $"mixed XAsset-order alignment matched {aligned.MatchedXModel:N0}, missing {aligned.MissingXModel:N0}, skipped {aligned.SkippedXModel:N0} false-positive candidate(s). " +
                        $"Packed-name bridge candidates={aligned.PackedNameCandidates:N0}, matchedPacked={aligned.MatchedPackedName:N0}; finite-radius recovery candidates={aligned.RadiusTolerantCandidates:N0}, matchedRadius={aligned.MatchedRadiusTolerant:N0}. " +
                        $"Alpha 9j still fails closed after bounded structural recovery; expanded anchors remain diagnostic only. Expanded anchors: {expandedReport}. Focused microscope: {gapReport}");
                }

                offsets = aligned.Offsets;
                warnings.Add(
                    $"Alpha 9j XAsset-order filter selected {aligned.MatchedXModel:N0}/{declared:N0} authoritative XModel bodies from {aligned.ObservedXModel:N0} structural candidate(s) " +
                    $"({rawOffsets.Count:N0} renderable raw-scan hits); skipped {aligned.SkippedXModel:N0} false-positive candidate(s). " +
                    $"Packed-name bridge: candidates={aligned.PackedNameCandidates:N0}, matched={aligned.MatchedPackedName:N0}. Finite-radius bounded recovery: candidates={aligned.RadiusTolerantCandidates:N0}, matched={aligned.MatchedRadiusTolerant:N0}. " +
                    $"Anchors: XAnim proof={(aligned.UsedXAnimProof ? "yes" : "typed scan")}, Weapon proof={(aligned.UsedWeaponProof ? "yes" : "no")}. " +
                    "Packed top-level names remain relocation obligations for final native FF emission.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
            var assets = new List<T4XModelProofAsset>(offsets.Count);

            await using (var fs = File.Create(outputFile))
            {
                await fs.WriteAsync(Magic, cancellationToken);
                WriteU32(fs, 5);
                WriteU32(fs, checked((uint)offsets.Count));

                foreach (var sourceOffset in offsets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var isReferenceStub = zone[sourceOffset + 4] == 0 && zone[sourceOffset + 6] == 0;
                    var packedTopLevelName = !IsInline(ReadU32(zone, sourceOffset, Endian.Little));
                    XModelMeasure measured;
                    try
                    {
                        measured = Measure(
                            zone, sourceOffset, Endian.Little,
                            requireRenderableBody: !isReferenceStub,
                            requireTopLevelName: !packedTopLevelName,
                            header: document.Header);
                    }
                    catch (InvalidDataException ex)
                    {
                        throw new InvalidDataException($"XModel at 0x{sourceOffset:X8} could not be fully measured: {ex.Message}", ex);
                    }

                    var sourceLength = checked(measured.EndOffset - sourceOffset);
                    var blob = TransformToXbox(
                        zone, sourceOffset, measured.EndOffset,
                        requireRenderableBody: !isReferenceStub,
                        requireTopLevelName: !packedTopLevelName);
                    if (blob.Length != sourceLength)
                        throw new InvalidDataException($"XModel '{measured.Name}' changed serialized byte length during endian conversion ({sourceLength:N0} -> {blob.Length:N0}).");

                    XModelMeasure verify;
                    try
                    {
                        verify = Measure(
                            blob, 0, Endian.Big,
                            requireRenderableBody: !isReferenceStub,
                            requireTopLevelName: !packedTopLevelName);
                    }
                    catch (InvalidDataException ex)
                    {
                        throw new InvalidDataException($"Big-endian XModel proof read-back failed for '{measured.Name}': {ex.Message}", ex);
                    }

                    if (verify.EndOffset != blob.Length || !verify.Name.Equals(measured.Name, StringComparison.Ordinal))
                        throw new InvalidDataException(
                            $"Big-endian XModel proof read-back boundary/name mismatch for '{measured.Name}': " +
                            $"end=0x{verify.EndOffset:X}, expected=0x{blob.Length:X}, name='{verify.Name}'.");

                    WriteU32(fs, checked((uint)sourceOffset));
                    WriteU32(fs, checked((uint)blob.Length));
                    await fs.WriteAsync(blob, cancellationToken);

                    assets.Add(new T4XModelProofAsset(
                        measured.Name,
                        sourceOffset,
                        sourceLength,
                        blob.Length,
                        measured.BoneCount,
                        measured.SurfaceCount,
                        measured.CollisionSurfaceCount,
                        measured.Stats.InlineMaterials,
                        measured.Stats.InlineImages,
                        measured.Stats.HasPhysPreset,
                        measured.Stats.HasPhysGeoms,
                        measured.Stats.HasCollmap));
                }
            }

            if (!VerifyContainer(outputFile, assets))
                throw new InvalidDataException("Stage 4 alpha 9j XModel proof container failed read-back verification.");

            return new T4XModelProofResult
            {
                SourceFastFile = sourceFastFile,
                OutputFile = outputFile,
                DeclaredXModelCount = declared,
                ResolvedXModelCount = offsets.Count,
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

    internal static bool TryBuildStructuralXboxBlobWithSmokeTechniqueSets(
        byte[] blob,
        out byte[] rebuilt,
        out string name,
        out int smokeTechniqueSets,
        out string reason)
    {
        rebuilt = Array.Empty<byte>();
        name = string.Empty;
        smokeTechniqueSets = 0;
        reason = string.Empty;
        try
        {
            if (blob.Length < XModelFixedSize)
                throw new InvalidDataException($"XModel proof blob is shorter than the {XModelFixedSize}-byte fixed body.");

            var patched = blob.ToArray();
            var insertions = new List<PlannedInsertion>();
            var isReferenceStub = blob[4] == 0 && blob[6] == 0;
            var planCtx = new WalkContext
            {
                Source = blob,
                Endianness = Endian.Big,
                Destination = patched,
                RootStart = 0,
                Header = null,
                Stats = new WalkStats(),
                PatchOnly = true,
                PlanSmokeTechniqueSets = true,
                PlannedInsertions = insertions
            };
            var measured = WalkXModel(planCtx, 0, requireTopLevelName: false, requireRenderableBody: !isReferenceStub);
            if (measured.EndOffset != blob.Length)
                throw new InvalidDataException($"XModel smoke-techset planning walk ended at 0x{measured.EndOffset:X}, expected 0x{blob.Length:X}.");
            if (planCtx.Stats.SmokeTechniqueSets == 0)
                throw new InvalidDataException("XModel has no packed Material.techniqueSet dependency eligible for smoke localization.");

            rebuilt = ApplyInsertions(patched, insertions);
            if (!TryValidateSelfContainedXboxBlob(rebuilt, out name, out reason))
                return false;

            smokeTechniqueSets = planCtx.Stats.SmokeTechniqueSets;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            reason = ex.Message;
            rebuilt = Array.Empty<byte>();
            name = string.Empty;
            smokeTechniqueSets = 0;
            return false;
        }
    }

    private static byte[] ApplyInsertions(byte[] source, IReadOnlyList<PlannedInsertion> insertions)
    {
        if (insertions.Count == 0) return source;
        using var ms = new MemoryStream(source.Length + insertions.Sum(x => x.Bytes.Length));
        var cursor = 0;
        foreach (var insertion in insertions.OrderBy(x => x.Offset))
        {
            if (insertion.Offset < cursor || insertion.Offset > source.Length)
                throw new InvalidDataException($"Invalid XModel insertion offset 0x{insertion.Offset:X}.");
            ms.Write(source, cursor, insertion.Offset - cursor);
            ms.Write(insertion.Bytes);
            cursor = insertion.Offset;
        }
        ms.Write(source, cursor, source.Length - cursor);
        return ms.ToArray();
    }

    private static byte[] BuildXboxNullTechniqueSet(string name)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var payload = new byte[XboxTechniqueSetFixedSize + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), Following);
        n.CopyTo(payload.AsSpan(XboxTechniqueSetFixedSize));
        return payload;
    }

    internal static bool TryValidateSelfContainedXboxBlob(byte[] blob, out string name, out string reason)
    {
        name = string.Empty;
        reason = string.Empty;
        try
        {
            if (blob.Length < XModelFixedSize)
                throw new InvalidDataException($"XModel proof blob is shorter than the {XModelFixedSize}-byte fixed body.");

            var isReferenceStub = blob[4] == 0 && blob[6] == 0;
            var ctx = new WalkContext
            {
                Source = blob,
                Endianness = Endian.Big,
                Destination = null,
                RootStart = 0,
                Header = null,
                Stats = new WalkStats(),
                RequireSelfContainedPointers = true
            };
            var measured = WalkXModel(ctx, 0, requireTopLevelName: false, requireRenderableBody: !isReferenceStub);
            if (measured.EndOffset != blob.Length)
                throw new InvalidDataException($"typed self-contained walk ended at 0x{measured.EndOffset:X}, expected 0x{blob.Length:X}.");
            if (measured.Name.StartsWith("<packed@", StringComparison.Ordinal))
                throw new InvalidDataException("top-level XModel name remains a packed source pointer.");

            name = measured.Name;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            reason = ex.Message;
            return false;
        }
    }

    internal static T4XModelDiagnosticCandidate MeasureDiagnosticCandidateAt(
        byte[] zone,
        int offset,
        Endian endian,
        ZoneHeaderSummary? header = null,
        bool allowPackedTopLevelName = false)
    {
        Ensure(zone, offset, XModelFixedSize, "XModel fixed body");
        var isReferenceStub = zone[offset + 4] == 0 && zone[offset + 6] == 0;
        var measured = Measure(
            zone, offset, endian,
            requireRenderableBody: !isReferenceStub,
            requireTopLevelName: !allowPackedTopLevelName,
            header: header);
        return new T4XModelDiagnosticCandidate(measured.Name, offset, measured.EndOffset);
    }

    internal static int MeasureInlineGfxImageEnd(byte[] data, int start, Endian endian, ZoneHeaderSummary? header = null)
    {
        var ctx = new WalkContext
        {
            Source = data,
            Endianness = endian,
            Destination = null,
            RootStart = start,
            Header = header,
            Stats = new WalkStats()
        };
        return WalkGfxImage(ctx, start, "TopLevelAnchor", 0, 0);
    }

    public static IReadOnlyList<T4XModelDiagnosticCandidate> DiscoverDiagnosticCandidates(
        byte[] zone,
        Endian endian,
        ZoneHeaderSummary? header = null)
    {
        var result = new List<T4XModelDiagnosticCandidate>();
        foreach (var offset in FindTopLevelXModelOffsets(zone, endian, includeReferenceStubs: true, header: header))
        {
            try
            {
                var isReferenceStub = zone[offset + 4] == 0 && zone[offset + 6] == 0;
                var m = Measure(zone, offset, endian, requireRenderableBody: !isReferenceStub, header: header);
                result.Add(new T4XModelDiagnosticCandidate(m.Name, offset, m.EndOffset));
            }
            catch (InvalidDataException) { }
        }
        return result;
    }


    internal static IReadOnlyList<T4RejectedInlineXModelCandidate> DiscoverRejectedInlineNameCandidates(
        byte[] zone,
        Endian endian,
        ZoneHeaderSummary? header,
        int rangeStart,
        int rangeEnd,
        CancellationToken cancellationToken = default)
    {
        var accepted = FindTopLevelXModelOffsets(zone, endian, includeReferenceStubs: true, header: header).ToHashSet();
        var result = new List<T4RejectedInlineXModelCandidate>();
        var start = Math.Max(0, rangeStart);
        var end = Math.Min(zone.Length, rangeEnd);
        if (end - start < XModelFixedSize + 5) return result;

        for (var offset = start; offset <= end - XModelFixedSize - 5; offset++)
        {
            if ((offset & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            var namePtr = ReadU32(zone, offset, endian);
            if (!IsInline(namePtr)) continue;
            if (!TryReadCString(zone, offset + XModelFixedSize, out var name, out _) || !PlausibleAssetName(name)) continue;
            if (accepted.Contains(offset)) continue;

            var numBones = zone[offset + 4];
            var numRootBones = zone[offset + 5];
            var numSurfs = zone[offset + 6];
            var lodRamp = zone[offset + 7];
            var numLods = ReadU16(zone, offset + 196, endian);
            var collLod = ReadI16(zone, offset + 198, endian);
            var isReferenceStub = numBones == 0 && numSurfs == 0;

            string reason;
            if ((numBones == 0) != (numSurfs == 0))
                reason = "precheck: bone/surface zero-state mismatch";
            else if (numRootBones > numBones)
                reason = $"precheck: root bones {numRootBones} > bones {numBones}";
            else if (numSurfs > 128)
                reason = $"precheck: surface count {numSurfs} > 128";
            else if (lodRamp > 1)
                reason = $"precheck: lodRamp={lodRamp} > 1";
            else if (numLods > 4)
                reason = $"precheck: numLods={numLods} > 4";
            else if (collLod < -1 || collLod > 3)
                reason = $"precheck: collLod={collLod} outside -1..3";
            else if (zone[offset + 209] > 1)
                reason = $"precheck: byte+209={zone[offset + 209]} > 1";
            else
            {
                reason = string.Empty;
                var pointerOffsets = new[] { 8, 12, 16, 20, 24, 28, 32, 36, 152, 164, 212, 216, 220, 224 };
                foreach (var p in pointerOffsets)
                {
                    var value = ReadU32(zone, offset + p, endian);
                    var valid = header is null ? IsSerializedPointer(value) : SerializedPointerWithinHeader(value, header);
                    if (!valid)
                    {
                        reason = $"precheck: pointer +0x{p:X}=0x{value:X8} invalid/out-of-bounds";
                        break;
                    }
                }

                if (reason.Length == 0 && isReferenceStub)
                {
                    if (numRootBones != 0)
                        reason = $"precheck: reference stub root bones={numRootBones}";
                    else if (ReadU32(zone, offset + 156, endian) != 0)
                        reason = $"precheck: reference stub +0x9C=0x{ReadU32(zone, offset + 156, endian):X8} expected 0";
                    else
                    {
                        var ownedPointerOffsets = new[] { 8, 12, 16, 20, 24, 28, 32, 36, 152, 164, 212, 216, 220, 224 };
                        var inlineOwned = ownedPointerOffsets.FirstOrDefault(p => IsInline(ReadU32(zone, offset + p, endian)));
                        if (inlineOwned != 0)
                            reason = $"precheck: reference stub owns inline pointer at +0x{inlineOwned:X}";
                    }
                }

                if (reason.Length == 0)
                {
                    var radius = ReadF32(zone, offset + 168, endian);
                    if (!FiniteRange(radius, 0f, 10_000_000f))
                        reason = $"precheck: radius={radius:G9} outside valid range";
                }

                if (reason.Length == 0)
                {
                    for (var p = 172; p <= 192; p += 4)
                    {
                        var value = ReadF32(zone, offset + p, endian);
                        if (!FiniteRange(value, -100_000_000f, 100_000_000f))
                        {
                            reason = $"precheck: bounds float +0x{p:X}={value:G9} invalid";
                            break;
                        }
                    }
                }

                if (reason.Length == 0)
                {
                    try
                    {
                        var measured = Measure(zone, offset, endian, requireRenderableBody: !isReferenceStub, header: header);
                        reason = $"strict Measure succeeds to 0x{measured.EndOffset:X8}; excluded by an earlier scanner condition not reproduced";
                    }
                    catch (InvalidDataException ex)
                    {
                        reason = "measure: " + ex.Message;
                    }
                }
            }

            result.Add(new T4RejectedInlineXModelCandidate(
                name, offset, reason, numBones, numRootBones, numSurfs, lodRamp, numLods, collLod, namePtr));
        }

        return result.OrderBy(x => x.Offset).ToList();
    }

    internal static IReadOnlyList<T4XModelDiagnosticCandidate> DiscoverPackedNameCandidates(
        byte[] zone,
        Endian endian,
        ZoneHeaderSummary header,
        int rangeStart,
        int rangeEnd,
        CancellationToken cancellationToken = default)
    {
        var result = new List<T4XModelDiagnosticCandidate>();
        var start = Math.Max(0, rangeStart);
        var end = Math.Min(zone.Length, rangeEnd);
        if (end - start < XModelFixedSize) return result;

        for (var offset = start; offset <= end - XModelFixedSize; offset++)
        {
            if ((offset & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            var namePtr = ReadU32(zone, offset, endian);
            if (namePtr == 0 || IsInline(namePtr) || !SerializedPointerWithinHeader(namePtr, header)) continue;

            var numBones = zone[offset + 4];
            var numRootBones = zone[offset + 5];
            var numSurfs = zone[offset + 6];
            var lodRamp = zone[offset + 7];
            var isReferenceStub = numBones == 0 && numSurfs == 0;
            if ((numBones == 0) != (numSurfs == 0)) continue;
            if (numRootBones > numBones || numSurfs > 128 || lodRamp > 1) continue;

            var numLods = ReadU16(zone, offset + 196, endian);
            var collLod = ReadI16(zone, offset + 198, endian);
            if (numLods > 4 || collLod < -1 || collLod > 3 || zone[offset + 209] > 1) continue;

            var pointerOffsets = new[] { 8, 12, 16, 20, 24, 28, 32, 36, 152, 164, 212, 216, 220, 224 };
            var pointersOkay = true;
            foreach (var p in pointerOffsets)
            {
                if (!SerializedPointerWithinHeader(ReadU32(zone, offset + p, endian), header))
                {
                    pointersOkay = false;
                    break;
                }
            }
            if (!pointersOkay) continue;

            if (isReferenceStub)
            {
                if (numRootBones != 0 || ReadU32(zone, offset + 156, endian) != 0) continue;
                var ownedPointerOffsets = new[] { 8, 12, 16, 20, 24, 28, 32, 36, 152, 164, 212, 216, 220, 224 };
                if (ownedPointerOffsets.Any(p => IsInline(ReadU32(zone, offset + p, endian)))) continue;
            }

            var radius = ReadF32(zone, offset + 168, endian);
            if (!FiniteRange(radius, 0f, 10_000_000f)) continue;
            var boundsOkay = true;
            for (var p = 172; p <= 192; p += 4)
            {
                if (!FiniteRange(ReadF32(zone, offset + p, endian), -100_000_000f, 100_000_000f))
                {
                    boundsOkay = false;
                    break;
                }
            }
            if (!boundsOkay) continue;

            try
            {
                var measured = Measure(
                    zone, offset, endian,
                    requireRenderableBody: !isReferenceStub,
                    requireTopLevelName: false,
                    header: header);
                if (measured.EndOffset <= end)
                    result.Add(new T4XModelDiagnosticCandidate(measured.Name, offset, measured.EndOffset));
            }
            catch (InvalidDataException)
            {
                // Not a complete packed-name top-level XModel structure.
            }
        }

        return result;
    }

    /// <summary>
    /// Alpha 9j bounded recovery for real custom-map XModels rejected solely by
    /// the old +0xA8 "radius" sanity heuristic. The value must still be finite,
    /// the top-level name must be inline/plausible, every serialized pointer must
    /// be valid for the source zone, every other fixed-field precheck must pass,
    /// and a complete ownership walk must end inside the authoritative deficit
    /// run. This is intentionally NOT a whole-zone relaxed scanner.
    /// </summary>
    internal static IReadOnlyList<T4XModelDiagnosticCandidate> DiscoverFiniteRadiusExceptionCandidates(
        byte[] zone,
        Endian endian,
        ZoneHeaderSummary header,
        int rangeStart,
        int rangeEnd,
        CancellationToken cancellationToken = default)
    {
        var result = new List<T4XModelDiagnosticCandidate>();
        var start = Math.Max(0, rangeStart);
        var end = Math.Min(zone.Length, rangeEnd);
        if (end - start < XModelFixedSize) return result;

        for (var offset = start; offset <= end - XModelFixedSize - 1; offset++)
        {
            if ((offset & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var namePtr = ReadU32(zone, offset, endian);
            if (!IsInline(namePtr)) continue;

            var numBones = zone[offset + 4];
            var numRootBones = zone[offset + 5];
            var numSurfs = zone[offset + 6];
            var lodRamp = zone[offset + 7];
            var isReferenceStub = numBones == 0 && numSurfs == 0;
            if ((numBones == 0) != (numSurfs == 0)) continue;
            if (isReferenceStub) continue;
            if (numRootBones > numBones || numSurfs > 128 || lodRamp > 1) continue;

            var numLods = ReadU16(zone, offset + 196, endian);
            var collLod = ReadI16(zone, offset + 198, endian);
            if (numLods > 4 || collLod < -1 || collLod > 3 || zone[offset + 209] > 1) continue;

            var pointerOffsets = new[] { 8, 12, 16, 20, 24, 28, 32, 36, 152, 164, 212, 216, 220, 224 };
            if (pointerOffsets.Any(p => !SerializedPointerWithinHeader(ReadU32(zone, offset + p, endian), header))) continue;

            var radius = ReadF32(zone, offset + 168, endian);
            // Normal strict candidates are handled by the ordinary scanner. This
            // bridge is specifically for finite values outside its old heuristic.
            if (!float.IsFinite(radius) || FiniteRange(radius, 0f, 10_000_000f)) continue;

            var boundsOkay = true;
            for (var p = 172; p <= 192; p += 4)
            {
                if (!FiniteRange(ReadF32(zone, offset + p, endian), -100_000_000f, 100_000_000f))
                {
                    boundsOkay = false;
                    break;
                }
            }
            if (!boundsOkay) continue;

            if (!TryReadCString(zone, offset + XModelFixedSize, out var name, out _) || !PlausibleAssetName(name)) continue;

            try
            {
                var measured = Measure(zone, offset, endian, requireRenderableBody: true, header: header);
                if (measured.EndOffset <= end && measured.Name.Equals(name, StringComparison.Ordinal))
                    result.Add(new T4XModelDiagnosticCandidate(measured.Name, offset, measured.EndOffset));
            }
            catch (InvalidDataException) { }
            catch (OverflowException) { }
        }

        return result.OrderBy(x => x.Offset).ToList();
    }

    private static bool SerializedPointerWithinHeader(uint value, ZoneHeaderSummary header)
    {
        if (value == 0 || IsInline(value)) return true;
        var decoded = T4ZonePointer.Decode(value);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null) return false;
        var block = (int)decoded.Block.Value;
        return block >= 0 && block < header.BlockSizes.Count && decoded.Offset < header.BlockSizes[block];
    }

    private static IReadOnlyList<int> FindTopLevelXModelOffsets(
        byte[] zone,
        Endian endian,
        bool includeReferenceStubs = false,
        ZoneHeaderSummary? header = null)
    {
        var offsets = new List<int>();

        for (var offset = 0; offset <= zone.Length - XModelFixedSize - 5; offset++)
        {
            if (!IsInline(ReadU32(zone, offset, endian))) continue;

            var numBones = zone[offset + 4];
            var numRootBones = zone[offset + 5];
            var numSurfs = zone[offset + 6];
            var lodRamp = zone[offset + 7];

            var isReferenceStub = numBones == 0 && numSurfs == 0;
            if ((numBones == 0) != (numSurfs == 0)) continue;
            if (isReferenceStub && !includeReferenceStubs) continue;
            if (numRootBones > numBones || numSurfs > 128 || lodRamp > 1) continue;

            var numLods = ReadU16(zone, offset + 196, endian);
            var collLod = ReadI16(zone, offset + 198, endian);
            if (numLods > 4 || collLod < -1 || collLod > 3 || zone[offset + 209] > 1) continue;

            var pointerOffsets = new[] { 8, 12, 16, 20, 24, 28, 32, 36, 152, 164, 212, 216, 220, 224 };
            var pointersOkay = true;
            foreach (var p in pointerOffsets)
            {
                var value = ReadU32(zone, offset + p, endian);
                var valid = header is null
                    ? IsSerializedPointer(value)
                    : SerializedPointerWithinHeader(value, header);
                if (!valid)
                {
                    pointersOkay = false;
                    break;
                }
            }
            if (!pointersOkay) continue;

            if (isReferenceStub)
            {
                if (numRootBones != 0 || ReadU32(zone, offset + 156, endian) != 0) continue;
                var ownedPointerOffsets = new[] { 8, 12, 16, 20, 24, 28, 32, 36, 152, 164, 212, 216, 220, 224 };
                if (ownedPointerOffsets.Any(p => IsInline(ReadU32(zone, offset + p, endian)))) continue;
            }

            var radius = ReadF32(zone, offset + 168, endian);
            if (!FiniteRange(radius, 0f, 10_000_000f)) continue;
            var boundsOkay = true;
            for (var p = 172; p <= 192; p += 4)
            {
                if (!FiniteRange(ReadF32(zone, offset + p, endian), -100_000_000f, 100_000_000f))
                {
                    boundsOkay = false;
                    break;
                }
            }
            if (!boundsOkay) continue;

            if (!TryReadCString(zone, offset + XModelFixedSize, out var name, out _) || !PlausibleAssetName(name))
                continue;

            try
            {
                var measured = Measure(zone, offset, endian, requireRenderableBody: !isReferenceStub, header: header);
                if (measured.EndOffset <= zone.Length && measured.Name.Equals(name, StringComparison.Ordinal))
                    offsets.Add(offset);
            }
            catch (InvalidDataException)
            {
                // Structural false positive.
            }
        }

        return offsets;
    }

    private static XModelMeasure Measure(
        byte[] data,
        int start,
        Endian endian,
        bool requireRenderableBody = true,
        bool requireTopLevelName = true,
        ZoneHeaderSummary? header = null)
    {
        var stats = new WalkStats();
        var ctx = new WalkContext
        {
            Source = data,
            Endianness = endian,
            Destination = null,
            RootStart = start,
            Header = header,
            Stats = stats
        };
        return WalkXModel(ctx, start, requireTopLevelName, requireRenderableBody);
    }

    private static byte[] TransformToXbox(
        byte[] source,
        int start,
        int end,
        bool requireRenderableBody = true,
        bool requireTopLevelName = true)
    {
        if (start < 0 || end < start || end > source.Length)
            throw new ArgumentOutOfRangeException(nameof(start));

        var dest = source.AsSpan(start, end - start).ToArray();
        var ctx = new WalkContext
        {
            Source = source,
            Endianness = Endian.Little,
            Destination = dest,
            RootStart = start,
            Stats = new WalkStats()
        };
        var measured = WalkXModel(ctx, start, requireTopLevelName, requireRenderableBody);
        if (measured.EndOffset != end)
            throw new InvalidDataException($"XModel transform ended at 0x{measured.EndOffset:X8}, expected 0x{end:X8}.");
        return dest;
    }

    private static XModelMeasure WalkXModel(
        WalkContext c,
        int start,
        bool requireTopLevelName,
        bool requireRenderableBody = true)
    {
        Ensure(c.Source, start, XModelFixedSize, "XModel fixed body");

        var namePtr = ReadU32(c.Source, start + 0, c.Endianness);
        var numBones = c.Source[start + 4];
        var numRootBones = c.Source[start + 5];
        var numSurfs = c.Source[start + 6];
        var numCollSurfs = checked((int)ReadU32(c.Source, start + 156, c.Endianness));

        var isReferenceStub = numBones == 0 && numSurfs == 0;
        if ((numBones == 0) != (numSurfs == 0))
            throw new InvalidDataException("XModel has a partial zero-body layout (bones/surfaces disagree).");
        if (requireRenderableBody && isReferenceStub)
            throw new InvalidDataException("Top-level XModel must have nonzero bones and surfaces.");
        if (numRootBones > numBones)
            throw new InvalidDataException("XModel numRootBones exceeds numBones.");
        if (numSurfs > 128 || numCollSurfs < 0 || numCollSurfs > 1_000_000)
            throw new InvalidDataException("XModel surface/collision counts are implausible.");
        if (requireTopLevelName && !IsInline(namePtr))
            throw new InvalidDataException("Top-level XModel name is not serialized inline.");

        Swap32(c, start + 0);
        foreach (var p in new[] { 8, 12, 16, 20, 24, 28, 32, 36 }) Swap32(c, start + p);

        for (var lod = 0; lod < 4; lod++)
        {
            var p = start + 40 + lod * 28;
            Swap32(c, p + 0); // dist
            Swap16(c, p + 4);
            Swap16(c, p + 6);
            for (var j = 0; j < 4; j++) Swap32(c, p + 8 + j * 4);
        }

        Swap32(c, start + 152);
        Swap32(c, start + 156);
        Swap32(c, start + 160);
        Swap32(c, start + 164);
        for (var p = 168; p <= 192; p += 4) Swap32(c, start + p);
        Swap16(c, start + 196);
        Swap16(c, start + 198);
        Swap32(c, start + 204);
        foreach (var p in new[] { 212, 216, 220, 224 }) Swap32(c, start + p);

        var ptrBoneNames = ReadU32(c.Source, start + 8, c.Endianness);
        var ptrParentList = ReadU32(c.Source, start + 12, c.Endianness);
        var ptrQuats = ReadU32(c.Source, start + 16, c.Endianness);
        var ptrTrans = ReadU32(c.Source, start + 20, c.Endianness);
        var ptrPartClass = ReadU32(c.Source, start + 24, c.Endianness);
        var ptrBaseMat = ReadU32(c.Source, start + 28, c.Endianness);
        var ptrSurfs = ReadU32(c.Source, start + 32, c.Endianness);
        var ptrMaterials = ReadU32(c.Source, start + 36, c.Endianness);
        var ptrCollSurfs = ReadU32(c.Source, start + 152, c.Endianness);
        var ptrBoneInfo = ReadU32(c.Source, start + 164, c.Endianness);
        var ptrPhysPreset = ReadU32(c.Source, start + 212, c.Endianness);
        var ptrPhysGeoms = ReadU32(c.Source, start + 216, c.Endianness);
        var ptrCollmap = ReadU32(c.Source, start + 220, c.Endianness);
        var ptrPhysConstraints = ReadU32(c.Source, start + 224, c.Endianness);

        if (isReferenceStub)
        {
            // T4 can serialize asset references as a full 228-byte XModel shell
            // containing an inline name but no owned model geometry/physics payload.
            // Accept that shape only when the caller explicitly allows zero-body
            // reference candidates; never reinterpret a half-populated model.
            var stubOwnedPointers = new[]
            {
                ptrBoneNames, ptrParentList, ptrQuats, ptrTrans, ptrPartClass, ptrBaseMat,
                ptrSurfs, ptrMaterials, ptrCollSurfs, ptrBoneInfo, ptrPhysPreset, ptrPhysGeoms,
                ptrCollmap, ptrPhysConstraints
            };
            if (numRootBones != 0 || numCollSurfs != 0 || stubOwnedPointers.Any(IsInline))
                throw new InvalidDataException("XModel zero-body reference stub owns unexpected inline geometry/physics data.");
        }

        ValidateSerializedPointer(c, "XModel.name", namePtr);
        ValidateSerializedPointer(c, "XModel.boneNames", ptrBoneNames);
        ValidateSerializedPointer(c, "XModel.parentList", ptrParentList);
        ValidateSerializedPointer(c, "XModel.quats", ptrQuats);
        ValidateSerializedPointer(c, "XModel.trans", ptrTrans);
        ValidateSerializedPointer(c, "XModel.partClassification", ptrPartClass);
        ValidateSerializedPointer(c, "XModel.baseMat", ptrBaseMat);
        ValidateSerializedPointer(c, "XModel.surfs", ptrSurfs);
        ValidateSerializedPointer(c, "XModel.materialHandles", ptrMaterials);
        ValidateSerializedPointer(c, "XModel.collSurfs", ptrCollSurfs);
        ValidateSerializedPointer(c, "XModel.boneInfo", ptrBoneInfo);
        ValidateSerializedPointer(c, "XModel.physPreset", ptrPhysPreset);
        ValidateSerializedPointer(c, "XModel.physGeoms", ptrPhysGeoms);
        ValidateSerializedPointer(c, "XModel.collmap", ptrCollmap);
        ValidateSerializedPointer(c, "XModel.physConstraints", ptrPhysConstraints);

        var cursor = checked(start + XModelFixedSize);
        string name;
        if (IsInline(namePtr))
            cursor = ConsumeCString(c, cursor, "XModel.name", out name);
        else
            name = $"<packed@0x{namePtr:X8}>";

        if (ptrBoneNames != 0 && IsInline(ptrBoneNames))
        {
            Ensure(c.Source, cursor, checked(numBones * 2), $"XModel '{name}' boneNames");
            for (var i = 0; i < numBones; i++) Swap16(c, cursor + i * 2);
            cursor += numBones * 2;
        }

        if (ptrParentList != 0 && IsInline(ptrParentList))
            cursor = Advance(c.Source, cursor, numBones - numRootBones, $"XModel '{name}' parentList");

        var childBones = numBones - numRootBones;
        if (ptrQuats != 0 && IsInline(ptrQuats))
        {
            var bytes = checked(childBones * 8);
            Ensure(c.Source, cursor, bytes, $"XModel '{name}' quats");
            for (var p = cursor; p < cursor + bytes; p += 2) Swap16(c, p);
            cursor += bytes;
        }

        if (ptrTrans != 0 && IsInline(ptrTrans))
        {
            var count = checked(childBones * 4);
            var bytes = checked(count * 4);
            Ensure(c.Source, cursor, bytes, $"XModel '{name}' trans");
            for (var p = cursor; p < cursor + bytes; p += 4) Swap32(c, p);
            cursor += bytes;
        }

        if (ptrPartClass != 0 && IsInline(ptrPartClass))
            cursor = Advance(c.Source, cursor, numBones, $"XModel '{name}' partClassification");

        if (ptrBaseMat != 0 && IsInline(ptrBaseMat))
        {
            var bytes = checked(numBones * 32);
            Ensure(c.Source, cursor, bytes, $"XModel '{name}' baseMat");
            for (var p = cursor; p < cursor + bytes; p += 4) Swap32(c, p);
            cursor += bytes;
        }

        if (ptrSurfs != 0 && IsInline(ptrSurfs))
            cursor = WalkSurfaceArray(c, cursor, numSurfs, name);

        if (ptrMaterials != 0 && IsInline(ptrMaterials))
            cursor = WalkMaterialHandleArray(c, cursor, numSurfs, name);

        if (ptrCollSurfs != 0 && IsInline(ptrCollSurfs))
            cursor = WalkCollisionSurfaces(c, cursor, numCollSurfs, name);

        if (ptrBoneInfo != 0 && IsInline(ptrBoneInfo))
        {
            var bytes = checked(numBones * XBoneInfoSize);
            Ensure(c.Source, cursor, bytes, $"XModel '{name}' boneInfo");
            for (var p = cursor; p < cursor + bytes; p += 4) Swap32(c, p);
            cursor += bytes;
        }

        if (ptrPhysPreset != 0 && IsInline(ptrPhysPreset))
        {
            c.Stats.HasPhysPreset = true;
            cursor = WalkPhysPreset(c, cursor, name);
        }

        if (ptrPhysGeoms != 0 && IsInline(ptrPhysGeoms))
        {
            c.Stats.HasPhysGeoms = true;
            cursor = WalkPhysGeomList(c, cursor, $"XModel '{name}' physGeoms");
        }

        if (ptrCollmap != 0 && IsInline(ptrCollmap))
        {
            c.Stats.HasCollmap = true;
            cursor = WalkPhysGeomList(c, cursor, $"XModel '{name}' collmap");
        }

        if (ptrPhysConstraints != 0 && IsInline(ptrPhysConstraints))
            throw new InvalidDataException($"XModel '{name}' owns inline PhysConstraints; Alpha 5 intentionally fails closed until that dependency serializer is proven.");

        return new XModelMeasure(name, cursor, numBones, numSurfs, numCollSurfs, c.Stats);
    }

    private static int WalkSurfaceArray(WalkContext c, int cursor, int count, string modelName)
    {
        var fixedStart = cursor;
        var fixedBytes = checked(count * XSurfaceFixedSize);
        Ensure(c.Source, fixedStart, fixedBytes, $"XModel '{modelName}' XSurface array");

        var surfaces = new List<(ushort VertCount, ushort TriCount, uint TriPtr, short[] BlendCounts, uint BlendPtr, uint VertsPtr, uint VertListCount, uint VertListPtr)>(count);

        for (var i = 0; i < count; i++)
        {
            var p = fixedStart + i * XSurfaceFixedSize;
            var vertCount = ReadU16(c.Source, p + 2, c.Endianness);
            var triCount = ReadU16(c.Source, p + 4, c.Endianness);
            var triPtr = ReadU32(c.Source, p + 12, c.Endianness);
            var blendCounts = new[]
            {
                ReadI16(c.Source, p + 16, c.Endianness),
                ReadI16(c.Source, p + 18, c.Endianness),
                ReadI16(c.Source, p + 20, c.Endianness),
                ReadI16(c.Source, p + 22, c.Endianness)
            };
            var blendPtr = ReadU32(c.Source, p + 24, c.Endianness);
            var vertsPtr = ReadU32(c.Source, p + 28, c.Endianness);
            var vertListCount = ReadU32(c.Source, p + 36, c.Endianness);
            var vertListPtr = ReadU32(c.Source, p + 40, c.Endianness);

            ValidateSerializedPointers(c, $"XModel '{modelName}' surface[{i}]", triPtr, blendPtr, vertsPtr, vertListPtr);
            if (blendCounts.Any(x => x < 0))
                throw new InvalidDataException($"XModel '{modelName}' surface[{i}] has negative vertsBlend counts.");
            if (vertListCount > 1_000_000)
                throw new InvalidDataException($"XModel '{modelName}' surface[{i}] has implausible vertListCount {vertListCount:N0}.");

            Swap16(c, p + 2);
            Swap16(c, p + 4);
            Swap16(c, p + 8);
            Swap16(c, p + 10);
            Swap32(c, p + 12);
            for (var q = 16; q <= 22; q += 2) Swap16(c, p + q);
            Swap32(c, p + 24);
            Swap32(c, p + 28);
            Swap32(c, p + 32);
            Swap32(c, p + 36);
            Swap32(c, p + 40);
            Swap32(c, p + 44);
            for (var q = 48; q <= 60; q += 4) Swap32(c, p + q);

            surfaces.Add((vertCount, triCount, triPtr, blendCounts, blendPtr, vertsPtr, vertListCount, vertListPtr));
        }

        cursor += fixedBytes;

        // T4 zone-code reorder: vertInfo/vertsBlend, verts0, vertList, triIndices.
        for (var i = 0; i < surfaces.Count; i++)
        {
            var s = surfaces[i];
            if (s.BlendPtr != 0 && IsInline(s.BlendPtr))
            {
                var blendCount = checked(s.BlendCounts[0] + 3 * s.BlendCounts[1] + 5 * s.BlendCounts[2] + 7 * s.BlendCounts[3]);
                var bytes = checked(blendCount * 2);
                Ensure(c.Source, cursor, bytes, $"XModel '{modelName}' surface[{i}].vertsBlend");
                for (var p = cursor; p < cursor + bytes; p += 2) Swap16(c, p);
                cursor += bytes;
            }

            if (s.VertsPtr != 0 && IsInline(s.VertsPtr))
            {
                var bytes = checked((int)s.VertCount * 32);
                Ensure(c.Source, cursor, bytes, $"XModel '{modelName}' surface[{i}].verts0");
                for (var p = cursor; p < cursor + bytes; p += 4) Swap32(c, p);
                cursor += bytes;
            }

            if (s.VertListPtr != 0 && IsInline(s.VertListPtr))
            {
                cursor = WalkRigidVertListArray(c, cursor, checked((int)s.VertListCount), modelName, i);
            }

            if (s.TriPtr != 0 && IsInline(s.TriPtr))
            {
                var bytes = checked((int)s.TriCount * 6);
                Ensure(c.Source, cursor, bytes, $"XModel '{modelName}' surface[{i}].triIndices");
                for (var p = cursor; p < cursor + bytes; p += 2) Swap16(c, p);
                cursor += bytes;
            }
        }

        return cursor;
    }

    private static int WalkRigidVertListArray(WalkContext c, int cursor, int count, string modelName, int surfaceIndex)
    {
        var fixedStart = cursor;
        var fixedBytes = checked(count * 12);
        Ensure(c.Source, fixedStart, fixedBytes, $"XModel '{modelName}' surface[{surfaceIndex}] rigid vert lists");
        var trees = new uint[count];

        for (var i = 0; i < count; i++)
        {
            var p = fixedStart + i * 12;
            for (var q = 0; q < 8; q += 2) Swap16(c, p + q);
            trees[i] = ReadU32(c.Source, p + 8, c.Endianness);
            if (!IsSerializedPointer(trees[i]))
                throw new InvalidDataException($"XModel '{modelName}' surface[{surfaceIndex}] rigid[{i}] has invalid collisionTree pointer 0x{trees[i]:X8}.");
            Swap32(c, p + 8);
        }

        cursor += fixedBytes;
        for (var i = 0; i < count; i++)
        {
            if (trees[i] != 0 && IsInline(trees[i]))
                cursor = WalkSurfaceCollisionTree(c, cursor, modelName, surfaceIndex, i);
        }
        return cursor;
    }

    private static int WalkSurfaceCollisionTree(WalkContext c, int cursor, string modelName, int surfaceIndex, int rigidIndex)
    {
        Ensure(c.Source, cursor, 40, "XSurfaceCollisionTree");
        var nodeCount = ReadU32(c.Source, cursor + 24, c.Endianness);
        var nodesPtr = ReadU32(c.Source, cursor + 28, c.Endianness);
        var leafCount = ReadU32(c.Source, cursor + 32, c.Endianness);
        var leafPtr = ReadU32(c.Source, cursor + 36, c.Endianness);
        ValidateSerializedPointers(c, "XSurfaceCollisionTree", nodesPtr, leafPtr);
        if (nodeCount > 10_000_000 || leafCount > 10_000_000)
            throw new InvalidDataException($"XModel '{modelName}' surface[{surfaceIndex}] rigid[{rigidIndex}] collision tree counts are implausible.");

        for (var p = cursor; p < cursor + 24; p += 4) Swap32(c, p);
        Swap32(c, cursor + 24);
        Swap32(c, cursor + 28);
        Swap32(c, cursor + 32);
        Swap32(c, cursor + 36);
        cursor += 40;

        if (nodesPtr != 0 && IsInline(nodesPtr))
        {
            var bytes = checked((int)nodeCount * 16);
            Ensure(c.Source, cursor, bytes, "XSurfaceCollisionTree.nodes");
            for (var p = cursor; p < cursor + bytes; p += 2) Swap16(c, p);
            cursor += bytes;
        }

        if (leafPtr != 0 && IsInline(leafPtr))
        {
            var bytes = checked((int)leafCount * 2);
            Ensure(c.Source, cursor, bytes, "XSurfaceCollisionTree.leafs");
            for (var p = cursor; p < cursor + bytes; p += 2) Swap16(c, p);
            cursor += bytes;
        }

        return cursor;
    }

    private static int WalkMaterialHandleArray(WalkContext c, int cursor, int count, string modelName)
    {
        var arrayStart = cursor;
        var bytes = checked(count * 4);
        Ensure(c.Source, arrayStart, bytes, $"XModel '{modelName}' material handles");
        var pointers = new uint[count];
        for (var i = 0; i < count; i++)
        {
            pointers[i] = ReadU32(c.Source, arrayStart + i * 4, c.Endianness);
            if (!IsSerializedPointer(pointers[i]))
                throw new InvalidDataException($"XModel '{modelName}' material handle[{i}] has invalid pointer 0x{pointers[i]:X8}.");
            Swap32(c, arrayStart + i * 4);
        }
        cursor += bytes;

        for (var i = 0; i < pointers.Length; i++)
        {
            if (pointers[i] != 0 && IsInline(pointers[i]))
            {
                c.Stats.InlineMaterials++;
                cursor = WalkMaterial(c, cursor, modelName, i);
            }
        }
        return cursor;
    }

    private static int WalkMaterial(WalkContext c, int cursor, string modelName, int materialIndex)
    {
        Ensure(c.Source, cursor, MaterialFixedSize, "Material fixed body");
        var start = cursor;
        var namePtr = ReadU32(c.Source, start + 0, c.Endianness);
        var textureCount = c.Source[start + 91];
        var constantCount = c.Source[start + 92];
        var stateBitsCount = c.Source[start + 93];
        var techniquePtr = ReadU32(c.Source, start + 96, c.Endianness);
        var texturePtr = ReadU32(c.Source, start + 100, c.Endianness);
        var constantPtr = ReadU32(c.Source, start + 104, c.Endianness);
        var statePtr = ReadU32(c.Source, start + 108, c.Endianness);
        ValidateSerializedPointer(c, "Material.info.name", namePtr);
        ValidateSerializedPointer(c, "Material.techniqueSet", techniquePtr);
        ValidateSerializedPointer(c, "Material.textureTable", texturePtr);
        ValidateSerializedPointer(c, "Material.constantTable", constantPtr);
        ValidateSerializedPointer(c, "Material.stateBitsTable", statePtr);

        Swap32(c, start + 0);
        Swap64(c, start + 8);
        Swap32(c, start + 16);
        Swap32(c, start + 20);
        Swap16(c, start + 24);
        Swap32(c, start + 96);
        Swap32(c, start + 100);
        Swap32(c, start + 104);
        Swap32(c, start + 108);
        cursor += MaterialFixedSize;

        if (namePtr != 0 && IsInline(namePtr))
            cursor = ConsumeCString(c, cursor, "Material.name", out _);

        if (techniquePtr != 0 && !IsInline(techniquePtr) && c.PlanSmokeTechniqueSets)
        {
            if (c.Destination is null || c.PlannedInsertions is null)
                throw new InvalidDataException("XModel smoke TechniqueSet planning requires a writable destination and insertion list.");
            var smokeName = $"__waw360_ts_xm_{materialIndex:D3}_{techniquePtr:X8}";
            WriteDestinationU32BigEndian(c, start + 96, Following);
            c.PlannedInsertions.Add(new PlannedInsertion(cursor, BuildXboxNullTechniqueSet(smokeName)));
            c.Stats.SmokeTechniqueSets++;
        }
        else if (techniquePtr != 0 && IsInline(techniquePtr))
        {
            if (c.Endianness != Endian.Big)
                throw new InvalidDataException($"XModel '{modelName}' material[{materialIndex}] owns an inline MaterialTechniqueSet; PC shader graphs are not serialized into Xbox output.");
            cursor = WalkXboxNullTechniqueSet(c, cursor, modelName, materialIndex);
        }

        if (texturePtr != 0 && IsInline(texturePtr))
        {
            var fixedStart = cursor;
            var fixedBytes = checked(textureCount * MaterialTextureDefSize);
            Ensure(c.Source, fixedStart, fixedBytes, "Material.textureTable");
            var imagePtrs = new (uint Ptr, byte Semantic)[textureCount];
            for (var i = 0; i < textureCount; i++)
            {
                var p = fixedStart + i * MaterialTextureDefSize;
                var semantic = c.Source[p + 7];
                var imagePtr = ReadU32(c.Source, p + 12, c.Endianness);
                if (!IsSerializedPointer(imagePtr))
                    throw new InvalidDataException($"Material texture[{i}] has invalid image/water pointer 0x{imagePtr:X8}.");
                Swap32(c, p + 0);
                Swap32(c, p + 12);
                imagePtrs[i] = (imagePtr, semantic);
            }
            cursor += fixedBytes;

            for (var i = 0; i < imagePtrs.Length; i++)
            {
                var entry = imagePtrs[i];
                if (entry.Ptr == 0 || !IsInline(entry.Ptr)) continue;
                if (entry.Semantic == 0x0B)
                    throw new InvalidDataException($"XModel '{modelName}' material[{materialIndex}] owns an inline water_t texture; Alpha 5 fails closed until water serialization is proven.");
                c.Stats.InlineImages++;
                cursor = WalkGfxImage(c, cursor, modelName, materialIndex, i);
            }
        }

        if (constantPtr != 0 && IsInline(constantPtr))
        {
            var bytes = checked(constantCount * MaterialConstantDefSize);
            Ensure(c.Source, cursor, bytes, "Material.constantTable");
            for (var i = 0; i < constantCount; i++)
            {
                var p = cursor + i * MaterialConstantDefSize;
                Swap32(c, p + 0);
                for (var q = 16; q < 32; q += 4) Swap32(c, p + q);
            }
            cursor += bytes;
        }

        if (statePtr != 0 && IsInline(statePtr))
        {
            var bytes = checked(stateBitsCount * GfxStateBitsSize);
            Ensure(c.Source, cursor, bytes, "Material.stateBitsTable");
            for (var p = cursor; p < cursor + bytes; p += 4) Swap32(c, p);
            cursor += bytes;
        }

        return cursor;
    }

    private static int WalkXboxNullTechniqueSet(WalkContext c, int cursor, string modelName, int materialIndex)
    {
        Ensure(c.Source, cursor, XboxTechniqueSetFixedSize, $"XModel '{modelName}' material[{materialIndex}] Xbox null TechniqueSet");
        var start = cursor;
        var namePtr = ReadU32(c.Source, start + 0, c.Endianness);
        if (!IsInline(namePtr))
            throw new InvalidDataException($"XModel '{modelName}' material[{materialIndex}] inline Xbox TechniqueSet has non-inline name pointer 0x{namePtr:X8}.");
        if (ReadU32(c.Source, start + 8, c.Endianness) != 0)
            throw new InvalidDataException($"XModel '{modelName}' material[{materialIndex}] smoke TechniqueSet remappedTechniqueSet is non-null.");
        for (var i = 0; i < XboxTechniqueCount; i++)
        {
            var raw = ReadU32(c.Source, start + 12 + i * 4, c.Endianness);
            if (raw != 0)
                throw new InvalidDataException($"XModel '{modelName}' material[{materialIndex}] smoke TechniqueSet slot {i} is non-null.");
        }

        cursor += XboxTechniqueSetFixedSize;
        cursor = ConsumeCString(c, cursor, "MaterialTechniqueSet.name", out _);
        return cursor;
    }

    private static int WalkGfxImage(WalkContext c, int cursor, string modelName, int materialIndex, int textureIndex)
    {
        Ensure(c.Source, cursor, GfxImageFixedSize, "GfxImage fixed body");
        var start = cursor;
        var texturePtr = ReadU32(c.Source, start + 4, c.Endianness);
        var namePtr = ReadU32(c.Source, start + 32, c.Endianness);
        ValidateSerializedPointers(c, "GfxImage", texturePtr, namePtr);

        Swap32(c, start + 0);
        Swap32(c, start + 4);
        Swap32(c, start + 16);
        Swap32(c, start + 20);
        Swap16(c, start + 24);
        Swap16(c, start + 26);
        Swap16(c, start + 28);
        Swap32(c, start + 32);
        cursor += GfxImageFixedSize;

        // T4 image serialization orders name before the load definition.
        if (namePtr != 0 && IsInline(namePtr))
            cursor = ConsumeCString(c, cursor, "GfxImage.name", out _);

        if (texturePtr != 0 && IsInline(texturePtr))
        {
            Ensure(c.Source, cursor, GfxImageLoadDefSize, "GfxImageLoadDef");
            var resourceSize = ReadU32(c.Source, cursor + 12, c.Endianness);
            Swap16(c, cursor + 2);
            Swap16(c, cursor + 4);
            Swap16(c, cursor + 6);
            Swap32(c, cursor + 8);
            Swap32(c, cursor + 12);
            cursor += GfxImageLoadDefSize;
            if (resourceSize > int.MaxValue)
                throw new InvalidDataException($"XModel '{modelName}' material[{materialIndex}] image[{textureIndex}] resource payload is too large.");
            cursor = Advance(c.Source, cursor, (int)resourceSize, "GfxImageLoadDef.resource");
        }

        return cursor;
    }

    private static int WalkCollisionSurfaces(WalkContext c, int cursor, int count, string modelName)
    {
        var fixedStart = cursor;
        var bytes = checked(count * XModelCollSurfSize);
        Ensure(c.Source, fixedStart, bytes, $"XModel '{modelName}' collision surfaces");
        var triInfo = new (uint Ptr, int Count)[count];

        for (var i = 0; i < count; i++)
        {
            var p = fixedStart + i * XModelCollSurfSize;
            var ptr = ReadU32(c.Source, p + 0, c.Endianness);
            var num = checked((int)ReadU32(c.Source, p + 4, c.Endianness));
            if (!IsSerializedPointer(ptr) || num < 0 || num > 10_000_000)
                throw new InvalidDataException($"XModel '{modelName}' collision surface[{i}] is invalid.");
            Swap32(c, p + 0);
            Swap32(c, p + 4);
            for (var q = 8; q <= 28; q += 4) Swap32(c, p + q);
            Swap32(c, p + 32);
            Swap32(c, p + 36);
            Swap32(c, p + 40);
            triInfo[i] = (ptr, num);
        }
        cursor += bytes;

        for (var i = 0; i < count; i++)
        {
            if (triInfo[i].Ptr != 0 && IsInline(triInfo[i].Ptr))
            {
                var triBytes = checked(triInfo[i].Count * 48);
                Ensure(c.Source, cursor, triBytes, "XModelCollTri_s array");
                for (var p = cursor; p < cursor + triBytes; p += 4) Swap32(c, p);
                cursor += triBytes;
            }
        }
        return cursor;
    }

    private static int WalkPhysPreset(WalkContext c, int cursor, string modelName)
    {
        Ensure(c.Source, cursor, PhysPresetSize, "PhysPreset");
        var start = cursor;
        var namePtr = ReadU32(c.Source, start + 0, c.Endianness);
        var soundPtr = ReadU32(c.Source, start + 28, c.Endianness);
        ValidateSerializedPointers(c, "PhysPreset", namePtr, soundPtr);

        Swap32(c, start + 0);
        for (var p = 4; p <= 24; p += 4) Swap32(c, start + p);
        Swap32(c, start + 28);
        Swap32(c, start + 32);
        Swap32(c, start + 36);
        Swap32(c, start + 40);
        Swap32(c, start + 44);
        cursor += PhysPresetSize;

        if (namePtr != 0 && IsInline(namePtr))
            cursor = ConsumeCString(c, cursor, "PhysPreset.name", out _);
        if (soundPtr != 0 && IsInline(soundPtr))
            cursor = ConsumeCString(c, cursor, "PhysPreset.sndAliasPrefix", out _);
        return cursor;
    }

    private static int WalkPhysGeomList(WalkContext c, int cursor, string label)
    {
        Ensure(c.Source, cursor, PhysGeomListSize, label + " PhysGeomList");
        var start = cursor;
        var count = ReadU32(c.Source, start + 0, c.Endianness);
        var geomsPtr = ReadU32(c.Source, start + 4, c.Endianness);
        if (count > 1_000_000 || !IsSerializedPointer(geomsPtr))
            throw new InvalidDataException($"{label}: invalid PhysGeomList.");
        Swap32(c, start + 0);
        Swap32(c, start + 4);
        Swap32(c, start + 8);
        Swap32(c, start + 12);
        Swap32(c, start + 16);
        cursor += PhysGeomListSize;

        if (geomsPtr == 0 || !IsInline(geomsPtr)) return cursor;

        var geomCount = checked((int)count);
        var fixedStart = cursor;
        var bytes = checked(geomCount * PhysGeomInfoSize);
        Ensure(c.Source, fixedStart, bytes, label + " PhysGeomInfo array");
        var brushes = new uint[geomCount];
        for (var i = 0; i < geomCount; i++)
        {
            var p = fixedStart + i * PhysGeomInfoSize;
            brushes[i] = ReadU32(c.Source, p + 0, c.Endianness);
            if (!IsSerializedPointer(brushes[i]))
                throw new InvalidDataException($"{label}: PhysGeomInfo[{i}] invalid brush pointer 0x{brushes[i]:X8}.");
            Swap32(c, p + 0);
            Swap32(c, p + 4);
            for (var q = 8; q < PhysGeomInfoSize; q += 4) Swap32(c, p + q);
        }
        cursor += bytes;

        for (var i = 0; i < brushes.Length; i++)
        {
            if (brushes[i] != 0 && IsInline(brushes[i]))
                cursor = WalkBrush(c, cursor, $"{label} geom[{i}]");
        }
        return cursor;
    }

    private static int WalkBrush(WalkContext c, int cursor, string label)
    {
        Ensure(c.Source, cursor, BrushWrapperSize, label + " BrushWrapper");
        var start = cursor;
        var numSides = ReadU32(c.Source, start + 28, c.Endianness);
        var sidesPtr = ReadU32(c.Source, start + 32, c.Endianness);
        var adjacentPtr = ReadU32(c.Source, start + 48, c.Endianness);
        var numVerts = ReadU32(c.Source, start + 72, c.Endianness);
        var vertsPtr = ReadU32(c.Source, start + 76, c.Endianness);
        var totalEdges = ReadU32(c.Source, start + 80, c.Endianness);
        var planesPtr = ReadU32(c.Source, start + 84, c.Endianness);
        ValidateSerializedPointers(c, "BrushWrapper", sidesPtr, adjacentPtr, vertsPtr, planesPtr);
        if (numSides > 1_000_000 || numVerts > 10_000_000 || totalEdges > 100_000_000)
            throw new InvalidDataException($"{label}: brush counts are implausible.");

        for (var p = 0; p <= 28; p += 4) Swap32(c, start + p);
        Swap32(c, start + 32);
        for (var p = 36; p <= 46; p += 2) Swap16(c, start + p);
        Swap32(c, start + 48);
        for (var p = 52; p <= 62; p += 2) Swap16(c, start + p);
        Swap32(c, start + 72);
        Swap32(c, start + 76);
        Swap32(c, start + 80);
        Swap32(c, start + 84);
        cursor += BrushWrapperSize;

        if (sidesPtr != 0 && IsInline(sidesPtr))
        {
            var sideCount = checked((int)numSides);
            var fixedStart = cursor;
            var bytes = checked(sideCount * BrushSideSize);
            Ensure(c.Source, fixedStart, bytes, label + " brush sides");
            var planePointers = new uint[sideCount];
            for (var i = 0; i < sideCount; i++)
            {
                var p = fixedStart + i * BrushSideSize;
                planePointers[i] = ReadU32(c.Source, p + 0, c.Endianness);
                if (!IsSerializedPointer(planePointers[i]))
                    throw new InvalidDataException($"{label}: side[{i}] invalid plane pointer 0x{planePointers[i]:X8}.");
                Swap32(c, p + 0);
                Swap32(c, p + 4);
                Swap16(c, p + 8);
            }
            cursor += bytes;

            for (var i = 0; i < planePointers.Length; i++)
            {
                if (planePointers[i] != 0 && IsInline(planePointers[i]))
                    cursor = WalkPlane(c, cursor, label + $" side[{i}].plane");
            }
        }

        if (adjacentPtr != 0 && IsInline(adjacentPtr))
            cursor = Advance(c.Source, cursor, checked((int)totalEdges), label + " baseAdjacentSide");

        if (vertsPtr != 0 && IsInline(vertsPtr))
        {
            var bytes = checked((int)numVerts * 12);
            Ensure(c.Source, cursor, bytes, label + " verts");
            for (var p = cursor; p < cursor + bytes; p += 4) Swap32(c, p);
            cursor += bytes;
        }

        if (planesPtr != 0 && IsInline(planesPtr))
        {
            var planeCount = checked((int)numSides);
            for (var i = 0; i < planeCount; i++)
                cursor = WalkPlane(c, cursor, label + $" planes[{i}]");
        }

        return cursor;
    }

    private static int WalkPlane(WalkContext c, int cursor, string label)
    {
        Ensure(c.Source, cursor, CPlaneSize, label);
        for (var p = 0; p < 16; p += 4) Swap32(c, cursor + p);
        return cursor + CPlaneSize;
    }

    /// <summary>
    /// Stage 4 alpha 7b bridge: measure an inline XModel dependency using the exact
    /// Alpha 5 serializer walker. This keeps WeaponDef ownership traversal from
    /// re-implementing XModel/XSurface/Material/GfxImage rules.
    /// </summary>
    internal static int MeasureInlineXModelEnd(byte[] data, int start, Endian endian, ZoneHeaderSummary? header = null)
    {
        var stats = new WalkStats();
        var ctx = new WalkContext
        {
            Source = data,
            Endianness = endian,
            Destination = null,
            RootStart = start,
            Header = header,
            Stats = stats
        };

        // WeaponDef-owned XModel pointers can serialize either a complete model or
        // a name-only zero-body reference shell. Alpha 5's top-level proof excludes
        // those shells by design; the nested bridge must accept the strict shell
        // shape while retaining every other structural check.
        return WalkXModel(
            ctx,
            start,
            requireTopLevelName: true,
            requireRenderableBody: false).EndOffset;
    }

    internal static byte[] TransformInlineXModelToXbox(byte[] source, int start, out int end, ZoneHeaderSummary? header = null)
    {
        end = MeasureInlineXModelEnd(source, start, Endian.Little, header);
        var dest = source.AsSpan(start, end - start).ToArray();
        var ctx = new WalkContext
        {
            Source = source,
            Endianness = Endian.Little,
            Destination = dest,
            RootStart = start,
            Header = header,
            Stats = new WalkStats()
        };
        var transformed = WalkXModel(
            ctx,
            start,
            requireTopLevelName: true,
            requireRenderableBody: false);
        if (transformed.EndOffset != end)
            throw new InvalidDataException(
                $"Inline/nested XModel transform ended at 0x{transformed.EndOffset:X8}, expected 0x{end:X8}.");
        return dest;
    }

    /// <summary>
    /// Stage 4 alpha 7b bridge for inline Material dependencies owned by WeaponDef
    /// (reticles/HUD/overlay icons and FlameTable materials). It intentionally
    /// inherits Alpha 5's fail-closed MaterialTechniqueSet/water behavior.
    /// </summary>
    internal static int MeasureInlineMaterialEnd(byte[] data, int start, Endian endian, ZoneHeaderSummary? header = null)
    {
        var ctx = new WalkContext
        {
            Source = data,
            Endianness = endian,
            Destination = null,
            RootStart = start,
            Header = header,
            Stats = new WalkStats()
        };
        return WalkMaterial(ctx, start, "WeaponDef", 0);
    }

    internal static byte[] TransformInlineMaterialToXbox(byte[] source, int start, out int end, ZoneHeaderSummary? header = null)
    {
        end = MeasureInlineMaterialEnd(source, start, Endian.Little, header);
        var dest = source.AsSpan(start, end - start).ToArray();
        var ctx = new WalkContext
        {
            Source = source,
            Endianness = Endian.Little,
            Destination = dest,
            RootStart = start,
            Header = header,
            Stats = new WalkStats()
        };
        var transformedEnd = WalkMaterial(ctx, start, "WeaponDef", 0);
        if (transformedEnd != end)
            throw new InvalidDataException($"Inline Material transform ended at 0x{transformedEnd:X8}, expected 0x{end:X8}.");
        return dest;
    }

    internal static string DescribeMaterialProbe(byte[] data, int expectedStart, Endian endian, ZoneHeaderSummary? header = null)
    {
        var deltas = new[] { -32, -16, -8, -4, -3, -2, -1, 0, 1, 2, 3, 4, 8, 16, 32 };
        var parts = new List<string>();
        foreach (var delta in deltas)
        {
            var start = expectedStart + delta;
            if (start < 0 || start >= data.Length) continue;

            // Source fastfiles in this stage are PC/little-endian. The authoritative
            // PC T4 Material fixed body is 112 bytes. Probe it first.
            if (start + MaterialFixedSize <= data.Length)
            {
                try
                {
                    var name = ReadU32(data, start + 0, endian);
                    var tech = ReadU32(data, start + 96, endian);
                    var tex = ReadU32(data, start + 100, endian);
                    var con = ReadU32(data, start + 104, endian);
                    var state = ReadU32(data, start + 108, endian);
                    var valid = CountProbePointers(new[] { name, tech, tex, con, state }, header);
                    parts.Add($"pc112{delta:+#;-#;0}:ptrs={valid}/5 counts={data[start + 91]}/{data[start + 92]}/{data[start + 93]} name=0x{name:X8} tech=0x{tech:X8} tex=0x{tex:X8} con=0x{con:X8} state=0x{state:X8}");
                }
                catch { }
            }

            // Stock Xbox 360 T4 donor data empirically matches a 96-byte Material
            // fixed body: same leading MaterialInfo, counts at 75..77, pointers at
            // 80..92. This is diagnostic only while reading a PC source; it helps
            // expose a +/-16 byte cursor error without accidentally changing the
            // source layout.
            if (start + XboxMaterialFixedSize <= data.Length)
            {
                try
                {
                    var name = ReadU32(data, start + 0, endian);
                    var tech = ReadU32(data, start + 80, endian);
                    var tex = ReadU32(data, start + 84, endian);
                    var con = ReadU32(data, start + 88, endian);
                    var state = ReadU32(data, start + 92, endian);
                    var valid = CountProbePointers(new[] { name, tech, tex, con, state }, header);
                    parts.Add($"xbox96{delta:+#;-#;0}:ptrs={valid}/5 counts={data[start + 75]}/{data[start + 76]}/{data[start + 77]} name=0x{name:X8} tech=0x{tech:X8} tex=0x{tex:X8} con=0x{con:X8} state=0x{state:X8}");
                }
                catch { }
            }
        }
        return parts.Count == 0 ? "<no probe candidates>" : string.Join(" | ", parts);
    }

    private static int CountProbePointers(IEnumerable<uint> values, ZoneHeaderSummary? header)
    {
        var valid = 0;
        foreach (var v in values)
        {
            if (v == 0 || IsInline(v))
            {
                valid++;
                continue;
            }
            var d = T4ZonePointer.Decode(v);
            if (d.Kind != T4ZonePointerKind.Offset || d.Block is null) continue;
            if (header is null)
            {
                valid++;
                continue;
            }
            var block = (int)d.Block.Value;
            if (block >= 0 && block < header.BlockSizes.Count && d.Offset < header.BlockSizes[block])
                valid++;
        }
        return valid;
    }

    private static bool VerifyContainer(string path, IReadOnlyList<T4XModelProofAsset> expected)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 16 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != 5) return false;
        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12, 4));
        if (count != (uint)expected.Count) return false;

        var cursor = 16;
        for (var i = 0; i < expected.Count; i++)
        {
            if (cursor + 8 > bytes.Length) return false;
            var sourceOffset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor, 4));
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor + 4, 4)));
            cursor += 8;
            if (length <= 0 || cursor + length > bytes.Length) return false;
            if (sourceOffset != (uint)expected[i].SourceOffset) return false;

            var blob = bytes.AsSpan(cursor, length).ToArray();
            var requireRenderableBody = expected[i].BoneCount != 0 || expected[i].SurfaceCount != 0;
            var packedTopLevelName = !IsInline(ReadU32(blob, 0, Endian.Big));
            var measured = Measure(
                blob, 0, Endian.Big,
                requireRenderableBody: requireRenderableBody,
                requireTopLevelName: !packedTopLevelName);
            if (measured.EndOffset != length || !measured.Name.Equals(expected[i].Name, StringComparison.Ordinal)) return false;
            cursor += length;
        }

        return cursor == bytes.Length;
    }

    private static int ConsumeCString(WalkContext c, int cursor, string label, out string value)
    {
        if (!TryReadCString(c.Source, cursor, out value, out var end))
            throw new InvalidDataException($"{label} at 0x{cursor:X8} is not a valid ASCII C string.");
        return end;
    }

    private static bool TryReadCString(byte[] data, int cursor, out string value, out int end)
    {
        value = string.Empty;
        end = cursor;
        if (cursor < 0 || cursor >= data.Length) return false;
        var max = Math.Min(data.Length, cursor + 1024);
        while (end < max && data[end] != 0)
        {
            var b = data[end];
            if (b < 0x20 || b > 0x7E) return false;
            end++;
        }
        if (end >= max || end == cursor || data[end] != 0) return false;
        value = Encoding.ASCII.GetString(data, cursor, end - cursor);
        end++;
        return true;
    }

    private static bool PlausibleAssetName(string name) =>
        name.Length >= 2 &&
        !name.Any(char.IsWhiteSpace) &&
        name.Any(char.IsLetter) &&
        name.All(ch => ch >= 0x20 && ch <= 0x7E);


    private static void ValidateSerializedPointer(WalkContext c, string label, uint value)
    {
        if (value == 0 || IsInline(value)) return;
        if (c.RequireSelfContainedPointers)
            throw new InvalidDataException($"{label} retains packed/source pointer 0x{value:X8}; self-contained promotion refused.");
        var decoded = T4ZonePointer.Decode(value);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null)
            throw new InvalidDataException($"{label} contains invalid serialized pointer 0x{value:X8}.");
        if (c.Header is not null)
        {
            var block = (int)decoded.Block.Value;
            if (block < 0 || block >= c.Header.BlockSizes.Count || decoded.Offset >= c.Header.BlockSizes[block])
                throw new InvalidDataException($"{label} pointer 0x{value:X8} resolves to {decoded} outside its block bounds.");
        }
    }

    private static void ValidateSerializedPointers(WalkContext c, string label, params uint[] values)
    {
        foreach (var value in values)
        {
            if (value == 0 || IsInline(value)) continue;
            if (c.RequireSelfContainedPointers)
                throw new InvalidDataException($"{label} retains packed/source pointer 0x{value:X8}; self-contained promotion refused.");
            var decoded = T4ZonePointer.Decode(value);
            if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null)
                throw new InvalidDataException($"{label} contains invalid serialized pointer 0x{value:X8}.");
            if (c.Header is not null)
            {
                var block = (int)decoded.Block.Value;
                if (block < 0 || block >= c.Header.BlockSizes.Count || decoded.Offset >= c.Header.BlockSizes[block])
                    throw new InvalidDataException($"{label} pointer 0x{value:X8} resolves to {decoded} outside its block bounds.");
            }
        }
    }

    private static bool IsInline(uint value) => value is Following or Insert;

    private static bool IsSerializedPointer(uint value)
    {
        if (value == 0 || IsInline(value)) return true;
        var decoded = T4ZonePointer.Decode(value);
        return decoded.Kind == T4ZonePointerKind.Offset && decoded.Block is not null;
    }

    private static int Advance(byte[] data, int cursor, int bytes, string label)
    {
        Ensure(data, cursor, bytes, label);
        return checked(cursor + bytes);
    }

    private static void Ensure(byte[] data, int offset, int count, string label)
    {
        if (offset < 0 || count < 0 || (long)offset + count > data.Length)
            throw new InvalidDataException($"{label} overruns the serialized record at 0x{offset:X8} (+0x{count:X}).");
    }

    private static bool FiniteRange(float value, float min, float max) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;

    private static void Swap16(WalkContext c, int absoluteOffset)
    {
        if (c.Destination is null || c.PatchOnly) return;
        var d = absoluteOffset - c.RootStart;
        if (d < 0 || d + 2 > c.Destination.Length)
            throw new InvalidDataException("Endian transform write exceeded proof buffer.");
        (c.Destination[d], c.Destination[d + 1]) = (c.Destination[d + 1], c.Destination[d]);
    }

    private static void Swap32(WalkContext c, int absoluteOffset)
    {
        if (c.Destination is null || c.PatchOnly) return;
        var d = absoluteOffset - c.RootStart;
        if (d < 0 || d + 4 > c.Destination.Length)
            throw new InvalidDataException("Endian transform write exceeded proof buffer.");
        Array.Reverse(c.Destination, d, 4);
    }

    private static void Swap64(WalkContext c, int absoluteOffset)
    {
        if (c.Destination is null || c.PatchOnly) return;
        var d = absoluteOffset - c.RootStart;
        if (d < 0 || d + 8 > c.Destination.Length)
            throw new InvalidDataException("Endian transform write exceeded proof buffer.");
        Array.Reverse(c.Destination, d, 8);
    }

    private static void WriteDestinationU32BigEndian(WalkContext c, int absoluteOffset, uint value)
    {
        if (c.Destination is null)
            throw new InvalidDataException("XModel patch requested without a destination buffer.");
        var d = absoluteOffset - c.RootStart;
        if (d < 0 || d + 4 > c.Destination.Length)
            throw new InvalidDataException("XModel patch write exceeded proof buffer.");
        BinaryPrimitives.WriteUInt32BigEndian(c.Destination.AsSpan(d, 4), value);
    }

    private static uint ReadU32(byte[] bytes, int offset, Endian endian)
    {
        Ensure(bytes, offset, 4, "uint32");
        var span = bytes.AsSpan(offset, 4);
        return endian == Endian.Big
            ? BinaryPrimitives.ReadUInt32BigEndian(span)
            : BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    private static ushort ReadU16(byte[] bytes, int offset, Endian endian)
    {
        Ensure(bytes, offset, 2, "uint16");
        var span = bytes.AsSpan(offset, 2);
        return endian == Endian.Big
            ? BinaryPrimitives.ReadUInt16BigEndian(span)
            : BinaryPrimitives.ReadUInt16LittleEndian(span);
    }

    private static short ReadI16(byte[] bytes, int offset, Endian endian)
    {
        Ensure(bytes, offset, 2, "int16");
        var span = bytes.AsSpan(offset, 2);
        return endian == Endian.Big
            ? BinaryPrimitives.ReadInt16BigEndian(span)
            : BinaryPrimitives.ReadInt16LittleEndian(span);
    }

    private static float ReadF32(byte[] bytes, int offset, Endian endian) =>
        BitConverter.Int32BitsToSingle(unchecked((int)ReadU32(bytes, offset, endian)));

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }

    private static uint ReadU32(Stream stream)
    {
        Span<byte> b = stackalloc byte[4];
        if (stream.Read(b) != 4) throw new EndOfStreamException();
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }
}
