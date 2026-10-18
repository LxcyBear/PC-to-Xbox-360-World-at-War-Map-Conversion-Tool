using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4Stage3RelocationAuditResult(
    string InputFastFile,
    string ReportFile,
    int RawZoneBytes,
    uint ZoneSize,
    uint TempBlockSize,
    uint RuntimeBlockSize,
    uint LargeRuntimeBlockSize,
    uint PhysicalRuntimeBlockSize,
    uint VirtualBlockSize,
    uint LargeBlockSize,
    uint PhysicalBlockSize,
    long LevelsUnlockMenuOffset,
    int LevelsUnlockItemCount,
    long ItemPointerArrayOffset,
    long CustomMapsItemOffset,
    uint LevelsUnlockRawParent,
    int CustomMapsSerializedSpan,
    string NextMenuName,
    long NextMenuOffset,
    int NextMenuItemCount,
    IReadOnlyList<string> ReportLines);

/// <summary>
/// Stage 3 alpha 4c is deliberately read-only.
///
/// Alpha 4b proved that requiring the immediately following menu to expose a non-zero
/// itemDef_s.parent value was an invalid assumption: stock menu items can legitimately
/// serialize parent as null. Alpha 4c therefore measures the restored hardware-good
/// patch_ui.ff instead of writing another speculative 62 -> 63 file.
///
/// The audit re-proves the alpha-3 levels_unlock structure, reports the exact XFile block
/// sizes, probes every byte alignment (console menu/item records can begin at odd offsets),
/// inventories strong Custom Maps owner identities, inventories repeated pre-linked parent
/// groups in the same address arena, and inspects the next menu's declared inline pointer
/// array plus its first structurally plausible item bodies. No source or output FF is changed.
/// </summary>
public static class T4Stage3RelocationAudit
{
    public static async Task<T4Stage3RelocationAuditResult> CreateReportAsync(
        string inputFastFile,
        string reportFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFastFile))
            throw new FileNotFoundException("Fastfile not found.", inputFastFile);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "stage3-alpha4c-audit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourceZonePath = Path.Combine(tempDir, "source.zone");

        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(inputFastFile, sourceZonePath, cancellationToken);
            if (decomp.Platform != WaWPlatform.Xbox360 || decomp.ZoneHeader.Endianness != Endian.Big)
                throw new InvalidDataException("Stage 3 alpha 4c currently targets the big-endian Xbox 360 WaW patch_ui.ff layout only.");

            var zone = await File.ReadAllBytesAsync(sourceZonePath, cancellationToken);
            var document = T4ZoneParser.ParseZoneBytes(
                inputFastFile,
                zone,
                decomp.Platform,
                decomp.Compression,
                decomp.ZoneHeader.Endianness);

            var trace = T4CustomMapsTrace.Trace(document, zone);
            var target = trace.Markers.SingleOrDefault(m =>
                m.StrongInlineItemCandidate &&
                string.Equals(m.UsermapMode, "solo", StringComparison.Ordinal) &&
                string.Equals(m.ResolvedOwnerMenu, "levels_unlock", StringComparison.Ordinal) &&
                m.LogicalItemIndex == 59 &&
                m.ItemPointerArrayOffset is not null &&
                m.ItemPointerArrayCount == 62 &&
                m.RegionSiblingCount == 62 &&
                m.Item is not null);

            if (target is null || target.Item is null || target.ResolvedOwnerMenuOffset is null)
                throw new InvalidDataException("Could not re-prove the hardware-good 62-item levels_unlock / Custom Maps item #59 structure. Use the restored alpha-3-tested patch_ui.ff.");

            var menu = trace.ProvenMenus.Single(m =>
                string.Equals(m.Name, "levels_unlock", StringComparison.Ordinal) &&
                m.SerializedOffset == target.ResolvedOwnerMenuOffset.Value);
            if (menu.ItemCount != 62)
                throw new InvalidDataException($"Alpha 4c audit requires levels_unlock.itemCount=62, found {menu.ItemCount}.");

            var orderedMenus = trace.ProvenMenus.OrderBy(m => m.SerializedOffset).ToArray();
            var menuIndex = Array.FindIndex(orderedMenus, m => m.SerializedOffset == menu.SerializedOffset);
            if (menuIndex < 0 || menuIndex + 1 >= orderedMenus.Length)
                throw new InvalidDataException("Could not locate a proven menu after levels_unlock.");

            var nextMenu = orderedMenus[menuIndex + 1];
            var nextMenuEnd = menuIndex + 2 < orderedMenus.Length
                ? orderedMenus[menuIndex + 2].SerializedOffset
                : zone.LongLength;

            var itemOffset = checked((int)target.Item.SerializedOffset);
            var nextSiblingOffset = FindNextSiblingOffset(zone, itemOffset, target.Item.RawParentPointer)
                ?? throw new InvalidDataException("Could not locate the structurally valid levels_unlock sibling immediately after Custom Maps #59.");
            var cloneSpan = checked((int)(nextSiblingOffset - target.Item.SerializedOffset));

            var lines = new List<string>();
            void Add(string text = "") => lines.Add(text);

            Add("WaW360Converter — Stage 3.0 alpha 4c relocation audit");
            Add("=========================================================");
            Add("READ-ONLY: this audit does not create or modify a fastfile.");
            Add();
            Add($"Input: {Path.GetFullPath(inputFastFile)}");
            Add($"Compression: {decomp.Compression}");
            Add($"Raw decompressed zone bytes: {zone.Length:N0} (0x{zone.Length:X})");
            Add($"Assets: {document.AssetCount}; script strings: {document.ScriptStringCount}");
            Add();
            Add("XFile header");
            Add($"  size              0x{document.Header.ZoneSize:X8}");
            Add($"  TEMP              0x{document.Header.TempBlockSize:X8}");
            Add($"  RUNTIME           0x{document.Header.RuntimeBlockSize:X8}");
            Add($"  LARGE_RUNTIME     0x{document.Header.LargeRuntimeBlockSize:X8}");
            Add($"  PHYSICAL_RUNTIME  0x{document.Header.PhysicalRuntimeBlockSize:X8}");
            Add($"  VIRTUAL           0x{document.Header.VirtualBlockSize:X8}");
            Add($"  LARGE             0x{document.Header.LargeBlockSize:X8}");
            Add($"  PHYSICAL          0x{document.Header.PhysicalBlockSize:X8}");
            Add();
            Add("Hardware-proven levels_unlock baseline");
            Add($"  menu serialized offset     0x{menu.SerializedOffset:X8}");
            Add($"  itemCount                  {menu.ItemCount}");
            Add($"  item pointer array         0x{target.ItemPointerArrayOffset!.Value:X8} ({target.ItemPointerArrayCount} entries)");
            Add($"  Custom Maps item #59       0x{target.Item.SerializedOffset:X8}");
            Add($"  raw parent identity        0x{target.Item.RawParentPointer:X8}");
            Add($"  complete item #59 span     {cloneSpan:N0} bytes (0x{cloneSpan:X})");
            Add();
            Add("Immediately following proven menu");
            Add($"  name                        {nextMenu.Name}");
            Add($"  serialized offset           0x{nextMenu.SerializedOffset:X8}");
            Add($"  itemCount                   {nextMenu.ItemCount}");
            Add($"  region end                  0x{nextMenuEnd:X8}");

            var pointerRuns = FindDeclaredInlinePointerRuns(zone, nextMenu, nextMenuEnd);
            Add($"  exact {nextMenu.ItemCount}-entry inline pointer runs: {pointerRuns.Count}");
            foreach (var run in pointerRuns.Take(8))
                Add($"    0x{run:X8} .. 0x{run + nextMenu.ItemCount * 4L - 1:X8}");

            if (pointerRuns.Count == 1)
            {
                var arrayEnd = checked((int)(pointerRuns[0] + nextMenu.ItemCount * 4L));
                var probes = ProbeFirstItemsAfterArray(zone, arrayEnd, checked((int)Math.Min(nextMenuEnd, zone.LongLength)));
                Add("  first high-confidence itemDef_s candidate(s) after declared array:");
                if (probes.Count == 0)
                    Add("    none within the first 64 bytes after the array");
                else
                {
                    foreach (var probe in probes)
                        Add($"    item=0x{probe.SerializedOffset:X8} parent=0x{probe.RawParentPointer:X8} type={probe.Type} checks={probe.StructuralChecksPassed}/{probe.StructuralChecksTotal}");
                }
            }
            Add();

            Add("Strong Custom Maps markers / proven owner groups");
            foreach (var marker in trace.Markers.Where(m => m.StrongInlineItemCandidate && m.Item is not null).OrderBy(m => m.MarkerOffset))
            {
                Add($"  mode={marker.UsermapMode ?? "-"} item=0x{marker.Item!.SerializedOffset:X8} parent=0x{marker.Item.RawParentPointer:X8} " +
                    $"owner={marker.ResolvedOwnerMenu ?? "?"} ownerOff={(marker.ResolvedOwnerMenuOffset is null ? "?" : $"0x{marker.ResolvedOwnerMenuOffset.Value:X8}")} " +
                    $"regionSiblings={marker.RegionSiblingCount} declared={marker.ResolvedOwnerDeclaredItemCount?.ToString() ?? "?"}");
            }
            Add();

            var arenaHighByte = target.Item.RawParentPointer & 0xFF000000u;
            var arenaItems = ScanArenaParentCandidates(zone, document, arenaHighByte);
            var arenaGroups = arenaItems
                .GroupBy(i => i.RawParentPointer)
                .Select(g => new
                {
                    Parent = g.Key,
                    Items = g.GroupBy(i => i.SerializedOffset).Select(x => x.First()).OrderBy(i => i.SerializedOffset).ToArray()
                })
                .Where(g => g.Items.Length >= 2)
                .OrderBy(g => g.Items[0].SerializedOffset)
                .ToArray();

            Add($"Byte-aligned pre-linked parent inventory (same 0x{arenaHighByte >> 24:X2}xxxxxx arena; repeated groups only)");
            Add($"  very-high-confidence item bodies in arena: {arenaItems.Select(i => i.SerializedOffset).Distinct().Count():N0}");
            Add($"  repeated parent groups: {arenaGroups.Length:N0}");
            foreach (var group in arenaGroups)
            {
                Add($"  parent=0x{group.Parent:X8} siblings={group.Items.Length,3} first=0x{group.Items[0].SerializedOffset:X8} last=0x{group.Items[^1].SerializedOffset:X8}");
            }
            Add();

            var virtualPointerStats = CountPackedVirtualCandidates(zone, document.Header.VirtualBlockSize, checked((int)nextSiblingOffset));
            Add("Packed VIRTUAL pointer inventory (diagnostic only; no relocation performed)");
            Add($"  candidates before insertion point: {virtualPointerStats.Before:N0}");
            Add($"  candidates at/after insertion point: {virtualPointerStats.After:N0}");
            Add($"  total: {virtualPointerStats.Before + virtualPointerStats.After:N0}");
            Add();
            Add("Alpha 4c conclusion");
            Add("  No FF was written. This report is the evidence input for the alpha-4d block-aware 62 -> 63 writer.");
            Add("  In particular, alpha 4d must not require cod5_levels_test to have a non-zero parent, and must not scan only even offsets.");

            var reportDirectory = Path.GetDirectoryName(Path.GetFullPath(reportFile));
            if (!string.IsNullOrWhiteSpace(reportDirectory))
                Directory.CreateDirectory(reportDirectory);
            await File.WriteAllLinesAsync(reportFile, lines, cancellationToken);

            return new T4Stage3RelocationAuditResult(
                inputFastFile,
                reportFile,
                zone.Length,
                document.Header.ZoneSize,
                document.Header.TempBlockSize,
                document.Header.RuntimeBlockSize,
                document.Header.LargeRuntimeBlockSize,
                document.Header.PhysicalRuntimeBlockSize,
                document.Header.VirtualBlockSize,
                document.Header.LargeBlockSize,
                document.Header.PhysicalBlockSize,
                menu.SerializedOffset,
                menu.ItemCount,
                target.ItemPointerArrayOffset.Value,
                target.Item.SerializedOffset,
                target.Item.RawParentPointer,
                cloneSpan,
                nextMenu.Name,
                nextMenu.SerializedOffset,
                nextMenu.ItemCount,
                lines);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static List<T4ConsoleItemDefCandidate> ScanArenaParentCandidates(byte[] zone, T4ZoneDocument document, uint arenaHighByte)
    {
        var result = new Dictionary<long, T4ConsoleItemDefCandidate>();
        var start = checked((int)Math.Max(0, document.AssetTableOffset + document.AssetCount * 8L));
        var end = zone.Length - T4ConsoleItemDefProbe.FixedSize;

        // IMPORTANT: byte-by-byte. Proven Custom Maps itself begins at an odd offset.
        for (var offset = start; offset <= end; offset++)
        {
            var parent = ReadU32Big(zone, offset + 244);
            if (parent is 0 or T4ZonePointer.Following or T4ZonePointer.Insert)
                continue;
            if ((parent & 0xFF000000u) != arenaHighByte)
                continue;

            var type = ReadI32Big(zone, offset + 192);
            if (type is < 0 or > 0x13)
                continue;
            if (ReadI32Big(zone, offset + 20) is < 0 or > 7 || ReadI32Big(zone, offset + 24) is < 0 or > 7)
                continue;

            if (!T4ConsoleItemDefProbe.TryRead(zone, offset, Endian.Big, out var candidate) || candidate is null)
                continue;
            if (candidate.StructuralChecksPassed < candidate.StructuralChecksTotal * 0.98)
                continue;

            result[candidate.SerializedOffset] = candidate;
        }

        return result.Values.OrderBy(i => i.SerializedOffset).ToList();
    }

    private static IReadOnlyList<long> FindDeclaredInlinePointerRuns(byte[] zone, T4MenuStructureCandidate menu, long regionEnd)
    {
        var result = new List<long>();
        if (menu.ItemCount <= 0)
            return result;

        var start = checked((int)Math.Max(0, menu.SerializedOffset + 312));
        var end = checked((int)Math.Min(regionEnd, zone.LongLength));
        var bytesNeeded = checked(menu.ItemCount * 4);
        if (end - start < bytesNeeded)
            return result;

        for (var offset = start; offset <= end - bytesNeeded; offset++)
        {
            var allInline = true;
            for (var i = 0; i < menu.ItemCount; i++)
            {
                var raw = ReadU32Big(zone, offset + i * 4);
                if (raw is not (T4ZonePointer.Following or T4ZonePointer.Insert))
                {
                    allInline = false;
                    break;
                }
            }
            if (!allInline)
                continue;

            // Avoid shifted starts inside one long FF/FE sentinel run. Do not reject the
            // declared run merely because the first item body also begins with an inline pointer.
            if (offset > start && zone[offset - 1] is 0xFF or 0xFE)
                continue;

            result.Add(offset);
        }

        return result;
    }

    private static IReadOnlyList<T4ConsoleItemDefCandidate> ProbeFirstItemsAfterArray(byte[] zone, int arrayEnd, int regionEnd)
    {
        var result = new List<T4ConsoleItemDefCandidate>();
        var end = Math.Min(regionEnd - T4ConsoleItemDefProbe.FixedSize, arrayEnd + 64);
        for (var offset = arrayEnd; offset <= end; offset++)
        {
            if (!T4ConsoleItemDefProbe.TryRead(zone, offset, Endian.Big, out var candidate) || candidate is null)
                continue;
            if (candidate.StructuralChecksPassed < candidate.StructuralChecksTotal * 0.98)
                continue;
            result.Add(candidate);
        }
        return result;
    }

    private static (int Before, int After) CountPackedVirtualCandidates(byte[] zone, uint virtualBlockSize, int insertionPoint)
    {
        var before = 0;
        var after = 0;
        // Diagnostic inventory only. Use every byte alignment so odd console structures are visible.
        for (var offset = 0; offset <= zone.Length - 4; offset++)
        {
            var raw = ReadU32Big(zone, offset);
            if (raw is 0 or T4ZonePointer.Following or T4ZonePointer.Insert)
                continue;
            var decoded = T4ZonePointer.Decode(raw);
            if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block != T4ZoneBlock.Virtual || decoded.Offset >= virtualBlockSize)
                continue;
            if (offset < insertionPoint) before++; else after++;
        }
        return (before, after);
    }

    private static long? FindNextSiblingOffset(byte[] zone, int currentItemOffset, uint parentPointer)
    {
        Span<byte> pattern = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(pattern, parentPointer);

        var start = checked(currentItemOffset + T4ConsoleItemDefProbe.FixedSize);
        while (start <= zone.Length - 4)
        {
            var relative = zone.AsSpan(start).IndexOf(pattern);
            if (relative < 0)
                return null;

            var parentFieldOffset = start + relative;
            var candidateOffset = parentFieldOffset - 244;
            if (candidateOffset > currentItemOffset &&
                T4ConsoleItemDefProbe.TryRead(zone, candidateOffset, Endian.Big, out var candidate) &&
                candidate is not null &&
                candidate.RawParentPointer == parentPointer &&
                candidate.StructuralChecksPassed >= candidate.StructuralChecksTotal * 0.98)
            {
                return candidate.SerializedOffset;
            }

            start = parentFieldOffset + 1;
        }

        return null;
    }

    private static uint ReadU32Big(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static int ReadI32Big(byte[] data, int offset) =>
        unchecked((int)ReadU32Big(data, offset));
}
