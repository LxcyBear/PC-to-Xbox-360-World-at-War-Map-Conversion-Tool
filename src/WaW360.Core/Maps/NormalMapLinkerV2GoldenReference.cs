using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record LinkerV2XboxReferenceFastFile(
    string FileName,
    string Sha256,
    long FastFileBytes,
    long ZoneBytes,
    int ScriptStrings,
    int Assets,
    int InlineSharedRoots,
    int InlineInsertRoots,
    int PackedAliasRoots,
    int NullRoots,
    IReadOnlyDictionary<string, int> AssetTypes,
    IReadOnlyDictionary<string, uint> BlockSizes,
    string Compression);

public sealed class LinkerV2XboxReferenceFolderResult
{
    public required string Folder { get; init; }
    public required IReadOnlyList<LinkerV2XboxReferenceFastFile> FastFiles { get; init; }
    public required int XmaFiles { get; init; }
    public required long XmaBytes { get; init; }
    public required int NonAlignedXmaFiles { get; init; }
    public required IReadOnlyList<string> OtherFiles { get; init; }
}

/// <summary>
/// Generic inspector for known-working Xbox map folders. This is validation tooling only:
/// it never copies reference payloads into a conversion. The output is safe to use as a
/// golden oracle for counts, block ownership, wrapper/compression, alias patterns and package shape.
/// </summary>
public static class NormalMapLinkerV2GoldenReference
{
    public static async Task<LinkerV2XboxReferenceFolderResult> InspectFolderAsync(
        string folder,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        var fastFiles = new List<LinkerV2XboxReferenceFastFile>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.ff", SearchOption.TopDirectoryOnly)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(path, cancellationToken);
            if (cached.Platform != WaWPlatform.Xbox360)
                throw new InvalidDataException($"Reference '{path}' parsed as {cached.Platform}, not Xbox360.");
            var doc = cached.Document;
            var blockSizes = Enum.GetValues<T4ZoneBlock>()
                .ToDictionary(x => x.ToString(), x => doc.Header.BlockSizes[(int)x], StringComparer.OrdinalIgnoreCase);
            fastFiles.Add(new LinkerV2XboxReferenceFastFile(
                Path.GetFileName(path),
                cached.SourceSha256.ToLowerInvariant(),
                new FileInfo(path).Length,
                cached.Bytes.LongLength,
                doc.ScriptStringCount,
                doc.AssetCount,
                doc.Assets.Count(a => a.ReferenceKind == T4AssetReferenceKind.InlineShared),
                doc.Assets.Count(a => a.ReferenceKind == T4AssetReferenceKind.Inline),
                doc.Assets.Count(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias),
                doc.Assets.Count(a => a.ReferenceKind == T4AssetReferenceKind.Null),
                new Dictionary<string, int>(doc.AssetCountsByType, StringComparer.OrdinalIgnoreCase),
                blockSizes,
                doc.Compression.ToString()));
        }

        var xmaPaths = Directory.EnumerateFiles(folder, "*.xma", SearchOption.AllDirectories).ToList();
        var other = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(x => !x.EndsWith(".ff", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
        return new LinkerV2XboxReferenceFolderResult
        {
            Folder = Path.GetFullPath(folder),
            FastFiles = fastFiles,
            XmaFiles = xmaPaths.Count,
            XmaBytes = xmaPaths.Sum(x => new FileInfo(x).Length),
            NonAlignedXmaFiles = xmaPaths.Count(x => new FileInfo(x).Length % 2048 != 0),
            OtherFiles = other
        };
    }

    public static async Task WriteReportAsync(
        string folder,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var result = await InspectFolderAsync(folder, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (Path.GetExtension(outputPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Linker V2 known-working Xbox reference inspection");
        sb.AppendLine(new string('=', 88));
        sb.AppendLine($"Folder: {result.Folder}");
        foreach (var ff in result.FastFiles)
        {
            sb.AppendLine($"[{ff.FileName}] bytes={ff.FastFileBytes:N0}; zone={ff.ZoneBytes:N0}; compression={ff.Compression}; strings={ff.ScriptStrings:N0}; assets={ff.Assets:N0}");
            sb.AppendLine($"  roots inlineShared={ff.InlineSharedRoots:N0}; insert={ff.InlineInsertRoots:N0}; packedAlias={ff.PackedAliasRoots:N0}; null={ff.NullRoots:N0}");
            sb.AppendLine("  blocks: " + string.Join(", ", ff.BlockSizes.Select(x => $"{x.Key}={x.Value:N0}")));
            sb.AppendLine("  types: " + string.Join(", ", ff.AssetTypes.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => $"{x.Key}={x.Value:N0}")));
            sb.AppendLine($"  sha256={ff.Sha256}");
        }
        sb.AppendLine($"XMA: files={result.XmaFiles:N0}; bytes={result.XmaBytes:N0}; non2048Aligned={result.NonAlignedXmaFiles:N0}");
        sb.AppendLine("Other top-level files: " + string.Join(", ", result.OtherFiles));
        await File.WriteAllTextAsync(outputPath, sb.ToString(), new UTF8Encoding(false), cancellationToken);
    }
}
