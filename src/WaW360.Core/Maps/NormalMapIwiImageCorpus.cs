using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4IwiImageRecord(
    string SourceIwd,
    string EntryName,
    string CanonicalName,
    byte Version,
    byte Format,
    byte Flags,
    ushort Width,
    ushort Height,
    ushort Depth,
    long EntryBytes,
    bool HeaderValid);

public sealed record T4IwiFormatSummary(
    byte Version,
    byte Format,
    int Images,
    long Bytes,
    int NativeMatches);

public sealed class NormalMapIwiImageCorpusResult
{
    public required string MapName { get; init; }
    public required int NativeImages { get; init; }
    public required int SourceIwiEntries { get; init; }
    public required int UniqueSourceImages { get; init; }
    public required int MatchedNativeImages { get; init; }
    public required int UnmatchedNativeImages { get; init; }
    public required int DuplicateSourceIdentities { get; init; }
    public required long SourceIwiBytes { get; init; }
    public required long MatchedIwiBytes { get; init; }
    public required double NativeIdentityCoverage { get; init; }
    public required double MatchedByteCoverage { get; init; }
    public required IReadOnlyList<T4IwiFormatSummary> Formats { get; init; }
    public required IReadOnlyList<T4IwiImageRecord> LargestMatchedImages { get; init; }
    public required IReadOnlyList<string> UnmatchedNativeNames { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required bool ReadyForCodecPlanning { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// NUCLEAR Stage 4G.
///
/// Builds the authoritative custom-image corpus from map IWD archives and matches
/// it against the exact native Xbox image identities recovered by Stage 4C.
///
/// No pixel data is converted here. This is an evidence stage that answers:
///   * how many native image roots have a source IWI;
///   * how much payload mass those matches represent;
///   * which IWI versions/formats/dimensions dominate the corpus.
///
/// Native image names conventionally carry the Xenon "_wj" suffix while source
/// IWI entries do not. The only identity transform performed here is removing that
/// terminal suffix plus the canonical IWD "images/" prefix and ".iwi" extension.
/// </summary>
public static class NormalMapIwiImageCorpus
{
    public static async Task<NormalMapIwiImageCorpusResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        string nativeIdentityManifest,
        CancellationToken cancellationToken = default)
    {
        var source = await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        if (!source.Verified)
            throw new InvalidDataException("Stage 4G requires a verified source archive.");

        var nativeImages = LoadNativeImages(nativeIdentityManifest);
        var iwds = source.SourceArchive
            .Where(x => x.Role.Equals("iwd", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(x.Path))
            .Select(x => x.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var all = new List<T4IwiImageRecord>();
        foreach (var iwd in iwds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var zip = ZipFile.OpenRead(iwd);

            foreach (var entry in zip.Entries
                         .Where(x => x.FullName.EndsWith(".iwi", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var canonical = NormalizeSourceImage(entry.FullName);
                var header = new byte[28];
                var read = 0;

                await using (var stream = entry.Open())
                {
                    while (read < header.Length)
                    {
                        var n = await stream.ReadAsync(
                            header.AsMemory(read, header.Length - read),
                            cancellationToken);
                        if (n == 0) break;
                        read += n;
                    }
                }

                var valid =
                    read >= 12 &&
                    header[0] == (byte)'I' &&
                    header[1] == (byte)'W' &&
                    header[2] == (byte)'i';

                byte version = 0;
                byte format = 0;
                byte flags = 0;
                ushort width = 0;
                ushort height = 0;
                ushort depth = 0;

                if (valid)
                {
                    version = header[3];
                    format = header[4];
                    flags = header[5];
                    width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6, 2));
                    height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8, 2));
                    depth = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10, 2));
                }

                all.Add(new T4IwiImageRecord(
                    iwd,
                    entry.FullName.Replace('\\', '/'),
                    canonical,
                    version,
                    format,
                    flags,
                    width,
                    height,
                    depth,
                    entry.Length,
                    valid));
            }
        }

        var byIdentity = all
            .GroupBy(x => x.CanonicalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.EntryBytes)
                      .ThenBy(x => x.SourceIwd, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(x => x.EntryName, StringComparer.OrdinalIgnoreCase)
                      .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var duplicateIdentities = byIdentity.Values.Count(x => x.Count > 1);
        var matched = new List<T4IwiImageRecord>();
        var unmatched = new List<string>();

        foreach (var nativeName in nativeImages)
        {
            var key = NormalizeNativeImage(nativeName);
            if (byIdentity.TryGetValue(key, out var candidates) && candidates.Count != 0)
                matched.Add(candidates[0]);
            else
                unmatched.Add(nativeName);
        }

        var sourceBytes = all.Sum(x => x.EntryBytes);
        var matchedBytes = matched.Sum(x => x.EntryBytes);

        var matchedKeys = matched
            .Select(x => x.CanonicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var formats = all
            .Where(x => x.HeaderValid)
            .GroupBy(x => (x.Version, x.Format))
            .Select(g => new T4IwiFormatSummary(
                g.Key.Version,
                g.Key.Format,
                g.Count(),
                g.Sum(x => x.EntryBytes),
                g.Select(x => x.CanonicalName)
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .Count(x => matchedKeys.Contains(x))))
            .OrderByDescending(x => x.Bytes)
            .ThenByDescending(x => x.Images)
            .ThenBy(x => x.Version)
            .ThenBy(x => x.Format)
            .ToList();

        var identityCoverage = nativeImages.Count == 0
            ? 0.0
            : matched.Count / (double)nativeImages.Count;

        var matchedByteCoverage = sourceBytes == 0
            ? 0.0
            : matchedBytes / (double)sourceBytes;

        var notes = new List<string>
        {
            "Stage 4G is identity/mass evidence only; it does not serialize or copy image pixels into an Xbox fastfile.",
            "Source identity normalization removes only the IWD images/ prefix and .iwi extension.",
            "Native identity normalization additionally removes the terminal Xenon _wj suffix.",
            "When duplicate IWD identities exist, the largest entry is selected deterministically for census purposes; duplicates remain reported.",
            "The existing T4XboxImageCodec is intentionally not generalized by this stage. Codec work should target the largest matched format group first."
        };

        var ready =
            nativeImages.Count != 0 &&
            all.Count != 0 &&
            matched.Count != 0 &&
            formats.Count != 0;

        var outDir = Path.Combine(outputRoot, map.Name, "nuclear_4g_iwi_image_corpus");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, "nuclear_4g_iwi_image_corpus.txt");
        var json = Path.Combine(outDir, "nuclear_4g_iwi_image_corpus.json");

        var result = new NormalMapIwiImageCorpusResult
        {
            MapName = map.Name,
            NativeImages = nativeImages.Count,
            SourceIwiEntries = all.Count,
            UniqueSourceImages = byIdentity.Count,
            MatchedNativeImages = matched.Count,
            UnmatchedNativeImages = unmatched.Count,
            DuplicateSourceIdentities = duplicateIdentities,
            SourceIwiBytes = sourceBytes,
            MatchedIwiBytes = matchedBytes,
            NativeIdentityCoverage = identityCoverage,
            MatchedByteCoverage = matchedByteCoverage,
            Formats = formats,
            LargestMatchedImages = matched
                .OrderByDescending(x => x.EntryBytes)
                .ThenBy(x => x.CanonicalName, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToList(),
            UnmatchedNativeNames = unmatched
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Notes = notes,
            ReadyForCodecPlanning = ready,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(
            json,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        return result;
    }

    private static List<string> LoadNativeImages(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        if (root.GetProperty("format").GetString() != "WaW360.T4Xenon.NativeIdentityManifest.v1" ||
            !root.GetProperty("typeSequenceVerified").GetBoolean())
            throw new InvalidDataException("Stage 4G native identity manifest is not verified.");

        return root.GetProperty("entries")
            .EnumerateArray()
            .Where(e =>
            {
                var type = e.GetProperty("TypeName").GetString() ?? string.Empty;
                var oat = e.GetProperty("OatTypeName").GetString() ?? string.Empty;
                return type.Equals("image", StringComparison.OrdinalIgnoreCase) ||
                       oat.Equals("image", StringComparison.OrdinalIgnoreCase);
            })
            .Select(e => e.GetProperty("Name").GetString() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static string NormalizeSourceImage(string entry)
    {
        var value = entry.Replace('\\', '/').TrimStart('/');
        if (value.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
            value = value["images/".Length..];
        if (value.EndsWith(".iwi", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];
        return value;
    }

    private static string NormalizeNativeImage(string name)
    {
        var value = name.Replace('\\', '/');
        if (value.EndsWith("_wj", StringComparison.OrdinalIgnoreCase))
            value = value[..^3];
        return value;
    }

    private static string Render(NormalMapIwiImageCorpusResult r)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("NUCLEAR STAGE 4G - IWD IWI IMAGE CORPUS / NATIVE IDENTITY CENSUS");
        sb.AppendLine(new string('=', 94));
        sb.AppendLine($"Map:                      {r.MapName}");
        sb.AppendLine($"Native image roots:       {r.NativeImages:N0}");
        sb.AppendLine($"Source IWI entries:       {r.SourceIwiEntries:N0}");
        sb.AppendLine($"Unique source images:     {r.UniqueSourceImages:N0}");
        sb.AppendLine($"Matched native images:    {r.MatchedNativeImages:N0}");
        sb.AppendLine($"Unmatched native images:  {r.UnmatchedNativeImages:N0}");
        sb.AppendLine($"Duplicate source IDs:     {r.DuplicateSourceIdentities:N0}");
        sb.AppendLine($"Native identity coverage: {r.NativeIdentityCoverage:P2}");
        sb.AppendLine($"Source IWI bytes:         {r.SourceIwiBytes:N0}");
        sb.AppendLine($"Matched IWI bytes:        {r.MatchedIwiBytes:N0}");
        sb.AppendLine($"Matched byte coverage:    {r.MatchedByteCoverage:P2}");
        sb.AppendLine($"READY FOR CODEC PLANNING: {r.ReadyForCodecPlanning}");
        sb.AppendLine();

        sb.AppendLine("FORMAT MASS (largest first)");
        sb.AppendLine("version format images nativeMatches       bytes");
        foreach (var f in r.Formats)
            sb.AppendLine($"   {f.Version,3}   0x{f.Format:X2} {f.Images,6:N0} {f.NativeMatches,13:N0} {f.Bytes,11:N0}");
        sb.AppendLine();

        sb.AppendLine("LARGEST MATCHED IMAGES");
        foreach (var image in r.LargestMatchedImages)
            sb.AppendLine(
                $"  {image.EntryBytes,10:N0} B  v{image.Version} fmt=0x{image.Format:X2} " +
                $"{image.Width}x{image.Height}x{image.Depth}  {image.CanonicalName}  [{Path.GetFileName(image.SourceIwd)}]");
        sb.AppendLine();

        sb.AppendLine("UNMATCHED NATIVE IMAGE NAMES (first 250)");
        foreach (var name in r.UnmatchedNativeNames.Take(250))
            sb.AppendLine("  " + name);
        if (r.UnmatchedNativeNames.Count > 250)
            sb.AppendLine($"  ... {r.UnmatchedNativeNames.Count - 250:N0} more");
        sb.AppendLine();

        foreach (var note in r.Notes)
            sb.AppendLine("NOTE: " + note);

        return sb.ToString();
    }
}
