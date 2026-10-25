using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4XAnimBoneCountSample(
    string Name,
    WaWPlatform Platform,
    int HeaderOffset,
    int FixedSize,
    ushort NumFrames,
    ushort DataByteCount,
    ushort DataShortCount,
    ushort DataIntCount,
    ushort RandomDataByteCount,
    ushort RandomDataIntCount,
    byte[] BoneCounts,
    byte NotifyCount,
    byte AssetType,
    uint RandomDataShortCount,
    uint IndexCount,
    float FrameRate,
    float Frequency,
    bool QuaternionTotalInvariant,
    bool TranslationTotalInvariant);

public sealed record T4XAnimBoneCountPair(
    string Name,
    T4XAnimBoneCountSample Pc,
    T4XAnimBoneCountSample Xbox,
    bool TotalMatches,
    bool DirectQuaternionBucketsMatch,
    bool FullQuaternionSplitMatches,
    bool FullQuaternionNoSizeSplitMatches,
    bool DirectTranslationBucketsMatch,
    bool FlatStreamCountsMatch,
    bool ScalarHeaderFieldsMatch,
    bool ExpandedLayoutMatches);

public sealed class T4XAnimBoneCountOracleResult
{
    public required string PcFastFile { get; init; }
    public required string XboxFastFile { get; init; }
    public required IReadOnlyList<T4XAnimBoneCountSample> PcSamples { get; init; }
    public required IReadOnlyList<T4XAnimBoneCountSample> XboxSamples { get; init; }
    public required IReadOnlyList<T4XAnimBoneCountPair> Matches { get; init; }
    public required string? RequestedName { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// Read-only PC/Xbox XAnimParts bone-count oracle.
///
/// PC WaW XAnimParts uses an 0x58-byte fixed root with ten boneCount bytes at +0x12.
/// Xbox 360 TU7 uses an 0x5C-byte fixed root with twelve boneCount bytes at +0x12.
/// Hardware-backed same-animation comparison establishes that the Xbox array is
/// seven quaternion buckets, four translation buckets, then the total. Relative to
/// PC, Xbox subdivides the two full-quaternion categories while translation is only
/// shifted by the two inserted quaternion buckets:
///   PC[0] == XB[0], PC[1] == XB[1], PC[2] == XB[2] + XB[3],
///   PC[3] == XB[4], PC[4] == XB[5] + XB[6],
///   PC[5] == XB[7], PC[6] == XB[8], PC[7] == XB[9],
///   PC[8] == XB[10], PC[9] == XB[11].
/// It deliberately does not guess the per-bone semantic rule that chooses each
/// console sub-bucket inside PC[2] and PC[4].
///
/// Only inline top-level XAnim names are considered. This keeps the oracle fail-closed:
/// packed/aliased names are skipped rather than guessed.
/// </summary>
public static class T4XAnimBoneCountOracle
{
    private const uint Following = 0xFFFFFFFFu;
    private const uint Insert = 0xFFFFFFFEu;
    private const int PcFixedSize = 0x58;
    private const int XboxFixedSize = 0x5C;

    public static async Task<T4XAnimBoneCountOracleResult> CompareAsync(
        string pcFastFile,
        string xboxFastFile,
        string? animationName = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pcFastFile)) throw new FileNotFoundException("PC fastfile not found.", pcFastFile);
        if (!File.Exists(xboxFastFile)) throw new FileNotFoundException("Xbox fastfile not found.", xboxFastFile);

        var pc = await LoadZoneAsync(pcFastFile, cancellationToken);
        var xbox = await LoadZoneAsync(xboxFastFile, cancellationToken);

        if (pc.Info.Platform != WaWPlatform.PC)
            throw new InvalidDataException($"Expected PC source fastfile, detected {pc.Info.Platform}/{pc.Info.Compression}.");
        if (xbox.Info.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException($"Expected Xbox 360 oracle fastfile, detected {xbox.Info.Platform}/{xbox.Info.Compression}.");

        var pcSamples = Scan(pc.Bytes, WaWPlatform.PC, animationName, cancellationToken);
        var xboxSamples = Scan(xbox.Bytes, WaWPlatform.Xbox360, animationName, cancellationToken);

        var xboxByName = xboxSamples
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matches = new List<T4XAnimBoneCountPair>();
        foreach (var p in pcSamples)
        {
            if (!xboxByName.TryGetValue(p.Name, out var x)) continue;

            var totalMatches = p.BoneCounts[9] == x.BoneCounts[11];
            var directQuat =
                p.BoneCounts[0] == x.BoneCounts[0] &&
                p.BoneCounts[1] == x.BoneCounts[1] &&
                p.BoneCounts[3] == x.BoneCounts[4];
            var fullQuatSplit = p.BoneCounts[2] == x.BoneCounts[2] + x.BoneCounts[3];
            var fullQuatNoSizeSplit = p.BoneCounts[4] == x.BoneCounts[5] + x.BoneCounts[6];
            var directTrans =
                p.BoneCounts[5] == x.BoneCounts[7] &&
                p.BoneCounts[6] == x.BoneCounts[8] &&
                p.BoneCounts[7] == x.BoneCounts[9] &&
                p.BoneCounts[8] == x.BoneCounts[10];

            var flatCounts =
                p.DataByteCount == x.DataByteCount &&
                p.DataShortCount == x.DataShortCount &&
                p.DataIntCount == x.DataIntCount &&
                p.RandomDataByteCount == x.RandomDataByteCount &&
                p.RandomDataIntCount == x.RandomDataIntCount &&
                p.RandomDataShortCount == x.RandomDataShortCount &&
                p.IndexCount == x.IndexCount;

            var scalarHeader =
                p.NumFrames == x.NumFrames &&
                p.NotifyCount == x.NotifyCount &&
                p.AssetType == x.AssetType &&
                BitConverter.SingleToInt32Bits(p.FrameRate) == BitConverter.SingleToInt32Bits(x.FrameRate) &&
                BitConverter.SingleToInt32Bits(p.Frequency) == BitConverter.SingleToInt32Bits(x.Frequency);

            matches.Add(new T4XAnimBoneCountPair(
                p.Name,
                p,
                x,
                totalMatches,
                directQuat,
                fullQuatSplit,
                fullQuatNoSizeSplit,
                directTrans,
                flatCounts,
                scalarHeader,
                totalMatches && directQuat && fullQuatSplit && fullQuatNoSizeSplit && directTrans));
        }

        var summary = BuildSummary(pcSamples, xboxSamples, matches, animationName);
        return new T4XAnimBoneCountOracleResult
        {
            PcFastFile = Path.GetFullPath(pcFastFile),
            XboxFastFile = Path.GetFullPath(xboxFastFile),
            PcSamples = pcSamples,
            XboxSamples = xboxSamples,
            Matches = matches,
            RequestedName = animationName,
            Summary = summary
        };
    }

    private sealed record LoadedZone(byte[] Bytes, DecompressionResult Info);

    private static async Task<LoadedZone> LoadZoneAsync(string fastFile, CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "xanim_bone_oracle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "zone.bin");
        try
        {
            var info = await FastFileDecompressor.DecompressAutoAsync(fastFile, zonePath, cancellationToken);
            var bytes = await File.ReadAllBytesAsync(zonePath, cancellationToken);
            return new LoadedZone(bytes, info);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static IReadOnlyList<T4XAnimBoneCountSample> Scan(
        byte[] zone,
        WaWPlatform platform,
        string? requestedName,
        CancellationToken cancellationToken)
    {
        var endian = platform == WaWPlatform.Xbox360 ? Endian.Big : Endian.Little;
        var fixedSize = platform == WaWPlatform.Xbox360 ? XboxFixedSize : PcFixedSize;
        var result = new List<T4XAnimBoneCountSample>();

        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            var needle = Encoding.ASCII.GetBytes(requestedName + "\0");
            var searchFrom = 0;
            while (searchFrom <= zone.Length - needle.Length)
            {
                var relative = zone.AsSpan(searchFrom).IndexOf(needle);
                if (relative < 0) break;
                var nameOffset = searchFrom + relative;
                var headerOffset = nameOffset - fixedSize;
                if (TryReadSample(zone, headerOffset, platform, endian, fixedSize, out var sample) &&
                    sample.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(sample);
                }
                searchFrom = nameOffset + needle.Length;
            }
            return result
                .GroupBy(x => (x.Name.ToLowerInvariant(), x.HeaderOffset))
                .Select(g => g.First())
                .OrderBy(x => x.HeaderOffset)
                .ToList();
        }

        for (var offset = 0; offset <= zone.Length - fixedSize - 5; offset++)
        {
            if ((offset & 0x3FFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (zone[offset] != 0xFF && !(platform == WaWPlatform.PC && zone[offset] == 0xFE))
                continue;

            if (TryReadSample(zone, offset, platform, endian, fixedSize, out var sample))
                result.Add(sample);
        }

        return result
            .GroupBy(x => (x.Name.ToLowerInvariant(), x.HeaderOffset))
            .Select(g => g.First())
            .OrderBy(x => x.HeaderOffset)
            .ToList();
    }

    private static bool TryReadSample(
        byte[] zone,
        int start,
        WaWPlatform platform,
        Endian endian,
        int fixedSize,
        out T4XAnimBoneCountSample sample)
    {
        sample = null!;
        if (start < 0 || start > zone.Length - fixedSize - 1) return false;

        var namePointer = ReadU32(zone, start, endian);
        if (!IsInline(namePointer)) return false;
        if (!TryReadAssetName(zone, start + fixedSize, out var name)) return false;

        var numFrames = ReadU16(zone, start + 0x0E, endian);
        if (numFrames > 10000) return false;

        var boneCountLength = platform == WaWPlatform.Xbox360 ? 12 : 10;
        var bones = zone.AsSpan(start + 0x12, boneCountLength).ToArray();
        var total = platform == WaWPlatform.Xbox360 ? bones[11] : bones[9];
        if (total == 0) return false;

        var notifyOffset = platform == WaWPlatform.Xbox360 ? 0x1E : 0x1C;
        var assetTypeOffset = platform == WaWPlatform.Xbox360 ? 0x1F : 0x1D;
        var randomShortOffset = platform == WaWPlatform.Xbox360 ? 0x24 : 0x20;
        var indexOffset = platform == WaWPlatform.Xbox360 ? 0x28 : 0x24;
        var frameRateOffset = platform == WaWPlatform.Xbox360 ? 0x2C : 0x28;
        var frequencyOffset = platform == WaWPlatform.Xbox360 ? 0x30 : 0x2C;
        var firstPointerOffset = platform == WaWPlatform.Xbox360 ? 0x34 : 0x30;
        var lastPointerOffset = platform == WaWPlatform.Xbox360 ? 0x58 : 0x54;

        var frameRate = ReadF32(zone, start + frameRateOffset, endian);
        var frequency = ReadF32(zone, start + frequencyOffset, endian);
        if (!FiniteRange(frameRate, 0f, 1000f) || !FiniteRange(frequency, 0f, 1000f)) return false;

        for (var p = firstPointerOffset; p <= lastPointerOffset; p += 4)
            if (!IsSerializedPointer(ReadU32(zone, start + p, endian)))
                return false;

        bool quatInvariant;
        bool transInvariant;
        if (platform == WaWPlatform.Xbox360)
        {
            quatInvariant = bones.Take(7).Sum(x => (int)x) == total;
            transInvariant = bones.Skip(7).Take(4).Sum(x => (int)x) == total;
        }
        else
        {
            quatInvariant = bones.Take(5).Sum(x => (int)x) == total;
            transInvariant = bones.Skip(5).Take(4).Sum(x => (int)x) == total;
        }

        if (!quatInvariant || !transInvariant)
            return false;

        sample = new T4XAnimBoneCountSample(
            name,
            platform,
            start,
            fixedSize,
            numFrames,
            ReadU16(zone, start + 0x04, endian),
            ReadU16(zone, start + 0x06, endian),
            ReadU16(zone, start + 0x08, endian),
            ReadU16(zone, start + 0x0A, endian),
            ReadU16(zone, start + 0x0C, endian),
            bones,
            zone[start + notifyOffset],
            zone[start + assetTypeOffset],
            ReadU32(zone, start + randomShortOffset, endian),
            ReadU32(zone, start + indexOffset, endian),
            frameRate,
            frequency,
            quatInvariant,
            transInvariant);
        return true;
    }

    private static string BuildSummary(
        IReadOnlyList<T4XAnimBoneCountSample> pc,
        IReadOnlyList<T4XAnimBoneCountSample> xbox,
        IReadOnlyList<T4XAnimBoneCountPair> matches,
        string? requestedName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360 XAnim PC/Xbox bone-count oracle");
        sb.AppendLine($"PC inline XAnim candidates:   {pc.Count:N0}");
        sb.AppendLine($"Xbox inline XAnim candidates: {xbox.Count:N0}");
        sb.AppendLine($"Same-name matches:            {matches.Count:N0}");
        if (!string.IsNullOrWhiteSpace(requestedName))
            sb.AppendLine($"Requested animation:          {requestedName}");

        if (matches.Count == 0)
        {
            sb.AppendLine("No same-name inline XAnim pair was found. No transform conclusion is made.");
            return sb.ToString();
        }

        var totalMatches = matches.Count(x => x.TotalMatches);
        var quatDirect = matches.Count(x => x.DirectQuaternionBucketsMatch);
        var fullQuatSplit = matches.Count(x => x.FullQuaternionSplitMatches);
        var fullQuatNoSizeSplit = matches.Count(x => x.FullQuaternionNoSizeSplitMatches);
        var transDirect = matches.Count(x => x.DirectTranslationBucketsMatch);
        var flatCounts = matches.Count(x => x.FlatStreamCountsMatch);
        var scalarHeaders = matches.Count(x => x.ScalarHeaderFieldsMatch);
        var expanded = matches.Count(x => x.ExpandedLayoutMatches);

        sb.AppendLine($"Total count PC[9] == Xbox[11]:                   {totalMatches}/{matches.Count}");
        sb.AppendLine($"Direct quat PC[0,1,3] -> XB[0,1,4]:             {quatDirect}/{matches.Count}");
        sb.AppendLine($"Full-quat split PC[2] == XB[2] + XB[3]:         {fullQuatSplit}/{matches.Count}");
        sb.AppendLine($"Full-quat-no-size PC[4] == XB[5] + XB[6]:       {fullQuatNoSizeSplit}/{matches.Count}");
        sb.AppendLine($"Translation PC[5..8] -> XB[7..10]:              {transDirect}/{matches.Count}");
        sb.AppendLine($"All seven flat-stream counts identical:          {flatCounts}/{matches.Count}");
        sb.AppendLine($"Scalar header fields byte-equivalent:             {scalarHeaders}/{matches.Count}");
        sb.AppendLine($"Complete 10 -> 12 expansion pattern:             {expanded}/{matches.Count}");
        sb.AppendLine();
        sb.AppendLine("Xbox grouping: [0..6] = 7 quaternion buckets; [7..10] = 4 translation buckets; [11] = total.");

        return sb.ToString();
    }

    private static bool TryReadAssetName(byte[] data, int offset, out string name)
    {
        name = string.Empty;
        if (offset < 0 || offset >= data.Length) return false;
        var end = offset;
        var max = Math.Min(data.Length, offset + 256);
        while (end < max && data[end] != 0)
        {
            var b = data[end];
            if (b < 0x20 || b > 0x7E) return false;
            end++;
        }

        if (end >= max || end == offset || data[end] != 0 || end - offset < 4) return false;
        name = Encoding.ASCII.GetString(data, offset, end - offset);
        if (name.Any(char.IsWhiteSpace) || !name.Any(char.IsLetter)) return false;
        return true;
    }

    private static bool IsInline(uint value) => value is Following or Insert;

    private static bool IsSerializedPointer(uint value)
    {
        if (value == 0 || IsInline(value)) return true;
        var decoded = T4ZonePointer.Decode(value);
        return decoded.Kind == T4ZonePointerKind.Offset && decoded.Block is not null;
    }

    private static ushort ReadU16(byte[] data, int offset, Endian endian)
    {
        var span = data.AsSpan(offset, 2);
        return endian == Endian.Big
            ? BinaryPrimitives.ReadUInt16BigEndian(span)
            : BinaryPrimitives.ReadUInt16LittleEndian(span);
    }

    private static uint ReadU32(byte[] data, int offset, Endian endian)
    {
        var span = data.AsSpan(offset, 4);
        return endian == Endian.Big
            ? BinaryPrimitives.ReadUInt32BigEndian(span)
            : BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    private static float ReadF32(byte[] data, int offset, Endian endian) =>
        BitConverter.Int32BitsToSingle(unchecked((int)ReadU32(data, offset, endian)));

    private static bool FiniteRange(float value, float min, float max) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
}
