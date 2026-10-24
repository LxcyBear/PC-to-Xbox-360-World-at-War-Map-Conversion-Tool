using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WaW360.Core.Maps;

/// <summary>
/// Alpha 4MP4: produces ONE sendable dataset for the complete selected Normal-map batch.
/// The compact front matter clusters shared failures before embedding each map's one-file
/// diagnostics. The repeated global batch section is stripped from embedded map bundles so
/// an all-map run stays useful instead of exploding with N copies of the same batch log.
/// </summary>
public static class NormalMapMasterDiagnostics
{
    private static readonly HashSet<string> TextArtifactExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".json", ".log", ".csv", ".md", ".xml"
    };

    private sealed record Failure(string Map, string Stage, string Detail, string Signature);
    private sealed record MapCounts(string Map, int Pass, int Fail, int Cached, string? HybridDetail);

    public static async Task<string> WriteAsync(
        IReadOnlyList<MapCandidate> maps,
        string outputRoot,
        IReadOnlyList<string> batchSummary,
        string batchReportPath,
        DateTime runStarted,
        bool fastMode,
        CancellationToken cancellationToken = default)
    {
        var batchDir = Path.Combine(outputRoot, "_batch_reports");
        Directory.CreateDirectory(batchDir);
        var outputPath = Path.Combine(batchDir, "SEND_THIS_TO_CHATGPT_ALL_NORMAL_MAPS.txt");

        var failures = ParseFailures(batchSummary);
        var counts = ParseMapCounts(batchSummary);
        var clusters = failures
            .GroupBy(x => x.Signature, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Select(x => x.Map).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            .ThenByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var productionBlockers = await ReadV2ProductionBlockersAsync(maps, outputRoot, cancellationToken);

        var sb = new StringBuilder(2 * 1024 * 1024);
        sb.AppendLine("WaW360Converter — ALL NORMAL MAPS MASTER DIAGNOSTIC DATASET");
        sb.AppendLine(new string('=', 108));
        sb.AppendLine($"Diagnostic format: {(fastMode ? "Linker V2 alpha 5 structural-main native gate" : "Legacy Safe Mode full validation")}");
        sb.AppendLine($"Run started: {runStarted:O}");
        sb.AppendLine($"Master generated: {DateTime.Now:O}");
        sb.AppendLine($"Mode: {(fastMode ? "Fast Mode" : "Safe Mode (Full Validation)")}");
        sb.AppendLine($"Maps in this dataset: {maps.Count:N0}");
        sb.AppendLine($"Output root: {outputRoot}");
        sb.AppendLine($"Batch report: {batchReportPath}");
        sb.AppendLine($"Runtime: .NET {Environment.Version}; OS={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine();
        sb.AppendLine("SEND THIS ONE FILE AFTER AN ALL-NORMAL RUN. It clusters shared failures first, then contains the complete batch report and every map's detailed diagnostic bundle.");
        sb.AppendLine();

        sb.AppendLine("MAP MATRIX");
        sb.AppendLine(new string('-', 108));
        foreach (var map in maps.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var c = counts.FirstOrDefault(x => x.Map.Equals(map.Name, StringComparison.OrdinalIgnoreCase));
            if (c is null)
            {
                sb.AppendLine($"  {map.Name,-36} PASS=? FAIL=? CACHED=?");
                continue;
            }
            sb.AppendLine($"  {map.Name,-36} PASS={c.Pass,-3} FAIL={c.Fail,-3} CACHED={c.Cached,-3}");
            if (!string.IsNullOrWhiteSpace(c.HybridDetail))
                sb.AppendLine($"    FINAL: {c.HybridDetail}");
        }
        sb.AppendLine();

        sb.AppendLine("CROSS-MAP FAILURE CLUSTERS");
        sb.AppendLine(new string('-', 108));
        sb.AppendLine("Clusters normalize map names, Windows paths, hashes, and numeric values so the same parser/linker bug across many maps appears once.");
        if (clusters.Count == 0)
        {
            sb.AppendLine("  <no failed stages in this batch>");
        }
        else
        {
            var index = 1;
            foreach (var g in clusters)
            {
                var uniqueMaps = g.Select(x => x.Map).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                var first = g.First();
                sb.AppendLine($"  [{index++:D2}] maps={uniqueMaps.Length:N0}; failures={g.Count():N0}; stage={first.Stage}");
                sb.AppendLine($"       signature={g.Key}");
                sb.AppendLine($"       maps={string.Join(", ", uniqueMaps)}");
                sb.AppendLine($"       example={first.Detail}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("CROSS-MAP LINKER V2 PRODUCTION BLOCKERS");
        sb.AppendLine(new string('-', 108));
        if (productionBlockers.Count == 0)
        {
            sb.AppendLine("  <no Linker V2 source-archive blocker reports found>");
        }
        else
        {
            var blockerGroups = productionBlockers
                .GroupBy(x => x.Blocker, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Select(x => x.Map).Distinct(StringComparer.OrdinalIgnoreCase).Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
            var blockerIndex = 1;
            foreach (var g in blockerGroups)
            {
                var affected = g.Select(x => x.Map).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                sb.AppendLine($"  [{blockerIndex++:D2}] maps={affected.Length:N0}: {g.Key}");
                sb.AppendLine($"       maps={string.Join(", ", affected)}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("ALL FAILED STAGES BY MAP");
        sb.AppendLine(new string('-', 108));
        if (failures.Count == 0)
        {
            sb.AppendLine("  <none>");
        }
        else
        {
            foreach (var mapGroup in failures.GroupBy(x => x.Map, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[{mapGroup.Key}]");
                foreach (var f in mapGroup) sb.AppendLine($"  FAIL {f.Stage}: {f.Detail}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("COMPLETE BATCH REPORT");
        sb.AppendLine(new string('-', 108));
        foreach (var line in batchSummary) sb.AppendLine(line);
        sb.AppendLine();

        sb.AppendLine("PER-MAP FULL DIAGNOSTIC BUNDLES");
        sb.AppendLine(new string('-', 108));
        foreach (var map in maps.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(outputRoot, map.Name, "SEND_THIS_TO_CHATGPT.txt");
            sb.AppendLine();
            sb.AppendLine($">>>>> BEGIN MAP BUNDLE: {map.Name} | {path}");
            sb.AppendLine(new string('>', 108));
            if (!File.Exists(path))
            {
                sb.AppendLine("<per-map one-file diagnostic was not generated>");
            }
            else
            {
                try
                {
                    var text = await File.ReadAllTextAsync(path, cancellationToken);
                    sb.Append(StripRepeatedBatchSection(text));
                    if (!text.EndsWith('\n')) sb.AppendLine();
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"<could not read map bundle: {ex.GetType().Name}: {ex.Message}>");
                }
            }
            sb.AppendLine(new string('<', 108));
            sb.AppendLine($"<<<<< END MAP BUNDLE: {map.Name}");
        }

        sb.AppendLine();
        sb.AppendLine("FORENSIC ALL-MAP ARTIFACT INDEX");
        sb.AppendLine(new string('-', 108));
        sb.AppendLine("This section is exhaustive for generated map output files. Binary files are represented by path/size/SHA-256; textual diagnostics are embedded verbatim in the next section.");
        foreach (var map in maps.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AppendGeneratedArtifactIndexAsync(sb, map, outputRoot, cancellationToken);
        }

        sb.AppendLine();
        sb.AppendLine("SOURCE TREE MANIFESTS");
        sb.AppendLine(new string('-', 108));
        sb.AppendLine("Source payload bytes are never embedded here. Every source path and size is recorded recursively so missing audio/mod/IWD support files are visible without bloating the sendable dataset.");
        foreach (var map in maps.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendSourceTreeManifest(sb, map);
        }

        sb.AppendLine();
        sb.AppendLine("ALL GENERATED TEXTUAL DIAGNOSTICS (RAW, EXHAUSTIVE)");
        sb.AppendLine(new string('-', 108));
        sb.AppendLine("Unlike the per-map bundle above, this sweep does not depend on a stage having referenced report=<path>. Every generated TXT/JSON/LOG/CSV/MD/XML artifact is included.");
        foreach (var map in maps.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AppendAllGeneratedTextArtifactsAsync(sb, map, outputRoot, cancellationToken);
        }

        sb.AppendLine();
        sb.AppendLine("END ALL NORMAL MAPS MASTER DIAGNOSTIC DATASET");
        await File.WriteAllTextAsync(outputPath, sb.ToString(), new UTF8Encoding(false), cancellationToken);
        return outputPath;
    }

    private static async Task AppendGeneratedArtifactIndexAsync(
        StringBuilder sb,
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(outputRoot, map.Name);
        sb.AppendLine();
        sb.AppendLine($"[{map.Name}] generated artifacts");
        if (!Directory.Exists(root))
        {
            sb.AppendLine("  <map output directory missing>");
            return;
        }

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(p => !Path.GetFileName(p).StartsWith("SEND_THIS_TO_CHATGPT", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => Path.GetRelativePath(root, p), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  <enumeration failed: {ex.GetType().Name}: {ex.Message}>");
            return;
        }

        long total = 0;
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fi = new FileInfo(path);
            total += fi.Length;
            var rel = Path.GetRelativePath(root, path);
            string sha;
            try { sha = await Sha256Async(path, cancellationToken); }
            catch (Exception ex) { sha = $"HASH_FAIL:{ex.GetType().Name}:{ex.Message}"; }
            sb.AppendLine($"  {rel} | bytes={fi.Length:N0} | sha256={sha}");
        }
        sb.AppendLine($"  TOTAL files={files.Count:N0}; bytes={total:N0}");
    }

    private static void AppendSourceTreeManifest(StringBuilder sb, MapCandidate map)
    {
        sb.AppendLine();
        sb.AppendLine($"[{map.Name}] source trees");
        var roots = map.SourceFolders
            .Concat(new[] { map.ModFolder, map.UsermapFolder }.OfType<string>())
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roots.Count == 0)
        {
            sb.AppendLine("  <no source directories>");
            return;
        }

        foreach (var root in roots)
        {
            sb.AppendLine($"  ROOT {root}");
            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .OrderBy(x => Path.GetRelativePath(root, x), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    <enumeration failed: {ex.GetType().Name}: {ex.Message}>");
                continue;
            }

            var extStats = files
                .GroupBy(x => Path.GetExtension(x).Length == 0 ? "<none>" : Path.GetExtension(x), StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Extension = g.Key, Count = g.Count(), Bytes = g.Sum(x => new FileInfo(x).Length) })
                .OrderByDescending(x => x.Bytes)
                .ThenBy(x => x.Extension, StringComparer.OrdinalIgnoreCase)
                .ToList();
            sb.AppendLine("    EXTENSIONS " + string.Join(" | ", extStats.Select(x => $"{x.Extension}:{x.Count:N0}/{x.Bytes:N0}B")));

            foreach (var path in files)
            {
                try
                {
                    var fi = new FileInfo(path);
                    sb.AppendLine($"    {Path.GetRelativePath(root, path)} | bytes={fi.Length:N0}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"    {Path.GetRelativePath(root, path)} | STAT_FAIL:{ex.GetType().Name}:{ex.Message}");
                }
            }
        }
    }

    private static async Task AppendAllGeneratedTextArtifactsAsync(
        StringBuilder sb,
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(outputRoot, map.Name);
        sb.AppendLine();
        sb.AppendLine($">>>>> BEGIN EXHAUSTIVE TEXT ARTIFACTS: {map.Name}");
        sb.AppendLine(new string('>', 108));
        if (!Directory.Exists(root))
        {
            sb.AppendLine("<map output directory missing>");
        }
        else
        {
            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(p => TextArtifactExtensions.Contains(Path.GetExtension(p)))
                    .Where(p => !Path.GetFileName(p).StartsWith("SEND_THIS_TO_CHATGPT", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => Path.GetRelativePath(root, p), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                files = new List<string>();
                sb.AppendLine($"<text-artifact enumeration failed: {ex.GetType().Name}: {ex.Message}>");
            }

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(root, path);
                sb.AppendLine();
                sb.AppendLine($">>>>> BEGIN RAW ARTIFACT: {rel}");
                try
                {
                    sb.AppendLine(await File.ReadAllTextAsync(path, cancellationToken));
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"<could not read artifact: {ex.GetType().Name}: {ex.Message}>");
                }
                sb.AppendLine($"<<<<< END RAW ARTIFACT: {rel}");
            }
        }
        sb.AppendLine(new string('<', 108));
        sb.AppendLine($"<<<<< END EXHAUSTIVE TEXT ARTIFACTS: {map.Name}");
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(fs, cancellationToken)).ToLowerInvariant();
    }

    private static List<Failure> ParseFailures(IReadOnlyList<string> lines)
    {
        var output = new List<Failure>();
        string currentMap = "<unknown>";
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.StartsWith("MAP: ", StringComparison.Ordinal))
            {
                currentMap = line[5..].Trim();
                continue;
            }
            if (!line.StartsWith("[FAIL] ", StringComparison.Ordinal)) continue;
            var stage = line[7..].Trim();
            var detail = "<no detail>";
            for (var j = i + 1; j < Math.Min(lines.Count, i + 8); j++)
            {
                var candidate = lines[j].Trim();
                if (candidate.Length == 0 || candidate.StartsWith("elapsed=", StringComparison.OrdinalIgnoreCase) || candidate.Equals("exception:", StringComparison.OrdinalIgnoreCase)) continue;
                if (candidate.StartsWith("[PASS", StringComparison.Ordinal) || candidate.StartsWith("[FAIL]", StringComparison.Ordinal) || candidate.StartsWith("MAP: ", StringComparison.Ordinal)) break;
                detail = candidate;
                break;
            }
            output.Add(new Failure(currentMap, stage, detail, NormalizeSignature(stage + " | " + detail, currentMap)));
        }
        return output;
    }

    private static List<MapCounts> ParseMapCounts(IReadOnlyList<string> lines)
    {
        var output = new List<MapCounts>();
        string? map = null;
        var pass = 0;
        var fail = 0;
        var cached = 0;
        string? final = null;

        void Flush()
        {
            if (map is not null) output.Add(new MapCounts(map, pass, fail, cached, final));
            pass = fail = cached = 0;
            final = null;
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("MAP: ", StringComparison.Ordinal))
            {
                Flush();
                map = line[5..].Trim();
                continue;
            }
            if (map is null) continue;
            if (line.StartsWith("[PASS CACHED] ", StringComparison.Ordinal)) { pass++; cached++; continue; }
            if (line.StartsWith("[PASS] ", StringComparison.Ordinal)) { pass++; continue; }
            if (line.StartsWith("[FAIL] ", StringComparison.Ordinal)) { fail++; continue; }
            if (line.Contains("COMPLETE_HYBRID_SET=", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("sizes=[", StringComparison.OrdinalIgnoreCase) ||
                (line.Contains("base=", StringComparison.OrdinalIgnoreCase) && line.Contains("blockers=", StringComparison.OrdinalIgnoreCase)) ||
                line.Contains("structuralReady=", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("nativeCandidate=", StringComparison.OrdinalIgnoreCase))
                final = line.Trim();
        }
        Flush();
        return output;
    }

    private static string NormalizeSignature(string input, string map)
    {
        var s = input.Replace(map, "<map>", StringComparison.OrdinalIgnoreCase);
        s = Regex.Replace(s, @"[A-Za-z]:\\[^;\r\n]+", "<path>");
        s = Regex.Replace(s, @"\b[a-fA-F0-9]{32,64}\b", "<hash>");
        s = Regex.Replace(s, @"\b\d[\d,]*(?:\.\d+)?\b", "<n>");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private sealed record V2ProductionBlocker(string Map, string Blocker);

    private static async Task<List<V2ProductionBlocker>> ReadV2ProductionBlockersAsync(
        IReadOnlyList<MapCandidate> maps,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var output = new List<V2ProductionBlocker>();
        foreach (var map in maps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mainGatePath = Path.Combine(outputRoot, map.Name, "linker_v2_main_gate", $"{map.Name}_linker_v2_main_gate.json");
            var sourcePath = Path.Combine(outputRoot, map.Name, "linker_v2_source_archive", $"{map.Name}_linker_v2_source_archive.json");
            try
            {
                if (File.Exists(mainGatePath))
                {
                    var gate = System.Text.Json.JsonSerializer.Deserialize<NormalMapLinkerV2MainReadinessResult>(
                        await File.ReadAllTextAsync(mainGatePath, cancellationToken));
                    if (gate is not null)
                    {
                        foreach (var blocker in gate.Blockers.Distinct(StringComparer.OrdinalIgnoreCase))
                            output.Add(new V2ProductionBlocker(map.Name, blocker));
                        continue;
                    }
                }

                if (!File.Exists(sourcePath)) continue;
                var result = System.Text.Json.JsonSerializer.Deserialize<NormalMapLinkerV2SourceArchiveResult>(
                    await File.ReadAllTextAsync(sourcePath, cancellationToken));
                if (result is null) continue;
                foreach (var blocker in result.Blockers.Distinct(StringComparer.OrdinalIgnoreCase))
                    output.Add(new V2ProductionBlocker(map.Name, blocker));
            }
            catch
            {
                // The master dataset must still be produced if one optional blocker JSON is stale/corrupt.
            }
        }
        return output;
    }

    private static string StripRepeatedBatchSection(string text)
    {
        const string startMarker = "CURRENT BATCH / GUI STAGE SUMMARY";
        const string endMarker = "REFERENCED STAGE REPORTS (FULL TEXT)";
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return text;
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0) return text;
        var prefix = text[..start];
        var suffix = text[end..];
        return prefix + "CURRENT BATCH / GUI STAGE SUMMARY\r\n<omitted here; the master batch report appears once above>\r\n\r\n" + suffix;
    }
}
