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

    private sealed class XboxDynamicQuatMeta
    {
        public required Dictionary<string, int> FrameCountByBone { get; init; }
        public required int NormalKeys { get; init; }
        public required int PrecisionKeys { get; init; }
    }

    private sealed record PackingRow(
        int NormalDynamicKeys,
        int PrecisionDynamicKeys,
        int NormalConstantBones,
        int PrecisionConstantBones,
        int RandomShortDelta,
        int DataShortDelta,
        int RandomIntDelta,
        int DataIntDelta,
        int IndexDelta);

    private sealed class PackingStats
    {
        public int Pairs;
        public int DataByteEqual;
        public int DataIntEqual;
        public int RandomDataByteEqual;
        public int RandomDataIntEqual;
        public int IndexEqual;
        public int NormalDynamicThreeShorts;
        public int PrecisionDynamicThreeShorts;
        public int NormalConstantThreeShorts;
        public int PrecisionConstantThreeShorts;
        public int NormalThreeShortsBoth;
        public int NormalDynamicPacked32;
        public int NormalConstantPacked32;
        public int NormalConstantDataIntGain;
        public int NormalConstantLeaves0Shorts;
        public int NormalConstantLeaves1Short;
        public int NormalConstantLeaves2Shorts;
        public int NormalConstantLeaves3Shorts;
        public int NormalPacked32All;
        public int PcVsXboxNormalKeyCountEqual;
        public int PcVsXboxPrecisionKeyCountEqual;
        public int PrecisionWidth2Matches;
        public int PrecisionWidth3Matches;
        public int PrecisionWidth4Matches;
        public readonly List<int> PrecisionKeyCountDeltas = new();
        public int RandomIntGainEqualsNormalKeys;
        public int RandomIntGainEqualsTwiceNormalKeys;
        public int RandomIntGainEqualsPrecisionKeys;
        public int RandomIntGainEqualsTwicePrecisionKeys;
        public readonly List<int> RandomShortDeltas = new();
        public readonly List<int> DataShortDeltas = new();
        public readonly List<int> RandomIntDeltas = new();
        public readonly List<int> DataIntDeltas = new();
        public readonly List<PackingRow> Rows = new();
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
        var packing = new PackingStats();
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

            var normalDynamicKeys = SumQuatFrames(
                xboxNames, qStart, pair.Xbox.BoneCounts[2], byName, expectedPcQuatBucket: 2);
            var precisionDynamicKeys = SumQuatFrames(
                xboxNames, qStart + pair.Xbox.BoneCounts[2], pair.Xbox.BoneCounts[3], byName, expectedPcQuatBucket: 2);
            var xboxDynamic = ReadXboxDynamicQuatMetadata(xboxZone.Bytes, pair.Xbox, xboxNames);
            ObservePacking(
                pair,
                normalDynamicKeys,
                precisionDynamicKeys,
                xboxDynamic,
                pair.Xbox.BoneCounts[5],
                pair.Xbox.BoneCounts[6],
                packing);

            pairsAnalyzed++;
        }

        var summary = BuildSummary(pairsAnalyzed, observations, fullQuat, fullQuatNoSize, precisionRule, packing);
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

    private static int SumQuatFrames(
        IReadOnlyList<string> xboxNames,
        int start,
        int count,
        IReadOnlyDictionary<string, PcBoneMeta> pcByName,
        int expectedPcQuatBucket)
    {
        if (start < 0 || count < 0 || start > xboxNames.Count - count)
            throw new InvalidDataException("Xbox quaternion range exceeds names[] bounds.");

        var sum = 0;
        for (var i = 0; i < count; i++)
        {
            var name = xboxNames[start + i];
            if (!pcByName.TryGetValue(name, out var meta))
                throw new InvalidDataException($"Could not resolve PC metadata for Xbox bone '{name}'.");
            if (meta.QuatBucket != expectedPcQuatBucket)
                throw new InvalidDataException(
                    $"Bone '{name}' maps to PC quat bucket {meta.QuatBucket}, expected {expectedPcQuatBucket}.");
            sum = checked(sum + meta.QuatFrameCount);
        }
        return sum;
    }

    private static void ObservePacking(
        T4XAnimBoneCountPair pair,
        int normalDynamicKeys,
        int precisionDynamicKeys,
        XboxDynamicQuatMeta xboxDynamic,
        int normalConstantBones,
        int precisionConstantBones,
        PackingStats stats)
    {
        stats.Pairs++;

        if (pair.Pc.DataByteCount == pair.Xbox.DataByteCount) stats.DataByteEqual++;
        if (pair.Pc.DataIntCount == pair.Xbox.DataIntCount) stats.DataIntEqual++;
        if (pair.Pc.RandomDataByteCount == pair.Xbox.RandomDataByteCount) stats.RandomDataByteEqual++;
        if (pair.Pc.RandomDataIntCount == pair.Xbox.RandomDataIntCount) stats.RandomDataIntEqual++;
        if (pair.Pc.IndexCount == pair.Xbox.IndexCount) stats.IndexEqual++;

        var randomShortDelta = checked((int)pair.Pc.RandomDataShortCount - (int)pair.Xbox.RandomDataShortCount);
        var dataShortDelta = (int)pair.Pc.DataShortCount - pair.Xbox.DataShortCount;
        var randomIntDelta = (int)pair.Xbox.RandomDataIntCount - pair.Pc.RandomDataIntCount;
        var dataIntDelta = (int)pair.Xbox.DataIntCount - pair.Pc.DataIntCount;

        stats.RandomShortDeltas.Add(randomShortDelta);
        stats.DataShortDeltas.Add(dataShortDelta);
        stats.RandomIntDeltas.Add(randomIntDelta);
        stats.DataIntDeltas.Add(dataIntDelta);
        stats.Rows.Add(new PackingRow(
            normalDynamicKeys,
            precisionDynamicKeys,
            normalConstantBones,
            precisionConstantBones,
            randomShortDelta,
            dataShortDelta,
            randomIntDelta,
            dataIntDelta,
            checked((int)pair.Xbox.IndexCount - (int)pair.Pc.IndexCount)));

        if (normalDynamicKeys == xboxDynamic.NormalKeys) stats.PcVsXboxNormalKeyCountEqual++;
        if (precisionDynamicKeys == xboxDynamic.PrecisionKeys) stats.PcVsXboxPrecisionKeyCountEqual++;
        stats.PrecisionKeyCountDeltas.Add(precisionDynamicKeys - xboxDynamic.PrecisionKeys);

        // PC T4 FULL_QUAT contributes four int16 values per dynamic key.
        // Xbox NORMAL_QUAT is accounted for by randomDataInt. Test how many
        // int16 values remain per Xbox PRECISION_QUAT key after allowing the
        // PC and Xbox precision key counts to differ.
        var pcFullQuatShorts = checked((normalDynamicKeys + precisionDynamicKeys) * 4);
        if (randomShortDelta == pcFullQuatShorts - checked(xboxDynamic.PrecisionKeys * 2))
            stats.PrecisionWidth2Matches++;
        if (randomShortDelta == pcFullQuatShorts - checked(xboxDynamic.PrecisionKeys * 3))
            stats.PrecisionWidth3Matches++;
        if (randomShortDelta == pcFullQuatShorts - checked(xboxDynamic.PrecisionKeys * 4))
            stats.PrecisionWidth4Matches++;

        // If Xbox NORMAL_QUAT uses a 3x16-bit packed representation while
        // PRECISION_QUAT preserves PC's 4x16-bit representation, exactly one
        // randomDataShort is saved per normal dynamic key and exactly one
        // dataShort is saved per normal constant quat.
        if (randomShortDelta == normalDynamicKeys) stats.NormalDynamicThreeShorts++;
        if (randomShortDelta == precisionDynamicKeys) stats.PrecisionDynamicThreeShorts++;
        if (dataShortDelta == normalConstantBones) stats.NormalConstantThreeShorts++;
        if (dataShortDelta == precisionConstantBones) stats.PrecisionConstantThreeShorts++;
        if (randomShortDelta == normalDynamicKeys && dataShortDelta == normalConstantBones)
            stats.NormalThreeShortsBoth++;

        // Stronger Xbox hypothesis: NORMAL_QUAT is a packed 32-bit quaternion.
        // PC FULL_QUAT contributes four int16 values per key; Xbox removes all
        // four from randomDataShort and emits one uint32 in randomDataInt.
        // The constant NORMAL_QUAT_NO_SIZE path analogously removes four
        // dataShort values and emits one dataInt value.
        var dynamicPacked32 =
            randomShortDelta == checked(normalDynamicKeys * 4) &&
            randomIntDelta == normalDynamicKeys;
        var constantPacked32 =
            dataShortDelta == checked(normalConstantBones * 4) &&
            dataIntDelta == normalConstantBones;
        if (dynamicPacked32) stats.NormalDynamicPacked32++;
        if (dataShortDelta == checked(normalConstantBones * 4)) stats.NormalConstantPacked32++;
        if (dataIntDelta == normalConstantBones) stats.NormalConstantDataIntGain++;

        // PC FULL_QUAT_NO_SIZE contributes four shorts. If Xbox emits one
        // dataInt plus K retained shorts, the dataShort decrease is (4-K)
        // per NORMAL_QUAT_NO_SIZE bone.
        if (dataShortDelta == checked(normalConstantBones * 4)) stats.NormalConstantLeaves0Shorts++;
        if (dataShortDelta == checked(normalConstantBones * 3)) stats.NormalConstantLeaves1Short++;
        if (dataShortDelta == checked(normalConstantBones * 2)) stats.NormalConstantLeaves2Shorts++;
        if (dataShortDelta == normalConstantBones) stats.NormalConstantLeaves3Shorts++;

        if (dynamicPacked32 && constantPacked32) stats.NormalPacked32All++;

        // Competing hypothesis: one subgroup migrates into the int streams.
        if (randomIntDelta == normalDynamicKeys) stats.RandomIntGainEqualsNormalKeys++;
        if (randomIntDelta == checked(normalDynamicKeys * 2)) stats.RandomIntGainEqualsTwiceNormalKeys++;
        if (randomIntDelta == precisionDynamicKeys) stats.RandomIntGainEqualsPrecisionKeys++;
        if (randomIntDelta == checked(precisionDynamicKeys * 2)) stats.RandomIntGainEqualsTwicePrecisionKeys++;
    }

    private static XboxDynamicQuatMeta ReadXboxDynamicQuatMetadata(
        byte[] bytes,
        T4XAnimBoneCountSample sample,
        IReadOnlyList<string> names)
    {
        if (sample.Platform != WaWPlatform.Xbox360 || sample.FixedSize != 0x5C)
            throw new InvalidDataException("Xbox dynamic metadata reader received a non-Xbox XAnim sample.");

        var start = sample.HeaderOffset;
        var cursor = checked(start + sample.FixedSize);

        while (cursor < bytes.Length && bytes[cursor] != 0) cursor++;
        if (cursor >= bytes.Length)
            throw new InvalidDataException($"Xbox XAnim '{sample.Name}' top-level name is unterminated.");
        cursor++;

        var totalBones = sample.BoneCounts[11];
        if (names.Count != totalBones)
            throw new InvalidDataException($"Xbox XAnim '{sample.Name}' names[] count mismatch.");

        var ptrNames = ReadU32Be(bytes, start + 0x34);
        var ptrDataByte = ReadU32Be(bytes, start + 0x38);
        var ptrDataShort = ReadU32Be(bytes, start + 0x3C);
        var ptrNotify = ReadU32Be(bytes, start + 0x54);
        var ptrDelta = ReadU32Be(bytes, start + 0x58);
        var notifyCount = bytes[start + 0x1E];
        var numFrames = ReadU16Be(bytes, start + 0x0E);

        if (ptrNames != 0)
            cursor = Advance(bytes, cursor, totalBones * 2, "Xbox names[]");
        if (ptrNotify != 0)
            cursor = Advance(bytes, cursor, notifyCount * 8, "Xbox notify[]");

        if (ptrDelta != 0)
        {
            Ensure(bytes, cursor, 8, "Xbox deltaPart");
            var transPtr = ReadU32Be(bytes, cursor);
            var quatPtr = ReadU32Be(bytes, cursor + 4);
            cursor += 8;
            if (transPtr != 0)
                cursor = SkipDeltaTransBe(bytes, cursor, numFrames);
            if (quatPtr != 0)
                cursor = SkipDeltaQuatBe(bytes, cursor, numFrames);
        }

        if (ptrDataByte != 0)
            cursor = Advance(bytes, cursor, sample.DataByteCount, "Xbox dataByte");
        else if (sample.DataByteCount != 0)
            throw new InvalidDataException($"Xbox XAnim '{sample.Name}' has dataByteCount without dataByte pointer.");

        if (ptrDataShort == 0)
        {
            if (sample.DataShortCount != 0)
                throw new InvalidDataException($"Xbox XAnim '{sample.Name}' has dataShortCount without dataShort pointer.");
            return new XboxDynamicQuatMeta
            {
                FrameCountByBone = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                NormalKeys = 0,
                PrecisionKeys = 0
            };
        }

        var dataShortOffset = cursor;
        var dsPos = 0;
        ushort PopShort()
        {
            if (dsPos >= sample.DataShortCount)
                throw new InvalidDataException($"Xbox XAnim '{sample.Name}' exhausted dataShort.");
            var value = ReadU16Be(bytes, dataShortOffset + dsPos * 2);
            dsPos++;
            return value;
        }

        void SkipIndexMetadata(ushort storedSize)
        {
            if (numFrames < 256) return;
            var count = checked((int)storedSize + 1);
            var shorts = storedSize >= 64
                ? ((count - 2) / 256) + 2
                : count;
            if (dsPos > sample.DataShortCount - shorts)
                throw new InvalidDataException($"Xbox XAnim '{sample.Name}' exhausted dataShort index metadata.");
            dsPos += shorts;
        }

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var boneIndex = sample.BoneCounts[0];
        var normalKeys = 0;
        var precisionKeys = 0;

        // SIMPLE_QUAT dynamic [1].
        for (var i = 0; i < sample.BoneCounts[1]; i++, boneIndex++)
        {
            var storedSize = PopShort();
            SkipIndexMetadata(storedSize);
        }

        // NORMAL_QUAT dynamic [2].
        for (var i = 0; i < sample.BoneCounts[2]; i++, boneIndex++)
        {
            var storedSize = PopShort();
            var frames = checked((int)storedSize + 1);
            SkipIndexMetadata(storedSize);
            if (boneIndex >= names.Count) throw new InvalidDataException("Xbox NORMAL_QUAT name index out of range.");
            map[names[boneIndex]] = frames;
            normalKeys = checked(normalKeys + frames);
        }

        // PRECISION_QUAT dynamic [3].
        for (var i = 0; i < sample.BoneCounts[3]; i++, boneIndex++)
        {
            var storedSize = PopShort();
            var frames = checked((int)storedSize + 1);
            SkipIndexMetadata(storedSize);
            if (boneIndex >= names.Count) throw new InvalidDataException("Xbox PRECISION_QUAT name index out of range.");
            map[names[boneIndex]] = frames;
            precisionKeys = checked(precisionKeys + frames);
        }

        return new XboxDynamicQuatMeta
        {
            FrameCountByBone = map,
            NormalKeys = normalKeys,
            PrecisionKeys = precisionKeys
        };
    }

    private static ushort ReadU16Be(byte[] data, int offset)
    {
        Ensure(data, offset, 2, "be-u16");
        return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        Ensure(data, offset, 4, "be-u32");
        return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    }

    private static int SkipDeltaTransBe(byte[] data, int cursor, ushort numFrames)
    {
        Ensure(data, cursor, 4, "Xbox delta trans");
        var size = ReadU16Be(data, cursor);
        var small = data[cursor + 2] != 0;
        if (size == 0)
            return Advance(data, cursor, 16, "Xbox delta trans frame0");

        Ensure(data, cursor, 32, "Xbox delta trans frames");
        var framesPtr = ReadU32Be(data, cursor + 28);
        cursor += 32;
        var count = checked((int)size + 1);
        cursor = Advance(data, cursor, count * (numFrames < 256 ? 1 : 2), "Xbox delta trans indices");
        if (framesPtr != 0)
            cursor = Advance(data, cursor, count * (small ? 3 : 6), "Xbox delta trans frames");
        return cursor;
    }

    private static int SkipDeltaQuatBe(byte[] data, int cursor, ushort numFrames)
    {
        Ensure(data, cursor, 8, "Xbox delta quat");
        var size = ReadU16Be(data, cursor);
        if (size == 0)
            return cursor + 8;

        var framesPtr = ReadU32Be(data, cursor + 4);
        cursor += 8;
        var count = checked((int)size + 1);
        cursor = Advance(data, cursor, count * (numFrames < 256 ? 1 : 2), "Xbox delta quat indices");
        if (framesPtr != 0)
            cursor = Advance(data, cursor, count * 4, "Xbox delta quat frames");
        return cursor;
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
        PrecisionRuleStats precisionRule,
        PackingStats packing)
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
        sb.AppendLine();
        AppendPackingStats(sb, packing);

        return sb.ToString();
    }

    private static IReadOnlyList<(int A, int B, int Matches)> SolveTwoTerm(
        IReadOnlyList<PackingRow> rows,
        Func<PackingRow, int> x,
        Func<PackingRow, int> y,
        Func<PackingRow, int> target,
        int minCoeff = -8,
        int maxCoeff = 8)
    {
        var result = new List<(int A, int B, int Matches)>();
        for (var a = minCoeff; a <= maxCoeff; a++)
        {
            for (var b = minCoeff; b <= maxCoeff; b++)
            {
                var matches = 0;
                foreach (var row in rows)
                {
                    if (target(row) == checked(a * x(row) + b * y(row)))
                        matches++;
                }
                result.Add((a, b, matches));
            }
        }

        return result
            .OrderByDescending(v => v.Matches)
            .ThenBy(v => Math.Abs(v.A) + Math.Abs(v.B))
            .ThenBy(v => v.A)
            .ThenBy(v => v.B)
            .Take(8)
            .ToList();
    }

    private static IReadOnlyList<(int A, int B, int C, int Matches)> SolveThreeTerm(
        IReadOnlyList<PackingRow> rows,
        Func<PackingRow, int> x,
        Func<PackingRow, int> y,
        Func<PackingRow, int> z,
        Func<PackingRow, int> target,
        int minCoeff = -8,
        int maxCoeff = 8)
    {
        var result = new List<(int A, int B, int C, int Matches)>();
        for (var a = minCoeff; a <= maxCoeff; a++)
        {
            for (var b = minCoeff; b <= maxCoeff; b++)
            {
                for (var cc = minCoeff; cc <= maxCoeff; cc++)
                {
                    var matches = 0;
                    foreach (var row in rows)
                    {
                        if (target(row) == checked(a * x(row) + b * y(row) + cc * z(row)))
                            matches++;
                    }
                    result.Add((a, b, cc, matches));
                }
            }
        }

        return result
            .OrderByDescending(v => v.Matches)
            .ThenBy(v => Math.Abs(v.A) + Math.Abs(v.B) + Math.Abs(v.C))
            .ThenBy(v => v.A)
            .ThenBy(v => v.B)
            .ThenBy(v => v.C)
            .Take(8)
            .ToList();
    }

    private static void AppendPackingStats(StringBuilder sb, PackingStats stats)
    {
        static string Range(IReadOnlyList<int> values) =>
            values.Count == 0 ? "<none>" : $"{values.Min():N0}..{values.Max():N0}";

        sb.AppendLine("XBOX QUATERNION PACKING HYPOTHESES");
        sb.AppendLine($"  Pairs:                                      {stats.Pairs:N0}");
        sb.AppendLine("  Unrelated stream equality:");
        sb.AppendLine($"    dataByteCount identical:                  {stats.DataByteEqual}/{stats.Pairs}");
        sb.AppendLine($"    dataIntCount identical:                   {stats.DataIntEqual}/{stats.Pairs}");
        sb.AppendLine($"    randomDataByteCount identical:            {stats.RandomDataByteEqual}/{stats.Pairs}");
        sb.AppendLine($"    randomDataIntCount identical:             {stats.RandomDataIntEqual}/{stats.Pairs}");
        sb.AppendLine($"    indexCount identical:                     {stats.IndexEqual}/{stats.Pairs}");
        sb.AppendLine();
        sb.AppendLine("  PC vs Xbox dynamic key counts:");
        sb.AppendLine($"    NORMAL_QUAT key count identical:                              {stats.PcVsXboxNormalKeyCountEqual}/{stats.Pairs}");
        sb.AppendLine($"    PRECISION_QUAT key count identical:                           {stats.PcVsXboxPrecisionKeyCountEqual}/{stats.Pairs}");
        sb.AppendLine($"    PC precision keys - Xbox precision keys range:                {Range(stats.PrecisionKeyCountDeltas)}");
        sb.AppendLine();
        sb.AppendLine("  Precision payload width after using Xbox's own key count:");
        sb.AppendLine($"    Xbox PRECISION_QUAT = 2 shorts/key:                           {stats.PrecisionWidth2Matches}/{stats.Pairs}");
        sb.AppendLine($"    Xbox PRECISION_QUAT = 3 shorts/key:                           {stats.PrecisionWidth3Matches}/{stats.Pairs}");
        sb.AppendLine($"    Xbox PRECISION_QUAT = 4 shorts/key:                           {stats.PrecisionWidth4Matches}/{stats.Pairs}");
        sb.AppendLine();
        sb.AppendLine("  PRIMARY CANDIDATE: NORMAL_QUAT is packed to one 32-bit word; PRECISION_QUAT stays 4x16-bit");
        sb.AppendLine($"    randomShort decrease == 4 * normal dynamic keys AND randomInt gain == keys: {stats.NormalDynamicPacked32}/{stats.Pairs}");
        sb.AppendLine($"    dataShort decrease == 4 * normal constant bones:                          {stats.NormalConstantPacked32}/{stats.Pairs}");
        sb.AppendLine($"    dataInt gain == normal constant bones:                                    {stats.NormalConstantDataIntGain}/{stats.Pairs}");
        sb.AppendLine($"    ALL dynamic + constant packed32 equations true:                           {stats.NormalPacked32All}/{stats.Pairs}");
        sb.AppendLine();
        sb.AppendLine("  Constant NORMAL_QUAT_NO_SIZE retained-short width (plus one dataInt):");
        sb.AppendLine($"    keeps 0 shorts (dataShort decreases by 4/bone):             {stats.NormalConstantLeaves0Shorts}/{stats.Pairs}");
        sb.AppendLine($"    keeps 1 short  (dataShort decreases by 3/bone):             {stats.NormalConstantLeaves1Short}/{stats.Pairs}");
        sb.AppendLine($"    keeps 2 shorts (dataShort decreases by 2/bone):             {stats.NormalConstantLeaves2Shorts}/{stats.Pairs}");
        sb.AppendLine($"    keeps 3 shorts (dataShort decreases by 1/bone):             {stats.NormalConstantLeaves3Shorts}/{stats.Pairs}");
        sb.AppendLine();
        sb.AppendLine("  Earlier 3x16-bit candidate:");
        sb.AppendLine($"    PC randomShort - XB randomShort == normal dynamic key count: {stats.NormalDynamicThreeShorts}/{stats.Pairs}");
        sb.AppendLine($"    PC dataShort - XB dataShort == normal constant bone count:   {stats.NormalConstantThreeShorts}/{stats.Pairs}");
        sb.AppendLine($"    BOTH equations true for the pair:                            {stats.NormalThreeShortsBoth}/{stats.Pairs}");
        sb.AppendLine();
        sb.AppendLine("  Competing checks:");
        sb.AppendLine($"    randomShort delta == precision dynamic key count:             {stats.PrecisionDynamicThreeShorts}/{stats.Pairs}");
        sb.AppendLine($"    dataShort delta == precision constant bone count:             {stats.PrecisionConstantThreeShorts}/{stats.Pairs}");
        sb.AppendLine($"    randomInt gain == normal dynamic keys:                        {stats.RandomIntGainEqualsNormalKeys}/{stats.Pairs}");
        sb.AppendLine($"    randomInt gain == 2 * normal dynamic keys:                    {stats.RandomIntGainEqualsTwiceNormalKeys}/{stats.Pairs}");
        sb.AppendLine($"    randomInt gain == precision dynamic keys:                     {stats.RandomIntGainEqualsPrecisionKeys}/{stats.Pairs}");
        sb.AppendLine($"    randomInt gain == 2 * precision dynamic keys:                 {stats.RandomIntGainEqualsTwicePrecisionKeys}/{stats.Pairs}");
        sb.AppendLine();
        sb.AppendLine("  Observed PC->Xbox count deltas:");
        sb.AppendLine($"    randomDataShort decrease: {Range(stats.RandomShortDeltas)}");
        sb.AppendLine($"    dataShort decrease:       {Range(stats.DataShortDeltas)}");
        sb.AppendLine($"    randomDataInt increase:   {Range(stats.RandomIntDeltas)}");
        sb.AppendLine($"    dataInt increase:         {Range(stats.DataIntDeltas)}");

        sb.AppendLine();
        sb.AppendLine("  EXACT INTEGER EQUATION SEARCH");
        sb.AppendLine("    randomShortDelta = A*normalDynamicKeys + B*precisionDynamicKeys");
        foreach (var v in SolveTwoTerm(
                     stats.Rows,
                     r => r.NormalDynamicKeys,
                     r => r.PrecisionDynamicKeys,
                     r => r.RandomShortDelta))
            sb.AppendLine($"      A={v.A,3}, B={v.B,3} -> {v.Matches}/{stats.Pairs}");

        sb.AppendLine();
        sb.AppendLine("    dataShortDelta = A*normalConstant + B*precisionConstant + C*indexDelta");
        foreach (var v in SolveThreeTerm(
                     stats.Rows,
                     r => r.NormalConstantBones,
                     r => r.PrecisionConstantBones,
                     r => r.IndexDelta,
                     r => r.DataShortDelta))
            sb.AppendLine($"      A={v.A,3}, B={v.B,3}, C={v.C,3} -> {v.Matches}/{stats.Pairs}");

        sb.AppendLine();
        sb.AppendLine("    indexDelta = Xbox.indexCount - PC.indexCount");
        var indexDeltas = stats.Rows.Select(r => r.IndexDelta).ToList();
        sb.AppendLine($"      range: {Range(indexDeltas)}");
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
