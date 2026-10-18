using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

/// <summary>
/// Builds one self-contained, human-sendable diagnostic report after a batch run.
/// The report intentionally contains no binary payloads: it records inventories,
/// exact byte sizes, hashes, wrapper/read-back facts, stage summaries/exceptions,
/// and the textual stage reports referenced by the current batch.
/// </summary>
public static class NormalMapConsolidatedDiagnostics
{
    private const long MiB = 1024L * 1024L;
    private static readonly Regex ReportPathRegex = new(@"report=([^;\r\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task<string> WriteAsync(
        MapCandidate map,
        string outputRoot,
        IReadOnlyList<string> batchSummary,
        DateTime runStarted,
        bool fastMode,
        CancellationToken cancellationToken = default)
    {
        var mapRoot = Path.Combine(outputRoot, map.Name);
        Directory.CreateDirectory(mapRoot);
        var outputPath = Path.Combine(mapRoot, "SEND_THIS_TO_CHATGPT.txt");
        var mapBatchSummary = ExtractMapBatchSummary(batchSummary, map.Name);

        var sb = new StringBuilder(256 * 1024);
        sb.AppendLine("WaW360Converter — ONE-FILE DIAGNOSTIC BUNDLE");
        sb.AppendLine(new string('=', 92));
        sb.AppendLine($"Diagnostic format: {(fastMode ? "Linker V2 alpha 5 structural-main native gate" : "Legacy Safe Mode full validation")}");
        sb.AppendLine($"Map: {map.Name}");
        sb.AppendLine($"Profile: {map.Profile}");
        sb.AppendLine($"Run started: {runStarted:O}");
        sb.AppendLine($"Bundle generated: {DateTime.Now:O}");
        sb.AppendLine($"Mode: {(fastMode ? "Fast Mode" : "Safe Mode (Full Validation)")}");
        sb.AppendLine($"Output root: {outputRoot}");
        sb.AppendLine($"Map output: {mapRoot}");
        sb.AppendLine($"Runtime: .NET {Environment.Version}; OS={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine();
        sb.AppendLine("SEND THIS ONE FILE AFTER A RUN. It contains the stage summary, failures, sizes, hashes, wrapper facts, and referenced TXT/JSON reports.");
        sb.AppendLine();

        var sourceFiles = EnumerateSourceFiles(map).ToList();
        sb.AppendLine("SOURCE INVENTORY");
        sb.AppendLine(new string('-', 92));
        if (sourceFiles.Count == 0)
        {
            sb.AppendLine("  <no top-level FF/IWD/deployment files found>");
        }
        else
        {
            foreach (var path in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fi = new FileInfo(path);
                var sha = await Sha256Async(path, cancellationToken);
                sb.AppendLine($"  {path}");
                sb.AppendLine($"    bytes={fi.Length:N0}; MiB={ToMiB(fi.Length):F3}; sha256={sha}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("GENERATED FASTFILE SIZE / WRAPPER CHECK");
        sb.AppendLine(new string('-', 92));
        sb.AppendLine("  Size sanity is informational. REVIEW_TOO_SMALL means the output is implausibly tiny for a large source FF; it is not a fidelity proof.");
        var candidates = EnumerateCandidateFastFiles(mapRoot, fastMode).ToList();
        // Multiple triage folders can intentionally contain byte-identical FFs. Probe each
        // unique SHA only once; this avoids repeatedly inflating the same multi-megabyte zone
        // during all-Normal diagnostics while preserving a line for every candidate path.
        var wrapperProbeBySha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (candidates.Count == 0)
        {
            sb.AppendLine("  <no generated candidate .ff files found>");
        }
        else
        {
            foreach (var path in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fi = new FileInfo(path);
                var source = FindMatchingSource(sourceFiles, fi.Name);
                var sourceBytes = source is null ? 0 : new FileInfo(source).Length;
                var ratio = sourceBytes <= 0 ? (double?)null : 100.0 * fi.Length / sourceBytes;
                var sanity = SizeSanity(sourceBytes, fi.Length);
                var sha = await Sha256Async(path, cancellationToken);
                var header = await ReadHeaderAsync(path, 16, cancellationToken);
                if (!wrapperProbeBySha.TryGetValue(sha, out var wrapper))
                {
                    wrapper = await ProbeWrapperAsync(path, cancellationToken);
                    wrapperProbeBySha[sha] = wrapper;
                }

                sb.AppendLine($"  {path}");
                sb.AppendLine($"    outputBytes={fi.Length:N0}; outputMiB={ToMiB(fi.Length):F3}; sizeCheck={sanity}");
                if (sourceBytes > 0)
                    sb.AppendLine($"    matchingSource={source}; sourceBytes={sourceBytes:N0}; sourceMiB={ToMiB(sourceBytes):F3}; output/source={ratio!.Value:F2}%");
                else
                    sb.AppendLine("    matchingSource=<none>");
                sb.AppendLine($"    sha256={sha}");
                sb.AppendLine($"    first16={Convert.ToHexString(header)}");
                sb.AppendLine($"    wrapper={wrapper}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("CURRENT BATCH / GUI STAGE SUMMARY");
        sb.AppendLine(new string('-', 92));
        foreach (var line in mapBatchSummary) sb.AppendLine(line);
        sb.AppendLine();

        var reportPaths = ExtractReferencedReports(mapBatchSummary)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Include each stage's JSON peer too. This makes the one-file report sufficient
        // even when a detail was omitted from the human-readable TXT renderer.
        foreach (var txt in reportPaths.ToList())
        {
            var json = Path.ChangeExtension(txt, ".json");
            if (File.Exists(json) && !reportPaths.Contains(json, StringComparer.OrdinalIgnoreCase))
                reportPaths.Add(json);
        }

        if (!fastMode)
        {
            var readme = Path.Combine(mapRoot, "hardware_candidates_minplay", "ALPHA4MP_README.txt");
            if (File.Exists(readme) && !reportPaths.Contains(readme, StringComparer.OrdinalIgnoreCase))
                reportPaths.Add(readme);

            var triageTxt = Path.Combine(mapRoot, "linker_v1_alpha4mp_minimum_playable", $"{map.Name}_linker_v1_alpha4mp3_tracked_family_triage.txt");
            var triageJson = Path.ChangeExtension(triageTxt, ".json");
            if (File.Exists(triageTxt) && !reportPaths.Contains(triageTxt, StringComparer.OrdinalIgnoreCase)) reportPaths.Add(triageTxt);
            if (File.Exists(triageJson) && !reportPaths.Contains(triageJson, StringComparer.OrdinalIgnoreCase)) reportPaths.Add(triageJson);
        }

        sb.AppendLine("REFERENCED STAGE REPORTS (FULL TEXT)");
        sb.AppendLine(new string('-', 92));
        if (reportPaths.Count == 0)
        {
            sb.AppendLine("  <no referenced stage reports found>");
        }
        else
        {
            foreach (var path in reportPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine();
                sb.AppendLine($">>>>> BEGIN REPORT: {path}");
                sb.AppendLine(new string('>', 92));
                try
                {
                    sb.AppendLine(await File.ReadAllTextAsync(path, cancellationToken));
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"<could not read report: {ex.GetType().Name}: {ex.Message}>");
                }
                sb.AppendLine(new string('<', 92));
                sb.AppendLine($"<<<<< END REPORT: {path}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("END ONE-FILE DIAGNOSTIC BUNDLE");
        await File.WriteAllTextAsync(outputPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        return outputPath;
    }

    private static IEnumerable<string> EnumerateSourceFiles(MapCandidate map)
    {
        var interesting = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ff", ".iwd", ".arena", ".json", ".dds", ".bin", ".txt"
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = map.SourceFolders
            .Concat(new[] { map.ModFolder, map.UsermapFolder }.OfType<string>())
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).ToList(); }
            catch { continue; }
            foreach (var path in files)
            {
                if (!interesting.Contains(Path.GetExtension(path))) continue;
                if (seen.Add(Path.GetFullPath(path))) yield return path;
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidateFastFiles(string mapRoot, bool fastMode)
    {
        if (!Directory.Exists(mapRoot)) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(mapRoot, "*.ff", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(mapRoot, path);
            var isV2 = rel.Contains("linker_v2", StringComparison.OrdinalIgnoreCase);
            if (fastMode)
            {
                // Fast Mode diagnostics must describe the CURRENT V2 run, not re-probe stale V1
                // smoke/minplay files left in the map output tree from earlier experiments.
                if (!isV2 && !rel.Contains("hardware_test_main_structural", StringComparison.OrdinalIgnoreCase)) continue;
            }
            else if (!rel.Contains("hardware_candidates", StringComparison.OrdinalIgnoreCase) &&
                     !rel.Contains("hardware_test_ready", StringComparison.OrdinalIgnoreCase) &&
                     !rel.Contains("linker_v1", StringComparison.OrdinalIgnoreCase) &&
                     !isV2)
            {
                continue;
            }
            if (seen.Add(Path.GetFullPath(path))) yield return path;
        }
    }

    private static string? FindMatchingSource(IReadOnlyList<string> sourceFiles, string fileName) =>
        sourceFiles.FirstOrDefault(x => Path.GetFileName(x).Equals(fileName, StringComparison.OrdinalIgnoreCase));

    private static string SizeSanity(long sourceBytes, long outputBytes)
    {
        if (outputBytes <= 12) return "REVIEW_EMPTY_OR_HEADER_ONLY";
        if (sourceBytes >= MiB && outputBytes < 256L * 1024L) return "REVIEW_TOO_SMALL";
        return "OK_INFO_ONLY";
    }


    private static IReadOnlyList<string> ExtractMapBatchSummary(IReadOnlyList<string> summary, string mapName)
    {
        var marker = "MAP: " + mapName;
        var start = -1;
        for (var i = 0; i < summary.Count; i++)
        {
            if (summary[i].Equals(marker, StringComparison.OrdinalIgnoreCase))
            {
                start = i;
                break;
            }
        }
        if (start < 0) return summary;

        var end = summary.Count;
        for (var i = start + 1; i < summary.Count; i++)
        {
            if (summary[i].StartsWith("MAP: ", StringComparison.Ordinal) ||
                summary[i].StartsWith("Finished:", StringComparison.Ordinal) ||
                summary[i].Equals("TIMING TELEMETRY", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }
        return summary.Skip(start).Take(end - start).ToArray();
    }

    private static IEnumerable<string> ExtractReferencedReports(IReadOnlyList<string> summary)
    {
        foreach (var line in summary)
        {
            foreach (Match match in ReportPathRegex.Matches(line))
            {
                var path = match.Groups[1].Value.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(path)) yield return path;
            }
        }
    }

    private static async Task<string> ProbeWrapperAsync(string fastFile, CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "WaW360Converter", "diagnostics", Guid.NewGuid().ToString("N") + ".zone");
        Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
        try
        {
            var d = await FastFileDecompressor.DecompressAutoAsync(fastFile, tmp, ct);
            var zoneBytes = File.Exists(tmp) ? new FileInfo(tmp).Length : 0;
            return $"platform={d.Platform}; compression={d.Compression}; zoneBytes={zoneBytes:N0}; zoneMiB={ToMiB(zoneBytes):F3}";
        }
        catch (Exception ex)
        {
            return $"PROBE_FAIL {ex.GetType().Name}: {ex.Message.Replace("\r", " ").Replace("\n", " ")}";
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static async Task<byte[]> ReadHeaderAsync(string path, int count, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var bytes = new byte[Math.Min(count, checked((int)Math.Min(fs.Length, count)))];
        if (bytes.Length == 0) return bytes;
        var read = await fs.ReadAsync(bytes.AsMemory(), ct);
        return read == bytes.Length ? bytes : bytes.AsSpan(0, read).ToArray();
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(fs, ct)).ToLowerInvariant();
    }

    private static double ToMiB(long bytes) => bytes / (1024.0 * 1024.0);
}
