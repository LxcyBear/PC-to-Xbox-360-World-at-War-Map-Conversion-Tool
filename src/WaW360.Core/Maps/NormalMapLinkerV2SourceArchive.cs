using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record LinkerV2SourceContainer(
    string Role,
    string Path,
    string FileName,
    string BaseName,
    string Sha256,
    long CompressedBytes,
    long DecompressedBytes,
    string Platform,
    string Compression,
    int ScriptStrings,
    int Assets,
    int InlineSharedRoots,
    int InlineInsertRoots,
    int PackedAliasRoots,
    int NullRoots,
    IReadOnlyDictionary<string, int> AssetTypes,
    string GraphParticipation);

public sealed record LinkerV2SourceArchiveFile(
    string Role,
    string Path,
    string Sha256,
    long SizeBytes,
    string Disposition);

public sealed record LinkerV2OracleDelta(
    string AssetType,
    int PcAggregate,
    int XboxGolden,
    int Delta,
    string Interpretation);

public sealed class NormalMapLinkerV2SourceArchiveResult
{
    public required string MapName { get; init; }
    public required string CanonicalBaseName { get; init; }
    public required string PrimaryOutputFastFile { get; init; }
    public required string? SelectedLoadSource { get; init; }
    public required string? LoadOutputFastFile { get; init; }
    public required IReadOnlyList<string> RejectedLoadCandidates { get; init; }
    public required IReadOnlyList<LinkerV2SourceContainer> FastFiles { get; init; }
    public required IReadOnlyList<LinkerV2SourceArchiveFile> SourceArchive { get; init; }
    public required IReadOnlyDictionary<string, int> PrimaryGraphRootCounts { get; init; }
    public required int PrimaryGraphSourceRoots { get; init; }
    public required int PrimaryGraphSourceStrings { get; init; }
    public required bool UsesIwdAsSourceArchive { get; init; }
    public required int IwdFiles { get; init; }
    public required int IwdEntries { get; init; }
    public required bool ExternalSoundsExpected { get; init; }
    public required bool GoldenOracleApplied { get; init; }
    public required string? GoldenOracleName { get; init; }
    public required int? GoldenMainAssetCount { get; init; }
    public required int? GoldenMainScriptStrings { get; init; }
    public required IReadOnlyDictionary<string, uint>? GoldenBlockSizes { get; init; }
    public required IReadOnlyList<LinkerV2OracleDelta> GoldenAssetDeltas { get; init; }
    public required IReadOnlyList<string> PackagePlan { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 1 source-archive/canonical-graph foundation.
///
/// Architectural rule: PC container boundaries are INPUT provenance only. The final Xbox package
/// is planned from one canonical asset graph. map.ff, patch.ff, mod.ff and IWD contents can all
/// contribute to the canonical main graph; source containers are not blindly mirrored into the
/// Xbox output directory.
///
/// This stage deliberately does not guess asset identity. It records every authoritative XAsset
/// root and source archive, selects the canonical load FF by map identity, and exposes the exact
/// dependency-closure/dedup delta when a known golden oracle exists.
/// </summary>
public static class NormalMapLinkerV2SourceArchive
{
    public static async Task<NormalMapLinkerV2SourceArchiveResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_source_archive");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_source_archive.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_source_archive.json");

        var primaryPath = analysis.InputFiles
            .FirstOrDefault(x => x.Role.Equals("map.ff", StringComparison.OrdinalIgnoreCase))?.Path
            ?? map.MapFastFile;
        var canonicalBase = !string.IsNullOrWhiteSpace(primaryPath)
            ? Path.GetFileNameWithoutExtension(primaryPath)
            : map.Name;

        var containers = new List<LinkerV2SourceContainer>();
        foreach (var input in analysis.InputFiles.Where(x => x.Path.EndsWith(".ff", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(input.Path, cancellationToken);
            var doc = cached.Document;
            var roots = doc.Assets;
            var participation = input.Role.Equals("load.ff", StringComparison.OrdinalIgnoreCase)
                ? "LOAD_CANDIDATE"
                : input.Role is "map.ff" or "map_patch.ff" or "mod.ff"
                    ? "PRIMARY_GRAPH_SOURCE"
                    : "AUXILIARY_GRAPH_SOURCE";

            containers.Add(new LinkerV2SourceContainer(
                input.Role,
                input.Path,
                Path.GetFileName(input.Path),
                Path.GetFileNameWithoutExtension(input.Path),
                cached.SourceSha256.ToLowerInvariant(),
                new FileInfo(input.Path).Length,
                cached.Bytes.LongLength,
                doc.Platform.ToString(),
                doc.Compression.ToString(),
                doc.ScriptStringCount,
                doc.AssetCount,
                roots.Count(a => a.ReferenceKind == T4AssetReferenceKind.InlineShared),
                roots.Count(a => a.ReferenceKind == T4AssetReferenceKind.Inline),
                roots.Count(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias),
                roots.Count(a => a.ReferenceKind == T4AssetReferenceKind.Null),
                new Dictionary<string, int>(doc.AssetCountsByType, StringComparer.OrdinalIgnoreCase),
                participation));
        }

        var loadCandidates = containers
            .Where(x => x.GraphParticipation == "LOAD_CANDIDATE")
            .OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var exactLoadName = canonicalBase + "_load.ff";
        var selectedLoad = loadCandidates.FirstOrDefault(x => x.FileName.Equals(exactLoadName, StringComparison.OrdinalIgnoreCase));
        var notes = new List<string>();
        if (selectedLoad is null && loadCandidates.Count == 1)
        {
            selectedLoad = loadCandidates[0];
            notes.Add($"LOAD SELECTOR: no exact '{exactLoadName}' existed; the only load candidate '{selectedLoad.FileName}' was selected provisionally.");
        }
        else if (selectedLoad is null && loadCandidates.Count > 1)
        {
            notes.Add($"LOAD SELECTOR: {loadCandidates.Count:N0} load FFs exist and none matches canonical primary base '{canonicalBase}'. No load FF is selected until identity is resolved.");
        }
        else if (selectedLoad is not null)
        {
            notes.Add($"LOAD SELECTOR: '{selectedLoad.FileName}' matches canonical primary base '{canonicalBase}' and is the production load-FF candidate.");
        }

        var rejectedLoads = loadCandidates
            .Where(x => selectedLoad is null || !x.Path.Equals(selectedLoad.Path, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Path)
            .ToList();
        foreach (var rejected in rejectedLoads)
            notes.Add($"SOURCE-ONLY LOAD: '{Path.GetFileName(rejected)}' remains available to dependency analysis but is not planned as a final Xbox package file.");

        var primaryGraphContainers = containers
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .ToList();
        var primaryCounts = SumCounts(primaryGraphContainers.Select(x => x.AssetTypes));
        var primaryRoots = primaryGraphContainers.Sum(x => x.Assets);
        var primaryStrings = primaryGraphContainers.Sum(x => x.ScriptStrings);

        var sourceArchive = new List<LinkerV2SourceArchiveFile>();
        foreach (var input in analysis.InputFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var disposition = input.Role switch
            {
                "iwd" => "SOURCE_ARCHIVE_ONLY: consume textures/scripts/weapons/audio/stringtables as linker dependencies; do not copy IWD to final Xbox package",
                "load.ff" when selectedLoad is not null && input.Path.Equals(selectedLoad.Path, StringComparison.OrdinalIgnoreCase) => "SELECTED_LOAD_SOURCE",
                "load.ff" => "SOURCE_ARCHIVE_ONLY: non-canonical load candidate",
                "map.ff" or "map_patch.ff" or "mod.ff" => "PRIMARY_GRAPH_SOURCE: merge into canonical Xbox main graph",
                _ => "SOURCE_ARCHIVE"
            };
            sourceArchive.Add(new LinkerV2SourceArchiveFile(
                input.Role,
                input.Path,
                await Sha256Async(input.Path, cancellationToken),
                input.SizeBytes,
                disposition));
        }

        // Also preserve top-level deployment/metadata files as provenance. They are not parsed as assets.
        foreach (var path in EnumerateTopLevelMetadata(map))
        {
            if (sourceArchive.Any(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            sourceArchive.Add(new LinkerV2SourceArchiveFile(
                "metadata",
                path,
                await Sha256Async(path, cancellationToken),
                new FileInfo(path).Length,
                "PACKAGE_METADATA_SOURCE: may be transformed/copied only by the packaging layer"));
        }

        var iwdFiles = analysis.Iwds.Count;
        var iwdEntries = analysis.Iwds.Sum(x => x.EntryCount);
        var iwdSoundEntries = analysis.Iwds.Sum(x => x.Categories.TryGetValue("sound", out var n) ? n : 0);
        var externalSoundsExpected = primaryCounts.TryGetValue("sound", out var soundRoots) && soundRoots > 0 || iwdSoundEntries > 0;

        var golden = LinkerV2GoldenCatalog.Find(canonicalBase);
        var goldenDeltas = new List<LinkerV2OracleDelta>();
        if (golden is not null)
        {
            var keys = primaryCounts.Keys.Union(golden.MainAssetCounts.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                primaryCounts.TryGetValue(key, out var pc);
                golden.MainAssetCounts.TryGetValue(key, out var xbox);
                var delta = xbox - pc;
                var interpretation = delta switch
                {
                    > 0 => "dependency closure/promotion required (golden has more canonical roots than the raw PC container sum)",
                    < 0 => "deduplication/override/canonicalization required (golden has fewer canonical roots than the raw PC container sum)",
                    _ => "top-level family count already matches golden after source-container aggregation"
                };
                goldenDeltas.Add(new LinkerV2OracleDelta(key, pc, xbox, delta, interpretation));
            }
            notes.Add($"GOLDEN ORACLE: {golden.Name}; main={golden.MainFastFileBytes:N0}B compressed/{golden.MainZoneBytes:N0}B zone, strings={golden.MainScriptStrings:N0}, XAssets={golden.MainAssets:N0}.");
            notes.Add($"GOLDEN ORACLE: reference package has {golden.XmaFiles:N0} external XMA file(s) / {golden.XmaBytes:N0} bytes. Reference payload bytes are NOT embedded or copied by Linker V2.");
            if (canonicalBase.Equals("simpsons", StringComparison.OrdinalIgnoreCase))
                notes.Add("GOLDEN ORACLE: Simpsons working output proves mod.ff/patch.ff/IWD/alternate Bocage load FF are source inputs, not required one-for-one Xbox package outputs.");
        }

        var packagePlan = new List<string>
        {
            canonicalBase + ".ff  <- canonical merged primary graph (NOT a byte-for-byte mirror of any one PC FF)"
        };
        if (selectedLoad is not null)
            packagePlan.Add(canonicalBase + "_load.ff <- selected canonical load graph: " + selectedLoad.FileName);
        if (externalSoundsExpected)
            packagePlan.Add("sounds\\ <- external Xbox SDNS/XMA sound payloads referenced by canonical Sound assets");
        packagePlan.Add("preview/description metadata <- packaging layer (not part of XAsset graph correctness)");

        var blockers = new List<string>();
        if (primaryPath is null)
            blockers.Add("No primary map.ff was identified; canonical Xbox main output cannot be named safely.");
        if (loadCandidates.Count > 1 && selectedLoad is null)
            blockers.Add("Multiple load FF candidates exist without an exact canonical-base match.");
        blockers.Add("Asset identity merge/override resolution is not complete yet; raw root counts must not be treated as final Xbox root counts.");
        blockers.Add("Owned dependency closure is not complete yet; promoted dependencies must be inserted deterministically from typed root walkers.");
        blockers.Add("Real Xbox block ownership + packed/shared target allocation/rebasing is required before main.ff emission. Self-contained Temp-only flattening is retired from the production path.");
        if (externalSoundsExpected)
            blockers.Add("Final audio path requires Xbox SDNS/XMA emission; silent Sound shells may be used only as an explicitly labeled hardware diagnostic, never strict output.");

        var result = new NormalMapLinkerV2SourceArchiveResult
        {
            MapName = map.Name,
            CanonicalBaseName = canonicalBase,
            PrimaryOutputFastFile = canonicalBase + ".ff",
            SelectedLoadSource = selectedLoad?.Path,
            LoadOutputFastFile = selectedLoad is null ? null : canonicalBase + "_load.ff",
            RejectedLoadCandidates = rejectedLoads,
            FastFiles = containers,
            SourceArchive = sourceArchive.OrderBy(x => x.Role).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            PrimaryGraphRootCounts = primaryCounts,
            PrimaryGraphSourceRoots = primaryRoots,
            PrimaryGraphSourceStrings = primaryStrings,
            UsesIwdAsSourceArchive = iwdFiles > 0,
            IwdFiles = iwdFiles,
            IwdEntries = iwdEntries,
            ExternalSoundsExpected = externalSoundsExpected,
            GoldenOracleApplied = golden is not null,
            GoldenOracleName = golden?.Name,
            GoldenMainAssetCount = golden?.MainAssets,
            GoldenMainScriptStrings = golden?.MainScriptStrings,
            GoldenBlockSizes = golden?.MainBlockSizes,
            GoldenAssetDeltas = goldenDeltas,
            PackagePlan = packagePlan,
            Blockers = blockers,
            Notes = notes,
            Verified = containers.All(x => x.Platform.Equals("PC", StringComparison.OrdinalIgnoreCase)) && primaryPath is not null,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private static Dictionary<string, int> SumCounts(IEnumerable<IReadOnlyDictionary<string, int>> dictionaries)
    {
        var sum = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var dict in dictionaries)
            foreach (var pair in dict)
                sum[pair.Key] = sum.GetValueOrDefault(pair.Key) + pair.Value;
        return sum;
    }

    private static IEnumerable<string> EnumerateTopLevelMetadata(MapCandidate map)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".arena", ".json", ".dds", ".bin", ".txt" };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extraFolders = new[] { map.ModFolder, map.UsermapFolder }.OfType<string>();
        var folders = map.SourceFolders
            .Concat(extraFolders)
            .Where(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (var path in files)
            {
                if (!extensions.Contains(Path.GetExtension(path))) continue;
                var full = Path.GetFullPath(path);
                if (seen.Add(full)) yield return full;
            }
        }
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
    }

    private static string Render(NormalMapLinkerV2SourceArchiveResult r)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 1 canonical source archive + package graph");
        sb.AppendLine(new string('=', 98));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Canonical primary identity: {r.CanonicalBaseName}");
        sb.AppendLine($"Planned Xbox main FF: {r.PrimaryOutputFastFile}");
        sb.AppendLine($"Selected load source: {r.SelectedLoadSource ?? "<none>"}");
        sb.AppendLine($"Planned Xbox load FF: {r.LoadOutputFastFile ?? "<none>"}");
        sb.AppendLine($"Primary-graph source roots: {r.PrimaryGraphSourceRoots:N0}; source script strings: {r.PrimaryGraphSourceStrings:N0}");
        sb.AppendLine($"IWD source archives: {r.IwdFiles:N0}; entries={r.IwdEntries:N0}; copied-to-Xbox=False");
        sb.AppendLine($"External Xbox sounds expected: {r.ExternalSoundsExpected}");
        sb.AppendLine($"Verified source inventory: {r.Verified}");
        sb.AppendLine();
        sb.AppendLine("ARCHITECTURE RULE");
        sb.AppendLine("  PC FF/IWD boundaries are provenance, not final-package boundaries.");
        sb.AppendLine("  Build one canonical graph, apply identity/override resolution, walk typed dependencies, deduplicate, then serialize to Xbox blocks.");
        sb.AppendLine("  Production Linker V2 must preserve packed/shared ownership and aliases. Nulling escaping references / Temp-only self-contained flattening is retired.");
        sb.AppendLine();

        sb.AppendLine("SOURCE FASTFILES");
        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"  [{ff.Role}] {ff.FileName} | {ff.GraphParticipation}");
            sb.AppendLine($"    sha256={ff.Sha256}; compressed={ff.CompressedBytes:N0}; zone={ff.DecompressedBytes:N0}; strings={ff.ScriptStrings:N0}; assets={ff.Assets:N0}");
            sb.AppendLine($"    roots: inlineShared={ff.InlineSharedRoots:N0}; insert={ff.InlineInsertRoots:N0}; packedAlias={ff.PackedAliasRoots:N0}; null={ff.NullRoots:N0}");
            sb.AppendLine("    types: " + string.Join(", ", ff.AssetTypes.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => $"{x.Key}={x.Value:N0}")));
        }
        sb.AppendLine();

        sb.AppendLine("PRIMARY GRAPH RAW ROOT AGGREGATE (PRE-IDENTITY-CLOSURE; NOT FINAL XBOX COUNTS)");
        sb.AppendLine("  " + string.Join(", ", r.PrimaryGraphRootCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => $"{x.Key}={x.Value:N0}")));
        sb.AppendLine();

        if (r.GoldenOracleApplied)
        {
            sb.AppendLine($"KNOWN-WORKING XBOX GOLDEN ORACLE — {r.GoldenOracleName}");
            sb.AppendLine($"  golden main roots={r.GoldenMainAssetCount:N0}; scriptStrings={r.GoldenMainScriptStrings:N0}");
            sb.AppendLine("  blocks: " + string.Join(", ", r.GoldenBlockSizes!.Select(x => $"{x.Key}={x.Value:N0}")));
            sb.AppendLine("  sourceAggregate -> goldenCanonical deltas:");
            foreach (var d in r.GoldenAssetDeltas.OrderByDescending(x => Math.Abs(x.Delta)).ThenBy(x => x.AssetType))
                sb.AppendLine($"    {d.AssetType,-16} PCsum={d.PcAggregate,6:N0} golden={d.XboxGolden,6:N0} delta={d.Delta,6:+0;-0;0} | {d.Interpretation}");
            sb.AppendLine("  IMPORTANT: golden counts validate only the matching map identity; they are never hardcoded as conversion targets for unrelated maps.");
            sb.AppendLine();
        }

        sb.AppendLine("FINAL PACKAGE PLAN");
        foreach (var line in r.PackagePlan) sb.AppendLine("  " + line);
        sb.AppendLine();

        sb.AppendLine("SOURCE ARCHIVE MANIFEST");
        foreach (var f in r.SourceArchive)
            sb.AppendLine($"  [{f.Role}] {f.Path} | {f.SizeBytes:N0} B | {f.Sha256} | {f.Disposition}");
        sb.AppendLine();

        sb.AppendLine("CURRENT PRODUCTION BLOCKERS");
        foreach (var blocker in r.Blockers) sb.AppendLine("  BLOCKER: " + blocker);
        foreach (var note in r.Notes) sb.AppendLine("  NOTE: " + note);
        return sb.ToString();
    }
}
