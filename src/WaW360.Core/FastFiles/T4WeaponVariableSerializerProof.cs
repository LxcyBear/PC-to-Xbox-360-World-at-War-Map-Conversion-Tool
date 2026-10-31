using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4WeaponVariableProofAsset(
    string Name,
    long SourceOffset,
    int SourceSerializedBytes,
    int XboxSerializedBytes,
    int InlineStrings,
    int SoundNameRecords,
    int BounceSoundTables,
    int AccuracyGraphArrays,
    int AccuracyGraphKnots,
    int FlameTables,
    int InlineXModels,
    int InlineMaterials);

public sealed class T4WeaponVariableProofResult
{
    public required string SourceFastFile { get; init; }
    public required string OutputFile { get; init; }
    public required int DeclaredWeaponCount { get; init; }
    public required int CoveredWeaponCount { get; init; }
    public required int SerializedBodyCount { get; init; }
    public required int AliasReferenceCount { get; init; }
    public required int FixedBodyBytes { get; init; }
    public required IReadOnlyList<T4WeaponVariableProofAsset> Assets { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool Verified { get; init; }
}

/// <summary>
/// Stage 4 alpha 7b: WeaponDef owned-payload serializer proof.
///
/// Alpha 7b deliberately reuses the proven Alpha 6f body discovery/coverage pass,
/// then walks each physical WeaponDef in T4 serialized ownership order. It emits:
/// - fixed WeaponDef body (Xbox big-endian),
/// - inline XStrings,
/// - reusable SndAliasCustom name records,
/// - the 31-entry bounceSound table,
/// - inline XModel/Material dependencies through the already-proven Alpha 5 walker,
/// - accuracy graph knot arrays in the T4 ZoneCode reorder,
/// - optional FlameTable bodies plus their strings/material/sound-name payloads.
///
/// Inline FxEffectDef dependencies are still fail-closed because the typed FX
/// serializer is not proven yet. This is an INTERNAL proof container, not a game FF.
/// </summary>
public static class T4WeaponVariableSerializerProof
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("W360A7W\0");
    private const uint Following = 0xFFFFFFFFu;
    private const uint Insert = 0xFFFFFFFEu;
    private const int FixedBodySize = 0x9AC;
    private const int FlameTableFixedSize = 476; // 106 floats + name + 8 Material* + 4 SndAliasCustom
    private const int BounceSoundCount = 31;

    private const int InternalName = 0;
    private const int DisplayName = 4;
    private const int OverlayName = 8;
    private const int GunModels = 12;       // 16 XModel*
    private const int HandModel = 76;
    private const int XAnims = 80;          // 35 XString/XAnim refs
    private const int ModeName = 220;

    private sealed record PlannedInsertion(int Offset, byte[] Bytes);

    internal sealed record OwnedFixedInlineXString(int FixedFieldOffset, string ProofLabel, string SourceValue, int SourceBodyByteOffset);
    // This is original-PC-sourced Xbox big-endian 7b proof data, NOT a native
    // FF relocation: no packed/external WeaponDef pointer may be carried out.
    internal sealed record OwnedFixedInlineAccuracyVec2(
        int FixedFieldOffset,string ProofLabel,int KnotCount,int SourceBodyByteOffset,byte[] BigEndianKnotBytes);
    // 31 SndAliasCustom pointer cells, exactly as serialized by the original
    // PC-owned Alpha 7b stream. These are POINTERS, not Xbox sound assets.
    internal sealed record OwnedInlineBounceSoundTable(
        int FixedFieldOffset,int SourceBodyByteOffset,byte[] BigEndianPointerBytes);
    internal sealed record OwnedInlineSndAliasName(
        string ProofLabel,int SourceRecordByteOffset,uint RawStringPointer,
        int? SourceInlineNameByteOffset,byte[]? InlineNameBytes);
    // FlameTable parent: 476 genuine big-endian serialized bytes. Pointer
    // cells at +424/428..456/460..472 are NOT native relocation values.
    internal sealed record OwnedInlineFlameTable(
        string ProofLabel,int FixedFieldOffset,int SourceBodyByteOffset,
        byte[] BigEndianFixedBytes);

    private sealed class TraverseStats
    {
        public int InlineStrings;
        public int LocalizedIwdStrings;
        public int SoundNameRecords;
        public int BounceSoundTables;
        public int AccuracyGraphArrays;
        public int AccuracyGraphKnots;
        public int FlameTables;
        public int InlineXModels;
        public int InlineMaterials;
    }

    private sealed class WalkContext
    {
        public required byte[] Source { get; init; }
        public required Endian Endianness { get; init; }
        public ZoneHeaderSummary? Header { get; init; }
        public Stream? Output { get; init; }
        public required TraverseStats Stats { get; init; }
        public required string WeaponName { get; init; }
        public bool RequireSelfContainedPointers { get; init; }
        public List<OwnedFixedInlineXString>? ObservedFixedInlineXStrings { get; init; }
        public List<OwnedFixedInlineAccuracyVec2>? ObservedFixedInlineAccuracyArrays { get; init; }
        public List<OwnedInlineBounceSoundTable>? ObservedBounceSoundTables { get; init; }
        public List<OwnedInlineSndAliasName>? ObservedSoundAliasNames { get; init; }
        public List<OwnedInlineFlameTable>? ObservedFlameTables { get; init; }
        public byte[]? PatchDestination { get; init; }
        public IReadOnlyDictionary<string, string>? StringOverrides { get; init; }
        public List<PlannedInsertion>? PlannedInsertions { get; init; }
        public bool PlanIwdStrings { get; init; }
    }

    public static Task<T4WeaponVariableProofResult> BuildProofAsync(
        string sourceFastFile,
        string outputFile,
        CancellationToken cancellationToken = default) =>
        BuildProofAsync(sourceFastFile, outputFile, existingFixedProofFile: null, cancellationToken: cancellationToken);

    public static async Task<T4WeaponVariableProofResult> BuildProofAsync(
        string sourceFastFile,
        string outputFile,
        string? existingFixedProofFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFastFile))
            throw new FileNotFoundException("Fastfile not found.", sourceFastFile);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "weapon_a7", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "zone.bin");
        var fixedProofPath = Path.Combine(tempDir, "alpha6f_fixed.bin");

        try
        {
            var cachedZone = await FastFileAnalysisCache.GetZoneAsync(sourceFastFile, cancellationToken);
            if (cachedZone.Platform != WaWPlatform.PC || cachedZone.ZoneHeader.Endianness != Endian.Little)
                throw new InvalidDataException("Alpha 7b Weapon proof expects a PC/little-endian WaW source fastfile.");

            var zone = cachedZone.Bytes;
            var document = cachedZone.Document;

            // Alpha 7b MUST inherit the exact body set proven by Alpha 6f. Alpha 9h
            // can reuse the immediately preceding fixed proof, but ONLY when the
            // proof schema and compressed-source SHA-256 match exactly. Otherwise
            // fall back to a fresh Alpha 6f build with no change in correctness.
            var weaponEntries = document.Assets
                .Where(a => a.TypeName.Equals("weapon", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var declaredWeaponCount = weaponEntries.Count;
            var aliasReferenceCount = weaponEntries.Count(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias);

            T4WeaponProofResult? fixedProof = null;
            if (!string.IsNullOrWhiteSpace(existingFixedProofFile))
            {
                fixedProof = await T4WeaponSerializerProof.TryLoadVerifiedProofAsync(
                    sourceFastFile, existingFixedProofFile, declaredWeaponCount, aliasReferenceCount, cancellationToken);
            }

            fixedProof ??= await T4WeaponSerializerProof.BuildProofAsync(
                sourceFastFile, fixedProofPath, cancellationToken);
            if (!fixedProof.Verified || fixedProof.ResolvedWeaponCount != fixedProof.DeclaredWeaponCount)
                throw new InvalidDataException("Alpha 7b prerequisite failed: Alpha 6f did not fully cover the Weapon XAsset table.");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
            var assets = new List<T4WeaponVariableProofAsset>(fixedProof.Assets.Count);

            await using (var fs = File.Create(outputFile))
            {
                await fs.WriteAsync(Magic, cancellationToken);
                WriteU32(fs, 8);
                WriteU32(fs, checked((uint)fixedProof.Assets.Count));
                WriteU32(fs, checked((uint)FixedBodySize));

                foreach (var asset in fixedProof.Assets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var start = checked((int)asset.SourceOffset);
                    Ensure(zone, start, FixedBodySize, $"Weapon '{asset.Name}' fixed body");

                    using var blobStream = new MemoryStream();
                    var fixedSource = zone.AsSpan(start, FixedBodySize).ToArray();
                    var fixedXbox = TransformFixedBodyToBigEndian(fixedSource);
                    await blobStream.WriteAsync(fixedXbox, cancellationToken);

                    var stats = new TraverseStats();
                    var ctx = new WalkContext
                    {
                        Source = zone,
                        Endianness = Endian.Little,
                        Header = document.Header,
                        Output = blobStream,
                        Stats = stats,
                        WeaponName = asset.Name
                    };
                    var end = WalkOwnedPayload(ctx, start);
                    var blob = blobStream.ToArray();
                    var expectedLength = checked(end - start);
                    if (blob.Length != expectedLength)
                        throw new InvalidDataException(
                            $"Weapon '{asset.Name}' changed serialized byte length during Alpha 7b conversion ({expectedLength:N0} -> {blob.Length:N0}).");

                    // Independent big-endian traversal of the exact serialized byte stream.
                    // Destination-memory alignment never consumes bytes from this stream.
                    var verifyStats = new TraverseStats();
                    var verifyCtx = new WalkContext
                    {
                        Source = blob,
                        Endianness = Endian.Big,
                        Header = null,
                        Output = null,
                        Stats = verifyStats,
                        WeaponName = asset.Name
                    };
                    var verifyEnd = WalkOwnedPayload(verifyCtx, 0);
                    if (verifyEnd != blob.Length)
                        throw new InvalidDataException(
                            $"Weapon '{asset.Name}' big-endian owned-payload read-back ended at 0x{verifyEnd:X}, expected 0x{blob.Length:X}.");
                    VerifyStats(asset.Name, stats, verifyStats);

                    var nameBytes = Encoding.UTF8.GetBytes(asset.Name);
                    WriteU32(fs, checked((uint)start));
                    WriteU32(fs, checked((uint)blob.Length));
                    WriteU32(fs, checked((uint)nameBytes.Length));
                    await fs.WriteAsync(nameBytes, cancellationToken);
                    await fs.WriteAsync(blob, cancellationToken);

                    assets.Add(new T4WeaponVariableProofAsset(
                        asset.Name,
                        asset.SourceOffset,
                        expectedLength,
                        blob.Length,
                        stats.InlineStrings,
                        stats.SoundNameRecords,
                        stats.BounceSoundTables,
                        stats.AccuracyGraphArrays,
                        stats.AccuracyGraphKnots,
                        stats.FlameTables,
                        stats.InlineXModels,
                        stats.InlineMaterials));
                }
            }

            if (!VerifyContainer(outputFile, assets))
                throw new InvalidDataException("Stage 4 alpha 7b Weapon owned-payload proof container failed read-back verification.");

            var warnings = fixedProof.Warnings.ToList();
            warnings.Add("Alpha 7b reuses Alpha 6f physical-body selection verbatim; no new WeaponDef body discovery is performed.");

            return new T4WeaponVariableProofResult
            {
                SourceFastFile = sourceFastFile,
                OutputFile = outputFile,
                DeclaredWeaponCount = fixedProof.DeclaredWeaponCount,
                CoveredWeaponCount = fixedProof.ResolvedWeaponCount,
                SerializedBodyCount = assets.Count,
                AliasReferenceCount = fixedProof.AliasReferenceCount,
                FixedBodyBytes = FixedBodySize,
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

    /// <summary>
    /// Validates the ORIGINAL source-owned endian-converted Xbox 7b stream
    /// with the exact source-aware traversal used by BuildProofAsync.
    /// Existing packed PC Virtual/asset pointers are acceptable EVIDENCE
    /// here but must NOT be copied into a runtime FF. For an emitted FF
    /// canary, validate its separately sanitized blob with
    /// TryValidateSelfContainedXboxBlob (which rejects all packed pointers).
    /// </summary>
    internal static bool TryValidateSourceOwnedXboxBlob(byte[] blob, string name, out string reason)
    {
        reason = string.Empty;
        try
        {
            var ctx = new WalkContext
            {
                Source = blob,
                Endianness = Endian.Big,
                Header = null,
                Output = null,
                Stats = new TraverseStats(),
                WeaponName = name,
                RequireSelfContainedPointers = false
            };
            var end = WalkOwnedPayload(ctx, 0);
            if (end != blob.Length)
                throw new InvalidDataException(
                    $"typed source-owned Xbox walk ended at 0x{end:X}, expected 0x{blob.Length:X}.");
            return true;
        }
        catch (Exception ex) when(ex is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Reuse the exact Alpha 7b big-endian T4 owned graph traversal to identify
    /// inline XString values whose pointer lives DIRECTLY in the fixed
    /// WeaponDef body. Packed source pointers and nested foreign assets are
    /// intentionally NOT promoted; this is forensic source content only.
    /// The caller must independently rebase/validate a fresh Xbox FF body.
    /// </summary>
    internal static bool TryCollectSourceOwnedFixedInlineXStrings(
        byte[] blob, string name,
        out IReadOnlyList<OwnedFixedInlineXString> strings,
        out string reason)
    {
        var observed = new List<OwnedFixedInlineXString>();
        strings = Array.Empty<OwnedFixedInlineXString>();
        reason = string.Empty;
        try
        {
            var ctx = new WalkContext
            {
                Source = blob,
                Endianness = Endian.Big,
                Header = null,
                Output = null,
                Stats = new TraverseStats(),
                WeaponName = name,
                RequireSelfContainedPointers = false,
                ObservedFixedInlineXStrings = observed
            };
            var end = WalkOwnedPayload(ctx, 0);
            if(end != blob.Length)
                throw new InvalidDataException(
                    $"original-PC source inline XString walk ended at 0x{end:X}, expected 0x{blob.Length:X}");
            if(observed.Any(s => s.FixedFieldOffset < 0 || s.FixedFieldOffset >= FixedBodySize ||
                                  (s.FixedFieldOffset & 3) != 0) ||
               observed.Select(s => s.FixedFieldOffset).Distinct().Count() != observed.Count)
                throw new InvalidDataException(
                    "original-PC source owned fixed XString offset duplicated, unaligned or out of bounds.");
            strings = observed.AsReadOnly();
            return true;
        }
        catch(Exception ex) when(ex is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            reason = ex.Message;
            strings = Array.Empty<OwnedFixedInlineXString>();
            return false;
        }
    }

    /// <summary>
    /// Read-only ORIGINAL source-owned 7b WeaponDef accuracy-graph array
    /// witness. Uses the exact Alpha 7b owned child traversal, not a
    /// fixed-offset guess into the variable-length owned payload stream.
    /// Returns only genuine inline original-PC 2-float/knot data; all
    /// non-inline/packed references remain explicitly unresolved.
    /// </summary>
    internal static bool TryCollectSourceOwnedAccuracyVec2Arrays(
        byte[] xboxSourceProofBlob,string name,
        out IReadOnlyList<OwnedFixedInlineAccuracyVec2> arrays,out string reason)
    {
        var observed = new List<OwnedFixedInlineAccuracyVec2>();
        arrays = Array.Empty<OwnedFixedInlineAccuracyVec2>();
        reason = string.Empty;
        try
        {
            var ctx = new WalkContext
            {
                Source = xboxSourceProofBlob,
                Endianness = Endian.Big,
                Header = null,
                Output = null,
                Stats = new TraverseStats(),
                WeaponName = name,
                RequireSelfContainedPointers = false,
                ObservedFixedInlineAccuracyArrays = observed
            };
            var end = WalkOwnedPayload(ctx,0);
            if(end != xboxSourceProofBlob.Length)
                throw new InvalidDataException(
                    $"source owned WeaponDef accuracy walk ended at 0x{end:X}, expected 0x{xboxSourceProofBlob.Length:X}");
            int[] expectedSlots = { 2184,2192,2188,2196 };
            if(observed.Any(a => !expectedSlots.Contains(a.FixedFieldOffset) ||
                                 a.KnotCount <= 0 || a.KnotCount > 4096 ||
                                 a.BigEndianKnotBytes.Length != checked(a.KnotCount*8) ||
                                 a.SourceBodyByteOffset < FixedBodySize ||
                                 (long)a.SourceBodyByteOffset+a.BigEndianKnotBytes.Length>xboxSourceProofBlob.Length) ||
               observed.Select(a => a.FixedFieldOffset).Distinct().Count() != observed.Count)
                throw new InvalidDataException(
                    $"source WeaponDef '{name}' accuracy array witness has invalid/duplicate slot, count or owned byte bounds.");
            arrays=observed.AsReadOnly();
            return true;
        }
        catch(Exception ex) when(ex is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            reason=ex.Message;
            arrays=Array.Empty<OwnedFixedInlineAccuracyVec2>();
            return false;
        }
    }

    /// <summary>
    /// Source-only forensic witness for T4 WeaponDef bounceSound at fixed
    /// pointer slot 624, and independently source-owned inline SndAliasCustom
    /// name records. Never treats 31 pointer words as resolved Xbox sounds.
    /// </summary>
    internal static bool TryCollectSourceOwnedBounceSound(
        byte[] xboxSourceProofBlob,string name,
        out IReadOnlyList<OwnedInlineBounceSoundTable> tables,
        out IReadOnlyList<OwnedInlineSndAliasName> aliases,
        out string reason)
    {
        var observedTables=new List<OwnedInlineBounceSoundTable>();
        var observedAliases=new List<OwnedInlineSndAliasName>();
        tables=Array.Empty<OwnedInlineBounceSoundTable>();
        aliases=Array.Empty<OwnedInlineSndAliasName>();
        reason=string.Empty;
        try
        {
            var ctx=new WalkContext
            {
                Source=xboxSourceProofBlob,
                Endianness=Endian.Big,
                Header=null,
                Output=null,
                Stats=new TraverseStats(),
                WeaponName=name,
                RequireSelfContainedPointers=false,
                ObservedBounceSoundTables=observedTables,
                ObservedSoundAliasNames=observedAliases
            };
            var end=WalkOwnedPayload(ctx,0);
            if(end!=xboxSourceProofBlob.Length)
                throw new InvalidDataException(
                    $"Weapon '{name}' bounce source stream ended at 0x{end:X}, expected 0x{xboxSourceProofBlob.Length:X}");
            if(observedTables.Count>1 ||
               observedTables.Any(t=>t.FixedFieldOffset!=624 ||
                   t.SourceBodyByteOffset<FixedBodySize ||
                   (long)t.SourceBodyByteOffset+BounceSoundCount*4>xboxSourceProofBlob.Length ||
                   t.BigEndianPointerBytes.Length!=BounceSoundCount*4) ||
               observedAliases.Any(a=>a.SourceRecordByteOffset<FixedBodySize ||
                   (long)a.SourceRecordByteOffset+4>xboxSourceProofBlob.Length ||
                   (a.InlineNameBytes is not null &&
                      (a.SourceInlineNameByteOffset is null ||
                       a.SourceInlineNameByteOffset.Value<FixedBodySize ||
                       (long)a.SourceInlineNameByteOffset.Value+a.InlineNameBytes.Length>xboxSourceProofBlob.Length))))
                throw new InvalidDataException(
                    $"Weapon '{name}' bounce/sound-name owned offsets out of source blob bounds.");
            tables=observedTables.AsReadOnly();
            aliases=observedAliases.AsReadOnly();
            return true;
        }
        catch(Exception ex) when(ex is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            reason=ex.Message;
            tables=Array.Empty<OwnedInlineBounceSoundTable>();
            aliases=Array.Empty<OwnedInlineSndAliasName>();
            return false;
        }
    }

    // Read-only proof of the actual two optional FlameTable parent bodies in
    // original PC-derived endian-correct 7b stream, independent of the six
    // nested SndAliasCustom records. Not a native Xbox FF graft.
    internal static bool TryCollectSourceOwnedFlameTables(
        byte[] xboxSourceProofBlob,string name,
        out IReadOnlyList<OwnedInlineFlameTable> tables,
        out IReadOnlyList<OwnedInlineSndAliasName> aliases,
        out string reason)
    {
        var observedTables=new List<OwnedInlineFlameTable>();
        var observedAliases=new List<OwnedInlineSndAliasName>();
        tables=Array.Empty<OwnedInlineFlameTable>();
        aliases=Array.Empty<OwnedInlineSndAliasName>();
        reason=string.Empty;
        try
        {
            var ctx=new WalkContext
            {
                Source=xboxSourceProofBlob,
                Endianness=Endian.Big,
                Header=null,
                Output=null,
                Stats=new TraverseStats(),
                WeaponName=name,
                RequireSelfContainedPointers=false,
                ObservedFlameTables=observedTables,
                ObservedSoundAliasNames=observedAliases
            };
            var end=WalkOwnedPayload(ctx,0);
            if(end!=xboxSourceProofBlob.Length)
                throw new InvalidDataException(
                    $"Weapon '{name}' 7b FlameTable source ended 0x{end:X}, expected 0x{xboxSourceProofBlob.Length:X}.");
            if(observedTables.Count>2 ||
               observedTables.Select(t=>t.FixedFieldOffset).Distinct().Count()!=observedTables.Count ||
               observedTables.Any(t=>t.FixedFieldOffset is not (2460 or 2464) ||
                   t.SourceBodyByteOffset<FixedBodySize ||
                   (long)t.SourceBodyByteOffset+FlameTableFixedSize>xboxSourceProofBlob.Length ||
                   t.BigEndianFixedBytes.Length!=FlameTableFixedSize ||
                   !xboxSourceProofBlob.AsSpan(t.SourceBodyByteOffset,FlameTableFixedSize)
                       .SequenceEqual(t.BigEndianFixedBytes)))
                throw new InvalidDataException(
                    $"Weapon '{name}' original-PC FlameTable fixed-body ownership or offset invalid.");
            tables=observedTables.AsReadOnly();
            aliases=observedAliases.AsReadOnly();
            return true;
        }
        catch(Exception ex) when(ex is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            reason=ex.Message;
            return false;
        }
    }

    internal static bool TryValidateSelfContainedXboxBlob(byte[] blob, string name, out string reason)
    {
        reason = string.Empty;
        try
        {
            var ctx = new WalkContext
            {
                Source = blob,
                Endianness = Endian.Big,
                Header = null,
                Output = null,
                Stats = new TraverseStats(),
                WeaponName = name,
                RequireSelfContainedPointers = true
            };
            var end = WalkOwnedPayload(ctx, 0);
            if (end != blob.Length)
                throw new InvalidDataException($"typed self-contained walk ended at 0x{end:X}, expected 0x{blob.Length:X}.");
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            reason = ex.Message;
            return false;
        }
    }

    internal static bool TryBuildStructuralXboxBlobWithIwdStrings(
        byte[] blob,
        string name,
        IReadOnlyDictionary<string, string> stringOverrides,
        out byte[] rebuilt,
        out int localizedStrings,
        out string reason)
    {
        rebuilt = Array.Empty<byte>();
        localizedStrings = 0;
        reason = string.Empty;
        try
        {
            if (blob.Length < FixedBodySize)
                throw new InvalidDataException($"Weapon '{name}' proof blob is shorter than the {FixedBodySize}-byte fixed body.");

            var patched = blob.ToArray();
            var insertions = new List<PlannedInsertion>();
            var stats = new TraverseStats();
            var ctx = new WalkContext
            {
                Source = blob,
                Endianness = Endian.Big,
                Header = null,
                Output = null,
                Stats = stats,
                WeaponName = name,
                PatchDestination = patched,
                StringOverrides = stringOverrides,
                PlannedInsertions = insertions,
                PlanIwdStrings = true
            };

            var end = WalkOwnedPayload(ctx, 0);
            if (end != blob.Length)
                throw new InvalidDataException($"IWD-string planning walk ended at 0x{end:X}, expected 0x{blob.Length:X}.");
            if (stats.LocalizedIwdStrings == 0)
                throw new InvalidDataException("Weapon has no packed direct XString field covered by its authoritative IWD weapon file.");

            rebuilt = ApplyInsertions(patched, insertions);
            localizedStrings = stats.LocalizedIwdStrings;
            if (!TryValidateSelfContainedXboxBlob(rebuilt, name, out reason))
                return false;

            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            reason = ex.Message;
            rebuilt = Array.Empty<byte>();
            localizedStrings = 0;
            return false;
        }
    }

    private static int WalkOwnedPayload(WalkContext c, int start)
    {
        Ensure(c.Source, start, FixedBodySize, $"Weapon '{c.WeaponName}' fixed body");
        var body = start;
        var cursor = checked(start + FixedBodySize);

        // The fixed body itself was written by the caller. During read-back, just
        // validate the most important endian-sensitive invariants.
        ValidateFixedBody(c, body);

        // WeaponDef ZoneCode order begins with its strings and early model refs.
        cursor = ConsumeWeaponXString(c, body + InternalName, cursor, "szInternalName");
        cursor = ConsumeWeaponXString(c, body + DisplayName, cursor, "szDisplayName");
        cursor = ConsumeWeaponXString(c, body + OverlayName, cursor, "szOverlayName");

        for (var i = 0; i < 16; i++)
            cursor = ConsumeXModelRef(c, ReadU32(c.Source, body + GunModels + i * 4, c.Endianness), cursor, $"gunXModel[{i}]");
        cursor = ConsumeXModelRef(c, ReadU32(c.Source, body + HandModel, c.Endianness), cursor, "handXModel");

        for (var i = 0; i < 35; i++)
            cursor = ConsumeWeaponXString(c, body + XAnims + i * 4, cursor, $"szXAnims[{i}]");
        cursor = ConsumeWeaponXString(c, body + ModeName, cursor, "szModeName");

        // viewFlashEffect/worldFlashEffect are asset refs, not Weapon-owned data.
        cursor = ConsumeUnsupportedFxRef(c, ReadU32(c.Source, body + 380, c.Endianness), cursor, "viewFlashEffect");
        cursor = ConsumeUnsupportedFxRef(c, ReadU32(c.Source, body + 384, c.Endianness), cursor, "worldFlashEffect");

        // 59 SndAliasCustom fields: pickupSound .. overheatSoundPlayer.
        for (var o = 388; o <= 620; o += 4)
            cursor = ConsumeSndAliasCustom(c, ReadU32(c.Source, body + o, c.Endianness), cursor, $"sound@0x{o:X}");

        // bounceSound is a reusable array of exactly 31 SndAliasCustom entries in T4.
        cursor = ConsumeBounceSoundTable(c, ReadU32(c.Source, body + 624, c.Endianness), cursor);

        cursor = ConsumeWeaponXString(c, body + 628, cursor, "standMountedWeapdef");
        cursor = ConsumeWeaponXString(c, body + 632, cursor, "crouchMountedWeapdef");
        cursor = ConsumeWeaponXString(c, body + 636, cursor, "proneMountedWeapdef");

        foreach (var (offset, label) in new[]
        {
            (652, "viewShellEjectEffect"), (656, "worldShellEjectEffect"),
            (660, "viewLastShotEjectEffect"), (664, "worldLastShotEjectEffect")
        })
            cursor = ConsumeUnsupportedFxRef(c, ReadU32(c.Source, body + offset, c.Endianness), cursor, label);

        cursor = ConsumeMaterialRef(c, ReadU32(c.Source, body + 668, c.Endianness), cursor, "reticleCenter");
        cursor = ConsumeMaterialRef(c, ReadU32(c.Source, body + 672, c.Endianness), cursor, "reticleSide");

        // worldModel[16], worldClipModel, rocketModel, knifeModel,
        // worldKnifeModel, mountedModel.
        for (var i = 0; i < 16; i++)
            cursor = ConsumeXModelRef(c, ReadU32(c.Source, body + 900 + i * 4, c.Endianness), cursor, $"worldModel[{i}]");
        cursor = ConsumeXModelRef(c, ReadU32(c.Source, body + 964, c.Endianness), cursor, "worldClipModel");
        cursor = ConsumeXModelRef(c, ReadU32(c.Source, body + 968, c.Endianness), cursor, "rocketModel");
        cursor = ConsumeXModelRef(c, ReadU32(c.Source, body + 972, c.Endianness), cursor, "knifeModel");
        cursor = ConsumeXModelRef(c, ReadU32(c.Source, body + 976, c.Endianness), cursor, "worldKnifeModel");
        cursor = ConsumeXModelRef(c, ReadU32(c.Source, body + 980, c.Endianness), cursor, "mountedModel");

        cursor = ConsumeMaterialRef(c, ReadU32(c.Source, body + 984, c.Endianness), cursor, "hudIcon");
        cursor = ConsumeMaterialRef(c, ReadU32(c.Source, body + 992, c.Endianness), cursor, "ammoCounterIcon");

        cursor = ConsumeWeaponXString(c, body + 1008, cursor, "szAmmoName");
        cursor = ConsumeWeaponXString(c, body + 1016, cursor, "szClipName");
        cursor = ConsumeWeaponXString(c, body + 1040, cursor, "szSharedAmmoCapName");

        cursor = ConsumeMaterialRef(c, ReadU32(c.Source, body + 1308, c.Endianness), cursor, "overlayMaterial");
        cursor = ConsumeMaterialRef(c, ReadU32(c.Source, body + 1312, c.Endianness), cursor, "overlayMaterialLowRes");
        cursor = ConsumeMaterialRef(c, ReadU32(c.Source, body + 1552, c.Endianness), cursor, "killIcon");
        cursor = ConsumeMaterialRef(c, ReadU32(c.Source, body + 1564, c.Endianness), cursor, "dpadIcon");

        cursor = ConsumeWeaponXString(c, body + 1592, cursor, "szAltWeaponName");
        cursor = ConsumeXModelRef(c, ReadU32(c.Source, body + 1664, c.Endianness), cursor, "projectileModel");
        cursor = ConsumeUnsupportedFxRef(c, ReadU32(c.Source, body + 1672, c.Endianness), cursor, "projExplosionEffect");
        cursor = ConsumeUnsupportedFxRef(c, ReadU32(c.Source, body + 1680, c.Endianness), cursor, "projDudEffect");

        cursor = ConsumeSndAliasCustom(c, ReadU32(c.Source, body + 1684, c.Endianness), cursor, "projExplosionSound");
        cursor = ConsumeSndAliasCustom(c, ReadU32(c.Source, body + 1688, c.Endianness), cursor, "projDudSound");
        cursor = ConsumeSndAliasCustom(c, ReadU32(c.Source, body + 1692, c.Endianness), cursor, "mortarShellSound");
        cursor = ConsumeSndAliasCustom(c, ReadU32(c.Source, body + 1696, c.Endianness), cursor, "tankShellSound");

        cursor = ConsumeUnsupportedFxRef(c, ReadU32(c.Source, body + 1976, c.Endianness), cursor, "projTrailEffect");
        cursor = ConsumeUnsupportedFxRef(c, ReadU32(c.Source, body + 2008, c.Endianness), cursor, "projIgnitionEffect");
        cursor = ConsumeSndAliasCustom(c, ReadU32(c.Source, body + 2012, c.Endianness), cursor, "projIgnitionSound");

        // OAT T4 ZoneCode explicitly reorders the accuracy graph block:
        // ai-vs-ai name, knots, original knots, then ai-vs-player equivalents.
        var aiCount = ReadI32(c.Source, body + 2200, c.Endianness);
        var playerCount = ReadI32(c.Source, body + 2204, c.Endianness);
        if (aiCount is < 0 or > 4096 || playerCount is < 0 or > 4096)
            throw new InvalidDataException($"Weapon '{c.WeaponName}' has implausible accuracy graph knot counts ({aiCount}, {playerCount}).");

        cursor = ConsumeWeaponXString(c, body + 2176, cursor, "aiVsAiAccuracyGraphName");
        cursor = ConsumeVec2Array(c, ReadU32(c.Source, body + 2184, c.Endianness), aiCount, cursor, "aiVsAiAccuracyGraphKnots", 2184);
        cursor = ConsumeVec2Array(c, ReadU32(c.Source, body + 2192, c.Endianness), aiCount, cursor, "originalAiVsAiAccuracyGraphKnots", 2192);
        cursor = ConsumeWeaponXString(c, body + 2180, cursor, "aiVsPlayerAccuracyGraphName");
        cursor = ConsumeVec2Array(c, ReadU32(c.Source, body + 2188, c.Endianness), playerCount, cursor, "aiVsPlayerAccuracyGraphKnots", 2188);
        cursor = ConsumeVec2Array(c, ReadU32(c.Source, body + 2196, c.Endianness), playerCount, cursor, "originalAiVsPlayerAccuracyGraphKnots", 2196);

        cursor = ConsumeWeaponXString(c, body + 2288, cursor, "szUseHintString");
        cursor = ConsumeWeaponXString(c, body + 2292, cursor, "dropHintString");
        cursor = ConsumeWeaponXString(c, body + 2312, cursor, "szScript");
        cursor = ConsumeWeaponXString(c, body + 2428, cursor, "fireRumble");
        cursor = ConsumeWeaponXString(c, body + 2432, cursor, "meleeImpactRumble");
        cursor = ConsumeWeaponXString(c, body + 2452, cursor, "flameTableFirstPerson");
        cursor = ConsumeWeaponXString(c, body + 2456, cursor, "flameTableThirdPerson");

        cursor = ConsumeFlameTable(c, ReadU32(c.Source, body + 2460, c.Endianness), cursor, "flameTableFirstPersonPtr");
        cursor = ConsumeFlameTable(c, ReadU32(c.Source, body + 2464, c.Endianness), cursor, "flameTableThirdPersonPtr");

        cursor = ConsumeUnsupportedFxRef(c, ReadU32(c.Source, body + 2468, c.Endianness), cursor, "tagFx_preparationEffect");
        cursor = ConsumeUnsupportedFxRef(c, ReadU32(c.Source, body + 2472, c.Endianness), cursor, "tagFlash_preparationEffect");
        return cursor;
    }

    private static int ConsumeWeaponXString(WalkContext c, int pointerOffset, int cursor, string label)
    {
        var raw = ReadU32(c.Source, pointerOffset, c.Endianness);
        if(c.ObservedFixedInlineXStrings is not null &&
           pointerOffset >= 0 && pointerOffset < FixedBodySize && IsInline(raw))
        {
            var next = FindCStringEnd(c.Source, cursor, label);
            var length = next - cursor - 1;
            if(length < 0 || length > 512)
                throw new InvalidDataException(
                    $"Weapon '{c.WeaponName}' original PC fixed XString '{label}' implausible length={length}.");
            var sourceValue = Encoding.Latin1.GetString(c.Source, cursor, length);
            c.ObservedFixedInlineXStrings.Add(
                new OwnedFixedInlineXString(pointerOffset,label,sourceValue,cursor));
        }
        if (c.PlanIwdStrings &&
            raw != 0 &&
            !IsInline(raw) &&
            c.StringOverrides is not null &&
            c.StringOverrides.TryGetValue(label, out var value))
        {
            // The original packed source pointer remains valid evidence, but the destination must
            // own its own XString. Patch only the rebuilt copy and insert the source-authoritative
            // IWD value exactly where T4 ZoneCode would serialize an inline XString.
            ValidateStringPointer(c, raw, label);
            if (c.PatchDestination is null || c.PlannedInsertions is null)
                throw new InvalidDataException($"Weapon '{c.WeaponName}' IWD-string planner is missing its patch buffers.");
            BinaryPrimitives.WriteUInt32BigEndian(c.PatchDestination.AsSpan(pointerOffset, 4), Following);
            var encoded = Encoding.Latin1.GetBytes(value);
            var bytes = new byte[encoded.Length + 1];
            encoded.CopyTo(bytes, 0);
            c.PlannedInsertions.Add(new PlannedInsertion(cursor, bytes));
            c.Stats.LocalizedIwdStrings++;
            return cursor;
        }

        return ConsumeXString(c, raw, cursor, label);
    }

    private static int ConsumeXString(WalkContext c, uint raw, int cursor, string label)
    {
        ValidateStringPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;

        var end = FindCStringEnd(c.Source, cursor, label);
        if (c.Output is not null)
            c.Output.Write(c.Source, cursor, end - cursor);
        c.Stats.InlineStrings++;
        return end;
    }

    private static int ConsumeSndAliasCustom(WalkContext c, uint raw, int cursor, string label)
    {
        ValidateGeneralPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;

        cursor = Align4(c, cursor, label + ".name");
        Ensure(c.Source, cursor, 4, label + ".snd_alias_list_name");
        var soundNamePtr = ReadU32(c.Source, cursor, c.Endianness);
        ValidateStringPointer(c, soundNamePtr, label + ".soundName");
        WriteScalar32(c, cursor, soundNamePtr);
        cursor += 4;
        c.Stats.SoundNameRecords++;
        if(c.ObservedSoundAliasNames is not null)
        {
            var nameOffset=IsInline(soundNamePtr)?cursor:(int?)null;
            byte[]? nameBytes=null;
            if(nameOffset is not null)
            {
                var end=FindCStringEnd(c.Source,cursor,label+".soundName");
                nameBytes=c.Source.AsSpan(cursor,end-cursor).ToArray();
            }
            c.ObservedSoundAliasNames.Add(new OwnedInlineSndAliasName(
                label,cursor-4,soundNamePtr,nameOffset,nameBytes));
        }
        return ConsumeXString(c, soundNamePtr, cursor, label + ".soundName");
    }

    private static int ConsumeBounceSoundTable(WalkContext c, uint raw, int cursor)
    {
        ValidateGeneralPointer(c, raw, "bounceSound");
        if (raw == 0 || !IsInline(raw)) return cursor;

        cursor = Align4(c, cursor, "bounceSound");
        Ensure(c.Source, cursor, BounceSoundCount * 4, "bounceSound[31]");
        if(c.ObservedBounceSoundTables is not null)
            c.ObservedBounceSoundTables.Add(new OwnedInlineBounceSoundTable(
                624,cursor,c.Source.AsSpan(cursor,BounceSoundCount*4).ToArray()));
        var pointers = new uint[BounceSoundCount];
        for (var i = 0; i < BounceSoundCount; i++)
        {
            pointers[i] = ReadU32(c.Source, cursor + i * 4, c.Endianness);
            ValidateGeneralPointer(c, pointers[i], $"bounceSound[{i}]");
            WriteScalar32(c, cursor + i * 4, pointers[i]);
        }
        cursor += BounceSoundCount * 4;
        c.Stats.BounceSoundTables++;

        for (var i = 0; i < BounceSoundCount; i++)
            cursor = ConsumeSndAliasCustom(c, pointers[i], cursor, $"bounceSound[{i}]");
        return cursor;
    }

    private static int ConsumeVec2Array(WalkContext c, uint raw, int count, int cursor,
        string label,int fixedPointerOffset)
    {
        ValidateGeneralPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;

        cursor = Align4(c, cursor, label);
        var bytes = checked(count * 8);
        Ensure(c.Source, cursor, bytes, label);
        if(c.ObservedFixedInlineAccuracyArrays is not null && bytes>0)
        {
            if(fixedPointerOffset < 0 || fixedPointerOffset+4>FixedBodySize)
                throw new InvalidDataException(
                    $"Weapon '{c.WeaponName}' inline accuracy knot source slot out of bounds: {label}");
            c.ObservedFixedInlineAccuracyArrays.Add(
                new OwnedFixedInlineAccuracyVec2(
                    fixedPointerOffset,label,count,cursor,
                    c.Source.AsSpan(cursor,bytes).ToArray()));
        }
        for (var p = cursor; p < cursor + bytes; p += 4)
            WriteScalar32(c, p, ReadU32(c.Source, p, c.Endianness));
        cursor += bytes;
        c.Stats.AccuracyGraphArrays++;
        c.Stats.AccuracyGraphKnots += count;
        return cursor;
    }

    private static int ConsumeXModelRef(WalkContext c, uint raw, int cursor, string label)
    {
        ValidateGeneralPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;
        cursor = Align4(c, cursor, label);

        if (c.Endianness == Endian.Little)
        {
            var xbox = T4XModelSerializerProof.TransformInlineXModelToXbox(c.Source, cursor, out var end);
            c.Output?.Write(xbox, 0, xbox.Length);
            cursor = end;
        }
        else
        {
            cursor = T4XModelSerializerProof.MeasureInlineXModelEnd(c.Source, cursor, Endian.Big);
        }
        c.Stats.InlineXModels++;
        return cursor;
    }

    private static int ConsumeMaterialRef(WalkContext c, uint raw, int cursor, string label)
    {
        ValidateGeneralPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;
        cursor = Align4(c, cursor, label);

        if (c.Endianness == Endian.Little)
        {
            var xbox = T4XModelSerializerProof.TransformInlineMaterialToXbox(c.Source, cursor, out var end);
            c.Output?.Write(xbox, 0, xbox.Length);
            cursor = end;
        }
        else
        {
            cursor = T4XModelSerializerProof.MeasureInlineMaterialEnd(c.Source, cursor, Endian.Big);
        }
        c.Stats.InlineMaterials++;
        return cursor;
    }

    private static int ConsumeUnsupportedFxRef(WalkContext c, uint raw, int cursor, string label)
    {
        ValidateGeneralPointer(c, raw, label);
        if (raw != 0 && IsInline(raw))
            throw new InvalidDataException(
                $"Weapon '{c.WeaponName}' owns inline FxEffectDef '{label}'. Alpha 7b fails closed until the typed FX serializer is integrated.");
        return cursor;
    }

    private static int ConsumeFlameTable(WalkContext c, uint raw, int cursor, string label)
    {
        ValidateGeneralPointer(c, raw, label);
        if (raw == 0 || !IsInline(raw)) return cursor;

        cursor = Align4(c, cursor, label);
        Ensure(c.Source, cursor, FlameTableFixedSize, label + " fixed body");
        var tableStart = cursor;
        if(c.ObservedFlameTables is not null)
        {
            var fixedField=label switch
            {
                "flameTableFirstPersonPtr"=>2460,
                "flameTableThirdPersonPtr"=>2464,
                _=>throw new InvalidDataException(
                    $"Unknown original-PC 7b FlameTable parent field {label}.")
            };
            c.ObservedFlameTables.Add(new OwnedInlineFlameTable(
                label,fixedField,tableStart,
                c.Source.AsSpan(tableStart,FlameTableFixedSize).ToArray()));
        }

        // All FlameTable fixed fields are 32-bit values/pointers.
        if (c.Output is not null)
        {
            for (var p = 0; p < FlameTableFixedSize; p += 4)
                WriteScalar32(c, tableStart + p, ReadU32(c.Source, tableStart + p, c.Endianness));
        }

        var namePtr = ReadU32(c.Source, tableStart + 424, c.Endianness);
        ValidateStringPointer(c, namePtr, label + ".name");
        var materialPtrs = new uint[8];
        for (var i = 0; i < materialPtrs.Length; i++)
        {
            materialPtrs[i] = ReadU32(c.Source, tableStart + 428 + i * 4, c.Endianness);
            ValidateGeneralPointer(c, materialPtrs[i], $"{label}.material[{i}]");
        }
        var soundPtrs = new uint[4];
        for (var i = 0; i < soundPtrs.Length; i++)
        {
            soundPtrs[i] = ReadU32(c.Source, tableStart + 460 + i * 4, c.Endianness);
            ValidateGeneralPointer(c, soundPtrs[i], $"{label}.sound[{i}]");
        }

        cursor += FlameTableFixedSize;
        c.Stats.FlameTables++;
        cursor = ConsumeXString(c, namePtr, cursor, label + ".name");
        for (var i = 0; i < materialPtrs.Length; i++)
            cursor = ConsumeMaterialRef(c, materialPtrs[i], cursor, $"{label}.material[{i}]");
        for (var i = 0; i < soundPtrs.Length; i++)
            cursor = ConsumeSndAliasCustom(c, soundPtrs[i], cursor, $"{label}.sound[{i}]");
        return cursor;
    }

    private static int Align4(WalkContext c, int cursor, string label)
    {
        // DB_AllocStreamPos alignment applies to the destination zone block in memory.
        // It does not insert padding bytes into the serialized fastfile stream. Alpha 4/5
        // already prove this byte-packed traversal model for XAnim/XModel ownership.
        _ = c;
        _ = label;
        return cursor;
    }

    private static void ValidateFixedBody(WalkContext c, int start)
    {
        var internalName = ReadU32(c.Source, start + InternalName, c.Endianness);
        if (internalName == 0)
            throw new InvalidDataException($"Weapon '{c.WeaponName}' has a null internal-name pointer during Alpha 7b traversal.");
        ValidateStringPointer(c, internalName, "szInternalName");
        var weapType = ReadI32(c.Source, start + 324, c.Endianness);
        var weapClass = ReadI32(c.Source, start + 328, c.Endianness);
        if (weapType is < 0 or > 6 || weapClass is < 0 or > 10)
            throw new InvalidDataException($"Weapon '{c.WeaponName}' failed Alpha 7b fixed-body endian validation.");
    }

    private static void ValidateStringPointer(WalkContext c, uint raw, string label)
    {
        if (raw == 0 || IsInline(raw)) return;
        if (c.RequireSelfContainedPointers)
            throw new InvalidDataException($"Weapon '{c.WeaponName}' {label} retains packed/source pointer 0x{raw:X8}; self-contained promotion refused.");
        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block != T4ZoneBlock.Virtual)
            throw new InvalidDataException($"Weapon '{c.WeaponName}' {label} has non-VIRTUAL string pointer 0x{raw:X8}.");
        if (c.Header is not null && decoded.Offset >= c.Header.VirtualBlockSize)
            throw new InvalidDataException($"Weapon '{c.WeaponName}' {label} string pointer 0x{raw:X8} exceeds VIRTUAL block bounds.");
    }

    private static void ValidateGeneralPointer(WalkContext c, uint raw, string label)
    {
        if (raw == 0 || IsInline(raw)) return;
        if (c.RequireSelfContainedPointers)
            throw new InvalidDataException($"Weapon '{c.WeaponName}' {label} retains packed/source pointer 0x{raw:X8}; self-contained promotion refused.");
        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null)
            throw new InvalidDataException($"Weapon '{c.WeaponName}' {label} has invalid serialized pointer 0x{raw:X8}.");
        if (c.Header is not null)
        {
            var block = (int)decoded.Block.Value;
            if (block < 0 || block >= c.Header.BlockSizes.Count || decoded.Offset >= c.Header.BlockSizes[block])
                throw new InvalidDataException($"Weapon '{c.WeaponName}' {label} pointer 0x{raw:X8} exceeds its block bounds.");
        }
    }

    private static byte[] ApplyInsertions(byte[] source, IReadOnlyList<PlannedInsertion> insertions)
    {
        if (insertions.Count == 0) return source;
        using var ms = new MemoryStream(source.Length + insertions.Sum(x => x.Bytes.Length));
        var cursor = 0;
        foreach (var insertion in insertions.OrderBy(x => x.Offset))
        {
            if (insertion.Offset < cursor || insertion.Offset > source.Length)
                throw new InvalidDataException($"Invalid Weapon insertion offset 0x{insertion.Offset:X}.");
            ms.Write(source, cursor, insertion.Offset - cursor);
            ms.Write(insertion.Bytes);
            cursor = insertion.Offset;
        }
        ms.Write(source, cursor, source.Length - cursor);
        return ms.ToArray();
    }

    private static int FindCStringEnd(byte[] data, int cursor, string label)
    {
        if (cursor < 0 || cursor >= data.Length)
            throw new InvalidDataException($"{label}: string begins outside the serialized stream.");
        var limit = Math.Min(data.Length, cursor + 4096);
        var end = cursor;
        while (end < limit && data[end] != 0) end++;
        if (end >= limit || data[end] != 0)
            throw new InvalidDataException($"{label}: unterminated/implausibly long inline string at 0x{cursor:X8}.");
        return end + 1;
    }

    private static byte[] TransformFixedBodyToBigEndian(byte[] src)
    {
        if (src.Length != FixedBodySize)
            throw new InvalidDataException("WeaponDef fixed body has the wrong size.");
        var dst = src.ToArray();
        for (var o = 0; o < dst.Length; o += 4)
        {
            dst[o] = src[o + 3];
            dst[o + 1] = src[o + 2];
            dst[o + 2] = src[o + 1];
            dst[o + 3] = src[o];
        }

        // Repair the three ScriptString arrays as 16-bit elements.
        SwapU16Range(src, dst, 224, 8);
        SwapU16Range(src, dst, 240, 20);
        SwapU16Range(src, dst, 280, 20);
        return dst;
    }

    private static void SwapU16Range(byte[] src, byte[] dst, int offset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var o = offset + i * 2;
            dst[o] = src[o + 1];
            dst[o + 1] = src[o];
        }
    }

    private static void WriteScalar32(WalkContext c, int sourceOffset, uint semanticValue)
    {
        if (c.Output is null) return;
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, semanticValue);
        c.Output.Write(b);
    }

    private static void VerifyStats(string name, TraverseStats a, TraverseStats b)
    {
        if (a.InlineStrings != b.InlineStrings ||
            a.SoundNameRecords != b.SoundNameRecords ||
            a.BounceSoundTables != b.BounceSoundTables ||
            a.AccuracyGraphArrays != b.AccuracyGraphArrays ||
            a.AccuracyGraphKnots != b.AccuracyGraphKnots ||
            a.FlameTables != b.FlameTables ||
            a.InlineXModels != b.InlineXModels ||
            a.InlineMaterials != b.InlineMaterials)
            throw new InvalidDataException($"Weapon '{name}' Alpha 7b read-back ownership statistics changed after endian conversion.");
    }

    private static bool VerifyContainer(string path, IReadOnlyList<T4WeaponVariableProofAsset> expected)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 20 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return false;
        var p = Magic.Length;
        if (ReadBe32(data, ref p) != 8) return false;
        if (ReadBe32(data, ref p) != (uint)expected.Count) return false;
        if (ReadBe32(data, ref p) != FixedBodySize) return false;

        for (var i = 0; i < expected.Count; i++)
        {
            var sourceOffset = ReadBe32(data, ref p);
            var len = checked((int)ReadBe32(data, ref p));
            var nameLen = checked((int)ReadBe32(data, ref p));
            if (sourceOffset != (uint)expected[i].SourceOffset || len != expected[i].XboxSerializedBytes) return false;
            if (nameLen < 0 || p + nameLen + len > data.Length) return false;
            var name = Encoding.UTF8.GetString(data, p, nameLen);
            p += nameLen;
            if (!name.Equals(expected[i].Name, StringComparison.Ordinal)) return false;

            var blob = data.AsSpan(p, len).ToArray();
            var stats = new TraverseStats();
            var ctx = new WalkContext
            {
                Source = blob,
                Endianness = Endian.Big,
                Header = null,
                Output = null,
                Stats = stats,
                WeaponName = name
            };
            if (WalkOwnedPayload(ctx, 0) != blob.Length) return false;
            p += len;
        }
        return p == data.Length;
    }

    private static uint ReadBe32(byte[] b, ref int p)
    {
        if (p + 4 > b.Length) throw new InvalidDataException("Unexpected end of proof container.");
        var value = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p, 4));
        p += 4;
        return value;
    }

    private static void WriteU32(Stream s, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        s.Write(b);
    }

    private static uint ReadU32(byte[] b, int o, Endian e)
    {
        Ensure(b, o, 4, "uint32");
        return e == Endian.Big
            ? BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4))
            : BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4));
    }

    private static int ReadI32(byte[] b, int o, Endian e) => unchecked((int)ReadU32(b, o, e));
    private static bool IsInline(uint p) => p is Following or Insert;

    private static void Ensure(byte[] data, int start, int count, string label)
    {
        if (start < 0 || count < 0 || (long)start + count > data.Length)
            throw new InvalidDataException($"{label} exceeds the serialized stream at 0x{start:X8} (+{count:N0}).");
    }
}
