using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4LoadMaterialSource(
    int AssetIndex,
    int SourceOffset,
    int SourceEnd,
    string Name,
    uint SourceTechniquePointer,
    byte[] XboxPayloadTemplate);

public sealed record T4LoadPhysicalSpan(int AssetIndex, int SourceOffset, int SourceEnd);

public sealed record T4LoadTechniqueSetSource(
    int AssetIndex,
    int SourceOffset,
    string Name,
    int NonNullConsoleTechniqueSlots,
    int InlineConsoleTechniqueSlots,
    int PackedConsoleTechniqueSlots,
    int NonNullDroppedPcSlots,
    byte[]? XboxPayload,
    string? BlockReason);

public sealed class T4MaterialTechniqueLoadScan
{
    public required IReadOnlyDictionary<int, T4LoadMaterialSource> MaterialsByIndex { get; init; }
    public required IReadOnlyDictionary<int, T4LoadTechniqueSetSource> TechniqueSetsByIndex { get; init; }
    public required int DeclaredPhysicalMaterials { get; init; }
    public required int DiscoveredMaterialCandidates { get; init; }
    public required int DeclaredPhysicalTechniqueSets { get; init; }
    public required int DiscoveredTechniqueSetCandidates { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyDictionary<int, T4LoadPhysicalSpan> SourcePhysicalSpansByAssetIndex { get; init; }
    public uint? ProvenTechniqueSetTempOffset { get; init; }
    public bool CoordinateProofVerified { get; init; }
}

/// <summary>
/// Alpha 10j fail-closed Material/TechniqueSet serializer for tiny load fastfiles.
///
/// Important scope boundary:
/// - PC T4 Material is 112 bytes; the Xbox fixed body is 96 bytes.
/// - PC has 59 technique slots; Xbox has 51. The eight PC editor/debug slots are
///   discarded ONLY when every one is null.
/// - A TechniqueSet that owns or aliases any of the first 51 MaterialTechnique graphs
///   remains blocked in 10h. PC D3D shader graphs are not Xbox/Xenos shader bytecode and
///   are never copied into a hardware candidate.
/// - Materials may own name/constant/state tables. Texture tables are accepted only when
///   every image/water pointer is null; packed or inline image dependencies remain blocked.
/// - Material.techniqueSet may be null or a packed pointer; the caller must prove/rebase
///   that packed pointer to the exact converted top-level TechniqueSet destination.
/// </summary>
public static class T4MaterialTechniqueLoadSerializer
{
    private const int PcMaterialFixedSize = 112;
    private const int XboxMaterialFixedSize = 96;
    private const int MaterialTextureDefSize = 16;
    private const int MaterialConstantDefSize = 32;
    private const int GfxStateBitsSize = 8;
    private const int PcTechniqueSetFixedSize = 248; // name + 4-byte PC flags/remap area + 59 pointers
    private const int XboxTechniqueSetFixedSize = 216; // name + worldVertFormat + padding + remappedTechniqueSet + 51 pointers
    private const int PcTechniqueCount = 59;
    private const int XboxTechniqueCount = 51;

    public static T4MaterialTechniqueLoadScan Build(
        T4ZoneDocument document,
        byte[] zone,
        CancellationToken cancellationToken = default)
    {
        if (document.Endianness != Endian.Little)
            throw new InvalidDataException("Alpha 10j Material/TechniqueSet conversion expects a PC/little-endian source fastfile.");

        var warnings = new List<string>();
        var payloadStart = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));

        var materialAssets = PhysicalAssets(document, "material");
        var materialCandidates = DiscoverLoadMaterials(zone, document.Header, payloadStart, cancellationToken);

        var materials = new Dictionary<int, T4LoadMaterialSource>();
        if (materialAssets.Count == materialCandidates.Count)
        {
            for (var i = 0; i < materialAssets.Count; i++)
            {
                var asset = materialAssets[i];
                var materialCandidate = materialCandidates[i];
                try
                {
                    var built = BuildMaterialTemplate(asset.Index, zone, materialCandidate, document.Header, payloadStart);
                    materials[asset.Index] = built;
                }
                catch (Exception ex) when (ex is InvalidDataException or OverflowException)
                {
                    warnings.Add($"material XAsset#{asset.Index} '{materialCandidate.Name}' remains blocked: {ex.Message}");
                }
            }
        }
        else if (materialAssets.Count != 0)
        {
            warnings.Add($"material scan mapping incomplete: physical XAsset count={materialAssets.Count:N0}, scan candidate count={materialCandidates.Count:N0}; alpha 10j will retry with corrected TEMP logical-offset resolution.");
        }

        var techAssets = PhysicalAssets(document, "techset");
        var techCandidates = DiscoverTechniqueSets(
            zone, document.Header, payloadStart,
            materialCandidates.Select(x => x.TechniquePointer),
            cancellationToken);
        var techsets = new Dictionary<int, T4LoadTechniqueSetSource>();
        if (techAssets.Count == techCandidates.Count)
        {
            for (var i = 0; i < techAssets.Count; i++)
            {
                var a = techAssets[i];
                var techCandidate = techCandidates[i];
                techsets[a.Index] = new T4LoadTechniqueSetSource(
                    a.Index, techCandidate.Offset, techCandidate.Name,
                    techCandidate.NonNullConsole, techCandidate.InlineConsole, techCandidate.PackedConsole, techCandidate.NonNullDropped,
                    techCandidate.Payload, techCandidate.BlockReason);
                if (techCandidate.BlockReason is not null)
                    warnings.Add($"techset XAsset#{a.Index} '{techCandidate.Name}' remains blocked: {techCandidate.BlockReason}");
            }
        }
        else if (techAssets.Count != 0)
        {
            warnings.Add($"techset scan mapping incomplete: physical XAsset count={techAssets.Count:N0}, scan candidate count={techCandidates.Count:N0}; alpha 10j will retry with corrected TEMP logical-offset resolution.");
        }

        // Alpha 10j recovery path for the tiny load zones exposed by Stage 5j.
        // The serialized file stream and TEMP logical address space are distinct. Recover the
        // authoritative physical stream first, then let unanimous Material.techniqueSet
        // pointers prove the TechniqueSet TEMP coordinate without guessing a file-offset base.
        var sourceSpans = new Dictionary<int, T4LoadPhysicalSpan>();
        uint? provenTechniqueSetTempOffset = null;
        var coordinateProofVerified = false;

        if (materials.Count != materialAssets.Count || techsets.Count != techAssets.Count)
        {
            var recovered = TryRecoverMicroLoadZoneByAssetOrder(
                document, zone, payloadStart,
                out var streamMaterials, out var streamTechsets, out var streamSpans, out var streamNotes);
            warnings.AddRange(streamNotes);
            if (recovered)
            {
                materials = streamMaterials;
                techsets = streamTechsets;
                sourceSpans = streamSpans;
                warnings.RemoveAll(x => x.StartsWith("material scan mapping incomplete:", StringComparison.OrdinalIgnoreCase) ||
                                        x.StartsWith("techset scan mapping incomplete:", StringComparison.OrdinalIgnoreCase));
                for (var i = warnings.Count - streamNotes.Count; i < warnings.Count; i++)
                {
                    if (i >= 0 && !warnings[i].StartsWith("INFO: ", StringComparison.Ordinal))
                        warnings[i] = "INFO: " + warnings[i];
                }
                warnings.Add($"INFO: alpha 10j fallback XAsset-order diagnostic mapped Material={materials.Count:N0}/{materialAssets.Count:N0}, TechniqueSet={techsets.Count:N0}/{techAssets.Count:N0}.");
            }
        }

        if (materials.Count == materialAssets.Count && techsets.Count == techAssets.Count)
        {
            var physical = document.Assets
                .Where(a => a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                .OrderBy(a => a.Index).ToList();
            var soleTech = techsets.Values.SingleOrDefault();
            var decodedTechniquePointers = materials.Values
                .Select(m => T4ZonePointer.Decode(m.SourceTechniquePointer))
                .ToList();

            foreach (var tech in techsets.Values.OrderBy(x => x.AssetIndex))
                warnings.Add($"INFO: alpha 10j TechniqueSet XAsset#{tech.AssetIndex} '{tech.Name}' sourceFile=0x{tech.SourceOffset:X8}.");
            foreach (var material in materials.Values.OrderBy(x => x.AssetIndex))
                warnings.Add($"INFO: alpha 10j Material XAsset#{material.AssetIndex} '{material.Name}' sourceFile=0x{material.SourceOffset:X8} techniquePtr={T4ZonePointer.Decode(material.SourceTechniquePointer)}.");

            if (soleTech is not null && decodedTechniquePointers.Count != 0 &&
                decodedTechniquePointers.All(d => d.Kind == T4ZonePointerKind.Offset && d.Block == T4ZoneBlock.Temp) &&
                decodedTechniquePointers.Select(d => d.Offset).Distinct().Count() == 1 &&
                physical.Count != 0 && physical[0].Index == soleTech.AssetIndex &&
                soleTech.SourceOffset >= payloadStart &&
                sourceSpans.TryGetValue(soleTech.AssetIndex, out var techSpan))
            {
                var consensus = decodedTechniquePointers[0].Offset;
                var leadingSerializedPad = soleTech.SourceOffset - payloadStart;
                var paddingOk = leadingSerializedPad >= 0 && Enumerable.Range(payloadStart, leadingSerializedPad).All(i => zone[i] == 0);
                if (paddingOk && consensus < document.Header.TempBlockSize)
                {
                    provenTechniqueSetTempOffset = consensus;
                    coordinateProofVerified = true;
                    warnings.Add($"INFO: alpha 10j coordinate proof: all {materials.Count:N0} Material.techniqueSet pointers unanimously target TEMP+0x{consensus:X8}; sole TechniqueSet XAsset#{soleTech.AssetIndex} begins at serialized file 0x{techSpan.SourceOffset:X8} (payloadStart=0x{payloadStart:X8}, leadingPad={leadingSerializedPad:N0}).");
                }
            }

            if (!coordinateProofVerified)
                warnings.Add("coordinate proof incomplete: Material.techniqueSet pointers did not unanimously prove the sole first physical TechniqueSet TEMP offset");
        }

        return new T4MaterialTechniqueLoadScan
        {
            MaterialsByIndex = materials,
            TechniqueSetsByIndex = techsets,
            DeclaredPhysicalMaterials = materialAssets.Count,
            DiscoveredMaterialCandidates = materialCandidates.Count,
            DeclaredPhysicalTechniqueSets = techAssets.Count,
            DiscoveredTechniqueSetCandidates = techCandidates.Count,
            Warnings = warnings,
            SourcePhysicalSpansByAssetIndex = sourceSpans,
            ProvenTechniqueSetTempOffset = provenTechniqueSetTempOffset,
            CoordinateProofVerified = coordinateProofVerified
        };
    }

    public static byte[] PatchMaterialTechniquePointer(byte[] template, uint destinationRaw)
    {
        if (template.Length < XboxMaterialFixedSize)
            throw new InvalidDataException("Xbox Material payload is shorter than its 96-byte fixed body.");
        var output = template.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(80, 4), destinationRaw);
        return output;
    }

    private sealed record LoadMaterialCandidate(
        int Offset, int End, string Name, uint TechniquePointer);

    private static T4LoadMaterialSource BuildMaterialTemplate(
        int assetIndex,
        byte[] zone,
        LoadMaterialCandidate candidate,
        ZoneHeaderSummary header,
        int payloadStart)
    {
        var start = candidate.Offset;
        Ensure(zone, start, PcMaterialFixedSize, "PC Material fixed body");

        var namePtr = ReadU32(zone, start + 0);
        var techniquePtr = ReadU32(zone, start + 96);
        var texturePtr = ReadU32(zone, start + 100);
        var constantPtr = ReadU32(zone, start + 104);
        var statePtr = ReadU32(zone, start + 108);
        var textureCount = zone[start + 91];
        var constantCount = zone[start + 92];
        var stateCount = zone[start + 93];

        RequirePointer(namePtr, header, "Material.name");
        RequirePointer(techniquePtr, header, "Material.techniqueSet");
        RequirePointer(texturePtr, header, "Material.textureTable");
        RequirePointer(constantPtr, header, "Material.constantTable");
        RequirePointer(statePtr, header, "Material.stateBitsTable");

        if (!TryResolveAssetName(zone, namePtr, start + PcMaterialFixedSize, payloadStart, header, out _, out _))
            throw new InvalidDataException($"top-level Material name pointer 0x{namePtr:X8} cannot be resolved safely");
        if (techniquePtr is T4ZonePointer.Following or T4ZonePointer.Insert)
            throw new InvalidDataException("Material owns an inline TechniqueSet graph; alpha 10j will not copy PC shader graphs to Xbox");
        if (texturePtr != 0 && texturePtr is not (T4ZonePointer.Following or T4ZonePointer.Insert))
            throw new InvalidDataException("Material uses a packed/shared texture table that alpha 10j cannot materialize safely");
        if (constantPtr != 0 && constantPtr is not (T4ZonePointer.Following or T4ZonePointer.Insert))
            throw new InvalidDataException("Material uses a packed/shared constant table that alpha 10j cannot materialize safely");
        if (statePtr != 0 && statePtr is not (T4ZonePointer.Following or T4ZonePointer.Insert))
            throw new InvalidDataException("Material uses a packed/shared state-bits table that alpha 10j cannot materialize safely");

        using var ms = new MemoryStream();
        var fixedOut = new byte[XboxMaterialFixedSize];
        BinaryPrimitives.WriteUInt32BigEndian(fixedOut.AsSpan(0, 4), T4ZonePointer.Following);
        zone.AsSpan(start + 4, 4).CopyTo(fixedOut.AsSpan(4, 4));
        WriteU64Be(fixedOut, 8, ReadU64(zone, start + 8));
        BinaryPrimitives.WriteUInt32BigEndian(fixedOut.AsSpan(16, 4), ReadU32(zone, start + 16));
        // Xbox omits PC layeredSurfaceTypes/hashIndex; bytes 20..23 are alignment padding.
        fixedOut.AsSpan(20, 4).Clear();
        zone.AsSpan(start + 32, XboxTechniqueCount).CopyTo(fixedOut.AsSpan(24, XboxTechniqueCount));
        fixedOut[75] = textureCount;
        fixedOut[76] = constantCount;
        fixedOut[77] = stateCount;
        fixedOut[78] = zone[start + 94]; // stateFlags
        fixedOut[79] = zone[start + 95]; // cameraRegion
        // technique pointer patched by caller after destination layout is proven.
        BinaryPrimitives.WriteUInt32BigEndian(fixedOut.AsSpan(80, 4), techniquePtr == 0 ? 0u : 0xCDCDCDCDu);
        BinaryPrimitives.WriteUInt32BigEndian(fixedOut.AsSpan(84, 4), texturePtr);
        BinaryPrimitives.WriteUInt32BigEndian(fixedOut.AsSpan(88, 4), constantPtr);
        BinaryPrimitives.WriteUInt32BigEndian(fixedOut.AsSpan(92, 4), statePtr);
        ms.Write(fixedOut);

        var cursor = start + PcMaterialFixedSize;
        string name;
        if (namePtr is T4ZonePointer.Following or T4ZonePointer.Insert)
            name = ReadAsciiZ(zone, ref cursor, 1024, "Material.name");
        else if (!TryResolvePackedTempString(zone, namePtr, payloadStart, header, out name))
            throw new InvalidDataException($"Material packed name 0x{namePtr:X8} could not be resolved from TEMP");
        if (!string.Equals(name, candidate.Name, StringComparison.Ordinal))
            throw new InvalidDataException($"Material name changed while measuring: '{candidate.Name}' vs '{name}'");
        WriteAsciiZ(ms, name);

        if (texturePtr != 0)
        {
            var bytes = checked(textureCount * MaterialTextureDefSize);
            Ensure(zone, cursor, bytes, "Material.textureTable");
            for (var i = 0; i < textureCount; i++)
            {
                var p = cursor + i * MaterialTextureDefSize;
                var imagePtr = ReadU32(zone, p + 12);
                RequirePointer(imagePtr, header, $"Material.texture[{i}].image");
                if (imagePtr != 0)
                    throw new InvalidDataException($"Material texture[{i}] references an image/water payload; alpha 10j load-zone subset requires null texture targets");

                Span<byte> entry = stackalloc byte[MaterialTextureDefSize];
                zone.AsSpan(p, MaterialTextureDefSize).CopyTo(entry);
                BinaryPrimitives.WriteUInt32BigEndian(entry.Slice(0, 4), ReadU32(zone, p));
                BinaryPrimitives.WriteUInt32BigEndian(entry.Slice(12, 4), imagePtr);
                ms.Write(entry);
            }
            cursor += bytes;
        }

        if (constantPtr != 0)
        {
            var bytes = checked(constantCount * MaterialConstantDefSize);
            Ensure(zone, cursor, bytes, "Material.constantTable");
            for (var i = 0; i < constantCount; i++)
            {
                var p = cursor + i * MaterialConstantDefSize;
                var entry = zone.AsSpan(p, MaterialConstantDefSize).ToArray();
                BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(0, 4), ReadU32(zone, p));
                for (var q = 16; q < 32; q += 4)
                    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(q, 4), ReadU32(zone, p + q));
                ms.Write(entry);
            }
            cursor += bytes;
        }

        if (statePtr != 0)
        {
            var bytes = checked(stateCount * GfxStateBitsSize);
            Ensure(zone, cursor, bytes, "Material.stateBitsTable");
            for (var p = cursor; p < cursor + bytes; p += 4)
            {
                Span<byte> b = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(b, ReadU32(zone, p));
                ms.Write(b);
            }
            cursor += bytes;
        }

        if (cursor != candidate.End)
            throw new InvalidDataException($"Material payload ended at 0x{cursor:X8}, conservative Stage-4 measurement ended at 0x{candidate.End:X8}");

        return new T4LoadMaterialSource(assetIndex, start, cursor, name, techniquePtr, ms.ToArray());
    }

    private sealed record TechCandidate(
        int Offset, int End, string Name, int NonNullConsole, int InlineConsole, int PackedConsole,
        int NonNullDropped, byte[]? Payload, string? BlockReason);

    private sealed record SourceRawFileBody(int Offset, int End, string Name, int Length);

    private static bool TryRecoverMicroLoadZoneByAssetOrder(
        T4ZoneDocument document,
        byte[] zone,
        int payloadStart,
        out Dictionary<int, T4LoadMaterialSource> materials,
        out Dictionary<int, T4LoadTechniqueSetSource> techsets,
        out Dictionary<int, T4LoadPhysicalSpan> spans,
        out List<string> notes)
    {
        materials = new Dictionary<int, T4LoadMaterialSource>();
        techsets = new Dictionary<int, T4LoadTechniqueSetSource>();
        spans = new Dictionary<int, T4LoadPhysicalSpan>();
        notes = new List<string>();

        var physical = document.Assets
            .Where(a => a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .OrderBy(a => a.Index)
            .ToList();
        if (physical.Count == 0 || physical.Count > 16) return false;
        if (document.Assets.Any(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias)) return false;
        if (physical.Any(a => !a.TypeName.Equals("rawfile", StringComparison.OrdinalIgnoreCase) &&
                              !a.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase) &&
                              !a.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase)))
            return false;

        // This recovery is intentionally limited to the Stage-5j micro-load shape.  A larger
        // or mixed fastfile goes through the normal scanners and remains fail-closed.
        var materialCount = physical.Count(a => a.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase));
        var techCount = physical.Count(a => a.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase));
        var rawCount = physical.Count(a => a.TypeName.Equals("rawfile", StringComparison.OrdinalIgnoreCase));
        if (materialCount == 0 || techCount == 0 || rawCount == 0) return false;

        var cursor = payloadStart;
        try
        {
            foreach (var asset in physical)
            {
                if (asset.TypeName.Equals("rawfile", StringComparison.OrdinalIgnoreCase))
                {
                    var rawMatches = new List<SourceRawFileBody>();
                    foreach (var start in CandidateTopLevelStarts(zone, cursor))
                    {
                        try { rawMatches.Add(ParseRawFileAt(zone, start, document.Header, payloadStart)); }
                        catch (Exception ex) when (ex is InvalidDataException or OverflowException) { }
                    }
                    rawMatches = rawMatches.GroupBy(x => x.Offset).Select(g => g.First()).ToList();
                    if (rawMatches.Count != 1)
                        throw new InvalidDataException($"XAsset#{asset.Index} RawFile stream start is {(rawMatches.Count == 0 ? "unresolved" : "ambiguous")} from cursor 0x{cursor:X8}");
                    var raw = rawMatches[0];
                    spans[asset.Index] = new T4LoadPhysicalSpan(asset.Index, raw.Offset, raw.End);
                    notes.Add($"alpha 10j stream XAsset#{asset.Index} rawfile '{raw.Name}' source=0x{raw.Offset:X8}..0x{raw.End:X8} len={raw.Length:N0}");
                    cursor = raw.End;
                    continue;
                }

                if (asset.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase))
                {
                    var candidates = new List<LoadMaterialCandidate>();
                    var rejects = new List<string>();
                    foreach (var start in CandidateTopLevelStarts(zone, cursor))
                    {
                        if (TryBuildMaterialCandidateAt(zone, document.Header, payloadStart, start, out var candidate, out var reject))
                            candidates.Add(candidate!);
                        else
                            rejects.Add($"0x{start:X8}: {reject}");
                    }
                    candidates = candidates.GroupBy(x => x.Offset).Select(g => g.First()).ToList();
                    if (candidates.Count != 1)
                        throw new InvalidDataException($"XAsset#{asset.Index} Material stream start is {(candidates.Count == 0 ? "unresolved" : "ambiguous")} from cursor 0x{cursor:X8}; {string.Join(" | ", rejects)}");
                    var built = BuildMaterialTemplate(asset.Index, zone, candidates[0], document.Header, payloadStart);
                    materials[asset.Index] = built;
                    spans[asset.Index] = new T4LoadPhysicalSpan(asset.Index, built.SourceOffset, built.SourceEnd);
                    notes.Add($"alpha 10j stream XAsset#{asset.Index} material '{built.Name}' source=0x{built.SourceOffset:X8}..0x{built.SourceEnd:X8} tech=0x{built.SourceTechniquePointer:X8}");
                    cursor = built.SourceEnd;
                    continue;
                }

                if (asset.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase))
                {
                    var candidates = new List<TechCandidate>();
                    foreach (var start in CandidateTopLevelStarts(zone, cursor))
                        if (TryBuildTechniqueSetCandidate(zone, document.Header, payloadStart, start, out var candidateTech, validateReusablePointers: false)) candidates.Add(candidateTech!);
                    candidates = candidates.GroupBy(x => x.Offset).Select(g => g.First()).ToList();
                    if (candidates.Count != 1)
                    {
                        var diag = cursor <= zone.Length - PcTechniqueSetFixedSize
                            ? $" rawName=0x{ReadU32(zone, cursor):X8} worldVert=0x{zone[cursor + 4]:X2} flags=0x{zone[cursor + 5]:X2}{zone[cursor + 6]:X2}{zone[cursor + 7]:X2} remap=0x{ReadU32(zone, cursor + 8):X8}"
                            : string.Empty;
                        throw new InvalidDataException($"XAsset#{asset.Index} TechniqueSet stream start is {(candidates.Count == 0 ? "unresolved" : "ambiguous")} from cursor 0x{cursor:X8};{diag}");
                    }
                    var selectedTech = candidates[0];
                    techsets[asset.Index] = new T4LoadTechniqueSetSource(
                        asset.Index, selectedTech.Offset, selectedTech.Name,
                        selectedTech.NonNullConsole, selectedTech.InlineConsole, selectedTech.PackedConsole, selectedTech.NonNullDropped,
                        selectedTech.Payload, selectedTech.BlockReason);
                    spans[asset.Index] = new T4LoadPhysicalSpan(asset.Index, selectedTech.Offset, selectedTech.End);
                    notes.Add($"alpha 10j stream XAsset#{asset.Index} techset '{selectedTech.Name}' source=0x{selectedTech.Offset:X8}..0x{selectedTech.End:X8} consoleGraphs={selectedTech.NonNullConsole:N0} droppedPc={selectedTech.NonNullDropped:N0}");
                    cursor = selectedTech.End;
                    if (selectedTech.NonNullConsole != 0 && asset != physical[^1])
                        throw new InvalidDataException("TechniqueSet owns/references a graph before the end of the micro-load stream; exact child traversal is required before later assets can be located");
                    continue;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException)
        {
            notes.Add("alpha 10j fallback XAsset-order diagnostic refused: " + ex.Message);
            materials.Clear();
            techsets.Clear();
            spans.Clear();
            return false;
        }

        // Success means every declared physical Material/TechniqueSet was located by the
        // authoritative stream order, not by a global signature scan.
        return materials.Count == materialCount && techsets.Count == techCount;
    }

    private static IReadOnlyList<int> CandidateTopLevelStarts(byte[] zone, int cursor)
    {
        var starts = new List<int> { cursor };
        foreach (var alignment in new[] { 4, 8, 16 })
        {
            var aligned = checked((cursor + alignment - 1) & ~(alignment - 1));
            if (aligned == cursor || aligned > zone.Length) continue;
            var zeroPadding = true;
            for (var p = cursor; p < aligned; p++)
                if (zone[p] != 0) { zeroPadding = false; break; }
            if (zeroPadding) starts.Add(aligned);
        }
        return starts.Distinct().OrderBy(x => x).ToList();
    }

    private static SourceRawFileBody ParseRawFileAt(
        byte[] zone, int start, ZoneHeaderSummary header, int payloadStart)
    {
        Ensure(zone, start, 12, "RawFile fixed body");
        var namePtr = ReadU32(zone, start + 0);
        var length = ReadU32(zone, start + 4);
        var bufferPtr = ReadU32(zone, start + 8);
        RequirePointer(namePtr, header, "RawFile.name");
        RequirePointer(bufferPtr, header, "RawFile.buffer");
        if (length > 64u * 1024u * 1024u) throw new InvalidDataException($"RawFile length {length:N0} is implausible");

        var cursor = start + 12;
        string name;
        if (namePtr is T4ZonePointer.Following or T4ZonePointer.Insert)
            name = ReadAsciiZ(zone, ref cursor, 1024, "RawFile.name");
        else if (!TryResolvePackedTempString(zone, namePtr, payloadStart, header, out name))
            throw new InvalidDataException($"RawFile packed name 0x{namePtr:X8} cannot be resolved from TEMP");
        if (!LooksLikeName(name)) throw new InvalidDataException($"RawFile name '{name}' is not a plausible asset name");

        if (bufferPtr == 0)
        {
            if (length != 0) throw new InvalidDataException("RawFile has null buffer with non-zero length");
        }
        else if (bufferPtr is T4ZonePointer.Following or T4ZonePointer.Insert)
        {
            var owned = checked((int)length + 1);
            Ensure(zone, cursor, owned, "RawFile.buffer");
            cursor += owned;
        }
        else
        {
            throw new InvalidDataException($"RawFile uses packed/shared buffer 0x{bufferPtr:X8}; micro-load stream recovery requires an owned or null buffer");
        }

        return new SourceRawFileBody(start, cursor, name, checked((int)length));
    }

    private static bool TryBuildMaterialCandidateAt(
        byte[] zone,
        ZoneHeaderSummary header,
        int payloadStart,
        int offset,
        out LoadMaterialCandidate? candidate,
        out string reject)
    {
        candidate = null;
        reject = string.Empty;
        try
        {
            Ensure(zone, offset, PcMaterialFixedSize, "PC Material fixed body");
            var namePtr = ReadU32(zone, offset + 0);
            var technique = ReadU32(zone, offset + 96);
            var textures = ReadU32(zone, offset + 100);
            var constants = ReadU32(zone, offset + 104);
            var states = ReadU32(zone, offset + 108);
            if (!PointerValid(namePtr, header) || !PointerValid(technique, header) ||
                !PointerValid(textures, header) || !PointerValid(constants, header) || !PointerValid(states, header))
            {
                reject = "one or more Material pointers are invalid";
                return false;
            }
            var textureCount = zone[offset + 91];
            var constantCount = zone[offset + 92];
            var stateCount = zone[offset + 93];
            if (textureCount > 64 || constantCount > 64 || stateCount > 64)
            {
                reject = $"Material counts are implausible tex={textureCount} const={constantCount} state={stateCount}";
                return false;
            }
            if (!TryResolveAssetName(zone, namePtr, offset + PcMaterialFixedSize, payloadStart, header, out var name, out _))
            {
                reject = $"Material name pointer 0x{namePtr:X8} cannot be resolved";
                return false;
            }
            var measuredEnd = T4XModelSerializerProof.MeasureInlineMaterialEnd(zone, offset, Endian.Little, header);
            if (measuredEnd < offset + PcMaterialFixedSize || measuredEnd > zone.Length)
            {
                reject = $"Material measured end 0x{measuredEnd:X8} is outside the source zone";
                return false;
            }
            candidate = new LoadMaterialCandidate(offset, measuredEnd, name, technique);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException)
        {
            reject = ex.Message;
            return false;
        }
    }

    private static List<LoadMaterialCandidate> DiscoverLoadMaterials(
        byte[] zone, ZoneHeaderSummary header, int payloadStart, CancellationToken ct)
    {
        var result = new List<LoadMaterialCandidate>();
        for (var offset = payloadStart; offset <= zone.Length - PcMaterialFixedSize; offset++)
        {
            if ((offset & 0x7FFFF) == 0) ct.ThrowIfCancellationRequested();
            if (!TryBuildMaterialCandidateAt(zone, header, payloadStart, offset, out var candidate, out _)) continue;
            result.Add(candidate!);
            offset = Math.Max(offset, candidate!.End - 1);
        }
        return result.GroupBy(x => x.Offset).Select(g => g.OrderByDescending(x => x.End).First()).OrderBy(x => x.Offset).ToList();
    }

    private static List<TechCandidate> DiscoverTechniqueSets(
        byte[] zone, ZoneHeaderSummary header, int payloadStart, IEnumerable<uint> materialTechniquePointers, CancellationToken ct)
    {
        var anchoredOffsets = new HashSet<int>();
        foreach (var raw in materialTechniquePointers)
        {
            if (raw == 0 || raw is T4ZonePointer.Following or T4ZonePointer.Insert) continue;
            var d = T4ZonePointer.Decode(raw);
            if (d.Kind != T4ZonePointerKind.Offset || d.Block != T4ZoneBlock.Temp || d.Offset > int.MaxValue) continue;
            var absolute = FastFileConstants.T4XFileHeaderSize + (int)d.Offset;
            if (absolute >= payloadStart && absolute <= zone.Length - PcTechniqueSetFixedSize) anchoredOffsets.Add(absolute);
        }
        var anchored = new List<TechCandidate>();
        foreach (var offset in anchoredOffsets.OrderBy(x => x))
        {
            ct.ThrowIfCancellationRequested();
            if (TryBuildTechniqueSetCandidate(zone, header, payloadStart, offset, out var anchoredTech)) anchored.Add(anchoredTech!);
        }
        if (anchored.Count != 0) return anchored.OrderBy(x => x.Offset).ToList();

        var result = new List<TechCandidate>();
        for (var offset = payloadStart; offset <= zone.Length - PcTechniqueSetFixedSize; offset++)
        {
            if ((offset & 0x7FFFF) == 0) ct.ThrowIfCancellationRequested();
            if (TryBuildTechniqueSetCandidate(zone, header, payloadStart, offset, out var scannedTech)) result.Add(scannedTech!);
        }
        return result.GroupBy(x => x.Offset).Select(g => g.First()).OrderBy(x => x.Offset).ToList();
    }

    private static bool TryBuildTechniqueSetCandidate(
        byte[] zone, ZoneHeaderSummary header, int payloadStart, int offset, out TechCandidate? candidate, bool validateReusablePointers = true)
    {
        candidate = null;
        if (offset < payloadStart || offset > zone.Length - PcTechniqueSetFixedSize) return false;
        var namePtr = ReadU32(zone, offset);
        if (!PointerValid(namePtr, header)) return false;
        var worldVert = zone[offset + 4];
        if (worldVert > 0x0B) return false;
        var remapped = ReadU32(zone, offset + 8);
        if (validateReusablePointers && !PointerValid(remapped, header)) return false;
        var ptrs = new uint[PcTechniqueCount];
        for (var i = 0; i < ptrs.Length; i++)
        {
            ptrs[i] = ReadU32(zone, offset + 12 + i * 4);
            if (validateReusablePointers && !PointerValid(ptrs[i], header)) return false;
        }
        if (!TryResolveAssetName(zone, namePtr, offset + PcTechniqueSetFixedSize, payloadStart, header, out var name, out var nameWasPacked)) return false;
        if (!LooksLikeName(name)) return false;
        var end = offset + PcTechniqueSetFixedSize;
        if (!nameWasPacked && namePtr is T4ZonePointer.Following or T4ZonePointer.Insert)
        {
            var nameCursor = end;
            try { _ = ReadAsciiZ(zone, ref nameCursor, 1024, "TechniqueSet.name"); }
            catch (InvalidDataException) { return false; }
            end = nameCursor;
        }
        var console = ptrs.Take(XboxTechniqueCount).ToArray();
        var dropped = ptrs.Skip(XboxTechniqueCount).ToArray();
        var nonNullConsole = console.Count(x => x != 0);
        var inlineConsole = console.Count(x => x is T4ZonePointer.Following or T4ZonePointer.Insert);
        var packedConsole = console.Count(x => x != 0 && x is not (T4ZonePointer.Following or T4ZonePointer.Insert));
        var nonNullDropped = dropped.Count(x => x != 0);
        string? reason = null;
        if (remapped != 0) reason = $"PC remappedTechniqueSet is non-null (0x{remapped:X8})";
        else if (nonNullDropped != 0) reason = $"{nonNullDropped} PC-only technique slot(s) 51..58 are non-null";
        else if (nonNullConsole != 0) reason = $"TechniqueSet owns/references {nonNullConsole} console technique graph(s) (inline={inlineConsole}, packed={packedConsole}); shader/pass conversion or Xbox donor resolution is required";
        byte[]? payload = null;
        if (reason is null)
        {
            payload = new byte[XboxTechniqueSetFixedSize + Encoding.ASCII.GetByteCount(name) + 1];
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), T4ZonePointer.Following);
            payload[4] = worldVert;
            Encoding.ASCII.GetBytes(name, payload.AsSpan(XboxTechniqueSetFixedSize));
            payload[^1] = 0;
        }
        candidate = new TechCandidate(offset, end, name, nonNullConsole, inlineConsole, packedConsole, nonNullDropped, payload, reason);
        return true;
    }

    private static bool TryResolveAssetName(
        byte[] zone, uint raw, int inlineOffset, int payloadStart, ZoneHeaderSummary header, out string name, out bool wasPacked)
    {
        name = string.Empty;
        wasPacked = false;
        if (raw is T4ZonePointer.Following or T4ZonePointer.Insert)
        {
            var cursor = inlineOffset;
            try { name = ReadAsciiZ(zone, ref cursor, 1024, "asset.name"); return LooksLikeName(name); }
            catch (InvalidDataException) { return false; }
        }
        if (raw == 0 || !TryResolvePackedTempString(zone, raw, payloadStart, header, out name)) return false;
        wasPacked = true;
        return LooksLikeName(name);
    }

    private static bool TryResolvePackedTempString(
        byte[] zone, uint raw, int payloadStart, ZoneHeaderSummary header, out string name)
    {
        name = string.Empty;
        if (!PointerValid(raw, header)) return false;
        var d = T4ZonePointer.Decode(raw);
        if (d.Kind != T4ZonePointerKind.Offset || d.Block != T4ZoneBlock.Temp || d.Offset > int.MaxValue) return false;
        // XFILE_BLOCK_TEMP logical offset 0 starts at the first byte after the 36-byte
        // XFile header (the XAssetList), not at payloadStart.
        var absolute = FastFileConstants.T4XFileHeaderSize + (int)d.Offset;
        if (absolute < FastFileConstants.T4XFileHeaderSize || absolute >= zone.Length) return false;
        var cursor = absolute;
        try { name = ReadAsciiZ(zone, ref cursor, 1024, "packed TEMP asset.name"); return true; }
        catch (InvalidDataException) { return false; }
    }

    private static List<T4AssetEntry> PhysicalAssets(T4ZoneDocument document, string type) =>
        document.Assets
            .Where(a => a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                        a.ReferenceKind is (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
            .OrderBy(a => a.Index)
            .ToList();

    private static bool PointerValid(uint raw, ZoneHeaderSummary header)
    {
        if (raw == 0 || raw is T4ZonePointer.Following or T4ZonePointer.Insert) return true;
        return T4ZonePointer.IsWithinBlock(raw, header);
    }

    private static void RequirePointer(uint raw, ZoneHeaderSummary header, string label)
    {
        if (!PointerValid(raw, header))
            throw new InvalidDataException($"{label} has invalid packed pointer 0x{raw:X8}");
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        Ensure(data, offset, 4, "uint32");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private static ulong ReadU64(byte[] data, int offset)
    {
        Ensure(data, offset, 8, "uint64");
        return BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));
    }

    private static void WriteU64Be(byte[] data, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset, 8), value);

    private static string ReadAsciiZ(byte[] data, ref int cursor, int max, string label)
    {
        if (cursor < 0 || cursor >= data.Length) throw new InvalidDataException($"{label} starts outside zone");
        var end = cursor;
        var limit = Math.Min(data.Length, cursor + max);
        while (end < limit && data[end] != 0)
        {
            if (data[end] < 0x20 || data[end] > 0x7E) throw new InvalidDataException($"{label} contains non-ASCII byte 0x{data[end]:X2}");
            end++;
        }
        if (end >= limit || data[end] != 0) throw new InvalidDataException($"{label} is unterminated");
        var s = Encoding.ASCII.GetString(data, cursor, end - cursor);
        cursor = end + 1;
        return s;
    }

    private static void WriteAsciiZ(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
        stream.WriteByte(0);
    }

    private static bool LooksLikeName(string value) =>
        value.Length is > 0 and <= 260 && value.Any(char.IsLetterOrDigit) &&
        value.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or '/' or '\\' or '$' or '@' or '+' or '#');

    private static void Ensure(byte[] data, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException($"{label} at 0x{offset:X} ({length:N0} bytes) exceeds zone length {data.Length:N0}");
    }
}
