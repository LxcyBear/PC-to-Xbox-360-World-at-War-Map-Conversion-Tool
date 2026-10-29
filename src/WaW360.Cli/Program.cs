using System.Buffers.Binary;
using System.Diagnostics;
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
            case "aztec-pc-null-image-boundary" when args.Length == 4:
            {
                var result = await T4AztecPcNullImageBoundaryProbe.ProbeAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine("[AZTEC-MAT-NULL] status=" + result.Status +
                    " image=" + result.NullImageHeaderAt +
                    " nameCandidate=" + result.NullImageName +
                    " afterName=" + result.CandidateAfterInlineName +
                    " continuation=" + result.ExactPostNameMaterialContinuationPlausible);
                foreach (var c in result.Candidates)
                    Console.WriteLine("[AZTEC-MAT-NULL] constants=" + c.ConstantTableAt +
                        " plausible=" + c.PlausibleConstants + "/2" +
                        " statebits=" + c.StateBitsAt +
                        " nextHeader=" + c.NextHeaderAt +
                        " nextName=" + c.NextNameCandidate +
                        " nextCounts=" + c.NextCountsPlausible);
                Console.WriteLine("[AZTEC-MAT-NULL] report=" + args[3] +
                    " -- SOURCE ONLY; no walker changes or FF.");
                return 0;
            }
            case "aztec-pc-material-stop-microscope" when args.Length == 4:
            {
                var result = await T4AztecPcMaterialStopMicroscope.ProbeAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine("[AZTEC-MAT-STOP] status=" + result.Status +
                    " imageStop=" + result.StopImageAt +
                    " material=" + result.PendingMaterialHeader +
                    " nameCandidate=" + result.PendingMaterialNameCandidate);
                Console.WriteLine("[AZTEC-MAT-STOP] exact reasons: " +
                    string.Join(", ", result.ExactStopRejectionReasons));
                foreach (var item in result.ImageHeaderCandidates)
                    Console.WriteLine("[AZTEC-MAT-STOP] candidate=" + item.HeaderOffset +
                        " delta=" + item.DeltaFromStop +
                        " map=" + item.MapType + " texture=" + item.TexturePointer +
                        " name=" + item.NamePointer + " strict=" + item.StrictWalkerAccepts);
                Console.WriteLine("[AZTEC-MAT-STOP] report=" + args[3] +
                    " -- original-PC diagnostic ONLY; no FF or Xbox identity.");
                return 0;
            }
            case "aztec-pc-material-stream-walker" when args.Length == 4:
            {
                var result = await T4AztecPcMaterialStreamWalker.ProbeAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine("[AZTEC-MAT-WALK] status=" + result.Status +
                    " decoded=" + result.DecodedMaterialCandidates +
                    " inlineImages=" + result.InlineImages +
                    " stopReason=" + result.StopReason +
                    " stopAt=" + result.StopAt);
                foreach (var m in result.Materials)
                    Console.WriteLine("[AZTEC-MAT-WALK] material#" + m.Ordinal +
                        " start=" + m.HeaderAt + " unverifiedName=" + m.CandidateName +
                        " textures=" + m.TextureCount +
                        " inlineImages=" + m.InlineImages +
                        " constants=" + m.ConstantCount +
                        " statebits=" + m.StateBitsCount +
                        " after=" + m.NextCursor);
                Console.WriteLine("[AZTEC-MAT-WALK] report=" + args[3] +
                    " -- structural source candidates ONLY; no Xbox FF.");
                return 0;
            }
            case "aztec-pc-second-material-children" when args.Length == 4:
            {
                var result = await T4AztecPcSecondMaterialChildrenProbe.ProbeAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine("[AZTEC-MAT2] status=" + result.Status +
                    " allImageDescriptors=" + result.AllSixAreImageDescriptors +
                    " imageChain=" + result.SixInlineImageChainCandidate +
                    " postImages=" + result.PostSixImagesCursor);
                foreach (var t in result.Textures)
                    Console.WriteLine("[AZTEC-MAT2] texture#" + t.Index +
                        " semantic=" + t.Semantic + " water=" + t.IsWater +
                        " pointer=" + t.ImagePointer);
                foreach (var image in result.InlineImageCandidates)
                    Console.WriteLine("[AZTEC-MAT2] image#" + image.Index +
                        " plausible=" + image.HeaderPlausible +
                        " nameCandidate=" + image.NameCandidate +
                        " loadDef=" + image.LoadDefAt +
                        " resource=" + image.ResourceSize +
                        " after=" + image.AfterLoadDef);
                foreach (var candidate in result.TopConstantTableCandidates.Take(6))
                    Console.WriteLine("[AZTEC-MAT2] constants=" + candidate.ConstantTableAt +
                        " plausible=" + candidate.PlausibleConstants + "/6" +
                        " nextHeader=" + candidate.NextHeaderAt);
                Console.WriteLine("[AZTEC-MAT2] report=" + args[3] +
                    " -- source-only; no ownership or Xbox bindings.");
                return 0;
            }
            case "aztec-pc-first-material-state-next-header" when args.Length == 4:
            {
                var result = await T4AztecPcFirstMaterialStateAndNextHeaderProbe.ProbeAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine("[AZTEC-MAT-NEXT] status=" + result.Status +
                    " secondHeader=" + result.SecondMaterialHeaderAt +
                    " shape=" + result.SecondHeaderShapePlausible +
                    " secondNameCandidate=" + result.SecondUnverifiedName);
                foreach (var s in result.StateBits)
                    Console.WriteLine("[AZTEC-MAT-NEXT] state#" + s.Index +
                        " offset=" + s.Offset +
                        " words=" + s.StateBits0 + "," + s.StateBits1);
                foreach (var t in result.SecondTextureCandidates)
                    Console.WriteLine("[AZTEC-MAT-NEXT] textureTable=" + t.SourceOffset +
                        " valid=" + t.PlausibleDescriptors + "/" + t.TextureCount);
                Console.WriteLine("[AZTEC-MAT-NEXT] report=" + args[3] +
                    " -- source-only candidate, NO FF/XBOX or ownership proof.");
                return 0;
            }
            case "aztec-pc-post-images-constants" when args.Length == 4:
            {
                var result = await T4AztecPcMaterialPostImagesConstantProbe.ProbeAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine("[AZTEC-MAT-CONST] status=" + result.Status +
                    " tested=" + result.TestedPhysicalStarts +
                    " sixOfSix=" + result.FullyPlausibleStarts +
                    " best=" + result.BestPlausibleRecordCount + "/6");
                foreach (var c in result.TopCandidateWindows.Take(10))
                    Console.WriteLine("[AZTEC-MAT-CONST] candidate=" + c.ConstantTableAt +
                        " delta=" + c.RelativeByteOffset +
                        " mod16=" + c.PhysicalOffsetModulo16 +
                        " valid=" + c.PlausibleConstantRecords + "/6" +
                        " names=" + string.Join("|", c.CandidateRecordNames));
                Console.WriteLine("[AZTEC-MAT-CONST] report=" + args[3] +
                    " -- not a material identity/ownership/binding proof.");
                return 0;
            }
            case "aztec-pc-first-material-image-chain" when args.Length == 4:
            {
                var result = await T4AztecPcFirstMaterialImageChainProbe.ProbeAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine("[AZTEC-MAT-CHAIN] status=" + result.Status);
                Console.WriteLine("[AZTEC-MAT-CHAIN] possible three-image chain=" +
                    result.ThreeImageSerializationChainCandidate +
                    " afterThird=" + result.HypotheticalPostThirdImageCursor);
                foreach (var image in result.AdditionalImageCandidates)
                    Console.WriteLine("[AZTEC-MAT-CHAIN] IMAGE#" + image.ImageIndex +
                        " header=" + image.HeaderAt +
                        " plausible=" + image.ImageHeaderPlausible +
                        " nameCandidate=" + image.CandidateName +
                        " LoadDef=" + image.CandidateLoadDefAt +
                        " dimsMatch=" + image.LoadDefPrefixDimensionsMatch +
                        " resourceBytes=" + image.LoadDefResourceBytes +
                        " nextCursor=" + image.CandidateAfterLoadDef);
                foreach (var table in result.NearbyAlignedConstantTableCandidates)
                    Console.WriteLine("[AZTEC-MAT-CHAIN] constants=" + table.ConstantTableAt +
                        " plausible=" + table.PlausibleConstants + "/6" +
                        " statebits=" + table.StateBitsTableAt +
                        " nextHeader=" + table.HypotheticalNextMaterialAt +
                        " nextNameFollowing=" + table.NextNameFollowing);
                Console.WriteLine("[AZTEC-MAT-CHAIN] report=" + args[3] +
                    " -- no PC material ownership, Xbox binding or FF.");
                return 0;
            }
            case "aztec-pc-first-image-loaddef-boundary" when args.Length == 4:
            {
                var result = await T4AztecPcFirstImageLoadDefBoundaryProbe.ProbeAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine("[AZTEC-LOADDEF] status=" + result.Status);
                Console.WriteLine("[AZTEC-LOADDEF] image=" + result.ImageHeaderAt +
                    " LoadDef=" + result.LoadDefPrefixAt +
                    " DXT5=" + result.FormatFourCC +
                    " mipCount=" + result.MipLevelCount +
                    " resourceBytes=" + result.ResourceSize);
                Console.WriteLine("[AZTEC-LOADDEF] hypothetical physical end=" +
                    result.CandidatePhysicalCursorAfterLoadDef);
                foreach (var candidate in result.BoundedNextImageCandidates)
                    Console.WriteLine("[AZTEC-LOADDEF] nextHeader=" + candidate.Offset +
                        " plausible=" + candidate.StructurallyPlausible +
                        " dimensions=" + candidate.Width + "x" +
                        candidate.Height + "x" + candidate.Depth);
                Console.WriteLine("[AZTEC-LOADDEF] report=" + args[3] +
                    " -- PC source-only; no ownership or Xbox bindings.");
                return 0;
            }
            case "aztec-pc-first-material-image-cursor" when args.Length == 4:
            {
                var result = await T4AztecPcFirstMaterialInlineImageProbe.ProbeAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine($"[AZTEC-MAT-IMAGE] status={result.Status}");
                Console.WriteLine($"[AZTEC-MAT-IMAGE] first Material={result.FirstMaterialHeaderAt} 6x16 texture table={result.FirstMaterialTextureTableAt} next physical={result.PhysicalCursorAfterTextureTable}");
                Console.WriteLine($"[AZTEC-MAT-IMAGE] source pointers FOLLOWING={result.FollowingImageReferences}/6 packed={result.PackedImageReferences}/6");
                foreach (var t in result.TextureReferences)
                    Console.WriteLine($"[AZTEC-MAT-IMAGE] texture#{t.Index} semantic={t.Semantic} image={t.SourceImagePointer} ({t.DecodedImagePointer})");
                Console.WriteLine($"[AZTEC-MAT-IMAGE] image 36B header plausible={result.PlausibleHeaderCandidates}; with name={result.HeaderWithInlineNameCandidates}; with loadDef={result.HeaderWithInlineLoadDefCandidates}");
                foreach (var h in result.CandidateImageHeaders)
                {
                    Console.WriteLine($"[AZTEC-MAT-IMAGE] BOUNDARY {h.PhysicalOffset} plausible={h.StructurePlausible} mapType={h.MapType} semantic={h.Semantic} dimensions={h.Width}x{h.Height}x{h.Depth} category={h.Category} namePtr={h.NamePointer} texPtr={h.TexturePointer}");
                    foreach (var n in h.InlineNameCandidates)
                    {
                        Console.WriteLine($"[AZTEC-MAT-IMAGE]   NAME CANDIDATE {n.PhysicalOffset}={n.UnverifiedName} end={n.AfterName} (NOT PROVEN)");
                        foreach (var d in n.LoadDefCandidates)
                            Console.WriteLine($"[AZTEC-MAT-IMAGE]   LOADDEF {d.PhysicalOffset} fits={d.FitsImageDimensionsAndSourceBounds} dims={d.Width}x{d.Height}x{d.Depth} resourceBytes={d.ResourceSize} format={d.Format} end={d.EndOfPayload}");
                    }
                }
                Console.WriteLine($"[AZTEC-MAT-IMAGE] report={args[3]}");
                Console.WriteLine("[AZTEC-MAT-IMAGE] Source-only diagnostic, NO material/image root mapping and NO FF.");
                return 0;
            }
            case "aztec-pc-gfxworld-first-material-children" when args.Length == 4:
            {
                var result = await T4AztecPcGfxWorldFirstMaterialChildrenProbe.ProbeAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine($"[AZTEC-MAT-CHILD] status={result.Status}");
                Console.WriteLine($"[AZTEC-MAT-CHILD] first source Material={result.FirstMaterialHeaderAt}, nameCandidate={result.UnverifiedName}, postName={result.ExactPostNameOffset}");
                Console.WriteLine($"[AZTEC-MAT-CHILD] childCounts textures={result.TextureCount}, constants={result.ConstantCount}, stateBits={result.StateBitsCount}, alignedLayouts={result.TestedAlignedChildLayouts}, fullyCompatible={result.FullyCompatibleDirectLayouts}");
                Console.WriteLine($"[AZTEC-MAT-CHILD] T4 ABI textureStride={result.TextureDescriptorStride}, imagePointerOffset=+{result.TextureImagePointerOffset}, bestTextureValid={result.BestValidTextureDescriptorCount}/6, fullyValidTextureTableStarts=[{string.Join(", ", result.FullyValidTextureTableSourceOffsets)}]");
                foreach (var c in result.BestCandidates.Take(6))
                {
                    Console.WriteLine($"[AZTEC-MAT-CHILD] layout texture={c.TextureTableAt} constant={c.ConstantTableAt} stateBits={c.StateBitsTableAt} nextHeader={c.NextExpectedMaterialHeaderAt}");
                    Console.WriteLine($"[AZTEC-MAT-CHILD]   textureValid={c.ValidTextureDescriptors}/6, constantValid={c.PlausibleConstantRecords}/6, nextNameFollowing={c.NextMaterialHeaderNameFollowing}, nextCountsPlausible={c.NextMaterialHeaderCountsPlausible}");
                    foreach (var t in c.TextureSamples)
                        Console.WriteLine($"[AZTEC-MAT-CHILD]   texture#{t.Index} semantic={t.Semantic} mature={t.IsMatureContent} image={t.ImagePointer} plausible={t.Plausible}");
                }
                Console.WriteLine($"[AZTEC-MAT-CHILD] report={args[3]}");
                Console.WriteLine("[AZTEC-MAT-CHILD] Structural candidates only; names/39 bindings/FF not proven.");
                return 0;
            }
            case "aztec-pc-gfxworld-material-inline-boundary" when args.Length == 4:
            {
                var result = await T4AztecPcGfxWorldMaterialInlineBoundaryProbe.ProbeAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine($"[AZTEC-MAT-BOUNDARY] status={result.Status}");
                foreach (var w in result.Windows)
                {
                    Console.WriteLine($"[AZTEC-MAT-BOUNDARY] WINDOW {w.WindowStart}, following={w.FollowingPointerFields}/39, after={w.FollowingHeaderCandidateStart}, afterWord={w.FollowingHeaderCandidateFirstWord}, preValid={w.ValidPrecedingEightByteRecord}, nextValid={w.ValidFollowingEightByteRecord}");
                    Console.WriteLine($"[AZTEC-MAT-BOUNDARY] memoryFields min={w.SmallestMemoryField}, max={w.LargestMemoryField}, total={w.SumMemoryFields}");
                    foreach (var h in w.PossibleMaterialHeaders)
                    {
                        Console.WriteLine($"[AZTEC-MAT-BOUNDARY] MaterialShape={h.CandidateMaterialBytes} infoBytes={h.CandidateMaterialInfoBytes}, plausibleCounts={h.PlausibleMaterialCountFields}, texture={h.TextureCount}, constant={h.ConstantCount}, stateBits={h.StateBitsCount}, camera={h.CameraRegion}, techset={h.TechniqueSetPointer}");
                        Console.WriteLine($"[AZTEC-MAT-BOUNDARY]  ABI check namePtr={h.MaterialNamePointer} nameFollowing={h.NamePointerIsFollowing}, techsetBounded={h.TechniqueSetPointerCompatible}, texturePtrBounded={h.TextureTablePointerCompatible}, constantPtrBounded={h.ConstantTablePointerCompatible}, bitsPtrBounded={h.StateBitsTablePointerCompatible}, allDependenciesCompatible={h.AllDependencyPointerFieldsCompatible}, stringAtExactPostHeader={h.ExactPostHeaderReadableString}");
                        Console.WriteLine($"[AZTEC-MAT-BOUNDARY]  rawDeps technique={h.TechniqueSetPointer} texture={h.TextureTablePointer} constant={h.ConstantTablePointer} stateBits={h.StateBitsTablePointer}");
                        foreach (var n in h.NameCandidates)
                            Console.WriteLine($"[AZTEC-MAT-BOUNDARY]   NAME-CANDIDATE {n.SourceOffset} = {n.UnverifiedName} (alignment={n.AlignmentAfterMaterialHeader}); NOT PROVEN");
                    }
                }
                Console.WriteLine($"[AZTEC-MAT-BOUNDARY] report={args[3]}");
                Console.WriteLine("[AZTEC-MAT-BOUNDARY] 39 names and Xbox bindings NOT proven. NO FF.");
                return 0;
            }
            case "aztec-pc-gfxworld-material-memory-probe" when args.Length == 5:
            {
                var result = await T4AztecPcGfxWorldMaterialMemorySourceProbe.ScanAsync(
                    args[1], args[2], args[3], args[4], CancellationToken.None);
                Console.WriteLine($"[AZTEC-MAT-MEM] status={result.Status}");
                Console.WriteLine($"[AZTEC-MAT-MEM] original source materialMemoryCount={result.MaterialMemoryCount}, FOLLOWING=0xFFFFFFFF, arrayBytes={result.MaterialMemoryArrayBytes}");
                Console.WriteLine($"[AZTEC-MAT-MEM] strict 3-record={result.ThreeRecordSignatures:N0}, full39={result.Full39RecordSignatures:N0}, isolated39={result.Isolated39RecordSignatures:N0}, 40-record-overlap={result.TwoFullWindowsOverlapIntoFortyRecordRun}");
                foreach (var w in result.FullWindowBoundaryEvidence)
                    Console.WriteLine($"[AZTEC-MAT-MEM] WINDOW {w.SourceOffsetHex} previous={w.PreviousEightByteRecordPlausible} next={w.NextEightByteRecordPlausible} matchesSourceSurfacePtrs={w.FieldMatchesWithSourceGfxSurfaceMaterialPointers}/39 virtual={w.PackedVirtualPointerFields} followingOrInsert={w.FollowingOrInsertDirectiveFields} distinct={w.DistinctMaterialPointerFields} first={w.FirstMaterialWord} last={w.LastMaterialWord} SHA={w.Source39WindowSha256}");
                foreach (var c in result.Candidates.Take(12))
                    Console.WriteLine($"[AZTEC-MAT-MEM] CANDIDATE {c.SourceOffsetHex} distanceToVertex={c.BytesBeforeVertexBuffer} ptrs: following={c.FollowingMaterialFields}, insert={c.InsertMaterialFields}, packed={c.PackedMaterialFields}, distinct={c.DistinctMaterialPointerFields}, positiveMemory={c.PositiveMemoryEntries}/39, sumMemory={c.TotalMemoryBytes:N0}, SHA={c.SourceArraySha256}");
                Console.WriteLine($"[AZTEC-MAT-MEM] report={args[4]}");
                Console.WriteLine("[AZTEC-MAT-MEM] Not physical ownership proof, no Material names, no Xbox FF.");
                return 0;
            }
            case "aztec-pc-gfxworld-material-alias-trace" when args.Length == 5:
            {
                var result = await T4AztecPcGfxWorldMaterialAliasTrace.ScanAsync(
                    args[1], args[2], args[3], args[4], CancellationToken.None);
                Console.WriteLine($"[AZTEC-MAT-ALIAS] status={result.Status}");
                Console.WriteLine($"[AZTEC-MAT-ALIAS] block={result.SourcePointerBlock}, packed reference groups={result.DistinctPackedWorldMaterialPointers}, eightByteLattice={result.SharedEightByteVirtualOffsetLattice}");
                Console.WriteLine($"[AZTEC-MAT-ALIAS] spanSlots={result.VirtualSpanEightByteSlots}, emptySlots={result.UnreferencedSlotsWithinSpan}, outsideSurfaceRawHits={result.AdditionalNonSurfaceSerializedPointerHits}");
                Console.WriteLine($"[AZTEC-MAT-ALIAS] GfxWorld +0x18C count={result.HeaderMaterialMemoryCountCandidateAt0x18C}; +0x190 ptr={result.HeaderFollowingMaterialMemoryPointerCandidateAt0x190} ({result.HeaderFollowingMaterialMemoryPointerDecoded})");
                Console.WriteLine($"[AZTEC-MAT-ALIAS] +0x190 pointer inBlock={result.HeaderMaterialMemoryPointerInSourceBlockBounds}, equalsFirstOf39={result.HeaderPointerEqualsFirstOf39VirtualSlots}, equalsAnyOf39={result.HeaderPointerEqualsAnyOf39VirtualSlots}");
                Console.WriteLine($"[AZTEC-MAT-ALIAS] 39-count source header candidates: {string.Join(", ", result.FixedWorldHeader39CountCandidateOffsets)}");
                foreach (var group in result.Groups.OrderByDescending(g => g.OtherSerializedReferenceCount).Take(12))
                    Console.WriteLine($"[AZTEC-MAT-ALIAS] {group.PcMaterialPointer}: tris={group.TriangleCount}, surfaces={group.SurfaceCount}, outsideSurfaceHits={group.OtherSerializedReferenceCount} first=[{string.Join(", ", group.OtherSerializedReferenceOffsets)}]");
                Console.WriteLine($"[AZTEC-MAT-ALIAS] report={args[4]}");
                Console.WriteLine("[AZTEC-MAT-ALIAS] No Material names or Xbox roots bound. NO FF.");
                return 0;
            }
            case "aztec-pc-gfxworld-xenon-components" when args.Length == 4:
            {
                var result = await T4AztecPcGfxWorldXenonGeometryCodec.ConvertAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine($"[AZTEC-GFX-XENON] status={result.Status}; 58,653 indices + 32,713 vertices + 735 surfaces");
                Console.WriteLine($"[AZTEC-GFX-XENON] BE indices SHA={result.XenonIndicesSha256}");
                Console.WriteLine($"[AZTEC-GFX-XENON] BE vertices SHA={result.XenonVerticesSha256}");
                Console.WriteLine($"[AZTEC-GFX-XENON] BE surfaces SHA={result.XenonSurfacesSha256}");
                Console.WriteLine($"[AZTEC-GFX-XENON] Source-PC material slots={result.SurfacesRequiringMaterialBinding}, distinct={result.DistinctPcMaterialPointers}; not bound for Xbox");
                Console.WriteLine($"[AZTEC-GFX-XENON] output={args[3]}");
                Console.WriteLine("[AZTEC-GFX-XENON] Verified endian components, NOT a Xenon GfxWorld root or a hardware FF.");
                return 0;
            }
            case "aztec-pc-gfxworld-export-obj" when args.Length == 5:
            {
                var result = await T4AztecPcGfxWorldMeshExporter.ExportAsync(
                    args[1], args[2], args[3], args[4], CancellationToken.None);
                Console.WriteLine($"[AZTEC-PC-OBJ] status={result.Status}");
                Console.WriteLine($"[AZTEC-PC-OBJ] source-PC vertices={result.VertexCount:N0} indexedTriangles={result.TriangleCount:N0} surfaces={result.SurfaceCount:N0}");
                Console.WriteLine($"[AZTEC-PC-OBJ] unique referenced vertices={result.ReferencedVertexCount:N0} source packed material pointers={result.DistinctPackedMaterialPointers:N0} degenerate triples={result.DegenerateFaceIndexTriples:N0}");
                Console.WriteLine($"[AZTEC-PC-OBJ] neutral geometry preview={result.ObjPath}; bytes={result.ObjBytes:N0}; sha256={result.ObjSha256}");
                Console.WriteLine($"[AZTEC-PC-OBJ] validation manifest={args[4]}");
                Console.WriteLine("[AZTEC-PC-OBJ] NOT a FastFile, Xbox render/world serialization is still pending.");
                return 0;
            }
            case "aztec-pc-gfxworld-surfaces" when args.Length == 4:
            {
                var result = await T4AztecPcGfxWorldSurfaceSourceProbe.ScanAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine("AZTEC-PC-GFX-SURFACE original PC 735 x 48-byte surface array investigation");
                Console.WriteLine($"status={result.Status}; first-three={result.ThreeRecordGates:N0}; six-record={result.SixRecordGates:N0}");
                foreach (var c in result.TopCandidates.Take(8))
                    Console.WriteLine($"SURFACE-CANDIDATE {c.SourceOffsetHex}: headers={c.ValidSurfaceRecords}/735 bounds={c.ValidBoundingBoxes}/735 materialPtrs={c.ValidMaterialPointers}/735 tris={c.TotalTriangleCount} maxEnd={c.MaximumIndexEnd}/58653 monotonicBase={c.MonotonicIndexBasePairs}/734 coverageMissing={c.UncoveredIndexEntries} overlaps={c.IndexSpanOverlapEntries} local={c.LocalIndicesInRange}/58653 global={c.GlobalIndicesInRange}/58653 materialPtrsInBlocks={c.SourceBlockMaterialPointers}/735 SHA={c.OriginalPCArraySha256}");
                Console.WriteLine($"report: {args[3]}");
                Console.WriteLine("Diagnostic source geometry only; no GfxWorld/Xbox-ready FF.");
                return 0;
            }
            case "aztec-pc-gfxworld-vertices" when args.Length == 4:
            {
                var result = await T4AztecPcGfxWorldVertexSourceProbe.ScanAsync(
                    args[1], args[2], args[3], CancellationToken.None);
                Console.WriteLine("AZTEC-PC-GFX-VERTICES original 32713 x 44-byte T4 GfxWorldVertex source scan");
                Console.WriteLine($"status={result.Status}; source indices={result.SourcePCIndexCount:N0}; PC vertices={result.SourcePCVertexCount:N0}");
                Console.WriteLine($"scan 0x{result.ScanStart:X8}..0x{result.ScanEnd:X8}; coarse={result.CoarseCandidates:N0}; deeper={result.DeepCandidates:N0}");
                foreach (var c in result.TopCandidates.Take(8))
                    Console.WriteLine($"CANDIDATE {c.SourceOffsetHex}: binormalSign={c.ValidBinormalSigns:N0}/32713 XYZ={c.ValidPositions:N0}/32713 UV={c.ValidTexCoords:N0}/32713 uniqueSampleXYZ={c.UniqueSampledPositions:N0} bbox=({c.MinX:N1},{c.MinY:N1},{c.MinZ:N1})..({c.MaxX:N1},{c.MaxY:N1},{c.MaxZ:N1}) SHA={c.OriginalPCArraySha256}");
                Console.WriteLine($"report: {args[3]}");
                Console.WriteLine("PC geometry candidate only; not full GfxWorld, no FF, not hardware ready.");
                return 0;
            }
            case "aztec-pc-gameworldsp-hunt" when args.Length == 3:
            {
                var result = await T4AztecPcGameWorldSpHeaderHunt.ScanAsync(
                    args[1], args[2], CancellationToken.None);
                Console.WriteLine("AZTEC-PC-GAMEWORLDSP 44-byte PathData structure scan");
                Console.WriteLine($"status={result.Status}; PC roots={result.PcWorldAssetRoots.Count}; PC XAssets={result.PcAssetCount}");
                foreach (var root in result.PcWorldAssetRoots)
                    Console.WriteLine($"PC XAsset #{root.PcAssetIndex} {root.PcTypeName} header={root.RawHeader} kind={root.ReferenceKind}");
                Console.WriteLine($"node-array marker windows={result.RawNodesArrayPointerMarkerHits:N0}; structural header candidates={result.StructurallyPlausibleHeaderHits:N0}");
                foreach (var item in result.TopCandidates.Take(8))
                    Console.WriteLine($"CANDIDATE {item.OffsetHex}: score={item.Score} nodes={item.NodeCount} chain={item.ChainNodeCount} vis={item.VisBytes} trees={item.NodeTreeCount} name={item.NamePtr} baseNodes={item.BaseNodesPtr}");
                Console.WriteLine($"report: {args[2]}");
                Console.WriteLine("These are structure candidates, not decoded world payloads or Xbox hardware candidates.");
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
            case "stage4-native-xanim" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");
                var result = await NormalMapXAnimSerializerProof.RunAsync(map, output);
                Console.WriteLine($"Stage 4 native Xbox XAnim serializer proof PASS: {result.MapName}");
                Console.WriteLine($"Profile: {result.Profile}");
                Console.WriteLine($"XAnim: {result.SerializedXAnim:N0}/{result.DeclaredXAnim:N0}");
                Console.WriteLine($"Delta XAnim: {result.DeltaXAnim:N0}");
                Console.WriteLine($"Xbox payload bytes: {result.SerializedBytes:N0}");
                Console.WriteLine($"Report: {result.TextReportFile}");
                Console.WriteLine("Stage-5 proof source updated: real 0x5C/12-bucket Xbox XAnim payloads.");
                return result.Verified ? 0 : 3;
            }
            case "xmodel-partclassification-per-model" when args.Length >= 6:
            {
                var xanimProof = args.Length >= 7 && !string.IsNullOrWhiteSpace(args[6]) ? args[6] : null;
                var weaponProof = args.Length >= 8 && !string.IsNullOrWhiteSpace(args[7]) ? args[7] : null;

                var result = await T4XModelPerModelClassificationExtractor.RunAsync(
                    args[1],
                    args[2],
                    args[3],
                    args[4],
                    args[5],
                    xanimProof,
                    weaponProof);

                Console.WriteLine("NUCLEAR per-model XModel partClassification recovery");
                Console.WriteLine($"declared={result.DeclaredXModels:N0}; listed={result.ListedXModels:N0}; aligned={result.AlignedXModels:N0}");
                Console.WriteLine($"completeModels={result.CompleteModels:N0}; failedModels={result.FailedModels:N0}; modelsWithBones={result.ModelsWithBones:N0}; totalBones={result.TotalBones:N0}; complete={result.Complete}");
                Console.WriteLine($"resolvedSidecars={result.ResolvedSidecarModels:N0}; usedResolvedSidecars={result.UsedResolvedSidecars}");
                Console.WriteLine($"output:   {result.OutputDirectory}");
                Console.WriteLine($"manifest: {result.ManifestFile}");
                foreach (var model in result.Models.Where(x => !x.Complete).Take(20))
                    Console.WriteLine($"FAIL: #{model.XModelOrdinal:N0} asset#{model.AssetIndex:N0} '{model.Name}': {model.Error}");
                if (result.FailedModels > 20)
                    Console.WriteLine($"FAIL: ... {result.FailedModels - 20:N0} more model(s) in manifest.");
                return result.Complete ? 0 : 4;
            }
            case "xmodel-partclassification" when args.Length >= 4:
            {
                var xanimProof = args.Length >= 5 && !string.IsNullOrWhiteSpace(args[4]) ? args[4] : null;
                var weaponProof = args.Length >= 6 && !string.IsNullOrWhiteSpace(args[5]) ? args[5] : null;

                var result = await T4XModelPartClassificationExtractor.RunAsync(
                    args[1],
                    args[2],
                    args[3],
                    xanimProof,
                    weaponProof);

                Console.WriteLine("NUCLEAR XModel PC partClassification recovery");
                Console.WriteLine($"declared={result.DeclaredXModels:N0}; aligned={result.AlignedXModels:N0}; modelsWithBones={result.ModelsWithBones:N0}; observed={result.ModelsObserved:N0}");
                Console.WriteLine($"boneObservations={result.BoneObservations:N0}; resolved={result.ResolvedBoneObservations:N0}; unresolvedStrings={result.UnresolvedScriptStrings:N0}; conflicts={result.Conflicts:N0}");
                Console.WriteLine($"packedBoneNames={result.ModelsSkippedPackedBoneNames:N0}; packedPartClassification={result.ModelsSkippedPackedClassification:N0}; rows={result.Rows.Count:N0}; complete={result.Complete}");
                Console.WriteLine($"csv:      {result.CsvFile}");
                Console.WriteLine($"manifest: {result.ManifestFile}");
                foreach (var note in result.Diagnostics.Take(20))
                    Console.WriteLine("NOTE: " + note);
                if (result.Diagnostics.Count > 20)
                    Console.WriteLine($"NOTE: ... {result.Diagnostics.Count - 20:N0} more diagnostic row(s) in manifest.");
                return result.Complete ? 0 : 4;
            }
            case "xenon-native-xmodel-layout" when args.Length >= 4:
            {
                var result = await T4XenonNativeXModelLayoutProbe.RunAsync(
                    args[1], args[2], args[3]);
                Console.WriteLine("NUCLEAR STAGE 4T native Xbox XModel fixed-body boundary proof");
                Console.WriteLine($"nativeXModels={result.NativeXModelRoots:N0}; uniqueD4={result.UniqueD4Matches:N0}; missingD4={result.MissingD4Matches:N0}; ambiguousD4={result.AmbiguousD4Matches:N0}");
                Console.WriteLine($"legacyE4={result.LegacyE4Matches:N0}; d4Only={result.D4OnlyMatches:N0}; exactD4BoundaryProof={result.ExactD4BoundaryProof}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"manifest: {result.JsonReportFile}");
                foreach (var note in result.Notes)
                    Console.WriteLine("NOTE: " + note);
                return result.ExactD4BoundaryProof ? 0 : 4;
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
            case "linker-v2-typed-closure" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");

                var result = await NormalMapLinkerV2TypedClosurePlan.RunAsync(map, output);
                Console.WriteLine($"Linker V2 typed closure: {result.MapName}");
                Console.WriteLine($"existingXAnim={result.ExistingNamedXAnims:N0}; existingRawFile={result.ExistingNamedRawFiles:N0}");
                Console.WriteLine($"iwdWeapons={result.IwdWeaponSources:N0}; iwdScripts={result.IwdScriptSources:N0}");
                Console.WriteLine($"xanimPromotions={result.PromotedXAnimCandidates:N0}; rawFilePromotions={result.PromotedRawFileCandidates:N0}; residentMenus={result.ResidentMenuCandidates:N0}");
                Console.WriteLine($"promotionEvidenceReady={result.PromotionEvidenceReady}; fullTypedClosureReady={result.FullTypedClosureReady}; verified={result.Verified}");
                Console.WriteLine($"report: {result.TextReportFile}");
                foreach (var blocker in result.Blockers) Console.WriteLine("BLOCKER: " + blocker);
                return result.Verified ? 0 : 3;
            }
            case "linker-v2-iwi-image-corpus" when args.Length >= 5:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var nativeIdentityManifest = args[4];

                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");

                var result = await NormalMapIwiImageCorpus.RunAsync(
                    map,
                    output,
                    nativeIdentityManifest);

                Console.WriteLine("NUCLEAR STAGE 4G IWI image corpus census");
                Console.WriteLine($"nativeImages={result.NativeImages:N0}; sourceIwi={result.SourceIwiEntries:N0}; uniqueSourceImages={result.UniqueSourceImages:N0}");
                Console.WriteLine($"matchedNative={result.MatchedNativeImages:N0}; unmatchedNative={result.UnmatchedNativeImages:N0}; duplicateSourceIds={result.DuplicateSourceIdentities:N0}");
                Console.WriteLine($"nativeIdentityCoverage={result.NativeIdentityCoverage:P2}; sourceIwiBytes={result.SourceIwiBytes:N0}; matchedIwiBytes={result.MatchedIwiBytes:N0}; matchedByteCoverage={result.MatchedByteCoverage:P2}");
                Console.WriteLine($"readyForCodecPlanning={result.ReadyForCodecPlanning}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"manifest: {result.JsonReportFile}");
                foreach (var format in result.Formats.Take(16))
                    Console.WriteLine($"FORMAT v{format.Version} 0x{format.Format:X2}: images={format.Images:N0}; nativeMatches={format.NativeMatches:N0}; bytes={format.Bytes:N0}");
                return result.ReadyForCodecPlanning ? 0 : 4;
            }
            case "linker-v2-full-content-readiness" when args.Length >= 9:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var nativeOracle = args[4];
                var graphRoot = args[5];
                var nativeIdentityManifest = args[6];
                var bindingManifest = args[7];
                var minRatio = double.TryParse(args[8], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var min) ? min : 0.80;
                var maxRatio = args.Length >= 10 &&
                               double.TryParse(args[9], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var max)
                    ? max
                    : 1.20;
                var productionImageGraftManifest = args.Length >= 11 && !string.IsNullOrWhiteSpace(args[10])
                    ? args[10]
                    : null;
                var productionXModelGraftManifest = args.Length >= 12 && !string.IsNullOrWhiteSpace(args[11])
                    ? args[11]
                    : null;
                var productionMaterialGraftManifest = args.Length >= 13 && !string.IsNullOrWhiteSpace(args[12])
                    ? args[12]
                    : null;
                var productionCriticalReferenceProofManifest = args.Length >= 14 && !string.IsNullOrWhiteSpace(args[13])
                    ? args[13]
                    : null;
                var productionImageReferenceProofManifest = args.Length >= 15 && !string.IsNullOrWhiteSpace(args[14])
                    ? args[14]
                    : null;
                var productionStockWeaponRoundTripManifest = args.Length >= 16 && !string.IsNullOrWhiteSpace(args[15])
                    ? args[15]
                    : null;
                var productionStockFxRoundTripManifest = args.Length >= 17 && !string.IsNullOrWhiteSpace(args[16])
                    ? args[16]
                    : null;
                var productionRawFileGraftManifest = args.Length >= 18 && !string.IsNullOrWhiteSpace(args[17])
                    ? args[17]
                    : null;

                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");

                var result = await NormalMapLinkerV2FullContentReadiness.RunAsync(
                    map,
                    output,
                    nativeOracle,
                    graphRoot,
                    nativeIdentityManifest,
                    bindingManifest,
                    minRatio,
                    maxRatio,
                    productionImageGraftManifest,
                    productionXModelGraftManifest,
                    productionMaterialGraftManifest,
                    productionCriticalReferenceProofManifest,
                    productionImageReferenceProofManifest,
                    productionStockWeaponRoundTripManifest,
                    productionStockFxRoundTripManifest,
                    productionRawFileGraftManifest);

                Console.WriteLine("NUCLEAR STAGE 4F full-content pre-hardware gate");
                Console.WriteLine($"nativeFF={result.NativeOracleFastFileBytes:N0}; nativeZone={result.NativeOracleZoneBytes:N0}; shellBaseline={result.AllShellBaselineZoneBytes:N0}; rendererGain={result.ProvenRendererGainBytes:N0}");
                Console.WriteLine($"estimatedFullContent={result.EstimatedFullContentZoneBytes:N0}; nativeSizeRatio={result.NativeSizeRatio:P2}");
                Console.WriteLine($"sourceMapped={result.SourceMappedRoots:N0}; realPromoted={result.RealPromotedRoots:N0}; fallback={result.FallbackRoots:N0}; synthetic={result.SyntheticDependencyRoots:N0}; criticalFallback={result.CriticalFallbackRoots:N0}; externalAudioPending={result.ExternalAudioPendingRoots:N0}");
                Console.WriteLine($"productionRawFiles={result.ProductionRawFileRoots:N0}; productionRawFileBytes={result.ProductionRawFileSerializedBytes:N0}");
                Console.WriteLine($"productionImages={result.ProductionImageRoots:N0}; productionImageSerializedBytes={result.ProductionImageSerializedBytes:N0}; productionImagePixelBytes={result.ProductionImagePixelBytes:N0}");
                Console.WriteLine($"productionXModels={result.ProductionXModelRoots:N0}; xmodelRefs={result.ProductionXModelReferenceRoots:N0}; productionXModelSerializedBytes={result.ProductionXModelSerializedBytes:N0}");
                Console.WriteLine($"productionMaterials={result.ProductionMaterialRoots:N0}; materialRefs={result.ProductionMaterialReferenceRoots:N0}; productionMaterialSerializedBytes={result.ProductionMaterialSerializedBytes:N0}; criticalRetailRefs={result.ProductionCriticalReferenceRoots:N0}; imageRetailRefs={result.ProductionImageReferenceRoots:N0}; stockWeaponRoots={result.ProductionStockWeaponRoots:N0}; stockFxRoots={result.ProductionStockFxRoots:N0}; integratedProductionBaseline={result.ProductionIntegratedBaselineZoneBytes:N0}");
                Console.WriteLine($"properSizeGate={result.ProperSizeGate}; criticalContentGate={result.CriticalContentGate}; readyForFullNativeEmission={result.ReadyForFullNativeEmission}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"manifest: {result.JsonReportFile}");
                foreach (var blocker in result.CriticalBlockers.Take(40))
                    Console.WriteLine("BLOCKER: " + blocker);
                return result.ReadyForFullNativeEmission ? 0 : 4;
            }
            case "linker-v2-native-sequence" when args.Length >= 5:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var nativeOracle = args[4];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");

                var result = await NormalMapLinkerV2StructuralMainEmitter.RunNativeSequenceDiagnosticAsync(
                    map, output, nativeOracle);
                Console.WriteLine($"Linker V2 native-sequence DB diagnostic: {result.MapName}");
                Console.WriteLine($"nativeRoots={result.NativeRoots:N0}; reusedSource={result.ReusedSourceRoots:N0}; syntheticDependencies={result.SyntheticDependencyRoots:N0}; droppedSource={result.DroppedSourceRoots:N0}");
                Console.WriteLine($"nativeScriptStrings={result.NativeScriptStrings:N0}; nameContractErrors={result.NameContractErrors:N0}; readBackErrors={result.ReadBackErrors:N0}; wrapperRoundTrip={result.WrapperRoundTripVerified}; ready={result.CandidateReady}");
                foreach (var failure in result.NameContractFailures.Take(32))
                    Console.WriteLine("NAME-CONTRACT: " + failure);
                Console.WriteLine($"hardware package: {result.HardwarePackageDirectory ?? "<not emitted>"}");
                Console.WriteLine($"allNameShellReady={result.AllShellCandidateReady}; nameContractErrors={result.AllShellNameContractErrors:N0}; readBackErrors={result.AllShellReadBackErrors:N0}; wrapperRoundTrip={result.AllShellWrapperRoundTripVerified}");
                Console.WriteLine($"all-name-shell package: {result.AllShellHardwarePackageDirectory ?? "<not emitted>"}");
                Console.WriteLine($"nativeFootprintReady={result.NativeFootprintCandidateReady}; nativeZone={result.NativeOracleZoneBytes:N0}B; candidateZone={result.NativeFootprintCandidateZoneBytes:N0}B; readBackErrors={result.NativeFootprintReadBackErrors:N0}; wrapperRoundTrip={result.NativeFootprintWrapperRoundTripVerified}");
                Console.WriteLine($"native-footprint package: {result.NativeFootprintHardwarePackageDirectory ?? "<not emitted>"}");
                Console.WriteLine($"report: {result.TextReportFile}");
                return result.CandidateReady ? 0 : 3;
            }
            case "linker-v2-main-structural" when args.Length >= 4:
            {
                var root = args[1];
                var mapName = args[2];
                var output = args[3];
                var map = MapDiscovery.DiscoverSourceFolder(root)
                    .FirstOrDefault(m => m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    throw new FileNotFoundException($"Map '{mapName}' was not found under '{root}'.");
                var nativeBlockOracle = args.Length >= 5 ? args[4] : null;
                var result = await NormalMapLinkerV2StructuralMainEmitter.RunAsync(
                    map,
                    output,
                    nativeBlockOracleFastFile: nativeBlockOracle);
                Console.WriteLine($"Linker V2 structural main emitter: {result.MapName}");
                Console.WriteLine($"payloads={result.PayloadsBuilt:N0}; fallback={result.FallbackPayloads:N0}; assets={result.XboxAssets:N0}");
                Console.WriteLine($"zone={result.ZoneBytes:N0}B; ff={result.FastFileBytes:N0}B; readBackErrors={result.ReadBackErrors:N0}");
                Console.WriteLine($"wrapperRoundTrip={result.WrapperRoundTripVerified}; structuralReady={result.StructuralNativeCandidateReady}; productionReady={result.ProductionMainReady}");
                Console.WriteLine($"hardware package: {result.HardwarePackageDirectory ?? "<none>"}");
                Console.WriteLine($"trackedShellReady={result.ConservativeCandidateReady}; readBackErrors={result.ConservativeReadBackErrors:N0}; wrapperRoundTrip={result.ConservativeWrapperRoundTripVerified}");
                Console.WriteLine($"tracked-shell package: {result.ConservativeHardwarePackageDirectory ?? "<not emitted>"}");
                Console.WriteLine($"scriptClosureReady={result.ScriptClosureCandidateReady}; residentMenusRemoved={result.ScriptClosureResidentMenusRemoved:N0}; rawFilesPromoted={result.ScriptClosureRawFilesPromoted:N0}; readBackErrors={result.ScriptClosureReadBackErrors:N0}; wrapperRoundTrip={result.ScriptClosureWrapperRoundTripVerified}");
                Console.WriteLine($"script-closure package: {result.ScriptClosureHardwarePackageDirectory ?? "<not emitted>"}");
                Console.WriteLine($"nativeBlockReady={result.NativeBlockCandidateReady}; readBackErrors={result.NativeBlockReadBackErrors:N0}; wrapperRoundTrip={result.NativeBlockWrapperRoundTripVerified}");
                Console.WriteLine($"native-block package: {result.NativeBlockHardwarePackageDirectory ?? "<not emitted>"}");
                Console.WriteLine($"report: {result.TextReportFile}");
                foreach (var blocker in result.Blockers) Console.WriteLine("BLOCKER: " + blocker);
                return result.Verified ? 0 : 3;
            }
            case "oat-root-bisect" when args.Length >= 4:
            {
                return await RunOatRootBisectAsync(args[1], args[2], args[3]);
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
            case "native-oat-identity" when args.Length >= 3:
            {
                var nativeFastFile = args[1];
                var outputDirectory = args[2];
                var explicitUnlinker = args.Length >= 4 ? args[3] : null;
                var result = await T4XenonNativeIdentityOracle.RunAsync(
                    nativeFastFile,
                    outputDirectory,
                    explicitUnlinker);
                Console.WriteLine("T4 Xenon native identity oracle");
                Console.WriteLine($"assets={result.Assets:N0}; scriptStrings={result.ScriptStrings:N0}; typeSequenceVerified={result.TypeSequenceVerified}; duplicateIdentities={result.DuplicateIdentityCount:N0}");
                Console.WriteLine($"identitySequenceSha256={result.IdentitySequenceSha256}");
                foreach (var type in result.Types)
                    Console.WriteLine($"  {type.TypeId,2} {type.TypeName,-18} count={type.Count,6:N0} unique={type.UniqueNames,6:N0} duplicate={type.DuplicateIdentities,5:N0}");
                Console.WriteLine($"manifest: {result.JsonManifestFile}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"OAT log:  {result.RawUnlinkerLogFile}");
                return 0;
            }
            case "pc-native-root-oracle" when args.Length >= 3:
            {
                var result = await T4PcNativeRootOrderOracle.CompareAsync(args[1], args[2]);
                Console.WriteLine(result.Summary);
                return 0;
            }
            case "pc-native-semantic-oracle" when args.Length >= 4:
            {
                var explicitUnlinker = args.Length >= 5 ? args[4] : null;
                var result = await T4PcNativeSemanticOracle.CompareAsync(
                    args[1],
                    args[2],
                    args[3],
                    explicitUnlinker);
                Console.WriteLine("T4 PC semantic pool -> native Xbox identity oracle");
                Console.WriteLine($"pcRoots={result.PcRootAssets:N0}; pcOatPool={result.PcOatPoolAssets:N0}; nativeRoots={result.NativeRootAssets:N0}; semanticDelta={result.NativeVsPcSemanticDelta:+#;-#;0}");
                Console.WriteLine($"matched={result.MatchedSemanticIdentities:N0}; pcOnly={result.PcOnlySemanticIdentities:N0}; xboxOnly={result.NativeOnlySemanticIdentities:N0}");
                Console.WriteLine($"scriptStringMultisetEqual={result.ScriptStringMultisetEqual}; exactIndexMatches={result.ScriptStringExactIndexMatches:N0}; remap={result.ScriptStringRemap.Count:N0}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"manifest: {result.JsonManifestFile}");
                Console.WriteLine($"PC OAT:   {result.PcOatLogFile}");
                return 0;
            }
            case "material-techset-bridge" when args.Length >= 4:
            {
                var explicitUnlinker = args.Length >= 5 ? args[4] : null;
                var result = await T4OatMaterialTechsetBridgeOracle.CompareAsync(
                    args[1],
                    args[2],
                    args[3],
                    explicitUnlinker);
                Console.WriteLine("NUCLEAR PC -> XENON material / TechniqueSet bridge");
                Console.WriteLine($"materials: PC={result.PcMaterialsDumped:N0}; Xbox={result.XboxMaterialsDumped:N0}; shared={result.SharedMaterials:N0}; paired={result.SharedMaterialsWithTechniqueSets:N0}");
                Console.WriteLine($"techsets: PC={result.PcTechniqueSetsObserved:N0}; Xbox={result.XboxTechniqueSetsObserved:N0}; deterministic={result.DeterministicTechniqueMappings:N0}; ambiguous={result.AmbiguousTechniqueMappings:N0}; mappedXbox={result.DistinctMappedXboxTechniqueSets:N0}");
                Console.WriteLine($"verified={result.Verified}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"manifest: {result.JsonReportFile}");
                return result.Verified ? 0 : 4;
            }
            case "pc-oat-techset-identities" when args.Length >= 3:
            {
                var explicitUnlinker = args.Length >= 4 ? args[3] : null;
                var result = await T4PcOatTechniqueSetIdentityCatalog.BuildAsync(
                    args[1], explicitUnlinker);
                var outputDir = Path.GetFullPath(args[2]);
                Directory.CreateDirectory(outputDir);
                var json = Path.Combine(outputDir, "pc_oat_techset_identities.json");
                var report = Path.Combine(outputDir, "pc_oat_techset_identities.txt");
                var ordered = result.ByAssetIndex.Values.OrderBy(x => x.AssetIndex).ToList();
                await File.WriteAllTextAsync(json, JsonSerializer.Serialize(new
                {
                    Schema = "WaW360.PcOatTechniqueSetIdentities.v1",
                    result.PcFastFile,
                    result.DeclaredPhysicalTechniqueSets,
                    result.OatTechniqueSets,
                    result.Verified,
                    result.Notes,
                    Entries = ordered
                }, new JsonSerializerOptions { WriteIndented = true }));
                await File.WriteAllLinesAsync(report,
                    new[]
                    {
                        "PC OAT SEMANTIC TECHNIQUESET IDENTITY REPORT",
                        $"PC: {result.PcFastFile}",
                        $"DeclaredPhysicalTechniqueSets={result.DeclaredPhysicalTechniqueSets}",
                        $"OatTechniqueSets={result.OatTechniqueSets}",
                        $"Verified={result.Verified}",
                        "Identity evidence only. No shader graph or Xbox payload was emitted."
                    }.Concat(result.Notes.Select(x => "NOTE: " + x))
                     .Concat(ordered.Select(x => $"#{x.AssetIndex} OAT#{x.OatPoolIndex} {x.Name}")));
                Console.WriteLine("PC full-zone OAT TechniqueSet identity census");
                Console.WriteLine($"mapped={ordered.Count:N0}/{result.DeclaredPhysicalTechniqueSets:N0} verified={result.Verified}");
                Console.WriteLine($"json: {json}");
                Console.WriteLine($"report: {report}");
                return result.Verified ? 0 : 4;
            }
            case "nuclear-techset-closure-plan" when args.Length >= 4:
            {
                var explicitUnlinker = args.Length >= 5 ? args[4] : null;
                var result = await T4NuclearTechniqueClosurePlanner.BuildAsync(
                    args[1],
                    args[2],
                    args[3],
                    explicitUnlinker);
                Console.WriteLine("NUCLEAR STAGE 2 TechniqueSet closure plan");
                Console.WriteLine($"PC roots={result.PcTechniqueRoots:N0}; reachable={result.ReachablePcTechniqueRoots:N0}; deadDropped={result.DeadPcTechniqueRoots:N0}");
                Console.WriteLine($"materialBridge={result.MaterialBridgeResolved:N0}; exactName={result.ExactNameFallbackResolved:N0}; donorBound={result.DonorBoundTechniqueRoots:N0}; residentPending={result.ResidentXboxTargetsPending:N0}; reachableUnresolved={result.UnresolvedTechniqueRoots:N0}");
                Console.WriteLine($"distinctXboxTargets={result.DistinctXboxTechniqueTargets:N0}; graphBodies=typed-extractor-pending");
                Console.WriteLine($"identityClosureComplete={result.IdentityClosureComplete}; graphExtractionReady={result.GraphExtractionReady}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"manifest: {result.JsonReportFile}");
                return result.GraphExtractionReady ? 0 : 4;
            }
            case "xenon-xmodel-replay" when args.Length >= 5:
            {
                var result = await T4XenonXModelReplayEmitter.EmitAsync(
                    args[1],
                    args[2],
                    args[3],
                    args[4]);

                Console.WriteLine("NUCLEAR STAGE 4X-C standalone Xenon XModel replay");
                Console.WriteLine($"model={result.ModelName}; roots={result.Roots:N0}; scripts={result.ScriptStrings:N0}; zoneBytes={result.ZoneBytes:N0}");
                Console.WriteLine($"TEMP=0x{result.TempBytes:X}; VIRTUAL=0x{result.VirtualBytes:X}");
                Console.WriteLine($"exactBlocks={result.ExactBlockReplay}; exactZoneSize={result.ExactZoneSizeReplay}; exactDecompressed={result.ExactDecompressedSizeReplay}");
                Console.WriteLine($"exactIdentity={result.ExactIdentitySequence}; oatZero={result.OatZeroWarningsZeroErrors}; graphRedumpExact={result.GraphRedumpExact}; wrapper={result.WrapperRoundTripVerified}");
                Console.WriteLine($"replayPass={result.ReplayPass}");
                Console.WriteLine($"candidate: {result.OutputFastFile}");
                Console.WriteLine($"report:    {result.TextReportFile}");
                Console.WriteLine($"manifest:  {result.JsonReportFile}");
                foreach (var blocker in result.Blockers.Take(32))
                    Console.WriteLine("BLOCKER: " + blocker);
                foreach (var diagnostic in result.Diagnostics.Take(32))
                    Console.WriteLine("DIAGNOSTIC: " + diagnostic);
                return result.ReplayPass ? 0 : 4;
            }
            case "xenon-material-root-bind" when args.Length >= 5:
            {
                var result = await T4XenonMaterialRootBindingPlanner.BuildAsync(
                    args[1],
                    args[2],
                    args[3],
                    args[4]);

                Console.WriteLine("NUCLEAR STAGE 4Z-D0 Xenon Material root binding");
                Console.WriteLine($"corpus={result.CorpusMaterials:N0}; nativeMaterialRoots={result.NativeMaterialRoots:N0}");
                Console.WriteLine($"direct={result.DirectIdentityMatches:N0}; semantic={result.SemanticMatches:N0}; unmatched={result.UnmatchedMaterials:N0}; ambiguous={result.AmbiguousMaterials:N0}");
                Console.WriteLine($"calibrationCorrect={result.SharedCalibrationMatches:N0}/{result.SharedCalibrationPairs:N0}; wrong={result.SharedCalibrationWrong:N0}; ambiguousOrMissing={result.SharedCalibrationAmbiguousOrMissing:N0}; precision={result.SharedCalibrationAgreement:P2}; coverage={result.SharedCalibrationCoverage:P2}; mode={result.SemanticFingerprintMode}; semanticEnabled={result.SemanticFallbackEnabled}");
                Console.WriteLine($"bindingPass={result.BindingPass}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"manifest: {result.JsonManifestFile}");
                return result.BindingPass ? 0 : 4;
            }
            case "xenon-material-replay" when args.Length >= 5:
            {
                var result = await T4XenonMaterialReplayEmitter.EmitAsync(
                    args[1],
                    args[2],
                    args[3],
                    args[4]);

                Console.WriteLine("NUCLEAR STAGE 4Z-C standalone exact Xenon Material corpus replay");
                Console.WriteLine($"materials={result.Materials:N0}; roots={result.Roots:N0}; scripts={result.ScriptStrings:N0}; zoneBytes={result.ZoneBytes:N0}");
                Console.WriteLine($"TEMP=0x{result.TempBytes:X}; VIRTUAL=0x{result.VirtualBytes:X}");
                Console.WriteLine($"exactIdentity={result.ExactIdentitySequence}; oatZero={result.OatZeroWarningsZeroErrors}; graphCorpusExact={result.GraphCorpusRedumpExact}; wrapper={result.WrapperRoundTripVerified}");
                Console.WriteLine($"replayPass={result.ReplayPass}");
                Console.WriteLine($"candidate: {result.OutputFastFile}");
                Console.WriteLine($"report:    {result.TextReportFile}");
                Console.WriteLine($"manifest:  {result.JsonReportFile}");
                foreach (var blocker in result.Blockers.Take(32))
                    Console.WriteLine("BLOCKER: " + blocker);
                foreach (var diagnostic in result.Diagnostics.Take(32))
                    Console.WriteLine("DIAGNOSTIC: " + diagnostic);
                return result.ReplayPass ? 0 : 4;
            }
            case "xenon-image-replay" when args.Length >= 4:
            {
                var unlinker = args.Length >= 5 && !string.IsNullOrWhiteSpace(args[4]) ? args[4] : null;
                var expectedImages = args.Length >= 6 && int.TryParse(args[5], out var ei) ? ei : 378;
                var result = await T4XenonImageGraphReplayEmitter.EmitAsync(
                    args[1],
                    args[2],
                    args[3],
                    unlinker,
                    expectedImages);

                Console.WriteLine("NUCLEAR STAGE 4O C# Xenon image serializer replay");
                Console.WriteLine($"images={result.Images:N0}; cardMemory={result.CardMemoryBytes:N0}; outputZoneBytes={result.ZoneBytes:N0}");
                Console.WriteLine($"TEMP={result.TempBytes:N0}; VIRTUAL={result.VirtualBytes:N0}; LARGE_RUNTIME={result.LargeRuntimeBytes:N0}");
                Console.WriteLine($"referenceBlocks=[{string.Join(", ", result.ReferenceBlockSizes)}]");
                Console.WriteLine($"outputBlocks=[{string.Join(", ", result.OutputBlockSizes)}]");
                Console.WriteLine($"exactBlockReplay={result.ExactBlockReplay}; exactZoneSizeReplay={result.ExactZoneSizeReplay}; exactDecompressedSizeReplay={result.ExactDecompressedSizeReplay}");
                Console.WriteLine($"oatListedRoots={result.OatListedRoots:N0}; exactIdentitySequence={result.ExactIdentitySequence}; wrapperRoundTrip={result.WrapperRoundTripVerified}");
                Console.WriteLine($"replayPass={result.ReplayPass}");
                Console.WriteLine($"candidate: {result.OutputFastFile}");
                Console.WriteLine($"report:    {result.TextReportFile}");
                Console.WriteLine($"manifest:  {result.JsonReportFile}");
                foreach (var blocker in result.Blockers.Take(32))
                    Console.WriteLine("BLOCKER: " + blocker);
                return result.ReplayPass ? 0 : 4;
            }
            case "xenon-graph-emit" when args.Length >= 6:
            {
                // Windows PowerShell 5.1 drops empty-string native-process args.
                // Accept an explicit RawFile-only option after the 10 required
                // positional args; avoid phantom image/model/material paths.
                var rawFileOnlyMode = args.Length >= 11 &&
                    args[10].Equals("--rawfile-graft", StringComparison.Ordinal);
                // A single named mode avoids Windows PowerShell 5.1 stripping
                // absent optional positional arguments and shifting the
                // image-graph and RawFile paths into incorrect slots.
                // This command is restricted to the currently proven Aztec
                // two-image diagnostic graft. It cannot authorize hardware.
                var aztecImageRawMode = args.Length >= 11 &&
                    args[10].Equals("--aztec-pc-iwd-image-rawfile-graft", StringComparison.Ordinal);
                var aztecOneModelMode = args.Length >= 11 &&
                    args[10].Equals("--aztec-pc-one-xmodel-image-rawfile-graft", StringComparison.Ordinal);
                var aztecTwoModelMode = args.Length >= 11 &&
                    args[10].Equals("--aztec-pc-two-xmodel-image-rawfile-graft", StringComparison.Ordinal);
                var aztecTwoModelMaterialMode = args.Length >= 11 &&
                    args[10].Equals("--aztec-pc-two-model-two-material-image-rawfile-graft", StringComparison.Ordinal);
                var aztecTwoModelMaterialMapEntsMode = args.Length >= 11 &&
                    args[10].Equals("--aztec-pc-two-model-two-material-mapents-image-rawfile-graft", StringComparison.Ordinal);
                var aztecSixFamilyComWorldMode = args.Length >= 11 &&
                    args[10].Equals("--aztec-pc-six-family-comworld-scratch-graft", StringComparison.Ordinal);
                var aztecTwoModelWithMaterials = aztecTwoModelMaterialMode || aztecTwoModelMaterialMapEntsMode || aztecSixFamilyComWorldMode;
                var aztecAnyTwoModels = aztecTwoModelMode || aztecTwoModelWithMaterials;
                var aztecAnyMixed = aztecImageRawMode || aztecOneModelMode || aztecAnyTwoModels;
                if (rawFileOnlyMode && (args.Length != 12 || string.IsNullOrWhiteSpace(args[11])))
                    throw new ArgumentException(
                        "RawFile-only syntax: xenon-graph-emit <graph> <native> <identity> <binding> <output> <map> <oat> <techsets> <pixelshaders> --rawfile-graft <pc-owned-manifest.json>");
                if (aztecImageRawMode &&
                    (args.Length != 14 ||
                     string.IsNullOrWhiteSpace(args[11]) ||
                     !int.TryParse(args[12], out var aztecImageCount) || aztecImageCount != 2 ||
                     string.IsNullOrWhiteSpace(args[13]) ||
                     !args[6].Equals("nazi_zombie_aztec", StringComparison.OrdinalIgnoreCase) ||
                     !int.TryParse(args[8], out var aztecTechsets) || aztecTechsets != 70 ||
                     !int.TryParse(args[9], out var aztecPixelShaders) || aztecPixelShaders != 436))
                    throw new ArgumentException(
                        "Aztec PC image+RawFile diagnostic syntax: xenon-graph-emit <graph> <native> <identity> <binding> <output> nazi_zombie_aztec <oat> 70 436 --aztec-pc-iwd-image-rawfile-graft <PC-IWD-derived-Xenon-image-graph> 2 <PC-rawfile-graft.json>");
                if (aztecOneModelMode &&
                    (args.Length != 16 || string.IsNullOrWhiteSpace(args[11]) ||
                     args[12] != "2" || string.IsNullOrWhiteSpace(args[13]) ||
                     string.IsNullOrWhiteSpace(args[14]) || string.IsNullOrWhiteSpace(args[15]) ||
                     !args[6].Equals("nazi_zombie_aztec", StringComparison.OrdinalIgnoreCase) ||
                     args[8] != "70" || args[9] != "436"))
                    throw new ArgumentException(
                        "Aztec one-model scratch syntax: xenon-graph-emit <renderer> <native> <identity> <binding> <scratch-output> nazi_zombie_aztec <oat> 70 436 --aztec-pc-one-xmodel-image-rawfile-graft <2-PC-image-graph> 2 <114-PC-rawfile-manifest> <one-PC-xmodel-graph> <single-model-binding>");
                if (aztecTwoModelMode &&
                    (args.Length != 16 || string.IsNullOrWhiteSpace(args[11]) ||
                     args[12] != "2" || string.IsNullOrWhiteSpace(args[13]) ||
                     string.IsNullOrWhiteSpace(args[14]) || string.IsNullOrWhiteSpace(args[15]) ||
                     !args[6].Equals("nazi_zombie_aztec", StringComparison.OrdinalIgnoreCase) ||
                     args[8] != "70" || args[9] != "436"))
                    throw new ArgumentException(
                        "Aztec two-model SCRATCH syntax: xenon-graph-emit <renderer> <native> <identity> <binding> <scratch-output> nazi_zombie_aztec <oat> 70 436 --aztec-pc-two-xmodel-image-rawfile-graft <2-PC-image-graph> 2 <114-PC-rawfile-manifest> <two-PC-xmodel-graph> <exact-two-model-binding>");
                if (aztecTwoModelMaterialMode &&
                    (args.Length != 18 || string.IsNullOrWhiteSpace(args[11]) ||
                     args[12] != "2" || string.IsNullOrWhiteSpace(args[13]) ||
                     string.IsNullOrWhiteSpace(args[14]) || string.IsNullOrWhiteSpace(args[15]) ||
                     string.IsNullOrWhiteSpace(args[16]) || string.IsNullOrWhiteSpace(args[17]) ||
                     !args[6].Equals("nazi_zombie_aztec", StringComparison.OrdinalIgnoreCase) ||
                     args[8] != "70" || args[9] != "436"))
                    throw new ArgumentException(
                        "Aztec two-XModel/two-Material SCRATCH syntax: xenon-graph-emit <renderer> <native> <identity> <binding> <scratch-output> nazi_zombie_aztec <oat> 70 436 --aztec-pc-two-model-two-material-image-rawfile-graft <2-PC-image-graph> 2 <114-PC-rawfile-manifest> <2-PC-xmodel-graphs> <xmodel-binding> <2-PC-material-graphs> <material-binding>");
                if (aztecTwoModelMaterialMapEntsMode &&
                    (args.Length != 19 || string.IsNullOrWhiteSpace(args[11]) ||
                     args[12] != "2" || string.IsNullOrWhiteSpace(args[13]) ||
                     string.IsNullOrWhiteSpace(args[14]) || string.IsNullOrWhiteSpace(args[15]) ||
                     string.IsNullOrWhiteSpace(args[16]) || string.IsNullOrWhiteSpace(args[17]) ||
                     string.IsNullOrWhiteSpace(args[18]) ||
                     !args[6].Equals("nazi_zombie_aztec", StringComparison.OrdinalIgnoreCase) ||
                     args[8] != "70" || args[9] != "436"))
                    throw new ArgumentException(
                        "Aztec original-PC MapEnts 5-payload SCRATCH mode requires 2 images, 114 RawFiles, 2 models, 2 materials, plus the passing standalone MapEnts canary manifest (19 arguments).");
                if (aztecSixFamilyComWorldMode &&
                    (args.Length != 20 || string.IsNullOrWhiteSpace(args[11]) ||
                     args[12] != "2" || string.IsNullOrWhiteSpace(args[13]) ||
                     string.IsNullOrWhiteSpace(args[14]) || string.IsNullOrWhiteSpace(args[15]) ||
                     string.IsNullOrWhiteSpace(args[16]) || string.IsNullOrWhiteSpace(args[17]) ||
                     string.IsNullOrWhiteSpace(args[18]) || string.IsNullOrWhiteSpace(args[19]) ||
                     !args[6].Equals("nazi_zombie_aztec", StringComparison.OrdinalIgnoreCase) ||
                     args[8] != "70" || args[9] != "436"))
                    throw new ArgumentException(
                        "Aztec ComWorld scratch graft requires 2 images, 114 RawFiles, 2 models, 2 materials, original PC MapEnts and source-pinned ComWorld burnable cells proof (20 arguments).");
                var canonicalBaseName = args.Length >= 7 ? args[6] : "nazi_zombie_FRUmansion";
                var unlinker = args.Length >= 8 && !string.IsNullOrWhiteSpace(args[7]) ? args[7] : null;
                var expectedTechsets = args.Length >= 9 && int.TryParse(args[8], out var et) ? et : 87;
                var expectedPixelShaders = args.Length >= 10 && int.TryParse(args[9], out var ep) ? ep : 897;
                var imageGraphRoot = aztecAnyMixed ? args[11] :
                    !rawFileOnlyMode && args.Length >= 11 && !string.IsNullOrWhiteSpace(args[10]) ? args[10] : null;
                var expectedImages = aztecAnyMixed ? 2 :
                    args.Length >= 12 && int.TryParse(args[11], out var ei) ? ei : 0;
                var imageValidationOnly = aztecAnyMixed || args.Length < 13 ||
                    !bool.TryParse(args[12], out var ivo) || ivo;
                var imageGraftLimit = aztecAnyMixed ? 0 :
                    args.Length >= 14 && int.TryParse(args[13], out var igl) ? igl : 0;
                var xmodelGraphRoot = (aztecOneModelMode || aztecAnyTwoModels) ? args[14] : aztecImageRawMode ? null :
                    args.Length >= 15 && !string.IsNullOrWhiteSpace(args[14]) ? args[14] : null;
                var xmodelBindingManifest = (aztecOneModelMode || aztecAnyTwoModels) ? args[15] : aztecImageRawMode ? null :
                    args.Length >= 16 && !string.IsNullOrWhiteSpace(args[15]) ? args[15] : null;
                var materialGraphRoot = aztecTwoModelWithMaterials ? args[16] : aztecAnyMixed ? null :
                    args.Length >= 17 && !string.IsNullOrWhiteSpace(args[16]) ? args[16] : null;
                var materialBindingManifest = aztecTwoModelWithMaterials ? args[17] : aztecAnyMixed ? null :
                    args.Length >= 18 && !string.IsNullOrWhiteSpace(args[17]) ? args[17] : null;
                var rawFileGraftManifest = aztecAnyMixed ? args[13] :
                    rawFileOnlyMode ? args[11] :
                    args.Length >= 19 && !string.IsNullOrWhiteSpace(args[18]) ? args[18] : null;
                var mapEntsCanaryManifest = (aztecTwoModelMaterialMapEntsMode || aztecSixFamilyComWorldMode) ? args[18] : null;
                var comWorldProofManifest = aztecSixFamilyComWorldMode ? args[19] : null;

                var result = await T4XenonRendererGraphEmitter.EmitAsync(
                    args[1], args[2], args[3], args[4], args[5],
                    canonicalBaseName, unlinker, expectedTechsets, expectedPixelShaders,
                    CancellationToken.None,
                    imageGraphRoot,
                    expectedImages,
                    imageValidationOnly,
                    imageGraftLimit,
                    xmodelGraphRoot,
                    xmodelBindingManifest,
                    materialGraphRoot,
                    materialBindingManifest,
                    rawFileGraftManifest,
                    mapEntsCanaryManifest,
                    comWorldProofManifest);

                Console.WriteLine("NUCLEAR STAGE 4E/4Y real Xenon renderer + image + XModel byte emission");
                Console.WriteLine($"roots={result.NativeRoots:N0}; rendererTechsets={result.RendererTechniqueSets:N0}; rendererPixelShaders={result.RendererPixelShaders:N0}; realImages={result.RealImageRoots:N0}; realRawFiles={result.RealRawFileRoots:N0}; realMapEnts={result.RealMapEntsRoots:N0}; realComWorlds={result.RealComWorldRoots:N0}; realMaterials={result.RealMaterialRoots:N0}; materialRefs={result.MaterialReferenceRoots:N0}; realXModels={result.RealXModelRoots:N0}; xmodelRefs={result.XModelReferenceRoots:N0}");
                Console.WriteLine($"zoneBytes={result.ZoneBytes:N0}; mainZoneBytes={result.MainZoneBytes:N0}; LARGE_RUNTIME={result.LargeRuntimeBytes:N0}; LARGE={result.LargeBytes:N0}");
                Console.WriteLine($"TEMP=0x{result.TempBytes:X}; VIRTUAL=0x{result.VirtualBytes:X}; PHYSICAL=0x{result.PhysicalBytes:X}");
                Console.WriteLine($"layoutReplayExact={result.LayoutReplayExact}; oatListedRoots={result.OatListedRoots:N0}; oatLoadedAssets={result.OatLoadedAssets:N0}; oatExternalRefs={result.OatExternalReferenceAssets:N0}; exactIdentitySequence={result.ExactIdentitySequence}");
                Console.WriteLine($"oatZeroWarningsZeroErrors={result.OatZeroWarningsZeroErrors}; validationOnly={result.ValidationOnly}; validationPass={result.ValidationPass}; readyForHardware={result.ReadyForHardware}");
                Console.WriteLine($"candidate: {result.OutputFastFile}");
                Console.WriteLine($"report:    {result.TextReportFile}");
                Console.WriteLine($"manifest:  {result.JsonReportFile}");
                if (!string.IsNullOrWhiteSpace(result.OatGateError))
                    Console.WriteLine("OAT GATE: " + result.OatGateError);
                return result.ValidationPass ? 0 : 4;
            }
            case "xenon-graph-layout" when args.Length >= 6:
            {
                var canonicalBaseName = args.Length >= 7 ? args[6] : "nazi_zombie_FRUmansion";
                var expectedTechsets = args.Length >= 8 && int.TryParse(args[7], out var et) ? et : 87;
                var expectedPixelShaders = args.Length >= 9 && int.TryParse(args[8], out var ep) ? ep : 897;
                var result = await T4XenonGraphBlockLayoutPlanner.BuildAsync(
                    args[1], args[2], args[3], args[4], args[5],
                    canonicalBaseName, expectedTechsets, expectedPixelShaders);
                Console.WriteLine("NUCLEAR STAGE 4D OAT-style Xenon block layout");
                Console.WriteLine($"roots={result.NativeRoots:N0}; scripts={result.ScriptStrings:N0}; insertSlots={result.InsertSlots:N0}");
                Console.WriteLine($"rendererTechsets={result.RendererTechniqueSets:N0}; rendererPixelShaders={result.RendererPixelShaders:N0}; forwardPixelRefs={result.ForwardPixelShaderReferences:N0}");
                Console.WriteLine($"TEMP=0x{result.TempBytes:X}; VIRTUAL=0x{result.VirtualBytes:X}; PHYSICAL=0x{result.PhysicalBytes:X}");
                Console.WriteLine($"serializedBlockBytes={result.SerializedBlockBytes:N0}; projectedZoneBytes={result.ProjectedUncompressedZoneBytes:N0}");
                Console.WriteLine($"reuse techniques={result.ReusedTechniques:N0}; vertexDecls={result.ReusedVertexDecls:N0}; vertexShaders={result.ReusedVertexShaders:N0}; literals={result.ReusedLiteralConstants:N0}");
                Console.WriteLine($"layoutReady={result.LayoutReady}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"manifest: {result.JsonManifestFile}");
                foreach (var blocker in result.Blockers.Take(32))
                    Console.WriteLine("BLOCKER: " + blocker);
                foreach (var note in result.Notes)
                    Console.WriteLine(note);
                return result.LayoutReady ? 0 : 4;
            }
            case "xenon-graph-bind" when args.Length >= 4:
            {
                var expectedTechsets = args.Length >= 5 && int.TryParse(args[4], out var et) ? et : 87;
                var expectedPixelShaders = args.Length >= 6 && int.TryParse(args[5], out var ep) ? ep : 897;
                var result = await T4XenonGraphRootBindingPlanner.BuildAsync(
                    args[1],
                    args[2],
                    args[3],
                    expectedTechsets,
                    expectedPixelShaders);
                Console.WriteLine("NUCLEAR STAGE 4C Xenon graph -> native root binding");
                Console.WriteLine($"TechniqueSets={result.GraphTechniqueSets:N0}; matched={result.MatchedTechniqueSets:N0}; nativeTechniqueSets={result.NativeTechniqueSets:N0}; nativeUnbound={result.UnboundNativeTechniqueSets:N0}");
                Console.WriteLine($"PixelShaders={result.GraphPixelShaders:N0}; matched={result.MatchedPixelShaders:N0}; nativePixelShaders={result.NativePixelShaders:N0}");
                Console.WriteLine($"ambiguous={result.AmbiguousBindings:N0}; missing={result.MissingBindings:N0}; exactRootBindingReady={result.ExactRootBindingReady}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"manifest: {result.JsonManifestFile}");
                foreach (var note in result.Notes)
                    Console.WriteLine(note);
                return result.ExactRootBindingReady ? 0 : 4;
            }
            case "xenon-graph-plan" when args.Length >= 3:
            {
                var expectedTechsets = args.Length >= 4 && int.TryParse(args[3], out var et) ? et : 87;
                var expectedPixelShaders = args.Length >= 5 && int.TryParse(args[4], out var ep) ? ep : 897;
                var result = T4XenonGraphSerializationPlanner.Build(
                    args[1],
                    args[2],
                    expectedTechsets,
                    expectedPixelShaders);
                Console.WriteLine("NUCLEAR STAGE 4B Xenon graph serialization plan");
                Console.WriteLine($"TechniqueSets={result.TechniqueSets:N0}; refs={result.TechniqueReferences:N0}; uniqueTechniques={result.UniqueTechniques:N0}");
                Console.WriteLine($"passes={result.Passes:N0}; multiPass={result.MultiPassTechniques:N0}; vertexDecls={result.UniqueVertexDecls:N0}");
                Console.WriteLine($"vertexShaders={result.UniqueVertexShaders:N0}; pixelShaders={result.UniquePixelShaders:N0}");
                Console.WriteLine($"shaderArgs={result.ShaderArguments:N0}; literalConsts={result.LiteralConstants:N0}/{result.UniqueLiteralConstants:N0} unique");
                Console.WriteLine($"shaderBlobBytes={result.ShaderBlobBytes:N0}; estimatedTempFixedBytes={result.EstimatedTempFixedBytes:N0}");
                Console.WriteLine($"readyForSerialization={result.ReadyForSerialization}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"manifest: {result.JsonReportFile}");
                foreach (var note in result.Notes)
                    Console.WriteLine(note);
                return result.ReadyForSerialization ? 0 : 4;
            }
            case "xenon-graph-import" when args.Length >= 2:
            {
                var expectedTechsets = args.Length >= 3 && int.TryParse(args[2], out var et) ? et : 87;
                var expectedPixelShaders = args.Length >= 4 && int.TryParse(args[3], out var ep) ? ep : 897;
                var result = T4XenonTechniqueGraphImporter.Load(args[1], expectedTechsets, expectedPixelShaders);
                Console.WriteLine("NUCLEAR STAGE 4A typed Xenon graph import");
                Console.WriteLine($"TechniqueSets={result.TechniqueSets:N0}/{expectedTechsets:N0}; verified={result.Verified}");
                Console.WriteLine($"nonNullSlots={result.NonNullTechniqueSlots:N0}; passes={result.Passes:N0}");
                Console.WriteLine($"vertexShaders={result.DistinctVertexShaders:N0}; pixelShaders={result.DistinctPixelShaders:N0}/{expectedPixelShaders:N0}");
                Console.WriteLine($"shaderBlobs={result.ShaderBlobFilesReferenced:N0}; shaderBytes={result.ShaderBlobBytes:N0}");
                foreach (var warning in result.Warnings)
                    Console.WriteLine("WARNING: " + warning);
                return result.Verified ? 0 : 4;
            }
            case "xenon-techset-bodies" when args.Length >= 3:
            {
                var explicitUnlinker = args.Length >= 4 ? args[3] : null;
                var result = await T4XenonTechniqueSetBodyCatalog.BuildAsync(
                    args[1],
                    args[2],
                    explicitUnlinker);
                Console.WriteLine("NUCLEAR STAGE 3A Xenon TechniqueSet body catalog");
                Console.WriteLine($"TechniqueSets={result.RecoveredTechniqueSets:N0}/{result.ExpectedTechniqueSets:N0}; verified={result.Verified}");
                Console.WriteLine($"rootOwnership(inline/alias/distinctBodies)={result.InlineOwnedTechniqueRoots:N0}/{result.AliasTechniqueRoots:N0}/{result.DistinctTechniqueBodies:N0}");
                Console.WriteLine($"slots(nonNull/inline/packed)={result.TotalNonNullTechniqueSlots:N0}/{result.TotalInlineTechniqueSlots:N0}/{result.TotalPackedTechniqueSlots:N0}");
                Console.WriteLine($"report:   {result.TextReportFile}");
                Console.WriteLine($"manifest: {result.JsonReportFile}");
                return result.Verified ? 0 : 4;
            }
            case "xasset-root-oracle" when args.Length >= 3:
            {
                var result = await T4XAssetRootOracle.CompareAsync(args[1], args[2]);
                Console.WriteLine(result.Summary);
                return 0;
            }
            case "xanim-console-proof" when args.Length >= 3:
            {
                var result = await T4XboxXAnimTransformProof.BuildAsync(args[1], args[2]);
                Console.WriteLine(result.Summary);
                Console.WriteLine($"Proof container: {result.OutputFile}");
                return result.Verified && result.ConvertedXAnim == result.DeclaredXAnim ? 0 : 4;
            }
            case "xanim-split-bones" when args.Length >= 3:
            {
                var result = await T4XAnimBoneSplitOracle.AnalyzeAsync(args[1], args[2]);
                Console.WriteLine(result.Summary);
                return result.NameSetMismatches == 0 ? 0 : 4;
            }
            case "xanim-split-features" when args.Length >= 3:
            {
                var result = await T4XAnimSplitFeatureOracle.AnalyzeAsync(args[1], args[2]);
                Console.WriteLine(result.Summary);
                return result.BoneObservations > 0 ? 0 : 4;
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


static async Task<int> RunOatRootBisectAsync(string candidateFastFile, string unlinkerExe, string outputDir)
{
    candidateFastFile = Path.GetFullPath(candidateFastFile);
    unlinkerExe = Path.GetFullPath(unlinkerExe);
    outputDir = Path.GetFullPath(outputDir);

    if (!File.Exists(candidateFastFile))
        throw new FileNotFoundException("Candidate fastfile not found.", candidateFastFile);
    if (!File.Exists(unlinkerExe))
        throw new FileNotFoundException("OAT Unlinker not found.", unlinkerExe);

    Directory.CreateDirectory(outputDir);

    var document = await T4ZoneParser.ParseFastFileAsync(candidateFastFile);
    if (document.Platform != WaWPlatform.Xbox360 || document.Endianness != Endian.Big)
        throw new InvalidDataException(
            $"OAT root bisection requires an Xbox360 big-endian candidate; parsed {document.Platform}/{document.Endianness}.");

    var rawZonePath = Path.Combine(outputDir, "oat_root_bisect_source.zone");
    TryDeleteCli(rawZonePath);
    var decompressed = await FastFileDecompressor.DecompressAutoAsync(candidateFastFile, rawZonePath);
    if (decompressed.Platform != WaWPlatform.Xbox360)
        throw new InvalidDataException($"Expected Xbox360 wrapper, parsed {decompressed.Platform}.");

    var sourceZone = await File.ReadAllBytesAsync(rawZonePath);
    var assetTableOffset = checked((int)document.AssetTableOffset);
    var tableBytes = checked(document.AssetCount * 8);
    if (assetTableOffset < 0 || assetTableOffset > sourceZone.Length - tableBytes)
        throw new InvalidDataException(
            $"XAsset table 0x{document.AssetTableOffset:X}+0x{tableBytes:X} is outside the {sourceZone.Length:N0}-byte raw zone.");

    Console.WriteLine($"OAT root bisection: assets={document.AssetCount:N0}; table=0x{document.AssetTableOffset:X}; zone={sourceZone.Length:N0}B");

    async Task<(bool Pass, int ExitCode, string Output)> ProbeAsync(int activeRoots, string ffPath)
    {
        if (activeRoots < 0 || activeRoots > document.AssetCount)
            throw new ArgumentOutOfRangeException(nameof(activeRoots));

        var zone = sourceZone.ToArray();
        for (var i = activeRoots; i < document.AssetCount; i++)
        {
            var headerOffset = checked(assetTableOffset + i * 8 + 4);
            BinaryPrimitives.WriteUInt32BigEndian(zone.AsSpan(headerOffset, 4), 0u);
        }

        TryDeleteCli(ffPath);
        var write = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
            ffPath,
            zone,
            preferNativeLevel6: true);
        if (!write.RoundTripVerified)
            throw new InvalidDataException($"Probe wrapper round-trip failed for activeRoots={activeRoots:N0}.");

        var psi = new ProcessStartInfo
        {
            FileName = unlinkerExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--list");
        psi.ArgumentList.Add("--no-color");
        psi.ArgumentList.Add(ffPath);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start OAT Unlinker.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var combined = string.IsNullOrEmpty(stderr)
            ? stdout
            : string.IsNullOrEmpty(stdout) ? stderr : stdout + Environment.NewLine + stderr;

        return (process.ExitCode == 0, process.ExitCode, combined);
    }

    static string Tail(string value, int lines)
    {
        return string.Join(
            Environment.NewLine,
            value.Replace("\r\n", "\n", StringComparison.Ordinal)
                 .Split('\n')
                 .TakeLast(lines));
    }

    var probePath = Path.Combine(outputDir, "oat_root_bisect_probe.ff");

    var zero = await ProbeAsync(0, probePath);
    Console.WriteLine($"control active=0       exit={zero.ExitCode} {(zero.Pass ? "PASS" : "FAIL")}");
    if (!zero.Pass)
    {
        Console.WriteLine("ZERO-ROOT CONTROL FAILED; prefix-null bisection is not valid for this zone.");
        Console.WriteLine(Tail(zero.Output, 12));
        TryDeleteCli(probePath);
        return 5;
    }

    var full = await ProbeAsync(document.AssetCount, probePath);
    Console.WriteLine($"control active={document.AssetCount,-7:N0} exit={full.ExitCode} {(full.Pass ? "PASS" : "FAIL")}");
    if (full.Pass)
    {
        Console.WriteLine("Full candidate now passes OAT; there is no failing root to bisect.");
        TryDeleteCli(probePath);
        return 0;
    }

    var lo = 0;
    var hi = document.AssetCount;

    while (lo + 1 < hi)
    {
        var mid = lo + (hi - lo) / 2;
        var result = await ProbeAsync(mid, probePath);
        Console.WriteLine($"probe active={mid,-7:N0} exit={result.ExitCode} {(result.Pass ? "PASS" : "FAIL")}");

        if (result.Pass)
            lo = mid;
        else
            hi = mid;
    }

    TryDeleteCli(probePath);

    var failingIndex = hi - 1;
    var failing = document.Assets[failingIndex];

    var lastPassPath = Path.Combine(outputDir, $"oat_root_bisect_last_pass_{lo:D4}.ff");
    var firstFailPath = Path.Combine(outputDir, $"oat_root_bisect_first_fail_{hi:D4}.ff");
    var lastPass = await ProbeAsync(lo, lastPassPath);
    var firstFail = await ProbeAsync(hi, firstFailPath);

    Console.WriteLine();
    Console.WriteLine("========== OAT ROOT FAILURE FRONTIER ==========");
    Console.WriteLine($"last PASS active roots : {lo:N0}");
    Console.WriteLine($"first FAIL active roots: {hi:N0}");
    Console.WriteLine($"failing XAsset index   : {failingIndex:N0} (0-based)");
    Console.WriteLine($"document asset index   : {failing.Index:N0}");
    Console.WriteLine($"type                    : {failing.TypeName} (0x{failing.TypeId:X})");
    Console.WriteLine($"raw top-level header    : 0x{failing.RawHeader:X8}");
    Console.WriteLine($"last-pass FF            : {lastPassPath}");
    Console.WriteLine($"first-fail FF           : {firstFailPath}");
    Console.WriteLine();
    Console.WriteLine("First-fail OAT tail:");
    Console.WriteLine(Tail(firstFail.Output, 12));

    Console.WriteLine();
    Console.WriteLine("Nearby XAsset entries:");
    for (var i = Math.Max(0, failingIndex - 3); i <= Math.Min(document.AssetCount - 1, failingIndex + 3); i++)
    {
        var asset = document.Assets[i];
        Console.WriteLine(
            $"  {(i == failingIndex ? ">" : " ")} #{i,5} doc#{asset.Index,5} type={asset.TypeName,-20} id=0x{asset.TypeId:X2} header=0x{asset.RawHeader:X8}");
    }

    return !lastPass.Pass || firstFail.Pass ? 6 : 0;
}

static void TryDeleteCli(string path)
{
    try
    {
        if (File.Exists(path))
            File.Delete(path);
    }
    catch
    {
    }
}

static void PrintHelp()
{
    Console.WriteLine("WaW360Converter Linker V2 / legacy proof CLI");
    Console.WriteLine("  inspect <file.ff>                       Probe PC/Xbox wrapper + XFile header");
    Console.WriteLine("  decompress <file.ff> <out.zone>        Decompress to raw zone");
    Console.WriteLine("  aztec-pc-gameworldsp-hunt <PC map.ff> <report.json>  Find source 44-byte path-header candidates");
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
    Console.WriteLine("  stage4-native-xanim <root> <map> <output>    Emit real 0x5C/12-bucket Xbox XAnim proof payloads for Stage 5");
    Console.WriteLine("  stage4-alpha9g-xmodel <root> <map> <output>  Prove XModel bodies; unresolved assets emit alpha9g rejected-forensics reports");
    Console.WriteLine("  stage4-alpha6-weapon <root> <map> <output>   Prove all WeaponDef fixed bodies in Xbox big-endian form");
    Console.WriteLine("  stage4-alpha7b-weapon <root> <map> <output>   Prove WeaponDef owned payloads + exact read-back");
    Console.WriteLine("  stage4-alpha9b-fx <root> <map> <output>         Run strict zero-total FX reference recovery proof");
    Console.WriteLine("  stage4-alpha9a-mixed <root> <map> <output>      Run mixed XAnim/XModel/Weapon/FX XAsset-order probe");
    Console.WriteLine("  link-load <pc_load.ff> [out.json]        Deterministic XAsset-stream graph + dependency-first Xbox plan");
    Console.WriteLine("  link-load-emit <root> <map> <output>     Emit/reopen Linker V1 alpha-2 Xbox load.ff smoke candidate");
    Console.WriteLine("  linker-v2-typed-closure <root> <map> <output>     Recompute IWD/script/menu typed closure from current sources");
    Console.WriteLine("  linker-v2-iwi-image-corpus <root> <map> <output> <native-identity.json>  Match IWD .iwi corpus to native Xenon image identities and rank codec mass");
    Console.WriteLine("  xmodel-partclassification-per-model <pc.ff> <pc-dump-root> <pc-oat-list.log> <output-dir> <manifest.json> [xanim-proof] [weapon-proof]  Recover model-specific PC hit-location CSVs using OAT canonical GLTF bone order");
    Console.WriteLine("  xmodel-partclassification <pc.ff> <partclassification.csv> <manifest.json> [xanim-proof] [weapon-proof]  Recover original PC XModel bone hit-location mapping for OAT Xenon import");
    Console.WriteLine("  xenon-native-xmodel-layout <native.ff> <native-identity.json> <output>  Prove native Xbox XModel 0xD4 fixed-body/name boundary against 0xE4 control");
    Console.WriteLine("  linker-v2-full-content-readiness <root> <map> <output> <native.ff> <graph-root> <native-identity.json> <binding.json> <min-ratio> [max-ratio] [4s-manifest] [4y-manifest] [4zd-manifest]  Gate full-content emission on real payload mass and zero critical shells, crediting exact production images, XModels and Materials when supplied");
    Console.WriteLine("  linker-v2-native-sequence <root> <map> <output> <native.ff>  Rebuild DB-table diagnostic in native root/string order without donor payload bytes");
    Console.WriteLine("  linker-v2-main-structural <root> <map> <output> [native.ff]  Emit current self-contained main diagnostic; optional native.ff also emits same-payload native-block-capacity A/B");
    Console.WriteLine("  oat-root-bisect <candidate.ff> <Unlinker.exe> <output-dir>  Binary-search first top-level root that makes OAT fail");
    Console.WriteLine("  linker-v2-source <root> <map> <output>    Build canonical multi-source graph/package plan");
    Console.WriteLine("  inspect-xbox-reference <folder> <out>    Fingerprint a known-working Xbox map package (validation only)");
    Console.WriteLine("  native-oat-identity <native.ff> <output-dir> [Unlinker.exe]  Extract exact native Xbox (index,type,name) sequence through pinned OAT Xenon");
    Console.WriteLine("  pc-native-root-oracle <pc.ff> <native.ff>       Compare original PC root/type order and ScriptStrings to native Xbox");
    Console.WriteLine("  pc-native-semantic-oracle <pc.ff> <native.ff> <output-dir> [Unlinker.exe]  Compare OAT-loaded PC semantic identities to native Xbox and emit ScriptString remap");
    Console.WriteLine("  material-techset-bridge <pc.ff> <xbox.ff> <out-dir> [Unlinker.exe]  Derive PC->Xenon TechniqueSet mappings through shared material identity");
    Console.WriteLine("  pc-oat-techset-identities <pc.ff> <out-dir> [Unlinker.exe]  Recover full-PC-map TechniqueSet names with OAT (identity only)");
    Console.WriteLine("  nuclear-techset-closure-plan <pc.ff> <xbox.ff> <out-dir> [Unlinker.exe]  Bind all PC TechniqueSet roots to concrete Xenon donors");
    Console.WriteLine("  xenon-techset-bodies <xbox.ff> <out-dir> [Unlinker.exe]  Recover all native TechniqueSet fixed bodies + 51-slot pointers");
    Console.WriteLine("  xenon-graph-import <dump-root> [expected-techsets] [expected-pixelshaders]  Import + validate OAT fixed-up Xenon graph JSON/blobs");
    Console.WriteLine("  xenon-graph-emit <dump-root> <native.ff> <native-identity.json> <binding.json> <out-dir> [canonical-base-name] [unlinker.exe] [expected-techsets] [expected-pixelshaders] [image-graph-root] [expected-images] [image-validation-only] [image-graft-limit]  Emit real Xenon renderer/image graph and run OAT hard gate");
    Console.WriteLine("  xenon-graph-layout <dump-root> <native.ff> <native-identity.json> <binding.json> <out-dir> [canonical-base-name] [expected-techsets] [expected-pixelshaders]  Dry-run OAT-style renderer block allocation");
    Console.WriteLine("  xenon-graph-bind <dump-root> <native.ff> <out-dir> [expected-techsets] [expected-pixelshaders]  Bind fixed-up Xenon graph identities to exact native XAsset root slots");
    Console.WriteLine("  xenon-graph-plan <dump-root> <out-dir> [expected-techsets] [expected-pixelshaders]  Build deterministic Xenon graph serialization/dedupe plan");
    Console.WriteLine("  xasset-root-oracle <candidate.ff> <native.ff>  Compare generated/native Xbox DB root graph, order, refs and simple names");
    Console.WriteLine("  xanim-console-proof <pc.ff> <out.bin>      Convert every inline PC XAnim to real 0x5C/12-bucket Xbox form and read it back");
    Console.WriteLine("  xanim-bone-compare <pc.ff> <xbox.ff> [name] [--verbose]  Compare PC/Xbox XAnim bone-count layouts; all-animation mode is summary-only");
    Console.WriteLine("  xanim-split-bones <pc.ff> <xbox.ff>       Resolve XAnim.names and test whether Xbox quaternion split side follows bone identity");
    Console.WriteLine("  xanim-split-features <pc.ff> <xbox.ff>    Correlate Xbox A/B split with PC translation type and quat frame count");
    Console.WriteLine("  roundtrip <in.ff> <out.ff>              Decompress/recompress and byte-verify raw zone");
    Console.WriteLine("  scan-maps [folder]                      Scan PC Maps (defaults to Desktop\\World at War\\PC Maps)");
}
