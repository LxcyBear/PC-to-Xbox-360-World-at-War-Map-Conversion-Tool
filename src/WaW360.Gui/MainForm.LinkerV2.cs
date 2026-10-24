using WaW360.Core.FastFiles;
using WaW360.Core.Maps;

namespace WaW360.Gui;

public sealed partial class MainForm
{
    /// <summary>
    /// Linker V2 Fast Mode. This intentionally does NOT execute the old 35-stage proof marathon.
    /// The legacy stages remain available through Safe Mode (Full Validation) while their proven
    /// serializer knowledge is migrated into deterministic Linker V2 root/dependency walkers.
    /// </summary>
    private async Task RunLinkerV2FastBatchAsync(
        IReadOnlyList<MapCandidate> maps,
        int skipped,
        string outputRoot)
    {
        const int stagesPerMap = 16;
        var totalSteps = maps.Count * stagesPerMap;
        var completedSteps = 0;
        var passCount = 0;
        var failCount = 0;
        var cachedCount = 0;
        var parallelMaps = Math.Clamp((int)_parallelMaps.Value, 1, 2);
        var started = DateTime.Now;
        var batchDir = Path.Combine(outputRoot, "_batch_reports");
        Directory.CreateDirectory(batchDir);
        var reportPath = Path.Combine(batchDir, $"linker_v2_alpha5_{started:yyyyMMdd_HHmmss}.txt");
        var report = new List<string>
        {
            "WaW360Converter — Linker V2 alpha 5 structural-main native-gate pipeline",
            new string('=', 72),
            $"Started: {started:O}",
            "Mode: Fast Mode (Linker V2 production architecture)",
            $"Parallel maps: {parallelMaps}",
            $"Maps: {maps.Count}",
            $"Skipped checked non-Normal rows: {skipped}",
            $"Output root: {outputRoot}",
            $"Shared FF cache (LocalAppData): {FastFileAnalysisCache.CacheRoot}",
            $"Fast-stage cache: {Path.Combine(outputRoot, "_analysis_cache", "stage_results")}",
            "",
            "FAST MODE POLICY: PC FF/IWD boundaries are source provenance only. Build one canonical source graph, select the canonical load graph, then close typed dependencies and serialize into real Xbox blocks/shared targets.",
            "LEGACY POLICY: old Stage 4a/5/10 proof stages are retired from Fast Mode. They remain available in Safe Mode (Full Validation) as deep diagnostics until every proven serializer has been absorbed into Linker V2.",
            "Stages: Source inventory | Linker V2 canonical source archive/package graph | Linker V2 canonical convergence contract | Linker V2 script-string canonicalization/remap | Linker V2 concrete identity evidence | Linker V2 canonical provenance-root policy | Linker V2 typed source-archive closure plan | Linker V2 packed/shared relocation obligations | Linker V2 source-derived destination block plan | Linker V2 consolidated production main-FF gate | Linker V2 self-contained structural main native emitter | Linker V2 selected load dependency graph | Linker V2 selected load Xbox emitter | Linker V2 load dependency resolution | Linker V2 load-only hardware package gate | Linker V2 authoritative root inventory",
            ""
        };

        var reportGate = new SemaphoreSlim(1, 1);
        var mapGate = new SemaphoreSlim(parallelMaps, parallelMaps);
        var timingGate = new object();
        var timings = new Dictionary<string, List<(double Seconds, bool Cached)>>(StringComparer.OrdinalIgnoreCase);

        BeginBatchProgress(totalSteps, $"Linker V2: 0/{totalSteps} stages complete...");
        Log("");
        Log($"================ LINKER V2 FAST BATCH: {maps.Count} Normal map(s), {totalSteps} stage(s) ================");
        if (skipped > 0) Log($"BATCH: skipped {skipped} checked row(s) because their profile hint is not Normal.");
        Log($"BATCH report: {reportPath}");
        Log("FAST MODE: legacy scanner/proof marathon is disabled; choose Safe Mode only when deep regression evidence is needed.");
        _tabs.SelectedTab = _logsTab;

        async Task FlushReportAsync()
        {
            await reportGate.WaitAsync();
            try { await File.WriteAllLinesAsync(reportPath, report.ToArray()); }
            finally { reportGate.Release(); }
        }

        async Task ProcessMapAsync(MapCandidate map)
        {
            await mapGate.WaitAsync();
            try
            {
                var mapFingerprint = await BatchStageCache.ComputeMapFingerprintAsync(map);
                ClearMapIssues(map.Name);
                UpdateMapIssueCell(map.Name);
                var mapReport = new List<string>
                {
                    $"MAP: {map.Name}",
                    new string('-', Math.Max(12, map.Name.Length + 5))
                };
                Log($"LINKER V2 MAP: {map.Name} ({map.DisplayLocation})");
                NormalMapLinkerV2SourceArchiveResult? sourceArchive = null;
                NormalMapLinkerV2CanonicalConvergenceResult? convergence = null;
                NormalMapLinkerV2ScriptStringMergeResult? scriptStrings = null;
                NormalMapLinkerV2IdentityEvidenceResult? identityEvidence = null;
                NormalMapLinkerV2RelocationObligationsResult? relocation = null;
                NormalMapLinkerV2CanonicalRootPolicyResult? rootPolicy = null;
                NormalMapLinkerV2DestinationBlockPlanResult? blockPlan = null;
                NormalMapLinkerV2LoadSelectionResult? loadSelection = null;
                NormalMapLinkerV2LoadEmitResult? loadEmitter = null;

                async Task Stage(
                    string stageKey,
                    string stageVersion,
                    string name,
                    Func<Task<string>> action,
                    bool cacheable = true)
                {
                    SetStatus($"{map.Name}: {name} [{completedSteps + 1}/{totalSteps}]...");
                    BatchStageResult result;
                    if (cacheable)
                    {
                        var cached = await BatchStageCache.TryGetAsync(
                            outputRoot, map.Name, stageKey, stageVersion, mapFingerprint, string.Empty);
                        if (cached is not null)
                        {
                            Log($"  -> {name}...");
                            Log($"     PASS (cached) {name}: {cached.Details}");
                            result = new BatchStageResult(true, cached.Details, TimeSpan.Zero, null, true);
                        }
                        else
                        {
                            var mapOutputRoot = Path.Combine(outputRoot, map.Name);
                            var beforeOutputs = BatchStageCache.SnapshotStageOutputs(mapOutputRoot);
                            result = await RunBatchStageAsync(map, name, action);
                            if (result.Passed)
                            {
                                var outputs = BatchStageCache.CaptureStageOutputs(mapOutputRoot, beforeOutputs);
                                await BatchStageCache.PutPassAsync(
                                    outputRoot, map.Name, stageKey, stageVersion, mapFingerprint,
                                    result.Details, outputs, string.Empty);
                            }
                        }
                    }
                    else
                    {
                        result = await RunBatchStageAsync(map, name, action);
                    }

                    completedSteps++;
                    if (result.Passed)
                    {
                        passCount++;
                        if (result.Cached) cachedCount++;
                        RemoveMapIssue(map.Name, name);
                    }
                    else
                    {
                        failCount++;
                        UpsertMapIssue(map.Name, name, SummarizeBatchIssue(name, result.Details));
                    }
                    UpdateMapIssueCell(map.Name);

                    lock (timingGate)
                    {
                        if (!timings.TryGetValue(name, out var list)) timings[name] = list = new();
                        list.Add((result.Elapsed.TotalSeconds, result.Cached));
                    }

                    mapReport.Add($"[{(result.Passed ? (result.Cached ? "PASS CACHED" : "PASS") : "FAIL")}] {name}");
                    mapReport.Add($"  {result.Details.Replace(Environment.NewLine, Environment.NewLine + "  ")}");
                    mapReport.Add(result.Cached ? "  elapsed=0.0s (cached)" : $"  elapsed={result.Elapsed.TotalSeconds:F1}s");
                    if (!result.Passed && !string.IsNullOrWhiteSpace(result.StackTrace))
                    {
                        mapReport.Add("  exception:");
                        foreach (var line in result.StackTrace.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                            mapReport.Add("    " + line);
                    }
                    UpdateBatchProgress(completedSteps, totalSteps, $"{map.Name}: {name} [{completedSteps}/{totalSteps}]");
                }

                await Stage("v2-analysis", "v2a5|analysis:a1b", "Source inventory", async () =>
                {
                    var r = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot);
                    return $"profile={r.Profile}; fastfiles={r.FastFiles.Count}; IWDs={r.Iwds.Count}; rawRootAssets={r.TotalAssetTypes.Values.Sum():N0}; report={r.TextReportFile}";
                });

                await Stage("v2-source-archive", "v2a5|canonical-source-archive|golden:simpsons+aztec", "LINKER V2 canonical source archive + package graph", async () =>
                {
                    var r = await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot);
                    sourceArchive = r;
                    var golden = r.GoldenOracleApplied ? $"; golden={r.GoldenOracleName}; goldenRoots={r.GoldenMainAssetCount:N0}" : string.Empty;
                    return $"base={r.CanonicalBaseName}; primarySources={r.FastFiles.Count(x => x.GraphParticipation != "LOAD_CANDIDATE")}; rawRoots={r.PrimaryGraphSourceRoots:N0}; IWD={r.IwdFiles:N0}/{r.IwdEntries:N0}; selectedLoad={Path.GetFileName(r.SelectedLoadSource ?? "<none>")}; rejectedLoads={r.RejectedLoadCandidates.Count:N0}; externalSounds={r.ExternalSoundsExpected}{golden}; blockers={r.Blockers.Count:N0}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-convergence", "v2a5|canonical-convergence|oracle-counts-validation-only", "LINKER V2 canonical convergence contract", async () =>
                {
                    var r = await NormalMapLinkerV2CanonicalConvergence.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    convergence = r;
                    var oracle = r.GoldenOracleApplied
                        ? $"goldenRoots={r.GoldenMainRoots:N0}; net={r.NetRootDelta:+#;-#;0}; edits={r.AbsoluteFamilyEdits:N0}; stable={r.StableFamilies:N0}; canonicalize={r.CanonicalizeFamilies:N0}; promote={r.PromoteFamilies:N0}"
                        : "golden=<none>; typedIdentityClosurePending=True";
                    return $"sourceRoots={r.SourceAggregateRoots:N0}; strings={r.SourceAggregateScriptStrings:N0}; {oracle}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-script-strings", "v2a5|null-safe-script-string-remap|ordinal-identity", "LINKER V2 script-string canonicalization/remap", async () =>
                {
                    var r = await NormalMapLinkerV2ScriptStringMerge.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    scriptStrings = r;
                    var golden = r.GoldenScriptStrings is null ? string.Empty : $"; golden={r.GoldenScriptStrings:N0}; delta={r.GoldenDeltaAfterExactMerge:+#;-#;0}; goldenMatch={r.GoldenCountMatched}";
                    return $"raw={r.RawSourceStrings:N0}; canonicalTable={r.CanonicalTableEntries:N0}; literalUnique={r.CanonicalUniqueStrings:N0}; nullSlots={r.CanonicalNullEntries:N0}; sourceNullsCollapsed={r.SourceNullEntriesCollapsed:N0}; duplicatesRemoved={r.ExactDuplicateStringsRemoved:N0}; aliases={r.AliasStrings:N0}; remapReady={r.FullRemapReady}{golden}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-identity-evidence", "v2a5|proof-backed-concrete-identity-evidence|no-scan", "LINKER V2 concrete identity evidence", async () =>
                {
                    var r = await NormalMapLinkerV2IdentityEvidence.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    identityEvidence = r;
                    return $"primaryRoots={r.PrimaryRoots:N0}; proofBacked={r.ProofBackedRoots:N0}; named={r.NamedProofRoots:N0}; unnamedProof={r.UnnamedProofRoots:N0}; collisions={r.ExactNameCollisionGroups:N0}/{r.RootsInExactNameCollisions:N0} roots; missingProofFiles={r.MissingProofFiles:N0}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-root-policy", "v2a5|provenance-root-policy|clipmap-platform-map|stable-name-collisions-retained", "LINKER V2 canonical provenance-root policy", async () =>
                {
                    identityEvidence ??= await NormalMapLinkerV2IdentityEvidence.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    var r = await NormalMapLinkerV2CanonicalRootPolicy.RunAsync(map, outputRoot, sourceArchive, identityEvidence);
                    rootPolicy = r;
                    return $"sourceRoots={r.SourceRoots:N0}; provenanceRoots={r.CanonicalProvenanceRoots:N0}; platformTransforms={r.PlatformTypeTransforms:N0}; stableCollisionsRetained={r.StableSameNameCollisionGroupsRetained:N0}/{r.RootsInStableSameNameCollisionGroupsRetained:N0} roots; provenanceReady={r.ProvenanceIdentityComplete}; semanticClosure={r.SemanticClosureComplete}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-typed-closure-plan", "v2a7|iwd-script-weapon-animation-and-xbox-technique-donor-closure", "LINKER V2 typed source-archive + Xbox donor closure", async () =>
                {
                    var r = await NormalMapLinkerV2TypedClosurePlan.RunAsync(map, outputRoot, sourceArchive);
                    var golden = r.GoldenExpectedXAnimPromotions is null ? string.Empty : $"; goldenXAnim={r.GoldenExpectedXAnimPromotions:+#;-#;0}/{r.XAnimGoldenCountMatched}; goldenRaw={r.GoldenExpectedRawFilePromotions:+#;-#;0}/{r.RawFileGoldenCountMatched}; goldenMenu={r.GoldenExpectedMenuCanonicalizations:+#;-#;0}/{r.MenuGoldenCountMatched}";
                    return $"existingXAnim={r.ExistingNamedXAnims:N0}; existingRaw={r.ExistingNamedRawFiles:N0}; iwdWeapons={r.IwdWeaponSources:N0}; iwdScripts={r.IwdScriptSources:N0}; promoteXAnim={r.PromotedXAnimCandidates:N0}; promoteRaw={r.PromotedRawFileCandidates:N0}; residentMenus={r.ResidentMenuCandidates:N0}; donorRequired={r.XboxDonorRequiredTechniqueSets:N0}; donorExact={r.ExactXboxDonorIdentityMatches:N0}; donorUnresolved={r.UnresolvedXboxDonorIdentities:N0}; donorAmbiguous={r.AmbiguousXboxDonorIdentities:N0}; donorCatalogFF={r.DonorCatalogFastFilesScanned:N0}; donorCatalogSets={r.DonorCatalogTechniqueSets:N0}; donorIdentityComplete={r.DonorIdentityResolutionComplete}; promotionEvidence={r.PromotionEvidenceReady}; fullClosure={r.FullTypedClosureReady}{golden}; blockers={r.Blockers.Count:N0}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-relocation-obligations", "v2a5|typed-top-level-packed-pointer-obligations|no-nulling", "LINKER V2 packed/shared relocation obligations", async () =>
                {
                    var r = await NormalMapLinkerV2RelocationObligations.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    relocation = r;
                    return $"proofAssets={r.ProofAssetsAudited:N0}; fields={r.KnownPointerFieldsAudited:N0}; packedRefs={r.PackedPointerReferences:N0}; uniqueTargets={r.UniquePackedTargets:N0}; invalid={r.InvalidPackedPointers:N0}; sourceAliases={r.SourceTopLevelAliasRoots:N0}; encodingVerified={r.PackedEncodingVerified}; ownershipResolved={r.SharedTargetOwnershipResolved}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-block-plan", "v2a5|source-derived-seven-block-capacity-plan|no-golden-copy", "LINKER V2 source-derived destination block plan", async () =>
                {
                    var r = await NormalMapLinkerV2DestinationBlockPlan.RunAsync(map, outputRoot, sourceArchive);
                    blockPlan = r;
                    return $"ready={r.SourceCapacityPlanReady}; productionOwnership={r.ProductionOwnershipComplete}; goldenUsed={r.UsesGoldenForAllocation}; temp={r.StructuralCapacities.GetValueOrDefault(T4ZoneBlock.Temp.ToString()):N0}B; virtual={r.StructuralCapacities.GetValueOrDefault(T4ZoneBlock.Virtual.ToString()):N0}B; largeRuntime={r.StructuralCapacities.GetValueOrDefault(T4ZoneBlock.LargeRuntime.ToString()):N0}B; blockers={r.Blockers.Count:N0}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-main-gate", "v2a5|main-native-readiness|structural-vs-strict", "LINKER V2 consolidated main-FF native gate", async () =>
                {
                    convergence ??= await NormalMapLinkerV2CanonicalConvergence.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    scriptStrings ??= await NormalMapLinkerV2ScriptStringMerge.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    identityEvidence ??= await NormalMapLinkerV2IdentityEvidence.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    relocation ??= await NormalMapLinkerV2RelocationObligations.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    var r = await NormalMapLinkerV2MainReadiness.RunAsync(map, outputRoot, sourceArchive, convergence, scriptStrings, identityEvidence, relocation);
                    return $"structuralReady={r.StructuralHardwareDiagnosticReady}; strictReady={r.StrictHardwareReady}; script={r.ScriptStringRemapReady}; roots={r.RootAccountingReady}; identity={r.ConcreteIdentityClosureReady}; closure={r.TypedDependencyClosureReady}; ptrEncoding={r.PackedPointerEncodingReady}; sharedOwnership={r.SharedTargetOwnershipReady}; blockAlloc={r.RealXboxBlockAllocationReady}; audio={r.AudioStrictReady}; blockers={r.Blockers.Count:N0}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-main-structural-emitter", "v2a5|self-contained-main-native|correct-zone-size|per-block-header", "LINKER V2 self-contained structural main native emitter", async () =>
                {
                    rootPolicy ??= await NormalMapLinkerV2CanonicalRootPolicy.RunAsync(map, outputRoot, sourceArchive, identityEvidence);
                    blockPlan ??= await NormalMapLinkerV2DestinationBlockPlan.RunAsync(map, outputRoot, sourceArchive);
                    scriptStrings ??= await NormalMapLinkerV2ScriptStringMerge.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    var r = await NormalMapLinkerV2StructuralMainEmitter.RunAsync(map, outputRoot, sourceArchive, rootPolicy, blockPlan, scriptStrings);
                    var topRejects = r.PromotionRejectReasons.Count == 0
    ? "<none>"
    : string.Join(" | ", r.PromotionRejectReasons.Take(6).Select(kv => $"{kv.Value:N0}x {kv.Key}"));
return $"nativeCandidate={r.StructuralNativeCandidateReady}; productionReady={r.ProductionMainReady}; assets={r.XboxAssets:N0}; strings={r.ScriptStrings:N0}; payloads={r.PayloadsBuilt:N0}; fallbacks={r.FallbackPayloads:N0}; realPromoted={r.RealPromotedPayloads:N0}; promotionRejected={r.PromotionRejectedPayloads:N0}; realXAnim={r.RealPromotedByType.GetValueOrDefault("xanim"):N0}; realXModel={r.RealPromotedByType.GetValueOrDefault("xmodel"):N0}; realWeapon={r.RealPromotedByType.GetValueOrDefault("weapon"):N0}; realFx={r.RealPromotedByType.GetValueOrDefault("fx"):N0}; rejectTop={topRejects}; zone={r.ZoneBytes:N0}B; ff={r.FastFileBytes:N0}B; zoneSizeSemantics={r.CorrectZoneSizeSemantics}; blockHeader={r.PerBlockCapacityHeader}; readBackErrors={r.ReadBackErrors:N0}; roundTrip={r.WrapperRoundTripVerified}; blockers={r.Blockers.Count:N0}; package={r.HardwarePackageDirectory ?? "<none>"}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-load-selection", "v2a5|canonical-load-selector|typed-walker", "LINKER V2 selected load dependency graph", async () =>
                {
                    var r = await NormalMapLinkerV2LoadSelection.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    loadSelection = r;
                    return $"selected={Path.GetFileName(r.SelectedSourceFastFile ?? "<none>")}; roots={r.PcRoots:N0}; linked={r.LinkedNodes:N0}; promoted={r.PromotedDependencies:N0}; sourceBlockedDeps={r.BlockedDependencies:N0}; exactWalk={r.ExactWalk}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-load-emitter", "v2a5|selected-load-emitter|aztec-hardware-proven-7node", "LINKER V2 selected load Xbox emitter", async () =>
                {
                    var r = await NormalMapLinkerV2LoadEmitter.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    loadEmitter = r;
                    return $"selected={Path.GetFileName(r.SelectedSourceFastFile ?? "<none>")}; emitted={(r.OutputFastFile is null ? 0 : 1)}; assets={r.XboxAssets:N0}; zone={r.ZoneBytes:N0}B; ff={r.FastFileBytes:N0}B; hardwareReady={r.HardwareReady}; roundTrip={r.WrapperRoundTripVerified}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-load-resolution", "v2a5|source-blocked-to-xbox-resolved", "LINKER V2 load dependency resolution", async () =>
                {
                    loadSelection ??= await NormalMapLinkerV2LoadSelection.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    loadEmitter ??= await NormalMapLinkerV2LoadEmitter.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    var r = await NormalMapLinkerV2LoadResolution.RunAsync(map, outputRoot, loadSelection, loadEmitter);
                    return $"sourceBlocked={r.SourceGraphBlockedDependencies:N0}; resolvedIWD={r.ResolvedCustomIwdImages:N0}; resolvedDonor={r.ResolvedReferenceDonors:N0}; remaining={r.RemainingUnresolvedDependencies:N0}; hardwareReady={r.HardwareCandidateReady}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-load-hardware-package", "v2a5|load-only-native-ab-package", "LINKER V2 load-only hardware package gate", async () =>
                {
                    loadEmitter ??= await NormalMapLinkerV2LoadEmitter.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    var r = await NormalMapLinkerV2LoadHardwarePackager.RunAsync(map, outputRoot, loadEmitter);
                    return $"ready={r.Ready}; ff={Path.GetFileName(r.FastFile ?? "<none>")}; package={r.PackageDirectory ?? "<none>"}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("v2-root-inventory", "v2a5|authoritative-root-table|no-candidate-scan", "LINKER V2 authoritative source-root inventory", async () =>
                {
                    var r = await NormalMapLinkerV2RootInventory.RunAsync(map, outputRoot, sourceOverride: sourceArchive);
                    var runs = r.FastFiles.Sum(x => x.TypeRunCount);
                    return $"sourceFF={r.FastFileCount:N0}; roots={r.TotalRoots:N0}; primary={r.PrimaryGraphRoots:N0}; load={r.LoadCandidateRoots:N0}; physical={r.PhysicalRoots:N0}; aliases={r.PackedAliasRoots:N0}; nulls={r.NullRoots:N0}; runs={runs:N0}; exactWalk={r.ExactRootWalk}; report={r.TextReportFile}";
                });

                mapReport.Add("");
                await reportGate.WaitAsync();
                try
                {
                    report.AddRange(mapReport);
                    await File.WriteAllLinesAsync(reportPath, report.ToArray());
                }
                finally { reportGate.Release(); }
            }
            finally { mapGate.Release(); }
        }

        try
        {
            await Task.WhenAll(maps.Select(ProcessMapAsync));
        }
        finally
        {
            var ended = DateTime.Now;
            var cacheStats = FastFileAnalysisCache.GetStats();
            report.Add(new string('=', 72));
            report.Add($"Finished: {ended:O}");
            report.Add($"Elapsed: {ended - started}");
            report.Add($"Stage results: PASS={passCount}; FAIL={failCount}; TOTAL={passCount + failCount}; CACHED_PASS={cachedCount}");
            report.Add("");
            report.Add("TIMING TELEMETRY");
            foreach (var kv in timings.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var executed = kv.Value.Where(x => !x.Cached).ToList();
                var cached = kv.Value.Count(x => x.Cached);
                var seconds = executed.Sum(x => x.Seconds);
                var avg = executed.Count == 0 ? 0 : seconds / executed.Count;
                report.Add($"  {kv.Key}: executed={executed.Count}; cached={cached}; total={seconds:F1}s; avg={avg:F1}s");
            }
            report.Add("");
            report.Add("SHARED FF ANALYSIS CACHE");
            report.Add($"  memory hits: {cacheStats.MemoryHits:N0}");
            report.Add($"  persistent hits: {cacheStats.PersistentHits:N0}");
            report.Add($"  decompression misses: {cacheStats.Misses:N0}");
            report.Add($"  decompressed bytes avoided: {cacheStats.DecompressedBytesAvoided / (1024.0 * 1024.0):F1} MB");
            await FlushReportAsync();

            foreach (var map in maps)
            {
                try
                {
                    var oneFile = await NormalMapConsolidatedDiagnostics.WriteAsync(map, outputRoot, report, started, fastMode: true);
                    Log($"ONE-FILE DIAGNOSTICS ({map.Name}): {oneFile}");
                }
                catch (Exception ex)
                {
                    Log($"ONE-FILE DIAGNOSTICS FAILED ({map.Name}): {ex.GetType().Name}: {ex.Message}");
                }
            }

            try
            {
                var master = await NormalMapMasterDiagnostics.WriteAsync(maps, outputRoot, report, reportPath, started, fastMode: true);
                Log($"LINKER V2 ALL-NORMAL MASTER DATASET: {master}");
            }
            catch (Exception ex)
            {
                Log($"ALL-NORMAL MASTER DATASET FAILED: {ex.GetType().Name}: {ex.Message}");
            }

            EndBatchProgress($"Linker V2 batch complete: {passCount} PASS / {failCount} FAIL ({cachedCount} cached). Master dataset ready.");
            Log($"================ LINKER V2 BATCH COMPLETE: PASS={passCount}, FAIL={failCount}, CACHED={cachedCount} ================");
            Log($"BATCH consolidated report: {reportPath}");
            _tabs.SelectedTab = _logsTab;
        }
    }
}
