using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed class NormalMapLinkerV2StructuralMainEmitResult
{
    public required string MapName { get; init; }
    public required string CanonicalBaseName { get; init; }
    public required string? OutputFastFile { get; init; }
    public required string? OutputZone { get; init; }
    public required string? HardwarePackageDirectory { get; init; }
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

        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_main_structural");
        var packageDir = Path.Combine(outputRoot, map.Name, "hardware_test_main_structural");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(packageDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_main_structural.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_main_structural.json");
        var zonePath = Path.Combine(outDir, source.CanonicalBaseName + ".zone");
        var ffPath = Path.Combine(packageDir, source.CanonicalBaseName + ".ff");

        // Never let a stale candidate survive a failed rerun.
        TryDelete(ffPath);
        TryDelete(zonePath);
        TryDelete(Path.Combine(packageDir, "STRUCTURAL_NATIVE_TEST.txt"));
        TryDelete(Path.Combine(packageDir, "mod.arena"));

        var blockers = new List<string>();
        var notes = new List<string>
        {
            "STRUCTURAL NATIVE DIAGNOSTIC ONLY: every emitted main-graph root is rebuilt as a self-contained Xbox-endian object; source packed/shared child pointers are not copied or nulled in-place.",
            "This path is intentionally separate from the production main gate. A native boot/load result validates XFile/XAsset/block/wrapper structure, not gameplay, graphics, collision, shader or audio fidelity.",
            "The old smoke XFile header strategy (zoneSize=entire file and one identical capacity in all seven blocks) is retired from this path."
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
                        container.Role, container.Path, cached.Document, cached.Bytes, cancellationToken);
                    contexts[container.Path] = new SourceContext(container.Role, container.Path, cached.Document, cached.Bytes, payloads);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    blockers.Add($"{container.FileName}: structural payload construction blocked: {ex.Message}");
                    break;
                }
            }
        }

        var rootPayloads = new List<StructuralPayload>(rootPolicy.Roots.Count);
        var fallbackCount = 0;
        var byType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (blockers.Count == 0)
        {
            foreach (var root in rootPolicy.Roots.OrderBy(r => r.CanonicalSourceOrdinal))
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
            var zone = BuildZone(rootPolicy.Roots, rootPayloads, canonicalStrings, blockPlan, out headerZoneSize);
            zoneBytes = zone.LongLength;
            correctZoneSize = headerZoneSize == checked((uint)(zone.Length - FastFileConstants.T4XFileHeaderSize));
            perBlockHeader = VerifyBlockHeader(zone, blockPlan);
            if (!correctZoneSize) blockers.Add("XFile zoneSize does not equal decompressed bytes after the 36-byte XFile header.");
            if (!perBlockHeader) blockers.Add("XFile seven-block header does not match the source-derived per-block capacity plan.");

            if (blockers.Count == 0)
            {
                await File.WriteAllBytesAsync(zonePath, zone, cancellationToken);
                readBackErrors = VerifyReadBack(zonePath, zone, rootPolicy.Roots, rootPayloads, canonicalStrings, blockPlan);
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
                             rootPayloads.Count == rootPolicy.Roots.Count &&
                             correctZoneSize && perBlockHeader && readBackErrors == 0 && wrapperRoundTrip;

        if (candidateReady)
        {
            await WriteReadmeAsync(packageDir, source.CanonicalBaseName, sha!, rootPolicy.Roots.Count, fallbackCount, cancellationToken);
            var promoted = rootPayloads
                .Where(p => TrackedTypes.Contains(p.TypeName, StringComparer.OrdinalIgnoreCase) && !p.Fallback)
                .GroupBy(p => p.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            notes.Add($"NATIVE GATE OPEN: structural main FF emitted with {rootPolicy.Roots.Count:N0} authoritative source roots and {fallbackCount:N0} explicitly reduced/fallback payload(s).");
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
            XboxAssets = rootPolicy.Roots.Count,
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
            if (proof.Records.Count != assets.Count)
                throw new InvalidDataException($"{Path.GetFileName(sourcePath)} {type} proof count mismatch: roots={assets.Count:N0}, proof={proof.Records.Count:N0}.");
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
                            safe = T4XModelSerializerProof.TryValidateSelfContainedXboxBlob(blob, out auditedName, out reason) &&
                                   auditedName.Equals(name, StringComparison.Ordinal);
                            if (safe && !auditedName.Equals(name, StringComparison.Ordinal))
                            {
                                safe = false;
                                reason = $"typed XModel name mismatch '{auditedName}' != '{name}'";
                            }
                            if (!safe &&
                                reason.Contains("Material.techniqueSet retains packed/source pointer", StringComparison.OrdinalIgnoreCase) &&
                                T4XModelSerializerProof.TryBuildStructuralXboxBlobWithSmokeTechniqueSets(
                                    blob, out var smokeBlob, out var smokeName, out var smokeTechniqueSets, out var smokeReason) &&
                                smokeName.Equals(name, StringComparison.Ordinal))
                            {
                                output[assets[i].Index] = new StructuralPayload(
                                    type,
                                    name,
                                    smokeBlob,
                                    true,
                                    $"smoke-promotion[xmodel]: preserved verified Xbox-endian model/material/image ownership and localized {smokeTechniqueSets:N0} packed PC TechniqueSet dependency(ies) as inline Xbox 216-byte null-graph TechniqueSets; no PC shader graph copied");
                                continue;
                            }
                            break;
                        case "weapon":
                            safe = T4WeaponVariableSerializerProof.TryValidateSelfContainedXboxBlob(blob, name, out reason);
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

        // Sound parser already closes every source snd_alias_list ownership graph and creates a
        // self-contained silent Xbox stub. Fail closed if a source sound run cannot be proven.
        var sound = T4SoundAliasSmokeSerializer.Build(doc, zone, ct);
        if (!sound.Verified && sound.DeclaredPhysicalSoundAssets != 0)
            throw new InvalidDataException($"Sound structural serialization is not exact for {Path.GetFileName(sourcePath)}: {sound.ParsedPhysicalSoundAssets:N0}/{sound.DeclaredPhysicalSoundAssets:N0}.");
        foreach (var s in sound.AssetsByIndex.Values)
            output[s.AssetIndex] = new StructuralPayload("sound", s.AliasListName, s.XboxSilentStubPayload, false,
                "silent Sound structural stub; SDNS/XMA fidelity pending");

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

        foreach (var root in roots.OrderBy(r => r.CanonicalSourceOrdinal))
        {
            WriteU32(ms, checked((uint)root.XboxTypeId));
            WriteU32(ms, T4ZonePointer.Following);
        }
        foreach (var payload in payloads) ms.Write(payload.Bytes);

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
            if (read.Assets[i].RawHeader != T4ZonePointer.Following) errors++;
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
        const int fixedSize = 88;
        if (blob.Length < fixedSize) return false;

        static bool SelfContained(uint value) =>
            value == 0 || value is T4ZonePointer.Following or T4ZonePointer.Insert;

        // XAnimParts::name plus every serialized owned pointer in the fixed body.
        if (!SelfContained(BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(0, 4))))
            return false;
        for (var offset = 48; offset <= 84; offset += 4)
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(offset, 4));
            if (!SelfContained(value))
                return false;
        }
        return true;
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
