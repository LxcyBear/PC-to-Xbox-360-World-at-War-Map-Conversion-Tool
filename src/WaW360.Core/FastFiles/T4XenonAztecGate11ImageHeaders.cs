using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Two source-owned GfxReflectionProbe 16B headers and two 8B lightmap
/// reference headers in a diagnostic Xbox FF. Original-PC float origins are
/// converted to native big endian; ALL six GfxImage* remain NULL because
/// source-generated/star-prefixed image payload ownership is NOT proven.
/// Distinct GfxWorld streamInfo has measured EMPTY 0/0/null child shape.
/// This is NOT a native texture convertor, pixel source or hardware map.
/// </summary>
public sealed class T4XenonAztecGate11ImageHeaders
{
    private readonly byte[] _probeHeads;
    private readonly byte[] _lightmapHeads;
    private readonly int _probeCountAt,_probeHeadsAt,_lightmapCountAt,_lightmapHeadsAt;
    // Optional diagnostic only: 6 exact native GfxImage root INSERT aliases.
    // Most target roots are name-only shells, NOT usable Xbox pixel bodies.
    private readonly int[]? _imageRootIndices;
    private bool _referencePointersApplied;
    public int ReflectionProbeHeaders => _probeHeads.Length / 16;
    public int LightmapHeaders => _lightmapHeads.Length / 8;
    public int VerifiedImagePointerBodies => 0;
    public int NamedImageRootLinkProbeCount => _imageRootIndices?.Length ?? 0;

    private T4XenonAztecGate11ImageHeaders(byte[] probes, byte[] lightmaps,
        int probeCount, int probePointer, int lightmapCount, int lightmapPointer,
        int[]? imageRootIndices)
    {
        _probeHeads=probes;_lightmapHeads=lightmaps;
        _probeCountAt=probeCount;_probeHeadsAt=probePointer;
        _lightmapCountAt=lightmapCount;_lightmapHeadsAt=lightmapPointer;
        _imageRootIndices=imageRootIndices;
    }

    private static void Demand(bool correct,string why)
    {
        if(!correct)throw new InvalidDataException("Gate11 image header probe: "+why);
    }
    private static bool Same(string? x,string? y)=>
        string.Equals(x,y,StringComparison.Ordinal);
    private static string Sha(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));

    public static async Task<T4XenonAztecGate11ImageHeaders> LoadAsync(
        string combinedImageStreamReport, byte[] root,
        IReadOnlyList<T4XenonNativeIdentityEntry> nativeRoots,
        bool linkNameOnlyRootShells=false, CancellationToken ct=default)
    {
        using var rd=JsonDocument.Parse(await File.ReadAllTextAsync(combinedImageStreamReport,ct));
        var r=rd.RootElement;
        Demand(r.GetProperty("schema").GetString()=="WaW360.AztecGate11JointImageSourceAndStreamStructuralFrontier.v1" &&
               r.GetProperty("status").GetString()=="AZTEC_GATE11_SIX_IMAGE_NAMES_NATIVE_REFERENCE_MATCH_AND_NATIVE_STREAM_EMPTY_STRUCT_PROVEN_PAYLOAD_PENDING" &&
               r.GetProperty("previouslyLoadedSourceOwnedWorldSha256").GetString()?.
                   Equals("B832EDCE7EECFBBF2B0A141FF2C3D946595B317D7E7C7ACA0D21991277522883",
                       StringComparison.OrdinalIgnoreCase)==true &&
               r.GetProperty("originalAndCurrentNativeEmptyStreamStructsMatch").GetBoolean() &&
               r.GetProperty("originalAndNativeReflectionLightmapNamesMatch").GetBoolean() &&
               r.GetProperty("nativeReferenceImageNameMatches").GetInt32()==6 &&
               r.GetProperty("nativeReferenceImagePointersPresent").GetInt32()==6 &&
               r.GetProperty("imageFieldsWithoutAvailableOwnedPixelProof").GetInt32()==6 &&
               !r.GetProperty("sourceImagePayloadGraftCompleted").GetBoolean() &&
               !r.GetProperty("sourceNativeStreamingReconstructionCompleted").GetBoolean() &&
               !r.GetProperty("fullGate11Passed").GetBoolean() &&
               !r.GetProperty("xboxHardwareReady").GetBoolean(),
               "the exact previous FF, structural zero-stream, six loaded native image names, or fail-closed payload fences changed.");

        var srcRows=r.GetProperty("originalPcToNativeXboxImageReferences").EnumerateArray().ToArray();
        Demand(srcRows.Length==6 && srcRows.All(x=>
             x.GetProperty("exactNameMatch").GetBoolean() &&
             x.GetProperty("nativePointerPresent").GetBoolean() &&
             x.GetProperty("sourceAndNativeOriginMatch").GetBoolean() &&
             !x.GetProperty("pixelsOrMipDataCompared").GetBoolean()),
             "original-PC and native Xbox names/reflect origins are not independently proved.");

        var nativeDir=Path.GetDirectoryName(
            Path.GetDirectoryName(Path.GetFullPath(combinedImageStreamReport)))!;
        // Rather than copying the original Xbox world root, consume only
        // our already exported SHA-locked original-PC typed GfxWorld graph.
        var sourceManifest=Path.Combine(nativeDir,"pc_g11_cells_740",
            "gate11_remaining_unified_source","aztec_gate11_exact_asset_reference_audit.json");
        Demand(File.Exists(sourceManifest),"original PC image name manifest missing.");
        using var bd=JsonDocument.Parse(await File.ReadAllTextAsync(sourceManifest,ct));
        var b=bd.RootElement;
        Demand(b.GetProperty("schema").GetString() ==
               "WaW360.AztecGate11OriginalPcWorldAssetExactNameBindingAudit.v1" &&
               b.GetProperty("uniqueExactImageRootNameCandidates").GetInt32()>=6 &&
               !b.GetProperty("rootReferencesRebasedInNewNativeFf").GetBoolean() &&
               !b.GetProperty("hardwareReady").GetBoolean(),
               "source image name/identity audit not trusted.");
        var sourcePath=b.GetProperty("sourceOnlyTypedGraph").GetString()!;
        var bytes=await File.ReadAllBytesAsync(sourcePath,ct);
        Demand(Sha(bytes).Equals(b.GetProperty("sourceOnlyTypedGraphSha256").GetString(),
            StringComparison.OrdinalIgnoreCase),"original PC typed world source SHA drifted.");
        using var src=JsonDocument.Parse(bytes.Length>2 && bytes[0]==0xEF &&
            bytes[1]==0xBB && bytes[2]==0xBF?bytes[3..]:bytes);
        var source=src.RootElement;
        var ps=source.GetProperty("reflectionProbeRefs").EnumerateArray().ToArray();
        var lm=source.GetProperty("lightmapRefs").EnumerateArray().ToArray();
        Demand(ps.Length==2 && lm.Length==2 &&
               source.GetProperty("counts").GetProperty("coronas").GetInt32()==0,
               "original PC reflection/lightmap counts or expected zero-corona child order drifted.");
        var headers=new byte[32];
        var lightmapHeaders=new byte[16];
        for(int i=0;i<2;i++)
        {
            var probe=ps[i];
            Demand(probe.GetProperty("index").GetInt32()==i &&
                   Same(probe.GetProperty("image").GetString(),
                        $"*reflection_probe{i}"),"source-generated reflection image name/order drifted.");
            var xyz=probe.GetProperty("origin").EnumerateArray().ToArray();
            Demand(xyz.Length==3,"original-PC reflection probe xyz length changed.");
            for(int ax=0;ax<3;ax++)
            {
                var fl=xyz[ax].GetSingle();
                Demand(float.IsFinite(fl) && Math.Abs(fl)<100000000f,
                    "original-PC reflection probe coordinate invalid.");
                BinaryPrimitives.WriteUInt32BigEndian(
                    headers.AsSpan(i*16+ax*4,4),
                    BitConverter.SingleToUInt32Bits(fl));
            }
            // offset 12 of each GfxReflectionProbe is NULL: no GfxImage
            // payload is proven, so no fake PARENT/FOLLOWING pointer.
            var light=lm[i];
            Demand(light.GetProperty("index").GetInt32()==i &&
                Same(light.GetProperty("primary").GetString(),$"*lightmap{i}_primary") &&
                Same(light.GetProperty("secondary").GetString(),$"*lightmap{i}_secondary"),
                "original-PC generated lightmap name/order drifted.");
        }
        // Native ABI struct offsets are contained in the pinned original-PC
        // source report and were independently sizeof/offsetof witnessed.
        var nativePrepPath=Path.Combine(nativeDir,"pc_g11_cells_740",
            "gate11_remaining_unified_source","aztec_gate11_combined_no_ff_build_preflight.json");
        using var pd=JsonDocument.Parse(await File.ReadAllTextAsync(nativePrepPath,ct));
        var leafPath=pd.RootElement.GetProperty("nativeEndiannessLeafReport").GetString()!;
        using var ld=JsonDocument.Parse(await File.ReadAllTextAsync(leafPath,ct));
        var readPath=ld.RootElement.GetProperty("sourceReadinessReport").GetString()!;
        using var sd=JsonDocument.Parse(await File.ReadAllTextAsync(readPath,ct));
        var readiness=sd.RootElement;
        var abiFile=readiness.GetProperty("nativeXboxAbiOnlyFile").GetString()!;
        var abiBytes=await File.ReadAllBytesAsync(abiFile,ct);
        Demand(Sha(abiBytes).Equals(readiness.GetProperty("nativeXboxAbiOnlySha256").GetString(),
            StringComparison.OrdinalIgnoreCase),"Xbox native structural ABI document changed.");
        using var ad=JsonDocument.Parse(abiBytes.Length>=3 && abiBytes[0]==0xEF &&
            abiBytes[1]==0xBB && abiBytes[2]==0xBF?abiBytes[3..]:abiBytes);
        var abi=ad.RootElement;
        Demand(abi.GetProperty("schema").GetString()=="WaW360.T4XenonGate11RemainingWorldNativeAbi.v1" &&
               abi.GetProperty("gfxWorldBytes").GetInt32()==900 &&
               abi.GetProperty("reflectionProbeBytes").GetInt32()==16 &&
               abi.GetProperty("reflectionProbeOriginAt").GetInt32()==0 &&
               abi.GetProperty("reflectionProbeImageAt").GetInt32()==12 &&
               abi.GetProperty("lightmapArrayBytes").GetInt32()==8 &&
               abi.GetProperty("lightmapPrimaryAt").GetInt32()==0 &&
               abi.GetProperty("lightmapSecondaryAt").GetInt32()==4 &&
               abi.GetProperty("streamInfoBytes").GetInt32()==16 &&
               !abi.GetProperty("containsOriginalXboxPayload").GetBoolean(),
               "measured Xbox 16-byte reflection/8-byte lightmap STRUCT ABI changed.");
        int pc=abi.GetProperty("reflectionProbeCountAt").GetInt32();
        int pp=abi.GetProperty("reflectionProbesAt").GetInt32();
        int lc=abi.GetProperty("lightmapCountAt").GetInt32();
        int lp=abi.GetProperty("lightmapsAt").GetInt32();
        int streamAt=abi.GetProperty("worldStreamInfoAt").GetInt32();
        Demand(root.Length==900 && pc>0 && pp>0 && lc>0 && lp>0 &&
               pc+4<=900 && pp+4<=900 && lc+4<=900 && lp+4<=900 &&
               streamAt>=0 && streamAt+16<=900,
               "world reflection/lightmap/stream fields outside measured 900B ABI.");
        // Fail closed: the current seed and source-owned light/shadow FF
        // contain NONE of these children. Blank streamInfo is intentional
        // only because native Xbox 0/0/null is now independently witnessed.
        foreach(int off in new[]{pc,pp,lc,lp})
            Demand(BinaryPrimitives.ReadUInt32BigEndian(root.AsSpan(off,4))==0,
                "existing world image slots are not zero/null; refusing overwrite.");
        Demand(root.AsSpan(streamAt,16).ToArray().All(x=>x==0),
            "streamInfo is not the expected native Xbox empty 0/0/null struct.");
        int[]? imageIndices=null;
        if(linkNameOnlyRootShells)
        {
            // This is intentionally a SOURCE ROOT REFERENCE/POINTER probe.
            // Native image roots have name shells but do NOT carry original-PC
            // generated reflection/lightmap pixel payloads. Never approve
            // hardware or claim any genuine source-owned GfxImage body.
            Demand(nativeRoots.Count==8509,"native 8509-root identity catalog changed.");
            var rows=r.GetProperty("imageFields").EnumerateArray().ToArray();
            Demand(rows.Length==6 && r.GetProperty("imageFieldsTotal").GetInt32()==6 &&
                   r.GetProperty("pcOwnedXenonImageRoundtripFields").GetInt32()==0 &&
                   r.GetProperty("imageFieldsWithoutAvailableOwnedPixelProof").GetInt32()==6 &&
                   !r.GetProperty("sourceImagePayloadGraftCompleted").GetBoolean(),
                "six generated images are not all exact-name-only unresolved root candidates.");
            string[] expectedFields={
                "GfxWorld.reflectionProbes[0].reflectionImage",
                "GfxWorld.reflectionProbes[1].reflectionImage",
                "GfxWorld.lightmaps[0].primary",
                "GfxWorld.lightmaps[0].secondary",
                "GfxWorld.lightmaps[1].primary",
                "GfxWorld.lightmaps[1].secondary"
            };
            string[] names={
                "*reflection_probe0","*reflection_probe1",
                "*lightmap0_primary","*lightmap0_secondary",
                "*lightmap1_primary","*lightmap1_secondary"
            };
            imageIndices=new int[6];
            var used=new HashSet<int>();
            for(int i=0;i<6;i++)
            {
                var slot=rows.SingleOrDefault(x=>
                    x.GetProperty("worldPointerField").GetString()==expectedFields[i]);
                Demand(slot.ValueKind!=JsonValueKind.Undefined,
                    "missing original-PC to native root image field "+expectedFields[i]);
                var index=slot.GetProperty("nativeImageRootIdentityIndex").GetInt32();
                Demand(index>=0 && index<nativeRoots.Count && used.Add(index) &&
                       nativeRoots[index].Index==index &&
                       nativeRoots[index].OatTypeName=="image" &&
                       Same(nativeRoots[index].Name,names[i]) &&
                       Same(slot.GetProperty("originalPcImageName").GetString(),names[i]) &&
                       slot.GetProperty("classification").GetString()=="NO_SOURCE_PIXEL_PROOF" &&
                       !slot.GetProperty("verifiedPcOwnedXenonImageBody").GetBoolean() &&
                       !slot.GetProperty("proofOfXboxRuntimeResidency").GetBoolean() &&
                       !slot.GetProperty("fieldActuallyRebasedInGate11Ff").GetBoolean() &&
                       !slot.GetProperty("copiedXboxReferencePixels").GetBoolean(),
                    "named root binding is NOT the exact unowned GfxImage shell for "+names[i]);
                imageIndices[i]=index;
            }
        }
        return new(headers,lightmapHeaders,pc,pp,lc,lp,imageIndices);
    }

    /// <summary>
    /// Stage 4E only: converts six exact named image ROOT references to the
    /// stable VIRTUAL slot pointers allocated by Stage 4D. Does NOT upgrade
    /// any of those six image shells or supply pixels/loadDefs.
    /// Stage 4D uses the same allocation plan but never needs these values.
    /// </summary>
    public void ApplyNamedRootAliases(IReadOnlyDictionary<int,uint> stableByRoot)
    {
        if(_imageRootIndices is null)return;
        Demand(!_referencePointersApplied,
            "six source shell root aliases were already applied.");
        var pointers=new uint[6];
        for(int i=0;i<6;i++)
        {
            int idx=_imageRootIndices[i];
            Demand(stableByRoot.TryGetValue(idx,out var ptr) &&
                   T4ZonePointer.Decode(ptr).Kind==T4ZonePointerKind.Offset &&
                   T4ZonePointer.Decode(ptr).Block==T4ZoneBlock.Virtual,
                "missing native identity-backed VIRTUAL GfxImage INSERT slot for "+idx);
            pointers[i]=ptr;
        }
        BinaryPrimitives.WriteUInt32BigEndian(_probeHeads.AsSpan(12,4),pointers[0]);
        BinaryPrimitives.WriteUInt32BigEndian(_probeHeads.AsSpan(28,4),pointers[1]);
        BinaryPrimitives.WriteUInt32BigEndian(_lightmapHeads.AsSpan(0,4),pointers[2]);
        BinaryPrimitives.WriteUInt32BigEndian(_lightmapHeads.AsSpan(4,4),pointers[3]);
        BinaryPrimitives.WriteUInt32BigEndian(_lightmapHeads.AsSpan(8,4),pointers[4]);
        BinaryPrimitives.WriteUInt32BigEndian(_lightmapHeads.AsSpan(12,4),pointers[5]);
        _referencePointersApplied=true;
    }

    public void ApplyRoot(byte[] body)
    {
        Demand(_imageRootIndices is null || _referencePointersApplied,
            "World image root alias pointers must be staged before emitting the 900B GfxWorld.");
        Demand(body.Length==900,"world buffer length not 900.");
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(_probeCountAt,4),2);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(_probeHeadsAt,4),T4ZonePointer.Following);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(_lightmapCountAt,4),2);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(_lightmapHeadsAt,4),T4ZonePointer.Following);
    }
    // Pinned OAT T4Xenon GfxWorld.txt serializes reflectionProbes BEFORE
    // dpvsPlanes and cells. The lightmaps array is emitted AFTER cells,
    // before the inline GfxLightGrid child buffers. Keep the two arrays
    // independently plannable so native loader ordering stays exact.
    public void PlanReflectionProbes(Action<long,int,bool> alloc)
    {
        alloc(_probeHeads.Length,4,true);
    }
    public void EmitReflectionProbes(Stream zone,Action<long,int,bool> alloc)
    {
        alloc(_probeHeads.Length,4,true);
        zone.Write(_probeHeads);
    }
    public void PlanLightmaps(Action<long,int,bool> alloc)
    {
        alloc(_lightmapHeads.Length,4,true);
    }
    public void EmitLightmaps(Stream zone,Action<long,int,bool> alloc)
    {
        alloc(_lightmapHeads.Length,4,true);
        zone.Write(_lightmapHeads);
    }
}