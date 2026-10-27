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
    public required long SourcePayloadBytes { get; init; }
    public required long RealPromotedBytes { get; init; }
    public required long FallbackBytes { get; init; }
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
            "sound",
            "loaded_sound",
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

                    if (CriticalTypes.Contains(type))
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

                if (CriticalTypes.Contains(type))
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

            if (deltaPools.TryGetValue(type, out var queue) && queue.Count != 0)
            {
                var payload = queue.Dequeue().Payload;
                nonRendererDelta += payload.Bytes.LongLength - shell.Bytes.LongLength;
            }
        }

        var estimatedFullContent =
            checked(rendererLayout.ProjectedUncompressedZoneBytes + nonRendererDelta);

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
            "EstimatedFullContentZoneBytes starts from the exact 4E renderer-layout zone and replaces each mapped non-renderer name shell with the current source-owned structural payload.",
            "Size is compared against the decompressed known-working FRUmansion Xbox oracle, not compressed .ff bytes.",
            $"A proper-size candidate must be between {minimumSizeRatio:P0} and {maximumSizeRatio:P0} of the native oracle before hardware testing is allowed.",
            "Critical families must have zero fallback/synthetic roots before Stage 4F can declare full native emission ready.",
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
            SourcePayloadBytes = sourcePayloadBytes,
            RealPromotedBytes = realPromotedBytes,
            FallbackBytes = fallbackBytes,
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
        sb.AppendLine($"Source payload bytes:        {r.SourcePayloadBytes:N0}");
        sb.AppendLine($"Real promoted bytes:         {r.RealPromotedBytes:N0}");
        sb.AppendLine($"Fallback bytes:              {r.FallbackBytes:N0}");
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
