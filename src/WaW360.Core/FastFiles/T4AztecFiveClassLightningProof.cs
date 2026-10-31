using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>Five original-PC source class proof sweeps, never a native game FastFile.</summary>
public static class T4AztecFiveClassLightningProof
{
    private sealed record Run(string Type,string SourceRole,string Stage,int Declared,int Covered,
        string Status,string Detail,string? Container);
    private sealed record Body(string Type,string SourceRole,string Name,long Bytes,
        string ProofStage,string? Container,string? ContainerSha);
    private static readonly string[] Types={"material","xmodel","fx","localize","weapon"};
    private static readonly HashSet<string> Roles=new(StringComparer.OrdinalIgnoreCase)
        {"map.ff","map_patch.ff","mod.ff"};

    public static async Task<string> RunAsync(
        string baselinePath,string inventoryPath,string identityPath,string xanim614Path,
        string xanim602Path,string materialCandidatePath,string outDir,
        bool evidenceOnly=false,CancellationToken ct=default)
    {
        foreach(var f in new[]{baselinePath,inventoryPath,identityPath,xanim614Path,
                                 xanim602Path,materialCandidatePath})
            if(!File.Exists(f))throw new FileNotFoundException("Five-class source proof prerequisite missing",f);
        using var bd=JsonDocument.Parse(await File.ReadAllTextAsync(baselinePath,ct));
        using var sd=JsonDocument.Parse(await File.ReadAllTextAsync(inventoryPath,ct));
        using var nd=JsonDocument.Parse(await File.ReadAllTextAsync(identityPath,ct));
        using var ad=JsonDocument.Parse(await File.ReadAllTextAsync(xanim614Path,ct));
        using var xd=JsonDocument.Parse(await File.ReadAllTextAsync(xanim602Path,ct));
        var b=bd.RootElement; var s=sd.RootElement; var n=nd.RootElement;
        var a=ad.RootElement; var x=xd.RootElement;
        static string St(JsonElement e,string key)=>e.GetProperty(key).GetString()??"";
        static int It(JsonElement e,string key)=>e.GetProperty(key).GetInt32();
        static bool Bo(JsonElement e,string key)=>e.GetProperty(key).GetBoolean();
        static bool Eq(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);

        if(St(b,"format")!="WaW360.SecondMapBaseline.v1" ||
           !Eq(St(b,"mapName"),"nazi_zombie_aztec_V2.2") ||
           St(s,"schema")!="WaW360.AztecPcFastfileImageSourceCensus.v1" ||
           s.GetProperty("sourceFastfiles").GetArrayLength()!=3 ||
           St(n,"format")!="WaW360.T4Xenon.NativeIdentityManifest.v1" ||
           It(n,"assets")!=8509 || !Bo(n,"typeSequenceVerified") ||
           St(a,"schema")!="WaW360.AztecOriginalPcXAnimNativeTransform614.v1" ||
           It(a,"convertedXboxXAnimBodiesReadbackVerified")!=614 ||
           St(x,"schema")!="WaW360.AztecXAnim602SourceBodyResolution.v1" ||
           St(x,"status")!="AZTEC_602_XANIM_NATIVE_BODY_DUPLICATE_CONFLICTS_QUARANTINED" ||
           It(x,"nativeRootsWithSingleSource")!=590 ||
           It(x,"nativeRootsWithConflictingDuplicateBodies")!=12 ||
           Bo(x,"hardwareReady"))
            throw new InvalidDataException("Source corpus, 8509 Xbox roots, or 12 XAnim conflicts no longer match.");

        var roots=n.GetProperty("entries").EnumerateArray().Select(e=>new{
            Index=It(e,"Index"),Type=St(e,"OatTypeName").ToLowerInvariant(),
            Name=St(e,"Name")}).ToArray();
        if(roots.Length!=8509 || roots.Where((e,i)=>e.Index!=i).Any())
            throw new InvalidDataException("Native XAsset root order changed.");
        var sourceHashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in s.GetProperty("sourceFastfiles").EnumerateArray())
        {
            var path=Path.GetFullPath(St(e,"SourceFastFile"));
            var sha=St(e,"Sha256");
            if(sha.Length!=64 || !sourceHashes.TryAdd(path,sha))
                throw new InvalidDataException("Original-PC source hashes not unique.");
        }
        var pcRows=b.GetProperty("fastfiles").EnumerateArray()
            .Where(e=>Roles.Contains(St(e,"Role"))).ToArray();
        if(pcRows.Length!=3 || pcRows.Select(e=>St(e,"Role"))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count()!=3)
            throw new InvalidDataException("Expected 3 original PC FF roles.");

        outDir=Path.GetFullPath(outDir);Directory.CreateDirectory(outDir);
        var payloadDir=Path.Combine(outDir,"standalone_pc_source_native_proofs");
        if(!evidenceOnly)Directory.CreateDirectory(payloadDir);
        var runs=new List<Run>();
        var bodies=new List<Body>();
        var errors=new List<string>();
        var sourceFiles=new List<(string Role,string Path,T4ZoneDocument Doc,byte[] Bytes)>();
        var xanimAnchors=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var ff in a.GetProperty("sourceFiles").EnumerateArray())
            if(It(ff,"xboxXAnimReadbackVerifiedBodies")>0 &&
               ff.GetProperty("internalXenonProofContainer").ValueKind==JsonValueKind.String)
                xanimAnchors[St(ff,"role")]=St(ff,"internalXenonProofContainer");

        foreach(var e in pcRows)
        {
            ct.ThrowIfCancellationRequested();
            var role=St(e,"Role");
            var path=Path.GetFullPath(St(e,"Path"));
            if(!sourceHashes.TryGetValue(path,out var sha) || !File.Exists(path) ||
               new FileInfo(path).Length!=e.GetProperty("Bytes").GetInt64())
                throw new InvalidDataException("Missing original-PC SHA-pinned FF: "+role);
            var zone=await FastFileAnalysisCache.GetZoneAsync(path,ct);
            if(zone.Platform!=WaWPlatform.PC ||
               zone.ZoneHeader.Endianness!=Endian.Little || !Eq(zone.SourceSha256,sha))
                throw new InvalidDataException("Original-PC source digest changed: "+role);
            sourceFiles.Add((role,path,zone.Document,zone.Bytes));
        }
        void Block(string type,string role,string stage,int expected,Exception ex)
        {
            var reason=ex.GetType().Name+": "+ex.Message;
            runs.Add(new Run(type,role,stage,expected,0,"BLOCKED",reason,null));
            errors.Add(type+"/"+role+"/"+stage+": "+reason);
            Console.WriteLine("[AZTEC-5LIGHTNING] BLOCKED "+type+" "+role+": "+reason);
        }
        async Task<(int Count,string Sha)> Verify(string path,string type,int expected)
        {
            var idx=await T4NativeProofContainerReader.IndexAsync(path,type,ct);
            if(idx.Records.Count!=expected)
                throw new InvalidDataException(type+" native proof container record count mismatch: "+idx.Records.Count+"/"+expected);
            return(idx.Records.Count,idx.Sha256);
        }

        // Weapons before models, since the weapon-owned proof may establish
        // an XAsset-order alignment anchor for XModel source-body discovery.
        var weaponAnchors=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var src in sourceFiles)
        {
            int declared=src.Doc.Assets.Count(e=>Eq(e.TypeName,"weapon"));
            if(declared==0)continue;
            if(evidenceOnly)
            {
                runs.Add(new Run("weapon",src.Role,"source_headers",declared,0,
                    "HEADERS_ONLY","No fixed or variable payload transform executed.",null));
                continue;
            }
            var fixedFile=Path.Combine(payloadDir,src.Role.Replace('.','_')+"_weapon_fixed.bin");
            var ownedFile=Path.Combine(payloadDir,src.Role.Replace('.','_')+"_weapon_owned.bin");
            try
            {
                var fixedProof=await T4WeaponSerializerProof.BuildProofAsync(src.Path,fixedFile,ct);
                if(!fixedProof.Verified || fixedProof.ResolvedWeaponCount!=declared)
                    throw new InvalidDataException("Weapon fixed XAsset coverage incomplete.");
                runs.Add(new Run("weapon",src.Role,"fixed_6f",declared,declared,
                    "FIXED_BODY_ONLY_PASS","0x9AC Xbox fixed bodies; variable data requires 7b.",fixedFile));
                try
                {
                    var owned=await T4WeaponVariableSerializerProof.BuildProofAsync(
                        src.Path,ownedFile,fixedFile,ct);
                    if(!owned.Verified || owned.CoveredWeaponCount!=declared)
                        throw new InvalidDataException("Weapon variable owned graph does not cover source XAsset list.");
                    var proof=await Verify(ownedFile,"weapon",owned.SerializedBodyCount);
                    weaponAnchors[src.Role]=ownedFile;
                    runs.Add(new Run("weapon",src.Role,"owned_7b",declared,declared,
                        "XBOX_OWNED_GRAPH_READBACK","Native fixed+variable child structure; not game FF.",ownedFile));
                    foreach(var item in owned.Assets)
                        bodies.Add(new Body("weapon",src.Role,item.Name,item.XboxSerializedBytes,
                            "owned_7b",ownedFile,proof.Sha));
                }
                catch(Exception ex) when(ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    Block("weapon",src.Role,"owned_7b",declared,ex);
                }
            }
            catch(Exception ex) when(ex is not OperationCanceledException and not OutOfMemoryException)
            {
                Block("weapon",src.Role,"fixed_6f",declared,ex);
            }
        }

        foreach(var src in sourceFiles)
        {
            int declared=src.Doc.Assets.Count(e=>Eq(e.TypeName,"xmodel"));
            if(declared==0)continue;
            if(evidenceOnly)
            {
                runs.Add(new Run("xmodel",src.Role,"source_headers",declared,0,
                    "HEADERS_ONLY","XModel body graph serializer skipped.",null));
                continue;
            }
            var path=Path.Combine(payloadDir,src.Role.Replace('.','_')+"_xmodel_owned.bin");
            try
            {
                xanimAnchors.TryGetValue(src.Role,out var xanim);
                weaponAnchors.TryGetValue(src.Role,out var weapon);
                var proof=await T4XModelSerializerProof.BuildProofAsync(src.Path,path,xanim,weapon,ct);
                if(!proof.Verified || proof.ResolvedXModelCount!=declared)
                    throw new InvalidDataException("XModel graph source body count mismatch.");
                var idx=await Verify(path,"xmodel",proof.Assets.Count);
                runs.Add(new Run("xmodel",src.Role,"owned_9j",declared,declared,
                    "XBOX_OWNED_GRAPH_READBACK","Typed model/geometry/dependency payload; no runtime.",path));
                foreach(var item in proof.Assets)
                    bodies.Add(new Body("xmodel",src.Role,item.Name,item.XboxSerializedBytes,
                        "owned_9j",path,idx.Sha));
            }
            catch(Exception ex) when(ex is not OperationCanceledException and not OutOfMemoryException)
            {
                Block("xmodel",src.Role,"owned_9j",declared,ex);
            }
        }
        foreach(var src in sourceFiles)
        {
            int declared=src.Doc.Assets.Count(e=>Eq(e.TypeName,"fx"));
            if(declared==0)continue;
            if(evidenceOnly)
            {
                runs.Add(new Run("fx",src.Role,"source_headers",declared,0,
                    "HEADERS_ONLY","FX body graph serializer skipped.",null));
                continue;
            }
            var path=Path.Combine(payloadDir,src.Role.Replace('.','_')+"_fx_owned.bin");
            try
            {
                var proof=await T4FxSerializerProof.BuildProofAsync(src.Path,path,ct);
                if(!proof.Verified || proof.CoveredFxCount!=declared)
                    throw new InvalidDataException("FX source-owned serializer XAsset mismatch.");
                var idx=await Verify(path,"fx",proof.SerializedBodyCount);
                runs.Add(new Run("fx",src.Role,"owned_9b",declared,declared,
                    "XBOX_OWNED_GRAPH_READBACK","Typed FxElemDef and child graph; no runtime.",path));
                foreach(var item in proof.Assets)
                    bodies.Add(new Body("fx",src.Role,item.Name,item.XboxSerializedBytes,
                        "owned_9b",path,idx.Sha));
            }
            catch(Exception ex) when(ex is not OperationCanceledException and not OutOfMemoryException)
            {
                Block("fx",src.Role,"owned_9b",declared,ex);
            }
        }

        foreach(var src in sourceFiles)
        {
            int declared=src.Doc.Assets.Count(e=>Eq(e.TypeName,"localize"));
            if(declared==0)continue;
            try
            {
                var scan=T4PayloadScanner.Scan(src.Doc,src.Bytes);
                var assigned=scan.ResolvedAssets.Where(e=>Eq(e.TypeName,"localize")).ToArray();
                if(assigned.Length!=declared || scan.LocalizeEntries.Count!=declared)
                    throw new InvalidDataException("Localize value/name source mapping is incomplete: "+
                        assigned.Length+"/"+declared);
                int packed=scan.LocalizeEntries.Count(e=>e.SourceValuePointer is not
                    (T4ZonePointer.Following or T4ZonePointer.Insert));
                runs.Add(new Run("localize",src.Role,"typed_original_pc_values",
                    declared,assigned.Length,packed==0?"TYPED_PC_VALUES_PASS":"PACKED_VALUE_POINTERS_UNREBASABLE",
                    "Source strings only; packed value pointers="+packed+". Xbox payload not emitted.",null));
                foreach(var item in assigned)
                    bodies.Add(new Body("localize",src.Role,item.Name,0,"typed_pc_value",null,null));
            }
            catch(Exception ex) when(ex is not OperationCanceledException and not OutOfMemoryException)
            {
                Block("localize",src.Role,"typed_original_pc_values",declared,ex);
            }
        }

        try
        {
            if(evidenceOnly)
                runs.Add(new Run("material","gfxworld","existing_39_material_codec",39,0,
                    "SKIPPED_EVIDENCE_ONLY","Source graph codec not rerun.",null));
            else
            {
                var report=await T4AztecPcXenonMaterial39CodecPreflight.RunAsync(
                    materialCandidatePath,identityPath,
                    Path.Combine(outDir,"material39_codec_recheck"),ct);
                if(report.MaterialGraphsLoadedByExistingCodec!=39)
                    throw new InvalidDataException("39 Material graphs not loaded by native codec.");
                runs.Add(new Run("material","gfxworld","existing_39_material_codec",39,39,
                    "39_GRAPH_CODEC_SOURCE_PASS",
                    "Native codec plan="+report.MaterialGraphsPlannedByExistingCodec+
                    "/39; dependency errors="+report.ExistingCodecDependencyErrorCount+
                    "; stateBits, shader and image semantics remain unresolved.",
                    Path.Combine(outDir,"material39_codec_recheck",
                        "aztec_39_material_existing_codec_preflight.json")));
                using var materialDoc=JsonDocument.Parse(await File.ReadAllTextAsync(materialCandidatePath,ct));
                var entries=materialDoc.RootElement.GetProperty("Materials").EnumerateArray().ToArray();
                if(entries.Length!=39)throw new InvalidDataException("World Material source graph list isn't 39.");
                foreach(var e in entries)
                {
                    var name=St(e,"VerifiedMaterialName");
                    var idx=It(e,"GoldenXboxRootIndex");
                    if(idx<0 || idx>=roots.Length || roots[idx].Type!="material" || !Eq(roots[idx].Name,name))
                        throw new InvalidDataException("39 source Material names differ from native XAsset roots.");
                    bodies.Add(new Body("material","gfxworld",name,0,"39_codec_graph",materialCandidatePath,null));
                }
            }
        }
        catch(Exception ex) when(ex is not OperationCanceledException and not OutOfMemoryException)
        {
            Block("material","gfxworld","existing_39_material_codec",39,ex);
        }

        var counts=new List<object>();
        foreach(var type in Types)
        {
            var native=roots.Where(e=>e.Type==type).ToArray();
            var provenNames=bodies.Where(e=>e.Type==type &&
                !e.Name.StartsWith("<",StringComparison.Ordinal))
                .Select(e=>e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var matched=native.Count(e=>provenNames.Contains(e.Name));
            counts.Add(new {
                type,nativeRootCount=native.Length,
                originalPcTopLevelAssetHeaders=sourceFiles.Sum(f=>f.Doc.Assets.Count(e=>Eq(e.TypeName,type))),
                realSourceTypedNames=provenNames.Count,nativeRootNamesWithMatchingSourceBodyProof=matched,
                nativeRootsNotCoveredByExactTypedSourceNames=native.Length-matched,
                standaloneXboxSourceBlobBodies=type=="material"||type=="localize"?0:
                    bodies.Count(e=>e.Type==type),
                sourceMaterialsValidatedInExistingCodec=type=="material"?
                    bodies.Count(e=>e.Type=="material"):0,
                proofRuns=runs.Where(e=>e.Type==type).ToArray(),
                integratedIntoNativeFastFile=false,runtimeResidencyProven=false
            });
        }

        var conflicts=x.GetProperty("namesOfConflictingDuplicateBodies").EnumerateArray()
            .Select(e=>e.GetString()??"").ToArray();
        if(conflicts.Length!=12)throw new InvalidDataException("12 XAnim blockers unexpectedly changed.");
        var output=new {
            schema="WaW360.AztecFiveClassLightningSourceProof.v1",
            status=errors.Count==0?
                "AZTEC_FIVE_CLASS_SOURCE_PROOFS_COMPLETE_NATIVE_FF_STILL_INCOMPLETE":
                "AZTEC_FIVE_CLASS_SOURCE_PROOFS_PARTIAL_BLOCKERS_REPORTED",
            originalPcSourceFastfiles=3,sourceShaVerified=true,nativeXAssetRootCount=8509,
            categories=counts,
            perOriginalPcTypedBodyProof=bodies,
            blockedProofs=errors,
            carriedForwardConflictingXAnimNames=conflicts,
            carriedForwardXAnimConflicts=12,
            noAztecFastFileCreatedOrModified=true,
            noXboxRetailAssetBodiesCopied=true,
            noOatRerun=true,
            nativeXboxRuntimeAssetsVerified=false,
            hardwareReady=false,
            gate11Closed=false
        };
        var json=Path.Combine(outDir,"aztec_five_class_lightning_source_proof.json");
        await File.WriteAllTextAsync(json,JsonSerializer.Serialize(output,
            new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false),ct);
        var md=new StringBuilder();
        md.AppendLine("# Aztec: Five-class original PC source verification");
        md.AppendLine("Status: "+output.status);
        md.AppendLine();
        md.AppendLine("| Type | Native roots | Original PC headers | Native names backed by typed source proof | No typed same-name source proof |");
        md.AppendLine("|---|---:|---:|---:|---:|");
        foreach(var type in Types)
        {
            var native=roots.Where(e=>e.Type==type).ToArray();
            var names=bodies.Where(e=>e.Type==type).Select(e=>e.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var matching=native.Count(e=>names.Contains(e.Name));
            md.AppendLine("| "+type+" | "+native.Length+" | "+
                sourceFiles.Sum(f=>f.Doc.Assets.Count(e=>Eq(e.TypeName,type)))+
                " | "+matching+" | "+(native.Length-matching)+" |");
        }
        md.AppendLine();
        foreach(var run in runs)
            md.AppendLine("- "+run.Type+"/"+run.SourceRole+": "+run.Stage+
                " "+run.Covered+"/"+run.Declared+" "+run.Status+"; "+run.Detail);
        foreach(var issue in errors)md.AppendLine("BLOCKER: "+issue);
        md.AppendLine();
        md.AppendLine("12 unresolved XAnim native same-name body differences were carried forward:");
        foreach(var name in conflicts)md.AppendLine("- "+name);
        md.AppendLine();
        md.AppendLine("Stand-alone source proofs are NOT payload grafts. Original PC Localize values are NOT native strings, and 39 converted world Material graph proofs do NOT complete remaining 498 material roots/technique/stateBits dependencies.");
        md.AppendLine("No map FF, native OAT, Xbox console or runtime test performed.");
        var mdPath=Path.Combine(outDir,"aztec_five_class_lightning_source_proof.md");
        await File.WriteAllTextAsync(mdPath,md.ToString(),new UTF8Encoding(false),ct);
        Console.WriteLine("[AZTEC-5LIGHTNING] Source verification: 5 classes, stages="+runs.Count+
            ", blocked="+errors.Count+", XAnim conflicts carried=12, no FF.");
        return json;
    }
}
