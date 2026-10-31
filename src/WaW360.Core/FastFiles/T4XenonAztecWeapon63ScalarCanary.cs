using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Isolated native FF OAT canary: real PC-sourced Xbox WeaponDef NUMERIC
/// SCALARS + original source-owned opening inline XStrings only.
/// All other WeaponDef pointer/child fields deliberately NULL. This is
/// NOT the full 7b owned graph nor a hardware-playable weapon.
/// Fail-closed SHA/identity join on the original 63 authentic proof blobs.
/// </summary>
public sealed class T4XenonAztecWeapon63ScalarCanary
{
    public const string ManifestEnvironment = "WAW360_AZTEC_WEAPON63_DIAGNOSTIC";
    public const string LimitEnvironment = "WAW360_AZTEC_WEAPON63_LIMIT";
    public const string ExtraInlineStringsEnvironment = "WAW360_AZTEC_WEAPON63_EXTENDED_INLINE_STRINGS";
    public const string AccuracyVec2WitnessEnvironment = "WAW360_AZTEC_WEAPON63_ACCURACY_VEC2_SOURCE";
    public const string SoundAliasWitnessEnvironment = "WAW360_AZTEC_WEAPON63_SOUND_ALIAS_SOURCE";
    private const int FixedBytes = 0x9AC;

    // Cross-validated 32-bit scalar-only slots in the original-PC Weapon
    // scalar witness. Never copy a PC pointer, scriptString index or field
    // that OAT could interpret as an unrebased Xbox asset reference.
    private static readonly int[] ScalarOffsets =
    {
        320,324,328,332,336,340,344,348,352,368,372,376,
        688,988,996,1000,1316,1320,1556,1568,1668,1704,1996,
        1244,1248,1252,1268,1280,1484,1488,1492,1496,1500,1512,
        1516,1520,1524,1528,1532,1536,1540,1544,1560,1572,1576,
        1580,1608,1612,1676,1700,1708,1712,1716,1720,1724,
        2200,2204,2208,2212
    };
    private static readonly UTF8Encoding Utf8 = new(false, true);
    // Source-owned INLINE children only, in original 7b ZoneCode order;
    // NEVER a packed/external XModel/FX/Material/Audio pointer or donor.
    public sealed record OwnedChild(int SourceBodyByteOffset,int FixedPointerSlot,
        string ProofLabel,byte[] Payload,int VirtualAlignment,bool AccuracyVec2,
        bool SourceVerifiedRebasedPointers=false);
    public sealed record Entry(
        int RootIndex,string Name,byte[] Body,IReadOnlyList<string> Children,
        string SourceProofSha256,string SourceBlobSha256,int ProofRecordOrdinal,
        int OriginalOwnedBlobBytes,int ExtendedFixedSourceInlineStrings,
        IReadOnlyList<OwnedChild>? OrderedOwnedChildren=null,
        int OwnedAccuracyArrays=0,int OwnedAccuracyKnots=0,long OwnedAccuracyBytes=0,
        int OwnedBounceTables=0,int OwnedSoundAliasRecords=0,
        int OwnedSoundAliasNames=0,long OwnedSoundAliasNameBytes=0)
    {
        public long SerializedDiagnosticBytes => Body.LongLength +
            (OrderedOwnedChildren is not null
                ? OrderedOwnedChildren.Sum(x => (long)x.Payload.Length)
                : Children.Sum(s => (long)Utf8.GetByteCount(s) + 1L));

        public void Plan(Action<long,int,bool> virtualAlloc,Action<int> tempRoot)
        {
            tempRoot(FixedBytes);
            if(OrderedOwnedChildren is not null)
            {
                foreach(var child in OrderedOwnedChildren)
                    virtualAlloc(child.Payload.Length,child.VirtualAlignment,true);
            }
            else
            {
                foreach(var value in Children)
                    virtualAlloc(Utf8.GetByteCount(value)+1L,1,true);
            }
        }
        public void Emit(Stream zone,Action<long,int,bool> virtualAlloc,Action<int> tempRoot)
        {
            tempRoot(FixedBytes);
            zone.Write(Body);
            if(OrderedOwnedChildren is not null)
            {
                foreach(var child in OrderedOwnedChildren)
                {
                    virtualAlloc(child.Payload.Length,child.VirtualAlignment,true);
                    zone.Write(child.Payload);
                }
            }
            else
            {
                foreach(var value in Children)
                {
                    var bytes=Utf8.GetBytes(value);
                    virtualAlloc(bytes.Length+1L,1,true);
                    zone.Write(bytes);
                    zone.WriteByte(0);
                }
            }
        }
    }

    public IReadOnlyDictionary<int,Entry> ByRoot { get; }
    public int AllSourceOwnedNamedBodies => 63;
    public long TotalOriginalOwnedProofBytes { get; }
    public int TotalExtendedFixedSourceInlineStrings =>
        ByRoot.Values.Sum(e => e.ExtendedFixedSourceInlineStrings);
    public int TotalOwnedAccuracyVec2Arrays => ByRoot.Values.Sum(e=>e.OwnedAccuracyArrays);
    public int TotalOwnedAccuracyVec2Knots => ByRoot.Values.Sum(e=>e.OwnedAccuracyKnots);
    public long TotalOwnedAccuracyVec2PayloadBytes => ByRoot.Values.Sum(e=>e.OwnedAccuracyBytes);
    public int TotalOwnedBounceTables => ByRoot.Values.Sum(e=>e.OwnedBounceTables);
    public int TotalOwnedSoundAliasRecords => ByRoot.Values.Sum(e=>e.OwnedSoundAliasRecords);
    public int TotalOwnedSoundAliasNames => ByRoot.Values.Sum(e=>e.OwnedSoundAliasNames);
    public long TotalOwnedSoundAliasNameBytes => ByRoot.Values.Sum(e=>e.OwnedSoundAliasNameBytes);
    public long TotalDiagnosticEmittedBytes => ByRoot.Values.Sum(e=>e.SerializedDiagnosticBytes);

    private T4XenonAztecWeapon63ScalarCanary(
        IReadOnlyDictionary<int,Entry> byRoot,long original)
    {
        ByRoot=byRoot;
        TotalOriginalOwnedProofBytes=original;
    }

    public static async Task<T4XenonAztecWeapon63ScalarCanary?> LoadFromEnvironmentAsync(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        CancellationToken cancellationToken=default)
    {
        var file=Environment.GetEnvironmentVariable(ManifestEnvironment);
        if(string.IsNullOrWhiteSpace(file))return null;
        file=Path.GetFullPath(file);
        if(!File.Exists(file))
            throw new FileNotFoundException("Aztec 63-weapon native source SHA manifest absent",file);

        var rawLimit=Environment.GetEnvironmentVariable(LimitEnvironment);
        var extras=Environment.GetEnvironmentVariable(ExtraInlineStringsEnvironment);
        if(extras is not null && extras != "1")
            throw new InvalidDataException("WeaponDef extra inline XString diagnostic mode must be unset or '1'.");
        var expandedSourceStrings=extras=="1";
        var accuracySourceFile=Environment.GetEnvironmentVariable(AccuracyVec2WitnessEnvironment);
        var emitAccuracyVec2=!string.IsNullOrWhiteSpace(accuracySourceFile);
        var soundSourceFile=Environment.GetEnvironmentVariable(SoundAliasWitnessEnvironment);
        var emitSoundAliases=!string.IsNullOrWhiteSpace(soundSourceFile);
        if(emitSoundAliases && (!emitAccuracyVec2 || !expandedSourceStrings || rawLimit!="63"))
            throw new InvalidDataException(
                "Authentic PC WeaponDef source sound aliases require all 63 original PC weapon bodies, typed XStrings and Vec2 prerequisites.");
        if(emitAccuracyVec2 && (!expandedSourceStrings || rawLimit!="63"))
            throw new InvalidDataException(
                "Weapon63 accuracy Vec2 native FF canary requires all 63 source bodies and typed expanded inline XStrings.");
        if(!int.TryParse(rawLimit,out var limit) || limit is not (0 or 1 or 6 or 63))
            throw new InvalidDataException("Weapon63 scratch canary limit must be 0 (A/B control), 1, 6, or 63; missing is rejected.");

        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(file,cancellationToken));
        var report=document.RootElement;
        if(report.GetProperty("schema").GetString()!="WaW360.AztecWeapon69UnifiedOriginalPcXboxBodySelection.v1" ||
           report.GetProperty("status").GetString()!="AZTEC_69_WEAPON_NATIVE_ROOTS_63_AUTHENTIC_PC_XBOX_BODIES_2_CONFLICT_4_STOCK_UNPROVEN_NOT_IN_FF" ||
           report.GetProperty("nativeWeaponDefRoots").GetInt32()!=69 ||
           report.GetProperty("uniqueSourceOwnedXboxWeaponBodyMappings").GetInt32()!=63 ||
           report.GetProperty("conflictingDifferentPcSourceBodiesStillFenced").GetInt32()!=2 ||
           report.GetProperty("offlineStockOnlyWeaponNamesNotRuntimeProven").GetInt32()!=4 ||
           !report.GetProperty("allRealOriginalPcSourceOwnedXboxBodyBlobShasRevalidated").GetBoolean() ||
           !report.GetProperty("noNativeFastfileBuiltOrModified").GetBoolean() ||
           report.GetProperty("xboxRuntimeReady").GetBoolean())
            throw new InvalidDataException("Aztec 63 real original-PC Xbox weapon source proof changed or falsely claims native runtime readiness.");

        var nativeWeapons=roots.Where(r=>Normalize(r.TypeName)=="weapon").ToArray();
        if(roots.Count!=8509 || nativeWeapons.Length!=69)
            throw new InvalidDataException("Native Aztec 8509 root / 69 WeaponDef identity cardinality changed.");

        var manifestRows=report.GetProperty("nativeWeaponRootSourceDecisions").EnumerateArray().ToArray();
        if(manifestRows.Length!=69 ||
           manifestRows.Count(r=>IsSelected(r.GetProperty("conclusion").GetString()??""))!=63)
            throw new InvalidDataException("Aztec source manifest lost one of its 69 native root decisions.");
        if(ScalarOffsets.Length<50 || ScalarOffsets.Any(p=>p<0 || p+4>FixedBytes || p%4!=0) ||
           ScalarOffsets.Distinct().Count()!=ScalarOffsets.Length)
            throw new InvalidDataException("WeaponDef original PC scalar whitelist is invalid.");

        // The independent previous source-only Vec2 witness is a hard,
        // per-array SHA and physical-offset proof, not a guess based on
        // how much an FF should grow. Never promote packed aliases.
        using var accuracyDoc=emitAccuracyVec2
            ? JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.GetFullPath(accuracySourceFile!),cancellationToken))
            : null;
        Dictionary<string,JsonElement>? accuracyWitnesses=null;
        if(emitAccuracyVec2)
        {
            var a=accuracyDoc!.RootElement;
            if(a.GetProperty("schema").GetString()!="WaW360.Aztec63WeaponOriginalPcAccuracyVec2OwnedSourceWitness.v1" ||
               a.GetProperty("status").GetString()!="AZTEC_63_WEAPON_ORIGINAL_PC_INLINE_VEC2_CHILD_GRAPH_SOURCE_PROVEN_NOT_IN_FF" ||
               a.GetProperty("originalPcWeaponRootsWithGenuineXbox7bOwnedProof").GetInt32()!=63 ||
               a.GetProperty("provenInlineSourceOwnedVec2ArrayRecords").GetInt32()!=96 ||
               a.GetProperty("provenInlineSourceOwnedVec2Knots").GetInt32()!=612 ||
               a.GetProperty("provenInlineBigEndianXenonOwnedBytes").GetInt64()!=4896 ||
               a.GetProperty("weaponRootsWithRealInlineVec2Children").GetInt32()!=49 ||
               a.GetProperty("packedSourceArrayPointersQuarantined").GetInt32()!=96 ||
               a.GetProperty("nullSourceArrayPointers").GetInt32()!=60 ||
               !a.GetProperty("sourcePcWeaponBodySourceProofHashesAllRevalidated").GetBoolean() ||
               !a.GetProperty("noXboxFfGeneratedOrModified").GetBoolean() ||
               a.GetProperty("xboxRuntimeGameplayReady").GetBoolean())
                throw new InvalidDataException(
                    "Original PC accuracy vec2 array witness changed. This FF graft requires 96 authenticated inline arrays (612 knots, 4896 B) across 49 original source weapon roots.");
            var rr=a.GetProperty("weapons").EnumerateArray().ToArray();
            if(rr.Length!=63 || rr.Any(v=>v.GetProperty("fourAccuracySourceFields").GetArrayLength()!=4))
                throw new InvalidDataException("Source-owned PC accuracy report must include 63 four-slot weapon decisions.");
            accuracyWitnesses=rr.ToDictionary(
                v=>v.GetProperty("nativeWeaponRootName").GetString()??"",
                v=>v,StringComparer.Ordinal);
            if(accuracyWitnesses.Count!=63)
                throw new InvalidDataException("Original PC accuracy source proof native WeaponDef names duplicated.");
        }

        // A second independent source-only witness verifies both source blob SHA
        // and physical 7b owned-stream offsets of bounce tables and sound names.
        using var soundDoc=emitSoundAliases
            ? JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.GetFullPath(soundSourceFile!),cancellationToken))
            : null;
        Dictionary<string,JsonElement>? soundWitnesses=null;
        if(emitSoundAliases)
        {
            var q=soundDoc!.RootElement;
            if(q.GetProperty("schema").GetString()!="WaW360.Aztec63WeaponOriginalPcBounceSoundSourceWitness.v1" ||
               q.GetProperty("status").GetString()!="AZTEC_63_WEAPON_ORIGINAL_PC_BOUNCE_TABLE_INLINE_OWNERSHIP_PROVEN_NOT_IN_FF" ||
               q.GetProperty("originalPcWeaponRootsWithGenuineXbox7bOwnedProof").GetInt32()!=63 ||
               q.GetProperty("inlineOriginalPcBounceTables").GetInt32()!=2 ||
               q.GetProperty("originalPcOwnedInlineBouncePointerTableBytes").GetInt64()!=248 ||
               q.GetProperty("originalPcBounceChildInlineAliasRecords").GetInt32()!=62 ||
               q.GetProperty("originalPcBounceChildInlineNameStrings").GetInt32()!=9 ||
               q.GetProperty("originalPcBounceChildInlineNameBytes").GetInt64()!=199 ||
               q.GetProperty("originalPcInlineSoundAliasCustomRecordsAcrossAllOwnedWeaponFields").GetInt32()!=228 ||
               q.GetProperty("originalPcOwnedInlineSoundAliasNameBytesAcrossAllOwnedWeaponFields").GetInt64()!=895 ||
               !q.GetProperty("sourcePcWeaponBodySourceProofHashesAllRevalidated").GetBoolean() ||
               !q.GetProperty("source7bTypedWalkerProvidesPhysicalOffsets").GetBoolean() ||
               !q.GetProperty("sourceOwnedPointerBytesAreNotValidNativeFfRebases").GetBoolean() ||
               !q.GetProperty("noNativeFastFileGeneratedOrModified").GetBoolean() ||
               q.GetProperty("xboxRuntimeGameplayReady").GetBoolean())
                throw new InvalidDataException(
                    "Original PC sound alias 7b source witness changed; refusing unsupported Xbox pointer promotion.");
            var ss=q.GetProperty("weapons").EnumerateArray().ToArray();
            soundWitnesses=ss.ToDictionary(
                row=>row.GetProperty("nativeWeaponRootName").GetString()??"",
                row=>row,StringComparer.Ordinal);
            if(ss.Length!=63 || soundWitnesses.Count!=63)
                throw new InvalidDataException("Original PC sound witness requires 63 unique source roots.");
        }
        var selected=manifestRows.Where(r=>IsSelected(r.GetProperty("conclusion").GetString()??""))
            .OrderBy(r=>r.GetProperty("nativeRootOrdinal").GetInt32()).Take(limit).ToArray();
        var rootByOrdinal=nativeWeapons.ToDictionary(r=>r.Index);
        var indexCache=new Dictionary<string,T4ProofContainerIndex>(StringComparer.OrdinalIgnoreCase);
        var chosen=new Dictionary<int,Entry>();
        long allSourceBytes=0;
        foreach(var row in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal=row.GetProperty("nativeRootOrdinal").GetInt32();
            var name=row.GetProperty("nativeRootName").GetString()??"";
            if(!rootByOrdinal.TryGetValue(ordinal,out var root) ||
               !name.Equals(root.Name,StringComparison.Ordinal))
                throw new InvalidDataException($"Weapon canary root/name mismatch #{ordinal} {name}.");

            var proofFile=Path.GetFullPath(row.GetProperty("proofFile").GetString()??"");
            var containerSha=row.GetProperty("proofContainerSha256").GetString()??"";
            var blobSha=row.GetProperty("xboxBlobSha256").GetString()??"";
            if(!indexCache.TryGetValue(proofFile,out var index))
            {
                index=await T4NativeProofContainerReader.IndexAsync(
                    proofFile,"weapon",cancellationToken);
                indexCache.Add(proofFile,index);
            }
            if(!index.Sha256.Equals(containerSha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Weapon63 source proof container SHA drift {name}.");

            var proofOrdinal=row.GetProperty("proofRecordOrdinal").GetInt32();
            if(proofOrdinal<0 || proofOrdinal>=index.Records.Count)
                throw new InvalidDataException($"Weapon63 owned-proof ordinal drift {name}.");
            var desc=index.Records[proofOrdinal];
            if(desc.ContainerBlobOffset!=row.GetProperty("xboxBlobByteOffset").GetInt64() ||
               desc.BlobLength!=row.GetProperty("xboxBlobBytes").GetInt32())
                throw new InvalidDataException($"Weapon63 owned source blob offset/length drift: {name}");

            bool direct=row.GetProperty("conclusion").GetString()!.StartsWith("DIRECT_",StringComparison.Ordinal);
            if(direct)
            {
                var pcPath=Path.GetFullPath(row.GetProperty("originalPcSourceFile").GetString()??"");
                var expectedPcSha=row.GetProperty("originalPcSourceSha256").GetString()??"";
                var realPcSha=await FastFileAnalysisCache.GetSourceSha256Async(pcPath,cancellationToken);
                var raw=row.GetProperty("originalPcRawNamePointer").GetString()??"";
                var ptr=Convert.ToUInt32(raw,16);
                var bodyOffset=row.GetProperty("originalPcBodyOffset").GetInt64();
                var synthetic=$"<packed-name@0x{bodyOffset:X}:0x{ptr:X8}>";
                if(!realPcSha.Equals(expectedPcSha,StringComparison.OrdinalIgnoreCase) ||
                   desc.SourceOffset!=bodyOffset ||
                   !string.Equals(desc.Name,synthetic,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Direct packed PC name/body association changed: {name}");
            }
            else if(!string.Equals(desc.Name,name,StringComparison.Ordinal))
                throw new InvalidDataException($"Literal PC weapon identity does not match native root: {name}");

            var original=await T4NativeProofContainerReader.ReadBlobAsync(desc,cancellationToken);
            if(!Convert.ToHexString(SHA256.HashData(original)).Equals(blobSha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Weapon63 source Xbox owned-body SHA changed: {name}");

            // This original-PC owned 7b stream was already endian-converted
            // and SHA-verified, but its XModel/FX/etc asset pointers may still
            // be authentic packed SOURCE pointers (e.g. 0x80001A51).
            // Revalidate its typed OWNED stream, not its readiness for native
            // FF relocation. Only the separate sanitized diagnostic below
            // may pass the STRICT self-contained pointer gate.
            if(!T4WeaponVariableSerializerProof.TryValidateSourceOwnedXboxBlob(
                    original,desc.Name??name,out var sourceReason))
                throw new InvalidDataException(
                    $"Original PC-owned WeaponDef 7b source typed walk failed {name}: {sourceReason}");

            var childStrings=new List<string>{name};
            var orderedSourceChildren=emitAccuracyVec2
                ? new List<OwnedChild>{
                    new(-1,0,"szInternalName native root",Utf8.GetBytes(name+"\0"),1,false)
                }
                : null;
            var fixedOut=new byte[FixedBytes];
            foreach(var offset in ScalarOffsets)
                original.AsSpan(offset,4).CopyTo(fixedOut.AsSpan(offset,4));
            BinaryPrimitives.WriteUInt32BigEndian(fixedOut.AsSpan(0,4),T4ZonePointer.Following);

            int extendedFixedFields=0;
            if(expandedSourceStrings)
            {
                // SOURCE 7b ownership traversal proves the real byte offset
                // of each original inline string even if earlier inline
                // models/sounds consumed bytes in the original stream.
                // Only the typed WeaponDef FIXED BODY XString slots are
                // re-emitted, in ZoneCode traversal order. All packed PC
                // string pointers and foreign asset/child pointers remain 0.
                if(!T4WeaponVariableSerializerProof.TryCollectSourceOwnedFixedInlineXStrings(
                        original,desc.Name??name,out var observed,out var inlineReason))
                    throw new InvalidDataException(
                        $"Source PC owned WeaponDef fixed XString provenance fails {name}: {inlineReason}");
                foreach(var field in observed)
                {
                    if(field.FixedFieldOffset==0)
                    {
                        if(!string.Equals(field.SourceValue,name,StringComparison.Ordinal))
                            throw new InvalidDataException(
                                $"Source PC original WeaponDef internal-name proof differs from native name: {name} vs {field.SourceValue}");
                        continue;
                    }
                    // 7b source uses Latin1 bytes. Only ASCII-safe strings
                    // may be re-emitted as UTF8 without changing semantics;
                    // others remain null rather than being fabricated or
                    // lossy re-encoded.
                    if(field.SourceValue.Length>512 ||
                       field.SourceValue.Any(ch => ch<0x20 || ch>0x7e))
                        continue;
                    if(field.FixedFieldOffset<4 || field.FixedFieldOffset+4>FixedBytes)
                        throw new InvalidDataException(
                            $"Source fixed XString pointer offset out of bounds {name}: {field.ProofLabel}");
                    var raw=BinaryPrimitives.ReadUInt32BigEndian(
                        original.AsSpan(field.FixedFieldOffset,4));
                    if(raw!=T4ZonePointer.Following && raw!=T4ZonePointer.Insert)
                        throw new InvalidDataException(
                            $"Source fixed XString is not genuinely inline {name}: {field.ProofLabel}");
                    BinaryPrimitives.WriteUInt32BigEndian(
                        fixedOut.AsSpan(field.FixedFieldOffset,4),T4ZonePointer.Following);
                    childStrings.Add(field.SourceValue);
                    if(orderedSourceChildren is not null)
                        orderedSourceChildren.Add(new OwnedChild(
                            field.SourceBodyByteOffset,field.FixedFieldOffset,field.ProofLabel,
                            Utf8.GetBytes(field.SourceValue+"\0"),1,false));
                    extendedFixedFields++;
                }
            }
            else
            {
                // Original already-OAT-passed 63-body case unchanged: only
                // the opening original inline display/overlay strings.
                var cursor=FixedBytes;
                for(var pointerSlot=0;pointerSlot<=8;pointerSlot+=4)
                {
                    var p=BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(pointerSlot,4));
                    if(p!=T4ZonePointer.Following && p!=T4ZonePointer.Insert)
                        continue;
                    if(!TryReadSourceCString(original,ref cursor,out var value))
                        throw new InvalidDataException($"Weapon63 original inline XString unreadable at +{pointerSlot}: {name}");
                    if(pointerSlot==0)
                    {
                        if(!string.Equals(value,name,StringComparison.Ordinal))
                            throw new InvalidDataException($"Original inline WeaponDef source string/name conflict: {name} vs {value}");
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(
                            fixedOut.AsSpan(pointerSlot,4),T4ZonePointer.Following);
                        childStrings.Add(value);
                    }
                }
            }
            int realOwnedArrays=0,realOwnedKnots=0;
            long realOwnedArrayBytes=0;
            if(emitAccuracyVec2)
            {
                if(!accuracyWitnesses!.TryGetValue(name,out var approved) ||
                   approved.GetProperty("nativeRootOrdinal").GetInt32()!=ordinal ||
                   !string.Equals(approved.GetProperty("originalPcWeapon7bProofContainerSha256").GetString(),
                         containerSha,StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(approved.GetProperty("sourceOwnedXbox7bProofBodySha256").GetString(),
                         blobSha,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Original PC accuracy Vec2 source witness root/proof body SHA mismatch: {name}");
                if(!T4WeaponVariableSerializerProof.TryCollectSourceOwnedAccuracyVec2Arrays(
                        original,desc.Name??name,out var arrays,out var arrayReason))
                    throw new InvalidDataException(
                        $"Typed Alpha 7b PC accuracy stream child walk failed {name}: {arrayReason}");
                var bySlot=arrays.ToDictionary(x=>x.FixedFieldOffset);
                var sourceSlots=approved.GetProperty("fourAccuracySourceFields").EnumerateArray().ToArray();
                if(sourceSlots.Length!=4)
                    throw new InvalidDataException(
                        $"Original PC accuracy report lacks four audited owned-child slots: {name}");
                foreach(var slot in sourceSlots)
                {
                    var fieldOffset=slot.GetProperty("pointerFieldOffset").GetInt32();
                    if(fieldOffset is not (2184 or 2192 or 2188 or 2196))
                        throw new InvalidDataException(
                            $"Weapon {name} original-PC accuracy source slot not supported: {fieldOffset}");
                    var evidence=slot.GetProperty("verdict").GetString()??"";
                    var raw=BinaryPrimitives.ReadUInt32BigEndian(
                        original.AsSpan(fieldOffset,4));
                    bool sourceArray=bySlot.TryGetValue(fieldOffset,out var array);
                    if(sourceArray)
                    {
                        if(evidence!="ORIGINAL_PC_OWNED_TYPED_INLINE_VEC2_ARRAY_PROVEN" ||
                           raw!=T4ZonePointer.Following && raw!=T4ZonePointer.Insert ||
                           slot.GetProperty("source7bOwnedSerializedBodyByteOffset").GetInt32()!=array!.SourceBodyByteOffset ||
                           slot.GetProperty("knotCount").GetInt32()!=array!.KnotCount ||
                           slot.GetProperty("source7bOwnedXenonEndianPayloadBytes").GetInt32()!=array!.BigEndianKnotBytes.Length ||
                           !string.Equals(
                               slot.GetProperty("source7bOwnedPayloadSha256").GetString(),
                               Convert.ToHexString(SHA256.HashData(array!.BigEndianKnotBytes)),
                               StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(array.ProofLabel,
                               slot.GetProperty("proofLabel").GetString(),StringComparison.Ordinal))
                            throw new InvalidDataException(
                                $"Original-PC accuracy array byte SHA, count, offset or pointer witness changed: {name}/{fieldOffset}");
                        BinaryPrimitives.WriteUInt32BigEndian(
                            fixedOut.AsSpan(fieldOffset,4),T4ZonePointer.Following);
                        orderedSourceChildren!.Add(new OwnedChild(
                            array.SourceBodyByteOffset,fieldOffset,array.ProofLabel,
                            array.BigEndianKnotBytes.ToArray(),4,true));
                        realOwnedArrays++;
                        realOwnedKnots+=array.KnotCount;
                        realOwnedArrayBytes+=array.BigEndianKnotBytes.Length;
                    }
                    else if(evidence=="NULL_SOURCE_POINTER" && raw==0)
                    {
                        // Null stays null. Never infer a source child from a
                        // knot count alone; external arrays are not ours.
                    }
                    else if(evidence=="PACKED_OR_EXTERNAL_SOURCE_POINTER_FENCED" &&
                            raw!=0 && raw!=T4ZonePointer.Following && raw!=T4ZonePointer.Insert)
                    {
                        // Packed original-PC VIRTUAL array alias stays NULL.
                    }
                    else if(evidence=="ZERO_KNOT_INLINE_NO_OWNED_BYTES" &&
                            (raw==T4ZonePointer.Following || raw==T4ZonePointer.Insert) &&
                            slot.GetProperty("knotCount").GetInt32()==0)
                    {
                        // Source owned zero-byte array is represented as NULL.
                    }
                    else
                        throw new InvalidDataException(
                            $"Original-PC source accuracy pointer/owned-array classification drift: {name}/{fieldOffset}");
                }
                if(realOwnedArrays!=arrays.Count)
                    throw new InvalidDataException(
                        $"Source-only accuracy witness disagrees with actual inline knot arrays: {name}");
            }
            // Speedrun mode: all 63 WeaponDef sound-table fields are
            // source-classified. Exactly 2 genuine INLINE tables have data,
            // 61 original-PC NULL fields remain NULL. No fictitious 63 tables.
            int realBounceTables=0,realBounceAliasRecords=0,realBounceInlineNames=0;
            long realBounceInlineNameBytes=0;
            if(emitSoundAliases)
            {
                if(!soundWitnesses!.TryGetValue(name,out var soundProof) ||
                   soundProof.GetProperty("nativeRootOrdinal").GetInt32()!=ordinal ||
                   !string.Equals(soundProof.GetProperty("originalPcWeapon7bProofContainerSha256").GetString(),
                       containerSha,StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(soundProof.GetProperty("sourceOwnedXbox7bProofBodySha256").GetString(),
                       blobSha,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Weapon {name} original-PC bounce witness root/proof SHA changed.");
                if(!T4WeaponVariableSerializerProof.TryCollectSourceOwnedBounceSound(
                       original,desc.Name??name,out var tables,out var aliases,out var reason))
                    throw new InvalidDataException(
                        $"Weapon {name} original-PC typed bounce stream refused: {reason}");
                var originalPointer=BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(624,4));
                if(tables.Count>1 ||
                   (tables.Count==1)!=(originalPointer is T4ZonePointer.Following or T4ZonePointer.Insert))
                    throw new InvalidDataException(
                        $"Weapon {name} 7b bounce table pointer/owned source mismatch.");

                if(tables.Count==1)
                {
                    var table=tables[0];
                    if(soundProof.GetProperty("bounceTableSourceVerdict").GetString()!=
                           "INLINE_31_POINTER_TABLE_SOURCE_BYTES_PROVEN" ||
                       soundProof.GetProperty("originalPcInlineBounceTableSerializedByteOffset").GetInt32()!=
                           table.SourceBodyByteOffset ||
                       soundProof.GetProperty("originalPcOwnedInlineBouncePointerTableBytes").GetInt32()!=124 ||
                       table.BigEndianPointerBytes.Length!=124 ||
                       !string.Equals(soundProof.GetProperty("originalPcOwnedInlineBounceTableSha256").GetString(),
                           Convert.ToHexString(SHA256.HashData(table.BigEndianPointerBytes)),
                           StringComparison.OrdinalIgnoreCase) ||
                       !original.AsSpan(table.SourceBodyByteOffset,124)
                           .SequenceEqual(table.BigEndianPointerBytes))
                        throw new InvalidDataException(
                            $"Weapon {name} original-PC owned bounce table source bytes/SHA changed.");

                    var sourceRecords=aliases.Where(a=>
                        a.ProofLabel.StartsWith("bounceSound[",StringComparison.Ordinal))
                        .ToDictionary(a=>a.ProofLabel,a=>a,StringComparer.Ordinal);
                    var expectedEntries=soundProof.GetProperty("bounceTableEntries").EnumerateArray().ToArray();
                    var witnessAliases=soundProof.GetProperty("originalPcInlineSoundAliasRecords")
                        .EnumerateArray().ToDictionary(
                            a=>a.GetProperty("label").GetString()??"",a=>a,StringComparer.Ordinal);
                    if(expectedEntries.Length!=31 || sourceRecords.Count!=31)
                        throw new InvalidDataException(
                            $"Weapon {name} 31-entry bounce alias source record count changed.");
                    var safeTable=new byte[124];
                    for(var i=0;i<31;i++)
                    {
                        var label=$"bounceSound[{i}]";
                        var raw=BinaryPrimitives.ReadUInt32BigEndian(
                            table.BigEndianPointerBytes.AsSpan(i*4,4));
                        var inlineAlias=raw is T4ZonePointer.Following or T4ZonePointer.Insert;
                        if(expectedEntries[i].GetProperty("index").GetInt32()!=i ||
                           !string.Equals(expectedEntries[i].GetProperty("originalPcPointerRaw").GetString(),
                               $"0x{raw:X8}",StringComparison.OrdinalIgnoreCase) ||
                           sourceRecords.ContainsKey(label)!=inlineAlias)
                            throw new InvalidDataException(
                                $"Weapon {name} bounce alias #{i} pointer/record mismatch.");
                        // Preserve only genuinely inline alias records; original
                        // source packed pointers and NULL fields are zeroed.
                        BinaryPrimitives.WriteUInt32BigEndian(safeTable.AsSpan(i*4,4),
                            inlineAlias?T4ZonePointer.Following:0u);
                        if(!inlineAlias)continue;

                        if(!witnessAliases.TryGetValue(label,out var evidence))
                            throw new InvalidDataException(
                                $"Weapon {name} bounce alias #{i} missing independent source witness.");
                        var record=sourceRecords[label];
                        if(evidence.GetProperty("source7bSoundAliasCustomRecordOffset").GetInt32()!=
                               record.SourceRecordByteOffset ||
                           !string.Equals(evidence.GetProperty("bigEndianFourByteSoundNamePointer").GetString(),
                               $"0x{record.RawStringPointer:X8}",StringComparison.OrdinalIgnoreCase) ||
                           BinaryPrimitives.ReadUInt32BigEndian(
                               original.AsSpan(record.SourceRecordByteOffset,4))!=record.RawStringPointer)
                            throw new InvalidDataException(
                                $"Weapon {name} bounce alias #{i} typed source record mismatch.");
                        var nameInline=record.RawStringPointer is
                            T4ZonePointer.Following or T4ZonePointer.Insert;
                        var namePayload=record.InlineNameBytes;
                        if(nameInline!=(namePayload is not null) ||
                           nameInline!=(record.SourceInlineNameByteOffset is not null) ||
                           evidence.GetProperty("originalPcOwnedInlineNameBytes").GetInt32()!=
                               (namePayload?.Length??0))
                            throw new InvalidDataException(
                                $"Weapon {name} bounce alias #{i} owned name/pointer mismatch.");

                        var safeAlias=new byte[4];
                        BinaryPrimitives.WriteUInt32BigEndian(safeAlias,
                            nameInline?T4ZonePointer.Following:0u);
                        orderedSourceChildren!.Add(new OwnedChild(
                            record.SourceRecordByteOffset,624,label,
                            safeAlias,4,false,true));
                        realBounceAliasRecords++;
                        if(nameInline)
                        {
                            if(!string.Equals(evidence.GetProperty("originalPcOwnedInlineNameSha256").GetString(),
                                   Convert.ToHexString(SHA256.HashData(namePayload!)),
                                   StringComparison.OrdinalIgnoreCase) ||
                               evidence.GetProperty("originalPcOwnedInlineNameSerializedOffset").GetInt32()!=
                                   record.SourceInlineNameByteOffset!.Value ||
                               !original.AsSpan(record.SourceInlineNameByteOffset.Value,namePayload!.Length)
                                   .SequenceEqual(namePayload))
                                throw new InvalidDataException(
                                    $"Weapon {name} bounce alias #{i} name bytes/SHA changed.");
                            orderedSourceChildren.Add(new OwnedChild(
                                record.SourceInlineNameByteOffset.Value,624,label+".soundName",
                                namePayload.ToArray(),1,false));
                            realBounceInlineNames++;
                            realBounceInlineNameBytes+=namePayload.Length;
                        }
                    }
                    BinaryPrimitives.WriteUInt32BigEndian(
                        fixedOut.AsSpan(624,4),T4ZonePointer.Following);
                    orderedSourceChildren!.Add(new OwnedChild(
                        table.SourceBodyByteOffset,624,"bounceSound[31]",safeTable,4,false,true));
                    realBounceTables++;
                }
                else if(soundProof.GetProperty("bounceTableEntries").GetArrayLength()!=0 ||
                        soundProof.GetProperty("bounceTableSourceVerdict").GetString()!=
                            (originalPointer==0?"NULL":"PACKED_EXTERNAL_TABLE_FENCED"))
                    throw new InvalidDataException(
                        $"Weapon {name} original-PC null/packed bounce table classification changed.");
            }
            byte[] fullDiagnostic;
            if(orderedSourceChildren is not null)
            {
                // Source 7b file byte offsets exactly encode the original
                // ZoneCode visitation order. Omit unsupported source-owned
                // foreign children while preserving the relative order of
                // authentic inline XStrings and accuracy vec2 knot arrays.
                var sorted=orderedSourceChildren
                    .OrderBy(x=>x.SourceBodyByteOffset).ToArray();
                if(sorted.Length==0 || sorted[0].SourceBodyByteOffset!=-1 ||
                   sorted[0].FixedPointerSlot!=0 || sorted[0].AccuracyVec2)
                    throw new InvalidDataException(
                        $"Missing first native WeaponDef inline root name: {name}");
                var lastEnd=FixedBytes;
                foreach(var child in sorted.Skip(1))
                {
                    if(child.SourceBodyByteOffset<lastEnd ||
                       child.SourceBodyByteOffset<FixedBytes ||
                       (long)child.SourceBodyByteOffset+child.Payload.Length>original.Length ||
                       (!child.SourceVerifiedRebasedPointers &&
                       !original.AsSpan(child.SourceBodyByteOffset,child.Payload.Length)
                           .SequenceEqual(child.Payload)))
                        throw new InvalidDataException(
                            $"Weapon {name} typed original-PC variable child overlap, byte identity or ordering mismatch: {child.ProofLabel}");
                    lastEnd=checked(child.SourceBodyByteOffset+child.Payload.Length);
                }
                var total=checked(FixedBytes+sorted.Sum(x=>x.Payload.Length));
                fullDiagnostic=new byte[total];
                fixedOut.CopyTo(fullDiagnostic,0);
                var write=FixedBytes;
                foreach(var child in sorted)
                {
                    child.Payload.CopyTo(fullDiagnostic,write);
                    write+=child.Payload.Length;
                }
                orderedSourceChildren=sorted.ToList();
            }
            else
            {
                // Existing OAT-good base/extended string-only modes are
                // retained exactly, with unchanged serializer bytes.
                fullDiagnostic=new byte[checked(FixedBytes+
                    childStrings.Sum(s=>Utf8.GetByteCount(s)+1))];
                fixedOut.CopyTo(fullDiagnostic,0);
                var at=FixedBytes;
                foreach(var child in childStrings)
                {
                    var bytes=Utf8.GetBytes(child);
                    bytes.CopyTo(fullDiagnostic,at);
                    at+=bytes.Length;
                    fullDiagnostic[at++]=0;
                }
            }
            if(!T4WeaponVariableSerializerProof.TryValidateSelfContainedXboxBlob(
                    fullDiagnostic,name,out var diagReason))
                throw new InvalidDataException($"Native source-backed scalar/XString canary rejected by 7b walker: {name}: {diagReason}");

            chosen.Add(ordinal,new Entry(
                ordinal,name,fixedOut,childStrings,index.Sha256,blobSha,
                proofOrdinal,original.Length,extendedFixedFields,
                orderedSourceChildren,realOwnedArrays,realOwnedKnots,realOwnedArrayBytes,
                realBounceTables,realBounceAliasRecords,realBounceInlineNames,realBounceInlineNameBytes));
            allSourceBytes+=original.Length;
        }
        if(chosen.Count!=limit)
            throw new InvalidDataException($"Requested {limit} weapon canaries but built {chosen.Count}.");
        if(emitAccuracyVec2 &&
           (chosen.Values.Sum(x=>x.OwnedAccuracyArrays)!=96 ||
            chosen.Values.Sum(x=>x.OwnedAccuracyKnots)!=612 ||
            chosen.Values.Sum(x=>x.OwnedAccuracyBytes)!=4896 ||
            chosen.Values.Count(x=>x.OwnedAccuracyArrays>0)!=49))
            throw new InvalidDataException(
                "Authenticated original-PC 63-WeaponDef 7b accuracy arrays no longer total 96 owned arrays / 612 knots / 4896 bytes / 49 weapon roots.");

        if(emitSoundAliases &&
           (chosen.Values.Sum(x=>x.OwnedBounceTables)!=2 ||
            chosen.Values.Sum(x=>x.OwnedSoundAliasRecords)!=62 ||
            chosen.Values.Sum(x=>x.OwnedSoundAliasNames)!=9 ||
            chosen.Values.Sum(x=>x.OwnedSoundAliasNameBytes)!=199))
            throw new InvalidDataException(
                "Original-PC sound bounce graft no longer has exactly 2 real tables / 62 inline alias records / 9 original source names / 199 name bytes.");
        return new T4XenonAztecWeapon63ScalarCanary(chosen,allSourceBytes);
    }

    private static bool IsSelected(string decision)=>
        decision is "EXACT_ORIGINAL_PC_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED" or
                    "DIRECT_PC_LOADED_VIRTUAL_POINTER_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED";

    private static string Normalize(string name)=>
        name.Replace("_",string.Empty,StringComparison.Ordinal)
            .Replace("-",string.Empty,StringComparison.Ordinal).ToLowerInvariant();

    private static bool TryReadSourceCString(byte[] blob,ref int cursor,out string value)
    {
        value="";
        var start=cursor;
        if(start<FixedBytes || start>=blob.Length)return false;
        int end=start;
        while(end<blob.Length && blob[end]!=0 && end-start<=512)end++;
        if(end==blob.Length || end-start>512)return false;
        var bytes=blob.AsSpan(start,end-start);
        foreach(var ch in bytes)
            if(ch<0x20 || ch>0x7e)return false;
        value=Utf8.GetString(bytes);
        cursor=end+1;
        return true;
    }
}
