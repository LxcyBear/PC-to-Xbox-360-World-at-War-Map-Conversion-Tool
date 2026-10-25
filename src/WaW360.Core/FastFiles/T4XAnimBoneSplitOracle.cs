using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4XAnimSplitBoneProfile(
    string BoneName,
    int Observations,
    int FullQuatA,
    int FullQuatB,
    int FullQuatNoSizeA,
    int FullQuatNoSizeB,
    bool ConflictingSide);

public sealed class T4XAnimBoneSplitOracleResult
{
    public required int MatchedAnimations { get; init; }
    public required int NameOrderMatches { get; init; }
    public required int NameOrderMismatches { get; init; }
    public required int UnresolvedScriptStrings { get; init; }
    public required IReadOnlyList<T4XAnimSplitBoneProfile> Profiles { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// Resolves XAnim.names ScriptStrings for same-name PC/Xbox assets and profiles the
/// two Xbox-only quaternion subdivisions by bone identity.
///
/// The 413-animation count oracle has already proven:
///   PC[2] = XB[2] + XB[3]
///   PC[4] = XB[5] + XB[6]
/// and that all other quaternion/translation categories map directly.
/// This oracle asks the remaining question: does a named bone consistently choose
/// the A or B side of those two subdivisions across animations?
/// </summary>
public static class T4XAnimBoneSplitOracle
{
    private enum SplitSide { A, B }

    private sealed class MutableProfile
    {
        public int FullQuatA;
        public int FullQuatB;
        public int FullQuatNoSizeA;
        public int FullQuatNoSizeB;

        public int Observations => FullQuatA + FullQuatB + FullQuatNoSizeA + FullQuatNoSizeB;
        public bool ConflictingSide =>
            (FullQuatA + FullQuatNoSizeA) != 0 &&
            (FullQuatB + FullQuatNoSizeB) != 0;
    }

    public static async Task<T4XAnimBoneSplitOracleResult> AnalyzeAsync(
        string pcFastFile,
        string xboxFastFile,
        CancellationToken cancellationToken = default)
    {
        var comparison = await T4XAnimBoneCountOracle.CompareAsync(
            pcFastFile, xboxFastFile, animationName: null, cancellationToken);

        var pcZone = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, cancellationToken);
        var xboxZone = await FastFileAnalysisCache.GetZoneAsync(xboxFastFile, cancellationToken);

        if (pcZone.Platform != WaWPlatform.PC || xboxZone.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException("Bone-split oracle requires PC source + Xbox 360 oracle fastfiles.");

        var profiles = new Dictionary<string, MutableProfile>(StringComparer.OrdinalIgnoreCase);
        var nameOrderMatches = 0;
        var nameOrderMismatches = 0;
        var unresolved = 0;

        foreach (var pair in comparison.Matches.Where(x => x.ExpandedLayoutMatches))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pcNames = ReadNames(pcZone, pair.Pc, ref unresolved);
            var xboxNames = ReadNames(xboxZone, pair.Xbox, ref unresolved);

            if (pcNames.Count != xboxNames.Count ||
                !pcNames.SequenceEqual(xboxNames, StringComparer.OrdinalIgnoreCase))
            {
                nameOrderMismatches++;
                continue;
            }

            nameOrderMatches++;

            // PC full-quat category [2] is subdivided into Xbox [2] (A) + [3] (B).
            var pcFullQuatStart = pair.Pc.BoneCounts[0] + pair.Pc.BoneCounts[1];
            var xbFullQuatA = pair.Xbox.BoneCounts[2];
            var xbFullQuatB = pair.Xbox.BoneCounts[3];
            ObserveRange(
                pcNames,
                pcFullQuatStart,
                xbFullQuatA,
                xbFullQuatB,
                profiles,
                fullQuatNoSize: false);

            // PC full-quat-no-size category [4] is subdivided into Xbox [5] (A) + [6] (B).
            var pcFullQuatNoSizeStart =
                pair.Pc.BoneCounts[0] +
                pair.Pc.BoneCounts[1] +
                pair.Pc.BoneCounts[2] +
                pair.Pc.BoneCounts[3];
            var xbFullQuatNoSizeA = pair.Xbox.BoneCounts[5];
            var xbFullQuatNoSizeB = pair.Xbox.BoneCounts[6];
            ObserveRange(
                pcNames,
                pcFullQuatNoSizeStart,
                xbFullQuatNoSizeA,
                xbFullQuatNoSizeB,
                profiles,
                fullQuatNoSize: true);
        }

        var frozen = profiles
            .Select(kv => new T4XAnimSplitBoneProfile(
                kv.Key,
                kv.Value.Observations,
                kv.Value.FullQuatA,
                kv.Value.FullQuatB,
                kv.Value.FullQuatNoSizeA,
                kv.Value.FullQuatNoSizeB,
                kv.Value.ConflictingSide))
            .OrderByDescending(x => x.Observations)
            .ThenBy(x => x.BoneName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var summary = BuildSummary(comparison.Matches.Count, nameOrderMatches, nameOrderMismatches, unresolved, frozen);

        return new T4XAnimBoneSplitOracleResult
        {
            MatchedAnimations = comparison.Matches.Count,
            NameOrderMatches = nameOrderMatches,
            NameOrderMismatches = nameOrderMismatches,
            UnresolvedScriptStrings = unresolved,
            Profiles = frozen,
            Summary = summary
        };
    }

    private static IReadOnlyList<string> ReadNames(
        FastFileAnalysisCache.CachedZone zone,
        T4XAnimBoneCountSample sample,
        ref int unresolved)
    {
        var bytes = zone.Bytes;
        var cursor = checked(sample.HeaderOffset + sample.FixedSize);

        // Oracle candidates require inline top-level names. Skip the serialized XString.
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
            if (cursor > bytes.Length - 2)
                throw new InvalidDataException($"XAnim '{sample.Name}' names[] exceeds zone bounds.");

            var id = sample.Platform == WaWPlatform.Xbox360
                ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(cursor, 2))
                : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor, 2));
            cursor += 2;

            if (id < zone.Document.ScriptStrings.Count &&
                zone.Document.ScriptStrings[id].Value is { Length: > 0 } value &&
                !value.StartsWith("<alias ", StringComparison.Ordinal))
            {
                result.Add(value);
            }
            else
            {
                unresolved++;
                result.Add($"<scriptstring:{id}>");
            }
        }

        return result;
    }

    private static void ObserveRange(
        IReadOnlyList<string> names,
        int start,
        int countA,
        int countB,
        Dictionary<string, MutableProfile> profiles,
        bool fullQuatNoSize)
    {
        var total = checked(countA + countB);
        if (start < 0 || total < 0 || start > names.Count - total)
            throw new InvalidDataException(
                $"Quaternion split range [{start}, {start + total}) exceeds names[] count {names.Count}.");

        for (var i = 0; i < countA; i++)
            Observe(names[start + i], SplitSide.A, profiles, fullQuatNoSize);

        for (var i = 0; i < countB; i++)
            Observe(names[start + countA + i], SplitSide.B, profiles, fullQuatNoSize);
    }

    private static void Observe(
        string boneName,
        SplitSide side,
        Dictionary<string, MutableProfile> profiles,
        bool fullQuatNoSize)
    {
        if (!profiles.TryGetValue(boneName, out var p))
        {
            p = new MutableProfile();
            profiles.Add(boneName, p);
        }

        if (fullQuatNoSize)
        {
            if (side == SplitSide.A) p.FullQuatNoSizeA++;
            else p.FullQuatNoSizeB++;
        }
        else
        {
            if (side == SplitSide.A) p.FullQuatA++;
            else p.FullQuatB++;
        }
    }

    private static string BuildSummary(
        int matchedAnimations,
        int nameOrderMatches,
        int nameOrderMismatches,
        int unresolved,
        IReadOnlyList<T4XAnimSplitBoneProfile> profiles)
    {
        var aOnly = profiles.Count(x => !x.ConflictingSide && x.FullQuatA + x.FullQuatNoSizeA > 0);
        var bOnly = profiles.Count(x => !x.ConflictingSide && x.FullQuatB + x.FullQuatNoSizeB > 0);
        var conflicts = profiles.Where(x => x.ConflictingSide).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("WaW360 XAnim quaternion split-by-bone oracle");
        sb.AppendLine($"Same-name count matches:                    {matchedAnimations:N0}");
        sb.AppendLine($"PC/Xbox names[] order identical:            {nameOrderMatches:N0}");
        sb.AppendLine($"PC/Xbox names[] order mismatches:           {nameOrderMismatches:N0}");
        sb.AppendLine($"Unresolved serialized ScriptString entries: {unresolved:N0}");
        sb.AppendLine($"Unique bones observed in split categories:  {profiles.Count:N0}");
        sb.AppendLine($"Consistent subgroup A bones:                {aOnly:N0}");
        sb.AppendLine($"Consistent subgroup B bones:                {bOnly:N0}");
        sb.AppendLine($"Bones observed on BOTH A and B:             {conflicts.Count:N0}");
        sb.AppendLine();

        if (conflicts.Count == 0 && nameOrderMatches > 0)
            sb.AppendLine("RESULT: split side is fully consistent with bone identity in the observed corpus.");
        else
            sb.AppendLine("RESULT: bone identity alone is not yet sufficient; inspect conflicts/name-order mismatches.");

        sb.AppendLine();
        sb.AppendLine("SUBGROUP B BONES (name | observations | fullQuat B | fullQuatNoSize B)");
        var bBones = profiles
            .Where(x => !x.ConflictingSide && x.FullQuatB + x.FullQuatNoSizeB > 0)
            .OrderByDescending(x => x.Observations)
            .ThenBy(x => x.BoneName, StringComparer.OrdinalIgnoreCase)
            .Take(96)
            .ToList();
        if (bBones.Count == 0) sb.AppendLine("  <none>");
        foreach (var p in bBones)
            sb.AppendLine($"  {p.BoneName} | {p.Observations} | {p.FullQuatB} | {p.FullQuatNoSizeB}");

        if (conflicts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("CONFLICTS (name | qA/qB | qNoSizeA/qNoSizeB)");
            foreach (var p in conflicts.Take(96))
                sb.AppendLine($"  {p.BoneName} | {p.FullQuatA}/{p.FullQuatB} | {p.FullQuatNoSizeA}/{p.FullQuatNoSizeB}");
        }

        return sb.ToString();
    }
}
