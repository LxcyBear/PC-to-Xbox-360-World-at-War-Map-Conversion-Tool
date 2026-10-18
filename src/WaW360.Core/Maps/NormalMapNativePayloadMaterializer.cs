using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4NativePayloadFastFile(
    string Role,
    string Source,
    string PayloadFile,
    string PayloadSha256,
    long ContainerBytes,
    long MaterializedPayloadBytes,
    int Assets,
    int PhysicalAssets,
    int AliasAssets,
    int PatchedPointersVerified,
    int PackedSharedTargetsPending,
    int ReadBackErrors,
    bool Verified);

public sealed class NormalMapNativePayloadMaterializerResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required string LocalPayloadRoot { get; init; }
    public required IReadOnlyList<T4NativePayloadFastFile> FastFiles { get; init; }
    public required int Assets { get; init; }
    public required int PhysicalAssets { get; init; }
    public required int AliasAssets { get; init; }
    public required long MaterializedPayloadBytes { get; init; }
    public required int PatchedPointersVerified { get; init; }
    public required int PackedSharedTargetsPending { get; init; }
    public required bool NestedPointerRebasePending { get; init; }
    public required int ReadBackErrors { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5e / alpha 10c: materializes the COMPLETE verified Stage-4 payload blob for
/// every tracked physical XAsset, applies the Stage-5c rebased pointer prefix to those
/// real bytes, and independently reads the resulting payload container back.
///
/// The proof blobs already contain each asset's inline-owned serialized payload stream.
/// Packed/shared targets remain separate relocation obligations and are deliberately
/// reported as pending instead of filled with synthetic bytes.
/// </summary>
public static class NormalMapNativePayloadMaterializer
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("W360N5E\0");
    private const uint Version = 1;

    private sealed class LayoutDoc
    {
        public string Source { get; set; } = string.Empty;
        public string SourceSha256 { get; set; } = string.Empty;
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

    private sealed class PrefixPatch
    {
        public int XAssetIndex { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public int PrefixLength { get; set; }
        public string PrefixBase64 { get; set; } = string.Empty;
        public int PatchedCount { get; set; }
    }

    internal sealed record PayloadDescriptor(
        int XAssetIndex,
        int XboxTypeId,
        string TypeName,
        T4AssetReferenceKind ReferenceKind,
        T4ZoneBlock DestinationBlock,
        uint DestinationOffset,
        int BlobLength,
        int PatchedCount,
        long BlobOffset);

    public static async Task<NormalMapNativePayloadMaterializerResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var cJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10b_destination_layout", $"{map.Name}_stage5_alpha10b_destination_layout.json");
        if (!File.Exists(cJson))
            throw new InvalidDataException("Stage 5e requires a verified Stage 5c destination layout.");

        var cResult = JsonSerializer.Deserialize<NormalMapNativeDestinationLayoutResult>(
            await File.ReadAllTextAsync(cJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5c result.");
        if (!cResult.Verified)
            throw new InvalidDataException("Stage 5c is not verified; payload materialization refused.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10c_payload_materialization");
        Directory.CreateDirectory(workspace);
        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WaW360Converter", "native_payloads", Sanitize(map.Name));
        Directory.CreateDirectory(localRoot);

        var ffResults = new List<T4NativePayloadFastFile>();
        var detail = new List<string>();

        foreach (var ff in cResult.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layout = JsonSerializer.Deserialize<LayoutDoc>(
                await File.ReadAllTextAsync(ff.LayoutManifest, cancellationToken), JsonOptions())
                ?? throw new InvalidDataException($"Could not read Stage 5c layout '{ff.LayoutManifest}'.");
            var patches = JsonSerializer.Deserialize<List<PrefixPatch>>(
                await File.ReadAllTextAsync(ff.PatchManifest, cancellationToken), JsonOptions())
                ?? throw new InvalidDataException($"Could not read Stage 5c patch manifest '{ff.PatchManifest}'.");

            var sourceCache = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            if (!string.Equals(sourceCache.SourceSha256, ff.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Source SHA-256 changed for '{ff.Source}'. Stage 5e refuses stale native state.");

            var patchByIndex = patches.ToDictionary(x => x.XAssetIndex);
            var payloadPath = Path.Combine(localRoot, Path.GetFileNameWithoutExtension(ff.Source) + "_" + Sanitize(ff.Role) + ".n5epayload");
            var writeStats = await EmitAsync(payloadPath, layout, patchByIndex, cancellationToken);
            var audit = await ReadBackAsync(payloadPath, layout, cancellationToken);
            if (audit.Errors != 0)
                throw new InvalidDataException($"Stage 5e read-back found {audit.Errors:N0} error(s) in '{payloadPath}'.");
            if (audit.MaterializedBytes != writeStats.MaterializedBytes)
                throw new InvalidDataException("Stage 5e materialized-byte count changed during read-back.");

            var sha = await Sha256Async(payloadPath, cancellationToken);
            var containerBytes = new FileInfo(payloadPath).Length;
            ffResults.Add(new T4NativePayloadFastFile(
                ff.Role,
                ff.Source,
                payloadPath,
                sha,
                containerBytes,
                audit.MaterializedBytes,
                audit.Assets,
                audit.PhysicalAssets,
                audit.AliasAssets,
                audit.PatchedPointers,
                layout.Relocations.Count,
                audit.Errors,
                true));

            detail.Add(
                $"[{ff.Role}] {Path.GetFileName(ff.Source)}: assets={audit.Assets:N0}; physical={audit.PhysicalAssets:N0}; aliases={audit.AliasAssets:N0}; " +
                $"payload={audit.MaterializedBytes:N0}B; patchedVerified={audit.PatchedPointers:N0}; packedSharedPending={layout.Relocations.Count:N0}; errors=0");
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10c_payload_materialization.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10c_payload_materialization.json");
        var result = new NormalMapNativePayloadMaterializerResult
        {
            MapName = map.Name,
            Workspace = workspace,
            LocalPayloadRoot = localRoot,
            FastFiles = ffResults,
            Assets = ffResults.Sum(x => x.Assets),
            PhysicalAssets = ffResults.Sum(x => x.PhysicalAssets),
            AliasAssets = ffResults.Sum(x => x.AliasAssets),
            MaterializedPayloadBytes = ffResults.Sum(x => x.MaterializedPayloadBytes),
            PatchedPointersVerified = ffResults.Sum(x => x.PatchedPointersVerified),
            PackedSharedTargetsPending = ffResults.Sum(x => x.PackedSharedTargetsPending),
            NestedPointerRebasePending = true,
            ReadBackErrors = ffResults.Sum(x => x.ReadBackErrors),
            Verified = ffResults.All(x => x.Verified),
            HardwareReady = false,
            TextReportFile = txt,
            JsonReportFile = json
        };
        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private sealed record WriteStats(long MaterializedBytes);

    private static async Task<WriteStats> EmitAsync(
        string path,
        LayoutDoc layout,
        IReadOnlyDictionary<int, PrefixPatch> patches,
        CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await fs.WriteAsync(Magic, ct);
        WriteU32(fs, Version);
        WriteU32(fs, checked((uint)layout.Assets.Count));
        WriteU32(fs, checked((uint)layout.Assets.Count(a => a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)));
        WriteU32(fs, checked((uint)layout.Relocations.Count));

        long materialized = 0;
        foreach (var asset in layout.Assets.OrderBy(a => a.XAssetIndex))
        {
            ct.ThrowIfCancellationRequested();
            var typeBytes = Encoding.UTF8.GetBytes(asset.TypeName);
            byte[] blob = Array.Empty<byte>();
            var patchedCount = asset.PatchedPackedPointers;

            if (asset.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            {
                if (string.IsNullOrWhiteSpace(asset.ProofContainer) || asset.ProofBlobOffset is null || asset.BlobLength <= 0)
                    throw new InvalidDataException($"Stage 5e physical XAsset#{asset.XAssetIndex} {asset.TypeName} lacks a proof blob.");
                blob = await ReadBlobAsync(asset.ProofContainer, asset.ProofBlobOffset.Value, asset.BlobLength, ct);
                if (!patches.TryGetValue(asset.XAssetIndex, out var patch))
                    throw new InvalidDataException($"Stage 5e has no Stage-5c pointer patch for XAsset#{asset.XAssetIndex} {asset.TypeName}.");
                var prefix = Convert.FromBase64String(patch.PrefixBase64);
                if (prefix.Length != patch.PrefixLength || prefix.Length > blob.Length)
                    throw new InvalidDataException($"Stage 5e pointer patch length mismatch for XAsset#{asset.XAssetIndex}.");
                prefix.CopyTo(blob, 0);
                if (patch.PatchedCount != asset.PatchedPackedPointers)
                    throw new InvalidDataException($"Stage 5e patched-pointer count mismatch for XAsset#{asset.XAssetIndex}.");
                materialized += blob.Length;
            }
            else if (asset.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias)
            {
                patchedCount = 1;
            }

            WriteU32(fs, checked((uint)asset.XAssetIndex));
            WriteU32(fs, checked((uint)asset.XboxTypeId));
            WriteU32(fs, (uint)asset.ReferenceKind);
            WriteU32(fs, (uint)asset.DestinationBlock);
            WriteU32(fs, asset.DestinationOffset);
            WriteU32(fs, checked((uint)blob.Length));
            WriteU32(fs, checked((uint)patchedCount));
            WriteU32(fs, checked((uint)typeBytes.Length));
            await fs.WriteAsync(typeBytes, ct);
            if (blob.Length != 0) await fs.WriteAsync(blob, ct);
        }
        await fs.FlushAsync(ct);
        return new WriteStats(materialized);
    }

    private sealed record Audit(int Assets, int PhysicalAssets, int AliasAssets, long MaterializedBytes, int PatchedPointers, int Errors);

    private static async Task<Audit> ReadBackAsync(string path, LayoutDoc layout, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var magic = new byte[8];
        await ReadExactlyAsync(fs, magic, ct);
        if (!magic.SequenceEqual(Magic)) throw new InvalidDataException("Stage 5e payload magic mismatch.");
        var version = await ReadU32Async(fs, ct);
        if (version != Version) throw new InvalidDataException($"Stage 5e payload version {version} is unsupported.");
        var assetCount = checked((int)await ReadU32Async(fs, ct));
        var physicalExpected = checked((int)await ReadU32Async(fs, ct));
        var relocationExpected = checked((int)await ReadU32Async(fs, ct));
        if (assetCount != layout.Assets.Count || relocationExpected != layout.Relocations.Count)
            throw new InvalidDataException("Stage 5e payload header count mismatch.");

        var expectedByIndex = layout.Assets.ToDictionary(x => x.XAssetIndex);
        var destinationPointers = layout.Relocations.Select(x => x.DestinationRaw).ToHashSet();
        var physical = 0;
        var aliases = 0;
        long bytes = 0;
        var patched = 0;
        var errors = 0;

        for (var i = 0; i < assetCount; i++)
        {
            var index = checked((int)await ReadU32Async(fs, ct));
            var xboxType = checked((int)await ReadU32Async(fs, ct));
            var kind = (T4AssetReferenceKind)await ReadU32Async(fs, ct);
            var block = (T4ZoneBlock)await ReadU32Async(fs, ct);
            var offset = await ReadU32Async(fs, ct);
            var blobLength = checked((int)await ReadU32Async(fs, ct));
            var expectedPatched = checked((int)await ReadU32Async(fs, ct));
            var typeLength = checked((int)await ReadU32Async(fs, ct));
            if (typeLength < 0 || typeLength > 4096 || blobLength < 0 || blobLength > 1024 * 1024 * 1024)
                throw new InvalidDataException("Stage 5e payload record contains implausible lengths.");
            var typeBytes = new byte[typeLength];
            await ReadExactlyAsync(fs, typeBytes, ct);
            var type = Encoding.UTF8.GetString(typeBytes);

            if (!expectedByIndex.TryGetValue(index, out var expected) ||
                expected.XboxTypeId != xboxType || expected.ReferenceKind != kind ||
                expected.DestinationBlock != block || expected.DestinationOffset != offset ||
                !expected.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase))
            {
                errors++;
            }

            var blob = new byte[blobLength];
            if (blobLength != 0) await ReadExactlyAsync(fs, blob, ct);
            if (kind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            {
                physical++;
                bytes += blobLength;
                var actualPatched = 0;
                foreach (var field in NormalMapNativeDestinationLayout.PointerFieldsFor(type))
                {
                    if (field.Offset + 4 > blob.Length) continue;
                    var raw = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(field.Offset, 4));
                    if (destinationPointers.Contains(raw)) actualPatched++;
                }
                if (actualPatched != expectedPatched) errors++;
                patched += actualPatched;
            }
            else if (kind == T4AssetReferenceKind.OffsetOrAlias)
            {
                aliases++;
                var raw = T4ZonePointer.Encode(block, offset);
                if (!destinationPointers.Contains(raw) || expectedPatched != 1) errors++;
                patched++;
            }
            else if (blobLength != 0)
            {
                errors++;
            }
        }
        if (physical != physicalExpected) errors++;
        if (fs.Position != fs.Length) errors++;
        return new Audit(assetCount, physical, aliases, bytes, patched, errors);
    }

    internal static async Task<IReadOnlyList<PayloadDescriptor>> IndexPayloadFileAsync(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var magic = new byte[8];
        await ReadExactlyAsync(fs, magic, ct);
        if (!magic.SequenceEqual(Magic)) throw new InvalidDataException("Stage 5e payload magic mismatch.");
        if (await ReadU32Async(fs, ct) != Version) throw new InvalidDataException("Unsupported Stage 5e payload version.");
        var count = checked((int)await ReadU32Async(fs, ct));
        _ = await ReadU32Async(fs, ct);
        _ = await ReadU32Async(fs, ct);
        var list = new List<PayloadDescriptor>(count);
        for (var i = 0; i < count; i++)
        {
            var index = checked((int)await ReadU32Async(fs, ct));
            var xboxType = checked((int)await ReadU32Async(fs, ct));
            var kind = (T4AssetReferenceKind)await ReadU32Async(fs, ct);
            var block = (T4ZoneBlock)await ReadU32Async(fs, ct);
            var offset = await ReadU32Async(fs, ct);
            var blobLength = checked((int)await ReadU32Async(fs, ct));
            var patched = checked((int)await ReadU32Async(fs, ct));
            var typeLength = checked((int)await ReadU32Async(fs, ct));
            if (typeLength < 0 || typeLength > 4096 || blobLength < 0 || blobLength > 1024 * 1024 * 1024)
                throw new InvalidDataException("Stage 5e payload index contains implausible lengths.");
            var typeBytes = new byte[typeLength];
            await ReadExactlyAsync(fs, typeBytes, ct);
            var type = Encoding.UTF8.GetString(typeBytes);
            var blobOffset = fs.Position;
            list.Add(new PayloadDescriptor(index, xboxType, type, kind, block, offset, blobLength, patched, blobOffset));
            fs.Seek(blobLength, SeekOrigin.Current);
        }
        if (fs.Position != fs.Length) throw new InvalidDataException("Stage 5e payload container has trailing bytes.");
        return list;
    }

    internal static async Task CopyBlobAsync(string payloadFile, PayloadDescriptor descriptor, Stream destination, CancellationToken ct)
    {
        if (descriptor.BlobLength == 0) return;
        await using var fs = new FileStream(payloadFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        fs.Seek(descriptor.BlobOffset, SeekOrigin.Begin);
        var remaining = descriptor.BlobLength;
        var buffer = new byte[Math.Min(1024 * 1024, remaining)];
        while (remaining > 0)
        {
            var take = Math.Min(buffer.Length, remaining);
            var read = await fs.ReadAsync(buffer.AsMemory(0, take), ct);
            if (read == 0) throw new EndOfStreamException();
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }
    }

    private static async Task<byte[]> ReadBlobAsync(string path, long offset, int length, CancellationToken ct)
    {
        var blob = new byte[length];
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        fs.Seek(offset, SeekOrigin.Begin);
        await ReadExactlyAsync(fs, blob, ct);
        return blob;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(fs, ct)).ToLowerInvariant();
    }

    private static async Task<uint> ReadU32Async(Stream s, CancellationToken ct)
    {
        var b = new byte[4];
        await ReadExactlyAsync(s, b, ct);
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    private static void WriteU32(Stream s, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        s.Write(b);
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

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
    private static string Sanitize(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));

    private static string Render(NormalMapNativePayloadMaterializerResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5e alpha 10c tracked payload materialization");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Tracked assets: {r.Assets:N0}");
        sb.AppendLine($"Physical tracked assets: {r.PhysicalAssets:N0}");
        sb.AppendLine($"Alias assets: {r.AliasAssets:N0}");
        sb.AppendLine($"Materialized full payload bytes: {r.MaterializedPayloadBytes:N0}");
        sb.AppendLine($"Rebased packed pointers read-back verified: {r.PatchedPointersVerified:N0}");
        sb.AppendLine($"Packed/shared target payloads still pending: {r.PackedSharedTargetsPending:N0}");
        sb.AppendLine($"Nested child-pointer rebasing pending: {r.NestedPointerRebasePending}");
        sb.AppendLine($"Read-back errors: {r.ReadBackErrors:N0}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine($"Hardware ready: {r.HardwareReady}");
        sb.AppendLine($"Local payload root: {r.LocalPayloadRoot}");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: Stage 5e now materializes the complete verified serialized payload blob for every tracked physical asset, including all inline-owned bytes already proven by Stage 4. Packed/shared targets still need real target-object ownership resolution, and packed pointer fields inside nested child structures still need a typed rebasing walk, so hardwareReady remains false.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }
}
