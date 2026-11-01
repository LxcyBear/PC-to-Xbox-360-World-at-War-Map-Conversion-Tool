using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// SHA-verified selection of XModel/FX/Weapon standalone source-owned Xbox blobs
/// against exact native Aztec XAsset root identities. Refuses differing duplicates.
/// No FF emission, runtime-residency claim or Xbox retail-body fallback.
/// </summary>
public static class T4AztecThreeBodyRootSelection
{
    private sealed record Body(
        string Type,string Name,string Role,string SourceContainer,string ContainerSha,
        int ContainerRecordOrdinal,long BlobOffset,int Bytes,string BodySha);

    private static readonly Dictionary<string,int> ExpectedRoots=new(StringComparer.OrdinalIgnoreCase)
    {
        ["xmodel"]=284,["fx"]=233,["weapon"]=69
    };
    private static readonly Dictionary<string,int> ExpectedBodies=new(StringComparer.OrdinalIgnoreCase)
    {
        ["xmodel"]=304,["fx"]=248,["weapon"]=71
    };
    private static readonly Dictionary<string,int> ExpectedMatchingNames=new(StringComparer.OrdinalIgnoreCase)
    {
        ["xmodel"]=278,["fx"]=225,["weapon"]=59
    };

    public static async Task<string> RunAsync(
        string fiveClassReport,string nativeIdentityManifest,string xanim602Selection,
        string outputDirectory,CancellationToken ct=default)
    {
        foreach(var file in new[]{fiveClassReport,nativeIdentityManifest,xanim602Selection})
            if(!File.Exists(file))throw new FileNotFoundException("Aztec source-native root-selection input absent",file);

        using var fd=JsonDocument.Parse(await File.ReadAllTextAsync(fiveClassReport,ct));
        using var nd=JsonDocument.Parse(await File.ReadAllTextAsync(nativeIdentityManifest,ct));
        using var xd=JsonDocument.Parse(await File.ReadAllTextAsync(xanim602Selection,ct));
        var f=fd.RootElement;var n=nd.RootElement;var x=xd.RootElement;
        static string S(JsonElement e,string key)=>e.GetProperty(key).GetString()??"";
        static int I(JsonElement e,string key)=>e.GetProperty(key).GetInt32();
        static bool B(JsonElement e,string key)=>e.GetProperty(key).GetBoolean();
        static bool Eq(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);

        if(S(f,"schema")!="WaW360.AztecFiveClassLightningSourceProof.v1" ||
           S(f,"status")!="AZTEC_FIVE_CLASS_SOURCE_DIAGNOSTIC_STAGES_PASS_CONTENT_CLOSURE_OPEN" ||
           I(f,"nativeXAssetRootCount")!=8509 ||
           I(f,"originalPcSourceFastfiles")!=3 || !B(f,"sourceShaVerified") ||
           f.GetProperty("blockedProofs").GetArrayLength()!=0 ||
           !B(f,"noAztecFastFileCreatedOrModified") || B(f,"hardwareReady") ||
           S(n,"format")!="WaW360.T4Xenon.NativeIdentityManifest.v1" ||
           I(n,"assets")!=8509 || !B(n,"typeSequenceVerified") ||
           n.GetProperty("entries").GetArrayLength()!=8509 ||
           S(x,"schema")!="WaW360.AztecXAnim602SourceBodyResolution.v1" ||
           I(x,"nativeRootsWithSingleSource")!=590 ||
           I(x,"nativeRootsWithConflictingDuplicateBodies")!=12 ||
           B(x,"hardwareReady"))
            throw new InvalidDataException("Five-class source proof, 8509 native root manifest or 590/12 XAnim fence changed.");

        var native=n.GetProperty("entries").EnumerateArray().Select(e=>new{
            Index=I(e,"Index"),Type=S(e,"OatTypeName").ToLowerInvariant(),Name=S(e,"Name")
        }).ToArray();
        if(native.Where((e,i)=>e.Index!=i).Any())
            throw new InvalidDataException("Native XAsset ordinal order changed.");

        var counts=f.GetProperty("categories").EnumerateArray().ToDictionary(
            e=>S(e,"type"),e=>e,StringComparer.OrdinalIgnoreCase);
        if(counts.Count!=5)
            throw new InvalidDataException("Original five-class category inventory has drifted.");
        foreach(var kind in ExpectedRoots.Keys)
        {
            if(!counts.TryGetValue(kind,out var category) ||
               I(category,"nativeRootCount")!=ExpectedRoots[kind] ||
               I(category,"nativeRootNamesWithMatchingSourceBodyProof")!=ExpectedMatchingNames[kind] ||
               I(category,"standaloneXboxSourceBlobBodies")!=ExpectedBodies[kind] ||
               category.GetProperty("proofRuns").EnumerateArray().Any(e=>
                   S(e,"Status")!="XBOX_OWNED_GRAPH_READBACK" &&
                   S(e,"Status")!="FIXED_BODY_ONLY_PASS"))
                throw new InvalidDataException("Original-PC owned Xbox-body class stage changed: "+kind);
        }

        var proofRows=f.GetProperty("perOriginalPcTypedBodyProof").EnumerateArray()
            .Where(e=>ExpectedRoots.ContainsKey(S(e,"Type"))).ToArray();
        if(proofRows.Length!=ExpectedBodies.Values.Sum())
            throw new InvalidDataException("XModel/FX/Weapon converted body records are not complete.");

        var allBodies=new List<Body>();
        var containerGroups=proofRows.GroupBy(e=>S(e,"Container"),StringComparer.OrdinalIgnoreCase);
        foreach(var group in containerGroups)
        {
            ct.ThrowIfCancellationRequested();
            var path=Path.GetFullPath(group.Key);
            var rows=group.ToArray();
            var kind=S(rows[0],"Type").ToLowerInvariant();
            if(rows.Any(r=>!Eq(S(r,"Type"),kind)) ||
               rows.Any(r=>!Eq(S(r,"ContainerSha"),S(rows[0],"ContainerSha"))))
                throw new InvalidDataException("Proof container reused across source types or SHA changed: "+path);
            var sha=S(rows[0],"ContainerSha");
            if(sha.Length!=64)
                throw new InvalidDataException("Original-PC source-native container SHA omitted: "+path);
            var index=await T4NativeProofContainerReader.IndexAsync(path,kind,ct);
            if(!Eq(sha,index.Sha256) || index.Records.Count!=rows.Length)
                throw new InvalidDataException("Source-native proof SHA/record-count mismatch: "+path);

            for(int ordinal=0;ordinal<rows.Length;ordinal++)
            {
                ct.ThrowIfCancellationRequested();
                var row=rows[ordinal];
                var rec=index.Records[ordinal];
                var name=S(row,"Name");
                var bytes=I(row,"Bytes");
                if(rec.BlobLength!=bytes || rec.BlobLength<=0 ||
                   (rec.Name!=null && !Eq(rec.Name,name)))
                    throw new InvalidDataException("Original-PC owned proof body ordinal, name or length mismatch: "+path+"#"+ordinal);
                var blob=await T4NativeProofContainerReader.ReadBlobAsync(rec,ct);
                var bodySha=Convert.ToHexString(SHA256.HashData(blob));
                allBodies.Add(new Body(kind,name,S(row,"SourceRole"),path,index.Sha256,
                    ordinal,rec.ContainerBlobOffset,rec.BlobLength,bodySha));
            }
            Console.WriteLine("[AZTEC-3ROOT] "+kind+" "+Path.GetFileName(path)+
                ": SHA-256 and "+rows.Length+" native owned payload records PASS");
        }
        if(allBodies.Count!=ExpectedBodies.Values.Sum())
            throw new InvalidDataException("Verified XModel/FX/Weapon source body count lost.");

        var nativeRows=new List<object>();
        var deltas=new List<object>();
        var rootCounters=new List<object>();
        foreach(var kind in ExpectedRoots.Keys)
        {
            var roots=native.Where(e=>e.Type==kind).ToArray();
            var bodies=allBodies.Where(e=>e.Type==kind).ToArray();
            var sourcesByName=bodies.GroupBy(e=>e.Name,StringComparer.OrdinalIgnoreCase)
                .ToDictionary(e=>e.Key,e=>e.ToArray(),StringComparer.OrdinalIgnoreCase);
            int single=0,identical=0,conflicting=0,missing=0;
            long selectedBytes=0;
            foreach(var root in roots)
            {
                var candidates=sourcesByName.TryGetValue(root.Name,out var matched)?
                    matched:Array.Empty<Body>();
                var hashes=candidates.Select(e=>e.BodySha).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                bool unique=hashes.Length==1;
                Body? selected=unique?candidates.OrderBy(c=>c.Role=="map.ff"?0:
                    c.Role=="mod.ff"?1:2).ThenBy(c=>c.ContainerRecordOrdinal).First():null;
                string decision;
                if(candidates.Length==0){missing++;decision="NO_EXACT_NAME_TYPED_SOURCE_BODY";}
                else if(!unique){conflicting++;decision="DUPLICATE_BODY_SHA_CONFLICT_NO_SELECTION";}
                else if(candidates.Length==1){single++;decision="SINGLE_NATIVE_SOURCE_BLOB";}
                else{identical++;decision="DUPLICATE_XBOX_BLOB_BYTES_IDENTICAL";}
                if(selected!=null)selectedBytes+=selected.Bytes;
                var candidateRows=candidates.Select(c=>new{
                    sourceRole=c.Role,proofContainer=c.SourceContainer,
                    proofContainerSha256=c.ContainerSha,
                    proofOrdinal=c.ContainerRecordOrdinal,blobOffset=c.BlobOffset,
                    blobBytes=c.Bytes,blobSha256=c.BodySha
                }).ToArray();
                nativeRows.Add(new {
                    category=kind,nativeRootOrdinal=root.Index,nativeRootName=root.Name,
                    sourceCandidates=candidates.Length,
                    differentSourceBodyHashes=hashes.Length,
                    decision,
                    selectedProofFile=selected?.SourceContainer,
                    selectedProofSha256=selected?.ContainerSha,
                    selectedProofOrdinal=selected?.ContainerRecordOrdinal,
                    selectedBlobOffset=selected?.BlobOffset,
                    selectedBlobLength=selected?.Bytes,
                    selectedBlobSha256=selected?.BodySha,
                    candidates=candidateRows,
                    nativeAztecFastFilePayloadGrafted=false,
                    xboxGameplayRuntimeValidated=false
                });
                if(selected==null)
                    deltas.Add(new {category=kind,nativeRootOrdinal=root.Index,
                        nativeRootName=root.Name,decision,candidates=candidateRows});
            }
            if(single+identical+conflicting+missing!=roots.Length ||
               roots.Length!=ExpectedRoots[kind] ||
               (single+identical+conflicting)!=ExpectedMatchingNames[kind])
                throw new InvalidDataException("Original typed source proof native-root matches drifted: "+kind);
            rootCounters.Add(new {
                category=kind,nativeRoots=roots.Length,originalPcXboxBlobRecords=bodies.Length,
                uniqueNativeRootBodies=single,identicalDuplicateNativeRootBodies=identical,
                conflictingDuplicateNativeRootBodies=conflicting,
                missingExactNameTypedSourceBodies=missing,selectedUncompressedXboxBlobBytes=selectedBytes,
                candidateBodiesPresentButNotGrafted=true
            });
            Console.WriteLine("[AZTEC-3ROOT] "+kind+": native="+roots.Length+
                ", single="+single+", byte-identical duplicates="+identical+
                ", duplicate conflicts="+conflicting+", missing bodies="+missing);
        }

        var material=counts["material"];
        var localize=counts["localize"];
        var materialRuns=material.GetProperty("proofRuns").EnumerateArray().ToArray();
        var localizeRuns=localize.GetProperty("proofRuns").EnumerateArray().ToArray();
        if(materialRuns.Length!=1 || S(materialRuns[0],"Status")!="39_GRAPH_CODEC_SOURCE_PASS" ||
           I(material,"nativeRootNamesWithMatchingSourceBodyProof")!=39 ||
           I(localize,"nativeRootNamesWithMatchingSourceBodyProof")!=177 ||
           !localizeRuns.Any(e=>S(e,"Status")=="PACKED_VALUE_POINTERS_UNREBASABLE"))
            throw new InvalidDataException("Material/Localization source-specific incomplete closure status changed.");

        var priorConflictNames=x.GetProperty("namesOfConflictingDuplicateBodies")
            .EnumerateArray().Select(e=>e.GetString()??"").ToArray();
        if(priorConflictNames.Length!=12)
            throw new InvalidDataException("Prior XAnim 12-body conflict identity fence disappeared.");

        var report=new{
            schema="WaW360.AztecThreeOwnedClassNativeRootSelection.v1",
            status="AZTEC_MODELS_FX_WEAPONS_EXACT_BODY_HASH_SELECTION_AND_GAPS_PASS_NOT_GRAFTED",
            nativeAllTypeRootCount=8509,
            nativeThreeClassRoots=ExpectedRoots.Values.Sum(),
            sourceAuthenticXboxBlobRecords=allBodies.Count,
            perTypeSummary=rootCounters,
            exactNativeRootSelection=nativeRows,
            missingOrConflictingNativeRoots=deltas,
            originalPCWorldMaterialGraphsCodecLoaded=39,
            originalWorldMaterialNativeRootProofRemaining=498,
            materialTechniqueSetStateBitsPixelDependencyClosureVerified=false,
            localizedSourceStringsRecovered=177,
            localizedNativePackedValueRebaseVerified=false,
            carriedForwardXAnimDuplicateConflicts=12,
            carriedForwardXAnimConflictNames=priorConflictNames,
            originalPcSourceProofContainersShaValidated=true,
            builtAnyFastFile=false,
            independentlyOatReadbackThisRun=false,
            xboxRuntimeResidencyProven=false,
            hardwareReady=false,
            gate11Passed=false
        };
        outputDirectory=Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var json=Path.Combine(outputDirectory,"aztec_three_class_native_root_body_resolution.json");
        await File.WriteAllTextAsync(json,JsonSerializer.Serialize(report,
            new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false),ct);

        var md=new StringBuilder();
        md.AppendLine("# Aztec native root body resolution: XModels, FX, Weapons");
        md.AppendLine();
        md.AppendLine("**Standalone Xbox source payload hash evidence only.** Current 8,509-root FastFile remains unchanged.");
        md.AppendLine();
        md.AppendLine("| Type | Xbox native roots | SHA-verified source blobs | Single match | Identical duplicates | Conflicting duplicates | No exact typed source body |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach(var row in rootCounters)
        {
            var e=JsonSerializer.SerializeToElement(row);
            md.AppendLine($"| {S(e,"category")} | {I(e,"nativeRoots")} | {I(e,"originalPcXboxBlobRecords")} | "+
                $"{I(e,"uniqueNativeRootBodies")} | {I(e,"identicalDuplicateNativeRootBodies")} | "+
                $"{I(e,"conflictingDuplicateNativeRootBodies")} | {I(e,"missingExactNameTypedSourceBodies")} |");
        }
        md.AppendLine();
        md.AppendLine("## Missing or conflicting original-PC bodies for exact native roots");
        foreach(var item in deltas)
        {
            var e=JsonSerializer.SerializeToElement(item);
            md.AppendLine("- "+S(e,"category")+" #"+I(e,"nativeRootOrdinal")+
                " "+S(e,"nativeRootName")+" — "+S(e,"decision"));
        }
        md.AppendLine();
        md.AppendLine("## Other source-verified, but NOT integration-ready classes");
        md.AppendLine("- Materials: 39 original-PC GfxWorld Material graphs loaded by native codec; 498 other native material roots not proven by this specific 39-material source proof. TechniqueSet/shader/stateBits/image child semantics still pending.");
        md.AppendLine("- Localization: 177 original-PC localized names/values recovered, packed value pointers not yet rebased into genuine native Xbox strings.");
        md.AppendLine("- XAnim: 590 unique source choices, 12 different duplicate native animation bodies still fenced.");
        md.AppendLine();
        md.AppendLine("No new FF, OAT rerun or Xbox deployment. To progress, resolve exact missing/duplicate native source ownership, then perform isolated real FF grafts and independent OAT readback.");
        var markdown=Path.Combine(outputDirectory,"aztec_three_class_native_root_body_resolution.md");
        await File.WriteAllTextAsync(markdown,md.ToString(),new UTF8Encoding(false),ct);
        return json;
    }
}
