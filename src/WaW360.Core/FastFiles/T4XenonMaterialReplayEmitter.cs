using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed class T4XenonMaterialReplayResult
{
    public required string GraphRoot { get; init; }
    public required string ReferenceFastFile { get; init; }
    public required string OutputFastFile { get; init; }
    public required string OutputZoneFile { get; init; }
    public required int Materials { get; init; }
    public required int Roots { get; init; }
    public required int ScriptStrings { get; init; }
    public required long ZoneBytes { get; init; }
    public required long TempBytes { get; init; }
    public required long VirtualBytes { get; init; }
    public required bool ExactIdentitySequence { get; init; }
    public required bool OatZeroWarningsZeroErrors { get; init; }
    public required bool GraphCorpusRedumpExact { get; init; }
    public required bool WrapperRoundTripVerified { get; init; }
    public required bool ReplayPass { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

public static class T4XenonMaterialReplayEmitter
{
    private const int XFileHeaderSize = 36;

    public static async Task<T4XenonMaterialReplayResult> EmitAsync(
        string graphRoot,
        string referenceFastFile,
        string outputDirectory,
        string explicitUnlinkerExe,
        CancellationToken cancellationToken = default)
    {
        graphRoot = Path.GetFullPath(graphRoot);
        referenceFastFile = Path.GetFullPath(referenceFastFile);
        outputDirectory = Path.GetFullPath(outputDirectory);
        explicitUnlinkerExe = Path.GetFullPath(explicitUnlinkerExe);
        Directory.CreateDirectory(outputDirectory);

        var graphDirectory = Path.Combine(graphRoot, "xenon_material_graphs");
        if (!Directory.Exists(graphDirectory))
            throw new DirectoryNotFoundException(
                $"Material graph directory is missing: {graphDirectory}");

        var graphFiles = Directory.GetFiles(
                graphDirectory,
                "*.json",
                SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (graphFiles.Length == 0)
            throw new InvalidDataException("Material replay graph corpus is empty.");

        var graphs = graphFiles
            .Select(LoadGraph)
            .ToList();

        var byName = new Dictionary<string, Graph>(StringComparer.OrdinalIgnoreCase);
        foreach (var graph in graphs)
        {
            ValidateGraph(graph);
            if (!byName.TryAdd(graph.Name, graph))
                throw new InvalidDataException(
                    $"Duplicate Material graph identity '{graph.Name}'.");
        }

        var reference = await FastFileAnalysisCache.GetZoneAsync(
            referenceFastFile,
            cancellationToken);
        if (reference.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException("Material replay reference must be Xbox360.");

        var referenceIdentity = await T4XenonNativeIdentityOracle.RunAsync(
            referenceFastFile,
            Path.Combine(outputDirectory, "reference_identity"),
            explicitUnlinkerExe,
            cancellationToken);

        var materialRoots = referenceIdentity.Entries
            .Where(x => NormalizeType(x.OatTypeName) == "material")
            .ToList();
        if (materialRoots.Count != graphs.Count)
            throw new InvalidDataException(
                $"Reference Material roots={materialRoots.Count:N0}, graph corpus={graphs.Count:N0}.");

        var missingGraphs = materialRoots
            .Where(x => !byName.ContainsKey(NormalizeReferenceName(x.Name)))
            .Select(x => x.Name)
            .Take(20)
            .ToArray();
        if (missingGraphs.Length != 0)
            throw new InvalidDataException(
                "Reference contains Material roots with no graph: " +
                string.Join(", ", missingGraphs));

        var extraGraphs = byName.Keys
            .Where(name => !materialRoots.Any(x =>
                NormalizeReferenceName(x.Name).Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase)))
            .Take(20)
            .ToArray();
        if (extraGraphs.Length != 0)
            throw new InvalidDataException(
                "Graph corpus contains Materials absent from reference: " +
                string.Join(", ", extraGraphs));

        var unsupportedRoots = referenceIdentity.Entries
            .Where(x =>
            {
                var type = NormalizeType(x.OatTypeName);
                return type is not ("material" or "techset" or "techniqueset" or "image");
            })
            .ToList();
        if (unsupportedRoots.Count != 0)
            throw new InvalidDataException(
                "Standalone Material replay encountered unsupported reference root types: " +
                string.Join(", ", unsupportedRoots.Take(20).Select(
                    x => $"{x.OatTypeName}:{x.Name}")));

        var state = new ReplayState();
        using var zone = new MemoryStream();
        zone.Write(new byte[XFileHeaderSize]);

        WriteU32(zone, checked((uint)reference.Document.ScriptStrings.Count));
        WriteU32(
            zone,
            reference.Document.ScriptStrings.Count == 0
                ? 0u
                : T4ZonePointer.Following);
        WriteU32(zone, checked((uint)referenceIdentity.Entries.Count));
        WriteU32(
            zone,
            referenceIdentity.Entries.Count == 0
                ? 0u
                : T4ZonePointer.Following);

        EmitScriptStrings(zone, state, reference.Document.ScriptStrings);

        state.VirtualAlloc(
            checked(referenceIdentity.Entries.Count * 8L),
            4,
            serialized: true);
        foreach (var root in referenceIdentity.Entries.OrderBy(x => x.Index))
        {
            WriteU32(zone, checked((uint)root.TypeId));
            WriteU32(zone, T4ZonePointer.Insert);
        }

        var stableByRoot = new Dictionary<int, uint>();
        var emittedMaterials = 0;

        foreach (var root in referenceIdentity.Entries.OrderBy(x => x.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var insertOffset = state.VirtualAlloc(4, 4, serialized: false);
            var stablePointer = T4ZonePointer.Encode(
                T4ZoneBlock.Virtual,
                checked((uint)insertOffset));
            stableByRoot.Add(root.Index, stablePointer);

            var type = NormalizeType(root.OatTypeName);
            if (type == "material")
            {
                var name = NormalizeReferenceName(root.Name);
                if (!byName.TryGetValue(name, out var graph))
                    throw new InvalidDataException(
                        $"No Material graph found for root #{root.Index} '{root.Name}'.");

                EmitMaterial(
                    zone,
                    state,
                    graphRoot,
                    graph,
                    referenceIdentity.Entries,
                    stableByRoot);
                emittedMaterials++;
                continue;
            }

            EmitReferenceShell(zone, state, root.OatTypeName, root.Name);
        }

        if (emittedMaterials != graphs.Count)
            throw new InvalidDataException(
                $"Material replay emitted {emittedMaterials:N0}/{graphs.Count:N0} graphs.");

        PatchHeader(
            zone,
            checked((uint)(zone.Length - XFileHeaderSize)),
            checked((uint)state.TempMax),
            checked((uint)state.Virtual));

        var zoneBytes = zone.ToArray();
        var zonePath = Path.Combine(
            outputDirectory,
            "nuclear_4zc_material_replay.zone");
        await File.WriteAllBytesAsync(zonePath, zoneBytes, cancellationToken);

        var parsed = ZoneHeaderParser.Parse(
            zoneBytes.AsSpan(0, XFileHeaderSize),
            Endian.Big,
            zoneBytes.LongLength);
        if (!parsed.Plausible)
            throw new InvalidDataException(
                "Replayed Material zone header is not plausible.");

        var outputFf = Path.Combine(
            outputDirectory,
            "nuclear_4zc_material_replay.ff");
        var wrapped = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
            outputFf,
            zoneBytes,
            preferNativeLevel6: true,
            cancellationToken);

        var blockers = new List<string>();
        var diagnostics = new List<string>();

        var verificationKey = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    Path.GetFullPath(outputFf) + "|material-corpus")))[..12]
            .ToLowerInvariant();
        var verificationRoot = Path.Combine(
            Path.GetTempPath(),
            "WaW360",
            "mrv",
            verificationKey);
        if (Directory.Exists(verificationRoot))
            Directory.Delete(verificationRoot, recursive: true);
        Directory.CreateDirectory(verificationRoot);

        var verificationFf = Path.Combine(verificationRoot, "c.ff");
        File.Copy(outputFf, verificationFf, overwrite: true);

        var exactIdentity = false;
        var oatZero = false;
        try
        {
            var outputIdentity = await T4XenonNativeIdentityOracle.RunAsync(
                verificationFf,
                Path.Combine(verificationRoot, "id"),
                explicitUnlinkerExe,
                cancellationToken);

            exactIdentity =
                outputIdentity.Entries.Count == referenceIdentity.Entries.Count &&
                outputIdentity.Entries.Zip(referenceIdentity.Entries).All(pair =>
                    pair.First.TypeId == pair.Second.TypeId &&
                    pair.First.Name.Equals(
                        pair.Second.Name,
                        StringComparison.Ordinal));

            var oatLog = await File.ReadAllTextAsync(
                outputIdentity.RawUnlinkerLogFile,
                cancellationToken);
            oatZero = HasZeroWarningErrorSummary(oatLog);

            File.Copy(
                outputIdentity.RawUnlinkerLogFile,
                Path.Combine(outputDirectory, "identity_reopen.log"),
                overwrite: true);

            if (!exactIdentity)
                blockers.Add(
                    "OAT output root identity sequence differs from the 4Z-A Material corpus.");
            if (!oatZero)
                blockers.Add(
                    "OAT identity reopen did not prove 0 warnings / 0 errors.");
        }
        catch (Exception ex)
        {
            blockers.Add("OAT identity reopen failed: " + ex.Message);
        }

        var redumpDir = Path.Combine(verificationRoot, "dump");
        Directory.CreateDirectory(redumpDir);
        var redumpLog = Path.Combine(outputDirectory, "redump.log");
        var redump = await RunProcessAsync(
            explicitUnlinkerExe,
            new[]
            {
                "--no-color",
                "--include-assets", "material",
                "--output-folder", redumpDir,
                verificationFf
            },
            redumpLog,
            cancellationToken);

        if (redump.ExitCode != 0)
            blockers.Add(
                $"Specialized OAT Material redump failed with exit {redump.ExitCode}.");
        if (!HasZeroWarningErrorSummary(redump.Output))
        {
            var tail = string.Join(
                " | ",
                redump.Output
                    .Split(
                        new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries)
                    .TakeLast(12)
                    .Select(x => x.Trim()));
            blockers.Add(
                "Specialized OAT Material redump did not prove 0 warnings / 0 errors." +
                (string.IsNullOrWhiteSpace(tail) ? "" : " Tail: " + tail));
        }

        var graphExact = false;
        if (redump.ExitCode == 0)
        {
            graphExact = CompareGraphCorpus(
                graphRoot,
                redumpDir,
                out var compareError);
            if (!graphExact)
                blockers.Add(
                    "Material relocatable graph corpus redump differs: " +
                    compareError);
        }

        if (!wrapped.RoundTripVerified)
            blockers.Add("Fastfile wrapper round-trip verification failed.");

        diagnostics.Add(
            $"referenceRoots={referenceIdentity.Entries.Count:N0}; " +
            $"materials={graphs.Count:N0}; TEMP=0x{state.TempMax:X}; " +
            $"VIRTUAL=0x{state.Virtual:X}; zoneBytes={zoneBytes.LongLength:N0}");

        var pass =
            blockers.Count == 0 &&
            exactIdentity &&
            oatZero &&
            graphExact &&
            wrapped.RoundTripVerified;

        var result = new T4XenonMaterialReplayResult
        {
            GraphRoot = graphRoot,
            ReferenceFastFile = referenceFastFile,
            OutputFastFile = outputFf,
            OutputZoneFile = zonePath,
            Materials = graphs.Count,
            Roots = referenceIdentity.Entries.Count,
            ScriptStrings = reference.Document.ScriptStrings.Count,
            ZoneBytes = zoneBytes.LongLength,
            TempBytes = state.TempMax,
            VirtualBytes = state.Virtual,
            ExactIdentitySequence = exactIdentity,
            OatZeroWarningsZeroErrors = oatZero,
            GraphCorpusRedumpExact = graphExact,
            WrapperRoundTripVerified = wrapped.RoundTripVerified,
            ReplayPass = pass,
            Blockers = blockers,
            Diagnostics = diagnostics,
            TextReportFile = Path.Combine(
                outputDirectory,
                "nuclear_4zc_material_replay.txt"),
            JsonReportFile = Path.Combine(
                outputDirectory,
                "nuclear_4zc_material_replay.json")
        };

        await WriteResult(result, cancellationToken);
        return result;
    }

    private static void ValidateGraph(Graph graph)
    {
        if (graph.Format != "WaW360.T4Xenon.MaterialRelocatableGraph.v1")
            throw new InvalidDataException(
                $"Unexpected Material graph format '{graph.Format}' for '{graph.Name}'.");
        if (graph.Abi.Material != 96)
            throw new InvalidDataException(
                $"{graph.Name}: Material ABI is {graph.Abi.Material}; expected 96.");
        if (graph.Reusable.AliasCount != 0)
            throw new InvalidDataException(
                $"{graph.Name}: replay refuses {graph.Reusable.AliasCount} reusable pointer aliases.");
        if (graph.Inventory.InvalidWaterDimensions != 0)
            throw new InvalidDataException(
                $"{graph.Name}: invalid water dimensions were recorded.");
        if (graph.Inventory.WaterTextures != 0)
            throw new InvalidDataException(
                $"{graph.Name}: water replay is not implemented yet, but this graph has {graph.Inventory.WaterTextures} water texture(s).");

        ValidateAlignment(graph.Name, "material", graph.Alignments.Material);
        ValidateAlignment(graph.Name, "textureDef", graph.Alignments.TextureDef);
        ValidateAlignment(graph.Name, "constantDef", graph.Alignments.ConstantDef);
        ValidateAlignment(graph.Name, "stateBits", graph.Alignments.StateBits);

        if (graph.Counts.Textures != graph.Textures.Count)
            throw new InvalidDataException(
                $"{graph.Name}: texture metadata count {graph.Textures.Count} != {graph.Counts.Textures}.");
    }

    private static void ValidateAlignment(
        string name,
        string field,
        int value)
    {
        if (value <= 0 || (value & (value - 1)) != 0)
            throw new InvalidDataException(
                $"{name}: invalid power-of-two alignment {field}={value}.");
    }

    private static void EmitMaterial(
        Stream zone,
        ReplayState state,
        string graphRoot,
        Graph graph,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyDictionary<int, uint> stableByRoot)
    {
        state.TempRoot(graph.Abi.Material);

        var rootBody = ReadBlob(
            graphRoot,
            graph.RootFile,
            graph.Abi.Material,
            $"{graph.Name} root");

        if (!string.IsNullOrWhiteSpace(graph.TechniqueSet))
        {
            var stable = ResolveAssetStablePointer(
                roots,
                stableByRoot,
                new[] { "techset", "techniqueset" },
                graph.TechniqueSet!);
            PutU32(rootBody, graph.Offsets.TechniqueSet, stable);
        }

        zone.Write(rootBody);

        if (graph.Presence.Name)
            EmitString(zone, state, graph.Name);

        if (graph.Presence.TextureTable)
        {
            var expected = checked((long)graph.Counts.Textures * graph.Abi.TextureDef);
            if (string.IsNullOrWhiteSpace(graph.TextureTableFile))
                throw new InvalidDataException(
                    $"{graph.Name}: textureTable is present but blob path is missing.");

            var textureBytes = ReadBlob(
                graphRoot,
                graph.TextureTableFile!,
                expected,
                $"{graph.Name} textures");

            for (var i = 0; i < graph.Textures.Count; i++)
            {
                var texture = graph.Textures[i];
                if (texture.Water.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                    throw new InvalidDataException(
                        $"{graph.Name}: texture[{i}] contains an unsupported water graph.");

                if (string.IsNullOrWhiteSpace(texture.Image))
                    continue;

                var stable = ResolveAssetStablePointer(
                    roots,
                    stableByRoot,
                    new[] { "image" },
                    texture.Image!);
                PutU32(
                    textureBytes,
                    checked(i * graph.Abi.TextureDef + graph.Offsets.TextureUnion),
                    stable);
            }

            state.VirtualAlloc(
                textureBytes.LongLength,
                graph.Alignments.TextureDef,
                serialized: true);
            zone.Write(textureBytes);
        }
        else if (graph.Counts.Textures != 0)
        {
            throw new InvalidDataException(
                $"{graph.Name}: null textureTable with {graph.Counts.Textures} texture(s).");
        }

        EmitOptionalBlob(
            zone,
            state,
            graphRoot,
            graph.ConstantTableFile,
            graph.Presence.ConstantTable,
            checked((long)graph.Counts.Constants * graph.Abi.ConstantDef),
            graph.Alignments.ConstantDef,
            $"{graph.Name} constants");

        EmitOptionalBlob(
            zone,
            state,
            graphRoot,
            graph.StateBitsTableFile,
            graph.Presence.StateBitsTable,
            checked((long)graph.Counts.StateBits * graph.Abi.StateBits),
            graph.Alignments.StateBits,
            $"{graph.Name} stateBits");
    }

    private static uint ResolveAssetStablePointer(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyDictionary<int, uint> stableByRoot,
        IReadOnlyCollection<string> acceptedTypes,
        string name)
    {
        var normalizedTypes = acceptedTypes
            .Select(NormalizeType)
            .ToHashSet(StringComparer.Ordinal);

        var candidates = roots
            .Where(x =>
                normalizedTypes.Contains(NormalizeType(x.OatTypeName)) &&
                NormalizeReferenceName(x.Name).Equals(
                    NormalizeReferenceName(name),
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count != 1)
            throw new InvalidDataException(
                $"Asset reference {string.Join("/", acceptedTypes)}:{name} resolved to {candidates.Count} root(s).");

        if (!stableByRoot.TryGetValue(candidates[0].Index, out var stable))
            throw new InvalidDataException(
                $"Asset reference {candidates[0].OatTypeName}:{name} points forward to root #{candidates[0].Index}; " +
                "4Z-C requires dependency-first OAT root ordering.");

        return stable;
    }

    private static void EmitReferenceShell(
        Stream zone,
        ReplayState state,
        string typeName,
        string name)
    {
        var type = NormalizeType(typeName);
        byte[] body;

        if (type == "image")
        {
            body = new byte[40];
            PutU32(body, 36, T4ZonePointer.Following);
        }
        else if (type is "techset" or "techniqueset")
        {
            body = new byte[216];
            PutU32(body, 0, T4ZonePointer.Following);
        }
        else
        {
            throw new InvalidDataException(
                $"No standalone Material reference shell ABI for {typeName}:{name}.");
        }

        state.TempRoot(body.Length);
        zone.Write(body);
        EmitString(zone, state, name);
    }

    private static void EmitOptionalBlob(
        Stream zone,
        ReplayState state,
        string graphRoot,
        string? relative,
        bool pointerPresent,
        long expectedBytes,
        int alignment,
        string label)
    {
        if (!pointerPresent)
        {
            if (expectedBytes != 0)
                throw new InvalidDataException(
                    $"{label}: pointer is null but count requires {expectedBytes:N0} bytes.");
            if (!string.IsNullOrWhiteSpace(relative))
                throw new InvalidDataException(
                    $"{label}: pointer is null but graph contains blob path '{relative}'.");
            return;
        }

        state.VirtualAlloc(expectedBytes, alignment, serialized: true);

        if (expectedBytes == 0)
            return;

        if (string.IsNullOrWhiteSpace(relative))
            throw new InvalidDataException(
                $"{label}: graph blob path is missing.");

        var blob = ReadBlob(
            graphRoot,
            relative!,
            expectedBytes,
            label);
        zone.Write(blob);
    }

    private static byte[] ReadBlob(
        string graphRoot,
        string relative,
        long expectedBytes,
        string label)
    {
        var full = ResolveGraphPath(graphRoot, relative);
        if (!File.Exists(full))
            throw new FileNotFoundException(
                $"{label} blob not found.",
                full);

        var bytes = File.ReadAllBytes(full);
        if (bytes.LongLength != expectedBytes)
            throw new InvalidDataException(
                $"{label}: {bytes.LongLength:N0} bytes, expected {expectedBytes:N0}.");
        return bytes;
    }

    private static string ResolveGraphPath(
        string root,
        string relative)
    {
        var normalized = relative.Replace(
            '/',
            Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootFull = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Graph path escapes dump root: {relative}");
        return full;
    }

    private static void EmitScriptStrings(
        Stream zone,
        ReplayState state,
        IReadOnlyList<T4ScriptStringEntry> strings)
    {
        if (strings.Count == 0)
            return;

        state.VirtualAlloc(
            checked(strings.Count * 4L),
            4,
            serialized: true);
        foreach (var item in strings)
            WriteU32(
                zone,
                item.RawPointer == 0
                    ? 0u
                    : T4ZonePointer.Following);

        foreach (var item in strings)
        {
            if (item.RawPointer == 0)
                continue;
            if (item.Value is null ||
                item.Value.StartsWith("<alias ", StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Unresolved ScriptString #{item.Index} cannot be replayed.");
            EmitString(zone, state, item.Value);
        }
    }

    private static void EmitString(
        Stream zone,
        ReplayState state,
        string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        state.VirtualAlloc(
            bytes.Length + 1L,
            1,
            serialized: true);
        zone.Write(bytes);
        zone.WriteByte(0);
    }

    private static void PatchHeader(
        MemoryStream zone,
        uint zoneSize,
        uint temp,
        uint virtualSize)
    {
        var pos = zone.Position;
        zone.Position = 0;
        WriteU32(zone, zoneSize);
        WriteU32(zone, 0);
        WriteU32(zone, temp);
        WriteU32(zone, 0);
        WriteU32(zone, 0);
        WriteU32(zone, 0);
        WriteU32(zone, virtualSize);
        WriteU32(zone, 0);
        WriteU32(zone, 0);
        zone.Position = pos;
    }

    private static bool CompareGraphCorpus(
        string sourceRoot,
        string redumpRoot,
        out string error)
    {
        foreach (var directory in new[]
        {
            "xenon_material_graphs",
            "xenon_material_graph_blobs"
        })
        {
            var leftRoot = Path.Combine(sourceRoot, directory);
            var rightRoot = Path.Combine(redumpRoot, directory);

            var leftFiles = Directory.Exists(leftRoot)
                ? Directory.GetFiles(leftRoot, "*", SearchOption.AllDirectories)
                    .Select(x => Path.GetRelativePath(leftRoot, x).Replace('\\', '/'))
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<string>();
            var rightFiles = Directory.Exists(rightRoot)
                ? Directory.GetFiles(rightRoot, "*", SearchOption.AllDirectories)
                    .Select(x => Path.GetRelativePath(rightRoot, x).Replace('\\', '/'))
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<string>();

            if (!leftFiles.SequenceEqual(rightFiles, StringComparer.Ordinal))
            {
                var missing = leftFiles
                    .Except(rightFiles, StringComparer.Ordinal)
                    .Take(12)
                    .ToArray();
                var extra = rightFiles
                    .Except(leftFiles, StringComparer.Ordinal)
                    .Take(12)
                    .ToArray();

                error =
                    $"{directory} file set differs: source={leftFiles.Length}, redump={rightFiles.Length}; " +
                    $"missing=[{string.Join(", ", missing)}]; extra=[{string.Join(", ", extra)}]";
                return false;
            }

            foreach (var relative in leftFiles)
            {
                var left = File.ReadAllBytes(
                    Path.Combine(
                        leftRoot,
                        relative.Replace('/', Path.DirectorySeparatorChar)));
                var right = File.ReadAllBytes(
                    Path.Combine(
                        rightRoot,
                        relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!left.AsSpan().SequenceEqual(right))
                {
                    error =
                        $"{directory}/{relative} SHA256 {Hash(left)} != {Hash(right)}";
                    return false;
                }
            }
        }

        error = string.Empty;
        return true;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string exe,
        IReadOnlyList<string> args,
        string logFile,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Could not start '{exe}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout) + (await stderr);
        await File.WriteAllTextAsync(
            logFile,
            output,
            new UTF8Encoding(false),
            cancellationToken);
        return new ProcessResult(process.ExitCode, output);
    }

    private static bool HasZeroWarningErrorSummary(string text) =>
        text.Split('\n').Any(
            line => line.Trim().Equals(
                "Finished with 0 warnings, 0 errors",
                StringComparison.OrdinalIgnoreCase));

    private static string NormalizeReferenceName(string value)
    {
        var result = value.Trim();
        if (result.StartsWith(",", StringComparison.Ordinal))
            result = result[1..];
        return result;
    }

    private static string NormalizeType(string value) =>
        value.Replace("_", string.Empty, StringComparison.Ordinal)
             .Replace("-", string.Empty, StringComparison.Ordinal)
             .ToLowerInvariant();

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static Graph LoadGraph(string graphFile)
    {
        var graph = JsonSerializer.Deserialize<Graph>(
            File.ReadAllText(graphFile),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        return graph
            ?? throw new InvalidDataException(
                $"Could not parse Material graph JSON: {graphFile}");
    }

    private static async Task WriteResult(
        T4XenonMaterialReplayResult result,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "NUCLEAR STAGE 4Z-C - STANDALONE EXACT XENON MATERIAL CORPUS REPLAY");
        sb.AppendLine(
            "====================================================================");
        sb.AppendLine($"Materials:                 {result.Materials:N0}");
        sb.AppendLine($"Roots:                     {result.Roots:N0}");
        sb.AppendLine($"ScriptStrings:             {result.ScriptStrings:N0}");
        sb.AppendLine($"Zone bytes:                {result.ZoneBytes:N0}");
        sb.AppendLine($"TEMP:                      0x{result.TempBytes:X}");
        sb.AppendLine($"VIRTUAL:                   0x{result.VirtualBytes:X}");
        sb.AppendLine($"Exact identity sequence:   {result.ExactIdentitySequence}");
        sb.AppendLine($"OAT 0 warnings / 0 errors: {result.OatZeroWarningsZeroErrors}");
        sb.AppendLine($"Graph corpus redump exact: {result.GraphCorpusRedumpExact}");
        sb.AppendLine($"Wrapper round-trip:        {result.WrapperRoundTripVerified}");
        sb.AppendLine($"REPLAY PASS:               {result.ReplayPass}");
        sb.AppendLine($"Reference FF:              {result.ReferenceFastFile}");
        sb.AppendLine($"Output FF:                 {result.OutputFastFile}");
        sb.AppendLine();
        foreach (var blocker in result.Blockers)
            sb.AppendLine("BLOCKER: " + blocker);
        foreach (var diagnostic in result.Diagnostics)
            sb.AppendLine("DIAGNOSTIC: " + diagnostic);

        await File.WriteAllTextAsync(
            result.TextReportFile,
            sb.ToString(),
            new UTF8Encoding(false),
            cancellationToken);
        await File.WriteAllTextAsync(
            result.JsonReportFile,
            JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);
    }

    private static void WriteU32(
        Stream stream,
        uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void PutU32(
        byte[] bytes,
        int offset,
        uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(
            bytes.AsSpan(offset, 4),
            value);

    private sealed record ProcessResult(
        int ExitCode,
        string Output);

    private sealed class ReplayState
    {
        private long _virtual;

        public long Virtual => _virtual;
        public long TempMax { get; private set; }
        public long Serialized { get; private set; }

        public long VirtualAlloc(
            long size,
            int alignment,
            bool serialized)
        {
            var mask = alignment - 1L;
            _virtual = checked((_virtual + mask) & ~mask);
            var result = _virtual;
            _virtual = checked(_virtual + size);
            if (serialized)
                Serialized = checked(Serialized + size);
            return result;
        }

        public void TempRoot(int size)
        {
            TempMax = Math.Max(TempMax, size);
            Serialized = checked(Serialized + size);
        }
    }

    private sealed class Graph
    {
        public string Format { get; set; } = "";
        public string Name { get; set; } = "";
        public AbiData Abi { get; set; } = new();
        public AlignmentData Alignments { get; set; } = new();
        public OffsetData Offsets { get; set; } = new();
        public CountData Counts { get; set; } = new();
        public PresenceData Presence { get; set; } = new();
        public ReusableData Reusable { get; set; } = new();
        public string? TechniqueSet { get; set; }
        public string RootFile { get; set; } = "";
        public string? TextureTableFile { get; set; }
        public string? ConstantTableFile { get; set; }
        public string? StateBitsTableFile { get; set; }
        public List<TextureData> Textures { get; set; } = new();
        public InventoryData Inventory { get; set; } = new();
    }

    private sealed class AbiData
    {
        public int Material { get; set; }
        public int TextureDef { get; set; }
        public int ConstantDef { get; set; }
        public int StateBits { get; set; }
        public int Water { get; set; }
        public int Float { get; set; }
    }

    private sealed class AlignmentData
    {
        public int Material { get; set; }
        public int TextureDef { get; set; }
        public int ConstantDef { get; set; }
        public int StateBits { get; set; }
        public int Water { get; set; }
        public int Float { get; set; }
    }

    private sealed class OffsetData
    {
        public int TechniqueSet { get; set; }
        public int TextureUnion { get; set; }
        public int WaterImage { get; set; }
    }

    private sealed class CountData
    {
        public int Textures { get; set; }
        public int Constants { get; set; }
        public int StateBits { get; set; }
    }

    private sealed class PresenceData
    {
        public bool Name { get; set; }
        public bool TextureTable { get; set; }
        public bool ConstantTable { get; set; }
        public bool StateBitsTable { get; set; }
    }

    private sealed class ReusableData
    {
        public int UniquePointers { get; set; }
        public int AliasCount { get; set; }
    }

    private sealed class TextureData
    {
        public int Index { get; set; }
        public int Semantic { get; set; }
        public string? Image { get; set; }
        public JsonElement Water { get; set; }
    }

    private sealed class InventoryData
    {
        public int WaterTextures { get; set; }
        public long WaterSamples { get; set; }
        public int InvalidWaterDimensions { get; set; }
    }
}
