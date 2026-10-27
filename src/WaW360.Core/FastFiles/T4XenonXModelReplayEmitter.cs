using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed class T4XenonXModelReplayResult
{
    public required string GraphFile { get; init; }
    public required string ReferenceFastFile { get; init; }
    public required string OutputFastFile { get; init; }
    public required string OutputZoneFile { get; init; }
    public required string ModelName { get; init; }
    public required int Roots { get; init; }
    public required int ScriptStrings { get; init; }
    public required long ZoneBytes { get; init; }
    public required long TempBytes { get; init; }
    public required long VirtualBytes { get; init; }
    public required bool ExactBlockReplay { get; init; }
    public required bool ExactZoneSizeReplay { get; init; }
    public required bool ExactDecompressedSizeReplay { get; init; }
    public required bool ExactIdentitySequence { get; init; }
    public required bool OatZeroWarningsZeroErrors { get; init; }
    public required bool GraphRedumpExact { get; init; }
    public required bool WrapperRoundTripVerified { get; init; }
    public required bool ReplayPass { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

public static class T4XenonXModelReplayEmitter
{
    private const int XFileHeaderSize = 36;

    public static async Task<T4XenonXModelReplayResult> EmitAsync(
        string graphFile,
        string referenceFastFile,
        string outputDirectory,
        string explicitUnlinkerExe,
        CancellationToken cancellationToken = default)
    {
        graphFile = Path.GetFullPath(graphFile);
        referenceFastFile = Path.GetFullPath(referenceFastFile);
        outputDirectory = Path.GetFullPath(outputDirectory);
        explicitUnlinkerExe = Path.GetFullPath(explicitUnlinkerExe);
        Directory.CreateDirectory(outputDirectory);

        var graph = LoadGraph(graphFile);
        var graphRoot = Directory.GetParent(Path.GetDirectoryName(graphFile)!)!.FullName;

        if (graph.Format != "WaW360.T4Xenon.XModelRelocatableGraph.v1")
            throw new InvalidDataException($"Unexpected XModel graph format '{graph.Format}'.");
        if (graph.Abi.XModel != 0xD4)
            throw new InvalidDataException($"XModel graph ABI is {graph.Abi.XModel}; expected 0xD4.");
        if (graph.Unsupported.Count != 0)
            throw new InvalidDataException("Replay refuses unsupported XModel graph branches.");
        if (graph.Reusable.AliasCount != 0)
            throw new InvalidDataException(
                $"Replay refuses {graph.Reusable.AliasCount} reusable pointer aliases until alias relocation is implemented.");

        var reference = await FastFileAnalysisCache.GetZoneAsync(referenceFastFile, cancellationToken);
        if (reference.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException("XModel replay reference must be Xbox360.");
        if (reference.ZoneHeader.BlockSizes.Count != 7)
            throw new InvalidDataException("Reference XFile does not expose seven T4 blocks.");

        var referenceIdentity = await T4XenonNativeIdentityOracle.RunAsync(
            referenceFastFile,
            Path.Combine(outputDirectory, "reference_identity"),
            explicitUnlinkerExe,
            cancellationToken);

        var ownedXModels = referenceIdentity.Entries
            .Where(x =>
                NormalizeType(x.OatTypeName) == "xmodel" &&
                !x.Name.StartsWith(",", StringComparison.Ordinal))
            .ToList();
        if (ownedXModels.Count != 1 ||
            !ownedXModels[0].Name.Equals(graph.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Reference must contain exactly one owned XModel '{graph.Name}'; found " +
                string.Join(", ", ownedXModels.Select(x => x.Name)));

        var unsupportedRoots = referenceIdentity.Entries
            .Where(x =>
            {
                var type = NormalizeType(x.OatTypeName);
                return type is not ("material" or "physpreset" or "xmodel");
            })
            .ToList();
        if (unsupportedRoots.Count != 0)
            throw new InvalidDataException(
                "Standalone XModel replay encountered unsupported reference root types: " +
                string.Join(", ", unsupportedRoots.Select(x => $"{x.OatTypeName}:{x.Name}")));

        var state = new ReplayState();
        using var zone = new MemoryStream();
        zone.Write(new byte[XFileHeaderSize]);

        WriteU32(zone, checked((uint)reference.Document.ScriptStrings.Count));
        WriteU32(
            zone,
            reference.Document.ScriptStrings.Count == 0 ? 0u : T4ZonePointer.Following);
        WriteU32(zone, checked((uint)referenceIdentity.Entries.Count));
        WriteU32(
            zone,
            referenceIdentity.Entries.Count == 0 ? 0u : T4ZonePointer.Following);

        EmitScriptStrings(zone, state, reference.Document.ScriptStrings);

        state.VirtualAlloc(checked(referenceIdentity.Entries.Count * 8L), 4);
        foreach (var root in referenceIdentity.Entries.OrderBy(x => x.Index))
        {
            WriteU32(zone, checked((uint)root.TypeId));
            WriteU32(zone, T4ZonePointer.Insert);
        }

        var stableByRoot = new Dictionary<int, uint>();
        foreach (var root in referenceIdentity.Entries.OrderBy(x => x.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var insertOffset = state.VirtualAlloc(4, 4, serialized: false);
            var stablePointer = T4ZonePointer.Encode(
                T4ZoneBlock.Virtual,
                checked((uint)insertOffset));
            stableByRoot.Add(root.Index, stablePointer);

            var type = NormalizeType(root.OatTypeName);
            if (type == "xmodel" &&
                !root.Name.StartsWith(",", StringComparison.Ordinal) &&
                root.Name.Equals(graph.Name, StringComparison.OrdinalIgnoreCase))
            {
                EmitXModel(
                    zone,
                    state,
                    graphRoot,
                    graph,
                    reference,
                    referenceIdentity.Entries,
                    stableByRoot);
                continue;
            }

            EmitReferenceShell(zone, state, root.OatTypeName, root.Name);
        }

        PatchHeader(
            zone,
            checked((uint)(zone.Length - XFileHeaderSize)),
            checked((uint)state.TempMax),
            checked((uint)state.Virtual));

        var zoneBytes = zone.ToArray();
        var zonePath = Path.Combine(outputDirectory, "nuclear_4xc_xmodel_replay.zone");
        await File.WriteAllBytesAsync(zonePath, zoneBytes, cancellationToken);

        var parsed = ZoneHeaderParser.Parse(
            zoneBytes.AsSpan(0, XFileHeaderSize),
            Endian.Big,
            zoneBytes.LongLength);
        if (!parsed.Plausible)
            throw new InvalidDataException("Replayed XModel zone header is not plausible.");

        var outputFf = Path.Combine(outputDirectory, "nuclear_4xc_xmodel_replay.ff");
        var wrapped = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
            outputFf,
            zoneBytes,
            preferNativeLevel6: true,
            cancellationToken);

        var blockers = new List<string>();

        var outputIdentity = await T4XenonNativeIdentityOracle.RunAsync(
            outputFf,
            Path.Combine(outputDirectory, "output_identity"),
            explicitUnlinkerExe,
            cancellationToken);

        var exactIdentity =
            outputIdentity.Entries.Count == referenceIdentity.Entries.Count &&
            outputIdentity.Entries.Zip(referenceIdentity.Entries).All(pair =>
                pair.First.TypeId == pair.Second.TypeId &&
                pair.First.Name.Equals(pair.Second.Name, StringComparison.Ordinal));

        var oatLog = await File.ReadAllTextAsync(
            outputIdentity.RawUnlinkerLogFile,
            cancellationToken);
        var oatZero = HasZeroWarningErrorSummary(oatLog);

        if (!exactIdentity)
            blockers.Add("OAT output root identity sequence differs from the reference XModel FF.");
        if (!oatZero)
            blockers.Add("OAT identity reopen did not prove 0 warnings / 0 errors.");

        var referenceBlocks = reference.ZoneHeader.BlockSizes.ToArray();
        var outputBlocks = parsed.BlockSizes.ToArray();
        var exactBlocks = referenceBlocks.SequenceEqual(outputBlocks);
        var exactZoneSize = reference.ZoneHeader.ZoneSize == parsed.ZoneSize;
        var exactDecompressed = reference.Bytes.LongLength == zoneBytes.LongLength;

        if (!exactBlocks)
            blockers.Add(
                "Block sizes differ: reference=[" + string.Join(",", referenceBlocks) +
                "] output=[" + string.Join(",", outputBlocks) + "].");
        if (!exactZoneSize)
            blockers.Add(
                $"zoneSize differs: reference={reference.ZoneHeader.ZoneSize:N0}, output={parsed.ZoneSize:N0}.");
        if (!exactDecompressed)
            blockers.Add(
                $"Decompressed size differs: reference={reference.Bytes.LongLength:N0}, output={zoneBytes.LongLength:N0}.");

        // Keep verification redumps out of the already-deep map output tree.
        // XModel collision graphs append asset names plus files such as
        // s###.vl###.tree.nodes.bin; under the suite output this can cross the
        // legacy Win32 MAX_PATH boundary and make a correct replay look like a
        // graph mismatch because only the deepest dump files fail to open.
        var redumpKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                Path.GetFullPath(outputFf) + "|" + graph.Name)))[..12].ToLowerInvariant();
        var redumpDir = Path.Combine(
            Path.GetTempPath(),
            "WaW360",
            "xmr",
            redumpKey);
        if (Directory.Exists(redumpDir))
            Directory.Delete(redumpDir, recursive: true);
        Directory.CreateDirectory(redumpDir);

        var redumpLog = Path.Combine(outputDirectory, "redump.log");
        var redump = await RunProcessAsync(
            explicitUnlinkerExe,
            new[]
            {
                "--no-color",
                "--include-assets", "xmodel",
                "--model-format", "gltf",
                "--output-folder", redumpDir,
                outputFf
            },
            redumpLog,
            cancellationToken);

        if (redump.ExitCode != 0)
            blockers.Add($"Specialized OAT XModel redump failed with exit {redump.ExitCode}.");
        if (!HasZeroWarningErrorSummary(redump.Output))
            blockers.Add("Specialized OAT XModel redump did not prove 0 warnings / 0 errors.");

        var graphRedumpExact = false;
        if (redump.ExitCode == 0)
        {
            var redumpGraphs = Directory.Exists(Path.Combine(redumpDir, "xenon_xmodel_graphs"))
                ? Directory.GetFiles(
                    Path.Combine(redumpDir, "xenon_xmodel_graphs"),
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            if (redumpGraphs.Length != 1)
            {
                blockers.Add(
                    $"Expected exactly one relocatable XModel graph after replay; found {redumpGraphs.Length}. " +
                    $"Verification redump root: {redumpDir}");
            }
            else
            {
                graphRedumpExact = CompareRelocatableGraphTrees(
                    graphRoot,
                    graphFile,
                    redumpDir,
                    redumpGraphs[0],
                    out var compareError);
                if (!graphRedumpExact)
                    blockers.Add(
                        "Relocatable graph redump differs: " + compareError +
                        $" | verification redump root: {redumpDir}");
            }
        }

        if (!wrapped.RoundTripVerified)
            blockers.Add("Fastfile wrapper round-trip verification failed.");

        var pass =
            blockers.Count == 0 &&
            exactBlocks &&
            exactZoneSize &&
            exactDecompressed &&
            exactIdentity &&
            oatZero &&
            graphRedumpExact &&
            wrapped.RoundTripVerified;

        var result = new T4XenonXModelReplayResult
        {
            GraphFile = graphFile,
            ReferenceFastFile = referenceFastFile,
            OutputFastFile = outputFf,
            OutputZoneFile = zonePath,
            ModelName = graph.Name,
            Roots = referenceIdentity.Entries.Count,
            ScriptStrings = reference.Document.ScriptStrings.Count,
            ZoneBytes = zoneBytes.LongLength,
            TempBytes = state.TempMax,
            VirtualBytes = state.Virtual,
            ExactBlockReplay = exactBlocks,
            ExactZoneSizeReplay = exactZoneSize,
            ExactDecompressedSizeReplay = exactDecompressed,
            ExactIdentitySequence = exactIdentity,
            OatZeroWarningsZeroErrors = oatZero,
            GraphRedumpExact = graphRedumpExact,
            WrapperRoundTripVerified = wrapped.RoundTripVerified,
            ReplayPass = pass,
            Blockers = blockers,
            TextReportFile = Path.Combine(outputDirectory, "nuclear_4xc_xmodel_replay.txt"),
            JsonReportFile = Path.Combine(outputDirectory, "nuclear_4xc_xmodel_replay.json")
        };

        await WriteResult(result, cancellationToken);
        return result;
    }

    private static void EmitXModel(
        Stream zone,
        ReplayState state,
        string graphRoot,
        Graph graph,
        FastFileAnalysisCache.CachedZone reference,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyDictionary<int, uint> stableByRoot)
    {
        state.TempRoot(graph.Abi.XModel);

        var rootBody = ReadBlob(graphRoot, graph.RootFile, graph.Abi.XModel, "XModel root");
        if (!string.IsNullOrWhiteSpace(graph.PhysPreset))
        {
            var stable = ResolveAssetStablePointer(
                roots,
                stableByRoot,
                "physpreset",
                graph.PhysPreset!);
            PutU32(rootBody, graph.Offsets.PhysPreset, stable);
        }
        zone.Write(rootBody);

        EmitString(zone, state, graph.Name);

        if (graph.BoneNames.Count != 0)
        {
            state.VirtualAlloc(checked(graph.BoneNames.Count * 2L), 2);
            foreach (var bone in graph.BoneNames.OrderBy(x => x.Index))
            {
                if (bone.SourceScriptString < 0 ||
                    bone.SourceScriptString >= reference.Document.ScriptStrings.Count)
                    throw new InvalidDataException(
                        $"{graph.Name}: bone '{bone.Name}' script index {bone.SourceScriptString} is outside reference table.");

                var entry = reference.Document.ScriptStrings[bone.SourceScriptString];
                if (entry.Value is null ||
                    !entry.Value.Equals(bone.Name, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"{graph.Name}: bone '{bone.Name}' does not match reference ScriptString #{bone.SourceScriptString}.");

                WriteU16(zone, checked((ushort)bone.SourceScriptString));
            }
        }

        EmitOptionalBlob(zone, state, graphRoot, graph.ParentListFile, graph.Counts.NonRootBones, 1, "parentList");
        EmitOptionalBlob(zone, state, graphRoot, graph.QuatsFile, checked((long)graph.Counts.NonRootBones * graph.Abi.XModelQuat), 2, "quats");
        EmitOptionalBlob(zone, state, graphRoot, graph.TransFile, checked((long)graph.Counts.NonRootBones * 16L), 4, "trans");
        EmitOptionalBlob(zone, state, graphRoot, graph.PartClassificationFile, graph.Counts.NumBones, 1, "partClassification");
        EmitOptionalBlob(zone, state, graphRoot, graph.BaseMatFile, checked((long)graph.Counts.NumBones * graph.Abi.DObjAnimMat), 4, "baseMat");

        EmitOptionalBlob(
            zone,
            state,
            graphRoot,
            graph.SurfacesFile,
            checked((long)graph.Counts.NumSurfs * graph.Abi.XSurface),
            4,
            "surfaces");

        foreach (var surface in graph.Surfaces.OrderBy(x => x.Index))
        {
            EmitOptionalBlob(
                zone, state, graphRoot, surface.VertsBlendFile,
                checked((long)surface.VertsBlendCount * 2L),
                2,
                $"surface[{surface.Index}].vertsBlend");

            EmitOptionalBlob(
                zone, state, graphRoot, surface.Verts0File,
                checked((long)surface.VertCount * graph.Abi.PackedVertex),
                16,
                $"surface[{surface.Index}].verts0");

            EmitOptionalBlob(
                zone, state, graphRoot, surface.VertListFile,
                checked((long)surface.VertListCount * graph.Abi.RigidVertList),
                4,
                $"surface[{surface.Index}].vertList");

            foreach (var tree in surface.CollisionTrees.OrderBy(x => x.VertListIndex))
            {
                EmitOptionalBlob(
                    zone, state, graphRoot, tree.TreeFile,
                    graph.Abi.CollisionTree,
                    4,
                    $"surface[{surface.Index}].tree[{tree.VertListIndex}]");

                EmitOptionalBlob(
                    zone, state, graphRoot, tree.NodesFile,
                    checked((long)tree.NodeCount * graph.Abi.CollisionNode),
                    16,
                    $"surface[{surface.Index}].tree[{tree.VertListIndex}].nodes");

                EmitOptionalBlob(
                    zone, state, graphRoot, tree.LeafsFile,
                    checked((long)tree.LeafCount * graph.Abi.CollisionLeaf),
                    2,
                    $"surface[{surface.Index}].tree[{tree.VertListIndex}].leafs");
            }

            EmitOptionalBlob(
                zone, state, graphRoot, surface.TriIndicesFile,
                checked((long)surface.TriCount * graph.Abi.TriIndex),
                16,
                $"surface[{surface.Index}].triIndices");
        }

        if (graph.Materials.Count != 0)
        {
            state.VirtualAlloc(checked(graph.Materials.Count * 4L), 4);
            foreach (var material in graph.Materials)
            {
                if (string.IsNullOrWhiteSpace(material))
                {
                    WriteU32(zone, 0);
                    continue;
                }

                var stable = ResolveAssetStablePointer(
                    roots,
                    stableByRoot,
                    "material",
                    material!);
                WriteU32(zone, stable);
            }
        }

        EmitOptionalBlob(
            zone, state, graphRoot, graph.BoneInfoFile,
            checked((long)graph.Counts.NumBones * graph.Abi.BoneInfo),
            4,
            "boneInfo");

        EmitOptionalBlob(
            zone, state, graphRoot, graph.HighMipBoundsFile,
            checked((long)graph.Counts.HighMipBounds * graph.Abi.HighMipBounds),
            4,
            "highMipBounds");
    }

    private static uint ResolveAssetStablePointer(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        IReadOnlyDictionary<int, uint> stableByRoot,
        string type,
        string name)
    {
        var candidates = roots
            .Where(x =>
                NormalizeType(x.OatTypeName) == NormalizeType(type) &&
                NormalizeReferenceName(x.Name).Equals(
                    NormalizeReferenceName(name),
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count != 1)
            throw new InvalidDataException(
                $"Asset reference {type}:{name} resolved to {candidates.Count} root(s).");

        if (!stableByRoot.TryGetValue(candidates[0].Index, out var stable))
            throw new InvalidDataException(
                $"Asset reference {type}:{name} points forward to root #{candidates[0].Index}; " +
                "deadline replay currently requires dependency-first OAT root ordering.");

        return stable;
    }

    private static void EmitReferenceShell(
        Stream zone,
        ReplayState state,
        string typeName,
        string name)
    {
        var type = NormalizeType(typeName);
        var size = type switch
        {
            "material" => 96,
            "physpreset" => 48,
            "xmodel" when name.StartsWith(",", StringComparison.Ordinal) => 0xD4,
            _ => throw new InvalidDataException(
                $"No standalone XModel reference-shell ABI for {typeName}:{name}.")
        };

        var body = new byte[size];
        PutU32(body, 0, T4ZonePointer.Following);
        state.TempRoot(size);
        zone.Write(body);
        EmitString(zone, state, name);
    }

    private static void EmitOptionalBlob(
        Stream zone,
        ReplayState state,
        string graphRoot,
        string? relative,
        long expectedBytes,
        int alignment,
        string label)
    {
        if (expectedBytes == 0)
        {
            if (!string.IsNullOrWhiteSpace(relative))
            {
                var bytes = ReadBlob(graphRoot, relative!, -1, label);
                if (bytes.Length != 0)
                    throw new InvalidDataException(
                        $"{label}: graph points to {bytes.Length:N0} bytes but expected zero.");
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(relative))
            throw new InvalidDataException($"{label}: graph blob path is missing.");

        var blob = ReadBlob(graphRoot, relative!, expectedBytes, label);
        state.VirtualAlloc(expectedBytes, alignment);
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
            throw new FileNotFoundException($"{label} blob not found.", full);

        var bytes = File.ReadAllBytes(full);
        if (expectedBytes >= 0 && bytes.LongLength != expectedBytes)
            throw new InvalidDataException(
                $"{label}: {bytes.LongLength:N0} bytes, expected {expectedBytes:N0}.");
        return bytes;
    }

    private static string ResolveGraphPath(string root, string relative)
    {
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Graph path escapes dump root: {relative}");
        return full;
    }

    private static void EmitScriptStrings(
        Stream zone,
        ReplayState state,
        IReadOnlyList<T4ScriptStringEntry> strings)
    {
        if (strings.Count == 0)
            return;

        state.VirtualAlloc(checked(strings.Count * 4L), 4);
        foreach (var item in strings)
            WriteU32(zone, item.RawPointer == 0 ? 0u : T4ZonePointer.Following);

        foreach (var item in strings)
        {
            if (item.RawPointer == 0)
                continue;
            if (item.Value is null || item.Value.StartsWith("<alias ", StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Unresolved ScriptString #{item.Index} cannot be replayed.");
            EmitString(zone, state, item.Value);
        }
    }

    private static void EmitString(Stream zone, ReplayState state, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        state.VirtualAlloc(bytes.Length + 1L, 1);
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

    private static bool CompareRelocatableGraphTrees(
        string sourceRoot,
        string sourceGraphFile,
        string redumpRoot,
        string redumpGraphFile,
        out string error)
    {
        var sourceGraphBytes = File.ReadAllBytes(sourceGraphFile);
        var redumpGraphBytes = File.ReadAllBytes(redumpGraphFile);
        if (!sourceGraphBytes.AsSpan().SequenceEqual(redumpGraphBytes))
        {
            error =
                $"graph JSON SHA256 {Hash(sourceGraphBytes)} != {Hash(redumpGraphBytes)}";
            return false;
        }

        var sourceBlobRoot = Path.Combine(sourceRoot, "xenon_xmodel_graph_blobs");
        var redumpBlobRoot = Path.Combine(redumpRoot, "xenon_xmodel_graph_blobs");
        var sourceFiles = Directory.Exists(sourceBlobRoot)
            ? Directory.GetFiles(sourceBlobRoot, "*", SearchOption.AllDirectories)
                .Select(x => Path.GetRelativePath(sourceBlobRoot, x).Replace('\\', '/'))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();
        var redumpFiles = Directory.Exists(redumpBlobRoot)
            ? Directory.GetFiles(redumpBlobRoot, "*", SearchOption.AllDirectories)
                .Select(x => Path.GetRelativePath(redumpBlobRoot, x).Replace('\\', '/'))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();

        if (!sourceFiles.SequenceEqual(redumpFiles, StringComparer.Ordinal))
        {
            error =
                $"blob file set differs: source={sourceFiles.Length}, redump={redumpFiles.Length}";
            return false;
        }

        foreach (var relative in sourceFiles)
        {
            var left = File.ReadAllBytes(
                Path.Combine(sourceBlobRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            var right = File.ReadAllBytes(
                Path.Combine(redumpBlobRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!left.AsSpan().SequenceEqual(right))
            {
                error =
                    $"{relative} SHA256 {Hash(left)} != {Hash(right)}";
                return false;
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
            ?? throw new InvalidOperationException($"Could not start '{exe}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout) + (await stderr);
        await File.WriteAllTextAsync(logFile, output, new UTF8Encoding(false), cancellationToken);
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
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return graph ?? throw new InvalidDataException("Could not parse XModel graph JSON.");
    }

    private static async Task WriteResult(
        T4XenonXModelReplayResult result,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("NUCLEAR STAGE 4X-C - STANDALONE XENON XMODEL REPLAY");
        sb.AppendLine("====================================================");
        sb.AppendLine($"Model:                     {result.ModelName}");
        sb.AppendLine($"Reference FF:              {result.ReferenceFastFile}");
        sb.AppendLine($"Output FF:                 {result.OutputFastFile}");
        sb.AppendLine($"Roots:                     {result.Roots:N0}");
        sb.AppendLine($"ScriptStrings:             {result.ScriptStrings:N0}");
        sb.AppendLine($"Zone bytes:                {result.ZoneBytes:N0}");
        sb.AppendLine($"TEMP:                      0x{result.TempBytes:X}");
        sb.AppendLine($"VIRTUAL:                   0x{result.VirtualBytes:X}");
        sb.AppendLine($"Exact block replay:        {result.ExactBlockReplay}");
        sb.AppendLine($"Exact zoneSize replay:     {result.ExactZoneSizeReplay}");
        sb.AppendLine($"Exact decompressed size:   {result.ExactDecompressedSizeReplay}");
        sb.AppendLine($"Exact identity sequence:   {result.ExactIdentitySequence}");
        sb.AppendLine($"OAT 0 warnings / 0 errors: {result.OatZeroWarningsZeroErrors}");
        sb.AppendLine($"Graph redump exact:        {result.GraphRedumpExact}");
        sb.AppendLine($"Wrapper round-trip:        {result.WrapperRoundTripVerified}");
        sb.AppendLine($"REPLAY PASS:               {result.ReplayPass}");
        sb.AppendLine();
        foreach (var blocker in result.Blockers)
            sb.AppendLine("BLOCKER: " + blocker);

        await File.WriteAllTextAsync(
            result.TextReportFile,
            sb.ToString(),
            new UTF8Encoding(false),
            cancellationToken);
        await File.WriteAllTextAsync(
            result.JsonReportFile,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);
    }

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

    private static void PutU32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);

    private sealed record ProcessResult(int ExitCode, string Output);

    private sealed class ReplayState
    {
        private long _virtual;
        public long Virtual => _virtual;
        public long TempMax { get; private set; }

        public long VirtualAlloc(long size, int alignment, bool serialized = true)
        {
            var mask = alignment - 1L;
            _virtual = checked((_virtual + mask) & ~mask);
            var result = _virtual;
            _virtual = checked(_virtual + size);
            return result;
        }

        public void TempRoot(int size) => TempMax = Math.Max(TempMax, size);
    }

    private sealed class Graph
    {
        public string Format { get; set; } = "";
        public string Name { get; set; } = "";
        public AbiData Abi { get; set; } = new();
        public OffsetData Offsets { get; set; } = new();
        public CountData Counts { get; set; } = new();
        public UnsupportedData Unsupported { get; set; } = new();
        public ReusableData Reusable { get; set; } = new();
        public string RootFile { get; set; } = "";
        public List<BoneData> BoneNames { get; set; } = new();
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
        public List<SurfaceData> Surfaces { get; set; } = new();
    }

    private sealed class AbiData
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

    private sealed class OffsetData
    {
        public int PhysPreset { get; set; }
    }

    private sealed class CountData
    {
        public int NumBones { get; set; }
        public int NumRootBones { get; set; }
        public int NonRootBones { get; set; }
        public int NumSurfs { get; set; }
        public int NumLods { get; set; }
        public int HighMipBounds { get; set; }
    }

    private sealed class UnsupportedData
    {
        public int Count { get; set; }
    }

    private sealed class ReusableData
    {
        public int AliasCount { get; set; }
    }

    private sealed class BoneData
    {
        public int Index { get; set; }
        public int SourceScriptString { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class SurfaceData
    {
        public int Index { get; set; }
        public int VertCount { get; set; }
        public int TriCount { get; set; }
        public int VertListCount { get; set; }
        public int VertsBlendCount { get; set; }
        public string? VertsBlendFile { get; set; }
        public string? Verts0File { get; set; }
        public string? VertListFile { get; set; }
        public string? TriIndicesFile { get; set; }
        public List<CollisionTreeData> CollisionTrees { get; set; } = new();
    }

    private sealed class CollisionTreeData
    {
        public int VertListIndex { get; set; }
        public int NodeCount { get; set; }
        public int LeafCount { get; set; }
        public string TreeFile { get; set; } = "";
        public string? NodesFile { get; set; }
        public string? LeafsFile { get; set; }
    }
}
