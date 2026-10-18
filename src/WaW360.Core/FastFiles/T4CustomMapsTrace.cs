using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4CustomMapsMarkerTrace(
    long MarkerOffset,
    string Text,
    long? CandidateItemOffset,
    bool StrongInlineItemCandidate,
    string? UsermapMode,
    string? NearestPreviousProvenMenu,
    long? NearestPreviousProvenMenuOffset,
    string? NextProvenMenu,
    long? NextProvenMenuOffset,
    IReadOnlyList<string> FollowingStrings,
    T4ConsoleItemDefCandidate? Item,
    int SharedParentValidItemCount,
    int RegionSiblingCount,
    int? SerializedSiblingOrdinal,
    long? ItemPointerArrayOffset,
    int ItemPointerArrayCount,
    int? LogicalItemIndex,
    string ItemPointerArrayConfidence,
    string? ResolvedOwnerMenu,
    long? ResolvedOwnerMenuOffset,
    int? ResolvedOwnerDeclaredItemCount,
    string OwnerConfidence,
    string Confidence);

public sealed class T4CustomMapsTraceResult
{
    public required T4ZoneDocument Document { get; init; }
    public required IReadOnlyList<T4CustomMapsMarkerTrace> Markers { get; init; }
    public required IReadOnlyList<T4MenuStructureCandidate> ProvenMenus { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Stage 2 correlation helper for codxe's existing "Custom Maps" UI entries.
///
/// Nothing is edited here. Alpha 6 replaces the earlier 408-byte heuristic with the
/// structurally derived 400-byte Xbox 360 T4 itemDef_s layout and validates the fixed
/// body field-by-field before accepting a marker as a real menu item.
///
/// Alpha 7 additionally treats itemDef_s.parent values as opaque pre-linked/runtime
/// addresses when they do not decode as sane zone offsets. It proves ownership by
/// finding every structurally valid itemDef_s carrying the exact same parent value,
/// then correlating that sibling group against a proven menuDef_t serialized region
/// and the menu's declared itemCount. This remains read-only.
/// </summary>
public static class T4CustomMapsTrace
{
    private static readonly byte[] CustomMapsBytes = Encoding.ASCII.GetBytes("Custom Maps");

    public static async Task<T4CustomMapsTraceResult> TraceFastFileAsync(
        string fastFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(fastFilePath))
            throw new FileNotFoundException("Fastfile not found.", fastFilePath);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "custommapstrace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "trace.zone");

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

            return Trace(document, bytes);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public static T4CustomMapsTraceResult Trace(T4ZoneDocument document, byte[] zone)
    {
        var warnings = new List<string>();
        if (document.Endianness != Endian.Big)
            warnings.Add("Custom Maps structural correlation currently targets the big-endian Xbox 360 T4 layout only.");

        var menuProbe = T4MenuStructureProbe.Probe(document, zone);
        warnings.AddRange(menuProbe.Warnings);

        var markerOffsets = FindAll(zone, CustomMapsBytes);
        var traces = new List<T4CustomMapsMarkerTrace>(markerOffsets.Count);

        foreach (var markerOffset in markerOffsets)
        {
            var strings = ReadConsecutivePrintableStrings(zone, checked((int)markerOffset), maxStrings: 8);
            var mode = DetectUsermapMode(strings);

            var itemOffset = markerOffset - T4ConsoleItemDefProbe.FixedSize;
            T4ConsoleItemDefCandidate? item = null;
            var structuralItem = itemOffset >= 0 &&
                                 T4ConsoleItemDefProbe.TryRead(
                                     zone,
                                     checked((int)itemOffset),
                                     document.Endianness,
                                     out item);

            // For these codxe buttons the text itself is an inline field immediately after the
            // fixed item body. Require the real text/action pointers plus the known usermap action;
            // this is much stronger than alpha 5's old offset-only heuristic.
            var hasCodxeAction = strings.Count >= 2 &&
                                 strings[0].Equals("Custom Maps", StringComparison.Ordinal) &&
                                 strings.Skip(1).Any(s => s.Contains("ui_codxe_usermap_mode", StringComparison.Ordinal)) &&
                                 strings.Skip(1).Any(s => s.Contains("codxe_usermaps", StringComparison.Ordinal));
            var strongItem = structuralItem && item is not null &&
                             item.RawTextPointer is 0xFFFFFFFFu or 0xFFFFFFFEu &&
                             item.RawActionPointer is 0xFFFFFFFFu or 0xFFFFFFFEu &&
                             hasCodxeAction;

            var previous = menuProbe.InlineNamedMenus
                .Where(m => m.SerializedOffset < markerOffset)
                .OrderByDescending(m => m.SerializedOffset)
                .FirstOrDefault();
            var next = menuProbe.InlineNamedMenus
                .Where(m => m.SerializedOffset > markerOffset)
                .OrderBy(m => m.SerializedOffset)
                .FirstOrDefault();

            var parentGroup = item is not null && item.RawParentPointer != 0
                ? FindItemsWithExactParent(zone, document.Endianness, item.RawParentPointer)
                : Array.Empty<T4ConsoleItemDefCandidate>();

            var regionSiblings = Array.Empty<T4ConsoleItemDefCandidate>();
            int? serializedOrdinal = null;
            string? ownerName = null;
            long? ownerOffset = null;
            int? ownerDeclaredCount = null;
            long? itemPointerArrayOffset = null;
            var itemPointerArrayCount = 0;
            int? logicalItemIndex = null;
            var itemPointerArrayConfidence = "Unresolved";
            var ownerConfidence = "Unresolved";

            if (item is not null && previous is not null)
            {
                var regionEnd = next?.SerializedOffset ?? zone.LongLength;
                regionSiblings = parentGroup
                    .Where(x => x.SerializedOffset > previous.SerializedOffset && x.SerializedOffset < regionEnd)
                    .OrderBy(x => x.SerializedOffset)
                    .ToArray();

                var ordinal = Array.FindIndex(regionSiblings, x => x.SerializedOffset == item.SerializedOffset);
                if (ordinal >= 0)
                    serializedOrdinal = ordinal;

                if (regionSiblings.Length == previous.ItemCount && ordinal >= 0)
                {
                    ownerName = previous.Name;
                    ownerOffset = previous.SerializedOffset;
                    ownerDeclaredCount = previous.ItemCount;
                    ownerConfidence = $"Strong: {regionSiblings.Length} sibling itemDef_s bodies share parent 0x{item.RawParentPointer:X8}, exactly matching {previous.Name}.itemCount={previous.ItemCount}";

                    if (TryFindInlineItemPointerArray(
                            zone,
                            document.Endianness,
                            previous,
                            regionSiblings[0].SerializedOffset,
                            out var pointerArrayOffset,
                            out var pointerArrayEntries))
                    {
                        itemPointerArrayOffset = pointerArrayOffset;
                        itemPointerArrayCount = pointerArrayEntries;
                        logicalItemIndex = ordinal;
                        itemPointerArrayConfidence =
                            $"Strong: {pointerArrayEntries} inline item pointer slot(s) at 0x{pointerArrayOffset:X8}, matching both {previous.Name}.itemCount and the {regionSiblings.Length} serialized sibling bodies; inline bodies preserve pointer-array order";
                    }
                    else
                    {
                        itemPointerArrayConfidence =
                            $"Owner is proven, but no exact {previous.ItemCount}-entry inline item pointer array was found before the first sibling body.";
                    }
                }
                else if (ordinal >= 0)
                {
                    ownerName = previous.Name;
                    ownerOffset = previous.SerializedOffset;
                    ownerDeclaredCount = previous.ItemCount;
                    ownerConfidence = $"Region correlation only: {regionSiblings.Length} matching-parent item(s) found; {previous.Name}.itemCount={previous.ItemCount}";
                }
            }

            var role = mode switch
            {
                "solo" when strongItem => "Strong: structurally valid 400-byte Xbox itemDef_s + codxe solo action",
                "lobby" when strongItem => "Strong: structurally valid 400-byte Xbox itemDef_s + codxe lobby action",
                _ when strongItem => "Strong structurally valid Custom Maps item",
                _ when structuralItem => "400-byte itemDef_s structure is plausible, but codxe action relationship is not proven",
                _ => "Marker only; no valid 400-byte Xbox itemDef_s immediately precedes the text"
            };

            traces.Add(new T4CustomMapsMarkerTrace(
                markerOffset,
                "Custom Maps",
                structuralItem ? itemOffset : null,
                strongItem,
                mode,
                previous?.Name,
                previous?.SerializedOffset,
                next?.Name,
                next?.SerializedOffset,
                strings,
                item,
                parentGroup.Length,
                regionSiblings.Length,
                serializedOrdinal,
                itemPointerArrayOffset,
                itemPointerArrayCount,
                logicalItemIndex,
                itemPointerArrayConfidence,
                ownerName,
                ownerOffset,
                ownerDeclaredCount,
                ownerConfidence,
                role));
        }

        if (traces.Count == 0)
            warnings.Add("No literal 'Custom Maps' marker was found in this zone.");
        else if (!traces.Any(t => t.UsermapMode == "solo" && t.StrongInlineItemCandidate))
            warnings.Add("No structurally proven Custom Maps button with ui_codxe_usermap_mode=solo was found.");

        return new T4CustomMapsTraceResult
        {
            Document = document,
            Markers = traces,
            ProvenMenus = menuProbe.InlineNamedMenus,
            Warnings = warnings.Distinct().ToArray()
        };
    }

    private static string? DetectUsermapMode(IReadOnlyList<string> strings)
    {
        var combined = string.Join("\n", strings);
        if (combined.Contains("ui_codxe_usermap_mode\" \"solo", StringComparison.Ordinal))
            return "solo";
        if (combined.Contains("ui_codxe_usermap_mode\" \"lobby", StringComparison.Ordinal))
            return "lobby";
        return null;
    }

    private static IReadOnlyList<string> ReadConsecutivePrintableStrings(byte[] zone, int offset, int maxStrings)
    {
        var result = new List<string>();
        var cursor = offset;

        for (var i = 0; i < maxStrings && cursor < zone.Length; i++)
        {
            if (!TryReadPrintableCString(zone, cursor, out var value, out var bytesRead))
                break;

            result.Add(value);
            cursor += bytesRead;
        }

        return result;
    }

    private static bool TryReadPrintableCString(byte[] zone, int offset, out string value, out int bytesRead)
    {
        value = string.Empty;
        bytesRead = 0;
        if (offset < 0 || offset >= zone.Length)
            return false;

        var end = offset;
        const int max = 4096;
        while (end < zone.Length && zone[end] != 0 && end - offset < max)
        {
            var b = zone[end];
            if (b < 0x20 || b > 0x7E)
                return false;
            end++;
        }

        if (end == offset || end >= zone.Length || zone[end] != 0)
            return false;

        value = Encoding.ASCII.GetString(zone, offset, end - offset);
        bytesRead = end - offset + 1;
        return true;
    }

    private static T4ConsoleItemDefCandidate[] FindItemsWithExactParent(byte[] zone, Endian endian, uint parentPointer)
    {
        if (parentPointer == 0)
            return Array.Empty<T4ConsoleItemDefCandidate>();

        Span<byte> pattern = stackalloc byte[4];
        if (endian == Endian.Big)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(pattern, parentPointer);
        else
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(pattern, parentPointer);

        var result = new Dictionary<long, T4ConsoleItemDefCandidate>();
        var start = 0;
        while (start <= zone.Length - 4)
        {
            var relative = zone.AsSpan(start).IndexOf(pattern);
            if (relative < 0)
                break;

            var parentFieldOffset = start + relative;
            var candidateOffset = parentFieldOffset - 244; // itemDef_s.parent field
            if (candidateOffset >= 0 &&
                T4ConsoleItemDefProbe.TryRead(zone, candidateOffset, endian, out var candidate) &&
                candidate is not null &&
                candidate.RawParentPointer == parentPointer &&
                candidate.StructuralChecksPassed >= candidate.StructuralChecksTotal * 0.98)
            {
                result[candidate.SerializedOffset] = candidate;
            }

            start = parentFieldOffset + 1;
        }

        return result.Values.OrderBy(x => x.SerializedOffset).ToArray();
    }



    private static uint ReadU32(byte[] data, int offset, Endian endian)
    {
        var span = data.AsSpan(offset, 4);
        return endian == Endian.Big
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(span)
            : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    private static bool TryFindInlineItemPointerArray(
        byte[] zone,
        Endian endian,
        T4MenuStructureCandidate menu,
        long firstSiblingOffset,
        out long arrayOffset,
        out int entryCount)
    {
        arrayOffset = 0;
        entryCount = 0;

        if (endian != Endian.Big || menu.ItemCount <= 0 || firstSiblingOffset <= menu.SerializedOffset)
            return false;

        var searchStart = checked((int)Math.Max(0, menu.SerializedOffset + 312));
        var searchEnd = checked((int)Math.Min(firstSiblingOffset, zone.LongLength));
        var bytesNeeded = checked(menu.ItemCount * 4);

        if (searchEnd - searchStart < bytesNeeded)
            return false;

        long? best = null;

        // Console T4 serialized menu data can be only 2-byte aligned after inline strings.
        // Look for an exact run of itemCount inline pointer sentinels. We intentionally do
        // not accept arbitrary packed/runtime-looking values here: the logical index proof
        // is only claimed when every menu item is serialized inline in pointer-array order.
        for (var offset = searchStart; offset <= searchEnd - bytesNeeded; offset += 2)
        {
            var allInline = true;
            for (var i = 0; i < menu.ItemCount; i++)
            {
                var value = ReadU32(zone, offset + i * 4, endian);
                if (value is not (0xFFFFFFFFu or 0xFFFFFFFEu))
                {
                    allInline = false;
                    break;
                }
            }

            if (!allInline)
                continue;

            var beforeInline = offset >= 4 &&
                               ReadU32(zone, offset - 4, endian) is 0xFFFFFFFFu or 0xFFFFFFFEu;
            var after = offset + bytesNeeded;
            var afterInline = after + 4 <= searchEnd &&
                              ReadU32(zone, after, endian) is 0xFFFFFFFFu or 0xFFFFFFFEu;
            if (beforeInline || afterInline)
                continue;

            best = offset;
        }

        if (best is null)
            return false;

        arrayOffset = best.Value;
        entryCount = menu.ItemCount;
        return true;
    }

    private static List<long> FindAll(byte[] haystack, byte[] needle)
    {
        var result = new List<long>();
        var start = 0;
        while (start <= haystack.Length - needle.Length)
        {
            var relative = haystack.AsSpan(start).IndexOf(needle);
            if (relative < 0)
                break;
            var absolute = start + relative;
            result.Add(absolute);
            start = absolute + 1;
        }
        return result;
    }
}
