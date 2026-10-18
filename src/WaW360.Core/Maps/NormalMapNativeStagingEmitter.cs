using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4NativeStagingFastFile(
    string Role,
    string Source,
    string ImageFile,
    string ImageSha256,
    long ImageBytes,
    int Assets,
    int Relocations,
    int PatchedPointersVerified,
    int ReadBackErrors,
    bool Verified);

public sealed class NormalMapNativeStagingEmitterResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required string LocalStagingRoot { get; init; }
    public required IReadOnlyList<T4NativeStagingFastFile> FastFiles { get; init; }
    public required int Assets { get; init; }
    public required int Relocations { get; init; }
    public required int PatchedPointersVerified { get; init; }
    public required int ReadBackErrors { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5d / alpha 10b: emits a compact native-relocation staging image, then opens it
/// as an unrelated reader and validates every directory entry, asset record, relocation,
/// and patched pointer. Large binary staging data lives in LocalAppData, not OneDrive.
///
/// The staging format is intentionally NOT named .ff and cannot become hardwareReady
/// until Stage 5c's nested target reservations are populated with real payload bytes and
/// the remaining XAsset classes are handled.
/// </summary>
public static class NormalMapNativeStagingEmitter
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("W360N5D\0");
    private const uint Version = 1;

    private sealed class LayoutDoc
    {
        public string Source { get; set; } = string.Empty;
        public string SourceSha256 { get; set; } = string.Empty;
        public Dictionary<string,uint> DestinationBlockSizes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<LayoutAsset> Assets { get; set; } = new();
        public List<T4NativeDestinationRelocation> Relocations { get; set; } = new();
    }
    private sealed class LayoutAsset
    {
        public int XAssetIndex { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public int XboxTypeId { get; set; }
        public T4AssetReferenceKind ReferenceKind { get; set; }
        public uint SourceRawHeader { get; set; }
        public string? ProofContainer { get; set; }
        public long? ProofBlobOffset { get; set; }
        public int BlobLength { get; set; }
        public T4ZoneBlock DestinationBlock { get; set; }
        public uint DestinationOffset { get; set; }
        public int PatchedPackedPointers { get; set; }
        public int InlineOrNullPointers { get; set; }
    }
    private sealed record PrefixPatch(int XAssetIndex, string TypeName, int PrefixLength, string PrefixBase64, int PatchedCount);

    public static async Task<NormalMapNativeStagingEmitterResult> RunAsync(MapCandidate map, string outputRoot, CancellationToken cancellationToken = default)
    {
        var cJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10b_destination_layout", $"{map.Name}_stage5_alpha10b_destination_layout.json");
        if (!File.Exists(cJson)) throw new InvalidDataException("Stage 5d requires a verified Stage 5c destination layout.");
        var cResult = JsonSerializer.Deserialize<NormalMapNativeDestinationLayoutResult>(await File.ReadAllTextAsync(cJson,cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5c result.");
        if (!cResult.Verified) throw new InvalidDataException("Stage 5c is not verified; staging emission refused.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10b_staging_emitter");
        Directory.CreateDirectory(workspace);
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WaW360Converter", "native_staging", Sanitize(map.Name));
        Directory.CreateDirectory(localRoot);
        var ffResults = new List<T4NativeStagingFastFile>();
        var detail = new List<string>();

        foreach (var ff in cResult.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layout = JsonSerializer.Deserialize<LayoutDoc>(await File.ReadAllTextAsync(ff.LayoutManifest,cancellationToken), JsonOptions())
                ?? throw new InvalidDataException($"Could not read Stage 5c layout '{ff.LayoutManifest}'.");
            var patches = JsonSerializer.Deserialize<List<PrefixPatch>>(await File.ReadAllTextAsync(ff.PatchManifest,cancellationToken), JsonOptions())
                ?? throw new InvalidDataException($"Could not read Stage 5c patch manifest '{ff.PatchManifest}'.");
            var patchByAsset = patches.ToDictionary(p=>p.XAssetIndex);

            var imagePath = Path.Combine(localRoot, Path.GetFileNameWithoutExtension(ff.Source) + "_" + Sanitize(ff.Role) + ".n5dimage");
            await EmitAsync(imagePath, layout, patchByAsset, cancellationToken);
            var audit = await ReadBackAsync(imagePath, cancellationToken);
            if (audit.Errors != 0)
                throw new InvalidDataException($"Stage 5d read-back found {audit.Errors:N0} error(s) in '{imagePath}'.");

            var sha = await Sha256Async(imagePath, cancellationToken);
            var size = new FileInfo(imagePath).Length;
            ffResults.Add(new T4NativeStagingFastFile(ff.Role, ff.Source, imagePath, sha, size, audit.Assets, audit.Relocations, audit.PatchedPointers, audit.Errors, true));
            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: image={size:N0}B; assets={audit.Assets:N0}; relocations={audit.Relocations:N0}; patchedVerified={audit.PatchedPointers:N0}; errors=0");
        }

        var txt = Path.Combine(workspace,$"{map.Name}_stage5_alpha10b_staging_emitter.txt");
        var json = Path.Combine(workspace,$"{map.Name}_stage5_alpha10b_staging_emitter.json");
        var result = new NormalMapNativeStagingEmitterResult
        {
            MapName=map.Name, Workspace=workspace, LocalStagingRoot=localRoot, FastFiles=ffResults,
            Assets=ffResults.Sum(x=>x.Assets), Relocations=ffResults.Sum(x=>x.Relocations),
            PatchedPointersVerified=ffResults.Sum(x=>x.PatchedPointersVerified), ReadBackErrors=ffResults.Sum(x=>x.ReadBackErrors),
            Verified=ffResults.All(x=>x.Verified), HardwareReady=false, TextReportFile=txt, JsonReportFile=json
        };
        await File.WriteAllTextAsync(txt, Render(result,detail),Encoding.UTF8,cancellationToken);
        await File.WriteAllTextAsync(json,JsonSerializer.Serialize(result,PrettyJson()),Encoding.UTF8,cancellationToken);
        return result;
    }

    private static async Task EmitAsync(string path, LayoutDoc layout, IReadOnlyDictionary<int,PrefixPatch> patches, CancellationToken ct)
    {
        await using var fs = new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);
        await fs.WriteAsync(Magic,ct);
        WriteU32(fs,Version);
        WriteU32(fs,7);
        WriteU32(fs,checked((uint)layout.Assets.Count));
        WriteU32(fs,checked((uint)layout.Relocations.Count));
        foreach(var block in Enum.GetValues<T4ZoneBlock>())
        {
            layout.DestinationBlockSizes.TryGetValue(block.ToString(),out var size);
            WriteU32(fs,(uint)block); WriteU32(fs,size);
        }
        foreach(var r in layout.Relocations.OrderBy(x=>x.DestinationBlock).ThenBy(x=>x.DestinationOffset))
        {
            WriteU32(fs,r.SourceRaw); WriteU32(fs,r.DestinationRaw); WriteU32(fs,checked((uint)r.ReferenceCount));
        }
        foreach(var a in layout.Assets.OrderBy(x=>x.XAssetIndex))
        {
            var typeBytes=Encoding.UTF8.GetBytes(a.TypeName);
            var patch=patches.TryGetValue(a.XAssetIndex,out var p)?p:null;
            var prefix=patch is null?Array.Empty<byte>():Convert.FromBase64String(patch.PrefixBase64);
            WriteU32(fs,checked((uint)a.XAssetIndex)); WriteU32(fs,checked((uint)a.XboxTypeId)); WriteU32(fs,(uint)a.ReferenceKind);
            WriteU32(fs,(uint)a.DestinationBlock); WriteU32(fs,a.DestinationOffset); WriteU32(fs,checked((uint)a.BlobLength));
            WriteU32(fs,checked((uint)a.PatchedPackedPointers)); WriteU32(fs,checked((uint)typeBytes.Length)); WriteU32(fs,checked((uint)prefix.Length));
            await fs.WriteAsync(typeBytes,ct); if(prefix.Length!=0) await fs.WriteAsync(prefix,ct);
        }
        await fs.FlushAsync(ct);
    }

    private sealed record Audit(int Assets,int Relocations,int PatchedPointers,int Errors);
    private static async Task<Audit> ReadBackAsync(string path,CancellationToken ct)
    {
        await using var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);
        var magic=new byte[8]; await ReadExactlyAsync(fs,magic,ct); if(!magic.SequenceEqual(Magic)) throw new InvalidDataException("Stage 5d image magic mismatch.");
        var version=await ReadU32Async(fs,ct); if(version!=Version) throw new InvalidDataException($"Stage 5d image version {version} is unsupported.");
        var blockCount=await ReadU32Async(fs,ct); if(blockCount!=7) throw new InvalidDataException("Stage 5d block directory must contain seven T4 blocks.");
        var assetCount=checked((int)await ReadU32Async(fs,ct)); var relocCount=checked((int)await ReadU32Async(fs,ct));
        var blockSizes=new Dictionary<T4ZoneBlock,uint>();
        for(var i=0;i<7;i++){var b=(T4ZoneBlock)await ReadU32Async(fs,ct);var s=await ReadU32Async(fs,ct);blockSizes[b]=s;}
        var reloc=new Dictionary<uint,uint>();
        var errors=0;
        for(var i=0;i<relocCount;i++)
        {
            var src=await ReadU32Async(fs,ct);var dst=await ReadU32Async(fs,ct);_ = await ReadU32Async(fs,ct);
            if(reloc.ContainsKey(src)) errors++; else reloc[src]=dst;
            var d=T4ZonePointer.Decode(dst);
            if(d.Kind!=T4ZonePointerKind.Offset||d.Block is null||!blockSizes.TryGetValue(d.Block.Value,out var size)||d.Offset>=size) errors++;
        }
        var destinationPointers=reloc.Values.ToHashSet();
        var patchedVerified=0;
        for(var i=0;i<assetCount;i++)
        {
            _=await ReadU32Async(fs,ct); _=await ReadU32Async(fs,ct); var referenceKind=(T4AssetReferenceKind)await ReadU32Async(fs,ct);
            var block=(T4ZoneBlock)await ReadU32Async(fs,ct); var offset=await ReadU32Async(fs,ct); _=await ReadU32Async(fs,ct);
            var expectedPatched=checked((int)await ReadU32Async(fs,ct)); var typeLen=checked((int)await ReadU32Async(fs,ct)); var prefixLen=checked((int)await ReadU32Async(fs,ct));
            if(!blockSizes.TryGetValue(block,out var blockSize)||offset>=blockSize) errors++;
            if(typeLen<0||typeLen>4096||prefixLen<0||prefixLen>4*1024*1024) throw new InvalidDataException("Stage 5d image contains implausible record lengths.");
            var typeBytes=new byte[typeLen];await ReadExactlyAsync(fs,typeBytes,ct);var type=Encoding.UTF8.GetString(typeBytes);
            var prefix=new byte[prefixLen];if(prefixLen!=0)await ReadExactlyAsync(fs,prefix,ct);
            var actual=0;
            if(referenceKind==T4AssetReferenceKind.OffsetOrAlias)
            {
                var aliasRaw=T4ZonePointer.Encode(block,offset);
                if(destinationPointers.Contains(aliasRaw)){actual=1;patchedVerified++;} else errors++;
            }
            else
            {
                var fields=NormalMapNativeDestinationLayout.PointerFieldsFor(type);
                foreach(var field in fields)
                {
                    if(field.Offset+4>prefix.Length) continue;
                    var raw=BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(field.Offset,4));
                    var d=T4ZonePointer.Decode(raw);
                    if(d.Kind!=T4ZonePointerKind.Offset||d.Block is null) continue;
                    if(!blockSizes.TryGetValue(d.Block.Value,out var s)||d.Offset>=s){errors++;continue;}
                    if(destinationPointers.Contains(raw)){actual++;patchedVerified++;}
                }
            }
            if(actual!=expectedPatched) errors++;
        }
        if(fs.Position!=fs.Length) errors++;
        return new Audit(assetCount,relocCount,patchedVerified,errors);
    }

    private static async Task<string> Sha256Async(string path,CancellationToken ct){await using var fs=File.OpenRead(path);using var sha=SHA256.Create();return Convert.ToHexString(await sha.ComputeHashAsync(fs,ct)).ToLowerInvariant();}
    private static async Task<uint> ReadU32Async(Stream s,CancellationToken ct){var b=new byte[4];await ReadExactlyAsync(s,b,ct);return BinaryPrimitives.ReadUInt32BigEndian(b);}
    private static void WriteU32(Stream s,uint v){Span<byte>b=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(b,v);s.Write(b);}
    private static async Task ReadExactlyAsync(Stream s,Memory<byte>b,CancellationToken ct){var n=0;while(n<b.Length){var r=await s.ReadAsync(b[n..],ct);if(r==0)throw new EndOfStreamException();n+=r;}}
    private static JsonSerializerOptions JsonOptions()=>new(){PropertyNameCaseInsensitive=true}; private static JsonSerializerOptions PrettyJson()=>new(){WriteIndented=true};
    private static string Sanitize(string value)=>string.Concat(value.Select(ch=>char.IsLetterOrDigit(ch)?ch:'_'));
    private static string Render(NormalMapNativeStagingEmitterResult r,IReadOnlyList<string> detail){var sb=new StringBuilder();sb.AppendLine("WaW360Converter — Stage 5d alpha 10b relocation staging image + independent read-back");sb.AppendLine(new string('=',84));sb.AppendLine($"Map: {r.MapName}");sb.AppendLine($"Assets: {r.Assets:N0}");sb.AppendLine($"Relocations: {r.Relocations:N0}");sb.AppendLine($"Patched pointers read-back verified: {r.PatchedPointersVerified:N0}");sb.AppendLine($"Read-back errors: {r.ReadBackErrors:N0}");sb.AppendLine($"Verified: {r.Verified}");sb.AppendLine($"Hardware ready: {r.HardwareReady}");sb.AppendLine($"Local staging root: {r.LocalStagingRoot}");sb.AppendLine();sb.AppendLine("IMPORTANT: .n5dimage is an internal relocation staging image, NOT a game .ff. It exists so the destination-address patcher and independent reader can be proved before nested payload materialization and full XAsset coverage unlock actual FF emission.");sb.AppendLine();foreach(var x in detail)sb.AppendLine(x);return sb.ToString();}
}
