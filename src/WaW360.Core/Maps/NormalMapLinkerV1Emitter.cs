using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;
using WaW360.Core.Linker;

namespace WaW360.Core.Maps;

public sealed record NormalMapLinkerV1EmitFastFileResult(
    string SourceFastFile,
    string OutputZone,
    string OutputFastFile,
    int PcRoots,
    int XboxAssets,
    int NonRuntimeBytes,
    int LargeRuntimeBytes,
    int OutputBytes,
    bool CustomIwiConverted,
    bool DefeatSmokeSubstituteUsed,
    bool GraphOrderVerified,
    bool ZoneReadBackVerified,
    bool WrapperRoundTripVerified,
    bool HardwareReady,
    string SourceSha256,
    string ZoneSha256,
    string FastFileSha256,
    IReadOnlyList<string> OutputOrder,
    IReadOnlyList<string> Notes);

public sealed class NormalMapLinkerV1EmitResult
{
    public required string MapName { get; init; }
    public required int LoadFastFiles { get; init; }
    public required int EmittedFastFiles { get; init; }
    public required int HardwareReadyFastFiles { get; init; }
    public required int SmokeSubstitutes { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<NormalMapLinkerV1EmitFastFileResult> FastFiles { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V1 alpha 2: first deterministic Xbox load.ff emitter.
///
/// Scope is deliberately narrow and fail-closed: the exact 5-root -> 7-node load graph
/// proven by alpha 1 (TechniqueSet, 3 Materials, 2 promoted Images, RawFile). The custom
/// load-screen IWI is really decoded/resized/BC1-encoded/Xenos-tiled. The stock `defeat`
/// image remains a donor obligation; until a verified Xbox donor resolver is connected,
/// alpha 2 emits a clearly-labelled structural smoke substitute and NEVER marks that FF
/// hardware-ready/final.
///
/// No structure-shaped candidate scan is used anywhere in this stage.
/// </summary>
public static class NormalMapLinkerV1Emitter
{
    private const int PcMaterialSize = 112;
    private const int XboxMaterialSize = 96;
    private const int TextureDefSize = 16;
    private const int ConstantDefSize = 32;
    private const int StateBitsSize = 8;
    private const int XboxTechniqueSetSize = 216;

    // These packed references are proven for the 7-node load graph and are independent
    // of string lengths: the graph/layout is fixed (techset, material, image, material,
    // image, material, rawfile). A generalized relocation allocator replaces these in
    // the next linker expansion.
    private const uint XboxTechniqueReference = 0x8000003Du;
    private const uint XboxFirstImageReference = 0x8000005Du;
    private const uint XboxSecondImageReference = 0x800000CDu;

    private sealed record PcMaterialEmissionData(
        string Name,
        byte[] Fixed,
        byte TextureCount,
        byte ConstantCount,
        byte StateCount,
        IReadOnlyList<byte[]> TextureDefs,
        IReadOnlyList<byte[]> Constants,
        IReadOnlyList<byte[]> StateBits,
        IReadOnlyList<string> ImageDependencies);

    public static async Task<NormalMapLinkerV1EmitResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        var loadFiles = analysis.InputFiles
            .Where(f => f.Role.Equals("load.ff", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var outputDir = Path.Combine(outputRoot, map.Name, "linker_v1_alpha2");
        Directory.CreateDirectory(outputDir);
        var textPath = Path.Combine(outputDir, $"{map.Name}_linker_v1_alpha2_emitter.txt");
        var jsonPath = Path.Combine(outputDir, $"{map.Name}_linker_v1_alpha2_emitter.json");

        var rows = new List<NormalMapLinkerV1EmitFastFileResult>();
        var warnings = new List<string>();

        foreach (var input in loadFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                rows.Add(await EmitOneAsync(analysis, input.Path, outputDir, cancellationToken));
            }
            catch (InvalidDataException ex) when (
                    ex.Message.Contains("expects exactly one custom IWD image and one stock donor image", StringComparison.OrdinalIgnoreCase))
            {
                // Compatibility skip, not a conversion success: this legacy load emitter has a
                // deliberately narrow proof contract. Keep the deterministic graph result, skip
                // only this incompatible load FF, and allow the rest of the map suite to continue.
                warnings.Add($"{Path.GetFileName(input.Path)}: compatibility-skip: {ex.Message}");
            }
        }

        if (loadFiles.Count == 0)
            warnings.Add("No load.ff was discovered; alpha-2 load emitter is a no-op for this map.");

        var final = new NormalMapLinkerV1EmitResult
        {
            MapName = map.Name,
            LoadFastFiles = loadFiles.Count,
            EmittedFastFiles = rows.Count,
            HardwareReadyFastFiles = rows.Count(r => r.HardwareReady),
            SmokeSubstitutes = rows.Count(r => r.DefeatSmokeSubstituteUsed),
            Verified = rows.All(r => r.GraphOrderVerified && r.ZoneReadBackVerified && r.WrapperRoundTripVerified),
            FastFiles = rows,
            Warnings = warnings,
            TextReportFile = textPath,
            JsonReportFile = jsonPath
        };

        await File.WriteAllTextAsync(textPath, RenderText(final), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(jsonPath,
            JsonSerializer.Serialize(final, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8, cancellationToken);
        return final;
    }

    private static async Task<NormalMapLinkerV1EmitFastFileResult> EmitOneAsync(
        NormalMapAnalysisResult analysis,
        string sourceFastFile,
        string outputDir,
        CancellationToken cancellationToken)
    {
        var graph = await T4LoadFastFileLinker.BuildAsync(sourceFastFile, cancellationToken);
        if (!graph.Verified || !graph.ExactSourceWalk || !graph.TechniqueBindingProven)
            throw new InvalidDataException("Alpha-2 emitter requires a fully verified deterministic alpha-1 load graph.");

        var expectedTypes = new[] { "techset", "material", "image", "material", "image", "material", "rawfile" };
        if (graph.OutputOrder.Count != expectedTypes.Length ||
            graph.OutputOrder.Where((x, i) => !x.TypeName.Equals(expectedTypes[i], StringComparison.OrdinalIgnoreCase)).Any())
            throw new InvalidDataException(
                "Alpha-2 emitter currently requires the proven 7-node load closure: techset, material, image, material, image, material, rawfile.");

        var cached = await FastFileAnalysisCache.GetZoneAsync(sourceFastFile, cancellationToken);
        var pcZone = cached.Bytes;
        var nodeMap = graph.Nodes.ToDictionary(n => Key(n.TypeName, n.Name), StringComparer.OrdinalIgnoreCase);

        var tech = graph.OutputOrder[0];
        if (!tech.Name.Equals(",2d", StringComparison.Ordinal))
            throw new InvalidDataException($"Alpha-2 TechniqueSet serializer is proven only for ',2d'; graph contains '{tech.Name}'.");
        var techNode = nodeMap[Key("techset", tech.Name)];
        if (!techNode.Metadata.TryGetValue("inlineTechniqueGraphs", out var inlineTech) || inlineTech != "0" ||
            !techNode.Metadata.TryGetValue("packedTechniqueGraphs", out var packedTech) || packedTech != "0" ||
            !techNode.Metadata.TryGetValue("remappedTechniqueSet", out var remappedTech) || !remappedTech.Equals("0x00000000", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Alpha-2 TechniqueSet emission requires the proven null-graph/unremapped ',2d' source shape.");

        var material0 = graph.OutputOrder[1];
        var image0 = graph.OutputOrder[2];
        var material1 = graph.OutputOrder[3];
        var image1 = graph.OutputOrder[4];
        var material2 = graph.OutputOrder[5];
        var raw = graph.OutputOrder[6];

        var customIwiIndex = ReadIwdImages(analysis.InputFiles
            .Where(f => f.Role.Equals("iwd", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Path));

        var imageSources = new Dictionary<string, (string Archive, string Entry)>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in new[] { image0, image1 })
            if (customIwiIndex.TryGetValue(image.Name, out var source)) imageSources[image.Name] = source;

        if (imageSources.Count != 1)
            throw new InvalidDataException($"Alpha-2 load emitter expects exactly one custom IWD image and one stock donor image; found {imageSources.Count} IWD image(s).");

        var customImageEntry = new[] { image0, image1 }.Single(x => imageSources.ContainsKey(x.Name));
        var donorImageEntry = new[] { image0, image1 }.Single(x => !imageSources.ContainsKey(x.Name));
        if (!donorImageEntry.Name.Equals("defeat", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Alpha-2 smoke substitute is only proven for stock image 'defeat'; unresolved donor is '{donorImageEntry.Name}'.");

        var customIwiBytes = ReadZipEntry(imageSources[customImageEntry.Name]);
        var customImage = T4XboxImageCodec.ConvertLoadscreenIwi(customIwiBytes);
        var donorImage = T4XboxImageCodec.CreateDefeatSmokeSubstitute();

        var pcMat0 = ParsePcMaterial(pcZone, nodeMap[Key("material", material0.Name)], graph);
        var pcMat1 = ParsePcMaterial(pcZone, nodeMap[Key("material", material1.Name)], graph);
        var pcMat2 = ParsePcMaterial(pcZone, nodeMap[Key("material", material2.Name)], graph);

        ValidateLoadMaterialShape(pcMat0, expectedTextures: 0, expectedStates: 0);
        ValidateLoadMaterialShape(pcMat1, expectedTextures: 1, expectedStates: 1);
        ValidateLoadMaterialShape(pcMat2, expectedTextures: 1, expectedStates: 1);

        if (!pcMat1.ImageDependencies.Contains(image0.Name, StringComparer.OrdinalIgnoreCase) ||
            !pcMat2.ImageDependencies.Contains(image1.Name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Dependency-first graph/material ownership does not match the expected 7-node load ordering.");

        var rawNode = nodeMap[Key("rawfile", raw.Name)];
        var rawProv = RequiredPcProvenance(rawNode);
        var rawStart = rawProv.SourceOffset!.Value;
        var rawLength = checked((int)ReadU32Little(pcZone, rawStart + 4));
        var rawBufferPtr = ReadU32Little(pcZone, rawStart + 8);
        var rawCursor = rawStart + 12;
        var rawName = ReadAsciiZ(pcZone, ref rawCursor, pcZone.Length, "PC RawFile.name");
        if (!rawName.Equals(raw.Name, StringComparison.Ordinal))
            throw new InvalidDataException($"RawFile identity mismatch: graph='{raw.Name}', stream='{rawName}'.");
        byte[] rawBuffer;
        if (rawBufferPtr == 0)
        {
            if (rawLength != 0) throw new InvalidDataException("RawFile has null buffer with non-zero length.");
            rawBuffer = Array.Empty<byte>();
        }
        else if (rawBufferPtr == T4ZonePointer.Following)
        {
            Ensure(pcZone, rawCursor, rawLength + 1, "PC RawFile buffer");
            rawBuffer = pcZone.AsSpan(rawCursor, rawLength + 1).ToArray();
        }
        else
        {
            throw new InvalidDataException("Alpha-2 emitter requires an inline/FOLLOWING RawFile buffer.");
        }

        var outputNames = graph.OutputOrder.Select(x => $"{x.TypeName}:{x.Name}").ToList();
        var zone = BuildZone(
            tech.Name,
            pcMat0,
            image0.Name,
            image0.Name.Equals(customImageEntry.Name, StringComparison.OrdinalIgnoreCase) ? customImage : donorImage,
            pcMat1,
            image1.Name,
            image1.Name.Equals(customImageEntry.Name, StringComparison.OrdinalIgnoreCase) ? customImage : donorImage,
            pcMat2,
            raw.Name,
            rawLength,
            rawBuffer);

        var readBack = T4XboxLoadZoneReader.Read(zone);
        var graphOrderVerified = readBack.Assets.Count == graph.OutputOrder.Count &&
                                 readBack.Assets.Zip(graph.OutputOrder).All(pair =>
                                     pair.First.TypeName.Equals(pair.Second.TypeName, StringComparison.OrdinalIgnoreCase) &&
                                     pair.First.Name.Equals(pair.Second.Name, StringComparison.Ordinal));
        if (!graphOrderVerified)
            throw new InvalidDataException("Alpha-2 read-back asset identities/order differ from the linked graph.");
        if (!readBack.ExactSerializedWalk)
            throw new InvalidDataException("Alpha-2 read-back did not close both the serialized and LargeRuntime streams exactly.");

        var baseName = Path.GetFileNameWithoutExtension(sourceFastFile);
        var zonePath = Path.Combine(outputDir, $"{baseName}_linker_v1_alpha2_smoke.zone");
        var ffPath = Path.Combine(outputDir, $"{baseName}_linker_v1_alpha2_smoke.ff");
        await File.WriteAllBytesAsync(zonePath, zone, cancellationToken);
        await WriteUnsignedXboxSingleZlibAsync(ffPath, zone, cancellationToken);

        var tempRoundTrip = Path.Combine(Path.GetTempPath(), "WaW360Converter", "linker-v1-alpha2", Guid.NewGuid().ToString("N") + ".zone");
        Directory.CreateDirectory(Path.GetDirectoryName(tempRoundTrip)!);
        bool wrapperVerified;
        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(ffPath, tempRoundTrip, cancellationToken);
            var roundTrip = await File.ReadAllBytesAsync(tempRoundTrip, cancellationToken);
            wrapperVerified = decomp.Platform == WaWPlatform.Xbox360 &&
                              decomp.Compression == WaWCompressionKind.XboxSingleZlib &&
                              roundTrip.AsSpan().SequenceEqual(zone);
            if (!wrapperVerified)
                throw new InvalidDataException("Alpha-2 Xbox single-zlib wrapper did not round-trip to identical zone bytes.");
        }
        finally
        {
            try { if (File.Exists(tempRoundTrip)) File.Delete(tempRoundTrip); } catch { }
        }

        var notes = new List<string>
        {
            "CUSTOM IMAGE: exact IWD identity was decoded, bilinear-resized to 1280x720, BC1/DXT1 encoded, 16-bit word-swapped, and Xenos tiled into 0x78000 LargeRuntime bytes.",
            "STOCK IMAGE: 'defeat' is still unresolved as a verified Xbox donor. Alpha 2 uses a zero-pixel 0x10000 BC1 structural smoke substitute with the proven Xbox metadata contract.",
            "OUTPUT POLICY: because the stock donor is not verified, this file is named *_smoke.ff and HardwareReady=False. Do not treat it as the final converted load.ff.",
            "READ-BACK: the generated Xbox zone was deterministically re-walked from its XAsset table; asset identities/order, serialized end, and LargeRuntime accounting all closed exactly."
        };

        return new NormalMapLinkerV1EmitFastFileResult(
            sourceFastFile,
            zonePath,
            ffPath,
            graph.SourceAssetCount,
            graph.LinkedAssetCount,
            readBack.NonRuntimeEnd,
            readBack.LargeRuntimeBytes,
            checked((int)new FileInfo(ffPath).Length),
            CustomIwiConverted: true,
            DefeatSmokeSubstituteUsed: true,
            GraphOrderVerified: graphOrderVerified,
            ZoneReadBackVerified: readBack.ExactSerializedWalk,
            WrapperRoundTripVerified: wrapperVerified,
            HardwareReady: false,
            graph.SourceSha256,
            Sha256(zone),
            Sha256(await File.ReadAllBytesAsync(ffPath, cancellationToken)),
            outputNames,
            notes);
    }

    private static byte[] BuildZone(
        string techniqueName,
        PcMaterialEmissionData material0,
        string image0Name,
        T4XboxConvertedImage image0,
        PcMaterialEmissionData material1,
        string image1Name,
        T4XboxConvertedImage image1,
        PcMaterialEmissionData material2,
        string rawName,
        int rawLength,
        byte[] rawBuffer)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[36]); // XFile header patched after serialized stream is complete.
        WriteU32(ms, 1); // Xbox reference load zones carry one null script-string slot.
        WriteU32(ms, T4ZonePointer.Following);
        WriteU32(ms, 7);
        WriteU32(ms, T4ZonePointer.Following);
        WriteU32(ms, 0); // null script-string entry

        foreach (var typeId in new[] { 8, 6, 9, 6, 9, 6, 33 })
        {
            WriteU32(ms, checked((uint)typeId));
            WriteU32(ms, T4ZonePointer.Insert);
        }

        var technique = new byte[XboxTechniqueSetSize];
        BinaryPrimitives.WriteUInt32BigEndian(technique.AsSpan(0, 4), T4ZonePointer.Following);
        ms.Write(technique);
        WriteAsciiZ(ms, techniqueName);

        WriteMaterial(ms, material0, 0, 0);

        ms.Write(T4XboxImageCodec.BuildXboxImageFixedBody(image0));
        WriteAsciiZ(ms, image0Name);
        ms.Write(image0.PlatformPayload);

        WriteMaterial(ms, material1, XboxTechniqueReference, XboxFirstImageReference);

        ms.Write(T4XboxImageCodec.BuildXboxImageFixedBody(image1));
        WriteAsciiZ(ms, image1Name);
        ms.Write(image1.PlatformPayload);

        WriteMaterial(ms, material2, XboxTechniqueReference, XboxSecondImageReference);

        WriteU32(ms, T4ZonePointer.Following);
        WriteU32(ms, checked((uint)rawLength));
        WriteU32(ms, T4ZonePointer.Following);
        WriteAsciiZ(ms, rawName);
        if (rawBuffer.Length == 0) ms.WriteByte(0);
        else ms.Write(rawBuffer);

        var nonRuntimeEnd = checked((int)ms.Length);
        var largeRuntime = checked(image0.AllocationBytes + image1.AllocationBytes);
        var virtualBytes = 3 * XboxMaterialSize + 2 * 40;

        var header = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), checked((uint)(nonRuntimeEnd - 36)));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), XboxTechniqueSetSize);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), checked((uint)largeRuntime));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(24, 4), checked((uint)virtualBytes));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(28, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(32, 4), 0);

        ms.Position = 0;
        ms.Write(header);
        ms.Position = ms.Length;
        ms.Write(image0.LargeRuntimeBytes);
        ms.Write(image1.LargeRuntimeBytes);
        return ms.ToArray();
    }

    private static void WriteMaterial(MemoryStream ms, PcMaterialEmissionData material, uint techniquePointer, uint imagePointer)
    {
        var fixedBody = ConvertMaterialFixed(material.Fixed, techniquePointer);
        ms.Write(fixedBody);
        WriteAsciiZ(ms, material.Name);

        for (var i = 0; i < material.TextureDefs.Count; i++)
        {
            if (material.TextureDefs.Count != 1 || imagePointer == 0)
                throw new InvalidDataException($"Alpha-2 material '{material.Name}' requires the proven zero-or-one texture load-screen shape.");
            ms.Write(ConvertTextureDef(material.TextureDefs[i], imagePointer));
        }
        foreach (var constant in material.Constants)
            ms.Write(ConvertConstantDef(constant));
        foreach (var state in material.StateBits)
            ms.Write(ConvertStateBits(state));
    }

    private static byte[] ConvertMaterialFixed(byte[] pc, uint techniquePointer)
    {
        if (pc.Length != PcMaterialSize) throw new ArgumentException("PC Material fixed body must be 112 bytes.", nameof(pc));
        var output = new byte[XboxMaterialSize];
        pc.AsSpan(0, 24).CopyTo(output.AsSpan(0, 24));
        // endian-sensitive u32 field proven by Aztec reference
        (output[20], output[23]) = (output[23], output[20]);
        (output[21], output[22]) = (output[22], output[21]);
        pc.AsSpan(32, 48).CopyTo(output.AsSpan(24, 48)); // remove PC-only +0x18..+0x1F
        pc.AsSpan(88, 8).CopyTo(output.AsSpan(72, 8));   // remove PC-only +0x50..+0x57

        var textureCount = pc[91];
        var constantCount = pc[92];
        var stateCount = pc[93];
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(80, 4), techniquePointer);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(84, 4), textureCount == 0 ? 0u : T4ZonePointer.Following);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(88, 4), constantCount == 0 ? 0u : T4ZonePointer.Following);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(92, 4), stateCount == 0 ? 0u : T4ZonePointer.Following);
        return output;
    }

    private static byte[] ConvertTextureDef(byte[] pc, uint imagePointer)
    {
        if (pc.Length != TextureDefSize) throw new ArgumentException("MaterialTextureDef must be 16 bytes.", nameof(pc));
        var output = new byte[TextureDefSize];
        output[0] = pc[3]; output[1] = pc[2]; output[2] = pc[1]; output[3] = pc[0];
        pc.AsSpan(4, 8).CopyTo(output.AsSpan(4, 8));
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(12, 4), imagePointer);
        return output;
    }

    private static byte[] ConvertConstantDef(byte[] pc)
    {
        // The Aztec load reference does not contain Material constants. Fail closed rather
        // than pretend the PC 32-byte form is Xbox-identical when this has not been proven.
        throw new InvalidDataException("Alpha-2 load emitter has no proven Xbox MaterialConstantDef conversion; material constants are unsupported in this milestone.");
    }

    private static byte[] ConvertStateBits(byte[] pc)
    {
        if (pc.Length != StateBitsSize) throw new ArgumentException("GfxStateBits must be 8 bytes.", nameof(pc));
        var output = new byte[StateBitsSize];
        output[0] = pc[3]; output[1] = pc[2]; output[2] = pc[1]; output[3] = pc[0];
        var pcSecond = BinaryPrimitives.ReadUInt32LittleEndian(pc.AsSpan(4, 4));
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(4, 4), pcSecond & 0xFFFFu);
        return output;
    }

    private static PcMaterialEmissionData ParsePcMaterial(
        byte[] zone,
        T4LoadLinkNodeSummary node,
        T4LoadLinkResult graph)
    {
        var provenance = RequiredPcProvenance(node);
        var start = provenance.SourceOffset!.Value;
        Ensure(zone, start, PcMaterialSize, $"PC Material '{node.Name}'");
        var fixedBody = zone.AsSpan(start, PcMaterialSize).ToArray();
        var textureCount = fixedBody[91];
        var constantCount = fixedBody[92];
        var stateCount = fixedBody[93];
        var texturePtr = ReadU32Little(fixedBody, 100);
        var constantPtr = ReadU32Little(fixedBody, 104);
        var statePtr = ReadU32Little(fixedBody, 108);
        var cursor = start + PcMaterialSize;
        var name = ReadAsciiZ(zone, ref cursor, zone.Length, "PC Material.name");
        if (!name.Equals(node.Name, StringComparison.Ordinal))
            throw new InvalidDataException($"Material identity mismatch: graph='{node.Name}', stream='{name}'.");

        var textureDefs = new List<byte[]>();
        if (texturePtr != 0)
        {
            if (texturePtr != T4ZonePointer.Following)
                throw new InvalidDataException($"Material '{name}' texture table is not inline/FOLLOWING.");
            for (var i = 0; i < textureCount; i++)
            {
                Ensure(zone, cursor, TextureDefSize, $"Material '{name}' texture[{i}]");
                textureDefs.Add(zone.AsSpan(cursor, TextureDefSize).ToArray());
                cursor += TextureDefSize;
            }

            foreach (var dependency in node.Dependencies
                         .Where(d => d.Kind == T4LinkDependencyKind.TextureImage)
                         .OrderBy(d => d.Ordinal))
            {
                var imageNode = graph.Nodes.Single(n =>
                    n.TypeName.Equals("image", StringComparison.OrdinalIgnoreCase) &&
                    n.Name.Equals(dependency.Target.Name, StringComparison.OrdinalIgnoreCase));
                var imageProv = RequiredPcProvenance(imageNode);
                if (imageProv.SourceOffset != cursor)
                    throw new InvalidDataException($"Material '{name}' child image '{imageNode.Name}' does not begin at deterministic cursor 0x{cursor:X}.");
                cursor = imageProv.SourceEnd!.Value;
            }
        }

        var constants = new List<byte[]>();
        if (constantPtr != 0)
        {
            if (constantPtr != T4ZonePointer.Following)
                throw new InvalidDataException($"Material '{name}' constant table is not inline/FOLLOWING.");
            for (var i = 0; i < constantCount; i++)
            {
                Ensure(zone, cursor, ConstantDefSize, $"Material '{name}' constant[{i}]");
                constants.Add(zone.AsSpan(cursor, ConstantDefSize).ToArray());
                cursor += ConstantDefSize;
            }
        }

        var states = new List<byte[]>();
        if (statePtr != 0)
        {
            if (statePtr != T4ZonePointer.Following)
                throw new InvalidDataException($"Material '{name}' state-bits table is not inline/FOLLOWING.");
            for (var i = 0; i < stateCount; i++)
            {
                Ensure(zone, cursor, StateBitsSize, $"Material '{name}' state[{i}]");
                states.Add(zone.AsSpan(cursor, StateBitsSize).ToArray());
                cursor += StateBitsSize;
            }
        }

        if (provenance.SourceEnd != cursor)
            throw new InvalidDataException($"Material '{name}' emission parse ended at 0x{cursor:X}, linker provenance ends at 0x{provenance.SourceEnd:X}.");

        return new PcMaterialEmissionData(
            name, fixedBody, textureCount, constantCount, stateCount,
            textureDefs, constants, states,
            node.Dependencies.Where(d => d.Kind == T4LinkDependencyKind.TextureImage).OrderBy(d => d.Ordinal).Select(d => d.Target.Name).ToList());
    }

    private static void ValidateLoadMaterialShape(PcMaterialEmissionData material, int expectedTextures, int expectedStates)
    {
        if (material.TextureCount != expectedTextures || material.TextureDefs.Count != expectedTextures ||
            material.ConstantCount != 0 || material.Constants.Count != 0 ||
            material.StateCount != expectedStates || material.StateBits.Count != expectedStates)
            throw new InvalidDataException(
                $"Material '{material.Name}' is outside the proven alpha-2 load shape: " +
                $"textures={material.TextureCount}, constants={material.ConstantCount}, states={material.StateCount}.");
    }

    private static T4LinkProvenance RequiredPcProvenance(T4LoadLinkNodeSummary node) =>
        node.Provenance.FirstOrDefault(p =>
            (p.Origin is T4LinkAssetOrigin.PcTopLevel or T4LinkAssetOrigin.PcNested) &&
            p.SourceOffset is not null && p.SourceEnd is not null)
        ?? throw new InvalidDataException($"Asset '{node.TypeName}:{node.Name}' has no exact PC byte-span provenance.");

    private static Dictionary<string, (string Archive, string Entry)> ReadIwdImages(IEnumerable<string> iwdPaths)
    {
        var result = new Dictionary<string, (string Archive, string Entry)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in iwdPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                var normalized = entry.FullName.Replace('\\', '/');
                if (!normalized.StartsWith("images/", StringComparison.OrdinalIgnoreCase) ||
                    !normalized.EndsWith(".iwi", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(normalized);
                if (!string.IsNullOrWhiteSpace(name)) result.TryAdd(name, (path, entry.FullName));
            }
        }
        return result;
    }

    private static byte[] ReadZipEntry((string Archive, string Entry) source)
    {
        using var archive = ZipFile.OpenRead(source.Archive);
        var entry = archive.GetEntry(source.Entry) ?? archive.Entries.FirstOrDefault(e => e.FullName.Equals(source.Entry, StringComparison.OrdinalIgnoreCase));
        if (entry is null) throw new InvalidDataException($"IWD entry '{source.Entry}' disappeared from '{source.Archive}'.");
        using var input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static async Task WriteUnsignedXboxSingleZlibAsync(string outputPath, byte[] zone, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await using var output = File.Create(outputPath);
        await output.WriteAsync(Encoding.ASCII.GetBytes("IWffu100"), cancellationToken);
        await output.WriteAsync(new byte[] { 0x00, 0x00, 0x01, 0x83 }, cancellationToken);
        await using var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true);
        await zlib.WriteAsync(zone, cancellationToken);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteAsciiZ(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
        stream.WriteByte(0);
    }

    private static uint ReadU32Little(ReadOnlySpan<byte> data, int offset)
    {
        Ensure(data, offset, 4, "uint32");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    private static string ReadAsciiZ(ReadOnlySpan<byte> data, ref int cursor, int limit, string label)
    {
        var start = cursor;
        while (cursor < limit && data[cursor] != 0) cursor++;
        if (cursor >= limit) throw new InvalidDataException($"Unterminated {label} at 0x{start:X}.");
        var value = Encoding.ASCII.GetString(data.Slice(start, cursor - start));
        cursor++;
        return value;
    }

    private static string Key(string type, string name) => type.ToLowerInvariant() + "\u001F" + name;

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void Ensure(ReadOnlySpan<byte> data, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException($"{label} at 0x{offset:X} ({length:N0} bytes) exceeds buffer length 0x{data.Length:X}.");
    }

    private static string RenderText(NormalMapLinkerV1EmitResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Linker V1 alpha 2 deterministic Xbox load.ff emitter");
        sb.AppendLine(new string('=', 82));
        sb.AppendLine($"Map: {result.MapName}");
        sb.AppendLine($"Load fastfiles: {result.LoadFastFiles:N0}");
        sb.AppendLine($"Emitted smoke fastfiles: {result.EmittedFastFiles:N0}");
        sb.AppendLine($"Hardware-ready fastfiles: {result.HardwareReadyFastFiles:N0}");
        sb.AppendLine($"Stock-image smoke substitutes: {result.SmokeSubstitutes:N0}");
        sb.AppendLine($"Emitter/read-back verified: {result.Verified}");
        sb.AppendLine();
        sb.AppendLine("POLICY");
        sb.AppendLine("  The emitter consumes only the deterministic Linker V1 graph. No broad candidate scan is permitted.");
        sb.AppendLine("  A generated *_smoke.ff is NOT final/hardware-ready while any stock image uses a synthetic smoke substitute.");
        sb.AppendLine("  Final promotion requires an exact verified Xbox donor for every DONOR_REQUIRED dependency.");

        foreach (var ff in result.FastFiles)
        {
            sb.AppendLine();
            sb.AppendLine($"[{Path.GetFileName(ff.SourceFastFile)}]");
            sb.AppendLine($"  PC roots={ff.PcRoots}; Xbox assets={ff.XboxAssets}; nonRuntime=0x{ff.NonRuntimeBytes:X}; LargeRuntime=0x{ff.LargeRuntimeBytes:X}; ffBytes={ff.OutputBytes:N0}");
            sb.AppendLine($"  customIWI={ff.CustomIwiConverted}; defeatSmoke={ff.DefeatSmokeSubstituteUsed}; graphOrder={ff.GraphOrderVerified}; zoneReadBack={ff.ZoneReadBackVerified}; wrapperRoundTrip={ff.WrapperRoundTripVerified}; hardwareReady={ff.HardwareReady}");
            sb.AppendLine($"  zone={ff.OutputZone}");
            sb.AppendLine($"  fastfile={ff.OutputFastFile}");
            sb.AppendLine($"  sourceSha256={ff.SourceSha256}");
            sb.AppendLine($"  zoneSha256={ff.ZoneSha256}");
            sb.AppendLine($"  ffSha256={ff.FastFileSha256}");
            sb.AppendLine("  order: " + string.Join(" -> ", ff.OutputOrder));
            foreach (var note in ff.Notes) sb.AppendLine("  NOTE: " + note);
        }
        foreach (var warning in result.Warnings) sb.AppendLine("WARNING: " + warning);
        return sb.ToString();
    }
}
