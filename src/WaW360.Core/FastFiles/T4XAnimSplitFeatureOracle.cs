using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed class T4XAnimSplitFeatureOracleResult
{
    public required int PairsAnalyzed { get; init; }
    public required int BoneObservations { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// Correlates the two Xbox-only full-quaternion subdivisions with PC per-bone
/// translation type and dynamic quaternion frame count.
///
/// This is intentionally an oracle, not a serializer rule: it reports which
/// observable PC feature predicts Xbox subgroup A/B across same-name retail
/// XAnims, and fails closed if the PC flat streams cannot be reconstructed.
/// </summary>
public static class T4XAnimSplitFeatureOracle
{
    private enum SplitSide { A, B }

    // CoD's PC XAnim source loader carries a separate high-precision quaternion
    // bit that is not represented as a distinct PC boneCount[] category. On
    // older titles this list is used in addition to making every viewmodel_
    // animation high precision. Xbox has two extra quaternion buckets, so test
    // whether those buckets are the serialized form of that hidden distinction.
    private static readonly HashSet<string> LegacyHighPrecisionBones = new(StringComparer.OrdinalIgnoreCase)
    {
        "j_ankle_le",
        "j_ankle_ri",
        "j_clavicle_le",
        "j_clavicle_ri",
        "j_elbow_le",
        "j_elbow_ri",
        "j_hip_le",
        "j_hip_ri",
        "j_knee_le",
        "j_knee_ri",
        "j_mainroot",
        "j_shoulder_le",
        "j_shoulder_ri",
        "j_spine4",
        "j_spinelower",
        "j_spineupper",
        "j_wrist_le",
        "j_wrist_ri"
    };

    private sealed class PcBoneMeta
    {
        public required string Name { get; init; }
        public int QuatBucket { get; set; }
        public int QuatFrameCount { get; set; }
        public int TransBucket { get; set; } = -1;
    }

    private sealed class FamilyStats
    {
        public readonly int[,] Trans = new int[4, 2];
        public readonly List<int> FramesA = new();
        public readonly List<int> FramesB = new();
        public int Total => Enumerable.Range(0, 4).Sum(i => Trans[i, 0] + Trans[i, 1]);
    }

    private sealed class PrecisionRuleStats
    {
        public int Total;
        public int Correct;
        public int ViewmodelTotal;
        public int ViewmodelCorrect;
        public int NamedBoneTotal;
        public int NamedBoneCorrect;
        public int OtherTotal;
        public int OtherCorrect;
        public readonly Dictionary<string, int> MismatchBones = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> MismatchAnimations = new(StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<T4XAnimSplitFeatureOracleResult> AnalyzeAsync(
        string pcFastFile,
        string xboxFastFile,
        CancellationToken cancellationToken = default)
    {
        var comparison = await T4XAnimBoneCountOracle.CompareAsync(
            pcFastFile, xboxFastFile, animationName: null, cancellationToken);

        var pcZone = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, cancellationToken);
        var xboxZone = await FastFileAnalysisCache.GetZoneAsync(xboxFastFile, cancellationToken);

        if (pcZone.Platform != WaWPlatform.PC || xboxZone.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException("Split-feature oracle requires PC source + Xbox 360 oracle fastfiles.");

        var fullQuat = new FamilyStats();
        var fullQuatNoSize = new FamilyStats();
        var precisionRule = new PrecisionRuleStats();
        var pairsAnalyzed = 0;
        var observations = 0;

        foreach (var pair in comparison.Matches.Where(x => x.ExpandedLayoutMatches))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pcNames = ReadNames(pcZone, pair.Pc);
            var xboxNames = ReadNames(xboxZone, pair.Xbox);
            if (!HaveSameNameMultiset(pcNames, xboxNames))
                continue;

            var pcMeta = ReadPcBoneMetadata(pcZone.Bytes, pair.Pc, pcNames);
            var byName = pcMeta
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);

            // Xbox [2]/[3] subdivide PC FULL_QUAT.
            var qStart = pair.Xbox.BoneCounts[0] + pair.Xbox.BoneCounts[1];
            observations += ObserveRange(
                xboxNames, qStart, pair.Xbox.BoneCounts[2], pair.Xbox.BoneCounts[3],
                byName, fullQuat, pair.Name, precisionRule, expectedPcQuatBucket: 2);

            // Xbox [5]/[6] subdivide PC FULL_QUAT_NO_SIZE.
            var qNoSizeStart =
                pair.Xbox.BoneCounts[0] +
                pair.Xbox.BoneCounts[1] +
                pair.Xbox.BoneCounts[2] +
                pair.Xbox.BoneCounts[3] +
                pair.Xbox.BoneCounts[4];
            observations += ObserveRange(
                xboxNames, qNoSizeStart, pair.Xbox.BoneCounts[5], pair.Xbox.BoneCounts[6],
                byName, fullQuatNoSize, pair.Name, precisionRule, expectedPcQuatBucket: 4);

            pairsAnalyzed++;
        }

        var summary = BuildSummary(pairsAnalyzed, observations, fullQuat, fullQuatNoSize, precisionRule);
        return new T4XAnimSplitFeatureOracleResult
        {
            PairsAnalyzed = pairsAnalyzed,
            BoneObservations = observations,
            Summary = summary
        };
    }

    private static int ObserveRange(
        IReadOnlyList<string> xboxNames,
        int start,
        int countA,
        int countB,
        IReadOnlyDictionary<string, PcBoneMeta> pcByName,
        FamilyStats stats,
        string animationName,
        PrecisionRuleStats precisionRule,
        int expectedPcQuatBucket)
    {
        var total = checked(countA + countB);
        if (start < 0 || start > xboxNames.Count - total)
            throw new InvalidDataException("Xbox split range exceeds names[] bounds.");

        var observed = 0;
        for (var i = 0; i < total; i++)
        {
            var name = xboxNames[start + i];
            if (!pcByName.TryGetValue(name, out var meta))
                continue;
            if (meta.QuatBucket != expectedPcQuatBucket)
                throw new InvalidDataException(
                    $"Bone '{name}' maps to PC quat bucket {meta.QuatBucket}, expected {expectedPcQuatBucket}.");

            if (meta.TransBucket is < 5 or > 8)
                throw new InvalidDataException($"Bone '{name}' has unresolved PC translation bucket.");

            var side = i < countA ? SplitSide.A : SplitSide.B;
            stats.Trans[meta.TransBucket - 5, side == SplitSide.A ? 0 : 1]++;

            var isViewmodel = animationName.StartsWith("viewmodel_", StringComparison.OrdinalIgnoreCase);
            var isNamedHighPrecision = LegacyHighPrecisionBones.Contains(name);
            var predictedB = isViewmodel || isNamedHighPrecision;
            var actualB = side == SplitSide.B;

            precisionRule.Total++;
            if (predictedB == actualB)
                precisionRule.Correct++;

            if (isViewmodel)
            {
                precisionRule.ViewmodelTotal++;
                if (predictedB == actualB) precisionRule.ViewmodelCorrect++;
            }
            else if (isNamedHighPrecision)
            {
                precisionRule.NamedBoneTotal++;
                if (predictedB == actualB) precisionRule.NamedBoneCorrect++;
            }
            else
            {
                precisionRule.OtherTotal++;
                if (predictedB == actualB) precisionRule.OtherCorrect++;
            }

            if (predictedB != actualB)
            {
                precisionRule.MismatchBones.TryGetValue(name, out var boneMisses);
                precisionRule.MismatchBones[name] = boneMisses + 1;
                precisionRule.MismatchAnimations.TryGetValue(animationName, out var animMisses);
                precisionRule.MismatchAnimations[animationName] = animMisses + 1;
            }

            if (expectedPcQuatBucket == 2)
            {
                if (side == SplitSide.A) stats.FramesA.Add(meta.QuatFrameCount);
                else stats.FramesB.Add(meta.QuatFrameCount);
            }
            observed++;
        }

        return observed;
    }

    private static IReadOnlyList<PcBoneMeta> ReadPcBoneMetadata(
        byte[] bytes,
        T4XAnimBoneCountSample sample,
        IReadOnlyList<string> names)
    {
        if (sample.Platform != WaWPlatform.PC || sample.FixedSize != 0x58)
            throw new InvalidDataException("PC metadata reader received a non-PC XAnim sample.");

        var start = sample.HeaderOffset;
        var cursor = checked(start + sample.FixedSize);

        // Inline top-level XString.
        while (cursor < bytes.Length && bytes[cursor] != 0) cursor++;
        if (cursor >= bytes.Length)
            throw new InvalidDataException($"XAnim '{sample.Name}' top-level name is unterminated.");
        cursor++;

        var totalBones = sample.BoneCounts[9];
        if (names.Count != totalBones)
            throw new InvalidDataException($"XAnim '{sample.Name}' names[] count mismatch.");

        var dataByteCount = ReadU16(bytes, start + 0x04);
        var dataShortCount = ReadU16(bytes, start + 0x06);
        var numFrames = ReadU16(bytes, start + 0x0E);
        var notifyCount = bytes[start + 0x1C];

        var ptrNames = ReadU32(bytes, start + 0x30);
        var ptrDataByte = ReadU32(bytes, start + 0x34);
        var ptrDataShort = ReadU32(bytes, start + 0x38);
        var ptrNotify = ReadU32(bytes, start + 0x50);
        var ptrDelta = ReadU32(bytes, start + 0x54);

        if (ptrNames != 0)
            cursor = Advance(bytes, cursor, totalBones * 2, "names[]");
        if (ptrNotify != 0)
            cursor = Advance(bytes, cursor, notifyCount * 8, "notify[]");

        if (ptrDelta != 0)
        {
            Ensure(bytes, cursor, 8, "deltaPart");
            var transPtr = ReadU32(bytes, cursor);
            var quatPtr = ReadU32(bytes, cursor + 4);
            cursor += 8;

            if (transPtr != 0)
                cursor = SkipDeltaTrans(bytes, cursor, numFrames);
            if (quatPtr != 0)
                cursor = SkipDeltaQuat(bytes, cursor, numFrames);
        }

        var dataByteOffset = cursor;
        if (ptrDataByte != 0)
            cursor = Advance(bytes, cursor, dataByteCount, "dataByte");
        else if (dataByteCount != 0)
            throw new InvalidDataException($"XAnim '{sample.Name}' has dataByteCount without dataByte pointer.");

        var dataShortOffset = cursor;
        if (ptrDataShort != 0)
            cursor = Advance(bytes, cursor, checked(dataShortCount * 2), "dataShort");
        else if (dataShortCount != 0)
            throw new InvalidDataException($"XAnim '{sample.Name}' has dataShortCount without dataShort pointer.");

        var meta = names.Select(n => new PcBoneMeta { Name = n }).ToArray();
        var dbPos = 0;
        var dsPos = 0;
        var indexPos = 0;
        var boneIndex = 0;

        byte PopDataByte()
        {
            if (dbPos >= dataByteCount)
                throw new InvalidDataException($"XAnim '{sample.Name}' exhausted dataByte.");
            return bytes[dataByteOffset + dbPos++];
        }

        ushort PopDataShort()
        {
            if (dsPos >= dataShortCount)
                throw new InvalidDataException($"XAnim '{sample.Name}' exhausted dataShort.");
            var value = ReadU16(bytes, dataShortOffset + dsPos * 2);
            dsPos++;
            return value;
        }

        void SkipDataShort(int count)
        {
            if (count < 0 || dsPos > dataShortCount - count)
                throw new InvalidDataException($"XAnim '{sample.Name}' exhausted dataShort while skipping {count}.");
            dsPos += count;
        }

        void SkipPackedIndices(ushort storedSize)
        {
            var count = checked((int)storedSize + 1);
            if (numFrames < 256)
            {
                if (dbPos > dataByteCount - count)
                    throw new InvalidDataException($"XAnim '{sample.Name}' exhausted byte indices.");
                dbPos += count;
                return;
            }

            if (storedSize >= 64)
            {
                indexPos += count;
                var checkpoints = ((count - 2) / 256) + 2;
                SkipDataShort(checkpoints);
                return;
            }

            SkipDataShort(count);
        }

        for (var quatBucket = 0; quatBucket <= 4; quatBucket++)
        {
            var count = sample.BoneCounts[quatBucket];
            for (var i = 0; i < count; i++, boneIndex++)
            {
                if (boneIndex >= meta.Length)
                    throw new InvalidDataException($"XAnim '{sample.Name}' quaternion buckets exceed bone count.");

                var m = meta[boneIndex];
                m.QuatBucket = quatBucket;
                switch (quatBucket)
                {
                    case 0:
                        m.QuatFrameCount = 0;
                        break;
                    case 1:
                    case 2:
                    {
                        var storedSize = PopDataShort();
                        m.QuatFrameCount = checked((int)storedSize + 1);
                        SkipPackedIndices(storedSize);
                        break;
                    }
                    case 3:
                        m.QuatFrameCount = 1;
                        SkipDataShort(2);
                        break;
                    case 4:
                        m.QuatFrameCount = 1;
                        SkipDataShort(4);
                        break;
                }
            }
        }

        if (boneIndex != meta.Length)
            throw new InvalidDataException($"XAnim '{sample.Name}' quaternion buckets do not cover all bones.");

        for (var transBucket = 5; transBucket <= 8; transBucket++)
        {
            var count = sample.BoneCounts[transBucket];
            for (var i = 0; i < count; i++)
            {
                var targetBone = PopDataByte();
                if (targetBone >= meta.Length)
                    throw new InvalidDataException($"XAnim '{sample.Name}' translation bone index {targetBone} is out of range.");
                if (meta[targetBone].TransBucket != -1)
                    throw new InvalidDataException($"XAnim '{sample.Name}' translation bone {targetBone} assigned twice.");

                meta[targetBone].TransBucket = transBucket;
                if (transBucket is 5 or 6)
                {
                    var storedSize = PopDataShort();
                    SkipPackedIndices(storedSize);
                }
            }
        }

        if (meta.Any(x => x.TransBucket == -1))
            throw new InvalidDataException($"XAnim '{sample.Name}' did not assign a translation type to every bone.");

        return meta;
    }

    private static int SkipDeltaTrans(byte[] data, int cursor, ushort numFrames)
    {
        Ensure(data, cursor, 4, "delta trans");
        var size = ReadU16(data, cursor);
        var small = data[cursor + 2] != 0;
        if (size == 0)
            return Advance(data, cursor, 16, "delta trans frame0");

        Ensure(data, cursor, 32, "delta trans frames");
        var framesPtr = ReadU32(data, cursor + 28);
        cursor += 32;
        var count = checked((int)size + 1);
        cursor = Advance(data, cursor, count * (numFrames < 256 ? 1 : 2), "delta trans indices");
        if (framesPtr != 0)
            cursor = Advance(data, cursor, count * (small ? 3 : 6), "delta trans frames");
        return cursor;
    }

    private static int SkipDeltaQuat(byte[] data, int cursor, ushort numFrames)
    {
        Ensure(data, cursor, 8, "delta quat");
        var size = ReadU16(data, cursor);
        if (size == 0)
            return cursor + 8;

        var framesPtr = ReadU32(data, cursor + 4);
        cursor += 8;
        var count = checked((int)size + 1);
        cursor = Advance(data, cursor, count * (numFrames < 256 ? 1 : 2), "delta quat indices");
        if (framesPtr != 0)
            cursor = Advance(data, cursor, count * 4, "delta quat frames");
        return cursor;
    }

    private static IReadOnlyList<string> ReadNames(
        FastFileAnalysisCache.CachedZone zone,
        T4XAnimBoneCountSample sample)
    {
        var bytes = zone.Bytes;
        var cursor = checked(sample.HeaderOffset + sample.FixedSize);

        while (cursor < bytes.Length && bytes[cursor] != 0) cursor++;
        if (cursor >= bytes.Length)
            throw new InvalidDataException($"XAnim at 0x{sample.HeaderOffset:X8} has an unterminated inline name.");
        cursor++;

        var total = sample.Platform == WaWPlatform.Xbox360
            ? sample.BoneCounts[11]
            : sample.BoneCounts[9];

        var result = new List<string>(total);
        for (var i = 0; i < total; i++)
        {
            Ensure(bytes, cursor, 2, "names[]");
            var id = sample.Platform == WaWPlatform.Xbox360
                ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(cursor, 2))
                : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor, 2));
            cursor += 2;

            if (id >= zone.Document.ScriptStrings.Count ||
                zone.Document.ScriptStrings[id].Value is not { Length: > 0 } value ||
                value.StartsWith("<alias ", StringComparison.Ordinal))
                throw new InvalidDataException($"XAnim '{sample.Name}' has unresolved ScriptString {id}.");

            result.Add(value);
        }

        return result;
    }

    private static bool HaveSameNameMultiset(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count) return false;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in left)
        {
            counts.TryGetValue(name, out var count);
            counts[name] = count + 1;
        }

        foreach (var name in right)
        {
            if (!counts.TryGetValue(name, out var count) || count == 0) return false;
            if (count == 1) counts.Remove(name);
            else counts[name] = count - 1;
        }

        return counts.Count == 0;
    }

    private static string BuildSummary(
        int pairsAnalyzed,
        int observations,
        FamilyStats fullQuat,
        FamilyStats fullQuatNoSize,
        PrecisionRuleStats precisionRule)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360 XAnim Xbox-split feature oracle");
        sb.AppendLine($"Same-name expanded pairs analyzed: {pairsAnalyzed:N0}");
        sb.AppendLine($"Split-bone observations:          {observations:N0}");
        sb.AppendLine();

        AppendFamily(sb, "FULL_QUAT: PC[2] -> Xbox[2]/Xbox[3]", fullQuat, includeFrames: true);
        sb.AppendLine();
        AppendFamily(sb, "FULL_QUAT_NO_SIZE: PC[4] -> Xbox[5]/Xbox[6]", fullQuatNoSize, includeFrames: false);
        sb.AppendLine();

        var combined = new FamilyStats();
        for (var t = 0; t < 4; t++)
        {
            combined.Trans[t, 0] = fullQuat.Trans[t, 0] + fullQuatNoSize.Trans[t, 0];
            combined.Trans[t, 1] = fullQuat.Trans[t, 1] + fullQuatNoSize.Trans[t, 1];
        }

        sb.AppendLine("CANDIDATE TRANSLATION RULES (predict Xbox A when predicate is true)");
        AppendRuleScore(sb, combined, "trans != NO_TRANS", t => t != 8);
        AppendRuleScore(sb, combined, "trans == NO_TRANS", t => t == 8);
        AppendRuleScore(sb, combined, "trans == SMALL_TRANS", t => t == 5);
        AppendRuleScore(sb, combined, "trans == FULL_TRANS", t => t == 6);
        AppendRuleScore(sb, combined, "trans == TRANS_NO_SIZE", t => t == 7);
        AppendRuleScore(sb, combined, "trans is dynamic (SMALL/FULL)", t => t is 5 or 6);
        AppendRuleScore(sb, combined, "trans is constant/none (NO_SIZE/NO_TRANS)", t => t is 7 or 8);

        if (fullQuat.FramesA.Count > 0 && fullQuat.FramesB.Count > 0)
        {
            sb.AppendLine();
            AppendBestFrameThreshold(sb, fullQuat);
        }

        sb.AppendLine();
        AppendPrecisionRule(sb, precisionRule);

        return sb.ToString();
    }

    private static void AppendPrecisionRule(StringBuilder sb, PrecisionRuleStats stats)
    {
        static double Pct(int correct, int total) => total == 0 ? 0.0 : 100.0 * correct / total;

        sb.AppendLine("HIGH-PRECISION QUATERNION HYPOTHESIS");
        sb.AppendLine("  Predict Xbox subgroup B when:");
        sb.AppendLine("    animation name starts with 'viewmodel_' OR");
        sb.AppendLine("    bone is in the legacy 18-bone high-precision set.");
        sb.AppendLine($"  Overall:                         {stats.Correct,7:N0}/{stats.Total,-7:N0} {Pct(stats.Correct, stats.Total),7:F2}%");
        sb.AppendLine($"  viewmodel_ observations:         {stats.ViewmodelCorrect,7:N0}/{stats.ViewmodelTotal,-7:N0} {Pct(stats.ViewmodelCorrect, stats.ViewmodelTotal),7:F2}%");
        sb.AppendLine($"  named high-precision bones:      {stats.NamedBoneCorrect,7:N0}/{stats.NamedBoneTotal,-7:N0} {Pct(stats.NamedBoneCorrect, stats.NamedBoneTotal),7:F2}%");
        sb.AppendLine($"  other non-viewmodel bones:       {stats.OtherCorrect,7:N0}/{stats.OtherTotal,-7:N0} {Pct(stats.OtherCorrect, stats.OtherTotal),7:F2}%");

        var mismatches = stats.Total - stats.Correct;
        sb.AppendLine($"  Mismatches: {mismatches:N0}");

        if (mismatches > 0)
        {
            sb.AppendLine("  Top mismatch bones:");
            foreach (var kv in stats.MismatchBones.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Take(20))
                sb.AppendLine($"    {kv.Key,-32} {kv.Value,6:N0}");

            sb.AppendLine("  Top mismatch animations:");
            foreach (var kv in stats.MismatchAnimations.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Take(20))
                sb.AppendLine($"    {kv.Key,-48} {kv.Value,6:N0}");
        }
    }

    private static void AppendFamily(StringBuilder sb, string title, FamilyStats stats, bool includeFrames)
    {
        sb.AppendLine(title);
        sb.AppendLine("  PC translation bucket        Xbox A      Xbox B       A%");
        var labels = new[] { "SMALL_TRANS [5]", "FULL_TRANS [6]", "TRANS_NO_SIZE [7]", "NO_TRANS [8]" };
        for (var i = 0; i < 4; i++)
        {
            var a = stats.Trans[i, 0];
            var b = stats.Trans[i, 1];
            var total = a + b;
            var pct = total == 0 ? 0.0 : 100.0 * a / total;
            sb.AppendLine($"  {labels[i],-24} {a,7:N0} {b,11:N0} {pct,8:F2}%");
        }

        if (includeFrames && stats.FramesA.Count > 0 && stats.FramesB.Count > 0)
        {
            sb.AppendLine($"  Dynamic quat frame counts A: min={stats.FramesA.Min()}, median={Median(stats.FramesA):F1}, max={stats.FramesA.Max()}");
            sb.AppendLine($"  Dynamic quat frame counts B: min={stats.FramesB.Min()}, median={Median(stats.FramesB):F1}, max={stats.FramesB.Max()}");
        }
    }

    private static void AppendRuleScore(StringBuilder sb, FamilyStats stats, string label, Func<int, bool> predictA)
    {
        var correct = 0;
        var total = 0;
        for (var i = 0; i < 4; i++)
        {
            var trans = i + 5;
            var a = stats.Trans[i, 0];
            var b = stats.Trans[i, 1];
            total += a + b;
            correct += predictA(trans) ? a : b;
        }

        var pct = total == 0 ? 0.0 : 100.0 * correct / total;
        sb.AppendLine($"  {label,-46} {correct,7:N0}/{total,-7:N0} {pct,7:F2}%");
    }

    private static void AppendBestFrameThreshold(StringBuilder sb, FamilyStats stats)
    {
        var values = stats.FramesA.Concat(stats.FramesB).Distinct().OrderBy(x => x).ToArray();
        var bestCorrect = -1;
        var bestThreshold = 0;
        var bestLessEqualMeansA = true;
        var total = stats.FramesA.Count + stats.FramesB.Count;

        foreach (var threshold in values)
        {
            var correctLeA =
                stats.FramesA.Count(x => x <= threshold) +
                stats.FramesB.Count(x => x > threshold);
            if (correctLeA > bestCorrect)
            {
                bestCorrect = correctLeA;
                bestThreshold = threshold;
                bestLessEqualMeansA = true;
            }

            var correctGtA =
                stats.FramesA.Count(x => x > threshold) +
                stats.FramesB.Count(x => x <= threshold);
            if (correctGtA > bestCorrect)
            {
                bestCorrect = correctGtA;
                bestThreshold = threshold;
                bestLessEqualMeansA = false;
            }
        }

        var pct = total == 0 ? 0.0 : 100.0 * bestCorrect / total;
        var op = bestLessEqualMeansA ? "<=" : ">";
        sb.AppendLine($"BEST FULL_QUAT FRAME-COUNT THRESHOLD: predict A when frameCount {op} {bestThreshold}: {bestCorrect:N0}/{total:N0} ({pct:F2}%)");
    }

    private static double Median(List<int> values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return 0;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 != 0
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static ushort ReadU16(byte[] data, int offset)
    {
        Ensure(data, offset, 2, "u16");
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        Ensure(data, offset, 4, "u32");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private static int Advance(byte[] data, int cursor, int count, string label)
    {
        Ensure(data, cursor, count, label);
        return checked(cursor + count);
    }

    private static void Ensure(byte[] data, int offset, int count, string label)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
            throw new InvalidDataException($"{label} exceeds zone bounds.");
    }
}
