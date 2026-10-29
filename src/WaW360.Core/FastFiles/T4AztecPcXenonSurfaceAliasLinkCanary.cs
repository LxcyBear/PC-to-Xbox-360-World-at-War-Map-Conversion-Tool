using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Stages valid packed VIRTUAL INSERT-slot pointers to the 39 native Xbox
/// Material ROOT identities into a copy of 735 fully source-derived 76B
/// T4Xenon surfaces. The six-family Aztec FF presently emits only TWO
/// unrelated real Material bodies; root existence != a real Material graph.
/// This emits NO FF, never claims runtime binding, and explicitly inventories
/// which target roots are only identity-shell placeholders.
/// </summary>
public static class T4AztecPcXenonSurfaceAliasLinkCanary
{
    public static async Task<AztecPcXenonSurfaceAliasLinkReport> BuildAsync(
        string canaryReport, string materialWorklist, string nativeIdentity,
        string stage4dLayout, string materialBinding, string outputDirectory,
        CancellationToken ct = default)
    {
        using var cd=JsonDocument.Parse(await File.ReadAllTextAsync(canaryReport,ct));
        using var wd=JsonDocument.Parse(await File.ReadAllTextAsync(materialWorklist,ct));
        using var nd=JsonDocument.Parse(await File.ReadAllTextAsync(nativeIdentity,ct));
        using var ld=JsonDocument.Parse(await File.ReadAllTextAsync(stage4dLayout,ct));
        using var bd=JsonDocument.Parse(await File.ReadAllTextAsync(materialBinding,ct));
        var c=cd.RootElement; var w=wd.RootElement; var n=nd.RootElement;
        var l=ld.RootElement; var bind=bd.RootElement;
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
           l.GetProperty("NativeRoots").GetInt32()!=8509 ||
           !l.GetProperty("LayoutReady").GetBoolean() ||
           l.GetProperty("Blockers").GetArrayLength()!=0 ||
           l.GetProperty("RealMaterialRoots").GetInt32()<0 ||
           bind.GetProperty("Format").GetString()!="WaW360.AztecPcTwoMaterialBinding.v1" ||
           !bind.GetProperty("BindingPass").GetBoolean() ||
           !bind.GetProperty("ValidationOnly").GetBoolean())
           throw new InvalidDataException("Require exact pinned 76B source-derived surfaces, native 8,509 root identity, passing existing six-family zone layout and two-Material scratch-corpus contract.");

        var file=c.GetProperty("BinaryFile").GetString()!;
        byte[] src=await File.ReadAllBytesAsync(file,ct);
        if(src.Length!=55860 ||
           !Sha(src).Equals(c.GetProperty("BinarySha256").GetString(),StringComparison.OrdinalIgnoreCase))
           throw new InvalidDataException("Source-derived 76B Xbox ABI surface buffer was modified.");
        var planned=l.GetProperty("RootInsertLayouts").EnumerateArray().ToArray();
        var roots=n.GetProperty("entries").EnumerateArray().ToArray();
        var bindingRows=bind.GetProperty("OwnedBindings").EnumerateArray().ToArray();
        if(planned.Length!=8509 || roots.Length!=8509 ||
           bindingRows.Length!=l.GetProperty("RealMaterialRoots").GetInt32() ||
           l.GetProperty("RealMaterialRoots").GetInt32()!=2)
           throw new InvalidDataException("Existing six-family graph/layout is not the expected pinned two-real-Material canary.");
        var emitted=new HashSet<int>();
        foreach(var row in bindingRows)
        {
            int idx=row.GetProperty("NativeRootIndex").GetInt32();
            if(idx<0||idx>=8509 || !emitted.Add(idx) ||
               !string.Equals(roots[idx].GetProperty("Name").GetString(),
                   row.GetProperty("NativeName").GetString(),StringComparison.OrdinalIgnoreCase) ||
               roots[idx].GetProperty("TypeId").GetInt32()!=6)
                throw new InvalidDataException("Real two-Material graph owned native roots disagree with pinned native identity.");
        }

        var sites=c.GetProperty("MaterialRelocationSites").EnumerateArray().ToArray();
        var work=w.GetProperty("RelocationIntents").EnumerateArray().ToArray();
        if(sites.Length!=735 || work.Length!=735)
            throw new InvalidDataException("Missing original-PC 735 source verified world surface pointers.");

        var output=(byte[])src.Clone();
        var groups=new Dictionary<int,(string Name,uint Packed,int Surfaces,int Triangles,bool Real)>();
        var entries=new List<AztecPcXenonSurfaceAliasLinkEntry>(735);
        for(int i=0;i<735;i++)
        {
            ct.ThrowIfCancellationRequested();
            var site=sites[i];var proof=work[i];
            int idx=site.GetProperty("UniqueGoldenXboxRootIndex").GetInt32();
            var name=site.GetProperty("VerifiedPcMaterialName").GetString()!;
            int at=site.GetProperty("FieldOffsetInCanaryBuffer").GetInt32();
            if(idx<0 || idx>=8509 || at!=i*76+44 ||
               site.GetProperty("SurfaceIndex").GetInt32()!=i ||
               proof.GetProperty("SurfaceIndex").GetInt32()!=i ||
               proof.GetProperty("UniqueMatchingXboxNativeRootIndex").GetInt32()!=idx ||
               site.GetProperty("Triangles").GetInt32()!=proof.GetProperty("TriangleCount").GetInt32() ||
               !site.GetProperty("XboxTopMipSourceProven").GetBoolean() ||
               site.GetProperty("MaterialPointerWritten").GetBoolean() ||
               proof.GetProperty("XenonMaterialPointerWritten").GetBoolean() ||
               !proof.GetProperty("PcLoadedMaterialPointerIdentityProven").GetBoolean() ||
               !proof.GetProperty("XboxMaterialRootNameIdentityUnique").GetBoolean() ||
               !string.Equals(name,proof.GetProperty("VerifiedPcMaterialName").GetString(),StringComparison.Ordinal) ||
               roots[idx].GetProperty("Index").GetInt32()!=idx ||
               roots[idx].GetProperty("TypeId").GetInt32()!=6 ||
               !string.Equals(roots[idx].GetProperty("Name").GetString(),name,StringComparison.Ordinal) ||
               planned[idx].GetProperty("RootIndex").GetInt32()!=idx ||
               !string.Equals(planned[idx].GetProperty("Name").GetString(),name,StringComparison.Ordinal) ||
               BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(at,4))!=0)
                throw new InvalidDataException($"GfxSurface#{i} native root identity or unlinked Xbox 76B material site mismatch.");

            long insert=planned[idx].GetProperty("InsertOffset").GetInt64();
            if(insert<0 || insert>0x1FFFFFFF || (insert&3)!=0)
                throw new InvalidDataException($"Invalid planned VIRTUAL INSERT slot at native material root #{idx}.");
            var packed=T4ZonePointer.Encode(T4ZoneBlock.Virtual,checked((uint)insert));
            if(planned[idx].GetProperty("StablePointer").GetUInt32()!=packed ||
               T4ZonePointer.Decode(packed).Kind!=T4ZonePointerKind.Offset ||
               T4ZonePointer.Decode(packed).Block!=T4ZoneBlock.Virtual ||
               T4ZonePointer.Decode(packed).Offset!=insert)
                throw new InvalidDataException($"Xenon packed-zone root alias encoding mismatch at material root #{idx}.");
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at,4),packed);
            var reloaded=BinaryPrimitives.ReadUInt32BigEndian(output.AsSpan(at,4));
            if(reloaded!=packed)
                throw new InvalidDataException($"Failed BE Xenon material stable pointer readback at surface#{i}.");

            bool real=emitted.Contains(idx);
            if(groups.TryGetValue(idx,out var prior))
                groups[idx]=(name,packed,prior.Surfaces+1,prior.Triangles+site.GetProperty("Triangles").GetInt32(),real);
            else
                groups.Add(idx,(name,packed,1,site.GetProperty("Triangles").GetInt32(),real));
            entries.Add(new(i,at,name,idx,$"0x{packed:X8}",real,real
                ? "REAL_MATERIAL_GRAPH_ROOT_IN_EXISTING_SIX_FAMILY_LAYOUT"
                : "IDENTITY_SHELL_ONLY_NO_MATERIAL_GRAPH"));
        }
        if(groups.Count!=39 || entries.Count!=735 ||
           groups.Sum(x=>x.Value.Triangles)!=19551 ||
           groups.Sum(x=>x.Value.Surfaces)!=735)
            throw new InvalidDataException("39 world material root / 735 surface join coverage disagrees.");

        for(int i=0;i<src.Length;i++)
        {
            bool materialByte=(i%76)>=44 && (i%76)<48;
            if(!materialByte && src[i]!=output[i])
                throw new InvalidDataException("Alias linker mutated non-material geometry/topMip/flags/bounds bytes.");
        }

        int realTargets=groups.Count(x=>x.Value.Real);
        int realSurfaces=groups.Where(x=>x.Value.Real).Sum(x=>x.Value.Surfaces);
        var dir=Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(dir);
        var binaryPath=Path.Combine(dir,"aztec_735_gfxsurfaces_76byte_BE_ALIAS_LINK_ONLY_NOT_FOR_XBOX.bin");
        await File.WriteAllBytesAsync(binaryPath,output,ct);
        var reportPath=Path.Combine(dir,"aztec_735_gfxsurfaces_material_root_alias_link_audit.json");
        var details=groups.OrderBy(x=>x.Key).Select(x=>
            new AztecPcXenonSurfaceRootTarget(
                x.Key,x.Value.Name,$"0x{x.Value.Packed:X8}",
                x.Value.Surfaces,x.Value.Triangles,x.Value.Real)).ToArray();
        var result=new AztecPcXenonSurfaceAliasLinkReport(
            "WaW360.AztecPcXenonSurfaceAliasLinkCanary.v1",
            "AZTEC_735_XENON_MATERIAL_ROOT_ALIAS_PACKED_POINTERS_DIAGNOSTIC_ONLY",
            Path.GetFullPath(canaryReport),Path.GetFullPath(materialWorklist),
            Path.GetFullPath(stage4dLayout),Path.GetFullPath(materialBinding),
            8509,735,39,19551,55860,76,44,
            realTargets,39-realTargets,realSurfaces,735-realSurfaces,
            l.GetProperty("RealMaterialRoots").GetInt32(),
            binaryPath,Sha(output),details,entries.ToArray(),
            false,false,false,false,
            "Every one of 735 four-byte BE Material* words now contains an actual " +
            "T4 packed VIRTUAL pointer to its native Material root INSERT ALIAS slot " +
            "in the *existing specific* Stage4D six-family planned zone allocation. " +
            "Such encoding is NOT enough for a real Material body: the baseline " +
            "only emits two unrelated real Material graph roots, while the 39 world " +
            "targets may be identity shells. Planned pointers also become INVALID " +
            "if future allocations change. No new zone root/DPVS, no FF, no OAT " +
            "roundtrip and NO Xbox release. Do NOT reuse this binary in another layout.");
        await File.WriteAllTextAsync(reportPath,
            JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),ct);
        return result;
    }

    private static string Sha(ReadOnlySpan<byte> b)=>Convert.ToHexString(SHA256.HashData(b));
}

public sealed record AztecPcXenonSurfaceRootTarget(
    int NativeMaterialRootIndex,string MaterialName,string Stage4DPackedPointer,
    int SurfaceCount,int TriangleCount,bool RealOwnedMaterialGraph);

public sealed record AztecPcXenonSurfaceAliasLinkEntry(
    int SurfaceIndex,int XboxMaterialFieldOffset,string MaterialName,
    int NativeMaterialRootIndex,string Stage4DPackedPointer,
    bool TargetIsRealMaterialGraph,string TargetStatus);

public sealed record AztecPcXenonSurfaceAliasLinkReport(
    string Schema,string Status,
    string OriginalXbox76SurfaceCanary,string VerifiedPcMaterialIdentityWorklist,
    string SpecificStage4DLayout,string MaterialOwnedGraphBindings,
    int NativeRoots,int TotalSurfaceMaterialReferences,int DistinctWorldMaterialRoots,
    int CoveredTriangles,int ByteLength,int XboxGfxSurfaceStride,int XboxMaterialOffset,
    int WorldRootsWithRealMaterialGraph,int WorldRootsWithIdentityShellOnly,
    int SurfaceReferencesToRealMaterialGraphs,int SurfaceReferencesToIdentityShells,
    int RealMaterialRootsElsewhereInLayout,
    string PseudoLinkedBinaryFile,string PseudoLinkedBinarySha256,
    IReadOnlyList<AztecPcXenonSurfaceRootTarget> Targets,
    IReadOnlyList<AztecPcXenonSurfaceAliasLinkEntry> LinkedSurfaceSites,
    bool RealXboxGfxWorldRootEmitted,bool All39WorldMaterialGraphsEmitted,
    bool IntegratedOatGfxWorldLoaded,bool XboxHardwareReady,string Note);
