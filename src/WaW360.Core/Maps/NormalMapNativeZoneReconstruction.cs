using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4NativeZoneReconstructionFastFile(
    string Role,
    string Source,
    string ZoneImageFile,
    string ZoneImageSha256,
    long ZoneImageBytes,
    int AssetCount,
    int ScriptStringCount,
    int TrackedPhysicalPayloads,
    long TrackedPayloadBytes,
    int UnhandledAssetsSuppressed,
    int ScriptStringAliasesPending,
    int PackedSharedTargetsPending,
    int ReadBackErrors,
    bool Verified);

public sealed class NormalMapNativeZoneReconstructionResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required string LocalZoneRoot { get; init; }
    public required IReadOnlyList<T4NativeZoneReconstructionFastFile> FastFiles { get; init; }
    public required int AssetCount { get; init; }
    public required int ScriptStringCount { get; init; }
    public required int TrackedPhysicalPayloads { get; init; }
    public required long TrackedPayloadBytes { get; init; }
    public required int UnhandledAssetsSuppressed { get; init; }
    public required int ScriptStringAliasesPending { get; init; }
    public required int PackedSharedTargetsPending { get; init; }
    public required bool NestedPointerRebasePending { get; init; }
    public required int ReadBackErrors { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5f / alpha 10c: rebuilds a real big-endian T4 XFile/XAssetList/script-string/
/// Xbox-XAsset table scaffold and appends the fully materialized tracked payload stream.
/// It then opens that emitted image independently and validates the header, table type
/// remapping, reference kinds, script strings, and the complete tracked payload tail.
///
/// Unsupported asset classes are deliberately emitted as NULL table entries. This makes
/// the file a truthful native-zone reconstruction scaffold, NOT a game-loadable .ff.
/// </summary>
public static class NormalMapNativeZoneReconstruction
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

    public static async Task<NormalMapNativeZoneReconstructionResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var eJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10c_payload_materialization", $"{map.Name}_stage5_alpha10c_payload_materialization.json");
        var cJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10b_destination_layout", $"{map.Name}_stage5_alpha10b_destination_layout.json");
        if (!File.Exists(eJson) || !File.Exists(cJson))
            throw new InvalidDataException("Stage 5f requires verified Stage 5c + 5e results.");

        var eResult = JsonSerializer.Deserialize<NormalMapNativePayloadMaterializerResult>(
            await File.ReadAllTextAsync(eJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5e result.");
        var cResult = JsonSerializer.Deserialize<NormalMapNativeDestinationLayoutResult>(
            await File.ReadAllTextAsync(cJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5c result.");
        if (!eResult.Verified || !cResult.Verified)
            throw new InvalidDataException("Stage 5c/5e is not fully verified; native zone reconstruction refused.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10c_zone_reconstruction");
        Directory.CreateDirectory(workspace);
        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WaW360Converter", "native_zones", Sanitize(map.Name));
        Directory.CreateDirectory(localRoot);

        var payloadBySourceRole = eResult.FastFiles.ToDictionary(
            x => SourceRoleKey(x.Source, x.Role), StringComparer.OrdinalIgnoreCase);
        var results = new List<T4NativeZoneReconstructionFastFile>();
        var detail = new List<string>();

        foreach (var ff in cResult.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!payloadBySourceRole.TryGetValue(SourceRoleKey(ff.Source, ff.Role), out var payloadResult))
                throw new InvalidDataException($"Stage 5f cannot find Stage-5e payload container for '{ff.Source}' role '{ff.Role}'.");

            var layout = JsonSerializer.Deserialize<LayoutDoc>(
                await File.ReadAllTextAsync(ff.LayoutManifest, cancellationToken), JsonOptions())
                ?? throw new InvalidDataException($"Could not read Stage 5c layout '{ff.LayoutManifest}'.");
            var source = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            if (!string.Equals(source.SourceSha256, layout.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Stage 5f source SHA-256 changed for '{ff.Source}'.");

            var payloadIndex = await NormalMapNativePayloadMaterializer.IndexPayloadFileAsync(payloadResult.PayloadFile, cancellationToken);
            var payloadByIndex = payloadIndex.ToDictionary(x => x.XAssetIndex);
            var layoutByIndex = layout.Assets.ToDictionary(x => x.XAssetIndex);

            var zonePath = Path.Combine(localRoot, Path.GetFileNameWithoutExtension(ff.Source) + "_" + Sanitize(ff.Role) + ".n5fzone");
            var write = await EmitZoneAsync(zonePath, source.Document, layout, payloadResult.PayloadFile, payloadByIndex, layoutByIndex, cancellationToken);
            var audit = await ReadBackAsync(zonePath, source.Document, layout, payloadResult.PayloadFile, payloadByIndex, layoutByIndex, cancellationToken);
            if (audit.Errors != 0)
                throw new InvalidDataException($"Stage 5f read-back found {audit.Errors:N0} error(s) in '{zonePath}'.");
            if (write.PayloadBytes != audit.PayloadBytes)
                throw new InvalidDataException("Stage 5f payload-byte count changed during independent read-back.");

            var sha = await Sha256Async(zonePath, cancellationToken);
            var fileBytes = new FileInfo(zonePath).Length;
            results.Add(new T4NativeZoneReconstructionFastFile(
                ff.Role,
                ff.Source,
                zonePath,
                sha,
                fileBytes,
                source.Document.AssetCount,
                source.Document.ScriptStringCount,
                write.PhysicalPayloads,
                write.PayloadBytes,
                write.UnhandledSuppressed,
                write.ScriptAliasesPending,
                layout.Relocations.Count,
                audit.Errors,
                true));

            detail.Add(
                $"[{ff.Role}] {Path.GetFileName(ff.Source)}: zone={fileBytes:N0}B; assets={source.Document.AssetCount:N0}; strings={source.Document.ScriptStringCount:N0}; " +
                $"trackedPayloads={write.PhysicalPayloads:N0}/{write.PayloadBytes:N0}B; unhandledNull={write.UnhandledSuppressed:N0}; " +
                $"scriptAliasPending={write.ScriptAliasesPending:N0}; packedSharedPending={layout.Relocations.Count:N0}; errors=0");
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10c_zone_reconstruction.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10c_zone_reconstruction.json");
        var result = new NormalMapNativeZoneReconstructionResult
        {
            MapName = map.Name,
            Workspace = workspace,
            LocalZoneRoot = localRoot,
            FastFiles = results,
            AssetCount = results.Sum(x => x.AssetCount),
            ScriptStringCount = results.Sum(x => x.ScriptStringCount),
            TrackedPhysicalPayloads = results.Sum(x => x.TrackedPhysicalPayloads),
            TrackedPayloadBytes = results.Sum(x => x.TrackedPayloadBytes),
            UnhandledAssetsSuppressed = results.Sum(x => x.UnhandledAssetsSuppressed),
            ScriptStringAliasesPending = results.Sum(x => x.ScriptStringAliasesPending),
            PackedSharedTargetsPending = results.Sum(x => x.PackedSharedTargetsPending),
            NestedPointerRebasePending = true,
            ReadBackErrors = results.Sum(x => x.ReadBackErrors),
            Verified = results.All(x => x.Verified),
            HardwareReady = false,
            TextReportFile = txt,
            JsonReportFile = json
        };
        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private sealed record WriteStats(int PhysicalPayloads, long PayloadBytes, int UnhandledSuppressed, int ScriptAliasesPending);

    private static async Task<WriteStats> EmitZoneAsync(
        string path,
        T4ZoneDocument document,
        LayoutDoc layout,
        string payloadFile,
        IReadOnlyDictionary<int, NormalMapNativePayloadMaterializer.PayloadDescriptor> payloadByIndex,
        IReadOnlyDictionary<int, LayoutAsset> layoutByIndex,
        CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // XFile header: zoneSize is patched once emission completes.
        WriteU32(fs, 0);
        WriteU32(fs, 0);
        foreach (var block in Enum.GetValues<T4ZoneBlock>())
        {
            layout.DestinationBlockSizes.TryGetValue(block.ToString(), out var size);
            WriteU32(fs, size);
        }

        // XAssetList (big-endian Xbox form).
        WriteU32(fs, checked((uint)document.ScriptStringCount));
        WriteU32(fs, document.ScriptStringCount == 0 ? 0u : 0xFFFFFFFFu);
        WriteU32(fs, checked((uint)document.AssetCount));
        WriteU32(fs, document.AssetCount == 0 ? 0u : 0xFFFFFFFFu);

        var inlineStrings = new List<byte[]>();
        var scriptAliasesPending = 0;
        foreach (var s in document.ScriptStrings)
        {
            if (s.Inline && s.Value is not null)
            {
                WriteU32(fs, 0xFFFFFFFFu);
                inlineStrings.Add(Encoding.UTF8.GetBytes(s.Value + "\0"));
            }
            else if (s.RawPointer == 0)
            {
                WriteU32(fs, 0);
            }
            else
            {
                // Do not preserve a stale PC packed pointer in a native reconstruction.
                // Keep the slot null and make the unresolved alias explicit in the report.
                WriteU32(fs, 0);
                scriptAliasesPending++;
            }
        }
        foreach (var bytes in inlineStrings) await fs.WriteAsync(bytes, ct);

        var unhandled = 0;
        foreach (var asset in document.Assets.OrderBy(a => a.Index))
        {
            if (!T4XboxAssetTypeMap.TryGetXboxTypeId(asset.TypeName, out var xboxType))
                throw new InvalidDataException($"Stage 5f has no Xbox XAsset type mapping for '{asset.TypeName}'.");
            WriteU32(fs, checked((uint)xboxType));

            uint header;
            if (!TrackedTypes.Contains(asset.TypeName))
            {
                header = 0;
                if (asset.ReferenceKind != T4AssetReferenceKind.Null) unhandled++;
            }
            else if (asset.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            {
                header = asset.ReferenceKind == T4AssetReferenceKind.InlineShared ? 0xFFFFFFFFu : 0xFFFFFFFEu;
            }
            else if (asset.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias)
            {
                if (!layoutByIndex.TryGetValue(asset.Index, out var planned))
                    throw new InvalidDataException($"Stage 5f has no rebased alias plan for tracked XAsset#{asset.Index}.");
                header = T4ZonePointer.Encode(planned.DestinationBlock, planned.DestinationOffset);
            }
            else
            {
                header = 0;
            }
            WriteU32(fs, header);
        }

        var physical = 0;
        long payloadBytes = 0;
        foreach (var asset in document.Assets.OrderBy(a => a.Index))
        {
            if (!TrackedTypes.Contains(asset.TypeName) ||
                asset.ReferenceKind is not (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
                continue;
            if (!payloadByIndex.TryGetValue(asset.Index, out var payload) || payload.BlobLength <= 0)
                throw new InvalidDataException($"Stage 5f cannot find materialized payload for XAsset#{asset.Index} {asset.TypeName}.");
            await NormalMapNativePayloadMaterializer.CopyBlobAsync(payloadFile, payload, fs, ct);
            physical++;
            payloadBytes += payload.BlobLength;
        }

        await fs.FlushAsync(ct);
        if (fs.Length > uint.MaxValue)
            throw new InvalidDataException("Stage 5f zone reconstruction exceeds the 32-bit T4 XFile zoneSize field.");
        fs.Seek(0, SeekOrigin.Begin);
        WriteU32(fs, checked((uint)fs.Length));
        await fs.FlushAsync(ct);
        return new WriteStats(physical, payloadBytes, unhandled, scriptAliasesPending);
    }

    private sealed record Audit(long PayloadBytes, int Errors);

    private static async Task<Audit> ReadBackAsync(
        string zonePath,
        T4ZoneDocument source,
        LayoutDoc layout,
        string payloadFile,
        IReadOnlyDictionary<int, NormalMapNativePayloadMaterializer.PayloadDescriptor> payloadByIndex,
        IReadOnlyDictionary<int, LayoutAsset> layoutByIndex,
        CancellationToken ct)
    {
        await using var fs = new FileStream(zonePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var errors = 0;
        var zoneSize = await ReadU32Async(fs, ct);
        var externalSize = await ReadU32Async(fs, ct);
        if (zoneSize != fs.Length || externalSize != 0) errors++;
        foreach (var block in Enum.GetValues<T4ZoneBlock>())
        {
            var actual = await ReadU32Async(fs, ct);
            layout.DestinationBlockSizes.TryGetValue(block.ToString(), out var expected);
            if (actual != expected) errors++;
        }

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
                if (ptr != 0xFFFFFFFFu) errors++;
                var actual = await ReadCStringAsync(fs, ct);
                if (!string.Equals(actual, src.Value, StringComparison.Ordinal)) errors++;
            }
            else if (ptr != 0)
            {
                errors++;
            }
        }

        for (var i = 0; i < assetCount; i++)
        {
            var xboxType = checked((int)await ReadU32Async(fs, ct));
            var header = await ReadU32Async(fs, ct);
            var src = source.Assets[i];
            var expectedType = T4XboxAssetTypeMap.GetXboxTypeId(src.TypeName);
            if (xboxType != expectedType) errors++;

            if (!TrackedTypes.Contains(src.TypeName))
            {
                if (header != 0) errors++;
            }
            else if (src.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            {
                var expectedHeader = src.ReferenceKind == T4AssetReferenceKind.InlineShared ? 0xFFFFFFFFu : 0xFFFFFFFEu;
                if (header != expectedHeader) errors++;
            }
            else if (src.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias)
            {
                if (!layoutByIndex.TryGetValue(src.Index, out var planned)) errors++;
                else if (header != T4ZonePointer.Encode(planned.DestinationBlock, planned.DestinationOffset)) errors++;
            }
            else if (header != 0)
            {
                errors++;
            }
        }

        long payloadBytes = 0;
        await using var payloadStream = new FileStream(payloadFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        foreach (var asset in source.Assets.OrderBy(a => a.Index))
        {
            if (!TrackedTypes.Contains(asset.TypeName) ||
                asset.ReferenceKind is not (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
                continue;
            if (!payloadByIndex.TryGetValue(asset.Index, out var payload))
            {
                errors++;
                continue;
            }
            payloadStream.Seek(payload.BlobOffset, SeekOrigin.Begin);
            if (!await CompareRangesAsync(payloadStream, fs, payload.BlobLength, ct)) errors++;
            payloadBytes += payload.BlobLength;
        }
        if (fs.Position != fs.Length) errors++;
        return new Audit(payloadBytes, errors);
    }

    private static async Task<bool> CompareRangesAsync(Stream expected, Stream actual, int length, CancellationToken ct)
    {
        var remaining = length;
        var a = new byte[Math.Min(1024 * 1024, Math.Max(1, length))];
        var b = new byte[a.Length];
        while (remaining > 0)
        {
            var take = Math.Min(a.Length, remaining);
            await ReadExactlyAsync(expected, a.AsMemory(0, take), ct);
            await ReadExactlyAsync(actual, b.AsMemory(0, take), ct);
            if (!a.AsSpan(0, take).SequenceEqual(b.AsSpan(0, take))) return false;
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
            if (ms.Length > 1024 * 1024) throw new InvalidDataException("Stage 5f script string exceeds 1 MiB.");
            ms.WriteByte(one[0]);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(fs, ct)).ToLowerInvariant();
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
        var total = 0;
        while (total < 4)
        {
            var read = await s.ReadAsync(b.AsMemory(total, 4 - total), ct);
            if (read == 0) throw new EndOfStreamException();
            total += read;
        }
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    private static void WriteU32(Stream s, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        s.Write(b);
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
    private static string Sanitize(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
    private static string SourceRoleKey(string source, string role) => Path.GetFullPath(source) + "|" + role;

    private static string Render(NormalMapNativeZoneReconstructionResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5f alpha 10c native T4 zone reconstruction scaffold");
        sb.AppendLine(new string('=', 84));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Xbox XAsset table entries: {r.AssetCount:N0}");
        sb.AppendLine($"Script strings: {r.ScriptStringCount:N0}");
        sb.AppendLine($"Tracked physical payloads: {r.TrackedPhysicalPayloads:N0}");
        sb.AppendLine($"Tracked payload bytes: {r.TrackedPayloadBytes:N0}");
        sb.AppendLine($"Unhandled non-null XAssets deliberately suppressed: {r.UnhandledAssetsSuppressed:N0}");
        sb.AppendLine($"Script-string aliases pending: {r.ScriptStringAliasesPending:N0}");
        sb.AppendLine($"Packed/shared targets pending ownership resolution: {r.PackedSharedTargetsPending:N0}");
        sb.AppendLine($"Nested child-pointer rebasing pending: {r.NestedPointerRebasePending}");
        sb.AppendLine($"Read-back errors: {r.ReadBackErrors:N0}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine($"Hardware ready: {r.HardwareReady}");
        sb.AppendLine($"Local native-zone root: {r.LocalZoneRoot}");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: .n5fzone now contains a real big-endian T4 XFile header, script-string table, Xbox-remapped XAsset table, and the complete tracked payload stream. Unsupported XAsset bodies are intentionally NULL in the table, packed/shared target ownership is still pending, and nested child-pointer rebasing is not yet complete. It is a reconstruction scaffold, NOT yet a game-loadable .ff.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }
}
