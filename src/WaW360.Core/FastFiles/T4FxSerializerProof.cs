using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4FxProofAsset(
    string Name,
    long SourceOffset,
    int SourceSerializedBytes,
    int XboxSerializedBytes,
    int DeclaredTotalSize,
    int FxOwnedBytes,
    int InlineDependencyBytes,
    int ElemDefs,
    int VelocitySamples,
    int VisualSamples,
    int InlineStrings,
    int MaterialVisuals,
    int ModelVisuals,
    int SoundVisuals,
    int RunnerVisuals,
    int DecalMarkArrays,
    int EffectRefNames,
    int TrailDefs,
    int TrailVertices,
    int TrailIndices,
    int InlineMaterials,
    int InlineXModels);

public sealed record T4FxDiagnosticCandidate(string Name, int Offset, int End, int DeclaredSize);

public sealed class T4FxProofResult
{
    public required string SourceFastFile { get; init; }
    public required string OutputFile { get; init; }
    public required int DeclaredFxCount { get; init; }
    public required int CoveredFxCount { get; init; }
    public required int SerializedBodyCount { get; init; }
    public required int AliasReferenceCount { get; init; }
    public required IReadOnlyList<T4FxProofAsset> Assets { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool Verified { get; init; }
}

/// <summary>
/// Stage 4 alpha 9b: typed T4 FxEffectDef owned-payload serializer proof with strict zero-total reference recovery.
///
/// T4 layout used here:
///   FxEffectDef = 36 bytes
///   FxElemDef   = 256 bytes
/// followed by inline effect name, the complete FxElemDef array, velocity and
/// visual-state samples, conditional visual dependencies, FxEffectDefRef names,
/// and optional FxTrailDef payloads.
///
/// Material/XModel visual dependencies are routed through the already-proven
/// Alpha 5 nested walkers. FxEffectDefRef and runner visual references are names
/// (asset references), not recursively embedded FxEffectDef bodies.
///
/// This emits an INTERNAL proof container only. It is not an Xbox fastfile.
/// </summary>
public static class T4FxSerializerProof
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("W360A9B\0");
    private const uint Following = 0xFFFFFFFFu;
    private const uint Insert = 0xFFFFFFFEu;
    private const int FxEffectDefSize = 36;
    private const int FxElemDefSize = 256;
    private const int FxVelSampleSize = 96;
    private const int FxVisSampleSize = 48;
    private const int FxTrailDefSize = 28;
    private const int FxTrailVertexSize = 20;
    private const int MaxElemDefs = 4096;

    private sealed class TraverseStats
    {
        // Bytes owned by FxEffectDef itself. This deliberately excludes inline
        // Material/XModel asset bodies, which are separate XAssets even when
        // serialized immediately inside the physical stream.
        public int FxOwnedBytes;
        public int ElemDefs;
        public int VelocitySamples;
        public int VisualSamples;
        public int InlineStrings;
        public int MaterialVisuals;
        public int ModelVisuals;
        public int SoundVisuals;
        public int RunnerVisuals;
        public int DecalMarkArrays;
        public int EffectRefNames;
        public int TrailDefs;
        public int TrailVertices;
        public int TrailIndices;
        public int InlineMaterials;
        public int InlineXModels;
    }

    private sealed class WalkContext
    {
        public required byte[] Source { get; init; }
        public required Endian Endianness { get; init; }
        public ZoneHeaderSummary? Header { get; init; }
        public Stream? Output { get; init; }
        public required TraverseStats Stats { get; init; }
        public required string EffectName { get; init; }

        // Alpha 8z diagnostic context. These fields describe the FxElemDef whose
        // owned visual dependency is currently being traversed. They do not alter
        // serialization; they make a cursor/order failure actionable.
        public int CurrentElemIndex { get; set; } = -1;
        public int CurrentElemStart { get; set; } = -1;
        public byte CurrentElemType { get; set; }
        public int CurrentVisualCount { get; set; }
        public int CurrentVelIntervalCount { get; set; }
        public int CurrentVisIntervalCount { get; set; }
        public uint CurrentVisualPointer { get; set; }
    }

    private sealed record Candidate(int Offset, int End, string? InlineName, int DeclaredSize, int FxOwnedBytes)
    {
        public bool HasInlineName => InlineName is not null;
    }

    private sealed class ScanDiagnostics
    {
        public long PositionsScanned;
        public int ValidNamePointer;
        public int InlineReadableNames;
        public int PackedNamePointers;
        public int PositiveTotalSize;
        public int NonNegativeLoopingLife;
        public int InfiniteLoopLifeHeaders;
        public int InfiniteLoopLifeTypedSuccess;
        public int SaneCountTriples;
        public int ZeroElementHeaders;
        public int PositiveElementHeaders;
        public int InlineElemDefs;
        public int PackedElemDefs;
        public int ZeroElementAccepted;
        public int ZeroTotalReferenceHeaders;
        public int ZeroTotalReferenceAccepted;
        public int ReusedElemDefsAccepted;
        public int NamedPackedElemDefsUnresolved;
        public int TypedWalkSuccess;
        public readonly Dictionary<string, int> RejectBuckets = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> NamedRejectBuckets = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> NamedNearMisses = new();
        public readonly HashSet<string> NamedReadableSet = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> NamedSuccessfulSet = new(StringComparer.OrdinalIgnoreCase);
        public int NamedTypedSuccess;
        public readonly List<string> FocusedNearMisses = new();
        public int DeepPositiveInlineHeaders;
        public int DeepNamedInlineHeaders;
        public int DeepTypedSuccess;
        public readonly Dictionary<string, int> DeepRejectBuckets = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> DeepNearMisses = new();
        public int PackedNameInlineElemDefsHeaders;
        public int PackedNameInlineElemDefsTypedSuccess;
        public int InlineNamePackedElemDefsHeaders;
        public int PackedNamePackedElemDefsHeaders;
        public int PackedNamePackedElemDefsMinSizeHeaders;
        public int StrongPackedFixedHeaders;
        public readonly List<Candidate> StrongPackedFixedCandidates = new();
        public readonly List<string> PackedBodyCensus = new();

        public void Reject(string bucket, int start, string? name, string? detail = null)
        {
            RejectBuckets[bucket] = RejectBuckets.TryGetValue(bucket, out var n) ? n + 1 : 1;
            if (name is not null)
            {
                NamedReadableSet.Add(name);
                NamedRejectBuckets[bucket] = NamedRejectBuckets.TryGetValue(bucket, out var namedN) ? namedN + 1 : 1;
                if (NamedNearMisses.Count < 512)
                {
                    var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" :: {detail}";
                    NamedNearMisses.Add($"0x{start:X} '{name}' => {bucket}{suffix}");
                }
            }
        }

        public void NamedSuccess(string name)
        {
            NamedReadableSet.Add(name);
            NamedSuccessfulSet.Add(name);
            NamedTypedSuccess++;
        }

        public void FocusedMiss(string bucket, int start, string label, string? detail = null)
        {
            if (FocusedNearMisses.Count >= 128) return;
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" :: {detail}";
            FocusedNearMisses.Add($"0x{start:X} '{label}' => {bucket}{suffix}");
        }

        public void DeepReject(string bucket, int start, string? name, string? detail = null)
        {
            DeepRejectBuckets[bucket] = DeepRejectBuckets.TryGetValue(bucket, out var n) ? n + 1 : 1;
            if (DeepNearMisses.Count >= 160) return;
            var label = name ?? "<packed-name>";
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" :: {detail}";
            DeepNearMisses.Add($"0x{start:X} '{label}' => {bucket}{suffix}");
        }

        public string Render(int requiredBodies, int acceptedBodies, int declaredAssets, int followingHeaders, int insertHeaders, int aliasHeaders)
        {
            var sb = new StringBuilder();
            sb.AppendLine("WaW360Converter — Stage 4 alpha 9b FX discovery diagnostics");
            sb.AppendLine("==========================================================");
            sb.AppendLine($"Declared FX XAssets:              {declaredAssets:N0}");
            sb.AppendLine($"Required physical bodies:         {requiredBodies:N0}");
            sb.AppendLine($"Accepted strong typed bodies:     {acceptedBodies:N0}");
            sb.AppendLine();
            sb.AppendLine("FX XASSET HEADER CENSUS");
            sb.AppendLine($"  following (-1 / FFFFFFFF):       {followingHeaders:N0}");
            sb.AppendLine($"  insert (-2 / FFFFFFFE):          {insertHeaders:N0}");
            sb.AppendLine($"  packed alias/offset headers:      {aliasHeaders:N0}");
            sb.AppendLine();
            sb.AppendLine("PACKED-BODY CENSUS");
            sb.AppendLine($"  packed-name + inline elemDefs:    {PackedNameInlineElemDefsHeaders:N0}");
            sb.AppendLine($"    full typed successes:           {PackedNameInlineElemDefsTypedSuccess:N0}");
            sb.AppendLine($"  inline-name + packed elemDefs:    {InlineNamePackedElemDefsHeaders:N0}");
            sb.AppendLine($"  packed-name + packed elemDefs:    {PackedNamePackedElemDefsHeaders:N0}");
            sb.AppendLine($"    totalSize >= fixed+elemDefs:    {PackedNamePackedElemDefsMinSizeHeaders:N0}");
            sb.AppendLine($"    strong VIRTUAL->TEMP fixed:     {StrongPackedFixedHeaders:N0}");
            foreach (var line in PackedBodyCensus.Take(96)) sb.AppendLine($"    {line}");
            sb.AppendLine();
            sb.AppendLine("SCAN PIPELINE");
            sb.AppendLine($"  byte positions scanned:          {PositionsScanned:N0}");
            sb.AppendLine($"  valid FX-name pointers:          {ValidNamePointer:N0}");
            sb.AppendLine($"  readable inline FX names:        {InlineReadableNames:N0}");
            sb.AppendLine($"  packed FX-name pointers:         {PackedNamePointers:N0}");
            sb.AppendLine($"  positive/bounded totalSize:      {PositiveTotalSize:N0}");
            sb.AppendLine($"  nonnegative looping life:        {NonNegativeLoopingLife:N0}");
            sb.AppendLine($"  msecLoopingLife == -1 headers:   {InfiniteLoopLifeHeaders:N0}");
            sb.AppendLine($"  -1 life full typed successes:    {InfiniteLoopLifeTypedSuccess:N0}");
            sb.AppendLine($"  sane element-count triples:      {SaneCountTriples:N0}");
            sb.AppendLine($"  zero-element headers:            {ZeroElementHeaders:N0}");
            sb.AppendLine($"  positive-element headers:        {PositiveElementHeaders:N0}");
            sb.AppendLine($"  inline elemDefs pointers:        {InlineElemDefs:N0}");
            sb.AppendLine($"  packed/reused elemDefs pointers: {PackedElemDefs:N0}");
            sb.AppendLine($"  accepted zero-element FX:        {ZeroElementAccepted:N0}");
            sb.AppendLine($"  strict zero-total refs found:     {ZeroTotalReferenceHeaders:N0}");
            sb.AppendLine($"  strict zero-total refs accepted:  {ZeroTotalReferenceAccepted:N0}");
            sb.AppendLine($"  accepted reused elemDefs FX:     {ReusedElemDefsAccepted:N0}");
            sb.AppendLine($"  named packed elemDefs unresolved:{NamedPackedElemDefsUnresolved,9:N0}");
            sb.AppendLine($"  full typed walk success:         {TypedWalkSuccess:N0}");
            sb.AppendLine();
            sb.AppendLine("READABLE INLINE-NAMED HEADER ACCOUNTING");
            sb.AppendLine($"  unique readable FX-like names:   {NamedReadableSet.Count:N0}");
            sb.AppendLine($"  named typed successes:           {NamedTypedSuccess:N0}");
            sb.AppendLine($"  unique successful names:         {NamedSuccessfulSet.Count:N0}");
            sb.AppendLine($"  named rejects:                   {NamedRejectBuckets.Values.Sum():N0}");
            foreach (var kv in NamedRejectBuckets.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
                sb.AppendLine($"    {kv.Key,-32} {kv.Value,8:N0}");
            sb.AppendLine();
            sb.AppendLine("DEEP POSITIVE-ELEMENT INLINE-elemDefs COHORT");
            sb.AppendLine($"  headers reaching deep cohort:    {DeepPositiveInlineHeaders:N0}");
            sb.AppendLine($"  with readable inline names:      {DeepNamedInlineHeaders:N0}");
            sb.AppendLine($"  full typed walk success:         {DeepTypedSuccess:N0}");
            sb.AppendLine($"  full typed walk failures:        {DeepRejectBuckets.Values.Sum():N0}");
            foreach (var kv in DeepRejectBuckets.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
                sb.AppendLine($"    {kv.Key,-32} {kv.Value,8:N0}");
            sb.AppendLine();
            sb.AppendLine("DEEP NEAR-MISSES (first 160)");
            if (DeepNearMisses.Count == 0) sb.AppendLine("  <none>");
            else foreach (var line in DeepNearMisses) sb.AppendLine("  " + line);
            sb.AppendLine();
            sb.AppendLine("REJECTION BUCKETS");
            foreach (var kv in RejectBuckets.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
                sb.AppendLine($"  {kv.Key,-34} {kv.Value,8:N0}");
            sb.AppendLine();
            sb.AppendLine("FOCUSED STRONG-HEADER NEAR-MISSES (first 128)");
            if (FocusedNearMisses.Count == 0) sb.AppendLine("  <none>");
            else foreach (var line in FocusedNearMisses) sb.AppendLine("  " + line);
            sb.AppendLine();
            sb.AppendLine("READABLE INLINE-NAMED REJECTIONS (first 512)");
            if (NamedNearMisses.Count == 0) sb.AppendLine("  <none>");
            else foreach (var line in NamedNearMisses) sb.AppendLine("  " + line);
            return sb.ToString();
        }
    }

    public static async Task<T4FxProofResult> BuildProofAsync(
        string sourceFastFile,
        string outputFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFastFile))
            throw new FileNotFoundException("Fastfile not found.", sourceFastFile);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "fx_a8z", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "zone.bin");

        try
        {
            var cachedZone = await FastFileAnalysisCache.GetZoneAsync(sourceFastFile, cancellationToken);
            if (cachedZone.Platform != WaWPlatform.PC || cachedZone.ZoneHeader.Endianness != Endian.Little)
                throw new InvalidDataException("Alpha 9b FX proof expects a PC/little-endian WaW source fastfile.");

            var zone = cachedZone.Bytes;
            var document = cachedZone.Document;

            var fxEntries = document.Assets.Where(a => a.TypeName.Equals("fx", StringComparison.OrdinalIgnoreCase)).ToList();
            var declared = fxEntries.Count;
            if (declared == 0)
                throw new InvalidDataException("Fastfile declares no FxEffectDef XAssets.");

            var followingHeaderCount = fxEntries.Count(a => a.ReferenceKind == T4AssetReferenceKind.InlineShared);
            var insertHeaderCount = fxEntries.Count(a => a.ReferenceKind == T4AssetReferenceKind.Inline);
            var inlineHeaderCount = followingHeaderCount + insertHeaderCount;
            var aliasEntries = fxEntries.Where(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias).ToList();
            var nullEntries = fxEntries.Where(a => a.ReferenceKind == T4AssetReferenceKind.Null).ToList();
            if (nullEntries.Count > 0)
                throw new InvalidDataException($"FX XAsset table contains {nullEntries.Count:N0} null header(s); Alpha 8 refuses to infer their ownership semantics.");
            if (inlineHeaderCount + aliasEntries.Count != declared)
                throw new InvalidDataException("FX XAsset header classification does not account for every declared asset.");

            // Alpha 8z selection proof:
            // FxEffectDef::totalSize is treated as a COMPILED HEADER INVARIANT, not as the
            // physical serialized-stream boundary. Stock T4 common.ff has 132 declared FX XAssets
            // and exactly 132 strong headers when we require: plausible name pointer/name, positive
            // bounded totalSize, coherent looping-life/count semantics, inline elemDefs ownership,
            // and a complete typed ownership traversal. Alpha 8z deliberately does not impose
            // a looping-count/msecLoopingLife relationship. Zero-element headers are diagnostic-only
            // because they proved too permissive for whole-zone scanning. Alpha 8z separately probes
            // msecLoopingLife==-1 through the full typed walker. Packed/reused elemDefs are
            // also diagnostic-only until their logical block target can be resolved and validated.
            //
            // Inline Material/XModel XAssets can make the physical stream longer than the effect's
            // own accounting, so Alpha 8z lets the typed walker determine the true end. It does not
            // rank or choose a subset: the complete strong-header + typed-walk set itself must equal
            // the XAsset-declared physical body count, and the resulting physical streams must not overlap.
            var payloadScanStart = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));
            var normalCandidates = FindCandidates(zone, document.Header, payloadScanStart, cancellationToken, out var scanDiagnostics, out var strictEmptyCandidates, out var zeroTotalReferenceCandidates, out var infiniteLoopCandidates);
            // Alpha 8z: probe the last major pre-walk gate separately. A signed
            // msecLoopingLife of -1 is treated as a candidate "infinite/indefinite"
            // representation, but never accepted merely because of that value. It must
            // pass the same positive-element + inline-elemDefs typed ownership walk as
            // the 159 proven normal bodies, remain physically non-overlapping with them,
            // and the unique non-overlapping set must exactly fill the XAsset deficit.
            var infiniteNonOverlapping = infiniteLoopCandidates
                .Where(e => !normalCandidates.Any(n => RangesOverlap(e.Offset, e.End, n.Offset, n.End)))
                .OrderBy(e => e.Offset)
                .ToList();

            var infiniteClusters = new List<List<Candidate>>();
            foreach (var candidate in infiniteNonOverlapping)
            {
                var cluster = infiniteClusters.LastOrDefault(c => c.Any(x => RangesOverlap(x.Offset, x.End, candidate.Offset, candidate.End)));
                if (cluster is null)
                {
                    cluster = new List<Candidate>();
                    infiniteClusters.Add(cluster);
                }
                cluster.Add(candidate);
            }
            var uniqueInfinite = infiniteClusters.Where(c => c.Count == 1).Select(c => c[0]).ToList();
            var ambiguousInfinite = infiniteClusters.Where(c => c.Count > 1).ToList();
            var fxTableRuns = BuildFxTableRuns(fxEntries);
            var typedChains = BuildCandidateChains(normalCandidates);
            var tableRunLengths = string.Join(",", fxTableRuns.Select(r => r.Length));
            var typedChainLengths = string.Join(",", typedChains.Select(c => c.Count));
            var tableRunDistribution = RenderLengthDistribution(fxTableRuns.Select(r => r.Length));
            var typedChainDistribution = RenderLengthDistribution(typedChains.Select(c => c.Count));
            var runAlignment = AlignFxRuns(fxTableRuns, typedChains);
            var runConsensus = BuildRunConsensus(runAlignment);
            var certainIncompleteRuns = runConsensus.Where(r => r.IncompleteAlignments == r.TotalAlignments && r.MinMissingBodies > 0).ToList();
            var possibleIncompleteRuns = runConsensus.Where(r => r.IncompleteAlignments > 0 && r.IncompleteAlignments < r.TotalAlignments).ToList();
            var certainMissingBodies = certainIncompleteRuns.Sum(r => r.MinMissingBodies);

            // Alpha 9b: the mixed-order probe proved that a large share of the missing FX are
            // serialized as a 0x24-byte FxEffectDef fixed body followed immediately by an
            // inline comma-prefixed reference name, with totalSize==0, zero element counts,
            // and a null elemDefs pointer.  These are not guessed from names alone: every
            // candidate must satisfy the full fixed-body pointer/count validation and typed
            // WalkEffect traversal.  Whole-zone false positives are controlled by requiring
            // the unique maximum non-overlapping set to participate in an exact-deficit proof.
            var zeroTotalNonOverlapping = zeroTotalReferenceCandidates
                .Where(e => !normalCandidates.Any(n => RangesOverlap(e.Offset, e.End, n.Offset, n.End)))
                .OrderBy(e => e.Offset)
                .ToList();
            var zeroTotalSelection = SelectUniqueMaximumNonOverlappingSet(zeroTotalNonOverlapping);

            // Alpha 8z packed-body audit. These are NOT accepted as FX bodies yet.
            // They are only candidates whose packed name resolves inside VIRTUAL, whose
            // packed elemDefs resolves inside TEMP, whose fixed-body padding is sane,
            // and whose runtime totalSize can cover the declared FxElemDef array.
            // Remove anything touching a fully proven typed stream, then collapse
            // overlapping starts to singleton clusters before looking for 0x24-byte chains.
            var packedFixedNonOverlapping = scanDiagnostics.StrongPackedFixedCandidates
                .Where(p => !normalCandidates.Any(n => RangesOverlap(p.Offset, p.End, n.Offset, n.End)))
                .OrderBy(p => p.Offset)
                .ToList();
            var packedFixedClusters = BuildOverlapClusters(packedFixedNonOverlapping);
            var uniquePackedFixed = packedFixedClusters.Where(c => c.Count == 1).Select(c => c[0]).OrderBy(c => c.Offset).ToList();
            var ambiguousPackedFixed = packedFixedClusters.Count(c => c.Count > 1);
            var packedFixedChains = BuildCandidateChains(uniquePackedFixed);
            var packedFixedChainDistribution = RenderLengthDistribution(packedFixedChains.Select(c => c.Count));

            var missingBodies = inlineHeaderCount - normalCandidates.Count;
            var candidates = new List<Candidate>(normalCandidates);
            var selectedZeroTotal = new List<Candidate>();
            var selectedInfiniteRecovery = new List<Candidate>();

            if (missingBodies > 0 && zeroTotalSelection.IsUnique && zeroTotalSelection.MaximumCount <= missingBodies)
            {
                var remainingAfterZero = missingBodies - zeroTotalSelection.MaximumCount;
                if (remainingAfterZero == 0 || uniqueInfinite.Count == remainingAfterZero)
                {
                    selectedZeroTotal.AddRange(zeroTotalSelection.Selected);
                    candidates.AddRange(selectedZeroTotal);
                    if (remainingAfterZero > 0)
                    {
                        selectedInfiniteRecovery.AddRange(uniqueInfinite);
                        candidates.AddRange(selectedInfiniteRecovery);
                    }
                }
            }
            else if (missingBodies > 0 && zeroTotalSelection.MaximumCount == 0 && uniqueInfinite.Count == missingBodies)
            {
                selectedInfiniteRecovery.AddRange(uniqueInfinite);
                candidates.AddRange(selectedInfiniteRecovery);
            }

            scanDiagnostics.ZeroTotalReferenceAccepted = selectedZeroTotal.Count;

            if (normalCandidates.Count == 0 && infiniteLoopCandidates.Count == 0 && zeroTotalReferenceCandidates.Count == 0)
                throw new InvalidDataException($"FX XAsset table requires {inlineHeaderCount:N0} physical body(ies), but Alpha 9b found neither a normal typed FxEffectDef body, a strict zero-total reference body, nor a fully traversed msecLoopingLife==-1 candidate.");

            if (candidates.Count != inlineHeaderCount)
            {
                var diagnosticPath = Path.ChangeExtension(outputFile, ".scan_diagnostics.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(diagnosticPath))!);
                var runDiagnostics = new StringBuilder();
                runDiagnostics.AppendLine(scanDiagnostics.Render(inlineHeaderCount, candidates.Count, declared, followingHeaderCount, insertHeaderCount, aliasEntries.Count));
                runDiagnostics.AppendLine();
                runDiagnostics.AppendLine("FX XASSET TABLE RUNS (authoritative order)");
                runDiagnostics.AppendLine($"  run count:                       {fxTableRuns.Count:N0}");
                runDiagnostics.AppendLine($"  run-length distribution:        {tableRunDistribution}");
                runDiagnostics.AppendLine($"  run lengths in table order:     {tableRunLengths}");
                foreach (var run in fxTableRuns)
                    runDiagnostics.AppendLine($"    assets #{run.FirstAssetIndex}-#{run.LastAssetIndex} len={run.Length}");
                runDiagnostics.AppendLine();
                runDiagnostics.AppendLine("PROVEN FX PHYSICAL ADJACENCY CHAINS");
                runDiagnostics.AppendLine($"  chain count:                     {typedChains.Count:N0}");
                runDiagnostics.AppendLine($"  chain-length distribution:      {typedChainDistribution}");
                runDiagnostics.AppendLine($"  chain lengths in stream order:  {typedChainLengths}");
                foreach (var chain in typedChains)
                    runDiagnostics.AppendLine($"    0x{chain.Start:X}-0x{chain.End:X} len={chain.Count}");
                runDiagnostics.AppendLine();
                runDiagnostics.AppendLine("ORDERED TABLE-RUN -> PHYSICAL-CHAIN ALIGNMENT");
                runDiagnostics.AppendLine($"  optimal alignments:              {runAlignment.OptimalAlignmentCount:N0}");
                runDiagnostics.AppendLine($"  skipped table runs:              {runAlignment.SkippedRuns:N0}");
                runDiagnostics.AppendLine($"  partial matched runs:            {runAlignment.PartialMatches:N0}");
                runDiagnostics.AppendLine($"  missing bodies explained:        {runAlignment.MissingBodies:N0}");
                runDiagnostics.AppendLine($"  unique best alignment:           {(runAlignment.OptimalAlignmentCount == 1 ? "yes" : "NO")}");
                foreach (var line in RenderRunAlignment(runAlignment, document.Assets))
                    runDiagnostics.AppendLine("  " + line);
                runDiagnostics.AppendLine();
                runDiagnostics.AppendLine("OPTIMAL-ALIGNMENT CONSENSUS");
                runDiagnostics.AppendLine($"  enumerated optimal alignments:     {runAlignment.OptimalRows.Count:N0} (DP count {runAlignment.OptimalAlignmentCount:N0})");
                runDiagnostics.AppendLine($"  certain incomplete runs:           {certainIncompleteRuns.Count:N0}");
                runDiagnostics.AppendLine($"  certain missing bodies (minimum):  {certainMissingBodies:N0}");
                runDiagnostics.AppendLine($"  alignment-dependent runs:          {possibleIncompleteRuns.Count:N0}");
                foreach (var row in runConsensus.Where(r => r.IncompleteAlignments > 0))
                {
                    var possible = string.Join(",", row.PossibleMissingBodies);
                    var certainty = row.IncompleteAlignments == row.TotalAlignments ? "CERTAIN" : "possible";
                    runDiagnostics.AppendLine($"    assets #{row.Run.FirstAssetIndex}-#{row.Run.LastAssetIndex} len={row.Run.Length}: {certainty} incomplete in {row.IncompleteAlignments}/{row.TotalAlignments}; missing {{{possible}}}");
                }
                runDiagnostics.AppendLine();
                runDiagnostics.AppendLine("Interpretation: Alpha 8z enumerates every optimal ordered run/chain alignment (up to the safety cap) instead of trusting one representative path. Runs marked CERTAIN are incomplete in every equally optimal alignment and are therefore localized independently of the repeated 1/2/6 run lengths. Alignment-dependent rows are not yet safe probe targets.");
                runDiagnostics.AppendLine();
                runDiagnostics.AppendLine("STRICT PACKED FIXED-BODY CENSUS (DIAGNOSTIC ONLY)");
                runDiagnostics.AppendLine($"  strong packed fixed headers:       {scanDiagnostics.StrongPackedFixedHeaders:N0}");
                runDiagnostics.AppendLine($"  after proven-stream exclusion:     {packedFixedNonOverlapping.Count:N0}");
                runDiagnostics.AppendLine($"  unique singleton starts:           {uniquePackedFixed.Count:N0}");
                runDiagnostics.AppendLine($"  ambiguous overlap clusters:        {ambiguousPackedFixed:N0}");
                runDiagnostics.AppendLine($"  0x24 adjacency chains:             {packedFixedChains.Count:N0} [{packedFixedChainDistribution}]");
                foreach (var chain in packedFixedChains.Where(c => c.Count >= 2).Take(96))
                    runDiagnostics.AppendLine($"    0x{chain.Start:X}-0x{chain.End:X} len={chain.Count}");
                runDiagnostics.AppendLine("  NOTE: these candidates are not accepted as physical FX until their packed block targets are resolved through a sequential zone/block map.");
                runDiagnostics.AppendLine();
                runDiagnostics.AppendLine("ALPHA 9B STRICT ZERO-TOTAL REFERENCE CENSUS");
                runDiagnostics.AppendLine($"  comma-prefixed totalSize=0 typed candidates: {zeroTotalReferenceCandidates.Count:N0}");
                runDiagnostics.AppendLine($"  non-overlapping maximum:                     {zeroTotalSelection.MaximumCount:N0}");
                runDiagnostics.AppendLine($"  maximum set unique:                         {zeroTotalSelection.IsUnique}");
                runDiagnostics.AppendLine($"  exact FX deficit before recovery:           {missingBodies:N0}");
                foreach (var z in zeroTotalReferenceCandidates.Take(128))
                    runDiagnostics.AppendLine($"    0x{z.Offset:X8}-0x{z.End:X8} ({z.End-z.Offset:N0} bytes) '{z.InlineName}'");
                runDiagnostics.AppendLine("  Acceptance rule: totalSize=0, comma-prefixed inline path, sane looping life, 0/0/0 element counts, elemDefs=null, exact fixed+name typed boundary, then unique non-overlap + exact-deficit proof.");
                runDiagnostics.AppendLine();
                runDiagnostics.AppendLine("  Material note: Alpha 8z restores the 112-byte PC source Material fixed body used by the cross-map-stable alpha 8v path (counts +91..+93; pointers +96..+108). Alpha 8w's 108-byte reinterpretation caused broad Material/trail regressions on probox, fortress, and nazi_zombie_ndh, so it is not used for source traversal.");

                await File.WriteAllTextAsync(
                    diagnosticPath,
                    runDiagnostics.ToString(),
                    Encoding.UTF8,
                    cancellationToken);

                // Alpha 8z: turn the authoritative XAsset ordering + proven physical FX
                // chains into bounded physical search windows around every CERTAIN incomplete
                // run.  This is deliberately a locator/audit, not a new acceptance heuristic:
                // it tells us which exact header gate dominates inside the window where the
                // missing bodies must live, while preserving fail-closed proof behavior.
                var locatorPath = Path.ChangeExtension(outputFile, ".xasset_order_locator.txt");
                var locatorReport = BuildXAssetOrderLocatorReport(
                    zone,
                    document.Header,
                    payloadScanStart,
                    document.Assets,
                    normalCandidates,
                    runAlignment,
                    certainIncompleteRuns,
                    cancellationToken);
                await File.WriteAllTextAsync(locatorPath, locatorReport, Encoding.UTF8, cancellationToken);

                var topBuckets = string.Join(", ", scanDiagnostics.RejectBuckets
                    .OrderByDescending(kv => kv.Value)
                    .Take(6)
                    .Select(kv => $"{kv.Key}={kv.Value:N0}"));
                var namedMissSource = scanDiagnostics.FocusedNearMisses.Count > 0
                    ? scanDiagnostics.FocusedNearMisses
                    : scanDiagnostics.NamedNearMisses;
                var namedMisses = string.Join(" | ", namedMissSource.Take(6));
                var unresolvedPacked = scanDiagnostics.NamedPackedElemDefsUnresolved;
                var namedBuckets = scanDiagnostics.NamedRejectBuckets.Count == 0
                    ? "<none>"
                    : string.Join(", ", scanDiagnostics.NamedRejectBuckets
                        .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(8)
                        .Select(kv => $"{kv.Key}={kv.Value:N0}"));
                var deepExamples = scanDiagnostics.DeepNearMisses.Count == 0
                    ? "<none>"
                    : string.Join(" | ", scanDiagnostics.DeepNearMisses.Take(3));
                var incompleteRunSummary = runAlignment.Rows.Count == 0
                    ? "<no alignment>"
                    : string.Join(" | ", runAlignment.Rows
                        .Where(r => r.MissingBodies > 0)
                        .Take(8)
                        .Select(r => r.Chain is null
                            ? $"assets#{r.Run.FirstAssetIndex}-{r.Run.LastAssetIndex} len={r.Run.Length}:MISSING"
                            : $"assets#{r.Run.FirstAssetIndex}-{r.Run.LastAssetIndex} len={r.Run.Length}->chain{r.Chain.Count} @0x{r.Chain.Start:X}-0x{r.Chain.End:X}"));
                var certainRunSummary = certainIncompleteRuns.Count == 0
                    ? "<none>"
                    : string.Join(" | ", certainIncompleteRuns.Take(8).Select(r =>
                        $"assets#{r.Run.FirstAssetIndex}-{r.Run.LastAssetIndex} len={r.Run.Length} missing={string.Join("/", r.PossibleMissingBodies)} ({r.IncompleteAlignments}/{r.TotalAlignments})"));
                throw new InvalidDataException(
                    $"FX XAsset table requires {inlineHeaderCount:N0} physical FxEffectDef body(ies). Alpha 9b has {normalCandidates.Count:N0} normal deep-typed body(ies), {zeroTotalReferenceCandidates.Count:N0} strict zero-total reference candidate(s) ({zeroTotalSelection.MaximumCount:N0} max non-overlap, unique={zeroTotalSelection.IsUnique}), and {infiniteLoopCandidates.Count:N0} fully traversed msecLoopingLife==-1 candidate(s); while the exact remaining deficit is {missingBodies:N0}. Combined accepted coverage is {candidates.Count:N0}. " +
                    $"-1 life candidates are accepted only when their unique non-overlapping set exactly fills the declared deficit. Ambiguous -1-life clusters: {ambiguousInfinite.Count:N0}. " +
                    $"Deep positive-element inline-elemDefs headers: {scanDiagnostics.DeepPositiveInlineHeaders:N0}; typed failures: {scanDiagnostics.DeepRejectBuckets.Values.Sum():N0}. " +
                    $"Deep failure buckets: {(scanDiagnostics.DeepRejectBuckets.Count == 0 ? "<none>" : string.Join(", ", scanDiagnostics.DeepRejectBuckets.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key}={kv.Value:N0}")))}. " +
                    $"Readable inline-name rejects by stage: {namedBuckets}. " +
                    $"Deep failure detail: {deepExamples}. " +
                    $"XAsset headers: following(-1)={followingHeaderCount:N0}, insert(-2)={insertHeaderCount:N0}, aliases={aliasEntries.Count:N0}. " +
                    $"FX table runs={fxTableRuns.Count:N0} [{tableRunDistribution}]; proven physical adjacency chains={typedChains.Count:N0} [{typedChainDistribution}]. Run lengths(table)={tableRunLengths}. Chain lengths(stream)={typedChainLengths}. " +
                    $"Ordered run alignment: best={(runAlignment.OptimalAlignmentCount == 1 ? "unique" : $"AMBIGUOUS x{runAlignment.OptimalAlignmentCount:N0}")}, skipped={runAlignment.SkippedRuns:N0}, partial={runAlignment.PartialMatches:N0}, missing={runAlignment.MissingBodies:N0}. Representative incomplete runs: {incompleteRunSummary}. " +
                    $"8s consensus: enumerated={runAlignment.OptimalRows.Count:N0}; CERTAIN incomplete runs={certainIncompleteRuns.Count:N0}; certain missing bodies(min)={certainMissingBodies:N0}; alignment-dependent runs={possibleIncompleteRuns.Count:N0}. Certain runs: {certainRunSummary}. " +
                    $"Packed census: packed-name+inline-elemDefs={scanDiagnostics.PackedNameInlineElemDefsHeaders:N0} (typed pass {scanDiagnostics.PackedNameInlineElemDefsTypedSuccess:N0}); inline-name+packed-elemDefs={scanDiagnostics.InlineNamePackedElemDefsHeaders:N0}; packed-name+packed-elemDefs={scanDiagnostics.PackedNamePackedElemDefsHeaders:N0} (min-size {scanDiagnostics.PackedNamePackedElemDefsMinSizeHeaders:N0}); STRICT VIRTUAL->TEMP fixed={scanDiagnostics.StrongPackedFixedHeaders:N0}, unique/non-overlap={uniquePackedFixed.Count:N0}, chains={packedFixedChains.Count:N0} [{packedFixedChainDistribution}]. " +
                    "Packed/reused elemDefs remain diagnostic-only; strict comma-prefixed totalSize==0 reference bodies are recoverable only through the exact-deficit proof. " +
                    $"Full diagnostics: {diagnosticPath}. XAsset-order locator: {locatorPath}");
            }

            var resolved = candidates.OrderBy(c => c.Offset).ToList();
            for (var i = 1; i < resolved.Count; i++)
            {
                if (resolved[i - 1].End > resolved[i].Offset)
                    throw new InvalidDataException(
                        $"Alpha 9b selected FX physical streams overlap: 0x{resolved[i - 1].Offset:X}-0x{resolved[i - 1].End:X} and 0x{resolved[i].Offset:X}-0x{resolved[i].End:X}. Refusing to guess.");
            }
            if (resolved.Count + aliasEntries.Count != declared)
                throw new InvalidDataException($"FX top-level coverage mismatch: {resolved.Count:N0} physical + {aliasEntries.Count:N0} aliases != {declared:N0}.");

            var selectedPacked = resolved.Where(c => !c.HasInlineName).ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
            var assets = new List<T4FxProofAsset>(resolved.Count);

            await using (var fs = File.Create(outputFile))
            {
                await fs.WriteAsync(Magic, cancellationToken);
                WriteU32(fs, 9);
                WriteU32(fs, checked((uint)resolved.Count));

                foreach (var candidate in resolved)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var label = candidate.InlineName ?? $"<packed-fx@0x{candidate.Offset:X}>";

                    using var blobStream = new MemoryStream();
                    var stats = new TraverseStats();
                    var ctx = new WalkContext
                    {
                        Source = zone,
                        Endianness = Endian.Little,
                        Header = document.Header,
                        Output = blobStream,
                        Stats = stats,
                        EffectName = label
                    };
                    var end = WalkEffect(ctx, candidate.Offset);
                    if (end != candidate.End)
                        throw new InvalidDataException($"FX '{label}' changed measured source boundary from 0x{candidate.End:X} to 0x{end:X}.");
                    if (stats.FxOwnedBytes != candidate.FxOwnedBytes)
                        throw new InvalidDataException($"FX '{label}' reconstructed FX-owned byte count changed from {candidate.FxOwnedBytes:N0} to {stats.FxOwnedBytes:N0}.");
                    var sourceTotalSize = ReadI32(zone, candidate.Offset + 8, Endian.Little);
                    if (sourceTotalSize != candidate.DeclaredSize)
                        throw new InvalidDataException($"FX '{label}' totalSize changed from {candidate.DeclaredSize:N0} to {sourceTotalSize:N0} during proof emission.");

                    var blob = blobStream.ToArray();
                    if (stats.FxOwnedBytes > blob.Length)
                        throw new InvalidDataException($"FX '{label}' reconstructed FX-owned bytes {stats.FxOwnedBytes:N0} exceed physical proof bytes {blob.Length:N0}.");
                    if (blob.Length != end - candidate.Offset)
                        throw new InvalidDataException($"FX '{label}' changed serialized byte length ({end - candidate.Offset:N0} -> {blob.Length:N0}).");

                    var verifyStats = new TraverseStats();
                    var verifyCtx = new WalkContext
                    {
                        Source = blob,
                        Endianness = Endian.Big,
                        Header = null,
                        Output = null,
                        Stats = verifyStats,
                        EffectName = label
                    };
                    var verifyEnd = WalkEffect(verifyCtx, 0);
                    if (verifyEnd != blob.Length)
                        throw new InvalidDataException($"FX '{label}' big-endian read-back ended at 0x{verifyEnd:X}, expected 0x{blob.Length:X}.");
                    var verifyTotalSize = ReadI32(blob, 8, Endian.Big);
                    if (verifyTotalSize != candidate.DeclaredSize)
                        throw new InvalidDataException($"FX '{label}' big-endian totalSize changed from {candidate.DeclaredSize:N0} to {verifyTotalSize:N0}.");
                    if (verifyStats.FxOwnedBytes != candidate.FxOwnedBytes)
                        throw new InvalidDataException($"FX '{label}' big-endian FX-owned byte count changed from {candidate.FxOwnedBytes:N0} to {verifyStats.FxOwnedBytes:N0}; physical proof length is {blob.Length:N0}.");
                    VerifyStats(label, stats, verifyStats);

                    var nameBytes = Encoding.UTF8.GetBytes(label);
                    WriteU32(fs, checked((uint)candidate.Offset));
                    WriteU32(fs, checked((uint)blob.Length));
                    WriteU32(fs, checked((uint)nameBytes.Length));
                    await fs.WriteAsync(nameBytes, cancellationToken);
                    await fs.WriteAsync(blob, cancellationToken);

                    assets.Add(new T4FxProofAsset(
                        label,
                        candidate.Offset,
                        end - candidate.Offset,
                        blob.Length,
                        candidate.DeclaredSize,
                        stats.FxOwnedBytes,
                        blob.Length - stats.FxOwnedBytes,
                        stats.ElemDefs,
                        stats.VelocitySamples,
                        stats.VisualSamples,
                        stats.InlineStrings,
                        stats.MaterialVisuals,
                        stats.ModelVisuals,
                        stats.SoundVisuals,
                        stats.RunnerVisuals,
                        stats.DecalMarkArrays,
                        stats.EffectRefNames,
                        stats.TrailDefs,
                        stats.TrailVertices,
                        stats.TrailIndices,
                        stats.InlineMaterials,
                        stats.InlineXModels));
                }
            }

            if (!VerifyContainer(outputFile, assets))
                throw new InvalidDataException("Stage 4 alpha 9b FX proof container failed read-back verification.");

            var warnings = new List<string>();
            var selectedInfiniteLife = resolved.Count(c => ReadI32(zone, c.Offset + 12, Endian.Little) == -1);
            if (selectedInfiniteLife > 0)
                warnings.Add($"Alpha 9b exact-deficit recovery: {selectedInfiniteLife:N0} FxEffectDef body(ies) use msecLoopingLife==-1 and were accepted only after full typed traversal plus unique/non-overlapping exact-deficit proof.");
            if (selectedZeroTotal.Count > 0)
                warnings.Add($"Alpha 9b zero-total reference recovery: {selectedZeroTotal.Count:N0} comma-prefixed name-only FxEffectDef body(ies) (totalSize=0, counts=0, elemDefs=null) were accepted only because their unique non-overlapping set participated in the exact XAsset deficit proof.");
            if (selectedPacked.Count > 0)
                warnings.Add($"{selectedPacked.Count:N0} physical FxEffectDef body(ies) use packed name pointers; proof labels are synthetic because Alpha 8z does not resolve block-local name aliases to strings.");
            warnings.Add($"Alpha 9b accepted exactly {resolved.Count:N0} strong FxEffectDef header(s) because every candidate passed the full typed ownership traversal and the set exactly matched the XAsset-declared body count; no subset selection was used.");
            var zeroElemFx = resolved.Count(c =>
                ReadI32(zone, c.Offset + 16, Endian.Little) + ReadI32(zone, c.Offset + 20, Endian.Little) + ReadI32(zone, c.Offset + 24, Endian.Little) == 0);
            var reusedElemFx = resolved.Count(c =>
            {
                var elemPtr = ReadU32(zone, c.Offset + 32, Endian.Little);
                var elemCount = ReadI32(zone, c.Offset + 16, Endian.Little) + ReadI32(zone, c.Offset + 20, Endian.Little) + ReadI32(zone, c.Offset + 24, Endian.Little);
                return elemCount > 0 && elemPtr != 0 && !IsInline(elemPtr);
            });
            if (zeroElemFx > 0)
                warnings.Add($"Alpha 9b ownership variants exercised: zero-element/null-elemDefs FX={zeroElemFx:N0}.");
            var nonStockLoopLife = resolved.Count(c =>
            {
                var looping = ReadI32(zone, c.Offset + 16, Endian.Little);
                var life = ReadI32(zone, c.Offset + 12, Endian.Little);
                return (looping == 0 && life != 0) || (looping > 0 && life == 0);
            });
            if (nonStockLoopLife > 0)
                warnings.Add($"{nonStockLoopLife:N0} FxEffectDef body(ies) use looping-count/msecLoopingLife combinations that Alpha 8e rejected as a stock-data heuristic. Alpha 9b accepts them because the T4 format does not require that relationship and their complete typed ownership walk still verifies.");
            var physicalEqualsTotal = assets.Count(a => a.XboxSerializedBytes == a.DeclaredTotalSize);
            var ownedEqualsTotal = assets.Count(a => a.FxOwnedBytes == a.DeclaredTotalSize);
            warnings.Add($"totalSize diagnostics: physical-stream length matches on {physicalEqualsTotal:N0}/{assets.Count:N0}; FX-owned byte accounting matches on {ownedEqualsTotal:N0}/{assets.Count:N0}. These are diagnostics only in Alpha 9b, not candidate-selection rules.");
            if (aliasEntries.Count > 0)
            {
                var refs = string.Join(", ", aliasEntries.Take(12).Select(a => $"#{a.Index}=0x{a.RawHeader:X8}"));
                if (aliasEntries.Count > 12) refs += $", ... +{aliasEntries.Count - 12:N0} more";
                warnings.Add($"{aliasEntries.Count:N0} top-level FX XAsset(s) are packed offset/alias references and require no fresh FxEffectDef body in this FF. Headers: {refs}");
            }

            return new T4FxProofResult
            {
                SourceFastFile = sourceFastFile,
                OutputFile = outputFile,
                DeclaredFxCount = declared,
                CoveredFxCount = assets.Count + aliasEntries.Count,
                SerializedBodyCount = assets.Count,
                AliasReferenceCount = aliasEntries.Count,
                Assets = assets,
                Warnings = warnings,
                Verified = true
            };
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public static IReadOnlyList<T4FxDiagnosticCandidate> DiscoverDiagnosticCandidates(
        byte[] zone,
        ZoneHeaderSummary header,
        int scanStart,
        CancellationToken cancellationToken = default)
    {
        var normal = FindCandidates(zone, header, scanStart, cancellationToken, out _, out _, out var zeroTotal, out var infinite);
        return normal.Concat(zeroTotal).Concat(infinite)
            .OrderBy(c => c.Offset)
            .Select(c => new T4FxDiagnosticCandidate(c.InlineName ?? $"<packed-fx@0x{c.Offset:X}>", c.Offset, c.End, c.DeclaredSize))
            .ToList();
    }

    private static List<Candidate> FindCandidates(
        byte[] zone,
        ZoneHeaderSummary header,
        int scanStart,
        CancellationToken cancellationToken,
        out ScanDiagnostics diagnostics,
        out List<Candidate> strictEmptyCandidates,
        out List<Candidate> zeroTotalReferenceCandidates,
        out List<Candidate> infiniteLoopCandidates)
    {
        diagnostics = new ScanDiagnostics();
        strictEmptyCandidates = new List<Candidate>();
        zeroTotalReferenceCandidates = new List<Candidate>();
        infiniteLoopCandidates = new List<Candidate>();
        var list = new List<Candidate>();
        for (var start = Math.Max(0, scanStart); start <= zone.Length - FxEffectDefSize; start++)
        {
            diagnostics.PositionsScanned++;
            if ((start & 0x3FFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            var namePtr = ReadU32(zone, start, Endian.Little);
            if (!IsStringPointerWithin(namePtr, header) || namePtr == 0)
            {
                diagnostics.Reject("name-pointer-invalid", start, null);
                continue;
            }
            diagnostics.ValidNamePointer++;

            string? inlineName = null;
            if (IsInline(namePtr))
            {
                if (!TryReadCString(zone, start + FxEffectDefSize, 256, out var n, out _) || !LooksLikeFxName(n))
                {
                    diagnostics.Reject("inline-name-invalid", start, null);
                    continue;
                }
                inlineName = n;
                diagnostics.InlineReadableNames++;
            }
            else
            {
                diagnostics.PackedNamePointers++;
            }
            var packedName = !IsInline(namePtr);

            var totalSize = ReadI32(zone, start + 8, Endian.Little);

            // Alpha 9b strict name-only reference form.  Cross-map mixed-order
            // windows proved that stock-style entries such as
            // ",weapon/muzzleflashes/pistolflash" occupy exactly fixed-body +
            // inline-name bytes and carry totalSize==0.  Do not treat arbitrary
            // totalSize==0 headers as FX: require the comma-prefixed path marker,
            // zero looping/one-shot/emission counts, a sane looping-life field, null
            // elemDefs, and a complete typed WalkEffect boundary equal to the
            // fixed body plus the one inline C string.
            if (totalSize == 0 && inlineName is not null && inlineName.StartsWith(",", StringComparison.Ordinal) && inlineName.Contains('/'))
            {
                var zLoopLife = ReadI32(zone, start + 12, Endian.Little);
                var zLooping = ReadI32(zone, start + 16, Endian.Little);
                var zOneShot = ReadI32(zone, start + 20, Endian.Little);
                var zEmission = ReadI32(zone, start + 24, Endian.Little);
                var zElemPtr = ReadU32(zone, start + 32, Endian.Little);
                if (zLoopLife >= -1 && zLoopLife <= 86_400_000 && zLooping == 0 && zOneShot == 0 && zEmission == 0 && zElemPtr == 0)
                {
                    try
                    {
                        var stats = new TraverseStats();
                        var ctx = new WalkContext
                        {
                            Source = zone,
                            Endianness = Endian.Little,
                            Header = header,
                            Output = null,
                            Stats = stats,
                            EffectName = inlineName
                        };
                        var end = WalkEffect(ctx, start);
                        if (end == start + FxEffectDefSize + Encoding.UTF8.GetByteCount(inlineName) + 1 && stats.ElemDefs == 0)
                        {
                            zeroTotalReferenceCandidates.Add(new Candidate(start, end, inlineName, totalSize, stats.FxOwnedBytes));
                            diagnostics.ZeroTotalReferenceHeaders++;
                            diagnostics.NamedSuccess(inlineName);
                            continue;
                        }
                    }
                    catch (Exception ex) when (ex is InvalidDataException or OverflowException)
                    {
                        diagnostics.Reject("zero-total-reference-walk-failed", start, inlineName, TrimDiagnostic(ex.Message));
                        continue;
                    }
                }
                diagnostics.Reject("zero-total-reference-shape-invalid", start, inlineName, $"life={zLoopLife}; counts={zLooping}/{zOneShot}/{zEmission}; ptr=0x{zElemPtr:X8}");
                continue;
            }

            if (totalSize < FxEffectDefSize || totalSize > zone.Length - start)
            {
                diagnostics.Reject("totalSize-invalid", start, inlineName, $"total=0x{totalSize:X}");
                continue;
            }
            diagnostics.PositiveTotalSize++;

            var loopingLife = ReadI32(zone, start + 12, Endian.Little);
            var infiniteLoopLife = loopingLife == -1;
            if (loopingLife < -1)
            {
                diagnostics.Reject("loopingLife-negative", start, inlineName, $"life={loopingLife}");
                continue;
            }
            if (infiniteLoopLife) diagnostics.InfiniteLoopLifeHeaders++;
            else diagnostics.NonNegativeLoopingLife++;

            var looping = ReadI32(zone, start + 16, Endian.Little);
            var oneShot = ReadI32(zone, start + 20, Endian.Little);
            var emission = ReadI32(zone, start + 24, Endian.Little);
            if (!CountOk(looping) || !CountOk(oneShot) || !CountOk(emission))
            {
                diagnostics.Reject("element-count-invalid", start, inlineName, $"counts={looping}/{oneShot}/{emission}");
                continue;
            }
            diagnostics.SaneCountTriples++;

            var elemCountLong = (long)looping + oneShot + emission;
            if (elemCountLong < 0 || elemCountLong > MaxElemDefs)
            {
                diagnostics.Reject("element-count-sum-invalid", start, inlineName, $"sum={elemCountLong}");
                continue;
            }
            var elemCount = (int)elemCountLong;
            var elemPtr = ReadU32(zone, start + 32, Endian.Little);

            // Alpha 8z diagnostic selection proof:
            // Return to the last conservative cohort that produced 159 deeply traversed bodies.
            // Zero-element headers are diagnostic-only because the 8i diagnostic proved they create thousands of
            // false positives in a byte scan. Packed/reused elemDefs remain unresolved until a real
            // logical block-pointer resolver exists. Only positive-element + inline-elemDefs headers
            // enter the deep typed-walk cohort below.
            if (elemCount == 0)
            {
                diagnostics.ZeroElementHeaders++;

                // Alpha 8z: totalSize is runtime FxEffectDef-owned memory, not the
                // serialized byte length including the separately written name string.
                // A true empty effect therefore has exactly the 0x24-byte fixed body,
                // zero element counts, and a null elemDefs pointer. These candidates are
                // collected separately and are only admitted later if their unique,
                // non-overlapping count exactly fills the XAsset-declared body deficit.
                if (totalSize == FxEffectDefSize && elemPtr == 0)
                {
                    try
                    {
                        var stats = new TraverseStats();
                        var ctx = new WalkContext
                        {
                            Source = zone,
                            Endianness = Endian.Little,
                            Header = header,
                            Output = null,
                            Stats = stats,
                            EffectName = inlineName ?? $"<empty-candidate@0x{start:X}>"
                        };
                        var end = WalkEffect(ctx, start);
                        if (end > start && end <= zone.Length && stats.ElemDefs == 0)
                        {
                            strictEmptyCandidates.Add(new Candidate(start, end, inlineName, totalSize, stats.FxOwnedBytes));
                            diagnostics.ZeroElementAccepted++;
                            if (inlineName is not null) diagnostics.NamedSuccess(inlineName);
                            continue;
                        }
                    }
                    catch (Exception ex) when (ex is InvalidDataException or OverflowException)
                    {
                        diagnostics.Reject("zero-element-strict-walk-failed", start, inlineName, TrimDiagnostic(ex.Message));
                        continue;
                    }
                }

                diagnostics.Reject("zero-element-not-strict", start, inlineName, $"total=0x{totalSize:X}; ptr=0x{elemPtr:X8}");
                continue;
            }

            // Non-empty effects must own more runtime memory than the fixed header.
            if (totalSize == FxEffectDefSize)
            {
                diagnostics.Reject("positive-elements-totalSize-fixed-only", start, inlineName, $"counts={looping}/{oneShot}/{emission}");
                continue;
            }

            diagnostics.PositiveElementHeaders++;
            if (IsInline(elemPtr))
            {
                diagnostics.InlineElemDefs++;
                diagnostics.DeepPositiveInlineHeaders++;
                if (inlineName is not null) diagnostics.DeepNamedInlineHeaders++;
                if (packedName) diagnostics.PackedNameInlineElemDefsHeaders++;
            }
            else if (IsGeneralPointerWithin(elemPtr, header))
            {
                diagnostics.PackedElemDefs++;
                if (inlineName is not null)
                {
                    diagnostics.NamedPackedElemDefsUnresolved++;
                    diagnostics.InlineNamePackedElemDefsHeaders++;
                }
                else
                {
                    diagnostics.PackedNamePackedElemDefsHeaders++;
                    var minRuntime = checked(FxEffectDefSize + elemCount * FxElemDefSize);
                    if (totalSize >= minRuntime)
                    {
                        diagnostics.PackedNamePackedElemDefsMinSizeHeaders++;
                        var nameDecoded = T4ZonePointer.Decode(namePtr);
                        var elemDecoded = T4ZonePointer.Decode(elemPtr);
                        var sanePadding = zone[start + 29] == 0 && zone[start + 30] == 0 && zone[start + 31] == 0;
                        var strongPackedFixed =
                            nameDecoded.Kind == T4ZonePointerKind.Offset &&
                            nameDecoded.Block == T4ZoneBlock.Virtual &&
                            nameDecoded.Offset < header.VirtualBlockSize &&
                            elemDecoded.Kind == T4ZonePointerKind.Offset &&
                            elemDecoded.Block == T4ZoneBlock.Temp &&
                            elemDecoded.Offset < header.TempBlockSize &&
                            sanePadding;
                        if (strongPackedFixed)
                        {
                            diagnostics.StrongPackedFixedHeaders++;
                            diagnostics.StrongPackedFixedCandidates.Add(new Candidate(start, start + FxEffectDefSize, null, totalSize, FxEffectDefSize));
                        }
                        if (diagnostics.PackedBodyCensus.Count < 96)
                            diagnostics.PackedBodyCensus.Add($"0x{start:X} name={nameDecoded} elem={elemDecoded} total=0x{totalSize:X} elems={elemCount} min=0x{minRuntime:X} pad={(sanePadding ? "ok" : "bad")} strong={(strongPackedFixed ? "yes" : "no")}");
                    }
                }
                diagnostics.Reject("packed-elemDefs-unresolved", start, inlineName, $"namePtr=0x{namePtr:X8}; ptr=0x{elemPtr:X8}; elems={elemCount}; total=0x{totalSize:X}");
                continue;
            }
            else
            {
                diagnostics.Reject("elemDefs-invalid-pointer", start, inlineName, $"ptr=0x{elemPtr:X8}; elems={elemCount}");
                continue;
            }

            try
            {
                var stats = new TraverseStats();
                var ctx = new WalkContext
                {
                    Source = zone,
                    Endianness = Endian.Little,
                    Header = header,
                    Output = null,
                    Stats = stats,
                    EffectName = inlineName ?? $"<candidate@0x{start:X}>"
                };
                var end = WalkEffect(ctx, start);
                if (end <= start || end > zone.Length)
                {
                    diagnostics.Reject("typed-walk-invalid-end", start, inlineName, $"end=0x{end:X}");
                    continue;
                }
                diagnostics.TypedWalkSuccess++;
                diagnostics.DeepTypedSuccess++;
                if (inlineName is not null) diagnostics.NamedSuccess(inlineName);
                if (packedName) diagnostics.PackedNameInlineElemDefsTypedSuccess++;
                var proven = new Candidate(start, end, inlineName, totalSize, stats.FxOwnedBytes);
                if (infiniteLoopLife)
                {
                    diagnostics.InfiniteLoopLifeTypedSuccess++;
                    infiniteLoopCandidates.Add(proven);
                }
                else
                {
                    list.Add(proven);
                }
            }
            catch (InvalidDataException ex)
            {
                var bucket = NormalizeWalkFailure(ex.Message);
                var detail = TrimDiagnostic(ex.Message);
                diagnostics.Reject(bucket, start, inlineName, detail);
                diagnostics.FocusedMiss(bucket, start, inlineName ?? $"<packed-name 0x{namePtr:X8}>", detail);
                diagnostics.DeepReject(bucket, start, inlineName, detail);
            }
            catch (OverflowException ex)
            {
                var detail = TrimDiagnostic(ex.Message);
                diagnostics.Reject("typed-walk-overflow", start, inlineName, detail);
                diagnostics.FocusedMiss("typed-walk-overflow", start, inlineName ?? $"<packed-name 0x{namePtr:X8}>", detail);
                diagnostics.DeepReject("typed-walk-overflow", start, inlineName, detail);
            }
        }
        return list;
    }

    private sealed record FxTableRun(int FirstAssetIndex, int LastAssetIndex, int Length);
    private sealed record CandidateChain(int Start, int End, int Count);
    private sealed record FxRunAlignmentRow(FxTableRun Run, CandidateChain? Chain, bool IsPartial, int MissingBodies);
    private sealed record FxRunAlignmentResult(
        IReadOnlyList<FxRunAlignmentRow> Rows,
        IReadOnlyList<IReadOnlyList<FxRunAlignmentRow>> OptimalRows,
        long OptimalAlignmentCount,
        int SkippedRuns,
        int PartialMatches,
        int MissingBodies);

    private readonly record struct AlignmentScore(int PartialMatches, int MaxPartialDeficit, int PartialDeficitSquares) : IComparable<AlignmentScore>
    {
        public int CompareTo(AlignmentScore other)
        {
            var c = PartialMatches.CompareTo(other.PartialMatches);
            if (c != 0) return c;
            c = MaxPartialDeficit.CompareTo(other.MaxPartialDeficit);
            if (c != 0) return c;
            return PartialDeficitSquares.CompareTo(other.PartialDeficitSquares);
        }

        public AlignmentScore AddPartial(int deficit) => deficit <= 0
            ? this
            : new AlignmentScore(
                PartialMatches + 1,
                Math.Max(MaxPartialDeficit, deficit),
                checked(PartialDeficitSquares + deficit * deficit));
    }

    private sealed class AlignmentCell
    {
        public bool Reachable;
        public AlignmentScore Score;
        public long Ways;
        public char Choice; // 'S' skip table run, 'M' match run->chain
    }

    private static FxRunAlignmentResult AlignFxRuns(IReadOnlyList<FxTableRun> runs, IReadOnlyList<CandidateChain> chains)
    {
        var n = runs.Count;
        var m = chains.Count;
        var dp = new AlignmentCell[n + 1, m + 1];
        for (var i = 0; i <= n; i++)
            for (var j = 0; j <= m; j++)
                dp[i, j] = new AlignmentCell();

        dp[0, 0].Reachable = true;
        dp[0, 0].Score = new AlignmentScore(0, 0, 0);
        dp[0, 0].Ways = 1;

        static void Relax(AlignmentCell target, AlignmentScore score, long ways, char choice)
        {
            if (!target.Reachable || score.CompareTo(target.Score) < 0)
            {
                target.Reachable = true;
                target.Score = score;
                target.Ways = ways;
                target.Choice = choice;
                return;
            }
            if (score.CompareTo(target.Score) == 0)
            {
                target.Ways = Math.Min(1_000_000_000L, target.Ways + ways);
                if (target.Choice != choice) target.Choice = 'A';
            }
        }

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j <= m; j++)
            {
                var cur = dp[i, j];
                if (!cur.Reachable) continue;

                Relax(dp[i + 1, j], cur.Score, cur.Ways, 'S');

                if (j < m && chains[j].Count <= runs[i].Length)
                {
                    var deficit = runs[i].Length - chains[j].Count;
                    Relax(dp[i + 1, j + 1], cur.Score.AddPartial(deficit), cur.Ways, 'M');
                }
            }
        }

        var final = dp[n, m];
        if (!final.Reachable)
            return new FxRunAlignmentResult(Array.Empty<FxRunAlignmentRow>(), Array.Empty<IReadOnlyList<FxRunAlignmentRow>>(), 0, n, 0, runs.Sum(r => r.Length));

        bool CanMatch(int ri, int cj)
        {
            if (ri <= 0 || cj <= 0) return false;
            var run = runs[ri - 1];
            var chain = chains[cj - 1];
            if (chain.Count > run.Length || !dp[ri - 1, cj - 1].Reachable) return false;
            var deficit = run.Length - chain.Count;
            var expected = dp[ri - 1, cj - 1].Score.AddPartial(deficit);
            return expected.CompareTo(dp[ri, cj].Score) == 0;
        }

        bool CanSkip(int ri, int cj)
        {
            if (ri <= 0 || !dp[ri - 1, cj].Reachable) return false;
            return dp[ri - 1, cj].Score.CompareTo(dp[ri, cj].Score) == 0;
        }

        var allRows = new List<IReadOnlyList<FxRunAlignmentRow>>();
        const int maxEnumeratedAlignments = 10000;
        void Backtrack(int ri, int cj, List<FxRunAlignmentRow> reverseRows)
        {
            if (allRows.Count >= maxEnumeratedAlignments) return;
            if (ri == 0)
            {
                if (cj != 0) return;
                var rows = reverseRows.AsEnumerable().Reverse().ToList();
                allRows.Add(rows);
                return;
            }

            var run = runs[ri - 1];
            // Enumerate match first so row 0 remains consistent with the old deterministic reconstruction.
            if (CanMatch(ri, cj))
            {
                var chain = chains[cj - 1];
                var deficit = run.Length - chain.Count;
                reverseRows.Add(new FxRunAlignmentRow(run, chain, deficit > 0, deficit));
                Backtrack(ri - 1, cj - 1, reverseRows);
                reverseRows.RemoveAt(reverseRows.Count - 1);
            }
            if (CanSkip(ri, cj))
            {
                reverseRows.Add(new FxRunAlignmentRow(run, null, false, run.Length));
                Backtrack(ri - 1, cj, reverseRows);
                reverseRows.RemoveAt(reverseRows.Count - 1);
            }
        }
        Backtrack(n, m, new List<FxRunAlignmentRow>(n));

        var rowsRepresentative = allRows.Count > 0 ? allRows[0] : Array.Empty<FxRunAlignmentRow>();
        var skipped = rowsRepresentative.Count(r => r.Chain is null);
        var partial = rowsRepresentative.Count(r => r.Chain is not null && r.IsPartial);
        var missing = rowsRepresentative.Sum(r => r.MissingBodies);
        return new FxRunAlignmentResult(rowsRepresentative, allRows, final.Ways, skipped, partial, missing);
    }

    private sealed record FxRunConsensusRow(
        FxTableRun Run,
        int IncompleteAlignments,
        int TotalAlignments,
        int MinMissingBodies,
        int MaxMissingBodies,
        IReadOnlyList<int> PossibleMissingBodies);

    private static List<FxRunConsensusRow> BuildRunConsensus(FxRunAlignmentResult alignment)
    {
        var output = new List<FxRunConsensusRow>();
        if (alignment.OptimalRows.Count == 0) return output;
        var total = alignment.OptimalRows.Count;
        var runOrder = alignment.OptimalRows[0].Select(r => r.Run).ToList();
        for (var i = 0; i < runOrder.Count; i++)
        {
            var missing = alignment.OptimalRows.Select(rows => rows[i].MissingBodies).ToList();
            var incomplete = missing.Count(v => v > 0);
            var possible = missing.Distinct().OrderBy(v => v).ToList();
            output.Add(new FxRunConsensusRow(
                runOrder[i],
                incomplete,
                total,
                missing.Min(),
                missing.Max(),
                possible));
        }
        return output;
    }

    private sealed class LocatorWindowStats
    {
        public long BytesScanned;
        public long ProvenBytesSkipped;
        public int ValidNamePointers;
        public int ReadableInlineNames;
        public readonly Dictionary<string, int> Buckets = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Examples = new();

        public void Hit(string bucket, int offset, string? name, string detail)
        {
            Buckets.TryGetValue(bucket, out var count);
            Buckets[bucket] = count + 1;
            if (Examples.Count < 48 && (name is not null || bucket is "packed-elemDefs" or "deep-shaped-not-proven" or "typed-walk-failed"))
                Examples.Add($"0x{offset:X}: {bucket}; {(name is null ? "<packed-name>" : $"'{name}'")}; {detail}");
        }
    }

    private static string BuildXAssetOrderLocatorReport(
        byte[] zone,
        ZoneHeaderSummary header,
        int payloadScanStart,
        IReadOnlyList<T4AssetEntry> allAssets,
        IReadOnlyList<Candidate> proven,
        FxRunAlignmentResult alignment,
        IReadOnlyList<FxRunConsensusRow> certainIncompleteRuns,
        CancellationToken cancellationToken)
    {
        const int MaxWindowBytes = 4 * 1024 * 1024;
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter Stage 4 alpha 8z - XAsset-order FX gap locator");
        sb.AppendLine("================================================================");
        sb.AppendLine("Purpose: use the authoritative XAsset table order plus proven FX physical chains to bound where CERTAIN missing FX runs must occur.");
        sb.AppendLine("This report does NOT accept new bodies. It classifies header shapes inside those bounded windows so the next parser change is evidence-driven.");
        sb.AppendLine();
        sb.AppendLine($"Zone bytes: {zone.Length:N0}");
        sb.AppendLine($"Payload scan start: 0x{payloadScanStart:X}");
        sb.AppendLine($"Proven typed FX bodies: {proven.Count:N0}");
        sb.AppendLine($"Optimal ordered alignments enumerated: {alignment.OptimalRows.Count:N0} (DP ways {alignment.OptimalAlignmentCount:N0})");
        sb.AppendLine($"CERTAIN incomplete table runs: {certainIncompleteRuns.Count:N0}");
        sb.AppendLine();

        var provenOrdered = proven.OrderBy(c => c.Offset).ToList();
        var probeCache = new Dictionary<(int Left, int Right), LocatorWindowStats>();
        foreach (var consensus in certainIncompleteRuns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = consensus.Run;
            var rowIndex = alignment.Rows.ToList().FindIndex(r => r.Run.FirstAssetIndex == run.FirstAssetIndex && r.Run.LastAssetIndex == run.LastAssetIndex);
            if (rowIndex < 0)
                continue;

            var windows = new List<(int Left, int Right, CandidateChain? Chain)>();
            foreach (var rows in alignment.OptimalRows)
            {
                var idx = rows.ToList().FindIndex(r => r.Run.FirstAssetIndex == run.FirstAssetIndex && r.Run.LastAssetIndex == run.LastAssetIndex);
                if (idx < 0) continue;
                var row = rows[idx];
                var prev = rows.Take(idx).Reverse().Select(r => r.Chain).FirstOrDefault(c => c is not null);
                var next = rows.Skip(idx + 1).Select(r => r.Chain).FirstOrDefault(c => c is not null);
                var left = Math.Clamp(prev?.End ?? payloadScanStart, payloadScanStart, zone.Length);
                var right = Math.Clamp(next?.Start ?? zone.Length, left, zone.Length);
                windows.Add((left, right, row.Chain));
            }

            var distinct = windows
                .GroupBy(w => (w.Left, w.Right, Start: w.Chain?.Start ?? -1, End: w.Chain?.End ?? -1, Count: w.Chain?.Count ?? 0))
                .Select(g => g.First())
                .OrderBy(w => w.Left)
                .ThenBy(w => w.Right)
                .ToList();

            sb.AppendLine($"RUN assets #{run.FirstAssetIndex}-#{run.LastAssetIndex} len={run.Length}; certain missing={consensus.MinMissingBodies}..{consensus.MaxMissingBodies}; incomplete in {consensus.IncompleteAlignments}/{consensus.TotalAlignments} alignments");
            sb.AppendLine("  table context:");
            var contextStart = Math.Max(0, run.FirstAssetIndex - 4);
            var contextEnd = Math.Min(allAssets.Count - 1, run.LastAssetIndex + 4);
            for (var i = contextStart; i <= contextEnd; i++)
            {
                var a = allAssets[i];
                var mark = i >= run.FirstAssetIndex && i <= run.LastAssetIndex ? "*" : " ";
                sb.AppendLine($"    {mark}#{a.Index,-6} {a.TypeName,-18} header=0x{a.RawHeader:X8} {a.ReferenceKind}");
            }
            sb.AppendLine($"  possible physical windows from all optimal alignments: {distinct.Count:N0}");

            for (var wi = 0; wi < distinct.Count; wi++)
            {
                var w = distinct[wi];
                var originalBytes = Math.Max(0, w.Right - w.Left);
                var slices = new List<(int Left, int Right, string Label)>();
                if (originalBytes <= MaxWindowBytes)
                {
                    slices.Add((w.Left, w.Right, "full"));
                }
                else
                {
                    var half = MaxWindowBytes / 2;
                    slices.Add((w.Left, Math.Min(w.Right, w.Left + half), "left edge"));
                    slices.Add((Math.Max(w.Left, w.Right - half), w.Right, "right edge"));
                }

                sb.AppendLine($"    window {wi + 1}: 0x{w.Left:X}-0x{w.Right:X} ({originalBytes:N0} bytes); matched chain={(w.Chain is null ? "<none>" : $"0x{w.Chain.Start:X}-0x{w.Chain.End:X} len={w.Chain.Count}")}");
                if (originalBytes > MaxWindowBytes)
                    sb.AppendLine($"      NOTE: window exceeds {MaxWindowBytes:N0} bytes; census samples both edges ({MaxWindowBytes:N0} bytes total) rather than rescanning the entire gap.");

                foreach (var slice in slices)
                {
                    if (!probeCache.TryGetValue((slice.Left, slice.Right), out var stats))
                    {
                        stats = ProbeLocatorWindow(zone, header, slice.Left, slice.Right, provenOrdered, cancellationToken);
                        probeCache[(slice.Left, slice.Right)] = stats;
                    }
                    sb.AppendLine($"      {slice.Label}: 0x{slice.Left:X}-0x{slice.Right:X}; scanned={stats.BytesScanned:N0}; proven-bytes-skipped={stats.ProvenBytesSkipped:N0}; valid-name-ptrs={stats.ValidNamePointers:N0}; readable-inline-names={stats.ReadableInlineNames:N0}");
                    var buckets = stats.Buckets.Count == 0
                        ? "<none>"
                        : string.Join(", ", stats.Buckets.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value:N0}"));
                    sb.AppendLine($"        buckets: {buckets}");
                    foreach (var ex in stats.Examples)
                        sb.AppendLine("        example: " + ex);
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("Interpretation guide");
        sb.AppendLine("  packed-elemDefs            = a structurally plausible FX header whose elemDefs pointer reuses previously loaded block data; this requires logical block-pointer resolution, not a fresh inline array.");
        sb.AppendLine("  zero-element               = plausible FX header with no elemDefs; retained as evidence only because whole-zone scanning made this shape too permissive.");
        sb.AppendLine("  deep-shaped-not-proven     = header reaches the same inline-elemDefs gate as proven FX but is not one of the already accepted physical bodies.");
        sb.AppendLine("  typed-walk-failed          = strong local FX shape whose owned-payload walk failed; the detail is a high-value parser target.");
        sb.AppendLine("  total/count/life/name-*    = the exact gate rejecting otherwise locally plausible bytes.");
        sb.AppendLine();
        sb.AppendLine("Alpha 9b keeps the legacy 8z locator diagnostic-only; zero-total recovery is handled separately by the strict exact-deficit proof.");
        return sb.ToString();
    }

    private static LocatorWindowStats ProbeLocatorWindow(
        byte[] zone,
        ZoneHeaderSummary header,
        int left,
        int right,
        IReadOnlyList<Candidate> proven,
        CancellationToken cancellationToken)
    {
        var stats = new LocatorWindowStats();
        left = Math.Clamp(left, 0, Math.Max(0, zone.Length - FxEffectDefSize));
        right = Math.Clamp(right, left, zone.Length);
        var provenIndex = 0;
        while (provenIndex < proven.Count && proven[provenIndex].End <= left) provenIndex++;

        for (var start = left; start <= right - FxEffectDefSize; start++)
        {
            if ((start & 0x3FFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            while (provenIndex < proven.Count && proven[provenIndex].End <= start) provenIndex++;
            if (provenIndex < proven.Count && start >= proven[provenIndex].Offset && start < proven[provenIndex].End)
            {
                stats.ProvenBytesSkipped++;
                continue;
            }
            stats.BytesScanned++;

            var namePtr = ReadU32(zone, start, Endian.Little);
            if (!IsStringPointerWithin(namePtr, header) || namePtr == 0)
                continue;
            stats.ValidNamePointers++;

            string? inlineName = null;
            if (IsInline(namePtr))
            {
                if (!TryReadCString(zone, start + FxEffectDefSize, 256, out var n, out _) || !LooksLikeFxName(n))
                {
                    stats.Hit("inline-name-invalid", start, null, $"namePtr=0x{namePtr:X8}");
                    continue;
                }
                inlineName = n;
                stats.ReadableInlineNames++;
            }

            var totalSize = ReadI32(zone, start + 8, Endian.Little);
            if (totalSize < FxEffectDefSize || totalSize > zone.Length - start)
            {
                stats.Hit("totalSize-invalid", start, inlineName, $"total=0x{totalSize:X}");
                continue;
            }

            var life = ReadI32(zone, start + 12, Endian.Little);
            if (life < -1)
            {
                stats.Hit("loopingLife-negative", start, inlineName, $"life={life}");
                continue;
            }

            var looping = ReadI32(zone, start + 16, Endian.Little);
            var oneShot = ReadI32(zone, start + 20, Endian.Little);
            var emission = ReadI32(zone, start + 24, Endian.Little);
            if (!CountOk(looping) || !CountOk(oneShot) || !CountOk(emission))
            {
                stats.Hit("element-count-invalid", start, inlineName, $"counts={looping}/{oneShot}/{emission}");
                continue;
            }
            var sumLong = (long)looping + oneShot + emission;
            if (sumLong < 0 || sumLong > MaxElemDefs)
            {
                stats.Hit("element-count-sum-invalid", start, inlineName, $"sum={sumLong}");
                continue;
            }
            var elemCount = (int)sumLong;
            var elemPtr = ReadU32(zone, start + 32, Endian.Little);
            if (elemCount == 0)
            {
                stats.Hit("zero-element", start, inlineName, $"total=0x{totalSize:X}; elemPtr=0x{elemPtr:X8}");
                continue;
            }
            if (totalSize == FxEffectDefSize)
            {
                stats.Hit("fixed-only-positive-elements", start, inlineName, $"counts={looping}/{oneShot}/{emission}");
                continue;
            }
            if (!IsInline(elemPtr))
            {
                if (IsGeneralPointerWithin(elemPtr, header))
                    stats.Hit("packed-elemDefs", start, inlineName, $"elemPtr=0x{elemPtr:X8}; elems={elemCount}; total=0x{totalSize:X}");
                else
                    stats.Hit("elemDefs-invalid-pointer", start, inlineName, $"elemPtr=0x{elemPtr:X8}; elems={elemCount}");
                continue;
            }

            try
            {
                var walkStats = new TraverseStats();
                var ctx = new WalkContext
                {
                    Source = zone,
                    Endianness = Endian.Little,
                    Header = header,
                    Output = null,
                    Stats = walkStats,
                    EffectName = inlineName ?? $"<locator@0x{start:X}>"
                };
                var end = WalkEffect(ctx, start);
                stats.Hit("deep-shaped-not-proven", start, inlineName, $"typed walk succeeded to 0x{end:X}; elems={elemCount}; total=0x{totalSize:X}");
            }
            catch (Exception ex) when (ex is InvalidDataException or OverflowException)
            {
                stats.Hit("typed-walk-failed", start, inlineName, TrimDiagnostic(ex.Message));
            }
        }
        return stats;
    }

    private static IEnumerable<string> RenderRunAlignment(FxRunAlignmentResult alignment, IReadOnlyList<T4AssetEntry> allAssets)
    {
        if (alignment.Rows.Count == 0)
        {
            yield return "<no valid ordered alignment>";
            yield break;
        }

        var byIndex = allAssets.ToDictionary(a => a.Index);
        CandidateChain? previousChain = null;
        for (var i = 0; i < alignment.Rows.Count; i++)
        {
            var row = alignment.Rows[i];
            var nextChain = alignment.Rows.Skip(i + 1).Select(r => r.Chain).FirstOrDefault(c => c is not null);
            var beforeType = byIndex.TryGetValue(row.Run.FirstAssetIndex - 1, out var before) ? before.TypeName : "<BOF>";
            var afterType = byIndex.TryGetValue(row.Run.LastAssetIndex + 1, out var after) ? after.TypeName : "<EOF>";
            var chainText = row.Chain is null
                ? "<MISSING WHOLE RUN>"
                : $"0x{row.Chain.Start:X}-0x{row.Chain.End:X} len={row.Chain.Count}";
            var status = row.Chain is null ? $"MISSING {row.MissingBodies}" : row.IsPartial ? $"PARTIAL missing={row.MissingBodies}" : "exact";
            var leftBoundary = previousChain is null ? "?" : $"0x{previousChain.End:X}";
            var rightBoundary = nextChain is null ? "?" : $"0x{nextChain.Start:X}";
            yield return $"assets #{row.Run.FirstAssetIndex}-#{row.Run.LastAssetIndex} len={row.Run.Length} [{beforeType} -> FX -> {afterType}] => {chainText} :: {status}; neighboring proven boundaries {leftBoundary} .. {rightBoundary}";
            if (row.Chain is not null) previousChain = row.Chain;
        }
    }

    private static List<FxTableRun> BuildFxTableRuns(IReadOnlyList<T4AssetEntry> fxEntries)
    {
        var output = new List<FxTableRun>();
        if (fxEntries.Count == 0) return output;
        var ordered = fxEntries.OrderBy(e => e.Index).ToList();
        var first = ordered[0].Index;
        var last = first;
        for (var i = 1; i < ordered.Count; i++)
        {
            var index = ordered[i].Index;
            if (index == last + 1)
            {
                last = index;
                continue;
            }
            output.Add(new FxTableRun(first, last, last - first + 1));
            first = last = index;
        }
        output.Add(new FxTableRun(first, last, last - first + 1));
        return output;
    }

    private static List<CandidateChain> BuildCandidateChains(IReadOnlyList<Candidate> candidates)
    {
        var output = new List<CandidateChain>();
        if (candidates.Count == 0) return output;
        var ordered = candidates.OrderBy(c => c.Offset).ToList();
        var start = ordered[0].Offset;
        var end = ordered[0].End;
        var count = 1;
        for (var i = 1; i < ordered.Count; i++)
        {
            var c = ordered[i];
            if (c.Offset == end)
            {
                end = c.End;
                count++;
                continue;
            }
            output.Add(new CandidateChain(start, end, count));
            start = c.Offset;
            end = c.End;
            count = 1;
        }
        output.Add(new CandidateChain(start, end, count));
        return output;
    }

    private static string RenderLengthDistribution(IEnumerable<int> lengths)
    {
        var groups = lengths.GroupBy(x => x).OrderBy(g => g.Key).Select(g => $"{g.Key}x{g.Count()}");
        var text = string.Join(",", groups);
        return string.IsNullOrEmpty(text) ? "<none>" : text;
    }

    private static List<List<Candidate>> BuildOverlapClusters(IReadOnlyList<Candidate> candidates)
    {
        var result = new List<List<Candidate>>();
        foreach (var candidate in candidates.OrderBy(c => c.Offset))
        {
            if (result.Count == 0 || !result[^1].Any(x => RangesOverlap(x.Offset, x.End, candidate.Offset, candidate.End)))
                result.Add(new List<Candidate> { candidate });
            else
                result[^1].Add(candidate);
        }
        return result;
    }

    private static bool RangesOverlap(int aStart, int aEnd, int bStart, int bEnd) =>
        (long)aStart < bEnd && (long)bStart < aEnd;

    private static bool IsGeneralPointerWithin(uint raw, ZoneHeaderSummary header)
    {
        if (raw == 0 || IsInline(raw)) return raw != 0;
        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null) return false;
        var block = (int)decoded.Block.Value;
        return block >= 0 && block < header.BlockSizes.Count && decoded.Offset < header.BlockSizes[block];
    }

    private static string NormalizeWalkFailure(string message)
    {
        if (message.Contains("invalid elemType", StringComparison.OrdinalIgnoreCase)) return "typed.invalid-elemType";
        if (message.Contains("implausible visualCount", StringComparison.OrdinalIgnoreCase)) return "typed.visualCount";
        if (message.Contains("trail", StringComparison.OrdinalIgnoreCase)) return "typed.trail";
        if (message.Contains("XModel", StringComparison.OrdinalIgnoreCase)) return "typed.xmodel";
        if (message.Contains("Material", StringComparison.OrdinalIgnoreCase)) return "typed.material";
        if (message.Contains("string", StringComparison.OrdinalIgnoreCase) || message.Contains("FX ref", StringComparison.OrdinalIgnoreCase)) return "typed.string-or-fxref";
        if (message.Contains("pointer", StringComparison.OrdinalIgnoreCase)) return "typed.pointer";
        if (message.Contains("sample", StringComparison.OrdinalIgnoreCase)) return "typed.samples";
        return "typed.other";
    }

    private static string TrimDiagnostic(string message)
    {
        const int max = 180;
        var oneLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..max] + "...";
    }

    private static int WalkEffect(WalkContext c, int start)
    {
        Ensure(c.Source, start, FxEffectDefSize, $"FX '{c.EffectName}' fixed body");
        c.Stats.FxOwnedBytes = checked(c.Stats.FxOwnedBytes + FxEffectDefSize);
        ValidateEffectFixed(c, start, out var elemCount, out var namePtr, out var elemPtr);
        WriteEffectFixed(c, start);

        var cursor = start + FxEffectDefSize;
        cursor = ConsumeXString(c, namePtr, cursor, "name");

        if (elemCount == 0 || elemPtr == 0 || !IsInline(elemPtr))
            return cursor;

        var elemsStart = cursor;
        var elemBytes = checked(elemCount * FxElemDefSize);
        Ensure(c.Source, elemsStart, elemBytes, $"FX '{c.EffectName}' FxElemDef[{elemCount}]");

        for (var i = 0; i < elemCount; i++)
        {
            var elemStart = elemsStart + i * FxElemDefSize;
            ValidateElemFixed(c, elemStart, i);
            WriteElemFixed(c, elemStart);
        }
        cursor += elemBytes;
        c.Stats.FxOwnedBytes = checked(c.Stats.FxOwnedBytes + elemBytes);
        c.Stats.ElemDefs += elemCount;

        for (var i = 0; i < elemCount; i++)
            cursor = WalkElemOwned(c, elemsStart + i * FxElemDefSize, i, cursor);

        return cursor;
    }

    private static int WalkElemOwned(WalkContext c, int elemStart, int index, int cursor)
    {
        var elemType = c.Source[elemStart + 180];
        var visualCount = c.Source[elemStart + 181];
        // FxElemDef stores these as signed char. The zone-code count expression is
        // intervalCount + 1, so 0xFF means -1 => zero serialized samples, not 256.
        var velIntervalCount = unchecked((sbyte)c.Source[elemStart + 182]);
        var visIntervalCount = unchecked((sbyte)c.Source[elemStart + 183]);
        if (velIntervalCount < -1 || visIntervalCount < -1)
            throw new InvalidDataException($"FX '{c.EffectName}' elem[{index}] has invalid signed sample interval counts ({velIntervalCount}/{visIntervalCount}).");
        var velCount = velIntervalCount + 1;
        var visCount = visIntervalCount + 1;
        var velPtr = ReadU32(c.Source, elemStart + 184, c.Endianness);
        var visPtr = ReadU32(c.Source, elemStart + 188, c.Endianness);
        var visualsPtr = ReadU32(c.Source, elemStart + 192, c.Endianness);
        var impactPtr = ReadU32(c.Source, elemStart + 220, c.Endianness);
        var deathPtr = ReadU32(c.Source, elemStart + 224, c.Endianness);
        var emittedPtr = ReadU32(c.Source, elemStart + 228, c.Endianness);
        var trailPtr = ReadU32(c.Source, elemStart + 248, c.Endianness);

        c.CurrentElemIndex = index;
        c.CurrentElemStart = elemStart;
        c.CurrentElemType = elemType;
        c.CurrentVisualCount = visualCount;
        c.CurrentVelIntervalCount = velIntervalCount;
        c.CurrentVisIntervalCount = visIntervalCount;
        c.CurrentVisualPointer = visualsPtr;

        cursor = ConsumeFloat32Array(c, velPtr, checked(velCount * (FxVelSampleSize / 4)), cursor, $"elem[{index}].velSamples");
        if (velPtr != 0 && IsInline(velPtr)) c.Stats.VelocitySamples += velCount;

        cursor = ConsumeVisualStateSamples(c, visPtr, visCount, cursor, $"elem[{index}].visSamples");
        if (visPtr != 0 && IsInline(visPtr)) c.Stats.VisualSamples += visCount;

        cursor = ConsumeVisuals(c, elemType, visualCount, visualsPtr, cursor, index);
        cursor = ConsumeFxRefName(c, impactPtr, cursor, $"elem[{index}].effectOnImpact");
        cursor = ConsumeFxRefName(c, deathPtr, cursor, $"elem[{index}].effectOnDeath");
        cursor = ConsumeFxRefName(c, emittedPtr, cursor, $"elem[{index}].effectEmitted");
        cursor = ConsumeTrail(c, trailPtr, cursor, index);
        return cursor;
    }

    private static int ConsumeVisuals(WalkContext c, byte elemType, int visualCount, uint raw, int cursor, int elemIndex)
    {
        if (visualCount > 1 || elemType == 10)
            ValidateGeneralPointer(c, raw, $"elem[{elemIndex}].visuals");
        else
            ValidateVisualPointer(c, elemType, raw, $"elem[{elemIndex}].visuals");
        if (visualCount == 0 || raw == 0) return cursor;

        if (elemType == 10) // FX_ELEM_TYPE_DECAL -> FxElemMarkVisuals[visualCount]
        {
            if (!IsInline(raw)) return cursor;
            var bytes = checked(visualCount * 8);
            Ensure(c.Source, cursor, bytes, $"elem[{elemIndex}].markArray");
            var ptrs = new uint[visualCount * 2];
            for (var i = 0; i < ptrs.Length; i++)
            {
                ptrs[i] = ReadU32(c.Source, cursor + i * 4, c.Endianness);
                ValidateGeneralPointer(c, ptrs[i], $"elem[{elemIndex}].markArray.material[{i}]");
                WriteU32Scalar(c, cursor + i * 4, ptrs[i]);
            }
            cursor += bytes;
            c.Stats.FxOwnedBytes = checked(c.Stats.FxOwnedBytes + bytes);
            c.Stats.DecalMarkArrays++;
            c.Stats.MaterialVisuals += ptrs.Length;
            for (var i = 0; i < ptrs.Length; i++)
                cursor = ConsumeMaterial(c, ptrs[i], cursor, $"elem[{elemIndex}].markArray.material[{i}]");
            return cursor;
        }

        if (visualCount > 1)
        {
            if (!IsInline(raw)) return cursor;
            var bytes = checked(visualCount * 4);
            Ensure(c.Source, cursor, bytes, $"elem[{elemIndex}].visualArray");
            var ptrs = new uint[visualCount];
            for (var i = 0; i < visualCount; i++)
            {
                ptrs[i] = ReadU32(c.Source, cursor + i * 4, c.Endianness);
                ValidateVisualPointer(c, elemType, ptrs[i], $"elem[{elemIndex}].visual[{i}]");
                WriteU32Scalar(c, cursor + i * 4, ptrs[i]);
            }
            cursor += bytes;
            c.Stats.FxOwnedBytes = checked(c.Stats.FxOwnedBytes + bytes);
            for (var i = 0; i < visualCount; i++)
                cursor = ConsumeVisualDependency(c, elemType, ptrs[i], cursor, $"elem[{elemIndex}].visual[{i}]");
            return cursor;
        }

        ValidateVisualPointer(c, elemType, raw, $"elem[{elemIndex}].visual");
        return ConsumeVisualDependency(c, elemType, raw, cursor, $"elem[{elemIndex}].visual");
    }

    private static void ValidateVisualPointer(WalkContext c, byte elemType, uint raw, string label)
    {
        if (elemType is 9 or 11)
        {
            ValidateStringPointer(c, raw, label);
            return;
        }
        if (elemType <= 6 || elemType == 10)
        {
            ValidateGeneralPointer(c, raw, label);
            return;
        }
        // Omni/spot-light FxElemVisuals use the anonymous union arm, which the
        // T4 ownership map deliberately marks as never serialized as a dependency.
        // Do not reinterpret the embedded 32-bit union value as an asset pointer.
    }

    private static int ConsumeVisualDependency(WalkContext c, byte elemType, uint raw, int cursor, string label)
    {
        switch (elemType)
        {
            case 0: // sprite billboard
            case 1: // sprite oriented
            case 2: // tail
            case 3: // line
            case 4: // trail
            case 5: // cloud
                c.Stats.MaterialVisuals++;
                return ConsumeMaterial(c, raw, cursor, label + ".material");
            case 6: // model
                c.Stats.ModelVisuals++;
                return ConsumeXModel(c, raw, cursor, label + ".model");
            case 9: // sound
                c.Stats.SoundVisuals++;
                return ConsumeXString(c, raw, cursor, label + ".soundName");
            case 11: // runner -> FxEffectDefRef name
                c.Stats.RunnerVisuals++;
                return ConsumeFxRefName(c, raw, cursor, label + ".effectDef");
            default:
                // omni/spot lights have no serialized FxElemVisuals dependency in
                // the T4 ZoneCode ownership map.
                return cursor;
        }
    }

    private static int ConsumeFxRefName(WalkContext c, uint raw, int cursor, string label)
    {
        ValidateStringPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;
        c.Stats.EffectRefNames++;
        return ConsumeXString(c, raw, cursor, label);
    }

    private static int ConsumeTrail(WalkContext c, uint raw, int cursor, int elemIndex)
    {
        ValidateGeneralPointer(c, raw, $"elem[{elemIndex}].trailDef");
        if (raw == 0 || !IsInline(raw)) return cursor;

        Ensure(c.Source, cursor, FxTrailDefSize, $"elem[{elemIndex}].trailDef");
        var trailStart = cursor;
        var vertCount = ReadI32(c.Source, trailStart + 12, c.Endianness);
        var vertsPtr = ReadU32(c.Source, trailStart + 16, c.Endianness);
        var indCount = ReadI32(c.Source, trailStart + 20, c.Endianness);
        var indsPtr = ReadU32(c.Source, trailStart + 24, c.Endianness);
        if (vertCount < 0 || vertCount > 1_000_000 || indCount < 0 || indCount > 4_000_000)
            throw new InvalidDataException($"FX '{c.EffectName}' elem[{elemIndex}] has implausible trail counts ({vertCount:N0} verts, {indCount:N0} inds).");
        ValidateGeneralPointer(c, vertsPtr, $"elem[{elemIndex}].trailDef.verts");
        ValidateGeneralPointer(c, indsPtr, $"elem[{elemIndex}].trailDef.inds");
        if (vertCount > 0 && vertsPtr == 0)
            throw new InvalidDataException($"FX '{c.EffectName}' elem[{elemIndex}] trail has {vertCount:N0} vertices but a null verts pointer.");
        if (indCount > 0 && indsPtr == 0)
            throw new InvalidDataException($"FX '{c.EffectName}' elem[{elemIndex}] trail has {indCount:N0} indices but a null inds pointer.");

        WriteAll32(c, trailStart, FxTrailDefSize);
        cursor += FxTrailDefSize;
        c.Stats.FxOwnedBytes = checked(c.Stats.FxOwnedBytes + FxTrailDefSize);
        c.Stats.TrailDefs++;

        if (vertsPtr != 0 && IsInline(vertsPtr))
        {
            var floats = checked(vertCount * (FxTrailVertexSize / 4));
            cursor = ConsumeFloat32Array(c, vertsPtr, floats, cursor, $"elem[{elemIndex}].trailDef.verts");
            c.Stats.TrailVertices += vertCount;
        }
        if (indsPtr != 0 && IsInline(indsPtr))
        {
            var bytes = checked(indCount * 2);
            Ensure(c.Source, cursor, bytes, $"elem[{elemIndex}].trailDef.inds");
            if (c.Output is not null)
            {
                var dst = new byte[bytes];
                for (var i = 0; i < indCount; i++)
                {
                    var v = EndianIO.ReadUInt16(c.Source.AsSpan(cursor + i * 2, 2), c.Endianness);
                    BinaryPrimitives.WriteUInt16BigEndian(dst.AsSpan(i * 2, 2), v);
                }
                c.Output.Write(dst, 0, dst.Length);
            }
            cursor += bytes;
            c.Stats.FxOwnedBytes = checked(c.Stats.FxOwnedBytes + bytes);
            c.Stats.TrailIndices += indCount;
        }
        return cursor;
    }

    private static int ConsumeVisualStateSamples(WalkContext c, uint raw, int count, int cursor, string label)
    {
        ValidateGeneralPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;
        var bytes = checked(count * FxVisSampleSize);
        Ensure(c.Source, cursor, bytes, label);

        if (c.Output is not null)
        {
            var dst = c.Source.AsSpan(cursor, bytes).ToArray();
            for (var s = 0; s < count; s++)
            {
                var sample = cursor + s * FxVisSampleSize;
                var outSample = s * FxVisSampleSize;
                // Two FxElemVisualState structs per sample. Each has color[4]
                // followed by five 32-bit float values.
                for (var state = 0; state < 2; state++)
                {
                    var srcBase = sample + state * 24;
                    var dstBase = outSample + state * 24;
                    for (var rel = 4; rel < 24; rel += 4)
                    {
                        var v = ReadU32(c.Source, srcBase + rel, c.Endianness);
                        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(dstBase + rel, 4), v);
                    }
                }
            }
            c.Output.Write(dst, 0, dst.Length);
        }
        c.Stats.FxOwnedBytes = checked(c.Stats.FxOwnedBytes + bytes);
        return cursor + bytes;
    }

    private static int ConsumeFloat32Array(WalkContext c, uint raw, int floatCount, int cursor, string label)
    {
        ValidateGeneralPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;
        var bytes = checked(floatCount * 4);
        Ensure(c.Source, cursor, bytes, label);
        WriteAll32(c, cursor, bytes);
        c.Stats.FxOwnedBytes = checked(c.Stats.FxOwnedBytes + bytes);
        return cursor + bytes;
    }

    private static int ConsumeMaterial(WalkContext c, uint raw, int cursor, string label)
    {
        ValidateGeneralPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;

        // Alpha 8z correction: serialized-file position is continuous. T4 allocation
        // alignment applies to the destination block offset, not to the input file
        // cursor, so never skip/insert bytes here. A failure at this exact cursor means
        // an earlier ownership/order/visual interpretation is wrong (or the Material
        // source layout itself is wrong), and must remain visible rather than being
        // hidden by a +1..+3 byte scan.
        var materialStart = cursor;
        try
        {
            if (c.Endianness == Endian.Little)
            {
                var xbox = T4XModelSerializerProof.TransformInlineMaterialToXbox(c.Source, materialStart, out var end, c.Header);
                c.Output?.Write(xbox, 0, xbox.Length);
                c.Stats.InlineMaterials++;
                return end;
            }

            var measuredEnd = T4XModelSerializerProof.MeasureInlineMaterialEnd(c.Source, materialStart, Endian.Big, c.Header);
            c.Stats.InlineMaterials++;
            return measuredEnd;
        }
        catch (InvalidDataException ex)
        {
            var probe = T4XModelSerializerProof.DescribeMaterialProbe(c.Source, materialStart, c.Endianness, c.Header);
            var elemContext = c.CurrentElemIndex >= 0
                ? $" elem[{c.CurrentElemIndex}]@0x{c.CurrentElemStart:X8} type={c.CurrentElemType} visualCount={c.CurrentVisualCount} velIntervals={c.CurrentVelIntervalCount} visIntervals={c.CurrentVisIntervalCount} visualsPtr=0x{c.CurrentVisualPointer:X8}"
                : string.Empty;
            throw new InvalidDataException(
                $"FX '{c.EffectName}' {label} inline Material at exact stream cursor 0x{materialStart:X8} failed: {ex.Message}." +
                $"{elemContext} Alpha 8z does not move the file cursor for alignment; allocalign is a logical block-offset rule. Material-start probe [{probe}]", ex);
        }
    }

    private static int ConsumeXModel(WalkContext c, uint raw, int cursor, string label)
    {
        ValidateGeneralPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;
        if (c.Endianness == Endian.Little)
        {
            var xbox = T4XModelSerializerProof.TransformInlineXModelToXbox(c.Source, cursor, out var end, c.Header);
            c.Output?.Write(xbox, 0, xbox.Length);
            cursor = end;
        }
        else
        {
            cursor = T4XModelSerializerProof.MeasureInlineXModelEnd(c.Source, cursor, Endian.Big, c.Header);
        }
        c.Stats.InlineXModels++;
        return cursor;
    }

    private static int ConsumeXString(WalkContext c, uint raw, int cursor, string label)
    {
        ValidateStringPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;
        var end = FindCStringEnd(c.Source, cursor, $"FX '{c.EffectName}' {label}");
        var bytes = end - cursor;
        c.Output?.Write(c.Source, cursor, bytes);
        c.Stats.FxOwnedBytes = checked(c.Stats.FxOwnedBytes + bytes);
        c.Stats.InlineStrings++;
        return end;
    }

    private static void ValidateEffectFixed(WalkContext c, int start, out int elemCount, out uint namePtr, out uint elemPtr)
    {
        namePtr = ReadU32(c.Source, start, c.Endianness);
        if (namePtr == 0)
            throw new InvalidDataException($"FX '{c.EffectName}' has a null name pointer.");
        ValidateStringPointer(c, namePtr, "name");

        var looping = ReadI32(c.Source, start + 16, c.Endianness);
        var oneShot = ReadI32(c.Source, start + 20, c.Endianness);
        var emission = ReadI32(c.Source, start + 24, c.Endianness);
        if (!CountOk(looping) || !CountOk(oneShot) || !CountOk(emission))
            throw new InvalidDataException($"FX '{c.EffectName}' has implausible element counts ({looping}/{oneShot}/{emission}).");
        elemCount = checked(looping + oneShot + emission);
        if (elemCount > MaxElemDefs)
            throw new InvalidDataException($"FX '{c.EffectName}' has {elemCount:N0} element definitions, above Alpha 8's safety limit.");

        elemPtr = ReadU32(c.Source, start + 32, c.Endianness);
        ValidateGeneralPointer(c, elemPtr, "elemDefs");
        if (elemCount > 0 && elemPtr == 0)
            throw new InvalidDataException($"FX '{c.EffectName}' declares {elemCount:N0} elements but has a null elemDefs pointer.");
        if (elemCount == 0 && elemPtr != 0)
            throw new InvalidDataException($"FX '{c.EffectName}' declares zero elements but has a non-null elemDefs pointer.");
    }

    private static void ValidateElemFixed(WalkContext c, int start, int index)
    {
        var type = c.Source[start + 180];
        if (type > 11)
            throw new InvalidDataException($"FX '{c.EffectName}' elem[{index}] has invalid elemType {type}.");
        var visualCount = c.Source[start + 181];
        if (visualCount > 128)
            throw new InvalidDataException($"FX '{c.EffectName}' elem[{index}] has implausible visualCount {visualCount}.");
        var velIntervals = unchecked((sbyte)c.Source[start + 182]);
        var visIntervals = unchecked((sbyte)c.Source[start + 183]);
        if (velIntervals < -1 || visIntervals < -1)
            throw new InvalidDataException($"FX '{c.EffectName}' elem[{index}] has invalid signed sample interval counts ({velIntervals}/{visIntervals}).");

        ValidateGeneralPointer(c, ReadU32(c.Source, start + 184, c.Endianness), $"elem[{index}].velSamples");
        ValidateGeneralPointer(c, ReadU32(c.Source, start + 188, c.Endianness), $"elem[{index}].visSamples");
        var rawVisuals = ReadU32(c.Source, start + 192, c.Endianness);
        if (visualCount > 1 || type == 10)
            ValidateGeneralPointer(c, rawVisuals, $"elem[{index}].visuals");
        else
            ValidateVisualPointer(c, type, rawVisuals, $"elem[{index}].visuals");
        ValidateStringPointer(c, ReadU32(c.Source, start + 220, c.Endianness), $"elem[{index}].effectOnImpact");
        ValidateStringPointer(c, ReadU32(c.Source, start + 224, c.Endianness), $"elem[{index}].effectOnDeath");
        ValidateStringPointer(c, ReadU32(c.Source, start + 228, c.Endianness), $"elem[{index}].effectEmitted");
        ValidateGeneralPointer(c, ReadU32(c.Source, start + 248, c.Endianness), $"elem[{index}].trailDef");
    }

    private static void WriteEffectFixed(WalkContext c, int start)
    {
        if (c.Output is null) return;
        var dst = c.Source.AsSpan(start, FxEffectDefSize).ToArray();
        foreach (var rel in new[] { 0, 4, 8, 12, 16, 20, 24, 32 })
        {
            var v = ReadU32(c.Source, start + rel, c.Endianness);
            BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(rel, 4), v);
        }
        c.Output.Write(dst, 0, dst.Length);
    }

    private static void WriteElemFixed(WalkContext c, int start)
    {
        if (c.Output is null) return;
        var dst = c.Source.AsSpan(start, FxElemDefSize).ToArray();
        for (var rel = 0; rel < 168; rel += 4)
        {
            var v = ReadU32(c.Source, start + rel, c.Endianness);
            BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(rel, 4), v);
        }
        var atlasCount = EndianIO.ReadUInt16(c.Source.AsSpan(start + 174, 2), c.Endianness);
        BinaryPrimitives.WriteUInt16BigEndian(dst.AsSpan(174, 2), atlasCount);
        foreach (var rel in new[] { 176, 184, 188, 192, 196, 200, 204, 208, 212, 216, 220, 224, 228, 232, 236, 240, 244, 248 })
        {
            var v = ReadU32(c.Source, start + rel, c.Endianness);
            BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(rel, 4), v);
        }
        c.Output.Write(dst, 0, dst.Length);
    }

    private static void WriteAll32(WalkContext c, int start, int bytes)
    {
        if (c.Output is null) return;
        if ((bytes & 3) != 0) throw new InvalidDataException("Alpha 8 internal error: 32-bit payload length is not divisible by four.");
        var dst = c.Source.AsSpan(start, bytes).ToArray();
        for (var rel = 0; rel < bytes; rel += 4)
        {
            var v = ReadU32(c.Source, start + rel, c.Endianness);
            BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(rel, 4), v);
        }
        c.Output.Write(dst, 0, dst.Length);
    }

    private static void WriteU32Scalar(WalkContext c, int sourceOffset, uint value)
    {
        if (c.Output is null) return;
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        c.Output.Write(b);
    }

    private static void ValidateStringPointer(WalkContext c, uint raw, string label)
    {
        if (raw == 0 || IsInline(raw)) return;
        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block != T4ZoneBlock.Virtual)
            throw new InvalidDataException($"FX '{c.EffectName}' {label} has non-VIRTUAL string pointer 0x{raw:X8}.");
        if (c.Header is not null && decoded.Offset >= c.Header.VirtualBlockSize)
            throw new InvalidDataException($"FX '{c.EffectName}' {label} pointer 0x{raw:X8} exceeds VIRTUAL block bounds.");
    }

    private static void ValidateGeneralPointer(WalkContext c, uint raw, string label)
    {
        if (raw == 0 || IsInline(raw)) return;
        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null)
            throw new InvalidDataException($"FX '{c.EffectName}' {label} has invalid serialized pointer 0x{raw:X8}.");
        if (c.Header is not null)
        {
            var block = (int)decoded.Block.Value;
            if (block < 0 || block >= c.Header.BlockSizes.Count || decoded.Offset >= c.Header.BlockSizes[block])
                throw new InvalidDataException($"FX '{c.EffectName}' {label} pointer 0x{raw:X8} exceeds its block bounds.");
        }
    }

    private static bool IsStringPointerWithin(uint raw, ZoneHeaderSummary header)
    {
        if (raw == 0) return false;
        if (IsInline(raw)) return true;
        var decoded = T4ZonePointer.Decode(raw);
        return decoded.Kind == T4ZonePointerKind.Offset && decoded.Block == T4ZoneBlock.Virtual && decoded.Offset < header.VirtualBlockSize;
    }

    private static bool IsInline(uint raw) => raw is Following or Insert;
    private static bool CountOk(int count) => count >= 0 && count <= MaxElemDefs;

    private static bool LooksLikeFxName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255) return false;
        var hasAlphaNum = false;
        foreach (var ch in value)
        {
            if (ch < 0x21 || ch > 0x7E || char.IsWhiteSpace(ch)) return false;
            if (char.IsLetterOrDigit(ch)) hasAlphaNum = true;
        }
        return hasAlphaNum;
    }

    private static bool TryReadCString(byte[] data, int offset, int maxBytes, out string value, out int end)
    {
        value = string.Empty;
        end = offset;
        if (offset < 0 || offset >= data.Length) return false;
        var limit = Math.Min(data.Length, offset + maxBytes);
        while (end < limit && data[end] != 0) end++;
        if (end >= limit || data[end] != 0) return false;
        value = Encoding.UTF8.GetString(data, offset, end - offset);
        end++;
        return true;
    }

    private static int FindCStringEnd(byte[] data, int cursor, string label)
    {
        if (cursor < 0 || cursor >= data.Length)
            throw new InvalidDataException($"{label}: string begins outside the serialized stream.");
        var limit = Math.Min(data.Length, cursor + 4096);
        var end = cursor;
        while (end < limit && data[end] != 0) end++;
        if (end >= limit || data[end] != 0)
            throw new InvalidDataException($"{label}: unterminated/overlong inline string.");
        return end + 1;
    }

    private sealed record NonOverlapSelection(int MaximumCount, bool IsUnique, IReadOnlyList<Candidate> Selected);

    private static NonOverlapSelection SelectUniqueMaximumNonOverlappingSet(IReadOnlyList<Candidate> source)
    {
        var items = source
            .Where(c => c.End > c.Offset)
            .OrderBy(c => c.End)
            .ThenBy(c => c.Offset)
            .ToList();
        var n = items.Count;
        if (n == 0) return new NonOverlapSelection(0, true, Array.Empty<Candidate>());

        // prev[i] = last interval strictly before i that does not overlap i.
        var ends = items.Select(c => c.End).ToArray();
        var prev = new int[n];
        for (var i = 0; i < n; i++)
        {
            var lo = 0;
            var hi = i - 1;
            var best = -1;
            while (lo <= hi)
            {
                var mid = lo + ((hi - lo) >> 1);
                if (ends[mid] <= items[i].Offset)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            prev[i] = best;
        }

        // dpCount[k] / dpWays[k] describe the first k sorted intervals. Ways are
        // saturated at 2 because Alpha 8z only needs to distinguish unique from ambiguous.
        var dpCount = new int[n + 1];
        var dpWays = new byte[n + 1];
        dpWays[0] = 1;
        for (var k = 1; k <= n; k++)
        {
            var i = k - 1;
            var takeBase = prev[i] + 1;
            var takeCount = 1 + dpCount[takeBase];
            var skipCount = dpCount[k - 1];

            if (takeCount > skipCount)
            {
                dpCount[k] = takeCount;
                dpWays[k] = dpWays[takeBase];
            }
            else if (skipCount > takeCount)
            {
                dpCount[k] = skipCount;
                dpWays[k] = dpWays[k - 1];
            }
            else
            {
                dpCount[k] = takeCount;
                dpWays[k] = (byte)Math.Min(2, dpWays[takeBase] + dpWays[k - 1]);
            }
        }

        if (dpWays[n] != 1)
            return new NonOverlapSelection(dpCount[n], false, Array.Empty<Candidate>());

        var selected = new List<Candidate>(dpCount[n]);
        for (var k = n; k > 0;)
        {
            var i = k - 1;
            var takeBase = prev[i] + 1;
            var takeCount = 1 + dpCount[takeBase];
            var skipCount = dpCount[k - 1];
            if (takeCount > skipCount)
            {
                selected.Add(items[i]);
                k = takeBase;
            }
            else if (skipCount > takeCount)
            {
                k--;
            }
            else
            {
                // A tie would imply at least two optimal covers, contradicting dpWays[n] == 1.
                throw new InvalidDataException("Alpha 9b internal error: unique interval-cover reconstruction encountered an ambiguous tie.");
            }
        }
        selected.Reverse();
        return new NonOverlapSelection(dpCount[n], true, selected);
    }

    private static List<List<Candidate>> BuildMaximalContiguousChains(IReadOnlyList<Candidate> source)
    {
        var byOffset = source
            .GroupBy(c => c.Offset)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.End).First());
        var hasPredecessor = new HashSet<int>();
        foreach (var c in byOffset.Values)
        {
            if (byOffset.ContainsKey(c.End))
                hasPredecessor.Add(c.End);
        }

        var chains = new List<List<Candidate>>();
        foreach (var start in byOffset.Values.Where(c => !hasPredecessor.Contains(c.Offset)).OrderBy(c => c.Offset))
        {
            var chain = new List<Candidate>();
            var seen = new HashSet<int>();
            var current = start;
            while (seen.Add(current.Offset))
            {
                chain.Add(current);
                if (!byOffset.TryGetValue(current.End, out var next))
                    break;
                current = next;
            }
            chains.Add(chain);
        }

        // A cycle is impossible for valid candidates because End > Offset, but preserve
        // fail-closed diagnostics if malformed arithmetic ever violates that assumption.
        var coveredStarts = chains.SelectMany(c => c).Select(c => c.Offset).ToHashSet();
        foreach (var orphan in byOffset.Values.Where(c => !coveredStarts.Contains(c.Offset)).OrderBy(c => c.Offset))
            chains.Add(new List<Candidate> { orphan });

        return chains;
    }

    private static List<List<Candidate>> ClusterOverlaps(List<Candidate> source)
    {
        var sorted = source.OrderBy(c => c.Offset).ThenBy(c => c.End).ToList();
        var result = new List<List<Candidate>>();
        if (sorted.Count == 0) return result;

        var current = new List<Candidate> { sorted[0] };
        var maxEnd = sorted[0].End;
        for (var i = 1; i < sorted.Count; i++)
        {
            var item = sorted[i];
            if (item.Offset < maxEnd)
            {
                current.Add(item);
                if (item.End > maxEnd) maxEnd = item.End;
            }
            else
            {
                result.Add(current);
                current = new List<Candidate> { item };
                maxEnd = item.End;
            }
        }
        result.Add(current);
        return result;
    }

    private static bool IntervalsOverlap(int aStart, int aEnd, int bStart, int bEnd) => aStart < bEnd && bStart < aEnd;

    private static void VerifyStats(string name, TraverseStats a, TraverseStats b)
    {
        if (a.FxOwnedBytes != b.FxOwnedBytes ||
            a.ElemDefs != b.ElemDefs ||
            a.VelocitySamples != b.VelocitySamples ||
            a.VisualSamples != b.VisualSamples ||
            a.InlineStrings != b.InlineStrings ||
            a.MaterialVisuals != b.MaterialVisuals ||
            a.ModelVisuals != b.ModelVisuals ||
            a.SoundVisuals != b.SoundVisuals ||
            a.RunnerVisuals != b.RunnerVisuals ||
            a.DecalMarkArrays != b.DecalMarkArrays ||
            a.EffectRefNames != b.EffectRefNames ||
            a.TrailDefs != b.TrailDefs ||
            a.TrailVertices != b.TrailVertices ||
            a.TrailIndices != b.TrailIndices ||
            a.InlineMaterials != b.InlineMaterials ||
            a.InlineXModels != b.InlineXModels)
            throw new InvalidDataException($"FX '{name}' ownership counts changed during big-endian read-back.");
    }

    private static bool VerifyContainer(string path, IReadOnlyList<T4FxProofAsset> expected)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 16 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != 9) return false;
        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12, 4));
        if (count != (uint)expected.Count) return false;
        var cursor = 16;
        for (var i = 0; i < expected.Count; i++)
        {
            if (cursor + 12 > bytes.Length) return false;
            var sourceOffset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor, 4));
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor + 4, 4)));
            var nameLen = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor + 8, 4)));
            cursor += 12;
            if (nameLen < 0 || cursor + nameLen > bytes.Length) return false;
            var name = Encoding.UTF8.GetString(bytes, cursor, nameLen);
            cursor += nameLen;
            if (length <= 0 || cursor + length > bytes.Length) return false;
            if (sourceOffset != (uint)expected[i].SourceOffset || !name.Equals(expected[i].Name, StringComparison.Ordinal)) return false;

            var blob = bytes.AsSpan(cursor, length).ToArray();
            try
            {
                var ctx = new WalkContext
                {
                    Source = blob,
                    Endianness = Endian.Big,
                    Header = null,
                    Output = null,
                    Stats = new TraverseStats(),
                    EffectName = name
                };
                if (WalkEffect(ctx, 0) != blob.Length) return false;
            }
            catch
            {
                return false;
            }
            cursor += length;
        }
        return cursor == bytes.Length;
    }

    private static int ReadI32(byte[] data, int offset, Endian endian) => unchecked((int)ReadU32(data, offset, endian));

    private static uint ReadU32(byte[] data, int offset, Endian endian)
    {
        Ensure(data, offset, 4, "uint32");
        return EndianIO.ReadUInt32(data.AsSpan(offset, 4), endian);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }

    private static void Ensure(byte[] data, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException($"{label} at 0x{offset:X} ({length:N0} bytes) exceeds the serialized stream ({data.Length:N0} bytes).");
    }
}
