using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Converts the 39 independently OAT-loaded ORIGINAL-PC GfxWorld Material
/// headers/tables to the ABI/endian expected by the existing T4Xenon
/// MaterialGraphCodec. Does not claim valid shader/image dependencies or FF.
/// PC: Material 112B, MaterialInfo 32B, stateBitsEntry[59].
/// Xenon: Material 96B, MaterialInfo 24B, stateBitsEntry[51].
/// </summary>
public static class T4AztecPcXenonMaterial39SourceBridge
{
    private const int MaterialCount = 39;
    private const string SourceFormat="WaW360.T4PcGfxWorld39MaterialSourceCorpus.v1";
    private const string GraphFormat="WaW360.T4Xenon.MaterialRelocatableGraph.v1";

    public static async Task<AztecPc39XenonMaterialCandidateReport> BuildAsync(
        string sourceCorpusSummary, string pcMaterialWorklist,
        string nativeIdentity, string outputDirectory, CancellationToken ct=default)
    {
        using var sd=JsonDocument.Parse(await File.ReadAllTextAsync(sourceCorpusSummary,ct));
        using var wd=JsonDocument.Parse(await File.ReadAllTextAsync(pcMaterialWorklist,ct));
        using var nd=JsonDocument.Parse(await File.ReadAllTextAsync(nativeIdentity,ct));
        var summary=sd.RootElement;
        var work=wd.RootElement;
        var native=nd.RootElement;
        if (summary.GetProperty("schema").GetString()!="WaW360.AztecPc39WorldMaterialSourceCorpusReport.v1" ||
            summary.GetProperty("status").GetString()!="AZTEC_PC_OAT_39_MATERIAL_HEADERS_AND_CHILD_TABLES_SOURCE_EXPORTED" ||
            summary.GetProperty("typedWorldMaterialOwners").GetInt32()!=39 ||
            summary.GetProperty("sourceHeaders").GetInt32()!=39 ||
            summary.GetProperty("textures").GetInt32()!=87 ||
            summary.GetProperty("constants").GetInt32()!=93 ||
            summary.GetProperty("stateBits").GetInt32()!=192 ||
            summary.GetProperty("waterNestedGraphsPending").GetInt32()!=0 ||
            summary.GetProperty("realXenonWorldMaterialGraphBodies").GetInt32()!=0 ||
            summary.GetProperty("hardwareReady").GetBoolean() ||
            work.GetProperty("Status").GetString()!="AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY" ||
            work.GetProperty("VerifiedTypedPcSurfaceIdentities").GetInt32()!=735 ||
            work.GetProperty("UniqueGoldenXboxNativeMaterialRootNames").GetInt32()!=39 ||
            work.GetProperty("XenonSurfaceMaterialPointersWritten").GetInt32()!=0 ||
            work.GetProperty("XboxHardwareReady").GetBoolean() ||
            native.GetProperty("format").GetString()!="WaW360.T4Xenon.NativeIdentityManifest.v1" ||
            native.GetProperty("assets").GetInt32()!=8509 ||
            !native.GetProperty("typeSequenceVerified").GetBoolean())
            throw new InvalidDataException("Requires SHA-verified PC Material source corpus, 735 typed owners and native 8509-root identity.");

        var pcFile=summary.GetProperty("originalPcFastFile").GetString()!;
        if (!File.Exists(pcFile) ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(pcFile,ct))),
                summary.GetProperty("sourceFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC FastFile SHA changed since OAT source corpus extraction.");

        var manifestPath=summary.GetProperty("sourceMaterialManifest").GetString()!;
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath,ct));
        var source=doc.RootElement;
        var abi=source.GetProperty("abi");
        if (source.GetProperty("format").GetString()!=SourceFormat ||
            source.GetProperty("sourcePlatform").GetString()!="PC_LOADED_T4_X86_LITTLE_ENDIAN" ||
            !source.GetProperty("abiVerified").GetBoolean() ||
            abi.GetProperty("material").GetInt32()!=112 ||
            abi.GetProperty("textureDef").GetInt32()!=16 ||
            abi.GetProperty("constantDef").GetInt32()!=32 ||
            abi.GetProperty("stateBits").GetInt32()!=8 ||
            source.GetProperty("exportedMaterialHeaders").GetInt32()!=39 ||
            source.GetProperty("totalTextureDescriptors").GetInt32()!=87 ||
            source.GetProperty("totalConstantRecords").GetInt32()!=93 ||
            source.GetProperty("totalStateBitsRecords").GetInt32()!=192 ||
            source.GetProperty("waterTexturesNeedingSeparateConversion").GetInt32()!=0)
            throw new InvalidDataException("Original PC OAT material source graph ABI or child inventory changed.");

        var sourceRows=source.GetProperty("materials").EnumerateArray().ToArray();
        var rootRows=native.GetProperty("entries").EnumerateArray().ToArray();
        var allIntents=work.GetProperty("RelocationIntents").EnumerateArray().ToArray();
        if (sourceRows.Length!=39 || rootRows.Length!=8509 || allIntents.Length!=735)
            throw new InvalidDataException("39 source material / 735 typed surfaces / 8509 Xbox roots coverage changed.");

        var rootUsage=new Dictionary<int,(int SurfaceCount,int Triangles,string Name)>();
        foreach (var intent in allIntents)
        {
            int id=intent.GetProperty("UniqueMatchingXboxNativeRootIndex").GetInt32();
            var name=intent.GetProperty("VerifiedPcMaterialName").GetString()!;
            if (id<0 || id>=rootRows.Length ||
                rootRows[id].GetProperty("TypeId").GetInt32()!=6 ||
                !string.Equals(name,rootRows[id].GetProperty("Name").GetString(),StringComparison.Ordinal))
                throw new InvalidDataException($"Original PC surface Material identity does not match native material root #{id}.");
            if (rootUsage.TryGetValue(id,out var prior))
            {
                if (prior.Name!=name) throw new InvalidDataException("Material root collides between PC world surface names.");
                rootUsage[id]=(prior.SurfaceCount+1,
                    prior.Triangles+intent.GetProperty("TriangleCount").GetInt32(),name);
            }
            else rootUsage.Add(id,(1,intent.GetProperty("TriangleCount").GetInt32(),name));
        }
        if (rootUsage.Count!=39 || rootUsage.Sum(x=>x.Value.SurfaceCount)!=735 ||
            rootUsage.Sum(x=>x.Value.Triangles)!=19551)
            throw new InvalidDataException("Source Material roots do not cover all 735 surfaces and 19551 triangles.");

        var sourceRoot=Path.GetFullPath(summary.GetProperty("sourceCorpusRoot").GetString()!);
        var dir=Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(dir);
        var convertedRoot=Path.Combine(dir,"xenon_material_graph_blobs");
        var graphDir=Path.Combine(dir,"xenon_material_graphs");
        Directory.CreateDirectory(convertedRoot);
        Directory.CreateDirectory(graphDir);

        var groups=new List<AztecPcXenonMaterialCandidateEntry>(39);
        int texSum=0,constSum=0,stateSum=0,unsafeTailCount=0;
        int absentDeps=0,ambiguousDeps=0,uniqueDeps=0;
        int noSourceTechniques=0,materialRoots=0;
        var seen=new HashSet<int>();
        foreach (var row in sourceRows)
        {
            ct.ThrowIfCancellationRequested();
            int ordinal=row.GetProperty("ordinal").GetInt32();
            if (ordinal!=groups.Count || !row.GetProperty("completeSourceTablesExported").GetBoolean() ||
                !row.GetProperty("sourceHeaderLoaded").GetBoolean() ||
                row.GetProperty("unserializedWaterTextures").GetInt32()!=0)
                throw new InvalidDataException($"Incomplete PC Material header/tables at source #{ordinal}.");
            var name=row.GetProperty("name").GetString()!;
            var rootIdx=rootUsage.Single(x=>x.Value.Name==name).Key;
            if (!seen.Add(rootIdx)) throw new InvalidDataException("A PC material maps to duplicate Xbox world root.");
            var count=row.GetProperty("counts");
            int nt=count.GetProperty("textures").GetInt32();
            int nc=count.GetProperty("constants").GetInt32();
            int ns=count.GetProperty("stateBits").GetInt32();
            if(nt<0 || nc<0 || ns<0 || nt>64 || nc>64 || ns>64)
                throw new InvalidDataException("PC material descriptor count outside OAT verified bounds.");

            byte[] pc=await ReadSourceBlob(row.GetProperty("headerFile").GetString()!,112,sourceRoot,ct);
            byte[] texture=await ReadSourceBlob(row.GetProperty("textureTableFile").GetString()!,nt*16,sourceRoot,ct);
            byte[] constants=await ReadSourceBlob(row.GetProperty("constantTableFile").GetString()!,nc*32,sourceRoot,ct);
            byte[] state=await ReadSourceBlob(row.GetProperty("stateBitsTableFile").GetString()!,ns*8,sourceRoot,ct);

            // PC MaterialInfo 32 bytes: name[0..3], flags[4..7],
            // drawSurf packed uint64 [8..15], surface bits uint32 [16..19],
            // layered bits [20..23], hashIndex and padding [24..31].
            // Xenon MaterialInfo is 24B, no hashIndex.
            // PC stateBitsEntry[59] at +32; Xenon [51] at +24.
            // PC counts at +91..95, Xenon at +75..79.
            // PC graph ptrs +96..111, Xenon +80..95.
            if (BinaryPrimitives.ReadUInt32LittleEndian(pc.AsSpan(0,4))!=0xFFFFFFFFu ||
                BinaryPrimitives.ReadUInt32LittleEndian(pc.AsSpan(100,4)) != (nt>0?0xFFFFFFFFu:0u) ||
                BinaryPrimitives.ReadUInt32LittleEndian(pc.AsSpan(104,4)) != (nc>0?0xFFFFFFFFu:0u) ||
                BinaryPrimitives.ReadUInt32LittleEndian(pc.AsSpan(108,4)) != (ns>0?0xFFFFFFFFu:0u) ||
                pc[91]!=nt || pc[92]!=nc || pc[93]!=ns)
                throw new InvalidDataException($"Material #{ordinal} header pointer/count ABI does not match source OAT normalized 112B layout.");

            var header=new byte[96];
            Put32(header,0,0xFFFFFFFFu);
            pc.AsSpan(4,4).CopyTo(header.AsSpan(4,4));
            BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(8,8),
                BinaryPrimitives.ReadUInt64LittleEndian(pc.AsSpan(8,8)));
            Put32(header,16,Read32(pc,16));
            Put32(header,20,Read32(pc,20));
            pc.AsSpan(32,51).CopyTo(header.AsSpan(24,51));
            pc.AsSpan(91,5).CopyTo(header.AsSpan(75,5));
            Put32(header,80,0u); // techniqueset reference resolved by existing graph emitter
            Put32(header,84,nt>0?0xFFFFFFFFu:0u);
            Put32(header,88,nc>0?0xFFFFFFFFu:0u);
            Put32(header,92,ns>0?0xFFFFFFFFu:0u);
            var clipped=pc.AsSpan(83,8).ToArray();
            var activeTail=clipped.Any(x=>x!=0xFF);
            if (activeTail) unsafeTailCount++;

            var convertedTex=new byte[texture.Length];
            var textureRows=row.GetProperty("textures").EnumerateArray().ToArray();
            if (textureRows.Length!=nt)
                throw new InvalidDataException("Original-PC OAT source MaterialTexture descriptor count mismatch.");
            var dependencies=new List<AztecPcXenonMaterialDependency>();
            var materialTech=row.GetProperty("techniqueSet");
            var technique=materialTech.ValueKind==JsonValueKind.Null?null:materialTech.GetString();
            if (string.IsNullOrWhiteSpace(technique))
                noSourceTechniques++;
            else dependencies.Add(ResolveDependency("techset",technique!,rootRows));
            for (int i=0;i<nt;i++)
            {
                var from=i*16;
                var sourceTex=textureRows[i];
                if (sourceTex.GetProperty("index").GetInt32()!=i ||
                    sourceTex.GetProperty("nameHash").GetUInt32()!=Read32(texture,from) ||
                    sourceTex.GetProperty("semantic").GetInt32()!=texture[from+7] ||
                    sourceTex.GetProperty("requiresWaterConversion").GetBoolean() ||
                    BinaryPrimitives.ReadUInt32LittleEndian(texture.AsSpan(from+12,4))!=0u)
                    throw new InvalidDataException($"Source PC texture descriptor/normalized pointer mismatch at Material #{ordinal}, texture#{i}.");
                Put32(convertedTex,from,Read32(texture,from));
                texture.AsSpan(from+4,8).CopyTo(convertedTex.AsSpan(from+4,8));
                Put32(convertedTex,from+12,0);
                var img=sourceTex.GetProperty("image");
                if(img.ValueKind!=JsonValueKind.Null &&
                   !string.IsNullOrWhiteSpace(img.GetString()))
                    dependencies.Add(ResolveDependency("image",img.GetString()!,rootRows));
            }

            var convertedConst=new byte[constants.Length];
            for(int i=0;i<nc;i++)
            {
                int at=i*32;
                Put32(convertedConst,at,Read32(constants,at));
                constants.AsSpan(at+4,12).CopyTo(convertedConst.AsSpan(at+4,12));
                for(int k=0;k<4;k++)
                    Put32(convertedConst,at+16+k*4,Read32(constants,at+16+k*4));
            }
            var convertedState=new byte[state.Length];
            for(int i=0;i<ns;i++)
            {
                Put32(convertedState,i*8,Read32(state,i*8));
                Put32(convertedState,i*8+4,Read32(state,i*8+4));
            }
            if(dependencies.Any(x=>x.State=="MISSING"))absentDeps+=dependencies.Count(x=>x.State=="MISSING");
            ambiguousDeps+=dependencies.Count(x=>x.State=="AMBIGUOUS");
            uniqueDeps+=dependencies.Count(x=>x.State=="UNIQUE_NATIVE_ROOT");
            var ownedDir=Path.Combine(convertedRoot,$"material_{ordinal:D2}");
            Directory.CreateDirectory(ownedDir);
            var rootRel=$"xenon_material_graph_blobs/material_{ordinal:D2}/root.bin";
            var texRel=$"xenon_material_graph_blobs/material_{ordinal:D2}/textures.bin";
            var constRel=$"xenon_material_graph_blobs/material_{ordinal:D2}/constants.bin";
            var stateRel=$"xenon_material_graph_blobs/material_{ordinal:D2}/stateBits.bin";
            await File.WriteAllBytesAsync(Path.Combine(ownedDir,"root.bin"),header,ct);
            await File.WriteAllBytesAsync(Path.Combine(ownedDir,"textures.bin"),convertedTex,ct);
            await File.WriteAllBytesAsync(Path.Combine(ownedDir,"constants.bin"),convertedConst,ct);
            await File.WriteAllBytesAsync(Path.Combine(ownedDir,"stateBits.bin"),convertedState,ct);

            // Matches the existing T4XenonMaterialGraphCodec input JSON
            // WITHOUT claiming dependencies or actual FF emission passed.
            var graph=new
            {
                format=GraphFormat,name,
                abi=new {material=96,textureDef=16,constantDef=32,stateBits=8,water=0,@float=4},
                alignments=new {material=4,textureDef=4,constantDef=16,stateBits=4,water=4,@float=4},
                offsets=new {techniqueSet=80,textureUnion=12,waterImage=0},
                counts=new {textures=nt,constants=nc,stateBits=ns},
                presence=new {name=true,textureTable=nt>0,constantTable=nc>0,stateBitsTable=ns>0},
                reusable=new {uniquePointers=0,aliasCount=0},
                techniqueSet=technique,
                rootFile=rootRel,
                textureTableFile=nt>0?texRel:null,
                constantTableFile=nc>0?constRel:null,
                stateBitsTableFile=ns>0?stateRel:null,
                textures=textureRows.Select(x=>new {
                    index=x.GetProperty("index").GetInt32(),
                    semantic=x.GetProperty("semantic").GetInt32(),
                    image=x.GetProperty("image").ValueKind==JsonValueKind.Null
                        ?null:x.GetProperty("image").GetString(),
                    water=(object?)null
                }).ToArray(),
                inventory=new {waterTextures=0,waterSamples=0,invalidWaterDimensions=0}
            };
            var graphFile=Path.Combine(graphDir,$"material_{ordinal:D2}.json");
            await File.WriteAllTextAsync(graphFile,JsonSerializer.Serialize(graph,
                new JsonSerializerOptions{WriteIndented=true}),ct);
            var usage=rootUsage[rootIdx];
            groups.Add(new AztecPcXenonMaterialCandidateEntry(
                ordinal,name,rootIdx,usage.SurfaceCount,usage.Triangles,
                nt,nc,ns,technique,activeTail,
                $"0x{Read32(pc,24):X8}", // legacy PC hash/metadata not part of Xenon MaterialInfo
                Path.GetFullPath(graphFile),Sha(header),Sha(convertedTex),
                Sha(convertedConst),Sha(convertedState),dependencies.ToArray(),
                false,false));
            texSum+=nt;constSum+=nc;stateSum+=ns;materialRoots++;
        }

        if(materialRoots!=39 || texSum!=87 || constSum!=93 || stateSum!=192 ||
           groups.Sum(x=>x.WorldSurfaceCount)!=735 ||
           groups.Sum(x=>x.WorldTriangleCount)!=19551)
            throw new InvalidDataException("Converted source Material graph corpus count/coverage changed.");

        var output=new AztecPc39XenonMaterialCandidateReport(
            "WaW360.AztecPcXenonMaterial39SourceBridge.v1",
            "AZTEC_39_PC_MATERIALS_TO_XENON_96B_SOURCE_GRAPH_CANDIDATES_ONLY",
            Path.GetFullPath(manifestPath),Path.GetFullPath(sourceCorpusSummary),
            Path.GetFullPath(pcMaterialWorklist),Path.GetFullPath(nativeIdentity),
            dir,39,39,735,19551,texSum,constSum,stateSum,
            uniqueDeps,absentDeps,ambiguousDeps,noSourceTechniques,unsafeTailCount,
            false,false,false,false,groups.ToArray(),
            "39 original-PC 112B material headers are rebuilt into 96B Xenon ABI " +
            "with endian-swapped name/drawSurf/surfaceFlags and pointer fields, " +
            "51 visible stateBitsEntry values, converted 16B image texture defs, " +
            "32B constants and 8B stateBits words. All source information is from " +
            "PC OAT typed Material*; no Xbox donor body copied. Candidate graphs " +
            "match the existing MaterialGraphCodec file contract but stateBits tail, " +
            "all shader technique/image references and runtime dependency closure " +
            "remain independently unverified. DO NOT insert or deploy before " +
            "source-to-native semantic checks and OAT validation; NO FF.");
        var reportPath=Path.Combine(dir,"aztec_39_pc_to_xenon_material_graph_candidates.json");
        await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(output,
            new JsonSerializerOptions{WriteIndented=true}),ct);
        return output;
    }

    private static AztecPcXenonMaterialDependency ResolveDependency(
        string type,string name,JsonElement[] roots)
    {
        var matches=roots.Where(x=>
            (type=="image"?x.GetProperty("OatTypeName").GetString() == "image":
                x.GetProperty("OatTypeName").GetString() is "techset" or "techniqueset") &&
            string.Equals(x.GetProperty("Name").GetString(),name,StringComparison.OrdinalIgnoreCase)
            ).ToArray();
        return new(type,name,matches.Length==1?"UNIQUE_NATIVE_ROOT":
            matches.Length==0?"MISSING":"AMBIGUOUS",
            matches.Length==1?matches[0].GetProperty("Index").GetInt32():-1);
    }

    private static async Task<byte[]> ReadSourceBlob(
        string relative,long size,string corpusRoot,CancellationToken ct)
    {
        if(!relative.StartsWith("pc_gfxworld_39_material_source_corpus/",StringComparison.Ordinal))
            throw new InvalidDataException("PC source corpus path cannot escape approved OAT dump family.");
        var file=Path.GetFullPath(Path.Combine(corpusRoot,relative.Replace('/',Path.DirectorySeparatorChar)));
        if (!file.StartsWith(corpusRoot.TrimEnd(Path.DirectorySeparatorChar)+
                Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source Material blob traversal attempt.");
        var b=await File.ReadAllBytesAsync(file,ct);
        if(b.LongLength!=size)
            throw new InvalidDataException($"PC source Material blob {file} bytes {b.LongLength} != expected {size}.");
        return b;
    }

    private static uint Read32(byte[] b,int at)=>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at,4));
    private static void Put32(byte[] b,int at,uint value)=>
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at,4),value);
    private static string Sha(ReadOnlySpan<byte> b)=>Convert.ToHexString(SHA256.HashData(b));
}

public sealed record AztecPcXenonMaterialDependency(
    string Type,string OriginalPcName,string State,int UniqueNativeRootIndex);

public sealed record AztecPcXenonMaterialCandidateEntry(
    int MaterialMemoryOrdinal,string VerifiedMaterialName,int GoldenXboxRootIndex,
    int WorldSurfaceCount,int WorldTriangleCount,
    int TextureCount,int ConstantCount,int StateBitsCount,
    string? OriginalPcTechniqueSet,bool TruncatedPcStateBitsTailHasNonFFBytes,
    string DiscardedPcOnlyHashIndexBytes,
    string GraphManifest,string XenonRootSha256,string XenonTexturesSha256,
    string XenonConstantsSha256,string XenonStateBitsSha256,
    IReadOnlyList<AztecPcXenonMaterialDependency> DependencyCandidates,
    bool RealXenonWorldMaterialRootWritten,bool OatLoadedMaterialGraphVerified);

public sealed record AztecPc39XenonMaterialCandidateReport(
    string Schema,string Status,string OriginalPcMaterialSourceManifest,
    string SourceCorpusExtractionReport,string VerifiedPc735MaterialWorklist,
    string OriginalXboxNativeIdentityManifest,
    string CandidateGraphRoot,
    int SourceMaterialHeaders,int SourceMaterialChildrenConverted,
    int CoveredWorldSurfaces,int CoveredWorldTriangles,
    int TextureDescriptors,int ConstantRecords,int StateBitsRecords,
    int UniqueNameMatchesInNativeRoots,int MissingNativeDependencies,
    int AmbiguousNativeDependencies,int MaterialsWithNullOriginalTechnique,
    int MaterialsWithPotentiallyActiveTruncatedStateBitsEntries,
    bool AllDependenciesVerified,bool RealXenonMaterialRootsEmitted,
    bool IntegratedOatFfValidated,bool XboxHardwareReady,
    IReadOnlyList<AztecPcXenonMaterialCandidateEntry> Materials,
    string Note);
