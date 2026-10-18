using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4IntegratedSmokeFastFile(
    string Role,
    string Source,
    int AssetCount,
    int SmokePayloadAssets,
    IReadOnlyDictionary<string, int> SmokePayloadsByType,
    int ClipMapsPromoted,
    int GfxWorldsPromoted,
    int ReducedTrackedShells,
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

public sealed class NormalMapWorldRootIntegratedSmokeEmitterResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required string HardwareCandidateRoot { get; init; }
    public required IReadOnlyList<T4IntegratedSmokeFastFile> FastFiles { get; init; }
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
/// Stage 5v / alpha 10r: first integrated reduced-content Xbox hardware-smoke emitter.
///
/// 10r is intentionally different from the strict fidelity path. It replaces every physical
/// source XAsset with an independently constructed, self-contained Xbox-endian payload so the
/// older 856 packed/shared target graph is no longer copied into the SMOKE zone at all. This is
/// how the first parser/loader hardware candidate can move forward without pretending those old
/// PC nested pointers were resolved.
///
/// The reduced smoke path preserves script/rawfile bodies and as many asset names as proven,
/// but deliberately uses silent Sound, null-graph TechniqueSet, inert Material/world roots and
/// zero-owned-data shells for XAnim/XModel/Weapon/FX. Therefore a 10r hardware candidate tests
/// native FF/zone/XAsset loading only; it is NOT a claim of gameplay, audio or visual fidelity.
/// </summary>
public static class NormalMapWorldRootIntegratedSmokeEmitter
{
    private static readonly string[] TrackedTypes = ["xanim", "xmodel", "weapon", "fx"];
    private static readonly HashSet<string> SmokeSupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "xanim", "xmodel", "weapon", "fx",
        "sound", "rawfile", "localize", "stringtable",
        "techset", "material", "comworld", "gameworld_sp", "clipmap", "gfxworld"
    };

    private sealed record SmokePayload(int AssetIndex, string TypeName, string Name, byte[] Bytes, string Obligation);
    private sealed record TrackedShellBuild(IReadOnlyDictionary<int, SmokePayload> Payloads, int FallbackNames, IReadOnlyList<string> Warnings);
    private sealed record EmitStats(long Bytes, int ScriptAliasesPending, int AssetAliasesPending, int PayloadAssets);
    private sealed record ReadBackStats(int Errors, int PayloadAssets);

    public static async Task<NormalMapWorldRootIntegratedSmokeEmitterResult> RunAsync(
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
        // Alpha 4MP3 diagnostic continuation: preserve per-FF blockers instead of cascading a
        // single upstream mismatch into a whole-map hard failure. canBuild below still requires
        // every serializer used by that FF to verify before any hardware candidate is emitted.
        var tailAccountingGloballyVerified = prior.Verified;

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10r_world_integrated_smoke_emitter");
        Directory.CreateDirectory(workspace);
        var zoneRoot = Path.Combine(workspace, "zones");
        var probeRoot = Path.Combine(workspace, "ff_probes");
        var hardwareRoot = Path.Combine(outputRoot, map.Name, "hardware_candidates_smoke");
        Directory.CreateDirectory(zoneRoot);
        Directory.CreateDirectory(probeRoot);
        Directory.CreateDirectory(hardwareRoot);

        // Never let a failed rerun look successful because an older smoke FF is still present.
        foreach (var ff in prior.FastFiles)
        {
            var stale = Path.Combine(hardwareRoot, Path.GetFileName(ff.Source));
            try { if (File.Exists(stale)) File.Delete(stale); } catch { }
        }
        try { File.Delete(Path.Combine(hardwareRoot, "SMOKE_ONLY_README.txt")); } catch { }

        var rows = new List<T4IntegratedSmokeFastFile>();
        var detail = new List<string>();
        if (!tailAccountingGloballyVerified) detail.Add("DIAGNOSTIC CONTINUATION: upstream Material/simple-tail accounting is partial/unverified; this stage continues per fastfile but hardware emission remains fail-closed for affected FFs.");

        foreach (var ff in prior.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            var doc = cached.Document;
            var zone = cached.Bytes;

            var warnings = new List<string>();
            var payloads = new Dictionary<int, SmokePayload>();

            // Stage-4 tracked types become name-preserving, zero-owned-data shells. This is the
            // crucial smoke-only replacement that retires the old packed child-pointer graph.
            var tracked = await BuildTrackedShellsAsync(outputRoot, map.Name, ff.Source, ff.Role, doc, cancellationToken);
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
            var zonePath = Path.Combine(zoneRoot, stem + ".a10r_smoke.zone");
            var probePath = Path.Combine(probeRoot, stem + ".a10r_smoke.ffprobe");
            var verifyPath = Path.Combine(probeRoot, stem + ".a10r_verify.zone");

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
                    await FastFileRepacker.RepackZoneFileAsync(ff.Source, zonePath, probePath,
                        WaWCompressionKind.XboxBlockDeflate, cancellationToken);
                    try
                    {
                        var decomp = await FastFileDecompressor.DecompressAutoAsync(probePath, verifyPath, cancellationToken);
                        wrapperOk = decomp.Platform == WaWPlatform.Xbox360 &&
                                    decomp.Compression == WaWCompressionKind.XboxBlockDeflate &&
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

            var reducedTracked = TrackedTypes.Sum(type => byType.TryGetValue(type, out var n) ? n : 0);
            var reducedMaterials = byType.TryGetValue("material", out var matCount) ? matCount : 0;
            var smokeCandidate = hardwareFile is not null;
            var strictFidelityReady = ff.StrictHardwareReady && obligations.Count == 0;

            rows.Add(new T4IntegratedSmokeFastFile(
                ff.Role, ff.Source,
                doc.AssetCount,
                payloads.Count,
                byType,
                world.PromotedClipMaps,
                world.PromotedGfxWorlds,
                reducedTracked,
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
                smokeCandidate,
                hardwareFile,
                hardwareSha,
                strictFidelityReady,
                obligations,
                warnings));

            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: world=clip {world.PromotedClipMaps:N0}/{world.DeclaredClipMaps:N0}, gfx {world.PromotedGfxWorlds:N0}/{world.DeclaredGfxWorlds:N0}; payloads={payloads.Count:N0}/{physical.Count:N0}; missing={missing.Count:N0}; trackedShells={reducedTracked:N0}; nameFallbacks={tracked.FallbackNames:N0}; legacySharedRetiredForSmoke={ff.StrictNestedSharedTargetsPending:N0}; legacyNestedRetiredForSmoke={ff.StrictNestedRebasePending}; scriptAliasPending={emit.ScriptAliasesPending:N0}; readBackErrors={audit.Errors:N0}; wrapperRoundTrip={wrapperOk}; smokeFF={(hardwareFile is null ? "NO" : hardwareFile)}; strictFidelity={strictFidelityReady}");
            foreach (var kv in byType) detail.Add($"  PAYLOAD {kv.Key}: {kv.Value:N0}");
            foreach (var warning in warnings.Take(24)) detail.Add("  NOTE: " + warning);
        }

        var completeSmoke = rows.Count != 0 && rows.All(x => x.SmokeHardwareCandidate);
        await WriteSmokeReadmeAsync(hardwareRoot, map, rows, completeSmoke, cancellationToken);
        CopyDeploymentMetadata(map, hardwareRoot);

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10r_world_integrated_smoke_emitter.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10r_world_integrated_smoke_emitter.json");
        var result = new NormalMapWorldRootIntegratedSmokeEmitterResult
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
            CompleteSmokeSet = completeSmoke,
            Verified = rows.Count != 0 && rows.All(x => x.SmokeSelfContained && x.WrapperRoundTripVerified),
            StrictFidelityReady = rows.Count != 0 && rows.All(x => x.StrictFidelityReady),
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static async Task<TrackedShellBuild> BuildTrackedShellsAsync(
        string outputRoot,
        string mapName,
        string sourcePath,
        string role,
        T4ZoneDocument doc,
        CancellationToken ct)
    {
        var output = new Dictionary<int, SmokePayload>();
        var warnings = new List<string>();
        var fallbackNames = 0;

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
                throw new InvalidDataException($"10r tracked-shell proof mismatch for {Path.GetFileName(sourcePath)} {type}: expected {assets.Count:N0}, proof={proof.Records.Count:N0}.");

            var fixedSize = type.ToLowerInvariant() switch
            {
                "xanim" => 88,
                "xmodel" => 228,
                "weapon" => 0x9AC,
                "fx" => 36,
                _ => throw new InvalidOperationException()
            };

            for (var i = 0; i < assets.Count; i++)
            {
                var record = proof.Records[i];
                var name = record.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    var prefix = await T4NativeProofContainerReader.ReadBlobPrefixAsync(
                        record, Math.Min(record.BlobLength, fixedSize + 2048), ct);
                    name = TryReadInlineProofName(prefix, fixedSize);
                }
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"__waw360_{type}_{assets[i].Index:D5}";
                    fallbackNames++;
                    warnings.Add($"{type} XAsset#{assets[i].Index}: source proof uses a packed/unresolved name; deterministic smoke name '{name}' substituted.");
                }

                var shell = BuildZeroOwnedShell(fixedSize, name);
                output[assets[i].Index] = new SmokePayload(
                    assets[i].Index, type, name, shell,
                    $"reduced {type} shell preserves name where proven but zeros owned child data; final {type} fidelity remains pending");
            }
        }

        return new TrackedShellBuild(output, fallbackNames, warnings);
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
            throw new InvalidDataException($"10r duplicate smoke payload for XAsset#{payload.AssetIndex}: {existing.TypeName} vs {payload.TypeName}.");
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
                throw new InvalidDataException($"10r emitter has no smoke payload for XAsset#{asset.Index} {asset.TypeName}.");
            await fs.WriteAsync(payload.Bytes.AsMemory(), ct);
            written++;
        }

        await fs.FlushAsync(ct);
        if (fs.Length > uint.MaxValue)
            throw new InvalidDataException("10r smoke zone exceeds T4's 32-bit zoneSize field.");

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

    private static async Task WriteSmokeReadmeAsync(
        string hardwareRoot,
        MapCandidate map,
        IReadOnlyList<T4IntegratedSmokeFastFile> rows,
        bool complete,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter alpha 10r — REDUCED-CONTENT XBOX HARDWARE SMOKE CANDIDATE");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"Map: {map.Name}");
        sb.AppendLine($"Complete fastfile set emitted: {complete} ({rows.Count(x => x.SmokeHardwareCandidate)}/{rows.Count})");
        sb.AppendLine();
        sb.AppendLine("PURPOSE");
        sb.AppendLine("  Test whether the Xbox 360 can parse/open the reconstructed native FF/zone/XAsset graph.");
        sb.AppendLine();
        sb.AppendLine("INTENTIONAL SMOKE REDUCTIONS");
        sb.AppendLine("  - Sound is silent structural data; source PC audio is not treated as Xbox audio.");
        sb.AppendLine("  - TechniqueSet shader/pass pointers are null; PC D3D bytecode is never copied as Xenos code.");
        sb.AppendLine("  - Materials are inert smoke objects.");
        sb.AppendLine("  - clipMap_t/GfxWorld/ComWorld/GameWorldSp world data is reduced/empty.");
        sb.AppendLine("  - XAnim/XModel/Weapon/FX use name-preserving zero-owned-data smoke shells.");
        sb.AppendLine("  - RawFile/script bodies are preserved where byte ownership was proven.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT");
        sb.AppendLine("  This is NOT a fidelity-ready converted map. Missing graphics/audio/collision/gameplay behavior is expected.");
        sb.AppendLine("  A crash/load error is still useful hardware evidence: save xbWatson/RGH logs and the exact on-screen result.");
        await File.WriteAllTextAsync(Path.Combine(hardwareRoot, "SMOKE_ONLY_README.txt"), sb.ToString(), Encoding.UTF8, ct);
    }

    private static void CopyDeploymentMetadata(MapCandidate map, string hardwareRoot)
    {
        foreach (var folder in map.SourceFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var arena = Path.Combine(folder, "mod.arena");
            if (!File.Exists(arena)) continue;
            File.Copy(arena, Path.Combine(hardwareRoot, "mod.arena"), overwrite: true);
            break;
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

    private static string Render(NormalMapWorldRootIntegratedSmokeEmitterResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5v alpha 10r world-root + integrated Xbox hardware-smoke emitter");
        sb.AppendLine(new string('=', 104));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Fastfiles: {r.FastFileCount:N0}");
        sb.AppendLine($"World roots promoted: {r.WorldRootsPromoted:N0}");
        sb.AppendLine($"Self-contained smoke XAsset payloads: {r.SmokePayloadAssets:N0}");
        sb.AppendLine($"Legacy packed/shared targets retired from SMOKE path: {r.LegacyPackedSharedTargetsRetiredFromSmokePath:N0}");
        sb.AppendLine($"Missing physical smoke payloads: {r.MissingPhysicalPayloads:N0}");
        sb.AppendLine($"Independent zone read-back errors: {r.ReadBackErrors:N0}");
        sb.AppendLine($"Xbox FF wrapper round-trips: {r.WrapperRoundTripVerifiedFastFiles:N0}/{r.FastFileCount:N0}");
        sb.AppendLine($"Hardware smoke FFs emitted: {r.SmokeHardwareFastFilesEmitted:N0}/{r.FastFileCount:N0}");
        sb.AppendLine($"COMPLETE SMOKE SET: {r.CompleteSmokeSet}");
        sb.AppendLine($"Verified smoke emission: {r.Verified}");
        sb.AppendLine($"Strict fidelity ready: {r.StrictFidelityReady}");
        sb.AppendLine($"Hardware smoke folder: {r.HardwareCandidateRoot}");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: 10r is the first reduced-content hardware SMOKE emitter. It does not 'resolve' the old packed/shared graph in-place; it retires that graph from the smoke path by rebuilding every physical XAsset as a self-contained Xbox-endian object. This is legitimate for a parser/loader smoke test but NOT equivalent to full-fidelity conversion. Keep the strict path fail-closed until real audio, shader graphs, materials, tracked child data and world data are restored.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
