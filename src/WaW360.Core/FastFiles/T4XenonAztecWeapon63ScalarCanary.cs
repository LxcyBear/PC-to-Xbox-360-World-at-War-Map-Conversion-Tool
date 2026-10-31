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
    public sealed record Entry(
        int RootIndex,string Name,byte[] Body,IReadOnlyList<string> Children,
        string SourceProofSha256,string SourceBlobSha256,int ProofRecordOrdinal,
        int OriginalOwnedBlobBytes)
    {
        public long SerializedDiagnosticBytes =>
            Body.LongLength + Children.Sum(s => (long)Utf8.GetByteCount(s) + 1L);

        public void Plan(Action<long,int,bool> virtualAlloc,Action<int> tempRoot)
        {
            tempRoot(FixedBytes);
            foreach(var value in Children)
                virtualAlloc(Utf8.GetByteCount(value)+1L,1,true);
        }
        public void Emit(Stream zone,Action<long,int,bool> virtualAlloc,Action<int> tempRoot)
        {
            tempRoot(FixedBytes);
            zone.Write(Body);
            foreach(var value in Children)
            {
                var bytes=Utf8.GetBytes(value);
                virtualAlloc(bytes.Length+1L,1,true);
                zone.Write(bytes);
                zone.WriteByte(0);
            }
        }
    }

    public IReadOnlyDictionary<int,Entry> ByRoot { get; }
    public int AllSourceOwnedNamedBodies => 63;
    public long TotalOriginalOwnedProofBytes { get; }
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
            var fixedOut=new byte[FixedBytes];
            foreach(var offset in ScalarOffsets)
                original.AsSpan(offset,4).CopyTo(fixedOut.AsSpan(offset,4));
            BinaryPrimitives.WriteUInt32BigEndian(fixedOut.AsSpan(0,4),T4ZonePointer.Following);

            // T4 ZoneCode begins with internalName, displayName, overlayName
            // in this exact order. Extract only ORIGINAL inline strings;
            // offset aliases cannot be promoted without genuine rebase.
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
                    BinaryPrimitives.WriteUInt32BigEndian(fixedOut.AsSpan(pointerSlot,4),T4ZonePointer.Following);
                    childStrings.Add(value);
                }
            }
            var fullDiagnostic=new byte[checked(FixedBytes+childStrings.Sum(s=>Utf8.GetByteCount(s)+1))];
            fixedOut.CopyTo(fullDiagnostic,0);
            var at=FixedBytes;
            foreach(var child in childStrings)
            {
                var bytes=Utf8.GetBytes(child);
                bytes.CopyTo(fullDiagnostic,at);
                at+=bytes.Length;
                fullDiagnostic[at++]=0;
            }
            if(!T4WeaponVariableSerializerProof.TryValidateSelfContainedXboxBlob(
                    fullDiagnostic,name,out var diagReason))
                throw new InvalidDataException($"Native source-backed scalar/XString canary rejected by 7b walker: {name}: {diagReason}");

            chosen.Add(ordinal,new Entry(
                ordinal,name,fixedOut,childStrings,index.Sha256,blobSha,
                proofOrdinal,original.Length));
            allSourceBytes+=original.Length;
        }
        if(chosen.Count!=limit)
            throw new InvalidDataException($"Requested {limit} weapon canaries but built {chosen.Count}.");

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
