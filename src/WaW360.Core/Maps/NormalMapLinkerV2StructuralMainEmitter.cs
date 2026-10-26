using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed class NormalMapLinkerV2NativeSequenceDiagnosticResult
{
    public required string MapName { get; init; }
    public required string NativeOracleFastFile { get; init; }
    public required int NativeRoots { get; init; }
    public required int ReusedSourceRoots { get; init; }
    public required int SyntheticDependencyRoots { get; init; }
    public required int DroppedSourceRoots { get; init; }
    public required int NativeScriptStrings { get; init; }
    public required int NameContractErrors { get; init; }
    public required IReadOnlyList<string> NameContractFailures { get; init; }
    public required int ReadBackErrors { get; init; }
    public required bool WrapperRoundTripVerified { get; init; }
    public required bool CandidateReady { get; init; }
    public required string? FastFileSha256 { get; init; }
    public required string? OutputFastFile { get; init; }
    public required string? HardwarePackageDirectory { get; init; }
    public required int AllShellNameContractErrors { get; init; }
    public required int AllShellReadBackErrors { get; init; }
    public required bool AllShellWrapperRoundTripVerified { get; init; }
    public required bool AllShellCandidateReady { get; init; }
    public required string? AllShellFastFileSha256 { get; init; }
    public required string? AllShellOutputFastFile { get; init; }
    public required string? AllShellHardwarePackageDirectory { get; init; }
    public required bool NativeFootprintCandidateReady { get; init; }
    public required int NativeFootprintReadBackErrors { get; init; }
    public required bool NativeFootprintWrapperRoundTripVerified { get; init; }
    public required string? NativeFootprintFastFileSha256 { get; init; }
    public required string? NativeFootprintOutputFastFile { get; init; }
    public required string? NativeFootprintHardwarePackageDirectory { get; init; }
    public required long NativeOracleZoneBytes { get; init; }
    public required long NativeFootprintCandidateZoneBytes { get; init; }
    public required string TextReportFile { get; init; }
}

public sealed class NormalMapLinkerV2StructuralMainEmitResult
{
    public required string MapName { get; init; }
    public required string CanonicalBaseName { get; init; }
    public required string? OutputFastFile { get; init; }
    public required string? OutputZone { get; init; }
    public required string? HardwarePackageDirectory { get; init; }
    public required string? ConservativeOutputFastFile { get; init; }
    public required string? ConservativeHardwarePackageDirectory { get; init; }
    public required bool ConservativeCandidateReady { get; init; }
    public required int ConservativeReadBackErrors { get; init; }
    public required bool ConservativeWrapperRoundTripVerified { get; init; }
    public required string? ConservativeFastFileSha256 { get; init; }
    public required string? ScriptClosureOutputFastFile { get; init; }
    public required string? ScriptClosureHardwarePackageDirectory { get; init; }
    public required bool ScriptClosureCandidateReady { get; init; }
    public required int ScriptClosureReadBackErrors { get; init; }
    public required bool ScriptClosureWrapperRoundTripVerified { get; init; }
    public required string? ScriptClosureFastFileSha256 { get; init; }
    public required int ScriptClosureResidentMenusRemoved { get; init; }
    public required int ScriptClosureRawFilesPromoted { get; init; }
    public required int XboxAssets { get; init; }
    public required int ScriptStrings { get; init; }
    public required int PayloadsBuilt { get; init; }
    public required int FallbackPayloads { get; init; }
    public required int RealPromotedPayloads { get; init; }
    public required IReadOnlyDictionary<string, int> RealPromotedByType { get; init; }
    public required int SmokePromotedPayloads { get; init; }
    public required IReadOnlyDictionary<string, int> SmokePromotedByType { get; init; }
    public required int PromotionRejectedPayloads { get; init; }
    public required IReadOnlyDictionary<string, int> PromotionRejectReasons { get; init; }
    public required int SourceAliasRoots { get; init; }
    public required int ReadBackErrors { get; init; }
    public required long ZoneBytes { get; init; }
    public required long FastFileBytes { get; init; }
    public required uint HeaderZoneSize { get; init; }
    public required bool CorrectZoneSizeSemantics { get; init; }
    public required bool PerBlockCapacityHeader { get; init; }
    public required bool WrapperRoundTripVerified { get; init; }
    public required bool StructuralNativeCandidateReady { get; init; }
    public required bool ProductionMainReady { get; init; }
    public required string? FastFileSha256 { get; init; }
    public required IReadOnlyDictionary<string, int> PayloadsByType { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 7 SELF-CONTAINED STRUCTURAL main-FF emitter.
///
/// This is deliberately NOT the production shared-object linker. It is an explicit native-loader
/// diagnostic that rebuilds each authoritative primary XAsset root as a self-contained Xbox-endian
/// object so no PC packed/shared child pointer can escape into the output. Unlike the old 10r smoke
/// writer it also fixes two concrete XFile header bugs proven by the working Simpsons reference:
///   1) zoneSize is bytes AFTER the 36-byte XFile header, not the complete decompressed file length;
///   2) all seven block fields receive independently derived capacities, not one zone-wide value.
///
/// Production readiness remains fail-closed until typed identity/dependency closure and real shared
/// target ownership are complete. Passing this stage authorizes only a controlled native A/B loader
/// test, never a fidelity claim.
/// </summary>
public static class NormalMapLinkerV2StructuralMainEmitter
{
    private static readonly string[] TrackedTypes = { "xanim", "xmodel", "weapon", "fx" };

    private sealed record StructuralPayload(string TypeName, string Name, byte[] Bytes, bool Fallback, string Note);
    private sealed record SourceContext(
        string Role,
        string Path,
        T4ZoneDocument Document,
        byte[] Zone,
        IReadOnlyDictionary<int, StructuralPayload> Payloads);

    public static async Task<NormalMapLinkerV2NativeSequenceDiagnosticResult> RunNativeSequenceDiagnosticAsync(
        MapCandidate map,
        string outputRoot,
        string nativeOracleFastFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(nativeOracleFastFile))
            throw new FileNotFoundException("Native Xbox oracle fastfile not found.", nativeOracleFastFile);

        var native = await FastFileAnalysisCache.GetZoneAsync(nativeOracleFastFile, cancellationToken);
        if (native.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException($"Native sequence diagnostic requires an Xbox360 oracle; parsed {native.Platform}.");

        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_native_sequence_diagnostic");
        Directory.CreateDirectory(outDir);

        // The semantic oracle proved that native root N has one exact (type,name)
        // identity. Reuse that evidence here so the hardware candidate does not fall
        // back to fake __waw360_* DB names for promoted Xbox-only dependencies.
        var nativeIdentity = await T4XenonNativeIdentityOracle.RunAsync(
            nativeOracleFastFile,
            outDir,
            cancellationToken: cancellationToken);
        if (nativeIdentity.Entries.Count != native.Document.AssetCount)
            throw new InvalidDataException(
                $"Native identity evidence count mismatch: identity={nativeIdentity.Entries.Count:N0}; " +
                $"XAssetList={native.Document.AssetCount:N0}.");

        var source = await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var rootPolicy = await NormalMapLinkerV2CanonicalRootPolicy.RunAsync(map, outputRoot, source, cancellationToken: cancellationToken);
        var blockPlan = await NormalMapLinkerV2DestinationBlockPlan.RunAsync(map, outputRoot, source, cancellationToken);
        var script = await NormalMapLinkerV2ScriptStringMerge.RunAsync(map, outputRoot, source, cancellationToken);

        if (!source.Verified)
            throw new InvalidDataException("Canonical source archive is not verified.");
        if (!rootPolicy.Verified || !rootPolicy.ProvenanceIdentityComplete)
            throw new InvalidDataException("Canonical provenance-root policy is not complete.");
        if (!blockPlan.Verified || !blockPlan.SourceCapacityPlanReady)
            throw new InvalidDataException("Source-derived destination block plan is not ready.");
        if (!script.Verified || !script.FullRemapReady)
            throw new InvalidDataException("Canonical ScriptString remap is not complete.");

        var aliasRoots = rootPolicy.Roots.Count(r =>
            r.SourceReferenceKind is T4AssetReferenceKind.OffsetOrAlias or T4AssetReferenceKind.Null);
        if (aliasRoots != 0)
            throw new InvalidDataException($"Native-sequence diagnostic refuses {aliasRoots:N0} source alias/null top-level roots.");

        var primary = source.FastFiles
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .OrderBy(x => LinkerV2ProofEvidence.RoleRank(x.Role))
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var weaponStrings = await LinkerV2IwdWeaponStringCatalog.BuildAsync(
            source.SourceArchive
                .Where(x => x.Role.Equals("iwd", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Path),
            cancellationToken);

        var contexts = new Dictionary<string, SourceContext>(StringComparer.OrdinalIgnoreCase);
        foreach (var container in primary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(container.Path, cancellationToken);
            var payloads = await BuildSourcePayloadsAsync(
                outputRoot,
                map.Name,
                source.CanonicalBaseName,
                container.Role,
                container.Path,
                cached.Document,
                cached.Bytes,
                weaponStrings,
                cancellationToken);
            contexts[container.Path] = new SourceContext(
                container.Role, container.Path, cached.Document, cached.Bytes, payloads);
        }

        // Build a source payload pool by Xbox asset type. The tracked families are
        // deliberately reduced to fixed shells so this diagnostic measures DB graph
        // shape/order rather than XAnim/XModel/Weapon/FX fidelity.
        var sourcePairs = new List<(LinkerV2CanonicalRoot Root, StructuralPayload Payload)>();
        foreach (var root in rootPolicy.Roots.OrderBy(r => r.CanonicalSourceOrdinal))
        {
            if (!contexts.TryGetValue(root.SourceFastFile, out var ctx) ||
                !ctx.Payloads.TryGetValue(root.SourceAssetIndex, out var payload))
            {
                throw new InvalidDataException(
                    $"No structural payload exists for source root {root.IdentityKey} ({root.PcTypeName}).");
            }

            var shell = BuildTrackedShellVariant(new[] { payload })[0];
            sourcePairs.Add((root, shell));
        }

        var pools = sourcePairs
            .GroupBy(x => x.Root.XboxTypeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new Queue<(LinkerV2CanonicalRoot Root, StructuralPayload Payload)>(g),
                StringComparer.OrdinalIgnoreCase);

        var diagnosticRoots = new List<LinkerV2CanonicalRoot>(native.Document.AssetCount);
        var diagnosticPayloads = new List<StructuralPayload>(native.Document.AssetCount);
        var reused = 0;
        var synthetic = 0;

        for (var i = 0; i < native.Document.Assets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nativeRoot = native.Document.Assets[i];
            var type = nativeRoot.TypeName;

            if (pools.TryGetValue(type, out var queue) && queue.Count != 0)
            {
                var pair = queue.Dequeue();
                diagnosticRoots.Add(pair.Root with
                {
                    CanonicalSourceOrdinal = i,
                    XboxTypeName = type,
                    XboxTypeId = nativeRoot.TypeId
                });
                diagnosticPayloads.Add(pair.Payload);
                reused++;
                continue;
            }

            var name = nativeIdentity.Entries[i].Name;
            diagnosticRoots.Add(new LinkerV2CanonicalRoot(
                i,
                "NATIVE_SEQUENCE_DIAGNOSTIC",
                nativeOracleFastFile,
                nativeRoot.Index,
                type,
                type,
                nativeRoot.TypeId,
                T4AssetReferenceKind.InlineShared,
                T4ZonePointer.Insert,
                $"native-sequence|{type}|{i}|{name}",
                "NATIVE_ORACLE_EXACT_IDENTITY__NO_DONOR_PAYLOAD_BYTES"));
            diagnosticPayloads.Add(BuildNativeSequenceSyntheticPayload(type, name, source.CanonicalBaseName));
            synthetic++;
        }

        var dropped = pools.Values.Sum(q => q.Count);

        // The PC and native oracle were proven to contain the same ScriptString value
        // set but in a different order. Reproduce the native index order by value only;
        // no donor pointers or payload bytes are copied.
        var nativeStrings = native.Document.ScriptStrings;
        var nullIndices = nativeStrings
            .Select((s, i) => (s, i))
            .Where(x => x.s.RawPointer == 0)
            .Select(x => x.i)
            .ToList();
        if (nullIndices.Count > 1 || (nullIndices.Count == 1 && nullIndices[0] != 0))
            throw new InvalidDataException(
                $"Native ScriptString layout has unsupported null positions: {string.Join(",", nullIndices)}.");

        var nativeValues = new List<string>(nativeStrings.Count);
        foreach (var s in nativeStrings)
        {
            if (s.RawPointer == 0) continue;
            // Empty ScriptStrings are legitimate T4 values. Only a null Value or
            // an explicitly unresolved packed-pointer marker is invalid here.
            if (s.Value is null || s.Value.StartsWith("<alias ", StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Native ScriptString #{s.Index} could not be resolved to a concrete value: " +
                    $"raw=0x{s.RawPointer:X8} ({T4ZonePointer.Describe(s.RawPointer)}).");
            nativeValues.Add(s.Value);
        }
        var nativeScriptTable = new CanonicalScriptTable(nullIndices.Count == 1, nativeValues);

        var packageDir = Path.Combine(outputRoot, map.Name, "hardware_test_main_native_sequence_shell");
        var allShellPackageDir = Path.Combine(outputRoot, map.Name, "hardware_test_main_native_sequence_all_name_shell");
        var nativeFootprintPackageDir = Path.Combine(outputRoot, map.Name, "hardware_test_main_native_sequence_native_footprint");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(packageDir);
        Directory.CreateDirectory(allShellPackageDir);
        Directory.CreateDirectory(nativeFootprintPackageDir);

        var zonePath = Path.Combine(outDir, source.CanonicalBaseName + ".native_sequence.zone");
        var ffPath = Path.Combine(packageDir, source.CanonicalBaseName + ".ff");
        var allShellZonePath = Path.Combine(outDir, source.CanonicalBaseName + ".native_sequence_all_name_shell.zone");
        var allShellFfPath = Path.Combine(allShellPackageDir, source.CanonicalBaseName + ".ff");
        var nativeFootprintZonePath = Path.Combine(outDir, source.CanonicalBaseName + ".native_sequence_native_footprint.zone");
        var nativeFootprintFfPath = Path.Combine(nativeFootprintPackageDir, source.CanonicalBaseName + ".ff");
        var reportPath = Path.Combine(outDir, $"{map.Name}_native_sequence_diagnostic.txt");
        TryDelete(zonePath);
        TryDelete(ffPath);
        TryDelete(allShellZonePath);
        TryDelete(allShellFfPath);
        TryDelete(nativeFootprintZonePath);
        TryDelete(nativeFootprintFfPath);
        TryDelete(Path.Combine(packageDir, "NATIVE_SEQUENCE_DB_TEST.txt"));
        TryDelete(Path.Combine(allShellPackageDir, "ALL_NAME_SHELL_DB_TEST.txt"));
        TryDelete(Path.Combine(nativeFootprintPackageDir, "NATIVE_FOOTPRINT_DB_TEST.txt"));

        var nameContractFailures = AuditXAssetNameContracts(diagnosticRoots, diagnosticPayloads);
        var nameContractErrors = nameContractFailures.Count;

        var zone = BuildZone(
            diagnosticRoots,
            diagnosticPayloads,
            nativeScriptTable,
            blockPlan,
            out var headerZoneSize);

        var sizeOk = headerZoneSize == checked((uint)(zone.Length - FastFileConstants.T4XFileHeaderSize));
        var blockOk = VerifyBlockHeader(zone, blockPlan);
        if (!sizeOk || !blockOk)
            throw new InvalidDataException(
                $"Native-sequence zone preflight failed: zoneSizeOk={sizeOk}; blockHeaderOk={blockOk}.");

        await File.WriteAllBytesAsync(zonePath, zone, cancellationToken);
        var readBackErrors = VerifyReadBack(
            zonePath,
            zone,
            diagnosticRoots,
            diagnosticPayloads,
            nativeScriptTable,
            blockPlan);

        var roundTrip = false;
        string? sha = null;
        if (readBackErrors == 0)
        {
            var write = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
                ffPath,
                zone,
                preferNativeLevel6: true,
                cancellationToken: cancellationToken);
            roundTrip = write.RoundTripVerified;
            if (roundTrip)
                sha = await Sha256Async(ffPath, cancellationToken);
        }

        var ready = nameContractErrors == 0 && readBackErrors == 0 && roundTrip;
        if (!ready)
            TryDelete(ffPath);

        // Fourth isolation layer: preserve the proven native TYPE sequence/count and
        // native ScriptString order, but replace EVERY root body with a minimal type-correct
        // name-only shell. This separates runtime name/fixup corruption in reused structural
        // payloads from errors in the shell layouts themselves.
        var allShellPayloads = diagnosticRoots
            .Select((root, i) => BuildNativeSequenceSyntheticPayload(
                root.XboxTypeName,
                nativeIdentity.Entries[i].Name,
                source.CanonicalBaseName))
            .ToList();
        var allShellNameFailures = AuditXAssetNameContracts(diagnosticRoots, allShellPayloads);
        var allShellNameErrors = allShellNameFailures.Count;
        var allShellZone = BuildZone(
            diagnosticRoots,
            allShellPayloads,
            nativeScriptTable,
            blockPlan,
            out var allShellHeaderZoneSize);
        var allShellSizeOk =
            allShellHeaderZoneSize == checked((uint)(allShellZone.Length - FastFileConstants.T4XFileHeaderSize));
        var allShellBlockOk = VerifyBlockHeader(allShellZone, blockPlan);
        var allShellReadBackErrors = 1;
        var allShellRoundTrip = false;
        string? allShellSha = null;
        if (allShellSizeOk && allShellBlockOk && allShellNameErrors == 0)
        {
            await File.WriteAllBytesAsync(allShellZonePath, allShellZone, cancellationToken);
            allShellReadBackErrors = VerifyReadBack(
                allShellZonePath,
                allShellZone,
                diagnosticRoots,
                allShellPayloads,
                nativeScriptTable,
                blockPlan);
            if (allShellReadBackErrors == 0)
            {
                var allShellWrite = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
                    allShellFfPath,
                    allShellZone,
                    preferNativeLevel6: true,
                    cancellationToken: cancellationToken);
                allShellRoundTrip = allShellWrite.RoundTripVerified;
                if (allShellRoundTrip)
                    allShellSha = await Sha256Async(allShellFfPath, cancellationToken);
            }
        }
        var allShellReady =
            allShellNameErrors == 0 &&
            allShellSizeOk &&
            allShellBlockOk &&
            allShellReadBackErrors == 0 &&
            allShellRoundTrip;
        if (!allShellReady)
            TryDelete(allShellFfPath);

        // Fifth isolation layer: keep the exact 6,811-slot native identity graph and
        // exact native ScriptString order from ALL-NAME-SHELL, but replace the structural
        // PC-derived block reservation table with the working Xbox oracle's exact seven
        // XFile block capacities. These header fields are what drive DB/runtime arena
        // allocation pressure; compressed .ff byte size is not the allocation contract.
        var nativeBlockCaps = Enum.GetValues<T4ZoneBlock>()
            .ToDictionary(
                block => block.ToString(),
                block => native.Document.Header.BlockSizes[(int)block],
                StringComparer.OrdinalIgnoreCase);
        var nativeFootprintPlan = new NormalMapLinkerV2DestinationBlockPlanResult
        {
            MapName = blockPlan.MapName,
            Sources = blockPlan.Sources,
            StructuralCapacities = nativeBlockCaps,
            GoldenBlockSizes = nativeBlockCaps,
            UsesGoldenForAllocation = true,
            SourceCapacityPlanReady = true,
            ProductionOwnershipComplete = false,
            Notes = blockPlan.Notes.Concat(new[]
            {
                "NATIVE-FOOTPRINT DIAGNOSTIC ONLY: seven XFile block capacities are copied from the user-supplied known-working native oracle to reproduce allocation pressure. No donor payload bytes are copied."
            }).ToList(),
            Blockers = Array.Empty<string>(),
            Verified = true,
            TextReportFile = blockPlan.TextReportFile,
            JsonReportFile = blockPlan.JsonReportFile
        };

        var nativeFootprintZone = BuildZone(
            diagnosticRoots,
            allShellPayloads,
            nativeScriptTable,
            nativeFootprintPlan,
            out var nativeFootprintHeaderZoneSize);
        var nativeFootprintSizeOk =
            nativeFootprintHeaderZoneSize == checked((uint)(nativeFootprintZone.Length - FastFileConstants.T4XFileHeaderSize));
        var nativeFootprintBlockOk = VerifyBlockHeader(nativeFootprintZone, nativeFootprintPlan);
        var nativeFootprintReadBackErrors = 1;
        var nativeFootprintRoundTrip = false;
        string? nativeFootprintSha = null;
        if (nativeFootprintSizeOk && nativeFootprintBlockOk && allShellNameErrors == 0)
        {
            await File.WriteAllBytesAsync(nativeFootprintZonePath, nativeFootprintZone, cancellationToken);
            nativeFootprintReadBackErrors = VerifyReadBack(
                nativeFootprintZonePath,
                nativeFootprintZone,
                diagnosticRoots,
                allShellPayloads,
                nativeScriptTable,
                nativeFootprintPlan);
            if (nativeFootprintReadBackErrors == 0)
            {
                var nativeFootprintWrite = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
                    nativeFootprintFfPath,
                    nativeFootprintZone,
                    preferNativeLevel6: true,
                    cancellationToken: cancellationToken);
                nativeFootprintRoundTrip = nativeFootprintWrite.RoundTripVerified;
                if (nativeFootprintRoundTrip)
                    nativeFootprintSha = await Sha256Async(nativeFootprintFfPath, cancellationToken);
            }
        }
        var nativeFootprintReady =
            allShellNameErrors == 0 &&
            nativeFootprintSizeOk &&
            nativeFootprintBlockOk &&
            nativeFootprintReadBackErrors == 0 &&
            nativeFootprintRoundTrip;
        if (!nativeFootprintReady)
            TryDelete(nativeFootprintFfPath);

        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — NATIVE ROOT-SEQUENCE DB HARDWARE DIAGNOSTIC");
        sb.AppendLine(new string('=', 86));
        sb.AppendLine($"Map: {map.Name}");
        sb.AppendLine($"Native oracle: {nativeOracleFastFile}");
        sb.AppendLine($"Native root slots reproduced: {diagnosticRoots.Count:N0}");
        sb.AppendLine($"Native exact (type,name) identities available: {nativeIdentity.Entries.Count:N0}");
        sb.AppendLine($"Source structural payloads reused: {reused:N0}");
        sb.AppendLine($"Synthetic dependency shells inserted: {synthetic:N0}");
        sb.AppendLine($"Source roots dropped because native family count is lower: {dropped:N0}");
        sb.AppendLine($"ScriptStrings reproduced in native value order: {nativeStrings.Count:N0}");
        sb.AppendLine($"XAsset name-contract errors: {nameContractErrors:N0}");
        foreach (var failure in nameContractFailures.Take(64))
            sb.AppendLine("  NAME-CONTRACT: " + failure);
        if (nameContractFailures.Count > 64)
            sb.AppendLine($"  ... {nameContractFailures.Count - 64:N0} additional name-contract failure(s)");
        sb.AppendLine($"readBackErrors={readBackErrors:N0}; wrapperRoundTrip={roundTrip}; ready={ready}");
        sb.AppendLine($"sha256={sha ?? "<none>"}");
        sb.AppendLine();
        sb.AppendLine("ALL-NAME-SHELL ISOLATION");
        sb.AppendLine($"  nameContractErrors={allShellNameErrors:N0}; readBackErrors={allShellReadBackErrors:N0}; wrapperRoundTrip={allShellRoundTrip}; ready={allShellReady}");
        sb.AppendLine($"  sha256={allShellSha ?? "<none>"}");
        sb.AppendLine($"  package={(allShellReady ? allShellPackageDir : "<not emitted>")}");
        foreach (var failure in allShellNameFailures.Take(32))
            sb.AppendLine("  ALL-SHELL NAME-CONTRACT: " + failure);
        sb.AppendLine();
        sb.AppendLine("NATIVE ALLOCATION-FOOTPRINT ISOLATION");
        sb.AppendLine($"  nativeOracleZoneBytes={native.Bytes.LongLength:N0}; candidateZoneBytes={nativeFootprintZone.Length:N0}");
        foreach (var block in Enum.GetValues<T4ZoneBlock>())
            sb.AppendLine($"  {block,-18} nativeCapacity={nativeBlockCaps[block.ToString()],14:N0} B");
        sb.AppendLine($"  readBackErrors={nativeFootprintReadBackErrors:N0}; wrapperRoundTrip={nativeFootprintRoundTrip}; ready={nativeFootprintReady}");
        sb.AppendLine($"  sha256={nativeFootprintSha ?? "<none>"}");
        sb.AppendLine($"  package={(nativeFootprintReady ? nativeFootprintPackageDir : "<not emitted>")}");
        sb.AppendLine("  NOTE: compressed FF bytes remain small because payload shells are mostly zero; the exact native block table reproduces the loader/DB allocation contract without meaningless random padding.");
        sb.AppendLine();
        sb.AppendLine("PURPOSE");
        sb.AppendLine("  This is an A/B test for the recurring SP TU7 0x82168594 / 0xC0000005 frontier.");
        sb.AppendLine("  It reproduces the known-working native FF's XAsset TYPE sequence/counts and ScriptString VALUE order.");
        sb.AppendLine("  The ALL-NAME-SHELL variant also reproduces every exact native XAsset (type,name) identity.");
        sb.AppendLine("  It does NOT copy any native/donor asset payload bytes, shaders, images, sounds or world data.");
        sb.AppendLine("  Missing native dependency families are represented by inert self-contained Xbox-layout shells using identity evidence only.");
        sb.AppendLine("  Top-level XAsset bodies are 4-byte aligned before each typed root, matching Xenon struct allocation semantics.");
        sb.AppendLine();
        sb.AppendLine("INTERPRETATION");
        sb.AppendLine("  - Different/later first fault: DB root/string graph shape participates in the 0x82168594 crash.");
        sb.AppendLine("  - Same first fault: exact root names/dependency pointers or another activation contract still dominates.");
        sb.AppendLine("  - ALL-NAME-SHELL moves the fault: one of the reused structural payloads corrupts its runtime DB name/fixup.");
        sb.AppendLine("  - ALL-NAME-SHELL still hits 0x82168594: at least one type shell layout/name-fixup contract is wrong, or the caller is not the XAsset-name path we currently infer.");
        await File.WriteAllTextAsync(reportPath, sb.ToString(), new UTF8Encoding(false), cancellationToken);

        if (ready)
        {
            await File.WriteAllTextAsync(
                Path.Combine(packageDir, "NATIVE_SEQUENCE_DB_TEST.txt"),
                sb.ToString(),
                new UTF8Encoding(false),
                cancellationToken);
        }
        if (allShellReady)
        {
            await File.WriteAllTextAsync(
                Path.Combine(allShellPackageDir, "ALL_NAME_SHELL_DB_TEST.txt"),
                sb.ToString(),
                new UTF8Encoding(false),
                cancellationToken);
        }
        if (nativeFootprintReady)
        {
            await File.WriteAllTextAsync(
                Path.Combine(nativeFootprintPackageDir, "NATIVE_FOOTPRINT_DB_TEST.txt"),
                sb.ToString(),
                new UTF8Encoding(false),
                cancellationToken);
        }

        return new NormalMapLinkerV2NativeSequenceDiagnosticResult
        {
            MapName = map.Name,
            NativeOracleFastFile = Path.GetFullPath(nativeOracleFastFile),
            NativeRoots = diagnosticRoots.Count,
            ReusedSourceRoots = reused,
            SyntheticDependencyRoots = synthetic,
            DroppedSourceRoots = dropped,
            NativeScriptStrings = nativeStrings.Count,
            NameContractErrors = nameContractErrors,
            NameContractFailures = nameContractFailures,
            ReadBackErrors = readBackErrors,
            WrapperRoundTripVerified = roundTrip,
            CandidateReady = ready,
            FastFileSha256 = sha,
            OutputFastFile = ready ? ffPath : null,
            HardwarePackageDirectory = ready ? packageDir : null,
            AllShellNameContractErrors = allShellNameErrors,
            AllShellReadBackErrors = allShellReadBackErrors,
            AllShellWrapperRoundTripVerified = allShellRoundTrip,
            AllShellCandidateReady = allShellReady,
            AllShellFastFileSha256 = allShellSha,
            AllShellOutputFastFile = allShellReady ? allShellFfPath : null,
            AllShellHardwarePackageDirectory = allShellReady ? allShellPackageDir : null,
            NativeFootprintCandidateReady = nativeFootprintReady,
            NativeFootprintReadBackErrors = nativeFootprintReadBackErrors,
            NativeFootprintWrapperRoundTripVerified = nativeFootprintRoundTrip,
            NativeFootprintFastFileSha256 = nativeFootprintSha,
            NativeFootprintOutputFastFile = nativeFootprintReady ? nativeFootprintFfPath : null,
            NativeFootprintHardwarePackageDirectory = nativeFootprintReady ? nativeFootprintPackageDir : null,
            NativeOracleZoneBytes = native.Bytes.LongLength,
            NativeFootprintCandidateZoneBytes = nativeFootprintZone.Length,
            TextReportFile = reportPath
        };
    }

    public static async Task<NormalMapLinkerV2StructuralMainEmitResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null,
        NormalMapLinkerV2CanonicalRootPolicyResult? rootPolicyOverride = null,
        NormalMapLinkerV2DestinationBlockPlanResult? blockPlanOverride = null,
        NormalMapLinkerV2ScriptStringMergeResult? scriptOverride = null,
        CancellationToken cancellationToken = default)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var rootPolicy = rootPolicyOverride ?? await NormalMapLinkerV2CanonicalRootPolicy.RunAsync(map, outputRoot, source, cancellationToken: cancellationToken);
        var blockPlan = blockPlanOverride ?? await NormalMapLinkerV2DestinationBlockPlan.RunAsync(map, outputRoot, source, cancellationToken);
        var script = scriptOverride ?? await NormalMapLinkerV2ScriptStringMerge.RunAsync(map, outputRoot, source, cancellationToken);

        // WaW360Converter XBOX ROOT ORDER HARDWARE DIAGNOSTIC
        //
        // PC source XAsset ordinal order is provenance evidence, not the final
        // Xbox serialization order. Emit roots type-major by Xbox XAsset id,
        // retaining stable source provenance order inside each Xbox asset family.
        // This exact list is shared by the XAsset table, payload construction and
        // read-back verification so table/payload ordering cannot diverge.
        var emissionRoots = rootPolicy.Roots
            .OrderBy(r => r.XboxTypeId)
            .ThenBy(r => r.CanonicalSourceOrdinal)
            .ToList();

        var reorderedRootCount = 0;
        for (var i = 0; i < emissionRoots.Count; i++)
        {
            if (emissionRoots[i].CanonicalSourceOrdinal != i)
                reorderedRootCount++;
        }

        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_main_xanim_native_layout");
        var packageDir = Path.Combine(outputRoot, map.Name, "hardware_test_main_xanim_native_layout");
        var conservativePackageDir = Path.Combine(outputRoot, map.Name, "hardware_test_main_xanim_native_layout_tracked_shell");
        var scriptClosurePackageDir = Path.Combine(outputRoot, map.Name, "hardware_test_main_xanim_native_layout_script_closure");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(packageDir);
        Directory.CreateDirectory(conservativePackageDir);
        Directory.CreateDirectory(scriptClosurePackageDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_main_structural.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_main_structural.json");
        var zonePath = Path.Combine(outDir, source.CanonicalBaseName + ".zone");
        var ffPath = Path.Combine(packageDir, source.CanonicalBaseName + ".ff");
        var conservativeZonePath = Path.Combine(outDir, source.CanonicalBaseName + ".tracked_shell.zone");
        var conservativeFfPath = Path.Combine(conservativePackageDir, source.CanonicalBaseName + ".ff");
        var scriptClosureZonePath = Path.Combine(outDir, source.CanonicalBaseName + ".script_closure.zone");
        var scriptClosureFfPath = Path.Combine(scriptClosurePackageDir, source.CanonicalBaseName + ".ff");

        // Never let a stale candidate survive a failed rerun.
        TryDelete(ffPath);
        TryDelete(zonePath);
        TryDelete(Path.Combine(packageDir, "STRUCTURAL_NATIVE_TEST.txt"));
        TryDelete(Path.Combine(packageDir, "mod.arena"));
        TryDelete(conservativeFfPath);
        TryDelete(conservativeZonePath);
        TryDelete(Path.Combine(conservativePackageDir, "TRACKED_SHELL_NATIVE_TEST.txt"));
        TryDelete(scriptClosureFfPath);
        TryDelete(scriptClosureZonePath);
        TryDelete(Path.Combine(scriptClosurePackageDir, "SCRIPT_CLOSURE_NATIVE_TEST.txt"));

        var blockers = new List<string>();
        var notes = new List<string>
        {
            "STRUCTURAL NATIVE DIAGNOSTIC ONLY: every emitted main-graph root is rebuilt as a self-contained Xbox-endian object; source packed/shared child pointers are not copied or nulled in-place.",
            "This path is intentionally separate from the production main gate. A native boot/load result validates XFile/XAsset/block/wrapper structure, not gameplay, graphics, collision, shader or audio fidelity.",
            "The old smoke XFile header strategy (zoneSize=entire file and one identical capacity in all seven blocks) is retired from this path.",
            $"XBOX ROOT ORDER DIAGNOSTIC: {reorderedRootCount:N0}/{emissionRoots.Count:N0} root position(s) differ from canonical source ordinal order after stable Xbox type-major sorting."
        };

        if (!source.Verified) blockers.Add("Canonical source archive is not verified.");
        if (!rootPolicy.Verified || !rootPolicy.ProvenanceIdentityComplete) blockers.Add("Canonical provenance-root policy is not complete.");
        if (!blockPlan.Verified || !blockPlan.SourceCapacityPlanReady) blockers.Add("Source-derived per-block destination capacity plan is not ready.");
        if (!script.Verified || !script.FullRemapReady) blockers.Add("Canonical script-string remap is not complete.");

        var aliasRoots = rootPolicy.Roots.Count(r => r.SourceReferenceKind is T4AssetReferenceKind.OffsetOrAlias or T4AssetReferenceKind.Null);
        if (aliasRoots != 0)
            blockers.Add($"{aliasRoots:N0} source top-level root(s) are alias/null references. Alpha 5 structural main emitter refuses to guess their ownership.");

        var primary = source.FastFiles
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .OrderBy(x => LinkerV2ProofEvidence.RoleRank(x.Role))
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var weaponStrings = blockers.Count == 0
            ? await LinkerV2IwdWeaponStringCatalog.BuildAsync(
                source.SourceArchive
                    .Where(x => x.Role.Equals("iwd", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Path),
                cancellationToken)
            : new Dictionary<string, LinkerV2IwdWeaponStringCatalog.WeaponStrings>(StringComparer.OrdinalIgnoreCase);

        if (weaponStrings.Count != 0)
            notes.Add($"IWD WEAPON OWNERSHIP: indexed {weaponStrings.Count:N0} authoritative custom weapon file(s) for direct-string localization.");

        var contexts = new Dictionary<string, SourceContext>(StringComparer.OrdinalIgnoreCase);
        if (blockers.Count == 0)
        {
            foreach (var container in primary)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cached = await FastFileAnalysisCache.GetZoneAsync(container.Path, cancellationToken);
                try
                {
                    var payloads = await BuildSourcePayloadsAsync(
                        outputRoot, map.Name, source.CanonicalBaseName,
                        container.Role, container.Path, cached.Document, cached.Bytes, weaponStrings, cancellationToken);
                    contexts[container.Path] = new SourceContext(container.Role, container.Path, cached.Document, cached.Bytes, payloads);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    blockers.Add($"{container.FileName}: structural payload construction blocked: {ex.Message}");
                    break;
                }
            }
        }

        var rootPayloads = new List<StructuralPayload>(emissionRoots.Count);
        var fallbackCount = 0;
        var byType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (blockers.Count == 0)
        {
            foreach (var root in emissionRoots)
            {
                if (!contexts.TryGetValue(root.SourceFastFile, out var ctx))
                {
                    blockers.Add($"No source context exists for canonical root {root.IdentityKey}.");
                    break;
                }
                if (!ctx.Payloads.TryGetValue(root.SourceAssetIndex, out var payload))
                {
                    blockers.Add($"No self-contained structural payload exists for {root.IdentityKey} ({root.PcTypeName}).");
                    break;
                }
                rootPayloads.Add(payload);
                if (payload.Fallback) fallbackCount++;
                byType[root.XboxTypeName] = byType.GetValueOrDefault(root.XboxTypeName) + 1;
            }
        }

        var canonicalStrings = blockers.Count == 0
            ? await RebuildCanonicalScriptStringsAsync(primary, cancellationToken)
            : new CanonicalScriptTable(false, Array.Empty<string>());
        if (blockers.Count == 0)
        {
            var expected = script.CanonicalTableEntries;
            var actual = canonicalStrings.Values.Count + (canonicalStrings.HasNullSentinel ? 1 : 0);
            if (actual != expected)
                blockers.Add($"Canonical script-string reconstruction mismatch: expected {expected:N0}, rebuilt {actual:N0}.");
        }

        var readBackErrors = 0;
        var headerZoneSize = 0u;
        var correctZoneSize = false;
        var perBlockHeader = false;
        var wrapperRoundTrip = false;
        var zoneBytes = 0L;
        var ffBytes = 0L;
        string? sha = null;

        if (blockers.Count == 0)
        {
            var zone = BuildZone(emissionRoots, rootPayloads, canonicalStrings, blockPlan, out headerZoneSize);
            zoneBytes = zone.LongLength;
            correctZoneSize = headerZoneSize == checked((uint)(zone.Length - FastFileConstants.T4XFileHeaderSize));
            perBlockHeader = VerifyBlockHeader(zone, blockPlan);
            if (!correctZoneSize) blockers.Add("XFile zoneSize does not equal decompressed bytes after the 36-byte XFile header.");
            if (!perBlockHeader) blockers.Add("XFile seven-block header does not match the source-derived per-block capacity plan.");

            if (blockers.Count == 0)
            {
                await File.WriteAllBytesAsync(zonePath, zone, cancellationToken);
                readBackErrors = VerifyReadBack(zonePath, zone, emissionRoots, rootPayloads, canonicalStrings, blockPlan);
                if (readBackErrors != 0)
                    blockers.Add($"Independent structural zone read-back reported {readBackErrors:N0} error(s).");
            }

            if (blockers.Count == 0)
            {
                var write = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(ffPath, zone, preferNativeLevel6: true, cancellationToken: cancellationToken);
                wrapperRoundTrip = write.RoundTripVerified;
                ffBytes = write.OutputBytes;
                if (!wrapperRoundTrip) blockers.Add("Xbox single-zlib wrapper round-trip failed.");
                else sha = await Sha256Async(ffPath, cancellationToken);
            }
        }

        var candidateReady = blockers.Count == 0 &&
                             rootPayloads.Count == emissionRoots.Count &&
                             correctZoneSize && perBlockHeader && readBackErrors == 0 && wrapperRoundTrip;

        // Emit a second main-FF hardware candidate from the SAME analyzed graph. It keeps all
        // non-tracked structural payloads identical but reduces XAnim/XModel/Weapon/FX to named,
        // zero-owned-data shells. This is an A/B isolation tool: if both variants fault at the same
        // hardware frontier, promoted tracked payloads are unlikely to be the cause. If only the
        // proof-heavy candidate faults, the promoted tracked graph becomes the primary suspect.
        var conservativeReady = false;
        var conservativeReadBackErrors = 0;
        var conservativeRoundTrip = false;
        string? conservativeSha = null;
        if (blockers.Count == 0 && rootPayloads.Count == emissionRoots.Count)
        {
            var conservativePayloads = BuildTrackedShellVariant(rootPayloads);
            var conservativeZone = BuildZone(emissionRoots, conservativePayloads, canonicalStrings, blockPlan, out var conservativeHeaderZoneSize);
            var conservativeSizeOk = conservativeHeaderZoneSize == checked((uint)(conservativeZone.Length - FastFileConstants.T4XFileHeaderSize));
            var conservativeBlockOk = VerifyBlockHeader(conservativeZone, blockPlan);
            if (conservativeSizeOk && conservativeBlockOk)
            {
                await File.WriteAllBytesAsync(conservativeZonePath, conservativeZone, cancellationToken);
                conservativeReadBackErrors = VerifyReadBack(
                    conservativeZonePath, conservativeZone, emissionRoots, conservativePayloads, canonicalStrings, blockPlan);
                if (conservativeReadBackErrors == 0)
                {
                    var write = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
                        conservativeFfPath, conservativeZone, preferNativeLevel6: true, cancellationToken: cancellationToken);
                    conservativeRoundTrip = write.RoundTripVerified;
                    if (conservativeRoundTrip)
                    {
                        conservativeSha = await Sha256Async(conservativeFfPath, cancellationToken);
                        conservativeReady = true;
                        await WriteTrackedShellReadmeAsync(
                            conservativePackageDir, source.CanonicalBaseName, conservativeSha,
                            emissionRoots.Count, cancellationToken);
                    }
                }
            }
        }
        if (!conservativeReady)
            TryDelete(conservativeFfPath);

        // Third hardware matrix candidate: start from the tracked-shell graph, remove only
        // exact-name resident UI MenuLists proven by typed closure, then add exact IWD .gsc/.csc
        // RawFiles that the closure stage proves are absent from the current named RawFile set.
        // This intentionally tests the SP DB/script-lookup frontier; it does NOT claim final graph
        // convergence because golden validation may still require a different net RawFile count.
        var scriptClosureReady = false;
        var scriptClosureReadBackErrors = 0;
        var scriptClosureRoundTrip = false;
        string? scriptClosureSha = null;
        var scriptClosureMenusRemoved = 0;
        var scriptClosureRawPromoted = 0;
        var scriptClosureSkipReason = string.Empty;
        if (blockers.Count == 0 && rootPayloads.Count == emissionRoots.Count)
        {
            try
            {
                var typedClosure = await LoadTypedClosureAsync(map, outputRoot, source, cancellationToken);
                var trackedShellPayloads = BuildTrackedShellVariant(rootPayloads);
                var scriptVariant = await BuildScriptClosureVariantAsync(
                    emissionRoots,
                    trackedShellPayloads,
                    typedClosure,
                    cancellationToken);

                scriptClosureMenusRemoved = scriptVariant.ResidentMenusRemoved;
                scriptClosureRawPromoted = scriptVariant.RawFilesPromoted;

                if (scriptClosureMenusRemoved != 0 || scriptClosureRawPromoted != 0)
                {
                    var scriptZone = BuildZone(
                        scriptVariant.Roots,
                        scriptVariant.Payloads,
                        canonicalStrings,
                        blockPlan,
                        out var scriptHeaderZoneSize);
                    var scriptSizeOk = scriptHeaderZoneSize == checked((uint)(scriptZone.Length - FastFileConstants.T4XFileHeaderSize));
                    var scriptBlockOk = VerifyBlockHeader(scriptZone, blockPlan);
                    if (scriptSizeOk && scriptBlockOk)
                    {
                        await File.WriteAllBytesAsync(scriptClosureZonePath, scriptZone, cancellationToken);
                        scriptClosureReadBackErrors = VerifyReadBack(
                            scriptClosureZonePath,
                            scriptZone,
                            scriptVariant.Roots,
                            scriptVariant.Payloads,
                            canonicalStrings,
                            blockPlan);
                        if (scriptClosureReadBackErrors == 0)
                        {
                            var write = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
                                scriptClosureFfPath,
                                scriptZone,
                                preferNativeLevel6: true,
                                cancellationToken: cancellationToken);
                            scriptClosureRoundTrip = write.RoundTripVerified;
                            if (scriptClosureRoundTrip)
                            {
                                scriptClosureSha = await Sha256Async(scriptClosureFfPath, cancellationToken);
                                scriptClosureReady = true;
                                await WriteScriptClosureReadmeAsync(
                                    scriptClosurePackageDir,
                                    source.CanonicalBaseName,
                                    scriptClosureSha,
                                    scriptVariant.Roots.Count,
                                    scriptClosureMenusRemoved,
                                    scriptClosureRawPromoted,
                                    typedClosure.GoldenExpectedRawFilePromotions,
                                    cancellationToken);
                            }
                            else
                            {
                                scriptClosureSkipReason = "Xbox fastfile wrapper round-trip verification failed.";
                            }
                        }
                        else
                        {
                            scriptClosureSkipReason = $"Independent script-closure zone read-back reported {scriptClosureReadBackErrors:N0} error(s).";
                        }
                    }
                    else
                    {
                        scriptClosureSkipReason =
                            $"Script-closure zone preflight failed: zoneSizeOk={scriptSizeOk}; blockHeaderOk={scriptBlockOk}.";
                    }
                }
                else
                {
                    scriptClosureSkipReason =
                        "Typed closure produced no script-closure delta: residentMenusRemoved=0 and rawFilesPromoted=0.";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                scriptClosureSkipReason = $"{ex.GetType().Name}: {ex.Message}";
                notes.Add($"SCRIPT-CLOSURE MATRIX SKIPPED: {scriptClosureSkipReason}");
            }
        }
        else
        {
            scriptClosureSkipReason =
                $"Main structural prerequisites were not satisfied: blockers={blockers.Count:N0}; payloads={rootPayloads.Count:N0}/{emissionRoots.Count:N0}.";
        }

        if (!scriptClosureReady)
        {
            TryDelete(scriptClosureFfPath);
            Directory.CreateDirectory(scriptClosurePackageDir);
            await File.WriteAllTextAsync(
                Path.Combine(scriptClosurePackageDir, "SCRIPT_CLOSURE_SKIPPED.txt"),
                "WaW360Converter — SCRIPT-CLOSURE CANDIDATE NOT EMITTED" + Environment.NewLine +
                new string('=', 64) + Environment.NewLine +
                $"Map: {map.Name}" + Environment.NewLine +
                $"Reason: {scriptClosureSkipReason}" + Environment.NewLine +
                $"residentMenusRemoved={scriptClosureMenusRemoved:N0}" + Environment.NewLine +
                $"rawFilesPromoted={scriptClosureRawPromoted:N0}" + Environment.NewLine +
                $"readBackErrors={scriptClosureReadBackErrors:N0}" + Environment.NewLine +
                $"wrapperRoundTrip={scriptClosureRoundTrip}" + Environment.NewLine,
                new UTF8Encoding(false),
                cancellationToken);
        }

        if (candidateReady)
        {
            await WriteReadmeAsync(packageDir, source.CanonicalBaseName, sha!, rootPolicy.Roots.Count, fallbackCount, cancellationToken);
            var promoted = rootPayloads
                .Where(p => TrackedTypes.Contains(p.TypeName, StringComparer.OrdinalIgnoreCase) && !p.Fallback)
                .GroupBy(p => p.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            notes.Add($"NATIVE GATE OPEN: structural main FF emitted with {emissionRoots.Count:N0} authoritative source roots and {fallbackCount:N0} explicitly reduced/fallback payload(s).");
            notes.Add($"REAL PAYLOAD PROMOTION: total={promoted.Values.Sum():N0}; " +
                      string.Join(", ", TrackedTypes.Select(t => $"{t}={promoted.GetValueOrDefault(t):N0}")) +
                      ". Every promoted proof blob passed its typed self-contained-pointer audit.");
            var smoke = rootPayloads
                .Where(p => p.Note.StartsWith("smoke-promotion[", StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => p.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            if (smoke.Count != 0)
                notes.Add($"SMOKE PAYLOAD PROMOTION: total={smoke.Values.Sum():N0}; " +
                          string.Join(", ", TrackedTypes.Select(t => $"{t}={smoke.GetValueOrDefault(t):N0}")) +
                          ". Smoke promotions remain fallback-class and are not counted as fidelity-ready real payloads.");
        }
        else
        {
            TryDelete(ffPath);
        }

        var realPromotedByType = rootPayloads
            .Where(p => TrackedTypes.Contains(p.TypeName, StringComparer.OrdinalIgnoreCase) && !p.Fallback)
            .GroupBy(p => p.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var smokePromotedByType = rootPayloads
            .Where(p => p.Note.StartsWith("smoke-promotion[", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var promotionRejectReasons = rootPayloads
            .Where(p => p.Fallback && TrackedTypes.Contains(p.TypeName, StringComparer.OrdinalIgnoreCase) &&
                        p.Note.StartsWith("promotion-rejected[", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => NormalizePromotionRejectReason(p.TypeName, p.Note), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Deliberately false: this stage bypasses packed/shared production ownership by rebuilding
        // self-contained smoke objects. It can native-test the loader but must never satisfy the real
        // main-linker readiness gate.
        const bool productionReady = false;
        var result = new NormalMapLinkerV2StructuralMainEmitResult
        {
            MapName = map.Name,
            CanonicalBaseName = source.CanonicalBaseName,
            OutputFastFile = candidateReady ? ffPath : null,
            OutputZone = File.Exists(zonePath) ? zonePath : null,
            HardwarePackageDirectory = candidateReady ? packageDir : null,
            ConservativeOutputFastFile = conservativeReady ? conservativeFfPath : null,
            ConservativeHardwarePackageDirectory = conservativeReady ? conservativePackageDir : null,
            ConservativeCandidateReady = conservativeReady,
            ConservativeReadBackErrors = conservativeReadBackErrors,
            ConservativeWrapperRoundTripVerified = conservativeRoundTrip,
            ConservativeFastFileSha256 = conservativeSha,
            ScriptClosureOutputFastFile = scriptClosureReady ? scriptClosureFfPath : null,
            ScriptClosureHardwarePackageDirectory = scriptClosureReady ? scriptClosurePackageDir : null,
            ScriptClosureCandidateReady = scriptClosureReady,
            ScriptClosureReadBackErrors = scriptClosureReadBackErrors,
            ScriptClosureWrapperRoundTripVerified = scriptClosureRoundTrip,
            ScriptClosureFastFileSha256 = scriptClosureSha,
            ScriptClosureResidentMenusRemoved = scriptClosureMenusRemoved,
            ScriptClosureRawFilesPromoted = scriptClosureRawPromoted,
            XboxAssets = emissionRoots.Count,
            ScriptStrings = canonicalStrings.Values.Count + (canonicalStrings.HasNullSentinel ? 1 : 0),
            PayloadsBuilt = rootPayloads.Count,
            FallbackPayloads = fallbackCount,
            RealPromotedPayloads = realPromotedByType.Values.Sum(),
            RealPromotedByType = realPromotedByType,
            SmokePromotedPayloads = smokePromotedByType.Values.Sum(),
            SmokePromotedByType = smokePromotedByType,
            PromotionRejectedPayloads = promotionRejectReasons.Values.Sum(),
            PromotionRejectReasons = promotionRejectReasons,
            SourceAliasRoots = aliasRoots,
            ReadBackErrors = readBackErrors,
            ZoneBytes = zoneBytes,
            FastFileBytes = ffBytes,
            HeaderZoneSize = headerZoneSize,
            CorrectZoneSizeSemantics = correctZoneSize,
            PerBlockCapacityHeader = perBlockHeader,
            WrapperRoundTripVerified = wrapperRoundTrip,
            StructuralNativeCandidateReady = candidateReady,
            ProductionMainReady = productionReady,
            FastFileSha256 = sha,
            PayloadsByType = byType,
            Notes = notes,
            Blockers = blockers,
            Verified = candidateReady,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, blockPlan), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private static async Task<IReadOnlyDictionary<int, StructuralPayload>> BuildSourcePayloadsAsync(
        string outputRoot,
        string mapName,
        string canonicalBaseName,
        string role,
        string sourcePath,
        T4ZoneDocument doc,
        byte[] zone,
        IReadOnlyDictionary<string, LinkerV2IwdWeaponStringCatalog.WeaponStrings> weaponStrings,
        CancellationToken ct)
    {
        if (doc.Endianness != Endian.Little)
            throw new InvalidDataException($"Alpha 5 structural main emitter expects PC/little-endian source FFs; {Path.GetFileName(sourcePath)} is {doc.Endianness}.");

        var output = new Dictionary<int, StructuralPayload>();

        // Proof-backed name-preserving reduced shells for the four already-verified typed families.
        foreach (var type in TrackedTypes)
        {
            var assets = Physical(doc, type);
            if (assets.Count == 0) continue;
            var proofPath = LinkerV2ProofEvidence.GetProofPath(outputRoot, mapName, sourcePath, role, type);
            if (!File.Exists(proofPath))
                throw new InvalidDataException($"typed {type} proof evidence is unavailable after Fast Mode identity materialization: {proofPath}");
            var proof = await T4NativeProofContainerReader.IndexAsync(proofPath, type, ct);
            if (type.Equals("xanim", StringComparison.OrdinalIgnoreCase) && proof.Version != 12)
                throw new InvalidDataException(
                    $"typed XAnim proof is stale (version={proof.Version}, required=12): {proofPath}. " +
                    "Regenerate the native Xbox XAnim proof before emitting a structural main candidate.");
            if (proof.Records.Count != assets.Count)
                throw new InvalidDataException($"{Path.GetFileName(sourcePath)} {type} proof count mismatch: roots={assets.Count:N0}, proof={proof.Records.Count:N0}.");
            var fixedSize = type.ToLowerInvariant() switch
            {
                "xanim" => 92,
                "xmodel" => 0xD4,
                "weapon" => 0x9AC,
                "fx" => 36,
                _ => throw new InvalidOperationException()
            };
            for (var i = 0; i < assets.Count; i++)
            {
                var record = proof.Records[i];
                var name = await LinkerV2ProofEvidence.TryReadNameAsync(record, ct);

                var reason = string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var blob = await T4NativeProofContainerReader.ReadBlobAsync(record, ct);
                    var safe = false;
                    var auditedName = name;

                    switch (type.ToLowerInvariant())
                    {
                        case "xanim":
                            safe = IsSelfContainedXboxXAnim(blob);
                            if (!safe) reason = "typed XAnim fixed body retains a packed/source pointer";
                            break;
                        case "xmodel":
                            // Hardware TU7 DB_GetXAssetTypeSize returns 0xD4 for XModel.
                            // The current proof serializer still carries the legacy 0xE4
                            // PC-shaped fixed body, so promoting it shifts the loader stream
                            // by 16 bytes and eventually leaves the runtime XModel name NULL.
                            // Quarantine proof-heavy XModels until a native 0xD4 serializer
                            // is implemented; the fallback below emits a hardware-correct
                            // 212-byte named shell instead.
                            safe = false;
                            reason = "Xbox TU7 XModel fixed body is 0xD4; legacy 0xE4 proof payload quarantined pending native 212-byte serializer";
                            break;
                        case "weapon":
                            safe = T4WeaponVariableSerializerProof.TryValidateSelfContainedXboxBlob(blob, name, out reason);
                            if (!safe &&
                                weaponStrings.TryGetValue(name, out var iwdWeapon))
                            {
                                var iwdSafe = T4WeaponVariableSerializerProof.TryBuildStructuralXboxBlobWithIwdStrings(
                                    blob,
                                    name,
                                    iwdWeapon.ByProofLabel,
                                    out var iwdBlob,
                                    out var localizedStrings,
                                    out var iwdReason);

                                if (iwdSafe)
                                {
                                    output[assets[i].Index] = new StructuralPayload(
                                        type,
                                        name,
                                        iwdBlob,
                                        true,
                                        $"smoke-promotion[weapon]: localized {localizedStrings:N0} packed direct WeaponDef string/animation field(s) from authoritative IWD '{Path.GetFileName(iwdWeapon.SourceIwd)}:{iwdWeapon.SourceEntry}', then passed the complete typed self-contained Weapon walk");
                                    continue;
                                }

                                if (localizedStrings != 0)
                                    reason = $"after localizing {localizedStrings:N0} authoritative IWD Weapon string/animation field(s): {iwdReason}";
                            }
                            break;
                        case "fx":
                            safe = T4FxSerializerProof.TryValidateSelfContainedXboxBlob(blob, name, out reason);
                            break;
                    }

                    if (safe)
                    {
                        output[assets[i].Index] = new StructuralPayload(
                            type,
                            name,
                            blob,
                            false,
                            $"verified Xbox-endian {type} proof blob promoted with complete typed owned payload; no packed source pointer retained");
                        continue;
                    }
                }

                name ??= $"__waw360_{type}_{Path.GetFileNameWithoutExtension(sourcePath)}_{assets[i].Index:D5}";
                if (string.IsNullOrWhiteSpace(reason))
                    reason = "verified proof blob retained a source dependency or could not satisfy the self-contained typed audit";
                output[assets[i].Index] = new StructuralPayload(type, name, BuildFixedNameShell(fixedSize, name), true,
                    $"promotion-rejected[{type}]: {reason}");
            }
        }

        // Audio fidelity is a STRICT gate, not a structural-loader gate. Preserve every sound
        // alias list the deterministic parser can prove; if a custom source run cannot be mapped
        // exactly, retain that root as an explicit one-alias silent structural stub instead of
        // aborting the entire map. This never claims XMA/SDNS fidelity and is reported as fallback.
        var sound = T4SoundAliasSmokeSerializer.Build(doc, zone, ct);
        if (sound.ReadBackErrors != 0)
            throw new InvalidDataException($"Sound structural read-back failed for {Path.GetFileName(sourcePath)}: errors={sound.ReadBackErrors:N0}.");
        foreach (var s in sound.AssetsByIndex.Values)
            output[s.AssetIndex] = new StructuralPayload(
                "sound",
                s.AliasListName,
                BuildSoundListEmpty(s.AliasListName),
                true,
                "native-sequence-safe empty SoundAliasList shell; source child alias graph intentionally suppressed");

        var physicalSounds = Physical(doc, "sound");
        foreach (var asset in physicalSounds)
        {
            if (output.ContainsKey(asset.Index)) continue;
            var fallbackName = $"__waw360_silent_sound_{Path.GetFileNameWithoutExtension(sourcePath)}_{asset.Index:D5}";
            output[asset.Index] = new StructuralPayload(
                "sound",
                fallbackName,
                BuildSoundListEmpty(fallbackName),
                true,
                $"native-sequence-safe empty SoundAliasList shell; deterministic sound coverage was {sound.ParsedPhysicalSoundAssets:N0}/{sound.DeclaredPhysicalSoundAssets:N0}; final SDNS/XMA conversion remains required");
        }

        var techsets = T4TechniqueSetNullGraphSerializer.Build(doc);
        if (!techsets.Verified && techsets.DeclaredPhysicalTechniqueSets != 0)
            throw new InvalidDataException($"TechniqueSet structural serialization failed for {Path.GetFileName(sourcePath)}.");
        foreach (var t in techsets.AssetsByIndex.Values)
            output[t.AssetIndex] = new StructuralPayload("techset", t.SmokeName, t.XboxPayload, true,
                "null-graph TechniqueSet; Xenos shader graph fidelity pending");

        // Preserve exact simple payloads where the deterministic count/order contract closes.
        var simple = T4SimpleNativeAssetSerializer.Build(doc, zone);
        foreach (var p in simple.AssetsByIndex.Values)
            output[p.AssetIndex] = new StructuralPayload(p.TypeName, p.Name, p.Payload, false, "source-owned simple payload preserved");

        // MenuList name recovery is used only for this explicit smoke candidate. The production
        // linker still treats scanner results as non-authoritative until a deterministic walker is
        // migrated.
        var payloadScan = T4PayloadScanner.Scan(doc, zone);
        var menus = Physical(doc, "menulist");
        var menuNames = payloadScan.MenuLists.Count == menus.Count
            ? payloadScan.MenuLists.Select(x => x.Name).ToArray()
            : Array.Empty<string>();
        for (var i = 0; i < menus.Count; i++)
        {
            var name = i < menuNames.Length && !string.IsNullOrWhiteSpace(menuNames[i])
                ? menuNames[i]
                : $"__waw360_menu_{menus[i].Index:D5}";
            output[menus[i].Index] = new StructuralPayload("menulist", name, BuildMenuList(name), i >= menuNames.Length,
                "MenuList root retained with zero menu pointers for structural native test");
        }

        // Inert fixed layouts. Use the real map identity for world roots so the engine can at least
        // resolve the requested BSP root names during a native A/B test.
        var worldName = $"maps/{canonicalBaseName}.d3dbsp";
        foreach (var asset in Physical(doc, "material"))
        {
            var name = $"__waw360_mat_{Path.GetFileNameWithoutExtension(sourcePath)}_{asset.Index:D5}";
            output[asset.Index] = new StructuralPayload("material", name, BuildFixedNameShell(96, name), true,
                "inert Material: technique/textures/constants/state intentionally null");
        }
        foreach (var asset in Physical(doc, "comworld"))
            output[asset.Index] = new StructuralPayload("comworld", worldName, BuildFixedNameShell(64, worldName), true, "reduced ComWorld root");
        foreach (var asset in Physical(doc, "gameworld_sp"))
            output[asset.Index] = new StructuralPayload("gameworld_sp", worldName, BuildFixedNameShell(44, worldName), true, "reduced GameWorldSp root");
        foreach (var asset in Physical(doc, "clipmap"))
            output[asset.Index] = new StructuralPayload("clipmap_pvs", worldName, BuildFixedNameShell(332, worldName), true, "reduced Xbox clipmap_pvs world root");
        foreach (var asset in Physical(doc, "gfxworld"))
            output[asset.Index] = new StructuralPayload("gfxworld", worldName, BuildGfxWorld(804, worldName, canonicalBaseName), true, "reduced GfxWorld root");
        foreach (var asset in Physical(doc, "lightdef"))
        {
            var name = $"__waw360_lightdef_{asset.Index:D5}";
            output[asset.Index] = new StructuralPayload("lightdef", name, BuildLightDef(name), true, "reduced GfxLightDef root with no attenuation image");
        }

        // Fail-safe simple fallbacks. These are deliberately obvious and reported. They keep the
        // structural native candidate self-contained without pretending unresolved scanner data was
        // converted faithfully.
        foreach (var asset in doc.Assets.Where(a => a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
        {
            if (output.ContainsKey(asset.Index)) continue;
            switch (asset.TypeName.ToLowerInvariant())
            {
                case "rawfile":
                {
                    var name = $"__waw360_raw_{Path.GetFileNameWithoutExtension(sourcePath)}_{asset.Index:D5}";
                    output[asset.Index] = new StructuralPayload("rawfile", name, BuildRawFileEmpty(name), true,
                        "fallback empty RawFile; source body/name mapping unresolved in this structural diagnostic");
                    break;
                }
                case "localize":
                {
                    var name = $"__waw360_loc_{Path.GetFileNameWithoutExtension(sourcePath)}_{asset.Index:D5}";
                    output[asset.Index] = new StructuralPayload("localize", name, BuildLocalize(name, "[waw360 structural]"), true,
                        "fallback Localize placeholder");
                    break;
                }
                case "stringtable":
                {
                    var name = $"__waw360_stringtable_{asset.Index:D5}";
                    output[asset.Index] = new StructuralPayload("stringtable", name, BuildStringTableEmpty(name), true,
                        "fallback empty StringTable");
                    break;
                }
                case "font":
                {
                    // T4 Font_s is a 24-byte 32-bit root:
                    // fontName*, pixelHeight, glyphCount, material*, glowMaterial*, glyphs*.
                    // Structural smoke output owns only a fresh name and leaves all rendering/glyph
                    // dependencies null with zero counts. This is intentionally fidelity-inert.
                    var name = $"__waw360_font_{Path.GetFileNameWithoutExtension(sourcePath)}_{asset.Index:D5}";
                    output[asset.Index] = new StructuralPayload("font", name, BuildFixedNameShell(24, name), true,
                        "fallback empty T4 Font_s root; pixelHeight/glyphCount/material/glow/glyphs are intentionally zero/null");
                    break;
                }
                default:
                    throw new InvalidDataException($"No alpha-5 self-contained structural serializer exists for XAsset#{asset.Index} '{asset.TypeName}' in {Path.GetFileName(sourcePath)}.");
            }
        }

        return output;
    }

    private sealed record CanonicalScriptTable(bool HasNullSentinel, IReadOnlyList<string> Values);

    private static async Task<CanonicalScriptTable> RebuildCanonicalScriptStringsAsync(
        IReadOnlyList<LinkerV2SourceContainer> primary,
        CancellationToken ct)
    {
        var hasNull = false;
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var container in primary)
        {
            var cached = await FastFileAnalysisCache.GetZoneAsync(container.Path, ct);
            foreach (var s in cached.Document.ScriptStrings)
            {
                if (s.RawPointer == 0)
                {
                    hasNull = true;
                    continue;
                }
                if (!s.Inline || s.Value is null || s.Value.StartsWith("<alias ", StringComparison.Ordinal))
                    throw new InvalidDataException($"Canonical script table still contains unresolved alias {container.FileName}[{s.Index}] 0x{s.RawPointer:X8}.");
                if (seen.Add(s.Value)) values.Add(s.Value);
            }
        }
        return new CanonicalScriptTable(hasNull, values);
    }

    private static byte[] BuildZone(
        IReadOnlyList<LinkerV2CanonicalRoot> roots,
        IReadOnlyList<StructuralPayload> payloads,
        CanonicalScriptTable strings,
        NormalMapLinkerV2DestinationBlockPlanResult blockPlan,
        out uint headerZoneSize)
    {
        if (roots.Count != payloads.Count)
            throw new InvalidDataException(
                $"XASSET/PAYLOAD ORDER GUARD: roots={roots.Count:N0}, payloads={payloads.Count:N0}.");

        for (var i = 0; i < roots.Count; i++)
        {
            var payloadTypeId = T4XboxAssetTypeMap.GetXboxTypeId(payloads[i].TypeName);
            if (payloadTypeId != roots[i].XboxTypeId)
            {
                throw new InvalidDataException(
                    $"XASSET/PAYLOAD ORDER GUARD: index {i:N0} root={roots[i].XboxTypeName}(0x{roots[i].XboxTypeId:X}) " +
                    $"payload={payloads[i].TypeName}(0x{payloadTypeId:X}).");
            }
        }

        using var ms = new MemoryStream();
        for (var i = 0; i < 9; i++) WriteU32(ms, 0);

        var scriptCount = strings.Values.Count + (strings.HasNullSentinel ? 1 : 0);
        WriteU32(ms, checked((uint)scriptCount));
        WriteU32(ms, scriptCount == 0 ? 0u : T4ZonePointer.Following);
        WriteU32(ms, checked((uint)roots.Count));
        WriteU32(ms, roots.Count == 0 ? 0u : T4ZonePointer.Following);

        if (strings.HasNullSentinel) WriteU32(ms, 0);
        foreach (var _ in strings.Values) WriteU32(ms, T4ZonePointer.Following);
        foreach (var value in strings.Values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            ms.Write(bytes);
            ms.WriteByte(0);
        }

        foreach (var root in roots)
        {
            WriteU32(ms, checked((uint)root.XboxTypeId));

            // T4 Xenon top-level XAssets are written through TEMP-block asset
            // writers. OAT's ZoneOutputStream::MarkFollowing emits INSERT (-2)
            // for TEMP pointers, not FOLLOWING (-1). INSERT lets the loader
            // preserve the resolved asset reference instead of leaving callers
            // pointed at the shared TEMP scratch buffer.
            WriteU32(ms, T4ZonePointer.Insert);
        }

        // IMPORTANT: do not serialize destination-block alignment padding.
        // OAT's ZoneOutputStream::Align only advances the selected XFile block's
        // allocation cursor. It does not append bytes to the zone data stream.
        // The payload bytes therefore remain contiguous in loader traversal order.
        foreach (var payload in payloads)
            ms.Write(payload.Bytes);

        if (ms.Length > uint.MaxValue)
            throw new InvalidDataException("Alpha 5 structural main zone exceeds T4's 32-bit zoneSize field.");
        if (ms.Length < FastFileConstants.T4XFileHeaderSize)
            throw new InvalidDataException("Alpha 5 structural main zone is shorter than the XFile header.");

        headerZoneSize = checked((uint)(ms.Length - FastFileConstants.T4XFileHeaderSize));
        ms.Position = 0;
        WriteU32(ms, headerZoneSize);
        WriteU32(ms, 0);
        foreach (var block in Enum.GetValues<T4ZoneBlock>())
            WriteU32(ms, blockPlan.StructuralCapacities.GetValueOrDefault(block.ToString()));
        return ms.ToArray();
    }

    private static int VerifyReadBack(
        string path,
        byte[] zone,
        IReadOnlyList<LinkerV2CanonicalRoot> roots,
        IReadOnlyList<StructuralPayload> payloads,
        CanonicalScriptTable strings,
        NormalMapLinkerV2DestinationBlockPlanResult blockPlan)
    {
        var errors = 0;
        T4ZoneDocument read;
        try
        {
            read = T4ZoneParser.ParseZoneBytes(path, zone, WaWPlatform.Xbox360, WaWCompressionKind.XboxSingleZlib, Endian.Big);
        }
        catch
        {
            return 1;
        }

        var expectedStrings = strings.Values.Count + (strings.HasNullSentinel ? 1 : 0);
        if (read.ScriptStringCount != expectedStrings || read.AssetCount != roots.Count) errors++;
        if (read.Header.ZoneSize != checked((uint)(zone.Length - FastFileConstants.T4XFileHeaderSize))) errors++;
        foreach (var block in Enum.GetValues<T4ZoneBlock>())
            if (read.Header.BlockSizes[(int)block] != blockPlan.StructuralCapacities.GetValueOrDefault(block.ToString())) errors++;

        var si = 0;
        if (strings.HasNullSentinel)
        {
            if (read.ScriptStrings.Count == 0 || read.ScriptStrings[0].RawPointer != 0) errors++;
            si = 1;
        }
        for (var i = 0; i < strings.Values.Count && si + i < read.ScriptStrings.Count; i++)
            if (!read.ScriptStrings[si + i].Inline || !string.Equals(read.ScriptStrings[si + i].Value, strings.Values[i], StringComparison.Ordinal)) errors++;

        for (var i = 0; i < Math.Min(roots.Count, read.Assets.Count); i++)
        {
            if (read.Assets[i].TypeId != roots[i].XboxTypeId) errors++;
            if (read.Assets[i].RawHeader != T4ZonePointer.Insert) errors++;
        }

        var cursor = checked((int)(read.AssetTableOffset + (long)read.AssetCount * 8));
        foreach (var p in payloads)
        {
            if (cursor > zone.Length - p.Bytes.Length)
            {
                errors++;
                break;
            }
            if (!zone.AsSpan(cursor, p.Bytes.Length).SequenceEqual(p.Bytes)) errors++;
            cursor += p.Bytes.Length;
        }
        if (cursor != zone.Length) errors++;
        return errors;
    }

    private static bool VerifyBlockHeader(byte[] zone, NormalMapLinkerV2DestinationBlockPlanResult plan)
    {
        if (zone.Length < FastFileConstants.T4XFileHeaderSize) return false;
        for (var i = 0; i < 7; i++)
        {
            var actual = BinaryPrimitives.ReadUInt32BigEndian(zone.AsSpan(8 + i * 4, 4));
            var block = (T4ZoneBlock)i;
            if (actual != plan.StructuralCapacities.GetValueOrDefault(block.ToString())) return false;
        }
        return true;
    }

    private static List<T4AssetEntry> Physical(T4ZoneDocument doc, string type) =>
        doc.Assets.Where(a => a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                              a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .OrderBy(a => a.Index).ToList();

    private static string NormalizePromotionRejectReason(string type, string note)
    {
        var prefix = $"promotion-rejected[{type}]: ";
        var reason = note.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? note[prefix.Length..]
            : note;

        // Preserve the typed field name while removing per-asset names/addresses so the
        // census groups the actual dependency class rather than producing one row per asset.
        var marker = " retains packed/source pointer ";
        var i = reason.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
            reason = reason[..i] + " retains packed/source pointer";

        // Typed diagnostics often prefix the concrete asset name before the child field.
        // Strip only that name so identical dependency classes group together in the census.
        foreach (var family in new[] { "XModel", "Weapon", "FX" })
        {
            var prefixWithName = family + " '";
            if (!reason.StartsWith(prefixWithName, StringComparison.OrdinalIgnoreCase)) continue;
            var quoteEnd = reason.IndexOf("' ", prefixWithName.Length, StringComparison.Ordinal);
            if (quoteEnd >= 0)
                reason = family + " " + reason[(quoteEnd + 2)..];
            break;
        }

        return $"{type}: {reason}";
    }

    private static bool IsSelfContainedXboxXAnim(byte[] blob)
    {
        const int fixedSize = 92;
        if (blob.Length < fixedSize) return false;

        static bool SelfContained(uint value) =>
            value == 0 || value is T4ZonePointer.Following or T4ZonePointer.Insert;

        // Xbox T4 XAnimParts is 0x5C. name remains at +0x00; the expanded
        // 12-byte boneCount table shifts the ten owned pointer fields to +0x34..+0x58.
        if (!SelfContained(BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(0, 4))))
            return false;
        for (var offset = 52; offset <= 88; offset += 4)
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(offset, 4));
            if (!SelfContained(value))
                return false;
        }
        return true;
    }

    private sealed record ScriptClosureVariant(
        IReadOnlyList<LinkerV2CanonicalRoot> Roots,
        IReadOnlyList<StructuralPayload> Payloads,
        int ResidentMenusRemoved,
        int RawFilesPromoted);

    private static Task<NormalMapLinkerV2TypedClosurePlanResult> LoadTypedClosureAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult source,
        CancellationToken ct)
    {
        // Hardware diagnostics must be derived from the CURRENT source/proof graph.
        // Never reuse an older typed-closure JSON here: XAnim proof identity/version,
        // source archives and IWD contents can all change while the cache file remains.
        return NormalMapLinkerV2TypedClosurePlan.RunAsync(map, outputRoot, source, ct);
    }

    private static async Task<ScriptClosureVariant> BuildScriptClosureVariantAsync(
        IReadOnlyList<LinkerV2CanonicalRoot> roots,
        IReadOnlyList<StructuralPayload> payloads,
        NormalMapLinkerV2TypedClosurePlanResult closure,
        CancellationToken ct)
    {
        if (roots.Count != payloads.Count)
            throw new InvalidDataException("Script-closure matrix requires one payload per canonical provenance root.");

        static string Full(string path) => Path.GetFullPath(path);
        var resident = closure.ResidentRoots
            .Select(x => (Path: Full(x.SourceFastFile), x.AssetIndex))
            .ToHashSet();

        var keptRoots = new List<LinkerV2CanonicalRoot>(roots.Count + closure.Promotions.Count);
        var keptPayloads = new List<StructuralPayload>(payloads.Count + closure.Promotions.Count);
        var removed = 0;
        for (var i = 0; i < roots.Count; i++)
        {
            var root = roots[i];
            if (resident.Contains((Full(root.SourceFastFile), root.SourceAssetIndex)))
            {
                removed++;
                continue;
            }
            keptRoots.Add(root);
            keptPayloads.Add(payloads[i]);
        }

        var rawPromotions = closure.Promotions
            .Where(x => x.AssetType.Equals("rawfile", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Archive, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Entry, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var nextOrdinal = keptRoots.Count == 0 ? 0 : keptRoots.Max(x => x.CanonicalSourceOrdinal) + 1;
        var promoted = 0;
        foreach (var p in rawPromotions)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(p.Archive))
                throw new FileNotFoundException("Typed-closure IWD archive disappeared.", p.Archive);

            byte[] sourceBytes;
            using (var zip = ZipFile.OpenRead(p.Archive))
            {
                var entry = zip.Entries.FirstOrDefault(x =>
                    x.FullName.Replace('\\', '/').Equals(p.Entry.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                    throw new InvalidDataException($"IWD RawFile promotion entry disappeared: {Path.GetFileName(p.Archive)}::{p.Entry}");
                await using var stream = entry.Open();
                using var ms = new MemoryStream(checked((int)Math.Min(entry.Length + 1L, int.MaxValue)));
                await stream.CopyToAsync(ms, ct);
                sourceBytes = ms.ToArray();
            }

            var name = p.Name.Replace('\\', '/').TrimStart('/');
            var payload = BuildRawFileOwned(name, sourceBytes);
            var root = new LinkerV2CanonicalRoot(
                nextOrdinal++,
                "iwd",
                p.Archive,
                -1,
                "rawfile",
                "rawfile",
                T4XboxAssetTypeMap.GetXboxTypeId("rawfile"),
                T4AssetReferenceKind.InlineShared,
                T4ZonePointer.Insert,
                $"iwd|{Path.GetFileName(p.Archive)}|{p.Entry}|rawfile",
                "HARDWARE_DIAGNOSTIC_TYPED_IWD_RAWFILE_PROMOTION");
            keptRoots.Add(root);
            keptPayloads.Add(new StructuralPayload(
                "rawfile",
                name,
                payload,
                false,
                $"script-closure hardware diagnostic: exact IWD RawFile bytes promoted from {Path.GetFileName(p.Archive)}::{p.Entry}"));
            promoted++;
        }

        var ordered = keptRoots
            .Select((root, i) => (root, payload: keptPayloads[i]))
            .OrderBy(x => x.root.XboxTypeId)
            .ThenBy(x => x.root.CanonicalSourceOrdinal)
            .ToList();

        return new ScriptClosureVariant(
            ordered.Select(x => x.root).ToList(),
            ordered.Select(x => x.payload).ToList(),
            removed,
            promoted);
    }

    private static byte[] BuildRawFileOwned(string name, ReadOnlySpan<byte> sourceBytes)
    {
        if (sourceBytes.Length > int.MaxValue - 1)
            throw new InvalidDataException($"IWD RawFile '{name}' is too large.");
        var n = Encoding.ASCII.GetBytes(name);
        var bytes = new byte[12 + n.Length + 1 + sourceBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), T4ZonePointer.Following);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), checked((uint)sourceBytes.Length));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), T4ZonePointer.Following);
        n.CopyTo(bytes.AsSpan(12));
        sourceBytes.CopyTo(bytes.AsSpan(12 + n.Length + 1));
        // Final byte is the owned RawFile buffer terminator outside the logical len.
        return bytes;
    }

    private static IReadOnlyList<string> AuditXAssetNameContracts(
        IReadOnlyList<LinkerV2CanonicalRoot> roots,
        IReadOnlyList<StructuralPayload> payloads)
    {
        var failures = new List<string>();
        for (var i = 0; i < Math.Min(roots.Count, payloads.Count); i++)
        {
            var root = roots[i];
            var payload = payloads[i];
            var nameOffset = root.XboxTypeName.ToLowerInvariant() switch
            {
                // Xbox T4 GfxImage is 0x28 bytes; name is its final 32-bit field at +0x24.
                "image" => 36,
                // T4 LocalizeEntry is { value*, name* }.
                "localize" => 4,
                // Every other asset family emitted by this diagnostic has its
                // canonical DB name pointer as the first 32-bit field.
                _ => 0
            };

            if (payload.Bytes.Length < nameOffset + 4)
            {
                failures.Add(
                    $"root#{i:N0} type={root.XboxTypeName} payload='{payload.Name}' is only {payload.Bytes.Length:N0} byte(s); " +
                    $"name field +0x{nameOffset:X} is outside the payload");
                continue;
            }

            var raw = BinaryPrimitives.ReadUInt32BigEndian(payload.Bytes.AsSpan(nameOffset, 4));
            if (raw == 0)
            {
                failures.Add(
                    $"root#{i:N0} type={root.XboxTypeName} payload='{payload.Name}' has NULL DB name pointer at +0x{nameOffset:X}");
                continue;
            }

            var decoded = T4ZonePointer.Decode(raw);
            if (decoded.Kind == T4ZonePointerKind.Offset && decoded.Block is null)
            {
                failures.Add(
                    $"root#{i:N0} type={root.XboxTypeName} payload='{payload.Name}' has undecodable DB name pointer " +
                    $"0x{raw:X8} at +0x{nameOffset:X}");
            }
        }
        return failures;
    }

    private static StructuralPayload BuildNativeSequenceSyntheticPayload(
        string typeName,
        string name,
        string canonicalBaseName)
    {
        var lower = typeName.ToLowerInvariant();
        byte[] bytes = lower switch
        {
            "xanim" => BuildFixedNameShell(92, name),
            "xmodel" => BuildFixedNameShell(228, name),
            "material" => BuildFixedNameShell(96, name),
            "pixelshader" => BuildFixedNameShell(16, name),
            "techset" => BuildFixedNameShell(216, name),
            "image" => BuildImageShell(name),
            "sound" => BuildSoundListEmpty(name),
            "loaded_sound" => BuildFixedNameShell(12, name),
            "weapon" => BuildFixedNameShell(0x9AC, name),
            "fx" => BuildFixedNameShell(36, name),
            "rawfile" => BuildRawFileEmpty(name),
            "physpreset" => BuildFixedNameShell(48, name),
            "clipmap_pvs" => BuildFixedNameShell(332, name),
            "comworld" => BuildFixedNameShell(64, name),
            "gameworld_sp" => BuildFixedNameShell(44, name),
            "gfxworld" => BuildGfxWorld(804, name, canonicalBaseName),
            "localize" => BuildLocalize(name, string.Empty),
            "map_ents" => BuildFixedNameShell(12, name),
            _ => throw new InvalidDataException(
                $"Native-sequence diagnostic has no safe synthetic shell for Xbox asset type '{typeName}'.")
        };

        return new StructuralPayload(
            typeName,
            name,
            bytes,
            true,
            "native-sequence synthetic dependency shell; donor payload bytes are never copied");
    }

    private static byte[] BuildSoundListEmpty(string name)
    {
        // Native-sequence diagnostic only: snd_alias_list_t is 12 bytes on T4's
        // 32-bit ABI: aliasName*, head*, count. Keep the canonical DB name owned
        // and make the list completely inert so no child alias traversal can
        // perturb this name/fixup isolation test.
        var n = Encoding.ASCII.GetBytes(name);
        var bytes = new byte[12 + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), T4ZonePointer.Following);
        // head = NULL, count = 0
        n.CopyTo(bytes.AsSpan(12));
        return bytes;
    }

    private static byte[] BuildImageShell(string name)
    {
        // Xbox T4 GfxImage is 0x28 / 40 bytes. Unlike most simple roots its
        // canonical DB name pointer is the final 32-bit field at +0x24.
        // texture/loadDef remains null; dimensions/category/flags remain zero.
        var n = Encoding.ASCII.GetBytes(name);
        var bytes = new byte[40 + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(36, 4), T4ZonePointer.Following);
        n.CopyTo(bytes.AsSpan(40));
        return bytes;
    }

    private static IReadOnlyList<StructuralPayload> BuildTrackedShellVariant(IReadOnlyList<StructuralPayload> payloads)
    {
        return payloads.Select(p =>
        {
            var fixedSize = p.TypeName.ToLowerInvariant() switch
            {
                "xanim" => 92,
                "xmodel" => 0xD4,
                "weapon" => 0x9AC,
                "fx" => 36,
                _ => 0
            };
            if (fixedSize == 0) return p;
            return new StructuralPayload(
                p.TypeName,
                p.Name,
                BuildFixedNameShell(fixedSize, p.Name),
                true,
                $"hardware-matrix tracked shell: {p.TypeName} owned children intentionally zeroed");
        }).ToList();
    }

    private static byte[] BuildFixedNameShell(int fixedSize, string name)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var bytes = new byte[fixedSize + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), T4ZonePointer.Following);
        n.CopyTo(bytes.AsSpan(fixedSize));
        return bytes;
    }

    private static byte[] BuildGfxWorld(int fixedSize, string name, string baseName)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var b = Encoding.ASCII.GetBytes(baseName);
        var bytes = new byte[fixedSize + n.Length + 1 + b.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), T4ZonePointer.Following);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), T4ZonePointer.Following);
        var cursor = fixedSize;
        n.CopyTo(bytes.AsSpan(cursor));
        cursor += n.Length + 1;
        b.CopyTo(bytes.AsSpan(cursor));
        return bytes;
    }

    private static byte[] BuildMenuList(string name)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var bytes = new byte[12 + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), T4ZonePointer.Following);
        // menuCount=0, menus=null
        n.CopyTo(bytes.AsSpan(12));
        return bytes;
    }

    private static byte[] BuildLightDef(string name)
    {
        // T4 GfxLightDef 32-bit root: name*, attenuation.image*, sampler/padding, lmapLookupStart.
        var n = Encoding.ASCII.GetBytes(name);
        var bytes = new byte[16 + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), T4ZonePointer.Following);
        // attenuation image pointer, sampler state/padding and lmap lookup are zero.
        n.CopyTo(bytes.AsSpan(16));
        return bytes;
    }

    private static byte[] BuildRawFileEmpty(string name)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var bytes = new byte[12 + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), T4ZonePointer.Following);
        n.CopyTo(bytes.AsSpan(12));
        return bytes;
    }

    private static byte[] BuildLocalize(string name, string value)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var v = Encoding.Latin1.GetBytes(value);
        var bytes = new byte[8 + v.Length + 1 + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), T4ZonePointer.Following);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), T4ZonePointer.Following);
        v.CopyTo(bytes.AsSpan(8));
        n.CopyTo(bytes.AsSpan(8 + v.Length + 1));
        return bytes;
    }

    private static byte[] BuildStringTableEmpty(string name)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var bytes = new byte[16 + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), T4ZonePointer.Following);
        // columnCount/rowCount/values are zero.
        n.CopyTo(bytes.AsSpan(16));
        return bytes;
    }

    private static async Task WriteScriptClosureReadmeAsync(
        string packageDir,
        string baseName,
        string sha,
        int roots,
        int residentMenusRemoved,
        int rawFilesPromoted,
        int? goldenExpectedRawPromotions,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — SCRIPT-CLOSURE HARDWARE MATRIX CANDIDATE");
        sb.AppendLine(new string('=', 82));
        sb.AppendLine($"FF: {baseName}.ff");
        sb.AppendLine($"SHA-256: {sha}");
        sb.AppendLine($"Xbox XAsset roots in this diagnostic: {roots:N0}");
        sb.AppendLine($"Resident UI MenuLists removed by exact identity: {residentMenusRemoved:N0}");
        sb.AppendLine($"Missing IWD .gsc/.csc RawFiles promoted by exact name+bytes: {rawFilesPromoted:N0}");
        if (goldenExpectedRawPromotions is not null)
            sb.AppendLine($"Known-working golden RawFile net delta (validation only): {goldenExpectedRawPromotions:+#;-#;0}");
        sb.AppendLine();
        sb.AppendLine("PURPOSE");
        sb.AppendLine("  This variant starts from tracked_shell and changes ONLY typed map-activation closure:");
        sb.AppendLine("  proven always-loaded resident UI MenuLists are omitted, and missing IWD scripts are emitted as owned RawFile roots.");
        sb.AppendLine("  It tests the SP TU7 DB/script lookup frontier at 0x82168594 / 0xC0000005.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT");
        sb.AppendLine("  This is NOT production graph convergence. Golden counts never force additions/removals.");
        sb.AppendLine("  A different/later hardware frontier proves this closure family matters; the final linker still needs exact identity closure.");
        await File.WriteAllTextAsync(
            Path.Combine(packageDir, "SCRIPT_CLOSURE_NATIVE_TEST.txt"),
            sb.ToString(),
            new UTF8Encoding(false),
            ct);
    }

    private static async Task WriteTrackedShellReadmeAsync(
        string packageDir,
        string baseName,
        string sha,
        int roots,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — TRACKED-SHELL HARDWARE MATRIX CANDIDATE");
        sb.AppendLine(new string('=', 82));
        sb.AppendLine($"FF: {baseName}.ff");
        sb.AppendLine($"SHA-256: {sha}");
        sb.AppendLine($"Xbox XAsset roots: {roots:N0}");
        sb.AppendLine();
        sb.AppendLine("A/B PURPOSE");
        sb.AppendLine("  This candidate is generated from the exact same Linker V2 root graph as the proof-heavy main FF.");
        sb.AppendLine("  Only XAnim/XModel/Weapon/FX differ: all four are reduced to named self-contained fixed shells.");
        sb.AppendLine("  World/material/sound/simple-root behavior is otherwise unchanged.");
        sb.AppendLine();
        sb.AppendLine("INTERPRETATION");
        sb.AppendLine("  - Same hardware fault as proof-heavy main => suspect world/material/simple-root initialization first.");
        sb.AppendLine("  - Different/later fault => promoted tracked payloads participate in the previous frontier.");
        sb.AppendLine("  - This is diagnostic only and never satisfies production fidelity.");
        await File.WriteAllTextAsync(
            Path.Combine(packageDir, "TRACKED_SHELL_NATIVE_TEST.txt"),
            sb.ToString(),
            new UTF8Encoding(false),
            ct);
    }

    private static async Task WriteReadmeAsync(string packageDir, string baseName, string sha, int roots, int fallback, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Linker V2 alpha 8 PROOF-PAYLOAD MAIN NATIVE STRUCTURAL TEST");
        sb.AppendLine(new string('=', 92));
        sb.AppendLine($"FF: {baseName}.ff");
        sb.AppendLine($"SHA-256: {sha}");
        sb.AppendLine($"Xbox XAsset roots: {roots:N0}");
        sb.AppendLine($"Explicitly reduced/fallback payloads: {fallback:N0}");
        sb.AppendLine();
        sb.AppendLine("THIS IS NOT A FULL CONVERSION OR STANDALONE MAP PACKAGE.");
        sb.AppendLine($"ONLY {baseName}.ff in this folder is the structural A/B candidate.");
        sb.AppendLine("It is a fail-closed loader/zone/XAsset structural diagnostic. Audio is silent; materials/shaders/world data and several unresolved simple assets are deliberately reduced.");
        sb.AppendLine();
        sb.AppendLine("GOLDEN A/B TEST:");
        sb.AppendLine($"  1. Start with the known-working Xbox {baseName} package.");
        sb.AppendLine($"  2. Back up its original {baseName}.ff.");
        sb.AppendLine($"  3. Replace ONLY {baseName}.ff with the candidate from this folder.");
        sb.AppendLine($"  4. Do NOT copy this folder as a map. Keep the known-good {baseName}_load.ff, sounds folder, previews, description, t4ff metadata, arena/IWD files and every other companion file untouched.");
        sb.AppendLine("  5. Record the exact screen/result and xbWatson output if available.");
        sb.AppendLine();
        sb.AppendLine("A loader success proves structural main-FF emission; it does NOT prove gameplay/fidelity readiness.");
        await File.WriteAllTextAsync(Path.Combine(packageDir, "STRUCTURAL_NATIVE_TEST.txt"), sb.ToString(), new UTF8Encoding(false), ct);
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(fs, ct)).ToLowerInvariant();
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }

    private static int AlignOffset(int offset, int alignment)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a positive power of two.");
        return checked((offset + alignment - 1) & ~(alignment - 1));
    }

    private static void AlignStream(Stream stream, int alignment)
    {
        var aligned = AlignOffset(checked((int)stream.Position), alignment);
        while (stream.Position < aligned)
            stream.WriteByte(0);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string Render(NormalMapLinkerV2StructuralMainEmitResult r, NormalMapLinkerV2DestinationBlockPlanResult blocks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 8 proof-payload structural main emitter");
        sb.AppendLine(new string('=', 104));
        sb.AppendLine($"Map: {r.MapName}; canonical={r.CanonicalBaseName}");
        sb.AppendLine($"candidateReady={r.StructuralNativeCandidateReady}; productionMainReady={r.ProductionMainReady}; verified={r.Verified}");
        sb.AppendLine($"assets={r.XboxAssets:N0}; scriptStrings={r.ScriptStrings:N0}; payloads={r.PayloadsBuilt:N0}; fallbackPayloads={r.FallbackPayloads:N0}; realPromotedPayloads={r.RealPromotedPayloads:N0}; smokePromotedPayloads={r.SmokePromotedPayloads:N0}; promotionRejected={r.PromotionRejectedPayloads:N0}; sourceAliasRoots={r.SourceAliasRoots:N0}");
        sb.AppendLine("realPromotedByType: " + string.Join(", ", TrackedTypes.Select(t => $"{t}={r.RealPromotedByType.GetValueOrDefault(t):N0}")));
        sb.AppendLine("smokePromotedByType: " + string.Join(", ", TrackedTypes.Select(t => $"{t}={r.SmokePromotedByType.GetValueOrDefault(t):N0}")));
        if (r.PromotionRejectReasons.Count != 0)
        {
            sb.AppendLine("PROMOTION REJECTION CENSUS");
            foreach (var reason in r.PromotionRejectReasons.Take(40))
                sb.AppendLine($"  {reason.Value,5:N0}  {reason.Key}");
        }
        sb.AppendLine($"zone={r.ZoneBytes:N0}B; headerZoneSize={r.HeaderZoneSize:N0}B; correctZoneSizeSemantics={r.CorrectZoneSizeSemantics}; perBlockHeader={r.PerBlockCapacityHeader}; readBackErrors={r.ReadBackErrors:N0}");
        sb.AppendLine($"ff={r.FastFileBytes:N0}B; wrapperRoundTrip={r.WrapperRoundTripVerified}; sha256={r.FastFileSha256 ?? "<none>"}");
        sb.AppendLine($"output={r.OutputFastFile ?? "<none>"}");
        sb.AppendLine($"trackedShellCandidate={r.ConservativeCandidateReady}; trackedShellReadBackErrors={r.ConservativeReadBackErrors:N0}; trackedShellRoundTrip={r.ConservativeWrapperRoundTripVerified}; trackedShellSha256={r.ConservativeFastFileSha256 ?? "<none>"}; trackedShellOutput={r.ConservativeOutputFastFile ?? "<none>"}");
        sb.AppendLine($"scriptClosureCandidate={r.ScriptClosureCandidateReady}; scriptClosureReadBackErrors={r.ScriptClosureReadBackErrors:N0}; scriptClosureRoundTrip={r.ScriptClosureWrapperRoundTripVerified}; residentMenusRemoved={r.ScriptClosureResidentMenusRemoved:N0}; iwdRawFilesPromoted={r.ScriptClosureRawFilesPromoted:N0}; scriptClosureSha256={r.ScriptClosureFastFileSha256 ?? "<none>"}; scriptClosureOutput={r.ScriptClosureOutputFastFile ?? "<none>"}");
        sb.AppendLine();
        sb.AppendLine("SOURCE-DERIVED BLOCK CAPACITIES");
        foreach (var block in Enum.GetValues<T4ZoneBlock>())
            sb.AppendLine($"  {block,-18} {blocks.StructuralCapacities.GetValueOrDefault(block.ToString()),14:N0} B");
        sb.AppendLine();
        sb.AppendLine("PAYLOAD ROOT COUNTS");
        foreach (var kv in r.PayloadsByType.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"  {kv.Key,-18} {kv.Value,8:N0}");
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        foreach (var blocker in r.Blockers) sb.AppendLine("BLOCKER: " + blocker);
        return sb.ToString();
    }
}
