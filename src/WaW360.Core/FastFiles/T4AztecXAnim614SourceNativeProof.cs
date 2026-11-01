using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>Authentic PC XAnim to Xenon 12-bucket proof containers, NOT an Xbox FF.</summary>
public static class T4AztecXAnim614SourceNativeProof
{
    private const int PcTotal=614;
    private const int NativeTotal=602;
    private static readonly HashSet<string> Roles=new(StringComparer.OrdinalIgnoreCase)
        {"map.ff","map_patch.ff","mod.ff"};

    public static async Task<string> RunAsync(
        string baselinePath,string pcSourcesPath,string nativeIdentityPath,
        string soundBodyPath,string outDir,bool namesOnly=false,CancellationToken ct=default)
    {
        foreach(var p in new[]{baselinePath,pcSourcesPath,nativeIdentityPath,soundBodyPath})
            if(!File.Exists(p))throw new FileNotFoundException("Aztec xanim proof input absent",p);
        using var bd=JsonDocument.Parse(await File.ReadAllTextAsync(baselinePath,ct));
        using var sd=JsonDocument.Parse(await File.ReadAllTextAsync(pcSourcesPath,ct));
        using var nd=JsonDocument.Parse(await File.ReadAllTextAsync(nativeIdentityPath,ct));
        using var td=JsonDocument.Parse(await File.ReadAllTextAsync(soundBodyPath,ct));
        var b=bd.RootElement;var s=sd.RootElement;var n=nd.RootElement;var t=td.RootElement;
        static string St(JsonElement e,string key)=>e.GetProperty(key).GetString()??"";
        static int It(JsonElement e,string key)=>e.GetProperty(key).GetInt32();
        static bool Bo(JsonElement e,string key)=>e.GetProperty(key).GetBoolean();
        if(St(b,"format")!="WaW360.SecondMapBaseline.v1" ||
           !St(b,"mapName").Equals("nazi_zombie_aztec_V2.2",StringComparison.OrdinalIgnoreCase) ||
           St(s,"schema")!="WaW360.AztecPcFastfileImageSourceCensus.v1" ||
           s.GetProperty("sourceFastfiles").GetArrayLength()!=3 ||
           St(n,"format")!="WaW360.T4Xenon.NativeIdentityManifest.v1" ||
           It(n,"assets")!=8509 || !Bo(n,"typeSequenceVerified") ||
           St(t,"schema")!="WaW360.AztecOriginalPcPhysicalBodiesQuickBatch.v1" ||
           St(t,"status")!="AZTEC_PC_XASSET_POINTER_AND_SOUND_ALIAS_STRUCTURE_PASS_NO_XBOX_AUDIO" ||
           It(t,"sourcePhysicalXanimHeaders")!=PcTotal ||
           It(t,"typedPcSoundAliasListsVerified")!=3774 ||
           It(t,"sourceSoundTypedFailedRuns")!=0)
            throw new InvalidDataException("Prior source/3774 typed sound/XAnim root evidence not proven.");
        var xbox=n.GetProperty("entries").EnumerateArray()
            .Where(e=>St(e,"OatTypeName").Equals("xanim",StringComparison.OrdinalIgnoreCase))
            .Select(e=>St(e,"Name")).ToArray();
        if(xbox.Length!=NativeTotal || xbox.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Expected exactly 602 named native Xbox xanim roots.");
        var xboxNames=new HashSet<string>(xbox,StringComparer.OrdinalIgnoreCase);
        var expected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in s.GetProperty("sourceFastfiles").EnumerateArray())
        {
            var path=Path.GetFullPath(St(item,"SourceFastFile"));
            var sha=St(item,"Sha256");
            if(sha.Length!=64 || !expected.TryAdd(path,sha))
                throw new InvalidDataException("Invalid or duplicate pinned original PC source SHA.");
        }
        var sources=b.GetProperty("fastfiles").EnumerateArray()
            .Where(x=>Roles.Contains(St(x,"Role"))).ToArray();
        if(sources.Length!=3)throw new InvalidDataException("Expected all three PC source roles.");
        outDir=Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);
        var proofDir=Path.Combine(outDir,"pc_to_xenon_internal_proof_containers");
        if(!namesOnly)Directory.CreateDirectory(proofDir);
        var pcNames=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        var rows=new List<object>();
        var failures=new List<string>();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int declaredTotal=0,discoveredTotal=0,convertedTotal=0;
        long nativeBytes=0,sourceBytes=0;
        foreach(var item in sources)
        {
            ct.ThrowIfCancellationRequested();
            var role=St(item,"Role");
            var path=Path.GetFullPath(St(item,"Path"));
            var expectedLength=item.GetProperty("Bytes").GetInt64();
            if(!seen.Add(role) || !File.Exists(path) ||
               new FileInfo(path).Length!=expectedLength ||
               !expected.TryGetValue(path,out var sha))
                throw new InvalidDataException("PC XAnim role/source path or byte length changed: "+role);
            var zone=await FastFileAnalysisCache.GetZoneAsync(path,ct);
            if(zone.Platform!=WaWPlatform.PC ||
               zone.ZoneHeader.Endianness!=Endian.Little ||
               !zone.SourceSha256.Equals(sha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PC XAnim source SHA/endian mismatch: "+role);
            int declared=zone.Document.Assets.Count(x=>x.TypeName.Equals("xanim",StringComparison.OrdinalIgnoreCase));
            var found=T4XAnimSerializerProof.DiscoverDiagnosticCandidates(zone.Bytes,Endian.Little)
                .OrderBy(x=>x.Offset).ToArray();
            declaredTotal+=declared;discoveredTotal+=found.Length;
            foreach(var anim in found)
            {
                if(!pcNames.TryGetValue(anim.Name,out var owners))
                {
                    owners=new List<string>();
                    pcNames.Add(anim.Name,owners);
                }
                owners.Add(role);
            }
            string? blocker=null,proofPath=null,proofSha=null;
            int converted=0;
            long pcBodyBytes=0,xboxBodyBytes=0;
            if(!namesOnly && declared>0)
            {
                if(found.Length!=declared)
                    blocker=$"Only {found.Length}/{declared} strict typed inline XAnim source bodies found.";
                else
                {
                    var dest=Path.Combine(proofDir,"aztec_"+role.Replace('.','_')+"_12bucket_xanim.proof.bin");
                    var partial=dest+".partial";
                    try
                    {
                        var result=await T4XboxXAnimTransformProof.BuildAsync(path,partial,ct);
                        if(!result.Verified || result.InlineCandidates!=declared ||
                           result.ConvertedXAnim!=declared || result.DeclaredXAnim!=declared)
                            throw new InvalidDataException("Typed native Xbox XAnim transform incomplete.");
                        var index=await T4NativeProofContainerReader.IndexAsync(partial,"xanim",ct);
                        if(index.Version!=12 || index.Records.Count!=declared ||
                           index.Records.Any(x=>x.BlobLength<92))
                            throw new InvalidDataException("Xbox 0x5C 12-bucket XAnim container readback failed.");
                        File.Move(partial,dest,true);
                        proofPath=dest;
                        await using(var stream=File.OpenRead(dest))
                            proofSha=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));
                        converted=result.ConvertedXAnim;
                        pcBodyBytes=result.SourceSerializedBytes;
                        xboxBodyBytes=result.XboxSerializedBytes;
                    }
                    catch(Exception ex) when(ex is not OperationCanceledException)
                    {
                        blocker=ex.GetType().Name+": "+ex.Message;
                        try{if(File.Exists(partial))File.Delete(partial);}catch{}
                    }
                }
                if(blocker!=null)failures.Add(role+": "+blocker);
            }
            convertedTotal+=converted;
            sourceBytes+=pcBodyBytes;
            nativeBytes+=xboxBodyBytes;
            rows.Add(new{
                role,originalPcFastFile=path,sourceSha256=sha,
                declaredPcPhysicalXAnimHeaders=declared,
                strictTypedNamesFound=found.Length,
                sourceNameCountExact=found.Length==declared,
                xboxXAnimReadbackVerifiedBodies=converted,
                originalPcSerializedBodyBytes=pcBodyBytes,
                nativeXboxSerializedBodyBytes=xboxBodyBytes,
                internalXenonProofContainer=proofPath,
                proofSha256=proofSha,blocker
            });
            Console.WriteLine("[AZTEC-XANIM-614] "+role+": PC="+declared+
                " typed="+found.Length+" Xbox transform/readback="+converted+
                (blocker is null?"":" BLOCKED "+blocker));
        }
        if(declaredTotal!=PcTotal)
            throw new InvalidDataException("Original PC 614 physical XAnim source count drifted.");
        var pcKeys=new HashSet<string>(pcNames.Keys,StringComparer.OrdinalIgnoreCase);
        var pcOnly=pcKeys.Except(xboxNames,StringComparer.OrdinalIgnoreCase)
            .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        var nativeOnly=xboxNames.Except(pcKeys,StringComparer.OrdinalIgnoreCase)
            .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        var matched=pcKeys.Intersect(xboxNames,StringComparer.OrdinalIgnoreCase).Count();
        var doubles=pcNames.Where(x=>x.Value.Count>1)
            .Select(x=>new {name=x.Key,occurrences=x.Value.Count,roles=x.Value.ToArray()})
            .OrderBy(x=>x.name,StringComparer.OrdinalIgnoreCase).ToArray();
        var allConverted=convertedTotal==PcTotal && failures.Count==0;
        var status=namesOnly?"AZTEC_XANIM_PC_NAME_DIFFERENTIAL_ONLY_NO_CONVERSION":
            allConverted && nativeOnly.Length==0 && xboxNames.Count==NativeTotal?
            "AZTEC_614_PC_XANIM_TO_XENON_BLOBS_READBACK_PASS_ALL_602_NATIVE_NAMES_MATCH_NOT_IN_FF":
            allConverted?"AZTEC_614_PC_XANIM_TO_XENON_BLOBS_READBACK_PASS_NATIVE_IDENTITY_GAPS":
            "AZTEC_PC_XANIM_TO_XENON_TRANSFORM_PARTIAL";
        var resultDoc=new{
            schema="WaW360.AztecOriginalPcXAnimNativeTransform614.v1",
            status,
            originalPcFastfiles=3,originalPcPhysicalXAnimHeaders=declaredTotal,
            originalPcStrictTypedCandidates=discoveredTotal,
            originalPcDistinctXAnimNames=pcKeys.Count,
            originalPcDuplicateXAnimBodyOccurrences=PcTotal-pcKeys.Count,
            duplicateOriginalPcXAnimNames=doubles,
            nativeXboxXAnimRootCount=NativeTotal,
            nativeXboxDistinctXAnimRootNames=xboxNames.Count,
            exactPcToXboxXAnimNameMatches=matched,
            pcOnlyXAnimNames=pcOnly,nativeOnlyXAnimNames=nativeOnly,
            convertedXboxXAnimBodiesReadbackVerified=convertedTotal,
            originalPcSerializedAnimationBodyBytes=sourceBytes,
            nativeXboxSerializedAnimationBodyBytes=nativeBytes,
            allPcXAnimBodiesConvertedWithReadback=allConverted,
            allNativeXboxXAnimNameRootsCoveredByProof=
                !namesOnly && allConverted && nativeOnly.Length==0 && xboxNames.Count==NativeTotal,
            priorOriginalPcSoundAliasListsVerified=It(t,"typedPcSoundAliasListsVerified"),
            priorOriginalPcLoadedSoundNodes=It(t,"typedPcLoadedSoundNodes"),
            xboxLoadedSoundRootCount=1532,
            pcSoundNodeCountDoesNotDirectlyEqualNativeXboxLoadedSoundRootCount=true,
            sourceFiles=rows,failures,
            internalProofContainersOnly=true,fastFileWasRebuilt=false,
            integratedInto8509NativeXboxRootFastFile=false,
            nativeXboxAnimationPlaybackTested=false,
            hardwareReady=false,fullGate11Passed=false
        };
        var json=Path.Combine(outDir,"aztec_614_pc_xanim_native_conversion_and_name_diff.json");
        await File.WriteAllTextAsync(json,JsonSerializer.Serialize(resultDoc,
            new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false),ct);
        var text=new StringBuilder();
        text.AppendLine("# Aztec: all 614 original-PC XAnim bodies vs 602 native roots");
        text.AppendLine();
        text.AppendLine("Status: "+status);
        text.AppendLine("PC physical: "+PcTotal+"; typed found: "+discoveredTotal+
            "; distinct PC names: "+pcKeys.Count+"; PC repeated name bodies: "+(PcTotal-pcKeys.Count)+".");
        text.AppendLine("Xbox root names: "+NativeTotal+"; matched: "+matched+
            "; PC-only: "+pcOnly.Length+"; native-only: "+nativeOnly.Length+".");
        text.AppendLine("Full source PC-to-Xbox 0x5C/12-bucket transformed and independently read back: "+convertedTotal+"/"+PcTotal+".");
        text.AppendLine("Original PC source XAnim body bytes: "+sourceBytes+
            "; converted internal Xbox proof body bytes: "+nativeBytes+".");
        text.AppendLine();
        foreach(var x in doubles.Take(60))text.AppendLine("Repeated PC XAnim: "+x.name+" roles "+string.Join(",",x.roles));
        foreach(var name in pcOnly.Take(60))text.AppendLine("PC only: "+name);
        foreach(var name in nativeOnly.Take(60))text.AppendLine("Native only: "+name);
        foreach(var error in failures)text.AppendLine("BLOCKED: "+error);
        text.AppendLine();
        text.AppendLine("No map FF was built, no console runtime was tested. These are source-authentic Xbox-layout blobs in standalone internal proof containers, not the 8509-root game FF.");
        text.AppendLine("PC loaded-sound nodes and Xbox LoadedSound native roots have different ownership semantics: counts alone cannot be subtracted as missing bytes/roots.");
        await File.WriteAllTextAsync(
            Path.Combine(outDir,"aztec_614_pc_xanim_native_conversion_and_name_diff.md"),
            text.ToString(),new UTF8Encoding(false),ct);
        return json;
    }
}
