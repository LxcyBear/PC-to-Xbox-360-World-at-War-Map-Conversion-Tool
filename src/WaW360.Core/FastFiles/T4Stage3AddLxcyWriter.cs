using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4Stage3AddLxcyResult(
    string InputFastFile,
    string OutputFastFile,
    string OwnerMenu,
    int OldItemCount,
    int NewItemCount,
    int CustomMapsItemIndex,
    int LxcyMapsItemIndex,
    long ItemPointerArrayOffset,
    long OriginalCustomMapsItemOffset,
    long NewLxcyMapsItemOffset,
    int ClonedSerializedBytes,
    int OldXFileSize,
    int NewXFileSize,
    int OldVirtualBlockSize,
    int NewVirtualBlockSize,
    int AssetCount,
    bool ReopenedAndVerified,
    bool OriginalCustomMapsPreserved,
    bool LxcyMapsPresent,
    bool PointerArrayVerified,
    bool SiblingGroupVerified,
    string TemporaryActionNote);

/// <summary>
/// Stage 3 alpha 2: first structural menu-growth proof.
///
/// Starting from the Stage-2-proven levels_unlock layout, this writer:
///  - keeps the existing Custom Maps item #59 untouched,
///  - consumes one already-zero alignment slot immediately after the 62-entry inline
///    item pointer array and turns it into a 63rd 0xFFFFFFFF inline pointer,
///  - increments levels_unlock.itemCount from 62 to 63,
///  - clones the complete serialized Custom Maps item immediately after item #59,
///  - changes only the clone's inline label to "Lxcy's Maps" and its mouse-over
///    ui_highlight ordinal from 6 to 7,
///  - grows XFile.size and the Virtual block allocation by the exact serialized clone size,
///  - repacks, reopens, reparses, and proves the old item is still #59 while the new
///    structurally valid sibling is #60.
///
/// The clone intentionally retains the original codxe solo click action in alpha 2.
/// Therefore both buttons open codxe_usermaps for this first on-console structure test.
/// A later Stage-3 build gives Lxcy's Maps its own submenu/list backing.
/// </summary>
public static class T4Stage3AddLxcyWriter
{
    public const string OriginalLabel = "Custom Maps";
    public const string NewLabel = "Lxcy's Maps";

    private static readonly byte[] OriginalLabelBytes = Encoding.ASCII.GetBytes(OriginalLabel);
    private static readonly byte[] NewLabelBytes = Encoding.ASCII.GetBytes(NewLabel);
    private static readonly byte[] Highlight6 = Encoding.ASCII.GetBytes("\"setLocalVarInt\" \"ui_highlight\" \"6\"");
    private static readonly byte[] Highlight7 = Encoding.ASCII.GetBytes("\"setLocalVarInt\" \"ui_highlight\" \"7\"");
    private static readonly byte[] CodxeActionNeedle = Encoding.ASCII.GetBytes("ui_codxe_usermap_mode");
    private static readonly byte[] CodxeMenuNeedle = Encoding.ASCII.GetBytes("codxe_usermaps");

    public static async Task<T4Stage3AddLxcyResult> CreateAsync(
        string inputFastFile,
        string outputFastFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFastFile))
            throw new FileNotFoundException("Fastfile not found.", inputFastFile);
        if (string.Equals(Path.GetFullPath(inputFastFile), Path.GetFullPath(outputFastFile), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stage 3 alpha 2 will not overwrite the source patch_ui.ff. Choose a new output file and keep the working original as a backup.");
        if (OriginalLabelBytes.Length != NewLabelBytes.Length)
            throw new InvalidOperationException("The Stage 3 alpha 2 clone relies on equal-length menu labels.");

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "stage3-add-lxcy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourceZonePath = Path.Combine(tempDir, "source.zone");
        var patchedZonePath = Path.Combine(tempDir, "patched.zone");
        var verifyZonePath = Path.Combine(tempDir, "verify.zone");

        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(inputFastFile, sourceZonePath, cancellationToken);
            if (decomp.Platform != WaWPlatform.Xbox360)
                throw new InvalidDataException("Stage 3 alpha 2 currently targets Xbox 360 WaW patch_ui.ff only.");
            if (decomp.Compression == WaWCompressionKind.XboxSignedSingleZlib)
                throw new NotSupportedException("Signed/authenticated Xbox fastfiles are not rewritten by Stage 3 alpha 2.");
            if (decomp.ZoneHeader.Endianness != Endian.Big)
                throw new InvalidDataException("Stage 3 alpha 2 requires the big-endian Xbox T4 zone layout.");

            var sourceZone = await File.ReadAllBytesAsync(sourceZonePath, cancellationToken);
            var document = T4ZoneParser.ParseZoneBytes(
                inputFastFile,
                sourceZone,
                decomp.Platform,
                decomp.Compression,
                decomp.ZoneHeader.Endianness);

            var trace = T4CustomMapsTrace.Trace(document, sourceZone);
            var targets = trace.Markers.Where(m =>
                    m.StrongInlineItemCandidate &&
                    string.Equals(m.UsermapMode, "solo", StringComparison.Ordinal) &&
                    string.Equals(m.ResolvedOwnerMenu, "levels_unlock", StringComparison.Ordinal) &&
                    m.LogicalItemIndex == 59 &&
                    m.ItemPointerArrayOffset is not null &&
                    m.ItemPointerArrayCount == 62 &&
                    m.Item is not null)
                .ToArray();

            if (targets.Length != 1)
                throw new InvalidDataException($"Expected exactly one proven levels_unlock/solo/Custom Maps item #59 with a 62-entry pointer array, found {targets.Length}. Use the original patch_ui.ff containing Custom Maps, not the alpha-1 renamed proof file.");

            var target = targets[0];
            var item = target.Item!;
            var menuOffset = checked((int)target.ResolvedOwnerMenuOffset!.Value);
            var pointerArrayOffset = checked((int)target.ItemPointerArrayOffset!.Value);
            var itemOffset = checked((int)item.SerializedOffset);

            if (ReadI32Big(sourceZone, menuOffset + 176) != 62)
                throw new InvalidDataException("levels_unlock no longer declares exactly 62 items in the source zone.");

            // The 63rd inline pointer occupies the first four bytes of the already-existing
            // zero alignment gap after the 62-entry array. This leaves every existing item
            // body's serialized start unchanged before the clone insertion point.
            var extraPointerSlot = checked(pointerArrayOffset + 62 * 4);
            if (extraPointerSlot + 4 > sourceZone.Length ||
                !sourceZone.AsSpan(extraPointerSlot, 4).SequenceEqual(new byte[4]))
                throw new InvalidDataException("The expected zero alignment slot after levels_unlock's 62-entry item pointer array is not available.");

            var nextSiblingOffset = FindNextSiblingOffset(
                sourceZone,
                itemOffset,
                item.RawParentPointer,
                document.Endianness);
            if (nextSiblingOffset is null)
                throw new InvalidDataException("Could not locate the structurally valid sibling immediately after Custom Maps item #59.");

            var nextItemOffset = checked((int)nextSiblingOffset.Value);
            var cloneLength = nextItemOffset - itemOffset;
            if (cloneLength <= T4ConsoleItemDefProbe.FixedSize || cloneLength > 16 * 1024)
                throw new InvalidDataException($"Unexpected serialized Custom Maps item span: {cloneLength:N0} bytes.");

            var markerRelative = checked((int)(target.MarkerOffset - item.SerializedOffset));
            if (markerRelative < T4ConsoleItemDefProbe.FixedSize || markerRelative + OriginalLabelBytes.Length > cloneLength)
                throw new InvalidDataException("The proven Custom Maps text is not inside the serialized item span.");

            var clone = sourceZone.AsSpan(itemOffset, cloneLength).ToArray();
            if (!clone.AsSpan(markerRelative, OriginalLabelBytes.Length).SequenceEqual(OriginalLabelBytes))
                throw new InvalidDataException("The source item clone no longer contains Custom Maps at the proven inline text location.");
            NewLabelBytes.CopyTo(clone, markerRelative);

            var highlightOffset = FindUnique(clone, Highlight6);
            if (highlightOffset < 0)
                throw new InvalidDataException("The source Custom Maps clone does not contain exactly one ui_highlight=6 mouse-over action.");
            Highlight7.CopyTo(clone, highlightOffset);

            if (clone.AsSpan().IndexOf(CodxeActionNeedle) < 0 || clone.AsSpan().IndexOf(CodxeMenuNeedle) < 0)
                throw new InvalidDataException("The cloned item lost codxe's usermap click action before writing.");

            // Build the new raw serialized zone.
            var patchedZone = new byte[sourceZone.Length + cloneLength];
            sourceZone.AsSpan(0, nextItemOffset).CopyTo(patchedZone);
            clone.CopyTo(patchedZone, nextItemOffset);
            sourceZone.AsSpan(nextItemOffset).CopyTo(patchedZone.AsSpan(nextItemOffset + cloneLength));

            // Existing menu and pointer-array positions are before nextItemOffset and therefore unchanged.
            WriteI32Big(patchedZone, menuOffset + 176, 63);
            WriteU32Big(patchedZone, extraPointerSlot, 0xFFFFFFFFu);

            // The clone adds one complete inline virtual item serialization. Grow both the
            // XFile aggregate size and the Virtual allocation by the exact clone span.
            var oldXFileSize = checked((int)ReadU32Big(sourceZone, 0));
            var oldVirtualSize = checked((int)ReadU32Big(sourceZone, 24));
            var newXFileSize = checked(oldXFileSize + cloneLength);
            var newVirtualSize = checked(oldVirtualSize + cloneLength);
            WriteU32Big(patchedZone, 0, checked((uint)newXFileSize));
            WriteU32Big(patchedZone, 24, checked((uint)newVirtualSize));

            var expectedLxcyMarker = checked(nextItemOffset + markerRelative);
            if (!patchedZone.AsSpan(checked((int)target.MarkerOffset), OriginalLabelBytes.Length).SequenceEqual(OriginalLabelBytes))
                throw new InvalidDataException("The structural insertion unexpectedly modified the original Custom Maps label.");
            if (!patchedZone.AsSpan(expectedLxcyMarker, NewLabelBytes.Length).SequenceEqual(NewLabelBytes))
                throw new InvalidDataException("The inserted item does not contain Lxcy's Maps at the expected inline text location.");
            if (!T4ConsoleItemDefProbe.TryRead(patchedZone, nextItemOffset, Endian.Big, out var newItem) || newItem is null)
                throw new InvalidDataException("The inserted Lxcy's Maps fixed item body failed the 400-byte Xbox itemDef_s structural check.");
            if (newItem.RawParentPointer != item.RawParentPointer)
                throw new InvalidDataException("The inserted item does not retain levels_unlock's proven parent identity.");

            await File.WriteAllBytesAsync(patchedZonePath, patchedZone, cancellationToken);
            await FastFileRepacker.RepackZoneFileAsync(
                inputFastFile,
                patchedZonePath,
                outputFastFile,
                decomp.Compression,
                cancellationToken);

            var verify = await FastFileDecompressor.DecompressAutoAsync(outputFastFile, verifyZonePath, cancellationToken);
            var verifyZone = await File.ReadAllBytesAsync(verifyZonePath, cancellationToken);
            if (!patchedZone.AsSpan().SequenceEqual(verifyZone))
                throw new InvalidDataException("The alpha-2 output reopened, but its decompressed zone did not exactly match the intended structural patch.");

            var verifyDocument = T4ZoneParser.ParseZoneBytes(
                outputFastFile,
                verifyZone,
                verify.Platform,
                verify.Compression,
                verify.ZoneHeader.Endianness);
            if (verifyDocument.Assets.Count != document.Assets.Count)
                throw new InvalidDataException("The structural insertion unexpectedly changed the XAsset count.");

            var verifyMenus = T4MenuStructureProbe.Probe(verifyDocument, verifyZone);
            var levelsUnlock = verifyMenus.InlineNamedMenus.SingleOrDefault(m =>
                string.Equals(m.Name, "levels_unlock", StringComparison.Ordinal));
            if (levelsUnlock is null)
                throw new InvalidDataException("The rebuilt zone no longer exposes levels_unlock.");
            if (levelsUnlock.ItemCount != 63)
                throw new InvalidDataException($"The rebuilt levels_unlock menu reports {levelsUnlock.ItemCount} items instead of 63.");

            var verifyTrace = T4CustomMapsTrace.Trace(verifyDocument, verifyZone);
            var originalTarget = verifyTrace.Markers.SingleOrDefault(m =>
                m.StrongInlineItemCandidate &&
                string.Equals(m.UsermapMode, "solo", StringComparison.Ordinal) &&
                string.Equals(m.ResolvedOwnerMenu, "levels_unlock", StringComparison.Ordinal) &&
                m.LogicalItemIndex == 59 &&
                m.ItemPointerArrayCount == 63 &&
                m.RegionSiblingCount == 63);
            if (originalTarget is null || originalTarget.Item is null)
                throw new InvalidDataException("After rebuilding, the original Custom Maps item was not re-proven as levels_unlock item #59 in a 63-item menu.");
            var verifiedOriginalItem = originalTarget.Item;

            var verifiedNewItemOffset = nextItemOffset;
            if (!T4ConsoleItemDefProbe.TryRead(verifyZone, verifiedNewItemOffset, Endian.Big, out var verifiedNewItem) || verifiedNewItem is null)
                throw new InvalidDataException("After reopening, the inserted Lxcy's Maps item body is no longer structurally valid.");
            if (verifiedNewItem.RawParentPointer != verifiedOriginalItem.RawParentPointer)
                throw new InvalidDataException("After reopening, the inserted Lxcy item no longer shares levels_unlock's parent identity.");
            if (!verifyZone.AsSpan(expectedLxcyMarker, NewLabelBytes.Length).SequenceEqual(NewLabelBytes))
                throw new InvalidDataException("After reopening, the inserted Lxcy's Maps label is missing.");

            var pointerArrayVerified = true;
            for (var i = 0; i < 63; i++)
            {
                if (ReadU32Big(verifyZone, pointerArrayOffset + i * 4) != 0xFFFFFFFFu)
                {
                    pointerArrayVerified = false;
                    break;
                }
            }
            if (!pointerArrayVerified)
                throw new InvalidDataException("The rebuilt 63-entry inline item pointer array is not intact.");

            // Re-scan exact-parent structurally valid siblings in the post-patch menu region.
            var siblingOffsets = FindExactParentSiblings(
                verifyZone,
                verifiedOriginalItem.RawParentPointer,
                checked((int)levelsUnlock.SerializedOffset),
                NextMenuBoundary(verifyTrace.ProvenMenus, levelsUnlock.SerializedOffset, verifyZone.Length));

            var siblingVerified = siblingOffsets.Count == 63 &&
                                  siblingOffsets[59] == verifiedOriginalItem.SerializedOffset &&
                                  siblingOffsets[60] == verifiedNewItemOffset;
            if (!siblingVerified)
                throw new InvalidDataException("The post-patch sibling group is not exactly 63 items with Custom Maps #59 followed by Lxcy's Maps #60.");

            return new T4Stage3AddLxcyResult(
                inputFastFile,
                outputFastFile,
                "levels_unlock",
                62,
                63,
                59,
                60,
                pointerArrayOffset,
                item.SerializedOffset,
                verifiedNewItemOffset,
                cloneLength,
                oldXFileSize,
                newXFileSize,
                oldVirtualSize,
                newVirtualSize,
                verifyDocument.Assets.Count,
                true,
                true,
                true,
                true,
                true,
                "Alpha 2 intentionally keeps the cloned codxe solo click action, so Lxcy's Maps temporarily opens the same codxe_usermaps screen as Custom Maps.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static long? FindNextSiblingOffset(byte[] zone, int currentItemOffset, uint parentPointer, Endian endian)
    {
        Span<byte> pattern = stackalloc byte[4];
        if (endian == Endian.Big)
            BinaryPrimitives.WriteUInt32BigEndian(pattern, parentPointer);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(pattern, parentPointer);

        var start = checked(currentItemOffset + T4ConsoleItemDefProbe.FixedSize);
        while (start <= zone.Length - 4)
        {
            var relative = zone.AsSpan(start).IndexOf(pattern);
            if (relative < 0)
                return null;

            var parentFieldOffset = start + relative;
            var candidateOffset = parentFieldOffset - 244;
            if (candidateOffset > currentItemOffset &&
                T4ConsoleItemDefProbe.TryRead(zone, candidateOffset, endian, out var candidate) &&
                candidate is not null &&
                candidate.RawParentPointer == parentPointer &&
                candidate.StructuralChecksPassed >= candidate.StructuralChecksTotal * 0.98)
            {
                return candidateOffset;
            }

            start = parentFieldOffset + 1;
        }

        return null;
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

    private static int FindUnique(byte[] data, byte[] needle)
    {
        var first = data.AsSpan().IndexOf(needle);
        if (first < 0)
            return -1;
        var nextStart = first + Math.Max(1, needle.Length);
        if (nextStart < data.Length && data.AsSpan(nextStart).IndexOf(needle) >= 0)
            return -1;
        return first;
    }

    private static uint ReadU32Big(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static int ReadI32Big(byte[] data, int offset) =>
        unchecked((int)ReadU32Big(data, offset));

    private static void WriteU32Big(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);

    private static void WriteI32Big(byte[] data, int offset, int value) =>
        WriteU32Big(data, offset, unchecked((uint)value));
}
