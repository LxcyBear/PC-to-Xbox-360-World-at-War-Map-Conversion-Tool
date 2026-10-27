using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed record T4XenonGraphShaderRef(
    string? Name,
    int CachedPartSize,
    int PhysicalPartSize,
    string? CachedFile,
    string? PhysicalFile);

public sealed record T4XenonVertexRouting(byte Source, byte Dest);

public sealed record T4XenonVertexDeclGraph(
    byte StreamCount,
    bool HasOptionalSource,
    IReadOnlyList<T4XenonVertexRouting> Routing);

public sealed record T4XenonCodeConst(byte Index, byte FirstRow, byte RowCount);

public sealed record T4XenonShaderArgGraph(
    byte Type,
    byte Dest,
    uint URaw,
    IReadOnlyList<float>? LiteralConst,
    T4XenonCodeConst? CodeConst);

public sealed record T4XenonMaterialPassGraph(
    byte PerPrimArgCount,
    byte PerObjArgCount,
    byte StableArgCount,
    byte CustomSamplerFlags,
    byte PrecompiledIndex,
    T4XenonVertexDeclGraph? VertexDecl,
    IReadOnlyList<T4XenonGraphShaderRef?> VertexShaderArray,
    T4XenonGraphShaderRef? VertexShader,
    T4XenonGraphShaderRef? PixelShader,
    IReadOnlyList<T4XenonShaderArgGraph> Args);

public sealed record T4XenonTechniqueGraph(
    string? Name,
    ushort Flags,
    ushort PassCount,
    IReadOnlyList<T4XenonMaterialPassGraph> Passes);

public sealed record T4XenonTechniqueSlotGraph(
    int Slot,
    T4XenonTechniqueGraph? Technique);

public sealed record T4XenonTechniqueSetGraph(
    string Format,
    string Name,
    byte WorldVertFormat,
    bool HasBeenUploaded,
    IReadOnlyList<T4XenonTechniqueSlotGraph> Slots,
    string SourceJson);

public sealed class T4XenonGraphImportResult
{
    public required string DumpRoot { get; init; }
    public required int TechniqueSets { get; init; }
    public required int NonNullTechniqueSlots { get; init; }
    public required int Passes { get; init; }
    public required int DistinctVertexShaders { get; init; }
    public required int DistinctPixelShaders { get; init; }
    public required int ShaderBlobFilesReferenced { get; init; }
    public required long ShaderBlobBytes { get; init; }
    public required IReadOnlyDictionary<string, T4XenonTechniqueSetGraph> TechniqueSetsByName { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool Verified { get; init; }
}

public static class T4XenonTechniqueGraphImporter
{
    public static T4XenonGraphImportResult Load(
        string dumpRoot,
        int expectedTechniqueSets = 87,
        int expectedPixelShaders = 897)
    {
        dumpRoot = Path.GetFullPath(dumpRoot);
        if (!Directory.Exists(dumpRoot))
            throw new DirectoryNotFoundException($"Xenon graph dump root was not found: {dumpRoot}");

        var jsonFiles = Directory.EnumerateFiles(dumpRoot, "*.json", SearchOption.AllDirectories)
            .Where(x => x.Replace('\\', '/').Contains("/xenon_techset_graphs/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (jsonFiles.Count == 0)
            throw new InvalidDataException("No xenon_techset_graphs JSON files were found.");

        var allFiles = Directory.EnumerateFiles(dumpRoot, "*", SearchOption.AllDirectories)
            .ToList();
        var suffixIndex = BuildSuffixIndex(dumpRoot, allFiles);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var graphs = new Dictionary<string, T4XenonTechniqueSetGraph>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var vertexShaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pixelShaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referencedBlobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long blobBytes = 0;
        var nonNullSlots = 0;
        var passes = 0;

        foreach (var file in jsonFiles)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;

            var format = ReadString(root, "format");
            if (!string.Equals(format, "WaW360.T4Xenon.TechniqueSetGraph.v1", StringComparison.Ordinal))
                throw new InvalidDataException($"Unexpected TechniqueSet graph format '{format}' in {file}.");

            var name = ReadString(root, "name");
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException($"TechniqueSet graph has an empty name: {file}");
            if (graphs.ContainsKey(name))
                throw new InvalidDataException($"Duplicate TechniqueSet graph identity '{name}'.");

            var worldVertFormat = checked((byte)ReadInt(root, "worldVertFormat"));
            var uploaded = root.TryGetProperty("hasBeenUploaded", out var uploadedProp) &&
                           uploadedProp.ValueKind == JsonValueKind.True;

            if (!root.TryGetProperty("slots", out var slotsProp) ||
                slotsProp.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"TechniqueSet '{name}' has no slot array.");
            if (slotsProp.GetArrayLength() != 51)
                throw new InvalidDataException($"TechniqueSet '{name}' has {slotsProp.GetArrayLength()} slots; expected 51.");

            var slots = new List<T4XenonTechniqueSlotGraph>(51);
            var slotIndex = 0;
            foreach (var slotEl in slotsProp.EnumerateArray())
            {
                var declaredSlot = ReadInt(slotEl, "slot");
                if (declaredSlot != slotIndex)
                    throw new InvalidDataException(
                        $"TechniqueSet '{name}' slot sequence mismatch: expected {slotIndex}, JSON says {declaredSlot}.");

                T4XenonTechniqueGraph? technique = null;
                if (slotEl.TryGetProperty("technique", out var techEl) &&
                    techEl.ValueKind != JsonValueKind.Null)
                {
                    technique = ParseTechnique(
                        techEl,
                        name,
                        slotIndex,
                        suffixIndex,
                        vertexShaders,
                        pixelShaders,
                        referencedBlobs,
                        ref blobBytes);
                    nonNullSlots++;
                    passes += technique.Passes.Count;
                }

                slots.Add(new T4XenonTechniqueSlotGraph(slotIndex, technique));
                slotIndex++;
            }

            graphs.Add(name, new T4XenonTechniqueSetGraph(
                format,
                name,
                worldVertFormat,
                uploaded,
                slots,
                file));
        }

        var verified =
            graphs.Count == expectedTechniqueSets &&
            pixelShaders.Count == expectedPixelShaders &&
            warnings.Count == 0;

        if (graphs.Count != expectedTechniqueSets)
            warnings.Add($"TechniqueSet graph count is {graphs.Count:N0}; expected {expectedTechniqueSets:N0}.");
        if (pixelShaders.Count != expectedPixelShaders)
            warnings.Add($"Distinct PixelShader count is {pixelShaders.Count:N0}; expected {expectedPixelShaders:N0}.");

        return new T4XenonGraphImportResult
        {
            DumpRoot = dumpRoot,
            TechniqueSets = graphs.Count,
            NonNullTechniqueSlots = nonNullSlots,
            Passes = passes,
            DistinctVertexShaders = vertexShaders.Count,
            DistinctPixelShaders = pixelShaders.Count,
            ShaderBlobFilesReferenced = referencedBlobs.Count,
            ShaderBlobBytes = blobBytes,
            TechniqueSetsByName = graphs,
            Warnings = warnings,
            Verified = verified
        };
    }

    private static T4XenonTechniqueGraph ParseTechnique(
        JsonElement techEl,
        string techsetName,
        int slot,
        IReadOnlyDictionary<string, string> suffixIndex,
        HashSet<string> vertexShaders,
        HashSet<string> pixelShaders,
        HashSet<string> referencedBlobs,
        ref long blobBytes)
    {
        var name = OptionalString(techEl, "name");
        var flags = checked((ushort)ReadInt(techEl, "flags"));
        var passCount = checked((ushort)ReadInt(techEl, "passCount"));

        if (!techEl.TryGetProperty("passes", out var passesEl) ||
            passesEl.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"TechniqueSet '{techsetName}' slot {slot} has no passes array.");
        if (passesEl.GetArrayLength() != passCount)
            throw new InvalidDataException(
                $"TechniqueSet '{techsetName}' slot {slot} passCount={passCount}, JSON passes={passesEl.GetArrayLength()}.");

        var passes = new List<T4XenonMaterialPassGraph>(passCount);
        foreach (var passEl in passesEl.EnumerateArray())
        {
            var perPrim = checked((byte)ReadInt(passEl, "perPrimArgCount"));
            var perObj = checked((byte)ReadInt(passEl, "perObjArgCount"));
            var stable = checked((byte)ReadInt(passEl, "stableArgCount"));
            var samplerFlags = checked((byte)ReadInt(passEl, "customSamplerFlags"));
            var precompiled = checked((byte)ReadInt(passEl, "precompiledIndex"));

            T4XenonVertexDeclGraph? decl = null;
            if (passEl.TryGetProperty("vertexDecl", out var declEl) &&
                declEl.ValueKind != JsonValueKind.Null)
            {
                var streamCount = checked((byte)ReadInt(declEl, "streamCount"));
                var optional = declEl.TryGetProperty("hasOptionalSource", out var opt) &&
                               opt.ValueKind == JsonValueKind.True;
                if (!declEl.TryGetProperty("routing", out var routingEl) ||
                    routingEl.ValueKind != JsonValueKind.Array ||
                    routingEl.GetArrayLength() != 16)
                    throw new InvalidDataException($"TechniqueSet '{techsetName}' slot {slot} has invalid vertex routing.");
                var routing = routingEl.EnumerateArray()
                    .Select(x => new T4XenonVertexRouting(
                        checked((byte)ReadInt(x, "source")),
                        checked((byte)ReadInt(x, "dest"))))
                    .ToList();
                decl = new T4XenonVertexDeclGraph(streamCount, optional, routing);
            }

            var shaderArray = new List<T4XenonGraphShaderRef?>(16);
            if (!passEl.TryGetProperty("vertexShaderArray", out var arrayEl) ||
                arrayEl.ValueKind != JsonValueKind.Array ||
                arrayEl.GetArrayLength() != 16)
                throw new InvalidDataException($"TechniqueSet '{techsetName}' slot {slot} has invalid vertexShaderArray.");

            foreach (var shaderEl in arrayEl.EnumerateArray())
                shaderArray.Add(ParseShader(
                    shaderEl, true, suffixIndex, vertexShaders, referencedBlobs, ref blobBytes));

            var vertex = passEl.TryGetProperty("vertexShader", out var vertexEl)
                ? ParseShader(vertexEl, true, suffixIndex, vertexShaders, referencedBlobs, ref blobBytes)
                : null;
            var pixel = passEl.TryGetProperty("pixelShader", out var pixelEl)
                ? ParseShader(pixelEl, false, suffixIndex, pixelShaders, referencedBlobs, ref blobBytes)
                : null;

            if (!passEl.TryGetProperty("args", out var argsEl) ||
                argsEl.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"TechniqueSet '{techsetName}' slot {slot} has no shader args array.");

            var expectedArgs = perPrim + perObj + stable;
            if (argsEl.GetArrayLength() != expectedArgs)
                throw new InvalidDataException(
                    $"TechniqueSet '{techsetName}' slot {slot} shader arg count mismatch: expected {expectedArgs}, got {argsEl.GetArrayLength()}.");

            var args = new List<T4XenonShaderArgGraph>(expectedArgs);
            foreach (var argEl in argsEl.EnumerateArray())
            {
                IReadOnlyList<float>? literal = null;
                if (argEl.TryGetProperty("literalConst", out var litEl) &&
                    litEl.ValueKind == JsonValueKind.Array)
                {
                    if (litEl.GetArrayLength() != 4)
                        throw new InvalidDataException("literalConst must contain exactly four floats.");
                    literal = litEl.EnumerateArray().Select(x => x.GetSingle()).ToList();
                }

                T4XenonCodeConst? codeConst = null;
                if (argEl.TryGetProperty("codeConst", out var codeEl) &&
                    codeEl.ValueKind == JsonValueKind.Object)
                {
                    codeConst = new T4XenonCodeConst(
                        checked((byte)ReadInt(codeEl, "index")),
                        checked((byte)ReadInt(codeEl, "firstRow")),
                        checked((byte)ReadInt(codeEl, "rowCount")));
                }

                args.Add(new T4XenonShaderArgGraph(
                    checked((byte)ReadInt(argEl, "type")),
                    checked((byte)ReadInt(argEl, "dest")),
                    ReadUInt(argEl, "uRaw"),
                    literal,
                    codeConst));
            }

            passes.Add(new T4XenonMaterialPassGraph(
                perPrim, perObj, stable, samplerFlags, precompiled,
                decl, shaderArray, vertex, pixel, args));
        }

        return new T4XenonTechniqueGraph(name, flags, passCount, passes);
    }

    private static T4XenonGraphShaderRef? ParseShader(
        JsonElement el,
        bool vertex,
        IReadOnlyDictionary<string, string> suffixIndex,
        HashSet<string> names,
        HashSet<string> referencedBlobs,
        ref long blobBytes)
    {
        if (el.ValueKind == JsonValueKind.Null)
            return null;

        var name = OptionalString(el, "name");
        if (!string.IsNullOrWhiteSpace(name))
            names.Add(name);

        var cachedSize = ReadInt(el, "cachedPartSize");
        var physicalSize = ReadInt(el, "physicalPartSize");
        var cachedFile = OptionalString(el, "cachedFile");
        var physicalFile = OptionalString(el, "physicalFile");

        ValidateBlob(cachedFile, cachedSize, suffixIndex, referencedBlobs, ref blobBytes, vertex ? "VS cached" : "PS cached");
        ValidateBlob(physicalFile, physicalSize, suffixIndex, referencedBlobs, ref blobBytes, vertex ? "VS physical" : "PS physical");

        return new T4XenonGraphShaderRef(name, cachedSize, physicalSize, cachedFile, physicalFile);
    }

    private static void ValidateBlob(
        string? relative,
        int expectedSize,
        IReadOnlyDictionary<string, string> suffixIndex,
        HashSet<string> referencedBlobs,
        ref long blobBytes,
        string label)
    {
        if (expectedSize == 0)
        {
            if (!string.IsNullOrWhiteSpace(relative) &&
                TryResolveSuffix(relative, suffixIndex, out var zeroFile) &&
                new FileInfo(zeroFile).Length != 0)
                throw new InvalidDataException($"{label} blob '{relative}' should be empty.");
            return;
        }

        if (string.IsNullOrWhiteSpace(relative))
            throw new InvalidDataException($"{label} blob path is missing for a {expectedSize}-byte shader part.");
        if (!TryResolveSuffix(relative, suffixIndex, out var full))
            throw new FileNotFoundException($"{label} blob was not found in graph dump: {relative}");

        var length = new FileInfo(full).Length;
        if (length != expectedSize)
            throw new InvalidDataException($"{label} blob '{relative}' is {length:N0} bytes; expected {expectedSize:N0}.");

        if (referencedBlobs.Add(full))
            blobBytes += length;
    }

    private static Dictionary<string, string> BuildSuffixIndex(string root, IReadOnlyList<string> files)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var marker = relative.IndexOf("xenon_graph_shaders/", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) continue;
            var suffix = relative[marker..];
            if (!result.TryAdd(suffix, file) &&
                !string.Equals(result[suffix], file, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Duplicate shader blob suffix '{suffix}'.");
        }
        return result;
    }

    private static bool TryResolveSuffix(
        string relative,
        IReadOnlyDictionary<string, string> suffixIndex,
        out string full)
    {
        var key = relative.Replace('\\', '/').TrimStart('/');
        return suffixIndex.TryGetValue(key, out full!);
    }

    private static int ReadInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p) || !p.TryGetInt32(out var value))
            throw new InvalidDataException($"JSON property '{name}' is missing or is not an int.");
        return value;
    }

    private static uint ReadUInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p) || !p.TryGetUInt32(out var value))
            throw new InvalidDataException($"JSON property '{name}' is missing or is not a uint.");
        return value;
    }

    private static string ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"JSON property '{name}' is missing or is not a string.");
        return p.GetString() ?? string.Empty;
    }

    private static string? OptionalString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null)
            return null;
        if (p.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"JSON property '{name}' is not a string/null.");
        return p.GetString();
    }
}
