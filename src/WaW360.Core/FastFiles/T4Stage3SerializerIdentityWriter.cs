using System.Buffers.Binary;

namespace WaW360.Core.FastFiles;

public sealed record T4Stage3SerializerIdentityResult(
    string InputFastFile,
    string OutputFastFile,
    string OwnerMenu,
    int ItemCount,
    long MenuOffset,
    long ItemPointerArrayOffset,
    int ItemPointerCount,
    long FirstItemOffset,
    long LastItemOffset,
    int RebuiltRegionBytes,
    int FixedItemsRewritten,
    int AssetCount,
    bool RegionByteIdentical,
    bool ZoneByteIdentical,
    bool ReopenedAndVerified,
    int CandidatePackedVirtualPointers,
    string RelocationAuditNote);

/// <summary>
/// Stage 3 alpha 3: record-aware 62 -> 62 serializer identity proof.
///
/// The failed alpha-2 growth test proved that inserting bytes into the middle of the
/// serialized Virtual stream is not safe until all moved references are rebuilt/relocated.
/// Alpha 3 therefore refuses to grow the zone. It proves our menu/item boundaries and
/// serializer by rebuilding the existing levels_unlock menu in place with exactly the
/// same byte length and requiring the decompressed result to be byte-for-byte identical.
///
/// Known fixed fields are written back from the parsed menu/item model. Unknown fields and
/// each item's inline variable-length tail are preserved verbatim for now. This is an
/// intentional stepping stone toward a full object-graph writer, not the final menu editor.
/// </summary>
public static class T4Stage3SerializerIdentityWriter
{
    public static async Task<T4Stage3SerializerIdentityResult> CreateAsync(
        string inputFastFile,
        string outputFastFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFastFile))
            throw new FileNotFoundException("Fastfile not found.", inputFastFile);
        if (string.Equals(Path.GetFullPath(inputFastFile), Path.GetFullPath(outputFastFile), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stage 3 alpha 3 will not overwrite the source patch_ui.ff. Choose a new output file.");

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "stage3-serializer-identity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourceZonePath = Path.Combine(tempDir, "source.zone");
        var rebuiltZonePath = Path.Combine(tempDir, "rebuilt.zone");
        var verifyZonePath = Path.Combine(tempDir, "verify.zone");

        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(inputFastFile, sourceZonePath, cancellationToken);
            if (decomp.Platform != WaWPlatform.Xbox360 || decomp.ZoneHeader.Endianness != Endian.Big)
                throw new InvalidDataException("Stage 3 alpha 3 currently targets the big-endian Xbox 360 WaW patch_ui.ff layout only.");
            if (decomp.Compression == WaWCompressionKind.XboxSignedSingleZlib)
                throw new NotSupportedException("Signed/authenticated Xbox fastfiles are not rewritten by Stage 3 alpha 3.");

            var sourceZone = await File.ReadAllBytesAsync(sourceZonePath, cancellationToken);
            var document = T4ZoneParser.ParseZoneBytes(
                inputFastFile,
                sourceZone,
                decomp.Platform,
                decomp.Compression,
                decomp.ZoneHeader.Endianness);

            var trace = T4CustomMapsTrace.Trace(document, sourceZone);
            var target = trace.Markers.SingleOrDefault(m =>
                m.StrongInlineItemCandidate &&
                string.Equals(m.UsermapMode, "solo", StringComparison.Ordinal) &&
                string.Equals(m.ResolvedOwnerMenu, "levels_unlock", StringComparison.Ordinal) &&
                m.LogicalItemIndex == 59 &&
                m.ItemPointerArrayOffset is not null &&
                m.ItemPointerArrayCount == 62 &&
                m.RegionSiblingCount == 62 &&
                m.Item is not null);

            if (target is null || target.Item is null)
                throw new InvalidDataException("Could not re-prove the original 62-item levels_unlock / Custom Maps item #59 structure. Use the restored known-good patch_ui.ff.");

            var menu = trace.ProvenMenus.Single(m =>
                string.Equals(m.Name, "levels_unlock", StringComparison.Ordinal) &&
                m.SerializedOffset == target.ResolvedOwnerMenuOffset);

            if (menu.ItemCount != 62)
                throw new InvalidDataException($"Identity proof requires levels_unlock.itemCount=62, found {menu.ItemCount}.");

            var menuOffset = checked((int)menu.SerializedOffset);
            var pointerArrayOffset = checked((int)target.ItemPointerArrayOffset!.Value);
            var parentPointer = target.Item.RawParentPointer;
            var nextMenuBoundary = NextMenuBoundary(trace.ProvenMenus, menu.SerializedOffset, sourceZone.Length);
            var siblingOffsets = FindExactParentSiblings(sourceZone, parentPointer, menuOffset, nextMenuBoundary);

            if (siblingOffsets.Count != 62)
                throw new InvalidDataException($"Expected exactly 62 structurally valid levels_unlock sibling items, found {siblingOffsets.Count}.");
            if (siblingOffsets[59] != target.Item.SerializedOffset)
                throw new InvalidDataException("The proven Custom Maps item is no longer sibling/index #59.");

            // Prove and preserve the exact 62-entry inline item-pointer array.
            var rawPointers = new uint[62];
            for (var i = 0; i < rawPointers.Length; i++)
            {
                var raw = ReadU32Big(sourceZone, pointerArrayOffset + i * 4);
                if (raw is not (T4ZonePointer.Following or T4ZonePointer.Insert))
                    throw new InvalidDataException($"levels_unlock item pointer #{i} is 0x{raw:X8}, not an inline sentinel.");
                rawPointers[i] = raw;
            }

            var firstItemOffset = checked((int)siblingOffsets[0]);
            var lastItemOffset = checked((int)siblingOffsets[^1]);
            if (pointerArrayOffset >= firstItemOffset)
                throw new InvalidDataException("levels_unlock pointer array does not precede its first item body.");

            // Rebuild in a fresh same-size zone buffer. Everything outside the proven menu
            // structures is copied verbatim; the known menu/item fields below are then
            // re-emitted from parsed values.
            var rebuiltZone = sourceZone.ToArray();

            // menuDef_t fields we currently understand and trust.
            WriteI32Big(rebuiltZone, menuOffset + 172, menu.FullScreen ? 1 : 0);
            WriteI32Big(rebuiltZone, menuOffset + 176, menu.ItemCount);
            WriteF32Big(rebuiltZone, menuOffset + 4, menu.X);
            WriteF32Big(rebuiltZone, menuOffset + 8, menu.Y);
            WriteF32Big(rebuiltZone, menuOffset + 12, menu.Width);
            WriteF32Big(rebuiltZone, menuOffset + 16, menu.Height);
            WriteI32Big(rebuiltZone, menuOffset + 20, menu.HorizontalAlign);
            WriteI32Big(rebuiltZone, menuOffset + 24, menu.VerticalAlign);
            WriteU32Big(rebuiltZone, menuOffset + 0, menu.RawNamePointer);
            WriteU32Big(rebuiltZone, menuOffset + 308, menu.RawItemsPointer);

            for (var i = 0; i < rawPointers.Length; i++)
                WriteU32Big(rebuiltZone, pointerArrayOffset + i * 4, rawPointers[i]);

            var fixedItemsRewritten = 0;
            foreach (var siblingOffsetLong in siblingOffsets)
            {
                var siblingOffset = checked((int)siblingOffsetLong);
                if (!T4ConsoleItemDefProbe.TryRead(sourceZone, siblingOffset, Endian.Big, out var item) || item is null)
                    throw new InvalidDataException($"Sibling item at 0x{siblingOffset:X8} no longer passes the 400-byte Xbox itemDef_s structural reader.");

                RewriteKnownItemFields(rebuiltZone, siblingOffset, item);
                fixedItemsRewritten++;
            }

            // The identity serializer is not allowed to change a single decompressed byte.
            var regionStart = menuOffset;
            var regionEnd = nextMenuBoundary;
            var regionByteIdentical = sourceZone.AsSpan(regionStart, regionEnd - regionStart)
                .SequenceEqual(rebuiltZone.AsSpan(regionStart, regionEnd - regionStart));
            var zoneByteIdentical = sourceZone.AsSpan().SequenceEqual(rebuiltZone);

            if (!regionByteIdentical || !zoneByteIdentical)
                throw new InvalidDataException("The 62->62 serializer did not reproduce the source bytes exactly. No FF was written.");

            // Read-only relocation audit: count 32-bit packed references that decode into the
            // existing Virtual allocation. This is intentionally only an inventory; alpha 3
            // never rewrites them. A later growth build will use typed object traversal rather
            // than blindly patching arbitrary 4-byte patterns.
            var packedVirtualCandidates = CountPackedVirtualPointerCandidates(sourceZone, document.Header.VirtualBlockSize);

            await File.WriteAllBytesAsync(rebuiltZonePath, rebuiltZone, cancellationToken);
            await FastFileRepacker.RepackZoneFileAsync(
                inputFastFile,
                rebuiltZonePath,
                outputFastFile,
                decomp.Compression,
                cancellationToken);

            var verify = await FastFileDecompressor.DecompressAutoAsync(outputFastFile, verifyZonePath, cancellationToken);
            var verifyZone = await File.ReadAllBytesAsync(verifyZonePath, cancellationToken);
            if (!sourceZone.AsSpan().SequenceEqual(verifyZone))
                throw new InvalidDataException("The alpha-3 output reopened, but the decompressed zone is not byte-identical to the known-good source.");

            var verifyDocument = T4ZoneParser.ParseZoneBytes(
                outputFastFile,
                verifyZone,
                verify.Platform,
                verify.Compression,
                verify.ZoneHeader.Endianness);
            if (verifyDocument.Assets.Count != document.Assets.Count)
                throw new InvalidDataException("The identity rebuild changed the XAsset count.");

            var verifyTrace = T4CustomMapsTrace.Trace(verifyDocument, verifyZone);
            var verifyTarget = verifyTrace.Markers.SingleOrDefault(m =>
                m.StrongInlineItemCandidate &&
                string.Equals(m.UsermapMode, "solo", StringComparison.Ordinal) &&
                string.Equals(m.ResolvedOwnerMenu, "levels_unlock", StringComparison.Ordinal) &&
                m.LogicalItemIndex == 59 &&
                m.ItemPointerArrayCount == 62 &&
                m.RegionSiblingCount == 62);
            if (verifyTarget is null)
                throw new InvalidDataException("After reopening, levels_unlock / Custom Maps item #59 was not re-proven exactly as in the source.");

            return new T4Stage3SerializerIdentityResult(
                inputFastFile,
                outputFastFile,
                "levels_unlock",
                62,
                menuOffset,
                pointerArrayOffset,
                62,
                firstItemOffset,
                lastItemOffset,
                regionEnd - regionStart,
                fixedItemsRewritten,
                verifyDocument.Assets.Count,
                regionByteIdentical,
                zoneByteIdentical,
                true,
                packedVirtualCandidates,
                "Read-only candidate inventory only. Alpha 3 performs no relocation and no zone growth; future growth must rebuild typed pointers/pre-linked references instead of byte-scanning them.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void RewriteKnownItemFields(byte[] zone, int offset, T4ConsoleItemDefCandidate item)
    {
        WriteU32Big(zone, offset + 0, item.RawWindowNamePointer);
        WriteF32Big(zone, offset + 4, item.X);
        WriteF32Big(zone, offset + 8, item.Y);
        WriteF32Big(zone, offset + 12, item.Width);
        WriteF32Big(zone, offset + 16, item.Height);
        WriteI32Big(zone, offset + 20, item.HorizontalAlign);
        WriteI32Big(zone, offset + 24, item.VerticalAlign);
        WriteI32Big(zone, offset + 192, item.Type);
        WriteI32Big(zone, offset + 196, item.DataType);
        WriteI32Big(zone, offset + 200, item.Alignment);
        WriteI32Big(zone, offset + 204, item.FontEnum);
        WriteI32Big(zone, offset + 208, item.TextAlignMode);
        WriteU32Big(zone, offset + 236, item.RawTextPointer);
        WriteU32Big(zone, offset + 244, item.RawParentPointer);
        WriteU32Big(zone, offset + 264, item.RawActionPointer);
    }

    private static IReadOnlyList<long> FindExactParentSiblings(byte[] zone, uint parentPointer, int regionStart, int regionEnd)
    {
        Span<byte> pattern = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(pattern, parentPointer);

        var result = new SortedSet<long>();
        var start = Math.Max(0, regionStart);
        var end = Math.Min(zone.Length, Math.Max(regionStart, regionEnd));
        while (start <= end - 4)
        {
            var relative = zone.AsSpan(start, end - start).IndexOf(pattern);
            if (relative < 0)
                break;

            var parentFieldOffset = start + relative;
            var candidateOffset = parentFieldOffset - 244;
            if (candidateOffset >= regionStart && candidateOffset < end &&
                T4ConsoleItemDefProbe.TryRead(zone, candidateOffset, Endian.Big, out var candidate) &&
                candidate is not null &&
                candidate.RawParentPointer == parentPointer &&
                candidate.StructuralChecksPassed >= candidate.StructuralChecksTotal * 0.98)
            {
                result.Add(candidate.SerializedOffset);
            }

            start = parentFieldOffset + 1;
        }

        return result.ToArray();
    }

    private static int NextMenuBoundary(IReadOnlyList<T4MenuStructureCandidate> menus, long currentOffset, int fallback)
    {
        foreach (var menu in menus.Where(m => m.SerializedOffset > currentOffset).OrderBy(m => m.SerializedOffset))
            return checked((int)menu.SerializedOffset);
        return fallback;
    }

    private static int CountPackedVirtualPointerCandidates(byte[] zone, uint virtualBlockSize)
    {
        var count = 0;
        // Console structures in this sample can be 2-byte aligned. Scan both 0/2 mod 4
        // locations but label the result as candidates, never as a relocation table.
        for (var offset = 0; offset <= zone.Length - 4; offset += 2)
        {
            var raw = ReadU32Big(zone, offset);
            if (raw is 0 or T4ZonePointer.Following or T4ZonePointer.Insert)
                continue;
            var decoded = T4ZonePointer.Decode(raw);
            if (decoded.Kind == T4ZonePointerKind.Offset &&
                decoded.Block == T4ZoneBlock.Virtual &&
                decoded.Offset < virtualBlockSize)
            {
                count++;
            }
        }
        return count;
    }

    private static uint ReadU32Big(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static void WriteU32Big(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);

    private static void WriteI32Big(byte[] data, int offset, int value) =>
        WriteU32Big(data, offset, unchecked((uint)value));

    private static void WriteF32Big(byte[] data, int offset, float value) =>
        WriteI32Big(data, offset, BitConverter.SingleToInt32Bits(value));
}
