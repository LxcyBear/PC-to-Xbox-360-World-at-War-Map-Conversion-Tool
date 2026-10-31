using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Original-PC-only Gate11 primitive child probe. Uses the SHA-verified
/// 13-family endian source batch but emits ONLY the four independently
/// pointer-free/non-asset groups: sky surf starts, brush models, and two
/// water-buffer float arrays. NOT complete GfxWorld, and NEVER hardware ready.
///
/// The staged and independently OAT-loaded 11-cell/740-AABB FF is left intact.
/// The rest of the Gate11 world graph (images, lightgrid, shadows, regions,
/// streamInfo, material semantics) remains a hard release fence.
/// </summary>
public sealed class T4XenonAztecGate11PrimitiveBatch
{
    private const string PinnedCellFfSha =
        "05CEEC82AF5D58706BA5A6EE24995D09A3FB764F3E547A8B4758A9347ECF2862";
    private readonly byte[] _sky, _models, _water0, _water1;
    private readonly int _skyCountAt, _skyPointerAt, _modelCountAt, _modelPointerAt, _waterAt;

    public int SkySurfaces => 2;
    public int BrushModels => 183;
    public long WaterBytes => _water0.LongLength + _water1.LongLength;
    public long TotalNativeBytes => _sky.LongLength + _models.LongLength + WaterBytes;

    private T4XenonAztecGate11PrimitiveBatch(
        byte[] sky, byte[] models, byte[] water0, byte[] water1,
        int skyCountAt, int skyPointerAt, int modelCountAt, int modelPointerAt, int waterAt)
    {
        _sky=sky; _models=models; _water0=water0; _water1=water1;
        _skyCountAt=skyCountAt; _skyPointerAt=skyPointerAt;
        _modelCountAt=modelCountAt; _modelPointerAt=modelPointerAt; _waterAt=waterAt;
    }

    public static async Task<T4XenonAztecGate11PrimitiveBatch> LoadAsync(
        string preflightReport,byte[] scalarRoot,CancellationToken ct=default)
    {
        using var pd=JsonDocument.Parse(await File.ReadAllTextAsync(preflightReport,ct));
        var plan=pd.RootElement;
        if(plan.GetProperty("schema").GetString()!="WaW360.AztecGate11CombinedNativeOneBuildPreparation.v1" ||
           plan.GetProperty("status").GetString()!="AZTEC_GATE11_SOURCE_LEAF_13_NATIVE_ENDIAN_PASS_IMAGE_IDENTITIES_AUDITED_FULL_FF_BLOCKED" ||
           plan.GetProperty("originalSourceLeafArraysConverted").GetInt32()!=13 ||
           plan.GetProperty("nativeEndianLeafBytes").GetInt64()!=6930524L ||
           !plan.GetProperty("endiannessRoundTripProven").GetBoolean() ||
           !plan.GetProperty("nativeCandidateNameIsNotPayloadEquivalence").GetBoolean() ||
           plan.GetProperty("currentFfEmissionPerformed").GetBoolean() ||
           plan.GetProperty("fullGate11Passed").GetBoolean() ||
           plan.GetProperty("XboxHardwareReady").GetBoolean() ||
           !Eq(plan.GetProperty("previousPassingGate11FfSha256").GetString(),PinnedCellFfSha))
            throw new InvalidDataException("Gate11 primitive probe requires pinned source-only preflight, not an approved FF.");

        var protectedFf=plan.GetProperty("previousPassingGate11Ff").GetString()!;
        var before=await File.ReadAllBytesAsync(protectedFf,ct);
        if(!Eq(Sha(before),PinnedCellFfSha))
            throw new InvalidDataException("Independently OAT-loaded Gate11 11-cell FF has changed.");

        using var ld=JsonDocument.Parse(await File.ReadAllTextAsync(
            plan.GetProperty("nativeEndiannessLeafReport").GetString()!,ct));
        var leaf=ld.RootElement;
        if(leaf.GetProperty("status").GetString()!="AZTEC_GATE11_13_SOURCE_LEAF_XENON_ENDIAN_CONVERSION_PASS_FULL_WORLD_UNPROVEN" ||
           leaf.GetProperty("families").GetInt32()!=13 ||
           leaf.GetProperty("nativeCandidateByteCount").GetInt64()!=6930524L ||
           !Eq(leaf.GetProperty("protectedIndependentGate11FfSha256").GetString(),PinnedCellFfSha) ||
           leaf.GetProperty("newNativeFfEmitted").GetBoolean() ||
           leaf.GetProperty("gate11OverallPassed").GetBoolean())
            throw new InvalidDataException("Gate11 endian leaf evidence does not match the protected cell FF.");

        var leaves=leaf.GetProperty("leaves");
        static async Task<byte[]> Read(JsonElement leaves,string name,int count,int stride,CancellationToken ct)
        {
            var entry=leaves.GetProperty(name);
            var file=entry.GetProperty("xenonNativeEndianFile").GetString()!;
            var bytes=await File.ReadAllBytesAsync(file,ct);
            if(entry.GetProperty("sourceRecordCount").GetInt64()!=count ||
               entry.GetProperty("sourceAndNativeStructBytes").GetInt32()!=stride ||
               entry.GetProperty("byteCount").GetInt64()!=bytes.LongLength ||
               bytes.LongLength!=(long)count*stride ||
               !entry.GetProperty("sourceOwned").GetBoolean() ||
               !entry.GetProperty("endianRoundTripExact").GetBoolean() ||
               entry.GetProperty("pointerFieldsPresent").GetBoolean() ||
               entry.GetProperty("containedXboxOraclePayload").GetBoolean() ||
               entry.GetProperty("hardwareReady").GetBoolean() ||
               !string.Equals(Sha(bytes),entry.GetProperty("xenonNativeEndianSha256").GetString(),
                   StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unverified PC-owned Gate11 native primitive {name}.");
            return bytes;
        }
        var sky=await Read(leaves,"skyStartSurfs",2,4,ct);
        var models=await Read(leaves,"brushModels",183,56,ct);
        var water0=await Read(leaves,"waterBuffer0",65544,16,ct);
        var water1=await Read(leaves,"waterBuffer1",65544,16,ct);

        // ABI is a source-layout witness only, never an original Xbox payload.
        using var sd=JsonDocument.Parse(await File.ReadAllTextAsync(
            leaf.GetProperty("sourceReadinessReport").GetString()!,ct));
        var source=sd.RootElement;
        if(source.GetProperty("schema").GetString()!="WaW360.AztecGate11SingleBatchRemainingWorldSource.v1" ||
           !Eq(source.GetProperty("protectedPreviousGate11FfSha256").GetString(),PinnedCellFfSha) ||
           source.GetProperty("originalPcSourceLeafByteCount").GetInt64()!=6930524L ||
           source.GetProperty("singleCombinedFfReadyToEmit").GetBoolean() ||
           source.GetProperty("actualMapFfBuildPerformed").GetBoolean())
            throw new InvalidDataException("Original PC source census has drifted.");

        string abiFile=source.GetProperty("nativeXboxAbiOnlyFile").GetString()!;
        var abiBytes=await File.ReadAllBytesAsync(abiFile,ct);
        if(!Eq(Sha(abiBytes),source.GetProperty("nativeXboxAbiOnlySha256").GetString()))
            throw new InvalidDataException("Measured Xbox field-only ABI SHA drifted.");
        using var ad=JsonDocument.Parse(abiBytes);
        var abi=ad.RootElement;
        if(abi.GetProperty("schema").GetString()!="WaW360.T4XenonGate11RemainingWorldNativeAbi.v1" ||
           abi.GetProperty("gfxWorldBytes").GetInt32()!=900 ||
           abi.GetProperty("containsOriginalXboxPayload").GetBoolean() ||
           abi.GetProperty("brushModelBytes").GetInt32()!=56 ||
           scalarRoot.Length!=900)
            throw new InvalidDataException("Missing native 900-byte GfxWorld or brush-model layout.");

        int skyCountAt=abi.GetProperty("skySurfCountAt").GetInt32();
        int skyPointerAt=abi.GetProperty("skyStartSurfsAt").GetInt32();
        int modelCountAt=abi.GetProperty("modelCountAt").GetInt32();
        int modelPointerAt=abi.GetProperty("modelsAt").GetInt32();
        int waterAt=abi.GetProperty("waterBuffersAt").GetInt32();
        var fields=new[]{skyCountAt,skyPointerAt,modelCountAt,modelPointerAt,
            waterAt,waterAt+4,waterAt+8,waterAt+12};
        if(fields.Any(x=>x<0 || x>896) || fields.Distinct().Count()!=fields.Length ||
           waterAt+16>scalarRoot.Length)
            throw new InvalidDataException("Measured Xenon Gate11 primitive root offsets invalid.");

        // Never replace already-populated native root pointers or data.
        foreach(int at in fields)
        {
            if(BinaryPrimitives.ReadUInt32BigEndian(scalarRoot.AsSpan(at,4))!=0)
                throw new InvalidDataException($"Source scalar-only 900B root is not NULL at native offset {at}.");
        }
        return new T4XenonAztecGate11PrimitiveBatch(
            sky,models,water0,water1,skyCountAt,skyPointerAt,modelCountAt,modelPointerAt,waterAt);
    }

    public void ApplyRoot(byte[] root)
    {
        if(root.Length!=900)throw new InvalidDataException("Xenon GfxWorld root length changed.");
        static void Put(byte[] b,int at,uint v)=>
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at,4),v);
        Put(root,_skyCountAt,(uint)SkySurfaces);
        Put(root,_skyPointerAt,T4ZonePointer.Following);
        Put(root,_modelCountAt,(uint)BrushModels);
        Put(root,_modelPointerAt,T4ZonePointer.Following);
        Put(root,_waterAt,(uint)_water0.Length);
        Put(root,_waterAt+4,T4ZonePointer.Following);
        Put(root,_waterAt+8,(uint)_water1.Length);
        Put(root,_waterAt+12,T4ZonePointer.Following);
    }

    public void PlanSky(Action<long,int,bool> alloc)=>alloc(_sky.Length,4,true);
    public void PlanModels(Action<long,int,bool> alloc)=>alloc(_models.Length,4,true);
    public void PlanWater(Action<long,int,bool> alloc)
    {
        alloc(_water0.Length,4,true);
        alloc(_water1.Length,4,true);
    }
    public void EmitSky(Stream zone,Action<long,int,bool> alloc)=>Emit(zone,alloc,_sky,4);
    public void EmitModels(Stream zone,Action<long,int,bool> alloc)=>Emit(zone,alloc,_models,4);
    public void EmitWater(Stream zone,Action<long,int,bool> alloc)
    {
        Emit(zone,alloc,_water0,4);
        Emit(zone,alloc,_water1,4);
    }
    private static void Emit(Stream zone,Action<long,int,bool> alloc,byte[] bytes,int align)
    {
        alloc(bytes.Length,align,true);
        zone.Write(bytes);
    }
    private static string Sha(ReadOnlySpan<byte> b)=>
        Convert.ToHexString(SHA256.HashData(b));
    private static bool Eq(string? a,string? b)=>
        string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
}
