using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Independent original-PC type-level physical/alias census, plus the
/// existing typed SoundAliasList parser. Reads original PC FFs only.
/// This does NOT serialize any Xbox assets or produce playable audio.
/// </summary>
public static class T4AztecSourceBodyQuickBatch
{
    private static readonly HashSet<string> Roles =
        new(StringComparer.OrdinalIgnoreCase){"map.ff","map_patch.ff","mod.ff"};
    private static readonly HashSet<string> Focus =
        new(StringComparer.OrdinalIgnoreCase)
        {"sound","loadedsound","xanim","image","xmodel","material","fx",
         "weapon","rawfile","localize","stringtable","gfxworld"};

    public static async Task<string> RunAsync(
        string baselineFile,string sizeLedgerFile,string sourceInventoryFile,
        string outputDirectory,bool skipDeepSound=false,CancellationToken ct=default)
    {
        foreach(var f in new[]{baselineFile,sizeLedgerFile,sourceInventoryFile})
            if(!File.Exists(f))throw new FileNotFoundException("Required original PC evidence missing",f);
        using var baseDoc=JsonDocument.Parse(await File.ReadAllTextAsync(baselineFile,ct));
        using var sizeDoc=JsonDocument.Parse(await File.ReadAllTextAsync(sizeLedgerFile,ct));
        using var inventoryDoc=JsonDocument.Parse(await File.ReadAllTextAsync(sourceInventoryFile,ct));
        var bas=baseDoc.RootElement;
        var size=sizeDoc.RootElement;
        var inv=inventoryDoc.RootElement;
        static string S(JsonElement j,string n)=>j.GetProperty(n).GetString()??"";
        static int I(JsonElement j,string n)=>j.GetProperty(n).GetInt32();
        static long L(JsonElement j,string n)=>j.GetProperty(n).GetInt64();


        if(S(bas,"format")!="WaW360.SecondMapBaseline.v1" ||
           !S(bas,"mapName").Equals("nazi_zombie_aztec_V2.2",StringComparison.OrdinalIgnoreCase) ||
           S(size,"schema")!="WaW360.AztecNativeFfSizeAndSourceCoverage.v1" ||
           S(size,"status")!="AZTEC_NATIVE_FF_SIZE_AUDIT_PASS_NOT_HARDWARE_READY" ||
           I(size.GetProperty("rootCoverage"),"total")!=8509 ||
           I(size.GetProperty("rootCoverage"),"nameShellOrUnverifiedRoots")!=7817 ||
           S(inv,"schema")!="WaW360.AztecPcFastfileImageSourceCensus.v1" ||
           I(inv,"nativeImageRoots")!=666 ||
           inv.GetProperty("sourceFastfiles").GetArrayLength()!=3)
            throw new InvalidDataException("Original PC baseline, size or PC image source census schema changed.");

        var inventory=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var r in inv.GetProperty("sourceFastfiles").EnumerateArray())
        {
            var full=Path.GetFullPath(S(r,"SourceFastFile"));
            var digest=S(r,"Sha256");
            if(digest.Length!=64 || !inventory.TryAdd(full,digest))
                throw new InvalidDataException("Missing or duplicated original PC source SHA in image source census.");
        }
        var sourceFiles=bas.GetProperty("fastfiles").EnumerateArray()
            .Where(f=>Roles.Contains(S(f,"Role"))).ToArray();
        if(sourceFiles.Length!=3)
            throw new InvalidDataException("Exactly map.ff/map_patch.ff/mod.ff original PC sources required.");
        var seenRole=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byType=new List<object>();
        var files=new List<object>();
        int soundDeclared=0,soundPhysical=0,soundTyped=0,aliasEntries=0;
        int soundLoadedNodes=0,soundStreamedNodes=0,soundPrimedNodes=0;
        long pcLoadedAudioBytesObserved=0;
        int soundFailures=0;
        int soundOffsetAliasHeaders=0,soundNullHeaders=0;
        int pcPhysicalXanims=0;
        foreach(var f in sourceFiles)
        {
            ct.ThrowIfCancellationRequested();
            var role=S(f,"Role");
            var path=Path.GetFullPath(S(f,"Path"));
            if(!seenRole.Add(role) ||
               !File.Exists(path) ||
               new FileInfo(path).Length!=L(f,"Bytes") ||
               !inventory.TryGetValue(path,out var expectedSha))
                throw new InvalidDataException("Original PC map role, source path or SHA inventory changed: "+role);
            var cached=await FastFileAnalysisCache.GetZoneAsync(path,ct);
            if(cached.Platform!=WaWPlatform.PC ||
               cached.ZoneHeader.Endianness!=Endian.Little ||
               !cached.SourceSha256.Equals(expectedSha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original PC FF decompression/SHA/source endianness changed: "+role);
            var doc=cached.Document;
            var types=doc.Assets.GroupBy(x=>x.TypeName,StringComparer.OrdinalIgnoreCase).ToArray();
            foreach(var group in types)
            {
                var physical=group.Count(x=>x.ReferenceKind is
                    T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared);
                var aliases=group.Count(x=>x.ReferenceKind==T4AssetReferenceKind.OffsetOrAlias);
                var missing=group.Count(x=>x.ReferenceKind==T4AssetReferenceKind.Null);
                if(physical+aliases+missing!=group.Count())
                    throw new InvalidDataException("PC XAsset header counts do not reconcile: "+role+"/"+group.Key);
                if(group.Key.Equals("xanim",StringComparison.OrdinalIgnoreCase))pcPhysicalXanims+=physical;
                if(!Focus.Contains(group.Key))continue;
                byType.Add(new{
                    role,assetType=group.Key,sourceXAssetCount=group.Count(),
                    originalPcPhysicalInlineHeaders=physical,
                    originalPcOffsetOrAliasHeaders=aliases,
                    originalPcNullHeaders=missing,
                    headerClassesReconcile=true,
                    actualPerBodySourceBytesMeasured=false,
                    xboxBodySerializationAndRuntimeProven=false
                });
            }
            var snd=doc.Assets.Where(x=>x.TypeName.Equals("sound",StringComparison.OrdinalIgnoreCase)).ToArray();
            var phys=snd.Count(x=>x.ReferenceKind is
                T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared);
            soundDeclared+=snd.Length;soundPhysical+=phys;
            soundOffsetAliasHeaders+=snd.Count(x=>x.ReferenceKind==T4AssetReferenceKind.OffsetOrAlias);
            soundNullHeaders+=snd.Count(x=>x.ReferenceKind==T4AssetReferenceKind.Null);
            object? soundResult=null;
            if(!skipDeepSound && phys!=0)
            {
                try
                {
                    var scan=T4SoundAliasSmokeSerializer.Build(doc,cached.Bytes,ct);
                    soundTyped+=scan.ParsedPhysicalSoundAssets;
                    aliasEntries+=scan.AliasEntries;
                    soundLoadedNodes+=scan.LoadedSoundNodes;
                    soundStreamedNodes+=scan.StreamedSoundNodes;
                    soundPrimedNodes+=scan.PrimedSoundNodes;
                    pcLoadedAudioBytesObserved+=scan.LoadedAudioBytesSkipped;
                    if(!scan.Verified || scan.ReadBackErrors!=0 ||
                       scan.DeclaredPhysicalSoundAssets!=phys)
                        soundFailures++;
                    soundResult=new{
                        attempted=true,typedAliasListVerified=scan.Verified,
                        physicalDeclared=scan.DeclaredPhysicalSoundAssets,
                        physicalTyped=scan.ParsedPhysicalSoundAssets,
                        aliases=scan.AliasEntries,loadedNodes=scan.LoadedSoundNodes,
                        streamedNodes=scan.StreamedSoundNodes,primedNodes=scan.PrimedSoundNodes,
                        originalPcAudioBytesSkipped=scan.LoadedAudioBytesSkipped,
                        silentDiagnosticStubReadbackErrors=scan.ReadBackErrors,
                        warnings=scan.Warnings.Take(12).ToArray(),
                        genuineXboxAudioProduced=false,
                        realSoundFilePointersXboxValidated=false
                    };
                }
                catch(Exception ex) when(ex is not OperationCanceledException)
                {
                    soundFailures++;
                    soundResult=new{
                        attempted=true,typedAliasListVerified=false,
                        failure=ex.GetType().Name+": "+ex.Message,
                        genuineXboxAudioProduced=false
                    };
                }
            }
            else
            {
                soundResult=new{
                    attempted=false,
                    reason=skipDeepSound?"SKIPPED_BY_REQUEST":"NO_PHYSICAL_PC_SOUND_HEADERS",
                    genuineXboxAudioProduced=false
                };
            }
            files.Add(new{
                role,originalPcFastFile=path,sha256=cached.SourceSha256,
                compressedBytes=new FileInfo(path).Length,
                decompressedBytes=cached.Bytes.LongLength,
                sourceAssets=doc.AssetCount,
                sourceScriptStrings=doc.ScriptStringCount,
                physicalSoundXAssets=phys,
                soundAliasStructure=soundResult
            });
            Console.WriteLine("[AZTEC-SOURCE-BODIES] "+role+": PC assets="+doc.AssetCount+
                ", inline-sound="+phys+", real body typed evidence="+
                (skipDeepSound?"SKIPPED":"REPORTED")+", source SHA PASS");
        }
        if(seenRole.Count!=3)throw new InvalidDataException("Original PC source roles not complete.");
        outputDirectory=Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var report=new{
            schema="WaW360.AztecOriginalPcPhysicalBodiesQuickBatch.v1",
            status=(skipDeepSound?"AZTEC_PC_XASSET_POINTER_HEADER_CENSUS_PASS_SOUND_TYPED_NOT_RUN":
                soundFailures==0 && soundTyped==soundPhysical?
                "AZTEC_PC_XASSET_POINTER_AND_SOUND_ALIAS_STRUCTURE_PASS_NO_XBOX_AUDIO":
                "AZTEC_PC_XASSET_POINTER_PASS_SOUND_TYPED_PROOF_INCOMPLETE"),
            originalPcSourceFastfiles=3,
            originalPcFastfilesSha256Verified=3,
            sourceZoneAssetHeaderClassificationReconciled=true,
            sourcePhysicalXanimHeaders=pcPhysicalXanims,
            originalPcSoundRootsDeclared=soundDeclared,
            originalPcSoundPhysicalInlineHeaders=soundPhysical,
            originalPcSoundAliasPointerHeaders=soundOffsetAliasHeaders,
            originalPcSoundNullHeaders=soundNullHeaders,
            typedPcSoundAliasListsVerified=soundTyped,
            sourceSoundTypedFailedRuns=soundFailures,
            typedPcSoundAliasEntries=aliasEntries,
            typedPcLoadedSoundNodes=soundLoadedNodes,
            typedPcStreamedSoundNodes=soundStreamedNodes,
            typedPcPrimedSoundNodes=soundPrimedNodes,
            typedPcLoadedAudioPayloadBytesSkipped=pcLoadedAudioBytesObserved,
            emittedToCurrentXboxAztecSoundBodies=0,
            currentXboxAztecImageSourceBodies=2,
            generatedWorldReflectionLightmapBodiesWithProvenPixels=0,
            originalPcSoundBytesConvertedToXboxXma=0,
            pcPhysicalBodyHeaderIsNotTypedSourceOwnershipProof=true,
            originalPcSourceHeaderIsNotEquivalentToNativeConvertedBody=true,
            XboxSoundResidencyProven=false,
            originalPcXAnimActualXboxBodyProofByThisRun=false,
            originalPcFxActualXboxBodyProofByThisRun=false,
            originalPcWeaponActualXboxBodyProofByThisRun=false,
            hardwareReady=false,
            fullGate11Passed=false,
            sourceFiles=files,
            sourceTypeHeaders=byType
        };
        var json=Path.Combine(outputDirectory,"aztec_pc_typed_sound_and_source_headers.json");
        var csv=Path.Combine(outputDirectory,"aztec_pc_three_ff_type_pointer_census.csv");
        await File.WriteAllTextAsync(json,JsonSerializer.Serialize(report,
            new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false),ct);
        var output=new StringBuilder();
        output.AppendLine("role,assetType,sourceXAssetCount,originalPcPhysicalInlineHeaders,originalPcOffsetOrAliasHeaders,originalPcNullHeaders");
        // Include exact root pointer counts without implying real payload.
        foreach(var item in byType)
        {
            var el=JsonSerializer.SerializeToElement(item);
            output.AppendLine(string.Join(",",new[]{
                S(el,"role"),S(el,"assetType"),
                I(el,"sourceXAssetCount").ToString(),
                I(el,"originalPcPhysicalInlineHeaders").ToString(),
                I(el,"originalPcOffsetOrAliasHeaders").ToString(),
                I(el,"originalPcNullHeaders").ToString()}));
        }
        await File.WriteAllTextAsync(csv,output.ToString(),new UTF8Encoding(false),ct);
        Console.WriteLine("[AZTEC-SOURCE-BODIES] Sound typed="+soundTyped+"/"+soundPhysical+
            " physical PC aliases, loadedNodes="+soundLoadedNodes+
            ", streamedNodes="+soundStreamedNodes+
            ", Xbox audio conversions=0; report="+json);
        return json;
    }
}
