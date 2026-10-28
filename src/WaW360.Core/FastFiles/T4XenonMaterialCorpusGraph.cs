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
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        int expectedOwned = 502)
    {
        graphRoot = Path.GetFullPath(graphRoot);
        if (!Directory.Exists(graphRoot))
            throw new DirectoryNotFoundException($"Material graph root not found: {graphRoot}");

        var graphDirectory = Path.Combine(graphRoot, "xenon_material_graphs");
        if (!Directory.Exists(graphDirectory))
            throw new DirectoryNotFoundException(
                $"Material graph directory not found: {graphDirectory}");

        var graphFiles = Directory.GetFiles(
                graphDirectory,
                "*.json",
                SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (graphFiles.Length != expectedOwned)
            throw new InvalidDataException(
                $"Material graph count {graphFiles.Length:N0} != expected {expectedOwned:N0}.");

        var entries = new Dictionary<int, T4XenonMaterialCorpusEntry>();
        foreach (var graphFile in graphFiles)
        {
            var graph = JsonSerializer.Deserialize<T4XenonRelocatableMaterialGraph>(
                File.ReadAllText(graphFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException(
                    $"Could not parse Material graph: {graphFile}");

            ValidateGraph(graph, graphFile);

            var matches = T4XenonXModelCorpus.FindReferenceCandidates(
                roots,
                "material",
                graph.Name);
            if (matches.Count != 1)
                throw new InvalidDataException(
                    $"{graph.Name}: Material graph resolves to {matches.Count} native root(s).");

            var nativeRoot = matches[0];
            if (!entries.TryAdd(
                    nativeRoot.Index,
                    new T4XenonMaterialCorpusEntry
                    {
                        NativeRootIndex = nativeRoot.Index,
                        NativeName = nativeRoot.Name,
                        CorpusName = graph.Name,
                        GraphFile = graphFile,
                        DumpRoot = graphRoot,
                        Graph = graph
                    }))
                throw new InvalidDataException(
                    $"{graph.Name}: native Material root #{nativeRoot.Index} is already claimed by another graph.");
        }

        var nativeMaterialRoots = roots.Count(
            x => T4XenonXModelCorpus.NormalizeType(x.OatTypeName) == "material");
        if (entries.Count != expectedOwned)
            throw new InvalidDataException(
                $"Bound Material graph count {entries.Count:N0} != expected {expectedOwned:N0}.");
        if (nativeMaterialRoots < entries.Count)
            throw new InvalidDataException(
                $"Native Material roots {nativeMaterialRoots:N0} < bound corpus {entries.Count:N0}.");

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

            if (!string.IsNullOrWhiteSpace(entry.Graph.TechniqueSet))
                ValidateReference(
                    roots,
                    new[] { "techset", "techniqueset" },
                    entry.Graph.TechniqueSet!,
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
        if (matches.Count != 1)
            errors.Add(
                $"{owner}: asset reference {string.Join("/", types)}:{name} resolves to {matches.Count} native root(s).");
    }

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
    public required T4XenonRelocatableMaterialGraph Graph { get; init; }
}

internal static class T4XenonMaterialGraphCodec
{
    public static void Plan(
        T4XenonMaterialCorpusEntry entry,
        Action<long, int, bool> virtualAlloc,
        Action<int> tempRoot)
    {
        var g = entry.Graph;
        tempRoot(g.Abi.Material);
        virtualAlloc(Utf8Z(entry.NativeName), 1, true);

        PlanOptional(
            g.Presence.TextureTable,
            checked((long)g.Counts.Textures * g.Abi.TextureDef),
            g.Alignments.TextureDef,
            virtualAlloc);
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
        tempRoot(g.Abi.Material);

        var rootBody = ReadBlob(
            entry.DumpRoot,
            g.RootFile,
            g.Abi.Material,
            $"{entry.CorpusName} root");

        if (!string.IsNullOrWhiteSpace(g.TechniqueSet))
        {
            var stable = T4XenonMaterialCorpus.ResolveStablePointer(
                roots,
                stableByRoot,
                new[] { "techset", "techniqueset" },
                g.TechniqueSet!);
            PutU32(rootBody, g.Offsets.TechniqueSet, stable);
        }

        zone.Write(rootBody);
        EmitString(zone, virtualAlloc, entry.NativeName);

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

            for (var i = 0; i < g.Textures.Count; i++)
            {
                var texture = g.Textures[i];
                if (texture.Water.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                    throw new InvalidDataException(
                        $"{entry.CorpusName}: texture[{i}] contains a water graph.");

                if (string.IsNullOrWhiteSpace(texture.Image))
                    continue;

                var stable = T4XenonMaterialCorpus.ResolveStablePointer(
                    roots,
                    stableByRoot,
                    new[] { "image" },
                    texture.Image!);
                PutU32(
                    textureBytes,
                    checked(i * g.Abi.TextureDef + g.Offsets.TextureUnion),
                    stable);
            }

            virtualAlloc(
                textureBytes.LongLength,
                g.Alignments.TextureDef,
                true);
            zone.Write(textureBytes);
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
