using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Read-only verified Aztec native zone size and source-root coverage.
/// Block sizes are shared arena capacities, NOT per-type payload byte counts.
/// </summary>
public static class T4AztecNativeSizeAudit
{
    private const string RetailSha="E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F";
    private const string CandidateSha="F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6";
    private const int Roots=8509;

    public static async Task<string> RunAsync(
        string retailFile,string candidateFile,string identityFile,
        string emitterFile,string independentProofFile,string outputDir,
        CancellationToken ct=default)
    {
        string[] inputs={retailFile,candidateFile,identityFile,emitterFile,independentProofFile};
        for(int i=0;i<inputs.Length;i++)
        {
            inputs[i]=Path.GetFullPath(inputs[i]);
            if(!File.Exists(inputs[i]))throw new FileNotFoundException("Aztec size proof missing",inputs[i]);
        }
        retailFile=inputs[0];candidateFile=inputs[1];identityFile=inputs[2];
        emitterFile=inputs[3];independentProofFile=inputs[4];
        if(string.Equals(retailFile,candidateFile,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original and converted FF must not be the same file.");
        using var idDoc=JsonDocument.Parse(await File.ReadAllTextAsync(identityFile,ct));
        using var emitDoc=JsonDocument.Parse(await File.ReadAllTextAsync(emitterFile,ct));
        using var proofDoc=JsonDocument.Parse(await File.ReadAllTextAsync(independentProofFile,ct));
        var id=idDoc.RootElement;var emit=emitDoc.RootElement;var proof=proofDoc.RootElement;
        static int Int(JsonElement e,string k)=>e.GetProperty(k).GetInt32();
        static long Long(JsonElement e,string k)=>e.GetProperty(k).GetInt64();
        static bool Yes(JsonElement e,string k)=>e.GetProperty(k).GetBoolean();
        static string Str(JsonElement e,string k)=>e.GetProperty(k).GetString()??"";
        static bool Eq(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);

        if(Str(proof,"schema")!="WaW360.AztecGate11SixImageRootNameAliasReadback.v1" ||
           Str(proof,"status")!="AZTEC_GATE11_SIX_GFXIMAGE_NAME_ONLY_ROOT_POINTERS_NATIVE_OAT_PASS_PIXEL_DATA_MISSING" ||
           !Eq(Str(proof,"newNativeFfSha256"),CandidateSha) ||
           !Eq(Path.GetFullPath(Str(proof,"newNativeFf")),candidateFile) ||
           Long(proof,"newNativeFfBytes")!=4510695L ||
           Int(proof,"sourceNamedGfxImagePointersResolved")!=6 ||
           !Yes(proof,"previous11Cells740Aabb14974IndexesStillMatch") ||
           !Yes(proof,"previous4LightGridGroupsAnd1903ShadowRefsStillMatch") ||
           !Yes(proof,"nativeEmptyStreamPointersStillMatch") ||
           Int(proof,"realOriginalPcGeneratedImagePixelsProven")!=0 ||
           Yes(proof,"fullGate11Passed") || Yes(proof,"xboxHardwareReady"))
            throw new InvalidDataException("Independent proof of current 4,510,695-byte source-only FF changed.");
        if(Int(id,"Assets")!=Roots || !Yes(id,"TypeSequenceVerified") ||
           Int(emit,"NativeRoots")!=Roots ||
           !Eq(Path.GetFullPath(Str(emit,"OutputFastFile")),candidateFile) ||
           !Yes(emit,"ExactIdentitySequence") ||
           !Yes(emit,"OatZeroWarningsZeroErrors") ||
           !Yes(emit,"LayoutReplayExact") ||
           !Yes(emit,"ValidationPass") ||
           !Yes(emit,"ValidationOnly") ||
           Yes(emit,"ReadyForHardware") ||
           !Yes(emit,"OriginalPcGfxWorldCoreEmitted") ||
           Yes(emit,"OriginalPcGfxWorldFullDpvsGraphComplete"))
            throw new InvalidDataException("Stage4E validation-only source/identity manifest changed.");

        var retail=await FastFileAnalysisCache.GetZoneAsync(retailFile,ct);
        var current=await FastFileAnalysisCache.GetZoneAsync(candidateFile,ct);
        long rb=new FileInfo(retailFile).Length, cb=new FileInfo(candidateFile).Length;
        if(retail.Platform!=WaWPlatform.Xbox360 ||
           current.Platform!=WaWPlatform.Xbox360 ||
           !Eq(retail.SourceSha256,RetailSha) ||
           !Eq(current.SourceSha256,CandidateSha) ||
           rb!=82739469L || cb!=4510695L ||
           retail.Document.AssetCount!=Roots ||
           current.Document.AssetCount!=Roots ||
           current.Bytes.LongLength!=Long(emit,"ZoneBytes") ||
           retail.ZoneHeader.BlockSizes.Count!=7 ||
           current.ZoneHeader.BlockSizes.Count!=7)
            throw new InvalidDataException("Retail/candidate SHA, zone bytes, platform or 8509-root counts do not match.");

        var byType=new SortedDictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var entries=id.GetProperty("Entries").EnumerateArray().ToArray();
        if(entries.Length!=Roots)throw new InvalidDataException("Identity entry count mismatch.");
        for(int i=0;i<Roots;i++)
        {
            var e=entries[i];
            if(Int(e,"Index")!=i ||
               Int(e,"TypeId")!=retail.Document.Assets[i].TypeId ||
               Int(e,"TypeId")!=current.Document.Assets[i].TypeId)
                throw new InvalidDataException("Native root ordinal/type mismatch at "+i);
            var kind=Str(e,"OatTypeName").Trim().ToLowerInvariant();
            if(kind.Length==0)throw new InvalidDataException("OAT asset type missing at "+i);
            byType[kind]=byType.GetValueOrDefault(kind)+1;
        }
        var pc=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase)
        {
            ["image"]=Int(emit,"RealImageRoots"),
            ["xmodel"]=Int(emit,"RealXModelRoots"),
            ["material"]=Int(emit,"RealMaterialRoots"),
            ["rawfile"]=Int(emit,"RealRawFileRoots"),
            ["mapents"]=Int(emit,"RealMapEntsRoots"),
            ["comworld"]=Int(emit,"RealComWorldRoots")
        };
        var expected=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase)
        {
            ["image"]=2,["xmodel"]=28,["material"]=39,
            ["rawfile"]=114,["mapents"]=1,["comworld"]=1
        };
        foreach(var pair in expected)
            if(pc[pair.Key]!=pair.Value)
                throw new InvalidDataException("Unexpected original-PC asset count for "+pair.Key);
        var graph=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase)
        {
            ["techset"]=Int(emit,"RendererTechniqueSets"),
            ["pixelshader"]=Int(emit,"RendererPixelShaders")
        };
        if(Int(emit,"OriginalPcGfxWorldGate11Cells")!=11 ||
           Int(emit,"OriginalPcGfxWorldGate11AabbTrees")!=740 ||
           Int(emit,"OriginalPcGfxWorldGate10DrawRecords")!=1903 ||
           Int(emit,"OriginalPcGfxWorldGate10UnverifiedStockDraws")!=1)
            throw new InvalidDataException("Source world structure/count proof changed.");

        var ledger=new List<(string Kind,int Count,int Pc,int Graph,int Partial,int Shell)>();
        foreach(var (kind,count) in byType)
        {
            int own=pc.GetValueOrDefault(kind),replay=graph.GetValueOrDefault(kind);
            int partial=kind=="gfxworld"?1:0;
            int shell=count-own-replay-partial;
            if(shell<0)throw new InvalidDataException("Claimed bodies exceed "+kind+" roots.");
            ledger.Add((kind,count,own,replay,partial,shell));
        }
        int ownTotal=ledger.Sum(x=>x.Pc),graphTotal=ledger.Sum(x=>x.Graph);
        int partialTotal=ledger.Sum(x=>x.Partial),shellTotal=ledger.Sum(x=>x.Shell);
        if(ownTotal+graphTotal+partialTotal+shellTotal!=Roots)
            throw new InvalidDataException("Native type-root provenance ledger doesn't reconcile.");

        string[] blockNames={"TEMP","RUNTIME","LARGE_RUNTIME","PHYSICAL_RUNTIME",
                             "VIRTUAL","LARGE","PHYSICAL"};
        var blockLedger=blockNames.Select((name,i)=>new {
            name,
            retailCapacityBytes=retail.ZoneHeader.BlockSizes[i],
            candidateCapacityBytes=current.ZoneHeader.BlockSizes[i],
            differenceBytes=(long)retail.ZoneHeader.BlockSizes[i]-current.ZoneHeader.BlockSizes[i],
            notActualPerAssetPayloadBytes=true
        }).ToArray();

        long rd=retail.Bytes.LongLength,cd=current.Bytes.LongLength;
        long rr=(long)retail.ZoneHeader.LargeRuntimeBlockSize+retail.ZoneHeader.PhysicalRuntimeBlockSize;
        long cr=(long)current.ZoneHeader.LargeRuntimeBlockSize+current.ZoneHeader.PhysicalRuntimeBlockSize;
        var json=new {
            schema="WaW360.AztecNativeFfSizeAndSourceCoverage.v1",
            status="AZTEC_NATIVE_FF_SIZE_AUDIT_PASS_NOT_HARDWARE_READY",
            retail=new {path=retailFile,sha256=retail.SourceSha256,
                compressedBytes=rb,decompressedBytes=rd,
                mainSerializedZoneBytes=retail.ZoneHeader.ZoneSize,
                appendedRuntimeHeaderBytes=rr,assetCount=retail.Document.AssetCount,
                scriptStringCount=retail.Document.ScriptStringCount,
                rawZoneSizeMinusDeclaredBytes=rd-(36L+retail.ZoneHeader.ZoneSize+rr)},
            converted=new {path=candidateFile,sha256=current.SourceSha256,
                compressedBytes=cb,decompressedBytes=cd,
                mainSerializedZoneBytes=current.ZoneHeader.ZoneSize,
                appendedRuntimeHeaderBytes=cr,assetCount=current.Document.AssetCount,
                scriptStringCount=current.Document.ScriptStringCount,
                rawZoneSizeMinusDeclaredBytes=cd-(36L+current.ZoneHeader.ZoneSize+cr)},
            delta=new {
                compressedBytes=rb-cb,
                decompressedBytes=rd-cd,
                mainSerializedBytes=(long)retail.ZoneHeader.ZoneSize-current.ZoneHeader.ZoneSize,
                appendedRuntimeBytes=rr-cr,
                compressedSizeRatio=rb/(double)cb,
                NOTPerTypeMissingByteMass=true
            },
            rootCoverage=new {
                total=Roots,originalPcSourceBodies=ownTotal,
                nativeRendererGraphReplays=graphTotal,
                partiallyConvertedGfxWorldRoots=partialTotal,
                nameShellOrUnverifiedRoots=shellTotal,
                representsRootCountsNotByteContributions=true
            },
            blocks=blockLedger,
            types=ledger.OrderByDescending(x=>x.Shell).ThenBy(x=>x.Kind).Select(x=>new {
                name=x.Kind,nativeRoots=x.Count,originalPcSourceBodies=x.Pc,
                nativeRendererGraphReplays=x.Graph,partiallyConverted=x.Partial,
                nameShellOrUnverified=x.Shell,exactBytesInThisTypeNotMeasured=true
            }).ToArray(),
            sourceWorld=new {
                cells=11,cellAabbTrees=740,cellModelIndexes=14974,staticDraws=1903,
                sourceLightGridBytes=4822860L,sourceWaterBytes=2097408L,
                sourceShadowModelReferences=1903,
                namedGfxImagePointersResolved=6,generatedImagePixelBodiesProven=0,
                nativeStreamingTrees=0,nativeStreamingLeafRefs=0
            },
            warning="Native XFile block sizes are shared allocation capacities. Neither an asset root/type nor a block capacity encodes the exact image/model/sound payload byte mass. The compressed difference is NOT a byte-by-type deficit. Many native roots are name shells; no retail asset bytes are copied to the new FF.",
            highestPriority="Independently source-back remaining image pixels and GfxImage loadDefs; audit XAnim/FX/Sound/Weapon/StringTable bodies; finish XModel/stock runtime and Material stateBits; instrument loader/emitter per-root spans for actual byte-by-type mass.",
            retailPayloadCopied=false,hardwareReady=false,gate11Closed=false
        };
        outputDir=Path.GetFullPath(outputDir);
        Directory.CreateDirectory(outputDir);
        var jsonFile=Path.Combine(outputDir,"aztec_native_ff_size_and_source_ledger.json");
        await File.WriteAllTextAsync(jsonFile,JsonSerializer.Serialize(json,
            new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false),ct);

        var md=new StringBuilder();
        md.AppendLine("# Aztec native FF size vs source-root coverage");
        md.AppendLine();
        md.AppendLine("Read-only SHA-pinned original Xbox and independently OAT-passing 8,509-root source diagnostic.");
        md.AppendLine();
        md.AppendLine("| Metric | Original Xbox | Converted | Original minus converted |");
        md.AppendLine("|---|---:|---:|---:|");
        md.AppendLine($"| Compressed FF bytes | {rb:N0} | {cb:N0} | {rb-cb:N0} |");
        md.AppendLine($"| Decompressed zone bytes | {rd:N0} | {cd:N0} | {rd-cd:N0} |");
        md.AppendLine($"| Main serialized zone bytes | {retail.ZoneHeader.ZoneSize:N0} | {current.ZoneHeader.ZoneSize:N0} | {(long)retail.ZoneHeader.ZoneSize-current.ZoneHeader.ZoneSize:N0} |");
        md.AppendLine($"| Appended runtime arena bytes | {rr:N0} | {cr:N0} | {rr-cr:N0} |");
        md.AppendLine($"| Native XAsset root count | {Roots:N0} | {Roots:N0} | 0 |");
        md.AppendLine();
        md.AppendLine("## Seven native block allocation capacities (NOT used bytes by asset type)");
        md.AppendLine();
        md.AppendLine("| Block | Xbox bytes | Candidate bytes | Difference |");
        md.AppendLine("|---|---:|---:|---:|");
        for(int i=0;i<7;i++)
            md.AppendLine($"| {blockNames[i]} | {retail.ZoneHeader.BlockSizes[i]:N0} | {current.ZoneHeader.BlockSizes[i]:N0} | {(long)retail.ZoneHeader.BlockSizes[i]-current.ZoneHeader.BlockSizes[i]:N0} |");
        md.AppendLine();
        md.AppendLine("## Type-by-type XAsset roots and source-body status");
        md.AppendLine();
        md.AppendLine("| Type | Roots | PC source bodies | Renderer graph replay | Partial world | Shell/unverified |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach(var x in ledger.OrderByDescending(x=>x.Shell).ThenBy(x=>x.Kind))
            md.AppendLine($"| {x.Kind} | {x.Count:N0} | {x.Pc:N0} | {x.Graph:N0} | {x.Partial:N0} | {x.Shell:N0} |");
        md.AppendLine($"| **TOTAL** | **{Roots:N0}** | **{ownTotal:N0}** | **{graphTotal:N0}** | **{partialTotal:N0}** | **{shellTotal:N0}** |");
        md.AppendLine();
        md.AppendLine("Source counts show that genuine PC-derived bodies were emitted, not that every associated runtime semantic is complete. Renderer graph replay is not the same as PC-owned shader provenance. GfxWorld is partially reconstructed.");
        md.AppendLine();
        md.AppendLine("Important: the six star-prefixed reflection/lightmap image roots have working OAT name pointers but no verified pixel bodies. The only genuine source pixel image bodies in this candidate number two.");
        md.AppendLine();
        md.AppendLine("We cannot calculate honest per-type missing bytes from root counts or seven XFile block capacities; these are shared arenas, while the compressed FF size also depends on entropy and encoding. Exact per-asset byte-mass attribution needs instrumentation of the native load/emission traversal.");
        md.AppendLine();
        md.AppendLine("Next: recover original PC source pixel/loadDef/mip graphs; audit unconverted xanim/fx/weapon/sound/stringtable roots; finish XModels and stock runtime, Material stateBits and world DPVS. Never pad FF size, transplant original Xbox donor bodies, or claim Xbox readiness from an OAT-only size audit.");
        var mdFile=Path.Combine(outputDir,"aztec_native_ff_size_and_source_ledger.md");
        await File.WriteAllTextAsync(mdFile,md.ToString(),new UTF8Encoding(false),ct);
        return mdFile;
    }
}
