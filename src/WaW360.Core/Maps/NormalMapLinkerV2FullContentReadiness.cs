using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record LinkerV2FullContentTypeMass(
    string TypeName,
    int NativeRoots,
    int SourceMappedRoots,
    int RealPromotedRoots,
    int FallbackRoots,
    int SyntheticRoots,
    long ShellBytes,
    long SourcePayloadBytes,
    long RealPromotedBytes,
    long FallbackBytes);

public sealed class NormalMapLinkerV2FullContentReadinessResult
{
    public required string MapName { get; init; }
    public required string CanonicalBaseName { get; init; }
    public required long NativeOracleFastFileBytes { get; init; }
    public required long NativeOracleZoneBytes { get; init; }
    public required long AllShellBaselineZoneBytes { get; init; }
    public required long ProvenRendererGainBytes { get; init; }
    public required long EstimatedFullContentZoneBytes { get; init; }
    public required double NativeSizeRatio { get; init; }
    public required double MinimumSizeRatio { get; init; }
    public required double MaximumSizeRatio { get; init; }
    public required int NativeRoots { get; init; }
    public required int SourceMappedRoots { get; init; }
    public required int RealPromotedRoots { get; init; }
    public required int FallbackRoots { get; init; }
    public required int SyntheticDependencyRoots { get; init; }
    public required int CriticalFallbackRoots { get; init; }
    public required int ExternalAudioPendingRoots { get; init; }
    public required long SourcePayloadBytes { get; init; }
    public required long RealPromotedBytes { get; init; }
    public required long FallbackBytes { get; init; }
    public required int ProductionImageRoots { get; init; }
    public required long ProductionImageSerializedBytes { get; init; }
    public required long ProductionImagePixelBytes { get; init; }
    public required int ProductionXModelRoots { get; init; }
    public required int ProductionXModelReferenceRoots { get; init; }
    public required long ProductionXModelSerializedBytes { get; init; }
    public required int ProductionMaterialRoots { get; init; }
    public required int ProductionMaterialReferenceRoots { get; init; }
    public required long ProductionMaterialSerializedBytes { get; init; }
    public required int ProductionCriticalReferenceRoots { get; init; }
    public required int ProductionImageReferenceRoots { get; init; }
    public required long ProductionIntegratedBaselineZoneBytes { get; init; }
    public required bool RendererLayoutReady { get; init; }
    public required bool ProperSizeGate { get; init; }
    public required bool CriticalContentGate { get; init; }
    public required bool ReadyForFullNativeEmission { get; init; }
    public required IReadOnlyList<LinkerV2FullContentTypeMass> Types { get; init; }
    public required IReadOnlyList<string> CriticalBlockers { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// NUCLEAR Stage 4F pre-emission gate.
///
/// 4E proved the renderer graph, but intentionally used name-only shells for every
/// non-renderer root. This stage measures the real source-owned payload mass that the
/// structural linker can currently preserve and combines it with the already-proven
/// 4E renderer graph mass.
///
/// The result is deliberately fail-closed: no hardware candidate is considered a
/// "proper native test" until the estimated uncompressed zone is close to the known
/// working FRUmansion Xbox oracle AND no critical source family is still falling back
/// to an inert shell.
///
/// This is a readiness gate, not the final byte emitter.
/// </summary>
public static class NormalMapLinkerV2FullContentReadiness
{
    private static readonly HashSet<string> RendererTypes =
        new(StringComparer.OrdinalIgnoreCase) { "techset", "techniqueset", "pixelshader" };

    private static readonly HashSet<string> CriticalTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "xanim",
            "xmodel",
            "material",
            "image",
            "clipmap_pvs",
            "clipmap",
            "comworld",
            "gameworld_sp",
            "gfxworld",
            "weapon",
            "fx",
            "rawfile"
        };

    public static async Task<NormalMapLinkerV2FullContentReadinessResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        string nativeFastFile,
        string graphRoot,
        string nativeIdentityManifest,
        string bindingManifest,
        double minimumSizeRatio = 0.80,
        double maximumSizeRatio = 1.20,
        string? productionImageGraftManifest = null,
        string? productionXModelGraftManifest = null,
        string? productionMaterialGraftManifest = null,
        string? productionCriticalReferenceProofManifest = null,
        string? productionImageReferenceProofManifest = null,
        CancellationToken cancellationToken = default)
    {
        if (minimumSizeRatio <= 0 || maximumSizeRatio <= minimumSizeRatio)
            throw new ArgumentOutOfRangeException(nameof(minimumSizeRatio));

        var source = await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var rootPolicy = await NormalMapLinkerV2CanonicalRootPolicy.RunAsync(
            map, outputRoot, source, cancellationToken: cancellationToken);

        if (!source.Verified)
            throw new InvalidDataException("Stage 4F requires a verified source archive.");
        if (!rootPolicy.Verified || !rootPolicy.ProvenanceIdentityComplete)
            throw new InvalidDataException("Stage 4F requires complete canonical source provenance.");

        var native = await FastFileAnalysisCache.GetZoneAsync(nativeFastFile, cancellationToken);
        if (native.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException("Stage 4F native oracle must be Xbox360.");

        var nativeIdentity = LoadNativeIdentity(nativeIdentityManifest);
        if (nativeIdentity.Count != native.Document.AssetCount)
            throw new InvalidDataException(
                $"Stage 4F native identity count={nativeIdentity.Count:N0} does not match XAssetList={native.Document.AssetCount:N0}.");

        var productionImages = LoadProductionImageCredit(
            productionImageGraftManifest,
            nativeIdentity,
            native.Document.AssetCount);

        var productionXModels = LoadProductionXModelCredit(
            productionXModelGraftManifest,
            nativeIdentity,
            native.Document.AssetCount);

        var productionMaterials = LoadProductionMaterialCredit(
            productionMaterialGraftManifest,
            nativeIdentity,
            native.Document.AssetCount);

        var productionCriticalReferences = LoadProductionCriticalReferenceCredit(
            productionCriticalReferenceProofManifest,
            nativeIdentity,
            native.Document.AssetCount);

        var productionImageReferences = LoadProductionImageReferenceCredit(
            productionImageReferenceProofManifest,
            nativeIdentity,
            native.Document.AssetCount);

        if (productionXModels.SerializedBytesByNativeIndex.Count != 0 &&
            productionXModels.IntegratedImageRoots != productionImages.SerializedBytesByNativeIndex.Count)
            throw new InvalidDataException(
                $"Stage 4Y integrated image roots={productionXModels.IntegratedImageRoots:N0} " +
                $"but production-image credit roots={productionImages.SerializedBytesByNativeIndex.Count:N0}.");

        if (productionMaterials.SerializedBytesByNativeIndex.Count != 0)
        {
            if (productionMaterials.IntegratedImageRoots != productionImages.SerializedBytesByNativeIndex.Count)
                throw new InvalidDataException(
                    $"Stage 4Z-D integrated image roots={productionMaterials.IntegratedImageRoots:N0} " +
                    $"but production-image credit roots={productionImages.SerializedBytesByNativeIndex.Count:N0}.");
            if (productionMaterials.IntegratedXModelRoots != productionXModels.SerializedBytesByNativeIndex.Count)
                throw new InvalidDataException(
                    $"Stage 4Z-D integrated XModel roots={productionMaterials.IntegratedXModelRoots:N0} " +
                    $"but Stage 4Y credit roots={productionXModels.SerializedBytesByNativeIndex.Count:N0}.");
        }

        var rendererLayout = await T4XenonGraphBlockLayoutPlanner.BuildAsync(
            graphRoot,
            nativeFastFile,
            nativeIdentityManifest,
            bindingManifest,
            Path.Combine(outputRoot, map.Name, "nuclear_4f_renderer_layout_recheck"),
            source.CanonicalBaseName,
            87,
            897,
            cancellationToken);
        if (!rendererLayout.LayoutReady)
            throw new InvalidDataException(
                "Stage 4F renderer layout recheck failed: " +
                string.Join(" | ", rendererLayout.Blockers));

        var weaponStrings = await LinkerV2IwdWeaponStringCatalog.BuildAsync(
            source.SourceArchive
                .Where(x => x.Role.Equals("iwd", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Path),
            cancellationToken);

        var primary = source.FastFiles
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .OrderBy(x => LinkerV2ProofEvidence.RoleRank(x.Role))
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var payloadsBySource = new Dictionary<string,
            IReadOnlyDictionary<int, NormalMapLinkerV2StructuralMainEmitter.StructuralPayload>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var container in primary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(container.Path, cancellationToken);
            payloadsBySource[container.Path] =
                await NormalMapLinkerV2StructuralMainEmitter.BuildSourcePayloadsAsync(
                    outputRoot,
                    map.Name,
                    source.CanonicalBaseName,
                    container.Role,
                    container.Path,
                    cached.Document,
                    cached.Bytes,
                    weaponStrings,
                    cancellationToken);
        }

        var sourcePairs = new List<(LinkerV2CanonicalRoot Root,
            NormalMapLinkerV2StructuralMainEmitter.StructuralPayload Payload)>();

        foreach (var root in rootPolicy.Roots.OrderBy(x => x.CanonicalSourceOrdinal))
        {
            if (!payloadsBySource.TryGetValue(root.SourceFastFile, out var table) ||
                !table.TryGetValue(root.SourceAssetIndex, out var payload))
            {
                throw new InvalidDataException(
                    $"Stage 4F has no structural payload for source root {root.IdentityKey}.");
            }
            sourcePairs.Add((root, payload));
        }

        var pools = sourcePairs
            .GroupBy(x => x.Root.XboxTypeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new Queue<(LinkerV2CanonicalRoot Root,
                    NormalMapLinkerV2StructuralMainEmitter.StructuralPayload Payload)>(g),
                StringComparer.OrdinalIgnoreCase);

        var typeStats = new Dictionary<string, MutableTypeMass>(StringComparer.OrdinalIgnoreCase);

        long shellPayloadBytes = 0;
        long sourcePayloadBytes = 0;
        long realPromotedBytes = 0;
        long fallbackBytes = 0;
        var sourceMappedRoots = 0;
        var realPromotedRoots = 0;
        var fallbackRoots = 0;
        var syntheticRoots = 0;
        var criticalFallbackRoots = 0;
        var externalAudioPendingRoots = 0;
        var criticalBlockers = new List<string>();

        for (var i = 0; i < native.Document.Assets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nativeRoot = native.Document.Assets[i];
            var type = nativeRoot.TypeName;
            var name = nativeIdentity[i].Name;

            var shell = NormalMapLinkerV2StructuralMainEmitter
                .BuildNativeSequenceSyntheticPayload(type, name, source.CanonicalBaseName);
            shellPayloadBytes += shell.Bytes.LongLength;

            var stat = GetStat(typeStats, type);
            stat.NativeRoots++;
            stat.ShellBytes += shell.Bytes.LongLength;

            // Renderer mass is already represented by the proven 4E layout.
            if (RendererTypes.Contains(type))
            {
                if (pools.TryGetValue(type, out var rendererQueue) && rendererQueue.Count != 0)
                    rendererQueue.Dequeue();
                continue;
            }

            // Stage 4Z-D production Materials are exact native-root credits from
            // legitimate PC semantic JSON reconstructed into Xenon Material graphs.
            // The remaining native Material roots are intentional OAT reference/name shells.
            if (type.Equals("material", StringComparison.OrdinalIgnoreCase) &&
                productionMaterials.ReferenceNativeIndices.Contains(i))
            {
                realPromotedRoots++;
                stat.RealPromotedRoots++;
                continue;
            }

            if (type.Equals("material", StringComparison.OrdinalIgnoreCase) &&
                productionMaterials.SerializedBytesByNativeIndex.TryGetValue(i, out var productionMaterialBytes))
            {
                if (pools.TryGetValue(type, out var materialQueue) && materialQueue.Count != 0)
                    materialQueue.Dequeue();

                sourceMappedRoots++;
                realPromotedRoots++;
                sourcePayloadBytes += productionMaterialBytes;
                realPromotedBytes += productionMaterialBytes;
                stat.SourceMappedRoots++;
                stat.RealPromotedRoots++;
                stat.SourcePayloadBytes += productionMaterialBytes;
                stat.RealPromotedBytes += productionMaterialBytes;
                continue;
            }

            // The 9 native XModel reference roots are intentional reference assets,
            // not missing payloads. Stage 4Y preserved and OAT validated them. Mark
            // them resolved without consuming one of the 280 PC-owned model payloads.
            if (type.Equals("xmodel", StringComparison.OrdinalIgnoreCase) &&
                productionXModels.ReferenceNativeIndices.Contains(i))
            {
                realPromotedRoots++;
                stat.RealPromotedRoots++;
                continue;
            }

            // Stage 4Y production XModels are exact native-root credits backed by
            // PC XModels reconstructed by the T4 Xenon loader and replayed through
            // our serializer. Credit only the 280 owned roots proven by the clean
            // integrated 6,811-root OAT hard gate; the 9 reference roots remain
            // reference/shell identities and continue through ordinary accounting.
            if (type.Equals("xmodel", StringComparison.OrdinalIgnoreCase) &&
                productionXModels.SerializedBytesByNativeIndex.TryGetValue(i, out var productionXModelBytes))
            {
                if (pools.TryGetValue(type, out var xmodelQueue) && xmodelQueue.Count != 0)
                    xmodelQueue.Dequeue();

                sourceMappedRoots++;
                realPromotedRoots++;
                sourcePayloadBytes += productionXModelBytes;
                realPromotedBytes += productionXModelBytes;
                stat.SourceMappedRoots++;
                stat.RealPromotedRoots++;
                stat.SourcePayloadBytes += productionXModelBytes;
                stat.RealPromotedBytes += productionXModelBytes;
                continue;
            }

            // Production images are exact native-root credits backed by verified multi-source
            // Xenon graphs. Credit only roots whose image graph and full 6,811-root 4U
            // replay both validate; unresolved image roots continue
            // through the ordinary fallback/synthetic accounting below.
            if (type.Equals("image", StringComparison.OrdinalIgnoreCase) &&
                productionImages.SerializedBytesByNativeIndex.TryGetValue(i, out var productionImageBytes))
            {
                if (pools.TryGetValue(type, out var imageQueue) && imageQueue.Count != 0)
                    imageQueue.Dequeue();

                sourceMappedRoots++;
                realPromotedRoots++;
                sourcePayloadBytes += productionImageBytes;
                realPromotedBytes += productionImageBytes;
                stat.SourceMappedRoots++;
                stat.RealPromotedRoots++;
                stat.SourcePayloadBytes += productionImageBytes;
                stat.RealPromotedBytes += productionImageBytes;
                continue;
            }

            // Stage 4W proves these exact native FX/Weapon/RawFile roots resolve as
            // external references through the retail Xbox common/base-Zombies pools.
            // Consume the legacy type-order source slot if one exists so subsequent
            // source/native pairing remains aligned, but do not charge embedded
            // payload bytes: the production representation is the reference itself.
            if (productionCriticalReferences.ReferenceNativeIndices.Contains(i))
            {
                if (pools.TryGetValue(type, out var referenceQueue) && referenceQueue.Count != 0)
                    referenceQueue.Dequeue();

                realPromotedRoots++;
                stat.RealPromotedRoots++;
                continue;
            }

            // Stage 4X proves these unresolved stock image identities are resident
            // in retail Xbox common/base-Zombies pools and survive exact OAT
            // reference replay. They are resolved references, not missing payloads.
            if (productionImageReferences.ReferenceNativeIndices.Contains(i))
            {
                if (pools.TryGetValue(type, out var imageReferenceQueue) && imageReferenceQueue.Count != 0)
                    imageReferenceQueue.Dequeue();

                realPromotedRoots++;
                stat.RealPromotedRoots++;
                continue;
            }

            if (pools.TryGetValue(type, out var queue) && queue.Count != 0)
            {
                var pair = queue.Dequeue();
                var payload = pair.Payload;
                sourceMappedRoots++;
                sourcePayloadBytes += payload.Bytes.LongLength;
                stat.SourceMappedRoots++;
                stat.SourcePayloadBytes += payload.Bytes.LongLength;

                if (payload.Fallback)
                {
                    fallbackRoots++;
                    fallbackBytes += payload.Bytes.LongLength;
                    stat.FallbackRoots++;
                    stat.FallbackBytes += payload.Bytes.LongLength;

                    if (type.Equals("sound", StringComparison.OrdinalIgnoreCase) ||
                        type.Equals("loaded_sound", StringComparison.OrdinalIgnoreCase))
                    {
                        externalAudioPendingRoots++;
                    }
                    else if (CriticalTypes.Contains(type))
                    {
                        criticalFallbackRoots++;
                        criticalBlockers.Add(
                            $"critical fallback {type}: '{payload.Name}' — {payload.Note}");
                    }
                }
                else
                {
                    realPromotedRoots++;
                    realPromotedBytes += payload.Bytes.LongLength;
                    stat.RealPromotedRoots++;
                    stat.RealPromotedBytes += payload.Bytes.LongLength;
                }
            }
            else
            {
                syntheticRoots++;
                stat.SyntheticRoots++;

                if (type.Equals("sound", StringComparison.OrdinalIgnoreCase) ||
                    type.Equals("loaded_sound", StringComparison.OrdinalIgnoreCase))
                {
                    externalAudioPendingRoots++;
                }
                else if (CriticalTypes.Contains(type))
                {
                    criticalFallbackRoots++;
                    criticalBlockers.Add(
                        $"critical synthetic dependency {type}: native root #{i} '{name}' has no source-owned payload.");
                }
            }
        }

        var scriptTableSerializedBytes =
            native.Document.ScriptStrings.Count * 4L +
            native.Document.ScriptStrings
                .Where(x => x.RawPointer != 0)
                .Sum(x => Encoding.UTF8.GetByteCount(x.Value ?? string.Empty) + 1L);

        var allShellBaseline =
            36L + // XFile header
            16L + // XAssetList fixed body
            scriptTableSerializedBytes +
            native.Document.AssetCount * 8L +
            shellPayloadBytes;

        var rendererGain =
            rendererLayout.ProjectedUncompressedZoneBytes - allShellBaseline;

        if (rendererGain < 0)
            throw new InvalidDataException(
                $"Stage 4F renderer gain became negative ({rendererGain:N0} bytes); " +
                "shell baseline no longer matches the 4E traversal contract.");

        // Replace every mapped non-renderer shell with its current source-owned payload.
        long nonRendererDelta = 0;
        foreach (var pair in typeStats.Values)
        {
            if (RendererTypes.Contains(pair.TypeName))
                continue;

            // For mapped roots, source payload bytes replace the corresponding shell bytes.
            // Exact per-root shell deltas are tracked below in a second deterministic walk.
        }

        // Deterministic second walk computes exact source-vs-shell delta by root.
        var deltaPools = sourcePairs
            .GroupBy(x => x.Root.XboxTypeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new Queue<(LinkerV2CanonicalRoot Root,
                    NormalMapLinkerV2StructuralMainEmitter.StructuralPayload Payload)>(g),
                StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < native.Document.Assets.Count; i++)
        {
            var nativeRoot = native.Document.Assets[i];
            var type = nativeRoot.TypeName;
            var shell = NormalMapLinkerV2StructuralMainEmitter
                .BuildNativeSequenceSyntheticPayload(
                    type,
                    nativeIdentity[i].Name,
                    source.CanonicalBaseName);

            if (RendererTypes.Contains(type))
            {
                if (deltaPools.TryGetValue(type, out var rendererQueue) && rendererQueue.Count != 0)
                    rendererQueue.Dequeue();
                continue;
            }

            if (type.Equals("material", StringComparison.OrdinalIgnoreCase) &&
                productionMaterials.ReferenceNativeIndices.Contains(i))
            {
                // Stage 4Z-D baseline already contains this validated Material reference root.
                continue;
            }

            if (type.Equals("material", StringComparison.OrdinalIgnoreCase) &&
                productionMaterials.SerializedBytesByNativeIndex.ContainsKey(i))
            {
                if (deltaPools.TryGetValue(type, out var materialQueue) && materialQueue.Count != 0)
                    materialQueue.Dequeue();

                // Stage 4Z-D baseline already contains this exact production Material.
                continue;
            }

            if (type.Equals("xmodel", StringComparison.OrdinalIgnoreCase) &&
                productionXModels.ReferenceNativeIndices.Contains(i))
            {
                // Stage 4Y baseline already contains this validated reference root.
                continue;
            }

            if (type.Equals("xmodel", StringComparison.OrdinalIgnoreCase) &&
                productionXModels.SerializedBytesByNativeIndex.ContainsKey(i))
            {
                if (deltaPools.TryGetValue(type, out var xmodelQueue) && xmodelQueue.Count != 0)
                    xmodelQueue.Dequeue();

                // Stage 4Y baseline already contains this exact production XModel.
                continue;
            }
            if (type.Equals("image", StringComparison.OrdinalIgnoreCase) &&
                productionImages.SerializedBytesByNativeIndex.TryGetValue(i, out var productionImageBytes))
            {
                if (deltaPools.TryGetValue(type, out var imageQueue) && imageQueue.Count != 0)
                    imageQueue.Dequeue();
                if (productionXModels.IntegratedZoneBytes == 0)
                    nonRendererDelta += productionImageBytes - shell.Bytes.LongLength;
                // A passing 4Y baseline already includes the exact production image roots.
                continue;
            }

            if (productionCriticalReferences.ReferenceNativeIndices.Contains(i))
            {
                if (deltaPools.TryGetValue(type, out var referenceQueue) && referenceQueue.Count != 0)
                    referenceQueue.Dequeue();

                // The integrated baseline already carries a name/reference-sized shell
                // for this native root. Stage 4W proves that shell can remain an
                // external retail reference, so no source-payload delta is applied.
                continue;
            }

            if (productionImageReferences.ReferenceNativeIndices.Contains(i))
            {
                if (deltaPools.TryGetValue(type, out var imageReferenceQueue) && imageReferenceQueue.Count != 0)
                    imageReferenceQueue.Dequeue();

                // Stage 4X proves this image root can remain a retail runtime
                // reference. The existing integrated shell/reference mass is kept.
                continue;
            }

            if (deltaPools.TryGetValue(type, out var queue) && queue.Count != 0)
            {
                var payload = queue.Dequeue().Payload;
                nonRendererDelta += payload.Bytes.LongLength - shell.Bytes.LongLength;
            }
        }

        var provenIntegratedBaseZoneBytes =
            productionMaterials.IntegratedZoneBytes != 0
                ? productionMaterials.IntegratedZoneBytes
                : productionXModels.IntegratedZoneBytes != 0
                    ? productionXModels.IntegratedZoneBytes
                    : rendererLayout.ProjectedUncompressedZoneBytes;

        var estimatedFullContent =
            checked(provenIntegratedBaseZoneBytes + nonRendererDelta);

        var nativeFastFileBytes = new FileInfo(nativeFastFile).Length;
        var nativeZoneBytes = native.Bytes.LongLength;
        var ratio = nativeZoneBytes == 0
            ? 0.0
            : estimatedFullContent / (double)nativeZoneBytes;

        var properSizeGate =
            ratio >= minimumSizeRatio &&
            ratio <= maximumSizeRatio;

        var criticalContentGate = criticalFallbackRoots == 0;
        var ready = rendererLayout.LayoutReady && properSizeGate && criticalContentGate;

        var notes = new List<string>
        {
            "4F does not emit a hardware candidate. It is a mandatory pre-hardware mass/fidelity gate.",
            productionMaterials.IntegratedZoneBytes != 0
                ? "EstimatedFullContentZoneBytes starts from the exact OAT-clean Stage 4Z-D integrated renderer + production-image + 280-XModel + 502-Material zone, then replaces only the remaining shell families with current source-owned structural payloads."
                : productionXModels.IntegratedZoneBytes != 0
                    ? "EstimatedFullContentZoneBytes starts from the exact OAT-clean Stage 4Y integrated renderer + production-image + 280-XModel zone, then replaces only the remaining shell families with current source-owned structural payloads."
                    : "EstimatedFullContentZoneBytes starts from the exact 4E renderer-layout zone and replaces each mapped non-renderer name shell with the current source-owned structural payload.",
            $"Known-working main FF on disk is {nativeFastFileBytes:N0} bytes; decompressed zone is {nativeZoneBytes:N0} bytes. Pre-emission mass estimates compare decompressed zone bytes because compression ratio is not known until a candidate is actually emitted.",
            "External Xbox SDNS/XMA payloads belong in sounds\\ and are not expected to account for main-FF size. Sound/LoadedSound incompleteness is reported separately from the main-content size gate.",
            $"A proper-size candidate must be between {minimumSizeRatio:P0} and {maximumSizeRatio:P0} of the native oracle before hardware testing is allowed.",
            "Critical families must have zero fallback/synthetic roots before Stage 4F can declare full native emission ready.",
            productionImages.SerializedBytesByNativeIndex.Count == 0
                ? "No production-image graft manifest was supplied; image readiness is still evaluated only from the structural source path."
                : $"Production image graft credits {productionImages.SerializedBytesByNativeIndex.Count:N0} exact Xenon image roots ({productionImages.TotalPixelBytes:N0} pixel bytes; {productionImages.TotalSerializedBytes:N0} serialized bytes). Unresolved images remain critical blockers.",
            productionXModels.SerializedBytesByNativeIndex.Count == 0
                ? "No passing Stage 4Y XModel production graft was supplied; XModel readiness is still evaluated from the legacy structural source path."
                : $"Stage 4Y resolves all {productionXModels.SerializedBytesByNativeIndex.Count + productionXModels.ReferenceRoots:N0} native XModel roots: {productionXModels.SerializedBytesByNativeIndex.Count:N0} exact PC-backed Xenon payloads + {productionXModels.ReferenceRoots:N0} OAT-validated reference roots.",
            productionMaterials.SerializedBytesByNativeIndex.Count == 0
                ? "No passing Stage 4Z-D Material production graft was supplied; Material readiness is still evaluated from the legacy structural source path."
                : $"Stage 4Z-D resolves all {productionMaterials.SerializedBytesByNativeIndex.Count + productionMaterials.ReferenceRoots:N0} native Material roots: {productionMaterials.SerializedBytesByNativeIndex.Count:N0} exact PC-backed Xenon payloads + {productionMaterials.ReferenceRoots:N0} OAT-validated reference roots, and supplies an OAT-clean {productionMaterials.IntegratedZoneBytes:N0}-byte integrated baseline.",
            productionCriticalReferences.ReferenceNativeIndices.Count == 0
                ? "No passing Stage 4W retail critical-reference proof was supplied; FX/Weapon/RawFile readiness still follows the legacy structural source path."
                : $"Stage 4W credits {productionCriticalReferences.ReferenceNativeIndices.Count:N0} exact retail-Xbox runtime references: {productionCriticalReferences.FxRoots:N0} FX + {productionCriticalReferences.WeaponRoots:N0} Weapon + {productionCriticalReferences.RawFileRoots:N0} RawFile.",
            productionImageReferences.ReferenceNativeIndices.Count == 0
                ? "No passing Stage 4X retail image-reference proof was supplied; unresolved stock image identities remain critical blockers."
                : $"Stage 4X credits {productionImageReferences.ReferenceNativeIndices.Count:N0} exact retail-Xbox image runtime references from common/base-Zombies pools.",
            ready
                ? "FULL-CONTENT READINESS: PASS. The next stage may emit the integrated full-size candidate."
                : "FULL-CONTENT READINESS: BLOCKED. Promote the largest/critical fallback families before emitting a hardware test."
        };

        var types = typeStats.Values
            .OrderByDescending(x => x.FallbackRoots + x.SyntheticRoots)
            .ThenByDescending(x => x.SourcePayloadBytes)
            .ThenBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.ToImmutable())
            .ToList();

        var outDir = Path.Combine(outputRoot, map.Name, "nuclear_4f_full_content_readiness");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, "nuclear_4f_full_content_readiness.txt");
        var json = Path.Combine(outDir, "nuclear_4f_full_content_readiness.json");

        var result = new NormalMapLinkerV2FullContentReadinessResult
        {
            MapName = map.Name,
            CanonicalBaseName = source.CanonicalBaseName,
            NativeOracleFastFileBytes = nativeFastFileBytes,
            NativeOracleZoneBytes = nativeZoneBytes,
            AllShellBaselineZoneBytes = allShellBaseline,
            ProvenRendererGainBytes = rendererGain,
            EstimatedFullContentZoneBytes = estimatedFullContent,
            NativeSizeRatio = ratio,
            MinimumSizeRatio = minimumSizeRatio,
            MaximumSizeRatio = maximumSizeRatio,
            NativeRoots = native.Document.AssetCount,
            SourceMappedRoots = sourceMappedRoots,
            RealPromotedRoots = realPromotedRoots,
            FallbackRoots = fallbackRoots,
            SyntheticDependencyRoots = syntheticRoots,
            CriticalFallbackRoots = criticalFallbackRoots,
            ExternalAudioPendingRoots = externalAudioPendingRoots,
            SourcePayloadBytes = sourcePayloadBytes,
            RealPromotedBytes = realPromotedBytes,
            FallbackBytes = fallbackBytes,
            ProductionImageRoots = productionImages.SerializedBytesByNativeIndex.Count,
            ProductionImageSerializedBytes = productionImages.TotalSerializedBytes,
            ProductionImagePixelBytes = productionImages.TotalPixelBytes,
            ProductionXModelRoots = productionXModels.SerializedBytesByNativeIndex.Count,
            ProductionXModelReferenceRoots = productionXModels.ReferenceRoots,
            ProductionXModelSerializedBytes = productionXModels.TotalSerializedBytes,
            ProductionMaterialRoots = productionMaterials.SerializedBytesByNativeIndex.Count,
            ProductionMaterialReferenceRoots = productionMaterials.ReferenceRoots,
            ProductionMaterialSerializedBytes = productionMaterials.TotalSerializedBytes,
            ProductionCriticalReferenceRoots = productionCriticalReferences.ReferenceNativeIndices.Count,
            ProductionImageReferenceRoots = productionImageReferences.ReferenceNativeIndices.Count,
            ProductionIntegratedBaselineZoneBytes = provenIntegratedBaseZoneBytes,
            RendererLayoutReady = rendererLayout.LayoutReady,
            ProperSizeGate = properSizeGate,
            CriticalContentGate = criticalContentGate,
            ReadyForFullNativeEmission = ready,
            Types = types,
            CriticalBlockers = criticalBlockers,
            Notes = notes,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(
            json,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        return result;
    }

    private sealed record ProductionImageReferenceCredit(
        IReadOnlySet<int> ReferenceNativeIndices);

    private static ProductionImageReferenceCredit LoadProductionImageReferenceCredit(
        string? proofManifestPath,
        IReadOnlyList<T4XenonNativeIdentityEntry> nativeIdentity,
        int nativeRootCount)
    {
        if (string.IsNullOrWhiteSpace(proofManifestPath))
            return new ProductionImageReferenceCredit(new HashSet<int>());

        proofManifestPath = Path.GetFullPath(proofManifestPath);
        if (!File.Exists(proofManifestPath))
            throw new FileNotFoundException(
                "Stage 4F image-reference proof manifest was not found.",
                proofManifestPath);

        using var proofDoc = JsonDocument.Parse(File.ReadAllText(proofManifestPath));
        var proof = proofDoc.RootElement;

        if (GetString(proof, "format") != "WaW360.Nuclear4X.XboxImageRuntimeReferenceProof.v1" ||
            !GetBool(proof, "referenceProofPass") ||
            GetInt(proof, "runtimeReferenceCandidates") != 7 ||
            GetInt(proof, "payloadRequiredStockImages") != 0 ||
            GetInt(proof, "resolvedReferences") != 7 ||
            GetInt(proof, "missingReferences") != 0 ||
            GetInt(proof, "linkerExitCode") != 0 ||
            GetInt(proof, "unlinkerExitCode") != 0)
            throw new InvalidDataException(
                "Stage 4X manifest does not prove a clean 7-root retail Xbox image-reference replay.");

        var indices = new HashSet<int>();
        foreach (var item in GetProperty(proof, "resolved").EnumerateArray())
        {
            var index = GetInt(item, "Index");
            var name = GetString(item, "Name");

            if ((uint)index >= (uint)nativeRootCount)
                throw new InvalidDataException(
                    $"Stage 4X image reference root #{index} is outside the native sequence.");

            var nativeRoot = nativeIdentity[index];
            var nativeType = nativeRoot.OatTypeName.Trim().ToLowerInvariant();
            if (nativeType.Length == 0)
                nativeType = nativeRoot.TypeName.Trim().ToLowerInvariant();

            if (!nativeType.Equals("image", StringComparison.OrdinalIgnoreCase) ||
                !nativeRoot.Name.Equals(name, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Stage 4X image reference root #{index} no longer matches native image identity.");

            if (!indices.Add(index))
                throw new InvalidDataException(
                    $"Stage 4X repeats native image reference root #{index}.");
        }

        if (indices.Count != 7)
            throw new InvalidDataException(
                $"Stage 4X reference count changed: {indices.Count:N0}; expected 7.");

        return new ProductionImageReferenceCredit(indices);
    }

    private sealed record ProductionCriticalReferenceCredit(
        IReadOnlySet<int> ReferenceNativeIndices,
        int FxRoots,
        int WeaponRoots,
        int RawFileRoots);

    private static ProductionCriticalReferenceCredit LoadProductionCriticalReferenceCredit(
        string? proofManifestPath,
        IReadOnlyList<T4XenonNativeIdentityEntry> nativeIdentity,
        int nativeRootCount)
    {
        if (string.IsNullOrWhiteSpace(proofManifestPath))
            return new ProductionCriticalReferenceCredit(
                new HashSet<int>(),
                0,
                0,
                0);

        proofManifestPath = Path.GetFullPath(proofManifestPath);
        if (!File.Exists(proofManifestPath))
            throw new FileNotFoundException(
                "Stage 4F critical-reference proof manifest was not found.",
                proofManifestPath);

        using var proofDoc = JsonDocument.Parse(File.ReadAllText(proofManifestPath));
        var proof = proofDoc.RootElement;

        if (GetString(proof, "format") != "WaW360.Nuclear4W.XboxRuntimeReferenceProof.v1" ||
            !GetBool(proof, "referenceProofPass") ||
            GetInt(proof, "referenceCandidates") != 200 ||
            GetInt(proof, "resolvedReferences") != 200 ||
            GetInt(proof, "missingReferences") != 0 ||
            GetInt(proof, "linkerExitCode") != 0 ||
            GetInt(proof, "unlinkerExitCode") != 0)
            throw new InvalidDataException(
                "Stage 4W manifest does not prove a clean 200-root retail Xbox runtime-reference replay.");

        var indices = new HashSet<int>();
        var fx = 0;
        var weapon = 0;
        var rawfile = 0;

        foreach (var item in GetProperty(proof, "resolved").EnumerateArray())
        {
            var index = GetInt(item, "Index");
            var type = GetString(item, "Type").Trim().ToLowerInvariant();
            var name = GetString(item, "Name");

            if ((uint)index >= (uint)nativeRootCount)
                throw new InvalidDataException(
                    $"Stage 4W reference root #{index} is outside the native sequence.");

            var nativeRoot = nativeIdentity[index];
            var nativeType = nativeRoot.OatTypeName.Trim().ToLowerInvariant();
            if (nativeType.Length == 0)
                nativeType = nativeRoot.TypeName.Trim().ToLowerInvariant();

            if (!nativeType.Equals(type, StringComparison.OrdinalIgnoreCase) ||
                !nativeRoot.Name.Equals(name, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Stage 4W reference root #{index} identity/type no longer matches the native sequence.");

            if (!indices.Add(index))
                throw new InvalidDataException(
                    $"Stage 4W repeats native reference root #{index}.");

            switch (type)
            {
                case "fx":
                    fx++;
                    break;
                case "weapon":
                    weapon++;
                    break;
                case "rawfile":
                    rawfile++;
                    break;
                default:
                    throw new InvalidDataException(
                        $"Stage 4W contains unsupported critical reference type '{type}' at root #{index}.");
            }
        }

        if (indices.Count != 200 || fx != 110 || weapon != 44 || rawfile != 46)
            throw new InvalidDataException(
                $"Stage 4W reference census changed: total={indices.Count:N0}, FX={fx:N0}, Weapon={weapon:N0}, RawFile={rawfile:N0}; expected 200 / 110 / 44 / 46.");

        return new ProductionCriticalReferenceCredit(indices, fx, weapon, rawfile);
    }

    private sealed record ProductionMaterialCredit(
        IReadOnlyDictionary<int, long> SerializedBytesByNativeIndex,
        IReadOnlySet<int> ReferenceNativeIndices,
        long TotalSerializedBytes,
        int ReferenceRoots,
        int IntegratedImageRoots,
        int IntegratedXModelRoots,
        long IntegratedZoneBytes);

    private static ProductionMaterialCredit LoadProductionMaterialCredit(
        string? graftManifestPath,
        IReadOnlyList<T4XenonNativeIdentityEntry> nativeIdentity,
        int nativeRootCount)
    {
        if (string.IsNullOrWhiteSpace(graftManifestPath))
            return new ProductionMaterialCredit(
                new Dictionary<int, long>(),
                new HashSet<int>(),
                0,
                0,
                0,
                0,
                0);

        graftManifestPath = Path.GetFullPath(graftManifestPath);
        if (!File.Exists(graftManifestPath))
            throw new FileNotFoundException(
                "Stage 4F production-Material graft manifest was not found.",
                graftManifestPath);

        using var graftDoc = JsonDocument.Parse(File.ReadAllText(graftManifestPath));
        var graft = graftDoc.RootElement;

        if (GetString(graft, "format") != "WaW360.Nuclear4ZD.MaterialProductionGraft.v1" ||
            !GetBool(graft, "productionMaterialGraftPass"))
            throw new InvalidDataException(
                "Stage 4F requires a passing WaW360.Nuclear4ZD.MaterialProductionGraft.v1 manifest.");

        if (GetInt(graft, "nativeRoots") != nativeRootCount ||
            GetInt(graft, "oatListedRoots") != nativeRootCount ||
            GetInt(graft, "pcBackedMaterials") != 502 ||
            GetInt(graft, "materialReferenceRoots") != 88 ||
            GetInt(graft, "pcBackedOwnedXModels") != 280 ||
            !GetBool(graft, "exactIdentitySequence") ||
            !GetBool(graft, "oatZeroWarningsZeroErrors") ||
            !GetBool(graft, "layoutReplayExact"))
            throw new InvalidDataException(
                "Stage 4Z-D manifest does not prove an exact clean 502-Material / full native-root replay.");

        var emitterManifestPath = GetString(graft, "emitterManifest");
        var materialGraphManifestPath = GetString(graft, "materialGraphManifest");
        var bindingManifestPath = GetString(graft, "materialBindingManifest");

        if (string.IsNullOrWhiteSpace(emitterManifestPath) ||
            string.IsNullOrWhiteSpace(materialGraphManifestPath) ||
            string.IsNullOrWhiteSpace(bindingManifestPath))
            throw new InvalidDataException(
                "Stage 4Z-D manifest is missing Material/emitter provenance.");

        string ResolvePath(string path) =>
            Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(graftManifestPath)!, path));

        emitterManifestPath = ResolvePath(emitterManifestPath);
        materialGraphManifestPath = ResolvePath(materialGraphManifestPath);
        bindingManifestPath = ResolvePath(bindingManifestPath);

        if (!File.Exists(emitterManifestPath))
            throw new FileNotFoundException(
                "Stage 4Z-D emitter manifest was not found.",
                emitterManifestPath);
        if (!File.Exists(materialGraphManifestPath))
            throw new FileNotFoundException(
                "Stage 4Z-D Material graph manifest was not found.",
                materialGraphManifestPath);
        if (!File.Exists(bindingManifestPath))
            throw new FileNotFoundException(
                "Stage 4Z-D Material binding manifest was not found.",
                bindingManifestPath);

        using var emitterDoc = JsonDocument.Parse(File.ReadAllText(emitterManifestPath));
        var emitter = emitterDoc.RootElement;
        if (GetInt(emitter, "NativeRoots") != nativeRootCount ||
            GetInt(emitter, "OatListedRoots") != nativeRootCount ||
            GetInt(emitter, "RealMaterialRoots") != 502 ||
            GetInt(emitter, "MaterialReferenceRoots") != 88 ||
            GetInt(emitter, "RealXModelRoots") != 280 ||
            !GetBool(emitter, "ExactIdentitySequence") ||
            !GetBool(emitter, "OatZeroWarningsZeroErrors") ||
            !GetBool(emitter, "LayoutReplayExact") ||
            !GetBool(emitter, "ValidationPass"))
            throw new InvalidDataException(
                "Stage 4Z-D emitter manifest no longer proves a clean full-root OAT replay.");

        var integratedZoneBytes = GetLong(emitter, "ZoneBytes");
        var integratedImageRoots = GetInt(emitter, "RealImageRoots");
        var integratedXModelRoots = GetInt(emitter, "RealXModelRoots");
        if (integratedZoneBytes <= 0)
            throw new InvalidDataException(
                "Stage 4Z-D emitter reports a non-positive integrated zone size.");

        var graphRoot = Path.GetDirectoryName(materialGraphManifestPath)
            ?? throw new InvalidDataException(
                "Stage 4Z-D Material graph manifest has no parent directory.");

        var corpus = T4XenonMaterialCorpus.Load(
            graphRoot,
            bindingManifestPath,
            nativeIdentity,
            expectedOwned: 502);

        var dependencyErrors = corpus.ValidateDependencies(nativeIdentity);
        if (dependencyErrors.Count != 0)
            throw new InvalidDataException(
                "Stage 4Z-D Material dependency provenance no longer resolves against the native sequence: " +
                string.Join(" | ", dependencyErrors.Take(20)));

        var byIndex = new Dictionary<int, long>();
        long totalSerialized = 0;

        foreach (var pair in corpus.OwnedByRoot.OrderBy(x => x.Key))
        {
            var rootIndex = pair.Key;
            var entry = pair.Value;
            long serialized = 0;

            T4XenonMaterialGraphCodec.Plan(
                entry,
                nativeIdentity,
                (size, _, isSerialized) =>
                {
                    if (isSerialized)
                        serialized = checked(serialized + size);
                },
                size => serialized = checked(serialized + size));

            if (serialized <= 0)
                throw new InvalidDataException(
                    $"Stage 4Z-D Material root #{rootIndex} computed non-positive serialized mass.");

            if (!byIndex.TryAdd(rootIndex, serialized))
                throw new InvalidDataException(
                    $"Stage 4Z-D repeats Material native root #{rootIndex}.");

            totalSerialized = checked(totalSerialized + serialized);
        }

        if (byIndex.Count != 502)
            throw new InvalidDataException(
                $"Stage 4Z-D resolved {byIndex.Count:N0} owned Material roots; expected 502.");

        var referenceIndices = nativeIdentity
            .Where(x => T4XenonXModelCorpus.NormalizeType(x.OatTypeName) == "material" &&
                        !byIndex.ContainsKey(x.Index))
            .Select(x => x.Index)
            .ToHashSet();

        if (referenceIndices.Count != 88 || corpus.ReferenceRoots != 88)
            throw new InvalidDataException(
                $"Stage 4Z-D resolved {referenceIndices.Count:N0} Material reference roots; expected 88.");

        return new ProductionMaterialCredit(
            byIndex,
            referenceIndices,
            totalSerialized,
            corpus.ReferenceRoots,
            integratedImageRoots,
            integratedXModelRoots,
            integratedZoneBytes);
    }

    private sealed record ProductionImageCredit(
        IReadOnlyDictionary<int, long> SerializedBytesByNativeIndex,
        long TotalSerializedBytes,
        long TotalPixelBytes);

    private sealed record ProductionXModelCredit(
        IReadOnlyDictionary<int, long> SerializedBytesByNativeIndex,
        IReadOnlySet<int> ReferenceNativeIndices,
        long TotalSerializedBytes,
        int ReferenceRoots,
        int IntegratedImageRoots,
        long IntegratedZoneBytes);

    private static ProductionXModelCredit LoadProductionXModelCredit(
        string? graftManifestPath,
        IReadOnlyList<T4XenonNativeIdentityEntry> nativeIdentity,
        int nativeRootCount)
    {
        if (string.IsNullOrWhiteSpace(graftManifestPath))
            return new ProductionXModelCredit(
                new Dictionary<int, long>(),
                new HashSet<int>(),
                0,
                0,
                0,
                0);

        graftManifestPath = Path.GetFullPath(graftManifestPath);
        if (!File.Exists(graftManifestPath))
            throw new FileNotFoundException(
                "Stage 4F production-XModel graft manifest was not found.",
                graftManifestPath);

        using var graftDoc = JsonDocument.Parse(File.ReadAllText(graftManifestPath));
        var graft = graftDoc.RootElement;

        if (GetString(graft, "format") != "WaW360.Nuclear4Y.XModelProductionGraft.v1" ||
            !GetBool(graft, "productionXModelGraftPass"))
            throw new InvalidDataException(
                "Stage 4F requires a passing WaW360.Nuclear4Y.XModelProductionGraft.v1 manifest.");

        if (GetInt(graft, "nativeRoots") != nativeRootCount ||
            GetInt(graft, "oatListedRoots") != nativeRootCount ||
            GetInt(graft, "pcBackedOwnedXModels") != 280 ||
            GetInt(graft, "preservedXModelReferenceRoots") != 9 ||
            !GetBool(graft, "exactIdentitySequence") ||
            !GetBool(graft, "oatZeroWarningsZeroErrors") ||
            !GetBool(graft, "layoutReplayExact"))
            throw new InvalidDataException(
                "Stage 4Y manifest does not prove an exact clean 280-XModel / full native-root replay.");

        var emitterManifestPath = GetString(graft, "emitterManifest");
        if (string.IsNullOrWhiteSpace(emitterManifestPath))
            throw new InvalidDataException("Stage 4Y manifest has no emitter-manifest path.");
        if (!Path.IsPathRooted(emitterManifestPath))
            emitterManifestPath = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(graftManifestPath)!, emitterManifestPath));
        if (!File.Exists(emitterManifestPath))
            throw new FileNotFoundException(
                "Stage 4Y emitter manifest referenced by the production graft was not found.",
                emitterManifestPath);

        using var emitterDoc = JsonDocument.Parse(File.ReadAllText(emitterManifestPath));
        var emitter = emitterDoc.RootElement;

        if (GetInt(emitter, "NativeRoots") != nativeRootCount ||
            GetInt(emitter, "OatListedRoots") != nativeRootCount ||
            GetInt(emitter, "RealXModelRoots") != 280 ||
            GetInt(emitter, "XModelReferenceRoots") != 9 ||
            !GetBool(emitter, "ExactIdentitySequence") ||
            !GetBool(emitter, "OatZeroWarningsZeroErrors") ||
            !GetBool(emitter, "LayoutReplayExact") ||
            !GetBool(emitter, "ValidationPass"))
            throw new InvalidDataException(
                "Stage 4Y emitter manifest no longer proves a clean full-root OAT replay.");

        var integratedZoneBytes = GetLong(emitter, "ZoneBytes");
        var integratedImageRoots = GetInt(emitter, "RealImageRoots");
        if (integratedZoneBytes <= 0)
            throw new InvalidDataException("Stage 4Y emitter reports a non-positive zone size.");

        var corpusManifestPath = GetString(graft, "xmodelCorpusManifest");
        var bindingManifestPath = GetString(graft, "xmodelBindingManifest");

        if (string.IsNullOrWhiteSpace(corpusManifestPath) ||
            string.IsNullOrWhiteSpace(bindingManifestPath))
            throw new InvalidDataException(
                "Stage 4Y manifest is missing XModel corpus/binding provenance.");

        if (!Path.IsPathRooted(corpusManifestPath))
            corpusManifestPath = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(graftManifestPath)!, corpusManifestPath));
        if (!Path.IsPathRooted(bindingManifestPath))
            bindingManifestPath = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(graftManifestPath)!, bindingManifestPath));

        if (!File.Exists(corpusManifestPath))
            throw new FileNotFoundException(
                "Stage 4Y XModel corpus manifest was not found.",
                corpusManifestPath);
        if (!File.Exists(bindingManifestPath))
            throw new FileNotFoundException(
                "Stage 4Y XModel binding manifest was not found.",
                bindingManifestPath);

        var graphRoot = Path.GetDirectoryName(corpusManifestPath)
            ?? throw new InvalidDataException(
                "Stage 4Y XModel corpus manifest has no parent graph directory.");

        var corpus = T4XenonXModelCorpus.Load(
            graphRoot,
            bindingManifestPath,
            expectedOwned: 280,
            expectedReferences: 9);

        var dependencyErrors = corpus.ValidateDependencies(nativeIdentity);
        if (dependencyErrors.Count != 0)
            throw new InvalidDataException(
                "Stage 4Y XModel dependency provenance no longer resolves against the native sequence: " +
                string.Join(" | ", dependencyErrors.Take(20)));

        using var bindingDoc = JsonDocument.Parse(File.ReadAllText(bindingManifestPath));
        var binding = bindingDoc.RootElement;
        if (GetString(binding, "format") != "WaW360.Nuclear4W.XModelRootBinding.v3" ||
            !GetBool(binding, "bindingPass") ||
            GetInt(binding, "boundOwnedModels") != 280 ||
            GetInt(binding, "boundReferenceRoots") != 9)
            throw new InvalidDataException(
                "Stage 4Y references a non-passing 4W XModel binding manifest.");

        var referenceIndices = new HashSet<int>();
        foreach (var row in GetProperty(binding, "referenceBindings").EnumerateArray())
        {
            var index = GetInt(row, "NativeRootIndex");
            var name = GetString(row, "NativeReferenceName");

            if ((uint)index >= (uint)nativeIdentity.Count)
                throw new InvalidDataException(
                    $"Stage 4W reference XModel root #{index} is outside the native sequence.");
            if (!nativeIdentity[index].Name.Equals(name, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Stage 4W reference XModel root #{index} identity '{name}' != native '{nativeIdentity[index].Name}'.");
            if (!referenceIndices.Add(index))
                throw new InvalidDataException(
                    $"Stage 4W repeats reference XModel native root #{index}.");
        }

        if (referenceIndices.Count != 9)
            throw new InvalidDataException(
                $"Stage 4W resolved {referenceIndices.Count:N0} reference XModel roots; expected 9.");

        var byIndex = new Dictionary<int, long>();
        long totalSerialized = 0;

        foreach (var pair in corpus.OwnedByRoot.OrderBy(x => x.Key))
        {
            var rootIndex = pair.Key;
            var entry = pair.Value;

            if ((uint)rootIndex >= (uint)nativeIdentity.Count)
                throw new InvalidDataException(
                    $"Stage 4Y XModel root #{rootIndex} is outside the native sequence.");

            var nativeRoot = nativeIdentity[rootIndex];
            if (!nativeRoot.Name.Equals(entry.NativeName, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Stage 4Y XModel root #{rootIndex} identity '{entry.NativeName}' != native '{nativeRoot.Name}'.");

            long serialized = 0;
            T4XenonXModelGraphCodec.Plan(
                entry,
                (size, _, isSerialized) =>
                {
                    if (isSerialized)
                        serialized = checked(serialized + size);
                },
                size => serialized = checked(serialized + size));

            if (serialized <= 0)
                throw new InvalidDataException(
                    $"Stage 4Y XModel root #{rootIndex} computed non-positive serialized mass.");

            if (!byIndex.TryAdd(rootIndex, serialized))
                throw new InvalidDataException(
                    $"Stage 4Y repeats XModel native root #{rootIndex}.");

            totalSerialized = checked(totalSerialized + serialized);
        }

        if (byIndex.Count != 280)
            throw new InvalidDataException(
                $"Stage 4Y resolved {byIndex.Count:N0} owned XModel roots; expected 280.");

        return new ProductionXModelCredit(
            byIndex,
            referenceIndices,
            totalSerialized,
            corpus.ReferenceRoots,
            integratedImageRoots,
            integratedZoneBytes);
    }

    private static ProductionImageCredit LoadProductionImageCredit(
        string? graftManifestPath,
        IReadOnlyList<T4XenonNativeIdentityEntry> nativeIdentity,
        int nativeRootCount)
    {
        if (string.IsNullOrWhiteSpace(graftManifestPath))
            return new ProductionImageCredit(new Dictionary<int, long>(), 0, 0);

        graftManifestPath = Path.GetFullPath(graftManifestPath);
        if (!File.Exists(graftManifestPath))
            throw new FileNotFoundException(
                "Stage 4F production-image graft manifest was not found.",
                graftManifestPath);

        using var graftDoc = JsonDocument.Parse(File.ReadAllText(graftManifestPath));
        var graft = graftDoc.RootElement;
        var format = GetString(graft, "format");

        if (format == "WaW360.Nuclear4U.ProductionImageGraft.v1")
        {
            if (!GetBool(graft, "productionImageGraftPass") ||
                !GetBool(graft, "validationPass") ||
                GetInt(graft, "nativeRoots") != nativeRootCount ||
                GetInt(graft, "oatListedRoots") != nativeRootCount ||
                !GetBool(graft, "exactIdentitySequence") ||
                !GetBool(graft, "oatZeroWarningsZeroErrors") ||
                !GetBool(graft, "layoutReplayExact"))
                throw new InvalidDataException(
                    "Stage 4U manifest does not prove an exact clean full native-root production-image replay.");

            var productionGraphManifestPath = GetString(graft, "productionImageManifest");
            if (string.IsNullOrWhiteSpace(productionGraphManifestPath))
                throw new InvalidDataException("Stage 4U manifest has no Stage 4T production-image manifest path.");
            if (!Path.IsPathRooted(productionGraphManifestPath))
                productionGraphManifestPath = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(graftManifestPath)!, productionGraphManifestPath));
            if (!File.Exists(productionGraphManifestPath))
                throw new FileNotFoundException(
                    "Stage 4T production-image manifest referenced by 4U was not found.",
                    productionGraphManifestPath);

            using var graphDoc = JsonDocument.Parse(File.ReadAllText(productionGraphManifestPath));
            var graph = graphDoc.RootElement;
            if (GetString(graph, "format") != "WaW360.Nuclear4T.ProductionImageGraph.v1" ||
                !GetBool(graph, "mergePass"))
                throw new InvalidDataException(
                    "Stage 4U references a non-passing Stage 4T production-image graph.");

            var expectedImages = GetInt(graft, "expectedImages");
            if (GetInt(graft, "actualRealImageRoots") != expectedImages ||
                GetInt(graph, "imageGraphs") != expectedImages)
                throw new InvalidDataException(
                    "Stage 4T/4U production-image root counts disagree.");

            var graphRoot = GetString(graph, "graphRoot");
            var imageGraph = T4XenonImageGraphImporter.Load(graphRoot, expectedImages);
            if (!imageGraph.Verified)
                throw new InvalidDataException(
                    "Stage 4T production image graph is no longer verified: " +
                    string.Join(" | ", imageGraph.Errors.Take(20)));

            var byIndex = new Dictionary<int, long>();
            long totalSerialized = 0;
            long totalPixels = 0;

            foreach (var item in GetProperty(graph, "images").EnumerateArray())
            {
                var name = GetString(item, "Name");
                var matches = nativeIdentity
                    .Where(x =>
                        x.Name.Equals(name, StringComparison.Ordinal) &&
                        (x.OatTypeName.Equals("image", StringComparison.OrdinalIgnoreCase) ||
                         x.TypeName.Equals("image", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (matches.Count != 1)
                    throw new InvalidDataException(
                        $"Stage 4T image '{name}' maps to {matches.Count:N0} native image roots; expected exactly one.");

                var index = matches[0].Index;
                if (!imageGraph.ImagesByName.TryGetValue(name, out var image))
                    throw new InvalidDataException(
                        $"Stage 4T graph is missing proven image '{name}'.");

                var serializedBytes = checked(
                    40L +
                    Encoding.UTF8.GetByteCount(name) + 1L +
                    16L +
                    52L +
                    image.PixelsBytes);

                if (!byIndex.TryAdd(index, serializedBytes))
                    throw new InvalidDataException(
                        $"Stage 4T repeats native image root #{index:N0}.");

                totalSerialized = checked(totalSerialized + serializedBytes);
                totalPixels = checked(totalPixels + image.PixelsBytes);
            }

            if (byIndex.Count != expectedImages)
                throw new InvalidDataException(
                    $"Stage 4T image list contains {byIndex.Count:N0} roots; expected {expectedImages:N0}.");

            var expectedPixels = GetLong(graft, "actualImageBytes");
            var graphPixels = GetLong(graph, "xenonPixelBytes");
            if (totalPixels != expectedPixels ||
                graphPixels != expectedPixels ||
                imageGraph.CardMemoryBytes != expectedPixels)
                throw new InvalidDataException(
                    $"Stage 4T/4U Xenon pixel mass disagrees: roots={totalPixels:N0}, " +
                    $"graphManifest={graphPixels:N0}, graphImporter={imageGraph.CardMemoryBytes:N0}, " +
                    $"4U={expectedPixels:N0}.");

            return new ProductionImageCredit(byIndex, totalSerialized, totalPixels);
        }

        if (format != "WaW360.Nuclear4S.PcImageProductionGraft.v1" ||
            !GetBool(graft, "productionImageGraftPass"))
            throw new InvalidDataException(
                "Stage 4F requires a passing Stage 4U production-image graft " +
                "or legacy WaW360.Nuclear4S.PcImageProductionGraft.v1 manifest.");

        if (GetInt(graft, "nativeRoots") != nativeRootCount ||
            GetInt(graft, "oatListedRoots") != nativeRootCount ||
            !GetBool(graft, "exactIdentitySequence") ||
            !GetBool(graft, "oatZeroWarningsZeroErrors") ||
            !GetBool(graft, "layoutReplayExact"))
            throw new InvalidDataException(
                "Legacy Stage 4S manifest does not prove an exact clean full native-root replay.");

        var bridgePath = GetString(graft, "pcImageBridgeManifest");
        if (string.IsNullOrWhiteSpace(bridgePath))
            throw new InvalidDataException("Legacy Stage 4S manifest has no 4R bridge manifest path.");
        if (!Path.IsPathRooted(bridgePath))
            bridgePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(graftManifestPath)!, bridgePath));
        if (!File.Exists(bridgePath))
            throw new FileNotFoundException(
                "Stage 4R bridge manifest referenced by legacy 4S was not found.",
                bridgePath);

        using var bridgeDoc = JsonDocument.Parse(File.ReadAllText(bridgePath));
        var bridge = bridgeDoc.RootElement;
        if (GetString(bridge, "format") != "WaW360.Nuclear4R.PcOwnedImageBridge.v1" ||
            !GetBool(bridge, "bridgePass"))
            throw new InvalidDataException(
                "Legacy Stage 4S references a non-passing Stage 4R image bridge.");

        var legacyExpectedImages = GetInt(graft, "pcBackedImages");
        if (GetInt(bridge, "resolvedSources") != legacyExpectedImages)
            throw new InvalidDataException(
                "Stage 4R/legacy-4S production-image root counts disagree.");

        var legacyGraphRoot = GetString(bridge, "graphRoot");
        var legacyImageGraph = T4XenonImageGraphImporter.Load(legacyGraphRoot, legacyExpectedImages);
        if (!legacyImageGraph.Verified)
            throw new InvalidDataException(
                "Stage 4R production image graph is no longer verified: " +
                string.Join(" | ", legacyImageGraph.Errors.Take(20)));

        var legacyByIndex = new Dictionary<int, long>();
        long legacyTotalSerialized = 0;
        long legacyTotalPixels = 0;

        foreach (var item in GetProperty(bridge, "resolved").EnumerateArray())
        {
            var index = GetInt(item, "Index");
            var name = GetString(item, "Name");

            if ((uint)index >= (uint)nativeIdentity.Count)
                throw new InvalidDataException(
                    $"Stage 4R image root index {index} is outside the native sequence.");
            if (!nativeIdentity[index].Name.Equals(name, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Stage 4R image root #{index:N0} identity '{name}' != native '{nativeIdentity[index].Name}'.");
            if (!legacyImageGraph.ImagesByName.TryGetValue(name, out var image))
                throw new InvalidDataException(
                    $"Stage 4R graph is missing proven image '{name}'.");

            var serializedBytes = checked(
                40L +
                Encoding.UTF8.GetByteCount(name) + 1L +
                16L +
                52L +
                image.PixelsBytes);

            if (!legacyByIndex.TryAdd(index, serializedBytes))
                throw new InvalidDataException(
                    $"Stage 4R repeats native image root #{index:N0}.");

            legacyTotalSerialized = checked(legacyTotalSerialized + serializedBytes);
            legacyTotalPixels = checked(legacyTotalPixels + image.PixelsBytes);
        }

        if (legacyByIndex.Count != legacyExpectedImages)
            throw new InvalidDataException(
                $"Stage 4R resolved list contains {legacyByIndex.Count:N0} roots; expected {legacyExpectedImages:N0}.");

        var legacyExpectedPixels = GetLong(graft, "actualImagePixelBytes");
        if (legacyTotalPixels != legacyExpectedPixels ||
            legacyImageGraph.CardMemoryBytes != legacyExpectedPixels)
            throw new InvalidDataException(
                $"Stage 4R/legacy-4S Xenon pixel mass disagrees: roots={legacyTotalPixels:N0}, " +
                $"graph={legacyImageGraph.CardMemoryBytes:N0}, 4S={legacyExpectedPixels:N0}.");

        return new ProductionImageCredit(
            legacyByIndex,
            legacyTotalSerialized,
            legacyTotalPixels);
    }

    private static JsonElement GetProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        throw new InvalidDataException($"Required JSON property '{name}' is missing.");
    }

    private static string GetString(JsonElement element, string name) =>
        GetProperty(element, name).GetString() ?? string.Empty;

    private static int GetInt(JsonElement element, string name) =>
        GetProperty(element, name).GetInt32();

    private static long GetLong(JsonElement element, string name) =>
        GetProperty(element, name).GetInt64();

    private static bool GetBool(JsonElement element, string name) =>
        GetProperty(element, name).GetBoolean();

    private static IReadOnlyList<T4XenonNativeIdentityEntry> LoadNativeIdentity(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        if (root.GetProperty("format").GetString() != "WaW360.T4Xenon.NativeIdentityManifest.v1" ||
            !root.GetProperty("typeSequenceVerified").GetBoolean())
            throw new InvalidDataException("Stage 4F native identity manifest is not verified.");

        return root.GetProperty("entries").EnumerateArray()
            .Select(e => new T4XenonNativeIdentityEntry(
                e.GetProperty("Index").GetInt32(),
                e.GetProperty("TypeId").GetInt32(),
                e.GetProperty("TypeName").GetString() ?? string.Empty,
                e.GetProperty("OatTypeName").GetString() ?? string.Empty,
                e.GetProperty("Name").GetString() ?? string.Empty))
            .OrderBy(x => x.Index)
            .ToList();
    }

    private static MutableTypeMass GetStat(
        IDictionary<string, MutableTypeMass> stats,
        string type)
    {
        if (!stats.TryGetValue(type, out var value))
        {
            value = new MutableTypeMass(type);
            stats.Add(type, value);
        }
        return value;
    }

    private static string Render(NormalMapLinkerV2FullContentReadinessResult r)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("NUCLEAR STAGE 4F - FULL-CONTENT MASS / PRE-HARDWARE GATE");
        sb.AppendLine(new string('=', 92));
        sb.AppendLine($"Map:                         {r.MapName}");
        sb.AppendLine($"Native roots:                {r.NativeRoots:N0}");
        sb.AppendLine($"Native working .ff on disk:  {r.NativeOracleFastFileBytes:N0} B");
        sb.AppendLine($"Native oracle zone:          {r.NativeOracleZoneBytes:N0} B");
        sb.AppendLine($"All-shell baseline zone:     {r.AllShellBaselineZoneBytes:N0} B");
        sb.AppendLine($"Proven renderer gain:        {r.ProvenRendererGainBytes:N0} B");
        sb.AppendLine($"Estimated full-content zone: {r.EstimatedFullContentZoneBytes:N0} B");
        sb.AppendLine($"Native size ratio:           {r.NativeSizeRatio:P2}");
        sb.AppendLine($"Required size window:        {r.MinimumSizeRatio:P0} .. {r.MaximumSizeRatio:P0}");
        sb.AppendLine();
        sb.AppendLine($"Source-mapped roots:         {r.SourceMappedRoots:N0}");
        sb.AppendLine($"Real promoted roots:         {r.RealPromotedRoots:N0}");
        sb.AppendLine($"Fallback roots:              {r.FallbackRoots:N0}");
        sb.AppendLine($"Synthetic dependency roots:  {r.SyntheticDependencyRoots:N0}");
        sb.AppendLine($"Critical fallback roots:     {r.CriticalFallbackRoots:N0}");
        sb.AppendLine($"External-audio pending roots:{r.ExternalAudioPendingRoots,10:N0}");
        sb.AppendLine($"Source payload bytes:        {r.SourcePayloadBytes:N0}");
        sb.AppendLine($"Real promoted bytes:         {r.RealPromotedBytes:N0}");
        sb.AppendLine($"Fallback bytes:              {r.FallbackBytes:N0}");
        sb.AppendLine($"Production image roots:      {r.ProductionImageRoots:N0}");
        sb.AppendLine($"Production image bytes:      {r.ProductionImageSerializedBytes:N0} serialized / {r.ProductionImagePixelBytes:N0} pixels");
        sb.AppendLine($"Production XModel roots:     {r.ProductionXModelRoots:N0}");
        sb.AppendLine($"XModel reference roots:      {r.ProductionXModelReferenceRoots:N0}");
        sb.AppendLine($"Production XModel bytes:     {r.ProductionXModelSerializedBytes:N0} serialized");
        sb.AppendLine($"Production Material roots:   {r.ProductionMaterialRoots:N0}");
        sb.AppendLine($"Material reference roots:    {r.ProductionMaterialReferenceRoots:N0}");
        sb.AppendLine($"Production Material bytes:   {r.ProductionMaterialSerializedBytes:N0} serialized");
        sb.AppendLine($"Critical retail references:  {r.ProductionCriticalReferenceRoots:N0}");
        sb.AppendLine($"Retail image references:     {r.ProductionImageReferenceRoots:N0}");
        sb.AppendLine($"Integrated production zone:  {r.ProductionIntegratedBaselineZoneBytes:N0} B");
        sb.AppendLine();
        sb.AppendLine($"Renderer layout ready:       {r.RendererLayoutReady}");
        sb.AppendLine($"Proper-size gate:            {r.ProperSizeGate}");
        sb.AppendLine($"Critical-content gate:       {r.CriticalContentGate}");
        sb.AppendLine($"READY FOR FULL EMISSION:     {r.ReadyForFullNativeEmission}");
        sb.AppendLine();

        sb.AppendLine("TYPE MASS / FALLBACK CENSUS");
        sb.AppendLine("type               native mapped real fallback synthetic   shellB    sourceB   fallbackB");
        foreach (var t in r.Types)
        {
            sb.AppendLine(
                $"{t.TypeName,-18} {t.NativeRoots,6:N0} {t.SourceMappedRoots,6:N0} {t.RealPromotedRoots,5:N0} " +
                $"{t.FallbackRoots,8:N0} {t.SyntheticRoots,9:N0} " +
                $"{t.ShellBytes,9:N0} {t.SourcePayloadBytes,10:N0} {t.FallbackBytes,11:N0}");
        }

        if (r.CriticalBlockers.Count != 0)
        {
            sb.AppendLine();
            sb.AppendLine("CRITICAL BLOCKERS (first 200)");
            foreach (var blocker in r.CriticalBlockers.Take(200))
                sb.AppendLine("  - " + blocker);
            if (r.CriticalBlockers.Count > 200)
                sb.AppendLine($"  ... {r.CriticalBlockers.Count - 200:N0} more");
        }

        sb.AppendLine();
        foreach (var note in r.Notes)
            sb.AppendLine("NOTE: " + note);

        return sb.ToString();
    }

    private sealed class MutableTypeMass
    {
        public MutableTypeMass(string typeName) => TypeName = typeName;

        public string TypeName { get; }
        public int NativeRoots { get; set; }
        public int SourceMappedRoots { get; set; }
        public int RealPromotedRoots { get; set; }
        public int FallbackRoots { get; set; }
        public int SyntheticRoots { get; set; }
        public long ShellBytes { get; set; }
        public long SourcePayloadBytes { get; set; }
        public long RealPromotedBytes { get; set; }
        public long FallbackBytes { get; set; }

        public LinkerV2FullContentTypeMass ToImmutable() =>
            new(
                TypeName,
                NativeRoots,
                SourceMappedRoots,
                RealPromotedRoots,
                FallbackRoots,
                SyntheticRoots,
                ShellBytes,
                SourcePayloadBytes,
                RealPromotedBytes,
                FallbackBytes);
    }
}
