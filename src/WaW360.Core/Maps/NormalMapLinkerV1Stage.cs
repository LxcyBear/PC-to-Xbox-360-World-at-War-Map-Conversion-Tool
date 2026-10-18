using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WaW360.Core.Linker;

namespace WaW360.Core.Maps;

public sealed record NormalMapLinkerImageResolution(
    string FastFile,
    string ImageName,
    string Resolution,
    string? Source,
    string Note);

public sealed record NormalMapLinkerFastFileResult(
    string FastFile,
    int PcRoots,
    int LinkedNodes,
    int PromotedNested,
    bool ExactSourceWalk,
    bool TechniqueBindingProven,
    bool Verified,
    IReadOnlyList<T4LoadLinkOrderEntry> OutputOrder,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapLinkerV1Result
{
    public required string MapName { get; init; }
    public required int LoadFastFiles { get; init; }
    public required int PcRootAssets { get; init; }
    public required int LinkedAssets { get; init; }
    public required int PromotedNestedAssets { get; init; }
    public required int IwdImageDependencies { get; init; }
    public required int EmbeddedImageDependencies { get; init; }
    public required int DonorImageDependenciesPending { get; init; }
    public required bool DeterministicWalkVerified { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<NormalMapLinkerFastFileResult> FastFiles { get; init; }
    public required IReadOnlyList<NormalMapLinkerImageResolution> ImageResolutions { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// First production-facing linker stage. It does not emit Xbox bytes.
/// It proves deterministic source ownership and constructs dependency-first load.ff plans.
/// No global structure candidate scan is used by this stage.
/// </summary>
public static class NormalMapLinkerV1Stage
{
    public static async Task<NormalMapLinkerV1Result> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        var loadFiles = analysis.InputFiles
            .Where(f => f.Role.Equals("load.ff", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var iwdEntries = ReadIwdImageIndex(analysis.InputFiles
            .Where(f => f.Role.Equals("iwd", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Path));

        var ffResults = new List<NormalMapLinkerFastFileResult>();
        var imageResults = new List<NormalMapLinkerImageResolution>();
        var warnings = new List<string>();
        var pcRoots = 0;
        var linked = 0;
        var promoted = 0;
        var allWalks = true;
        var allVerified = true;

        foreach (var input in loadFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await T4LoadFastFileLinker.BuildAsync(input.Path, cancellationToken);
            pcRoots += result.SourceAssetCount;
            linked += result.LinkedAssetCount;
            promoted += result.PromotedNestedAssetCount;
            allWalks &= result.ExactSourceWalk;
            allVerified &= result.Verified;

            ffResults.Add(new NormalMapLinkerFastFileResult(
                input.Path,
                result.SourceAssetCount,
                result.LinkedAssetCount,
                result.PromotedNestedAssetCount,
                result.ExactSourceWalk,
                result.TechniqueBindingProven,
                result.Verified,
                result.OutputOrder,
                result.Warnings));

            foreach (var image in result.Nodes.Where(n => n.TypeName.Equals("image", StringComparison.OrdinalIgnoreCase)))
            {
                var embedded = image.Metadata.TryGetValue("embeddedResourceBytes", out var resourceText) &&
                               int.TryParse(resourceText, out var resourceBytes) && resourceBytes > 0;
                if (embedded)
                {
                    imageResults.Add(new NormalMapLinkerImageResolution(
                        input.Path, image.Name, "EMBEDDED_PC_IMAGE", input.Path,
                        "Image owns source pixels in the PC serialized graph; Xbox format/tiling conversion is required."));
                    continue;
                }

                if (iwdEntries.TryGetValue(image.Name, out var iwiSource))
                {
                    imageResults.Add(new NormalMapLinkerImageResolution(
                        input.Path, image.Name, "IWD_IMAGE", iwiSource,
                        "Exact images/<asset>.iwi identity found; decode/resize/re-encode/Xenos-tile conversion is required."));
                    continue;
                }

                imageResults.Add(new NormalMapLinkerImageResolution(
                    input.Path, image.Name, "DONOR_REQUIRED", null,
                    "No embedded source pixels and no exact IWD image identity; linker must resolve a verified Xbox donor."));
            }
        }

        if (loadFiles.Count == 0)
        {
            warnings.Add("No *_load.ff input was discovered for this map. Linker V1 load-graph stage is a no-op; main.ff graph closure is the next linker milestone.");
            allWalks = true;
            allVerified = true;
        }

        var iwdCount = imageResults.Count(x => x.Resolution == "IWD_IMAGE");
        var embeddedCount = imageResults.Count(x => x.Resolution == "EMBEDDED_PC_IMAGE");
        var donorCount = imageResults.Count(x => x.Resolution == "DONOR_REQUIRED");

        var outputDir = Path.Combine(outputRoot, map.Name, "linker_v1");
        Directory.CreateDirectory(outputDir);
        var textPath = Path.Combine(outputDir, $"{map.Name}_linker_v1_load_graph.txt");
        var jsonPath = Path.Combine(outputDir, $"{map.Name}_linker_v1_load_graph.json");

        var final = new NormalMapLinkerV1Result
        {
            MapName = map.Name,
            LoadFastFiles = loadFiles.Count,
            PcRootAssets = pcRoots,
            LinkedAssets = linked,
            PromotedNestedAssets = promoted,
            IwdImageDependencies = iwdCount,
            EmbeddedImageDependencies = embeddedCount,
            DonorImageDependenciesPending = donorCount,
            DeterministicWalkVerified = allWalks,
            Verified = allVerified,
            FastFiles = ffResults,
            ImageResolutions = imageResults,
            Warnings = warnings,
            TextReportFile = textPath,
            JsonReportFile = jsonPath
        };

        await File.WriteAllTextAsync(textPath, RenderText(final), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(jsonPath,
            JsonSerializer.Serialize(final, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8, cancellationToken);
        return final;
    }

    private static Dictionary<string, string> ReadIwdImageIndex(IEnumerable<string> iwdPaths)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in iwdPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try
            {
                using var archive = ZipFile.OpenRead(path);
                foreach (var entry in archive.Entries)
                {
                    var normalized = entry.FullName.Replace('\\', '/');
                    if (!normalized.StartsWith("images/", StringComparison.OrdinalIgnoreCase) ||
                        !normalized.EndsWith(".iwi", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var name = Path.GetFileNameWithoutExtension(normalized);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    output.TryAdd(name, $"{path}::{entry.FullName}");
                }
            }
            catch (InvalidDataException)
            {
                // The normal analyzer owns IWD validity reporting. Linker V1 stays fail-closed
                // by simply not treating an unreadable archive as a proven image source.
            }
        }
        return output;
    }

    private static string RenderText(NormalMapLinkerV1Result result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Linker V1 deterministic load.ff graph");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"Map: {result.MapName}");
        sb.AppendLine($"Load fastfiles: {result.LoadFastFiles:N0}");
        sb.AppendLine($"PC top-level roots: {result.PcRootAssets:N0}");
        sb.AppendLine($"Linked graph nodes: {result.LinkedAssets:N0}");
        sb.AppendLine($"Nested dependencies promoted: {result.PromotedNestedAssets:N0}");
        sb.AppendLine($"Image sources: embedded={result.EmbeddedImageDependencies:N0}; IWD={result.IwdImageDependencies:N0}; donor pending={result.DonorImageDependenciesPending:N0}");
        sb.AppendLine($"Exact deterministic stream walk: {result.DeterministicWalkVerified}");
        sb.AppendLine($"Graph verified: {result.Verified}");
        sb.AppendLine();
        sb.AppendLine("ARCHITECTURE RULE");
        sb.AppendLine("  XAsset table -> typed stream walker -> owned dependencies -> promoted graph nodes -> dependency-first order.");
        sb.AppendLine("  No broad candidate scan is allowed in this stage. Unsupported ownership fails closed.");

        foreach (var ff in result.FastFiles)
        {
            sb.AppendLine();
            sb.AppendLine($"[{Path.GetFileName(ff.FastFile)}]");
            sb.AppendLine($"  roots={ff.PcRoots:N0}; linked={ff.LinkedNodes:N0}; promoted={ff.PromotedNested:N0}; exactWalk={ff.ExactSourceWalk}; techniqueBinding={ff.TechniqueBindingProven}; verified={ff.Verified}");
            sb.AppendLine("  dependency-first Xbox plan:");
            foreach (var asset in ff.OutputOrder)
            {
                var deps = asset.Dependencies.Count == 0 ? string.Empty : " | deps=" + string.Join(", ", asset.Dependencies);
                var block = string.IsNullOrWhiteSpace(asset.BlockReason) ? string.Empty : " | BLOCK=" + asset.BlockReason;
                sb.AppendLine($"    #{asset.OutputIndex,-3} xboxType={asset.XboxTypeId,-2} {asset.TypeName,-12} {asset.Name} [{asset.Origin}/{asset.State}]{deps}{block}");
            }
            foreach (var note in ff.Warnings) sb.AppendLine("  NOTE: " + note);
        }

        if (result.ImageResolutions.Count != 0)
        {
            sb.AppendLine();
            sb.AppendLine("IMAGE SOURCE RESOLUTION");
            foreach (var image in result.ImageResolutions)
                sb.AppendLine($"  {image.ImageName}: {image.Resolution}" + (image.Source is null ? string.Empty : $" <- {image.Source}") + $" | {image.Note}");
        }

        foreach (var warning in result.Warnings) sb.AppendLine("WARNING: " + warning);
        return sb.ToString();
    }
}
