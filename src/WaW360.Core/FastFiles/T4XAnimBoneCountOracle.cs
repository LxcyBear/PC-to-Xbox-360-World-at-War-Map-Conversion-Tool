using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4XAnimBoneCountSample(
    string Name,
    WaWPlatform Platform,
    int HeaderOffset,
    int FixedSize,
    ushort NumFrames,
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
    int[] SharedBucketDelta,
    int XboxQuaternionExtra,
    int XboxTranslationExtra,
    bool QuaternionDeficitExplainsExtra,
    bool TranslationDeficitExplainsExtra);

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
/// This diagnostic intentionally does not assume how the two Xbox-only buckets are
/// populated. Instead it compares the same serialized animation name on both sides
/// and reports the per-bucket deltas needed to derive the real transform.
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

            var delta = new int[9];
            for (var i = 0; i < delta.Length; i++)
                delta[i] = p.BoneCounts[i] - x.BoneCounts[i];

            var quatDeficit = delta.Take(5).Sum();
            var transDeficit = delta.Skip(5).Take(4).Sum();
            matches.Add(new T4XAnimBoneCountPair(
                p.Name,
                p,
                x,
                p.BoneCounts[9] == x.BoneCounts[11],
                delta,
                x.BoneCounts[9],
                x.BoneCounts[10],
                quatDeficit == x.BoneCounts[9],
                transDeficit == x.BoneCounts[10]));
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
            quatInvariant = bones.Take(5).Sum(x => (int)x) + bones[9] == total;
            transInvariant = bones.Skip(5).Take(4).Sum(x => (int)x) + bones[10] == total;
        }
        else
        {
            quatInvariant = bones.Take(5).Sum(x => (int)x) == total;
            transInvariant = bones.Skip(5).Take(4).Sum(x => (int)x) == total;
        }

        sample = new T4XAnimBoneCountSample(
            name,
            platform,
            start,
            fixedSize,
            numFrames,
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
        var quatExplained = matches.Count(x => x.QuaternionDeficitExplainsExtra);
        var transExplained = matches.Count(x => x.TranslationDeficitExplainsExtra);
        sb.AppendLine($"Total count agrees PC[9] == Xbox[11]: {totalMatches}/{matches.Count}");
        sb.AppendLine($"Xbox[9] explained by PC->Xbox quat deficit: {quatExplained}/{matches.Count}");
        sb.AppendLine($"Xbox[10] explained by PC->Xbox trans deficit: {transExplained}/{matches.Count}");

        var quatSources = InferSourceBuckets(matches, 0, 5, 9);
        var transSources = InferSourceBuckets(matches, 5, 4, 10);
        if (quatSources.Count > 0)
            sb.AppendLine("Candidate source bucket(s) feeding Xbox[9]: " + string.Join(", ", quatSources.Select(i => $"PC[{i}]")));
        if (transSources.Count > 0)
            sb.AppendLine("Candidate source bucket(s) feeding Xbox[10]: " + string.Join(", ", transSources.Select(i => $"PC[{i}]")));

        return sb.ToString();
    }

    private static IReadOnlyList<int> InferSourceBuckets(
        IReadOnlyList<T4XAnimBoneCountPair> matches,
        int startBucket,
        int bucketCount,
        int xboxExtraBucket)
    {
        var candidates = new List<int>();
        for (var bucket = startBucket; bucket < startBucket + bucketCount; bucket++)
        {
            var observedNonZero = false;
            var valid = true;
            foreach (var match in matches)
            {
                var extra = match.Xbox.BoneCounts[xboxExtraBucket];
                var delta = match.Pc.BoneCounts[bucket] - match.Xbox.BoneCounts[bucket];
                if (extra != 0) observedNonZero = true;
                if (delta != extra)
                {
                    valid = false;
                    break;
                }

                for (var other = startBucket; other < startBucket + bucketCount; other++)
                {
                    if (other == bucket) continue;
                    if (match.Pc.BoneCounts[other] != match.Xbox.BoneCounts[other])
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid) break;
            }

            if (valid && observedNonZero) candidates.Add(bucket);
        }
        return candidates;
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
