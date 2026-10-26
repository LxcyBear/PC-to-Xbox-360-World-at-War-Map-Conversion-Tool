using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Linker;

public sealed record T4XboxTechniqueDonor(
    string Name,
    string SourceFastFile,
    string SourceSha256,
    int AssetIndex,
    int SourceOffset,
    int NonNullTechniqueSlots,
    int InlineTechniqueSlots,
    int PackedTechniqueSlots,
    bool RemappedTechniqueSetNonNull,
    string Evidence);

public sealed class T4XboxTechniqueDonorCatalogResult
{
    public required string XboxGameFolder { get; init; }
    public required int FastFilesDiscovered { get; init; }
    public required int FastFilesScanned { get; init; }
    public required int FastFilesRejected { get; init; }
    public required int DeclaredTechniqueSets { get; init; }
    public required int ResolvedTechniqueSets { get; init; }
    public required IReadOnlyList<T4XboxTechniqueDonor> Donors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public IReadOnlyList<T4XboxTechniqueDonor> FindExact(string name) =>
        Donors.Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
}

/// <summary>
/// Conservative donor-name index over user-owned Xbox 360 WaW fastfiles.
///
/// This scanner does not copy or redistribute game data. It only establishes donor identity
/// and source provenance. A donor match is NOT permission to serialize its shader graph yet;
/// graph extraction/rebasing remains a separate production gate.
///
/// Fail-closed policy:
/// - only Xbox 360 / big-endian T4 zones are accepted;
/// - only physical top-level TechniqueSet rows are considered;
/// - candidate fixed bodies must have a valid serialized-pointer shape for all 51 Xbox slots;
/// - candidate count must exactly equal the declared physical TechniqueSet count before
///   asset-index/order mapping is accepted.
/// </summary>
public static class T4XboxTechniqueDonorCatalog
{
    private const int XboxTechniqueSetSize = 216;
    private const int XboxTechniqueSlots = 51;
    private static readonly ConcurrentDictionary<string, Lazy<Task<T4XboxTechniqueDonorCatalogResult>>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task<T4XboxTechniqueDonorCatalogResult> BuildFastFileAsync(
        string xboxFastFile,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xboxFastFile))
            return Empty(string.Empty, "Xbox donor fastfile is not configured.");

        var full = Path.GetFullPath(xboxFastFile);
        if (!File.Exists(full))
            return Empty(Path.GetDirectoryName(full) ?? string.Empty, $"Xbox donor fastfile does not exist: {full}");

        var warnings = new List<string>();
        try
        {
            var cached = await FastFileAnalysisCache.GetZoneAsync(full, cancellationToken);
            if (cached.Platform != WaWPlatform.Xbox360 || cached.Document.Endianness != Endian.Big)
                return Empty(Path.GetDirectoryName(full) ?? string.Empty,
                    $"Xbox donor fastfile is not Xbox360/big-endian: {cached.Platform}/{cached.Document.Endianness}");

            var physical = cached.Document.Assets
                .Where(a => a.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase) &&
                            a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                .OrderBy(a => a.Index)
                .ToList();

            var rows = ScanExact(cached.Bytes, cached.Document, physical.Count, out var reason);
            if (rows is null)
                return Empty(Path.GetDirectoryName(full) ?? string.Empty,
                    $"{Path.GetFileName(full)}: TechniqueSet donor identity refused: {reason}");

            var donors = new List<T4XboxTechniqueDonor>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var asset = physical[i];
                donors.Add(new T4XboxTechniqueDonor(
                    row.Name,
                    full,
                    cached.SourceSha256,
                    asset.Index,
                    row.Offset,
                    row.NonNull,
                    row.Inline,
                    row.Packed,
                    row.RemappedNonNull,
                    $"exact single-file declared-count/order mapping in Xbox big-endian zone; fixedBody=0x{row.Offset:X}; slots={row.NonNull}"));
            }

            return new T4XboxTechniqueDonorCatalogResult
            {
                XboxGameFolder = Path.GetDirectoryName(full) ?? string.Empty,
                FastFilesDiscovered = 1,
                FastFilesScanned = 1,
                FastFilesRejected = 0,
                DeclaredTechniqueSets = physical.Count,
                ResolvedTechniqueSets = donors.Count,
                Donors = donors
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.AssetIndex)
                    .ToList(),
                Warnings = warnings
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"{Path.GetFileName(full)}: donor scan skipped: {ex.Message}");
            return new T4XboxTechniqueDonorCatalogResult
            {
                XboxGameFolder = Path.GetDirectoryName(full) ?? string.Empty,
                FastFilesDiscovered = 1,
                FastFilesScanned = 0,
                FastFilesRejected = 1,
                DeclaredTechniqueSets = 0,
                ResolvedTechniqueSets = 0,
                Donors = Array.Empty<T4XboxTechniqueDonor>(),
                Warnings = warnings
            };
        }
    }

    public static Task<T4XboxTechniqueDonorCatalogResult> BuildAsync(
        string xboxGameFolder,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xboxGameFolder))
            return Task.FromResult(Empty(string.Empty, "Xbox game folder is not configured."));

        var full = Path.GetFullPath(xboxGameFolder);
        if (!Directory.Exists(full))
            return Task.FromResult(Empty(full, $"Xbox game folder does not exist: {full}"));

        // The catalog is immutable for one process run. FastFileAnalysisCache already keys
        // decompressed content by source SHA-256, so rebuilding the GUI/process naturally
        // refreshes this directory index after users replace game files.
        var lazy = Cache.GetOrAdd(full, key => new Lazy<Task<T4XboxTechniqueDonorCatalogResult>>(
            () => BuildCoreAsync(key, CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication));

        return AwaitWithCancellationAsync(lazy.Value, cancellationToken);
    }

    private static async Task<T4XboxTechniqueDonorCatalogResult> BuildCoreAsync(
        string xboxGameFolder,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var donors = new List<T4XboxTechniqueDonor>();
        var files = Directory.EnumerateFiles(xboxGameFolder, "*.ff", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scanned = 0;
        var rejected = 0;
        var declared = 0;

        foreach (var ff in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var cached = await FastFileAnalysisCache.GetZoneAsync(ff, cancellationToken);
                if (cached.Platform != WaWPlatform.Xbox360 || cached.Document.Endianness != Endian.Big)
                {
                    rejected++;
                    continue;
                }

                var physical = cached.Document.Assets
                    .Where(a => a.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase) &&
                                a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                    .OrderBy(a => a.Index)
                    .ToList();
                if (physical.Count == 0)
                {
                    scanned++;
                    continue;
                }

                declared += physical.Count;
                var rows = ScanExact(cached.Bytes, cached.Document, physical.Count, out var reason);
                if (rows is null)
                {
                    rejected++;
                    warnings.Add($"{Path.GetFileName(ff)}: TechniqueSet donor identity refused: {reason}");
                    continue;
                }

                scanned++;
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    var asset = physical[i];
                    donors.Add(new T4XboxTechniqueDonor(
                        row.Name,
                        ff,
                        cached.SourceSha256,
                        asset.Index,
                        row.Offset,
                        row.NonNull,
                        row.Inline,
                        row.Packed,
                        row.RemappedNonNull,
                        $"exact declared-count/order mapping in Xbox big-endian zone; fixedBody=0x{row.Offset:X}; slots={row.NonNull}"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rejected++;
                warnings.Add($"{Path.GetFileName(ff)}: donor scan skipped: {ex.Message}");
            }
        }

        return new T4XboxTechniqueDonorCatalogResult
        {
            XboxGameFolder = xboxGameFolder,
            FastFilesDiscovered = files.Count,
            FastFilesScanned = scanned,
            FastFilesRejected = rejected,
            DeclaredTechniqueSets = declared,
            ResolvedTechniqueSets = donors.Count,
            Donors = donors
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.SourceFastFile, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.AssetIndex)
                .ToList(),
            Warnings = warnings
        };
    }

    private sealed record Candidate(
        int Offset,
        string Name,
        int NonNull,
        int Inline,
        int Packed,
        bool RemappedNonNull);

    private static List<Candidate>? ScanExact(
        byte[] zone,
        T4ZoneDocument document,
        int expected,
        out string reason)
    {
        var payloadStart = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));
        var candidates = new List<Candidate>();

        // T4 serialized structs are at least 4-byte aligned in the zone stream. Restricting
        // to aligned starts both reduces false positives and avoids a byte-by-byte full-zone scan.
        var first = (payloadStart + 3) & ~3;
        for (var offset = first; offset <= zone.Length - XboxTechniqueSetSize - 2; offset += 4)
        {
            var namePtr = U32(zone, offset);
            if (namePtr is not (T4ZonePointer.Following or T4ZonePointer.Insert))
                continue;

            var remapped = U32(zone, offset + 8);
            if (!PointerShapeValid(remapped, document.Header))
                continue;

            var nonNull = 0;
            var inline = 0;
            var packed = 0;
            var okay = true;
            for (var slot = 0; slot < XboxTechniqueSlots; slot++)
            {
                var ptr = U32(zone, offset + 12 + slot * 4);
                if (!PointerShapeValid(ptr, document.Header))
                {
                    okay = false;
                    break;
                }
                if (ptr == 0) continue;
                nonNull++;
                if (ptr is T4ZonePointer.Following or T4ZonePointer.Insert) inline++;
                else packed++;
            }
            if (!okay) continue;

            if (!TryReadAssetName(zone, offset + XboxTechniqueSetSize, out var name))
                continue;

            candidates.Add(new Candidate(offset, name, nonNull, inline, packed, remapped != 0));
        }

        candidates = candidates
            .GroupBy(x => x.Offset)
            .Select(g => g.First())
            .OrderBy(x => x.Offset)
            .ToList();

        if (candidates.Count != expected)
        {
            reason = $"declared {expected:N0} physical TechniqueSet root(s), but conservative fixed-body scan found {candidates.Count:N0}; no donor identity mapping accepted";
            return null;
        }

        var duplicateNames = candidates
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Take(8)
            .ToList();
        if (duplicateNames.Count != 0)
        {
            reason = $"candidate scan contains duplicate TechniqueSet identities ({string.Join(", ", duplicateNames)}); exact top-level identity is ambiguous";
            return null;
        }

        reason = string.Empty;
        return candidates;
    }

    private static bool PointerShapeValid(uint value, ZoneHeaderSummary header) =>
        value == 0 ||
        value is T4ZonePointer.Following or T4ZonePointer.Insert ||
        T4ZonePointer.IsWithinBlock(value, header);

    private static uint U32(byte[] zone, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(zone.AsSpan(offset, 4));

    private static bool TryReadAssetName(byte[] zone, int offset, out string name)
    {
        name = string.Empty;
        if (offset < 0 || offset >= zone.Length) return false;
        var end = offset;
        while (end < zone.Length && end - offset <= 512 && zone[end] != 0)
        {
            var b = zone[end];
            if (b < 0x20 || b > 0x7E) return false;
            end++;
        }
        if (end == offset || end >= zone.Length || end - offset > 512 || zone[end] != 0)
            return false;

        name = Encoding.ASCII.GetString(zone, offset, end - offset);
        if (name.Length < 2 || name.Any(char.IsWhiteSpace)) return false;
        if (!name.Any(char.IsLetter)) return false;
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch)) continue;
            if (ch is '_' or '-' or '/' or '\\' or '.' or '$' or '@') continue;
            return false;
        }
        return true;
    }

    private static T4XboxTechniqueDonorCatalogResult Empty(string folder, string warning) =>
        new()
        {
            XboxGameFolder = folder,
            FastFilesDiscovered = 0,
            FastFilesScanned = 0,
            FastFilesRejected = 0,
            DeclaredTechniqueSets = 0,
            ResolvedTechniqueSets = 0,
            Donors = Array.Empty<T4XboxTechniqueDonor>(),
            Warnings = new[] { warning }
        };

    private static async Task<T> AwaitWithCancellationAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled) return await task;
        return await task.WaitAsync(cancellationToken);
    }
}
