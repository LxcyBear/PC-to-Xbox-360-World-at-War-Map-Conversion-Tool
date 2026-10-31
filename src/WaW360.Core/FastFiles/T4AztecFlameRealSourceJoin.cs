using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Cross-checks four genuine 476-byte FlameTables against the exact PC
/// FastFile used by the independent OAT-loaded 39 world materials.
/// The packed offset is meaningful only inside the SAME original source zone;
/// a numeric match without a byte-for-byte WeaponDef+parent provenance is NOT
/// a native material, and missing refs are NEVER zeroed or synthesized.
/// </summary>
public static class T4AztecFlameRealSourceJoin
{
    private const int WeaponFixedBytes=0x9AC;
    private const int FlameFixedBytes=476;
    private static string Sha(ReadOnlySpan<byte> b)=>Convert.ToHexString(SHA256.HashData(b));
    private static uint U32(byte[] b,int offset)=>BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset,4));
    private static bool IsInline(uint p)=>p is T4ZonePointer.Following or T4ZonePointer.Insert;
    private static bool IsSelected(string s)=>s is
        "EXACT_ORIGINAL_PC_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED" or
        "DIRECT_PC_LOADED_VIRTUAL_POINTER_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED";

    private static uint ParsePointer(string? s)
    {
        if(s is null || s.Length!=10 || !s.StartsWith("0x",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Non-canonical original PC pointer '{s}'.");
        return Convert.ToUInt32(s[2..],16);
    }

    // Compare ORIGINAL PC little-endian source fixed WeaponDef against the
    // independent 7b proof's Xbox big-endian fixed body, including scriptstrings.
    // A weak offset or same-name match is insufficient to bind two .ff sources.
    private static bool SameOriginalPcWeaponFixed(byte[] pc,int offset,byte[] be)
    {
        if(offset<0 || offset>pc.Length-WeaponFixedBytes || be.Length<WeaponFixedBytes)
            return false;
        var fixedBe=new byte[WeaponFixedBytes];
        for(int i=0;i<WeaponFixedBytes;i+=4)
        {
            fixedBe[i]=pc[offset+i+3];
            fixedBe[i+1]=pc[offset+i+2];
            fixedBe[i+2]=pc[offset+i+1];
            fixedBe[i+3]=pc[offset+i];
        }
        foreach(var (start,count) in new[]{(224,8),(240,20),(280,20)})
            for(int j=0;j<count;j++)
            {
                var p=start+j*2;
                fixedBe[p]=pc[offset+p+1];
                fixedBe[p+1]=pc[offset+p];
            }
        return fixedBe.AsSpan().SequenceEqual(be.AsSpan(0,WeaponFixedBytes));
    }

    // FlameTable 7b serializer wrote all 476 bytes as endian-swapped u32.
    private static bool SameOriginalPcFlameParent(
        byte[] pc,long absolutePcOffset,string expectedSha)
    {
        if(absolutePcOffset<0 || absolutePcOffset>pc.Length-FlameFixedBytes)
            return false;
        var o=checked((int)absolutePcOffset);
        var be=new byte[FlameFixedBytes];
        for(int i=0;i<FlameFixedBytes;i+=4)
        {
            be[i]=pc[o+i+3];
            be[i+1]=pc[o+i+2];
            be[i+2]=pc[o+i+1];
            be[i+3]=pc[o+i];
        }
        return Sha(be).Equals(expectedSha,StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string> RunAsync(
        string originalWeaponManifest,string sourceFlameWitness,
        string verifiedWorldMaterialWorklist,string outputJson,
        CancellationToken ct=default)
    {
        using var wd=JsonDocument.Parse(await File.ReadAllTextAsync(originalWeaponManifest,ct));
        using var fd=JsonDocument.Parse(await File.ReadAllTextAsync(sourceFlameWitness,ct));
        using var md=JsonDocument.Parse(await File.ReadAllTextAsync(verifiedWorldMaterialWorklist,ct));
        var w=wd.RootElement;
        var f=fd.RootElement;
        var m=md.RootElement;
        if(w.GetProperty("schema").GetString()!="WaW360.AztecWeapon69UnifiedOriginalPcXboxBodySelection.v1" ||
           w.GetProperty("status").GetString()!="AZTEC_69_WEAPON_NATIVE_ROOTS_63_AUTHENTIC_PC_XBOX_BODIES_2_CONFLICT_4_STOCK_UNPROVEN_NOT_IN_FF" ||
           w.GetProperty("uniqueSourceOwnedXboxWeaponBodyMappings").GetInt32()!=63 ||
           w.GetProperty("selectedUncompressedXboxOwnedPayloadBytes").GetInt64()!=179775 ||
           f.GetProperty("schema").GetString()!="WaW360.AztecWeapon63OriginalPcFlameParentSourceWitness.v1" ||
           f.GetProperty("status").GetString()!="AZTEC_63_WEAPON_FLAME_PARENT_ORIGINAL_PC_OWNERSHIP_PROVEN_NOT_IN_FF" ||
           f.GetProperty("originalPcWeaponBodiesRechecked").GetInt32()!=63 ||
           f.GetProperty("inlineOriginalPcFlameParentTables").GetInt32()!=4 ||
           f.GetProperty("originalPcFlameMaterialPointersPackedUnresolved").GetInt32()!=17 ||
           f.GetProperty("originalPcFlameMaterialPointersInlineOwned").GetInt32()!=3 ||
           f.GetProperty("originalPcFlameNamePointersPackedUnresolved").GetInt32()!=3 ||
           f.GetProperty("originalPcFlameNamePointersInlineOwned").GetInt32()!=1 ||
           f.GetProperty("originalPcOwnedNestedFlameSoundAliasRecords").GetInt32()!=6 ||
           !f.GetProperty("originalPcNameMaterialInlineSourceChildrenShaVerified").GetBoolean() ||
           !f.GetProperty("noNativeFastfileCreatedOrModified").GetBoolean() ||
           m.GetProperty("Schema").GetString()!="WaW360.AztecPcVerifiedSurfaceMaterialRelocationPlan.v1" ||
           m.GetProperty("Status").GetString()!="AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY" ||
           m.GetProperty("VerifiedPcMaterialMemoryEntries").GetInt32()!=39 ||
           m.GetProperty("VerifiedPcSurfaceReferences").GetInt32()!=735 ||
           m.GetProperty("UniqueGoldenXboxNativeMaterialRootNames").GetInt32()!=39 ||
           !m.GetProperty("AllPcSourceMaterialIdentitiesVerified").GetBoolean() ||
           m.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException(
                "63 genuine source WeaponDefs, 4 FlameTable parents or 735 independently OAT-loaded PC GfxSurface material source identities changed.");

        var zoneFile=Path.GetFullPath(m.GetProperty("OriginalPcFastFile").GetString()??"");
        var zone=await FastFileAnalysisCache.GetZoneAsync(zoneFile,ct);
        if(zone.Platform!=WaWPlatform.PC || zone.Document.Endianness!=Endian.Little ||
           !zone.SourceSha256.Equals(
               m.GetProperty("OriginalPcFastFileSha256").GetString(),
               StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Original PC 735-surface source FastFile SHA/platform/endianness changed.");

        var materialLookup=new Dictionary<uint,(string Name,int NativeRoot)>();
        var materialCounts=new Dictionary<uint,int>();
        foreach(var r in m.GetProperty("RelocationIntents").EnumerateArray())
        {
            if(!r.GetProperty("PcLoadedMaterialPointerIdentityProven").GetBoolean() ||
               !r.GetProperty("XboxMaterialRootNameIdentityUnique").GetBoolean() ||
               r.GetProperty("XenonMaterialPointerWritten").GetBoolean() ||
               r.GetProperty("XboxRuntimeMaterialGraphBindingProven").GetBoolean())
                throw new InvalidDataException("World 39 material source witness is not an unmodified genuine PC loaded identity.");
            var raw=ParsePointer(r.GetProperty("OriginalPcPackedMaterialPointer").GetString());
            if(raw==0 || IsInline(raw) || !T4ZonePointer.IsWithinBlock(raw,zone.ZoneHeader))
                throw new InvalidDataException("World material reference is not a valid exact source block pointer.");
            var name=r.GetProperty("VerifiedPcMaterialName").GetString()??"";
            var native=r.GetProperty("UniqueMatchingXboxNativeRootIndex").GetInt32();
            if(name.Length==0 || native<0)
                throw new InvalidDataException("Source world Material* lost typed name/root identity.");
            if(materialLookup.TryGetValue(raw,out var known))
            {
                if(known.Name!=name || known.NativeRoot!=native)
                    throw new InvalidDataException("The same actual original-PC packed Material* was assigned two different asset identities.");
                materialCounts[raw]++;
            }
            else
            {
                materialLookup.Add(raw,(name,native));
                materialCounts.Add(raw,1);
            }
        }
        if(materialLookup.Count!=39 || materialCounts.Values.Sum()!=735)
            throw new InvalidDataException("Source typed GfxWorld Material* fingerprint lost 39/735 exact identity.");

        var sources=w.GetProperty("nativeWeaponRootSourceDecisions").EnumerateArray()
            .Where(x=>IsSelected(x.GetProperty("conclusion").GetString()??""))
            .ToDictionary(x=>x.GetProperty("nativeRootName").GetString()??"",x=>x,StringComparer.Ordinal);
        if(sources.Count!=63 || f.GetProperty("weapons").GetArrayLength()!=63)
            throw new InvalidDataException("Original-PC 63-weapon source selection or FlameTable records changed.");

        var proofCache=new Dictionary<string,T4ProofContainerIndex>(StringComparer.OrdinalIgnoreCase);
        var records=new List<object>();
        int parents=0,provenSameZone=0,unprovenOwnerZone=0;
        int realWorldMaterialPointerMatches=0,unknownWorldMaterialPointerMatches=0;
        int packedMaterialCount=0,sourceInlineMaterialCount=0,sourceNullMaterialCount=0;
        int packedNames=0,inlineNames=0,packedSounds=0,inlineSounds=0,sourceNullSounds=0;
        int actualPcSoundRoots=zone.Document.Assets.Count(
            a=>a.TypeName.Equals("sound",StringComparison.OrdinalIgnoreCase));
        foreach(var row in f.GetProperty("weapons").EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var weapon=row.GetProperty("nativeWeaponRootName").GetString()??"";
            if(!sources.TryGetValue(weapon,out var src))throw new InvalidDataException("Unknown source WeaponDef: "+weapon);
            if(row.GetProperty("nativeRootOrdinal").GetInt32()!=src.GetProperty("nativeRootOrdinal").GetInt32() ||
               !string.Equals(row.GetProperty("originalPcXboxOwned7bBodySha256").GetString(),
                   src.GetProperty("xboxBlobSha256").GetString(),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original 7b WeaponDef source owner SHA/ordinal drift: "+weapon);
            var parentsHere=row.GetProperty("originalPcOwnedFlameParents").EnumerateArray()
                .Where(p=>p.GetProperty("ownership").GetString()=="SOURCE_OWNED_476_BYTE_INLINE_PARENT").ToArray();
            if(parentsHere.Length==0)continue;
            var proofFile=Path.GetFullPath(src.GetProperty("proofFile").GetString()??"");
            if(!proofCache.TryGetValue(proofFile,out var index))
            {
                index=await T4NativeProofContainerReader.IndexAsync(proofFile,"weapon",ct);
                proofCache.Add(proofFile,index);
            }
            if(!index.Sha256.Equals(src.GetProperty("proofContainerSha256").GetString(),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original-PC 7b container proof SHA changed: "+weapon);
            var ordinal=src.GetProperty("proofRecordOrdinal").GetInt32();
            if(ordinal<0 || ordinal>=index.Records.Count)throw new InvalidDataException("7b weapon proof ordinal out of bounds.");
            var descriptor=index.Records[ordinal];
            if(descriptor.ContainerBlobOffset!=src.GetProperty("xboxBlobByteOffset").GetInt64() ||
               descriptor.BlobLength!=src.GetProperty("xboxBlobBytes").GetInt32())
                throw new InvalidDataException("7b weapon owned proof byte extent changed.");
            var xboxBlob=await T4NativeProofContainerReader.ReadBlobAsync(descriptor,ct);
            if(!Sha(xboxBlob).Equals(src.GetProperty("xboxBlobSha256").GetString(),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original PC 7b WeaponDef body SHA changed: "+weapon);

            bool sameWeapon=descriptor.SourceOffset<=int.MaxValue &&
                SameOriginalPcWeaponFixed(zone.Bytes,checked((int)descriptor.SourceOffset),xboxBlob);
            if(src.GetProperty("originalPcSourceFile").ValueKind==JsonValueKind.String)
            {
                var explicitSource=Path.GetFullPath(src.GetProperty("originalPcSourceFile").GetString()??"");
                if(!explicitSource.Equals(zoneFile,StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(src.GetProperty("originalPcSourceSha256").GetString(),
                       zone.SourceSha256,StringComparison.OrdinalIgnoreCase))
                    sameWeapon=false;
            }
            foreach(var p in parentsHere)
            {
                parents++;
                var label=p.GetProperty("name").GetString()??"";
                var offset=p.GetProperty("source7bOwnedParentByteOffset").GetInt32();
                var parentHash=p.GetProperty("source7bOwnedParentSha256").GetString()??"";
                if(offset<WeaponFixedBytes || offset>xboxBlob.Length-FlameFixedBytes ||
                   !Sha(xboxBlob.AsSpan(offset,FlameFixedBytes)).Equals(parentHash,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("FlameTable genuine parent source hash/extent drift: "+weapon);
                var sameZone=sameWeapon &&
                    SameOriginalPcFlameParent(zone.Bytes,descriptor.SourceOffset+offset,parentHash);
                if(sameZone)provenSameZone++;else unprovenOwnerZone++;
                var refs=new List<object>();
                foreach(var child in p.GetProperty("originalPcNameAndMaterialTypedChildren").EnumerateArray())
                {
                    var kind=child.GetProperty("kind").GetString()??"";
                    var raw=ParsePointer(child.GetProperty("originalPcRawPointer").GetString());
                    var cls=child.GetProperty("sourceVerdict").GetString()??"";
                    bool packed=raw!=0&&!IsInline(raw), inline=IsInline(raw);
                    var expected=raw==0?"AUTHENTIC_SOURCE_NULL":
                        inline?"GENUINE_ORIGINAL_PC_7B_INLINE_OWNED_BYTES_SHA_VERIFIED":
                        "SOURCE_PACKED_REFERENCE_REQUIRES_REAL_TARGET_ASSET";
                    if(cls!=expected)throw new InvalidDataException("Original PC typed FlameTable pointer category drift: "+weapon);
                    if(kind=="FlameMaterial")
                    {
                        if(packed)packedMaterialCount++;
                        else if(inline)sourceInlineMaterialCount++;
                        else sourceNullMaterialCount++;
                    }
                    else if(kind=="FlameName")
                    {
                        if(packed)packedNames++;
                        else if(inline)inlineNames++;
                        else throw new InvalidDataException("FlameTable parent name unexpectedly original source NULL.");
                    }
                    else throw new InvalidDataException("Unexpected non-typed FlameTable child field.");
                    bool sameSourceMatch=kind=="FlameMaterial" && packed && sameZone &&
                        materialLookup.TryGetValue(raw,out var resolved);
                    if(kind=="FlameMaterial"&&packed)
                    {
                        if(sameSourceMatch)realWorldMaterialPointerMatches++;
                        else unknownWorldMaterialPointerMatches++;
                    }
                    var rawDecoded=T4ZonePointer.Decode(raw);
                    refs.Add(new{
                        label=child.GetProperty("label").GetString(),
                        kind,sourcePointer=$"0x{raw:X8}",rawPointerKind=rawDecoded.Kind.ToString(),
                        sourceBlock=rawDecoded.Block?.ToString(),
                        sourceBlockOffset=rawDecoded.Kind==T4ZonePointerKind.Offset?
                            $"0x{rawDecoded.Offset:X8}":null,
                        weaponAndParentByteProvenSameGfxWorldPcZone=sameZone,
                        matchingTypedWorldMaterialName=sameSourceMatch?resolved.Name:null,
                        matchingNativeRootNameIndex=sameSourceMatch?(int?)resolved.NativeRoot:null,
                        conclusion=raw==0?"GENUINE_SOURCE_NULL_PRESERVE":
                            inline?"AUTHENTIC_7B_INLINE_SOURCE_BYTES_ONLY":
                            sameSourceMatch?"EXACT_SAME_PC_ZONE_TYPED_MATERIAL_POINTER_IDENTITY_MATCH_NO_FULL_NATIVE_BODY_CLAIM":
                            "PACKED_REFERENCE_UNRESOLVED_REQUIRES_REAL_SOURCE_ASSET",
                        xboxMaterialOrXmaRuntimePayloadVerified=false
                    });
                }
                var soundRefs=new List<object>();
                foreach(var child in p.GetProperty("nestedSounds").EnumerateArray())
                {
                    var raw=ParsePointer(child.GetProperty("rawPointer").GetString());
                    if(raw==0)sourceNullSounds++;
                    else if(IsInline(raw))inlineSounds++;
                    else packedSounds++;
                    var decoded=T4ZonePointer.Decode(raw);
                    soundRefs.Add(new{
                        label=child.GetProperty("label").GetString(),raw=$"0x{raw:X8}",
                        kind=decoded.Kind.ToString(),block=decoded.Block?.ToString(),
                        sourceBlockOffset=decoded.Kind==T4ZonePointerKind.Offset?
                            $"0x{decoded.Offset:X8}":null,
                        originalPcZoneProvenSameSource=sameZone,
                        realSoundSampleOrXmaCodecResolved=false
                    });
                }
                records.Add(new{
                    weapon,sourceWeaponProofBodySha256=Sha(xboxBlob),
                    originalWeaponSourcePhysicalByteOffset=descriptor.SourceOffset,
                    parentLabel=label,parentSourcePhysicalByteOffset=offset,
                    parentSourceSha256=parentHash,
                    originalPcGfxWorldFastFileByteProvenSameSource=sameZone,
                    sourceNameAndMaterialReferences=refs,
                    sourceFlameSoundReferences=soundRefs
                });
            }
        }
        if(parents!=4 || provenSameZone+unprovenOwnerZone!=4 ||
           packedMaterialCount!=17 || sourceInlineMaterialCount!=3 ||
           sourceNullMaterialCount!=12 || packedNames!=3 || inlineNames!=1 ||
           packedSounds!=6 || inlineSounds!=6 || sourceNullSounds!=4 ||
           realWorldMaterialPointerMatches+unknownWorldMaterialPointerMatches!=17)
            throw new InvalidDataException(
                "Aztec genuine 4 FlameTable parent and 17 material / 3 packed name / 6 packed sound conservation failed.");

        var result=new {
            schema="WaW360.AztecFlameRealSourceMaterialJoin.v1",
            status="AZTEC_FLAME_PACKED_SOURCE_MATERIAL_MATCH_AUDIT_COMPLETE_NATIVE_ASSETS_UNRESOLVED",
            worldPcFastfile=zoneFile,originalPcFastfileSha256=zone.SourceSha256,
            worldOatLoadedTypedMaterialIdentities=39,worldMaterialSourceReferences=735,
            originalPcSoundAssetRootsInSameFastfile=actualPcSoundRoots,
            realOriginalPcFlameParentBodies=parents,
            originalPcFlameParentSameZoneFixedBodyAndParentShaVerified=provenSameZone,
            originalPcFlameParentSourceZoneUnproven=unprovenOwnerZone,
            nonNullPackedFlameMaterialPointers=packedMaterialCount,
            sameOriginalPcZoneExactOatTypedWorldMaterialPointerMatches=realWorldMaterialPointerMatches,
            packedFlameMaterialPointersStillUnresolved=unknownWorldMaterialPointerMatches,
            realOriginalPcInlineMaterialChildBodies=sourceInlineMaterialCount,
            naturallyNullMaterialPointersPreserved=sourceNullMaterialCount,
            originalPcInlineFlameNames=inlineNames,originalPcPackedFlameNamesStillUnresolved=packedNames,
            realOriginalPcInlineSoundAliasCustomRecords=inlineSounds,
            originalPcPackedFlameSoundAliasReferencesUnresolved=packedSounds,
            naturallyNullFlameSoundSlotsPreserved=sourceNullSounds,
            soundSampleXmaEncodeOrSampleBytesVerified=false,
            noPlaceholderMaterialOrSoundEmitted=true,
            noNativeFfOrXboxHardwareChanged=true,
            readyForNativeFlameEmission=false,
            parents=records
        };
        var dest=Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllTextAsync(dest,
            JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),
            new UTF8Encoding(false),ct);
        return dest;
    }
}
