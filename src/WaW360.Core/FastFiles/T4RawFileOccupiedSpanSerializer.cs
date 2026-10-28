using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4RawFileOccupiedAsset(
    int AssetIndex,
    long SourceOffset,
    long SourceEndOffset,
    string Name,
    int Length,
    uint SourceNamePointer,
    bool SourceNamePacked,
    bool NativePromotable,
    string Confidence,
    byte[]? XboxPayload);

public sealed class T4RawFileOccupiedSpanScan
{
    public required IReadOnlyDictionary<int, T4RawFileOccupiedAsset> AssetsByIndex { get; init; }
    public required int DeclaredPhysicalRawFiles { get; init; }
    public required int StructuralRawFiles { get; init; }
    public required int NativePromotableRawFiles { get; init; }
    public required int PackedNameRawFilesPending { get; init; }
    public required int RejectedOccupiedCandidates { get; init; }
    public required int RejectedWeakNameCandidates { get; init; }
    public required long SourceBufferBytes { get; init; }
    public required long XboxPayloadBytes { get; init; }
    public required int ReadBackErrors { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Alpha 10o RawFile recovery.
///
/// Stage 2's global RawFile signature scan is intentionally conservative, but large mixed zones
/// can contain convincing RawFile-shaped byte patterns inside already-proven XAnim/XModel/Weapon/
/// FX/Sound owned payloads.  10o subtracts those byte-verified occupied spans first, rejects weak
/// path signatures (notably a one-character "\\" false positive in Tower), and then accepts the
/// remaining unique/non-overlapping inline RawFile bodies only when their count is exact.
///
/// A second proof handles the special final-XAsset case seen in nazi_zombie_tower.ff: the final
/// RawFile reuses a packed/shared name string, but owns an inline buffer whose body starts exactly
/// where the final already-proven payload ends and consumes the decompressed zone to EOF.  That
/// body is structurally accounted for but remains native-blocked until the packed name target is
/// explicitly resolved/rebased.  No guessed name is emitted.
/// </summary>
public static class T4RawFileOccupiedSpanSerializer
{
    private const uint Following = T4ZonePointer.Following;
    private const uint Insert = T4ZonePointer.Insert;
    private const int MaxNameBytes = 1024;
    private const uint MaxRawBytes = 64u * 1024u * 1024u;

    private sealed record Candidate(long Start, long End, string Name, int Length, uint NamePointer, byte[] Buffer);

    public static T4RawFileOccupiedSpanScan Build(
        T4ZoneDocument document,
        byte[] zone,
        IReadOnlyList<(long Start, long End, string Owner)> occupiedSpans,
        CancellationToken cancellationToken = default,
        IReadOnlyList<(int AssetIndex, long Start, long End, string Owner)>? orderedAssetAnchors = null)
    {
        var warnings = new List<string>();
        var physical = document.Assets
            .Where(a => a.TypeName.Equals("rawfile", StringComparison.OrdinalIgnoreCase) &&
                        a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .OrderBy(a => a.Index)
            .ToList();

        if (physical.Count == 0)
            return Empty(0, warnings);

        if (document.Endianness != Endian.Little)
        {
            warnings.Add("alpha 10o RawFile occupied-span proof currently accepts PC/little-endian source zones only.");
            return Empty(physical.Count, warnings);
        }

        var payloadStart = checked((int)Math.Max(
            FastFileConstants.WaWZoneHeaderSize,
            document.AssetTableOffset + (long)document.AssetCount * 8L));
        var sourceStem = Path.GetFileNameWithoutExtension(document.SourceFastFile);
        var occupied = occupiedSpans
            .Where(x => x.Start >= 0 && x.End > x.Start && x.End <= zone.LongLength)
            .OrderBy(x => x.Start)
            .ToList();

        var allStrong = new List<Candidate>();
        var rejectedOccupied = 0;
        var rejectedWeak = 0;

        for (var offset = payloadStart; offset <= zone.Length - 12; offset++)
        {
            if ((offset & 0x3FFFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            var namePtr = ReadU32(zone, offset);
            if (namePtr is not (Following or Insert)) continue;
            var length = ReadU32(zone, offset + 4);
            var bufferPtr = ReadU32(zone, offset + 8);
            if (bufferPtr != 0 && bufferPtr is not (Following or Insert)) continue;
            if (length > MaxRawBytes) continue;

            if (!TryReadAsciiZ(zone, offset + 12, MaxNameBytes, out var name, out var bufferOffset)) continue;
            if (!LooksLikeStrongRawFileName(name, sourceStem))
            {
                // Count only signatures that the older permissive path rule would have admitted.
                if (!string.IsNullOrWhiteSpace(name) && (name.Contains('/') || name.Contains('\\')))
                    rejectedWeak++;
                continue;
            }

            long end;
            var total = 0L;
            if (bufferPtr == 0)
            {
                if (length != 0) continue;
                end = bufferOffset;
            }
            else
            {
                total = checked((long)length + 1L);
                if (bufferOffset < 0 || bufferOffset > zone.LongLength - total) continue;
                end = bufferOffset + total;
            }

            if (OverlapsAny(offset, end, occupied))
            {
                rejectedOccupied++;
                continue;
            }
            var buffer = bufferPtr == 0
                ? Array.Empty<byte>()
                : zone.AsSpan(bufferOffset, checked((int)total)).ToArray();
            allStrong.Add(new Candidate(offset, end, name, checked((int)length), namePtr, buffer));
        }

        var unique = allStrong
            .GroupBy(x => (x.Start, x.End, x.Name), x => x)
            .Select(g => g.First())
            .OrderBy(x => x.Start)
            .ToList();

        if (!NonOverlapping(unique.Select(x => (x.Start, x.End))))
        {
            warnings.Add($"RawFile occupied-span proof found {unique.Count:N0} surviving strong candidates but their owned bodies overlap; no 10o promotion applied.");
            return Failure(physical.Count, rejectedOccupied, rejectedWeak, warnings);
        }

        // Tower's last RawFile has a packed/shared name pointer.  Prove only the serialized body:
        // final physical XAsset, starts at the end of the last byte-verified occupied object,
        // inline buffer, and consumes the zone exactly to EOF.  The packed name remains blocked.
        Candidate? packedTail = null;
        uint packedTailNamePtr = 0;
        var maxOccupiedEnd = occupied.Count == 0 ? -1L : occupied.Max(x => x.End);
        if (physical[^1].Index == document.AssetCount - 1 && maxOccupiedEnd >= payloadStart && maxOccupiedEnd <= zone.LongLength - 12)
        {
            var start = checked((int)maxOccupiedEnd);
            var namePtr = ReadU32(zone, start);
            var length = ReadU32(zone, start + 4);
            var bufferPtr = ReadU32(zone, start + 8);
            var decoded = T4ZonePointer.Decode(namePtr);
            var packedNameValid = decoded.Kind == T4ZonePointerKind.Offset && decoded.Block is not null &&
                                  T4ZonePointer.IsWithinBlock(namePtr, document.Header);
            if (packedNameValid && length <= MaxRawBytes && bufferPtr is Following or Insert)
            {
                var end = checked((long)start + 12L + length + 1L);
                if (end == zone.LongLength)
                {
                    var buffer = zone.AsSpan(start + 12, checked((int)length + 1)).ToArray();
                    packedTailNamePtr = namePtr;
                    packedTail = new Candidate(start, end, T4ZonePointer.Describe(namePtr), checked((int)length), namePtr, buffer);
                    warnings.Add($"INFO: packed-name tail RawFile body proven at 0x{start:X8}: name={T4ZonePointer.Describe(namePtr)}, length={length:N0}, inline buffer reaches EOF exactly; name target remains unresolved and is NOT emitted.");
                }
            }
        }

        var expectedInline = physical.Count - (packedTail is null ? 0 : 1);
        var selected = unique;

        if (unique.Count != expectedInline)
        {
            // Alpha 10q: first try an authoritative mixed-XAsset order proof.
            // The Stage-4/Sound anchors carry exact source spans for known top-level
            // assets. A RawFile body must serialize after the nearest proven earlier
            // XAsset and finish before the nearest proven later XAsset. Because the
            // candidate list and RawFile slots are both in serialized/XAsset order,
            // an overcount of one can be resolved without names or hard-coded offsets
            // only when exactly one possible candidate omission yields a complete
            // ordered slot/window assignment.
            if (orderedAssetAnchors is not null && unique.Count == expectedInline + 1)
            {
                var windowProof = TrySelectByAssetOrderWindows(
                    physical.Take(expectedInline).ToList(),
                    unique,
                    orderedAssetAnchors,
                    payloadStart,
                    zone.LongLength);

                if (windowProof.Selected is not null)
                {
                    selected = windowProof.Selected;
                    warnings.Add(
                        $"INFO: RawFile XAsset-order window proof reduced {unique.Count:N0} candidate(s) to " +
                        $"{selected.Count:N0}/{expectedInline:N0}; uniquely rejected " +
                        $"0x{windowProof.Rejected!.Start:X8}..0x{windowProof.Rejected.End:X8} " +
                        $"len={windowProof.Rejected.Length:N0} name='{windowProof.Rejected.Name}'.");
                }
                else
                {
                    warnings.Add(
                        $"INFO: RawFile XAsset-order window proof could not isolate one candidate: " +
                        $"valid one-drop assignments={windowProof.ValidAssignments:N0}; anchors={windowProof.AnchorCount:N0}.");
                }
            }

            if (selected.Count == expectedInline)
            {
                // Window proof closed the count exactly; continue to independent
                // Xbox payload read-back below.
            }
            else
            {
            // Alpha 10p: the older conservative RawFile scanner can recover an exact
            // serialized continuation chain even when the global signature scan has
            // extra strong-looking candidates. Apply the same proof only after the
            // occupied-span subtraction above, and only when exactly one chain has the
            // XAsset-declared length. This removes stray signatures without any
            // map-specific names/offsets or guessed index mapping.
            var exactChains = BuildContinuationChains(unique, zone)
                .Where(chain => chain.Count == expectedInline)
                .ToList();

            if (exactChains.Count == 1)
            {
                selected = exactChains[0]
                    .OrderBy(x => x.Start)
                    .ToList();

                var selectedKeys = selected
                    .Select(x => (x.Start, x.End, x.Name))
                    .ToHashSet();
                var dropped = unique
                    .Where(x => !selectedKeys.Contains((x.Start, x.End, x.Name)))
                    .OrderBy(x => x.Start)
                    .ToList();

                warnings.Add(
                    $"INFO: RawFile occupied-span continuation proof reduced {unique.Count:N0} surviving strong candidate(s) " +
                    $"to one authoritative {expectedInline:N0}-entry serialized chain matching the XAsset declaration.");
                foreach (var d in dropped.Take(16))
                    warnings.Add(
                        $"INFO: RawFile continuation proof rejected stray candidate " +
                        $"0x{d.Start:X8}..0x{d.End:X8} len={d.Length:N0} name='{d.Name}' namePtr=0x{d.NamePointer:X8}.");
                if (dropped.Count > 16)
                    warnings.Add($"INFO: RawFile continuation proof rejected {dropped.Count - 16:N0} additional stray candidate(s).");
            }
            else
            {
                var delta = unique.Count - expectedInline;
                warnings.Add(
                    $"RawFile occupied-span exact-count gate: XAssetList declares {physical.Count:N0}; " +
                    $"surviving strong inline bodies={unique.Count:N0}; packed-tail bodies={(packedTail is null ? 0 : 1):N0}. " +
                    $"Expected {expectedInline:N0} inline body(ies); exact continuation chains={exactChains.Count:N0}. Type remains blocked.");
                warnings.Add(delta > 0
                    ? $"DIAGNOSTIC: RawFile candidate OVERCOUNT by {delta:N0}; continuation proof could not isolate one unique declared-length chain."
                    : $"DIAGNOSTIC: RawFile candidate UNDERCOUNT by {-delta:N0}; at least one declared RawFile body is not represented by the current strong inline-name signature rule.");
                var cidx = 0;
                foreach (var candidate in unique.Take(96))
                    warnings.Add($"CANDIDATE[{cidx++:D2}] 0x{candidate.Start:X8}..0x{candidate.End:X8} len={candidate.Length:N0} name='{candidate.Name}' namePtr=0x{candidate.NamePointer:X8}");
                if (unique.Count > 96)
                    warnings.Add($"CANDIDATE LIST TRUNCATED: {unique.Count - 96:N0} additional candidate(s).");
                return Failure(
                    physical.Count,
                    rejectedOccupied,
                    rejectedWeak,
                    warnings,
                    structural: unique.Count + (packedTail is null ? 0 : 1));
            }
            }
        }

        var result = new Dictionary<int, T4RawFileOccupiedAsset>();
        var readBackErrors = 0;
        long sourceBytes = 0;
        long xboxBytes = 0;

        for (var i = 0; i < selected.Count; i++)
        {
            var asset = physical[i];
            var c = selected[i];
            var payload = BuildXboxPayload(c);
            if (!VerifyXboxPayload(payload, c)) readBackErrors++;
            result[asset.Index] = new T4RawFileOccupiedAsset(
                asset.Index, c.Start, c.End, c.Name, c.Length, c.NamePointer,
                false, true, "strong inline RawFile signature outside byte-verified occupied spans; exact type count", payload);
            sourceBytes += c.Buffer.LongLength;
            xboxBytes += payload.LongLength;
        }

        if (packedTail is not null)
        {
            var a = physical[^1];
            result[a.Index] = new T4RawFileOccupiedAsset(
                a.Index, packedTail.Start, packedTail.End, packedTail.Name, packedTail.Length,
                packedTailNamePtr, true, false,
                "final physical RawFile consumes source tail to EOF; packed/shared name target still requires explicit resolution/rebase", null);
            sourceBytes += packedTail.Buffer.LongLength;
        }

        var structural = result.Count;
        var promotable = result.Values.Count(x => x.NativePromotable);
        var packedPending = result.Values.Count(x => x.SourceNamePacked && !x.NativePromotable);
        var verified = structural == physical.Count && promotable == expectedInline && readBackErrors == 0;
        if (verified)
            warnings.Add($"INFO: RawFile occupied-span accounting proved {structural:N0}/{physical.Count:N0} physical bod{(physical.Count == 1 ? "y" : "ies")}: native-promotable={promotable:N0}, packed-name pending={packedPending:N0}.");

        return new T4RawFileOccupiedSpanScan
        {
            AssetsByIndex = result,
            DeclaredPhysicalRawFiles = physical.Count,
            StructuralRawFiles = structural,
            NativePromotableRawFiles = promotable,
            PackedNameRawFilesPending = packedPending,
            RejectedOccupiedCandidates = rejectedOccupied,
            RejectedWeakNameCandidates = rejectedWeak,
            SourceBufferBytes = sourceBytes,
            XboxPayloadBytes = xboxBytes,
            ReadBackErrors = readBackErrors,
            Verified = verified,
            Warnings = warnings
        };
    }

    private static byte[] BuildXboxPayload(Candidate c)
    {
        using var ms = new MemoryStream(12 + c.Name.Length + 1 + c.Buffer.Length);
        WriteU32Be(ms, Following);
        WriteU32Be(ms, checked((uint)c.Length));
        WriteU32Be(ms, c.Buffer.Length == 0 ? 0u : Following);
        ms.Write(Encoding.ASCII.GetBytes(c.Name));
        ms.WriteByte(0);
        if (c.Buffer.Length != 0) ms.Write(c.Buffer);
        return ms.ToArray();
    }

    private static bool VerifyXboxPayload(byte[] payload, Candidate source)
    {
        if (payload.Length < 13) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(0, 4)) != Following) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(4, 4)) != (uint)source.Length) return false;
        var expectedBufferPtr = source.Buffer.Length == 0 ? 0u : Following;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(8, 4)) != expectedBufferPtr) return false;
        var zero = Array.IndexOf(payload, (byte)0, 12);
        if (zero < 12) return false;
        var name = Encoding.ASCII.GetString(payload, 12, zero - 12);
        if (!name.Equals(source.Name, StringComparison.Ordinal)) return false;
        var bufferStart = zero + 1;
        if (payload.Length - bufferStart != source.Buffer.Length) return false;
        return payload.AsSpan(bufferStart).SequenceEqual(source.Buffer);
    }

    private static bool LooksLikeStrongRawFileName(string name, string sourceStem)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 512 || !name.All(c => c is >= ' ' and <= '~')) return false;
        if (name.Equals(sourceStem, StringComparison.OrdinalIgnoreCase)) return true;

        // A bare slash/backslash is a common binary false positive.  Paths must contain
        // at least one alphanumeric path component.
        if ((name.Contains('/') || name.Contains('\\')) && name.Any(char.IsLetterOrDigit)) return true;

        var ext = Path.GetExtension(name);
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

    private static bool TryReadAsciiZ(byte[] data, int offset, int maxBytes, out string value, out int next)
    {
        value = string.Empty;
        next = offset;
        if (offset < 0 || offset >= data.Length) return false;
        var end = offset;
        var limit = Math.Min(data.Length, offset + maxBytes + 1);
        while (end < limit && data[end] != 0) end++;
        if (end >= limit || end >= data.Length) return false;
        for (var i = offset; i < end; i++)
            if (data[i] is < 0x20 or > 0x7E) return false;
        value = Encoding.ASCII.GetString(data, offset, end - offset);
        next = end + 1;
        return true;
    }

    private sealed record AssetWindowSelection(
        List<Candidate>? Selected,
        Candidate? Rejected,
        int ValidAssignments,
        int AnchorCount);

    private static AssetWindowSelection TrySelectByAssetOrderWindows(
        IReadOnlyList<T4AssetEntry> rawSlots,
        IReadOnlyList<Candidate> candidates,
        IReadOnlyList<(int AssetIndex, long Start, long End, string Owner)> anchors,
        long payloadStart,
        long zoneLength)
    {
        var orderedAnchors = anchors
            .Where(a => a.AssetIndex >= 0 &&
                        a.Start >= payloadStart &&
                        a.End > a.Start &&
                        a.End <= zoneLength)
            .OrderBy(a => a.AssetIndex)
            .ThenBy(a => a.Start)
            .ToList();

        if (rawSlots.Count == 0 ||
            candidates.Count != rawSlots.Count + 1 ||
            orderedAnchors.Count < 2)
            return new AssetWindowSelection(null, null, 0, orderedAnchors.Count);

        // Trust the anchor/index relation only if source offsets preserve XAsset order.
        long previousStart = -1;
        long previousEnd = -1;
        var previousAssetIndex = -1;
        foreach (var a in orderedAnchors)
        {
            if (a.AssetIndex <= previousAssetIndex ||
                a.Start < previousStart ||
                a.Start < previousEnd)
                return new AssetWindowSelection(null, null, 0, orderedAnchors.Count);

            previousAssetIndex = a.AssetIndex;
            previousStart = a.Start;
            previousEnd = a.End;
        }

        var windows = new List<(long Low, long High)>(rawSlots.Count);
        foreach (var slot in rawSlots.OrderBy(x => x.Index))
        {
            var prev = orderedAnchors.LastOrDefault(a => a.AssetIndex < slot.Index);
            var next = orderedAnchors.FirstOrDefault(a => a.AssetIndex > slot.Index);

            var low = prev == default ? payloadStart : prev.End;
            var high = next == default ? zoneLength : next.Start;
            if (low < payloadStart) low = payloadStart;
            if (high > zoneLength) high = zoneLength;
            if (high <= low)
                return new AssetWindowSelection(null, null, 0, orderedAnchors.Count);

            windows.Add((low, high));
        }

        var orderedCandidates = candidates.OrderBy(x => x.Start).ToList();
        var valid = new List<(List<Candidate> Selected, Candidate Rejected)>();

        for (var drop = 0; drop < orderedCandidates.Count; drop++)
        {
            var chosen = new List<Candidate>(rawSlots.Count);
            for (var i = 0; i < orderedCandidates.Count; i++)
                if (i != drop) chosen.Add(orderedCandidates[i]);

            var fits = true;
            for (var i = 0; i < chosen.Count; i++)
            {
                var c = chosen[i];
                var w = windows[i];
                if (c.Start < w.Low || c.End > w.High)
                {
                    fits = false;
                    break;
                }
            }

            if (fits)
                valid.Add((chosen, orderedCandidates[drop]));
        }

        return valid.Count == 1
            ? new AssetWindowSelection(valid[0].Selected, valid[0].Rejected, 1, orderedAnchors.Count)
            : new AssetWindowSelection(null, null, valid.Count, orderedAnchors.Count);
    }

    private static List<List<Candidate>> BuildContinuationChains(
        IReadOnlyList<Candidate> candidates,
        byte[] zone)
    {
        var ordered = candidates.OrderBy(x => x.Start).ToList();
        var byStart = ordered
            .GroupBy(x => x.Start)
            .ToDictionary(g => g.Key, g => g.ToList());
        var chains = new List<List<Candidate>>();

        foreach (var first in ordered)
        {
            var chain = new List<Candidate> { first };
            var current = first;
            var seenStarts = new HashSet<long> { first.Start };

            while (true)
            {
                var next = CandidateContinuationOffsets(zone, current.End)
                    .Where(byStart.ContainsKey)
                    .SelectMany(offset => byStart[offset])
                    .Where(x => x.Start > current.Start)
                    .OrderBy(x => x.Start)
                    .ToList();

                if (next.Count != 1)
                    break;

                var chosen = next[0];
                if (!seenStarts.Add(chosen.Start))
                    break;

                chain.Add(chosen);
                current = chosen;
            }

            chains.Add(chain);
        }

        return chains;
    }

    private static IReadOnlyList<long> CandidateContinuationOffsets(byte[] zone, long endOffset)
    {
        var output = new List<long>();
        if (endOffset < 0 || endOffset > zone.LongLength)
            return output;

        output.Add(endOffset);
        foreach (var alignment in new[] { 4L, 8L, 16L })
        {
            var aligned = checked((endOffset + alignment - 1) & ~(alignment - 1));
            if (aligned == endOffset || aligned > zone.LongLength)
                continue;

            var zeroPad = true;
            for (var p = endOffset; p < aligned; p++)
            {
                if (zone[checked((int)p)] == 0)
                    continue;
                zeroPad = false;
                break;
            }

            if (zeroPad)
                output.Add(aligned);
        }

        return output.Distinct().OrderBy(x => x).ToList();
    }

    private static bool OverlapsAny(long start, long end, IReadOnlyList<(long Start, long End, string Owner)> spans)
    {
        foreach (var s in spans)
        {
            if (s.Start >= end) break;
            if (start < s.End && end > s.Start) return true;
        }
        return false;
    }

    private static bool NonOverlapping(IEnumerable<(long Start, long End)> spans)
    {
        long previousEnd = -1;
        foreach (var s in spans.OrderBy(x => x.Start))
        {
            if (s.Start < 0 || s.End <= s.Start || s.Start < previousEnd) return false;
            previousEnd = s.End;
        }
        return true;
    }

    private static uint ReadU32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static void WriteU32Be(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }

    private static T4RawFileOccupiedSpanScan Empty(int declared, IReadOnlyList<string> warnings) => new()
    {
        AssetsByIndex = new Dictionary<int, T4RawFileOccupiedAsset>(),
        DeclaredPhysicalRawFiles = declared,
        StructuralRawFiles = 0,
        NativePromotableRawFiles = 0,
        PackedNameRawFilesPending = 0,
        RejectedOccupiedCandidates = 0,
        RejectedWeakNameCandidates = 0,
        SourceBufferBytes = 0,
        XboxPayloadBytes = 0,
        ReadBackErrors = 0,
        Verified = declared == 0,
        Warnings = warnings
    };

    private static T4RawFileOccupiedSpanScan Failure(
        int declared, int rejectedOccupied, int rejectedWeak, IReadOnlyList<string> warnings, int structural = 0) => new()
    {
        AssetsByIndex = new Dictionary<int, T4RawFileOccupiedAsset>(),
        DeclaredPhysicalRawFiles = declared,
        StructuralRawFiles = structural,
        NativePromotableRawFiles = 0,
        PackedNameRawFilesPending = 0,
        RejectedOccupiedCandidates = rejectedOccupied,
        RejectedWeakNameCandidates = rejectedWeak,
        SourceBufferBytes = 0,
        XboxPayloadBytes = 0,
        ReadBackErrors = 0,
        Verified = false,
        Warnings = warnings
    };
}
