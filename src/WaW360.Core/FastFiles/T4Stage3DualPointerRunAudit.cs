using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4Stage3DualPointerRunAuditResult(
    string InputFastFile,
    string ReportFile,
    long LevelsUnlockOffset,
    long ProvenPointerArrayOffset,
    long SecondPointerRunOffset,
    int ItemCount,
    long CustomMapsItemOffset,
    long InsertionPoint,
    int CloneSpan,
    int SecondRunAnonymousMenuCandidates,
    int SecondRunNearbyCountFields,
    int SecondRunFollowingHighConfidenceItems,
    IReadOnlyList<string> Lines);

/// <summary>
/// Stage 3 alpha 4e read-only dual-pointer-run audit.
///
/// Alpha 4d exposed two exact 62-entry FOLLOWING-pointer runs inside the serialized
/// levels_unlock region. Alpha 2 only expanded the first, proven menuDef_t.items array.
/// Before attempting any more zone growth, this audit classifies the second run and asks
/// whether it belongs to another menu-like object/count relationship or to unrelated nested
/// serialization. No fastfile is ever written.
/// </summary>
public static class T4Stage3DualPointerRunAudit
{
    private const int ConsoleMenuDefSize = 312;

    private sealed record AnonymousMenuCandidate(
        int Offset,
        int ItemCount,
        uint RawNamePointer,
        uint RawItemsPointer,
        int GapToRun,
        int Score);

    public static async Task<T4Stage3DualPointerRunAuditResult> CreateAsync(
        string inputFastFile,
        string reportFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFastFile))
            throw new FileNotFoundException("Fastfile not found.", inputFastFile);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "stage3-alpha4e-audit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "source.zone");

        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(inputFastFile, zonePath, cancellationToken);
            if (decomp.Platform != WaWPlatform.Xbox360 || decomp.ZoneHeader.Endianness != Endian.Big)
                throw new InvalidDataException("Stage 3 alpha 4e currently targets the big-endian Xbox 360 WaW patch_ui.ff layout only.");

            var zone = await File.ReadAllBytesAsync(zonePath, cancellationToken);
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
                m.Item is not null &&
                m.ResolvedOwnerMenuOffset is not null);

            if (target is null || target.Item is null || target.ResolvedOwnerMenuOffset is null || target.ItemPointerArrayOffset is null)
                throw new InvalidDataException("Could not re-prove the hardware-good 62-item levels_unlock / Custom Maps #59 baseline.");

            var menu = trace.ProvenMenus.Single(m =>
                string.Equals(m.Name, "levels_unlock", StringComparison.Ordinal) &&
                m.SerializedOffset == target.ResolvedOwnerMenuOffset.Value);
            if (menu.ItemCount != 62)
                throw new InvalidDataException($"Alpha 4e requires levels_unlock.itemCount=62, found {menu.ItemCount}.");

            var itemOffset = checked((int)target.Item.SerializedOffset);
            var nextSiblingOffset = FindNextSiblingOffset(zone, itemOffset, target.Item.RawParentPointer)
                ?? throw new InvalidDataException("Could not locate old item #60 after Custom Maps #59.");
            var insertionPoint = checked((int)nextSiblingOffset);
            var cloneSpan = insertionPoint - itemOffset;
            if (cloneSpan <= T4ConsoleItemDefProbe.FixedSize || cloneSpan > 16 * 1024)
                throw new InvalidDataException($"Unexpected Custom Maps serialized span: {cloneSpan:N0} bytes.");

            var regionEnd = NextMenuBoundary(trace.ProvenMenus, menu.SerializedOffset, zone.Length);
            var runs = FindDeclaredInlinePointerRuns(zone, menu, regionEnd);
            var provenRun = checked((int)target.ItemPointerArrayOffset.Value);
            if (!runs.Contains(provenRun))
                throw new InvalidDataException("The Stage-2-proven levels_unlock item array is no longer present in the exact pointer-run inventory.");

            var otherRuns = runs.Where(x => x != provenRun).OrderBy(x => x).ToArray();
            if (otherRuns.Length != 1)
                throw new InvalidDataException($"Alpha 4e expected exactly one additional 62-entry run besides the proven array, found {otherRuns.Length}. No conclusion was guessed.");
            var secondRun = otherRuns[0];

            var anonymousMenus = FindAnonymousMenuCandidates(zone, secondRun, menu.ItemCount);
            var nearbyCounts = FindNearbyCountFields(zone, secondRun, menu.ItemCount);
            var followingItems = FindFollowingHighConfidenceItems(zone, secondRun + menu.ItemCount * 4, Math.Min(regionEnd, secondRun + menu.ItemCount * 4 + 0x3000));
            var maximalRun = FindMaximalFfRun(zone, secondRun);

            var lines = new List<string>();
            void Add(string value = "") => lines.Add(value);

            Add("WaW360Converter — Stage 3.0 alpha 4e dual 62-pointer-run audit");
            Add("=================================================================");
            Add("READ-ONLY: this audit does not create, repack, or modify a fastfile.");
            Add();
            Add($"Input: {inputFastFile}");
            Add($"Compression: {decomp.Compression}");
            Add($"Raw decompressed zone bytes: {zone.Length:N0} (0x{zone.Length:X})");
            Add($"VIRTUAL block: 0x{document.Header.VirtualBlockSize:X8}; TEMP block: 0x{document.Header.TempBlockSize:X8}");
            Add();
            Add("Hardware-proven levels_unlock baseline");
            Add($"  menu                         0x{menu.SerializedOffset:X8}");
            Add($"  itemCount                    {menu.ItemCount}");
            Add($"  proven item pointer array    0x{provenRun:X8} .. 0x{provenRun + menu.ItemCount * 4 - 1:X8}");
            Add($"  Custom Maps #59              0x{itemOffset:X8}");
            Add($"  old #60 / insertion point    0x{insertionPoint:X8}");
            Add($"  complete #59 clone span      0x{cloneSpan:X} ({cloneSpan:N0} bytes)");
            Add();
            Add("Exact declared-count FOLLOWING/INSERT pointer runs inside levels_unlock serialized region");
            foreach (var run in runs)
                Add($"  0x{run:X8} .. 0x{run + menu.ItemCount * 4 - 1:X8}{(run == provenRun ? "  [PROVEN menuDef_t.items]" : "  [SECOND RUN]")}");
            Add();
            Add("Second-run byte shape");
            Add($"  second run                   0x{secondRun:X8}");
            Add($"  distance from proven array   0x{secondRun - provenRun:X}");
            Add($"  maximal contiguous 0xFF run  0x{maximalRun.Start:X8} .. 0x{maximalRun.EndExclusive - 1:X8} ({maximalRun.Length:N0} bytes)");
            Add($"  exact pointer bytes           {menu.ItemCount * 4:N0}");
            Add($"  preceding 32 bytes            {Hex(zone, Math.Max(0, secondRun - 32), Math.Min(32, secondRun))}");
            Add($"  following 32 bytes            {Hex(zone, secondRun + menu.ItemCount * 4, 32)}");
            Add();
            Add($"Anonymous/pre-linked console menuDef_t candidates near second run: {anonymousMenus.Count}");
            foreach (var c in anonymousMenus.Take(32))
            {
                Add($"  menu? @0x{c.Offset:X8} itemCount={c.ItemCount} nameRaw=0x{c.RawNamePointer:X8} itemsRaw=0x{c.RawItemsPointer:X8} fixedEnd->run=0x{c.GapToRun:X} score={c.Score}/18");
            }
            Add();
            Add($"Nearby 32-bit fields equal to itemCount=62 within 0x800 bytes before second run: {nearbyCounts.Count}");
            foreach (var off in nearbyCounts.Take(64))
                Add($"  count=62 @0x{off:X8} (run-countField delta=0x{secondRun - off:X})");
            Add();
            Add($"Very-high-confidence itemDef_s starts in first 0x3000 bytes after second run: {followingItems.Count}");
            foreach (var i in followingItems.Take(64))
                Add($"  item @0x{i.SerializedOffset:X8} type={i.Type} parent=0x{i.RawParentPointer:X8} textRaw=0x{i.RawTextPointer:X8} actionRaw=0x{i.RawActionPointer:X8}");
            Add();

            var hasMenuOwner = anonymousMenus.Any(c => c.GapToRun is >= 0 and <= 0x800 && c.Score >= 15);
            Add("Alpha 4e conclusion");
            if (hasMenuOwner)
            {
                Add("  The second 62-pointer run has a strong nearby menuDef_t/count owner candidate.");
                Add("  Alpha 2 must NOT blindly expand only the first run; the second owner relationship must be understood before any writer is enabled.");
            }
            else
            {
                Add("  No strong nearby menuDef_t owner was proven for the second run.");
                Add("  The second run is therefore NOT automatically treated as a second levels_unlock item array. Alpha 4e refuses to patch it by pattern alone.");
            }
            Add("  No FF was written. This audit only classifies the unexplained second 62-slot structure exposed by alpha 4d.");

            var reportDirectory = Path.GetDirectoryName(Path.GetFullPath(reportFile));
            if (!string.IsNullOrWhiteSpace(reportDirectory))
                Directory.CreateDirectory(reportDirectory);
            await File.WriteAllLinesAsync(reportFile, lines, cancellationToken);

            return new T4Stage3DualPointerRunAuditResult(
                inputFastFile,
                reportFile,
                menu.SerializedOffset,
                provenRun,
                secondRun,
                menu.ItemCount,
                itemOffset,
                insertionPoint,
                cloneSpan,
                anonymousMenus.Count,
                nearbyCounts.Count,
                followingItems.Count,
                lines);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static List<AnonymousMenuCandidate> FindAnonymousMenuCandidates(byte[] zone, int runOffset, int itemCount)
    {
        var result = new List<AnonymousMenuCandidate>();
        var start = Math.Max(0, runOffset - 0x1800);
        var end = runOffset - ConsoleMenuDefSize;
        for (var offset = start; offset <= end; offset++)
        {
            if (ReadI32Big(zone, offset + 176) != itemCount)
                continue;
            var itemsRaw = ReadU32Big(zone, offset + 308);
            if (itemsRaw is not (T4ZonePointer.Following or T4ZonePointer.Insert))
                continue;

            var score = ScoreConsoleMenuFixed(zone, offset);
            if (score < 13)
                continue;

            var gap = runOffset - (offset + ConsoleMenuDefSize);
            if (gap < 0 || gap > 0x1800)
                continue;

            result.Add(new AnonymousMenuCandidate(offset, itemCount, ReadU32Big(zone, offset), itemsRaw, gap, score));
        }
        return result.OrderByDescending(c => c.Score).ThenBy(c => c.GapToRun).ThenBy(c => c.Offset).ToList();
    }

    private static int ScoreConsoleMenuFixed(byte[] z, int o)
    {
        if (o < 0 || o + ConsoleMenuDefSize > z.Length)
            return 0;
        var s = 0;
        bool Ptr(int x) => IsSerializedPointer(ReadU32Big(z, o + x));
        bool F(int x, float lo, float hi)
        {
            var v = ReadF32Big(z, o + x);
            return !float.IsNaN(v) && !float.IsInfinity(v) && v >= lo && v <= hi;
        }
        if (Ptr(0)) s++;
        if (F(4, -1000, 1000) && F(8, -1000, 1000) && F(12, 1, 2000) && F(16, 1, 2000)) s++;
        if (ReadI32Big(z, o + 20) is >= 0 and <= 7 && ReadI32Big(z, o + 24) is >= 0 and <= 7) s++;
        if (Ptr(52)) s++;
        if (F(72, 0, 10)) s++;
        if (Ptr(164)) s++;
        if (Ptr(168)) s++;
        if (ReadI32Big(z, o + 172) is 0 or 1) s++;
        if (ReadI32Big(z, o + 176) is >= 1 and <= 500) s++;
        if (ReadI32Big(z, o + 180) is >= -1 and <= 20) s++;
        if (Ptr(220) && Ptr(224) && Ptr(228) && Ptr(232)) s++;
        if (Ptr(236) && Ptr(244) && Ptr(248) && Ptr(252)) s++;
        if (Ptr(296) && Ptr(304) && Ptr(308)) s++;
        if (ReadI32Big(z, o + 240) is >= 0 and <= 1000) s++;
        if (ReadI32Big(z, o + 292) is >= 0 and <= 1000) s++;
        if (ReadI32Big(z, o + 300) is >= 0 and <= 1000) s++;
        var colorsOkay = true;
        foreach (var b in new[] { 100, 116, 132, 148, 260, 276 })
            for (var c = 0; c < 4; c++)
                colorsOkay &= F(b + c * 4, -0.01f, 1.5f);
        if (colorsOkay) s++;
        var cursorsOkay = true;
        foreach (var x in new[] { 184, 188, 192, 196 })
            cursorsOkay &= ReadI32Big(z, o + x) is >= -1 and <= 500;
        if (cursorsOkay) s++;
        return s;
    }

    private static List<int> FindNearbyCountFields(byte[] zone, int runOffset, int value)
    {
        var result = new List<int>();
        var start = Math.Max(0, runOffset - 0x800);
        for (var o = start; o <= runOffset - 4; o++)
            if (ReadI32Big(zone, o) == value)
                result.Add(o);
        return result;
    }

    private static List<T4ConsoleItemDefCandidate> FindFollowingHighConfidenceItems(byte[] zone, int start, int end)
    {
        var result = new List<T4ConsoleItemDefCandidate>();
        start = Math.Max(0, start);
        end = Math.Min(zone.Length, end);
        for (var o = start; o <= end - T4ConsoleItemDefProbe.FixedSize; o++)
        {
            if (!T4ConsoleItemDefProbe.TryRead(zone, o, Endian.Big, out var item) || item is null)
                continue;
            if (item.StructuralChecksPassed < item.StructuralChecksTotal * 0.98)
                continue;
            result.Add(item);
            o += T4ConsoleItemDefProbe.FixedSize - 1;
        }
        return result;
    }

    private static (int Start, int EndExclusive, int Length) FindMaximalFfRun(byte[] zone, int insideOffset)
    {
        var start = insideOffset;
        while (start > 0 && zone[start - 1] == 0xFF) start--;
        var end = insideOffset;
        while (end < zone.Length && zone[end] == 0xFF) end++;
        return (start, end, end - start);
    }

    private static string Hex(byte[] data, int offset, int count)
    {
        if (offset < 0 || offset >= data.Length || count <= 0)
            return "";
        count = Math.Min(count, data.Length - offset);
        return Convert.ToHexString(data.AsSpan(offset, count));
    }

    private static IReadOnlyList<int> FindDeclaredInlinePointerRuns(byte[] zone, T4MenuStructureCandidate menu, int regionEnd)
    {
        var result = new List<int>();
        var start = checked((int)Math.Max(0, menu.SerializedOffset + ConsoleMenuDefSize));
        var end = Math.Min(regionEnd, zone.Length);
        var bytesNeeded = checked(menu.ItemCount * 4);
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
            if (offset > start && zone[offset - 1] is 0xFF or 0xFE)
                continue;
            result.Add(offset);
        }
        return result;
    }

    private static long? FindNextSiblingOffset(byte[] zone, int currentItemOffset, uint parentPointer)
    {
        Span<byte> pattern = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(pattern, parentPointer);
        var start = currentItemOffset + T4ConsoleItemDefProbe.FixedSize;
        while (start <= zone.Length - 4)
        {
            var relative = zone.AsSpan(start).IndexOf(pattern);
            if (relative < 0) return null;
            var parentFieldOffset = start + relative;
            var candidateOffset = parentFieldOffset - 244;
            if (candidateOffset > currentItemOffset &&
                T4ConsoleItemDefProbe.TryRead(zone, candidateOffset, Endian.Big, out var candidate) &&
                candidate is not null && candidate.RawParentPointer == parentPointer &&
                candidate.StructuralChecksPassed >= candidate.StructuralChecksTotal * 0.98)
                return candidate.SerializedOffset;
            start = parentFieldOffset + 1;
        }
        return null;
    }

    private static int NextMenuBoundary(IReadOnlyList<T4MenuStructureCandidate> menus, long currentOffset, int fallback)
    {
        foreach (var m in menus.Where(m => m.SerializedOffset > currentOffset).OrderBy(m => m.SerializedOffset))
            return checked((int)m.SerializedOffset);
        return fallback;
    }

    private static bool IsSerializedPointer(uint value)
    {
        if (value is 0 or T4ZonePointer.Following or T4ZonePointer.Insert)
            return true;
        var d = T4ZonePointer.Decode(value);
        return d.Kind == T4ZonePointerKind.Offset && d.Block is not null;
    }

    private static uint ReadU32Big(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static int ReadI32Big(byte[] data, int offset) => unchecked((int)ReadU32Big(data, offset));

    private static float ReadF32Big(byte[] data, int offset) =>
        BitConverter.Int32BitsToSingle(ReadI32Big(data, offset));
}
