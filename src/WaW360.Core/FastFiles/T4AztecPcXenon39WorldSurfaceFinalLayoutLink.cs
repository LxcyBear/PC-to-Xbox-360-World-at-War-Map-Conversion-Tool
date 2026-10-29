using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// The post-39-Material-FF version of the 735 real Xenon GfxSurface
/// pointer linker. Input layout MUST come from the successful, OAT-loaded
/// 39-real-world-Material scratch FF, not the obsolete two-Material canary.
/// Writes only 735 Material* words of a standalone source-derived 76B surface
/// buffer. Does NOT insert GfxWorld, surfaces, DPVS or any pointer into an FF.
/// Re-run after EVERY actual FF layout change.
/// </summary>
public static class T4AztecPcXenon39WorldSurfaceFinalLayoutLink
{
    public static async Task<AztecPcXenon39WorldSurfaceFinalLayoutReport> BuildAsync(
        string surfacesCanaryJson,
        string verifiedMaterialWorklistJson,
        string nativeXboxIdentityJson,
        string actual39WorldScratchFfReport,
        string outputDirectory,
        CancellationToken ct = default)
    {
        using var cd=JsonDocument.Parse(await File.ReadAllTextAsync(surfacesCanaryJson,ct));
        using var wd=JsonDocument.Parse(await File.ReadAllTextAsync(verifiedMaterialWorklistJson,ct));
        using var nd=JsonDocument.Parse(await File.ReadAllTextAsync(nativeXboxIdentityJson,ct));
        using var fd=JsonDocument.Parse(await File.ReadAllTextAsync(actual39WorldScratchFfReport,ct));
        var c=cd.RootElement;
        var w=wd.RootElement;
        var n=nd.RootElement;
        var f=fd.RootElement;
        if(c.GetProperty("Schema").GetString()!="WaW360.AztecPcXenonGfxSurface76Canary.v1" ||
           c.GetProperty("Status").GetString()!="AZTEC_PC_735_XENON_76B_GFXSURFACES_SOURCE_DERIVED_TOPMIP_READBACK_ONLY" ||
           c.GetProperty("Surfaces").GetInt32()!=735 ||
           c.GetProperty("SurfaceStride").GetInt32()!=76 ||
           c.GetProperty("MaterialOffset").GetInt32()!=44 ||
           c.GetProperty("ByteLength").GetInt32()!=55860 ||
           c.GetProperty("Triangles").GetInt32()!=19551 ||
           c.GetProperty("DistinctNamedXboxMaterialRoots").GetInt32()!=39 ||
           !c.GetProperty("TopMipFullySourceProven").GetBoolean() ||
           c.GetProperty("SourceDerivedNativeExactTopMipSurfaces").GetInt32()!=735 ||
           c.GetProperty("UnresolvedTopMipSurfaces").GetInt32()!=0 ||
           c.GetProperty("ZeroMaterialPointerFields").GetInt32()!=735 ||
           c.GetProperty("WrittenMaterialPointerFields").GetInt32()!=0 ||
           c.GetProperty("XenonGfxWorldSerialized").GetBoolean() ||
           c.GetProperty("XboxHardwareReady").GetBoolean() ||
           w.GetProperty("Status").GetString()!="AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY" ||
           w.GetProperty("VerifiedTypedPcSurfaceIdentities").GetInt32()!=735 ||
           w.GetProperty("UniqueGoldenXboxNativeMaterialRootNames").GetInt32()!=39 ||
           w.GetProperty("XenonSurfaceMaterialPointersWritten").GetInt32()!=0 ||
           w.GetProperty("XboxHardwareReady").GetBoolean() ||
           n.GetProperty("format").GetString()!="WaW360.T4Xenon.NativeIdentityManifest.v1" ||
           n.GetProperty("assets").GetInt32()!=8509 ||
           !n.GetProperty("typeSequenceVerified").GetBoolean() ||
           f.GetProperty("schema").GetString()!="WaW360.AztecPc39WorldMaterialScratchFfIntegration.v1" ||
           f.GetProperty("status").GetString()!="AZTEC_PC_39_REAL_WORLD_MATERIAL_ROOTS_IN_SCRATCH_XENON_FF_OAT_PASS" ||
           f.GetProperty("realSourceWorldMaterialRoots").GetInt32()!=39 ||
           f.GetProperty("nativeWorldGfxSurfaceReferencesInFf").GetInt32()!=0 ||
           !f.GetProperty("oatLoaderPass").GetBoolean() ||
           !f.GetProperty("oatZeroWarningsZeroErrors").GetBoolean() ||
           !f.GetProperty("layoutReplayExact").GetBoolean() ||
           !f.GetProperty("xboxNativeRootIdentityExact").GetBoolean() ||
           f.GetProperty("originalXboxIdentityRoots").GetInt32()!=8509 ||
           !f.GetProperty("diagnosticOnly").GetBoolean() ||
           f.GetProperty("hardwareReady").GetBoolean() ||
           f.GetProperty("xenonGfxWorldRootEmitted").GetBoolean() ||
           f.GetProperty("realGfxWorldDpvsLinked").GetBoolean())
            throw new InvalidDataException("Require EXACT independently proven 735 source Xbox 76B surfaces AND passing 39-world-Material OAT scratch FF. No partial world outputs.");

        var emitterFile=f.GetProperty("emitterReport").GetString()!;
        using var ed=JsonDocument.Parse(await File.ReadAllTextAsync(emitterFile,ct));
        var emitter=ed.RootElement;
        if(emitter.GetProperty("NativeRoots").GetInt32()!=8509 ||
           emitter.GetProperty("RealMaterialRoots").GetInt32()!=39 ||
           emitter.GetProperty("RealXModelRoots").GetInt32()!=2 ||
           emitter.GetProperty("RealImageRoots").GetInt32()!=2 ||
           emitter.GetProperty("RealMapEntsRoots").GetInt32()!=1 ||
           emitter.GetProperty("RealComWorldRoots").GetInt32()!=1 ||
           !emitter.GetProperty("OatZeroWarningsZeroErrors").GetBoolean() ||
           !emitter.GetProperty("ValidationPass").GetBoolean() ||
           !emitter.GetProperty("ExactIdentitySequence").GetBoolean() ||
           !emitter.GetProperty("LayoutReplayExact").GetBoolean() ||
           !emitter.GetProperty("ValidationOnly").GetBoolean() ||
           emitter.GetProperty("ReadyForHardware").GetBoolean())
            throw new InvalidDataException("Live emitter 39-Material source FF proof does not agree with the recorded PASS.");

        var actualFF=Path.GetFullPath(emitter.GetProperty("OutputFastFile").GetString()!);
        if(!File.Exists(actualFF) ||
           !Path.GetFullPath(f.GetProperty("smokeTestFfPath").GetString()!)
            .Equals(actualFF,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The exact 39-Material scratch FF being linked is missing or has moved.");

        var actualZone=await FastFileAnalysisCache.GetZoneAsync(actualFF,ct);
        if(actualZone.Platform != WaWPlatform.Xbox360 ||
           actualZone.Document.AssetCount!=8509)
            throw new InvalidDataException("Actual scratch FF independently failed native Xbox root count/platform re-read.");
        var ffSha=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(actualFF,ct)));

        var layoutFile=Path.Combine(Path.GetDirectoryName(emitterFile)!,"layout_recheck",
            "nuclear_xenon_block_layout.json");
        using var ld=JsonDocument.Parse(await File.ReadAllTextAsync(layoutFile,ct));
        var layout=ld.RootElement;
        if(!layout.GetProperty("LayoutReady").GetBoolean() ||
           layout.GetProperty("Blockers").GetArrayLength()!=0 ||
           layout.GetProperty("NativeRoots").GetInt32()!=8509 ||
           layout.GetProperty("RealMaterialRoots").GetInt32()!=39 ||
           layout.GetProperty("RealImageRoots").GetInt32()!=2 ||
           layout.GetProperty("RealXModelRoots").GetInt32()!=2)
            throw new InvalidDataException("The exact OAT-tested scratch FF Stage 4D layout does not contain 39 real world Material graphs.");

        using var md=JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.GetFullPath(f.GetProperty("originalPcMaterialGraphCandidateReport").GetString()!),ct));
        var materials=md.RootElement;
        if(materials.GetProperty("Status").GetString()!="AZTEC_39_PC_MATERIALS_TO_XENON_96B_SOURCE_GRAPH_CANDIDATES_ONLY" ||
           materials.GetProperty("SourceMaterialChildrenConverted").GetInt32()!=39 ||
           materials.GetProperty("CoveredWorldSurfaces").GetInt32()!=735 ||
           materials.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Underlying PC-derived Xenon Material graph source provenance changed.");

        var source=await File.ReadAllBytesAsync(c.GetProperty("BinaryFile").GetString()!,ct);
        if(source.Length!=55860 || !Sha(source).Equals(
            c.GetProperty("BinarySha256").GetString(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("735 exact PC-derived 76B Xbox GfxSurface input differs from prior SHA.");

        var roots=n.GetProperty("entries").EnumerateArray().ToArray();
        var plans=layout.GetProperty("RootInsertLayouts").EnumerateArray().ToArray();
        var sites=c.GetProperty("MaterialRelocationSites").EnumerateArray().ToArray();
        var intents=w.GetProperty("RelocationIntents").EnumerateArray().ToArray();
        var owners=materials.GetProperty("Materials").EnumerateArray().ToArray();
        if(roots.Length!=8509 || plans.Length!=8509 || sites.Length!=735 ||
           intents.Length!=735 || owners.Length!=39)
            throw new InvalidDataException("Original-PC-to-Xenon source/FF layout inventory drift.");

        var realMaterialRoots=new Dictionary<int,string>();
        foreach(var owner in owners)
        {
            int idx=owner.GetProperty("GoldenXboxRootIndex").GetInt32();
            string name=owner.GetProperty("VerifiedMaterialName").GetString()!;
            if(idx<0 || idx>=8509 ||
               !realMaterialRoots.TryAdd(idx,name) ||
               roots[idx].GetProperty("Index").GetInt32()!=idx ||
               roots[idx].GetProperty("TypeId").GetInt32()!=6 ||
               !string.Equals(roots[idx].GetProperty("Name").GetString(),name,StringComparison.Ordinal) ||
               owner.GetProperty("RealXenonWorldMaterialRootWritten").GetBoolean())
                throw new InvalidDataException($"39 FF material corpus/native root ownership mismatch at #{idx}.");
        }
        if(realMaterialRoots.Count!=39)
            throw new InvalidDataException("Expected exactly 39 source-owned REAL world Material graph roots.");

        byte[] linked=(byte[])source.Clone();
        var found=new Dictionary<int,(string Name,uint Pointer,int Surfaces,int Triangles)>();
        var surfaceRows=new List<AztecPcXenon39WorldLinkedSurface>(735);
        for(int i=0;i<735;i++)
        {
            ct.ThrowIfCancellationRequested();
            var site=sites[i];
            var intent=intents[i];
            int id=site.GetProperty("UniqueGoldenXboxRootIndex").GetInt32();
            var name=site.GetProperty("VerifiedPcMaterialName").GetString()!;
            int offset=site.GetProperty("FieldOffsetInCanaryBuffer").GetInt32();
            int tri=site.GetProperty("Triangles").GetInt32();
            if(!realMaterialRoots.TryGetValue(id,out var expectedName) ||
               !string.Equals(name,expectedName,StringComparison.Ordinal) ||
               id!=intent.GetProperty("UniqueMatchingXboxNativeRootIndex").GetInt32() ||
               i!=site.GetProperty("SurfaceIndex").GetInt32() ||
               i!=intent.GetProperty("SurfaceIndex").GetInt32() ||
               tri!=intent.GetProperty("TriangleCount").GetInt32() ||
               offset!=i*76+44 ||
               !site.GetProperty("XboxTopMipSourceProven").GetBoolean() ||
               site.GetProperty("MaterialPointerWritten").GetBoolean() ||
               intent.GetProperty("XenonMaterialPointerWritten").GetBoolean() ||
               !intent.GetProperty("PcLoadedMaterialPointerIdentityProven").GetBoolean() ||
               !intent.GetProperty("XboxMaterialRootNameIdentityUnique").GetBoolean() ||
               plans[id].GetProperty("RootIndex").GetInt32()!=id ||
               !string.Equals(plans[id].GetProperty("Name").GetString(),name,StringComparison.Ordinal) ||
               BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset,4))!=0)
                throw new InvalidDataException($"World surface #{i} does not match all 39 source-derived real FF root ownership prerequisites.");

            long slot=plans[id].GetProperty("InsertOffset").GetInt64();
            if(slot<0 || slot>0x1FFFFFFF)
                throw new InvalidDataException("Actual 39-Material FF root insertion slot out of packed VIRTUAL range.");
            var pointer=T4ZonePointer.Encode(T4ZoneBlock.Virtual,checked((uint)slot));
            var decoded=T4ZonePointer.Decode(pointer);
            if(pointer!=plans[id].GetProperty("StablePointer").GetUInt32() ||
               decoded.Kind!=T4ZonePointerKind.Offset ||
               decoded.Block!=T4ZoneBlock.Virtual ||
               decoded.Offset!=slot)
                throw new InvalidDataException($"Actual 39-material FF root VIRTUAL slot has invalid stable packed pointer, id={id}.");

            BinaryPrimitives.WriteUInt32BigEndian(linked.AsSpan(offset,4),pointer);
            if(BinaryPrimitives.ReadUInt32BigEndian(linked.AsSpan(offset,4))!=pointer)
                throw new InvalidDataException("BE source surface live Material pointer encoding readback failed.");

            if(found.TryGetValue(id,out var prior))
                found[id]=(name,pointer,prior.Surfaces+1,prior.Triangles+tri);
            else found.Add(id,(name,pointer,1,tri));
            surfaceRows.Add(new(i,offset,id,name,$"0x{pointer:X8}",tri,
                true,"SOURCE_76B_SURFACE_POINTER_TO_REAL_39_MATERIAL_FF_ROOT_LAYOUT"));
        }

        if(found.Count!=39 || surfaceRows.Count!=735 ||
           found.Sum(x=>x.Value.Surfaces)!=735 ||
           found.Sum(x=>x.Value.Triangles)!=19551)
            throw new InvalidDataException("39/39 real material FF roots and 735 surfaces do not have exact source joins.");
        for(int i=0;i<linked.Length;i++)
        {
            if(i%76 is >=44 and <48)continue;
            if(linked[i]!=source[i])
                throw new InvalidDataException("Rebased FF pointer linker modified original PC geometry or topMip bytes.");
        }

        var dir=Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(dir);
        var binary=Path.Combine(dir,"aztec_735_surfaces_76byte_BE_LINKED_TO_REAL_39_FF_LAYOUT_NOT_IN_FF.bin");
        await File.WriteAllBytesAsync(binary,linked,ct);
        var output=new AztecPcXenon39WorldSurfaceFinalLayoutReport(
            "WaW360.AztecPcXenon39WorldSurfaceFinalLayoutLink.v1",
            "AZTEC_735_SURFACE_MATERIAL_REFS_TO_39_REAL_XBOX_FF_MATERIAL_ROOTS_REBASED_STANDALONE_PASS",
            actualFF,ffSha,layoutFile,
            Path.GetFullPath(surfacesCanaryJson),Path.GetFullPath(verifiedMaterialWorklistJson),
            8509,39,735,19551,76,44,55860,39,0,735,0,
            binary,Sha(linked),
            found.OrderBy(x=>x.Key).Select(x=>new AztecPcXenon39WorldRootLink(
                x.Key,x.Value.Name,$"0x{x.Value.Pointer:X8}",
                x.Value.Surfaces,x.Value.Triangles)).ToArray(),
            surfaceRows.ToArray(),
            false,false,false,false,
            "All 735 MATERIAL* sites of original PC-derived Xbox-ABI 76B surfaces " +
            "now contain BE packed VIRTUAL INSERT-slot references to the 39 REAL, " +
            "OAT-loaded Material graph bodies in the EXACT 8509-root SCRATCH FF " +
            "whose SHA is pinned here. Not yet in FF: this linked surface buffer " +
            "is STANDALONE; re-run this link after ANY FF allocation change. " +
            "Original Xbox topMip source values remain unchanged and exact. " +
            "GfxWorld/DPVS + stateBits semantics and full runtime dependencies " +
            "not yet verified. NO DEPLOYMENT.");
        var report=Path.Combine(dir,"aztec_735_world_surfaces_linked_to_39_ff_material_roots.json");
        await File.WriteAllTextAsync(report,
            JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}),ct);
        return output;
    }
    private static string Sha(ReadOnlySpan<byte> b)=>
        Convert.ToHexString(SHA256.HashData(b));
}

public sealed record AztecPcXenon39WorldRootLink(
    int RootIndex,string MaterialName,string FfSpecificPackedVirtualPointer,
    int ReferencingWorldSurfaces,int TriangleCount);
public sealed record AztecPcXenon39WorldLinkedSurface(
    int SurfaceIndex,int MaterialFieldOffset,int NativeRootIndex,
    string VerifiedMaterialName,string MaterialPointer,int Triangles,
    bool RealMaterialGraphInVerifiedFf,string Status);
public sealed record AztecPcXenon39WorldSurfaceFinalLayoutReport(
    string Schema,string Status,string ExactValidated39MaterialFastFile,
    string ExactValidated39MaterialFastFileSha256,string ActualFfBlockLayout,
    string VerifiedSource76SurfaceCanary,string MaterialRelocationWorklist,
    int NativeXboxRoots,int RealFfMaterialBodies,int SourceOwnedXboxSurfaceCount,
    int TriangleCount,int GfxSurfaceXboxStride,int GfxSurfaceMaterialFieldOffset,
    int LinkedSurfaceByteLength,int MaterialRootsWithRealFfGraphBody,
    int MaterialIdentityShellTargets,int SurfacesLinkedToRealFfBodies,
    int SurfacesLinkedToIdentityShells,
    string StandaloneLinkedSurfaceArray,string StandaloneLinkedSurfaceArraySha256,
    IReadOnlyList<AztecPcXenon39WorldRootLink> MaterialRootTargets,
    IReadOnlyList<AztecPcXenon39WorldLinkedSurface> Surfaces,
    bool WorldGfxSurfaceArrayInsertedInFf,bool GfxWorldRootSerialized,
    bool OatValidatedGfxWorldLoad,bool XboxHardwareReady,string Note);
