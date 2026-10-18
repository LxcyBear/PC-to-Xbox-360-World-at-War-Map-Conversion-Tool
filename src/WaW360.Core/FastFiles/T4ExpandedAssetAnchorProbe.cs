using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4ExpandedTargetRun(
    int FirstAssetIndex,
    int LastAssetIndex,
    string TargetType,
    int ExpectedTargetCount,
    int ObservedTargetCount,
    int Deficit,
    int WindowStart,
    int WindowEnd,
    int? PreviousAnchorAssetIndex,
    string? PreviousAnchorType,
    int? PreviousAnchorEnd,
    int? NextAnchorAssetIndex,
    string? NextAnchorType,
    int? NextAnchorStart,
    string TableContext);

public sealed record T4MaterialAnchorCandidate(int Offset, int End, string Name);

public sealed class T4ExpandedAnchorProbeResult
{
    public required string TargetType { get; init; }
    public required int ExpandedExpectedAssets { get; init; }
    public required int ExpandedObservedCandidates { get; init; }
    public required int MaterialAnchorCandidates { get; init; }
    public required int MatchedMaterialAnchors { get; init; }
    public required int MissingTargetAssets { get; init; }
    public required IReadOnlyList<T4ExpandedTargetRun> TargetRuns { get; init; }
    public required IReadOnlyList<T4AssetGapWindow> MicroscopeWindows { get; init; }
    public required string ReportFile { get; init; }
}

/// <summary>
/// Alpha 9i diagnostic-only expanded XAsset-order probe.
///
/// The proven serializer still aligns only XAnim/XModel/Weapon/FX.  That is a
/// good fail-closed acceptance rule, but it can make a forensic window appear to
/// contain a missing XModel/XAnim when an untracked top-level Material actually
/// occupies those bytes.  This probe adds structurally measured Material anchors
/// and then reports target-type deficits between matched NON-target anchors.
///
/// Nothing discovered here is ever accepted for serialization.
/// </summary>
public static class T4ExpandedAssetAnchorProbe
{
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        "xanim", "xmodel", "weapon", "fx", "material"
    };

    private sealed record Expected(T4AssetEntry Asset);
    private sealed record Observed(string TypeName, int Start, int End, string Name, string Source);

    public static T4ExpandedAnchorProbeResult AnalyzeAndWrite(
        byte[] zone,
        T4ZoneDocument document,
        string targetType,
        IReadOnlyList<int> acceptedTargetOffsets,
        string? xanimProofFile,
        string? weaponOwnedProofFile,
        string reportFile,
        CancellationToken cancellationToken = default)
    {
        if (!targetType.Equals("xmodel", StringComparison.OrdinalIgnoreCase) &&
            !targetType.Equals("xanim", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentOutOfRangeException(nameof(targetType), "Alpha 9i expanded anchors support XModel/XAnim diagnostics only.");

        var payloadStart = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));
        var expected = document.Assets
            .Where(a => Types.Contains(a.TypeName) && a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .Select(a => new Expected(a))
            .ToList();

        var observed = BuildObserved(zone, document, targetType, acceptedTargetOffsets,
            xanimProofFile, weaponOwnedProofFile, payloadStart, cancellationToken);

        var materials = CachedMaterialCandidates(zone, document, payloadStart, cancellationToken);
        var ownedRanges = MergeRanges(observed.Select(o => (o.Start, o.End)));
        foreach (var material in materials)
        {
            // Materials serialized inside an accepted XModel/Weapon/FX are nested
            // dependencies, not top-level XAsset anchors.  Exclude them from the
            // expanded order stream; only free-standing measured Material bodies
            // are useful as landmarks between top-level assets.
            if (InsideOwnedRange(material.Offset, ownedRanges)) continue;
            observed.Add(new Observed("material", material.Offset, material.End, material.Name, "measured-material-anchor"));
        }

        observed = Normalize(observed, payloadStart, zone.Length);
        var alignment = Align(expected, observed);
        var map = alignment.ToDictionary(x => x.ExpectedIndex, x => x.ObservedIndex);
        var matchedObserved = alignment.Select(x => x.ObservedIndex).ToHashSet();

        var targetExpectedIndices = Enumerable.Range(0, expected.Count)
            .Where(i => expected[i].Asset.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var missingExpected = targetExpectedIndices.Where(i => !map.ContainsKey(i)).ToList();

        var runs = BuildTargetRuns(expected, observed, map, targetType, missingExpected, payloadStart, zone.Length);
        var windows = new List<T4AssetGapWindow>();
        foreach (var run in runs.Where(r => r.Deficit > 0 && r.WindowStart < r.WindowEnd))
        {
            var missingInRun = missingExpected
                .Where(i => expected[i].Asset.Index >= run.FirstAssetIndex && expected[i].Asset.Index <= run.LastAssetIndex)
                .ToList();
            if (missingInRun.Count == 0)
            {
                // Repeated same-type alignment can choose a different individual slot.
                // Use the first target slot(s) in this run purely as labels; the run-level
                // deficit is the authoritative diagnostic fact.
                missingInRun = targetExpectedIndices
                    .Where(i => expected[i].Asset.Index >= run.FirstAssetIndex && expected[i].Asset.Index <= run.LastAssetIndex)
                    .Take(Math.Max(1, run.Deficit))
                    .ToList();
            }

            foreach (var i in missingInRun.Take(Math.Max(1, run.Deficit)))
            {
                windows.Add(new T4AssetGapWindow(
                    i,
                    expected[i].Asset.Index,
                    expected[i].Asset.TypeName,
                    run.WindowStart,
                    run.WindowEnd,
                    run.PreviousAnchorAssetIndex,
                    run.PreviousAnchorType,
                    null,
                    run.PreviousAnchorEnd,
                    run.NextAnchorAssetIndex,
                    run.NextAnchorType,
                    run.NextAnchorStart,
                    null,
                    run.TableContext));
            }
        }

        var matchedMaterials = alignment.Count(pair =>
            expected[pair.ExpectedIndex].Asset.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase) &&
            observed[pair.ObservedIndex].TypeName.Equals("material", StringComparison.OrdinalIgnoreCase));

        var result = new T4ExpandedAnchorProbeResult
        {
            TargetType = targetType,
            ExpandedExpectedAssets = expected.Count,
            ExpandedObservedCandidates = observed.Count,
            MaterialAnchorCandidates = materials.Count,
            MatchedMaterialAnchors = matchedMaterials,
            MissingTargetAssets = missingExpected.Count,
            TargetRuns = runs,
            MicroscopeWindows = windows,
            ReportFile = reportFile
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile))!);
        File.WriteAllText(reportFile, Render(result, expected, observed, map, matchedObserved, document), Encoding.UTF8);
        return result;
    }

    private static List<Observed> BuildObserved(
        byte[] zone,
        T4ZoneDocument document,
        string targetType,
        IReadOnlyList<int> acceptedTargetOffsets,
        string? xanimProofFile,
        string? weaponOwnedProofFile,
        int payloadStart,
        CancellationToken cancellationToken)
    {
        var observed = new List<Observed>();

        var xanim = FastFileAnalysisCache.GetOrCreateDerived(
            document.SourceFastFile, "mixed:xanim-diagnostic:a9g",
            () => T4XAnimSerializerProof.DiscoverDiagnosticCandidates(zone, Endian.Little).ToList());
        var xmodel = FastFileAnalysisCache.GetOrCreateDerived(
            document.SourceFastFile, "mixed:xmodel-diagnostic:a9g",
            () => T4XModelSerializerProof.DiscoverDiagnosticCandidates(zone, Endian.Little, document.Header).ToList());
        var fx = FastFileAnalysisCache.GetOrCreateDerived(
            document.SourceFastFile, $"mixed:fx-diagnostic:a9b:{payloadStart}",
            () => T4FxSerializerProof.DiscoverDiagnosticCandidates(zone, document.Header, payloadStart, cancellationToken).ToList());

        // For the target type, use the exact set already accepted by the proven
        // mixed-order selector (including packed-name matches). This keeps the
        // expanded diagnostic's deficit equal to the real fail-closed deficit.
        if (targetType.Equals("xanim", StringComparison.OrdinalIgnoreCase))
        {
            var strict = xanim.ToDictionary(x => x.Offset);
            foreach (var offset in acceptedTargetOffsets.Distinct().OrderBy(x => x))
            {
                if (strict.TryGetValue(offset, out var a))
                    observed.Add(new Observed("xanim", a.Offset, a.End, a.Name, "accepted-xanim"));
                else
                {
                    try
                    {
                        var a2 = T4XAnimSerializerProof.MeasureDiagnosticCandidateAt(zone, offset, Endian.Little, allowPackedTopLevelName: true);
                        observed.Add(new Observed("xanim", a2.Offset, a2.End, a2.Name, "accepted-xanim-packed"));
                    }
                    catch { }
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(xanimProofFile) && File.Exists(xanimProofFile))
        {
            observed.AddRange(ReadXAnimProof(xanimProofFile!));
        }
        else
        {
            observed.AddRange(xanim.Select(a => new Observed("xanim", a.Offset, a.End, a.Name, "typed-xanim")));
        }

        if (targetType.Equals("xmodel", StringComparison.OrdinalIgnoreCase))
        {
            var strict = xmodel.ToDictionary(x => x.Offset);
            foreach (var offset in acceptedTargetOffsets.Distinct().OrderBy(x => x))
            {
                if (strict.TryGetValue(offset, out var a))
                    observed.Add(new Observed("xmodel", a.Offset, a.End, a.Name, "accepted-xmodel"));
                else
                {
                    try
                    {
                        var a2 = T4XModelSerializerProof.MeasureDiagnosticCandidateAt(
                            zone, offset, Endian.Little, document.Header, allowPackedTopLevelName: true);
                        observed.Add(new Observed("xmodel", a2.Offset, a2.End, a2.Name, "accepted-xmodel-packed"));
                    }
                    catch { }
                }
            }
        }
        else
        {
            // For XAnim forensics, XModel is a non-target anchor.  Use the proven
            // mixed-order XModel selection rather than the intentionally noisy raw
            // candidate list. Fortress, for example, has 1,361 authoritative models
            // but hundreds of structural false positives in the raw scan.
            try
            {
                var selectedModels = T4MixedAssetOrderProbe.SelectAlignedXModels(
                    zone, document, null, weaponOwnedProofFile, cancellationToken);
                var strictModels = xmodel.ToDictionary(x => x.Offset);
                foreach (var offset in selectedModels.Offsets)
                {
                    if (strictModels.TryGetValue(offset, out var a))
                        observed.Add(new Observed("xmodel", a.Offset, a.End, a.Name, "authoritative-xmodel-anchor"));
                    else
                    {
                        try
                        {
                            var a2 = T4XModelSerializerProof.MeasureDiagnosticCandidateAt(
                                zone, offset, Endian.Little, document.Header, allowPackedTopLevelName: true);
                            observed.Add(new Observed("xmodel", a2.Offset, a2.End, a2.Name, "authoritative-xmodel-packed-anchor"));
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                observed.AddRange(xmodel.Select(a => new Observed("xmodel", a.Offset, a.End, a.Name, "typed-xmodel")));
            }
        }

        observed.AddRange(fx.Select(a => new Observed("fx", a.Offset, a.End, a.Name, "typed-fx")));
        if (!string.IsNullOrWhiteSpace(weaponOwnedProofFile) && File.Exists(weaponOwnedProofFile))
            observed.AddRange(ReadWeaponOwnedProof(weaponOwnedProofFile!));

        return observed;
    }

    /// <summary>
    /// Stage 5 alpha 10f bridge: returns the same conservative top-level Material
    /// candidates used by the 9i anchor probe.  Keeping one scanner prevents the native
    /// writer from inventing a second, looser Material acceptance rule.
    /// </summary>
    internal static IReadOnlyList<T4MaterialAnchorCandidate> DiscoverMaterialCandidatesForNative(
        byte[] zone, T4ZoneDocument document, int payloadStart, CancellationToken cancellationToken = default) =>
        CachedMaterialCandidates(zone, document, payloadStart, cancellationToken);

    private static IReadOnlyList<T4MaterialAnchorCandidate> CachedMaterialCandidates(
        byte[] zone, T4ZoneDocument document, int payloadStart, CancellationToken cancellationToken) =>
        FastFileAnalysisCache.GetOrCreateDerived(
            document.SourceFastFile, $"expanded:material-anchor:a9i:{payloadStart}",
            () => DiscoverMaterialCandidates(zone, document.Header, payloadStart, cancellationToken));

    private static List<T4MaterialAnchorCandidate> DiscoverMaterialCandidates(
        byte[] zone, ZoneHeaderSummary header, int payloadStart, CancellationToken cancellationToken)
    {
        const int fixedSize = 112;
        var result = new List<T4MaterialAnchorCandidate>();
        for (var offset = payloadStart; offset <= zone.Length - fixedSize - 2; offset++)
        {
            if ((offset & 0x7FFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            // Cheap sentinel prefilter before any 32-bit reads / parser work.
            if (zone[offset + 3] != 0xFF) continue;
            var namePtr = BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan(offset, 4));
            if (namePtr != T4ZonePointer.Following && namePtr != T4ZonePointer.Insert) continue;

            var textureCount = zone[offset + 91];
            var constantCount = zone[offset + 92];
            var stateCount = zone[offset + 93];
            if (textureCount > 64 || constantCount > 64 || stateCount > 64) continue;

            var technique = BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan(offset + 96, 4));
            var textures = BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan(offset + 100, 4));
            var constants = BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan(offset + 104, 4));
            var states = BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan(offset + 108, 4));
            if (!PointerWithinHeader(technique, header) || !PointerWithinHeader(textures, header) ||
                !PointerWithinHeader(constants, header) || !PointerWithinHeader(states, header)) continue;

            if (!TryReadAssetName(zone, offset + fixedSize, 384, out var name)) continue;
            try
            {
                var end = T4XModelSerializerProof.MeasureInlineMaterialEnd(zone, offset, Endian.Little, header);
                if (end > offset + fixedSize && end <= zone.Length)
                    result.Add(new T4MaterialAnchorCandidate(offset, end, name));
            }
            catch (InvalidDataException) { }
            catch (OverflowException) { }
        }
        return result.OrderBy(x => x.Offset).ToList();
    }

    private static bool PointerWithinHeader(uint raw, ZoneHeaderSummary header)
    {
        if (raw == 0 || raw == T4ZonePointer.Following || raw == T4ZonePointer.Insert) return true;
        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null) return false;
        var block = (int)decoded.Block.Value;
        return block >= 0 && block < header.BlockSizes.Count && decoded.Offset < header.BlockSizes[block];
    }

    private static bool TryReadAssetName(byte[] data, int start, int maxBytes, out string name)
    {
        name = string.Empty;
        if (start < 0 || start >= data.Length) return false;
        var end = start;
        var limit = Math.Min(data.Length, start + maxBytes);
        while (end < limit && data[end] != 0)
        {
            var b = data[end];
            if (b < 0x20 || b > 0x7E) return false;
            end++;
        }
        if (end == start || end >= limit || data[end] != 0) return false;
        name = Encoding.ASCII.GetString(data, start, end - start);
        if (name.Length > 260 || !name.Any(char.IsLetterOrDigit)) return false;
        return name.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or '/' or '\\' or '$' or '@' or '+' or '#');
    }

    private static List<(int Start, int End)> MergeRanges(IEnumerable<(int Start, int End)> ranges)
    {
        var sorted = ranges.Where(r => r.End > r.Start).OrderBy(r => r.Start).ThenByDescending(r => r.End).ToList();
        if (sorted.Count == 0) return new List<(int, int)>();
        var merged = new List<(int Start, int End)> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            var current = sorted[i];
            var last = merged[^1];
            if (current.Start <= last.End)
                merged[^1] = (last.Start, Math.Max(last.End, current.End));
            else
                merged.Add(current);
        }
        return merged;
    }

    private static bool InsideOwnedRange(int offset, IReadOnlyList<(int Start, int End)> ranges)
    {
        // Ranges are merged/sorted; binary search for the last range whose start
        // does not exceed the candidate. Exact range starts remain eligible so a
        // legitimate top-level Material cannot be hidden by a coincident false hit.
        var lo = 0; var hi = ranges.Count - 1; var found = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (ranges[mid].Start <= offset) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found >= 0 && offset > ranges[found].Start && offset < ranges[found].End;
    }

    private static List<Observed> Normalize(IEnumerable<Observed> observed, int payloadStart, int zoneLength) =>
        observed
            .Where(o => o.Start >= payloadStart && o.End > o.Start && o.End <= zoneLength)
            .GroupBy(o => (o.TypeName.ToLowerInvariant(), o.Start))
            .Select(g => g.OrderByDescending(x => SourceStrength(x.Source)).ThenByDescending(x => x.End).First())
            .OrderBy(o => o.Start)
            .ThenBy(o => TypeOrder(o.TypeName))
            .ThenByDescending(o => o.End)
            .ToList();

    private static int SourceStrength(string source) => source switch
    {
        "accepted-xanim" or "accepted-xanim-packed" or "accepted-xmodel" or "accepted-xmodel-packed" => 50,
        "authoritative-xmodel-anchor" or "authoritative-xmodel-packed-anchor" => 48,
        "alpha4-xanim-proof" or "alpha7b-weapon-proof" => 45,
        "typed-fx" => 35,
        "measured-material-anchor" => 25,
        _ => 10
    };

    private static List<(int ExpectedIndex, int ObservedIndex)> Align(
        IReadOnlyList<Expected> expected, IReadOnlyList<Observed> observed)
    {
        var n = expected.Count;
        var m = observed.Count;
        const int Inf = 1_000_000_000;
        var cost = new int[n + 1, m + 1];
        var prev = new byte[n + 1, m + 1];
        for (var i = 0; i <= n; i++) for (var j = 0; j <= m; j++) cost[i, j] = Inf;
        cost[0, 0] = 0;
        for (var i = 1; i <= n; i++) { cost[i, 0] = cost[i - 1, 0] + MissCost(expected[i - 1].Asset.TypeName); prev[i, 0] = 1; }
        for (var j = 1; j <= m; j++) { cost[0, j] = cost[0, j - 1] + SkipCost(observed[j - 1]); prev[0, j] = 2; }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var best = cost[i - 1, j] + MissCost(expected[i - 1].Asset.TypeName);
                byte dir = 1;
                var skip = cost[i, j - 1] + SkipCost(observed[j - 1]);
                if (skip < best) { best = skip; dir = 2; }
                if (expected[i - 1].Asset.TypeName.Equals(observed[j - 1].TypeName, StringComparison.OrdinalIgnoreCase))
                {
                    var match = cost[i - 1, j - 1];
                    if (match <= best) { best = match; dir = 3; }
                }
                cost[i, j] = best;
                prev[i, j] = dir;
            }
        }

        var pairs = new List<(int, int)>();
        var x = n; var y = m;
        while (x > 0 || y > 0)
        {
            var d = prev[x, y];
            if (d == 3) { pairs.Add((x - 1, y - 1)); x--; y--; }
            else if (d == 1) x--;
            else if (d == 2) y--;
            else break;
        }
        pairs.Reverse();
        return pairs;
    }

    private static int MissCost(string type) => type.ToLowerInvariant() switch
    {
        "weapon" => 30,
        "xanim" => 18,
        "fx" => 12,
        "material" => 11,
        "xmodel" => 9,
        _ => 10
    };

    private static int SkipCost(Observed o) => o.TypeName.ToLowerInvariant() switch
    {
        "weapon" => 30,
        "xanim" => 18,
        "fx" => 16,
        "material" => 3,
        "xmodel" => 2,
        _ => 10
    };

    private static IReadOnlyList<T4ExpandedTargetRun> BuildTargetRuns(
        IReadOnlyList<Expected> expected,
        IReadOnlyList<Observed> observed,
        IReadOnlyDictionary<int, int> map,
        string targetType,
        IReadOnlyList<int> missingExpected,
        int payloadStart,
        int zoneLength)
    {
        var groups = new Dictionary<(int Prev, int Next), List<int>>();
        foreach (var i in missingExpected)
        {
            var prev = -1;
            var next = expected.Count;
            for (var p = i - 1; p >= 0; p--)
            {
                if (map.ContainsKey(p) && !expected[p].Asset.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase))
                { prev = p; break; }
            }
            for (var n = i + 1; n < expected.Count; n++)
            {
                if (map.ContainsKey(n) && !expected[n].Asset.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase))
                { next = n; break; }
            }
            groups.TryAdd((prev, next), new List<int>());
            groups[(prev, next)].Add(i);
        }

        var runs = new List<T4ExpandedTargetRun>();
        foreach (var (key, missing) in groups.OrderBy(g => g.Value.Min()))
        {
            var prevExpected = key.Prev;
            var nextExpected = key.Next;
            var first = prevExpected + 1;
            var last = nextExpected - 1;
            if (first > last) { first = missing.Min(); last = missing.Max(); }

            var expectedTargets = Enumerable.Range(first, last - first + 1)
                .Where(k => expected[k].Asset.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var start = prevExpected >= 0 ? observed[map[prevExpected]].End : payloadStart;
            var end = nextExpected < expected.Count ? observed[map[nextExpected]].Start : zoneLength;
            start = Math.Clamp(start, payloadStart, zoneLength);
            end = Math.Clamp(end, payloadStart, zoneLength);

            var observedTargets = observed.Count(o =>
                o.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase) &&
                o.Start >= Math.Min(start, end) && o.Start < Math.Max(start, end));
            var deficit = Math.Max(0, expectedTargets.Count - observedTargets);

            var firstAssetIndex = expected[Math.Max(0, first)].Asset.Index;
            var lastAssetIndex = expected[Math.Min(expected.Count - 1, last)].Asset.Index;
            // Context below is the expanded filtered sequence; the renderer also
            // prints the original full XAsset table around the run.
            var context = string.Join(" | ", Enumerable.Range(Math.Max(0, first), Math.Min(expected.Count - 1, last) - Math.Max(0, first) + 1)
                .Select(k => $"#{expected[k].Asset.Index}:{expected[k].Asset.TypeName}/{ShortRef(expected[k].Asset.ReferenceKind)}"));

            runs.Add(new T4ExpandedTargetRun(
                firstAssetIndex,
                lastAssetIndex,
                targetType,
                expectedTargets.Count,
                observedTargets,
                Math.Max(deficit, missing.Count),
                Math.Min(start, end),
                Math.Max(start, end),
                prevExpected >= 0 ? expected[prevExpected].Asset.Index : null,
                prevExpected >= 0 ? expected[prevExpected].Asset.TypeName : null,
                prevExpected >= 0 ? observed[map[prevExpected]].End : null,
                nextExpected < expected.Count ? expected[nextExpected].Asset.Index : null,
                nextExpected < expected.Count ? expected[nextExpected].Asset.TypeName : null,
                nextExpected < expected.Count ? observed[map[nextExpected]].Start : null,
                context));
        }
        return runs;
    }

    private static string Render(
        T4ExpandedAnchorProbeResult result,
        IReadOnlyList<Expected> expected,
        IReadOnlyList<Observed> observed,
        IReadOnlyDictionary<int, int> map,
        IReadOnlySet<int> matchedObserved,
        T4ZoneDocument document)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 9i expanded asset-anchor forensics");
        sb.AppendLine("====================================================================");
        sb.AppendLine("DIAGNOSTIC ONLY: no Material anchor or candidate in this report can make a proof PASS.");
        sb.AppendLine($"Source: {document.SourceFastFile}");
        sb.AppendLine($"Target: {result.TargetType}");
        sb.AppendLine($"Expanded expected physical assets: {result.ExpandedExpectedAssets:N0}");
        sb.AppendLine($"Expanded observed candidates: {result.ExpandedObservedCandidates:N0}");
        sb.AppendLine($"Measured Material candidates: {result.MaterialAnchorCandidates:N0}; matched as order anchors: {result.MatchedMaterialAnchors:N0}");
        sb.AppendLine($"Unresolved target slots after expanded alignment: {result.MissingTargetAssets:N0}");
        sb.AppendLine();

        sb.AppendLine("TARGET DEFICIT RUNS (bounded by matched NON-target anchors)");
        if (result.TargetRuns.Count == 0) sb.AppendLine("  <none>");
        foreach (var run in result.TargetRuns)
        {
            sb.AppendLine($"  XAsset #{run.FirstAssetIndex}..#{run.LastAssetIndex}: expected {run.ExpectedTargetCount:N0} {run.TargetType}, observed {run.ObservedTargetCount:N0}, deficit={run.Deficit:N0}");
            sb.AppendLine($"    physical run: 0x{run.WindowStart:X8}-0x{run.WindowEnd:X8} ({Math.Max(0, run.WindowEnd-run.WindowStart):N0} bytes)");
            sb.AppendLine($"    prev anchor: #{Fmt(run.PreviousAnchorAssetIndex)} {run.PreviousAnchorType ?? "-"} end={Hex(run.PreviousAnchorEnd)}");
            sb.AppendLine($"    next anchor: #{Fmt(run.NextAnchorAssetIndex)} {run.NextAnchorType ?? "-"} start={Hex(run.NextAnchorStart)}");
            if (run.WindowStart == run.WindowEnd)
                sb.AppendLine("    NOTE: no physical room remains between expanded anchors; do not invent an inline target body here.");
            sb.AppendLine($"    filtered context: {run.TableContext}");

            var lo = Math.Max(0, run.FirstAssetIndex - 5);
            var hi = Math.Min(document.Assets.Count - 1, run.LastAssetIndex + 5);
            sb.AppendLine("    full XAsset table context:");
            foreach (var asset in document.Assets.Skip(lo).Take(hi - lo + 1))
                sb.AppendLine($"      #{asset.Index,-5} {asset.TypeName,-18} ref={ShortRef(asset.ReferenceKind),-4} raw=0x{asset.RawHeader:X8}");

            sb.AppendLine("    observed candidates in physical run:");
            foreach (var o in observed.Where(o => o.Start >= run.WindowStart && o.Start < run.WindowEnd).Take(120))
            {
                var oi = IndexOfReference(observed, o);
                var mark = oi >= 0 && matchedObserved.Contains(oi) ? "MATCH" : "skip ";
                sb.AppendLine($"      {mark} 0x{o.Start:X8}-0x{o.End:X8} {o.TypeName,-9} {o.Source,-26} '{o.Name}'");
            }
            sb.AppendLine();
        }

        sb.AppendLine("MATCHED MATERIAL ANCHORS (first 160)");
        var shown = 0;
        foreach (var pair in map.OrderBy(p => p.Key))
        {
            var e = expected[pair.Key].Asset;
            var o = observed[pair.Value];
            if (!e.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase)) continue;
            sb.AppendLine($"  XAsset #{e.Index,-5} -> 0x{o.Start:X8}-0x{o.End:X8} '{o.Name}'");
            if (++shown >= 160) break;
        }
        if (shown == 0) sb.AppendLine("  <none>");
        sb.AppendLine();

        sb.AppendLine("Interpretation");
        sb.AppendLine("  Alpha 9i adds measured Material bodies only as forensic landmarks; the proven XAnim/XModel serializers remain unchanged and fail closed.");
        sb.AppendLine("  A deficit run is more reliable than naming one missing member of a repeated same-type sequence: it asks how many target bodies are absent between two different, matched asset types.");
        sb.AppendLine("  If a former 'missing target gap' is fully consumed by a matched Material, the prior gap assignment was an alignment artifact rather than evidence of an alternate target header at that byte offset.");
        return sb.ToString();
    }

    private static int IndexOfReference(IReadOnlyList<Observed> list, Observed item)
    {
        for (var i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], item) || list[i] == item) return i;
        return -1;
    }

    private static IEnumerable<Observed> ReadXAnimProof(string path)
    {
        var data = File.ReadAllBytes(path);
        var magic = Encoding.ASCII.GetBytes("W360A4X\0");
        if (data.Length < 16 || !data.AsSpan(0, magic.Length).SequenceEqual(magic)) yield break;
        var p = magic.Length;
        _ = ReadBE(data, ref p);
        var count = checked((int)ReadBE(data, ref p));
        for (var i = 0; i < count; i++)
        {
            if (p > data.Length - 8) yield break;
            var start = checked((int)ReadBE(data, ref p));
            var len = checked((int)ReadBE(data, ref p));
            if (len < 0 || p > data.Length - len) yield break;
            p += len;
            yield return new Observed("xanim", start, checked(start + len), $"<xanim@0x{start:X}>", "alpha4-xanim-proof");
        }
    }

    private static IEnumerable<Observed> ReadWeaponOwnedProof(string path)
    {
        var data = File.ReadAllBytes(path);
        var magic = Encoding.ASCII.GetBytes("W360A7W\0");
        if (data.Length < 20 || !data.AsSpan(0, magic.Length).SequenceEqual(magic)) yield break;
        var p = magic.Length;
        _ = ReadBE(data, ref p);
        var count = checked((int)ReadBE(data, ref p));
        _ = ReadBE(data, ref p);
        for (var i = 0; i < count; i++)
        {
            if (p > data.Length - 12) yield break;
            var start = checked((int)ReadBE(data, ref p));
            var len = checked((int)ReadBE(data, ref p));
            var nameLen = checked((int)ReadBE(data, ref p));
            if (nameLen < 0 || p > data.Length - nameLen) yield break;
            var name = Encoding.UTF8.GetString(data, p, nameLen); p += nameLen;
            if (len < 0 || p > data.Length - len) yield break;
            p += len;
            yield return new Observed("weapon", start, checked(start + len), name, "alpha7b-weapon-proof");
        }
    }

    private static uint ReadBE(byte[] data, ref int p)
    {
        var v = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p, 4));
        p += 4;
        return v;
    }

    private static int TypeOrder(string type) => type.ToLowerInvariant() switch
    {
        "xanim" => 0,
        "xmodel" => 1,
        "weapon" => 2,
        "fx" => 3,
        "material" => 4,
        _ => 9
    };

    private static string ShortRef(T4AssetReferenceKind k) => k switch
    {
        T4AssetReferenceKind.InlineShared => "-1",
        T4AssetReferenceKind.Inline => "-2",
        T4AssetReferenceKind.OffsetOrAlias => "ref",
        _ => "null"
    };

    private static string Fmt(int? value) => value?.ToString() ?? "-";
    private static string Hex(int? value) => value.HasValue ? $"0x{value.Value:X8}" : "-";
}
