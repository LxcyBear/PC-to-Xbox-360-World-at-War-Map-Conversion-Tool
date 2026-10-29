using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

internal sealed class T4XenonXModelCorpus
{
    public required string GraphRoot { get; init; }
    public required string BindingManifest { get; init; }
    public required IReadOnlyDictionary<int, T4XenonXModelCorpusEntry> OwnedByRoot { get; init; }
    public required int ReferenceRoots { get; init; }

    public static T4XenonXModelCorpus Load(
        string graphRoot,
        string bindingManifest,
        int expectedOwned = 280,
        int expectedReferences = 9)
    {
        graphRoot = Path.GetFullPath(graphRoot);
        bindingManifest = Path.GetFullPath(bindingManifest);

        if (!Directory.Exists(graphRoot))
            throw new DirectoryNotFoundException($"XModel graph root not found: {graphRoot}");
        if (!File.Exists(bindingManifest))
            throw new FileNotFoundException("XModel root-binding manifest not found.", bindingManifest);

        using var bindingDoc = JsonDocument.Parse(File.ReadAllText(bindingManifest));
        var root = bindingDoc.RootElement;
        if (!root.TryGetProperty("format", out var format))
            throw new InvalidDataException("XModel binding manifest has no format.");
        var bindingFormat = format.GetString();
        var aztecCanary = bindingFormat == "WaW360.AztecPcSingleXModelBinding.v1";
        if (!aztecCanary && bindingFormat != "WaW360.Nuclear4W.XModelRootBinding.v3")
            throw new InvalidDataException("Unexpected XModel root-binding manifest format.");
        if (!root.GetProperty("bindingPass").GetBoolean())
            throw new InvalidDataException("XModel root-binding manifest is not passing.");

        // The original 280 + 9 production corpus gate remains unchanged.
        // Only an explicitly marked, single-root Aztec diagnostic binding
        // may bypass those FRUmansion-specific counts. Never generalize to an
        // arbitrary partial corpus: the runner SHA-pins the source model,
        // checks every graph blob, and keeps hardware readiness false.
        var requiredOwned = aztecCanary ? 1 : expectedOwned;
        var requiredRefs = aztecCanary ? 0 : expectedReferences;
        var ownedRows = root.GetProperty("ownedBindings").EnumerateArray().ToArray();
        var referenceRows = root.GetProperty("referenceBindings").EnumerateArray().ToArray();
        if (ownedRows.Length != requiredOwned)
            throw new InvalidDataException($"XModel owned binding count {ownedRows.Length} != {requiredOwned}.");
        if (referenceRows.Length != requiredRefs)
            throw new InvalidDataException($"XModel reference binding count {referenceRows.Length} != {requiredRefs}.");
        if (aztecCanary)
        {
            if (root.GetProperty("nativeRoots").GetInt32() != 8509 ||
                root.GetProperty("nativeXModelRoots").GetInt32() != 284 ||
                root.GetProperty("model").GetString() != "zombie_x2_icon" ||
                ownedRows[0].GetProperty("CorpusOrdinal").GetInt32() != 0 ||
                ownedRows[0].GetProperty("CorpusName").GetString() != "zombie_x2_icon" ||
                !NormalizeReferenceName(ownedRows[0].GetProperty("NativeName").GetString() ?? "")
                    .Equals("zombie_x2_icon", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Aztec single-XModel diagnostic binding identity/count gate failed.");
        }

        var entries = new Dictionary<int, T4XenonXModelCorpusEntry>();
        foreach (var row in ownedRows)
        {
            var rootIndex = row.GetProperty("NativeRootIndex").GetInt32();
            var ordinal = row.GetProperty("CorpusOrdinal").GetInt32();
            var nativeName = row.GetProperty("NativeName").GetString() ?? "";
            var corpusName = row.GetProperty("CorpusName").GetString() ?? "";

            var dumpRoot = Path.Combine(graphRoot, "dumps", ordinal.ToString("D3"));
            var graphsDir = Path.Combine(dumpRoot, "xenon_xmodel_graphs");
            if (!Directory.Exists(graphsDir))
                throw new DirectoryNotFoundException(
                    $"XModel graph directory missing for corpus ordinal {ordinal}: {graphsDir}");

            var graphFiles = Directory.GetFiles(graphsDir, "*.json", SearchOption.TopDirectoryOnly);
            if (graphFiles.Length != 1)
                throw new InvalidDataException(
                    $"Corpus ordinal {ordinal} expected one relocatable graph JSON; found {graphFiles.Length}.");

            var graph = JsonSerializer.Deserialize<T4XenonRelocatableXModelGraph>(
                File.ReadAllText(graphFiles[0]),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException($"Could not parse XModel graph: {graphFiles[0]}");

            ValidateGraph(graph, corpusName, graphFiles[0]);

            if (!entries.TryAdd(rootIndex, new T4XenonXModelCorpusEntry
            {
                NativeRootIndex = rootIndex,
                CorpusOrdinal = ordinal,
                NativeName = nativeName,
                CorpusName = corpusName,
                GraphFile = graphFiles[0],
                DumpRoot = dumpRoot,
                Graph = graph
            }))
                throw new InvalidDataException($"Duplicate owned XModel native root #{rootIndex}.");
        }

        return new T4XenonXModelCorpus
        {
            GraphRoot = graphRoot,
            BindingManifest = bindingManifest,
            OwnedByRoot = entries,
            ReferenceRoots = referenceRows.Length
        };
    }

    public IReadOnlyList<string> ValidateDependencies(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots)
    {
        var errors = new List<string>();

        foreach (var entry in OwnedByRoot.Values.OrderBy(x => x.NativeRootIndex))
        {
            var nativeRoot = roots.SingleOrDefault(x => x.Index == entry.NativeRootIndex);
            if (nativeRoot is null)
            {
                errors.Add($"{entry.CorpusName}: native root #{entry.NativeRootIndex} is missing.");
                continue;
            }

            if (!NormalizeType(nativeRoot.OatTypeName).Equals("xmodel", StringComparison.Ordinal))
                errors.Add($"{entry.CorpusName}: root #{entry.NativeRootIndex} is {nativeRoot.OatTypeName}, expected xmodel.");
            if (!nativeRoot.Name.Equals(entry.NativeName, StringComparison.Ordinal))
                errors.Add(
                    $"{entry.CorpusName}: binding native name '{entry.NativeName}' != identity '{nativeRoot.Name}'.");

            foreach (var material in entry.Graph.Materials.Where(x => !string.IsNullOrWhiteSpace(x)))
                ValidateAssetReference(roots, "material", material!, entry.CorpusName, errors);

            if (!string.IsNullOrWhiteSpace(entry.Graph.PhysPreset))
                ValidateAssetReference(roots, "physpreset", entry.Graph.PhysPreset!, entry.CorpusName, errors);
        }

        return errors;
    }

    private static void ValidateAssetReference(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        string type,
        string name,
        string owner,
        ICollection<string> errors)
    {
        var matches = FindReferenceCandidates(roots, type, name);
        if (matches.Count != 1)
            errors.Add($"{owner}: asset reference {type}:{name} resolves to {matches.Count} native root(s).");
    }

    internal static IReadOnlyList<T4XenonNativeIdentityEntry> FindReferenceCandidates(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        string type,
        string name)
    {
        var wantedType = NormalizeType(type);
        var typed = roots
            .Where(x => NormalizeType(x.OatTypeName) == wantedType)
            .ToList();

        // Tier 1: exact identity, including OAT's leading reference comma.
        // This is authoritative when the full native sequence already contains
        // the exact reference root emitted by standalone OAT XModel linking.
        var exact = typed
            .Where(x => NormalizeSlashes(x.Name).Equals(
                NormalizeSlashes(name),
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count != 0)
            return exact;

        // Tier 2: ignore only OAT's reference marker. This preserves a native
        // unsuffixed material when no comma-decorated reference root exists,
        // without collapsing it together with the Xbox-owned _wj variant.
        var noReference = typed
            .Where(x => StripReferenceMarker(x.Name).Equals(
                StripReferenceMarker(name),
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (noReference.Count != 0)
            return noReference;

        // Tier 3: Xbox identity fallback. Some standalone OAT references have
        // no corresponding reference/unsuffixed root in the full map; their
        // native material exists only as the _wj-owned identity. Use this only
        // after the stronger tiers above fail.
        var xboxEquivalent = typed
            .Where(x => StripXboxSuffix(StripReferenceMarker(x.Name)).Equals(
                StripXboxSuffix(StripReferenceMarker(name)),
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        return xboxEquivalent;
    }

    internal static string NormalizeType(string value) =>
        value.Replace("_", string.Empty, StringComparison.Ordinal)
             .Replace("-", string.Empty, StringComparison.Ordinal)
             .ToLowerInvariant();

    private static string NormalizeSlashes(string value) =>
        value.Trim().Replace('\\', '/');

    private static string StripReferenceMarker(string value)
    {
        var result = NormalizeSlashes(value);
        while (result.StartsWith(",", StringComparison.Ordinal))
            result = result[1..];
        return result;
    }

    private static string StripXboxSuffix(string value)
    {
        var result = NormalizeSlashes(value);
        if (result.EndsWith("_wj", StringComparison.OrdinalIgnoreCase))
            result = result[..^3];
        return result;
    }

    internal static string NormalizeReferenceName(string value) =>
        StripXboxSuffix(StripReferenceMarker(value));

    private static void ValidateGraph(
        T4XenonRelocatableXModelGraph graph,
        string expectedName,
        string graphFile)
    {
        if (graph.Format != "WaW360.T4Xenon.XModelRelocatableGraph.v1")
            throw new InvalidDataException($"{graphFile}: unexpected graph format '{graph.Format}'.");
        if (!graph.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"{graphFile}: graph name '{graph.Name}' != corpus binding '{expectedName}'.");
        if (graph.Abi.XModel != 0xD4)
            throw new InvalidDataException($"{graphFile}: XModel ABI 0x{graph.Abi.XModel:X} != 0xD4.");
        if (graph.Unsupported.Count != 0)
            throw new InvalidDataException($"{graphFile}: unsupported XModel branch count={graph.Unsupported.Count}.");
        if (graph.Reusable.AliasCount != 0)
            throw new InvalidDataException($"{graphFile}: reusable alias count={graph.Reusable.AliasCount}.");

        var alignments = new (string Name, int Value)[]
        {
            ("scriptString", graph.Alignments.ScriptString),
            ("parentList", graph.Alignments.ParentList),
            ("xmodelQuat", graph.Alignments.XModelQuat),
            ("trans", graph.Alignments.Trans),
            ("partClassification", graph.Alignments.PartClassification),
            ("dobjAnimMat", graph.Alignments.DObjAnimMat),
            ("xsurface", graph.Alignments.XSurface),
            ("materialHandle", graph.Alignments.MaterialHandle),
            ("boneInfo", graph.Alignments.BoneInfo),
            ("highMipBounds", graph.Alignments.HighMipBounds),
            ("vertsBlend", graph.Alignments.VertsBlend),
            ("verts0", graph.Alignments.Verts0),
            ("rigidVertList", graph.Alignments.RigidVertList),
            ("triIndices", graph.Alignments.TriIndices),
            ("collisionTree", graph.Alignments.CollisionTree),
            ("collisionNodes", graph.Alignments.CollisionNodes),
            ("collisionLeafs", graph.Alignments.CollisionLeafs)
        };

        foreach (var (name, value) in alignments)
        {
            if (value <= 0 || (value & (value - 1)) != 0)
                throw new InvalidDataException(
                    $"{graphFile}: invalid/missing power-of-two alignment {name}={value}.");
        }

        if (graph.BoneNames.Count != graph.Counts.NumBones)
            throw new InvalidDataException(
                $"{graphFile}: bone-name count {graph.BoneNames.Count} != numBones {graph.Counts.NumBones}.");
        if (graph.Presence.MaterialHandles && graph.Materials.Count != graph.Counts.NumSurfs)
            throw new InvalidDataException(
                $"{graphFile}: material count {graph.Materials.Count} != numsurfs {graph.Counts.NumSurfs}.");
        if (graph.Presence.Surfs && graph.Surfaces.Count != graph.Counts.NumSurfs)
            throw new InvalidDataException(
                $"{graphFile}: surface count {graph.Surfaces.Count} != numsurfs {graph.Counts.NumSurfs}.");
    }
}

internal sealed class T4XenonXModelCorpusEntry
{
    public required int NativeRootIndex { get; init; }
    public required int CorpusOrdinal { get; init; }
    public required string NativeName { get; init; }
    public required string CorpusName { get; init; }
    public required string GraphFile { get; init; }
    public required string DumpRoot { get; init; }
    public required T4XenonRelocatableXModelGraph Graph { get; init; }
}

internal static class T4XenonXModelGraphCodec
{
    public static void Plan(
        T4XenonXModelCorpusEntry entry,
        Action<long, int, bool> virtualAlloc,
        Action<int> tempRoot)
    {
        var g = entry.Graph;
        tempRoot(g.Abi.XModel);
        virtualAlloc(Utf8Z(entry.NativeName), 1, true);

        if (g.Presence.BoneNames)
            virtualAlloc(checked(g.BoneNames.Count * 2L), g.Alignments.ScriptString, true);

        PlanOptional(g.Presence.ParentList, g.Counts.NonRootBones, g.Alignments.ParentList, virtualAlloc);
        PlanOptional(g.Presence.Quats, checked((long)g.Counts.NonRootBones * g.Abi.XModelQuat), g.Alignments.XModelQuat, virtualAlloc);
        PlanOptional(g.Presence.Trans, checked((long)g.Counts.NonRootBones * 16L), g.Alignments.Trans, virtualAlloc);
        PlanOptional(g.Presence.PartClassification, g.Counts.NumBones, g.Alignments.PartClassification, virtualAlloc);
        PlanOptional(g.Presence.BaseMat, checked((long)g.Counts.NumBones * g.Abi.DObjAnimMat), g.Alignments.DObjAnimMat, virtualAlloc);
        PlanOptional(g.Presence.Surfs, checked((long)g.Counts.NumSurfs * g.Abi.XSurface), g.Alignments.XSurface, virtualAlloc);

        foreach (var surface in g.Surfaces.OrderBy(x => x.Index))
        {
            PlanOptional(surface.Presence.VertsBlend, checked((long)surface.VertsBlendCount * 2L), g.Alignments.VertsBlend, virtualAlloc);
            PlanOptional(surface.Presence.Verts0, checked((long)surface.VertCount * g.Abi.PackedVertex), g.Alignments.Verts0, virtualAlloc);
            PlanOptional(surface.Presence.VertList, checked((long)surface.VertListCount * g.Abi.RigidVertList), g.Alignments.RigidVertList, virtualAlloc);

            foreach (var tree in surface.CollisionTrees.OrderBy(x => x.VertListIndex))
            {
                PlanOptional(true, g.Abi.CollisionTree, g.Alignments.CollisionTree, virtualAlloc);
                PlanOptional(tree.Presence.Nodes, checked((long)tree.NodeCount * g.Abi.CollisionNode), g.Alignments.CollisionNodes, virtualAlloc);
                PlanOptional(tree.Presence.Leafs, checked((long)tree.LeafCount * g.Abi.CollisionLeaf), g.Alignments.CollisionLeafs, virtualAlloc);
            }

            PlanOptional(surface.Presence.TriIndices, checked((long)surface.TriCount * g.Abi.TriIndex), g.Alignments.TriIndices, virtualAlloc);
        }

        if (g.Presence.MaterialHandles)
            virtualAlloc(checked(g.Materials.Count * 4L), g.Alignments.MaterialHandle, true);

        PlanOptional(g.Presence.BoneInfo, checked((long)g.Counts.NumBones * g.Abi.BoneInfo), g.Alignments.BoneInfo, virtualAlloc);
        PlanOptional(g.Presence.HighMipBounds, checked((long)g.Counts.HighMipBounds * g.Abi.HighMipBounds), g.Alignments.HighMipBounds, virtualAlloc);
    }

    public static void Emit(
        T4XenonXModelCorpusEntry entry,
        Stream zone,
        Action<long, int, bool> virtualAlloc,
        Action<int> tempRoot,
        IReadOnlyList<T4ScriptStringEntry> scriptStrings,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyDictionary<int, uint> stableByRoot)
    {
        var g = entry.Graph;
        tempRoot(g.Abi.XModel);

        var rootBody = ReadBlob(entry.DumpRoot, g.RootFile, g.Abi.XModel, "XModel root");
        if (!string.IsNullOrWhiteSpace(g.PhysPreset))
        {
            var stable = ResolveAssetStablePointer(
                roots, stableByRoot, "physpreset", g.PhysPreset!);
            PutU32(rootBody, g.Offsets.PhysPreset, stable);
        }
        zone.Write(rootBody);

        EmitString(zone, virtualAlloc, entry.NativeName);

        if (g.Presence.BoneNames)
        {
            virtualAlloc(checked(g.BoneNames.Count * 2L), g.Alignments.ScriptString, true);
            foreach (var bone in g.BoneNames.OrderBy(x => x.Index))
                WriteU16(zone, ResolveScriptString(scriptStrings, bone.Name, entry.CorpusName));
        }

        EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, g.ParentListFile, g.Presence.ParentList, g.Counts.NonRootBones, g.Alignments.ParentList, "parentList");
        EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, g.QuatsFile, g.Presence.Quats, checked((long)g.Counts.NonRootBones * g.Abi.XModelQuat), g.Alignments.XModelQuat, "quats");
        EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, g.TransFile, g.Presence.Trans, checked((long)g.Counts.NonRootBones * 16L), g.Alignments.Trans, "trans");
        EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, g.PartClassificationFile, g.Presence.PartClassification, g.Counts.NumBones, g.Alignments.PartClassification, "partClassification");
        EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, g.BaseMatFile, g.Presence.BaseMat, checked((long)g.Counts.NumBones * g.Abi.DObjAnimMat), g.Alignments.DObjAnimMat, "baseMat");
        EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, g.SurfacesFile, g.Presence.Surfs, checked((long)g.Counts.NumSurfs * g.Abi.XSurface), g.Alignments.XSurface, "surfaces");

        foreach (var surface in g.Surfaces.OrderBy(x => x.Index))
        {
            EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, surface.VertsBlendFile, surface.Presence.VertsBlend, checked((long)surface.VertsBlendCount * 2L), g.Alignments.VertsBlend, $"surface[{surface.Index}].vertsBlend");
            EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, surface.Verts0File, surface.Presence.Verts0, checked((long)surface.VertCount * g.Abi.PackedVertex), g.Alignments.Verts0, $"surface[{surface.Index}].verts0");
            EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, surface.VertListFile, surface.Presence.VertList, checked((long)surface.VertListCount * g.Abi.RigidVertList), g.Alignments.RigidVertList, $"surface[{surface.Index}].vertList");

            foreach (var tree in surface.CollisionTrees.OrderBy(x => x.VertListIndex))
            {
                EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, tree.TreeFile, true, g.Abi.CollisionTree, g.Alignments.CollisionTree, $"surface[{surface.Index}].tree[{tree.VertListIndex}]");
                EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, tree.NodesFile, tree.Presence.Nodes, checked((long)tree.NodeCount * g.Abi.CollisionNode), g.Alignments.CollisionNodes, $"surface[{surface.Index}].tree[{tree.VertListIndex}].nodes");
                EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, tree.LeafsFile, tree.Presence.Leafs, checked((long)tree.LeafCount * g.Abi.CollisionLeaf), g.Alignments.CollisionLeafs, $"surface[{surface.Index}].tree[{tree.VertListIndex}].leafs");
            }

            EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, surface.TriIndicesFile, surface.Presence.TriIndices, checked((long)surface.TriCount * g.Abi.TriIndex), g.Alignments.TriIndices, $"surface[{surface.Index}].triIndices");
        }

        if (g.Presence.MaterialHandles)
        {
            virtualAlloc(checked(g.Materials.Count * 4L), g.Alignments.MaterialHandle, true);
            foreach (var material in g.Materials)
            {
                WriteU32(
                    zone,
                    string.IsNullOrWhiteSpace(material)
                        ? 0u
                        : ResolveAssetStablePointer(
                            roots, stableByRoot, "material", material!));
            }
        }

        EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, g.BoneInfoFile, g.Presence.BoneInfo, checked((long)g.Counts.NumBones * g.Abi.BoneInfo), g.Alignments.BoneInfo, "boneInfo");
        EmitOptionalBlob(zone, virtualAlloc, entry.DumpRoot, g.HighMipBoundsFile, g.Presence.HighMipBounds, checked((long)g.Counts.HighMipBounds * g.Abi.HighMipBounds), g.Alignments.HighMipBounds, "highMipBounds");
    }

    private static void PlanOptional(
        bool pointerPresent,
        long size,
        int alignment,
        Action<long, int, bool> virtualAlloc)
    {
        if (!pointerPresent)
            return;
        virtualAlloc(size, alignment, true);
    }

    private static void EmitOptionalBlob(
        Stream zone,
        Action<long, int, bool> virtualAlloc,
        string graphRoot,
        string? relative,
        bool pointerPresent,
        long expectedBytes,
        int alignment,
        string label)
    {
        if (!pointerPresent)
        {
            if (!string.IsNullOrWhiteSpace(relative))
                throw new InvalidDataException(
                    $"{label}: pointer is null but graph contains blob path '{relative}'.");
            return;
        }

        virtualAlloc(expectedBytes, alignment, true);

        if (expectedBytes == 0)
        {
            if (!string.IsNullOrWhiteSpace(relative))
            {
                var empty = ReadBlob(graphRoot, relative!, -1, label);
                if (empty.Length != 0)
                    throw new InvalidDataException(
                        $"{label}: graph points to {empty.Length:N0} bytes but expected zero.");
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(relative))
            throw new InvalidDataException($"{label}: graph blob path is missing.");

        zone.Write(ReadBlob(graphRoot, relative!, expectedBytes, label));
    }

    private static ushort ResolveScriptString(
        IReadOnlyList<T4ScriptStringEntry> strings,
        string value,
        string owner)
    {
        var matches = strings
            .Where(x => x.Value is not null &&
                        x.Value.Equals(value, StringComparison.Ordinal))
            .Select(x => x.Index)
            .Distinct()
            .ToArray();

        if (matches.Length != 1)
            throw new InvalidDataException(
                $"{owner}: bone ScriptString '{value}' resolves to {matches.Length} native entries.");

        return checked((ushort)matches[0]);
    }

    private static uint ResolveAssetStablePointer(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyDictionary<int, uint> stableByRoot,
        string type,
        string name)
    {
        var candidates = T4XenonXModelCorpus.FindReferenceCandidates(roots, type, name);
        if (candidates.Count != 1)
            throw new InvalidDataException(
                $"Asset reference {type}:{name} resolved to {candidates.Count} root(s).");

        if (!stableByRoot.TryGetValue(candidates[0].Index, out var stable))
            throw new InvalidDataException(
                $"Asset reference {type}:{name} root #{candidates[0].Index} has no planned insert pointer.");

        return stable;
    }

    private static byte[] ReadBlob(
        string root,
        string relative,
        long expectedBytes,
        string label)
    {
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Graph path escapes dump root: {relative}");
        if (!File.Exists(full))
            throw new FileNotFoundException($"{label}: graph blob not found.", full);

        var bytes = File.ReadAllBytes(full);
        if (expectedBytes >= 0 && bytes.LongLength != expectedBytes)
            throw new InvalidDataException(
                $"{label}: blob is {bytes.LongLength:N0} bytes; expected {expectedBytes:N0}.");
        return bytes;
    }

    private static void EmitString(
        Stream zone,
        Action<long, int, bool> virtualAlloc,
        string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        virtualAlloc(bytes.Length + 1L, 1, true);
        zone.Write(bytes);
        zone.WriteByte(0);
    }

    private static int Utf8Z(string value) => Encoding.UTF8.GetByteCount(value) + 1;

    private static void PutU32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);

    private static void WriteU16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}

internal sealed class T4XenonRelocatableXModelGraph
{
    public string Format { get; set; } = "";
    public string Name { get; set; } = "";
    public T4XenonXModelAbiData Abi { get; set; } = new();
    public T4XenonXModelAlignmentData Alignments { get; set; } = new();
    public T4XenonXModelOffsetData Offsets { get; set; } = new();
    public T4XenonXModelCountData Counts { get; set; } = new();
    public T4XenonXModelUnsupportedData Unsupported { get; set; } = new();
    public T4XenonXModelReusableData Reusable { get; set; } = new();
    public T4XenonXModelRootPresenceData Presence { get; set; } = new();
    public string RootFile { get; set; } = "";
    public List<T4XenonXModelBoneData> BoneNames { get; set; } = new();
    public List<string?> Materials { get; set; } = new();
    public string? PhysPreset { get; set; }
    public string? ParentListFile { get; set; }
    public string? QuatsFile { get; set; }
    public string? TransFile { get; set; }
    public string? PartClassificationFile { get; set; }
    public string? BaseMatFile { get; set; }
    public string? SurfacesFile { get; set; }
    public string? BoneInfoFile { get; set; }
    public string? HighMipBoundsFile { get; set; }
    public List<T4XenonXModelSurfaceData> Surfaces { get; set; } = new();
}

internal sealed class T4XenonXModelAbiData
{
    public int XModel { get; set; }
    public int XSurface { get; set; }
    public int XModelQuat { get; set; }
    public int DObjAnimMat { get; set; }
    public int PackedVertex { get; set; }
    public int RigidVertList { get; set; }
    public int CollisionTree { get; set; }
    public int CollisionNode { get; set; }
    public int CollisionLeaf { get; set; }
    public int TriIndex { get; set; }
    public int BoneInfo { get; set; }
    public int HighMipBounds { get; set; }
}

internal sealed class T4XenonXModelAlignmentData
{
    public int ScriptString { get; set; }
    public int ParentList { get; set; }
    public int XModelQuat { get; set; }
    public int Trans { get; set; }
    public int PartClassification { get; set; }
    public int DObjAnimMat { get; set; }
    public int XSurface { get; set; }
    public int MaterialHandle { get; set; }
    public int BoneInfo { get; set; }
    public int HighMipBounds { get; set; }
    public int VertsBlend { get; set; }
    public int Verts0 { get; set; }
    public int RigidVertList { get; set; }
    public int TriIndices { get; set; }
    public int CollisionTree { get; set; }
    public int CollisionNodes { get; set; }
    public int CollisionLeafs { get; set; }
}

internal sealed class T4XenonXModelRootPresenceData
{
    public bool BoneNames { get; set; }
    public bool ParentList { get; set; }
    public bool Quats { get; set; }
    public bool Trans { get; set; }
    public bool PartClassification { get; set; }
    public bool BaseMat { get; set; }
    public bool Surfs { get; set; }
    public bool MaterialHandles { get; set; }
    public bool BoneInfo { get; set; }
    public bool HighMipBounds { get; set; }
}

internal sealed class T4XenonXModelOffsetData
{
    public int PhysPreset { get; set; }
}

internal sealed class T4XenonXModelCountData
{
    public int NumBones { get; set; }
    public int NumRootBones { get; set; }
    public int NonRootBones { get; set; }
    public int NumSurfs { get; set; }
    public int NumLods { get; set; }
    public int HighMipBounds { get; set; }
}

internal sealed class T4XenonXModelUnsupportedData
{
    public int Count { get; set; }
}

internal sealed class T4XenonXModelReusableData
{
    public int AliasCount { get; set; }
}

internal sealed class T4XenonXModelBoneData
{
    public int Index { get; set; }
    public int SourceScriptString { get; set; }
    public string Name { get; set; } = "";
}

internal sealed class T4XenonXModelSurfaceData
{
    public int Index { get; set; }
    public int VertCount { get; set; }
    public int TriCount { get; set; }
    public int VertListCount { get; set; }
    public int VertsBlendCount { get; set; }
    public T4XenonXModelSurfacePresenceData Presence { get; set; } = new();
    public string? VertsBlendFile { get; set; }
    public string? Verts0File { get; set; }
    public string? VertListFile { get; set; }
    public string? TriIndicesFile { get; set; }
    public List<T4XenonXModelCollisionTreeData> CollisionTrees { get; set; } = new();
}

internal sealed class T4XenonXModelSurfacePresenceData
{
    public bool VertsBlend { get; set; }
    public bool Verts0 { get; set; }
    public bool VertList { get; set; }
    public bool TriIndices { get; set; }
}

internal sealed class T4XenonXModelCollisionTreePresenceData
{
    public bool Nodes { get; set; }
    public bool Leafs { get; set; }
}

internal sealed class T4XenonXModelCollisionTreeData
{
    public int VertListIndex { get; set; }
    public int NodeCount { get; set; }
    public int LeafCount { get; set; }
    public T4XenonXModelCollisionTreePresenceData Presence { get; set; } = new();
    public string TreeFile { get; set; } = "";
    public string? NodesFile { get; set; }
    public string? LeafsFile { get; set; }
}
