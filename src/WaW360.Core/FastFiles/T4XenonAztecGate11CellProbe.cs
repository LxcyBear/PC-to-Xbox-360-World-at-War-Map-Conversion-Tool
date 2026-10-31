using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Gate 11: source-owned 11-cell / 740-AABB typed scalar conversion.
///
/// Strictly gated, diagnostic-only, loaded from a SHA-authenticated original-PC
/// OAT witness. No original PC process pointers or Xbox-reference body bytes
/// are copied; all Xenon pointers are newly created FOLLOWING children.
/// Source model/probe indices are values, NOT asset root pointers.
/// </summary>
public sealed class T4XenonAztecGate11CellProbe
{
    private const int CellStride=56;
    private const int TreeStride=40;
    private const int CellCountExpected=11;
    private const int TreeCountExpected=740;
    private const int SmodelCountExpected=1903;
    private const int SurfaceCountExpected=735;
    private const string PinnedGate10Sha=
        "0BB41398C4236EF33CB9D60F923E09152966D52AA623DAEFC33D731A42630564";
    private readonly byte[] _cells;
    private readonly CellChild[] _children;
    public int CellCount => CellCountExpected;
    public int TreeCount => TreeCountExpected;
    public int ModelIndexCount { get; }
    public int ReflectionIndexCount { get; }
    public int WorldCellPointerAt { get; }

    private sealed record CellChild(byte[] Trees,byte[][] SmodelIndexes,
        byte[] ReflectionIndices);

    private T4XenonAztecGate11CellProbe(
        byte[] cells,CellChild[] children,int worldCellPointerAt,
        int modelIndexCount,int reflectionIndexCount)
    {
        _cells=cells;
        _children=children;
        WorldCellPointerAt=worldCellPointerAt;
        ModelIndexCount=modelIndexCount;
        ReflectionIndexCount=reflectionIndexCount;
    }

    public static async Task<T4XenonAztecGate11CellProbe> LoadAsync(
        string summaryPath,CancellationToken ct=default)
    {
        using var sd=JsonDocument.Parse(await File.ReadAllTextAsync(summaryPath,ct));
        var proof=sd.RootElement;
        if(proof.GetProperty("schema").GetString()!="WaW360.AztecGate11PcCellTopologyNativeAbiWitness.v1" ||
           proof.GetProperty("status").GetString()!="AZTEC_GATE11_11_CELL_740_AABB_TYPED_SOURCE_NATIVE_ABI_PASS_NOT_EMITTED" ||
           proof.GetProperty("sourceWorldCellCount").GetInt32()!=CellCountExpected ||
           proof.GetProperty("originalPcTypedAabbTreeCount").GetInt32()!=TreeCountExpected ||
           !proof.GetProperty("typedPcCellGraphSourcePass").GetBoolean() ||
           !proof.GetProperty("nativeCellAabbOffsetsProven").GetBoolean() ||
           !proof.GetProperty("nativeCellPortalAabbSizesAndCriticalOffsetsMatchPc").GetBoolean() ||
           !Eq(proof.GetProperty("existingPassingGate10FfSha256").GetString(),PinnedGate10Sha) ||
           proof.GetProperty("sourcePointerValuesCopied").GetBoolean() ||
           proof.GetProperty("xboxOraclePayloadBytesCopied").GetBoolean() ||
           proof.GetProperty("portableGfxCellChildDataEmitted").GetBoolean() ||
           proof.GetProperty("gate11ProductionPassed").GetBoolean() ||
           proof.GetProperty("hardwareReady").GetBoolean())
            throw new InvalidDataException("Source-only Gate11 typed cell witness or pinned Gate10 FF lineage changed.");

        var graphPath=proof.GetProperty("originalPcTypedCellAabbGraph").GetString()!;
        var bytes=await File.ReadAllBytesAsync(graphPath,ct);
        if(!Eq(Convert.ToHexString(SHA256.HashData(bytes)),
               proof.GetProperty("originalPcTypedCellAabbGraphSha256").GetString()))
            throw new InvalidDataException("Original-PC 11-cell/740-AABB typed child bytes do not match source SHA.");

        var nativeAbiPath=proof.GetProperty("originalXboxReferenceAbi").GetString()!;
        var nativeBytes=await File.ReadAllBytesAsync(nativeAbiPath,ct);
        if(!Eq(Convert.ToHexString(SHA256.HashData(nativeBytes)),
               proof.GetProperty("originalXboxReferenceAbiSha256").GetString()))
            throw new InvalidDataException("Original Xbox native ABI witness SHA changed.");

        using var nd=JsonDocument.Parse(nativeBytes);
        var abi=nd.RootElement.GetProperty("gfxWorldRootAbi");
        int cellsAt=abi.GetProperty("cellsAt").GetInt32();
        if(abi.GetProperty("gfxWorldSize").GetInt32()!=900 ||
           abi.GetProperty("gfxCellSize").GetInt32()!=CellStride ||
           abi.GetProperty("gfxAabbTreeSize").GetInt32()!=TreeStride ||
           cellsAt<=0 || cellsAt>=896 ||
           abi.GetProperty("cellMinsAt").GetInt32()!=0 ||
           abi.GetProperty("cellMaxsAt").GetInt32()!=12 ||
           abi.GetProperty("cellAabbTreeCountAt").GetInt32()!=24 ||
           abi.GetProperty("cellAabbTreeAt").GetInt32()!=28 ||
           abi.GetProperty("cellPortalCountAt").GetInt32()!=32 ||
           abi.GetProperty("cellPortalsAt").GetInt32()!=36 ||
           abi.GetProperty("cellCullGroupCountAt").GetInt32()!=40 ||
           abi.GetProperty("cellCullGroupsAt").GetInt32()!=44 ||
           abi.GetProperty("cellReflectionProbeCountAt").GetInt32()!=48 ||
           abi.GetProperty("cellReflectionProbesAt").GetInt32()!=52 ||
           abi.GetProperty("aabbMinsAt").GetInt32()!=0 ||
           abi.GetProperty("aabbMaxsAt").GetInt32()!=12 ||
           abi.GetProperty("aabbChildCountAt").GetInt32()!=24 ||
           abi.GetProperty("aabbSurfaceCountAt").GetInt32()!=26 ||
           abi.GetProperty("aabbStartSurfIndexAt").GetInt32()!=28 ||
           abi.GetProperty("aabbSmodelIndexCountAt").GetInt32()!=30 ||
           abi.GetProperty("aabbSmodelIndexesAt").GetInt32()!=32 ||
           abi.GetProperty("aabbChildrenOffsetAt").GetInt32()!=36)
            throw new InvalidDataException("Native Xenon 900/56/40 GfxWorld/Cell/AABB field offsets changed.");

        using var gd=JsonDocument.Parse(bytes);
        var graph=gd.RootElement;
        if(graph.GetProperty("schema").GetString()!="WaW360.T4PcGate11TypedGfxCellAabbSource.v1" ||
           graph.GetProperty("cellCount").GetInt32()!=CellCountExpected ||
           graph.GetProperty("actualExportedTreeCount").GetInt32()!=TreeCountExpected ||
           !graph.GetProperty("completeAndValidated").GetBoolean() ||
           graph.GetProperty("originalPcSourcePointerBytesExported").GetBoolean() ||
           graph.GetProperty("xboxReferencePayloadUsed").GetBoolean() ||
           graph.GetProperty("xboxGfxCellsPhysicallyEmitted").GetBoolean() ||
           graph.GetProperty("xboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Untrusted or non-source PC GfxCell graph supplied to Xbox cell writer.");

        var sourceCells=graph.GetProperty("cells").EnumerateArray().ToArray();
        if(sourceCells.Length!=CellCountExpected)
            throw new InvalidDataException("Expected 11 original-PC GfxCells in typed source graph.");

        var cellBytes=new byte[CellCountExpected*CellStride];
        var children=new CellChild[CellCountExpected];
        int treeTotal=0,indexTotal=0,probeTotal=0;
        for(int cellOrdinal=0;cellOrdinal<CellCountExpected;cellOrdinal++)
        {
            var cell=sourceCells[cellOrdinal];
            int count=cell.GetProperty("aabbTreeCount").GetInt32();
            int portal=cell.GetProperty("portalCount").GetInt32();
            int culls=cell.GetProperty("cullGroupCount").GetInt32();
            int reflection=cell.GetProperty("reflectionProbeCount").GetInt32();
            var trees=cell.GetProperty("aabbTrees").EnumerateArray().ToArray();
            var probeIds=cell.GetProperty("reflectionProbeIndices").EnumerateArray().ToArray();
            if(cell.GetProperty("ordinal").GetInt32()!=cellOrdinal ||
               count<0 || count>TreeCountExpected || trees.Length!=count ||
               portal!=0 || culls!=0 || reflection<0 || reflection>255 ||
               probeIds.Length!=reflection)
                throw new InvalidDataException($"PC GfxCell[{cellOrdinal}] child counts or zero-portal/cull conditions differ.");

            int cellAt=cellOrdinal*CellStride;
            Bounds(cellBytes,cellAt,cell.GetProperty("mins"),cell.GetProperty("maxs"));
            Be32(cellBytes,cellAt+24,checked((uint)count));
            if(count>0)Be32(cellBytes,cellAt+28,T4ZonePointer.Following);
            cellBytes[cellAt+48]=checked((byte)reflection);
            if(reflection>0)Be32(cellBytes,cellAt+52,T4ZonePointer.Following);

            var treeBytes=new byte[count*TreeStride];
            var modelLists=new byte[count][];
            for(int treeOrdinal=0;treeOrdinal<count;treeOrdinal++)
            {
                var tree=trees[treeOrdinal];
                int childCount=tree.GetProperty("childCount").GetInt32();
                int surfaces=tree.GetProperty("surfaceCount").GetInt32();
                int startSurf=tree.GetProperty("startSurfIndex").GetInt32();
                int smodels=tree.GetProperty("smodelIndexCount").GetInt32();
                int childOffset=tree.GetProperty("childrenOffset").GetInt32();
                var modelIds=tree.GetProperty("smodelIndexes").EnumerateArray().ToArray();
                if(tree.GetProperty("ordinal").GetInt32()!=treeOrdinal ||
                   childCount<0 || childCount>ushort.MaxValue ||
                   surfaces<0 || surfaces>ushort.MaxValue ||
                   startSurf<0 || startSurf>ushort.MaxValue ||
                   smodels<0 || smodels>SmodelCountExpected ||
                   modelIds.Length!=smodels ||
                   startSurf>SurfaceCountExpected || surfaces>SurfaceCountExpected)
                    throw new InvalidDataException($"GfxCell[{cellOrdinal}] tree[{treeOrdinal}] invalid child counts.");

                int at=treeOrdinal*TreeStride;
                Bounds(treeBytes,at,tree.GetProperty("mins"),tree.GetProperty("maxs"));
                Be16(treeBytes,at+24,checked((ushort)childCount));
                Be16(treeBytes,at+26,checked((ushort)surfaces));
                Be16(treeBytes,at+28,checked((ushort)startSurf));
                Be16(treeBytes,at+30,checked((ushort)smodels));
                if(smodels>0)Be32(treeBytes,at+32,T4ZonePointer.Following);
                Be32(treeBytes,at+36,unchecked((uint)childOffset));
                var childIndexes=new byte[smodels*2];
                for(int j=0;j<smodels;j++)
                {
                    int id=modelIds[j].GetInt32();
                    if(id<0 || id>=SmodelCountExpected)
                        throw new InvalidDataException($"GfxCell[{cellOrdinal}] tree[{treeOrdinal}] smodel index {id} out of source range.");
                    Be16(childIndexes,j*2,checked((ushort)id));
                }
                modelLists[treeOrdinal]=childIndexes;
                treeTotal++;
                indexTotal+=smodels;
            }
            var cellProbes=new byte[reflection];
            for(int j=0;j<reflection;j++)
            {
                int id=probeIds[j].GetInt32();
                if(id<0 || id>byte.MaxValue)
                    throw new InvalidDataException($"GfxCell[{cellOrdinal}] reflection probe index {id} invalid.");
                cellProbes[j]=checked((byte)id);
            }
            probeTotal+=reflection;
            children[cellOrdinal]=new CellChild(treeBytes,modelLists,cellProbes);
        }
        if(treeTotal!=TreeCountExpected ||
           indexTotal!=proof.GetProperty("originalPcTypedSmodelIndicesCount").GetInt32() ||
           probeTotal!=proof.GetProperty("originalPcTypedCellReflectionProbeIndicesCount").GetInt32())
            throw new InvalidDataException("Original-PC AABB/smodel/reflection counts mismatch source witness.");

        return new T4XenonAztecGate11CellProbe(cellBytes,children,cellsAt,indexTotal,probeTotal);
    }

    public void Plan(Action<long,int,bool> virtualAlloc)
    {
        virtualAlloc(_cells.Length,4,true);
        foreach(var cell in _children)
        {
            if(cell.Trees.Length>0)
            {
                virtualAlloc(cell.Trees.Length,4,true);
                foreach(var indexes in cell.SmodelIndexes)
                    if(indexes.Length>0)virtualAlloc(indexes.Length,2,true);
            }
            if(cell.ReflectionIndices.Length>0)
                virtualAlloc(cell.ReflectionIndices.Length,1,true);
        }
    }

    public void Emit(Stream zone,Action<long,int,bool> virtualAlloc)
    {
        virtualAlloc(_cells.Length,4,true);
        zone.Write(_cells);
        foreach(var cell in _children)
        {
            if(cell.Trees.Length>0)
            {
                virtualAlloc(cell.Trees.Length,4,true);
                zone.Write(cell.Trees);
                foreach(var indexes in cell.SmodelIndexes)
                {
                    if(indexes.Length==0)continue;
                    virtualAlloc(indexes.Length,2,true);
                    zone.Write(indexes);
                }
            }
            if(cell.ReflectionIndices.Length>0)
            {
                virtualAlloc(cell.ReflectionIndices.Length,1,true);
                zone.Write(cell.ReflectionIndices);
            }
        }
    }

    private static void Bounds(byte[] bytes,int at,JsonElement mins,JsonElement maxs)
    {
        var lo=mins.EnumerateArray().ToArray();
        var hi=maxs.EnumerateArray().ToArray();
        if(lo.Length!=3 || hi.Length!=3)
            throw new InvalidDataException("Original-PC bounds must have exactly three axes.");
        for(int i=0;i<3;i++)
        {
            float a=lo[i].GetSingle();
            float b=hi[i].GetSingle();
            if(!float.IsFinite(a) || !float.IsFinite(b) ||
               Math.Abs(a)>100000000f || Math.Abs(b)>100000000f || a>b)
                throw new InvalidDataException($"Original-PC GfxCell/AABB bounds invalid on axis {i}.");
            BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(at+i*4,4),a);
            BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(at+12+i*4,4),b);
        }
    }
    private static void Be32(byte[] b,int at,uint value)=>
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at,4),value);
    private static void Be16(byte[] b,int at,ushort value)=>
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at,2),value);
    private static bool Eq(string? a,string? b)=>
        string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
}
