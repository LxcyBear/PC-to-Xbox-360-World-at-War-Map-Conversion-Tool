using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4MixedObservedAsset(string TypeName, int Start, int End, string Name, string Source);
public sealed record T4MixedMatch(int AssetIndex, string TypeName, int? ObservedStart, int? ObservedEnd, string? ObservedName, bool Missing);
public sealed record T4MixedMissingFxWindow(int AssetIndex, int? WindowStart, int? WindowEnd, int? PreviousAssetIndex, string? PreviousType, int? PreviousEnd, int? NextAssetIndex, string? NextType, int? NextStart, string TableContext);
public sealed record T4AssetGapWindow(
    int ExpectedIndex,
    int AssetIndex,
    string TypeName,
    int Start,
    int End,
    int? PreviousAssetIndex,
    string? PreviousType,
    int? PreviousStart,
    int? PreviousEnd,
    int? NextAssetIndex,
    string? NextType,
    int? NextStart,
    int? NextEnd,
    string TableContext);

public sealed record T4AlignedXModelMapping(
    int AssetIndex,
    int Offset,
    int End,
    string ObservedName,
    string Source);

public sealed record T4AlignedXModelSelection(
    IReadOnlyList<int> Offsets,
    int ExpectedXModel,
    int ObservedXModel,
    int MatchedXModel,
    int MissingXModel,
    int SkippedXModel,
    int PackedNameCandidates,
    int MatchedPackedName,
    int RadiusTolerantCandidates,
    int MatchedRadiusTolerant,
    bool UsedXAnimProof,
    bool UsedWeaponProof,
    IReadOnlyList<T4AssetGapWindow> MissingWindows)
{
    public IReadOnlyList<T4AlignedXModelMapping> Mappings { get; init; } =
        Array.Empty<T4AlignedXModelMapping>();
}

public sealed record T4AlignedXAnimSelection(
    IReadOnlyList<int> Offsets,
    int ExpectedXAnim,
    int ObservedXAnim,
    int MatchedXAnim,
    int MissingXAnim,
    int SkippedXAnim,
    int PackedNameCandidates,
    int MatchedPackedName,
    bool UsedWeaponProof,
    IReadOnlyList<T4AssetGapWindow> MissingWindows);

public sealed class T4MixedAssetOrderProbeResult
{
    public required string SourceFastFile { get; init; }
    public required int ExpectedAssets { get; init; }
    public required int ObservedCandidates { get; init; }
    public required IReadOnlyDictionary<string, int> ExpectedByType { get; init; }
    public required IReadOnlyDictionary<string, int> ObservedByType { get; init; }
    public required IReadOnlyDictionary<string, int> MatchedByType { get; init; }
    public required IReadOnlyDictionary<string, int> MissingByType { get; init; }
    public required IReadOnlyDictionary<string, int> SkippedByType { get; init; }
    public required IReadOnlyList<T4MixedMatch> Matches { get; init; }
    public required IReadOnlyList<T4MixedMissingFxWindow> MissingFxWindows { get; init; }
    public required string ReportFile { get; init; }
}

/// <summary>
/// Stage 4 alpha 9a diagnostic: align the authoritative XAsset order against a
/// mixed physical candidate stream (XAnim + XModel + Weapon + FX).  This is a
/// locator, not an acceptance rule.  It exists specifically to stop treating
/// the physical bytes between two FX chains as though they contained only FX.
/// </summary>
public static class T4MixedAssetOrderProbe
{
    private static readonly HashSet<string> Tracked = new(StringComparer.OrdinalIgnoreCase)
    {
        "xanim", "xmodel", "weapon", "fx"
    };

    // Alpha 9h: these three whole-zone typed scans are by far the most expensive
    // part of mixed-order alignment. They are immutable for a content hash, so
    // XAnim/XModel selection and the later standalone Mixed probe share them.
    private static IReadOnlyList<T4XAnimDiagnosticCandidate> CachedXAnimCandidates(byte[] zone, T4ZoneDocument document) =>
        FastFileAnalysisCache.GetOrCreateDerived(
            document.SourceFastFile, "mixed:xanim-diagnostic:a9g",
            () => T4XAnimSerializerProof.DiscoverDiagnosticCandidates(zone, Endian.Little).ToList());

    private static IReadOnlyList<T4XModelDiagnosticCandidate> CachedXModelCandidates(byte[] zone, T4ZoneDocument document) =>
        FastFileAnalysisCache.GetOrCreateDerived(
            document.SourceFastFile, "mixed:xmodel-diagnostic:a9g",
            () => T4XModelSerializerProof.DiscoverDiagnosticCandidates(zone, Endian.Little, document.Header).ToList());

    private static IReadOnlyList<T4FxDiagnosticCandidate> CachedFxCandidates(
        byte[] zone, T4ZoneDocument document, int payloadStart, CancellationToken cancellationToken) =>
        FastFileAnalysisCache.GetOrCreateDerived(
            document.SourceFastFile, $"mixed:fx-diagnostic:a9b:{payloadStart}",
            () => T4FxSerializerProof.DiscoverDiagnosticCandidates(zone, document.Header, payloadStart, cancellationToken).ToList());

    private sealed record Expected(T4AssetEntry Asset);

    public static async Task<T4MixedAssetOrderProbeResult> RunAsync(
        string sourceFastFile,
        string? xanimProofFile,
        string? weaponOwnedProofFile,
        string reportFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFastFile)) throw new FileNotFoundException("Fastfile not found.", sourceFastFile);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "mixed_a9a", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "zone.bin");
        try
        {
            var cachedZone = await FastFileAnalysisCache.GetZoneAsync(sourceFastFile, cancellationToken);
            if (cachedZone.Platform != WaWPlatform.PC || cachedZone.ZoneHeader.Endianness != Endian.Little)
                throw new InvalidDataException("Alpha 9a mixed-order probe expects a PC/little-endian WaW source fastfile.");

            var zone = cachedZone.Bytes;
            var document = cachedZone.Document;
            var payloadStart = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));

            var expected = document.Assets
                .Where(a => Tracked.Contains(a.TypeName) && a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                .Select(a => new Expected(a))
                .ToList();

            var observed = new List<T4MixedObservedAsset>();
            if (!string.IsNullOrWhiteSpace(xanimProofFile) && File.Exists(xanimProofFile))
                observed.AddRange(ReadXAnimProof(xanimProofFile));
            else
                foreach (var a in CachedXAnimCandidates(zone, document))
                    observed.Add(new("xanim", a.Offset, a.End, a.Name, "typed-xanim-scan"));
            foreach (var a in CachedXModelCandidates(zone, document))
                observed.Add(new("xmodel", a.Offset, a.End, a.Name, "typed-xmodel-candidate"));
            foreach (var a in CachedFxCandidates(zone, document, payloadStart, cancellationToken))
                observed.Add(new("fx", a.Offset, a.End, a.Name, "typed-fx"));

            if (!string.IsNullOrWhiteSpace(weaponOwnedProofFile) && File.Exists(weaponOwnedProofFile))
                observed.AddRange(ReadWeaponOwnedProof(weaponOwnedProofFile));

            observed = observed
                .Where(o => o.Start >= payloadStart && o.End > o.Start && o.End <= zone.Length)
                .OrderBy(o => o.Start)
                .ThenBy(o => TypeOrder(o.TypeName))
                .ThenByDescending(o => o.End)
                .ToList();

            var alignment = Align(expected, observed);
            var matchedObs = new bool[observed.Count];
            var matches = new List<T4MixedMatch>(expected.Count);
            var map = new Dictionary<int, int>();
            foreach (var pair in alignment)
            {
                map[pair.ExpectedIndex] = pair.ObservedIndex;
                matchedObs[pair.ObservedIndex] = true;
            }

            for (var i = 0; i < expected.Count; i++)
            {
                var e = expected[i].Asset;
                if (map.TryGetValue(i, out var oi))
                {
                    var o = observed[oi];
                    matches.Add(new(e.Index, e.TypeName, o.Start, o.End, o.Name, false));
                }
                else matches.Add(new(e.Index, e.TypeName, null, null, null, true));
            }

            var windows = new List<T4MixedMissingFxWindow>();
            for (var i = 0; i < expected.Count; i++)
            {
                if (!expected[i].Asset.TypeName.Equals("fx", StringComparison.OrdinalIgnoreCase) || map.ContainsKey(i)) continue;

                int? prevI = null, nextI = null;
                for (var p = i - 1; p >= 0; p--) if (map.ContainsKey(p)) { prevI = p; break; }
                for (var n = i + 1; n < expected.Count; n++) if (map.ContainsKey(n)) { nextI = n; break; }

                var prevObs = prevI.HasValue ? observed[map[prevI.Value]] : null;
                var nextObs = nextI.HasValue ? observed[map[nextI.Value]] : null;
                int? ws = prevObs?.End ?? payloadStart;
                int? we = nextObs?.Start ?? zone.Length;
                if (ws.HasValue && we.HasValue && ws.Value > we.Value) { ws = null; we = null; }

                var ai = expected[i].Asset.Index;
                var lo = Math.Max(0, ai - 3);
                var hi = Math.Min(document.Assets.Count - 1, ai + 3);
                var context = string.Join(" | ", document.Assets.Skip(lo).Take(hi - lo + 1)
                    .Select(a => $"#{a.Index}:{a.TypeName}:{ShortRef(a.ReferenceKind)}"));

                windows.Add(new T4MixedMissingFxWindow(
                    ai, ws, we,
                    prevI.HasValue ? expected[prevI.Value].Asset.Index : null,
                    prevI.HasValue ? expected[prevI.Value].Asset.TypeName : null,
                    prevObs?.End,
                    nextI.HasValue ? expected[nextI.Value].Asset.Index : null,
                    nextI.HasValue ? expected[nextI.Value].Asset.TypeName : null,
                    nextObs?.Start,
                    context));
            }

            var expectedBy = CountExpected(expected);
            var observedBy = CountObserved(observed);
            var matchedBy = matches.Where(m => !m.Missing).GroupBy(m => m.TypeName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var missingBy = matches.Where(m => m.Missing).GroupBy(m => m.TypeName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var skippedBy = observed.Select((o, i) => (o, i)).Where(x => !matchedObs[x.i]).GroupBy(x => x.o.TypeName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var result = new T4MixedAssetOrderProbeResult
            {
                SourceFastFile = sourceFastFile,
                ExpectedAssets = expected.Count,
                ObservedCandidates = observed.Count,
                ExpectedByType = expectedBy,
                ObservedByType = observedBy,
                MatchedByType = matchedBy,
                MissingByType = missingBy,
                SkippedByType = skippedBy,
                Matches = matches,
                MissingFxWindows = windows,
                ReportFile = reportFile
            };

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile))!);
            await File.WriteAllTextAsync(reportFile, Render(result, observed, matchedObs), Encoding.UTF8, cancellationToken);
            return result;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Stage 4 alpha 9g bridge: use mixed XAsset order to select authoritative
    /// top-level XModel candidates. If the inline-name pass leaves holes, scan
    /// only those bounded physical windows for the valid T4 form where the
    /// model name is a packed/offset zone pointer rather than FOLLOWING/INSERT.
    /// Existing matches must remain stable or the bridge is rejected.
    /// </summary>
    public static T4AlignedXModelSelection SelectAlignedXModels(
        byte[] zone,
        T4ZoneDocument document,
        string? xanimProofFile,
        string? weaponOwnedProofFile,
        CancellationToken cancellationToken = default)
    {
        var payloadStart = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));
        var expected = document.Assets
            .Where(a => Tracked.Contains(a.TypeName) && a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .Select(a => new Expected(a))
            .ToList();

        var observed = new List<T4MixedObservedAsset>();
        var usedXAnimProof = !string.IsNullOrWhiteSpace(xanimProofFile) && File.Exists(xanimProofFile);
        if (usedXAnimProof)
            observed.AddRange(ReadXAnimProof(xanimProofFile!));
        else
            foreach (var a in CachedXAnimCandidates(zone, document))
                observed.Add(new("xanim", a.Offset, a.End, a.Name, "typed-xanim-scan"));

        foreach (var a in CachedXModelCandidates(zone, document))
            observed.Add(new("xmodel", a.Offset, a.End, a.Name, "typed-xmodel-candidate"));

        foreach (var a in CachedFxCandidates(zone, document, payloadStart, cancellationToken))
            observed.Add(new("fx", a.Offset, a.End, a.Name, "typed-fx"));

        var usedWeaponProof = !string.IsNullOrWhiteSpace(weaponOwnedProofFile) && File.Exists(weaponOwnedProofFile);
        if (usedWeaponProof)
            observed.AddRange(ReadWeaponOwnedProof(weaponOwnedProofFile!));

        observed = NormalizeObserved(observed, payloadStart, zone.Length);
        var baseObserved = observed.ToList();
        var initialAlignment = Align(expected, observed);
        var initialTargetMap = TargetMap(expected, observed, initialAlignment, "xmodel");
        var expectedXModel = expected.Count(e => e.Asset.TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase));
        var initialMissing = Math.Max(0, expectedXModel - initialTargetMap.Count);
        var packedNameCandidates = 0;
        var radiusTolerantCandidates = 0;
        var alignment = initialAlignment;

        if (initialMissing > 0)
        {
            // Preserve the proven alpha 9g packed-name bridge exactly: use the
            // old slot-bounded windows and require every already matched XModel
            // to stay attached to the same expected slot. This keeps Tower's
            // noisy packed-name region fail-closed.
            var packedWindows = MissingWindows(expected, observed, initialAlignment, "xmodel", payloadStart, zone.Length);
            var existingStarts = observed.Where(o => o.TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase)).Select(o => o.Start).ToHashSet();
            foreach (var window in packedWindows)
            {
                foreach (var a in T4XModelSerializerProof.DiscoverPackedNameCandidates(
                             zone, Endian.Little, document.Header, window.Start, window.End, cancellationToken))
                {
                    if (!existingStarts.Add(a.Offset)) continue;
                    observed.Add(new("xmodel", a.Offset, a.End, a.Name, "typed-xmodel-packed-name"));
                    packedNameCandidates++;
                }
            }

            if (packedNameCandidates > 0)
            {
                observed = NormalizeObserved(observed, payloadStart, zone.Length);
                var augmented = Align(expected, observed);
                var augmentedTargetMap = TargetMap(expected, observed, augmented, "xmodel");
                if (PreservesTargetMatches(initialTargetMap, augmentedTargetMap))
                {
                    alignment = augmented;
                }
                else
                {
                    observed = baseObserved.ToList();
                    alignment = initialAlignment;
                    packedNameCandidates = 0;
                }
            }

            // Alpha 9j second bridge: the 9i microscope proved that Backrooms'
            // remaining models are ordinary inline-name XModels rejected solely
            // by the old finite-range sanity heuristic at +0xA8. Recover that
            // form only when an authoritative same-type run has an exact deficit
            // and the number of fully measured exception candidates equals that
            // deficit. Ambiguous runs remain failures.
            var beforeRadiusMap = TargetMap(expected, observed, alignment, "xmodel");
            var remaining = Math.Max(0, expectedXModel - beforeRadiusMap.Count);
            if (remaining > 0)
            {
                var beforeRadiusObserved = observed.ToList();
                existingStarts = observed.Where(o => o.TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase)).Select(o => o.Start).ToHashSet();
                foreach (var window in TargetRunWindows(expected, observed, alignment, "xmodel", payloadStart, zone.Length))
                {
                    var candidates = T4XModelSerializerProof.DiscoverFiniteRadiusExceptionCandidates(
                            zone, Endian.Little, document.Header, window.Start, window.End, cancellationToken)
                        .Where(a => !existingStarts.Contains(a.Offset))
                        .GroupBy(a => a.Offset)
                        .Select(g => g.First())
                        .OrderBy(a => a.Offset)
                        .ToList();

                    if (candidates.Count != window.Deficit) continue; // ambiguity => fail closed
                    foreach (var a in candidates)
                    {
                        if (!existingStarts.Add(a.Offset)) continue;
                        observed.Add(new("xmodel", a.Offset, a.End, a.Name, "typed-xmodel-finite-radius-exception"));
                        radiusTolerantCandidates++;
                    }
                }

                if (radiusTolerantCandidates > 0)
                {
                    observed = NormalizeObserved(observed, payloadStart, zone.Length);
                    var augmented = Align(expected, observed);
                    var augmentedTargetMap = TargetMap(expected, observed, augmented, "xmodel");
                    if (PreservesTargetBodies(beforeRadiusMap, augmentedTargetMap) &&
                        augmentedTargetMap.Count == beforeRadiusMap.Count + radiusTolerantCandidates)
                    {
                        alignment = augmented;
                    }
                    else
                    {
                        observed = beforeRadiusObserved;
                        alignment = Align(expected, observed);
                        radiusTolerantCandidates = 0;
                    }
                }
            }
        }

        var matchedObserved = new bool[observed.Count];
        foreach (var pair in alignment) matchedObserved[pair.ObservedIndex] = true;

        var xmodelPairs = alignment
            .Where(pair => expected[pair.ExpectedIndex].Asset.TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase)
                           && observed[pair.ObservedIndex].TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var xmodelOffsets = xmodelPairs.Select(pair => observed[pair.ObservedIndex].Start).Distinct().OrderBy(x => x).ToList();
        var observedXModel = observed.Count(o => o.TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase));
        var matchedXModel = xmodelOffsets.Count;
        var skippedXModel = observed.Select((o, i) => (o, i))
            .Count(x => x.o.TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase) && !matchedObserved[x.i]);
        var matchedPacked = xmodelPairs.Count(pair => observed[pair.ObservedIndex].Source.Equals("typed-xmodel-packed-name", StringComparison.OrdinalIgnoreCase));
        var matchedRadiusTolerant = xmodelPairs.Count(pair => observed[pair.ObservedIndex].Source.Equals("typed-xmodel-finite-radius-exception", StringComparison.OrdinalIgnoreCase));
        var missingWindows = DetailedMissingWindows(expected, observed, alignment, "xmodel", payloadStart, zone.Length);

        var mappings = xmodelPairs
            .Select(pair => new T4AlignedXModelMapping(
                expected[pair.ExpectedIndex].Asset.Index,
                observed[pair.ObservedIndex].Start,
                observed[pair.ObservedIndex].End,
                observed[pair.ObservedIndex].Name,
                observed[pair.ObservedIndex].Source))
            .OrderBy(x => x.AssetIndex)
            .ToList();

        return new T4AlignedXModelSelection(
            xmodelOffsets,
            expectedXModel,
            observedXModel,
            matchedXModel,
            Math.Max(0, expectedXModel - matchedXModel),
            skippedXModel,
            packedNameCandidates,
            matchedPacked,
            radiusTolerantCandidates,
            matchedRadiusTolerant,
            usedXAnimProof,
            usedWeaponProof,
            missingWindows)
        {
            Mappings = mappings
        };
    }

    /// <summary>
    /// Alpha 9g counterpart for XAnim. Alpha 4's original typed scan required
    /// the top-level name pointer itself to be inline, even though T4 permits a
    /// reused string through a packed zone pointer. We only search for that form
    /// inside windows for authoritative XAnim slots that the inline pass missed.
    /// </summary>
    public static T4AlignedXAnimSelection SelectAlignedXAnims(
        byte[] zone,
        T4ZoneDocument document,
        string? weaponOwnedProofFile,
        CancellationToken cancellationToken = default)
    {
        var payloadStart = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));
        var expected = document.Assets
            .Where(a => Tracked.Contains(a.TypeName) && a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .Select(a => new Expected(a))
            .ToList();

        var observed = new List<T4MixedObservedAsset>();
        foreach (var a in CachedXAnimCandidates(zone, document))
            observed.Add(new("xanim", a.Offset, a.End, a.Name, "typed-xanim-scan"));
        foreach (var a in CachedXModelCandidates(zone, document))
            observed.Add(new("xmodel", a.Offset, a.End, a.Name, "typed-xmodel-candidate"));
        foreach (var a in CachedFxCandidates(zone, document, payloadStart, cancellationToken))
            observed.Add(new("fx", a.Offset, a.End, a.Name, "typed-fx"));

        var usedWeaponProof = !string.IsNullOrWhiteSpace(weaponOwnedProofFile) && File.Exists(weaponOwnedProofFile);
        if (usedWeaponProof)
            observed.AddRange(ReadWeaponOwnedProof(weaponOwnedProofFile!));

        observed = NormalizeObserved(observed, payloadStart, zone.Length);
        var baseObserved = observed.ToList();
        var initialAlignment = Align(expected, observed);
        var initialTargetMap = TargetMap(expected, observed, initialAlignment, "xanim");
        var expectedXAnim = expected.Count(e => e.Asset.TypeName.Equals("xanim", StringComparison.OrdinalIgnoreCase));
        var initialMissing = Math.Max(0, expectedXAnim - initialTargetMap.Count);
        var packedNameCandidates = 0;
        var alignment = initialAlignment;

        if (initialMissing > 0)
        {
            // Alpha 9j: alpha 9i showed three independent weapon-animation runs
            // where one structurally complete XAnim sits between idle and reload
            // with a packed/offset top-level name. The old missing-slot window was
            // ambiguous inside the repeated XAnim sequence. Recover only when the
            // whole same-type run between matched non-target anchors contains
            // exactly as many strict packed-name bodies as its authoritative deficit.
            var acceptedXAnims = observed.Where(o => o.TypeName.Equals("xanim", StringComparison.OrdinalIgnoreCase)).ToList();
            var existingStarts = acceptedXAnims.Select(o => o.Start).ToHashSet();
            var acceptedStarts = acceptedXAnims.Select(o => o.Start).ToHashSet();
            var acceptedEnds = acceptedXAnims.Select(o => o.End).ToHashSet();
            foreach (var window in TargetRunWindows(expected, observed, initialAlignment, "xanim", payloadStart, zone.Length))
            {
                var candidates = T4XAnimSerializerProof.DiscoverPackedNameCandidates(
                        zone, Endian.Little, document.Header, window.Start, window.End, cancellationToken)
                    .Where(a => !existingStarts.Contains(a.Offset))
                    // The 9i evidence is stronger than merely being in the run: all
                    // three missing bodies exactly fill a serialized gap between two
                    // already accepted XAnims (idle -> packed body -> reload).
                    .Where(a => acceptedEnds.Contains(a.Offset) && acceptedStarts.Contains(a.End))
                    .GroupBy(a => a.Offset)
                    .Select(g => g.First())
                    .OrderBy(a => a.Offset)
                    .ToList();

                if (candidates.Count != window.Deficit) continue; // ambiguity => fail closed
                foreach (var a in candidates)
                {
                    if (!existingStarts.Add(a.Offset)) continue;
                    observed.Add(new("xanim", a.Offset, a.End, a.Name, "typed-xanim-packed-name"));
                    packedNameCandidates++;
                }
            }

            if (packedNameCandidates > 0)
            {
                observed = NormalizeObserved(observed, payloadStart, zone.Length);
                var augmented = Align(expected, observed);
                var augmentedTargetMap = TargetMap(expected, observed, augmented, "xanim");
                if (PreservesTargetBodies(initialTargetMap, augmentedTargetMap) &&
                    augmentedTargetMap.Count == initialTargetMap.Count + packedNameCandidates)
                {
                    alignment = augmented;
                }
                else
                {
                    observed = baseObserved;
                    alignment = initialAlignment;
                    packedNameCandidates = 0;
                }
            }
        }

        var matchedObserved = new bool[observed.Count];
        foreach (var pair in alignment) matchedObserved[pair.ObservedIndex] = true;

        var xanimPairs = alignment
            .Where(pair => expected[pair.ExpectedIndex].Asset.TypeName.Equals("xanim", StringComparison.OrdinalIgnoreCase)
                           && observed[pair.ObservedIndex].TypeName.Equals("xanim", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var offsets = xanimPairs.Select(pair => observed[pair.ObservedIndex].Start).Distinct().OrderBy(x => x).ToList();
        var observedXAnim = observed.Count(o => o.TypeName.Equals("xanim", StringComparison.OrdinalIgnoreCase));
        var matchedXAnim = offsets.Count;
        var skippedXAnim = observed.Select((o, i) => (o, i))
            .Count(x => x.o.TypeName.Equals("xanim", StringComparison.OrdinalIgnoreCase) && !matchedObserved[x.i]);
        var matchedPacked = xanimPairs.Count(pair => observed[pair.ObservedIndex].Source.Equals("typed-xanim-packed-name", StringComparison.OrdinalIgnoreCase));
        var missingWindows = DetailedMissingWindows(expected, observed, alignment, "xanim", payloadStart, zone.Length);

        return new T4AlignedXAnimSelection(
            offsets,
            expectedXAnim,
            observedXAnim,
            matchedXAnim,
            Math.Max(0, expectedXAnim - matchedXAnim),
            skippedXAnim,
            packedNameCandidates,
            matchedPacked,
            usedWeaponProof,
            missingWindows);
    }

    private static List<T4MixedObservedAsset> NormalizeObserved(IEnumerable<T4MixedObservedAsset> observed, int payloadStart, int zoneLength) =>
        observed
            .Where(o => o.Start >= payloadStart && o.End > o.Start && o.End <= zoneLength)
            .OrderBy(o => o.Start)
            .ThenBy(o => TypeOrder(o.TypeName))
            .ThenByDescending(o => o.End)
            .ToList();

    private static Dictionary<int, int> TargetMap(
        IReadOnlyList<Expected> expected,
        IReadOnlyList<T4MixedObservedAsset> observed,
        IEnumerable<(int ExpectedIndex, int ObservedIndex)> alignment,
        string targetType)
    {
        var map = new Dictionary<int, int>();
        foreach (var pair in alignment)
        {
            if (expected[pair.ExpectedIndex].Asset.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase)
                && observed[pair.ObservedIndex].TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase))
                map[pair.ExpectedIndex] = observed[pair.ObservedIndex].Start;
        }
        return map;
    }

    private static bool PreservesTargetMatches(IReadOnlyDictionary<int, int> before, IReadOnlyDictionary<int, int> after)
    {
        foreach (var pair in before)
            if (!after.TryGetValue(pair.Key, out var start) || start != pair.Value)
                return false;
        return true;
    }

    private sealed record RecoveryRunWindow(int Start, int End, int Deficit);

    /// <summary>
    /// Alpha 9j recovery windows. Dynamic-programming alignment cannot uniquely
    /// label the missing member of a repeated same-type run, so recovery is
    /// bounded by nearest matched NON-target anchors and uses only the run-level
    /// count deficit. No candidate is trusted merely because of position.
    /// </summary>
    private static IReadOnlyList<RecoveryRunWindow> TargetRunWindows(
        IReadOnlyList<Expected> expected,
        IReadOnlyList<T4MixedObservedAsset> observed,
        IReadOnlyList<(int ExpectedIndex, int ObservedIndex)> alignment,
        string targetType,
        int payloadStart,
        int zoneLength)
    {
        var map = alignment.ToDictionary(x => x.ExpectedIndex, x => x.ObservedIndex);
        var windows = new List<RecoveryRunWindow>();
        var i = 0;
        while (i < expected.Count)
        {
            if (!expected[i].Asset.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase)) { i++; continue; }
            var first = i;
            while (i + 1 < expected.Count && expected[i + 1].Asset.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase)) i++;
            var last = i;
            var expectedCount = last - first + 1;
            var matchedCount = Enumerable.Range(first, expectedCount).Count(map.ContainsKey);
            var deficit = expectedCount - matchedCount;
            if (deficit > 0)
            {
                int? prev = null, next = null;
                for (var p = first - 1; p >= 0; p--)
                {
                    if (expected[p].Asset.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase)) continue;
                    if (map.ContainsKey(p)) { prev = p; break; }
                }
                for (var n = last + 1; n < expected.Count; n++)
                {
                    if (expected[n].Asset.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase)) continue;
                    if (map.ContainsKey(n)) { next = n; break; }
                }
                var start = prev.HasValue ? observed[map[prev.Value]].End : payloadStart;
                var end = next.HasValue ? observed[map[next.Value]].Start : zoneLength;
                if (start < end) windows.Add(new RecoveryRunWindow(start, end, deficit));
            }
            i++;
        }
        return windows;
    }

    private static bool PreservesTargetBodies(IReadOnlyDictionary<int, int> before, IReadOnlyDictionary<int, int> after)
    {
        if (after.Count < before.Count) return false;
        var afterStarts = after.Values.ToHashSet();
        return before.Values.All(afterStarts.Contains);
    }

    private static IReadOnlyList<T4AssetGapWindow> DetailedMissingWindows(
        IReadOnlyList<Expected> expected,
        IReadOnlyList<T4MixedObservedAsset> observed,
        IReadOnlyList<(int ExpectedIndex, int ObservedIndex)> alignment,
        string targetType,
        int payloadStart,
        int zoneLength)
    {
        var map = alignment.ToDictionary(x => x.ExpectedIndex, x => x.ObservedIndex);
        var windows = new List<T4AssetGapWindow>();
        for (var i = 0; i < expected.Count; i++)
        {
            if (!expected[i].Asset.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase) || map.ContainsKey(i)) continue;

            int? prevI = null, nextI = null;
            for (var p = i - 1; p >= 0; p--) if (map.ContainsKey(p)) { prevI = p; break; }
            for (var n = i + 1; n < expected.Count; n++) if (map.ContainsKey(n)) { nextI = n; break; }

            var start = prevI.HasValue ? observed[map[prevI.Value]].End : payloadStart;
            var end = nextI.HasValue ? observed[map[nextI.Value]].Start : zoneLength;
            if (start >= end) continue;

            T4MixedObservedAsset? prev = prevI.HasValue ? observed[map[prevI.Value]] : null;
            T4MixedObservedAsset? next = nextI.HasValue ? observed[map[nextI.Value]] : null;
            var lo = Math.Max(0, i - 3);
            var hi = Math.Min(expected.Count - 1, i + 3);
            var context = string.Join(" | ", Enumerable.Range(lo, hi - lo + 1).Select(k =>
                $"#{expected[k].Asset.Index}:{expected[k].Asset.TypeName}/{ShortRef(expected[k].Asset.ReferenceKind)}{(k == i ? "<MISSING>" : string.Empty)}"));

            windows.Add(new T4AssetGapWindow(
                i, expected[i].Asset.Index, expected[i].Asset.TypeName, start, end,
                prevI.HasValue ? expected[prevI.Value].Asset.Index : null, prev?.TypeName, prev?.Start, prev?.End,
                nextI.HasValue ? expected[nextI.Value].Asset.Index : null, next?.TypeName, next?.Start, next?.End,
                context));
        }
        return windows;
    }

    private static List<(int Start, int End)> MissingWindows(
        IReadOnlyList<Expected> expected,
        IReadOnlyList<T4MixedObservedAsset> observed,
        IReadOnlyList<(int ExpectedIndex, int ObservedIndex)> alignment,
        string targetType,
        int payloadStart,
        int zoneLength)
    {
        var map = alignment.ToDictionary(x => x.ExpectedIndex, x => x.ObservedIndex);
        var windows = new List<(int Start, int End)>();
        for (var i = 0; i < expected.Count; i++)
        {
            if (!expected[i].Asset.TypeName.Equals(targetType, StringComparison.OrdinalIgnoreCase) || map.ContainsKey(i)) continue;

            int? prevI = null, nextI = null;
            for (var p = i - 1; p >= 0; p--) if (map.ContainsKey(p)) { prevI = p; break; }
            for (var n = i + 1; n < expected.Count; n++) if (map.ContainsKey(n)) { nextI = n; break; }

            var start = prevI.HasValue ? observed[map[prevI.Value]].End : payloadStart;
            var end = nextI.HasValue ? observed[map[nextI.Value]].Start : zoneLength;
            if (start < end) windows.Add((start, end));
        }

        if (windows.Count <= 1) return windows;
        windows.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(int Start, int End)> { windows[0] };
        for (var i = 1; i < windows.Count; i++)
        {
            var last = merged[^1];
            var next = windows[i];
            if (next.Start <= last.End)
                merged[^1] = (last.Start, Math.Max(last.End, next.End));
            else
                merged.Add(next);
        }
        return merged;
    }

    private static List<(int ExpectedIndex, int ObservedIndex)> Align(IReadOnlyList<Expected> expected, IReadOnlyList<T4MixedObservedAsset> observed)
    {
        var n = expected.Count; var m = observed.Count;
        const int Inf = 1_000_000_000;
        var cost = new int[n + 1, m + 1];
        var prev = new byte[n + 1, m + 1]; // 1=miss expected, 2=skip observed, 3=match
        for (var i = 0; i <= n; i++) for (var j = 0; j <= m; j++) cost[i, j] = Inf;
        cost[0, 0] = 0;
        for (var i = 1; i <= n; i++) { cost[i, 0] = cost[i - 1, 0] + MissCost(expected[i - 1].Asset.TypeName); prev[i, 0] = 1; }
        for (var j = 1; j <= m; j++) { cost[0, j] = cost[0, j - 1] + SkipCost(observed[j - 1]); prev[0, j] = 2; }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var best = cost[i - 1, j] + MissCost(expected[i - 1].Asset.TypeName); byte dir = 1;
                var skip = cost[i, j - 1] + SkipCost(observed[j - 1]);
                if (skip < best) { best = skip; dir = 2; }
                if (expected[i - 1].Asset.TypeName.Equals(observed[j - 1].TypeName, StringComparison.OrdinalIgnoreCase))
                {
                    var match = cost[i - 1, j - 1];
                    if (match <= best) { best = match; dir = 3; }
                }
                cost[i, j] = best; prev[i, j] = dir;
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
        "xmodel" => 9,
        _ => 10
    };

    private static int SkipCost(T4MixedObservedAsset o) => o.TypeName.ToLowerInvariant() switch
    {
        "weapon" => 30,
        "xanim" => 18,
        "fx" => 16,
        "xmodel" => 2,
        _ => 10
    };

    private static IReadOnlyDictionary<string, int> CountExpected(IEnumerable<Expected> xs) =>
        xs.GroupBy(x => x.Asset.TypeName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyDictionary<string, int> CountObserved(IEnumerable<T4MixedObservedAsset> xs) =>
        xs.GroupBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<T4MixedObservedAsset> ReadXAnimProof(string path)
    {
        var data = File.ReadAllBytes(path);
        var magic = Encoding.ASCII.GetBytes("W360A4X\0");
        if (data.Length < 16 || !data.AsSpan(0, magic.Length).SequenceEqual(magic)) yield break;
        var p = magic.Length;
        _ = ReadBE(data, ref p); // version
        var count = checked((int)ReadBE(data, ref p));
        for (var i = 0; i < count; i++)
        {
            if (p > data.Length - 8) yield break;
            var start = checked((int)ReadBE(data, ref p));
            var len = checked((int)ReadBE(data, ref p));
            if (len < 0 || p > data.Length - len) yield break;
            p += len;
            yield return new T4MixedObservedAsset("xanim", start, checked(start + len), $"<xanim@0x{start:X}>", "alpha4-xanim-proof");
        }
    }

    private static IEnumerable<T4MixedObservedAsset> ReadWeaponOwnedProof(string path)
    {
        var data = File.ReadAllBytes(path);
        var magic = Encoding.ASCII.GetBytes("W360A7W\0");
        if (data.Length < 20 || !data.AsSpan(0, magic.Length).SequenceEqual(magic)) yield break;
        var p = magic.Length;
        _ = ReadBE(data, ref p); // version
        var count = checked((int)ReadBE(data, ref p));
        _ = ReadBE(data, ref p); // fixed size
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
            yield return new T4MixedObservedAsset("weapon", start, checked(start + len), name, "alpha7b-weapon-proof");
        }
    }

    private static uint ReadBE(byte[] data, ref int p)
    {
        var v = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p, 4)); p += 4; return v;
    }

    private static int TypeOrder(string type) => type.ToLowerInvariant() switch { "xanim" => 0, "xmodel" => 1, "weapon" => 2, "fx" => 3, _ => 9 };
    private static string ShortRef(T4AssetReferenceKind k) => k switch { T4AssetReferenceKind.InlineShared => "-1", T4AssetReferenceKind.Inline => "-2", T4AssetReferenceKind.OffsetOrAlias => "ref", _ => "null" };

    private static int Get(IReadOnlyDictionary<string, int> d, string k) => d.TryGetValue(k, out var n) ? n : 0;

    private static string Render(T4MixedAssetOrderProbeResult r, IReadOnlyList<T4MixedObservedAsset> observed, bool[] matchedObs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 9a mixed XAsset-order resolver");
        sb.AppendLine("==============================================================");
        sb.AppendLine("DIAGNOSTIC ONLY: this report does not accept new assets or emit a game fastfile.");
        sb.AppendLine($"Source: {r.SourceFastFile}");
        sb.AppendLine($"Tracked physical XAssets: {r.ExpectedAssets:N0}");
        sb.AppendLine($"Observed typed candidates: {r.ObservedCandidates:N0}");
        sb.AppendLine();
        sb.AppendLine("TYPE MATRIX");
        foreach (var t in new[] { "xanim", "xmodel", "weapon", "fx" })
            sb.AppendLine($"  {t,-7} expected={Get(r.ExpectedByType,t),5:N0} observed={Get(r.ObservedByType,t),5:N0} matched={Get(r.MatchedByType,t),5:N0} missing={Get(r.MissingByType,t),5:N0} skipped={Get(r.SkippedByType,t),5:N0}");
        sb.AppendLine();
        sb.AppendLine("MISSING FX WINDOWS (bounded by mixed-type matched anchors)");
        if (r.MissingFxWindows.Count == 0) sb.AppendLine("  <none>");
        foreach (var w in r.MissingFxWindows)
        {
            var range = w.WindowStart.HasValue && w.WindowEnd.HasValue ? $"0x{w.WindowStart:X8}-0x{w.WindowEnd:X8} ({w.WindowEnd.Value-w.WindowStart.Value:N0} bytes)" : "<unbounded/overlap>";
            sb.AppendLine($"  XAsset #{w.AssetIndex} fx -> {range}");
            sb.AppendLine($"    prev: #{w.PreviousAssetIndex?.ToString() ?? "-"} {w.PreviousType ?? "-"} end={(w.PreviousEnd.HasValue ? $"0x{w.PreviousEnd:X8}" : "-")}");
            sb.AppendLine($"    next: #{w.NextAssetIndex?.ToString() ?? "-"} {w.NextType ?? "-"} start={(w.NextStart.HasValue ? $"0x{w.NextStart:X8}" : "-")}");
            sb.AppendLine($"    table: {w.TableContext}");
        }
        sb.AppendLine();
        sb.AppendLine("SKIPPED OBSERVED CANDIDATES (first 160; expected false positives are mostly XModel)");
        var shown = 0;
        for (var i = 0; i < observed.Count && shown < 160; i++)
        {
            if (matchedObs[i]) continue;
            var o = observed[i];
            sb.AppendLine($"  0x{o.Start:X8}-0x{o.End:X8} {o.TypeName} {o.Source} '{o.Name}'");
            shown++;
        }
        sb.AppendLine();
        sb.AppendLine("Interpretation");
        sb.AppendLine("  Alpha 9a aligns a mixed physical candidate stream to the authoritative filtered XAsset order.");
        sb.AppendLine("  XModel candidates are intentionally cheap to skip because Alpha 5 currently over-detects them; Weapon/XAnim/FX candidates are expensive to discard.");
        sb.AppendLine("  Missing FX windows are therefore bounded by whichever trusted mixed-type assets actually surround that table slot, instead of by unrelated FX chains hundreds of KB away.");
        return sb.ToString();
    }
}
