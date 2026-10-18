using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Stage 4 alpha 9i diagnostic-only microscope for authoritative XAsset slots
/// that remain unresolved after the strict normal + packed-name discovery paths.
/// It never contributes candidates to serialization. It only records the exact
/// mixed-order gap and relaxed structural signatures so a new representation can
/// be characterized without weakening the accepted parser.
/// </summary>
public static class T4AssetGapMicroscope
{
    private const int XModelFixedSize = 228;
    private const int XAnimFixedSize = 88;
    private const int BoundaryDumpBytes = 1024;
    private const int CandidateDumpBytes = 320;
    private const int MaxRelaxedCandidatesPerWindow = 96;
    private const int MaxStringsPerWindow = 160;
    private const int ExactGapCaptureLimit = 32 * 1024 * 1024;
    private const int LargeGapSliceBytes = 16 * 1024 * 1024;

    private sealed record RelaxedCandidate(int Offset, int Score, string Summary);

    public static string WriteReport(
        byte[] zone,
        T4ZoneDocument document,
        string sourceFastFile,
        string targetType,
        int unresolvedCount,
        IReadOnlyList<T4AssetGapWindow> missingWindows,
        string reportFile,
        CancellationToken cancellationToken = default,
        bool includeGlobalRejectedScan = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile))!);
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 9i focused asset forensics");
        sb.AppendLine("=================================================================");
        sb.AppendLine("DIAGNOSTIC ONLY: this report never accepts or serializes a candidate.");
        sb.AppendLine($"Source: {sourceFastFile}");
        sb.AppendLine($"Target type: {targetType}");
        sb.AppendLine($"Unresolved authoritative {targetType} count: {unresolvedCount:N0}");
        sb.AppendLine($"Alignment-assigned physical windows: {missingWindows.Count:N0}");
        sb.AppendLine($"Zone bytes: {zone.Length:N0}");
        sb.AppendLine($"Header: {document.Header}");
        sb.AppendLine();

        var unique = missingWindows
            .GroupBy(w => (w.Start, w.End))
            .OrderBy(g => g.Key.Start)
            .ToList();

        for (var groupIndex = 0; groupIndex < unique.Count; groupIndex++)
        {
            var group = unique[groupIndex].ToList();
            var start = Math.Max(0, group[0].Start);
            var end = Math.Min(zone.Length, group[0].End);
            var len = Math.Max(0, end - start);

            sb.AppendLine($"GAP {groupIndex + 1}/{unique.Count}: 0x{start:X8}-0x{end:X8} ({len:N0} bytes)");
            sb.AppendLine($"  unresolved slots in this physical gap: {group.Count}");
            foreach (var w in group)
            {
                sb.AppendLine($"  - XAsset #{w.AssetIndex} {w.TypeName} (filtered expected index {w.ExpectedIndex})");
                sb.AppendLine($"    prev: #{Fmt(w.PreviousAssetIndex)} {w.PreviousType ?? "-"} start={Hex(w.PreviousStart)} end={Hex(w.PreviousEnd)}");
                sb.AppendLine($"    next: #{Fmt(w.NextAssetIndex)} {w.NextType ?? "-"} start={Hex(w.NextStart)} end={Hex(w.NextEnd)}");
                sb.AppendLine($"    table: {w.TableContext}");
            }
            sb.AppendLine();

            AppendGapCapture(sb, zone, reportFile, groupIndex + 1, start, end);
            sb.AppendLine();

            sb.AppendLine("  Boundary bytes — gap start");
            AppendHexDump(sb, zone, start, Math.Min(BoundaryDumpBytes, len), "    ");
            if (len > BoundaryDumpBytes)
            {
                sb.AppendLine("  Boundary bytes — gap end");
                var tailStart = Math.Max(start, end - BoundaryDumpBytes);
                AppendHexDump(sb, zone, tailStart, end - tailStart, "    ");
            }
            sb.AppendLine();

            var candidates = targetType.Equals("xmodel", StringComparison.OrdinalIgnoreCase)
                ? ScanRelaxedXModels(zone, document.Header, start, end)
                : ScanRelaxedXAnims(zone, document.Header, start, end);

            sb.AppendLine($"  Relaxed {targetType} header candidates: {candidates.Count:N0}");
            foreach (var c in candidates.Take(MaxRelaxedCandidatesPerWindow))
            {
                sb.AppendLine($"  CANDIDATE 0x{c.Offset:X8} score={c.Score}: {c.Summary}");
                AppendPointerWords(sb, zone, document.Header, c.Offset,
                    Math.Min(targetType.Equals("xmodel", StringComparison.OrdinalIgnoreCase) ? XModelFixedSize : XAnimFixedSize, zone.Length - c.Offset));
                sb.AppendLine("    bytes:");
                AppendHexDump(sb, zone, c.Offset, Math.Min(CandidateDumpBytes, zone.Length - c.Offset), "      ");
            }
            if (candidates.Count > MaxRelaxedCandidatesPerWindow)
                sb.AppendLine($"  ... {candidates.Count - MaxRelaxedCandidatesPerWindow:N0} additional relaxed candidates omitted");
            sb.AppendLine();

            sb.AppendLine($"  Focused strict-rejection reasons inside this expanded-anchor run");
            if (targetType.Equals("xmodel", StringComparison.OrdinalIgnoreCase))
            {
                var rejected = T4XModelSerializerProof.DiscoverRejectedInlineNameCandidates(
                    zone, Endian.Little, document.Header, start, end, cancellationToken);
                sb.AppendLine($"    rejected inline-name XModel shapes: {rejected.Count:N0}");
                foreach (var g in rejected.GroupBy(x => NormalizeReason(x.Reason)).OrderByDescending(g => g.Count()).Take(16))
                    sb.AppendLine($"      {g.Count(),5:N0}  {g.Key}");
                foreach (var c in rejected.Take(48))
                    sb.AppendLine($"      0x{c.Offset:X8} '{c.Name}' -> {c.Reason}");
            }
            else
            {
                var rejected = T4XAnimSerializerProof.DiscoverRejectedInlineNameCandidates(
                    zone, Endian.Little, document.Header, start, end, cancellationToken);
                sb.AppendLine($"    rejected inline-name XAnim shapes: {rejected.Count:N0}");
                foreach (var g in rejected.GroupBy(x => NormalizeReason(x.Reason)).OrderByDescending(g => g.Count()).Take(16))
                    sb.AppendLine($"      {g.Count(),5:N0}  {g.Key}");
                foreach (var c in rejected.Take(48))
                    sb.AppendLine($"      0x{c.Offset:X8} '{c.Name}' -> {c.Reason}");
            }
            sb.AppendLine();

            var strings = ScanAsciiStrings(zone, start, end, candidates.Select(c => c.Offset).ToArray());
            sb.AppendLine($"  Nearby/plausible ASCII strings: {strings.Count:N0}");
            foreach (var item in strings.Take(MaxStringsPerWindow))
                sb.AppendLine($"    0x{item.Offset:X8}: '{item.Text}'");
            if (strings.Count > MaxStringsPerWindow)
                sb.AppendLine($"    ... {strings.Count - MaxStringsPerWindow:N0} additional strings omitted");
            sb.AppendLine();

            sb.AppendLine("  32-bit word classification at the first 512 gap bytes");
            AppendPointerWords(sb, zone, document.Header, start, Math.Min(512, len));
            sb.AppendLine();
        }


        if (includeGlobalRejectedScan)
        {
        sb.AppendLine("GLOBAL REJECTED INLINE-NAME FORENSICS");
        sb.AppendLine("  This scan does NOT rely on the dynamic-programming choice of which repeated table slot is missing.");
        sb.AppendLine("  It searches the entire serialized asset payload for bodies with the normal inline top-level name placement");
        sb.AppendLine("  that were nevertheless rejected by the strict typed scanner, and records the first rejecting rule.");
        sb.AppendLine();

        var payloadStart = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));
        if (targetType.Equals("xmodel", StringComparison.OrdinalIgnoreCase))
        {
            var rejected = T4XModelSerializerProof.DiscoverRejectedInlineNameCandidates(
                zone, Endian.Little, document.Header, payloadStart, zone.Length, cancellationToken);
            sb.AppendLine($"  Rejected inline-name XModel candidates: {rejected.Count:N0}");
            var reasonGroups = rejected
                .GroupBy(x => NormalizeReason(x.Reason))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(32)
                .ToList();
            foreach (var g in reasonGroups)
                sb.AppendLine($"    {g.Count(),5:N0}  {g.Key}");
            sb.AppendLine();

            foreach (var c in rejected.Take(160))
            {
                sb.AppendLine($"  REJECTED XMODEL 0x{c.Offset:X8} name='{c.Name}' bones={c.BoneCount} roots={c.RootBoneCount} surfs={c.SurfaceCount} lodRamp={c.LodRamp} lods={c.LodCount} collLod={c.CollisionLod}");
                sb.AppendLine($"    reject: {c.Reason}");
                AppendRejectedCapture(sb, zone, reportFile, "xmodel", c.Offset, XModelFixedSize, c.Name);
            }
            if (rejected.Count > 160)
                sb.AppendLine($"  ... {rejected.Count - 160:N0} additional rejected inline-name XModel candidates omitted");
        }
        else
        {
            var rejected = T4XAnimSerializerProof.DiscoverRejectedInlineNameCandidates(
                zone, Endian.Little, document.Header, payloadStart, zone.Length, cancellationToken);
            sb.AppendLine($"  Rejected inline-name XAnim candidates: {rejected.Count:N0}");
            var reasonGroups = rejected
                .GroupBy(x => NormalizeReason(x.Reason))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(32)
                .ToList();
            foreach (var g in reasonGroups)
                sb.AppendLine($"    {g.Count(),5:N0}  {g.Key}");
            sb.AppendLine();

            foreach (var c in rejected.Take(160))
            {
                sb.AppendLine($"  REJECTED XANIM 0x{c.Offset:X8} name='{c.Name}' frames={c.FrameCount} bones={c.BoneCount} notify={c.NotifyCount} fps={c.FrameRate:G6} freq={c.Frequency:G6}");
                sb.AppendLine($"    reject: {c.Reason}");
                AppendRejectedCapture(sb, zone, reportFile, "xanim", c.Offset, XAnimFixedSize, c.Name);
            }
            if (rejected.Count > 160)
                sb.AppendLine($"  ... {rejected.Count - 160:N0} additional rejected inline-name XAnim candidates omitted");
        }
        sb.AppendLine();
        }
        else
        {
            sb.AppendLine("GLOBAL REJECTED INLINE-NAME FORENSICS");
            sb.AppendLine("  <disabled in alpha 9i: prior whole-zone scan produced thousands of shader/material false positives>");
            sb.AppendLine("  Focused relaxed scans above are bounded by expanded non-target XAsset anchors instead.");
            sb.AppendLine();
        }

        sb.AppendLine("Interpretation");
        sb.AppendLine("  The authoritative mixed XAsset order says each listed slot exists between the recorded trusted anchors.");
        sb.AppendLine("  Relaxed candidates are hints only: they intentionally permit field patterns that the accepted serializer rejects.");
        sb.AppendLine("  Compare repeated high-scoring shapes across failing maps before adding a new parser rule. Never promote a candidate from this report by count alone.");
        sb.AppendLine("  If the same alternate header/pointer layout repeats across independent maps, implement it as a structural fail-closed representation and re-run the full regression set before native FF emission.");

        File.WriteAllText(reportFile, sb.ToString(), Encoding.UTF8);
        return reportFile;
    }

    private static List<RelaxedCandidate> ScanRelaxedXModels(byte[] zone, ZoneHeaderSummary header, int start, int end)
    {
        var result = new List<RelaxedCandidate>();
        var last = Math.Min(end, zone.Length) - XModelFixedSize;
        for (var offset = Math.Max(0, start); offset <= last; offset++)
        {
            var bones = zone[offset + 4];
            var roots = zone[offset + 5];
            var surfs = zone[offset + 6];
            var lodRamp = zone[offset + 7];
            var numLods = ReadU16(zone, offset + 196);
            var collLod = ReadI16(zone, offset + 198);
            var flags209 = zone[offset + 209];

            var score = 0;
            if (roots <= bones) score += 2;
            if (surfs <= 128) score += 2;
            if ((bones == 0) == (surfs == 0)) score += 2;
            if (lodRamp <= 1) score += 1;
            if (numLods <= 4) score += 2;
            if (collLod >= -1 && collLod <= 3) score += 2;
            if (flags209 <= 1) score += 1;

            var radius = ReadF32(zone, offset + 168);
            if (Finite(radius) && radius >= 0f && radius <= 10_000_000f) score += 2;
            var finiteBounds = true;
            for (var p = 172; p <= 192; p += 4)
            {
                var f = ReadF32(zone, offset + p);
                if (!Finite(f) || f < -100_000_000f || f > 100_000_000f) { finiteBounds = false; break; }
            }
            if (finiteBounds) score += 2;

            var pointerOffsets = new[] { 0, 8, 12, 16, 20, 24, 28, 32, 36, 152, 164, 212, 216, 220, 224 };
            var validPointers = 0;
            foreach (var p in pointerOffsets)
                if (PointerLooksSerialized(ReadU32(zone, offset + p), header)) validPointers++;
            score += validPointers / 3;

            if (score < 13) continue;
            var name = ReadU32(zone, offset);
            AddTopCandidate(result, new RelaxedCandidate(offset, score,
                $"name={DescribePointer(name, header)} bones={bones} roots={roots} surfs={surfs} lodRamp={lodRamp} numLods={numLods} collLod={collLod} ptrs={validPointers}/{pointerOffsets.Length} radius={radius:G6}"));
        }
        return result.OrderByDescending(x => x.Score).ThenBy(x => x.Offset).ToList();
    }

    private static List<RelaxedCandidate> ScanRelaxedXAnims(byte[] zone, ZoneHeaderSummary header, int start, int end)
    {
        var result = new List<RelaxedCandidate>();
        var last = Math.Min(end, zone.Length) - XAnimFixedSize;
        for (var offset = Math.Max(0, start); offset <= last; offset++)
        {
            var score = 0;
            var frames = ReadU16(zone, offset + 14);
            if (frames <= 10000) score += 3;
            if (zone[offset + 16] <= 1) score += 1;
            if (zone[offset + 17] <= 1) score += 1;
            if (zone[offset + 30] <= 1) score += 1;

            var framerate = ReadF32(zone, offset + 40);
            var frequency = ReadF32(zone, offset + 44);
            if (Finite(framerate) && framerate >= 0f && framerate <= 1000f) score += 2;
            if (Finite(frequency) && frequency >= 0f && frequency <= 1000f) score += 2;

            var validPointers = 0;
            for (var p = 48; p <= 84; p += 4)
                if (PointerLooksSerialized(ReadU32(zone, offset + p), header)) validPointers++;
            score += validPointers / 2;

            var name = ReadU32(zone, offset);
            if (PointerLooksSerialized(name, header)) score += 2;
            if (score < 12) continue;

            AddTopCandidate(result, new RelaxedCandidate(offset, score,
                $"name={DescribePointer(name, header)} frames={frames} flags16={zone[offset + 16]} flags17={zone[offset + 17]} flag30={zone[offset + 30]} fps={framerate:G6} freq={frequency:G6} ptrs={validPointers}/10"));
        }
        return result.OrderByDescending(x => x.Score).ThenBy(x => x.Offset).ToList();
    }

    private static void AddTopCandidate(List<RelaxedCandidate> result, RelaxedCandidate candidate)
    {
        var cap = MaxRelaxedCandidatesPerWindow * 4;
        if (result.Count < cap)
        {
            result.Add(candidate);
            return;
        }

        var weakestIndex = 0;
        for (var i = 1; i < result.Count; i++)
        {
            if (result[i].Score < result[weakestIndex].Score ||
                (result[i].Score == result[weakestIndex].Score && result[i].Offset > result[weakestIndex].Offset))
                weakestIndex = i;
        }
        var weakest = result[weakestIndex];
        if (candidate.Score > weakest.Score || (candidate.Score == weakest.Score && candidate.Offset < weakest.Offset))
            result[weakestIndex] = candidate;
    }

    private static List<(int Offset, string Text)> ScanAsciiStrings(byte[] zone, int start, int end, IReadOnlyList<int> candidateOffsets)
    {
        var results = new List<(int, string)>();
        var lo = Math.Max(0, start);
        var hi = Math.Min(zone.Length, end);
        var i = lo;
        while (i < hi)
        {
            if (!Printable(zone[i])) { i++; continue; }
            var s = i;
            while (i < hi && Printable(zone[i]) && i - s < 192) i++;
            var len = i - s;
            if (len >= 4)
            {
                var text = Encoding.ASCII.GetString(zone, s, len);
                if (text.Any(char.IsLetter) && (LooksAssetish(text) || NearAny(s, candidateOffsets, 0x10000)))
                    if (results.Count < MaxStringsPerWindow * 4) results.Add((s, text));
            }
            if (i == s) i++;
        }
        return results.Take(MaxStringsPerWindow * 4).ToList();
    }

    private static bool NearAny(int offset, IReadOnlyList<int> xs, int distance)
    {
        foreach (var x in xs) if (Math.Abs((long)offset - x) <= distance) return true;
        return false;
    }

    private static bool LooksAssetish(string s) =>
        s.Contains('/') || s.Contains('\\') || s.Contains('_') || s.Contains('.') ||
        s.Contains("model", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("anim", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("zombie", StringComparison.OrdinalIgnoreCase);

    private static bool Printable(byte b) => b >= 0x20 && b <= 0x7E;

    private static void AppendGapCapture(StringBuilder sb, byte[] zone, string reportFile, int gapNumber, int start, int end)
    {
        var length = Math.Max(0, end - start);
        var dir = Path.GetDirectoryName(Path.GetFullPath(reportFile))!;
        var stem = Path.GetFileNameWithoutExtension(reportFile);
        if (length <= ExactGapCaptureLimit)
        {
            var path = Path.Combine(dir, $"{stem}.gap{gapNumber:D2}_0x{start:X8}-0x{end:X8}.bin");
            using var fs = File.Create(path);
            fs.Write(zone, start, length);
            sb.AppendLine($"  Raw gap capture: {path} ({length:N0} bytes, exact)");
            return;
        }

        var headLength = Math.Min(LargeGapSliceBytes, length);
        var tailLength = Math.Min(LargeGapSliceBytes, Math.Max(0, length - headLength));
        var headPath = Path.Combine(dir, $"{stem}.gap{gapNumber:D2}_HEAD_0x{start:X8}.bin");
        using (var fs = File.Create(headPath)) fs.Write(zone, start, headLength);
        sb.AppendLine($"  Raw gap capture: {headPath} ({headLength:N0} bytes, head slice)");
        if (tailLength > 0)
        {
            var tailStart = end - tailLength;
            var tailPath = Path.Combine(dir, $"{stem}.gap{gapNumber:D2}_TAIL_0x{tailStart:X8}.bin");
            using var fs = File.Create(tailPath);
            fs.Write(zone, tailStart, tailLength);
            sb.AppendLine($"  Raw gap capture: {tailPath} ({tailLength:N0} bytes, tail slice)");
        }
        sb.AppendLine($"  NOTE: full gap is {length:N0} bytes; capture is sliced because it exceeds {ExactGapCaptureLimit:N0} bytes.");
    }

    private static void AppendPointerWords(StringBuilder sb, byte[] zone, ZoneHeaderSummary header, int start, int length)
    {
        var end = Math.Min(zone.Length, checked(start + Math.Max(0, length)));
        for (var p = Math.Max(0, start); p <= end - 4; p += 4)
        {
            var raw = ReadU32(zone, p);
            var decoded = T4ZonePointer.Decode(raw);
            var interesting = raw == 0 || raw == T4ZonePointer.Following || raw == T4ZonePointer.Insert || decoded.Kind == T4ZonePointerKind.Offset;
            if (!interesting) continue;
            var inBounds = PointerLooksSerialized(raw, header);
            sb.AppendLine($"    +0x{p - start:X4} @0x{p:X8}: 0x{raw:X8} -> {decoded} bounds={(inBounds ? "yes" : "NO")}");
        }
    }

    private static void AppendHexDump(StringBuilder sb, byte[] data, int start, int length, string indent)
    {
        var end = Math.Min(data.Length, checked(start + Math.Max(0, length)));
        for (var p = start; p < end; p += 16)
        {
            var count = Math.Min(16, end - p);
            var hex = new StringBuilder();
            var ascii = new StringBuilder();
            for (var i = 0; i < 16; i++)
            {
                if (i < count)
                {
                    var b = data[p + i];
                    hex.Append(b.ToString("X2")).Append(' ');
                    ascii.Append(Printable(b) ? (char)b : '.');
                }
                else
                {
                    hex.Append("   ");
                    ascii.Append(' ');
                }
            }
            sb.AppendLine($"{indent}0x{p:X8}: {hex}|{ascii}|");
        }
    }

    private static bool PointerLooksSerialized(uint raw, ZoneHeaderSummary header)
    {
        if (raw == 0 || raw == T4ZonePointer.Following || raw == T4ZonePointer.Insert) return true;
        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null) return false;
        var block = (int)decoded.Block.Value;
        return block >= 0 && block < header.BlockSizes.Count && decoded.Offset < header.BlockSizes[block];
    }

    private static string DescribePointer(uint raw, ZoneHeaderSummary header) =>
        $"0x{raw:X8}/{T4ZonePointer.Decode(raw)}/{(PointerLooksSerialized(raw, header) ? "in-bounds" : "out-of-bounds")}";


    private static void AppendRejectedCapture(
        StringBuilder sb,
        byte[] zone,
        string reportFile,
        string kind,
        int offset,
        int fixedSize,
        string name)
    {
        var start = Math.Max(0, offset - 64);
        var end = Math.Min(zone.Length, offset + fixedSize + Math.Max(256, Math.Min(1024, name.Length + 256)));
        var length = Math.Max(0, end - start);
        var safe = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_').Take(48).ToArray());
        if (string.IsNullOrWhiteSpace(safe)) safe = "unnamed";
        var dir = Path.GetDirectoryName(Path.GetFullPath(reportFile))!;
        var stem = Path.GetFileNameWithoutExtension(reportFile);
        var path = Path.Combine(dir, $"{stem}.rejected_{kind}_0x{offset:X8}_{safe}.bin");
        using (var fs = File.Create(path)) fs.Write(zone, start, length);
        sb.AppendLine($"    raw: {path} (capture 0x{start:X8}-0x{end:X8}, {length:N0} bytes)");
        sb.AppendLine("    fixed/body neighborhood:");
        AppendHexDump(sb, zone, offset, Math.Min(fixedSize + 128, zone.Length - offset), "      ");
    }

    private static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "<none>";
        var colon = reason.IndexOf(':');
        if (colon > 0)
        {
            var prefix = reason[..colon].Trim();
            if (prefix.Equals("precheck", StringComparison.OrdinalIgnoreCase) ||
                prefix.Equals("measure", StringComparison.OrdinalIgnoreCase))
            {
                var body = reason[(colon + 1)..].Trim();
                var cut = body.IndexOfAny(new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' });
                if (cut > 16) body = body[..cut].TrimEnd();
                return prefix + ": " + body;
            }
        }
        return reason.Length <= 160 ? reason : reason[..160] + "...";
    }

    private static uint ReadU32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    private static ushort ReadU16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    private static short ReadI16(byte[] data, int offset) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, 2));
    private static float ReadF32(byte[] data, int offset) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));
    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    private static string Hex(int? v) => v.HasValue ? $"0x{v.Value:X8}" : "-";
    private static string Fmt(int? v) => v?.ToString() ?? "-";
}
