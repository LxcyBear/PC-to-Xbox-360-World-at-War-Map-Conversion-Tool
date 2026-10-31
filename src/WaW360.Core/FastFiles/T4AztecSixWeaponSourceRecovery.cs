using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Source-owned WeaponDef discovery and genuine native Xbox owned-payload
/// proof for six Aztec missing roots. Other-map original-PC FF input is
/// forensic SOURCE evidence only, never an automatic donor for production.
/// </summary>
public static class T4AztecSixWeaponSourceRecovery
{
    private static readonly string[] TargetNames={
        "zombie_perk_bottle_doubletap","zombie_perk_bottle_jugg",
        "zombie_perk_bottle_revive","zombie_perk_bottle_sleight",
        "m7_launcher_zombie","m7_launcher_zombie_upgraded"
    };
    private sealed record SourceCandidate(string File,string Baseline,string ListFile,string Role);
    private sealed record ProvenBody(string Name,string SourceFile,string SourceSha,
        string SourceRole,string ProofPath,string ProofSha,int RecordOrdinal,
        long BlobOffset,int BlobBytes,string BlobSha);

    public static async Task<string> RunAsync(
        string sourceSearchReport,string nativeIdentityManifest,string outputDirectory,
        CancellationToken ct=default)
    {
        foreach(var f in new[]{sourceSearchReport,nativeIdentityManifest})
            if(!File.Exists(f))throw new FileNotFoundException("Six-weapon source evidence missing",f);
        using var sd=JsonDocument.Parse(await File.ReadAllTextAsync(sourceSearchReport,ct));
        using var nd=JsonDocument.Parse(await File.ReadAllTextAsync(nativeIdentityManifest,ct));
        var s=sd.RootElement;var n=nd.RootElement;
        static string St(JsonElement e,string key)=>e.GetProperty(key).GetString()??"";
        static int It(JsonElement e,string key)=>e.GetProperty(key).GetInt32();
        static bool Bo(JsonElement e,string key)=>e.GetProperty(key).GetBoolean();
        static bool Eq(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
        if(St(s,"schema")!="WaW360.AztecSixWeapon43OverrideSourceTriage.v1" ||
           St(s,"status")!="AZTEC_SIX_WEAPONS_SOURCE_SEARCH_AND_43_PC_BODY_CONFLICT_ROLES_TRIAGED_NO_SELECTION" ||
           It(s,"priorSixOriginalPcUnmatchedAndRetailUnlistedWeaponRootCount")!=6 ||
           It(s,"priorXboxRetailZoneListsAccepted")!=67 ||
           It(s,"sixWeaponSourceHintsFound")!=6 ||
           It(s,"totalConflictingSourceBodies")!=43 ||
           It(s,"sourceOwnedSixWeaponXboxSerializationProven")!=0)
        {
            throw new InvalidDataException("Six candidate weapon names/43 conflict report changed.");
        }
        if(St(n,"format")!="WaW360.T4Xenon.NativeIdentityManifest.v1" ||
           It(n,"assets")!=8509 || !Bo(n,"typeSequenceVerified"))
            throw new InvalidDataException("8509 native Xbox Aztec source identity changed.");
        var nativeWeaponNames=n.GetProperty("entries").EnumerateArray()
            .Where(x=>Eq(St(x,"OatTypeName"),"weapon"))
            .Select(x=>St(x,"Name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(nativeWeaponNames.Count!=69 || TargetNames.Any(x=>!nativeWeaponNames.Contains(x)))
            throw new InvalidDataException("Six native WeaponDef identities or total 69 native roots changed.");

        var candidates=new Dictionary<string,Dictionary<string,SourceCandidate>>(StringComparer.OrdinalIgnoreCase);
        var allRows=s.GetProperty("sixWeaponRoots").EnumerateArray().ToArray();
        if(allRows.Length!=6)throw new InvalidDataException("Six prior original-PC source roots absent.");
        foreach(var row in allRows)
        {
            var name=St(row,"name");
            if(!TargetNames.Contains(name,StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected source weapon identity: "+name);
            var byFile=new Dictionary<string,SourceCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach(var hint in row.GetProperty("potentialPcSourceFileNameOrOatCandidates").EnumerateArray())
            {
                if(St(hint,"sourceKind")!="OTHER_PC_FF_OAT_OWNED_WEAPON_NAME_ONLY")
                    continue; // reference-only/loose/IWD names are NOT owned WeaponDef evidence
                var file=Path.GetFullPath(St(hint,"sourcePcFastFile"));
                var baseline=Path.GetFullPath(St(hint,"sourceBaseline"));
                var list=Path.GetFullPath(St(hint,"sourceOatList"));
                var role=St(hint,"sourceRole");
                if(!byFile.ContainsKey(file))
                    byFile.Add(file,new SourceCandidate(file,baseline,list,role));
            }
            if(!candidates.TryAdd(name,byFile))
                throw new InvalidDataException("Repeated prior target WeaponDef name: "+name);
        }
        if(candidates.Count!=6)throw new InvalidDataException("Not six distinct source target names.");

        var unique=candidates.Values.SelectMany(x=>x.Values)
            .GroupBy(x=>x.File,StringComparer.OrdinalIgnoreCase)
            .Select(x=>x.First()).ToArray();
        outputDirectory=Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var proofDir=Path.Combine(outputDirectory,"source_pc_only_native_weapon_proofs");
        Directory.CreateDirectory(proofDir);
        var found=new List<ProvenBody>();
        var fileRuns=new List<object>();
        var failures=new List<string>();
        for(int z=0;z<unique.Length;z++)
        {
            ct.ThrowIfCancellationRequested();
            var src=unique[z];
            var original=src.File;
            var label="pc_source_"+(z+1).ToString("D2");
            var fixedProof=Path.Combine(proofDir,label+"_fixed.bin");
            var ownedProof=Path.Combine(proofDir,label+"_owned.bin");
            string? sha=null,reason=null,ownedSha=null;
            int declared=0,physical=0,serialized=0;
            try
            {
                // Validate ORIGINAL PC source provenance against the exact
                // baseline file record and OAT cached successful list fingerprint.
                if(!File.Exists(original)||!File.Exists(src.Baseline)||!File.Exists(src.ListFile))
                    throw new FileNotFoundException("Prior source FF / source baseline / exact cached OAT list absent.",original);
                using var bd=JsonDocument.Parse(await File.ReadAllTextAsync(src.Baseline,ct));
                var baseline=bd.RootElement;
                if(St(baseline,"format")!="WaW360.SecondMapBaseline.v1")
                    throw new InvalidDataException("Original-PC source baseline format changed.");
                var entries=baseline.GetProperty("fastfiles").EnumerateArray().ToArray();
                var matches=entries.Select((row,i)=>(row,i))
                    .Where(x=>Eq(Path.GetFullPath(St(x.row,"Path")),original) &&
                           Eq(St(x.row,"Role"),src.Role)).ToArray();
                if(matches.Length!=1||!Bo(matches[0].row,"Success"))
                    throw new InvalidDataException("Typed original-PC FF role is not a passing original FastFile.");
                var fi=new FileInfo(original);
                if(fi.Length!=matches[0].row.GetProperty("Bytes").GetInt64())
                    throw new InvalidDataException("Original PC FF length changed.");
                var stem=Path.GetFileNameWithoutExtension(original);
                var safe=new string(stem.Select(c=>
                    char.IsLetterOrDigit(c)||c is '_' or '.' or '-'?c:'_').ToArray());
                var expectedLog=Path.Combine(Path.GetDirectoryName(src.Baseline)!,
                    "oat_asset_lists",$"{matches[0].i+1:D2}_{safe}.list.log");
                if(!Eq(expectedLog,src.ListFile))
                    throw new InvalidDataException("Cached PC OAT --list path does not match baseline FF ordinal.");
                var meta=expectedLog.Replace(".list.log",".meta.json",StringComparison.OrdinalIgnoreCase);
                if(!File.Exists(meta))
                    throw new FileNotFoundException("Original PC OAT fingerprint record absent",meta);
                using var md=JsonDocument.Parse(await File.ReadAllTextAsync(meta,ct));
                var expectedFingerprint=fi.FullName+"|"+fi.Length+"|"+fi.LastWriteTimeUtc.Ticks;
                if(!St(md.RootElement,"Fingerprint").Equals(expectedFingerprint,StringComparison.Ordinal) ||
                   It(md.RootElement,"ExitCode")!=0)
                    throw new InvalidDataException("Original-PC OAT source listing fingerprint is stale.");
                await using(var srcStream=File.OpenRead(original))
                    sha=Convert.ToHexString(await SHA256.HashDataAsync(srcStream,ct));
                var zone=await FastFileAnalysisCache.GetZoneAsync(original,ct);
                if(zone.Platform!=WaWPlatform.PC||zone.ZoneHeader.Endianness!=Endian.Little||
                   !Eq(zone.SourceSha256,sha))
                    throw new InvalidDataException("Original-PC FF SHA or endian changed.");
                var weaponEntries=zone.Document.Assets
                    .Where(a=>Eq(a.TypeName,"weapon")).ToArray();
                declared=weaponEntries.Length;
                physical=weaponEntries.Count(a=>a.ReferenceKind is
                    T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared);
                if(physical==0)
                    throw new InvalidDataException("PC source lists WeaponDef names but has zero physical inline WeaponDef headers.");
                var fp=await T4WeaponSerializerProof.BuildProofAsync(original,fixedProof,ct);
                if(!fp.Verified||fp.DeclaredWeaponCount!=declared||fp.ResolvedWeaponCount!=declared)
                    throw new InvalidDataException("Fixed Xbox WeaponDef proof did not cover source FF XAsset table.");
                var owned=await T4WeaponVariableSerializerProof.BuildProofAsync(original,ownedProof,fixedProof,ct);
                if(!owned.Verified || owned.CoveredWeaponCount!=declared)
                    throw new InvalidDataException("Xbox WeaponDef owned-payload structural proof incomplete.");
                var index=await T4NativeProofContainerReader.IndexAsync(ownedProof,"weapon",ct);
                if(index.Records.Count!=owned.SerializedBodyCount ||
                   index.Records.Count!=owned.Assets.Count)
                    throw new InvalidDataException("Owned weapon container count mismatch.");
                ownedSha=index.Sha256;serialized=index.Records.Count;
                for(var i=0;i<owned.Assets.Count;i++)
                {
                    var asset=owned.Assets[i];
                    if(!candidates.TryGetValue(asset.Name,out var ownFiles) ||
                       !ownFiles.ContainsKey(original))continue;
                    if(index.Records[i].Name is null || !Eq(index.Records[i].Name!,asset.Name))
                        throw new InvalidDataException("Owned Xbox WeaponDef record/name changed.");
                    var blob=await T4NativeProofContainerReader.ReadBlobAsync(index.Records[i],ct);
                    if(!T4WeaponVariableSerializerProof.TryValidateSelfContainedXboxBlob(
                        blob,asset.Name,out var error))
                        throw new InvalidDataException("Converted Xbox WeaponDef independently rejected: "+error);
                    found.Add(new ProvenBody(asset.Name,original,sha,src.Role,ownedProof,
                        index.Sha256,i,index.Records[i].ContainerBlobOffset,
                        blob.Length,Convert.ToHexString(SHA256.HashData(blob))));
                }
            }
            catch(Exception ex) when(ex is not OperationCanceledException and not OutOfMemoryException)
            {
                reason=ex.GetType().Name+": "+ex.Message;
                failures.Add(Path.GetFileName(original)+": "+reason);
            }
            fileRuns.Add(new{sourcePcFastFile=original,role=src.Role,
                sourceSha256=sha,sourceWeaponXAssets=declared,
                sourcePhysicalWeaponHeaders=physical,
                convertedWeaponBodies=serialized,verifiedOwnedContainerSha256=ownedSha,
                proofContainer=reason is null?ownedProof:null,
                status=reason is null?"SOURCE_XBOX_WEAPON_OWNED_PROOF_PASS":"SOURCE_OWNED_WEAPON_PROOF_BLOCKED",
                blocker=reason});
            Console.WriteLine("[AZTEC-SIX-RECOVERY] "+Path.GetFileName(original)+
                ": source="+declared+" physical="+physical+
                " owned-native="+serialized+" "+(reason is null?"PASS":"BLOCKED "+reason));
        }
        var rows=new List<object>();
        var full=0;var conflicted=0;
        foreach(var name in TargetNames)
        {
            var alternatives=found.Where(x=>Eq(x.Name,name))
                .GroupBy(x=>x.SourceFile+"|"+x.RecordOrdinal,StringComparer.OrdinalIgnoreCase)
                .Select(x=>x.First()).ToArray();
            var hashes=alternatives.Select(x=>x.BlobSha)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var uniqueHash=hashes.Length==1 && alternatives.Length>0;
            if(uniqueHash)full++;
            if(hashes.Length>1)conflicted++;
            rows.Add(new{
                nativeWeaponName=name,
                originalCachedOwnedPcFfNameHints=candidates[name].Count,
                exactTypedSourceNativeXboxWeaponDefBodies=alternatives.Length,
                differentConvertedXboxOwnedWeaponDefBodySha256=hashes.Length,
                decision=alternatives.Length==0?"NO_AUTHENTIC_PC_OWNED_BODY_VERIFIED":
                    uniqueHash?"UNAMBIGUOUS_NATIVE_SOURCE_BODY_PROOF_NOT_GRAFTED":
                    "MULTIPLE_DIFFERING_SOURCE_XBOX_WEAPON_BODIES_NO_SELECTION",
                sourceProvenance=alternatives,
                perRootNativeFFBodyGrafted=false,
                actualRuntimeWeaponBehaviorVerified=false
            });
            Console.WriteLine("[AZTEC-SIX-RECOVERY] "+name+
                ": original-PC OAT body name hints="+candidates[name].Count+
                ", real Xbox owned source bodies="+alternatives.Length+
                ", hashes="+hashes.Length+
                (uniqueHash?" UNAMBIGUOUS":" NOT SELECTED"));
        }
        var result=new{
            schema="WaW360.AztecSixWeaponRealPcSourceOwnedXboxProof.v1",
            status=full==6?"AZTEC_SIX_REAL_NATIVE_WEAPON_SOURCE_BODIES_UNAMBIGUOUS_NOT_GRAFTED":
                "AZTEC_SIX_PC_WEAPON_SOURCE_PROOF_PARTIAL_OR_CONFLICTING",
            sourcePcFastfilesWithOwnedNames=unique.Length,
            originalSixNativeWeaponRootNames=6,
            sixWithProvenIdenticalOrSingleConvertedXboxBody=full,
            sixWithConflictingDifferentConvertedWeaponBodies=conflicted,
            sixWithoutUnambiguousConvertedXboxBody=6-full,
            actualXboxWeaponDefSourceBodyProof=rows,
            sourcePcFfRuns=fileRuns,sourceProofFailures=failures,
            notSourceOwnedByAztecMapAutomatically=true,
            noXboxRetailDonorBodiesCopied=true,
            noAztecFastFileBuiltOrModified=true,
            existing43SourceBodyConflictsNotResolved=true,
            hardwareReady=false,gate11Closed=false
        };
        var output=Path.Combine(outputDirectory,"aztec_six_real_pc_weapon_source_body_proof.json");
        await File.WriteAllTextAsync(output,JsonSerializer.Serialize(result,
            new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false),ct);
        var md=new StringBuilder();
        md.AppendLine("# Aztec six missing WeaponDef roots - PC origin owned-body conversion");
        md.AppendLine();
        md.AppendLine($"Source FF candidates with owned OAT names: {unique.Length}; verified native Xbox bodies for {full}/6 roots.");
        md.AppendLine($"Different-converted-body conflicts: {conflicted}; no native FF graft or Xbox runtime validation.");
        md.AppendLine();
        foreach(var row in rows)
        {
            var e=JsonSerializer.SerializeToElement(row);
            md.AppendLine("- "+St(e,"nativeWeaponName")+": "+St(e,"decision")+
                "; verified Xbox owned body candidates="+It(e,"exactTypedSourceNativeXboxWeaponDefBodies"));
        }
        foreach(var error in failures)md.AppendLine("BLOCKER: "+error);
        md.AppendLine();
        md.AppendLine("Other-map original PC FastFiles are forensic provenance evidence, NOT authorized production donor bytes. If bodies are proven, next step is original Aztec source ownership/override equivalence plus isolated FF graph integration, OAT and dependency tests. All 43 previous conflicts remain unresolved.");
        var txt=Path.Combine(outputDirectory,"aztec_six_real_pc_weapon_source_body_proof.md");
        await File.WriteAllTextAsync(txt,md.ToString(),new UTF8Encoding(false),ct);
        return output;
    }
}
