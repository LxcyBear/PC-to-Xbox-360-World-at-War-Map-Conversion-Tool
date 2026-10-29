using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// First source-owned GfxWorld serializer fragment: independently read the
/// pinned ORIGINAL-PC 800-byte root and its embedded 100-byte DPVS static
/// object, then map verified PC scalar fields to the measured Xenon 900-byte
/// root and 100-byte embedded DPVS static object. No whole-root blind copy,
/// no donor payloads, no guessed active child pointers and NO FF.
/// The actual 735 world surfaces and 39 material bindings remain standalone
/// until the complete root + children can be planned/emitted together.
/// </summary>
public static class T4AztecPcGfxWorldRootDpvsSeed
{
    private const int PcRootOffset = 0x049122C0;
    private const int PcRootBytes = 800;
    private const int XboxRootBytes = 900;
    private const int DpvsBytes = 100;
    private const string PcZoneSha = "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string WitnessStatus = "AZTEC_NATIVE_XBOX_GFXWORLD_ROOT_AND_DPVS_ABI_EXACT_SOURCE_WITNESS";
    private const string LinkStatus = "AZTEC_735_SURFACE_MATERIAL_REFS_TO_39_REAL_XBOX_FF_MATERIAL_ROOTS_REBASED_STANDALONE_PASS";

    public static async Task<AztecPcGfxWorldRootDpvsSeedReport> BuildAsync(
        string dualAbiWitnessPath, string linked735ReportPath,
        string outputDirectory, CancellationToken ct = default)
    {
        using var ad = JsonDocument.Parse(await File.ReadAllTextAsync(dualAbiWitnessPath,ct));
        using var ld = JsonDocument.Parse(await File.ReadAllTextAsync(linked735ReportPath,ct));
        var a=ad.RootElement;
        var link=ld.RootElement;
        if(a.GetProperty("schema").GetString()!="WaW360.AztecXenonNativeGfxWorldRootDpvsAbiWitness.v1" ||
           a.GetProperty("status").GetString()!=WitnessStatus ||
           a.GetProperty("hardwareReady").GetBoolean() ||
           a.GetProperty("sourceGfxWorldRootSerialized").GetBoolean() ||
           a.GetProperty("sourceDpvsStaticSerialized").GetBoolean() ||
           !a.GetProperty("pcVsNativeGeometryAndMaterialCountsEqual").GetBoolean() ||
           link.GetProperty("Status").GetString()!=LinkStatus ||
           link.GetProperty("NativeXboxRoots").GetInt32()!=8509 ||
           link.GetProperty("SourceOwnedXboxSurfaceCount").GetInt32()!=735 ||
           link.GetProperty("RealFfMaterialBodies").GetInt32()!=39 ||
           link.GetProperty("SurfacesLinkedToRealFfBodies").GetInt32()!=735 ||
           link.GetProperty("SurfacesLinkedToIdentityShells").GetInt32()!=0 ||
           link.GetProperty("GfxWorldRootSerialized").GetBoolean() ||
           link.GetProperty("WorldGfxSurfaceArrayInsertedInFf").GetBoolean() ||
           link.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Require dual original-PC/Xbox GfxWorld ABI witness and 735/39 real-Material standalone link PASS. No hardware-promotion.");

        var pc=a.GetProperty("originalPcGfxWorldRootAbi");
        var xb=a.GetProperty("nativeGfxWorldRootAbi");
        var pcs=a.GetProperty("originalPcLoadedWorldChildShape");
        var xbs=a.GetProperty("loadedNativeWorldChildShape");
        if(pc.GetProperty("gfxWorldSize").GetInt32()!=PcRootBytes ||
           xb.GetProperty("gfxWorldSize").GetInt32()!=XboxRootBytes ||
           pc.GetProperty("dpvsStaticSize").GetInt32()!=DpvsBytes ||
           xb.GetProperty("dpvsStaticSize").GetInt32()!=DpvsBytes ||
           pc.GetProperty("materialMemorySize").GetInt32()!=8 ||
           xb.GetProperty("materialMemorySize").GetInt32()!=8 ||
           pc.GetProperty("dpvsStaticSurfaceCountAt").GetInt32()!=4 ||
           xb.GetProperty("dpvsStaticSurfaceCountAt").GetInt32()!=4 ||
           pc.GetProperty("dpvsStaticSurfacesAt").GetInt32()!=76 ||
           xb.GetProperty("dpvsStaticSurfacesAt").GetInt32()!=76 ||
           pc.GetProperty("nameAt").GetInt32()!=0 ||
           xb.GetProperty("nameAt").GetInt32()!=0 ||
           pc.GetProperty("baseNameAt").GetInt32()!=4 ||
           xb.GetProperty("baseNameAt").GetInt32()!=4 ||
           xb.GetProperty("dpvsStaticAt").GetInt32()!=704 ||
           xb.GetProperty("vertexDataAt").GetInt32()!=100 ||
           xb.GetProperty("indicesAt").GetInt32()!=20 ||
           xb.GetProperty("materialMemoryAt").GetInt32()!=500 ||
           pcs.GetProperty("indexCount").GetInt32()!=58653 ||
           pcs.GetProperty("vertexCount").GetInt32()!=32713 ||
           pcs.GetProperty("surfaceCount").GetInt32()!=735 ||
           pcs.GetProperty("materialMemoryCount").GetInt32()!=39 ||
           pcs.GetProperty("staticSurfaceCount").GetInt32()!=613 ||
           pcs.GetProperty("surfaceVisDataCount").GetInt32()!=20 ||
           pcs.GetProperty("smodelVisDataCount").GetInt32()!=60 ||
           pcs.GetProperty("cellCount").GetInt32()!=11)
            throw new InvalidDataException("PC 800B / Xenon 900B root ABI or 100B DPVS offsets/counts changed.");
        foreach(var name in new[]{"planeCount","nodeCount","cellCount","indexCount",
                     "surfaceCount","vertexCount","vertexLayerDataSize",
                     "materialMemoryCount","smodelCount","staticSurfaceCount",
                     "smodelVisDataCount","surfaceVisDataCount"})
            if(pcs.GetProperty(name).GetInt32()!=xbs.GetProperty(name).GetInt32())
                throw new InvalidDataException($"Source/native original world shape mismatch: {name}.");

        var originalPc=a.GetProperty("originalPcFastFile").GetString()!;
        var zone=await FastFileAnalysisCache.GetZoneAsync(originalPc,ct);
        if(zone.Platform!=WaWPlatform.PC ||
           !Eq(zone.SourceSha256,a.GetProperty("originalPcFastFileSha256").GetString()) ||
           !Eq(Sha(zone.Bytes),PcZoneSha) ||
           PcRootOffset+PcRootBytes>zone.Bytes.Length)
            throw new InvalidDataException("Original PC world source root/zone identity mismatch.");
        var raw=zone.Bytes.AsSpan(PcRootOffset,PcRootBytes).ToArray();
        var originalRoot=raw.AsSpan();
        int pDpvs=pc.GetProperty("dpvsStaticAt").GetInt32();
        int xDpvs=xb.GetProperty("dpvsStaticAt").GetInt32();
        if(pDpvs<0 || pDpvs+DpvsBytes>PcRootBytes ||
           xDpvs<0 || xDpvs+DpvsBytes>XboxRootBytes)
            throw new InvalidDataException("Embedded source/native DPVS offset falls outside measured root.");
        // Read real little-endian original PC fields from the pinned root
        // and independently cross-check their values against both OAT-loaded
        // witnesses. Never infer root offsets from a platform's total size.
        var validatedRoot = new List<AztecGfxWorldSeedField>();
        var xenon = new byte[XboxRootBytes];
        foreach(var (sourceOffset,nativeOffset,field) in new[]{
            ("indexCountAt","indexCountAt","indexCount"),
            ("surfaceCountAt","surfaceCountAt","surfaceCount"),
            ("vertexCountAt","vertexCountAt","vertexCount"),
            ("materialMemoryCountAt","materialMemoryCountAt","materialMemoryCount")})
        {
            // Older original-PC OAT metadata explicitly provides the first
            // three offsets but materialMemoryCount needs a new witness
            // position; don't silently derive it as materialMemoryAt-4.
            if(!pc.TryGetProperty(sourceOffset,out var pcOffsetValue) ||
               !xb.TryGetProperty(nativeOffset,out var nativeOffsetValue))
                throw new InvalidDataException($"Measured dual ABI does not expose {field} field offset.");
            int so=pcOffsetValue.GetInt32(),xo=nativeOffsetValue.GetInt32();
            if(so<0 || so+4>raw.Length || xo<0 || xo+4>xenon.Length)
                throw new InvalidDataException($"Measured {field} offset exceeds source/native root.");
            uint val=BinaryPrimitives.ReadUInt32LittleEndian(originalRoot.Slice(so,4));
            if(val!=(uint)pcs.GetProperty(field).GetInt32())
                throw new InvalidDataException($"Original PC root {field} bytes disagree with typed OAT loaded world.");
            BinaryPrimitives.WriteUInt32BigEndian(xenon.AsSpan(xo,4),val);
            validatedRoot.Add(new(field,so,xo,val));
        }
        var dpvs=raw.AsSpan(pDpvs,DpvsBytes);
        // The beginning of both native 100-byte DPVS structs is a shared,
        // explicitly measured prefix of ten 32-bit scalar counters/ranges.
        // Its pointer-bearing remainder is all zero until real child
        // allocations are planned. This partial header cannot load a world.
        var keys=new[]{"smodelCount","staticSurfaceCount","litSurfsBegin",
            "litSurfsEnd","decalSurfsBegin","decalSurfsEnd",
            "emissiveSurfsBegin","emissiveSurfsEnd",
            "smodelVisDataCount","surfaceVisDataCount"};
        var dpvsFields=new List<AztecGfxWorldSeedField>();
        for(int i=0;i<keys.Length;i++)
        {
            int offset=i*4;
            uint val=BinaryPrimitives.ReadUInt32LittleEndian(dpvs.Slice(offset,4));
            if(keys[i] is "smodelCount" or "staticSurfaceCount" or
                          "smodelVisDataCount" or "surfaceVisDataCount")
            {
                if(val!=(uint)pcs.GetProperty(keys[i]).GetInt32())
                    throw new InvalidDataException($"PC DPVS scalar bytes {keys[i]} disagree with loaded typed OAT value.");
            }
            BinaryPrimitives.WriteUInt32BigEndian(xenon.AsSpan(xDpvs+offset,4),val);
            dpvsFields.Add(new(keys[i],pDpvs+offset,xDpvs+offset,val));
        }
        // Preserve full unmodified original-PC DPVS for later type-aware
        // child graph reconstruction; its raw source pointers are NOT
        // allowed into the Xbox candidate root.
        foreach(var x in validatedRoot)
        {
            uint be=BinaryPrimitives.ReadUInt32BigEndian(xenon.AsSpan(x.XboxOffset,4));
            if(be!=x.Value)throw new InvalidDataException("Xenon root scalar big-endian readback failed.");
        }
        foreach(var d in dpvsFields)
        {
            uint be=BinaryPrimitives.ReadUInt32BigEndian(xenon.AsSpan(d.XboxOffset,4));
            if(be!=d.Value)throw new InvalidDataException("Xenon DPVS scalar big-endian readback failed.");
        }
        // The measured root/DPVS offsets are obeyed. Do not insert any FF
        // pointer; all true name/geometry/material/DPVS children remain null.
        var output=Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var pcRoot=Path.Combine(output,"pc_original_world_root_800B_readonly.bin");
        var pcDpvs=Path.Combine(output,"pc_original_world_dpvs_100B_readonly.bin");
        var xboxRoot=Path.Combine(output,"xenon_world_root_900B_SCALARS_ONLY_NOT_FF.bin");
        await File.WriteAllBytesAsync(pcRoot,raw,ct);
        await File.WriteAllBytesAsync(pcDpvs,dpvs.ToArray(),ct);
        await File.WriteAllBytesAsync(xboxRoot,xenon,ct);

        var result=new AztecPcGfxWorldRootDpvsSeedReport(
            "WaW360.AztecPcGfxWorldRootDpvsSeed.v1",
            "AZTEC_PC_GFXWORLD_800_TO_XBOX_900_ROOT_SCALAR_AND_DPVS_SEED_PASS_NOT_FF",
            Path.GetFullPath(dualAbiWitnessPath),Path.GetFullPath(linked735ReportPath),
            originalPc,pcRoot,pcDpvs,xboxRoot,
            Sha(raw),Sha(dpvs),Sha(xenon),
            PcRootBytes,XboxRootBytes,DpvsBytes, pDpvs,xDpvs,
            735,39,32713,58653,613,11,
            validatedRoot.ToArray(),dpvsFields.ToArray(),
            false,false,false,false,
            "Built from original-PC world root bytes and independently measured " +
            "PC/Xbox OAT ABI. Only four full root scalar fields and ten DPVS " +
            "static scalar fields are endianness-converted. Actual world " +
            "names, indices, vertices, 39 MaterialMemory child entries, " +
            "735 GfxSurface entries, 613 static sortedSurfaces, " +
            "vis-runtime data and the other world child graph are NOT written " +
            "to this root. DO NOT feed this partial header to OAT or Xbox. " +
            "The 735 Material* pointers from the earlier pass are FF-layout " +
            "specific and must be RECOMPUTED after allocating the real world.");
        var report=Path.Combine(output,"aztec_pc_gfxworld_800_to_xenon_900_root_dpvs_seed.json");
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(result,new JsonSerializerOptions{
            WriteIndented=true
        }),ct);
        return result;
    }
    private static bool Eq(string? left,string? right)=>
        string.Equals(left,right,StringComparison.OrdinalIgnoreCase);
    private static string Sha(ReadOnlySpan<byte> b)=>
        Convert.ToHexString(SHA256.HashData(b));
}
public sealed record AztecGfxWorldSeedField(
    string Name,int OriginalPcOffset,int XboxOffset,uint Value);
public sealed record AztecPcGfxWorldRootDpvsSeedReport(
    string Schema,string Status,string DualSourceNativeAbiWitness,
    string Existing735RealMaterialRootLinkReport,
    string OriginalPcFastFile,string OriginalPcRoot800File,
    string OriginalPcDpvs100File,string XboxScalarOnlyRoot900File,
    string OriginalPcRootSha256,string OriginalPcDpvsSha256,
    string XboxScalarOnlyRootSha256,
    int OriginalPcRootSize,int XboxRootSize,int DpvsSize,
    int OriginalPcDpvsRootOffset,int XboxDpvsRootOffset,
    int WorldSurfaces,int WorldMaterials,int WorldVertices,
    int WorldIndices,int StaticSurfaces,int Cells,
    IReadOnlyList<AztecGfxWorldSeedField> SourceVerifiedRootFields,
    IReadOnlyList<AztecGfxWorldSeedField> SourceVerifiedDpvsFields,
    bool FullRootAllFieldsMapped,bool FullWorldChildrenSerialized,
    bool OatGfxWorldLoadValidated,bool XboxHardwareReady,
    string Note);
