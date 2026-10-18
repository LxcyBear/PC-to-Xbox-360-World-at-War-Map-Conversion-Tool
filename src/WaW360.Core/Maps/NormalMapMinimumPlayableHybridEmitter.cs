using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4MinimumPlayableFastFile(
    string Role,
    string Source,
    int AssetCount,
    int SmokePayloadAssets,
    IReadOnlyDictionary<string, int> SmokePayloadsByType,
    int ClipMapsPromoted,
    int GfxWorldsPromoted,
    int FullTrackedProofs,
    int TrackedNamesFallback,
    int SilentSoundAssets,
    int NullGraphTechniqueSets,
    int ReducedMaterials,
    int LegacyPackedSharedTargetsRetiredFromSmokePath,
    bool LegacyNestedGraphRetiredFromSmokePath,
    int ScriptAliasesPending,
    int AssetAliasesPending,
    int MissingPhysicalPayloads,
    int ReadBackErrors,
    string ZoneImageFile,
    long ZoneImageBytes,
    string ProbeFastFile,
    long ProbeFastFileBytes,
    bool WrapperRoundTripVerified,
    bool SmokeSelfContained,
    bool SmokeHardwareCandidate,
    string? HardwareFastFile,
    string? HardwareFastFileSha256,
    bool StrictFidelityReady,
    IReadOnlyList<string> FidelityObligations,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapMinimumPlayableHybridEmitterResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required string HardwareCandidateRoot { get; init; }
    public required IReadOnlyList<T4MinimumPlayableFastFile> FastFiles { get; init; }
    public required int FastFileCount { get; init; }
    public required int WorldRootsPromoted { get; init; }
    public required int SmokePayloadAssets { get; init; }
    public required int LegacyPackedSharedTargetsRetiredFromSmokePath { get; init; }
    public required int MissingPhysicalPayloads { get; init; }
    public required int ReadBackErrors { get; init; }
    public required int WrapperRoundTripVerifiedFastFiles { get; init; }
    public required int SmokeHardwareFastFilesEmitted { get; init; }
    public required bool CompleteSmokeSet { get; init; }
    public required bool Verified { get; init; }
    public required bool StrictFidelityReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V1 alpha 4MP4: minimum-playable hybrid hardware emitter + optimized tracked-family hardware triage.
///
/// Alpha 4MP is an experimental bridge between the reduced smoke path and strict fidelity. It restores the
/// complete verified Stage-4 serializer proof payloads for XAnim/XModel/Weapon/FX, preserves proven RawFile/
/// Localize data, and keeps the reduced 10r Sound/TechniqueSet/Material/world serializers for asset families
/// that still lack strict writers. Packed top-level references that would escape a standalone proof blob are
/// nulled rather than left as stale PC/Xbox packed pointers. Nested shared references remain an explicit risk.
/// This stage is intended to increase real content dramatically while keeping the build fail-closed at the
/// top-level XAsset accounting boundary; it is still not a strict-fidelity conversion.
/// </summary>
public static class NormalMapMinimumPlayableHybridEmitter
{
    private static readonly string[] TrackedTypes = ["xanim", "xmodel", "weapon", "fx"];
    private static readonly HashSet<string> SmokeSupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "xanim", "xmodel", "weapon", "fx",
        "sound", "rawfile", "localize", "stringtable",
        "techset", "material", "comworld", "gameworld_sp", "clipmap", "gfxworld"
    };

    private sealed record SmokePayload(int AssetIndex, string TypeName, string Name, byte[] Bytes, string Obligation);
    private sealed record TrackedHybridBuild(IReadOnlyDictionary<int, SmokePayload> Payloads, int FallbackNames, IReadOnlyList<string> Warnings);
    private sealed record VariantPayloadBuild(
        IReadOnlyDictionary<int, SmokePayload> Payloads,
        int PhysicalCount,
        int MissingCount,
        int AliasAssets,
        bool CanBuild,
        IReadOnlyList<string> Warnings);
    private sealed record TriageRow(
        string Variant, string Role, string Source, string? HardwareFastFile, long ZoneBytes, long FastFileBytes,
        bool ZoneReadBackVerified, bool WrapperRoundTripVerified, int MissingPayloads, string Sha256, IReadOnlyList<string> Warnings);
    private sealed record EmitStats(long Bytes, int ScriptAliasesPending, int AssetAliasesPending, int PayloadAssets);
    private sealed record ReadBackStats(int Errors, int PayloadAssets);

    public static async Task<NormalMapMinimumPlayableHybridEmitterResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var qJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10q_material_simple_tail_promotion",
            $"{map.Name}_stage5_alpha10q_material_simple_tail_promotion.json");
        if (!File.Exists(qJson))
            throw new InvalidDataException("Stage 5v10r requires the verified Stage 5u10q Material/simple-tail promotion report.");

        var prior = JsonSerializer.Deserialize<NormalMapMaterialSimpleTailSmokePromotionResult>(
            await File.ReadAllTextAsync(qJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5u10q result.");
        // Alpha 4MP3 diagnostic continuation: a partial 10q result is still useful. Each FF
        // below re-runs RawFile/Material/world verification and can only become a hardware
        // candidate when its own complete payload set is independently verified.
        var tailAccountingGloballyVerified = prior.Verified;

        var workspace = Path.Combine(outputRoot, map.Name, "linker_v1_alpha4mp_minimum_playable");
        Directory.CreateDirectory(workspace);
        var zoneRoot = Path.Combine(workspace, "zones");
        var probeRoot = Path.Combine(workspace, "ff_probes");
        var hardwareRoot = Path.Combine(outputRoot, map.Name, "hardware_candidates_minplay");
        var triageRoot = Path.Combine(outputRoot, map.Name, "hardware_candidates_triage");
        Directory.CreateDirectory(zoneRoot);
        Directory.CreateDirectory(probeRoot);
        Directory.CreateDirectory(hardwareRoot);
        Directory.CreateDirectory(triageRoot);

        var triageSpecs = new (string Name, HashSet<string> Restore, bool RequireZeroEscapes)[]
        {
            ("00_baseline_singlezlib", new HashSet<string>(StringComparer.OrdinalIgnoreCase), false),
            ("01_xanim_only", new HashSet<string>(["xanim"], StringComparer.OrdinalIgnoreCase), false),
            ("02_xmodel_only", new HashSet<string>(["xmodel"], StringComparer.OrdinalIgnoreCase), false),
            ("03_weapon_only", new HashSet<string>(["weapon"], StringComparer.OrdinalIgnoreCase), false),
            ("04_fx_only", new HashSet<string>(["fx"], StringComparer.OrdinalIgnoreCase), false),
            ("05_safe_no_escape_all", new HashSet<string>(TrackedTypes, StringComparer.OrdinalIgnoreCase), true),
        };
        foreach (var spec in triageSpecs)
        {
            var dir = Path.Combine(triageRoot, spec.Name);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
        }

        // Never let a failed rerun look successful because an older smoke FF is still present.
        foreach (var ff in prior.FastFiles)
        {
            var stale = Path.Combine(hardwareRoot, Path.GetFileName(ff.Source));
            try { if (File.Exists(stale)) File.Delete(stale); } catch { }
        }
        try { File.Delete(Path.Combine(hardwareRoot, "ALPHA4MP_README.txt")); } catch { }

        var rows = new List<T4MinimumPlayableFastFile>();
        var triageRows = new List<TriageRow>();
        var detail = new List<string>();
        // For large all-map batches, build the six-way hardware family matrix against ONE
        // representative primary FF only. Every other FF still gets a verified single-zlib
        // baseline and that exact baseline is copied into each variant package. The final
        // minimum-playable candidate remains unchanged and still restores tracked families
        // across every FF. Prefer the canonical map.ff role; otherwise use the largest FF.
        var triageTarget = prior.FastFiles.FirstOrDefault(x => x.Role.Equals("map.ff", StringComparison.OrdinalIgnoreCase))
            ?? prior.FastFiles.OrderByDescending(x => File.Exists(x.Source) ? new FileInfo(x.Source).Length : 0L).FirstOrDefault();
        var triageTargetSource = triageTarget?.Source;
        if (triageTargetSource is not null)
            detail.Add($"INFO: alpha 4MP4 triage matrix target={Path.GetFileName(triageTargetSource)} role={triageTarget!.Role}; non-target FFs reuse their verified baseline across family variants.");
        if (!tailAccountingGloballyVerified) detail.Add("DIAGNOSTIC CONTINUATION: upstream Material/simple-tail accounting is partial/unverified; this stage continues per fastfile but hardware emission remains fail-closed for affected FFs.");

        foreach (var ff in prior.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            var doc = cached.Document;
            var zone = cached.Bytes;

            var warnings = new List<string>();
            var payloads = new Dictionary<int, SmokePayload>();

            // Alpha 4MP restores the COMPLETE verified Stage-4 serializer proof blobs for
            // XAnim/XModel/Weapon/FX instead of the 10r zero-owned-data shells. Packed
            // top-level references that would escape the standalone blob are nulled, while
            // inline-owned child streams remain intact. This is intentionally experimental.
            var tracked = await BuildTrackedHybridPayloadsAsync(outputRoot, map.Name, ff.Source, ff.Role, doc, cancellationToken);
            foreach (var p in tracked.Payloads.Values) AddPayload(payloads, p);
            warnings.AddRange(tracked.Warnings);

            // Sound: verified self-contained SILENT alias-list stubs from 10n.
            var sound = T4SoundAliasSmokeSerializer.Build(doc, zone, cancellationToken);
            if (!sound.Verified)
                warnings.Add($"Sound smoke serializer is not verified ({sound.ParsedPhysicalSoundAssets:N0}/{sound.DeclaredPhysicalSoundAssets:N0}).");
            foreach (var s in sound.AssetsByIndex.Values)
                AddPayload(payloads, new SmokePayload(s.AssetIndex, "sound", s.AliasListName, s.XboxSilentStubPayload,
                    "silent smoke SoundFile graph; final Xbox XMA/streamed audio remains pending"));

            // TechniqueSet: concrete Xbox fixed body with 51 null graph slots.
            var tech = T4TechniqueSetNullGraphSerializer.Build(doc);
            if (!tech.Verified)
                warnings.Add($"TechniqueSet smoke serializer is not verified ({tech.NativeSmokeTechniqueSets:N0}/{tech.DeclaredPhysicalTechniqueSets:N0}).");
            foreach (var t in tech.AssetsByIndex.Values)
                AddPayload(payloads, new SmokePayload(t.AssetIndex, "techset", t.SmokeName, t.XboxPayload,
                    "51 Xbox technique graph slots are null; final Xenos shader/pass graphs remain pending"));

            // Occupied-span proof is still used for RawFile body ownership, but the emitted smoke
            // payload is self-contained and contains no source packed-name pointer.
            var occupied = await BuildOccupiedSpansAsync(outputRoot, map.Name, ff.Source, ff.Role, doc, zone, cancellationToken);
            var raw = T4RawFileOccupiedSpanSerializer.Build(doc, zone, occupied, cancellationToken);
            if (!raw.Verified)
                warnings.Add($"RawFile occupied-span accounting is not verified ({raw.StructuralRawFiles:N0}/{raw.DeclaredPhysicalRawFiles:N0}).");
            foreach (var r in raw.AssetsByIndex.Values.Where(x => x.NativePromotable && x.XboxPayload is not null))
                AddPayload(payloads, new SmokePayload(r.AssetIndex, "rawfile", r.Name, r.XboxPayload!, "original RawFile body preserved"));

            // Foundation serializer supplies normal Localize/StringTable payloads. RawFile is
            // intentionally sourced from the stronger 10o occupied-span proof above.
            var foundation = T4SimpleNativeAssetSerializer.Build(doc, zone);
            foreach (var f in foundation.AssetsByIndex.Values.Where(x =>
                         x.TypeName.Equals("localize", StringComparison.OrdinalIgnoreCase) ||
                         x.TypeName.Equals("stringtable", StringComparison.OrdinalIgnoreCase)))
                AddPayload(payloads, new SmokePayload(f.AssetIndex, f.TypeName, f.Name, f.Payload,
                    f.TypeName.Equals("localize", StringComparison.OrdinalIgnoreCase)
                        ? "localized key/value preserved"
                        : "stringtable payload preserved"));

            // 10q fills Material, packed Localize, packed-name RawFile, ComWorld and GameWorldSp.
            var tail = T4MaterialSimpleTailSmokeSerializer.Build(doc, zone, raw);
            if (!tail.Verified)
                warnings.Add("Material/simple-tail smoke serializer is not verified.");
            foreach (var t in tail.AssetsByIndex.Values)
                AddPayload(payloads, new SmokePayload(t.AssetIndex, t.TypeName, t.SmokeName, t.XboxPayload, t.FidelityObligation));

            // 10r's two final top-level blockers.
            var world = T4WorldRootSmokeSerializer.Build(doc);
            if (!world.Verified)
                warnings.Add("clipMap_t/GfxWorld smoke-root serializer is not verified.");
            foreach (var w in world.AssetsByIndex.Values)
                AddPayload(payloads, new SmokePayload(w.AssetIndex, w.TypeName, w.SmokeName, w.XboxPayload,
                    w.TypeName.Equals("clipmap", StringComparison.OrdinalIgnoreCase)
                        ? "reduced clipMap_t has no collision/static-model/dynent data"
                        : "reduced GfxWorld has no render/light/grid/surface data"));

            var physical = doc.Assets
                .Where(a => a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                .OrderBy(a => a.Index)
                .ToList();
            var unsupportedTypes = physical
                .Where(a => !SmokeSupportedTypes.Contains(a.TypeName))
                .GroupBy(a => a.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in unsupportedTypes)
                warnings.Add($"Unsupported smoke XAsset family '{kv.Key}' x{kv.Value:N0}; hardware smoke emission is fail-closed.");

            var missing = physical.Where(a => !payloads.ContainsKey(a.Index)).ToList();
            if (missing.Count != 0)
            {
                foreach (var group in missing.GroupBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase).Take(16))
                    warnings.Add($"Missing self-contained smoke payload: {group.Key} x{group.Count():N0}.");
            }

            var aliasAssets = doc.Assets.Count(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias);
            if (aliasAssets != 0)
                warnings.Add($"Source XAsset table contains {aliasAssets:N0} packed/alias top-level entry/entries; 10r refuses to guess alias ownership.");

            var stem = Path.GetFileNameWithoutExtension(ff.Source) + "_" + Sanitize(ff.Role);
            var zonePath = Path.Combine(zoneRoot, stem + ".a4mp_hybrid.zone");
            var probePath = Path.Combine(probeRoot, stem + ".a4mp_hybrid.ffprobe");
            var verifyPath = Path.Combine(probeRoot, stem + ".a4mp_verify.zone");

            var canBuild = missing.Count == 0 && unsupportedTypes.Count == 0 && aliasAssets == 0 &&
                           sound.Verified && tech.Verified && raw.Verified && tail.Verified && world.Verified;

            EmitStats emit = new(0, 0, aliasAssets, 0);
            ReadBackStats audit = new(1, 0);
            var wrapperOk = false;
            var smokeSelfContained = false;
            string? hardwareFile = null;
            string? hardwareSha = null;
            long probeBytes = 0;

            if (canBuild)
            {
                emit = await EmitSmokeZoneAsync(zonePath, doc, payloads, cancellationToken);
                audit = await ReadBackSmokeZoneAsync(zonePath, doc, payloads, cancellationToken);
                smokeSelfContained = emit.ScriptAliasesPending == 0 && emit.AssetAliasesPending == 0 &&
                                     audit.Errors == 0 && emit.PayloadAssets == physical.Count;

                if (smokeSelfContained)
                {
                    // Alpha 4MP follows the known-working CoDXe Aztec custom-map wrapper contract:
                    // IWffu100 + big-endian 0x183 + one normal zlib stream. The writer prefers
                    // classic zlib level 6 and independently round-trips the exact zone bytes.
                    var zoneBytes = await File.ReadAllBytesAsync(zonePath, cancellationToken);
                    _ = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
                        probePath, zoneBytes, preferNativeLevel6: true, cancellationToken: cancellationToken);
                    try
                    {
                        var decomp = await FastFileDecompressor.DecompressAutoAsync(probePath, verifyPath, cancellationToken);
                        wrapperOk = decomp.Platform == WaWPlatform.Xbox360 &&
                                    decomp.Compression == WaWCompressionKind.XboxSingleZlib &&
                                    await FilesEqualAsync(zonePath, verifyPath, cancellationToken);
                    }
                    finally
                    {
                        try { File.Delete(verifyPath); } catch { }
                    }
                }

                if (wrapperOk)
                {
                    hardwareFile = Path.Combine(hardwareRoot, Path.GetFileName(ff.Source));
                    File.Copy(probePath, hardwareFile, overwrite: true);
                    hardwareSha = await Sha256Async(hardwareFile, cancellationToken);
                }
                if (File.Exists(probePath)) probeBytes = new FileInfo(probePath).Length;
            }

            var byType = payloads.Values
                .GroupBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var obligations = payloads.Values
                .Select(x => x.Obligation)
                .Where(x => !string.IsNullOrWhiteSpace(x) && !x.Contains("preserved", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var fullTracked = TrackedTypes.Sum(type => byType.TryGetValue(type, out var n) ? n : 0);
            var reducedMaterials = byType.TryGetValue("material", out var matCount) ? matCount : 0;
            var hybridCandidate = hardwareFile is not null;
            var strictFidelityReady = ff.StrictHardwareReady && obligations.Count == 0;

            rows.Add(new T4MinimumPlayableFastFile(
                ff.Role, ff.Source,
                doc.AssetCount,
                payloads.Count,
                byType,
                world.PromotedClipMaps,
                world.PromotedGfxWorlds,
                fullTracked,
                tracked.FallbackNames,
                sound.ParsedPhysicalSoundAssets,
                tech.NativeSmokeTechniqueSets,
                reducedMaterials,
                ff.StrictNestedSharedTargetsPending,
                ff.StrictNestedRebasePending,
                emit.ScriptAliasesPending,
                emit.AssetAliasesPending,
                missing.Count,
                audit.Errors,
                zonePath,
                File.Exists(zonePath) ? new FileInfo(zonePath).Length : 0,
                probePath,
                probeBytes,
                wrapperOk,
                smokeSelfContained,
                hybridCandidate,
                hardwareFile,
                hardwareSha,
                strictFidelityReady,
                obligations,
                warnings));

            var sourceBytes = File.Exists(ff.Source) ? new FileInfo(ff.Source).Length : 0;
            var hardwareBytes = hardwareFile is not null && File.Exists(hardwareFile) ? new FileInfo(hardwareFile).Length : 0;
            var sizeRatio = sourceBytes > 0 ? (100.0 * hardwareBytes / sourceBytes) : 0.0;
            var sizeSanity = hardwareBytes <= 12
                ? "REVIEW_EMPTY_OR_HEADER_ONLY"
                : sourceBytes >= 1024L * 1024L && hardwareBytes < 256L * 1024L
                    ? "REVIEW_TOO_SMALL"
                    : "OK_INFO_ONLY";

            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: world=clip {world.PromotedClipMaps:N0}/{world.DeclaredClipMaps:N0}, gfx {world.PromotedGfxWorlds:N0}/{world.DeclaredGfxWorlds:N0}; payloads={payloads.Count:N0}/{physical.Count:N0}; missing={missing.Count:N0}; trackedFullProofs={fullTracked:N0}; nameFallbacks={tracked.FallbackNames:N0}; legacySharedRetiredForSmoke={ff.StrictNestedSharedTargetsPending:N0}; legacyNestedRetiredForSmoke={ff.StrictNestedRebasePending}; scriptAliasPending={emit.ScriptAliasesPending:N0}; readBackErrors={audit.Errors:N0}; wrapperRoundTrip={wrapperOk}; hybridFF={(hardwareFile is null ? "NO" : hardwareFile)}; strictFidelity={strictFidelityReady}");
            detail.Add($"  SIZE sourceFF={sourceBytes:N0} B ({sourceBytes / (1024.0 * 1024.0):F3} MiB); hybridZone={(File.Exists(zonePath) ? new FileInfo(zonePath).Length : 0):N0} B; hybridFF={hardwareBytes:N0} B ({hardwareBytes / (1024.0 * 1024.0):F3} MiB); output/source={sizeRatio:F2}%; sizeCheck={sizeSanity}");
            foreach (var kv in byType) detail.Add($"  PAYLOAD {kv.Key}: {kv.Value:N0}");
            foreach (var warning in warnings.Take(24)) detail.Add("  NOTE: " + warning);

            // Alpha 4MP4 hardware triage matrix. Baseline is emitted once per FF. Variants that
            // cannot differ because the FF contains none of their target tracked families reuse the
            // already-verified baseline bytes instead of rebuilding, recompressing, decompressing,
            // and hashing identical data. This is especially important for all-Normal batches with
            // load/mod/patch FFs that often contain no XAnim/XModel/Weapon/FX assets at all.
            var presentTrackedTypes = doc.Assets
                .Where(a => a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                .Select(a => a.TypeName)
                .Where(t => TrackedTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var isTriageTarget = triageTargetSource is not null &&
                                 Path.GetFullPath(ff.Source).Equals(Path.GetFullPath(triageTargetSource), StringComparison.OrdinalIgnoreCase);
            TriageRow? baselineRow = null;

            foreach (var spec in triageSpecs)
            {
                var variantFolder = Path.Combine(triageRoot, spec.Name);
                var targetedFamilyPresent = isTriageTarget && spec.Restore.Count != 0 && spec.Restore.Any(presentTrackedTypes.Contains);
                if (spec.Name != "00_baseline_singlezlib" && !targetedFamilyPresent && baselineRow is not null)
                {
                    string? reusedHardware = null;
                    if (baselineRow.HardwareFastFile is not null && File.Exists(baselineRow.HardwareFastFile))
                    {
                        reusedHardware = Path.Combine(variantFolder, Path.GetFileName(ff.Source));
                        File.Copy(baselineRow.HardwareFastFile, reusedHardware, overwrite: true);
                    }
                    var reuseWarnings = baselineRow.Warnings
                        .Concat(new[] { isTriageTarget
                            ? $"INFO: alpha 4MP4 reused baseline bytes for {spec.Name}; {Path.GetFileName(ff.Source)} contains no targeted tracked family, so recompression was skipped."
                            : $"INFO: alpha 4MP4 reused baseline bytes for {spec.Name}; family isolation is targeted at {Path.GetFileName(triageTargetSource)} and this non-target FF is held constant." })
                        .ToArray();
                    triageRows.Add(new TriageRow(
                        spec.Name, ff.Role, ff.Source, reusedHardware,
                        baselineRow.ZoneBytes, baselineRow.FastFileBytes,
                        baselineRow.ZoneReadBackVerified, baselineRow.WrapperRoundTripVerified,
                        baselineRow.MissingPayloads, baselineRow.Sha256, reuseWarnings));
                    continue;
                }

                var variant = await BuildVariantPayloadSetAsync(
                    outputRoot, map.Name, ff.Source, ff.Role, doc, zone, spec.Restore, spec.RequireZeroEscapes, cancellationToken);
                var variantZoneDir = Path.Combine(workspace, "triage_zones", spec.Name);
                var variantProbeDir = Path.Combine(workspace, "triage_ff_probes", spec.Name);
                Directory.CreateDirectory(variantZoneDir);
                Directory.CreateDirectory(variantProbeDir);

                var vStem = Path.GetFileNameWithoutExtension(ff.Source) + "_" + Sanitize(ff.Role);
                var vZone = Path.Combine(variantZoneDir, vStem + ".a4mp4_" + spec.Name + ".zone");
                var vProbe = Path.Combine(variantProbeDir, vStem + ".a4mp4_" + spec.Name + ".ffprobe");
                var vVerify = Path.Combine(variantProbeDir, vStem + ".verify.zone");
                string? vHardware = null;
                var vReadBack = false;
                var vWrapper = false;
                long vZoneBytes = 0;
                long vFastFileBytes = 0;
                var vSha = string.Empty;

                if (variant.CanBuild)
                {
                    var vEmit = await EmitSmokeZoneAsync(vZone, doc, variant.Payloads, cancellationToken);
                    var vAudit = await ReadBackSmokeZoneAsync(vZone, doc, variant.Payloads, cancellationToken);
                    vReadBack = vAudit.Errors == 0 && vEmit.PayloadAssets == variant.PhysicalCount &&
                                vEmit.ScriptAliasesPending == 0 && vEmit.AssetAliasesPending == 0;
                    vZoneBytes = File.Exists(vZone) ? new FileInfo(vZone).Length : 0;

                    if (vReadBack)
                    {
                        var vZoneData = await File.ReadAllBytesAsync(vZone, cancellationToken);
                        _ = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
                            vProbe, vZoneData, preferNativeLevel6: true, cancellationToken: cancellationToken);
                        try
                        {
                            var decomp = await FastFileDecompressor.DecompressAutoAsync(vProbe, vVerify, cancellationToken);
                            vWrapper = decomp.Platform == WaWPlatform.Xbox360 &&
                                       decomp.Compression == WaWCompressionKind.XboxSingleZlib &&
                                       await FilesEqualAsync(vZone, vVerify, cancellationToken);
                        }
                        finally
                        {
                            try { File.Delete(vVerify); } catch { }
                        }
                    }

                    if (vWrapper)
                    {
                        vHardware = Path.Combine(variantFolder, Path.GetFileName(ff.Source));
                        File.Copy(vProbe, vHardware, overwrite: true);
                        vFastFileBytes = new FileInfo(vHardware).Length;
                        vSha = await Sha256Async(vHardware, cancellationToken);
                    }
                }

                var row = new TriageRow(
                    spec.Name, ff.Role, ff.Source, vHardware, vZoneBytes, vFastFileBytes,
                    vReadBack, vWrapper, variant.MissingCount, vSha, variant.Warnings);
                triageRows.Add(row);
                if (spec.Name == "00_baseline_singlezlib") baselineRow = row;
            }
        }

        foreach (var spec in triageSpecs)
        {
            var dir = Path.Combine(triageRoot, spec.Name);
            CopyDeploymentMetadata(map, dir);
        }

        var triageTxt = Path.Combine(workspace, $"{map.Name}_linker_v1_alpha4mp3_tracked_family_triage.txt");
        var triageJson = Path.Combine(workspace, $"{map.Name}_linker_v1_alpha4mp3_tracked_family_triage.json");
        var triageLines = new List<string>
        {
            "WaW360Converter — Linker V1 alpha 4MP4 tracked-family hardware triage matrix",
            new string('=', 100),
            $"Map: {map.Name}",
            "PURPOSE: isolate TU7 rejection to one restored Stage-4 family while keeping every other tracked family as the known smoke shell.",
            "All variants use Xbox big-endian 0x183 + single-zlib and independently round-trip their zone bytes.",
            "TEST ORDER: 00 baseline -> 05 safe-no-escape-all -> 01 xanim -> 02 xmodel -> 03 weapon -> 04 fx -> hardware_candidates_minplay (all tracked).",
            "If baseline itself fails, do not blame a tracked family; investigate the smoke zone/world/material contract first.",
            ""
        };
        foreach (var spec in triageSpecs)
        {
            var vr = triageRows.Where(x => x.Variant.Equals(spec.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var complete = rows.Count != 0 && vr.Count == rows.Count && vr.All(x => x.HardwareFastFile is not null);
            triageLines.Add($"[{spec.Name}] complete={complete}; ff={vr.Count(x => x.HardwareFastFile is not null)}/{rows.Count}");
            foreach (var x in vr)
            {
                triageLines.Add($"  {Path.GetFileName(x.Source)}: zone={x.ZoneBytes:N0}B; ff={x.FastFileBytes:N0}B; readBack={x.ZoneReadBackVerified}; wrapper={x.WrapperRoundTripVerified}; missing={x.MissingPayloads:N0}; sha256={(string.IsNullOrWhiteSpace(x.Sha256) ? "<none>" : x.Sha256)}");
                foreach (var w in x.Warnings.Where(w => !w.StartsWith("INFO:", StringComparison.OrdinalIgnoreCase)).Take(6))
                    triageLines.Add("    NOTE: " + w);
            }
        }
        await File.WriteAllLinesAsync(triageTxt, triageLines, Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(triageJson, JsonSerializer.Serialize(triageRows, PrettyJson()), Encoding.UTF8, cancellationToken);
        await File.WriteAllLinesAsync(Path.Combine(triageRoot, "TRIAGE_TEST_ORDER.txt"), new[]
        {
            "Alpha 4MP4 hardware triage test order",
            "1) 00_baseline_singlezlib",
            "2) 05_safe_no_escape_all",
            "3) 01_xanim_only",
            "4) 02_xmodel_only",
            "5) 03_weapon_only",
            "6) 04_fx_only",
            "7) ../hardware_candidates_minplay  (all tracked families restored)",
            "Replace the test map folder with ONE variant at a time. Stop at the first variant that changes the hardware result and send SEND_THIS_TO_CHATGPT.txt plus the observed checkpoint."
        }, Encoding.UTF8, cancellationToken);
        detail.Add($"TRIAGE_MATRIX: {triageTxt}");
        foreach (var line in triageLines.Skip(6)) detail.Add("  " + line);

        var completeHybrid = rows.Count != 0 && rows.All(x => x.SmokeHardwareCandidate);
        await WriteHybridReadmeAsync(hardwareRoot, map, rows, completeHybrid, cancellationToken);
        CopyDeploymentMetadata(map, hardwareRoot);

        var txt = Path.Combine(workspace, $"{map.Name}_linker_v1_alpha4mp_minimum_playable.txt");
        var json = Path.Combine(workspace, $"{map.Name}_linker_v1_alpha4mp_minimum_playable.json");
        var result = new NormalMapMinimumPlayableHybridEmitterResult
        {
            MapName = map.Name,
            Workspace = workspace,
            HardwareCandidateRoot = hardwareRoot,
            FastFiles = rows,
            FastFileCount = rows.Count,
            WorldRootsPromoted = rows.Sum(x => x.ClipMapsPromoted + x.GfxWorldsPromoted),
            SmokePayloadAssets = rows.Sum(x => x.SmokePayloadAssets),
            LegacyPackedSharedTargetsRetiredFromSmokePath = rows.Sum(x => x.LegacyPackedSharedTargetsRetiredFromSmokePath),
            MissingPhysicalPayloads = rows.Sum(x => x.MissingPhysicalPayloads),
            ReadBackErrors = rows.Sum(x => x.ReadBackErrors),
            WrapperRoundTripVerifiedFastFiles = rows.Count(x => x.WrapperRoundTripVerified),
            SmokeHardwareFastFilesEmitted = rows.Count(x => x.SmokeHardwareCandidate),
            CompleteSmokeSet = completeHybrid,
            Verified = rows.Count != 0 && rows.All(x => x.SmokeSelfContained && x.WrapperRoundTripVerified),
            StrictFidelityReady = rows.Count != 0 && rows.All(x => x.StrictFidelityReady),
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static async Task<VariantPayloadBuild> BuildVariantPayloadSetAsync(
        string outputRoot,
        string mapName,
        string sourcePath,
        string role,
        T4ZoneDocument doc,
        byte[] zone,
        IReadOnlySet<string> restoreTypes,
        bool requireZeroEscapes,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var payloads = new Dictionary<int, SmokePayload>();

        var tracked = await BuildTrackedHybridPayloadsAsync(
            outputRoot, mapName, sourcePath, role, doc, cancellationToken, restoreTypes, requireZeroEscapes);
        foreach (var p in tracked.Payloads.Values) AddPayload(payloads, p);
        warnings.AddRange(tracked.Warnings);

        var sound = T4SoundAliasSmokeSerializer.Build(doc, zone, cancellationToken);
        if (!sound.Verified)
            warnings.Add($"Sound smoke serializer is not verified ({sound.ParsedPhysicalSoundAssets:N0}/{sound.DeclaredPhysicalSoundAssets:N0}).");
        foreach (var s in sound.AssetsByIndex.Values)
            AddPayload(payloads, new SmokePayload(s.AssetIndex, "sound", s.AliasListName, s.XboxSilentStubPayload,
                "silent smoke SoundFile graph; final Xbox XMA/streamed audio remains pending"));

        var tech = T4TechniqueSetNullGraphSerializer.Build(doc);
        if (!tech.Verified)
            warnings.Add($"TechniqueSet smoke serializer is not verified ({tech.NativeSmokeTechniqueSets:N0}/{tech.DeclaredPhysicalTechniqueSets:N0}).");
        foreach (var tt in tech.AssetsByIndex.Values)
            AddPayload(payloads, new SmokePayload(tt.AssetIndex, "techset", tt.SmokeName, tt.XboxPayload,
                "51 Xbox technique graph slots are null; final Xenos shader/pass graphs remain pending"));

        var occupied = await BuildOccupiedSpansAsync(outputRoot, mapName, sourcePath, role, doc, zone, cancellationToken);
        var raw = T4RawFileOccupiedSpanSerializer.Build(doc, zone, occupied, cancellationToken);
        if (!raw.Verified)
            warnings.Add($"RawFile occupied-span accounting is not verified ({raw.StructuralRawFiles:N0}/{raw.DeclaredPhysicalRawFiles:N0}).");
        foreach (var r in raw.AssetsByIndex.Values.Where(x => x.NativePromotable && x.XboxPayload is not null))
            AddPayload(payloads, new SmokePayload(r.AssetIndex, "rawfile", r.Name, r.XboxPayload!, "original RawFile body preserved"));

        var foundation = T4SimpleNativeAssetSerializer.Build(doc, zone);
        foreach (var f in foundation.AssetsByIndex.Values.Where(x =>
                     x.TypeName.Equals("localize", StringComparison.OrdinalIgnoreCase) ||
                     x.TypeName.Equals("stringtable", StringComparison.OrdinalIgnoreCase)))
            AddPayload(payloads, new SmokePayload(f.AssetIndex, f.TypeName, f.Name, f.Payload,
                f.TypeName.Equals("localize", StringComparison.OrdinalIgnoreCase)
                    ? "localized key/value preserved"
                    : "stringtable payload preserved"));

        var tail = T4MaterialSimpleTailSmokeSerializer.Build(doc, zone, raw);
        if (!tail.Verified) warnings.Add("Material/simple-tail smoke serializer is not verified.");
        foreach (var x in tail.AssetsByIndex.Values)
            AddPayload(payloads, new SmokePayload(x.AssetIndex, x.TypeName, x.SmokeName, x.XboxPayload, x.FidelityObligation));

        var world = T4WorldRootSmokeSerializer.Build(doc);
        if (!world.Verified) warnings.Add("clipMap_t/GfxWorld smoke-root serializer is not verified.");
        foreach (var w in world.AssetsByIndex.Values)
            AddPayload(payloads, new SmokePayload(w.AssetIndex, w.TypeName, w.SmokeName, w.XboxPayload,
                w.TypeName.Equals("clipmap", StringComparison.OrdinalIgnoreCase)
                    ? "reduced clipMap_t has no collision/static-model/dynent data"
                    : "reduced GfxWorld has no render/light/grid/surface data"));

        var physical = doc.Assets
            .Where(a => a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .OrderBy(a => a.Index)
            .ToList();
        var unsupported = physical
            .Where(a => !SmokeSupportedTypes.Contains(a.TypeName))
            .GroupBy(a => a.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var kv in unsupported)
            warnings.Add($"Unsupported smoke XAsset family '{kv.Key}' x{kv.Value:N0}.");

        var missing = physical.Count(a => !payloads.ContainsKey(a.Index));
        if (missing != 0) warnings.Add($"Missing self-contained payloads={missing:N0}.");
        var aliases = doc.Assets.Count(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias);
        if (aliases != 0) warnings.Add($"Top-level packed/alias XAssets={aliases:N0}.");

        var canBuild = missing == 0 && unsupported.Count == 0 && aliases == 0 &&
                       sound.Verified && tech.Verified && raw.Verified && tail.Verified && world.Verified;
        return new VariantPayloadBuild(payloads, physical.Count, missing, aliases, canBuild, warnings);
    }

    private static async Task<TrackedHybridBuild> BuildTrackedHybridPayloadsAsync(
        string outputRoot,
        string mapName,
        string sourcePath,
        string role,
        T4ZoneDocument doc,
        CancellationToken ct,
        IReadOnlySet<string>? restoreTypes = null,
        bool requireZeroEscapes = false)
    {
        var output = new Dictionary<int, SmokePayload>();
        var warnings = new List<string>();
        var fallbackNames = 0;
        var nulledPackedRootPointers = 0;
        long fullProofBytes = 0;
        var restoredAssets = 0;

        foreach (var type in TrackedTypes)
        {
            var assets = doc.Assets
                .Where(a => a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                            a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                .OrderBy(a => a.Index)
                .ToList();
            if (assets.Count == 0) continue;

            var proofPath = GetProofPath(outputRoot, mapName, sourcePath, role, type);
            var proof = await T4NativeProofContainerReader.IndexAsync(proofPath, type, ct);
            if (proof.Records.Count != assets.Count)
                throw new InvalidDataException($"alpha 4MP tracked-proof mismatch for {Path.GetFileName(sourcePath)} {type}: expected {assets.Count:N0}, proof={proof.Records.Count:N0}.");

            var fixedSize = type.ToLowerInvariant() switch
            {
                "xanim" => 88,
                "xmodel" => 228,
                "weapon" => 0x9AC,
                "fx" => 36,
                _ => throw new InvalidOperationException()
            };
            var restore = restoreTypes is null || restoreTypes.Contains(type);

            for (var i = 0; i < assets.Count; i++)
            {
                var record = proof.Records[i];
                var blob = await T4NativeProofContainerReader.ReadBlobAsync(record, ct);
                if (blob.Length < fixedSize)
                    throw new InvalidDataException($"alpha 4MP proof blob for {type} XAsset#{assets[i].Index} is shorter than fixed body ({blob.Length:N0} < {fixedSize:N0}).");

                var name = record.Name;
                if (string.IsNullOrWhiteSpace(name))
                    name = TryReadInlineProofName(blob, fixedSize);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"__waw360_{type}_{assets[i].Index:D5}";
                    fallbackNames++;
                }

                if (!restore)
                {
                    output[assets[i].Index] = new SmokePayload(
                        assets[i].Index, type, name!, BuildZeroOwnedShell(fixedSize, name!),
                        $"triage reduced {type} shell; owned child data intentionally zeroed");
                    continue;
                }

                var rootName = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(0, 4));
                if (rootName is not (T4ZonePointer.Following or T4ZonePointer.Insert))
                {
                    blob = InsertInlineRootName(blob, fixedSize, name!);
                }

                var nulled = NullEscapingPackedRootPointers(blob, type);
                if (requireZeroEscapes && nulled != 0)
                {
                    output[assets[i].Index] = new SmokePayload(
                        assets[i].Index, type, name!, BuildZeroOwnedShell(fixedSize, name!),
                        $"safe triage fallback: {type} proof needed {nulled:N0} escaping packed root pointer null(s), so this asset stayed a reduced shell");
                    continue;
                }
                nulledPackedRootPointers += nulled;
                fullProofBytes += blob.LongLength;
                restoredAssets++;
                output[assets[i].Index] = new SmokePayload(
                    assets[i].Index, type, name!, blob,
                    nulled == 0
                        ? $"full verified Stage-4 {type} proof payload preserved"
                        : $"full verified Stage-4 {type} proof payload preserved; {nulled:N0} packed root reference(s) nulled for standalone hybrid safety");
            }
        }

        var restoreLabel = restoreTypes is null ? "ALL" : string.Join(",", restoreTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        warnings.Add($"INFO: alpha 4MP tracked restore set={restoreLabel}; requireZeroEscapes={requireZeroEscapes}; restored={restoredAssets:N0}; fullProofBytes={fullProofBytes:N0}; packed root references nulled={nulledPackedRootPointers:N0}; fallback names={fallbackNames:N0}.");
        if (restoredAssets != 0)
            warnings.Add("INFO: nested packed/shared references inside owned proof subgraphs are not globally rebased yet; restored-family variants are hardware diagnostics, not strict fidelity.");
        return new TrackedHybridBuild(output, fallbackNames, warnings);
    }

    private static byte[] InsertInlineRootName(byte[] blob, int fixedSize, string name)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var output = new byte[checked(blob.Length + n.Length + 1)];
        Buffer.BlockCopy(blob, 0, output, 0, fixedSize);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0, 4), T4ZonePointer.Following);
        Buffer.BlockCopy(n, 0, output, fixedSize, n.Length);
        Buffer.BlockCopy(blob, fixedSize, output, fixedSize + n.Length + 1, blob.Length - fixedSize);
        return output;
    }

    private static int NullEscapingPackedRootPointers(byte[] blob, string type)
    {
        var nulled = 0;
        foreach (var field in NormalMapNativeDestinationLayout.PointerFieldsFor(type))
        {
            if (field.Offset == 0 || field.Offset < 0 || field.Offset > blob.Length - 4) continue;
            var raw = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(field.Offset, 4));
            if (raw == 0 || raw == T4ZonePointer.Following || raw == T4ZonePointer.Insert) continue;
            BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(field.Offset, 4), 0u);
            nulled++;
        }
        return nulled;
    }

    private static string? TryReadInlineProofName(ReadOnlySpan<byte> prefix, int fixedSize)
    {
        if (prefix.Length <= fixedSize || prefix.Length < 4) return null;
        var ptr = BinaryPrimitives.ReadUInt32BigEndian(prefix.Slice(0, 4));
        if (ptr is not (T4ZonePointer.Following or T4ZonePointer.Insert)) return null;
        var end = fixedSize;
        var limit = Math.Min(prefix.Length, fixedSize + 1024);
        while (end < limit && prefix[end] != 0) end++;
        if (end == fixedSize || end >= limit) return null;
        for (var i = fixedSize; i < end; i++)
            if (prefix[i] is < 0x20 or > 0x7E) return null;
        return Encoding.ASCII.GetString(prefix.Slice(fixedSize, end - fixedSize));
    }

    private static byte[] BuildZeroOwnedShell(int fixedSize, string name)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var payload = new byte[fixedSize + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), T4ZonePointer.Following);
        n.CopyTo(payload.AsSpan(fixedSize));
        return payload;
    }

    private static void AddPayload(Dictionary<int, SmokePayload> output, SmokePayload payload)
    {
        if (output.TryGetValue(payload.AssetIndex, out var existing))
            throw new InvalidDataException($"alpha 4MP duplicate payload for XAsset#{payload.AssetIndex}: {existing.TypeName} vs {payload.TypeName}.");
        output.Add(payload.AssetIndex, payload);
    }

    private static async Task<EmitStats> EmitSmokeZoneAsync(
        string path,
        T4ZoneDocument source,
        IReadOnlyDictionary<int, SmokePayload> payloads,
        CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Header is patched after the final size is known. For this reduced smoke path every block
        // receives a capacity equal to the complete aligned zone size; that is intentionally
        // over-provisioned but prevents a reduced serializer from under-allocating a T4 stream block.
        for (var i = 0; i < 9; i++) WriteU32(fs, 0);

        WriteU32(fs, checked((uint)source.ScriptStringCount));
        WriteU32(fs, source.ScriptStringCount == 0 ? 0u : T4ZonePointer.Following);
        WriteU32(fs, checked((uint)source.AssetCount));
        WriteU32(fs, source.AssetCount == 0 ? 0u : T4ZonePointer.Following);

        var inlineStrings = new List<byte[]>();
        var scriptAliases = 0;
        foreach (var s in source.ScriptStrings)
        {
            if (s.Inline && s.Value is not null)
            {
                WriteU32(fs, T4ZonePointer.Following);
                inlineStrings.Add(Encoding.UTF8.GetBytes(s.Value + "\0"));
            }
            else if (s.RawPointer == 0)
            {
                WriteU32(fs, 0);
            }
            else
            {
                WriteU32(fs, 0);
                scriptAliases++;
            }
        }
        foreach (var bytes in inlineStrings) await fs.WriteAsync(bytes, ct);

        var assetAliases = 0;
        foreach (var asset in source.Assets.OrderBy(a => a.Index))
        {
            WriteU32(fs, checked((uint)T4XboxAssetTypeMap.GetXboxTypeId(asset.TypeName)));
            var header = asset.ReferenceKind switch
            {
                T4AssetReferenceKind.Null => 0u,
                T4AssetReferenceKind.InlineShared => T4ZonePointer.Following,
                T4AssetReferenceKind.Inline => T4ZonePointer.Insert,
                _ => 0u
            };
            if (asset.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias) assetAliases++;
            WriteU32(fs, header);
        }

        var written = 0;
        foreach (var asset in source.Assets.OrderBy(a => a.Index))
        {
            if (asset.ReferenceKind is not (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)) continue;
            if (!payloads.TryGetValue(asset.Index, out var payload))
                throw new InvalidDataException($"alpha 4MP emitter has no payload for XAsset#{asset.Index} {asset.TypeName}.");
            await fs.WriteAsync(payload.Bytes.AsMemory(), ct);
            written++;
        }

        await fs.FlushAsync(ct);
        if (fs.Length > uint.MaxValue)
            throw new InvalidDataException("alpha 4MP hybrid zone exceeds T4's 32-bit zoneSize field.");

        var zoneBytes = checked((uint)fs.Length);
        var blockCapacity = Align16(zoneBytes);
        fs.Seek(0, SeekOrigin.Begin);
        WriteU32(fs, zoneBytes);
        WriteU32(fs, 0);
        foreach (var _ in Enum.GetValues<T4ZoneBlock>()) WriteU32(fs, blockCapacity);
        await fs.FlushAsync(ct);

        return new EmitStats(fs.Length, scriptAliases, assetAliases, written);
    }

    private static async Task<ReadBackStats> ReadBackSmokeZoneAsync(
        string path,
        T4ZoneDocument source,
        IReadOnlyDictionary<int, SmokePayload> payloads,
        CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var errors = 0;
        T4ZoneDocument read;
        try
        {
            read = T4ZoneParser.ParseZoneBytes(path, bytes, WaWPlatform.Xbox360, WaWCompressionKind.XboxBlockDeflate, Endian.Big);
        }
        catch
        {
            return new ReadBackStats(1, 0);
        }

        if (read.AssetCount != source.AssetCount || read.ScriptStringCount != source.ScriptStringCount) errors++;
        if (read.Header.ZoneSize != bytes.LongLength) errors++;
        if (read.Header.BlockSizes.Any(x => x < read.Header.ZoneSize)) errors++;

        for (var i = 0; i < Math.Min(read.ScriptStrings.Count, source.ScriptStrings.Count); i++)
        {
            var expected = source.ScriptStrings[i];
            var actual = read.ScriptStrings[i];
            if (expected.Inline && expected.Value is not null)
            {
                if (!actual.Inline || !string.Equals(expected.Value, actual.Value, StringComparison.Ordinal)) errors++;
            }
            else if (expected.RawPointer == 0)
            {
                if (actual.RawPointer != 0) errors++;
            }
            else if (actual.RawPointer != 0)
            {
                errors++;
            }
        }

        for (var i = 0; i < Math.Min(read.Assets.Count, source.Assets.Count); i++)
        {
            var s = source.Assets[i];
            var a = read.Assets[i];
            if (!a.TypeName.Equals(s.TypeName, StringComparison.OrdinalIgnoreCase)) errors++;
            var expectedHeader = s.ReferenceKind switch
            {
                T4AssetReferenceKind.Null => 0u,
                T4AssetReferenceKind.InlineShared => T4ZonePointer.Following,
                T4AssetReferenceKind.Inline => T4ZonePointer.Insert,
                _ => 0u
            };
            if (a.RawHeader != expectedHeader) errors++;
        }

        var cursor = checked((int)(read.AssetTableOffset + (long)read.AssetCount * 8L));
        var payloadCount = 0;
        foreach (var asset in source.Assets.OrderBy(a => a.Index))
        {
            if (asset.ReferenceKind is not (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)) continue;
            if (!payloads.TryGetValue(asset.Index, out var payload))
            {
                errors++;
                continue;
            }
            if (cursor < 0 || cursor > bytes.Length - payload.Bytes.Length)
            {
                errors++;
                break;
            }
            if (!bytes.AsSpan(cursor, payload.Bytes.Length).SequenceEqual(payload.Bytes)) errors++;
            cursor += payload.Bytes.Length;
            payloadCount++;
        }
        if (cursor != bytes.Length) errors++;
        return new ReadBackStats(errors, payloadCount);
    }

    private static async Task<List<(long Start, long End, string Owner)>> BuildOccupiedSpansAsync(
        string outputRoot,
        string mapName,
        string sourcePath,
        string role,
        T4ZoneDocument doc,
        byte[] zone,
        CancellationToken cancellationToken)
    {
        var spans = new List<(long Start, long End, string Owner)>();
        foreach (var type in TrackedTypes)
        {
            var needed = doc.Assets.Count(a => a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                                               a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared);
            if (needed == 0) continue;
            var path = GetProofPath(outputRoot, mapName, sourcePath, role, type);
            var proof = await T4NativeProofContainerReader.IndexAsync(path, type, cancellationToken);
            if (proof.Records.Count != needed)
                throw new InvalidDataException($"10r occupied-span proof mismatch for {Path.GetFileName(sourcePath)} {type}: expected {needed:N0}, proof={proof.Records.Count:N0}.");
            spans.AddRange(proof.Records.Select(r => (r.SourceOffset, r.SourceOffset + (long)r.BlobLength, $"{type} proof")));
        }

        var sound = T4SoundAliasSmokeSerializer.Build(doc, zone, cancellationToken);
        if (sound.DeclaredPhysicalSoundAssets != 0 && !sound.Verified)
            throw new InvalidDataException($"10r requires verified Sound source spans for {Path.GetFileName(sourcePath)}.");
        spans.AddRange(sound.AssetsByIndex.Values.Select(s => (s.SourceOffset, s.SourceEndOffset, "sound proof")));
        return spans.OrderBy(x => x.Item1).Select(x => (x.Item1, x.Item2, x.Item3)).ToList();
    }

    private static string GetProofPath(string outputRoot, string mapName, string sourcePath, string role, string type)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath) + $"_{Sanitize(role)}";
        return type.ToLowerInvariant() switch
        {
            "xanim" => Path.Combine(outputRoot, mapName, "stage4_alpha4_xanim", stem + ".xbox_xanim_proof.bin"),
            "xmodel" => Path.Combine(outputRoot, mapName, "stage4_alpha9g_xmodel", stem + ".xbox_xmodel_proof.bin"),
            "weapon" => Path.Combine(outputRoot, mapName, "stage4_alpha7b_weapon_owned", stem + ".xbox_weapon_owned_proof.bin"),
            "fx" => Path.Combine(outputRoot, mapName, "stage4_alpha9b_fx", stem + ".xbox_fx_proof.bin"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private static async Task WriteHybridReadmeAsync(
        string hardwareRoot,
        MapCandidate map,
        IReadOnlyList<T4MinimumPlayableFastFile> rows,
        bool complete,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V1 ALPHA 4MP MINIMUM-PLAYABLE HYBRID CANDIDATE");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"Map: {map.Name}");
        sb.AppendLine($"Complete fastfile set emitted: {complete} ({rows.Count(x => x.SmokeHardwareCandidate)}/{rows.Count})");
        sb.AppendLine();
        sb.AppendLine("PURPOSE");
        sb.AppendLine("  Test whether the Xbox 360 can parse/open the reconstructed native FF/zone/XAsset graph.");
        sb.AppendLine();
        sb.AppendLine("ALPHA 4MP CONTENT POLICY");
        sb.AppendLine("  - XAnim/XModel/Weapon/FX now use the COMPLETE verified Stage-4 Xbox proof payloads, including inline-owned child data.");
        sb.AppendLine("  - Escaping packed references in tracked root structs are nulled; nested shared references remain an experimental risk.");
        sb.AppendLine("  - Sound is still silent structural data; source PC audio is not treated as Xbox audio.");
        sb.AppendLine("  - TechniqueSet shader/pass pointers are still null; PC D3D bytecode is never copied as Xenos code.");
        sb.AppendLine("  - Top-level Materials are still reduced smoke objects.");
        sb.AppendLine("  - clipMap_t/GfxWorld/ComWorld/GameWorldSp world data is still reduced/empty.");
        sb.AppendLine("  - RawFile/script bodies are preserved where byte ownership was proven.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT");
        sb.AppendLine("  This is NOT a fidelity-ready converted map. World rendering/collision, shaders/materials and audio are still incomplete.");
        sb.AppendLine("  Compared with 10r smoke, this build should be dramatically larger because real tracked asset payloads are restored.");
        sb.AppendLine("  A crash/load error is still useful hardware evidence: save xbWatson/RGH logs and the exact on-screen result.");
        sb.AppendLine();
        sb.AppendLine("GENERATED FILE SIZE CHECK");
        foreach (var row in rows)
        {
            var srcBytes = File.Exists(row.Source) ? new FileInfo(row.Source).Length : 0;
            var outBytes = row.HardwareFastFile is not null && File.Exists(row.HardwareFastFile) ? new FileInfo(row.HardwareFastFile).Length : 0;
            var ratio = srcBytes > 0 ? 100.0 * outBytes / srcBytes : 0.0;
            var check = outBytes <= 12 ? "REVIEW_EMPTY_OR_HEADER_ONLY" : srcBytes >= 1024L * 1024L && outBytes < 256L * 1024L ? "REVIEW_TOO_SMALL" : "OK_INFO_ONLY";
            sb.AppendLine($"  {Path.GetFileName(row.Source)}: source={srcBytes:N0} B ({srcBytes / (1024.0 * 1024.0):F3} MiB); zone={row.ZoneImageBytes:N0} B ({row.ZoneImageBytes / (1024.0 * 1024.0):F3} MiB); output={outBytes:N0} B ({outBytes / (1024.0 * 1024.0):F3} MiB); output/source={ratio:F2}%; sizeCheck={check}");
        }
        await File.WriteAllTextAsync(Path.Combine(hardwareRoot, "ALPHA4MP_README.txt"), sb.ToString(), Encoding.UTF8, ct);
    }

    private static void CopyDeploymentMetadata(MapCandidate map, string hardwareRoot)
    {
        Directory.CreateDirectory(hardwareRoot);
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".arena", ".iwd", ".json", ".dds"
        };

        foreach (var folder in map.SourceFolders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var source in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                if (!allowed.Contains(Path.GetExtension(source))) continue;
                var name = Path.GetFileName(source);
                if (!copied.Add(name)) continue;
                File.Copy(source, Path.Combine(hardwareRoot, name), overwrite: true);
            }
        }
    }

    private static async Task<bool> FilesEqualAsync(string a, string b, CancellationToken ct)
    {
        var ai = new FileInfo(a);
        var bi = new FileInfo(b);
        if (ai.Length != bi.Length) return false;
        await using var x = File.OpenRead(a);
        await using var y = File.OpenRead(b);
        var xb = new byte[1024 * 1024];
        var yb = new byte[xb.Length];
        while (true)
        {
            var xn = await x.ReadAsync(xb.AsMemory(), ct);
            var yn = await y.ReadAsync(yb.AsMemory(), ct);
            if (xn != yn) return false;
            if (xn == 0) return true;
            if (!xb.AsSpan(0, xn).SequenceEqual(yb.AsSpan(0, yn))) return false;
        }
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(fs, ct)).ToLowerInvariant();
    }

    private static uint Align16(uint value)
    {
        var aligned = (value + 15u) & ~15u;
        return aligned < value ? uint.MaxValue & ~15u : aligned;
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }

    private static string Sanitize(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars).Trim('_').ToLowerInvariant();
    }

    private static string Render(NormalMapMinimumPlayableHybridEmitterResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Linker V1 alpha 4MP4 minimum-playable hybrid emitter");
        sb.AppendLine(new string('=', 104));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Fastfiles: {r.FastFileCount:N0}");
        sb.AppendLine($"World roots promoted: {r.WorldRootsPromoted:N0}");
        sb.AppendLine($"Hybrid XAsset payloads: {r.SmokePayloadAssets:N0}");
        sb.AppendLine($"Legacy packed/shared targets bypassed by hybrid path: {r.LegacyPackedSharedTargetsRetiredFromSmokePath:N0}");
        sb.AppendLine($"Missing physical hybrid payloads: {r.MissingPhysicalPayloads:N0}");
        sb.AppendLine($"Independent zone read-back errors: {r.ReadBackErrors:N0}");
        sb.AppendLine($"Xbox FF wrapper round-trips: {r.WrapperRoundTripVerifiedFastFiles:N0}/{r.FastFileCount:N0}");
        sb.AppendLine($"Hardware hybrid FFs emitted: {r.SmokeHardwareFastFilesEmitted:N0}/{r.FastFileCount:N0}");
        sb.AppendLine($"COMPLETE HYBRID SET: {r.CompleteSmokeSet}");
        sb.AppendLine($"Verified hybrid emission: {r.Verified}");
        sb.AppendLine($"Strict fidelity ready: {r.StrictFidelityReady}");
        sb.AppendLine($"Hardware hybrid folder: {r.HardwareCandidateRoot}");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: alpha 4MP3 restores complete Stage-4 XAnim/XModel/Weapon/FX proof payloads but still uses reduced world/material/techset/sound assets. It does not 'resolve' the old packed/shared graph in-place; it retires that graph from the smoke path by rebuilding every physical XAsset as a self-contained Xbox-endian object. This is legitimate for a parser/loader smoke test but NOT equivalent to full-fidelity conversion. Keep the strict path fail-closed until real audio, shader graphs/materials and world data are restored.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
