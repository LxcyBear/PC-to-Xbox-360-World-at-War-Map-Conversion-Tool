using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4Stage3LabelProofResult(
    string InputFastFile,
    string OutputFastFile,
    long MarkerOffset,
    string OwnerMenu,
    int LogicalItemIndex,
    string OriginalText,
    string ReplacementText,
    int RawZoneChangedByteCount,
    long RawZoneBytes,
    long OutputFastFileBytes,
    bool OnlyTargetSpanChanged,
    bool ReopenedAndVerified,
    int AssetCount,
    int MenuItemCount);

/// <summary>
/// Stage 3 alpha 1: the first controlled write operation.
///
/// This deliberately avoids changing any serialized sizes, pointers, arrays or counts.
/// It locates the already-proven solo Custom Maps button in levels_unlock item #59,
/// replaces the 11-byte inline label with the equally-sized "Lxcy's Maps" label,
/// repacks the fastfile, reopens it, and proves the raw zone differs only inside that
/// one label span.
///
/// This is a writer sanity test. It is NOT the final Lxcy's Maps insertion; the final
/// writer must preserve Custom Maps and grow levels_unlock from 62 to 63 items.
/// </summary>
public static class T4Stage3LabelProofWriter
{
    public const string OriginalLabel = "Custom Maps";
    public const string ReplacementLabel = "Lxcy's Maps";

    private static readonly byte[] OriginalBytes = Encoding.ASCII.GetBytes(OriginalLabel);
    private static readonly byte[] ReplacementBytes = Encoding.ASCII.GetBytes(ReplacementLabel);

    public static async Task<T4Stage3LabelProofResult> CreateAsync(
        string inputFastFile,
        string outputFastFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFastFile))
            throw new FileNotFoundException("Fastfile not found.", inputFastFile);
        if (OriginalBytes.Length != ReplacementBytes.Length)
            throw new InvalidOperationException("Stage 3 alpha 1 requires an equal-length proof label.");

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "stage3-label-proof", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourceZonePath = Path.Combine(tempDir, "source.zone");
        var patchedZonePath = Path.Combine(tempDir, "patched.zone");
        var verifyZonePath = Path.Combine(tempDir, "verify.zone");

        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(inputFastFile, sourceZonePath, cancellationToken);
            if (decomp.Platform != WaWPlatform.Xbox360)
                throw new InvalidDataException("The Stage 3 label proof currently targets Xbox 360 WaW fastfiles only.");
            if (decomp.Compression == WaWCompressionKind.XboxSignedSingleZlib)
                throw new NotSupportedException("Signed/authenticated Xbox fastfiles are not rewritten by Stage 3 alpha 1.");

            var sourceZone = await File.ReadAllBytesAsync(sourceZonePath, cancellationToken);
            var document = T4ZoneParser.ParseZoneBytes(
                inputFastFile,
                sourceZone,
                decomp.Platform,
                decomp.Compression,
                decomp.ZoneHeader.Endianness);

            var trace = T4CustomMapsTrace.Trace(document, sourceZone);
            var targets = trace.Markers.Where(m =>
                    m.StrongInlineItemCandidate &&
                    string.Equals(m.UsermapMode, "solo", StringComparison.Ordinal) &&
                    string.Equals(m.ResolvedOwnerMenu, "levels_unlock", StringComparison.Ordinal) &&
                    m.LogicalItemIndex == 59 &&
                    m.ItemPointerArrayOffset is not null)
                .ToArray();

            if (targets.Length != 1)
                throw new InvalidDataException($"Expected exactly one proven levels_unlock/solo/Custom Maps item #59, found {targets.Length}.");

            var target = targets[0];
            var markerOffset = checked((int)target.MarkerOffset);
            if (markerOffset < 0 || markerOffset + OriginalBytes.Length > sourceZone.Length)
                throw new InvalidDataException("The proven Custom Maps text span is outside the decompressed zone.");
            if (!sourceZone.AsSpan(markerOffset, OriginalBytes.Length).SequenceEqual(OriginalBytes))
                throw new InvalidDataException("The proven target no longer contains the expected Custom Maps label.");

            var patchedZone = (byte[])sourceZone.Clone();
            ReplacementBytes.CopyTo(patchedZone, markerOffset);

            var changedOffsets = new List<int>();
            for (var i = 0; i < sourceZone.Length; i++)
            {
                if (sourceZone[i] != patchedZone[i])
                    changedOffsets.Add(i);
            }

            if (changedOffsets.Count == 0)
                throw new InvalidDataException("The proof patch unexpectedly changed zero raw-zone bytes.");
            if (changedOffsets.Any(i => i < markerOffset || i >= markerOffset + ReplacementBytes.Length))
                throw new InvalidDataException("The proof patch altered bytes outside the proven label span.");

            await File.WriteAllBytesAsync(patchedZonePath, patchedZone, cancellationToken);
            await FastFileRepacker.RepackZoneFileAsync(
                inputFastFile,
                patchedZonePath,
                outputFastFile,
                decomp.Compression,
                cancellationToken);

            var verify = await FastFileDecompressor.DecompressAutoAsync(outputFastFile, verifyZonePath, cancellationToken);
            var verifyZone = await File.ReadAllBytesAsync(verifyZonePath, cancellationToken);
            if (!patchedZone.AsSpan().SequenceEqual(verifyZone))
                throw new InvalidDataException("The output fastfile reopened, but its decompressed zone did not exactly match the intended patched zone.");
            if (!verifyZone.AsSpan(markerOffset, ReplacementBytes.Length).SequenceEqual(ReplacementBytes))
                throw new InvalidDataException("The output zone does not contain Lxcy's Maps at the proven target offset.");

            var verifyDocument = T4ZoneParser.ParseZoneBytes(
                outputFastFile,
                verifyZone,
                verify.Platform,
                verify.Compression,
                verify.ZoneHeader.Endianness);
            if (verifyDocument.Assets.Count != document.Assets.Count)
                throw new InvalidDataException("The proof patch changed the XAsset count, which must remain unchanged.");

            var menuProbe = T4MenuStructureProbe.Probe(verifyDocument, verifyZone);
            var levelsUnlock = menuProbe.InlineNamedMenus.SingleOrDefault(m =>
                string.Equals(m.Name, "levels_unlock", StringComparison.Ordinal));
            if (levelsUnlock is null)
                throw new InvalidDataException("The rebuilt zone no longer exposes the proven levels_unlock menu structure.");
            if (levelsUnlock.ItemCount != 62)
                throw new InvalidDataException($"The proof patch changed levels_unlock.itemCount from 62 to {levelsUnlock.ItemCount}.");

            return new T4Stage3LabelProofResult(
                inputFastFile,
                outputFastFile,
                target.MarkerOffset,
                target.ResolvedOwnerMenu!,
                target.LogicalItemIndex!.Value,
                OriginalLabel,
                ReplacementLabel,
                changedOffsets.Count,
                patchedZone.LongLength,
                new FileInfo(outputFastFile).Length,
                true,
                true,
                verifyDocument.Assets.Count,
                levelsUnlock.ItemCount);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
