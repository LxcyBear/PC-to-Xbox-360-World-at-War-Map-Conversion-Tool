using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4ResolvedAsset(
    int AssetIndex,
    string TypeName,
    long PayloadOffset,
    string Name,
    string Details,
    string Confidence);

public sealed record T4StringTableData(
    long PayloadOffset,
    string Name,
    int ColumnCount,
    int RowCount,
    IReadOnlyList<uint> RawPointers,
    IReadOnlyList<string?> Values)
{
    public string? GetValue(int row, int column)
    {
        if (row < 0 || row >= RowCount || column < 0 || column >= ColumnCount)
            return null;
        return Values[row * ColumnCount + column];
    }
}

public sealed record T4LocalizeData(long PayloadOffset, string Name, string Value, uint SourceValuePointer = T4ZonePointer.Following);
public sealed record T4RawFileData(long PayloadOffset, long EndOffset, string Name, int Length, string Preview, byte[] Buffer);
public sealed record T4MenuListData(long PayloadOffset, string Name, int MenuCount, IReadOnlyList<uint> MenuPointers);

public sealed class T4PayloadScanResult
{
    public required T4ZoneDocument Document { get; init; }
    public required IReadOnlyList<T4ResolvedAsset> ResolvedAssets { get; init; }
    public required IReadOnlyList<T4StringTableData> StringTables { get; init; }
    public required IReadOnlyList<T4LocalizeData> LocalizeEntries { get; init; }
    public required IReadOnlyList<T4RawFileData> RawFiles { get; init; }
    public required IReadOnlyList<T4MenuListData> MenuLists { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public IReadOnlyDictionary<int, T4ResolvedAsset> ResolvedByAssetIndex =>
        ResolvedAssets.ToDictionary(x => x.AssetIndex);
}

/// <summary>
/// Conservative Stage 2 resolver for the T4 asset types whose serialized layouts are small and
/// well understood. It deliberately refuses to guess when the number of discovered payloads does
/// not match the XAssetList declaration.
///
/// T4 fixed layouts used here (32-bit):
/// LocalizeEntry = value*, name* (8 bytes)
/// RawFile       = name*, len, buffer* (12 bytes)
/// StringTable   = name*, columnCount, rowCount, values* (16 bytes)
/// MenuList      = name*, menuCount, menus* (12 bytes)
/// </summary>
public static class T4PayloadScanner
{
    private const uint InlineShared = 0xFFFFFFFFu;
    private const uint Inline = 0xFFFFFFFEu;

    public static async Task<T4PayloadScanResult> ScanFastFileAsync(
        string fastFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(fastFilePath))
            throw new FileNotFoundException("Fastfile not found.", fastFilePath);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "payloadscan", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "payload.zone");

        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(fastFilePath, zonePath, cancellationToken);
            var bytes = await File.ReadAllBytesAsync(zonePath, cancellationToken);
            var document = T4ZoneParser.ParseZoneBytes(
                fastFilePath,
                bytes,
                decomp.Platform,
                decomp.Compression,
                decomp.ZoneHeader.Endianness);
            return Scan(document, bytes);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public static T4PayloadScanResult Scan(T4ZoneDocument document, byte[] zone)
    {
        var warnings = new List<string>();
        var payloadStart = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));
        if (payloadStart < 0 || payloadStart >= zone.Length)
            throw new InvalidDataException("The XAsset table ends outside the decompressed zone.");

        var stringTables = ScanStringTables(zone, payloadStart, document.Endianness);
        var menuLists = ScanMenuLists(zone, payloadStart, document.Endianness);
        var localizeCandidates = ScanLocalizeCandidates(zone, payloadStart, document.Endianness, document.Header);
        var expectedLocalize = CountType(document, "localize");
        var localize = SelectContiguousLocalizeChain(localizeCandidates, expectedLocalize, zone, warnings);

        var menuListOffsets = menuLists.Select(x => x.PayloadOffset).ToHashSet();
        var sourceStem = Path.GetFileNameWithoutExtension(document.SourceFastFile);
        var rawCandidates = ScanRawFiles(zone, payloadStart, document.Endianness, sourceStem, menuListOffsets);
        var expectedRawFiles = CountType(document, "rawfile");
        var rawFiles = SelectContiguousRawFileChain(rawCandidates, expectedRawFiles, zone, warnings);

        var resolved = new List<T4ResolvedAsset>();
        AssignExact(document, "stringtable", stringTables,
            x => (x.PayloadOffset, x.Name, $"{x.RowCount} row(s) x {x.ColumnCount} column(s)"),
            resolved, warnings);
        AssignExact(document, "menulist", menuLists,
            x => (x.PayloadOffset, x.Name, $"{x.MenuCount} menu reference(s)"),
            resolved, warnings);
        AssignExact(document, "localize", localize,
            x => (x.PayloadOffset, x.Name, x.SourceValuePointer is T4ZonePointer.Following or T4ZonePointer.Insert ? Preview(x.Value, 90) : T4ZonePointer.Describe(x.SourceValuePointer)),
            resolved, warnings);
        AssignExact(document, "rawfile", rawFiles,
            x => (x.PayloadOffset, x.Name, $"{x.Length:N0} bytes; {x.Preview}"),
            resolved, warnings);

        return new T4PayloadScanResult
        {
            Document = document,
            ResolvedAssets = resolved.OrderBy(x => x.AssetIndex).ToList(),
            StringTables = stringTables,
            LocalizeEntries = localize,
            RawFiles = rawFiles,
            MenuLists = menuLists,
            Warnings = warnings
        };
    }

    private static int CountType(T4ZoneDocument document, string typeName) =>
        document.Assets.Count(a => a.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));

    private static void AssignExact<T>(
        T4ZoneDocument document,
        string typeName,
        IReadOnlyList<T> payloads,
        Func<T, (long Offset, string Name, string Details)> project,
        List<T4ResolvedAsset> resolved,
        List<string> warnings)
    {
        var assets = document.Assets
            .Where(a => a.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Index)
            .ToList();

        if (assets.Count == 0)
            return;

        if (assets.Count != payloads.Count)
        {
            warnings.Add($"{typeName}: XAssetList declares {assets.Count:N0}, but conservative payload scanning found {payloads.Count:N0}. " +
                         "No index-to-payload mapping was applied for this type.");
            return;
        }

        for (var i = 0; i < assets.Count; i++)
        {
            var p = project(payloads[i]);
            resolved.Add(new T4ResolvedAsset(
                assets[i].Index,
                typeName,
                p.Offset,
                p.Name,
                p.Details,
                "Exact count/order"));
        }
    }

    private static List<T4StringTableData> ScanStringTables(byte[] zone, int start, Endian endian)
    {
        var output = new List<T4StringTableData>();

        for (var offset = start; offset <= zone.Length - 16; offset++)
        {
            var namePtr = ReadU32(zone, offset, endian);
            var columns = ReadU32(zone, offset + 4, endian);
            var rows = ReadU32(zone, offset + 8, endian);
            var valuesPtr = ReadU32(zone, offset + 12, endian);

            if (!IsInline(namePtr) || !IsInline(valuesPtr)) continue;
            if (columns is < 1 or > 128 || rows is < 1 or > 4096) continue;

            var valueCount64 = (long)columns * rows;
            if (valueCount64 > 200_000) continue;
            var valueCount = (int)valueCount64;

            if (!TryReadCString(zone, offset + 16, 512, out var name, out var pointerTableOffset)) continue;
            if (!LooksLikeAssetPath(name)) continue;
            if (pointerTableOffset > zone.Length - valueCount * 4) continue;

            var rawPointers = new uint[valueCount];
            var validPointers = 0;
            for (var i = 0; i < valueCount; i++)
            {
                var pointer = ReadU32(zone, pointerTableOffset + i * 4, endian);
                rawPointers[i] = pointer;
                if (IsSerializedPointer(pointer)) validPointers++;
            }

            if (validPointers != valueCount) continue;

            var cursor = pointerTableOffset + valueCount * 4;
            var values = new string?[valueCount];
            var okay = true;
            for (var i = 0; i < valueCount; i++)
            {
                var pointer = rawPointers[i];
                if (IsInline(pointer))
                {
                    if (!TryReadCString(zone, cursor, 64 * 1024, out var value, out var next))
                    {
                        okay = false;
                        break;
                    }
                    values[i] = value;
                    cursor = next;
                }
                else if (pointer == 0)
                {
                    values[i] = null;
                }
                else
                {
                    values[i] = T4ZonePointer.Describe(pointer);
                }
            }

            if (!okay) continue;

            output.Add(new T4StringTableData(
                offset,
                name,
                (int)columns,
                (int)rows,
                rawPointers,
                values));

            // Avoid rediscovering structures inside a table's pointer/string body.
            offset = Math.Max(offset, cursor - 1);
        }

        return output;
    }

    private sealed record LocalizeCandidate(long PayloadOffset, string Name, string Value, int EndOffset, uint SourceValuePointer = T4ZonePointer.Following);

    private static List<LocalizeCandidate> ScanLocalizeCandidates(byte[] zone, int start, Endian endian, ZoneHeaderSummary header)
    {
        var output = new List<LocalizeCandidate>();
        for (var offset = start; offset <= zone.Length - 8; offset++)
        {
            var valuePtr = ReadU32(zone, offset, endian);
            var namePtr = ReadU32(zone, offset + 4, endian);
            if (!IsInline(valuePtr) || !IsInline(namePtr)) continue;

            // Localized values in stock WaW are not guaranteed to be strict ASCII/UTF-8.
            // The shipped patch.ff contains single-byte characters such as ü and ®.  Use a
            // conservative Latin-1 reader for VALUE only, while keeping the localization key
            // itself strict ASCII so binary data cannot masquerade as a LocalizeEntry.
            if (!TryReadLocalizedValue(zone, offset + 8, 16 * 1024, out var value, out var afterValue)) continue;
            if (!TryReadCString(zone, afterValue, 1024, out var name, out var end)) continue;
            if (!LooksLikeLocalizationKey(name)) continue;

            output.Add(new LocalizeCandidate(offset, name, value, end, valuePtr));
        }

        // 10m: verified on the original PC nazi_zombie_tower.ff. One of its 128
        // serialized LocalizeEntries uses a packed Virtual-block VALUE pointer,
        // followed by an inline localization NAME. The previous scanner assumed
        // both fields were inline and saw only 127 out of the real 128-entry chain.
        // Do not globally guess packed aliases: only admit one when its physical
        // body bridges TWO already validated inline LocalizeEntry bodies, including
        // only proven zero-byte alignment padding. A packed VALUE is never treated
        // as a string or blindly copied into an Xbox zone.
        var inlineStarts = output.Select(c => c.PayloadOffset).ToHashSet();
        var packedEntries = new List<LocalizeCandidate>();
        var inspected = new HashSet<long>();
        foreach (var prior in output)
        {
            foreach (var possible in CandidateContinuationOffsets(zone, prior.EndOffset))
            {
                if (!inspected.Add(possible) || possible < start || possible > zone.Length - 8)
                    continue;
                var valuePointer = ReadU32(zone, checked((int)possible), endian);
                var namePointer = ReadU32(zone, checked((int)possible + 4), endian);
                if (!IsInline(namePointer) || IsInline(valuePointer) || valuePointer == 0 ||
                    !T4ZonePointer.IsWithinBlock(valuePointer, header))
                    continue;

                if (!TryReadCString(zone, checked((int)possible + 8), 1024, out var name, out var end) ||
                    !LooksLikeLocalizationKey(name))
                    continue;
                if (!CandidateContinuationOffsets(zone, end).Any(inlineStarts.Contains))
                    continue;

                packedEntries.Add(new LocalizeCandidate(possible, name, string.Empty, end, valuePointer));
            }
        }
        output.AddRange(packedEntries);
        return output.OrderBy(c => c.PayloadOffset).ToList();
    }

    private static List<T4LocalizeData> SelectContiguousLocalizeChain(
        IReadOnlyList<LocalizeCandidate> candidates,
        int expectedCount,
        byte[] zone,
        List<string> warnings)
    {
        if (expectedCount == 0) return new List<T4LocalizeData>();
        if (candidates.Count == 0)
        {
            warnings.Add($"localize: XAssetList declares {expectedCount:N0}, but no conservative LocalizeEntry chain was found.");
            return new List<T4LocalizeData>();
        }

        // Alpha 10l: restore the strong exact-count proof before requiring adjacency.
        // 10k accidentally made every candidate body participate in one physical
        // continuation chain. That regressed valid patch/load zones where the
        // XAsset bodies are separated by other serialized ownership data even
        // though the conservative global scan found exactly the declared set.
        // Exact declared count + unique, non-overlapping bodies is authoritative.
        if (candidates.Count == expectedCount && NonOverlapping(
                candidates.OrderBy(x => x.PayloadOffset).Select(x => (x.PayloadOffset, (long)x.EndOffset))))
        {
            warnings.Add($"INFO: localize: exact-count proof accepted {expectedCount:N0}/{expectedCount:N0} unique non-overlapping conservative body(ies) without requiring physical adjacency.");
            return candidates
                .OrderBy(x => x.PayloadOffset)
                .Select(x => new T4LocalizeData(x.PayloadOffset, x.Name, x.Value, x.SourceValuePointer))
                .ToList();
        }

        var chains = BuildContinuationChains(
            candidates,
            x => x.PayloadOffset,
            x => x.EndOffset,
            zone);

        var exact = chains.Where(c => c.Count == expectedCount).ToList();
        List<LocalizeCandidate>? selected = exact.Count == 1
            ? exact[0]
            : exact.Count > 1
                ? exact.OrderBy(c => c[0].PayloadOffset).First()
                : chains.OrderByDescending(c => c.Count).ThenBy(c => c[0].PayloadOffset).FirstOrDefault();

        if (selected is null || selected.Count != expectedCount)
        {
            warnings.Add($"localize: XAssetList declares {expectedCount:N0}; global scan found {candidates.Count:N0} candidate(s), longest exact/aligned zero-pad chain is {selected?.Count ?? 0:N0}. " +
                         "Localization assets were left unresolved rather than guessed.");
            return new List<T4LocalizeData>();
        }

        if (exact.Count > 1)
            warnings.Add($"localize: multiple {expectedCount:N0}-entry chains matched; using the earliest serialized chain.");

        foreach (var linked in selected.Where(x => !IsInline(x.SourceValuePointer)))
            warnings.Add($"INFO: localize '{linked.Name}': packed source VALUE {T4ZonePointer.Describe(linked.SourceValuePointer)} proven in serialized chain; native string promotion remains blocked for this one entry.");

        return selected
            .OrderBy(x => x.PayloadOffset)
            .Select(x => new T4LocalizeData(x.PayloadOffset, x.Name, x.Value, x.SourceValuePointer))
            .ToList();
    }

    private static List<T4MenuListData> ScanMenuLists(byte[] zone, int start, Endian endian)
    {
        var output = new List<T4MenuListData>();
        for (var offset = start; offset <= zone.Length - 12; offset++)
        {
            var namePtr = ReadU32(zone, offset, endian);
            var count = ReadU32(zone, offset + 4, endian);
            var menusPtr = ReadU32(zone, offset + 8, endian);
            if (!IsInline(namePtr) || !IsInline(menusPtr)) continue;
            if (count is < 1 or > 512) continue;

            if (!TryReadCString(zone, offset + 12, 1024, out var name, out var pointerOffset)) continue;
            if (!LooksLikeAssetPath(name)) continue;
            if (pointerOffset > zone.Length - (int)count * 4) continue;

            var pointers = new uint[(int)count];
            var okay = true;
            for (var i = 0; i < count; i++)
            {
                var pointer = ReadU32(zone, pointerOffset + i * 4, endian);
                if (!IsSerializedPointer(pointer))
                {
                    okay = false;
                    break;
                }
                pointers[i] = pointer;
            }
            if (!okay) continue;

            output.Add(new T4MenuListData(offset, name, (int)count, pointers));
        }
        return output;
    }

    private static List<T4RawFileData> ScanRawFiles(
        byte[] zone,
        int start,
        Endian endian,
        string sourceStem,
        HashSet<long> menuListOffsets)
    {
        var output = new List<T4RawFileData>();
        for (var offset = start; offset <= zone.Length - 12; offset++)
        {
            if (menuListOffsets.Contains(offset)) continue;

            var namePtr = ReadU32(zone, offset, endian);
            var length = ReadU32(zone, offset + 4, endian);
            var bufferPtr = ReadU32(zone, offset + 8, endian);
            if (!IsInline(namePtr)) continue;
            if (bufferPtr != 0 && !IsInline(bufferPtr)) continue;
            if (length > 64 * 1024 * 1024) continue;

            if (!TryReadCString(zone, offset + 12, 1024, out var name, out var bufferOffset)) continue;
            if (!LooksLikeRawFileName(name, sourceStem)) continue;

            if (bufferPtr == 0)
            {
                if (length != 0) continue;
                output.Add(new T4RawFileData(offset, bufferOffset, name, 0, "<null buffer>", Array.Empty<byte>()));
                continue;
            }

            var totalLength = checked((long)length + 1L);
            if (bufferOffset < 0 || bufferOffset > zone.Length - totalLength) continue;

            var previewLength = (int)Math.Min(length, 96u);
            var preview = PreviewBuffer(zone.AsSpan(bufferOffset, previewLength));
            var buffer = zone.AsSpan(bufferOffset, checked((int)length + 1)).ToArray();
            output.Add(new T4RawFileData(offset, bufferOffset + totalLength, name, (int)length, preview, buffer));
        }

        return output;
    }

    private static List<T4RawFileData> SelectContiguousRawFileChain(
        IReadOnlyList<T4RawFileData> candidates,
        int expectedCount,
        byte[] zone,
        List<string> warnings)
    {
        if (expectedCount == 0) return new List<T4RawFileData>();
        if (candidates.Count == 0)
        {
            warnings.Add($"rawfile: XAssetList declares {expectedCount:N0}, but no conservative RawFile chain was found.");
            return new List<T4RawFileData>();
        }

        // Alpha 10l exact-count restoration. A conservative RawFile candidate
        // already proves its fixed body, owned name, owned/null buffer, bounded
        // length and plausible asset path. When that candidate set is exactly
        // the XAsset-declared physical count and the measured bodies do not
        // overlap, requiring byte adjacency is unnecessary. This is the 10k
        // regression that collapsed Fortress foundation promotion 271 -> 4.
        if (candidates.Count == expectedCount && NonOverlapping(
                candidates.OrderBy(x => x.PayloadOffset).Select(x => (x.PayloadOffset, x.EndOffset))))
        {
            warnings.Add($"INFO: rawfile: exact-count proof accepted {expectedCount:N0}/{expectedCount:N0} unique non-overlapping conservative body(ies) without requiring physical adjacency.");
            return candidates.OrderBy(x => x.PayloadOffset).ToList();
        }

        var chains = BuildContinuationChains(
            candidates,
            x => x.PayloadOffset,
            x => x.EndOffset,
            zone);

        var exact = chains.Where(c => c.Count == expectedCount).ToList();
        List<T4RawFileData>? selected = exact.Count == 1
            ? exact[0]
            : exact.Count > 1
                ? exact.OrderBy(c => c[0].PayloadOffset).First()
                : chains.OrderByDescending(c => c.Count).ThenBy(c => c[0].PayloadOffset).FirstOrDefault();

        if (selected is null || selected.Count != expectedCount)
        {
            warnings.Add($"rawfile: XAssetList declares {expectedCount:N0}; global scan found {candidates.Count:N0} candidate(s), " +
                         $"but the longest exact/aligned zero-pad chain is {selected?.Count ?? 0:N0}. RawFiles remain unresolved.");
            return new List<T4RawFileData>();
        }

        if (exact.Count > 1)
            warnings.Add($"rawfile: multiple {expectedCount:N0}-entry conservative chains matched; using the earliest serialized chain.");
        else if (candidates.Count != expectedCount)
            warnings.Add($"INFO: rawfile: reduced {candidates.Count:N0} global candidate(s) to one authoritative {expectedCount:N0}-entry serialized chain.");

        return selected.OrderBy(x => x.PayloadOffset).ToList();
    }

    private static List<List<T>> BuildContinuationChains<T>(
        IReadOnlyList<T> candidates,
        Func<T, long> getStart,
        Func<T, long> getEnd,
        byte[] zone)
    {
        var ordered = candidates.OrderBy(getStart).ToList();
        var byStart = ordered
            .GroupBy(getStart)
            .ToDictionary(g => g.Key, g => g.ToList());
        var chains = new List<List<T>>();

        foreach (var first in ordered)
        {
            var chain = new List<T> { first };
            var current = first;
            var seenStarts = new HashSet<long> { getStart(first) };
            while (true)
            {
                var next = CandidateContinuationOffsets(zone, getEnd(current))
                    .Where(byStart.ContainsKey)
                    .SelectMany(o => byStart[o])
                    .Where(x => getStart(x) > getStart(current))
                    .OrderBy(getStart)
                    .ToList();
                if (next.Count != 1) break;
                var chosen = next[0];
                var chosenStart = getStart(chosen);
                if (!seenStarts.Add(chosenStart)) break;
                chain.Add(chosen);
                current = chosen;
            }
            chains.Add(chain);
        }
        return chains;
    }

    private static bool NonOverlapping(IEnumerable<(long Start, long End)> spans)
    {
        long? previousEnd = null;
        foreach (var span in spans.OrderBy(x => x.Start))
        {
            if (span.Start < 0 || span.End <= span.Start) return false;
            if (previousEnd.HasValue && span.Start < previousEnd.Value) return false;
            previousEnd = span.End;
        }
        return previousEnd.HasValue;
    }

    private static IReadOnlyList<long> CandidateContinuationOffsets(byte[] zone, long endOffset)
    {
        var output = new List<long>();
        if (endOffset < 0 || endOffset > zone.LongLength) return output;
        output.Add(endOffset);
        foreach (var alignment in new[] { 4L, 8L, 16L })
        {
            var aligned = checked((endOffset + alignment - 1) & ~(alignment - 1));
            if (aligned == endOffset || aligned > zone.LongLength) continue;
            var zeroPad = true;
            for (var p = endOffset; p < aligned; p++)
            {
                if (zone[checked((int)p)] == 0) continue;
                zeroPad = false;
                break;
            }
            if (zeroPad) output.Add(aligned);
        }
        return output.Distinct().OrderBy(x => x).ToList();
    }

    private static bool LooksLikeRawFileName(string name, string sourceStem)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 512 || !IsPrintableAscii(name)) return false;
        if (name.Equals(sourceStem, StringComparison.OrdinalIgnoreCase)) return true;
        // Alpha 10o: a bare slash/backslash is a binary false positive, not an asset path.
        if ((name.Contains('/') || name.Contains('\\')) && name.Any(char.IsLetterOrDigit)) return true;

        var ext = Path.GetExtension(name);
        if (!string.IsNullOrWhiteSpace(ext))
        {
            return ext.Equals(".gsc", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".csc", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".cfg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".menu", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".inc", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".vision", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".sun", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".arena", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".str", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".atr", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".rmb", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool LooksLikeAssetPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || !IsPrintableAscii(value)) return false;
        return value.Contains('/') || value.Contains('\\') || value.Contains('.') || value.Contains("menu", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeLocalizationKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || !IsPrintableAscii(value)) return false;
        var hasLetter = false;
        foreach (var c in value)
        {
            if (char.IsLetter(c)) hasLetter = true;
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or ':' or '@')) return false;
        }
        return hasLetter;
    }

    private static bool IsPrintableAscii(string value) =>
        value.All(c => c is >= ' ' and <= '~');

    private static bool IsInline(uint pointer) => pointer is InlineShared or Inline;

    private static bool IsSerializedPointer(uint pointer) =>
        pointer == 0 || IsInline(pointer) || (pointer & 0x80000000u) != 0;

    private static uint ReadU32(byte[] data, int offset, Endian endian)
    {
        if (offset < 0 || offset > data.Length - 4) return uint.MaxValue - 2;
        return EndianIO.ReadUInt32(data.AsSpan(offset, 4), endian);
    }


    private static bool TryReadLocalizedValue(byte[] data, int offset, int maxBytes, out string value, out int nextOffset)
    {
        value = string.Empty;
        nextOffset = offset;
        if (offset < 0 || offset >= data.Length || maxBytes <= 0) return false;

        var limit = Math.Min(data.Length, offset + maxBytes + 1);
        var end = offset;
        while (end < limit && data[end] != 0) end++;
        if (end >= limit || end >= data.Length) return false;

        var span = data.AsSpan(offset, end - offset);
        var highBytes = 0;
        foreach (var b in span)
        {
            if (b is < 0x09 || (b is > 0x0D and < 0x20))
                return false;
            if (b >= 0x80) highBytes++;
        }

        // Enough to admit ordinary Western-European localization characters without
        // accepting arbitrary binary blobs that happen to be NUL-terminated.
        if (highBytes > Math.Max(8, span.Length / 8))
            return false;

        value = Encoding.Latin1.GetString(span);
        nextOffset = end + 1;
        return true;
    }

    private static bool TryReadCString(byte[] data, int offset, int maxBytes, out string value, out int nextOffset)
    {
        value = string.Empty;
        nextOffset = offset;
        if (offset < 0 || offset >= data.Length || maxBytes <= 0) return false;

        var limit = Math.Min(data.Length, offset + maxBytes + 1);
        var end = offset;
        while (end < limit && data[end] != 0) end++;
        if (end >= limit || end >= data.Length) return false;

        var span = data.AsSpan(offset, end - offset);
        // Asset names/menus/localization keys in the T4 zones we care about are ASCII/UTF-8.
        // Reject obvious binary blobs here; this scanner is intentionally conservative.
        foreach (var b in span)
        {
            if (b is < 0x09 or > 0x7E || (b is > 0x0D and < 0x20))
                return false;
        }

        value = Encoding.UTF8.GetString(span);
        nextOffset = end + 1;
        return true;
    }

    private static string PreviewBuffer(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return "<empty>";
        var sb = new StringBuilder(data.Length);
        foreach (var b in data)
        {
            if (b is >= 0x20 and <= 0x7E) sb.Append((char)b);
            else if (b is 0x0D or 0x0A or 0x09) sb.Append(' ');
            else sb.Append('.');
        }
        return Preview(sb.ToString(), 90);
    }

    private static string Preview(string value, int max)
    {
        var flat = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}
