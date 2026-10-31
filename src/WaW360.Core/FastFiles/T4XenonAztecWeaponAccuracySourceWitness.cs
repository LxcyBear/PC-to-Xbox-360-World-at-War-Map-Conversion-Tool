using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Read-only independent SHA/typed Alpha 7b witness for the next candidate
/// NON-STRING native WeaponDef child type: accuracy graph vec2 knot arrays.
/// Four known fixed-body pointer slots and two original source counts are
/// checked; variable source body offsets come ONLY from the typed 7b walker.
/// No FF writer, no OAT, no donor/oracle bodies and no runtime ready claim.
/// </summary>
public static class T4XenonAztecWeaponAccuracySourceWitness
{
    private const int FixedBytes = 0x9AC;
    private const uint Following = 0xFFFFFFFF;
    private const uint Insert = 0xFFFFFFFE;
    private sealed record Slot(int FieldOffset,int CountFieldOffset,string Label);
    // Must preserve the actual T4 ZoneCode traversal order, not slot order.
    private static readonly Slot[] Slots =
    {
        new(2184,2200,"aiVsAiAccuracyGraphKnots"),
        new(2192,2200,"originalAiVsAiAccuracyGraphKnots"),
        new(2188,2204,"aiVsPlayerAccuracyGraphKnots"),
        new(2196,2204,"originalAiVsPlayerAccuracyGraphKnots")
    };

    public static async Task<string> RunAsync(
        string weapon69Manifest,string protectedSizeLedger,string outputJson,
        CancellationToken ct=default)
    {
        weapon69Manifest=Path.GetFullPath(weapon69Manifest);
        protectedSizeLedger=Path.GetFullPath(protectedSizeLedger);
        outputJson=Path.GetFullPath(outputJson);
        using var manifestDoc=JsonDocument.Parse(await File.ReadAllTextAsync(weapon69Manifest,ct));
        using var sizeDoc=JsonDocument.Parse(await File.ReadAllTextAsync(protectedSizeLedger,ct));
        var m=manifestDoc.RootElement;
        var ledger=sizeDoc.RootElement;
        if(m.GetProperty("schema").GetString()!="WaW360.AztecWeapon69UnifiedOriginalPcXboxBodySelection.v1" ||
           m.GetProperty("status").GetString()!="AZTEC_69_WEAPON_NATIVE_ROOTS_63_AUTHENTIC_PC_XBOX_BODIES_2_CONFLICT_4_STOCK_UNPROVEN_NOT_IN_FF" ||
           m.GetProperty("nativeWeaponDefRoots").GetInt32()!=69 ||
           m.GetProperty("uniqueSourceOwnedXboxWeaponBodyMappings").GetInt32()!=63 ||
           m.GetProperty("selectedUncompressedXboxOwnedPayloadBytes").GetInt64()!=179775 ||
           m.GetProperty("conflictingDifferentPcSourceBodiesStillFenced").GetInt32()!=2 ||
           m.GetProperty("offlineStockOnlyWeaponNamesNotRuntimeProven").GetInt32()!=4 ||
           !m.GetProperty("allRealOriginalPcSourceOwnedXboxBodyBlobShasRevalidated").GetBoolean() ||
           m.GetProperty("xboxRuntimeReady").GetBoolean() ||
           ledger.GetProperty("schema").GetString()!="WaW360.AztecNativeFfSizeAndSourceCoverage.v1" ||
           !string.Equals(ledger.GetProperty("converted").GetProperty("sha256").GetString(),
              "F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6",
              StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("WeaponDef 69 native roots, authentic 63 original-PC body SHA or protected FF source ledger changed.");

        var protectedPath=ledger.GetProperty("converted").GetProperty("path").GetString()??"";
        if(!File.Exists(protectedPath))
            throw new FileNotFoundException("Protected originally OAT-passing Aztec FF missing",protectedPath);
        var goodInfo=new FileInfo(protectedPath);
        var goodSha=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(protectedPath,ct)));
        if(goodInfo.Length!=4510695 ||
           !string.Equals(goodSha,ledger.GetProperty("converted").GetProperty("sha256").GetString(),
               StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Protected 4,510,695-byte native FastFile changed SHA/length; forensic readback fenced.");

        var rootRows=m.GetProperty("nativeWeaponRootSourceDecisions").EnumerateArray().ToArray();
        if(rootRows.Length!=69 || Slots.Length!=4 ||
           Slots.Select(s=>s.FieldOffset).Distinct().Count()!=4)
            throw new InvalidDataException("Original native WeaponDef root/source accuracy layout census cardinality changed.");

        var selected=rootRows.Where(r=>IsSelected(r.GetProperty("conclusion").GetString()??""))
            .OrderBy(r=>r.GetProperty("nativeRootOrdinal").GetInt32()).ToArray();
        if(selected.Length!=63 || selected.Select(r=>r.GetProperty("nativeRootOrdinal").GetInt32()).Distinct().Count()!=63 ||
           selected.Select(r=>r.GetProperty("nativeRootName").GetString()).Distinct(StringComparer.Ordinal).Count()!=63)
            throw new InvalidDataException("Expected exactly 63 unique source-matched native WeaponDefs.");
        var proofIndexes=new Dictionary<string,T4ProofContainerIndex>(StringComparer.OrdinalIgnoreCase);
        var rows=new List<object>();
        int inlineArrays=0,inlineKnots=0,packed=0,empty=0,inlineWeaponRoots=0;
        long inlineBytes=0;
        foreach(var selectedRoot in selected)
        {
            ct.ThrowIfCancellationRequested();
            var name=selectedRoot.GetProperty("nativeRootName").GetString()??"";
            var nativeOrdinal=selectedRoot.GetProperty("nativeRootOrdinal").GetInt32();
            var proof=Path.GetFullPath(selectedRoot.GetProperty("proofFile").GetString()??"");
            var expectedProofSha=selectedRoot.GetProperty("proofContainerSha256").GetString()??"";
            var expectedBlobSha=selectedRoot.GetProperty("xboxBlobSha256").GetString()??"";
            if(!proofIndexes.TryGetValue(proof,out var proofIndex))
            {
                proofIndex=await T4NativeProofContainerReader.IndexAsync(proof,"weapon",ct);
                proofIndexes.Add(proof,proofIndex);
            }
            if(!string.Equals(proofIndex.Sha256,expectedProofSha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Original PC owned WeaponDef proof SHA changed: {name}");
            var recordOrdinal=selectedRoot.GetProperty("proofRecordOrdinal").GetInt32();
            if(recordOrdinal<0 || recordOrdinal>=proofIndex.Records.Count)
                throw new InvalidDataException($"Original PC proof record ordinal out of range: {name}");
            var record=proofIndex.Records[recordOrdinal];
            if(record.ContainerBlobOffset!=selectedRoot.GetProperty("xboxBlobByteOffset").GetInt64() ||
               record.BlobLength!=selectedRoot.GetProperty("xboxBlobBytes").GetInt32())
                throw new InvalidDataException($"Original PC WeaponDef 7b proof blob offset/size changed: {name}");
            var original=await T4NativeProofContainerReader.ReadBlobAsync(record,ct);
            if(original.Length<FixedBytes || !string.Equals(
                 Convert.ToHexString(SHA256.HashData(original)),expectedBlobSha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Original PC WeaponDef 7b owned source bytes changed: {name}");

            var direct=(selectedRoot.GetProperty("conclusion").GetString()??"")
                .StartsWith("DIRECT_",StringComparison.Ordinal);
            if(direct)
            {
                var sourcePc=selectedRoot.GetProperty("originalPcSourceFile").GetString()??"";
                var pcExpected=selectedRoot.GetProperty("originalPcSourceSha256").GetString()??"";
                var pcActual=await FastFileAnalysisCache.GetSourceSha256Async(sourcePc,ct);
                var bodyOffset=selectedRoot.GetProperty("originalPcBodyOffset").GetInt64();
                var ptr=Convert.ToUInt32(selectedRoot.GetProperty("originalPcRawNamePointer").GetString(),16);
                var synthetic=$"<packed-name@0x{bodyOffset:X}:0x{ptr:X8}>";
                if(!string.Equals(pcActual,pcExpected,StringComparison.OrdinalIgnoreCase) ||
                   record.SourceOffset!=bodyOffset ||
                   !string.Equals(record.Name,synthetic,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Direct virtual-string source identity drift on WeaponDef accuracy arrays: {name}");
            }
            else if(!string.Equals(record.Name,name,StringComparison.Ordinal))
                throw new InvalidDataException($"Literal source WeaponDef name differs from native source root: {name}");

            if(!T4WeaponVariableSerializerProof.TryCollectSourceOwnedAccuracyVec2Arrays(
                    original,record.Name??name,out var actual,out var reason))
                throw new InvalidDataException($"Genuine original-PC 7b typed accuracy graph traversal refused {name}: {reason}");
            var actualByOffset=actual.ToDictionary(a=>a.FixedFieldOffset);
            var perSlot=new List<object>();
            int thisInline=0;
            foreach(var slot in Slots)
            {
                var pointer=BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(slot.FieldOffset,4));
                var knots=BinaryPrimitives.ReadInt32BigEndian(original.AsSpan(slot.CountFieldOffset,4));
                if(knots is < 0 or > 4096)
                    throw new InvalidDataException($"Source 7b accuracy count out of bounds for {name}/{slot.Label}: {knots}");
                var has=actualByOffset.TryGetValue(slot.FieldOffset,out var array);
                var pointerInline=pointer is Following or Insert;
                if(has!=(pointerInline && knots>0))
                    throw new InvalidDataException($"Original PC accuracy array pointer/owned byte walk mismatch: {name}/{slot.Label}");
                var classification=pointer==0?"NULL_SOURCE_POINTER":
                    !pointerInline?"PACKED_OR_EXTERNAL_SOURCE_POINTER_FENCED":
                    knots==0?"ZERO_KNOT_INLINE_NO_OWNED_BYTES":
                    "ORIGINAL_PC_OWNED_TYPED_INLINE_VEC2_ARRAY_PROVEN";
                var sourceByteOffset=has?array!.SourceBodyByteOffset:(int?)null;
                var payloadBytes=has?array!.BigEndianKnotBytes.Length:0;
                var payloadSha=has?Convert.ToHexString(SHA256.HashData(array!.BigEndianKnotBytes)):null;
                if(has)
                {
                    if(array!.KnotCount!=knots ||
                       !string.Equals(array.ProofLabel,slot.Label,StringComparison.Ordinal) ||
                       !original.AsSpan(array.SourceBodyByteOffset,payloadBytes).SequenceEqual(array.BigEndianKnotBytes))
                        throw new InvalidDataException($"Source owned accuracy Vec2 payload/type/offset mismatch: {name}/{slot.Label}");
                    thisInline++;
                    inlineArrays++;
                    inlineKnots+=knots;
                    inlineBytes+=payloadBytes;
                }
                else if(pointer==0)empty++;
                else if(!pointerInline)packed++;
                perSlot.Add(new{
                    pointerFieldOffset=slot.FieldOffset,
                    countFieldOffset=slot.CountFieldOffset,
                    typeName="vec2_t[knots] / big-endian 2 x 32-bit scalar per knot",
                    proofLabel=slot.Label,
                    originalPcPointerRaw=$"0x{pointer:X8}",
                    knotCount=knots,
                    verdict=classification,
                    source7bOwnedSerializedBodyByteOffset=sourceByteOffset,
                    source7bOwnedXenonEndianPayloadBytes=payloadBytes,
                    source7bOwnedPayloadSha256=payloadSha,
                    canBeCopiedIntoProductionXboxFf=false
                });
            }
            if(thisInline>0)inlineWeaponRoots++;
            rows.Add(new{
                nativeRootOrdinal=nativeOrdinal,
                nativeWeaponRootName=name,
                sourceOwnedProofFile=proof,
                originalPcWeapon7bProofContainerSha256=expectedProofSha,
                sourceOwnedXbox7bProofBodySha256=expectedBlobSha,
                originalPcOwned7bProofBodyByteCount=original.Length,
                inlineOwnedVec2Arrays=thisInline,
                fourAccuracySourceFields=perSlot
            });
        }
        if(inlineBytes!=(long)inlineKnots*8)
            throw new InvalidDataException("Source original-PC vec2 payload/knots byte cardinality failed.");
        var totalSites=63*4;
        var allFields=rows.Count*Slots.Length;
        if(allFields!=totalSites || inlineArrays+packed+empty>allFields)
            throw new InvalidDataException("Original 63 WeaponDef accuracy-array field census cardinality changed.");
        var result=new{
            schema="WaW360.Aztec63WeaponOriginalPcAccuracyVec2OwnedSourceWitness.v1",
            status=inlineArrays>0?
                "AZTEC_63_WEAPON_ORIGINAL_PC_INLINE_VEC2_CHILD_GRAPH_SOURCE_PROVEN_NOT_IN_FF":
                "AZTEC_63_WEAPON_ACCURACY_VEC2_ALL_SOURCE_ARRAYS_ABSENT_OR_PACKED_NO_INLINE_PROOF",
            originalPcWeaponRootsWithGenuineXbox7bOwnedProof=63,
            totalNativeWeaponRoots=69,
            originalPcWeaponOwnedPayloadSourceBytes=179775,
            originalPcFixedAccuracyPointerFields=totalSites,
            fourSourceAccuracyArraysPerWeapon=4,
            weaponRootsWithRealInlineVec2Children=inlineWeaponRoots,
            provenInlineSourceOwnedVec2ArrayRecords=inlineArrays,
            provenInlineSourceOwnedVec2Knots=inlineKnots,
            provenInlineBigEndianXenonOwnedBytes=inlineBytes,
            packedSourceArrayPointersQuarantined=packed,
            nullSourceArrayPointers=empty,
            sourcePcWeaponBodySourceProofHashesAllRevalidated=true,
            selectedSourceBodiesUseOriginalPcOnly=true,
            noRetailOrOracleDonorBytes=true,
            noWeaponDefChildGraphsCopiedToNativeFf=true,
            protectedOriginalOatAcceptedNativeFfBytes=goodInfo.Length,
            protectedOriginalOatAcceptedNativeFfSha256=goodSha,
            noXboxFfGeneratedOrModified=true,
            onlySourceOwnedInlineArraysQualifyForFutureRebase=true,
            theOriginalPc7bVariableStreamTraversalProvidedArrayOffsets=true,
            externalPackedPointerTargetsNeverPromoted=true,
            xboxRuntimeGameplayReady=false,
            gate11Closed=false,
            note="This read-only source-owned proof is a prerequisite for future exact native FF relocation. The 4 real fixed accuracy pointers, 2 counts, and genuine 7b inline serialized vec2 arrays have been source-walked. NO weapon knot arrays are placed into an actual native Xbox FF, OAT is NOT run, and no packed pointer is promoted.",
            weapons=rows
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputJson)
            ??throw new InvalidDataException("Cannot resolve forensic audit output folder."));
        await File.WriteAllTextAsync(outputJson,
            JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),
            new UTF8Encoding(false),ct);
        return outputJson;
    }

    private static bool IsSelected(string verdict)=>
        verdict is "EXACT_ORIGINAL_PC_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED"
            or "DIRECT_PC_LOADED_VIRTUAL_POINTER_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED";
}
