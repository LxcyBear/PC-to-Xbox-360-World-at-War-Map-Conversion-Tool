using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4Stage3VirtualPointerAuditResult(
    string InputFastFile,
    string ReportFile,
    int RawZoneBytes,
    uint VirtualBlockSize,
    long LevelsUnlockOffset,
    long PointerArrayOffset,
    long CustomMapsItemOffset,
    long InsertionPoint,
    int CloneSpan,
    int ProvenMenuCount,
    int HighConfidenceItemCount,
    int KnownVirtualPointerFieldCount,
    int KnownVirtualPointerFieldsBeforeInsertion,
    int KnownVirtualPointerFieldsAfterInsertion,
    uint? MaxKnownTargetBeforeInsertion,
    uint? MinKnownTargetAfterInsertion,
    IReadOnlyList<string> Lines);

/// <summary>
/// Stage 3 alpha 4d read-only VIRTUAL pointer-map audit.
///
/// Alpha 4c proved two important facts on the hardware-good patch_ui.ff:
///  - TEMP is only 0x138 (the fixed 312-byte console menuDef_t scratch object), while the
///    nested menu/item data lives in the normal VIRTUAL arena;
///  - itemDef_s.parent is not a normal packed zone pointer and must not be relocated by a
///    generic block-offset pass.
///
/// This audit therefore does NOT write a fastfile. It inventories packed VIRTUAL offsets only
/// at pointer fields whose type is known from the console menuDef_t/itemDef_s layouts, plus the
/// XAsset/script-string pointer tables. It deliberately excludes itemDef_s.parent (+244).
/// The resulting source-position/target-offset map brackets the VIRTUAL cursor at the intended
/// #59 -> #60 insertion point so the following writer can relocate targets, not random 4-byte
/// values that merely resemble packed pointers.
/// </summary>
public static class T4Stage3VirtualPointerAudit
{
    private sealed record KnownPointer(
        long SourceOffset,
        string Owner,
        string Field,
        uint Raw,
        uint TargetOffset,
        bool SourceBeforeInsertion);

    private static readonly (int Offset, string Name)[] MenuPointerFields =
    {
        (0, "window.name"),
        (52, "window.group"),
        (164, "window.background"),
        (168, "font"),
        (220, "onOpen"),
        (224, "onFocus"),
        (228, "onClose"),
        (232, "onESC"),
        (236, "onKey"),
        (244, "visibleExp.entries"),
        (248, "allowedBinding"),
        (252, "soundName"),
        (296, "rectXExp.entries"),
        (304, "rectYExp.entries"),
        (308, "items")
    };

    // itemDef_s.parent at +244 is intentionally NOT in this table. OpenAssetTools' T4 schema
    // marks it `set condition parent never`, so treating it as a packed block offset was the
    // incorrect alpha-4/4b theory.
    private static readonly (int Offset, string Name)[] ItemPointerFields =
    {
        (0, "window.name"),
        (52, "window.group"),
        (164, "window.background"),
        (236, "text"),
        (248, "mouseEnterText"),
        (252, "mouseExitText"),
        (256, "mouseEnter"),
        (260, "mouseExit"),
        (264, "action"),
        (268, "onAccept"),
        (272, "onFocus"),
        (276, "leaveFocus"),
        (280, "dvar"),
        (284, "dvarTest"),
        (288, "onListboxSelectionChange"),
        (292, "onKey"),
        (296, "enableDvar"),
        (304, "focusSound"),
        (328, "typeData")
    };

    public static async Task<T4Stage3VirtualPointerAuditResult> CreateAsync(
        string inputFastFile,
        string reportFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFastFile))
            throw new FileNotFoundException("Fastfile not found.", inputFastFile);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "stage3-alpha4d-audit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "source.zone");

        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(inputFastFile, zonePath, cancellationToken);
            if (decomp.Platform != WaWPlatform.Xbox360 || decomp.ZoneHeader.Endianness != Endian.Big)
                throw new InvalidDataException("Stage 3 alpha 4d currently targets the big-endian Xbox 360 WaW patch_ui.ff layout only.");

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

            if (target is null || target.Item is null || target.ResolvedOwnerMenuOffset is null)
                throw new InvalidDataException("Could not re-prove the hardware-good 62-item levels_unlock / Custom Maps item #59 baseline.");

            var menu = trace.ProvenMenus.Single(m =>
                string.Equals(m.Name, "levels_unlock", StringComparison.Ordinal) &&
                m.SerializedOffset == target.ResolvedOwnerMenuOffset.Value);
            if (menu.ItemCount != 62)
                throw new InvalidDataException($"Alpha 4d requires levels_unlock.itemCount=62, found {menu.ItemCount}.");

            var itemOffset = checked((int)target.Item.SerializedOffset);
            var nextSiblingOffset = FindNextSiblingOffset(zone, itemOffset, target.Item.RawParentPointer)
                ?? throw new InvalidDataException("Could not locate the structurally valid sibling immediately after Custom Maps item #59.");
            var insertionPoint = checked((int)nextSiblingOffset);
            var cloneSpan = insertionPoint - itemOffset;
            if (cloneSpan <= T4ConsoleItemDefProbe.FixedSize || cloneSpan > 16 * 1024)
                throw new InvalidDataException($"Unexpected complete Custom Maps item span: {cloneSpan:N0} bytes.");

            var pointerRuns = FindDeclaredInlinePointerRuns(
                zone,
                menu,
                NextMenuBoundary(trace.ProvenMenus, menu.SerializedOffset, zone.Length));

            var menuPointers = new List<KnownPointer>();
            foreach (var provenMenu in trace.ProvenMenus.OrderBy(m => m.SerializedOffset))
            {
                foreach (var field in MenuPointerFields)
                    AddKnownVirtualPointer(zone, document.Header.VirtualBlockSize, insertionPoint, provenMenu.SerializedOffset + field.Offset,
                        $"menu:{provenMenu.Name}@0x{provenMenu.SerializedOffset:X}", field.Name, menuPointers);
            }

            // Script-string pointer table and XAsset header/reference table are exact typed pointer locations.
            for (var i = 0; i < document.ScriptStringCount; i++)
            {
                var fieldOffset = document.ScriptStringPointerTableOffset + i * 4L;
                AddKnownVirtualPointer(zone, document.Header.VirtualBlockSize, insertionPoint, fieldOffset,
                    $"scriptString[{i}]", "value", menuPointers);
            }
            for (var i = 0; i < document.AssetCount; i++)
            {
                var fieldOffset = document.AssetTableOffset + i * 8L + 4;
                AddKnownVirtualPointer(zone, document.Header.VirtualBlockSize, insertionPoint, fieldOffset,
                    $"xasset[{i}]/{document.Assets[i].TypeName}", "header", menuPointers);
            }

            var items = ScanVeryHighConfidenceItems(zone, document);
            foreach (var item in items)
            {
                var owner = $"item@0x{item.SerializedOffset:X}/type={item.Type}";
                foreach (var field in ItemPointerFields)
                    AddKnownVirtualPointer(zone, document.Header.VirtualBlockSize, insertionPoint, item.SerializedOffset + field.Offset,
                        owner, field.Name, menuPointers);

                for (var statementOffset = 336; statementOffset < 400; statementOffset += 8)
                    AddKnownVirtualPointer(zone, document.Header.VirtualBlockSize, insertionPoint, item.SerializedOffset + statementOffset + 4,
                        owner, $"statement[{(statementOffset - 336) / 8}].entries", menuPointers);
            }

            // Deduplicate exact source fields: a menu/item false-overlap should never inflate the map.
            var known = menuPointers
                .GroupBy(p => p.SourceOffset)
                .Select(g => g.OrderBy(p => p.Owner, StringComparer.Ordinal).First())
                .OrderBy(p => p.SourceOffset)
                .ToArray();

            var before = known.Where(p => p.SourceBeforeInsertion).ToArray();
            var after = known.Where(p => !p.SourceBeforeInsertion).ToArray();
            uint? maxBefore = before.Length == 0 ? null : before.Max(p => p.TargetOffset);
            uint? minAfter = after.Length == 0 ? null : after.Min(p => p.TargetOffset);

            var lines = new List<string>();
            void Add(string value = "") => lines.Add(value);

            Add("WaW360Converter — Stage 3.0 alpha 4d VIRTUAL pointer-map audit");
            Add("================================================================");
            Add("READ-ONLY: this audit does not create or modify a fastfile.");
            Add();
            Add($"Input: {inputFastFile}");
            Add($"Compression: {decomp.Compression}");
            Add($"Raw decompressed zone bytes: {zone.Length:N0} (0x{zone.Length:X})");
            Add($"VIRTUAL block size: 0x{document.Header.VirtualBlockSize:X8}");
            Add($"TEMP block size:    0x{document.Header.TempBlockSize:X8}");
            Add();
            Add("Hardware-proven insertion baseline");
            Add($"  levels_unlock menu         0x{menu.SerializedOffset:X8}");
            Add($"  itemCount                  {menu.ItemCount}");
            Add($"  menuDef_t.items raw        0x{menu.RawItemsPointer:X8} ({T4ZonePointer.Describe(menu.RawItemsPointer)})");
            Add($"  proven pointer array       0x{target.ItemPointerArrayOffset!.Value:X8}");
            Add($"  exact 62-sentinel runs     {pointerRuns.Count}");
            foreach (var run in pointerRuns.Take(16))
                Add($"    0x{run:X8} .. 0x{run + 62L * 4L - 1:X8}");
            Add($"  Custom Maps #59            0x{target.Item.SerializedOffset:X8}");
            Add($"  insertion point / old #60  0x{insertionPoint:X8}");
            Add($"  complete clone span        {cloneSpan:N0} bytes (0x{cloneSpan:X})");
            Add();
            Add("Known-structure packed VIRTUAL pointer fields");
            Add($"  proven menuDef_t structs   {trace.ProvenMenus.Count:N0}");
            Add($"  very-high-confidence items {items.Count:N0}");
            Add($"  known VIRTUAL fields total {known.Length:N0}");
            Add($"  source fields before insert {before.Length:N0}");
            Add($"  source fields at/after insert {after.Length:N0}");
            Add($"  max known target referenced before insert: {(maxBefore is null ? "none" : $"0x{maxBefore.Value:X8}")}");
            Add($"  min known target referenced at/after insert: {(minAfter is null ? "none" : $"0x{minAfter.Value:X8}")}");
            if (maxBefore is not null)
            {
                var afterAbove = after.Where(p => p.TargetOffset > maxBefore.Value).OrderBy(p => p.TargetOffset).ToArray();
                Add($"  post-insert known fields targeting above max-before: {afterAbove.Length:N0}");
                foreach (var p in afterAbove.Take(64))
                    Add($"    src=0x{p.SourceOffset:X8} target=VIRTUAL+0x{p.TargetOffset:X8} raw=0x{p.Raw:X8} {p.Owner}.{p.Field}");
            }
            Add();

            Add("Known pointer fields nearest the physical insertion point (±0x4000)");
            foreach (var p in known.Where(p => Math.Abs(p.SourceOffset - insertionPoint) <= 0x4000).OrderBy(p => p.SourceOffset))
                Add($"  src=0x{p.SourceOffset:X8} {(p.SourceBeforeInsertion ? "BEFORE" : "AFTER ")} target=VIRTUAL+0x{p.TargetOffset:X8} raw=0x{p.Raw:X8} {p.Owner}.{p.Field}");
            Add();

            Add("Target-offset histogram from typed fields (0x10000-byte VIRTUAL buckets)");
            foreach (var bucket in known.GroupBy(p => p.TargetOffset >> 16).OrderBy(g => g.Key))
            {
                var b = bucket.Count(p => p.SourceBeforeInsertion);
                var a = bucket.Count() - b;
                Add($"  VIRTUAL+0x{bucket.Key << 16:X8}..0x{((bucket.Key + 1) << 16) - 1:X8}: before={b,4} after={a,4} total={bucket.Count(),4}");
            }
            Add();

            Add("Full typed VIRTUAL pointer map");
            foreach (var p in known)
                Add($"  src=0x{p.SourceOffset:X8} {(p.SourceBeforeInsertion ? "BEFORE" : "AFTER ")} target=VIRTUAL+0x{p.TargetOffset:X8} raw=0x{p.Raw:X8} {p.Owner}.{p.Field}");
            Add();
            Add("Alpha 4d conclusion");
            Add("  No FF was written. This report identifies real packed VIRTUAL pointers only at known typed fields.");
            Add("  itemDef_s.parent is deliberately excluded. The next writer must relocate by decoded target offset, not by source-file position and not by parent identity.");

            var reportDirectory = Path.GetDirectoryName(Path.GetFullPath(reportFile));
            if (!string.IsNullOrWhiteSpace(reportDirectory))
                Directory.CreateDirectory(reportDirectory);
            await File.WriteAllLinesAsync(reportFile, lines, cancellationToken);

            return new T4Stage3VirtualPointerAuditResult(
                inputFastFile,
                reportFile,
                zone.Length,
                document.Header.VirtualBlockSize,
                menu.SerializedOffset,
                target.ItemPointerArrayOffset.Value,
                target.Item.SerializedOffset,
                insertionPoint,
                cloneSpan,
                trace.ProvenMenus.Count,
                items.Count,
                known.Length,
                before.Length,
                after.Length,
                maxBefore,
                minAfter,
                lines);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void AddKnownVirtualPointer(
        byte[] zone,
        uint virtualBlockSize,
        int insertionPoint,
        long fieldOffsetLong,
        string owner,
        string field,
        List<KnownPointer> output)
    {
        if (fieldOffsetLong < 0 || fieldOffsetLong > zone.LongLength - 4)
            return;
        var fieldOffset = checked((int)fieldOffsetLong);
        var raw = ReadU32Big(zone, fieldOffset);
        if (raw is 0 or T4ZonePointer.Following or T4ZonePointer.Insert)
            return;
        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block != T4ZoneBlock.Virtual || decoded.Offset >= virtualBlockSize)
            return;
        output.Add(new KnownPointer(fieldOffsetLong, owner, field, raw, decoded.Offset, fieldOffset < insertionPoint));
    }

    private static List<T4ConsoleItemDefCandidate> ScanVeryHighConfidenceItems(byte[] zone, T4ZoneDocument document)
    {
        var result = new Dictionary<long, T4ConsoleItemDefCandidate>();
        var start = checked((int)Math.Max(0, document.AssetTableOffset + document.AssetCount * 8L));
        var end = zone.Length - T4ConsoleItemDefProbe.FixedSize;

        // Byte-aligned: the hardware-proven Custom Maps item begins at an odd serialized offset.
        // Cheap fixed-field prefilters avoid invoking the full probe on every byte window.
        for (var offset = start; offset <= end; offset++)
        {
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

    private static IReadOnlyList<long> FindDeclaredInlinePointerRuns(byte[] zone, T4MenuStructureCandidate menu, int regionEnd)
    {
        var result = new List<long>();
        if (menu.ItemCount <= 0)
            return result;
        var start = checked((int)Math.Max(0, menu.SerializedOffset + 312));
        var end = Math.Min(regionEnd, zone.Length);
        var bytesNeeded = checked(menu.ItemCount * 4);
        if (end - start < bytesNeeded)
            return result;

        // Byte-aligned; do not require the following word to be non-sentinel because an inline
        // item body may itself begin with 0xFFFFFFFF/0xFFFFFFFE.
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
                return candidate.SerializedOffset;
            start = parentFieldOffset + 1;
        }
        return null;
    }

    private static int NextMenuBoundary(IReadOnlyList<T4MenuStructureCandidate> menus, long currentOffset, int fallback)
    {
        foreach (var candidate in menus.Where(m => m.SerializedOffset > currentOffset).OrderBy(m => m.SerializedOffset))
            return checked((int)candidate.SerializedOffset);
        return fallback;
    }

    private static uint ReadU32Big(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static int ReadI32Big(byte[] data, int offset) =>
        unchecked((int)ReadU32Big(data, offset));
}
