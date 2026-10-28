using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

internal sealed class T4XenonMaterialCorpus
{
    public required string GraphRoot { get; init; }
    public required IReadOnlyDictionary<int, T4XenonMaterialCorpusEntry> OwnedByRoot { get; init; }
    public required int ReferenceRoots { get; init; }

    public static T4XenonMaterialCorpus Load(
        string graphRoot,
        string bindingManifest,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        int expectedOwned = 502)
    {
        graphRoot = Path.GetFullPath(graphRoot);
        bindingManifest = Path.GetFullPath(bindingManifest);

        if (!Directory.Exists(graphRoot))
            throw new DirectoryNotFoundException($"Material graph root not found: {graphRoot}");
        if (!File.Exists(bindingManifest))
            throw new FileNotFoundException(
                "Material root-binding manifest not found.",
                bindingManifest);

        using var bindingDoc = JsonDocument.Parse(File.ReadAllText(bindingManifest));
        var binding = bindingDoc.RootElement;
        if (!binding.TryGetProperty("BindingPass", out var bindingPass) ||
            !bindingPass.GetBoolean())
            throw new InvalidDataException(
                "Material root-binding manifest is not passing.");

        var bindingRows = binding.GetProperty("OwnedBindings")
            .EnumerateArray()
            .ToArray();

        var bridgeManifest = binding.GetProperty("BridgeManifest").GetString()
            ?? throw new InvalidDataException(
                "Material root-binding manifest does not identify its bridge manifest.");
        using var bridgeDoc = JsonDocument.Parse(File.ReadAllText(bridgeManifest));
        var techniqueMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (bridgeDoc.RootElement.TryGetProperty("Mappings", out var mappings) &&
            mappings.ValueKind == JsonValueKind.Array)
        {
            foreach (var mapRow in mappings.EnumerateArray())
            {
                var pc = mapRow.GetProperty("PcTechniqueSet").GetString() ?? "";
                var xbox = mapRow.GetProperty("XboxTechniqueSet").GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(pc) && !string.IsNullOrWhiteSpace(xbox))
                    techniqueMap[pc] = xbox;
            }
        }
        if (bindingRows.Length != expectedOwned)
            throw new InvalidDataException(
                $"Material binding count {bindingRows.Length:N0} != expected {expectedOwned:N0}.");

        var graphDirectory = Path.Combine(graphRoot, "xenon_material_graphs");
        if (!Directory.Exists(graphDirectory))
            throw new DirectoryNotFoundException(
                $"Material graph directory not found: {graphDirectory}");

        var graphsByName = new Dictionary<string, (string File, T4XenonRelocatableMaterialGraph Graph)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var graphFile in Directory.GetFiles(
                     graphDirectory,
                     "*.json",
                     SearchOption.AllDirectories))
        {
            var graph = JsonSerializer.Deserialize<T4XenonRelocatableMaterialGraph>(
                File.ReadAllText(graphFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException(
                    $"Could not parse Material graph: {graphFile}");

            ValidateGraph(graph, graphFile);
            if (!graphsByName.TryAdd(graph.Name, (graphFile, graph)))
                throw new InvalidDataException(
                    $"Duplicate Material graph identity '{graph.Name}'.");
        }

        if (graphsByName.Count != expectedOwned)
            throw new InvalidDataException(
                $"Material graph count {graphsByName.Count:N0} != expected {expectedOwned:N0}.");

        var entries = new Dictionary<int, T4XenonMaterialCorpusEntry>();
        foreach (var row in bindingRows)
        {
            var rootIndex = row.GetProperty("NativeRootIndex").GetInt32();
            var nativeName = row.GetProperty("NativeName").GetString() ?? "";
            var corpusName = row.GetProperty("CorpusName").GetString() ?? "";

            if (!graphsByName.TryGetValue(corpusName, out var graphRow))
                throw new InvalidDataException(
                    $"Material binding references missing graph '{corpusName}'.");

            var nativeRoot = roots.SingleOrDefault(x => x.Index == rootIndex)
                ?? throw new InvalidDataException(
                    $"Material binding root #{rootIndex} is missing from native identity.");

            if (T4XenonXModelCorpus.NormalizeType(nativeRoot.OatTypeName) != "material")
                throw new InvalidDataException(
                    $"Material binding root #{rootIndex} is {nativeRoot.OatTypeName}, expected material.");

            if (!nativeRoot.Name.Equals(nativeName, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Material binding root #{rootIndex} name '{nativeName}' != native identity '{nativeRoot.Name}'.");

            if (!entries.TryAdd(
                    rootIndex,
                    new T4XenonMaterialCorpusEntry
                    {
                        NativeRootIndex = rootIndex,
                        NativeName = nativeName,
                        CorpusName = corpusName,
                        GraphFile = graphRow.File,
                        DumpRoot = graphRoot,
                        ProductionTechniqueSet =
                            !string.IsNullOrWhiteSpace(graphRow.Graph.TechniqueSet) &&
                            techniqueMap.TryGetValue(graphRow.Graph.TechniqueSet!, out var mappedTech)
                                ? mappedTech
                                : graphRow.Graph.TechniqueSet,
                        Graph = graphRow.Graph
                    }))
                throw new InvalidDataException(
                    $"Duplicate Material binding for native root #{rootIndex}.");
        }

        var nativeMaterialRoots = roots.Count(
            x => T4XenonXModelCorpus.NormalizeType(x.OatTypeName) == "material");

        return new T4XenonMaterialCorpus
        {
            GraphRoot = graphRoot,
            OwnedByRoot = entries,
            ReferenceRoots = nativeMaterialRoots - entries.Count
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

            if (T4XenonXModelCorpus.NormalizeType(nativeRoot.OatTypeName) != "material")
                errors.Add(
                    $"{entry.CorpusName}: root #{entry.NativeRootIndex} is {nativeRoot.OatTypeName}, expected material.");

            if (!string.IsNullOrWhiteSpace(entry.ProductionTechniqueSet))
                ValidateReference(
                    roots,
                    new[] { "techset", "techniqueset" },
                    entry.ProductionTechniqueSet!,
                    entry.CorpusName,
                    errors);

            foreach (var texture in entry.Graph.Textures)
            {
                if (!string.IsNullOrWhiteSpace(texture.Image))
                    ValidateReference(
                        roots,
                        new[] { "image" },
                        texture.Image!,
                        entry.CorpusName,
                        errors);
            }
        }

        return errors;
    }

    public IReadOnlyList<T4XenonOatExternalReference> CollectExternalReferences(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots)
    {
        // Preserve occurrences, not just unique identities. OAT's loaded-asset
        // listing can contain the same external reference more than once when
        // multiple Material fields serialize independent reference shells.
        var result = new List<T4XenonOatExternalReference>();

        foreach (var entry in OwnedByRoot.Values.OrderBy(x => x.NativeRootIndex))
        {
            if (!string.IsNullOrWhiteSpace(entry.ProductionTechniqueSet))
            {
                var name = entry.ProductionTechniqueSet!;
                var matches = FindDependencyCandidates(
                    roots,
                    new[] { "techset", "techniqueset" },
                    name);
                if (matches.Count == 0 && IsExplicitReference(name))
                {
                    result.Add(new T4XenonOatExternalReference(
                        "techniqueset",
                        ReferenceAssetName(name)));
                }
            }

            foreach (var texture in entry.Graph.Textures)
            {
                if (string.IsNullOrWhiteSpace(texture.Image))
                    continue;

                var name = texture.Image!;
                var matches = FindDependencyCandidates(
                    roots,
                    new[] { "image" },
                    name);
                if (matches.Count == 0 && IsExplicitReference(name))
                {
                    result.Add(new T4XenonOatExternalReference(
                        "image",
                        ReferenceAssetName(name)));
                }
            }
        }

        return result;
    }

    internal static uint ResolveStablePointer(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyDictionary<int, uint> stableByRoot,
        IReadOnlyCollection<string> acceptedTypes,
        string name)
    {
        var matches = FindCandidates(roots, acceptedTypes, name);
        if (matches.Count != 1)
            throw new InvalidDataException(
                $"Asset reference {string.Join("/", acceptedTypes)}:{name} resolves to {matches.Count} native root(s).");

        if (!stableByRoot.TryGetValue(matches[0].Index, out var stable))
            throw new InvalidDataException(
                $"Asset reference {matches[0].OatTypeName}:{name} has no stable root pointer.");

        return stable;
    }

    private static void ValidateReference(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyCollection<string> types,
        string name,
        string owner,
        ICollection<string> errors)
    {
        var matches = FindCandidates(roots, types, name);
        if (matches.Count == 1)
            return;

        // A comma-prefixed OAT dependency is an explicit external/reference
        // asset. It is valid for the full native zone to have no top-level
        // root for it; production emission writes an inline reference shell.
        if (matches.Count == 0 && IsExplicitReference(name))
            return;

        errors.Add(
            $"{owner}: asset reference {string.Join("/", types)}:{name} resolves to {matches.Count} native root(s).");
    }

    internal static bool IsExplicitReference(string name)
    {
        var value = name.Trim();
        return value.StartsWith(",", StringComparison.Ordinal);
    }

    internal static string ReferenceAssetName(string name)
    {
        var value = name.Trim();
        while (value.StartsWith(",", StringComparison.Ordinal))
            value = value[1..];
        return "," + value;
    }

    internal static IReadOnlyList<T4XenonNativeIdentityEntry> FindDependencyCandidates(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyCollection<string> acceptedTypes,
        string name) =>
        FindCandidates(roots, acceptedTypes, name);

    private static IReadOnlyList<T4XenonNativeIdentityEntry> FindCandidates(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyCollection<string> acceptedTypes,
        string name)
    {
        var result = new List<T4XenonNativeIdentityEntry>();
        foreach (var type in acceptedTypes)
        {
            foreach (var candidate in T4XenonXModelCorpus.FindReferenceCandidates(
                         roots,
                         type,
                         name))
            {
                if (!result.Any(x => x.Index == candidate.Index))
                    result.Add(candidate);
            }
        }
        return result;
    }

    private static void ValidateGraph(
        T4XenonRelocatableMaterialGraph graph,
        string graphFile)
    {
        if (graph.Format != "WaW360.T4Xenon.MaterialRelocatableGraph.v1")
            throw new InvalidDataException(
                $"{graphFile}: unexpected Material graph format '{graph.Format}'.");
        if (graph.Abi.Material != 96)
            throw new InvalidDataException(
                $"{graphFile}: Material ABI {graph.Abi.Material} != 96.");
        if (graph.Reusable.AliasCount != 0)
            throw new InvalidDataException(
                $"{graphFile}: reusable alias count={graph.Reusable.AliasCount}.");
        if (graph.Inventory.WaterTextures != 0 ||
            graph.Inventory.WaterSamples != 0 ||
            graph.Inventory.InvalidWaterDimensions != 0)
            throw new InvalidDataException(
                $"{graphFile}: production simple path requires zero water branches.");
        if (graph.Counts.Textures != graph.Textures.Count)
            throw new InvalidDataException(
                $"{graphFile}: texture count {graph.Textures.Count} != {graph.Counts.Textures}.");

        foreach (var (name, value) in new (string Name, int Value)[]
        {
            ("material", graph.Alignments.Material),
            ("textureDef", graph.Alignments.TextureDef),
            ("constantDef", graph.Alignments.ConstantDef),
            ("stateBits", graph.Alignments.StateBits)
        })
        {
            if (value <= 0 || (value & (value - 1)) != 0)
                throw new InvalidDataException(
                    $"{graphFile}: invalid power-of-two alignment {name}={value}.");
        }
    }
}

internal sealed class T4XenonMaterialCorpusEntry
{
    public required int NativeRootIndex { get; init; }
    public required string NativeName { get; init; }
    public required string CorpusName { get; init; }
    public required string GraphFile { get; init; }
    public required string DumpRoot { get; init; }
    public required string? ProductionTechniqueSet { get; init; }
    public required T4XenonRelocatableMaterialGraph Graph { get; init; }
}

internal static class T4XenonMaterialGraphCodec
{
    public static void Plan(
        T4XenonMaterialCorpusEntry entry,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        Action<long, int, bool> virtualAlloc,
        Action<int> tempRoot)
    {
        var g = entry.Graph;
        var dependencyPlan = BuildDependencyPlan(entry, roots);

        // TEMP is one scratch scope per top-level Material. Referenced child
        // assets are serialized into that same scope after the Material body.
        tempRoot(dependencyPlan.TempScope);

        // Material::info.name
        virtualAlloc(Utf8Z(entry.NativeName), 1, true);

        // Material::techniqueSet is traversed before textureTable.
        if (dependencyPlan.Technique.IsExternal)
            virtualAlloc(
                Utf8Z(T4XenonMaterialCorpus.ReferenceAssetName(entry.ProductionTechniqueSet!)),
                1,
                true);

        if (g.Presence.TextureTable)
        {
            virtualAlloc(
                checked((long)g.Counts.Textures * g.Abi.TextureDef),
                g.Alignments.TextureDef,
                true);

            // The texture array body is written first, then each embedded
            // MaterialTextureDef asset pointer is traversed in index order.
            for (var i = 0; i < g.Textures.Count; i++)
            {
                if (!dependencyPlan.Images[i].IsExternal)
                    continue;

                virtualAlloc(
                    Utf8Z(T4XenonMaterialCorpus.ReferenceAssetName(g.Textures[i].Image!)),
                    1,
                    true);
            }
        }
        else if (g.Counts.Textures != 0)
        {
            throw new InvalidDataException(
                $"{entry.CorpusName}: null texture table with {g.Counts.Textures} texture(s).");
        }

        PlanOptional(
            g.Presence.ConstantTable,
            checked((long)g.Counts.Constants * g.Abi.ConstantDef),
            g.Alignments.ConstantDef,
            virtualAlloc);
        PlanOptional(
            g.Presence.StateBitsTable,
            checked((long)g.Counts.StateBits * g.Abi.StateBits),
            g.Alignments.StateBits,
            virtualAlloc);
    }

    public static void Emit(
        T4XenonMaterialCorpusEntry entry,
        Stream zone,
        Action<long, int, bool> virtualAlloc,
        Action<int> tempRoot,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyDictionary<int, uint> stableByRoot)
    {
        var g = entry.Graph;
        var dependencyPlan = BuildDependencyPlan(entry, roots);
        tempRoot(dependencyPlan.TempScope);

        var rootBody = ReadBlob(
            entry.DumpRoot,
            g.RootFile,
            g.Abi.Material,
            $"{entry.CorpusName} root");

        var techniqueResolution = dependencyPlan.Technique;
        if (!string.IsNullOrWhiteSpace(entry.ProductionTechniqueSet))
        {
            PutU32(
                rootBody,
                g.Offsets.TechniqueSet,
                techniqueResolution.IsExternal
                    ? T4ZonePointer.Following
                    : ResolveStablePointer(
                        stableByRoot,
                        techniqueResolution,
                        "techset/techniqueset",
                        entry.ProductionTechniqueSet!));
        }

        zone.Write(rootBody);
        EmitString(zone, virtualAlloc, entry.NativeName);

        if (techniqueResolution.IsExternal)
            EmitReferenceShell(
                zone,
                virtualAlloc,
                shellSize: 216,
                nameOffset: 0,
                referenceName: T4XenonMaterialCorpus.ReferenceAssetName(entry.ProductionTechniqueSet!));

        if (g.Presence.TextureTable)
        {
            var expected = checked((long)g.Counts.Textures * g.Abi.TextureDef);
            if (string.IsNullOrWhiteSpace(g.TextureTableFile))
                throw new InvalidDataException(
                    $"{entry.CorpusName}: texture table blob path is missing.");

            var textureBytes = ReadBlob(
                entry.DumpRoot,
                g.TextureTableFile!,
                expected,
                $"{entry.CorpusName} textures");

            var imageResolutions = dependencyPlan.Images;
            for (var i = 0; i < g.Textures.Count; i++)
            {
                var texture = g.Textures[i];
                if (texture.Water.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                    throw new InvalidDataException(
                        $"{entry.CorpusName}: texture[{i}] contains a water graph.");

                if (string.IsNullOrWhiteSpace(texture.Image))
                    continue;

                var resolution = imageResolutions[i];

                PutU32(
                    textureBytes,
                    checked(i * g.Abi.TextureDef + g.Offsets.TextureUnion),
                    resolution.IsExternal
                        ? T4ZonePointer.Following
                        : ResolveStablePointer(
                            stableByRoot,
                            resolution,
                            "image",
                            texture.Image!));
            }

            virtualAlloc(
                textureBytes.LongLength,
                g.Alignments.TextureDef,
                true);
            zone.Write(textureBytes);

            // ZoneCode writes the entire MaterialTextureDef array first, then
            // traverses each image pointer in element order.
            for (var i = 0; i < g.Textures.Count; i++)
            {
                if (!imageResolutions[i].IsExternal)
                    continue;

                EmitReferenceShell(
                    zone,
                    virtualAlloc,
                    shellSize: 40,
                    nameOffset: 36,
                    referenceName: T4XenonMaterialCorpus.ReferenceAssetName(g.Textures[i].Image!));
            }
        }
        else if (g.Counts.Textures != 0)
        {
            throw new InvalidDataException(
                $"{entry.CorpusName}: null texture table with {g.Counts.Textures} texture(s).");
        }

        EmitOptionalBlob(
            zone,
            virtualAlloc,
            entry.DumpRoot,
            g.ConstantTableFile,
            g.Presence.ConstantTable,
            checked((long)g.Counts.Constants * g.Abi.ConstantDef),
            g.Alignments.ConstantDef,
            $"{entry.CorpusName} constants");

        EmitOptionalBlob(
            zone,
            virtualAlloc,
            entry.DumpRoot,
            g.StateBitsTableFile,
            g.Presence.StateBitsTable,
            checked((long)g.Counts.StateBits * g.Abi.StateBits),
            g.Alignments.StateBits,
            $"{entry.CorpusName} stateBits");
    }

    private sealed record MaterialDependencyPlan(
        int TempScope,
        DependencyResolution Technique,
        DependencyResolution[] Images);

    private static MaterialDependencyPlan BuildDependencyPlan(
        T4XenonMaterialCorpusEntry entry,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots)
    {
        var g = entry.Graph;
        var tempScope = g.Abi.Material;
        var technique = DependencyResolution.None;

        if (!string.IsNullOrWhiteSpace(entry.ProductionTechniqueSet))
        {
            technique = ResolveDependency(
                roots,
                new[] { "techset", "techniqueset" },
                entry.ProductionTechniqueSet!);
            if (technique.IsExternal)
                tempScope = checked(tempScope + 216);
        }

        var images = new DependencyResolution[g.Textures.Count];
        for (var i = 0; i < g.Textures.Count; i++)
        {
            var image = g.Textures[i].Image;
            if (string.IsNullOrWhiteSpace(image))
                continue;

            images[i] = ResolveDependency(
                roots,
                new[] { "image" },
                image!);
            if (images[i].IsExternal)
                tempScope = checked(tempScope + 40);
        }

        return new MaterialDependencyPlan(
            tempScope,
            technique,
            images);
    }

    private readonly record struct DependencyResolution(
        bool IsExternal,
        int RootIndex)
    {
        public static DependencyResolution None => new(false, -1);
    }

    private static DependencyResolution ResolveDependency(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyCollection<string> acceptedTypes,
        string name)
    {
        var matches = T4XenonMaterialCorpus.FindDependencyCandidates(
            roots,
            acceptedTypes,
            name);

        if (matches.Count == 1)
            return new DependencyResolution(false, matches[0].Index);

        if (matches.Count == 0 &&
            T4XenonMaterialCorpus.IsExplicitReference(name))
            return new DependencyResolution(true, -1);

        throw new InvalidDataException(
            $"Asset reference {string.Join("/", acceptedTypes)}:{name} resolves to {matches.Count} native root(s) and is not a valid external reference.");
    }

    private static uint ResolveStablePointer(
        IReadOnlyDictionary<int, uint> stableByRoot,
        DependencyResolution resolution,
        string type,
        string name)
    {
        if (resolution.IsExternal || resolution.RootIndex < 0)
            throw new InvalidOperationException(
                $"External asset reference {type}:{name} has no root stable pointer.");

        if (!stableByRoot.TryGetValue(resolution.RootIndex, out var stable))
            throw new InvalidDataException(
                $"Asset reference {type}:{name} root #{resolution.RootIndex} has no planned insert pointer.");

        return stable;
    }

    private static void EmitReferenceShell(
        Stream zone,
        Action<long, int, bool> virtualAlloc,
        int shellSize,
        int nameOffset,
        string referenceName)
    {
        var body = new byte[shellSize];
        PutU32(body, nameOffset, T4ZonePointer.Following);
        zone.Write(body);
        EmitString(zone, virtualAlloc, referenceName);
    }

    private static void PlanOptional(
        bool present,
        long bytes,
        int alignment,
        Action<long, int, bool> virtualAlloc)
    {
        if (!present)
        {
            if (bytes != 0)
                throw new InvalidDataException(
                    $"Material graph has null pointer with required payload {bytes:N0} bytes.");
            return;
        }

        virtualAlloc(bytes, alignment, true);
    }

    private static void EmitOptionalBlob(
        Stream zone,
        Action<long, int, bool> virtualAlloc,
        string graphRoot,
        string? relative,
        bool present,
        long expectedBytes,
        int alignment,
        string label)
    {
        if (!present)
        {
            if (expectedBytes != 0)
                throw new InvalidDataException(
                    $"{label}: pointer is null but count requires {expectedBytes:N0} bytes.");
            if (!string.IsNullOrWhiteSpace(relative))
                throw new InvalidDataException(
                    $"{label}: pointer is null but graph contains '{relative}'.");
            return;
        }

        virtualAlloc(expectedBytes, alignment, true);
        if (expectedBytes == 0)
            return;

        if (string.IsNullOrWhiteSpace(relative))
            throw new InvalidDataException($"{label}: graph blob path is missing.");

        zone.Write(ReadBlob(graphRoot, relative!, expectedBytes, label));
    }

    private static byte[] ReadBlob(
        string graphRoot,
        string relative,
        long expectedBytes,
        string label)
    {
        var full = ResolveGraphPath(graphRoot, relative);
        if (!File.Exists(full))
            throw new FileNotFoundException($"{label} blob not found.", full);

        var bytes = File.ReadAllBytes(full);
        if (bytes.LongLength != expectedBytes)
            throw new InvalidDataException(
                $"{label}: {bytes.LongLength:N0} bytes, expected {expectedBytes:N0}.");
        return bytes;
    }

    private static string ResolveGraphPath(string root, string relative)
    {
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootFull = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Graph path escapes dump root: {relative}");
        return full;
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

    private static int Utf8Z(string value) =>
        Encoding.UTF8.GetByteCount(value) + 1;

    private static void PutU32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(
            bytes.AsSpan(offset, 4),
            value);
}

internal sealed class T4XenonRelocatableMaterialGraph
{
    public string Format { get; set; } = "";
    public string Name { get; set; } = "";
    public T4XenonMaterialAbiData Abi { get; set; } = new();
    public T4XenonMaterialAlignmentData Alignments { get; set; } = new();
    public T4XenonMaterialOffsetData Offsets { get; set; } = new();
    public T4XenonMaterialCountData Counts { get; set; } = new();
    public T4XenonMaterialPresenceData Presence { get; set; } = new();
    public T4XenonMaterialReusableData Reusable { get; set; } = new();
    public string? TechniqueSet { get; set; }
    public string RootFile { get; set; } = "";
    public string? TextureTableFile { get; set; }
    public string? ConstantTableFile { get; set; }
    public string? StateBitsTableFile { get; set; }
    public List<T4XenonMaterialTextureData> Textures { get; set; } = new();
    public T4XenonMaterialInventoryData Inventory { get; set; } = new();
}

internal sealed class T4XenonMaterialAbiData
{
    public int Material { get; set; }
    public int TextureDef { get; set; }
    public int ConstantDef { get; set; }
    public int StateBits { get; set; }
    public int Water { get; set; }
    public int Float { get; set; }
}

internal sealed class T4XenonMaterialAlignmentData
{
    public int Material { get; set; }
    public int TextureDef { get; set; }
    public int ConstantDef { get; set; }
    public int StateBits { get; set; }
    public int Water { get; set; }
    public int Float { get; set; }
}

internal sealed class T4XenonMaterialOffsetData
{
    public int TechniqueSet { get; set; }
    public int TextureUnion { get; set; }
    public int WaterImage { get; set; }
}

internal sealed class T4XenonMaterialCountData
{
    public int Textures { get; set; }
    public int Constants { get; set; }
    public int StateBits { get; set; }
}

internal sealed class T4XenonMaterialPresenceData
{
    public bool Name { get; set; }
    public bool TextureTable { get; set; }
    public bool ConstantTable { get; set; }
    public bool StateBitsTable { get; set; }
}

internal sealed class T4XenonMaterialReusableData
{
    public int UniquePointers { get; set; }
    public int AliasCount { get; set; }
}

internal sealed class T4XenonMaterialTextureData
{
    public int Index { get; set; }
    public int Semantic { get; set; }
    public string? Image { get; set; }
    public JsonElement Water { get; set; }
}

internal sealed class T4XenonMaterialInventoryData
{
    public int WaterTextures { get; set; }
    public long WaterSamples { get; set; }
    public int InvalidWaterDimensions { get; set; }
}
