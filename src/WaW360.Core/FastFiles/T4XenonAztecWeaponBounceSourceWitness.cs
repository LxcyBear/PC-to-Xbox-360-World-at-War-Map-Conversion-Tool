using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Independent SOURCE-ONLY T4 WeaponDef 7b bounceSound/SndAliasCustom witness.
/// Reads the original PC-derived typed Xbox-endian proof containers; it does
/// not copy any native donor sound bytes, build a FF or authorize runtime use.
/// </summary>
public static class T4XenonAztecWeaponBounceSourceWitness
{
    private const int FixedBytes=0x9AC;
    private const int BounceFieldOffset=624;
    private const int BounceEntries=31;
    private const uint Following=0xFFFFFFFF;
    private const uint Insert=0xFFFFFFFE;

    private static bool IsInline(uint raw)=>raw is Following or Insert;

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
            throw new InvalidDataException(
                "Original-PC 69/63 weapon source manifest or protected FF size/identity changed.");

        var protectedPath=ledger.GetProperty("converted").GetProperty("path").GetString()??"";
        if(!File.Exists(protectedPath))
            throw new FileNotFoundException("Protected originally OAT-good Aztec FF missing",protectedPath);
        var goodInfo=new FileInfo(protectedPath);
        var goodSha=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(protectedPath,ct)));
        if(goodInfo.Length!=4510695 ||
           !string.Equals(goodSha,ledger.GetProperty("converted").GetProperty("sha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Protected 4,510,695 B OAT-good FF SHA or length changed.");

        var roots=m.GetProperty("nativeWeaponRootSourceDecisions").EnumerateArray().ToArray();
        if(roots.Length!=69)
            throw new InvalidDataException("Native original-PC WeaponDef root count changed.");
        var selected=roots.Where(r=>IsSelected(r.GetProperty("conclusion").GetString()??""))
            .OrderBy(r=>r.GetProperty("nativeRootOrdinal").GetInt32()).ToArray();
        if(selected.Length!=63 ||
           selected.Select(r=>r.GetProperty("nativeRootOrdinal").GetInt32()).Distinct().Count()!=63 ||
           selected.Select(r=>r.GetProperty("nativeRootName").GetString())
              .Distinct(StringComparer.Ordinal).Count()!=63)
            throw new InvalidDataException("Expected exactly 63 unique genuine original-PC weapon proof roots.");

        var proofIndexes=new Dictionary<string,T4ProofContainerIndex>(StringComparer.OrdinalIgnoreCase);
        var rows=new List<object>();
        var inlineTables=0;
        var nullTableFields=0;
        var packedTableFields=0;
        var inlineTableBytes=0L;
        var inlineBounceEntryRefs=0;
        var packedBounceEntryRefs=0;
        var nullBounceEntryRefs=0;
        var realInlineAliasRecords=0;
        var inlineAliasNames=0;
        var inlineAliasNameBytes=0L;
        var bounceInlineAliasRecords=0;
        var bounceInlineAliasNames=0;
        var bounceInlineAliasNameBytes=0L;

        foreach(var root in selected)
        {
            ct.ThrowIfCancellationRequested();
            var name=root.GetProperty("nativeRootName").GetString()??"";
            var ordinal=root.GetProperty("nativeRootOrdinal").GetInt32();
            var proof=Path.GetFullPath(root.GetProperty("proofFile").GetString()??"");
            var expectedProofSha=root.GetProperty("proofContainerSha256").GetString()??"";
            var expectedBlobSha=root.GetProperty("xboxBlobSha256").GetString()??"";
            if(!proofIndexes.TryGetValue(proof,out var index))
            {
                index=await T4NativeProofContainerReader.IndexAsync(proof,"weapon",ct);
                proofIndexes.Add(proof,index);
            }
            if(!string.Equals(index.Sha256,expectedProofSha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Original-PC weapon proof container SHA changed: {name}");
            var recordOrdinal=root.GetProperty("proofRecordOrdinal").GetInt32();
            if(recordOrdinal<0 || recordOrdinal>=index.Records.Count)
                throw new InvalidDataException($"Original-PC weapon proof ordinal out of bounds: {name}");
            var record=index.Records[recordOrdinal];
            if(record.ContainerBlobOffset!=root.GetProperty("xboxBlobByteOffset").GetInt64() ||
               record.BlobLength!=root.GetProperty("xboxBlobBytes").GetInt32())
                throw new InvalidDataException($"Original-PC weapon proof source byte extent changed: {name}");
            var blob=await T4NativeProofContainerReader.ReadBlobAsync(record,ct);
            if(blob.Length<FixedBytes ||
               !string.Equals(Convert.ToHexString(SHA256.HashData(blob)),expectedBlobSha,
                   StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Original PC WeaponDef source blob SHA changed: {name}");

            var verdict=root.GetProperty("conclusion").GetString()??"";
            if(verdict.StartsWith("DIRECT_",StringComparison.Ordinal))
            {
                var sourcePc=root.GetProperty("originalPcSourceFile").GetString()??"";
                var pcExpected=root.GetProperty("originalPcSourceSha256").GetString()??"";
                var pcActual=await FastFileAnalysisCache.GetSourceSha256Async(sourcePc,ct);
                var offset=root.GetProperty("originalPcBodyOffset").GetInt64();
                var namePtr=Convert.ToUInt32(root.GetProperty("originalPcRawNamePointer").GetString(),16);
                var expectedName=$"<packed-name@0x{offset:X}:0x{namePtr:X8}>";
                if(!string.Equals(pcExpected,pcActual,StringComparison.OrdinalIgnoreCase) ||
                   record.SourceOffset!=offset ||
                   !string.Equals(record.Name,expectedName,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Direct original-PC weapon name pointer identity drift: {name}");
            }
            else if(!string.Equals(record.Name,name,StringComparison.Ordinal))
                throw new InvalidDataException($"Literal original-PC WeaponDef source name drift: {name}");

            if(!T4WeaponVariableSerializerProof.TryCollectSourceOwnedBounceSound(
                   blob,record.Name??name,out var tables,out var aliases,out var reason))
                throw new InvalidDataException(
                    $"Original-PC typed 7b bounce/sound name traversal refused {name}: {reason}");

            var bouncePointer=BinaryPrimitives.ReadUInt32BigEndian(
                blob.AsSpan(BounceFieldOffset,4));
            var actualTable=tables.Count==1?tables[0]:null;
            if(tables.Count>1 || (actualTable is not null)!=IsInline(bouncePointer))
                throw new InvalidDataException(
                    $"Original-PC bounceSound fixed pointer vs typed owned stream mismatch: {name}");
            if(actualTable is not null)
            {
                if(actualTable.FixedFieldOffset!=BounceFieldOffset ||
                   actualTable.BigEndianPointerBytes.Length!=BounceEntries*4 ||
                   !blob.AsSpan(actualTable.SourceBodyByteOffset,BounceEntries*4)
                       .SequenceEqual(actualTable.BigEndianPointerBytes))
                    throw new InvalidDataException($"Original-PC exact inline bounce pointer-table bytes changed: {name}");
                inlineTables++;
                inlineTableBytes+=BounceEntries*4;
            }
            else if(bouncePointer==0)nullTableFields++;
            else packedTableFields++;

            var aliasLabels=new HashSet<string>(StringComparer.Ordinal);
            var bounceAliases=new Dictionary<string,T4WeaponVariableSerializerProof.OwnedInlineSndAliasName>(
                StringComparer.Ordinal);
            var aliasRows=new List<object>();
            foreach(var alias in aliases)
            {
                if(!aliasLabels.Add(alias.ProofLabel) ||
                   alias.SourceRecordByteOffset<FixedBytes ||
                   (long)alias.SourceRecordByteOffset+4>blob.Length ||
                   BinaryPrimitives.ReadUInt32BigEndian(
                       blob.AsSpan(alias.SourceRecordByteOffset,4))!=alias.RawStringPointer)
                    throw new InvalidDataException(
                        $"Duplicate label or sound alias record pointer source bytes mismatch: {name}/{alias.ProofLabel}");
                var nameOwned=IsInline(alias.RawStringPointer);
                if(nameOwned!=(alias.InlineNameBytes is not null) ||
                   nameOwned!=(alias.SourceInlineNameByteOffset is not null))
                    throw new InvalidDataException($"Inline source sound alias name proof mismatch: {name}/{alias.ProofLabel}");
                if(nameOwned)
                {
                    var offset=alias.SourceInlineNameByteOffset!.Value;
                    var nameBytes=alias.InlineNameBytes!;
                    if(nameBytes.Length==0 || nameBytes[^1]!=0 ||
                       offset<FixedBytes || (long)offset+nameBytes.Length>blob.Length ||
                       !blob.AsSpan(offset,nameBytes.Length).SequenceEqual(nameBytes))
                        throw new InvalidDataException($"Original-PC sound name bytes/extent mismatch: {name}/{alias.ProofLabel}");
                    inlineAliasNames++;
                    inlineAliasNameBytes+=nameBytes.Length;
                }
                realInlineAliasRecords++;
                var isBounce=alias.ProofLabel.StartsWith("bounceSound[",StringComparison.Ordinal);
                if(isBounce)
                {
                    bounceAliases.Add(alias.ProofLabel,alias);
                    bounceInlineAliasRecords++;
                    if(nameOwned)
                    {
                        bounceInlineAliasNames++;
                        bounceInlineAliasNameBytes+=alias.InlineNameBytes!.Length;
                    }
                }
                aliasRows.Add(new{
                    label=alias.ProofLabel,
                    source7bSoundAliasCustomRecordOffset=alias.SourceRecordByteOffset,
                    bigEndianFourByteSoundNamePointer=$"0x{alias.RawStringPointer:X8}",
                    namePointerClassification=alias.RawStringPointer==0?"NULL":
                        nameOwned?"ORIGINAL_PC_INLINE_SOUND_NAME":
                        "PACKED_EXTERNAL_SOUND_NAME_FENCED",
                    originalPcOwnedInlineNameSerializedOffset=alias.SourceInlineNameByteOffset,
                    originalPcOwnedInlineNameBytes=alias.InlineNameBytes?.Length??0,
                    originalPcOwnedInlineNameSha256=alias.InlineNameBytes is null?null:
                        Convert.ToHexString(SHA256.HashData(alias.InlineNameBytes)),
                    soundAssetBodyOwned=false
                });
            }

            var tableEntries=new List<object>();
            if(actualTable is not null)
            {
                for(int i=0;i<BounceEntries;i++)
                {
                    var raw=BinaryPrimitives.ReadUInt32BigEndian(
                        actualTable.BigEndianPointerBytes.AsSpan(i*4,4));
                    var label=$"bounceSound[{i}]";
                    var owned=IsInline(raw);
                    if(owned!=bounceAliases.ContainsKey(label))
                        throw new InvalidDataException(
                            $"Bounce sound nested owned alias record mismatch: {name}/{label}");
                    if(owned)inlineBounceEntryRefs++;
                    else if(raw==0)nullBounceEntryRefs++;
                    else packedBounceEntryRefs++;
                    tableEntries.Add(new{
                        index=i,
                        originalPcPointerRaw=$"0x{raw:X8}",
                        verdict=raw==0?"NULL_SOUND_ALIAS":
                            owned?"INLINE_ORIGINAL_PC_SND_ALIAS_CUSTOM_RECORD_PROVEN":
                            "PACKED_EXTERNAL_SOUND_ALIAS_FENCED",
                        source7bInlineAliasRecordByteOffset=owned?
                            (int?)bounceAliases[label].SourceRecordByteOffset:null
                    });
                }
            }
            else if(bounceAliases.Count!=0)
                throw new InvalidDataException(
                    $"Unowned bounceSound table somehow has nested observed aliases: {name}");

            rows.Add(new{
                nativeRootOrdinal=ordinal,
                nativeWeaponRootName=name,
                sourceOwnedProofFile=proof,
                originalPcWeapon7bProofContainerSha256=expectedProofSha,
                sourceOwnedXbox7bProofBodySha256=expectedBlobSha,
                originalPcOwned7bProofBodyByteCount=blob.Length,
                fixedBodyBounceSoundPointerOffset=BounceFieldOffset,
                originalPcBounceSoundSourcePointerRaw=$"0x{bouncePointer:X8}",
                bounceTableSourceVerdict=bouncePointer==0?"NULL":
                    actualTable is null?"PACKED_EXTERNAL_TABLE_FENCED":
                    "INLINE_31_POINTER_TABLE_SOURCE_BYTES_PROVEN",
                originalPcInlineBounceTableSerializedByteOffset=actualTable?.SourceBodyByteOffset,
                originalPcOwnedInlineBouncePointerTableBytes=actualTable?.BigEndianPointerBytes.Length??0,
                originalPcOwnedInlineBounceTableSha256=actualTable is null?null:
                    Convert.ToHexString(SHA256.HashData(actualTable.BigEndianPointerBytes)),
                bounceTableEntries=tableEntries,
                originalPcInlineSoundAliasRecords=aliasRows,
                allRealAudioSamplePayloadsStillUnproven=true
            });
        }
        if(inlineTables+packedTableFields+nullTableFields!=63 ||
           inlineTableBytes!=inlineTables*BounceEntries*4L ||
           inlineBounceEntryRefs+packedBounceEntryRefs+nullBounceEntryRefs!=inlineTables*BounceEntries)
            throw new InvalidDataException("Typed original-PC bounceSound source-table count/bytes inconsistent.");

        var result=new{
            schema="WaW360.Aztec63WeaponOriginalPcBounceSoundSourceWitness.v1",
            status=inlineTables>0?
                "AZTEC_63_WEAPON_ORIGINAL_PC_BOUNCE_TABLE_INLINE_OWNERSHIP_PROVEN_NOT_IN_FF":
                "AZTEC_63_WEAPON_BOUNCE_TABLES_ABSENT_OR_PACKED_NO_INLINE_PROMOTION",
            originalPcWeaponRootsWithGenuineXbox7bOwnedProof=63,
            nativeWeaponRoots=69,
            originalPcWeaponOwnedPayloadSourceBytes=179775,
            fixedBouncePointerFieldOffset=BounceFieldOffset,
            bounceTableEntryCount=BounceEntries,
            inlineOriginalPcBounceTables=inlineTables,
            packedBounceTablesQuarantined=packedTableFields,
            nullBounceTableFields=nullTableFields,
            originalPcOwnedInlineBouncePointerTableBytes=inlineTableBytes,
            inlineBounceEntryAliasRecords=inlineBounceEntryRefs,
            packedBounceEntryAliasPointersQuarantined=packedBounceEntryRefs,
            nullBounceEntryAliasPointers=nullBounceEntryRefs,
            originalPcInlineSoundAliasCustomRecordsAcrossAllOwnedWeaponFields=realInlineAliasRecords,
            originalPcInlineSoundAliasNameStringsAcrossAllOwnedWeaponFields=inlineAliasNames,
            originalPcOwnedInlineSoundAliasNameBytesAcrossAllOwnedWeaponFields=inlineAliasNameBytes,
            originalPcBounceChildInlineAliasRecords=bounceInlineAliasRecords,
            originalPcBounceChildInlineNameStrings=bounceInlineAliasNames,
            originalPcBounceChildInlineNameBytes=bounceInlineAliasNameBytes,
            sourcePcWeaponBodySourceProofHashesAllRevalidated=true,
            noRetailOrOracleDonorBytes=true,
            originalSourceEndianBig=true,
            source7bTypedWalkerProvidesPhysicalOffsets=true,
            sourceOwnedPointerBytesAreNotValidNativeFfRebases=true,
            noNativeFastFileGeneratedOrModified=true,
            noNewSoundBodiesOrXmaDecoded=true,
            soundAliasGraphAndRuntimeAudioStillBlocked=true,
            protectedOriginalOatAcceptedNativeFfBytes=goodInfo.Length,
            protectedOriginalOatAcceptedNativeFfSha256=goodSha,
            xboxRuntimeGameplayReady=false,
            gate11Closed=false,
            note="Source-only forensic census. Inline bounce 31-pointer tables are REAL original-PC bytes, but they contain sound alias pointers and cannot be grafted raw. Packed aliases, sound bodies and XMA streaming must be resolved separately before any native WeaponDef/audio FF emission.",
            weapons=rows
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputJson)
            ??throw new InvalidDataException("No bounce source proof output directory."));
        await File.WriteAllTextAsync(outputJson,
            JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),
            new UTF8Encoding(false),ct);
        return outputJson;
    }

    private static bool IsSelected(string value)=>
        value is "EXACT_ORIGINAL_PC_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED"
            or "DIRECT_PC_LOADED_VIRTUAL_POINTER_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED";
}
