using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed class T4XenonRendererEmitResult
{
    public required string OutputFastFile { get; init; }
    public required string OutputZoneFile { get; init; }
    public required int NativeRoots { get; init; }
    public required int RendererTechniqueSets { get; init; }
    public required int RendererPixelShaders { get; init; }
    public required int RealImageRoots { get; init; }
    public required int RealMaterialRoots { get; init; }
    public required int MaterialReferenceRoots { get; init; }
    public required int RealXModelRoots { get; init; }
    public required int XModelReferenceRoots { get; init; }
    public required long ZoneBytes { get; init; }
    public required long MainZoneBytes { get; init; }
    public required long LargeRuntimeBytes { get; init; }
    public required long LargeBytes { get; init; }
    public required long TempBytes { get; init; }
    public required long VirtualBytes { get; init; }
    public required long PhysicalBytes { get; init; }
    public required int OatListedRoots { get; init; }
    public required int OatLoadedAssets { get; init; }
    public required int OatExternalReferenceAssets { get; init; }
    public required bool OatZeroWarningsZeroErrors { get; init; }
    public required bool ExactIdentitySequence { get; init; }
    public required bool LayoutReplayExact { get; init; }
    public required bool ValidationOnly { get; init; }
    public required bool ValidationPass { get; init; }
    public required bool ReadyForHardware { get; init; }
    public required string? OatGateError { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

public static class T4XenonRendererGraphEmitter
{
    private const int TechniqueSetSize = 216;
    private const int TechniqueHeaderSize = 8;
    private const int PassSize = 88;
    private const int VertexDeclSize = 68;
    private const int VertexShaderSize = 16;
    private const int PixelShaderSize = 16;
    private const int ShaderArgSize = 8;
    private const int LiteralSize = 16;

    public static async Task<T4XenonRendererEmitResult> EmitAsync(
        string dumpRoot,
        string nativeFastFile,
        string nativeIdentityManifest,
        string bindingManifest,
        string outputDirectory,
        string canonicalBaseName,
        string? explicitUnlinkerExe = null,
        int expectedTechniqueSets = 87,
        int expectedPixelShaders = 897,
        CancellationToken cancellationToken = default,
        string? imageGraphDumpRoot = null,
        int expectedImages = 0,
        bool imageGraphValidationOnly = true,
        int imageGraftLimit = 0,
        string? xmodelGraphRoot = null,
        string? xmodelBindingManifest = null,
        string? materialGraphRoot = null,
        string? materialBindingManifest = null)
    {
        dumpRoot = Path.GetFullPath(dumpRoot);
        nativeFastFile = Path.GetFullPath(nativeFastFile);
        nativeIdentityManifest = Path.GetFullPath(nativeIdentityManifest);
        bindingManifest = Path.GetFullPath(bindingManifest);
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var graph = T4XenonTechniqueGraphImporter.Load(
            dumpRoot, expectedTechniqueSets, expectedPixelShaders);
        if (!graph.Verified)
            throw new InvalidDataException("Stage 4E requires a verified fixed-up Xenon graph.");

        T4XenonImageGraphImportResult? imageGraph = null;
        if (!string.IsNullOrWhiteSpace(imageGraphDumpRoot))
        {
            imageGraph = T4XenonImageGraphImporter.Load(
                imageGraphDumpRoot,
                expectedImages > 0 ? expectedImages : null);
            if (!imageGraph.Verified)
                throw new InvalidDataException(
                    "Stage 4E image graph is not verified: " +
                    string.Join(" | ", imageGraph.Errors.Take(20)));
        }

        T4XenonXModelCorpus? xmodelCorpus = null;
        if (!string.IsNullOrWhiteSpace(xmodelGraphRoot) ||
            !string.IsNullOrWhiteSpace(xmodelBindingManifest))
        {
            if (string.IsNullOrWhiteSpace(xmodelGraphRoot) ||
                string.IsNullOrWhiteSpace(xmodelBindingManifest))
                throw new InvalidDataException(
                    "Stage 4E requires both XModel graph root and XModel binding manifest.");

            xmodelCorpus = T4XenonXModelCorpus.Load(
                xmodelGraphRoot!,
                xmodelBindingManifest!);
        }

        // Re-run the corrected Stage 4D allocator inside 4E. This makes byte
        // emission fail closed if traversal order and allocation ever drift.
        var layout = await T4XenonGraphBlockLayoutPlanner.BuildAsync(
            dumpRoot,
            nativeFastFile,
            nativeIdentityManifest,
            bindingManifest,
            Path.Combine(outputDirectory, "layout_recheck"),
            canonicalBaseName,
            expectedTechniqueSets,
            expectedPixelShaders,
            cancellationToken,
            imageGraphDumpRoot,
            expectedImages,
            imageGraftLimit,
            xmodelGraphRoot,
            xmodelBindingManifest,
            materialGraphRoot,
            materialBindingManifest);
        if (!layout.LayoutReady)
            throw new InvalidDataException(
                "Stage 4E layout recheck failed: " + string.Join(" | ", layout.Blockers));

        var native = await FastFileAnalysisCache.GetZoneAsync(nativeFastFile, cancellationToken);
        var roots = LoadNativeIdentity(nativeIdentityManifest);
        var bindings = LoadBindings(bindingManifest);

        T4XenonMaterialCorpus? materialCorpus = null;
        if (!string.IsNullOrWhiteSpace(materialGraphRoot) ||
            !string.IsNullOrWhiteSpace(materialBindingManifest))
        {
            if (string.IsNullOrWhiteSpace(materialGraphRoot) ||
                string.IsNullOrWhiteSpace(materialBindingManifest))
                throw new InvalidDataException(
                    "Stage 4E requires both Material graph root and Material binding manifest.");

            materialCorpus = T4XenonMaterialCorpus.Load(
                materialGraphRoot!,
                materialBindingManifest!,
                roots);
            var materialDependencyErrors = materialCorpus.ValidateDependencies(roots);
            if (materialDependencyErrors.Count != 0)
                throw new InvalidDataException(
                    "Stage 4E Material dependency validation failed: " +
                    string.Join(" | ", materialDependencyErrors.Take(20)));
        }

        if (xmodelCorpus is not null)
        {
            var dependencyErrors = xmodelCorpus.ValidateDependencies(roots);
            if (dependencyErrors.Count != 0)
                throw new InvalidDataException(
                    "Stage 4E XModel dependency validation failed: " +
                    string.Join(" | ", dependencyErrors.Take(20)));
        }
        if (!bindings.Ready)
            throw new InvalidDataException("Stage 4C binding manifest is not ready.");

        var techniqueByRoot = bindings.Techniques.ToDictionary(x => x.NativeRootIndex);
        var pixelByRoot = bindings.Pixels.ToDictionary(x => x.NativeRootIndex);
        var pixelByName = bindings.Pixels.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var pixelDefs = CollectPixelShaders(graph);

        var plannedTechniques = layout.Nodes
            .Where(x => x.Kind == "technique")
            .ToDictionary(x => x.Key, x => x.Offset, StringComparer.Ordinal);
        var plannedDecls = layout.Nodes
            .Where(x => x.Kind == "vertex-decl")
            .ToDictionary(x => x.Key, x => x.Offset, StringComparer.Ordinal);
        var plannedVertexShaders = layout.Nodes
            .Where(x => x.Kind == "vertex-shader")
            .ToDictionary(x => x.Key, x => x.Offset, StringComparer.Ordinal);
        var plannedLiterals = layout.Nodes
            .Where(x => x.Kind == "literal")
            .ToDictionary(x => x.Key, x => x.Offset, StringComparer.Ordinal);

        var plannedRootInsert = layout.RootInsertLayouts
            .ToDictionary(x => x.RootIndex);
        var stableByRoot = layout.RootInsertLayouts
            .ToDictionary(x => x.RootIndex, x => x.StablePointer);

        var state = new EmitState();
        var blobIndex = BuildBlobIndex(dumpRoot);
        var pixelStable = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var emittedTechniques = new HashSet<string>(StringComparer.Ordinal);
        var emittedDecls = new HashSet<string>(StringComparer.Ordinal);
        var emittedVertexShaders = new HashSet<string>(StringComparer.Ordinal);
        var emittedLiterals = new HashSet<string>(StringComparer.Ordinal);

        using var zone = new MemoryStream(capacity: checked((int)Math.Min(int.MaxValue, Math.Max(4096, layout.ProjectedUncompressedZoneBytes - layout.LargeRuntimeBytes + 4096))));
        using var largeRuntime = new MemoryStream(capacity: checked((int)Math.Min(int.MaxValue, Math.Max(0, layout.LargeRuntimeBytes))));
        zone.Write(new byte[36]);

        WriteU32(zone, checked((uint)native.Document.ScriptStrings.Count));
        WriteU32(zone, native.Document.ScriptStrings.Count == 0 ? 0u : T4ZonePointer.Following);
        WriteU32(zone, checked((uint)roots.Count));
        WriteU32(zone, roots.Count == 0 ? 0u : T4ZonePointer.Following);

        EmitScriptStrings(zone, state, native.Document.ScriptStrings);

        state.VirtualAlloc(checked(roots.Count * 8L), 4, serialized: true);
        foreach (var root in roots.OrderBy(x => x.Index))
        {
            WriteU32(zone, checked((uint)root.TypeId));
            WriteU32(zone, T4ZonePointer.Insert);
        }

        var rendererTechsets = 0;
        var rendererPixels = 0;
        var realImages = 0;
        var realMaterials = 0;
        var realXModels = 0;
        var targetImageRoots = imageGraph is null
            ? 0
            : imageGraftLimit > 0
                ? Math.Min(imageGraftLimit, imageGraph.Images)
                : imageGraph.Images;

        foreach (var root in roots.OrderBy(x => x.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var insertOffset = state.VirtualAlloc(4, 4, serialized: false);
            if (!plannedRootInsert.TryGetValue(root.Index, out var plannedRoot))
                throw new InvalidDataException(
                    $"Missing planned root insert for #{root.Index} {root.TypeName}:{root.Name}.");
            if (plannedRoot.InsertOffset != insertOffset)
                throw new InvalidDataException(
                    $"Root insert drift at #{root.Index}: emit=0x{insertOffset:X}, plan=0x{plannedRoot.InsertOffset:X}.");
            var stablePointer = plannedRoot.StablePointer;

            if (pixelByRoot.TryGetValue(root.Index, out var pixelBinding))
            {
                rendererPixels++;
                if (!pixelDefs.TryGetValue(pixelBinding.Name, out var shader))
                    throw new InvalidDataException($"Missing graph PixelShader '{pixelBinding.Name}'.");

                state.TempRoot(PixelShaderSize);
                var body = BuildPixelShaderBody(root.Name, shader);
                zone.Write(body);

                EmitString(zone, state, root.Name);
                EmitShaderParts(zone, state, dumpRoot, blobIndex, shader);
                pixelStable[root.Name] = stablePointer;
                continue;
            }

            if (techniqueByRoot.TryGetValue(root.Index, out var techniqueBinding))
            {
                rendererTechsets++;
                if (!graph.TechniqueSetsByName.TryGetValue(techniqueBinding.Name, out var techset))
                    throw new InvalidDataException($"Missing graph TechniqueSet '{techniqueBinding.Name}'.");

                var body = BuildTechniqueSetBody(
                    techset,
                    emittedTechniques,
                    plannedTechniques);
                state.TempRoot(TechniqueSetSize);
                zone.Write(body);

                EmitString(zone, state, root.Name);

                foreach (var slot in techset.Slots)
                {
                    if (slot.Technique is null) continue;
                    var key = Hash(slot.Technique);
                    if (!emittedTechniques.Add(key)) continue;

                    EmitTechnique(
                        zone,
                        state,
                        dumpRoot,
                        blobIndex,
                        slot.Technique,
                        key,
                        pixelByName,
                        pixelStable,
                        emittedDecls,
                        emittedVertexShaders,
                        emittedLiterals,
                        plannedTechniques,
                        plannedDecls,
                        plannedVertexShaders,
                        plannedLiterals);
                }
                continue;
            }

            if (materialCorpus is not null &&
                materialCorpus.OwnedByRoot.TryGetValue(root.Index, out var materialEntry))
            {
                realMaterials++;
                T4XenonMaterialGraphCodec.Emit(
                    materialEntry,
                    zone,
                    (size, align, serialized) =>
                    {
                        state.VirtualAlloc(size, align, serialized);
                    },
                    state.TempRoot,
                    roots,
                    stableByRoot);
                continue;
            }

            if (xmodelCorpus is not null &&
                xmodelCorpus.OwnedByRoot.TryGetValue(root.Index, out var xmodelEntry))
            {
                realXModels++;
                T4XenonXModelGraphCodec.Emit(
                    xmodelEntry,
                    zone,
                    (size, align, serialized) =>
                    {
                        state.VirtualAlloc(size, align, serialized);
                    },
                    state.TempRoot,
                    native.Document.ScriptStrings,
                    roots,
                    stableByRoot);
                continue;
            }

            if (imageGraph is not null &&
                realImages < targetImageRoots &&
                Normalize(root.TypeName) == "image" &&
                imageGraph.ImagesByName.TryGetValue(root.Name, out var image))
            {
                realImages++;
                EmitImage(
                    zone,
                    largeRuntime,
                    state,
                    image,
                    cancellationToken);
                continue;
            }

            var shell = BuildShell(root.TypeName, root.Name, canonicalBaseName);
            state.TempRoot(shell.Body.Length);
            zone.Write(shell.Body);
            foreach (var child in shell.Children)
                EmitString(zone, state, child);
        }

        if (rendererTechsets != expectedTechniqueSets || rendererPixels != expectedPixelShaders)
            throw new InvalidDataException(
                $"Renderer root upgrade mismatch: TechniqueSets={rendererTechsets:N0}/{expectedTechniqueSets:N0}, " +
                $"PixelShaders={rendererPixels:N0}/{expectedPixelShaders:N0}.");

        if (imageGraph is not null && realImages != targetImageRoots)
            throw new InvalidDataException(
                $"Image root upgrade mismatch: upgraded={realImages:N0}, target={targetImageRoots:N0}, graph={imageGraph.Images:N0}.");

        if (materialCorpus is not null && realMaterials != materialCorpus.OwnedByRoot.Count)
            throw new InvalidDataException(
                $"Material root upgrade mismatch: upgraded={realMaterials:N0}, expected={materialCorpus.OwnedByRoot.Count:N0}.");
        if (xmodelCorpus is not null && realXModels != xmodelCorpus.OwnedByRoot.Count)
            throw new InvalidDataException(
                $"XModel root upgrade mismatch: upgraded={realXModels:N0}, expected={xmodelCorpus.OwnedByRoot.Count:N0}.");

        var layoutReplayExact =
            state.TempMax == layout.TempBytes &&
            state.Virtual == layout.VirtualBytes &&
            state.Physical == layout.PhysicalBytes &&
            state.LargeRuntime == layout.LargeRuntimeBytes &&
            state.Large == layout.LargeBytes &&
            state.Serialized == layout.SerializedBlockBytes &&
            zone.Length + largeRuntime.Length == layout.ProjectedUncompressedZoneBytes;

        if (!layoutReplayExact)
            throw new InvalidDataException(
                "Stage 4E byte traversal drifted from Stage 4D: " +
                $"TEMP 0x{state.TempMax:X}/0x{layout.TempBytes:X}, " +
                $"VIRTUAL 0x{state.Virtual:X}/0x{layout.VirtualBytes:X}, " +
                $"PHYSICAL 0x{state.Physical:X}/0x{layout.PhysicalBytes:X}, " +
                $"LARGE_RUNTIME 0x{state.LargeRuntime:X}/0x{layout.LargeRuntimeBytes:X}, " +
                $"LARGE 0x{state.Large:X}/0x{layout.LargeBytes:X}, " +
                $"serialized {state.Serialized:N0}/{layout.SerializedBlockBytes:N0}, " +
                $"zone {zone.Length + largeRuntime.Length:N0}/{layout.ProjectedUncompressedZoneBytes:N0}.");

        PatchHeader(
            zone,
            state.TempMax,
            state.LargeRuntime,
            state.Virtual,
            state.Large,
            state.Physical);

        var mainBytes = zone.ToArray();
        var runtimeBytes = largeRuntime.ToArray();
        var zoneBytes = new byte[checked(mainBytes.Length + runtimeBytes.Length)];
        Buffer.BlockCopy(mainBytes, 0, zoneBytes, 0, mainBytes.Length);
        Buffer.BlockCopy(runtimeBytes, 0, zoneBytes, mainBytes.Length, runtimeBytes.Length);
        var zonePath = Path.Combine(outputDirectory, "nuclear_xenon_renderer.zone");
        await File.WriteAllBytesAsync(zonePath, zoneBytes, cancellationToken);

        // Independent parser preflight before compression/OAT.
        var parsed = T4ZoneParser.ParseZoneBytes(
            zonePath,
            zoneBytes,
            WaWPlatform.Xbox360,
            WaWCompressionKind.XboxSingleZlib,
            Endian.Big);
        if (parsed.AssetCount != roots.Count ||
            parsed.ScriptStringCount != native.Document.ScriptStrings.Count)
            throw new InvalidDataException(
                $"Stage 4E zone preflight mismatch: assets={parsed.AssetCount:N0}/{roots.Count:N0}, " +
                $"scripts={parsed.ScriptStringCount:N0}/{native.Document.ScriptStrings.Count:N0}.");

        var ffPath = Path.Combine(outputDirectory, Path.GetFileName(nativeFastFile));
        var wrapped = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
            ffPath,
            zoneBytes,
            preferNativeLevel6: true,
            cancellationToken);

        var oatListedRoots = 0;
        var oatLoadedAssets = 0;
        var oatExternalReferenceAssets = 0;
        var oatZero = false;
        var exactIdentity = false;
        string? oatError = null;

        try
        {
            var externalReferences = materialCorpus?.CollectExternalReferences(roots)
                ?? Array.Empty<T4XenonOatExternalReference>();

            var gate = await T4XenonNativeIdentityOracle.RunAsync(
                ffPath,
                Path.Combine(outputDirectory, "oat_hard_gate"),
                explicitUnlinkerExe,
                cancellationToken,
                roots,
                externalReferences);

            oatListedRoots = gate.Entries.Count;
            oatLoadedAssets = gate.LoadedAssets;
            oatExternalReferenceAssets = gate.ExternalReferenceAssets;
            exactIdentity =
                gate.Entries.Count == roots.Count &&
                gate.Entries.Zip(roots).All(pair =>
                    pair.First.TypeId == pair.Second.TypeId &&
                    pair.First.Name.Equals(pair.Second.Name, StringComparison.Ordinal));

            var rawGate = await File.ReadAllTextAsync(gate.RawUnlinkerLogFile, cancellationToken);
            oatZero = HasZeroWarningErrorSummary(rawGate);

            if (!exactIdentity)
                oatError = "OAT loaded the candidate, but its resolved root identity sequence differs from the native oracle.";
            else if (!oatZero)
                oatError = "OAT root identity gate passed, but the log did not prove '0 warnings, 0 errors'.";
        }
        catch (T4XenonIdentityGateException ex)
        {
            oatLoadedAssets = ex.LoadedAssets;
            oatListedRoots = ex.ReconciledRoots;
            oatExternalReferenceAssets = ex.SeenExternalReferences;
            oatError = ex.Message;
        }
        catch (Exception ex)
        {
            oatError = ex.Message;
        }

        var validationOnly = imageGraph is not null && imageGraphValidationOnly;
        var validationPass =
            layoutReplayExact && wrapped.RoundTripVerified &&
            exactIdentity && oatZero;
        var ready = !validationOnly && validationPass;

        var txt = Path.Combine(outputDirectory, "nuclear_xenon_renderer_emit.txt");
        var json = Path.Combine(outputDirectory, "nuclear_xenon_renderer_emit.json");

        var report = new StringBuilder();
        report.AppendLine("NUCLEAR STAGE 4E - REAL XENON RENDERER BYTE EMISSION");
        report.AppendLine("=====================================================");
        report.AppendLine($"Output FF:                  {ffPath}");
        report.AppendLine($"Native roots:               {roots.Count:N0}");
        report.AppendLine($"Renderer TechniqueSets:     {rendererTechsets:N0}");
        report.AppendLine($"Renderer PixelShaders:      {rendererPixels:N0}");
        report.AppendLine($"Real Material roots:        {realMaterials:N0}");
        report.AppendLine($"Material reference roots:   {(materialCorpus?.ReferenceRoots ?? 0):N0}");
        report.AppendLine($"Real XModel roots:          {realXModels:N0}");
        report.AppendLine($"XModel reference roots:     {(xmodelCorpus?.ReferenceRoots ?? 0):N0}");
        report.AppendLine($"Zone bytes:                 {zoneBytes.LongLength:N0}");
        report.AppendLine($"TEMP:                       0x{state.TempMax:X}");
        report.AppendLine($"VIRTUAL:                    0x{state.Virtual:X}");
        report.AppendLine($"PHYSICAL:                   0x{state.Physical:X}");
        report.AppendLine($"LARGE_RUNTIME:              0x{state.LargeRuntime:X}");
        report.AppendLine($"LARGE:                      0x{state.Large:X}");
        report.AppendLine($"4D layout replay exact:     {layoutReplayExact}");
        report.AppendLine($"Wrapper round-trip:         {wrapped.RoundTripVerified}");
        report.AppendLine($"OAT listed roots:           {oatListedRoots:N0}");
        report.AppendLine($"OAT loaded assets:          {oatLoadedAssets:N0}");
        report.AppendLine($"OAT external references:    {oatExternalReferenceAssets:N0}");
        report.AppendLine($"OAT exact identity sequence:{exactIdentity}");
        report.AppendLine($"OAT 0 warnings / 0 errors:  {oatZero}");
        report.AppendLine($"VALIDATION ONLY:            {validationOnly}");
        report.AppendLine($"VALIDATION PASS:            {validationPass}");
        report.AppendLine($"READY FOR HARDWARE:         {ready}");
        if (!string.IsNullOrWhiteSpace(oatError))
            report.AppendLine($"OAT GATE:                    {oatError}");

        await File.WriteAllTextAsync(txt, report.ToString(), new UTF8Encoding(false), cancellationToken);

        var result = new T4XenonRendererEmitResult
        {
            OutputFastFile = ffPath,
            OutputZoneFile = zonePath,
            NativeRoots = roots.Count,
            RendererTechniqueSets = rendererTechsets,
            RendererPixelShaders = rendererPixels,
            RealImageRoots = realImages,
            RealMaterialRoots = realMaterials,
            MaterialReferenceRoots = materialCorpus?.ReferenceRoots ?? 0,
            RealXModelRoots = realXModels,
            XModelReferenceRoots = xmodelCorpus?.ReferenceRoots ?? 0,
            ZoneBytes = zoneBytes.LongLength,
            MainZoneBytes = mainBytes.LongLength,
            LargeRuntimeBytes = state.LargeRuntime,
            LargeBytes = state.Large,
            TempBytes = state.TempMax,
            VirtualBytes = state.Virtual,
            PhysicalBytes = state.Physical,
            OatListedRoots = oatListedRoots,
            OatLoadedAssets = oatLoadedAssets,
            OatExternalReferenceAssets = oatExternalReferenceAssets,
            OatZeroWarningsZeroErrors = oatZero,
            ExactIdentitySequence = exactIdentity,
            LayoutReplayExact = layoutReplayExact,
            ValidationOnly = validationOnly,
            ValidationPass = validationPass,
            ReadyForHardware = ready,
            OatGateError = oatError,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(
            json,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        return result;
    }

    private static void EmitScriptStrings(
        Stream zone,
        EmitState state,
        IReadOnlyList<T4ScriptStringEntry> strings)
    {
        if (strings.Count == 0) return;

        state.VirtualAlloc(checked(strings.Count * 4L), 4, serialized: true);
        foreach (var item in strings)
            WriteU32(zone, item.RawPointer == 0 ? 0u : T4ZonePointer.Following);

        foreach (var item in strings)
        {
            if (item.RawPointer == 0) continue;
            if (item.Value is null || item.Value.StartsWith("<alias ", StringComparison.Ordinal))
                throw new InvalidDataException($"Unresolved ScriptString #{item.Index}.");
            EmitString(zone, state, item.Value);
        }
    }

    private static byte[] BuildPixelShaderBody(
        string name,
        T4XenonGraphShaderRef shader)
    {
        var b = new byte[PixelShaderSize];
        PutU32(b, 0, string.IsNullOrEmpty(name) ? 0u : T4ZonePointer.Following);
        PutU32(b, 4, shader.CachedPartSize == 0 ? 0u : T4ZonePointer.Following);
        PutU32(b, 8, shader.PhysicalPartSize == 0 ? 0u : T4ZonePointer.Following);
        PutU16(b, 12, checked((ushort)shader.CachedPartSize));
        PutU16(b, 14, checked((ushort)shader.PhysicalPartSize));
        return b;
    }

    private static byte[] BuildTechniqueSetBody(
        T4XenonTechniqueSetGraph techset,
        IReadOnlySet<string> emittedTechniques,
        IReadOnlyDictionary<string, long> plannedOffsets)
    {
        var b = new byte[TechniqueSetSize];
        PutU32(b, 0, string.IsNullOrEmpty(techset.Name) ? 0u : T4ZonePointer.Following);
        b[4] = techset.WorldVertFormat;
        b[5] = techset.HasBeenUploaded ? (byte)1 : (byte)0;
        // [6..7] unused, remappedTechniqueSet @ +8 remains null.

        var predicted = new HashSet<string>(emittedTechniques, StringComparer.Ordinal);
        foreach (var slot in techset.Slots)
        {
            if (slot.Technique is null) continue;
            var key = Hash(slot.Technique);
            uint pointer;
            if (predicted.Add(key))
            {
                pointer = T4ZonePointer.Following;
            }
            else
            {
                pointer = T4ZonePointer.Encode(
                    T4ZoneBlock.Virtual,
                    checked((uint)plannedOffsets[key]));
            }
            PutU32(b, 12 + slot.Slot * 4, pointer);
        }
        return b;
    }

    private static void EmitTechnique(
        Stream zone,
        EmitState state,
        string dumpRoot,
        IReadOnlyDictionary<string, string> blobIndex,
        T4XenonTechniqueGraph technique,
        string techniqueKey,
        IReadOnlyDictionary<string, T4XenonGraphRootBindingEntry> pixelByName,
        IReadOnlyDictionary<string, uint> pixelStable,
        HashSet<string> emittedDecls,
        HashSet<string> emittedVertexShaders,
        HashSet<string> emittedLiterals,
        IReadOnlyDictionary<string, long> plannedTechniques,
        IReadOnlyDictionary<string, long> plannedDecls,
        IReadOnlyDictionary<string, long> plannedVertexShaders,
        IReadOnlyDictionary<string, long> plannedLiterals)
    {
        var size = checked(TechniqueHeaderSize + technique.Passes.Count * PassSize);
        var offset = state.VirtualAlloc(size, 4, serialized: true);
        RequireOffset("technique", techniqueKey, offset, plannedTechniques);

        var body = BuildTechniqueBody(
            technique,
            emittedDecls,
            emittedVertexShaders,
            plannedDecls,
            plannedVertexShaders,
            pixelByName,
            pixelStable);
        zone.Write(body);

        foreach (var pass in technique.Passes)
        {
            if (pass.VertexDecl is not null)
            {
                var key = Hash(pass.VertexDecl);
                if (emittedDecls.Add(key))
                {
                    var declOffset = state.VirtualAlloc(VertexDeclSize, 4, serialized: true);
                    RequireOffset("vertex-decl", key, declOffset, plannedDecls);
                    zone.Write(BuildVertexDeclBody(pass.VertexDecl));
                }
            }

            foreach (var vs in pass.VertexShaderArray)
                EmitVertexShader(
                    zone, state, dumpRoot, blobIndex, vs,
                    emittedVertexShaders, plannedVertexShaders);
            EmitVertexShader(
                zone, state, dumpRoot, blobIndex, pass.VertexShader,
                emittedVertexShaders, plannedVertexShaders);

            if (pass.Args.Count != 0)
            {
                var argsBody = BuildArgsBody(pass.Args, emittedLiterals, plannedLiterals);
                state.VirtualAlloc(argsBody.Length, 4, serialized: true);
                zone.Write(argsBody);

                foreach (var arg in pass.Args)
                {
                    if (arg.LiteralConst is null) continue;
                    var key = LiteralKey(arg.LiteralConst);
                    if (!emittedLiterals.Add(key)) continue;

                    var literalOffset = state.VirtualAlloc(LiteralSize, 4, serialized: true);
                    RequireOffset("literal", key, literalOffset, plannedLiterals);
                    Span<byte> literal = stackalloc byte[LiteralSize];
                    for (var i = 0; i < 4; i++)
                        BinaryPrimitives.WriteUInt32BigEndian(
                            literal.Slice(i * 4, 4),
                            unchecked((uint)BitConverter.SingleToInt32Bits(arg.LiteralConst[i])));
                    zone.Write(literal);
                }
            }
        }

        // T4 Xenon ZoneCode: MaterialTechnique reorder = passArray, then name.
        if (!string.IsNullOrEmpty(technique.Name))
            EmitString(zone, state, technique.Name);
    }

    private static byte[] BuildTechniqueBody(
        T4XenonTechniqueGraph technique,
        IReadOnlySet<string> emittedDecls,
        IReadOnlySet<string> emittedVertexShaders,
        IReadOnlyDictionary<string, long> plannedDecls,
        IReadOnlyDictionary<string, long> plannedVertexShaders,
        IReadOnlyDictionary<string, T4XenonGraphRootBindingEntry> pixelByName,
        IReadOnlyDictionary<string, uint> pixelStable)
    {
        var b = new byte[TechniqueHeaderSize + technique.Passes.Count * PassSize];
        PutU32(b, 0, string.IsNullOrEmpty(technique.Name) ? 0u : T4ZonePointer.Following);
        PutU16(b, 4, technique.Flags);
        PutU16(b, 6, checked((ushort)technique.Passes.Count));

        var predictedDecls = new HashSet<string>(emittedDecls, StringComparer.Ordinal);
        var predictedVs = new HashSet<string>(emittedVertexShaders, StringComparer.Ordinal);

        for (var i = 0; i < technique.Passes.Count; i++)
        {
            var pass = technique.Passes[i];
            var p = TechniqueHeaderSize + i * PassSize;

            if (pass.VertexDecl is not null)
            {
                var key = Hash(pass.VertexDecl);
                PutU32(b, p, PredictReusablePointer(
                    key, predictedDecls, plannedDecls));
            }

            for (var j = 0; j < pass.VertexShaderArray.Count; j++)
            {
                var vs = pass.VertexShaderArray[j];
                if (vs is null) continue;
                var key = ShaderKey(vs);
                PutU32(b, p + 4 + j * 4, PredictReusablePointer(
                    key, predictedVs, plannedVertexShaders));
            }

            if (pass.VertexShader is not null)
            {
                var key = ShaderKey(pass.VertexShader);
                PutU32(b, p + 68, PredictReusablePointer(
                    key, predictedVs, plannedVertexShaders));
            }

            if (pass.PixelShader is not null &&
                !string.IsNullOrWhiteSpace(pass.PixelShader.Name))
            {
                var name = pass.PixelShader.Name;
                if (!pixelByName.ContainsKey(name))
                    throw new InvalidDataException($"Technique references unbound PixelShader '{name}'.");
                if (!pixelStable.TryGetValue(name, out var stable))
                    throw new InvalidDataException(
                        $"Technique references PixelShader '{name}' before its top-level insert slot exists.");
                PutU32(b, p + 72, stable);
            }

            b[p + 76] = pass.PerPrimArgCount;
            b[p + 77] = pass.PerObjArgCount;
            b[p + 78] = pass.StableArgCount;
            b[p + 79] = pass.CustomSamplerFlags;
            b[p + 80] = pass.PrecompiledIndex;
            PutU32(b, p + 84, pass.Args.Count == 0 ? 0u : T4ZonePointer.Following);
        }

        return b;
    }

    private static byte[] BuildVertexDeclBody(T4XenonVertexDeclGraph decl)
    {
        var b = new byte[VertexDeclSize];
        b[0] = decl.StreamCount;
        b[1] = decl.HasOptionalSource ? (byte)1 : (byte)0;
        for (var i = 0; i < Math.Min(16, decl.Routing.Count); i++)
        {
            b[4 + i * 2] = decl.Routing[i].Source;
            b[5 + i * 2] = decl.Routing[i].Dest;
        }
        return b;
    }

    private static void EmitVertexShader(
        Stream zone,
        EmitState state,
        string dumpRoot,
        IReadOnlyDictionary<string, string> blobIndex,
        T4XenonGraphShaderRef? shader,
        HashSet<string> emitted,
        IReadOnlyDictionary<string, long> plannedOffsets)
    {
        if (shader is null) return;
        var key = ShaderKey(shader);
        if (!emitted.Add(key)) return;

        var offset = state.VirtualAlloc(VertexShaderSize, 4, serialized: true);
        RequireOffset("vertex-shader", key, offset, plannedOffsets);

        var b = new byte[VertexShaderSize];
        PutU32(b, 0, string.IsNullOrEmpty(shader.Name) ? 0u : T4ZonePointer.Following);
        PutU32(b, 4, shader.CachedPartSize == 0 ? 0u : T4ZonePointer.Following);
        PutU32(b, 8, shader.PhysicalPartSize == 0 ? 0u : T4ZonePointer.Following);
        PutU16(b, 12, checked((ushort)shader.CachedPartSize));
        PutU16(b, 14, checked((ushort)shader.PhysicalPartSize));
        zone.Write(b);

        if (!string.IsNullOrEmpty(shader.Name))
            EmitString(zone, state, shader.Name);
        EmitShaderParts(zone, state, dumpRoot, blobIndex, shader);
    }

    private static void EmitShaderParts(
        Stream zone,
        EmitState state,
        string dumpRoot,
        IReadOnlyDictionary<string, string> blobIndex,
        T4XenonGraphShaderRef shader)
    {
        // T4 Xenon ZoneCode reorders physicalPart before cachedPart.
        if (shader.PhysicalPartSize != 0)
        {
            var bytes = ReadBlob(
                dumpRoot, blobIndex, shader.PhysicalFile, shader.PhysicalPartSize);
            state.PhysicalAlloc(bytes.Length, 32, serialized: true);
            zone.Write(bytes);
        }

        if (shader.CachedPartSize != 0)
        {
            var bytes = ReadBlob(
                dumpRoot, blobIndex, shader.CachedFile, shader.CachedPartSize);
            state.VirtualAlloc(bytes.Length, 4, serialized: true);
            zone.Write(bytes);
        }
    }

    private static byte[] BuildArgsBody(
        IReadOnlyList<T4XenonShaderArgGraph> args,
        IReadOnlySet<string> emittedLiterals,
        IReadOnlyDictionary<string, long> plannedLiterals)
    {
        var b = new byte[args.Count * ShaderArgSize];
        var predicted = new HashSet<string>(emittedLiterals, StringComparer.Ordinal);

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var p = i * ShaderArgSize;
            PutU16(b, p, arg.Type);
            PutU16(b, p + 2, arg.Dest);

            if (arg.LiteralConst is not null)
            {
                var key = LiteralKey(arg.LiteralConst);
                PutU32(b, p + 4, PredictReusablePointer(
                    key, predicted, plannedLiterals));
            }
            else if (arg.CodeConst is not null)
            {
                PutU16(b, p + 4, arg.CodeConst.Index);
                b[p + 6] = arg.CodeConst.FirstRow;
                b[p + 7] = arg.CodeConst.RowCount;
            }
            else
            {
                PutU32(b, p + 4, arg.URaw);
            }
        }

        return b;
    }

    private static uint PredictReusablePointer(
        string key,
        ISet<string> predicted,
        IReadOnlyDictionary<string, long> plannedOffsets)
    {
        if (predicted.Add(key))
            return T4ZonePointer.Following;

        if (!plannedOffsets.TryGetValue(key, out var offset))
            throw new InvalidDataException($"Missing Stage 4D offset for reusable graph key '{key}'.");
        return T4ZonePointer.Encode(T4ZoneBlock.Virtual, checked((uint)offset));
    }

    private static void RequireOffset(
        string kind,
        string key,
        long actual,
        IReadOnlyDictionary<string, long> planned)
    {
        if (!planned.TryGetValue(key, out var expected))
            throw new InvalidDataException($"Stage 4D has no {kind} offset for '{key}'.");
        if (actual != expected)
            throw new InvalidDataException(
                $"{kind} allocation drift for '{key}': actual=0x{actual:X}, expected=0x{expected:X}.");
    }

    private static void EmitImage(
        Stream zone,
        Stream largeRuntime,
        EmitState state,
        T4XenonImageGraph image,
        CancellationToken cancellationToken)
    {
        const int gfxImageSize = 40;
        const int loadDefSize = 16;
        const int d3dTextureSize = 52;

        state.TempRoot(gfxImageSize + loadDefSize);
        zone.Write(BuildImageBody(image));

        // T4 Xenon ZoneCode marks loadDef reusable: reserve its non-serialized
        // VIRTUAL insert slot before walking reordered children.
        state.VirtualAlloc(4, 4, serialized: false);

        // GfxImage child traversal is name -> pixels -> texture.
        EmitString(zone, state, image.Name);

        if (image.PixelsBytes != 0)
        {
            var pixels = File.ReadAllBytes(image.PixelBlobFile);
            cancellationToken.ThrowIfCancellationRequested();
            if (pixels.Length != image.PixelsBytes)
                throw new InvalidDataException(
                    $"{image.Name}: pixel blob length {pixels.Length:N0} != graph {image.PixelsBytes:N0}.");

            // T4 Xenon block choice is controlled by delayLoadPixels.
            // Delayed map textures live in LARGE_RUNTIME (block 2) and are appended
            // after zoneSize. Immediate/generated images live in LARGE (block 5)
            // and therefore remain in the serialized main-zone stream.
            if (image.DelayLoadPixels)
            {
                state.LargeRuntimeAlloc(image.PixelsBytes, 4096, serialized: true);
                largeRuntime.Write(pixels);
            }
            else
            {
                state.LargeAlloc(image.PixelsBytes, 4096, serialized: true);
                zone.Write(pixels);
            }
        }

        zone.Write(BuildImageLoadDefBody(image));

        state.VirtualAlloc(d3dTextureSize, 4, serialized: true);
        var texture = File.ReadAllBytes(image.TextureHeaderBlobFile);
        cancellationToken.ThrowIfCancellationRequested();
        if (texture.Length != d3dTextureSize)
            throw new InvalidDataException(
                $"{image.Name}: D3D texture header length {texture.Length:N0} != {d3dTextureSize}.");
        zone.Write(texture);
    }

    private static byte[] BuildImageBody(T4XenonImageGraph image)
    {
        var b = new byte[40];
        PutU32(b, 0, checked((uint)image.MapType));
        PutU32(b, 4, T4ZonePointer.Insert);
        b[8] = image.Semantic;
        PutU32(b, 12, checked((uint)image.CardMemoryBytes));
        PutU16(b, 16, image.Width);
        PutU16(b, 18, image.Height);
        PutU16(b, 20, image.Depth);
        b[22] = image.Category;
        b[23] = image.DelayLoadPixels ? (byte)1 : (byte)0;
        PutU32(b, 24, image.PixelsBytes == 0 ? 0u : T4ZonePointer.Following);
        PutU32(b, 28, image.BaseSize);
        PutU16(b, 32, image.StreamSlot);
        b[34] = image.Streaming ? (byte)1 : (byte)0;
        PutU32(b, 36, string.IsNullOrEmpty(image.Name) ? 0u : T4ZonePointer.Following);
        return b;
    }

    private static byte[] BuildImageLoadDefBody(T4XenonImageGraph image)
    {
        var d = image.LoadDef;
        var b = new byte[16];
        b[0] = d.LevelCount;
        b[1] = d.Flags;
        PutI16(b, 2, d.Dimensions[0]);
        PutI16(b, 4, d.Dimensions[1]);
        PutI16(b, 6, d.Dimensions[2]);
        PutU32(b, 8, unchecked((uint)d.Format));
        PutU32(b, 12, T4ZonePointer.Following);
        return b;
    }

    private static void PutI16(byte[] b, int offset, short value) =>
        BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(offset, 2), value);

    private sealed record ShellPayload(byte[] Body, IReadOnlyList<string> Children);

    private static ShellPayload BuildShell(
        string typeName,
        string name,
        string canonicalBaseName)
    {
        var type = Normalize(typeName);
        if (type == "image")
        {
            var b = new byte[40];
            PutU32(b, 36, T4ZonePointer.Following);
            return new(b, new[] { name });
        }
        if (type == "gfxworld")
        {
            var b = new byte[900];
            PutU32(b, 0, T4ZonePointer.Following);
            PutU32(b, 4, T4ZonePointer.Following);
            return new(b, new[] { name, canonicalBaseName });
        }
        if (type == "localize")
        {
            var b = new byte[8];
            PutU32(b, 0, T4ZonePointer.Following);
            PutU32(b, 4, T4ZonePointer.Following);
            return new(b, new[] { string.Empty, name });
        }

        var size = ShellSize(typeName);
        if (size == 0)
            throw new InvalidDataException($"No all-name-shell ABI for '{typeName}'.");

        var body = new byte[size];
        PutU32(body, 0, T4ZonePointer.Following);
        return new(body, new[] { name });
    }

    private static int ShellSize(string typeName) => Normalize(typeName) switch
    {
        "xanim" => 92,
        "xmodel" => 0xD4,
        "material" => 96,
        "pixelshader" => 16,
        "techset" or "techniqueset" => 216,
        "image" => 40,
        "sound" => 12,
        "loadedsound" => 160,
        "weapon" => 0x9AC,
        "fx" => 36,
        "rawfile" => 12,
        "physpreset" => 48,
        "clipmappvs" or "clipmap" => 332,
        "comworld" => 64,
        "gameworldsp" => 44,
        "gfxworld" => 900,
        "localize" => 8,
        "mapents" => 12,
        _ => 0
    };

    private static void EmitString(Stream zone, EmitState state, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        state.VirtualAlloc(bytes.Length + 1L, 1, serialized: true);
        zone.Write(bytes);
        zone.WriteByte(0);
    }

    private static void PatchHeader(
        MemoryStream zone,
        long temp,
        long largeRuntime,
        long virtualSize,
        long large,
        long physical)
    {
        if (zone.Length > uint.MaxValue)
            throw new InvalidDataException("Stage 4E main zone exceeds 32-bit T4 zoneSize.");

        var zoneSize = checked((uint)(zone.Length - 36));
        var pos = zone.Position;
        zone.Position = 0;
        WriteU32(zone, zoneSize);
        WriteU32(zone, 0);
        WriteU32(zone, checked((uint)temp));
        WriteU32(zone, 0);
        WriteU32(zone, checked((uint)largeRuntime));
        WriteU32(zone, 0);
        WriteU32(zone, checked((uint)virtualSize));
        WriteU32(zone, checked((uint)large));
        WriteU32(zone, checked((uint)physical));
        zone.Position = pos;
    }

    private static Dictionary<string, T4XenonGraphShaderRef> CollectPixelShaders(
        T4XenonGraphImportResult graph)
    {
        var map = new Dictionary<string, T4XenonGraphShaderRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var ts in graph.TechniqueSetsByName.Values)
        foreach (var slot in ts.Slots)
        {
            if (slot.Technique is null) continue;
            foreach (var pass in slot.Technique.Passes)
            {
                var ps = pass.PixelShader;
                if (ps is null || string.IsNullOrWhiteSpace(ps.Name)) continue;
                if (!map.TryGetValue(ps.Name, out var prior))
                    map.Add(ps.Name, ps);
                else if (ShaderSignature(prior) != ShaderSignature(ps))
                    throw new InvalidDataException($"PixelShader '{ps.Name}' has conflicting graph definitions.");
            }
        }
        return map;
    }

    private static Dictionary<string, string> BuildBlobIndex(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories))
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

    private static byte[] ReadBlob(
        string dumpRoot,
        IReadOnlyDictionary<string, string> index,
        string? relative,
        int expectedSize)
    {
        if (expectedSize == 0) return Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(relative))
            throw new InvalidDataException("Shader blob path is missing.");

        var key = relative.Replace('\\', '/').TrimStart('/');
        if (!index.TryGetValue(key, out var full))
        {
            var direct = Path.Combine(dumpRoot, key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(direct))
                throw new FileNotFoundException($"Shader blob not found: {relative}");
            full = direct;
        }

        var bytes = File.ReadAllBytes(full);
        if (bytes.Length != expectedSize)
            throw new InvalidDataException(
                $"Shader blob '{relative}' is {bytes.Length:N0} bytes; expected {expectedSize:N0}.");
        return bytes;
    }

    private static bool HasZeroWarningErrorSummary(string log)
    {
        foreach (var line in log.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("Finished with 0 warnings, 0 errors", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string ShaderKey(T4XenonGraphShaderRef shader) =>
        !string.IsNullOrWhiteSpace(shader.Name)
            ? shader.Name!
            : "<anon>:" + ShaderSignature(shader);

    private static string ShaderSignature(T4XenonGraphShaderRef shader) =>
        string.Join("|", shader.CachedPartSize, shader.PhysicalPartSize,
            (shader.CachedFile ?? "").Replace('\\', '/').ToLowerInvariant(),
            (shader.PhysicalFile ?? "").Replace('\\', '/').ToLowerInvariant());

    private static string LiteralKey(IReadOnlyList<float> value) =>
        string.Join(",", value.Select(x => BitConverter.SingleToInt32Bits(x).ToString("X8")));

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static string Normalize(string value) =>
        value.Replace("_", string.Empty, StringComparison.Ordinal)
             .Replace("-", string.Empty, StringComparison.Ordinal)
             .ToLowerInvariant();

    private static void PutU16(byte[] b, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(offset, 2), value);

    private static void PutU32(byte[] b, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(offset, 4), value);

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }

    private sealed record BindingData(
        bool Ready,
        IReadOnlyList<T4XenonGraphRootBindingEntry> Techniques,
        IReadOnlyList<T4XenonGraphRootBindingEntry> Pixels);

    private static BindingData LoadBindings(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        if (r.GetProperty("format").GetString() != "WaW360.T4Xenon.GraphRootBinding.v1")
            throw new InvalidDataException("Unexpected Stage 4C binding manifest.");
        return new(
            r.GetProperty("exactRootBindingReady").GetBoolean(),
            ParseBindings(r.GetProperty("techniqueSetBindings")),
            ParseBindings(r.GetProperty("pixelShaderBindings")));
    }

    private static IReadOnlyList<T4XenonGraphRootBindingEntry> ParseBindings(JsonElement a) =>
        a.EnumerateArray().Select(e => new T4XenonGraphRootBindingEntry(
            e.GetProperty("Kind").GetString() ?? "",
            e.GetProperty("Name").GetString() ?? "",
            e.GetProperty("NativeRootIndex").GetInt32(),
            e.GetProperty("NativeTypeId").GetInt32(),
            e.GetProperty("NativeTypeName").GetString() ?? "")).ToList();

    private static IReadOnlyList<T4XenonNativeIdentityEntry> LoadNativeIdentity(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        if (r.GetProperty("format").GetString() != "WaW360.T4Xenon.NativeIdentityManifest.v1" ||
            !r.GetProperty("typeSequenceVerified").GetBoolean())
            throw new InvalidDataException("Stage 4C native identity manifest is not verified.");

        return r.GetProperty("entries").EnumerateArray().Select(e => new T4XenonNativeIdentityEntry(
            e.GetProperty("Index").GetInt32(),
            e.GetProperty("TypeId").GetInt32(),
            e.GetProperty("TypeName").GetString() ?? "",
            e.GetProperty("OatTypeName").GetString() ?? "",
            e.GetProperty("Name").GetString() ?? "")).OrderBy(x => x.Index).ToList();
    }

    private sealed class EmitState
    {
        private long _virtual;
        private long _physical;
        private long _largeRuntime;
        private long _large;

        public long Virtual => _virtual;
        public long Physical => _physical;
        public long LargeRuntime => _largeRuntime;
        public long Large => _large;
        public long TempMax { get; private set; }
        public long Serialized { get; private set; }

        public long VirtualAlloc(long size, int alignment, bool serialized) =>
            Allocate(ref _virtual, size, alignment, serialized);

        public long PhysicalAlloc(long size, int alignment, bool serialized) =>
            Allocate(ref _physical, size, alignment, serialized);

        public long LargeRuntimeAlloc(long size, int alignment, bool serialized) =>
            Allocate(ref _largeRuntime, size, alignment, serialized);

        public long LargeAlloc(long size, int alignment, bool serialized) =>
            Allocate(ref _large, size, alignment, serialized);

        public void TempRoot(int size)
        {
            TempMax = Math.Max(TempMax, size);
            Serialized += size;
        }

        private long Allocate(ref long cursor, long size, int alignment, bool serialized)
        {
            var mask = alignment - 1L;
            cursor = checked((cursor + mask) & ~mask);
            var result = cursor;
            cursor = checked(cursor + size);
            if (serialized) Serialized = checked(Serialized + size);
            return result;
        }
    }
}
