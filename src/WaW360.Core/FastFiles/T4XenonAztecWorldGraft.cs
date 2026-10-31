using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// The first DIRECT integration of original-PC Aztec world geometry into the
/// actual 8,509-root Xenon FF planner/emitter. This is NOT a standalone canary:
/// Plan/Emit are called as the gfxworld XAsset is traversed in Stage 4D/4E.
///
/// Initial graph is intentionally limited to source-backed indices, vertex
/// data, the full 735 Xbox GfxSurface array, and 39 MaterialMemory owners.
/// The untouched source world has additional nonzero DPVS/cell/lightmap
/// children; their serialization remains an explicit blocker to approving
/// hardware even if OAT accepts this first integrated core graph.
/// </summary>
public sealed class T4XenonAztecWorldGraft
{
    private const int SourceIndexStart=0x049125F2;
    private const int SourceVertexStart=0x057359F9;
    private const int IndexCount=58653;
    private const int VertexCount=32713;
    private const int VertexStride=44;
    private const int SurfaceCount=735;
    private const int MaterialCount=39;
    private const int XboxRootSize=900;
    private const int XboxSurfaceSize=76;
    private const int PcSurfaceStart=0x058E361E;
    private const int StaticDpvsSortedAt=68;
    private const int StaticDpvsModelsAt=72;
    private const int StaticModelInstStride=28;
    private const string PcZoneSha="7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string SourceIndicesSha="E89621777CB47C46BB47FE0CA948645D6813151E02A706C2A2325C01BDEFD32D";
    private const string SourceVerticesSha="6303269A8170F6F06233DCCE0BF1230EC6A659C4D0EE78CFDF7C14EE242D8041";

    private readonly byte[] _root;
    private readonly byte[] _indices;
    private readonly byte[] _vertices;
    private readonly byte[] _surfaces;
    // Populated only for the opt-in, source-verified DPVS child probe.
    private readonly byte[]? _sortedSurfIndex;
    private readonly byte[]? _staticModelInsts;
    private readonly IReadOnlyList<int> _surfaceOwners;
    private readonly IReadOnlyList<int> _materialOwners;
    private readonly int _indicesAt;
    private readonly int _verticesAt;
    private readonly int _materialMemoryAt;
    private readonly int _materialMemoryCountAt;
    private readonly int _dpvsSurfaceAt;
    private readonly int _dpvsSortedAt;
    private readonly int _dpvsModelsAt;
    // Opt-in typed original-PC Gate8 source children; never Xbox donor bytes.
    private byte[]? _gate8Planes, _gate8Nodes, _gate8Layer;
    private int _gate8PlanePointerAt, _gate8NodePointerAt;
    private int _gate8LayerPointerAt, _gate8LayerSizeAt;
    // Gate10 OAT-only source draw probe: never hardware-approved.
    private T4XenonAztecGate10DrawProbe? _gate10DrawProbe;
    private int _gate10DrawPointerAt;
    // Gate11 opt-in: 11 source-owned PC GfxCells, 740 AABB and nested index arrays.
    private T4XenonAztecGate11CellProbe? _gate11Cells;
    // Opt-in isolated Gate11 original-PC-only sky/brush/water primitive batch.
    private T4XenonAztecGate11PrimitiveBatch? _gate11Primitives;
    // Coordinated Wave A+B: source light-grid, shadows and empty light-region headers.
    private T4XenonAztecGate11LightingShadowBatch? _gate11Lighting;
    // Gate11 star-image header only: no GfxImage payload or runtime fabrication.
    private T4XenonAztecGate11ImageHeaders? _gate11ImageHeaders;

    public int NativeRootIndex { get; }
    public string NativeRootName { get; }
    // Scratch-only child bisection: default is the unchanged complete emitter.
    // The integrated diagnostic script sets this environment variable for probes.
    private static int ProbeStage => (Environment.GetEnvironmentVariable(
        "WAW360_AZTEC_WORLD_CHILD_PROBE")?.Trim().ToLowerInvariant()) switch
    {
        null or "" or "full" => 4,
        "root" => 0,
        "indices" => 1,
        "surfaces" => 2,
        "materialmemory" => 3,
        var value => throw new InvalidDataException(
            $"Unknown GfxWorld child probe '{value}'.")
    };

    // Only when WAW360_AZTEC_GATE8_CHILD_CORPUS is set; diagnostic partial
    // stages isolate the exact T4 Xenon child that fails OAT without moving
    // or modifying any previously validated source-owned scratch graph.
    private static int Gate8ChildLevel =>
        (Environment.GetEnvironmentVariable("WAW360_AZTEC_GATE8_CHILD_LEVEL")?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "all" => 3,
            "planes" => 1,
            "nodes" => 2,
            var value => throw new InvalidDataException($"Unknown Gate8 GfxWorld child level '{value}'.")
        };

    private static bool StaticDpvsProbeEnabled =>
        (Environment.GetEnvironmentVariable("WAW360_AZTEC_WORLD_DPVS_STATIC_PROBE")?.Trim().ToLowerInvariant()) switch
        {
            null or "" => false,
            "sorted-smodel" => true,
            var value => throw new InvalidDataException($"Unknown static DPVS child probe '{value}'.")
        };

    public bool FullDpvsChildGraphComplete => false;
    public int SourceSurfaces => _surfaceOwners.Count;
    public int RealMaterialOwners => _materialOwners.Count;
    public int Gate10OatDiagnosticDrawRecords => _gate10DrawProbe?.DrawCount ?? 0;
    public int Gate10UnverifiedStockDraws => _gate10DrawProbe?.UnverifiedStockDraws ?? 0;
    public int Gate11DiagnosticCells => _gate11Cells?.CellCount ?? 0;
    public int Gate11DiagnosticAabbTrees => _gate11Cells?.TreeCount ?? 0;
    public int Gate11DiagnosticSmodelIndexChildren => _gate11Cells?.ModelIndexCount ?? 0;
    public int Gate11DiagnosticReflectionIndices => _gate11Cells?.ReflectionIndexCount ?? 0;
    public int Gate11PrimitiveSkySurfaces => _gate11Primitives?.SkySurfaces ?? 0;
    public int Gate11PrimitiveBrushModels => _gate11Primitives?.BrushModels ?? 0;
    public long Gate11PrimitiveWaterBytes => _gate11Primitives?.WaterBytes ?? 0;
    public long Gate11LightingGridBytes => _gate11Lighting?.GridBytes ?? 0;
    public int Gate11LightingShadowModelRefs => _gate11Lighting?.ShadowModelReferences ?? 0;
    public int Gate11LightingRegionHeaders => _gate11Lighting?.RegionHeaders ?? 0;
    public int Gate11SourceReflectionHeaders => _gate11ImageHeaders?.ReflectionProbeHeaders ?? 0;
    public int Gate11SourceLightmapHeaders => _gate11ImageHeaders?.LightmapHeaders ?? 0;

    private T4XenonAztecWorldGraft(
        int nativeRootIndex,string rootName,byte[] root,byte[] indices,
        byte[] vertices,byte[] surfaces,byte[]? sortedSurfIndex,
        byte[]? staticModelInsts,IReadOnlyList<int> surfaceOwners,
        IReadOnlyList<int> materialOwners,int indicesAt,int verticesAt,
        int materialMemoryAt,int materialMemoryCountAt,int dpvsSurfaceAt,
        int dpvsSortedAt,int dpvsModelsAt)
    {
        NativeRootIndex=nativeRootIndex;NativeRootName=rootName;
        _root=root;_indices=indices;_vertices=vertices;_surfaces=surfaces;
        _sortedSurfIndex=sortedSurfIndex;_staticModelInsts=staticModelInsts;
        _surfaceOwners=surfaceOwners;_materialOwners=materialOwners;
        _indicesAt=indicesAt;_verticesAt=verticesAt;
        _materialMemoryAt=materialMemoryAt;_materialMemoryCountAt=materialMemoryCountAt;
        _dpvsSurfaceAt=dpvsSurfaceAt;_dpvsSortedAt=dpvsSortedAt;
        _dpvsModelsAt=dpvsModelsAt;
    }

    public static async Task<T4XenonAztecWorldGraft> LoadAsync(
        string seedReportFile,string source76SurfaceManifest,
        string materialCandidateReport,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        CancellationToken ct=default)
    {
        using var sd=JsonDocument.Parse(await File.ReadAllTextAsync(seedReportFile,ct));
        using var cd=JsonDocument.Parse(await File.ReadAllTextAsync(source76SurfaceManifest,ct));
        using var md=JsonDocument.Parse(await File.ReadAllTextAsync(materialCandidateReport,ct));
        var seed=sd.RootElement;
        var surfaces=cd.RootElement;
        var material=md.RootElement;
        if(seed.GetProperty("Status").GetString()!="AZTEC_PC_GFXWORLD_800_TO_XBOX_900_ROOT_SCALAR_AND_DPVS_SEED_PASS_NOT_FF" ||
           seed.GetProperty("WorldSurfaces").GetInt32()!=SurfaceCount ||
           seed.GetProperty("WorldMaterials").GetInt32()!=MaterialCount ||
           seed.GetProperty("WorldVertices").GetInt32()!=VertexCount ||
           seed.GetProperty("WorldIndices").GetInt32()!=IndexCount ||
           seed.GetProperty("XboxRootSize").GetInt32()!=XboxRootSize ||
           seed.GetProperty("FullWorldChildrenSerialized").GetBoolean() ||
           seed.GetProperty("XboxHardwareReady").GetBoolean() ||
           surfaces.GetProperty("Status").GetString()!="AZTEC_PC_735_XENON_76B_GFXSURFACES_SOURCE_DERIVED_TOPMIP_READBACK_ONLY" ||
           surfaces.GetProperty("Surfaces").GetInt32()!=SurfaceCount ||
           surfaces.GetProperty("MaterialOffset").GetInt32()!=44 ||
           surfaces.GetProperty("ZeroMaterialPointerFields").GetInt32()!=SurfaceCount ||
           !surfaces.GetProperty("TopMipFullySourceProven").GetBoolean() ||
           material.GetProperty("Status").GetString()!="AZTEC_39_PC_MATERIALS_TO_XENON_96B_SOURCE_GRAPH_CANDIDATES_ONLY" ||
           material.GetProperty("SourceMaterialChildrenConverted").GetInt32()!=MaterialCount ||
           material.GetProperty("CoveredWorldSurfaces").GetInt32()!=SurfaceCount ||
           roots.Count!=8509)
            throw new InvalidDataException("The integrated world needs proven original PC 735/39 source and 900-byte Xenon ABI artifacts.");

        var world=roots.Where(x=>x.OatTypeName=="gfxworld").ToArray();
        if(world.Length!=1 || !world[0].Name.Contains("nazi_zombie_aztec",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Exactly one native Xbox Aztec GfxWorld owner is required.");
        var root=await File.ReadAllBytesAsync(
            seed.GetProperty("XboxScalarOnlyRoot900File").GetString()!,ct);
        if(root.Length!=XboxRootSize ||
           !Eq(Sha(root),seed.GetProperty("XboxScalarOnlyRootSha256").GetString()))
            throw new InvalidDataException("The OAT-verified PC->Xbox world seed bytes changed.");

        var nativeAbiPath=seed.GetProperty("DualSourceNativeAbiWitness").GetString()!;
        using var ad=JsonDocument.Parse(await File.ReadAllTextAsync(nativeAbiPath,ct));
        var abi=ad.RootElement.GetProperty("nativeGfxWorldRootAbi");
        var indicesAt=abi.GetProperty("indicesAt").GetInt32();
        var verticesAt=abi.GetProperty("vertexDataAt").GetInt32();
        var materialMemoryAt=abi.GetProperty("materialMemoryAt").GetInt32();
        var materialMemoryCountAt=abi.GetProperty("materialMemoryCountAt").GetInt32();
        var dpvsRootAt=abi.GetProperty("dpvsStaticAt").GetInt32();
        var dpvsSurfaceAt=dpvsRootAt+abi.GetProperty("dpvsStaticSurfacesAt").GetInt32();
        var dpvsSortedAt=dpvsRootAt+abi.GetProperty("dpvsStaticSortedSurfaceIndexAt").GetInt32();
        var dpvsModelsAt=dpvsRootAt+StaticDpvsModelsAt;
        if(indicesAt!=20||verticesAt!=100||dpvsSurfaceAt!=780||
           dpvsRootAt!=704||
           abi.GetProperty("dpvsStaticSize").GetInt32()!=100||
           dpvsSortedAt!=772||dpvsModelsAt!=776||
           materialMemoryAt<=0||materialMemoryAt>=XboxRootSize-4||
           materialMemoryCountAt<=0||materialMemoryCountAt>=XboxRootSize-4||
           materialMemoryCountAt==materialMemoryAt)
            throw new InvalidDataException("Native Xenon GfxWorld field offsets changed.");

        var surfaceBytes=await File.ReadAllBytesAsync(surfaces.GetProperty("BinaryFile").GetString()!,ct);
        if(surfaceBytes.Length!=SurfaceCount*XboxSurfaceSize ||
           !Eq(Sha(surfaceBytes),surfaces.GetProperty("BinarySha256").GetString()))
            throw new InvalidDataException("Source 735 real 76-byte Xbox surface array SHA mismatch.");

        var candidates=material.GetProperty("Materials").EnumerateArray().ToArray();
        if(candidates.Length!=MaterialCount)throw new InvalidDataException("39 Material owner corpus missing.");
        var owners=new int[MaterialCount];
        var names=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<owners.Length;i++)
        {
            var row=candidates[i];
            int id=row.GetProperty("GoldenXboxRootIndex").GetInt32();
            string name=row.GetProperty("VerifiedMaterialName").GetString()!;
            if(row.GetProperty("MaterialMemoryOrdinal").GetInt32()!=i ||
               id<0||id>=roots.Count||roots[id].OatTypeName!="material"||
               roots[id].Name!=name||!names.Add(name))
                throw new InvalidDataException($"Real source PC material owner #{i} drifted.");
            owners[i]=id;
        }
        var byOwner=owners.ToHashSet();
        var sites=surfaces.GetProperty("MaterialRelocationSites").EnumerateArray().ToArray();
        var surfaceOwnerIndices=new int[SurfaceCount];
        if(sites.Length!=SurfaceCount)throw new InvalidDataException("Expected exactly 735 source surface relocation sites.");
        int totalTriangles=0;
        for(int i=0;i<sites.Length;i++)
        {
            var site=sites[i];
            int id=site.GetProperty("UniqueGoldenXboxRootIndex").GetInt32();
            if(site.GetProperty("SurfaceIndex").GetInt32()!=i ||
               site.GetProperty("FieldOffsetInCanaryBuffer").GetInt32()!=i*XboxSurfaceSize+44 ||
               !site.GetProperty("XboxTopMipSourceProven").GetBoolean() ||
               site.GetProperty("MaterialPointerWritten").GetBoolean()||
               !byOwner.Contains(id)||
               roots[id].Name!=site.GetProperty("VerifiedPcMaterialName").GetString()||
               BinaryPrimitives.ReadUInt32BigEndian(surfaceBytes.AsSpan(i*XboxSurfaceSize+44,4))!=0)
                throw new InvalidDataException($"World GfxSurface #{i} has invalid source material owner.");
            surfaceOwnerIndices[i]=id;
            totalTriangles+=site.GetProperty("Triangles").GetInt32();
        }
        if(totalTriangles!=19551||surfaceOwnerIndices.Distinct().Count()!=MaterialCount)
            throw new InvalidDataException("Source world 735/39 material topology changed.");

        // The ORIGINAL PC zone is the only source of these geometry arrays;
        // never use the original Xbox FF world/geometry as a payload donor.
        var original=seed.GetProperty("OriginalPcFastFile").GetString()!;
        var zone=await FastFileAnalysisCache.GetZoneAsync(original,ct);
        if(zone.Platform!=WaWPlatform.PC ||
           !Eq(zone.SourceSha256,ad.RootElement.GetProperty("originalPcFastFileSha256").GetString()) ||
           !Eq(Sha(zone.Bytes),PcZoneSha) ||
           !Eq(Sha(zone.Bytes.AsSpan(SourceIndexStart,IndexCount*2)),SourceIndicesSha) ||
           !Eq(Sha(zone.Bytes.AsSpan(SourceVertexStart,VertexCount*VertexStride)),SourceVerticesSha))
            throw new InvalidDataException("Original-PC source world geometry SHA/provenance changed.");
        var ix=new byte[IndexCount*2];
        for(int i=0;i<IndexCount;i++)
            BinaryPrimitives.WriteUInt16BigEndian(ix.AsSpan(i*2,2),
                BinaryPrimitives.ReadUInt16LittleEndian(zone.Bytes.AsSpan(SourceIndexStart+i*2,2)));
        var vs=new byte[VertexCount*VertexStride];
        for(int i=0;i<VertexCount;i++)
        {
            var sourceAt=SourceVertexStart+i*VertexStride;
            var destAt=i*VertexStride;
            for(int k=0;k<VertexStride;k+=4)
            {
                if(k==16)zone.Bytes.AsSpan(sourceAt+k,4).CopyTo(vs.AsSpan(destAt+k,4));
                else BinaryPrimitives.WriteUInt32BigEndian(vs.AsSpan(destAt+k,4),
                    BinaryPrimitives.ReadUInt32LittleEndian(zone.Bytes.AsSpan(sourceAt+k,4)));
            }
        }
        // Xenon GfxWorldDpvsStatic load order:
        //   sortedSurfIndex[staticSurfaceCount] (uint16)
        //   smodelInsts[smodelCount] (GfxStaticModelInst, 28B)
        //   surfaces[surfaceCount] (GfxSurface, 76B).
        // In the original PC uncompressed stream, these arrays are adjacent
        // even when their in-memory VIRTUAL block offsets are aligned. The
        // probe verifies each source record before allowing OAT to read it.
        byte[]? sortedBytes=null,modelBytes=null;
        if(StaticDpvsProbeEnabled)
        {
            if(ProbeStage!=4)
                throw new InvalidDataException("Static DPVS probe is only valid with the Full core.");
            var pcDpvs=await File.ReadAllBytesAsync(
                seed.GetProperty("OriginalPcDpvs100File").GetString()!,ct);
            if(pcDpvs.Length!=100 ||
               !Eq(Sha(pcDpvs),seed.GetProperty("OriginalPcDpvsSha256").GetString()))
                throw new InvalidDataException("Original-PC DPVS static 100B provenance changed.");
            uint smodelCount=BinaryPrimitives.ReadUInt32LittleEndian(pcDpvs.AsSpan(0,4));
            uint staticCount=BinaryPrimitives.ReadUInt32LittleEndian(pcDpvs.AsSpan(4,4));
            var originalShape=ad.RootElement.GetProperty("originalPcLoadedWorldChildShape");
            if(staticCount!=613 || smodelCount>4096 ||
               originalShape.GetProperty("smodelCount").GetUInt32()!=smodelCount ||
               originalShape.GetProperty("staticSurfaceCount").GetUInt32()!=staticCount)
                throw new InvalidDataException("Original-PC DPVS static sorted/model counts differ from OAT witness.");
            uint rawSorted=BinaryPrimitives.ReadUInt32LittleEndian(pcDpvs.AsSpan(StaticDpvsSortedAt,4));
            uint rawModels=BinaryPrimitives.ReadUInt32LittleEndian(pcDpvs.AsSpan(StaticDpvsModelsAt,4));
            uint rawSurfaces=BinaryPrimitives.ReadUInt32LittleEndian(pcDpvs.AsSpan(76,4));
            if(rawSorted!=T4ZonePointer.Following ||
               (smodelCount>0 && rawModels!=T4ZonePointer.Following) ||
               (smodelCount==0 && rawModels!=0) ||
               rawSurfaces!=T4ZonePointer.Following)
                throw new InvalidDataException(
                    $"PC DPVS sorted/models/surfaces must be inline FOLLOWING children: " +
                    $"sorted=0x{rawSorted:X8}, models=0x{rawModels:X8}, surfaces=0x{rawSurfaces:X8}.");
            int modelSize=checked((int)smodelCount*StaticModelInstStride);
            int sortedSize=checked((int)staticCount*2);
            int sortedStart=PcSurfaceStart-modelSize-sortedSize;
            int modelsStart=sortedStart+sortedSize;
            if(sortedStart<SourceVertexStart+VertexCount*VertexStride ||
               modelsStart+modelSize!=PcSurfaceStart)
                throw new InvalidDataException("DPVS child range does not precede the source 735-surface array.");
            sortedBytes=new byte[sortedSize];
            modelBytes=new byte[modelSize];
            var seen=new HashSet<ushort>();
            for(int i=0;i<staticCount;i++)
            {
                ushort index=BinaryPrimitives.ReadUInt16LittleEndian(
                    zone.Bytes.AsSpan(sortedStart+i*2,2));
                if(index>=SurfaceCount || !seen.Add(index))
                    throw new InvalidDataException(
                        $"Source sortedSurfIndex entry #{i} is not a unique valid surface index: {index}.");
                BinaryPrimitives.WriteUInt16BigEndian(
                    sortedBytes.AsSpan(i*2,2),index);
            }
            for(int i=0;i<smodelCount;i++)
            {
                int at=modelsStart+i*StaticModelInstStride;
                int target=i*StaticModelInstStride;
                for(int k=0;k<24;k+=4)
                {
                    float value=BinaryPrimitives.ReadSingleLittleEndian(
                        zone.Bytes.AsSpan(at+k,4));
                    if(!float.IsFinite(value) || Math.Abs(value)>100000000f)
                        throw new InvalidDataException($"Original-PC smodelInsts[{i}] bounds are not finite.");
                    BinaryPrimitives.WriteSingleBigEndian(modelBytes.AsSpan(target+k,4),value);
                }
                for(int axis=0;axis<3;axis++)
                    if(BinaryPrimitives.ReadSingleLittleEndian(zone.Bytes.AsSpan(at+axis*4,4))>
                       BinaryPrimitives.ReadSingleLittleEndian(zone.Bytes.AsSpan(at+12+axis*4,4)))
                        throw new InvalidDataException($"Original-PC smodelInsts[{i}] inverted bounds axis {axis}.");
                // GfxColor is four raw bytes, not an endian-swapped float.
                zone.Bytes.AsSpan(at+24,4).CopyTo(modelBytes.AsSpan(target+24,4));
            }
        }
        var graft=new T4XenonAztecWorldGraft(
            world[0].Index,world[0].Name,root,ix,vs,surfaceBytes,
            sortedBytes,modelBytes,surfaceOwnerIndices,owners,indicesAt,verticesAt,
            materialMemoryAt,materialMemoryCountAt,dpvsSurfaceAt,
            dpvsSortedAt,dpvsModelsAt);
        var gate8Path=Environment.GetEnvironmentVariable("WAW360_AZTEC_GATE8_CHILD_CORPUS");
        if(!string.IsNullOrWhiteSpace(gate8Path))
        {
            if(!StaticDpvsProbeEnabled || ProbeStage!=4)
                throw new InvalidDataException("Gate8 typed plane/node/layer probe requires Full + source-static-DPVS mode.");
            using var childDoc=JsonDocument.Parse(await File.ReadAllTextAsync(gate8Path,ct));
            var child=childDoc.RootElement;
            if(child.GetProperty("schema").GetString()!="WaW360.AztecGate8PcTypedChildCorpus.v1" ||
               child.GetProperty("status").GetString()!="AZTEC_PC_GATE8_TYPED_PLANES_NODES_LAYER_SOURCE_PASS" ||
               child.GetProperty("xboxFfEmitted").GetBoolean() ||
               child.GetProperty("fullDpvsComplete").GetBoolean() ||
               child.GetProperty("xboxHardwareReady").GetBoolean() ||
               !Eq(child.GetProperty("originalPcFFSha256").GetString(),zone.SourceSha256) ||
               child.GetProperty("planeCount").GetInt32()!=3447 ||
               child.GetProperty("nodeCount").GetInt32()!=24511 ||
               child.GetProperty("staticModelDrawCount").GetInt32()!=1903)
                throw new InvalidDataException("Original-PC Gate8 typed child source proof drifted.");
            var children=child.GetProperty("sourceBlobs");
            static async Task<byte[]> ReadBlob(JsonElement blob,int expected,CancellationToken ct)
            {
                var path=blob.GetProperty("path").GetString()!;
                var data=await File.ReadAllBytesAsync(path,ct);
                if(data.Length!=expected ||
                   blob.GetProperty("bytes").GetInt64()!=expected ||
                   !Eq(Sha(data),blob.GetProperty("sha256").GetString()))
                    throw new InvalidDataException($"Gate8 PC source child length/SHA failed: {path}");
                return data;
            }
            var planeSrc=await ReadBlob(children.GetProperty("planes"),3447*20,ct);
            var nodeSrc=await ReadBlob(children.GetProperty("nodes"),24511*2,ct);
            var layerSrc=await ReadBlob(children.GetProperty("vertexLayer"),
                child.GetProperty("vertexLayerBytes").GetInt32(),ct);
            if(layerSrc.Length==0 || layerSrc.Length>33554432)
                throw new InvalidDataException("Gate8 PC vertex-layer byte count is invalid.");
            var planesBe=new byte[planeSrc.Length];
            var nodesBe=new byte[nodeSrc.Length];
            for(int i=0;i<3447;i++)
            {
                int at=i*20;
                for(int k=0;k<4;k++)
                {
                    float value=BinaryPrimitives.ReadSingleLittleEndian(planeSrc.AsSpan(at+k*4,4));
                    if(!float.IsFinite(value) || Math.Abs(value)>100000000f)
                        throw new InvalidDataException($"PC GfxWorld plane[{i}].coeff[{k}] invalid.");
                    BinaryPrimitives.WriteSingleBigEndian(planesBe.AsSpan(at+k*4,4),value);
                }
                planeSrc.AsSpan(at+16,4).CopyTo(planesBe.AsSpan(at+16,4));
            }
            for(int i=0;i<24511;i++)
                BinaryPrimitives.WriteUInt16BigEndian(nodesBe.AsSpan(i*2,2),
                    BinaryPrimitives.ReadUInt16LittleEndian(nodeSrc.AsSpan(i*2,2)));
            var vldAt=abi.GetProperty("vertexLayerDataAt").GetInt32();
            var dpvsPlanesAt=abi.GetProperty("dpvsPlanesAt").GetInt32();
            // Pinned native layout: GfxWorld::vertexLayerDataSize immediately
            // precedes GfxWorldVertexLayerData::data, and DPVS plane/node
            // pointers occupy +4/+8 in GfxWorldDpvsPlanes.
            if(vldAt<8 || vldAt>=XboxRootSize-4 ||
               dpvsPlanesAt<0 || dpvsPlanesAt>=XboxRootSize-16 ||
               vldAt-4<=verticesAt)
                throw new InvalidDataException("Native Xenon PC-derived plane/node/vld pointer offsets unsupported.");
            var childLevel=Gate8ChildLevel;
            graft._gate8Planes=planesBe;
            graft._gate8Nodes=childLevel>=2?nodesBe:null;
            graft._gate8Layer=childLevel>=3?layerSrc:null;
            graft._gate8PlanePointerAt=dpvsPlanesAt+4;
            graft._gate8NodePointerAt=dpvsPlanesAt+8;
            graft._gate8LayerPointerAt=vldAt;
            graft._gate8LayerSizeAt=vldAt-4;
        }
        // GfxWorldDpvsStatic.smodelDrawInsts is +84 within the 100-byte
        // original Xbox T4 32-bit DPVS struct (smodelInsts +72,
        // surfaces +76, cullGroups +80, smodelDrawInsts +84).
        // Gate10 opt-in is deliberately separate from passing FF outputs.
        var lightProof=Environment.GetEnvironmentVariable("WAW360_AZTEC_GATE10_SOURCE_LIGHT_REPORT");
        var relocationProof=Environment.GetEnvironmentVariable("WAW360_AZTEC_GATE10_ROOT_RELOCATION_SUMMARY");
        if(!string.IsNullOrWhiteSpace(lightProof) ||
           !string.IsNullOrWhiteSpace(relocationProof))
        {
            if(string.IsNullOrWhiteSpace(lightProof) ||
               string.IsNullOrWhiteSpace(relocationProof) ||
               !StaticDpvsProbeEnabled || ProbeStage!=4 ||
               graft._gate8Planes is null || graft._gate8Nodes is null ||
               graft._gate8Layer is null || dpvsRootAt!=704 ||
               dpvsRootAt+84!=788 ||
               BinaryPrimitives.ReadUInt32BigEndian(root.AsSpan(dpvsRootAt,4))!=1903)
                throw new InvalidDataException("Gate10 draw probe requires exact 1903-count source DPVS and all verified Gate8 children.");
            graft._gate10DrawProbe=await T4XenonAztecGate10DrawProbe.LoadAsync(
                lightProof,relocationProof,ct);
            graft._gate10DrawPointerAt=dpvsRootAt+84;
        }
        var gate11Source=Environment.GetEnvironmentVariable("WAW360_AZTEC_GATE11_CELL_GRAPH_REPORT");
        if(!string.IsNullOrWhiteSpace(gate11Source))
        {
            if(graft._gate10DrawProbe is null ||
               !StaticDpvsProbeEnabled || ProbeStage!=4 ||
               graft._gate8Planes is null || graft._gate8Nodes is null ||
               graft._gate8Layer is null)
                throw new InvalidDataException(
                    "Gate11 source GfxCells require all proven Gate8 native children and Gate10 1903-draw diagnostics.");
            graft._gate11Cells=await T4XenonAztecGate11CellProbe.LoadAsync(gate11Source,ct);
            if(graft._gate11Cells.WorldCellPointerAt<=0 ||
               graft._gate11Cells.WorldCellPointerAt>=XboxRootSize-4 ||
               graft._gate11Cells.WorldCellCountAt<=0 ||
               graft._gate11Cells.WorldCellCountAt>=XboxRootSize-4 ||
               graft._gate11Cells.WorldCellPointerAt==graft._gate11Cells.WorldCellCountAt ||
               BinaryPrimitives.ReadUInt32BigEndian(
                   graft._root.AsSpan(graft._gate11Cells.WorldCellPointerAt,4))!=0 ||
               BinaryPrimitives.ReadUInt32BigEndian(
                   graft._root.AsSpan(graft._gate11Cells.WorldCellCountAt,4))!=0)
                throw new InvalidDataException(
                    "Gate11 requires a NULL GfxWorld::cells pointer and zero source-seed dpvsPlanes.cellCount at exact measured Xbox offsets. The cell count is activated only with the 11 source-owned native cell children.");
        }
        var primitiveSource=Environment.GetEnvironmentVariable("WAW360_AZTEC_GATE11_PRIMITIVE_BATCH");
        if(!string.IsNullOrWhiteSpace(primitiveSource))
        {
            // Never allow this candidate to run outside the existing
            // independently OAT-verified 11-cell/740-tree branch.
            if(graft._gate11Cells is null || graft._gate10DrawProbe is null ||
               ProbeStage!=4 || !StaticDpvsProbeEnabled)
                throw new InvalidDataException("Gate11 primitives require verified Gate10 draws and full Gate11 cell graph.");
            graft._gate11Primitives=await T4XenonAztecGate11PrimitiveBatch.LoadAsync(
                primitiveSource,graft._root,ct);
        }
        var lightingBatch=Environment.GetEnvironmentVariable("WAW360_AZTEC_GATE11_LIGHTING_BATCH");
        if(!string.IsNullOrWhiteSpace(lightingBatch))
        {
            // Never emit new world branches without 11 cells and the
            // independently validated source-owned primitive baseline.
            if(graft._gate11Cells is null || graft._gate11Primitives is null ||
               graft._gate10DrawProbe is null || ProbeStage!=4 ||
               !StaticDpvsProbeEnabled || string.IsNullOrWhiteSpace(primitiveSource))
                throw new InvalidDataException(
                    "Gate11 lighting/shadow requires full source cells, draw diagnostics and primitive preflight.");
            graft._gate11Lighting=await T4XenonAztecGate11LightingShadowBatch.LoadAsync(
                lightingBatch,primitiveSource,graft._root,ct);
        }
        var imageHeadersReport=Environment.GetEnvironmentVariable("WAW360_AZTEC_GATE11_IMAGE_HEADERS_BATCH");
        if(!string.IsNullOrWhiteSpace(imageHeadersReport))
        {
            if(graft._gate11Lighting is null || graft._gate11Primitives is null ||
               graft._gate11Cells is null || graft._gate10DrawProbe is null ||
               ProbeStage!=4 || !StaticDpvsProbeEnabled)
                throw new InvalidDataException(
                    "Gate11 image header diagnostic requires existing source grid/shadow, 11 cells and 1903 draws.");
            graft._gate11ImageHeaders=await T4XenonAztecGate11ImageHeaders.LoadAsync(
                imageHeadersReport,graft._root,ct);
        }
        return graft;
    }

    // Both passes perform precisely the same VIRTUAL allocations. No
    // absolute FF pointers from a previous scratch build survive rebasing.
    public void Plan(Action<long,int,bool> virtualAlloc,Action<int> tempRoot)
    {
        int stage=ProbeStage;
        tempRoot(XboxRootSize);
        virtualAlloc(Encoding.UTF8.GetByteCount(NativeRootName)+1L,1,true);
        virtualAlloc(Encoding.UTF8.GetByteCount("nazi_zombie_aztec")+1L,1,true);
        if(stage>=1)virtualAlloc(_indices.Length,2,true);
        _gate11Primitives?.PlanSky(virtualAlloc);
        // Native T4 Xenon GfxWorld child traversal loads reflectionProbes
        // before dpvsPlanes (planes/nodes), then cells, then lightmaps.
        _gate11ImageHeaders?.PlanReflectionProbes(virtualAlloc);
        // Both source-owned arrays are loaded inside GfxWorldDpvsPlanes.
        // A single- or two-child probe must keep the same relative order.
        if(_gate8Planes is not null)
            virtualAlloc(_gate8Planes.Length,4,true);
        if(_gate8Nodes is not null)
            virtualAlloc(_gate8Nodes.Length,2,true);
        // GfxWorld::cells is decoded after dpvsPlanes and BEFORE the
        // materialMemory/vd/vld reordered payloads, per pinned T4 Xenon
        // GfxWorld child layout. Nested AABB and index arrays follow cells.
        _gate11Cells?.Plan(virtualAlloc);
        // Lightmap headers are decoded after cells, ahead of GfxLightGrid.
        // All six unverified GfxImage* pointers remain NULL.
        _gate11ImageHeaders?.PlanLightmaps(virtualAlloc);
        // Native world field order: lightmaps -> embedded lightGrid -> models.
        _gate11Lighting?.PlanGrid(virtualAlloc);
        _gate11Primitives?.PlanModels(virtualAlloc);
        // Pinned OAT GfxWorld reorder: '... materialMemory vd vld'.
        // Its first member remains at the original position; vd/vld follow
        // immediately, while the embedded DPVS surfaces are loaded later.
        // Keep this exact allocation order in both Stage 4D and Stage 4E.
        if(stage>=3)virtualAlloc(MaterialCount*8L,4,true);
        if(stage>=4)virtualAlloc(_vertices.Length,4,true);
        if(_gate8Layer is not null)
            virtualAlloc(_gate8Layer.Length,1,true);
        // Native world field order: materialMemory/vd/vld, shadowGeom,
        // lightRegion, then static DPVS and its nested source children.
        _gate11Lighting?.PlanShadowsAndRegions(virtualAlloc);
        if(StaticDpvsProbeEnabled)
        {
            if(_sortedSurfIndex is null || _staticModelInsts is null)
                throw new InvalidDataException("Static DPVS probe missing verified original-PC children.");
            virtualAlloc(_sortedSurfIndex.Length,2,true);
            if(_staticModelInsts.Length>0)
                virtualAlloc(_staticModelInsts.Length,4,true);
        }
        if(stage>=2)virtualAlloc(_surfaces.Length,4,true);
        // T4 Xenon source DPVS children: sorted, smodelInsts, surfaces,
        // then cull groups if loaded, then smodelDrawInsts.
        if(_gate10DrawProbe is not null)
            virtualAlloc(_gate10DrawProbe.SourceByteCount,4,true);
        _gate11Primitives?.PlanWater(virtualAlloc);
    }

    public void Emit(Stream zone,Action<long,int,bool> virtualAlloc,
        Action<int> tempRoot,
        IReadOnlyDictionary<int,uint> finalRootPointers)
    {
        int stage=ProbeStage;
        if(stage>=2 && (_surfaceOwners.Any(x=>!finalRootPointers.ContainsKey(x)) ||
           _materialOwners.Any(x=>!finalRootPointers.ContainsKey(x))))
            throw new InvalidDataException("World FF layout has missing final stable real Material insert slots.");
        var body=(byte[])_root.Clone();
        Put(body,0,T4ZonePointer.Following);
        Put(body,4,T4ZonePointer.Following);
        if(stage>=1)Put(body,_indicesAt,T4ZonePointer.Following);
        if(stage>=4)Put(body,_verticesAt,T4ZonePointer.Following);
        if(stage>=2)Put(body,_dpvsSurfaceAt,T4ZonePointer.Following);
        if(_gate10DrawProbe is not null)
            Put(body,_gate10DrawPointerAt,T4ZonePointer.Following);
        _gate11Primitives?.ApplyRoot(body);
        _gate11Lighting?.ApplyRoot(body);
        _gate11ImageHeaders?.ApplyRoot(body);
        if(_gate11Cells is not null)
        {
            // CRITICAL NATIVE LOADER FIX: Gate10's intentionally partial
            // scalar-only 900B root advertises dpvsPlanes.cellCount=0.
            // When the pointer becomes FOLLOWING, OAT needs the REAL 11
            // to consume the 616B contiguous GfxCell array before walking
            // 740 AABB trees and 14,974 model indices. Otherwise it reads
            // cell[0].mins[0] (-2682.5 / 0xC527A800) as a zone pointer.
            Put(body,_gate11Cells.WorldCellCountAt,(uint)_gate11Cells.CellCount);
            Put(body,_gate11Cells.WorldCellPointerAt,T4ZonePointer.Following);
        }
        if(StaticDpvsProbeEnabled)
        {
            Put(body,_dpvsSortedAt,T4ZonePointer.Following);
            if(_staticModelInsts is { Length: > 0 })
                Put(body,_dpvsModelsAt,T4ZonePointer.Following);
        }
        if(stage>=3)Put(body,_materialMemoryAt,T4ZonePointer.Following);
        if(_gate8Planes is not null)
        {
            Put(body,8,3447);
            Put(body,_gate8PlanePointerAt,T4ZonePointer.Following);
        }
        if(_gate8Nodes is not null)
        {
            Put(body,12,24511);
            Put(body,_gate8NodePointerAt,T4ZonePointer.Following);
        }
        if(_gate8Layer is not null)
        {
            Put(body,_gate8LayerSizeAt,(uint)_gate8Layer.Length);
            Put(body,_gate8LayerPointerAt,T4ZonePointer.Following);
        }
        Put(body,_materialMemoryCountAt,MaterialCount);
        tempRoot(XboxRootSize);
        zone.Write(body);
        EmitString(zone,virtualAlloc,NativeRootName);
        EmitString(zone,virtualAlloc,"nazi_zombie_aztec");
        if(stage>=1)
        {
            virtualAlloc(_indices.Length,2,true);
            zone.Write(_indices);
        }
        _gate11Primitives?.EmitSky(zone,virtualAlloc);
        _gate11ImageHeaders?.EmitReflectionProbes(zone,virtualAlloc);
        if(_gate8Planes is not null)
        {
            virtualAlloc(_gate8Planes.Length,4,true);
            zone.Write(_gate8Planes);
        }
        if(_gate8Nodes is not null)
        {
            virtualAlloc(_gate8Nodes.Length,2,true);
            zone.Write(_gate8Nodes);
        }
        _gate11Cells?.Emit(zone,virtualAlloc);
        _gate11ImageHeaders?.EmitLightmaps(zone,virtualAlloc);
        _gate11Lighting?.EmitGrid(zone,virtualAlloc);
        _gate11Primitives?.EmitModels(zone,virtualAlloc);
        // OAT reads MaterialMemory, then GfxWorldVertexData, then embedded
        // GfxWorldDpvsStatic.surfaces. A surface-first stream causes the first
        // surface's vertexCount/triCount to be decoded as a TEMP-block pointer.
        if(stage>=3)
        {
            var materialMemory=new byte[MaterialCount*8];
            for(int i=0;i<MaterialCount;i++)
            {
                Put(materialMemory,i*8,finalRootPointers[_materialOwners[i]]);
                // MaterialMemory.memory requires source-owned conversion;
                // 0 is an explicit diagnostic placeholder, not donor data.
            }
            virtualAlloc(materialMemory.Length,4,true);
            zone.Write(materialMemory);
        }
        if(stage>=4)
        {
            virtualAlloc(_vertices.Length,4,true);
            zone.Write(_vertices);
        }
        if(_gate8Layer is not null)
        {
            virtualAlloc(_gate8Layer.Length,1,true);
            zone.Write(_gate8Layer);
        }
        _gate11Lighting?.EmitShadowsAndRegions(zone,virtualAlloc);
        if(StaticDpvsProbeEnabled)
        {
            if(_sortedSurfIndex is null || _staticModelInsts is null)
                throw new InvalidDataException("Static DPVS probe missing verified child arrays.");
            virtualAlloc(_sortedSurfIndex.Length,2,true);
            zone.Write(_sortedSurfIndex);
            if(_staticModelInsts.Length>0)
            {
                virtualAlloc(_staticModelInsts.Length,4,true);
                zone.Write(_staticModelInsts);
            }
        }
        if(stage>=2)
        {
            var linkedSurfaces=(byte[])_surfaces.Clone();
            for(int i=0;i<SurfaceCount;i++)
                Put(linkedSurfaces,i*XboxSurfaceSize+44,
                    finalRootPointers[_surfaceOwners[i]]);
            virtualAlloc(linkedSurfaces.Length,4,true);
            zone.Write(linkedSurfaces);
        }
        if(_gate10DrawProbe is not null)
        {
            // Must rebase to THIS Stage4D root layout, never reuse old FF
            // pointers. A nonresident stock XModel stays unlinked.
            var nativeDraws=_gate10DrawProbe.RebaseForLayout(finalRootPointers);
            virtualAlloc(nativeDraws.Length,4,true);
            zone.Write(nativeDraws);
        }
        _gate11Primitives?.EmitWater(zone,virtualAlloc);
    }

    private static void EmitString(Stream zone,Action<long,int,bool> alloc,string s)
    {
        var b=Encoding.UTF8.GetBytes(s);
        alloc(b.Length+1L,1,true);
        zone.Write(b);
        zone.WriteByte(0);
    }
    private static void Put(byte[] b,int at,uint x)=>
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at,4),x);
    private static bool Eq(string? a,string? b)=>
        string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    private static string Sha(ReadOnlySpan<byte> b)=>
        Convert.ToHexString(SHA256.HashData(b));
}
