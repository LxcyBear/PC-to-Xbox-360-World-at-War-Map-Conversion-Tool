using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4FoundationPromotionFastFile(
    string Role,
    string Source,
    string ZoneImageFile,
    string ZoneImageSha256,
    long ZoneImageBytes,
    int AssetCount,
    int TrackedPhysicalPayloads,
    int FoundationPhysicalPayloads,
    long TrackedPayloadBytes,
    long FoundationPayloadBytes,
    int UnhandledAssetsSuppressed,
    IReadOnlyDictionary<string, int> PromotedByType,
    IReadOnlyDictionary<string, int> UnresolvedFoundationByType,
    int ScriptStringAliasesPending,
    int PackedSharedTargetsPending,
    int ReadBackErrors,
    bool Verified);

public sealed class NormalMapFoundationAssetPromotionResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required string LocalZoneRoot { get; init; }
    public required IReadOnlyList<T4FoundationPromotionFastFile> FastFiles { get; init; }
    public required int AssetCount { get; init; }
    public required int TrackedPhysicalPayloads { get; init; }
    public required int FoundationPhysicalPayloads { get; init; }
    public required long TrackedPayloadBytes { get; init; }
    public required long FoundationPayloadBytes { get; init; }
    public required int UnhandledAssetsSuppressed { get; init; }
    public required IReadOnlyDictionary<string, int> PromotedByType { get; init; }
    public required IReadOnlyDictionary<string, int> UnresolvedFoundationByType { get; init; }
    public required int ReadBackErrors { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5i / alpha 10e: promotes the already-proven Stage-4 foundation serializers
/// (RawFile, LocalizeEntry, StringTable) into the real native-zone reconstruction path.
/// The complete zone is rebuilt in XAsset order and independently read back.
///
/// This intentionally does NOT promote Material/TechniqueSet yet. T4's PC Material body
/// is 112 bytes while the empirically observed Xbox body is 96 bytes, and TechniqueSet
/// owns shader/pass graphs. Those get a dedicated typed conversion rather than an unsafe
/// byte copy just to unlock a .ff filename.
/// </summary>
public static class NormalMapFoundationAssetPromotion
{
    private static readonly HashSet<string> TrackedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "xanim", "xmodel", "weapon", "fx"
    };

    private sealed class LayoutDoc
    {
        public string Source { get; set; } = string.Empty;
        public string SourceSha256 { get; set; } = string.Empty;
        public Dictionary<string, uint> DestinationBlockSizes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
        public T4ZoneBlock DestinationBlock { get; set; }
        public uint DestinationOffset { get; set; }
    }

    private sealed record WriteStats(
        int TrackedPhysical,
        int FoundationPhysical,
        long TrackedBytes,
        long FoundationBytes,
        int Unhandled,
        int ScriptAliasesPending,
        Dictionary<string, uint> BlockSizes,
        Dictionary<int, byte[]> FoundationPayloads);

    private sealed record Audit(long TrackedBytes, long FoundationBytes, int Errors);

    public static async Task<NormalMapFoundationAssetPromotionResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var eJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10c_payload_materialization", $"{map.Name}_stage5_alpha10c_payload_materialization.json");
        var cJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10b_destination_layout", $"{map.Name}_stage5_alpha10b_destination_layout.json");
        if (!File.Exists(eJson) || !File.Exists(cJson))
            throw new InvalidDataException("Stage 5i requires verified Stage 5c + 5e results.");

        var eResult = JsonSerializer.Deserialize<NormalMapNativePayloadMaterializerResult>(
            await File.ReadAllTextAsync(eJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5e result.");
        var cResult = JsonSerializer.Deserialize<NormalMapNativeDestinationLayoutResult>(
            await File.ReadAllTextAsync(cJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5c result.");
        if (!eResult.Verified || !cResult.Verified)
            throw new InvalidDataException("Stage 5c/5e is not fully verified; foundation-asset promotion refused.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10e_foundation_promotion");
        Directory.CreateDirectory(workspace);
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WaW360Converter", "native_zones_10e", Sanitize(map.Name));
        Directory.CreateDirectory(localRoot);

        var payloadBySourceRole = eResult.FastFiles.ToDictionary(
            x => SourceRoleKey(x.Source, x.Role), StringComparer.OrdinalIgnoreCase);
        var rows = new List<T4FoundationPromotionFastFile>();
        var detail = new List<string>();
        var aggregatePromoted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var aggregateUnresolved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var ff in cResult.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!payloadBySourceRole.TryGetValue(SourceRoleKey(ff.Source, ff.Role), out var payloadResult))
                throw new InvalidDataException($"Stage 5i cannot find Stage-5e payload container for '{ff.Source}' role '{ff.Role}'.");

            var layout = JsonSerializer.Deserialize<LayoutDoc>(
                await File.ReadAllTextAsync(ff.LayoutManifest, cancellationToken), JsonOptions())
                ?? throw new InvalidDataException($"Could not read Stage 5c layout '{ff.LayoutManifest}'.");
            var source = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            if (!string.Equals(source.SourceSha256, layout.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Stage 5i source SHA-256 changed for '{ff.Source}'.");

            var payloadIndex = await NormalMapNativePayloadMaterializer.IndexPayloadFileAsync(payloadResult.PayloadFile, cancellationToken);
            var payloadByIndex = payloadIndex.ToDictionary(x => x.XAssetIndex);
            var layoutByIndex = layout.Assets.ToDictionary(x => x.XAssetIndex);
            var simple = T4SimpleNativeAssetSerializer.Build(source.Document, source.Bytes);

            var zonePath = Path.Combine(localRoot, Path.GetFileNameWithoutExtension(ff.Source) + "_" + Sanitize(ff.Role) + ".n5izone");
            var write = await EmitZoneAsync(zonePath, source.Document, layout, payloadResult.PayloadFile,
                payloadByIndex, layoutByIndex, simple, cancellationToken);
            var audit = await ReadBackAsync(zonePath, source.Document, layout, payloadResult.PayloadFile,
                payloadByIndex, layoutByIndex, simple, write.FoundationPayloads, cancellationToken);
            if (audit.Errors != 0)
                throw new InvalidDataException($"Stage 5i read-back found {audit.Errors:N0} error(s) in '{zonePath}'.");
            if (write.TrackedBytes != audit.TrackedBytes || write.FoundationBytes != audit.FoundationBytes)
                throw new InvalidDataException("Stage 5i byte accounting changed during independent read-back.");

            foreach (var kv in simple.PromotedByType)
                aggregatePromoted[kv.Key] = aggregatePromoted.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;
            foreach (var kv in simple.UnresolvedByType)
                aggregateUnresolved[kv.Key] = aggregateUnresolved.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;

            var sha = await Sha256Async(zonePath, cancellationToken);
            var fileBytes = new FileInfo(zonePath).Length;
            rows.Add(new T4FoundationPromotionFastFile(
                ff.Role, ff.Source, zonePath, sha, fileBytes, source.Document.AssetCount,
                write.TrackedPhysical, write.FoundationPhysical, write.TrackedBytes, write.FoundationBytes,
                write.Unhandled, simple.PromotedByType, simple.UnresolvedByType,
                write.ScriptAliasesPending, layout.Relocations.Count, audit.Errors, true));

            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: assets={source.Document.AssetCount:N0}; tracked={write.TrackedPhysical:N0}/{write.TrackedBytes:N0}B; foundation={write.FoundationPhysical:N0}/{write.FoundationBytes:N0}B; unhandledNull={write.Unhandled:N0}; errors=0");
            foreach (var kv in simple.PromotedByType.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                detail.Add($"  PROMOTED {kv.Key}: {kv.Value:N0}");
            foreach (var kv in simple.UnresolvedByType.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                detail.Add($"  BLOCKED foundation {kv.Key}: {kv.Value:N0}");
            foreach (var warning in simple.Warnings.Take(8))
                detail.Add("  NOTE: " + warning);
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10e_foundation_promotion.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10e_foundation_promotion.json");
        var result = new NormalMapFoundationAssetPromotionResult
        {
            MapName = map.Name,
            Workspace = workspace,
            LocalZoneRoot = localRoot,
            FastFiles = rows,
            AssetCount = rows.Sum(x => x.AssetCount),
            TrackedPhysicalPayloads = rows.Sum(x => x.TrackedPhysicalPayloads),
            FoundationPhysicalPayloads = rows.Sum(x => x.FoundationPhysicalPayloads),
            TrackedPayloadBytes = rows.Sum(x => x.TrackedPayloadBytes),
            FoundationPayloadBytes = rows.Sum(x => x.FoundationPayloadBytes),
            UnhandledAssetsSuppressed = rows.Sum(x => x.UnhandledAssetsSuppressed),
            PromotedByType = aggregatePromoted.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase),
            UnresolvedFoundationByType = aggregateUnresolved.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase),
            ReadBackErrors = rows.Sum(x => x.ReadBackErrors),
            Verified = rows.Count != 0 && rows.All(x => x.Verified),
            HardwareReady = false,
            TextReportFile = txt,
            JsonReportFile = json
        };
        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static async Task<WriteStats> EmitZoneAsync(
        string path,
        T4ZoneDocument document,
        LayoutDoc layout,
        string payloadFile,
        IReadOnlyDictionary<int, NormalMapNativePayloadMaterializer.PayloadDescriptor> payloadByIndex,
        IReadOnlyDictionary<int, LayoutAsset> layoutByIndex,
        T4SimpleNativeAssetScan simple,
        CancellationToken ct)
    {
        // Extend TEMP allocation accounting for newly promoted physical foundation assets.
        var blockSizes = layout.DestinationBlockSizes.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        blockSizes.TryGetValue(T4ZoneBlock.Temp.ToString(), out var tempCursor);
        foreach (var item in simple.AssetsByIndex.Values.OrderBy(x => x.AssetIndex))
        {
            tempCursor = Align(tempCursor, item.Alignment);
            tempCursor = checked(tempCursor + (uint)item.Payload.Length);
        }
        blockSizes[T4ZoneBlock.Temp.ToString()] = tempCursor;

        await using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        WriteU32(fs, 0); // zoneSize patched later
        WriteU32(fs, 0); // externalSize
        foreach (var block in Enum.GetValues<T4ZoneBlock>())
        {
            blockSizes.TryGetValue(block.ToString(), out var size);
            WriteU32(fs, size);
        }

        WriteU32(fs, checked((uint)document.ScriptStringCount));
        WriteU32(fs, document.ScriptStringCount == 0 ? 0u : T4ZonePointer.Following);
        WriteU32(fs, checked((uint)document.AssetCount));
        WriteU32(fs, document.AssetCount == 0 ? 0u : T4ZonePointer.Following);

        var inlineStrings = new List<byte[]>();
        var scriptAliasesPending = 0;
        foreach (var s in document.ScriptStrings)
        {
            if (s.Inline && s.Value is not null)
            {
                WriteU32(fs, T4ZonePointer.Following);
                inlineStrings.Add(Encoding.UTF8.GetBytes(s.Value + "\0"));
            }
            else if (s.RawPointer == 0)
            {
                WriteU32(fs, 0);
            }
            else
            {
                WriteU32(fs, 0);
                scriptAliasesPending++;
            }
        }
        foreach (var bytes in inlineStrings) await fs.WriteAsync(bytes, ct);

        var unhandled = 0;
        foreach (var asset in document.Assets.OrderBy(a => a.Index))
        {
            WriteU32(fs, checked((uint)T4XboxAssetTypeMap.GetXboxTypeId(asset.TypeName)));
            uint header;
            if (TrackedTypes.Contains(asset.TypeName))
            {
                if (asset.ReferenceKind is (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
                    header = asset.ReferenceKind == T4AssetReferenceKind.InlineShared ? T4ZonePointer.Following : T4ZonePointer.Insert;
                else if (asset.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias)
                {
                    if (!layoutByIndex.TryGetValue(asset.Index, out var planned))
                        throw new InvalidDataException($"Stage 5i has no rebased alias plan for tracked XAsset#{asset.Index}.");
                    header = T4ZonePointer.Encode(planned.DestinationBlock, planned.DestinationOffset);
                }
                else header = 0;
            }
            else if (simple.AssetsByIndex.TryGetValue(asset.Index, out var promoted))
            {
                if (asset.ReferenceKind is not (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
                    throw new InvalidDataException($"Stage 5i simple serializer produced non-physical XAsset#{asset.Index}.");
                header = asset.ReferenceKind == T4AssetReferenceKind.InlineShared ? T4ZonePointer.Following : T4ZonePointer.Insert;
            }
            else
            {
                header = 0;
                if (asset.ReferenceKind != T4AssetReferenceKind.Null) unhandled++;
            }
            WriteU32(fs, header);
        }

        var trackedPhysical = 0;
        var foundationPhysical = 0;
        long trackedBytes = 0;
        long foundationBytes = 0;
        var foundationPayloads = new Dictionary<int, byte[]>();
        foreach (var asset in document.Assets.OrderBy(a => a.Index))
        {
            if (TrackedTypes.Contains(asset.TypeName) && asset.ReferenceKind is (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
            {
                if (!payloadByIndex.TryGetValue(asset.Index, out var payload) || payload.BlobLength <= 0)
                    throw new InvalidDataException($"Stage 5i cannot find materialized tracked payload for XAsset#{asset.Index} {asset.TypeName}.");
                await NormalMapNativePayloadMaterializer.CopyBlobAsync(payloadFile, payload, fs, ct);
                trackedPhysical++;
                trackedBytes += payload.BlobLength;
            }
            else if (simple.AssetsByIndex.TryGetValue(asset.Index, out var promoted))
            {
                await fs.WriteAsync(promoted.Payload, ct);
                foundationPayloads[asset.Index] = promoted.Payload;
                foundationPhysical++;
                foundationBytes += promoted.Payload.Length;
            }
        }

        await fs.FlushAsync(ct);
        if (fs.Length > uint.MaxValue)
            throw new InvalidDataException("Stage 5i native zone exceeds the 32-bit T4 XFile zoneSize field.");
        fs.Seek(0, SeekOrigin.Begin);
        WriteU32(fs, checked((uint)fs.Length));
        await fs.FlushAsync(ct);
        return new WriteStats(trackedPhysical, foundationPhysical, trackedBytes, foundationBytes,
            unhandled, scriptAliasesPending, blockSizes.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase), foundationPayloads);
    }

    private static async Task<Audit> ReadBackAsync(
        string zonePath,
        T4ZoneDocument source,
        LayoutDoc layout,
        string payloadFile,
        IReadOnlyDictionary<int, NormalMapNativePayloadMaterializer.PayloadDescriptor> payloadByIndex,
        IReadOnlyDictionary<int, LayoutAsset> layoutByIndex,
        T4SimpleNativeAssetScan simple,
        IReadOnlyDictionary<int, byte[]> foundationPayloads,
        CancellationToken ct)
    {
        await using var fs = new FileStream(zonePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var errors = 0;
        var zoneSize = await ReadU32Async(fs, ct);
        var externalSize = await ReadU32Async(fs, ct);
        if (zoneSize != fs.Length || externalSize != 0) errors++;
        for (var i = 0; i < Enum.GetValues<T4ZoneBlock>().Length; i++) _ = await ReadU32Async(fs, ct);

        var stringCount = checked((int)await ReadU32Async(fs, ct));
        _ = await ReadU32Async(fs, ct);
        var assetCount = checked((int)await ReadU32Async(fs, ct));
        _ = await ReadU32Async(fs, ct);
        if (stringCount != source.ScriptStringCount || assetCount != source.AssetCount) errors++;

        var stringPointers = new uint[stringCount];
        for (var i = 0; i < stringCount; i++) stringPointers[i] = await ReadU32Async(fs, ct);
        for (var i = 0; i < stringCount; i++)
        {
            var src = source.ScriptStrings[i];
            var ptr = stringPointers[i];
            if (src.Inline && src.Value is not null)
            {
                if (ptr != T4ZonePointer.Following) errors++;
                var actual = await ReadCStringAsync(fs, ct);
                if (!string.Equals(actual, src.Value, StringComparison.Ordinal)) errors++;
            }
            else if (ptr != 0) errors++;
        }

        foreach (var src in source.Assets.OrderBy(a => a.Index))
        {
            var xboxType = checked((int)await ReadU32Async(fs, ct));
            var header = await ReadU32Async(fs, ct);
            if (xboxType != T4XboxAssetTypeMap.GetXboxTypeId(src.TypeName)) errors++;

            if (TrackedTypes.Contains(src.TypeName))
            {
                if (src.ReferenceKind is (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
                {
                    var expected = src.ReferenceKind == T4AssetReferenceKind.InlineShared ? T4ZonePointer.Following : T4ZonePointer.Insert;
                    if (header != expected) errors++;
                }
                else if (src.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias)
                {
                    if (!layoutByIndex.TryGetValue(src.Index, out var planned) || header != T4ZonePointer.Encode(planned.DestinationBlock, planned.DestinationOffset)) errors++;
                }
                else if (header != 0) errors++;
            }
            else if (simple.AssetsByIndex.ContainsKey(src.Index))
            {
                var expected = src.ReferenceKind == T4AssetReferenceKind.InlineShared ? T4ZonePointer.Following : T4ZonePointer.Insert;
                if (header != expected) errors++;
            }
            else if (header != 0) errors++;
        }

        long trackedBytes = 0;
        long foundationBytes = 0;
        await using var payloadStream = new FileStream(payloadFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        foreach (var asset in source.Assets.OrderBy(a => a.Index))
        {
            if (TrackedTypes.Contains(asset.TypeName) && asset.ReferenceKind is (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
            {
                if (!payloadByIndex.TryGetValue(asset.Index, out var payload)) { errors++; continue; }
                payloadStream.Seek(payload.BlobOffset, SeekOrigin.Begin);
                if (!await CompareRangesAsync(payloadStream, fs, payload.BlobLength, ct)) errors++;
                trackedBytes += payload.BlobLength;
            }
            else if (foundationPayloads.TryGetValue(asset.Index, out var expected))
            {
                var actual = new byte[expected.Length];
                await ReadExactlyAsync(fs, actual, ct);
                if (!actual.AsSpan().SequenceEqual(expected)) errors++;
                foundationBytes += expected.Length;
            }
        }
        if (fs.Position != fs.Length) errors++;
        return new Audit(trackedBytes, foundationBytes, errors);
    }

    private static uint Align(uint value, uint alignment)
    {
        if (alignment <= 1) return value;
        var mask = alignment - 1;
        return checked((value + mask) & ~mask);
    }

    private static async Task<bool> CompareRangesAsync(Stream a, Stream b, long length, CancellationToken ct)
    {
        var ab = new byte[1024 * 1024];
        var bb = new byte[ab.Length];
        var remaining = length;
        while (remaining > 0)
        {
            var take = (int)Math.Min(ab.Length, remaining);
            await ReadExactlyAsync(a, ab.AsMemory(0, take), ct);
            await ReadExactlyAsync(b, bb.AsMemory(0, take), ct);
            if (!ab.AsSpan(0, take).SequenceEqual(bb.AsSpan(0, take))) return false;
            remaining -= take;
        }
        return true;
    }

    private static async Task<string> ReadCStringAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(0, 1), ct);
            if (read == 0) throw new EndOfStreamException();
            if (one[0] == 0) break;
            if (ms.Length > 1024 * 1024) throw new InvalidDataException("Stage 5i script string exceeds 1 MiB.");
            ms.WriteByte(one[0]);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task ReadExactlyAsync(Stream s, Memory<byte> buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await s.ReadAsync(buffer[total..], ct);
            if (read == 0) throw new EndOfStreamException();
            total += read;
        }
    }

    private static async Task<uint> ReadU32Async(Stream s, CancellationToken ct)
    {
        var b = new byte[4];
        await ReadExactlyAsync(s, b.AsMemory(), ct);
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    private static void WriteU32(Stream s, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        s.Write(b);
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(fs, ct)).ToLowerInvariant();
    }

    private static string Render(NormalMapFoundationAssetPromotionResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5i alpha 10e foundation asset promotion");
        sb.AppendLine(new string('=', 82));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Tracked physical payloads: {r.TrackedPhysicalPayloads:N0} ({r.TrackedPayloadBytes:N0} bytes)");
        sb.AppendLine($"Promoted foundation payloads: {r.FoundationPhysicalPayloads:N0} ({r.FoundationPayloadBytes:N0} bytes)");
        sb.AppendLine($"Unhandled non-null XAssets remaining: {r.UnhandledAssetsSuppressed:N0}");
        sb.AppendLine($"Read-back errors: {r.ReadBackErrors:N0}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine($"Hardware ready: {r.HardwareReady}");
        sb.AppendLine();
        sb.AppendLine("PROMOTED FOUNDATION TYPES");
        foreach (var kv in r.PromotedByType) sb.AppendLine($"  {kv.Key,-20} {kv.Value,8:N0}");
        if (r.PromotedByType.Count == 0) sb.AppendLine("  (none)");
        sb.AppendLine();
        sb.AppendLine("UNRESOLVED FOUNDATION TYPES (fail-closed)");
        foreach (var kv in r.UnresolvedFoundationByType) sb.AppendLine($"  {kv.Key,-20} {kv.Value,8:N0}");
        if (r.UnresolvedFoundationByType.Count == 0) sb.AppendLine("  (none)");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: RawFile/Localize/StringTable now participate in the native zone instead of being nulled. Material/TechniqueSet are intentionally still blocked until their PC->Xbox layout/owned shader graph is converted by a typed writer.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
    private static string Sanitize(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
    private static string SourceRoleKey(string source, string role) => Path.GetFullPath(source) + "|" + role;
}
