using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>Read-only selected native XAnim mapping. Never guesses among differing duplicates.</summary>
public static class T4AztecXAnim602RootSelection
{
    private sealed record Body(
        string Name,string Role,string Source,string SourceSha,
        string Container,string ContainerSha,int RecordIndex,
        long SourceOffset,long BodyOffset,int BodyLength,string BodySha);

    public static async Task<string> RunAsync(
        string proofPath,string identityPath,string sourceInventoryPath,
        string outDir,CancellationToken ct=default)
    {
        foreach(var f in new[]{proofPath,identityPath,sourceInventoryPath})
            if(!File.Exists(f))throw new FileNotFoundException("Missing verified XAnim source input",f);
        using var pd=JsonDocument.Parse(await File.ReadAllTextAsync(proofPath,ct));
        using var nd=JsonDocument.Parse(await File.ReadAllTextAsync(identityPath,ct));
        using var sd=JsonDocument.Parse(await File.ReadAllTextAsync(sourceInventoryPath,ct));
        var p=pd.RootElement;var n=nd.RootElement;var s=sd.RootElement;
        static string St(JsonElement e,string k)=>e.GetProperty(k).GetString()??"";
        static int It(JsonElement e,string k)=>e.GetProperty(k).GetInt32();
        static long Lt(JsonElement e,string k)=>e.GetProperty(k).GetInt64();
        static bool Bo(JsonElement e,string k)=>e.GetProperty(k).GetBoolean();
        static bool Eq(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
        if(St(p,"schema")!="WaW360.AztecOriginalPcXAnimNativeTransform614.v1" ||
           St(p,"status")!="AZTEC_614_PC_XANIM_TO_XENON_BLOBS_READBACK_PASS_ALL_602_NATIVE_NAMES_MATCH_NOT_IN_FF" ||
           It(p,"originalPcPhysicalXAnimHeaders")!=614 ||
           It(p,"convertedXboxXAnimBodiesReadbackVerified")!=614 ||
           It(p,"originalPcStrictTypedCandidates")!=614 ||
           It(p,"nativeXboxXAnimRootCount")!=602 ||
           It(p,"exactPcToXboxXAnimNameMatches")!=602 ||
           It(p,"originalPcDuplicateXAnimBodyOccurrences")!=12 ||
           !Bo(p,"allPcXAnimBodiesConvertedWithReadback") ||
           !Bo(p,"allNativeXboxXAnimNameRootsCoveredByProof") ||
           Bo(p,"integratedInto8509NativeXboxRootFastFile") ||
           Bo(p,"hardwareReady") ||
           St(n,"format")!="WaW360.T4Xenon.NativeIdentityManifest.v1" ||
           It(n,"assets")!=8509 || !Bo(n,"typeSequenceVerified") ||
           n.GetProperty("entries").GetArrayLength()!=8509 ||
           St(s,"schema")!="WaW360.AztecPcFastfileImageSourceCensus.v1" ||
           s.GetProperty("sourceFastfiles").GetArrayLength()!=3)
            throw new InvalidDataException("Native Xbox source XAnim proof/identity baseline changed.");

        var roots=n.GetProperty("entries").EnumerateArray()
            .Where(e=>Eq(St(e,"OatTypeName"),"xanim"))
            .Select(e=>new{Index=It(e,"Index"),Name=St(e,"Name")}).ToArray();
        if(roots.Length!=602 || roots.Select(e=>e.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count()!=602)
            throw new InvalidDataException("Native root XAnim identity is not 602 unique names.");

        var sourceSha=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var x in s.GetProperty("sourceFastfiles").EnumerateArray())
        {
            var key=Path.GetFullPath(St(x,"SourceFastFile"));
            var sha=St(x,"Sha256");
            if(sha.Length!=64 || !sourceSha.TryAdd(key,sha))
                throw new InvalidDataException("Original PC source inventory missing/duplicate SHA.");
        }

        var files=p.GetProperty("sourceFiles").EnumerateArray().ToArray();
        if(files.Length!=3 || files.Select(x=>St(x,"role"))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count()!=3)
            throw new InvalidDataException("Expected original-PC map/patch/mod role evidence.");
        var bodies=new List<Body>(614);
        long nativeBodyBytes=0;
        foreach(var entry in files)
        {
            ct.ThrowIfCancellationRequested();
            var role=St(entry,"role");
            if(role!="map.ff" && role!="map_patch.ff" && role!="mod.ff")
                throw new InvalidDataException("Unknown original PC role: "+role);
            var source=Path.GetFullPath(St(entry,"originalPcFastFile"));
            var claimedSha=St(entry,"sourceSha256");
            if(!sourceSha.TryGetValue(source,out var expectedSha) || !Eq(claimedSha,expectedSha))
                throw new InvalidDataException("Original-PC SHA proof mismatch at "+role);
            var zone=await FastFileAnalysisCache.GetZoneAsync(source,ct);
            if(zone.Platform!=WaWPlatform.PC || zone.ZoneHeader.Endianness!=Endian.Little ||
               !Eq(zone.SourceSha256,expectedSha))
                throw new InvalidDataException("Original PC source bytes changed: "+role);
            var names=T4XAnimSerializerProof.DiscoverDiagnosticCandidates(zone.Bytes,Endian.Little)
                .OrderBy(x=>x.Offset).ToArray();
            int total=It(entry,"declaredPcPhysicalXAnimHeaders");
            if(names.Length!=total || It(entry,"strictTypedNamesFound")!=total ||
               It(entry,"xboxXAnimReadbackVerifiedBodies")!=total)
                throw new InvalidDataException("Original PC XAnim typed ordinal proof changed: "+role);
            if(total==0)continue;
            var container=Path.GetFullPath(St(entry,"internalXenonProofContainer"));
            var containerSha=St(entry,"proofSha256");
            if(containerSha.Length!=64)
                throw new InvalidDataException("Original native proof SHA absent: "+role);
            var index=await T4NativeProofContainerReader.IndexAsync(container,"xanim",ct);
            if(index.Version!=12 || index.Records.Count!=total || !Eq(index.Sha256,containerSha))
                throw new InvalidDataException("Native Xbox XAnim proof container was replaced: "+role);
            long fileBodyBytes=0;
            for(int i=0;i<total;i++)
            {
                ct.ThrowIfCancellationRequested();
                var rec=index.Records[i];
                var sourceName=names[i];
                if(rec.SourceOffset!=sourceName.Offset || rec.BlobLength<92)
                    throw new InvalidDataException("Native Xbox XAnim proof ordinal/source offset mismatch: "+role+" #"+i);
                var bytes=await T4NativeProofContainerReader.ReadBlobAsync(rec,ct);
                T4XboxXAnimAssetConverter.ValidateXboxBlob(bytes,sourceName.Name);
                var digest=Convert.ToHexString(SHA256.HashData(bytes));
                bodies.Add(new Body(sourceName.Name,role,source,claimedSha,container,containerSha,
                    i,rec.SourceOffset,rec.ContainerBlobOffset,rec.BlobLength,digest));
                fileBodyBytes+=bytes.LongLength;
            }
            if(fileBodyBytes!=Lt(entry,"nativeXboxSerializedBodyBytes"))
                throw new InvalidDataException("Xbox XAnim converted body byte count mismatch: "+role);
            nativeBodyBytes+=fileBodyBytes;
            Console.WriteLine("[AZTEC-XANIM-602] "+role+": body hashes/readback "+total+"/"+total+" PASS");
        }
        if(bodies.Count!=614 || nativeBodyBytes!=Lt(p,"nativeXboxSerializedAnimationBodyBytes"))
            throw new InvalidDataException("The 614 authentic converted Xbox animation bodies are incomplete.");

        var byName=bodies.GroupBy(x=>x.Name,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x=>x.Key,x=>x.ToArray(),StringComparer.OrdinalIgnoreCase);
        if(byName.Count!=602 || byName.Values.Sum(x=>x.Length-1)!=12)
            throw new InvalidDataException("Original PC XAnim native 602-root/12-extra identity drift.");

        int unique=0,duplicatesIdentical=0,duplicateConflicts=0;
        long selectedBytes=0;
        var rows=new List<object>();
        var blockedNames=new List<string>();
        foreach(var root in roots)
        {
            if(!byName.TryGetValue(root.Name,out var candidates))
                throw new InvalidDataException("Native root with no PC XAnim body: "+root.Name);
            var hashes=candidates.Select(x=>x.BodySha)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            bool unambiguous=hashes.Length==1;
            Body? selected=unambiguous?candidates
                .OrderBy(x=>x.Role=="map.ff"?0:x.Role=="mod.ff"?1:2)
                .ThenBy(x=>x.RecordIndex).First():null;
            if(selected!=null)
            {
                selectedBytes+=selected.BodyLength;
                if(candidates.Length==1)unique++;
                else duplicatesIdentical++;
            }
            else{duplicateConflicts++;blockedNames.Add(root.Name);}
            rows.Add(new {
                nativeRootIndex=root.Index,nativeXAnimName=root.Name,
                originalPcBodyOccurrences=candidates.Length,
                uniqueXenonBodyHashes=hashes.Length,
                decision=selected is null?"DUPLICATE_CONFLICT_REFUSED":
                    candidates.Length>1?"DUPLICATE_IDENTICAL_NATIVE_BODIES":"SINGLE_SOURCE_NATIVE_BODY",
                selectedProofFile=selected?.Container,
                selectedProofSha256=selected?.ContainerSha,
                selectedProofRecordOrdinal=selected?.RecordIndex,
                selectedProofBodyOffset=selected?.BodyOffset,
                selectedProofBodyBytes=selected?.BodyLength,
                selectedNativeBodySha256=selected?.BodySha,
                candidates=candidates.Select(x=>new {
                    role=x.Role,pcSourceFile=x.Source,pcSourceSha256=x.SourceSha,
                    proofFile=x.Container,proofSha256=x.ContainerSha,
                    recordOrdinal=x.RecordIndex,sourceOffset=x.SourceOffset,
                    blobOffset=x.BodyOffset,blobBytes=x.BodyLength,
                    xboxBlobSha256=x.BodySha
                }).ToArray(),
                actuallyGraftedIntoNativeAztecFf=false,
                xboxRuntimeAnimationPlaybackProven=false
            });
        }
        if(unique+duplicatesIdentical+duplicateConflicts!=602)
            throw new InvalidDataException("602 selected XAnim rows do not sum.");
        var result=new {
            schema="WaW360.AztecXAnim602SourceBodyResolution.v1",
            status=duplicateConflicts==0?
                "AZTEC_602_XANIM_NATIVE_BODY_SELECTION_FULLY_UNAMBIGUOUS_NOT_IN_FF":
                "AZTEC_602_XANIM_NATIVE_BODY_DUPLICATE_CONFLICTS_QUARANTINED",
            sourceAuthenticXenonBodiesReadbackVerified=bodies.Count,
            sourceAuthenticXenonTotalBodyBytes=nativeBodyBytes,
            nativeXboxXAnimRoots=602,duplicatePcOccurrences=12,
            nativeRootsWithSingleSource=unique,
            nativeRootsWithIdenticalDuplicateBodies=duplicatesIdentical,
            nativeRootsWithConflictingDuplicateBodies=duplicateConflicts,
            namesOfConflictingDuplicateBodies=blockedNames,
            uniqueNativeRootBodiesSelectionBytes=selectedBytes,
            all602NativeRootBodySourcesUnambiguous=duplicateConflicts==0,
            nativeRootBodySelection=rows,
            noRetailXboxAssetBytesCopied=true,
            noAztecFastFileGeneratedOrMutated=true,
            nativeXAnimGraphGraftedInto8509RootFF=false,
            runtimeAnimationPlaybackProven=false,
            hardwareReady=false,gate11Completed=false
        };
        outDir=Path.GetFullPath(outDir);Directory.CreateDirectory(outDir);
        var json=Path.Combine(outDir,"aztec_602_xanim_source_body_selection.json");
        await File.WriteAllTextAsync(json,JsonSerializer.Serialize(result,
            new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false),ct);
        var md=new StringBuilder();
        md.AppendLine("# 602 native XAnim root body selection from 614 PC-source converted Xbox blobs");
        md.AppendLine();
        md.AppendLine("Status: "+result.status);
        md.AppendLine("Single-source roots: "+unique+
            "; identical converted duplicate names: "+duplicatesIdentical+
            "; conflicting converted duplicates: "+duplicateConflicts+".");
        md.AppendLine("Selected uncompressed native body bytes: "+selectedBytes+" (NOT compressed FF growth).");
        foreach(var name in blockedNames)md.AppendLine("BLOCKED DIFFERENT XBOX BODY SHA: "+name);
        md.AppendLine();
        md.AppendLine("This report includes exact per-root verified proof container, record index, byte offset, blob length and SHA-256; no ambiguous duplicates are selected. No FF built, no runtime proof, no Xbox.");
        var txt=Path.Combine(outDir,"aztec_602_xanim_source_body_selection.md");
        await File.WriteAllTextAsync(txt,md.ToString(),new UTF8Encoding(false),ct);
        Console.WriteLine($"[AZTEC-XANIM-602] Root selection: single={unique} identical-dupes={duplicatesIdentical} conflicts={duplicateConflicts} selected-bytes={selectedBytes}");
        return json;
    }
}
