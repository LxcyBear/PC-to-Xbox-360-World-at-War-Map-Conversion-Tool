using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Gate11 isolated source-owned WAVE A+B integration: typed light-grid and
/// original-PC shadow indices plus (empty) region hulls. The six world
/// reflection/lightmap image refs are deliberately NOT forged from name
/// matches, and Xbox-only streaming metadata remains a separate hard fence.
/// A Gate11 PASS and hardware deployment are forbidden for this probe.
/// </summary>
public sealed class T4XenonAztecGate11LightingShadowBatch
{
    private const string ProvenPrimitiveSha =
        "D46ABC8E5681E4F235ED82821016F6F0002505AD0DEF68D680D70FCCE75F1FCB";
    private const string OriginalCellsSha =
        "05CEEC82AF5D58706BA5A6EE24995D09A3FB764F3E547A8B4758A9347ECF2862";
    private readonly byte[][] _grid;
    private readonly byte[] _shadowHeaders, _regionHeaders;
    private readonly byte[][] _shadowSurfaces, _shadowModels;
    private readonly int _gridAt, _primaryCountAt, _shadowAt, _regionAt;
    private readonly bool _gridHasLightRegions;
    private readonly uint _gridSunPrimaryLightIndex, _rowAxis, _colAxis;
    private readonly ushort[] _mins, _maxs;

    public long GridBytes => _grid.Sum(b => (long)b.Length);
    public int PrimaryLights => 2;
    public int ShadowModelReferences => _shadowModels.Sum(b => b.Length / 2);
    public int RegionHeaders => _regionHeaders.Length / 8;
    public bool NativeImagePayloadsBound => false;
    public bool NativeStreamInfoReady => false;

    private T4XenonAztecGate11LightingShadowBatch(byte[][] grid,
        byte[] shadowHeaders, byte[][] shadowSurfaces, byte[][] shadowModels,
        byte[] regionHeaders, int gridAt, int primaryCountAt, int shadowAt,
        int regionAt, bool gridHasLightRegions, uint gridSunPrimaryLightIndex,
        ushort[] mins, ushort[] maxs, uint rowAxis, uint colAxis)
    {
        _grid=grid;_shadowHeaders=shadowHeaders;
        _shadowSurfaces=shadowSurfaces;_shadowModels=shadowModels;
        _regionHeaders=regionHeaders;_gridAt=gridAt;
        _primaryCountAt=primaryCountAt;_shadowAt=shadowAt;_regionAt=regionAt;
        _gridHasLightRegions=gridHasLightRegions;
        _gridSunPrimaryLightIndex=gridSunPrimaryLightIndex;
        _mins=mins;_maxs=maxs;_rowAxis=rowAxis;_colAxis=colAxis;
    }

    public static async Task<T4XenonAztecGate11LightingShadowBatch> LoadAsync(
        string twoWaveReport, string preflightReport, byte[] scalarRoot,
        CancellationToken ct=default)
    {
        using var td=JsonDocument.Parse(await File.ReadAllTextAsync(twoWaveReport,ct));
        var t=td.RootElement;
        if(t.GetProperty("schema").GetString()!="WaW360.AztecGate11TwoWaveJointLightingWorldClosure.v1" ||
           t.GetProperty("status").GetString()!="AZTEC_GATE11_TWO_WAVE_ALL_LANES_SOURCE_AND_NATIVE_ABI_AUDITED_NEXT_FF_NOT_EMITTED" ||
           !Eq(t.GetProperty("previousCellWorldSha256").GetString(),OriginalCellsSha) ||
           !Eq(t.GetProperty("protectedCurrentPrimitiveWorldSha256").GetString(),ProvenPrimitiveSha) ||
           t.GetProperty("newMapFastFileProduced").GetBoolean() ||
           t.GetProperty("fullGate11Passed").GetBoolean() ||
           t.GetProperty("xboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Joint original-PC lighting/shadow preparation not verified.");
        var a=t.GetProperty("waveA");
        var b=t.GetProperty("waveB");
        if(a.GetProperty("sourceTypedGridLeafFamilies").GetInt32()!=4 ||
           a.GetProperty("originalPcGridNativeEndianBytes").GetInt64()!=4822860L ||
           a.GetProperty("lightmapAndReflectionImageReferenceFields").GetInt32()!=6 ||
           a.GetProperty("exactNameIdentityOnlyImageFields").GetInt32()!=6 ||
           a.GetProperty("missingOrAmbiguousImageNames").GetInt32()!=0 ||
           a.GetProperty("imagePayloadProvenanceOrEquivalentOwnedGfxImageRootsReady").GetBoolean() ||
           a.GetProperty("imageBindingsMayBeRebasedInOutputFf").GetBoolean() ||
           a.GetProperty("primaryLights").GetInt32()!=2 ||
           a.GetProperty("typedShadowHeaders").GetInt32()!=2 ||
           a.GetProperty("originalPcShadowSurfaceIndices").GetInt64()!=0 ||
           a.GetProperty("originalPcShadowModelIndices").GetInt64()!=1903 ||
           b.GetProperty("lightRegionHeaders").GetInt32()!=2 ||
           b.GetProperty("lightRegionHulls").GetInt64()!=0 ||
           b.GetProperty("lightRegionAxes").GetInt64()!=0 ||
           b.GetProperty("xenonStreamInfo").GetProperty("readyToEmit").GetBoolean() ||
           scalarRoot.Length!=900)
            throw new InvalidDataException("Joint source graph is not the narrowly proven Aztec grid/shadow/empty-region batch.");

        using var pd=JsonDocument.Parse(await File.ReadAllTextAsync(preflightReport,ct));
        var p=pd.RootElement;
        if(p.GetProperty("schema").GetString()!="WaW360.AztecGate11CombinedNativeOneBuildPreparation.v1" ||
           p.GetProperty("originalSourceLeafArraysConverted").GetInt32()!=13 ||
           !Eq(p.GetProperty("previousPassingGate11FfSha256").GetString(),OriginalCellsSha))
            throw new InvalidDataException("Original PC 13-family preflight drifted.");

        using var ld=JsonDocument.Parse(await File.ReadAllTextAsync(
            p.GetProperty("nativeEndiannessLeafReport").GetString()!,ct));
        var l=ld.RootElement;
        if(l.GetProperty("families").GetInt32()!=13 ||
           !Eq(l.GetProperty("protectedIndependentGate11FfSha256").GetString(),OriginalCellsSha))
            throw new InvalidDataException("Original-PC endian leaf witness changed.");
        var leaves=l.GetProperty("leaves");
        static async Task<byte[]> Get(JsonElement leaves,string name,
            int count,int stride,CancellationToken ct)
        {
            var e=leaves.GetProperty(name);
            var data=await File.ReadAllBytesAsync(
                e.GetProperty("xenonNativeEndianFile").GetString()!,ct);
            if(e.GetProperty("sourceRecordCount").GetInt64()!=count ||
               e.GetProperty("sourceAndNativeStructBytes").GetInt32()!=stride ||
               data.Length!=(long)count*stride ||
               !e.GetProperty("sourceOwned").GetBoolean() ||
               !e.GetProperty("endianRoundTripExact").GetBoolean() ||
               e.GetProperty("pointerFieldsPresent").GetBoolean() ||
               e.GetProperty("containedXboxOraclePayload").GetBoolean() ||
               !Eq(Sha(data),e.GetProperty("xenonNativeEndianSha256").GetString()))
                throw new InvalidDataException($"Gate11 grid {name} is not original-PC native endian source.");
            return data;
        }
        var grid=new[]{
            await Get(leaves,"gridRowDataStart",322,2,ct),
            await Get(leaves,"gridRawRowData",10320,1,ct),
            await Get(leaves,"gridEntries",1036024,4,ct),
            await Get(leaves,"gridColors",3975,168,ct)
        };

        using var sd=JsonDocument.Parse(await File.ReadAllTextAsync(
            l.GetProperty("sourceReadinessReport").GetString()!,ct));
        var s=sd.RootElement;
        var worldFile=s.GetProperty("sourceTypedRemainderGraph").GetString()!;
        var worldBytes=await File.ReadAllBytesAsync(worldFile,ct);
        if(!Eq(Sha(worldBytes),s.GetProperty("sourceTypedRemainderGraphSha256").GetString()))
            throw new InvalidDataException("Original PC normalized shadow/light-region graph SHA changed.");
        using var wd=JsonDocument.Parse(StripBom(worldBytes));
        var world=wd.RootElement;
        var sourceGrid=world.GetProperty("sourceGridShape");
        var nativeAbiFile=s.GetProperty("nativeXboxAbiOnlyFile").GetString()!;
        var abiBytes=await File.ReadAllBytesAsync(nativeAbiFile,ct);
        if(!Eq(Sha(abiBytes),s.GetProperty("nativeXboxAbiOnlySha256").GetString()))
            throw new InvalidDataException("Measured native Xbox ABI SHA drifted.");
        using var ad=JsonDocument.Parse(StripBom(abiBytes));
        var abi=ad.RootElement;
        if(abi.GetProperty("schema").GetString()!="WaW360.T4XenonGate11RemainingWorldNativeAbi.v1" ||
           abi.GetProperty("gfxWorldBytes").GetInt32()!=900 ||
           abi.GetProperty("lightGridBytes").GetInt32()!=56 ||
           abi.GetProperty("gridRowDataStartAt").GetInt32()!=28 ||
           abi.GetProperty("gridRawRowDataAt").GetInt32()!=36 ||
           abi.GetProperty("gridEntriesAt").GetInt32()!=44 ||
           abi.GetProperty("gridColorsAt").GetInt32()!=52 ||
           abi.GetProperty("shadowGeometryBytes").GetInt32()!=12 ||
           abi.GetProperty("shadowSortedAt").GetInt32()!=4 ||
           abi.GetProperty("shadowModelsAt").GetInt32()!=8 ||
           abi.GetProperty("lightRegionBytes").GetInt32()!=8 ||
           abi.GetProperty("containsOriginalXboxPayload").GetBoolean())
            throw new InvalidDataException("Native 56B light-grid / 12B shadow / 8B region ABI has changed.");

        int gridAt=abi.GetProperty("lightGridAt").GetInt32();
        int primaryCountAt=abi.GetProperty("primaryLightCountAt").GetInt32();
        int shadowAt=abi.GetProperty("shadowGeomAt").GetInt32();
        int regionAt=abi.GetProperty("lightRegionAt").GetInt32();
        if(gridAt<0 || gridAt+56>900 || primaryCountAt<0 ||
           primaryCountAt+4>900 || shadowAt<0 || shadowAt+4>900 ||
           regionAt<0 || regionAt+4>900)
            throw new InvalidDataException("Native 900B GfxWorld light-grid/shadow offsets invalid.");
        if(scalarRoot.AsSpan(gridAt,56).ToArray().Any(x=>x!=0) ||
           BinaryPrimitives.ReadUInt32BigEndian(scalarRoot.AsSpan(shadowAt,4))!=0 ||
           BinaryPrimitives.ReadUInt32BigEndian(scalarRoot.AsSpan(regionAt,4))!=0)
            throw new InvalidDataException("Original PC->Xenon seed already has grid/shadow/region bytes; refuse overwrite.");
        var oldCount=BinaryPrimitives.ReadUInt32BigEndian(scalarRoot.AsSpan(primaryCountAt,4));
        if(oldCount!=0 && oldCount!=2)
            throw new InvalidDataException($"Native GfxWorld primary light count {oldCount} not compatible with two source lights.");

        var min=sourceGrid.GetProperty("mins").EnumerateArray()
            .Select(x=>checked((ushort)x.GetInt32())).ToArray();
        var max=sourceGrid.GetProperty("maxs").EnumerateArray()
            .Select(x=>checked((ushort)x.GetInt32())).ToArray();
        var row=sourceGrid.GetProperty("rowAxis").GetUInt32();
        var col=sourceGrid.GetProperty("colAxis").GetUInt32();
        if(min.Length!=3 || max.Length!=3 || row>=3 || col>=3 ||
           row==col || max[row]-min[row]+1!=322)
            throw new InvalidDataException("Original-PC grid geometry shape invalid.");

        var shadowRows=world.GetProperty("shadowGeometry").EnumerateArray().ToArray();
        var regionRows=world.GetProperty("lightRegions").EnumerateArray().ToArray();
        if(shadowRows.Length!=2 || regionRows.Length!=2)
            throw new InvalidDataException("Two original PC light region/shadow root headers required.");
        var heads=new byte[2*12];
        var regs=new byte[2*8];
        var surfaces=new byte[2][];
        var models=new byte[2][];
        int totalModelRefs=0;
        for(int i=0;i<2;i++)
        {
            var sh=shadowRows[i];
            var sorted=sh.GetProperty("sortedSurfIndex").EnumerateArray().ToArray();
            var smodel=sh.GetProperty("smodelIndex").EnumerateArray().ToArray();
            if(sh.GetProperty("index").GetInt32()!=i ||
               sh.GetProperty("surfaceCount").GetInt32()!=sorted.Length ||
               sh.GetProperty("smodelCount").GetInt32()!=smodel.Length ||
               sorted.Length!=0 || smodel.Length>1903)
                throw new InvalidDataException($"Original PC shadow light {i} indices are not proven.");
            surfaces[i]=new byte[sorted.Length*2];
            models[i]=new byte[smodel.Length*2];
            for(int j=0;j<smodel.Length;j++)
            {
                int index=smodel[j].GetInt32();
                if(index<0 || index>=1903)
                    throw new InvalidDataException($"Unverified PC static shadow model index {index}.");
                BinaryPrimitives.WriteUInt16BigEndian(models[i].AsSpan(j*2,2),(ushort)index);
            }
            totalModelRefs+=smodel.Length;
            Put16(heads,i*12,(ushort)sorted.Length);
            Put16(heads,i*12+2,(ushort)smodel.Length);
            Put32(heads,i*12+4,0);
            Put32(heads,i*12+8,smodel.Length>0?T4ZonePointer.Following:0);

            var r=regionRows[i];
            if(r.GetProperty("index").GetInt32()!=i ||
               r.GetProperty("hullCount").GetInt32()!=0 ||
               r.GetProperty("hulls").GetArrayLength()!=0)
                throw new InvalidDataException("Aztec region was unexpectedly nonempty; do not fabricate nested hulls.");
            // 8-byte GfxLightRegion: count=0, pointer=null, source-backed.
        }
        if(totalModelRefs!=1903)
            throw new InvalidDataException("Expected original-PC total 1903 shadow model-index references.");
        return new T4XenonAztecGate11LightingShadowBatch(
            grid,heads,surfaces,models,regs,gridAt,primaryCountAt,
            shadowAt,regionAt,sourceGrid.GetProperty("hasLightRegions").GetBoolean(),
            sourceGrid.GetProperty("sunPrimaryLightIndex").GetUInt32(),
            min,max,row,col);
    }

    public void ApplyRoot(byte[] root)
    {
        if(root.Length!=900)throw new InvalidDataException("Native world root length drifted.");
        var off=_gridAt;
        root[off]=_gridHasLightRegions?(byte)1:(byte)0;
        Put32(root,off+4,_gridSunPrimaryLightIndex);
        for(int i=0;i<3;i++)
        {
            Put16(root,off+8+i*2,_mins[i]);
            Put16(root,off+14+i*2,_maxs[i]);
        }
        Put32(root,off+20,_rowAxis);
        Put32(root,off+24,_colAxis);
        Put32(root,off+28,T4ZonePointer.Following);
        Put32(root,off+32,(uint)_grid[1].Length);
        Put32(root,off+36,T4ZonePointer.Following);
        Put32(root,off+40,(uint)(_grid[2].Length/4));
        Put32(root,off+44,T4ZonePointer.Following);
        Put32(root,off+48,(uint)(_grid[3].Length/168));
        Put32(root,off+52,T4ZonePointer.Following);
        Put32(root,_primaryCountAt,2);
        Put32(root,_shadowAt,T4ZonePointer.Following);
        Put32(root,_regionAt,T4ZonePointer.Following);
    }

    public void PlanGrid(Action<long,int,bool> alloc)
    {
        alloc(_grid[0].Length,2,true);
        alloc(_grid[1].Length,1,true);
        alloc(_grid[2].Length,4,true);
        alloc(_grid[3].Length,4,true);
    }
    public void EmitGrid(Stream zone,Action<long,int,bool> alloc)
    {
        Emit(zone,alloc,_grid[0],2);
        Emit(zone,alloc,_grid[1],1);
        Emit(zone,alloc,_grid[2],4);
        Emit(zone,alloc,_grid[3],4);
    }
    public void PlanShadowsAndRegions(Action<long,int,bool> alloc)
    {
        alloc(_shadowHeaders.Length,4,true);
        for(int i=0;i<2;i++)
        {
            if(_shadowSurfaces[i].Length>0)alloc(_shadowSurfaces[i].Length,2,true);
            if(_shadowModels[i].Length>0)alloc(_shadowModels[i].Length,2,true);
        }
        alloc(_regionHeaders.Length,4,true);
    }
    public void EmitShadowsAndRegions(Stream zone,Action<long,int,bool> alloc)
    {
        Emit(zone,alloc,_shadowHeaders,4);
        for(int i=0;i<2;i++)
        {
            if(_shadowSurfaces[i].Length>0)Emit(zone,alloc,_shadowSurfaces[i],2);
            if(_shadowModels[i].Length>0)Emit(zone,alloc,_shadowModels[i],2);
        }
        Emit(zone,alloc,_regionHeaders,4);
    }
    private static void Emit(Stream zone,Action<long,int,bool> alloc,byte[] data,int align)
    {alloc(data.Length,align,true);zone.Write(data);}
    private static void Put16(byte[] b,int at,ushort x)=>
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at,2),x);
    private static void Put32(byte[] b,int at,uint x)=>
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at,4),x);
    private static byte[] StripBom(byte[] b)=>
        b.Length>=3 && b[0]==0xEF && b[1]==0xBB && b[2]==0xBF?b[3..]:b;
    private static string Sha(ReadOnlySpan<byte> b)=>
        Convert.ToHexString(SHA256.HashData(b));
    private static bool Eq(string? a,string? b)=>
        string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
}
