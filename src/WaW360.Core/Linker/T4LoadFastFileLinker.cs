using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Linker;

public sealed record T4LoadLinkOrderEntry(
    int OutputIndex,
    int XboxTypeId,
    string TypeName,
    string Name,
    T4LinkAssetOrigin Origin,
    T4LinkAssetState State,
    string? BlockReason,
    IReadOnlyList<string> Dependencies);

public sealed record T4LoadLinkNodeSummary(
    string TypeName,
    string Name,
    T4LinkAssetOrigin Origin,
    T4LinkAssetState State,
    int? PcTypeId,
    int XboxTypeId,
    bool IsRoot,
    string? BlockReason,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<T4LinkDependency> Dependencies,
    IReadOnlyList<T4LinkProvenance> Provenance);

public sealed class T4LoadLinkResult
{
    public required string SourceFastFile { get; init; }
    public required string SourceSha256 { get; init; }
    public required int SourceAssetCount { get; init; }
    public required int LinkedAssetCount { get; init; }
    public required int PromotedNestedAssetCount { get; init; }
    public required int SourcePayloadStart { get; init; }
    public required int SourceWalkEnd { get; init; }
    public required int SourceZoneLength { get; init; }
    public required bool ExactSourceWalk { get; init; }
    public required bool TechniqueBindingProven { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<T4LoadLinkNodeSummary> Nodes { get; init; }
    public required IReadOnlyList<T4LoadLinkOrderEntry> OutputOrder { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Linker V1: deterministic, stream-ordered PC load.ff graph builder.
///
/// This deliberately does NOT scan the decompressed zone for structure-shaped candidates.
/// The XAsset table is authoritative: each physical top-level asset is consumed from the
/// current serialized-stream cursor using its real T4 type layout, then owned children are
/// walked immediately. Nested Images become independent graph nodes and dependencies.
///
/// Current V1 scope is intentionally fail-closed: techset, material, image and rawfile.
/// That is enough to reproduce the Aztec 5-root PC load graph and its 7-node Xbox closure.
/// Unsupported inline shader/water/packed texture graphs stop the walk rather than falling
/// back to the legacy broad scanners.
/// </summary>
public static class T4LoadFastFileLinker
{
    private const int PcTechniqueSetFixedSize = 248;
    private const int PcTechniqueCount = 59;
    private const int XboxTechniqueSetFixedSize = 216; // 12-byte header incl. remappedTechniqueSet + 51 technique pointers
    private const int XboxTechniqueCount = 51;
    private const int PcMaterialFixedSize = 112;
    private const int XboxMaterialFixedSize = 96;
    private const int MaterialTextureDefSize = 16;
    private const int MaterialConstantDefSize = 32;
    private const int GfxStateBitsSize = 8;
    private const int PcGfxImageFixedSize = 36;
    private const int PcGfxImageLoadDefSize = 16;
    private const int XboxGfxImageFixedSize = 40;
    private const int XboxGfxImagePlatformPayloadSize = 68;
    private const int RawFileFixedSize = 12;

    private sealed record MaterialTechniqueReference(T4LinkAssetKey Material, uint RawPointer);

    private sealed record ParsedImage(
        T4LinkAssetNode Node,
        int End,
        int ResourceSize,
        uint Format,
        ushort Width,
        ushort Height,
        ushort Depth);

    public static async Task<T4LoadLinkResult> BuildAsync(
        string sourceFastFile,
        CancellationToken cancellationToken = default)
    {
        var cached = await FastFileAnalysisCache.GetZoneAsync(sourceFastFile, cancellationToken);
        var document = cached.Document;
        var zone = cached.Bytes;

        if (document.Platform != WaWPlatform.PC || document.Endianness != Endian.Little)
            throw new InvalidDataException("Linker V1 load graph expects a PC/little-endian source fastfile.");
        if (document.AssetCount == 0)
            throw new InvalidDataException("Linker V1 load graph cannot link an empty XAsset list.");
        if (document.AssetCount > 128)
            throw new InvalidDataException($"Linker V1 load graph is intentionally limited to small load zones; source declares {document.AssetCount:N0} assets.");
        if (document.Assets.Any(a => a.ReferenceKind is T4AssetReferenceKind.Null or T4AssetReferenceKind.OffsetOrAlias))
            throw new InvalidDataException("Linker V1 load graph requires physical top-level XAssets. Alias/null top-level rows are not yet supported.");

        var sourcePath = Path.GetFullPath(sourceFastFile);
        var graph = new T4LinkGraph();
        var warnings = new List<string>();
        var materialTechniqueRefs = new List<MaterialTechniqueReference>();
        var topLevelTechsets = new List<T4LinkAssetKey>();

        var cursor = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));
        var payloadStart = cursor;

        foreach (var asset in document.Assets.OrderBy(a => a.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = cursor;

            switch (asset.TypeName.ToLowerInvariant())
            {
                case "techset":
                {
                    var (node, end) = ParseTechniqueSet(
                        graph, sourcePath, cached.SourceSha256, asset, zone, cursor, document.Header);
                    topLevelTechsets.Add(node.Key);
                    cursor = end;
                    break;
                }
                case "material":
                {
                    var (node, end, techniquePointer) = ParseMaterial(
                        graph, sourcePath, cached.SourceSha256, asset, zone, cursor, document.Header);
                    if (techniquePointer != 0)
                        materialTechniqueRefs.Add(new MaterialTechniqueReference(node.Key, techniquePointer));
                    cursor = end;
                    break;
                }
                case "rawfile":
                {
                    var (_, end) = ParseRawFile(
                        graph, sourcePath, cached.SourceSha256, asset, zone, cursor, document.Header);
                    cursor = end;
                    break;
                }
                default:
                    throw new InvalidDataException(
                        $"Linker V1 reached XAsset#{asset.Index} '{asset.TypeName}' at stream cursor 0x{cursor:X8}. " +
                        "That type has no deterministic load-zone walker yet; refusing to scan for the next asset.");
            }

            if (cursor <= start)
                throw new InvalidDataException($"Linker V1 walker for XAsset#{asset.Index} '{asset.TypeName}' did not advance the serialized stream.");
        }

        var techniqueBindingProven = BindMaterialTechniqueSets(
            graph, topLevelTechsets, materialTechniqueRefs, warnings);

        var exactWalk = cursor == zone.Length;
        if (!exactWalk)
        {
            var remainder = zone.Length - cursor;
            warnings.Add($"Serialized source walk ended at 0x{cursor:X8}, zone length is 0x{zone.Length:X8}; {remainder:N0} byte(s) remain unowned.");
        }

        var order = graph.BuildDependencyFirstOrder();
        var orderRows = order.Select((node, index) => new T4LoadLinkOrderEntry(
            index,
            node.XboxTypeId,
            node.Key.TypeName,
            node.Key.Name,
            node.Origin,
            node.State,
            node.BlockReason,
            node.Dependencies
                .OrderBy(d => d.Ordinal)
                .Select(d => $"{d.Relationship} -> {d.Target}")
                .ToList()))
            .ToList();

        var nodeRows = graph.Nodes
            .OrderBy(n => n.FirstSeenOrder)
            .Select(n => new T4LoadLinkNodeSummary(
                n.Key.TypeName,
                n.Key.Name,
                n.Origin,
                n.State,
                n.PcTypeId,
                n.XboxTypeId,
                n.IsRoot,
                n.BlockReason,
                n.Metadata,
                n.Dependencies.ToList(),
                n.Provenance.ToList()))
            .ToList();

        var promoted = graph.Nodes.Count(n => n.Origin == T4LinkAssetOrigin.PcNested);
        var verified = exactWalk && techniqueBindingProven && order.Count == graph.Nodes.Count;
        if (verified)
            warnings.Add("INFO: deterministic XAsset-stream walk closed the complete source load zone without any global candidate scan.");

        return new T4LoadLinkResult
        {
            SourceFastFile = sourcePath,
            SourceSha256 = cached.SourceSha256,
            SourceAssetCount = document.AssetCount,
            LinkedAssetCount = graph.Nodes.Count,
            PromotedNestedAssetCount = promoted,
            SourcePayloadStart = payloadStart,
            SourceWalkEnd = cursor,
            SourceZoneLength = zone.Length,
            ExactSourceWalk = exactWalk,
            TechniqueBindingProven = techniqueBindingProven,
            Verified = verified,
            Nodes = nodeRows,
            OutputOrder = orderRows,
            Warnings = warnings
        };
    }

    private static (T4LinkAssetNode Node, int End) ParseTechniqueSet(
        T4LinkGraph graph,
        string sourcePath,
        string sourceHash,
        T4AssetEntry asset,
        byte[] zone,
        int start,
        ZoneHeaderSummary header)
    {
        Ensure(zone, start, PcTechniqueSetFixedSize, "PC MaterialTechniqueSet fixed body");
        var namePtr = ReadU32(zone, start + 0);
        var remappedPtr = ReadU32(zone, start + 8);
        RequireSerializedPointer(namePtr, header, "MaterialTechniqueSet.name");
        RequireSerializedPointer(remappedPtr, header, "MaterialTechniqueSet.remappedTechniqueSet");
        if (!IsInline(namePtr))
            throw new InvalidDataException($"XAsset#{asset.Index} TechniqueSet name is not inline; deterministic packed-name resolution is not implemented in Linker V1.");

        var techniquePointers = new uint[PcTechniqueCount];
        for (var i = 0; i < techniquePointers.Length; i++)
        {
            techniquePointers[i] = ReadU32(zone, start + 12 + i * 4);
            RequireSerializedPointer(techniquePointers[i], header, $"MaterialTechniqueSet.techniques[{i}]");
        }

        var cursor = start + PcTechniqueSetFixedSize;
        var name = ReadAsciiZ(zone, ref cursor, 1024, "MaterialTechniqueSet.name");
        var key = T4LinkAssetKey.Create("techset", name);
        var node = graph.AddOrMerge(key, T4LinkAssetOrigin.PcTopLevel, T4LinkAssetState.ConversionRequired, asset.TypeId, isRoot: true);

        var inlineTechniqueCount = techniquePointers.Count(IsInline);
        var packedTechniqueCount = techniquePointers.Count(p => p != 0 && !IsInline(p));
        node.SetMetadata("pcFixedSize", PcTechniqueSetFixedSize.ToString());
        node.SetMetadata("pcTechniqueSlots", PcTechniqueCount.ToString());
        node.SetMetadata("xboxFixedSize", XboxTechniqueSetFixedSize.ToString());
        node.SetMetadata("xboxTechniqueSlots", XboxTechniqueCount.ToString());
        node.SetMetadata("inlineTechniqueGraphs", inlineTechniqueCount.ToString());
        node.SetMetadata("packedTechniqueGraphs", packedTechniqueCount.ToString());
        node.SetMetadata("remappedTechniqueSet", $"0x{remappedPtr:X8}");

        if (inlineTechniqueCount != 0)
        {
            node.State = T4LinkAssetState.Blocked;
            node.BlockReason = $"TechniqueSet owns {inlineTechniqueCount:N0} inline PC technique graph(s); Linker V1 will not guess their serialized lengths or copy D3D shader graphs to Xbox.";
            throw new InvalidDataException(node.BlockReason);
        }
        if (packedTechniqueCount != 0 || remappedPtr != 0)
        {
            node.State = T4LinkAssetState.Blocked;
            node.BlockReason = "TechniqueSet references packed/remapped PC technique data; Xbox shader/pass relinking is required before emission.";
        }

        node.AddProvenance(Provenance(
            T4LinkAssetOrigin.PcTopLevel, sourcePath, sourceHash, asset.Index, start, cursor,
            zone.AsSpan(start, cursor - start), "deterministic top-level TechniqueSet stream walk"));
        return (node, cursor);
    }

    private static (T4LinkAssetNode Node, int End, uint TechniquePointer) ParseMaterial(
        T4LinkGraph graph,
        string sourcePath,
        string sourceHash,
        T4AssetEntry asset,
        byte[] zone,
        int start,
        ZoneHeaderSummary header)
    {
        Ensure(zone, start, PcMaterialFixedSize, "PC Material fixed body");
        var namePtr = ReadU32(zone, start + 0);
        var textureCount = zone[start + 91];
        var constantCount = zone[start + 92];
        var stateCount = zone[start + 93];
        var techniquePtr = ReadU32(zone, start + 96);
        var texturePtr = ReadU32(zone, start + 100);
        var constantPtr = ReadU32(zone, start + 104);
        var statePtr = ReadU32(zone, start + 108);

        RequireSerializedPointer(namePtr, header, "Material.name");
        RequireSerializedPointer(techniquePtr, header, "Material.techniqueSet");
        RequireSerializedPointer(texturePtr, header, "Material.textureTable");
        RequireSerializedPointer(constantPtr, header, "Material.constantTable");
        RequireSerializedPointer(statePtr, header, "Material.stateBitsTable");
        if (!IsInline(namePtr))
            throw new InvalidDataException($"XAsset#{asset.Index} Material name is not inline; deterministic packed-name resolution is not implemented in Linker V1.");
        if (textureCount > 64 || constantCount > 64 || stateCount > 64)
            throw new InvalidDataException($"XAsset#{asset.Index} Material counts are implausible: tex={textureCount}, const={constantCount}, state={stateCount}.");
        if (IsInline(techniquePtr))
            throw new InvalidDataException($"XAsset#{asset.Index} Material owns an inline TechniqueSet graph; Linker V1 refuses to skip an unparsed shader graph.");

        var cursor = start + PcMaterialFixedSize;
        var name = ReadAsciiZ(zone, ref cursor, 4096, "Material.name");
        var key = T4LinkAssetKey.Create("material", name);
        var node = graph.AddOrMerge(key, T4LinkAssetOrigin.PcTopLevel, T4LinkAssetState.ConversionRequired, asset.TypeId, isRoot: true);
        node.SetMetadata("pcFixedSize", PcMaterialFixedSize.ToString());
        node.SetMetadata("xboxFixedSize", XboxMaterialFixedSize.ToString());
        node.SetMetadata("textureCount", textureCount.ToString());
        node.SetMetadata("constantCount", constantCount.ToString());
        node.SetMetadata("stateBitsCount", stateCount.ToString());
        node.SetMetadata("sourceTechniquePointer", $"0x{techniquePtr:X8}");

        if (texturePtr != 0 && !IsInline(texturePtr))
            throw new InvalidDataException($"Material '{name}' uses packed/shared textureTable {T4ZonePointer.Describe(texturePtr)}; Linker V1 cannot identify texture dependencies without a block resolver.");

        if (texturePtr != 0)
        {
            var tableStart = cursor;
            var tableBytes = checked(textureCount * MaterialTextureDefSize);
            Ensure(zone, tableStart, tableBytes, $"Material '{name}' texture table");
            var textureRefs = new (byte Semantic, uint ImagePointer)[textureCount];
            for (var i = 0; i < textureCount; i++)
            {
                var p = tableStart + i * MaterialTextureDefSize;
                var semantic = zone[p + 7];
                var imagePtr = ReadU32(zone, p + 12);
                RequireSerializedPointer(imagePtr, header, $"Material '{name}' texture[{i}].image");
                textureRefs[i] = (semantic, imagePtr);
            }
            cursor += tableBytes;

            for (var i = 0; i < textureRefs.Length; i++)
            {
                var (semantic, imagePtr) = textureRefs[i];
                if (imagePtr == 0) continue;
                if (semantic == 0x0B)
                    throw new InvalidDataException($"Material '{name}' texture[{i}] is water_t; Linker V1 has no deterministic water dependency walker yet.");
                if (!IsInline(imagePtr))
                    throw new InvalidDataException($"Material '{name}' texture[{i}] uses packed/shared image pointer {T4ZonePointer.Describe(imagePtr)}; Linker V1 requires a resolved block target before linking it.");

                var image = ParseNestedImage(graph, sourcePath, sourceHash, zone, cursor, header, asset.Index, name, i, semantic);
                cursor = image.End;
                node.AddDependency(new T4LinkDependency(
                    image.Node.Key,
                    T4LinkDependencyKind.TextureImage,
                    i,
                    $"texture[{i}] semantic=0x{semantic:X2}"));
            }
        }

        if (constantPtr != 0)
        {
            if (!IsInline(constantPtr))
            {
                node.State = T4LinkAssetState.Blocked;
                node.BlockReason = $"Material constant table is packed/shared at {T4ZonePointer.Describe(constantPtr)}; block-aware child resolution is pending.";
            }
            else
            {
                var bytes = checked(constantCount * MaterialConstantDefSize);
                Ensure(zone, cursor, bytes, $"Material '{name}' constant table");
                cursor += bytes;
            }
        }

        if (statePtr != 0)
        {
            if (!IsInline(statePtr))
            {
                node.State = T4LinkAssetState.Blocked;
                node.BlockReason = $"Material state-bits table is packed/shared at {T4ZonePointer.Describe(statePtr)}; block-aware child resolution is pending.";
            }
            else
            {
                var bytes = checked(stateCount * GfxStateBitsSize);
                Ensure(zone, cursor, bytes, $"Material '{name}' state-bits table");
                cursor += bytes;
            }
        }

        node.AddProvenance(Provenance(
            T4LinkAssetOrigin.PcTopLevel, sourcePath, sourceHash, asset.Index, start, cursor,
            zone.AsSpan(start, cursor - start), "deterministic top-level Material + owned-child stream walk"));
        return (node, cursor, techniquePtr);
    }

    private static ParsedImage ParseNestedImage(
        T4LinkGraph graph,
        string sourcePath,
        string sourceHash,
        byte[] zone,
        int start,
        ZoneHeaderSummary header,
        int ownerAssetIndex,
        string ownerMaterial,
        int textureIndex,
        byte semantic)
    {
        Ensure(zone, start, PcGfxImageFixedSize, "PC GfxImage fixed body");
        var texturePtr = ReadU32(zone, start + 4);
        var width = ReadU16(zone, start + 24);
        var height = ReadU16(zone, start + 26);
        var depth = ReadU16(zone, start + 28);
        var namePtr = ReadU32(zone, start + 32);
        RequireSerializedPointer(texturePtr, header, "GfxImage.texture/loadDef");
        RequireSerializedPointer(namePtr, header, "GfxImage.name");
        if (!IsInline(namePtr))
            throw new InvalidDataException($"Nested image owned by Material '{ownerMaterial}' has a packed/shared name; Linker V1 cannot establish asset identity safely.");

        var cursor = start + PcGfxImageFixedSize;
        var name = ReadAsciiZ(zone, ref cursor, 4096, "GfxImage.name");
        var loadDefWidth = width;
        var loadDefHeight = height;
        var loadDefDepth = depth;
        uint format = 0;
        var resourceSize = 0;

        if (texturePtr != 0)
        {
            if (!IsInline(texturePtr))
                throw new InvalidDataException($"Nested image '{name}' uses packed/shared loadDef {T4ZonePointer.Describe(texturePtr)}; block-aware image resolution is pending.");
            Ensure(zone, cursor, PcGfxImageLoadDefSize, $"GfxImage '{name}' load definition");
            loadDefWidth = ReadU16(zone, cursor + 2);
            loadDefHeight = ReadU16(zone, cursor + 4);
            loadDefDepth = ReadU16(zone, cursor + 6);
            format = ReadU32(zone, cursor + 8);
            var resourceSizeRaw = ReadU32(zone, cursor + 12);
            if (resourceSizeRaw > int.MaxValue)
                throw new InvalidDataException($"GfxImage '{name}' resource is too large ({resourceSizeRaw:N0} bytes).");
            resourceSize = checked((int)resourceSizeRaw);
            cursor += PcGfxImageLoadDefSize;
            Ensure(zone, cursor, resourceSize, $"GfxImage '{name}' resource payload");
            cursor += resourceSize;
        }

        var key = T4LinkAssetKey.Create("image", name);
        var state = resourceSize == 0 ? T4LinkAssetState.Blocked : T4LinkAssetState.ConversionRequired;
        var node = graph.AddOrMerge(key, T4LinkAssetOrigin.PcNested, state, pcTypeId: 8, isRoot: false);
        node.SetMetadata("ownerMaterial", ownerMaterial);
        node.SetMetadata("xboxFixedSize", XboxGfxImageFixedSize.ToString());
        node.SetMetadata("xboxPlatformPayloadSize", XboxGfxImagePlatformPayloadSize.ToString());
        node.SetMetadata("ownerTextureIndex", textureIndex.ToString());
        node.SetMetadata("semantic", $"0x{semantic:X2}");
        node.SetMetadata("width", loadDefWidth.ToString());
        node.SetMetadata("height", loadDefHeight.ToString());
        node.SetMetadata("depth", loadDefDepth.ToString());
        node.SetMetadata("pcFormat", $"0x{format:X8}");
        node.SetMetadata("embeddedResourceBytes", resourceSize.ToString());
        if (resourceSize == 0)
            node.BlockReason = "PC image descriptor owns no embedded pixel resource; linker must resolve matching IWD pixels or a verified Xbox donor before emission.";

        node.AddProvenance(Provenance(
            T4LinkAssetOrigin.PcNested, sourcePath, sourceHash, ownerAssetIndex, start, cursor,
            zone.AsSpan(start, cursor - start), $"nested image from Material '{ownerMaterial}' texture[{textureIndex}]"));
        return new ParsedImage(node, cursor, resourceSize, format, loadDefWidth, loadDefHeight, loadDefDepth);
    }

    private static (T4LinkAssetNode Node, int End) ParseRawFile(
        T4LinkGraph graph,
        string sourcePath,
        string sourceHash,
        T4AssetEntry asset,
        byte[] zone,
        int start,
        ZoneHeaderSummary header)
    {
        Ensure(zone, start, RawFileFixedSize, "RawFile fixed body");
        var namePtr = ReadU32(zone, start + 0);
        var lengthRaw = ReadU32(zone, start + 4);
        var bufferPtr = ReadU32(zone, start + 8);
        RequireSerializedPointer(namePtr, header, "RawFile.name");
        RequireSerializedPointer(bufferPtr, header, "RawFile.buffer");
        if (!IsInline(namePtr))
            throw new InvalidDataException($"XAsset#{asset.Index} RawFile name is packed/shared; Linker V1 does not guess its identity.");
        if (lengthRaw > int.MaxValue)
            throw new InvalidDataException($"RawFile length {lengthRaw:N0} is too large.");

        var cursor = start + RawFileFixedSize;
        var name = ReadAsciiZ(zone, ref cursor, 4096, "RawFile.name");
        var length = checked((int)lengthRaw);
        if (bufferPtr == 0)
        {
            if (length != 0)
                throw new InvalidDataException($"RawFile '{name}' has a null buffer but non-zero length {length:N0}.");
        }
        else if (IsInline(bufferPtr))
        {
            var serializedBytes = checked(length + 1);
            Ensure(zone, cursor, serializedBytes, $"RawFile '{name}' buffer");
            cursor += serializedBytes;
        }
        else
        {
            throw new InvalidDataException($"RawFile '{name}' uses packed/shared buffer {T4ZonePointer.Describe(bufferPtr)}; block-aware RawFile resolution is pending.");
        }

        var key = T4LinkAssetKey.Create("rawfile", name);
        var node = graph.AddOrMerge(key, T4LinkAssetOrigin.PcTopLevel, T4LinkAssetState.ConversionRequired, asset.TypeId, isRoot: true);
        node.SetMetadata("pcFixedSize", RawFileFixedSize.ToString());
        node.SetMetadata("xboxFixedSize", RawFileFixedSize.ToString());
        node.SetMetadata("length", length.ToString());
        node.AddProvenance(Provenance(
            T4LinkAssetOrigin.PcTopLevel, sourcePath, sourceHash, asset.Index, start, cursor,
            zone.AsSpan(start, cursor - start), "deterministic top-level RawFile stream walk"));
        return (node, cursor);
    }

    private static bool BindMaterialTechniqueSets(
        T4LinkGraph graph,
        IReadOnlyList<T4LinkAssetKey> topLevelTechsets,
        IReadOnlyList<MaterialTechniqueReference> references,
        ICollection<string> warnings)
    {
        if (references.Count == 0) return true;
        if (topLevelTechsets.Count != 1)
        {
            warnings.Add($"Technique binding pending: {references.Count:N0} Material reference(s) and {topLevelTechsets.Count:N0} top-level TechniqueSet(s); V1 requires a unique target proof.");
            foreach (var reference in references)
            {
                var node = graph.GetRequired(reference.Material);
                node.State = T4LinkAssetState.Blocked;
                node.BlockReason = "Material.techniqueSet target is not uniquely proven.";
            }
            return false;
        }

        var distinctPointers = references.Select(r => r.RawPointer).Distinct().ToList();
        if (distinctPointers.Count != 1)
        {
            warnings.Add($"Technique binding pending: Materials reference {distinctPointers.Count:N0} distinct packed TechniqueSet pointer(s), but V1 only proves the single-TechSet consensus case.");
            foreach (var reference in references)
            {
                var node = graph.GetRequired(reference.Material);
                node.State = T4LinkAssetState.Blocked;
                node.BlockReason = "Material.techniqueSet packed target requires the generalized block-coordinate resolver.";
            }
            return false;
        }

        var target = topLevelTechsets[0];
        for (var i = 0; i < references.Count; i++)
        {
            var reference = references[i];
            graph.AddDependency(reference.Material, new T4LinkDependency(
                target,
                T4LinkDependencyKind.TechniqueSet,
                -1000,
                $"Material.techniqueSet {T4ZonePointer.Describe(reference.RawPointer)}"));
        }
        warnings.Add($"INFO: unique TechniqueSet binding proven by {references.Count:N0} Material reference(s) sharing raw pointer 0x{distinctPointers[0]:X8} -> {target}.");
        return true;
    }

    private static T4LinkProvenance Provenance(
        T4LinkAssetOrigin origin,
        string sourcePath,
        string sourceHash,
        int sourceAssetIndex,
        int start,
        int end,
        ReadOnlySpan<byte> payload,
        string note) => new(
            origin,
            sourcePath,
            sourceHash,
            sourceAssetIndex,
            start,
            end,
            Convert.ToHexString(SHA256.HashData(payload)),
            note);

    private static bool IsInline(uint pointer) =>
        pointer is T4ZonePointer.Following or T4ZonePointer.Insert;

    private static void RequireSerializedPointer(uint pointer, ZoneHeaderSummary header, string label)
    {
        if (!T4ZonePointer.IsWithinBlock(pointer, header))
            throw new InvalidDataException($"{label} has invalid serialized pointer 0x{pointer:X8}.");
    }

    private static uint ReadU32(byte[] bytes, int offset)
    {
        Ensure(bytes, offset, 4, "uint32");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    }

    private static ushort ReadU16(byte[] bytes, int offset)
    {
        Ensure(bytes, offset, 2, "uint16");
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    }

    private static string ReadAsciiZ(byte[] bytes, ref int cursor, int maxBytes, string label)
    {
        if (cursor < 0 || cursor >= bytes.Length)
            throw new InvalidDataException($"{label} starts outside the decompressed zone at 0x{cursor:X8}.");
        var start = cursor;
        while (cursor < bytes.Length && bytes[cursor] != 0)
        {
            cursor++;
            if (cursor - start > maxBytes)
                throw new InvalidDataException($"{label} exceeds {maxBytes:N0} bytes.");
        }
        if (cursor >= bytes.Length)
            throw new InvalidDataException($"{label} is unterminated.");
        var value = Encoding.ASCII.GetString(bytes, start, cursor - start);
        cursor++;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{label} is empty.");
        return value;
    }

    private static void Ensure(byte[] bytes, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException($"{label} at 0x{offset:X8} ({length:N0} bytes) exceeds decompressed zone length 0x{bytes.Length:X8}.");
    }
}
