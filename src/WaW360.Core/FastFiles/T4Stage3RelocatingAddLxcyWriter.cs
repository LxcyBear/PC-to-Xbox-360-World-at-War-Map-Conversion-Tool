using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4Stage3RelocatingAddLxcyResult(
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
    int OldTempBlockSize,
    int NewTempBlockSize,
    int VirtualBlockSize,
    uint LevelsUnlockRuntimeAddress,
    uint NextMenuRuntimeAddress,
    int RuntimeVsSerializedSpanDelta,
    int ItemBodiesDiscovered,
    int ParentPointersRelocated,
    int ParentGroupsRelocated,
    int AssetCount,
    bool ReopenedAndVerified,
    bool OriginalCustomMapsPreserved,
    bool LxcyMapsPresent,
    bool PointerArrayVerified,
    bool SiblingGroupVerified,
    bool TempBlockAccountingVerified,
    bool VirtualBlockUnchanged,
    string RelocationNote);

/// <summary>
/// Stage 3 alpha 4b: first block-aware 62 -> 63 growth test (owner-proof fix).
///
/// Alpha 2 proved that the serialized menu structure itself could be made to parse as 63
/// items, but it grew XFILE_BLOCK_VIRTUAL and did not relocate itemDef_s.parent values.
/// On real hardware the resulting patch_ui.ff repeatedly relaunched/failed to boot.
///
/// Alpha 4b keeps the hardware-proven alpha-3 serializer baseline and changes only the
/// growth mechanics:
///   * menuDef_t/itemDef_s serialization is accounted to XFILE_BLOCK_TEMP;
///   * TEMP is treated as a peak scratch allocation, not a cumulative stream. Its reserve is
///     increased only if the enlarged levels_unlock menu would exceed the existing peak;
///   * the Virtual block is deliberately left unchanged;
///   * the complete Custom Maps serialization is cloned as item #60;
///   * the existing 4-byte alignment gap supplies pointer-array slot #63;
///   * itemDef_s.parent is treated as the typed pre-linked back-reference it is. Relocation is
///     allowed only after the source zone proves a linear serialized-offset/runtime-parent
///     mapping across multiple later menu anchors;
///   * no arbitrary 4-byte zone-wide pointer scan is performed.
///
/// If the TEMP peak, parent ordering, or source mapping cannot be proven conservatively, the
/// writer fails closed rather than producing another speculative Xbox test file.
/// </summary>
public static class T4Stage3RelocatingAddLxcyWriter
{
    public const string OriginalLabel = "Custom Maps";
    public const string NewLabel = "Lxcy's Maps";

    private static readonly byte[] OriginalLabelBytes = Encoding.ASCII.GetBytes(OriginalLabel);
    private static readonly byte[] NewLabelBytes = Encoding.ASCII.GetBytes(NewLabel);
    private static readonly byte[] Highlight6 = Encoding.ASCII.GetBytes("\"setLocalVarInt\" \"ui_highlight\" \"6\"");
    private static readonly byte[] Highlight7 = Encoding.ASCII.GetBytes("\"setLocalVarInt\" \"ui_highlight\" \"7\"");
    private static readonly byte[] CodxeActionNeedle = Encoding.ASCII.GetBytes("ui_codxe_usermap_mode");
    private static readonly byte[] CodxeMenuNeedle = Encoding.ASCII.GetBytes("codxe_usermaps");

    public static async Task<T4Stage3RelocatingAddLxcyResult> CreateAsync(
        string inputFastFile,
        string outputFastFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFastFile))
            throw new FileNotFoundException("Fastfile not found.", inputFastFile);
        if (string.Equals(Path.GetFullPath(inputFastFile), Path.GetFullPath(outputFastFile), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stage 3 alpha 4b will not overwrite the known-good patch_ui.ff. Choose a new output file.");
        if (OriginalLabelBytes.Length != NewLabelBytes.Length)
            throw new InvalidOperationException("The alpha-4 clone requires equal-length menu labels.");

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "stage3-alpha4", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourceZonePath = Path.Combine(tempDir, "source.zone");
        var patchedZonePath = Path.Combine(tempDir, "patched.zone");
        var verifyZonePath = Path.Combine(tempDir, "verify.zone");

        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(inputFastFile, sourceZonePath, cancellationToken);
            if (decomp.Platform != WaWPlatform.Xbox360 || decomp.ZoneHeader.Endianness != Endian.Big)
                throw new InvalidDataException("Stage 3 alpha 4b currently targets the big-endian Xbox 360 WaW patch_ui.ff layout only.");
            if (decomp.Compression == WaWCompressionKind.XboxSignedSingleZlib)
                throw new NotSupportedException("Signed/authenticated Xbox fastfiles are not rewritten by Stage 3 alpha 4b.");

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

            if (target is null || target.Item is null || target.ResolvedOwnerMenuOffset is null)
                throw new InvalidDataException("Could not re-prove the hardware-good 62-item levels_unlock / Custom Maps item #59 structure. Use the restored original patch_ui.ff.");

            var item = target.Item;
            var menu = trace.ProvenMenus.Single(m =>
                string.Equals(m.Name, "levels_unlock", StringComparison.Ordinal) &&
                m.SerializedOffset == target.ResolvedOwnerMenuOffset.Value);
            if (menu.ItemCount != 62)
                throw new InvalidDataException($"Alpha 4 requires levels_unlock.itemCount=62, found {menu.ItemCount}.");

            var menuOffset = checked((int)menu.SerializedOffset);
            var pointerArrayOffset = checked((int)target.ItemPointerArrayOffset!.Value);
            var itemOffset = checked((int)item.SerializedOffset);

            // Re-prove the source pointer array. The 63rd slot uses the first four bytes of
            // the existing zero alignment gap, exactly as the structural alpha-2 proof did.
            for (var i = 0; i < 62; i++)
            {
                var raw = ReadU32Big(sourceZone, pointerArrayOffset + i * 4);
                if (raw is not (T4ZonePointer.Following or T4ZonePointer.Insert))
                    throw new InvalidDataException($"levels_unlock item pointer #{i} is 0x{raw:X8}, not an inline sentinel.");
            }

            var extraPointerSlot = checked(pointerArrayOffset + 62 * 4);
            if (extraPointerSlot + 4 > sourceZone.Length ||
                !sourceZone.AsSpan(extraPointerSlot, 4).SequenceEqual(new byte[4]))
                throw new InvalidDataException("The expected zero alignment slot after levels_unlock's 62-entry item pointer array is not available.");

            var nextSiblingOffset = FindNextSiblingOffset(sourceZone, itemOffset, item.RawParentPointer);
            if (nextSiblingOffset is null)
                throw new InvalidDataException("Could not locate the structurally valid sibling immediately after Custom Maps item #59.");

            var nextItemOffset = checked((int)nextSiblingOffset.Value);
            var cloneLength = nextItemOffset - itemOffset;
            if (cloneLength <= T4ConsoleItemDefProbe.FixedSize || cloneLength > 16 * 1024)
                throw new InvalidDataException($"Unexpected serialized Custom Maps item span: {cloneLength:N0} bytes.");

            var markerRelative = checked((int)(target.MarkerOffset - item.SerializedOffset));
            if (markerRelative < T4ConsoleItemDefProbe.FixedSize || markerRelative + OriginalLabelBytes.Length > cloneLength)
                throw new InvalidDataException("The proven Custom Maps text is not inside the complete serialized item span.");

            var clone = sourceZone.AsSpan(itemOffset, cloneLength).ToArray();
            if (!clone.AsSpan(markerRelative, OriginalLabelBytes.Length).SequenceEqual(OriginalLabelBytes))
                throw new InvalidDataException("The source item clone no longer contains Custom Maps at the proven inline text location.");
            NewLabelBytes.CopyTo(clone, markerRelative);

            var highlightOffset = FindUnique(clone, Highlight6);
            if (highlightOffset < 0)
                throw new InvalidDataException("The source Custom Maps item does not contain exactly one ui_highlight=6 mouse-over action.");
            Highlight7.CopyTo(clone, highlightOffset);

            if (clone.AsSpan().IndexOf(CodxeActionNeedle) < 0 || clone.AsSpan().IndexOf(CodxeMenuNeedle) < 0)
                throw new InvalidDataException("The cloned item lost codxe's usermap action before writing.");

            // Scan the real 400-byte console item bodies once. This is typed relocation input:
            // only itemDef_s.parent (+244) is modified later.
            var allItems = ScanHighConfidenceItems(sourceZone, document);
            var groups = allItems
                .Where(x => x.RawParentPointer is not (0 or T4ZonePointer.Following or T4ZonePointer.Insert))
                .GroupBy(x => x.RawParentPointer)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SerializedOffset).ToArray());

            // The target parent value is a pre-linked runtime/UI-arena address for levels_unlock.
            // Infer several later menu owners from exact item groups and prove that runtime-parent
            // distance tracks serialized menu distance before changing any pre-linked address.
            var orderedMenus = trace.ProvenMenus.OrderBy(m => m.SerializedOffset).ToArray();
            var menuIndex = Array.FindIndex(orderedMenus, m => m.SerializedOffset == menu.SerializedOffset);
            if (menuIndex < 0 || menuIndex + 1 >= orderedMenus.Length)
                throw new InvalidDataException("Could not locate a proven menu after levels_unlock.");

            var nextMenu = orderedMenus[menuIndex + 1];
            var nextMenuRegionEnd = menuIndex + 2 < orderedMenus.Length
                ? orderedMenus[menuIndex + 2].SerializedOffset
                : sourceZone.LongLength;

            var levelsUnlockRuntime = item.RawParentPointer;

            // Do not depend on the broad discovery scan to recover every sibling. Re-prove the
            // already-known levels_unlock group directly from its exact parent value, then seed the
            // relocation group table with those typed bodies. This is the same parent-specific proof
            // used by the Stage-2/alpha-3 trace and cannot be derailed by an unrelated false-positive
            // structure elsewhere in the serialized stream.
            var targetGroup = RecoverExactParentGroup(
                sourceZone,
                levelsUnlockRuntime,
                checked((int)menu.SerializedOffset),
                checked((int)nextMenu.SerializedOffset));
            if (targetGroup.Length != 62)
                throw new InvalidDataException(
                    $"The exact-parent walk recovered {targetGroup.Length} levels_unlock item body/bodies; expected 62.");
            groups[levelsUnlockRuntime] = targetGroup;

            var serializedSpan = checked((int)(nextMenu.SerializedOffset - menu.SerializedOffset));
            var nextMenuOwner = InferMenuRuntimeAddressByExactRegionProof(
                sourceZone,
                nextMenu,
                nextMenuRegionEnd,
                groups)
                ?? throw new InvalidDataException(
                    $"Could not prove one unique typed itemDef_s.parent group for next menu '{nextMenu.Name}' ({nextMenu.ItemCount} items). " +
                    "Alpha 4b requires both an exact parent-sibling count and an exact inline item-pointer array inside that menu's serialized region, so it still refuses to guess relocation addresses. " +
                    $"Region diagnostics: {DescribeMenuOwnerCandidates(sourceZone, nextMenu, nextMenuRegionEnd)}");

            if (nextMenuOwner <= levelsUnlockRuntime)
                throw new InvalidDataException(
                    $"Next menu runtime address 0x{nextMenuOwner:X8} is not after levels_unlock 0x{levelsUnlockRuntime:X8}; relocation ordering is not proven.");

            var runtimeSpan = checked((int)(nextMenuOwner - levelsUnlockRuntime));
            var spanDelta = runtimeSpan - serializedSpan;
            if (spanDelta is < -16 or > 16)
                throw new InvalidDataException(
                    $"Pre-linked mapping proof failed: levels_unlock -> {nextMenu.Name} is {serializedSpan:N0} serialized bytes but 0x{runtimeSpan:X} runtime bytes (delta {spanDelta}). " +
                    "No alpha-4 FF was written; report these values so the object walk can be extended instead of guessing.");

            var mappingAnchors = new List<(T4MenuStructureCandidate Menu, uint Owner, int Delta)>
            {
                (menu, levelsUnlockRuntime, 0),
                (nextMenu, nextMenuOwner, spanDelta)
            };
            for (var i = menuIndex + 2; i < orderedMenus.Length; i++)
            {
                var candidateMenu = orderedMenus[i];
                var regionEnd = i + 1 < orderedMenus.Length ? orderedMenus[i + 1].SerializedOffset : sourceZone.LongLength;
                var serializedDistance = checked((long)(candidateMenu.SerializedOffset - menu.SerializedOffset));
                var owner = InferMenuRuntimeAddressByExactRegionProof(
                    sourceZone,
                    candidateMenu,
                    regionEnd,
                    groups);
                if (owner is null || owner.Value <= levelsUnlockRuntime)
                    continue;

                var runtimeDistance = (long)owner.Value - levelsUnlockRuntime;
                var delta = checked((int)(runtimeDistance - serializedDistance));
                if (Math.Abs(delta - spanDelta) > 16)
                    throw new InvalidDataException(
                        $"Pre-linked mapping diverges at menu '{candidateMenu.Name}': source delta is {delta}, while the first proven delta is {spanDelta}. " +
                        "No alpha-4b FF was written; later parent relocation is not linear enough to prove safely.");

                mappingAnchors.Add((candidateMenu, owner.Value, delta));
            }

            if (mappingAnchors.Count < 4)
                throw new InvalidDataException(
                    $"Only {mappingAnchors.Count} menu/runtime mapping anchors were proven. Alpha 4 requires at least four before relocating pre-linked parent references.");

            // Build the new serialized stream: existing bytes through Custom Maps, then the
            // complete clone, then the untouched remainder.
            var patchedZone = new byte[sourceZone.Length + cloneLength];
            sourceZone.AsSpan(0, nextItemOffset).CopyTo(patchedZone);
            clone.CopyTo(patchedZone, nextItemOffset);
            sourceZone.AsSpan(nextItemOffset).CopyTo(patchedZone.AsSpan(nextItemOffset + cloneLength));

            WriteI32Big(patchedZone, menuOffset + 176, 63);
            WriteU32Big(patchedZone, extraPointerSlot, T4ZonePointer.Following);

            // XFile.size is the number of bytes emitted into the serialized zone stream. The
            // clone therefore always grows it by cloneLength. T4 TEMP, however, is a peak scratch
            // reserve: it resets when a TEMP scope is popped. So do NOT blindly add cloneLength to
            // the header as if TEMP were cumulative. The enlarged levels_unlock asset needs at most
            // its old serialized TEMP span plus the clone; preserve a larger existing global peak.
            var oldXFileSize = checked((int)ReadU32Big(sourceZone, 0));
            var oldTempSize = checked((int)ReadU32Big(sourceZone, 8));
            var oldVirtualSize = checked((int)ReadU32Big(sourceZone, 24));
            var newXFileSize = checked(oldXFileSize + cloneLength);
            var requiredLevelsUnlockTemp = checked(serializedSpan + cloneLength);
            var newTempSize = Math.Max(oldTempSize, requiredLevelsUnlockTemp);
            WriteU32Big(patchedZone, 0, checked((uint)newXFileSize));
            if (newTempSize != oldTempSize)
                WriteU32Big(patchedZone, 8, checked((uint)newTempSize));
            // offset 24 / XFILE_BLOCK_VIRTUAL intentionally remains byte-identical.

            // Typed relocation: itemDef_s.parent is the one T4 menu back-reference explicitly
            // excluded from normal pointer processing. The source mapping above proves that these
            // pre-linked owner addresses advance with serialized menu position. Only high-confidence
            // item bodies after the insertion, in the same proven address arena, are eligible.
            var arenaHighByte = levelsUnlockRuntime & 0xFF000000u;
            var eligibleGroups = groups
                .Where(kvp =>
                    (kvp.Key & 0xFF000000u) == arenaHighByte &&
                    kvp.Key >= nextMenuOwner &&
                    kvp.Value.Min(i => i.SerializedOffset) >= nextMenu.SerializedOffset)
                .OrderBy(kvp => kvp.Value.Min(i => i.SerializedOffset))
                .ToArray();

            if (eligibleGroups.Length == 0 || eligibleGroups[0].Key != nextMenuOwner)
                throw new InvalidDataException("Relocation set does not begin with the immediately following proven menu; refusing to emit an incomplete fixup pass.");

            for (var i = 1; i < eligibleGroups.Length; i++)
            {
                if (eligibleGroups[i].Key <= eligibleGroups[i - 1].Key)
                    throw new InvalidDataException(
                        $"Pre-linked parent ordering diverges near serialized item 0x{eligibleGroups[i].Value[0].SerializedOffset:X}. Alpha 4 refuses a non-monotonic relocation set.");
            }

            var parentsToRelocate = eligibleGroups.Select(kvp => kvp.Key).ToHashSet();

            var parentPointersRelocated = 0;
            foreach (var sourceItem in allItems)
            {
                if (!parentsToRelocate.Contains(sourceItem.RawParentPointer))
                    continue;

                var sourceFieldOffset = checked((int)sourceItem.SerializedOffset + 244);
                var destinationFieldOffset = sourceFieldOffset >= nextItemOffset
                    ? checked(sourceFieldOffset + cloneLength)
                    : sourceFieldOffset;
                var relocated = checked(sourceItem.RawParentPointer + (uint)cloneLength);
                WriteU32Big(patchedZone, destinationFieldOffset, relocated);
                parentPointersRelocated++;
            }

            if (parentPointersRelocated == 0)
                throw new InvalidDataException("No typed itemDef_s.parent references were relocated; the alpha-4 relocation proof is incomplete.");

            // The new Lxcy clone belongs to levels_unlock, whose address itself did not move.
            // It must therefore retain the original parent value.
            WriteU32Big(patchedZone, nextItemOffset + 244, levelsUnlockRuntime);

            var expectedLxcyMarker = checked(nextItemOffset + markerRelative);
            if (!patchedZone.AsSpan(checked((int)target.MarkerOffset), OriginalLabelBytes.Length).SequenceEqual(OriginalLabelBytes))
                throw new InvalidDataException("The alpha-4 rebuild unexpectedly modified the original Custom Maps label.");
            if (!patchedZone.AsSpan(expectedLxcyMarker, NewLabelBytes.Length).SequenceEqual(NewLabelBytes))
                throw new InvalidDataException("The inserted item does not contain Lxcy's Maps at the expected inline text location.");
            if (!T4ConsoleItemDefProbe.TryRead(patchedZone, nextItemOffset, Endian.Big, out var newItem) || newItem is null)
                throw new InvalidDataException("The inserted Lxcy's Maps fixed item body failed the 400-byte Xbox itemDef_s structural check.");
            if (newItem.RawParentPointer != levelsUnlockRuntime)
                throw new InvalidDataException("The inserted item does not retain levels_unlock's original parent identity.");

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
                throw new InvalidDataException("The alpha-4 output reopened, but its decompressed zone did not exactly match the intended relocation-aware patch.");

            var verifyDocument = T4ZoneParser.ParseZoneBytes(
                outputFastFile,
                verifyZone,
                verify.Platform,
                verify.Compression,
                verify.ZoneHeader.Endianness);
            if (verifyDocument.Assets.Count != document.Assets.Count)
                throw new InvalidDataException("The alpha-4 rebuild unexpectedly changed the XAsset count.");

            var verifyMenus = T4MenuStructureProbe.Probe(verifyDocument, verifyZone);
            var levelsUnlock = verifyMenus.InlineNamedMenus.SingleOrDefault(m => string.Equals(m.Name, "levels_unlock", StringComparison.Ordinal));
            if (levelsUnlock is null || levelsUnlock.ItemCount != 63)
                throw new InvalidDataException("After reopening, levels_unlock is missing or does not report exactly 63 items.");

            var verifyTrace = T4CustomMapsTrace.Trace(verifyDocument, verifyZone);
            var originalTarget = verifyTrace.Markers.SingleOrDefault(m =>
                m.StrongInlineItemCandidate &&
                string.Equals(m.UsermapMode, "solo", StringComparison.Ordinal) &&
                string.Equals(m.ResolvedOwnerMenu, "levels_unlock", StringComparison.Ordinal) &&
                m.LogicalItemIndex == 59 &&
                m.ItemPointerArrayCount == 63 &&
                m.RegionSiblingCount == 63);
            if (originalTarget is null || originalTarget.Item is null)
                throw new InvalidDataException("After reopening, original Custom Maps was not re-proven as levels_unlock item #59 in a 63-item menu.");

            if (!T4ConsoleItemDefProbe.TryRead(verifyZone, nextItemOffset, Endian.Big, out var verifiedNewItem) || verifiedNewItem is null)
                throw new InvalidDataException("After reopening, the inserted Lxcy's Maps body is no longer structurally valid.");
            if (verifiedNewItem.RawParentPointer != originalTarget.Item.RawParentPointer)
                throw new InvalidDataException("After reopening, Lxcy's Maps no longer shares levels_unlock's parent identity.");
            if (!verifyZone.AsSpan(expectedLxcyMarker, NewLabelBytes.Length).SequenceEqual(NewLabelBytes))
                throw new InvalidDataException("After reopening, the inserted Lxcy's Maps label is missing.");

            var pointerArrayVerified = true;
            for (var i = 0; i < 63; i++)
            {
                var p = ReadU32Big(verifyZone, pointerArrayOffset + i * 4);
                if (p is not (T4ZonePointer.Following or T4ZonePointer.Insert))
                {
                    pointerArrayVerified = false;
                    break;
                }
            }
            if (!pointerArrayVerified)
                throw new InvalidDataException("The rebuilt 63-entry inline item pointer array is not intact.");

            var siblingOffsets = FindExactParentSiblings(
                verifyZone,
                originalTarget.Item.RawParentPointer,
                checked((int)levelsUnlock.SerializedOffset),
                NextMenuBoundary(verifyTrace.ProvenMenus, levelsUnlock.SerializedOffset, verifyZone.Length));
            var siblingVerified = siblingOffsets.Count == 63 &&
                                  siblingOffsets[59] == originalTarget.Item.SerializedOffset &&
                                  siblingOffsets[60] == nextItemOffset;
            if (!siblingVerified)
                throw new InvalidDataException("The post-patch sibling group is not exactly 63 items with Custom Maps #59 followed by Lxcy's Maps #60.");

            var tempAccountingVerified = ReadU32Big(verifyZone, 8) == checked((uint)newTempSize);
            var virtualUnchanged = ReadU32Big(verifyZone, 24) == checked((uint)oldVirtualSize);
            if (!tempAccountingVerified || !virtualUnchanged)
                throw new InvalidDataException("Reopened block accounting did not preserve the alpha-4 TEMP-reserve / Virtual-unchanged invariant.");

            return new T4Stage3RelocatingAddLxcyResult(
                inputFastFile,
                outputFastFile,
                "levels_unlock",
                62,
                63,
                59,
                60,
                pointerArrayOffset,
                item.SerializedOffset,
                nextItemOffset,
                cloneLength,
                oldXFileSize,
                newXFileSize,
                oldTempSize,
                newTempSize,
                oldVirtualSize,
                levelsUnlockRuntime,
                nextMenuOwner,
                spanDelta,
                allItems.Count,
                parentPointersRelocated,
                parentsToRelocate.Count,
                verifyDocument.Assets.Count,
                true,
                true,
                true,
                true,
                true,
                tempAccountingVerified,
                virtualUnchanged,
                $"Typed relocation only: {parentPointersRelocated:N0} itemDef_s.parent field(s) across {parentsToRelocate.Count:N0} later owner group(s) moved by +0x{cloneLength:X}. Source mapping anchors={mappingAnchors.Count}; TEMP reserve 0x{oldTempSize:X}->0x{newTempSize:X} (required levels_unlock peak 0x{requiredLevelsUnlockTemp:X}); XFILE_BLOCK_VIRTUAL was not changed. Lxcy's Maps intentionally keeps Custom Maps' codxe action for this hardware structure test.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static List<T4ConsoleItemDefCandidate> ScanHighConfidenceItems(byte[] zone, T4ZoneDocument document)
    {
        var result = new List<T4ConsoleItemDefCandidate>();
        var start = checked((int)Math.Max(0, document.AssetTableOffset + document.AssetCount * 8L));
        var end = zone.Length - T4ConsoleItemDefProbe.FixedSize;

        // Alpha 4 originally skipped the next 400 bytes after every high-confidence hit. That is
        // fine for a display probe, but unsafe for relocation discovery: a rare false-positive in a
        // variable tail can jump over a real item body and make an exact N-item owner group appear
        // incomplete. Alpha 4b instead uses several cheap fixed-field gates and then probes every
        // remaining 2-byte-aligned candidate without a blind post-hit skip.
        for (var offset = start; offset <= end; offset += 2)
        {
            var type = ReadI32Big(zone, offset + 192);
            if (type is < 0 or > 0x13)
                continue;
            if (ReadI32Big(zone, offset + 20) is < 0 or > 7 ||
                ReadI32Big(zone, offset + 24) is < 0 or > 7)
                continue;

            var parent = ReadU32Big(zone, offset + 244);
            if (parent is 0 or T4ZonePointer.Following or T4ZonePointer.Insert)
                continue;

            if (!T4ConsoleItemDefProbe.TryRead(zone, offset, Endian.Big, out var candidate) || candidate is null)
                continue;
            if (candidate.StructuralChecksPassed < candidate.StructuralChecksTotal * 0.98)
                continue;

            result.Add(candidate);
        }

        return result;
    }

    private static uint? InferMenuRuntimeAddressByExactRegionProof(
        byte[] zone,
        T4MenuStructureCandidate menu,
        long regionEnd,
        IDictionary<uint, T4ConsoleItemDefCandidate[]> groups)
    {
        var regionStart = checked((int)menu.SerializedOffset);
        var regionLimit = checked((int)Math.Min(regionEnd, zone.LongLength));
        if (regionLimit <= regionStart + T4ConsoleItemDefProbe.FixedSize)
            return null;

        // Derive candidate owner values only from very-high-confidence itemDef_s bodies inside
        // this one menu's serialized region. We do not predict the runtime address from file
        // position: the source bytes themselves must prove the owner identity.
        var candidateParents = new HashSet<uint>();
        var scanStart = checked(regionStart + 312);
        var scanEnd = regionLimit - T4ConsoleItemDefProbe.FixedSize;
        for (var offset = scanStart; offset <= scanEnd; offset += 2)
        {
            var type = ReadI32Big(zone, offset + 192);
            if (type is < 0 or > 0x13)
                continue;
            if (ReadI32Big(zone, offset + 20) is < 0 or > 7 ||
                ReadI32Big(zone, offset + 24) is < 0 or > 7)
                continue;

            var parent = ReadU32Big(zone, offset + 244);
            if (parent is 0 or T4ZonePointer.Following or T4ZonePointer.Insert)
                continue;

            if (!T4ConsoleItemDefProbe.TryRead(zone, offset, Endian.Big, out var candidate) || candidate is null)
                continue;
            if (candidate.StructuralChecksPassed < candidate.StructuralChecksTotal * 0.98)
                continue;

            candidateParents.Add(parent);
        }

        var matches = new List<uint>();
        foreach (var candidateOwner in candidateParents)
        {
            var siblings = RecoverExactParentGroup(zone, candidateOwner, regionStart, regionLimit);
            if (siblings.Length != menu.ItemCount)
                continue;

            if (!TryFindInlineItemPointerArray(
                    zone,
                    menu,
                    siblings[0].SerializedOffset,
                    out _,
                    out var pointerCount) ||
                pointerCount != menu.ItemCount)
                continue;

            groups[candidateOwner] = siblings;
            matches.Add(candidateOwner);
        }

        return matches.Distinct().Count() == 1 ? matches[0] : null;
    }

    private static string DescribeMenuOwnerCandidates(
        byte[] zone,
        T4MenuStructureCandidate menu,
        long regionEnd)
    {
        var regionStart = checked((int)menu.SerializedOffset);
        var regionLimit = checked((int)Math.Min(regionEnd, zone.LongLength));
        var candidateParents = new HashSet<uint>();
        var scanStart = checked(regionStart + 312);
        var scanEnd = regionLimit - T4ConsoleItemDefProbe.FixedSize;

        for (var offset = scanStart; offset <= scanEnd; offset += 2)
        {
            var parent = ReadU32Big(zone, offset + 244);
            if (parent is 0 or T4ZonePointer.Following or T4ZonePointer.Insert)
                continue;
            if (!T4ConsoleItemDefProbe.TryRead(zone, offset, Endian.Big, out var candidate) || candidate is null)
                continue;
            if (candidate.StructuralChecksPassed < candidate.StructuralChecksTotal * 0.98)
                continue;
            candidateParents.Add(parent);
        }

        var notes = new List<string>();
        foreach (var candidateOwner in candidateParents.OrderBy(x => x))
        {
            var siblings = RecoverExactParentGroup(zone, candidateOwner, regionStart, regionLimit);
            if (siblings.Length == 0)
                continue;

            var pointerOk = TryFindInlineItemPointerArray(
                zone,
                menu,
                siblings[0].SerializedOffset,
                out var pointerOffset,
                out var pointerCount);
            notes.Add(
                $"0x{candidateOwner:X8}:siblings={siblings.Length},ptr={(pointerOk ? $"{pointerCount}@0x{pointerOffset:X}" : "no")}");
        }

        return notes.Count == 0 ? "no very-high-confidence typed item groups found in region" : string.Join("; ", notes);
    }

    private static T4ConsoleItemDefCandidate[] RecoverExactParentGroup(
        byte[] zone,
        uint parentPointer,
        int regionStart,
        int regionEnd)
    {
        var offsets = FindExactParentSiblings(zone, parentPointer, regionStart, regionEnd);
        var result = new List<T4ConsoleItemDefCandidate>(offsets.Count);
        foreach (var offset in offsets)
        {
            if (!T4ConsoleItemDefProbe.TryRead(zone, checked((int)offset), Endian.Big, out var candidate) || candidate is null)
                continue;
            if (candidate.RawParentPointer != parentPointer ||
                candidate.StructuralChecksPassed < candidate.StructuralChecksTotal * 0.98)
                continue;
            result.Add(candidate);
        }

        return result.OrderBy(x => x.SerializedOffset).ToArray();
    }

    private static bool TryFindInlineItemPointerArray(
        byte[] zone,
        T4MenuStructureCandidate menu,
        long firstSiblingOffset,
        out long arrayOffset,
        out int entryCount)
    {
        arrayOffset = 0;
        entryCount = 0;
        if (menu.ItemCount <= 0 || firstSiblingOffset <= menu.SerializedOffset)
            return false;

        var searchStart = checked((int)Math.Max(0, menu.SerializedOffset + 312));
        var searchEnd = checked((int)Math.Min(firstSiblingOffset, zone.LongLength));
        var bytesNeeded = checked(menu.ItemCount * 4);
        if (searchEnd - searchStart < bytesNeeded)
            return false;

        long? best = null;
        for (var offset = searchStart; offset <= searchEnd - bytesNeeded; offset += 2)
        {
            var allInline = true;
            for (var i = 0; i < menu.ItemCount; i++)
            {
                var value = ReadU32Big(zone, offset + i * 4);
                if (value is not (T4ZonePointer.Following or T4ZonePointer.Insert))
                {
                    allInline = false;
                    break;
                }
            }

            if (!allInline)
                continue;

            var beforeInline = offset >= 4 &&
                               ReadU32Big(zone, offset - 4) is T4ZonePointer.Following or T4ZonePointer.Insert;
            var after = offset + bytesNeeded;
            var afterInline = after + 4 <= searchEnd &&
                              ReadU32Big(zone, after) is T4ZonePointer.Following or T4ZonePointer.Insert;
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
        foreach (var candidate in menus.Where(m => m.SerializedOffset > currentOffset).OrderBy(m => m.SerializedOffset))
            return checked((int)candidate.SerializedOffset);
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
