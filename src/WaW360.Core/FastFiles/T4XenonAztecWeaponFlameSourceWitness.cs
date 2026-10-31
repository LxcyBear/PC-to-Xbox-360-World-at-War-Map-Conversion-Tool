using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Read-only original-PC 7b source census of inline 476-byte FlameTable
/// parents. A nested sound-name pointer is NOT an Xbox sound/XMA asset.
/// Never emits native FF children or borrows oracle/donor bytes.
/// </summary>
public static class T4XenonAztecWeaponFlameSourceWitness
{
    private const int FixedWeaponBytes=0x9AC;
    private const int FlameBytes=476;
    private const uint Following=0xFFFFFFFFu;
    private const uint Insert=0xFFFFFFFEu;
    private static bool Inline(uint x)=>x is Following or Insert;
    private static string Hash(ReadOnlySpan<byte> b)=>Convert.ToHexString(SHA256.HashData(b));

    public static async Task<string> RunAsync(
        string manifestFile,string ledgerFile,string bounceWitnessFile,string outputFile,
        CancellationToken ct=default)
    {
        manifestFile=Path.GetFullPath(manifestFile);
        ledgerFile=Path.GetFullPath(ledgerFile);
        bounceWitnessFile=Path.GetFullPath(bounceWitnessFile);
        outputFile=Path.GetFullPath(outputFile);
        using var mDoc=JsonDocument.Parse(await File.ReadAllTextAsync(manifestFile,ct));
        using var lDoc=JsonDocument.Parse(await File.ReadAllTextAsync(ledgerFile,ct));
        using var bDoc=JsonDocument.Parse(await File.ReadAllTextAsync(bounceWitnessFile,ct));
        var m=mDoc.RootElement;
        var l=lDoc.RootElement;
        var b=bDoc.RootElement;
        if(m.GetProperty("schema").GetString()!="WaW360.AztecWeapon69UnifiedOriginalPcXboxBodySelection.v1" ||
           m.GetProperty("status").GetString()!="AZTEC_69_WEAPON_NATIVE_ROOTS_63_AUTHENTIC_PC_XBOX_BODIES_2_CONFLICT_4_STOCK_UNPROVEN_NOT_IN_FF" ||
           m.GetProperty("nativeWeaponDefRoots").GetInt32()!=69 ||
           m.GetProperty("uniqueSourceOwnedXboxWeaponBodyMappings").GetInt32()!=63 ||
           m.GetProperty("selectedUncompressedXboxOwnedPayloadBytes").GetInt64()!=179775 ||
           m.GetProperty("conflictingDifferentPcSourceBodiesStillFenced").GetInt32()!=2 ||
           m.GetProperty("offlineStockOnlyWeaponNamesNotRuntimeProven").GetInt32()!=4 ||
           !m.GetProperty("allRealOriginalPcSourceOwnedXboxBodyBlobShasRevalidated").GetBoolean() ||
           m.GetProperty("xboxRuntimeReady").GetBoolean() ||
           l.GetProperty("schema").GetString()!="WaW360.AztecNativeFfSizeAndSourceCoverage.v1" ||
           l.GetProperty("converted").GetProperty("sha256").GetString()!="F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6" ||
           b.GetProperty("schema").GetString()!="WaW360.Aztec63WeaponOriginalPcBounceSoundSourceWitness.v1" ||
           b.GetProperty("status").GetString()!="AZTEC_63_WEAPON_ORIGINAL_PC_BOUNCE_TABLE_INLINE_OWNERSHIP_PROVEN_NOT_IN_FF" ||
           b.GetProperty("originalPcWeaponRootsWithGenuineXbox7bOwnedProof").GetInt32()!=63 ||
           b.GetProperty("originalPcInlineSoundAliasCustomRecordsAcrossAllOwnedWeaponFields").GetInt32()!=228 ||
           b.GetProperty("originalPcOwnedInlineSoundAliasNameBytesAcrossAllOwnedWeaponFields").GetInt64()!=895 ||
           !b.GetProperty("source7bTypedWalkerProvidesPhysicalOffsets").GetBoolean() ||
           !b.GetProperty("noRetailOrOracleDonorBytes").GetBoolean() ||
           !b.GetProperty("noNativeFastFileGeneratedOrModified").GetBoolean() ||
           b.GetProperty("xboxRuntimeGameplayReady").GetBoolean())
            throw new InvalidDataException("PC 69/63 weapon manifest, original protected native FF or independent 228-owned-alias source witness changed.");

        var protectedFile=l.GetProperty("converted").GetProperty("path").GetString()??"";
        if(!File.Exists(protectedFile) || new FileInfo(protectedFile).Length!=4510695L ||
           !Hash(await File.ReadAllBytesAsync(protectedFile,ct)).Equals(
               l.GetProperty("converted").GetProperty("sha256").GetString(),
               StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Protected original OAT-good 4,510,695 byte FF changed.");
        var selected=m.GetProperty("nativeWeaponRootSourceDecisions").EnumerateArray()
            .Where(r=>IsSelected(r.GetProperty("conclusion").GetString()??""))
            .OrderBy(r=>r.GetProperty("nativeRootOrdinal").GetInt32()).ToArray();
        var oldRows=b.GetProperty("weapons").EnumerateArray().ToDictionary(
            r=>r.GetProperty("nativeWeaponRootName").GetString()??"",r=>r,StringComparer.Ordinal);
        if(selected.Length!=63 || oldRows.Count!=63)
            throw new InvalidDataException("FlameTable witness needs 63 independent source-proof roots.");

        var idxCache=new Dictionary<string,T4ProofContainerIndex>(StringComparer.OrdinalIgnoreCase);
        var rows=new List<object>();
        int inlineParents=0,nullParents=0,packedParents=0;
        int inlineNestedSoundAliases=0,inlineNestedAliasNames=0;
        long parentBytes=0,inlineNestedNameBytes=0;
        foreach(var root in selected)
        {
            ct.ThrowIfCancellationRequested();
            var name=root.GetProperty("nativeRootName").GetString()??"";
            var ordinal=root.GetProperty("nativeRootOrdinal").GetInt32();
            var proofFile=Path.GetFullPath(root.GetProperty("proofFile").GetString()??"");
            var proofSha=root.GetProperty("proofContainerSha256").GetString()??"";
            var bodySha=root.GetProperty("xboxBlobSha256").GetString()??"";
            if(!idxCache.TryGetValue(proofFile,out var index))
            {
                index=await T4NativeProofContainerReader.IndexAsync(proofFile,"weapon",ct);
                idxCache.Add(proofFile,index);
            }
            if(!index.Sha256.Equals(proofSha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Original-PC proof container SHA drift: {name}");
            var proofOrdinal=root.GetProperty("proofRecordOrdinal").GetInt32();
            if(proofOrdinal<0 || proofOrdinal>=index.Records.Count)
                throw new InvalidDataException($"Original-PC 7b record ordinal out of bounds: {name}");
            var record=index.Records[proofOrdinal];
            if(record.ContainerBlobOffset!=root.GetProperty("xboxBlobByteOffset").GetInt64() ||
               record.BlobLength!=root.GetProperty("xboxBlobBytes").GetInt32())
                throw new InvalidDataException($"Original-PC source proof record offset or length drift: {name}");
            var blob=await T4NativeProofContainerReader.ReadBlobAsync(record,ct);
            if(blob.Length<FixedWeaponBytes || !Hash(blob).Equals(bodySha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Original-PC 7b byte SHA drift: {name}");
            var direct=(root.GetProperty("conclusion").GetString()??"").StartsWith("DIRECT_",StringComparison.Ordinal);
            if(direct)
            {
                var pcFile=Path.GetFullPath(root.GetProperty("originalPcSourceFile").GetString()??"");
                var pcSha=await FastFileAnalysisCache.GetSourceSha256Async(pcFile,ct);
                var physical=root.GetProperty("originalPcBodyOffset").GetInt64();
                var packed=Convert.ToUInt32(root.GetProperty("originalPcRawNamePointer").GetString(),16);
                if(!pcSha.Equals(root.GetProperty("originalPcSourceSha256").GetString(),StringComparison.OrdinalIgnoreCase) ||
                   record.SourceOffset!=physical ||
                   !string.Equals(record.Name,$"<packed-name@0x{physical:X}:0x{packed:X8}>",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Original-PC loaded weapon name pointer/body match drift: {name}");
            }
            else if(!string.Equals(record.Name,name,StringComparison.Ordinal))
                throw new InvalidDataException($"Literal PC weapon name mismatch: {name}");

            if(!oldRows.TryGetValue(name,out var prior) ||
               prior.GetProperty("nativeRootOrdinal").GetInt32()!=ordinal ||
               !string.Equals(prior.GetProperty("originalPcWeapon7bProofContainerSha256").GetString(),proofSha,StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(prior.GetProperty("sourceOwnedXbox7bProofBodySha256").GetString(),bodySha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Independent 228-alias prior witness does not match WeaponDef proof SHA: {name}");
            if(!T4WeaponVariableSerializerProof.TryCollectSourceOwnedFlameTables(
                   blob,record.Name??name,out var tables,out var aliases,out var reason))
                throw new InvalidDataException($"Original-PC 7b FlameTable typed walk failed: {name}: {reason}");

            var aliasSource=aliases.Where(a=>a.ProofLabel.StartsWith("flameTable",StringComparison.Ordinal)).ToArray();
            var priorSource=prior.GetProperty("originalPcInlineSoundAliasRecords").EnumerateArray()
                .Where(a=>(a.GetProperty("label").GetString()??"").StartsWith("flameTable",StringComparison.Ordinal))
                .ToDictionary(a=>a.GetProperty("label").GetString()??"",a=>a,StringComparer.Ordinal);
            if(priorSource.Count!=aliasSource.Length)
                throw new InvalidDataException($"Original 7b nested flame sound alias record count/label changed: {name}");

            var tableRows=new List<object>();
            foreach(var (slot,label) in new[]{
                (2460,"flameTableFirstPersonPtr"),(2464,"flameTableThirdPersonPtr")})
            {
                var raw=BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(slot,4));
                var table=tables.SingleOrDefault(t=>t.FixedFieldOffset==slot);
                if((table is not null)!=Inline(raw))
                    throw new InvalidDataException($"Weapon {name} FlameTable parent pointer source owns unexpected inline body: {label}");
                if(raw==0)nullParents++;
                else if(!Inline(raw))packedParents++;
                else inlineParents++;
                var textBytes=table?.BigEndianFixedBytes;
                if(table is not null)
                {
                    if(table.ProofLabel!=label ||
                       textBytes is null || textBytes.Length!=FlameBytes ||
                       !blob.AsSpan(table.SourceBodyByteOffset,FlameBytes).SequenceEqual(textBytes))
                        throw new InvalidDataException($"Weapon {name} original FlameTable fixed body exact bytes mismatch: {label}");
                    parentBytes+=FlameBytes;
                }
                var children=new List<object>();
                if(textBytes is not null)
                {
                    for(var i=0;i<4;i++)
                    {
                        var soundRaw=BinaryPrimitives.ReadUInt32BigEndian(textBytes.AsSpan(460+i*4,4));
                        var childLabel=$"{label}.sound[{i}]";
                        var child=aliasSource.SingleOrDefault(a=>a.ProofLabel==childLabel);
                        if((child is not null)!=Inline(soundRaw))
                            throw new InvalidDataException($"Weapon {name} original FlameTable child pointer not source-owned: {childLabel}");
                        if(child is not null)
                        {
                            if(!priorSource.TryGetValue(childLabel,out var old) ||
                               old.GetProperty("source7bSoundAliasCustomRecordOffset").GetInt32()!=child.SourceRecordByteOffset ||
                               BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(child.SourceRecordByteOffset,4))!=child.RawStringPointer ||
                               !string.Equals(old.GetProperty("bigEndianFourByteSoundNamePointer").GetString(),
                                   $"0x{child.RawStringPointer:X8}",StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException($"Weapon {name} original inline FlameTable alias SHA/record drift: {childLabel}");
                            inlineNestedSoundAliases++;
                            if(child.InlineNameBytes is not null)
                            {
                                inlineNestedAliasNames++;
                                inlineNestedNameBytes+=child.InlineNameBytes.Length;
                            }
                        }
                        children.Add(new{
                            label=childLabel,rawPointer=$"0x{soundRaw:X8}",
                            verdict=soundRaw==0?"NULL":Inline(soundRaw)?"GENUINE_OWNED_INLINE_ALIAS":"PACKED_EXTERNAL_ALIAS_FENCED",
                            source7bAliasRecordOffset=child?.SourceRecordByteOffset
                        });
                    }
                }
                tableRows.Add(new{
                    name=label,fixedParentSlot=slot,originalPcRawParentPointer=$"0x{raw:X8}",
                    ownership=raw==0?"NULL":Inline(raw)?"SOURCE_OWNED_476_BYTE_INLINE_PARENT":"PACKED_EXTERNAL_PARENT_FENCED",
                    source7bOwnedParentByteOffset=table?.SourceBodyByteOffset,
                    source7bOwnedParentBytes=textBytes?.Length??0,
                    source7bOwnedParentSha256=textBytes is null?null:Hash(textBytes),
                    originalPcNamePointer=textBytes is null?null:
                        $"0x{BinaryPrimitives.ReadUInt32BigEndian(textBytes.AsSpan(424,4)):X8}",
                    originalPcMaterialPointerCells= textBytes is null?Array.Empty<string>():
                        Enumerable.Range(0,8).Select(i=>
                            $"0x{BinaryPrimitives.ReadUInt32BigEndian(textBytes.AsSpan(428+i*4,4)):X8}").ToArray(),
                    nestedSounds=children
                });
            }
            rows.Add(new{
                nativeWeaponRootName=name,nativeRootOrdinal=ordinal,
                originalPcProofContainerSha256=proofSha,
                originalPcXboxOwned7bBodySha256=bodySha,
                originalPcOwnedFlameParents=tableRows
            });
        }
        if(inlineParents+nullParents+packedParents!=126 ||
           inlineNestedSoundAliases!=6 || inlineNestedAliasNames!=0 ||
           parentBytes!=inlineParents*FlameBytes)
            throw new InvalidDataException(
                $"Original-PC typed FlameTable parent census did not close: inline={inlineParents} null={nullParents} packed={packedParents} nestedSound={inlineNestedSoundAliases} names={inlineNestedAliasNames}.");
        var result=new{
            schema="WaW360.AztecWeapon63OriginalPcFlameParentSourceWitness.v1",
            status="AZTEC_63_WEAPON_FLAME_PARENT_ORIGINAL_PC_OWNERSHIP_PROVEN_NOT_IN_FF",
            originalPcWeaponBodiesRechecked=63,
            twoFlameParentPointerSlotsPerWeapon=2,
            inlineOriginalPcFlameParentTables=inlineParents,
            nullOriginalPcFlameParentFields=nullParents,
            packedOriginalPcFlameParentFieldsFenced=packedParents,
            originalPcOwnedInlineFlameParentBytes=parentBytes,
            originalPcOwnedNestedFlameSoundAliasRecords=inlineNestedSoundAliases,
            originalPcOwnedNestedFlameSoundAliasNames=inlineNestedAliasNames,
            originalPcOwnedNestedFlameSoundAliasNameBytes=inlineNestedNameBytes,
            parentTablesIncludePotentialForeignMaterialAndNamePointers=true,
            parentSourcePointersMustNotBeCopiedRawIntoNativeFf=true,
            previousBounceSourceWitnessSha256=Hash(await File.ReadAllBytesAsync(bounceWitnessFile,ct)),
            originalProtectedNativeFastfileSha256=Hash(await File.ReadAllBytesAsync(protectedFile,ct)),
            originalPcProofBodiesAllShaValidated=true,
            noDonorOrOracleBytes=true,
            noNativeFastfileCreatedOrModified=true,
            noAudioXmaOrRuntimeClosure=true,
            gate11Closed=false,
            xboxHardwareReady=false,
            weapons=rows
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)??throw new InvalidDataException("No output folder"));
        await File.WriteAllTextAsync(outputFile,
            JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),
            new UTF8Encoding(false),ct);
        return outputFile;
    }

    private static bool IsSelected(string value)=>
        value is "EXACT_ORIGINAL_PC_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED" or
        "DIRECT_PC_LOADED_VIRTUAL_POINTER_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED";
}
