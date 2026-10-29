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
    private const string PcZoneSha="7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string SourceIndicesSha="E89621777CB47C46BB47FE0CA948645D6813151E02A706C2A2325C01BDEFD32D";
    private const string SourceVerticesSha="6303269A8170F6F06233DCCE0BF1230EC6A659C4D0EE78CFDF7C14EE242D8041";

    private readonly byte[] _root;
    private readonly byte[] _indices;
    private readonly byte[] _vertices;
    private readonly byte[] _surfaces;
    private readonly IReadOnlyList<int> _surfaceOwners;
    private readonly IReadOnlyList<int> _materialOwners;
    private readonly int _indicesAt;
    private readonly int _verticesAt;
    private readonly int _materialMemoryAt;
    private readonly int _dpvsSurfaceAt;

    public int NativeRootIndex { get; }
    public string NativeRootName { get; }
    public bool FullDpvsChildGraphComplete => false;
    public int SourceSurfaces => _surfaceOwners.Count;
    public int RealMaterialOwners => _materialOwners.Count;

    private T4XenonAztecWorldGraft(
        int nativeRootIndex,string rootName,byte[] root,byte[] indices,
        byte[] vertices,byte[] surfaces,IReadOnlyList<int> surfaceOwners,
        IReadOnlyList<int> materialOwners,int indicesAt,int verticesAt,
        int materialMemoryAt,int dpvsSurfaceAt)
    {
        NativeRootIndex=nativeRootIndex;NativeRootName=rootName;
        _root=root;_indices=indices;_vertices=vertices;_surfaces=surfaces;
        _surfaceOwners=surfaceOwners;_materialOwners=materialOwners;
        _indicesAt=indicesAt;_verticesAt=verticesAt;
        _materialMemoryAt=materialMemoryAt;_dpvsSurfaceAt=dpvsSurfaceAt;
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
        var dpvsSurfaceAt=abi.GetProperty("dpvsStaticAt").GetInt32()+
                          abi.GetProperty("dpvsStaticSurfacesAt").GetInt32();
        if(indicesAt!=20||verticesAt!=100||dpvsSurfaceAt!=780||
           materialMemoryAt<=0||materialMemoryAt>=XboxRootSize-4)
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
        return new(world[0].Index,world[0].Name,root,ix,vs,surfaceBytes,
            surfaceOwnerIndices,owners,indicesAt,verticesAt,materialMemoryAt,dpvsSurfaceAt);
    }

    // Both passes perform precisely the same VIRTUAL allocations. No
    // absolute FF pointers from a previous scratch build survive rebasing.
    public void Plan(Action<long,int,bool> virtualAlloc,Action<int> tempRoot)
    {
        tempRoot(XboxRootSize);
        virtualAlloc(Encoding.UTF8.GetByteCount(NativeRootName)+1L,1,true);
        virtualAlloc(Encoding.UTF8.GetByteCount("nazi_zombie_aztec")+1L,1,true);
        virtualAlloc(_indices.Length,2,true);
        virtualAlloc(_surfaces.Length,4,true);
        virtualAlloc(MaterialCount*8L,4,true);
        virtualAlloc(_vertices.Length,4,true);
    }

    public void Emit(Stream zone,Action<long,int,bool> virtualAlloc,
        Action<int> tempRoot,
        IReadOnlyDictionary<int,uint> finalRootPointers)
    {
        if(_surfaceOwners.Any(x=>!finalRootPointers.ContainsKey(x)) ||
           _materialOwners.Any(x=>!finalRootPointers.ContainsKey(x)))
            throw new InvalidDataException("World FF layout has missing final stable real Material insert slots.");
        var body=(byte[])_root.Clone();
        Put(body,0,T4ZonePointer.Following);
        Put(body,4,T4ZonePointer.Following);
        Put(body,_indicesAt,T4ZonePointer.Following);
        Put(body,_verticesAt,T4ZonePointer.Following);
        Put(body,_dpvsSurfaceAt,T4ZonePointer.Following);
        Put(body,_materialMemoryAt,T4ZonePointer.Following);
        tempRoot(XboxRootSize);
        zone.Write(body);
        EmitString(zone,virtualAlloc,NativeRootName);
        EmitString(zone,virtualAlloc,"nazi_zombie_aztec");
        virtualAlloc(_indices.Length,2,true);
        zone.Write(_indices);
        var linkedSurfaces=(byte[])_surfaces.Clone();
        for(int i=0;i<SurfaceCount;i++)
            Put(linkedSurfaces,i*XboxSurfaceSize+44,
                finalRootPointers[_surfaceOwners[i]]);
        virtualAlloc(linkedSurfaces.Length,4,true);
        zone.Write(linkedSurfaces);
        var materialMemory=new byte[MaterialCount*8];
        for(int i=0;i<MaterialCount;i++)
        {
            Put(materialMemory,i*8,finalRootPointers[_materialOwners[i]]);
            // MaterialMemory.memory requires a real source-owned conversion;
            // 0 is an explicit placeholder, not fabricated donor data.
        }
        virtualAlloc(materialMemory.Length,4,true);
        zone.Write(materialMemory);
        virtualAlloc(_vertices.Length,4,true);
        zone.Write(_vertices);
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
