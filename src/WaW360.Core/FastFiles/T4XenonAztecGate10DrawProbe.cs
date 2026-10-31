using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Strict PC-owned draw input for an isolated Aztec OAT diagnostic.
/// Never treats one unverified stock XModel as a valid runtime body.
/// </summary>
public sealed class T4XenonAztecGate10DrawProbe
{
    private readonly byte[] _source;
    private readonly T4XenonStaticDrawLayoutRebaser.Draw[] _models;
    public int DrawCount => _models.Length;
    public int UnverifiedStockDraws => 1;
    public bool HardwareReady => false;
    public int SourceByteCount => _source.Length;

    private T4XenonAztecGate10DrawProbe(byte[] source,
        T4XenonStaticDrawLayoutRebaser.Draw[] models)
    {
        _source=source;_models=models;
    }

    public static async Task<T4XenonAztecGate10DrawProbe> LoadAsync(
        string lightReportPath,string relocationSummaryPath,CancellationToken ct=default)
    {
        using var lightDoc=JsonDocument.Parse(await File.ReadAllTextAsync(lightReportPath,ct));
        using var summaryDoc=JsonDocument.Parse(await File.ReadAllTextAsync(relocationSummaryPath,ct));
        var light=lightDoc.RootElement;
        var summary=summaryDoc.RootElement;
        if(light.GetProperty("schema").GetString()!="WaW360.AztecGate10SourceLightHypothesisCandidate.v1" ||
           light.GetProperty("status").GetString()!="AZTEC_GATE10_SOURCE_LIGHT_ARITHMETIC_MATCHES_ORIGINAL_XBOX_1903_NOT_PRODUCTION" ||
           light.GetProperty("reflectionIndexBaseHypothesisExactRows").GetInt32()!=1903 ||
           light.GetProperty("lightingHandleAdd256HypothesisExactRows").GetInt32()!=1903 ||
           light.GetProperty("sourceFlagsRetained").GetInt32()!=1903 ||
           light.GetProperty("convertedOwnedXModelPointersRetained").GetInt32()!=1902 ||
           light.GetProperty("externallyResidentStockXModelUnproven").GetInt32()!=1 ||
           light.GetProperty("nativeReferencePayloadBytesCopied").GetBoolean() ||
           light.GetProperty("productionApproved").GetBoolean() ||
           summary.GetProperty("schema").GetString()!="WaW360.Aztec1903ModelRootRelocationPlanSummary.v1" ||
           summary.GetProperty("sourceDrawCount").GetInt32()!=1903 ||
           summary.GetProperty("physicalXModelBodyDraws").GetInt32()!=1902 ||
           summary.GetProperty("externalStockBodyIdentityDraws").GetInt32()!=1 ||
           summary.GetProperty("externalStockResidencyProven").GetBoolean())
            throw new InvalidDataException("Gate10 diagnostic inputs lack exact source/ownership provenance.");

        var source=await File.ReadAllBytesAsync(light.GetProperty("candidate").GetString()!,ct);
        if(source.Length!=1903*60 || !Eq(Sha(source),light.GetProperty("candidateSha256").GetString()))
            throw new InvalidDataException("Original PC 1903-draw byte candidate changed.");

        var planPath=summary.GetProperty("planFile").GetString()!;
        var planBytes=await File.ReadAllBytesAsync(planPath,ct);
        if(!Eq(Sha(planBytes),summary.GetProperty("planSha256").GetString()))
            throw new InvalidDataException("Original PC draw-to-root relocation plan SHA changed.");
        using var planDoc=JsonDocument.Parse(planBytes);
        var plan=planDoc.RootElement;
        if(plan.GetProperty("schema").GetString()!="WaW360.Aztec1903ModelRootRelocations.v1" ||
           plan.GetProperty("drawCount").GetInt32()!=1903 ||
           plan.GetProperty("nativeRecordStride").GetInt32()!=60 ||
           plan.GetProperty("nativeModelPointerFieldOffset").GetInt32()!=32 ||
           plan.GetProperty("donorBytesUsed").GetBoolean())
            throw new InvalidDataException("Source draw plan has incompatible T4 ABI.");

        var entries=plan.GetProperty("records").EnumerateArray().ToArray();
        if(entries.Length!=1903)
            throw new InvalidDataException("Source PC draw count differs from 1903.");
        var models=new T4XenonStaticDrawLayoutRebaser.Draw[1903];
        var owned=0;var stock=0;
        for(var i=0;i<entries.Length;i++)
        {
            var row=entries[i];
            var kind=row.GetProperty("bodyStatus").GetString();
            var real=kind=="SOURCE_XMODEL_EMITTED" &&
                row.GetProperty("sourceOwnedBodyReady").GetBoolean();
            var external=kind=="STOCK_REFERENCE_RESIDENCY_UNVERIFIED" &&
                !row.GetProperty("sourceOwnedBodyReady").GetBoolean();
            if(row.GetProperty("ordinal").GetInt32()!=i ||
               row.GetProperty("recordOffsetInFutureArray").GetInt32()!=i*60 ||
               row.GetProperty("modelPointerOffsetInFutureArray").GetInt32()!=i*60+32 ||
               (!real && !external))
                throw new InvalidDataException($"Unresolved source draw #{i}.");
            if(real)owned++;else stock++;
            models[i]=new(i,row.GetProperty("nativeXModelRootIndex").GetInt32(),real);
        }
        if(owned!=1902 || stock!=1)
            throw new InvalidDataException("Source model ownership count drifted.");
        return new(source,models);
    }

    public byte[] RebaseForLayout(IReadOnlyDictionary<int,uint> stableByRoot)
    {
        var rebased=T4XenonStaticDrawLayoutRebaser.Rebase(_source,60,32,_models,stableByRoot);
        if(rebased.SourceOwnedRelocated!=1902 ||
           rebased.UnverifiedExternalDraws!=1 ||
           rebased.Candidate.Length!=1903*60)
            throw new InvalidDataException("Final-layout static draw relocation count mismatch.");
        return rebased.Candidate;
    }

    private static string Sha(ReadOnlySpan<byte> x)=>
        Convert.ToHexString(SHA256.HashData(x));
    private static bool Eq(string? a,string? b)=>
        string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
}
