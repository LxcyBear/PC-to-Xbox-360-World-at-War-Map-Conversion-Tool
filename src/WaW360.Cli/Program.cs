using System.Text.Json;
using WaW360.Core.Diagnostics;
using WaW360.Core.FastFiles;
using WaW360.Core.Linker;
using WaW360.Core.Maps;

return await MainAsync(args);

static async Task<int> MainAsync(string[] args)
{
    if (args.Length == 0)
    {
        PrintHelp();
        return 0;
    }

    try
    {
        switch (args[0].ToLowerInvariant())
        {
            case "inspect" when args.Length >= 2:
            {
                var info = await FastFileInspector.InspectAsync(args[1]);
                Console.WriteLine(JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            case "decompress" when args.Length >= 3:
            {
                var result = await FastFileDecompressor.DecompressAutoAsync(args[1], args[2]);
                Console.WriteLine($"{result.Platform} / {result.Compression}");
                Console.WriteLine(result.ZoneHeader);
                Console.WriteLine($"Wrote {result.DecompressedSize:N0} bytes -> {result.OutputPath}");
                return 0;
            }
            case "compare" when args.Length >= 3:
            {
                var result = await ZoneComparer.CompareAsync(args[1], args[2]);
                foreach (var item in result.Items)
                    Console.WriteLine($"{item.Field,-22} {item.Left,-24} | {item.Right,-24} {(item.Equal ? "=" : "!=")}");
                return 0;
            }
            case "assets" when args.Length >= 2:
            {
                var zone = await T4ZoneParser.ParseFastFileAsync(args[1]);
                Console.WriteLine($"{zone.Platform} {zone.Endianness}-endian | strings={zone.ScriptStringCount:N0} assets={zone.AssetCount:N0} table=0x{zone.AssetTableOffset:X}");
                foreach (var pair in zone.AssetCountsByType.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
                    Console.WriteLine($"{pair.Key,-22} {pair.Value,6:N0}");
                return 0;
            }
            case "manifest" when args.Length >= 3:
            {
                var zone = await T4ZoneParser.ParseFastFileAsync(args[1]);
                await T4ZoneManifestWriter.WriteAsync(zone, args[2]);
                Console.WriteLine($"Wrote manifest: {args[2]}");
                return 0;
            }
            case "resolve" when args.Length >= 2:
            {
                var result = await T4PayloadScanner.ScanFastFileAsync(args[1]);
                Console.WriteLine($"Resolved {result.ResolvedAssets.Count:N0}/{result.Document.AssetCount:N0} XAsset payload(s)");
                foreach (var asset in result.ResolvedAssets)
                    Console.WriteLine($"#{asset.AssetIndex,-5} {asset.TypeName,-12} 0x{asset.PayloadOffset:X8}  {asset.Name}");
                foreach (var warning in result.Warnings)
                    Console.WriteLine("WARNING: " + warning);
                return 0;
            }
            case "stringtable" when args.Length >= 3:
            {
                var result = await T4PayloadScanner.ScanFastFileAsync(args[1]);
                var table = result.StringTables.FirstOrDefault(t => t.Name.Equals(args[2], StringComparison.OrdinalIgnoreCase));
                if (table is null)
                {
                    Console.Error.WriteLine($"StringTable not found: {args[2]}");
                    return 3;
                }
                for (var row = 0; row < table.RowCount; row++)
                {
                    var cells = Enumerable.Range(0, table.ColumnCount)
                        .Select(column => table.GetValue(row, column) ?? string.Empty);
                    Console.WriteLine(string.Join("\t", cells));
                }
                return 0;
            }
            case "menu-structs" when args.Length >= 2:
            {
                var result = await T4MenuStructureProbe.ProbeFastFileAsync(args[1]);
                var declared = result.Document.Assets.Count(a => a.TypeName.Equals("menu", StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"Declared menu assets: {declared:N0}");
                Console.WriteLine($"Proven inline-named menuDef_t structures: {result.InlineNamedMenus.Count:N0}");
                foreach (var menu in result.InlineNamedMenus)
                    Console.WriteLine($"0x{menu.SerializedOffset:X8}  items={menu.ItemCount,3}  {menu.Name}");
                foreach (var marker in result.MarkerHits)
                    Console.WriteLine($"MARKER 0x{marker.Offset:X8}  {marker.Value}");
                foreach (var warning in result.Warnings)
                    Console.WriteLine("NOTE: " + warning);
                return 0;
            }
            case "custom-maps-trace" when args.Length >= 2:
            {
                var result = await T4CustomMapsTrace.TraceFastFileAsync(args[1]);
                foreach (var trace in result.Markers)
                {
                    var itemOffset = trace.CandidateItemOffset is long o ? $"0x{o:X8}" : "-";
                    var parent = trace.Item is not null ? $"0x{trace.Item.RawParentPointer:X8}" : "-";
                    var checks = trace.Item is not null ? $"{trace.Item.StructuralChecksPassed}/{trace.Item.StructuralChecksTotal}" : "-";
                    var ptrArray = trace.ItemPointerArrayOffset is long pa ? $"0x{pa:X8}" : "-";
                    var logicalIndex = trace.LogicalItemIndex is int li ? li.ToString() : "-";
                    Console.WriteLine($"0x{trace.MarkerOffset:X8} mode={trace.UsermapMode ?? "-"} item={itemOffset} strong={trace.StrongInlineItemCandidate} parent={parent} checks={checks} owner={trace.ResolvedOwnerMenu ?? "-"} ptrArray={ptrArray} index={logicalIndex}");
                    foreach (var text in trace.FollowingStrings)
                        Console.WriteLine("  " + text);
                }
                foreach (var warning in result.Warnings)
                    Console.WriteLine("NOTE: " + warning);
                return 0;
            }
            case "menu-probe" when args.Length >= 2:
            {
                var hits = await T4MenuStringProbe.ProbeFastFileAsync(args[1]);
                foreach (var hit in hits)
                    Console.WriteLine($"0x{hit.Offset:X8}  {hit.Value}");
                return 0;
            }
            case "stage3-label-proof" when args.Length >= 3:
            {
                var result = await T4Stage3LabelProofWriter.CreateAsync(args[1], args[2]);
                Console.WriteLine($"Target: {result.OwnerMenu} item #{result.LogicalItemIndex} @ 0x{result.MarkerOffset:X8}");
                Console.WriteLine($"Label: {result.OriginalText} -> {result.ReplacementText}");
                Console.WriteLine($"Raw-zone changed bytes: {result.RawZoneChangedByteCount}");
                Console.WriteLine($"Assets: {result.AssetCount}; menu items: {result.MenuItemCount}");
                Console.WriteLine($"Verified: {result.OnlyTargetSpanChanged && result.ReopenedAndVerified}");
                return 0;
            }
            case "stage3-serializer-proof" when args.Length >= 3:
            {
                var result = await T4Stage3SerializerIdentityWriter.CreateAsync(args[1], args[2]);
                Console.WriteLine($"{result.OwnerMenu}: {result.ItemCount} -> {result.ItemCount} items");
                Console.WriteLine($"Pointer array @ 0x{result.ItemPointerArrayOffset:X8}; entries={result.ItemPointerCount}");
                Console.WriteLine($"Known fixed item bodies re-emitted: {result.FixedItemsRewritten}");
                Console.WriteLine($"Region byte-identical={result.RegionByteIdentical}");
                Console.WriteLine($"Zone byte-identical={result.ZoneByteIdentical}");
                Console.WriteLine($"Reopened/verified={result.ReopenedAndVerified}");
                Console.WriteLine($"Packed Virtual pointer candidates (read-only audit): {result.CandidatePackedVirtualPointers}");
                return 0;
            }
            case "stage3-alpha4c-audit" when args.Length >= 3:
            {
                var result = await T4Stage3RelocationAudit.CreateReportAsync(args[1], args[2]);
                Console.WriteLine("Stage 3 alpha 4c relocation audit PASS (read-only)");
                Console.WriteLine($"levels_unlock: {result.LevelsUnlockItemCount} items @ 0x{result.LevelsUnlockMenuOffset:X8}");
                Console.WriteLine($"Pointer array @ 0x{result.ItemPointerArrayOffset:X8}");
                Console.WriteLine($"Custom Maps #59 @ 0x{result.CustomMapsItemOffset:X8}; parent=0x{result.LevelsUnlockRawParent:X8}; span=0x{result.CustomMapsSerializedSpan:X}");
                Console.WriteLine($"Next menu: {result.NextMenuName} @ 0x{result.NextMenuOffset:X8} ({result.NextMenuItemCount} items)");
                Console.WriteLine($"Report: {result.ReportFile}");
                Console.WriteLine("No fastfile was created or modified.");
                return 0;
            }
            case "stage3-alpha4d-audit" when args.Length >= 3:
            {
                var result = await T4Stage3VirtualPointerAudit.CreateAsync(args[1], args[2]);
                Console.WriteLine("Stage 3 alpha 4d VIRTUAL pointer audit PASS (read-only)");
                Console.WriteLine($"levels_unlock @ 0x{result.LevelsUnlockOffset:X8}; pointer array @ 0x{result.PointerArrayOffset:X8}");
                Console.WriteLine($"Custom Maps #59 @ 0x{result.CustomMapsItemOffset:X8}; insertion=0x{result.InsertionPoint:X8}; clone span=0x{result.CloneSpan:X}");
                Console.WriteLine($"Typed VIRTUAL fields: {result.KnownVirtualPointerFieldCount:N0} (before={result.KnownVirtualPointerFieldsBeforeInsertion:N0}, after={result.KnownVirtualPointerFieldsAfterInsertion:N0})");
                Console.WriteLine($"Max target before: {(result.MaxKnownTargetBeforeInsertion is null ? "none" : $"0x{result.MaxKnownTargetBeforeInsertion.Value:X8}")}");
                Console.WriteLine($"Min target after: {(result.MinKnownTargetAfterInsertion is null ? "none" : $"0x{result.MinKnownTargetAfterInsertion.Value:X8}")}");
                Console.WriteLine($"Report: {result.ReportFile}");
                Console.WriteLine("No fastfile was created or modified.");
                return 0;
            }
            case "stage3-alpha4e-audit" when args.Length >= 3:
            {
                var result = await T4Stage3DualPointerRunAudit.CreateAsync(args[1], args[2]);
                Console.WriteLine("Stage 3 alpha 4e dual-pointer-run audit PASS (read-only)");
                Console.WriteLine($"levels_unlock @ 0x{result.LevelsUnlockOffset:X8}; items={result.ItemCount}");
                Console.WriteLine($"Proven array @ 0x{result.ProvenPointerArrayOffset:X8}; second run @ 0x{result.SecondPointerRunOffset:X8}");
                Console.WriteLine($"Anonymous menu candidates={result.SecondRunAnonymousMenuCandidates}; nearby count fields={result.SecondRunNearbyCountFields}; following items={result.SecondRunFollowingHighConfidenceItems}");
                Console.WriteLine($"Report: {result.ReportFile}");
                Console.WriteLine("No fastfile was created or modified.");
                return 0;
            }
            case "stage4-normal-analyze" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");
                var result = await NormalMapAnalyzer.AnalyzeAsync(map, output);
                Console.WriteLine($"Stage 4 alpha 1 NORMAL MAP analysis PASS: {result.MapName}");
                Console.WriteLine($"Profile: {result.Profile}");
                Console.WriteLine($"Inputs={result.InputFiles.Count}; fastfiles={result.FastFiles.Count}; IWDs={result.Iwds.Count}");
                Console.WriteLine($"Declared FF assets={result.TotalAssetTypes.Values.Sum():N0}");
                Console.WriteLine($"Report: {result.TextReportFile}");
                Console.WriteLine($"JSON:   {result.JsonReportFile}");
                Console.WriteLine("READ-ONLY: no fastfile or IWD was modified.");
                return 0;
            }
            case "stage4-alpha2-build" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");
                var result = await NormalMapXboxBuildAttempt.BuildAsync(map, output);
                Console.WriteLine($"Stage 4 alpha 2 Xbox writer pass: {result.MapName}");
                Console.WriteLine($"Profile: {result.Profile}");
                Console.WriteLine($"Critical blockers: {result.CriticalBlockers.Count}");
                Console.WriteLine($"High blockers: {result.HighBlockers.Count}");
                Console.WriteLine($"Final Xbox FF emitted: {result.CanEmitXboxMapFastFile}");
                Console.WriteLine($"Report: {result.TextReportFile}");
                return result.CanEmitXboxMapFastFile ? 0 : 3;
            }
            case "stage4-alpha5-xmodel" or "stage4-alpha9c-xmodel" or "stage4-alpha9d-xmodel" or "stage4-alpha9e-xmodel" or "stage4-alpha9g-xmodel" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");
                var result = await NormalMapXModelSerializerProof.RunAsync(map, output);
                Console.WriteLine($"Stage 4 alpha 9g packed-name + rejected-forensics XModel serializer proof PASS: {result.MapName}");
                Console.WriteLine($"Profile: {result.Profile}");
                Console.WriteLine($"XModel: {result.SerializedXModel:N0}/{result.DeclaredXModel:N0}");
                Console.WriteLine($"Bones={result.TotalBones:N0}; surfaces={result.TotalSurfaces:N0}; materials={result.InlineMaterials:N0}; images={result.InlineImages:N0}");
                Console.WriteLine($"Payload bytes: {result.SerializedBytes:N0}");
                Console.WriteLine($"Report: {result.TextReportFile}");
                Console.WriteLine("INTERNAL PROOF ONLY: no game fastfile was emitted.");
                return result.Verified ? 0 : 3;
            }
            case "stage4-alpha6-weapon" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");
                var result = await NormalMapWeaponSerializerProof.RunAsync(map, output);
                Console.WriteLine($"Stage 4 alpha 6f WeaponDef body/reference proof PASS: {result.MapName}");
                Console.WriteLine($"Profile: {result.Profile}");
                Console.WriteLine($"WeaponDef: {result.SerializedWeapon:N0}/{result.DeclaredWeapon:N0}");
                Console.WriteLine($"Fixed body: 0x{result.FixedBodyBytes:X} ({result.FixedBodyBytes:N0} bytes)");
                Console.WriteLine($"Fixed payload bytes: {result.SerializedFixedBytes:N0}");
                Console.WriteLine($"Report: {result.TextReportFile}");
                Console.WriteLine("INTERNAL PROOF ONLY: no game fastfile was emitted.");
                return result.Verified ? 0 : 3;
            }
            case "stage4-alpha7b-weapon" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");
                var result = await NormalMapWeaponVariableSerializerProof.RunAsync(map, output);
                Console.WriteLine($"Stage 4 alpha 7b WeaponDef owned-payload proof PASS: {result.MapName}");
                Console.WriteLine($"Profile: {result.Profile}");
                Console.WriteLine($"WeaponDef: {result.SerializedWeapon:N0}/{result.DeclaredWeapon:N0}");
                Console.WriteLine($"Full bytes: {result.SerializedBytes:N0}; owned bytes: {result.OwnedPayloadBytes:N0}");
                Console.WriteLine($"Strings={result.InlineStrings:N0}; soundNames={result.SoundNameRecords:N0}; bounce={result.BounceSoundTables:N0}; accuracy={result.AccuracyGraphArrays:N0}/{result.AccuracyGraphKnots:N0}; flame={result.FlameTables:N0}");
                Console.WriteLine($"Inline deps: XModel={result.InlineXModels:N0}; Material={result.InlineMaterials:N0}");
                Console.WriteLine($"Report: {result.TextReportFile}");
                Console.WriteLine("INTERNAL PROOF ONLY: no game fastfile was emitted.");
                return result.Verified ? 0 : 3;
            }
            case "stage4-alpha8g-fx" or "stage4-alpha8u-fx" or "stage4-alpha8x-fx" or "stage4-alpha8z-fx" or "stage4-alpha9b-fx" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");
                var result = await NormalMapFxSerializerProof.RunAsync(map, output);
                Console.WriteLine($"Stage 4 alpha 9b zero-total FX reference recovery PASS: {result.MapName}");
                Console.WriteLine($"Profile: {result.Profile}");
                Console.WriteLine($"FxEffectDef: {result.CoveredFx:N0}/{result.DeclaredFx:N0} (physical={result.SerializedFx:N0}, aliases={result.AliasFx:N0})");
                Console.WriteLine($"Bytes: {result.SerializedBytes:N0}; elemDefs={result.ElemDefs:N0}; vel/vis={result.VelocitySamples:N0}/{result.VisualSamples:N0}");
                Console.WriteLine($"Visual deps M/X/S/R={result.MaterialVisuals:N0}/{result.ModelVisuals:N0}/{result.SoundVisuals:N0}/{result.RunnerVisuals:N0}; trails={result.TrailDefs:N0}");
                Console.WriteLine($"Report: {result.TextReportFile}");
                Console.WriteLine("INTERNAL PROOF ONLY: no game fastfile was emitted.");
                return result.Verified ? 0 : 3;
            }
            case "stage4-alpha9a-mixed" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");
                var result = await NormalMapMixedAssetOrderProbe.RunAsync(map, output);
                Console.WriteLine($"Stage 4 alpha 9a mixed XAsset-order probe PASS: {result.MapName}");
                Console.WriteLine($"Missing FX slots: {result.MissingFx:N0}; bounded windows: {result.TightFxWindows:N0}");
                Console.WriteLine($"XModel missing/skipped: {result.MissingXModel:N0}/{result.SkippedXModel:N0}");
                Console.WriteLine($"Report: {result.TextReportFile}");
                Console.WriteLine("DIAGNOSTIC ONLY: no game fastfile was emitted.");
                return 0;
            }
            case "link-load" when args.Length >= 2:
            {
                var result = await T4LoadFastFileLinker.BuildAsync(args[1]);
                Console.WriteLine($"Linker V1 deterministic load graph: {Path.GetFileName(result.SourceFastFile)}");
                Console.WriteLine($"PC roots: {result.SourceAssetCount:N0}; linked nodes: {result.LinkedAssetCount:N0}; promoted nested: {result.PromotedNestedAssetCount:N0}");
                Console.WriteLine($"Exact source walk: {result.ExactSourceWalk}; technique binding: {result.TechniqueBindingProven}; verified: {result.Verified}");
                foreach (var asset in result.OutputOrder)
                {
                    var deps = asset.Dependencies.Count == 0 ? string.Empty : " | deps=" + string.Join(", ", asset.Dependencies);
                    var block = string.IsNullOrWhiteSpace(asset.BlockReason) ? string.Empty : " | BLOCK=" + asset.BlockReason;
                    Console.WriteLine($"#{asset.OutputIndex,-3} xboxType={asset.XboxTypeId,-2} {asset.TypeName,-12} {asset.Name} [{asset.Origin}/{asset.State}]{deps}{block}");
                }
                foreach (var warning in result.Warnings) Console.WriteLine("NOTE: " + warning);
                if (args.Length >= 3)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
                    await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"Wrote linker graph JSON: {args[2]}");
                }
                return result.Verified ? 0 : 3;
            }
            case "link-load-emit" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");
                var result = await NormalMapLinkerV1Emitter.RunAsync(map, output);
                Console.WriteLine($"Linker V1 alpha 2 load emitter: {result.MapName}");
                Console.WriteLine($"loadFF={result.LoadFastFiles}; emitted={result.EmittedFastFiles}; smokeSub={result.SmokeSubstitutes}; hardwareReady={result.HardwareReadyFastFiles}; verified={result.Verified}");
                foreach (var ff in result.FastFiles)
                    Console.WriteLine($"  {ff.OutputFastFile} | nonRuntime=0x{ff.NonRuntimeBytes:X} LargeRuntime=0x{ff.LargeRuntimeBytes:X} readBack={ff.ZoneReadBackVerified} wrapper={ff.WrapperRoundTripVerified} hardwareReady={ff.HardwareReady}");
                Console.WriteLine($"Report: {result.TextReportFile}");
                return result.Verified ? 0 : 3;
            }
            case "linker-v2-source" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");
                var result = await NormalMapLinkerV2SourceArchive.RunAsync(map, output);
                Console.WriteLine($"Linker V2 canonical source graph: {result.CanonicalBaseName}");
                Console.WriteLine($"rawRoots={result.PrimaryGraphSourceRoots:N0}; IWD={result.IwdFiles:N0}/{result.IwdEntries:N0}; selectedLoad={Path.GetFileName(result.SelectedLoadSource ?? "<none>")}; blockers={result.Blockers.Count:N0}");
                Console.WriteLine($"Package main={result.PrimaryOutputFastFile}; load={result.LoadOutputFastFile ?? "<none>"}; externalSounds={result.ExternalSoundsExpected}");
                if (result.GoldenOracleApplied)
                    Console.WriteLine($"Golden oracle: {result.GoldenOracleName}; mainAssets={result.GoldenMainAssetCount:N0}");
                Console.WriteLine($"Report: {result.TextReportFile}");
                return result.Verified ? 0 : 3;
            }
            case "xanim-split-bones" when args.Length >= 3:
            {
                var result = await T4XAnimBoneSplitOracle.AnalyzeAsync(args[1], args[2]);
                Console.WriteLine(result.Summary);
                return result.NameOrderMismatches == 0 ? 0 : 4;
            }
            case "xanim-bone-compare" when args.Length >= 3:
            {
                var extraArgs = args.Skip(3).ToArray();
                var verbose = extraArgs.Any(x => x.Equals("--verbose", StringComparison.OrdinalIgnoreCase));
                var name = extraArgs.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal));
                var result = await T4XAnimBoneCountOracle.CompareAsync(args[1], args[2], name);
                Console.WriteLine(result.Summary);

                // Keep the default all-animation oracle compact enough to copy from a console.
                // A requested animation is always shown; use --verbose to dump every pair.
                if (verbose || !string.IsNullOrWhiteSpace(name))
                {
                    Console.WriteLine();
                    foreach (var pair in result.Matches)
                    {
                        Console.WriteLine(pair.Name);
                        Console.WriteLine($"  PC   @ 0x{pair.Pc.HeaderOffset:X8}  [{string.Join(" ", pair.Pc.BoneCounts.Select(x => x.ToString("X2")))}]");
                        Console.WriteLine($"  Xbox @ 0x{pair.Xbox.HeaderOffset:X8}  [{string.Join(" ", pair.Xbox.BoneCounts.Select(x => x.ToString("X2")))}]");
                        Console.WriteLine($"  fullQuat:       PC[2]={pair.Pc.BoneCounts[2]} -> Xbox[2]+Xbox[3]={pair.Xbox.BoneCounts[2]}+{pair.Xbox.BoneCounts[3]}");
                        Console.WriteLine($"  fullQuatNoSize: PC[4]={pair.Pc.BoneCounts[4]} -> Xbox[5]+Xbox[6]={pair.Xbox.BoneCounts[5]}+{pair.Xbox.BoneCounts[6]}");
                        Console.WriteLine($"  translation:    PC[5..8]=[{string.Join(" ", pair.Pc.BoneCounts.Skip(5).Take(4).Select(x => x.ToString("X2")))}] -> Xbox[7..10]=[{string.Join(" ", pair.Xbox.BoneCounts.Skip(7).Take(4).Select(x => x.ToString("X2")))}]");
                        Console.WriteLine($"  directQuat={pair.DirectQuaternionBucketsMatch}; fullQuatSplit={pair.FullQuaternionSplitMatches}; fullQuatNoSizeSplit={pair.FullQuaternionNoSizeSplitMatches}; translation={pair.DirectTranslationBucketsMatch}; total={pair.TotalMatches}");
                        Console.WriteLine($"  completeExpansion={pair.ExpandedLayoutMatches}; Xbox quatTotal={pair.Xbox.QuaternionTotalInvariant}; Xbox transTotal={pair.Xbox.TranslationTotalInvariant}");
                    }
                }

                return result.Matches.Count > 0 ? 0 : 3;
            }
            case "inspect-xbox-reference" when args.Length >= 3:
            {
                await NormalMapLinkerV2GoldenReference.WriteReportAsync(args[1], args[2]);
                Console.WriteLine($"Wrote Xbox reference fingerprint: {args[2]}");
                return 0;
            }
            case "roundtrip" when args.Length >= 3:
            {
                var result = await FastFileRepacker.RoundTripAsync(args[1], args[2]);
                Console.WriteLine($"{result.Platform}/{result.Compression}");
                Console.WriteLine($"Zone bytes: {result.ZoneBytes:N0}");
                Console.WriteLine($"Output bytes: {result.OutputBytes:N0}");
                Console.WriteLine($"Verified: {result.VerifiedByteForByte}");
                return 0;
            }
            case "scan-maps":
            {
                var root = args.Length >= 2 ? args[1] : MapDiscovery.DefaultPcMapsFolder;
                var roots = MapDiscovery.GetListingRoots(root);
                foreach (var map in MapDiscovery.DiscoverSourceFolders(roots))
                    Console.WriteLine($"{map.Name,-36} {map.Profile,-12} {map.Tags,-24} {map.DisplayLocation}");
                return 0;
            }
            default:
                PrintHelp();
                return 2;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        return 1;
    }
}

static void PrintHelp()
{
    Console.WriteLine("WaW360Converter Linker V2 / legacy proof CLI");
    Console.WriteLine("  inspect <file.ff>                       Probe PC/Xbox wrapper + XFile header");
    Console.WriteLine("  decompress <file.ff> <out.zone>        Decompress to raw zone");
    Console.WriteLine("  compare <left.ff> <right.ff>            Compare fastfile metadata/block sizes");
    Console.WriteLine("  assets <file.ff>                        Parse T4 script strings + XAssetList");
    Console.WriteLine("  manifest <file.ff> <out.json>           Export Stage 2 asset manifest");
    Console.WriteLine("  resolve <file.ff>                       Resolve Localize/RawFile/StringTable/MenuList payloads");
    Console.WriteLine("  stringtable <file.ff> <asset-name>      Print a decoded T4 StringTable as tab-separated rows");
    Console.WriteLine("  menu-probe <file.ff>                    List menu/zombie-related serialized strings");
    Console.WriteLine("  custom-maps-trace <patch_ui.ff>         Validate 400-byte codxe Custom Maps itemDef_s bodies");
    Console.WriteLine("  menu-structs <file.ff>                  Probe proven 312-byte Xbox/PS3 WaW menuDef_t structures");
    Console.WriteLine("  stage3-label-proof <patch_ui.ff> <out.ff>  Rename proven solo Custom Maps label in a verified proof copy");
    Console.WriteLine("  stage3-serializer-proof <patch_ui.ff> <out.ff>  Rebuild levels_unlock 62->62 and require byte-identical raw zone");
    Console.WriteLine("  stage3-alpha4c-audit <patch_ui.ff> <report.txt>  Read-only relocation audit; writes no fastfile");
    Console.WriteLine("  stage3-alpha4d-audit <patch_ui.ff> <report.txt>  Read-only typed VIRTUAL pointer-map audit");
    Console.WriteLine("  stage3-alpha4e-audit <patch_ui.ff> <report.txt>  Read-only second 62-pointer-run classification");
    Console.WriteLine("  stage4-normal-analyze <root> <map> <output>  Read-only normal-map FF/IWD conversion manifest");
    Console.WriteLine("  stage4-alpha2-build <root> <map> <output>    Start fail-closed Xbox serializer build workspace");
    Console.WriteLine("  stage4-alpha9g-xmodel <root> <map> <output>  Prove XModel bodies; unresolved assets emit alpha9g rejected-forensics reports");
    Console.WriteLine("  stage4-alpha6-weapon <root> <map> <output>   Prove all WeaponDef fixed bodies in Xbox big-endian form");
    Console.WriteLine("  stage4-alpha7b-weapon <root> <map> <output>   Prove WeaponDef owned payloads + exact read-back");
    Console.WriteLine("  stage4-alpha9b-fx <root> <map> <output>         Run strict zero-total FX reference recovery proof");
    Console.WriteLine("  stage4-alpha9a-mixed <root> <map> <output>      Run mixed XAnim/XModel/Weapon/FX XAsset-order probe");
    Console.WriteLine("  link-load <pc_load.ff> [out.json]        Deterministic XAsset-stream graph + dependency-first Xbox plan");
    Console.WriteLine("  link-load-emit <root> <map> <output>     Emit/reopen Linker V1 alpha-2 Xbox load.ff smoke candidate");
    Console.WriteLine("  linker-v2-source <root> <map> <output>    Build canonical multi-source graph/package plan");
    Console.WriteLine("  inspect-xbox-reference <folder> <out>    Fingerprint a known-working Xbox map package (validation only)");
    Console.WriteLine("  xanim-bone-compare <pc.ff> <xbox.ff> [name] [--verbose]  Compare PC/Xbox XAnim bone-count layouts; all-animation mode is summary-only");
    Console.WriteLine("  xanim-split-bones <pc.ff> <xbox.ff>       Resolve XAnim.names and test whether Xbox quaternion split side follows bone identity");
    Console.WriteLine("  roundtrip <in.ff> <out.ff>              Decompress/recompress and byte-verify raw zone");
    Console.WriteLine("  scan-maps [folder]                      Scan PC Maps (defaults to Desktop\\World at War\\PC Maps)");
}
