using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.Maps;

/// <summary>
/// Persistent PASS-only cache used by Fast Mode.
/// Entries are keyed by a content fingerprint of the map runtime inputs plus an explicit
/// stage/dependency version. Safe Mode (Full Validation) ignores these entries completely.
/// </summary>
public static class BatchStageCache
{
    private const int SchemaVersion = 3;
    private static readonly object HashGate = new();
    private static readonly Dictionary<string, FileHashMemo> HashMemo = new(StringComparer.OrdinalIgnoreCase);

    public sealed record CachedStage(string Details, IReadOnlyList<string> OutputFiles, DateTime CreatedUtc);
    public sealed record FileStamp(long Length, long LastWriteTicks);
    private sealed record OutputArtifact(string Path, long Length, long LastWriteTicks);

    private sealed record StageEntry(
        int SchemaVersion,
        string MapFingerprint,
        string StageVersion,
        string ContextFingerprint,
        string Details,
        List<OutputArtifact> OutputFiles,
        DateTime CreatedUtc);

    private sealed record FileHashMemo(long Length, long LastWriteTicks, string Sha256);

    public static async Task<string> ComputeMapFingerprintAsync(MapCandidate map, CancellationToken cancellationToken = default)
    {
        var files = DiscoverRuntimeInputs(map);
        using var sha = SHA256.Create();
        foreach (var path in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            var fileHash = await GetFileHashAsync(path, cancellationToken);
            var line = $"{Path.GetFullPath(path).ToUpperInvariant()}|{info.Length}|{fileHash}\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        var profile = Encoding.UTF8.GetBytes($"PROFILE|{map.Profile}\n");
        sha.TransformFinalBlock(profile, 0, profile.Length);
        return Convert.ToHexString(sha.Hash!);
    }

    public static async Task<CachedStage?> TryGetAsync(
        string outputRoot,
        string mapName,
        string stageKey,
        string stageVersion,
        string mapFingerprint,
        string contextFingerprint = "",
        CancellationToken cancellationToken = default)
    {
        var path = EntryPath(outputRoot, mapName, stageKey);
        if (!File.Exists(path)) return null;
        try
        {
            var entry = JsonSerializer.Deserialize<StageEntry>(await File.ReadAllTextAsync(path, cancellationToken));
            if (entry is null || entry.SchemaVersion != SchemaVersion ||
                !entry.MapFingerprint.Equals(mapFingerprint, StringComparison.OrdinalIgnoreCase) ||
                !entry.StageVersion.Equals(stageVersion, StringComparison.Ordinal) ||
                !entry.ContextFingerprint.Equals(contextFingerprint ?? string.Empty, StringComparison.Ordinal))
                return null;

            if (entry.OutputFiles.Count == 0) return null;
            foreach (var artifact in entry.OutputFiles)
            {
                var info = new FileInfo(artifact.Path);
                if (!info.Exists || info.Length != artifact.Length || info.LastWriteTimeUtc.Ticks != artifact.LastWriteTicks)
                    return null;
            }

            return new CachedStage(entry.Details, entry.OutputFiles.Select(x => x.Path).ToList(), entry.CreatedUtc);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public static async Task PutPassAsync(
        string outputRoot,
        string mapName,
        string stageKey,
        string stageVersion,
        string mapFingerprint,
        string details,
        IReadOnlyList<string> outputFiles,
        string contextFingerprint = "",
        CancellationToken cancellationToken = default)
    {
        var path = EntryPath(outputRoot, mapName, stageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var entry = new StageEntry(
            SchemaVersion,
            mapFingerprint,
            stageVersion,
            contextFingerprint ?? string.Empty,
            details,
            outputFiles
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(x =>
                {
                    var info = new FileInfo(x);
                    return new OutputArtifact(Path.GetFullPath(x), info.Length, info.LastWriteTimeUtc.Ticks);
                })
                .ToList(),
            DateTime.UtcNow);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    public static IReadOnlyDictionary<string, FileStamp> SnapshotStageOutputs(string mapOutputRoot)
    {
        var snapshot = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(mapOutputRoot)) return snapshot;

        foreach (var path in Directory.EnumerateFiles(mapOutputRoot, "*", SearchOption.AllDirectories))
        {
            try
            {
                var full = Path.GetFullPath(path);
                // Never treat the PASS cache metadata itself as a stage artifact.
                if (full.Contains($"{Path.DirectorySeparatorChar}_analysis_cache{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    continue;
                var info = new FileInfo(full);
                snapshot[full] = new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks);
            }
            catch { }
        }
        return snapshot;
    }

    public static IReadOnlyList<string> CaptureStageOutputs(
        string mapOutputRoot,
        IReadOnlyDictionary<string, FileStamp> before)
    {
        if (!Directory.Exists(mapOutputRoot)) return Array.Empty<string>();

        var changed = new List<string>();
        foreach (var path in Directory.EnumerateFiles(mapOutputRoot, "*", SearchOption.AllDirectories))
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (full.Contains($"{Path.DirectorySeparatorChar}_analysis_cache{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    continue;

                var info = new FileInfo(full);
                if (!before.TryGetValue(full, out var old) ||
                    old.Length != info.Length ||
                    old.LastWriteTicks != info.LastWriteTimeUtc.Ticks)
                {
                    changed.Add(full);
                }
            }
            catch { }
        }
        return changed;
    }

    public static void ClearStageEntries(string outputRoot)
    {
        var path = Path.Combine(Path.GetFullPath(outputRoot), "_analysis_cache", "stage_results");
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static string EntryPath(string outputRoot, string mapName, string stageKey)
    {
        var safeMap = Sanitize(mapName);
        var safeStage = Sanitize(stageKey);
        return Path.Combine(Path.GetFullPath(outputRoot), "_analysis_cache", "stage_results", safeMap, safeStage + ".json");
    }

    private static List<string> DiscoverRuntimeInputs(MapCandidate map)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? p)
        {
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) set.Add(Path.GetFullPath(p));
        }
        Add(map.MapFastFile);
        Add(map.ModFastFile);
        Add(map.PatchFastFile);

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in map.SourceFolders) if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) folders.Add(Path.GetFullPath(p));
        if (!string.IsNullOrWhiteSpace(map.ModFolder) && Directory.Exists(map.ModFolder)) folders.Add(Path.GetFullPath(map.ModFolder));
        if (!string.IsNullOrWhiteSpace(map.UsermapFolder) && Directory.Exists(map.UsermapFolder)) folders.Add(Path.GetFullPath(map.UsermapFolder));
        foreach (var dir in folders)
        {
            foreach (var pattern in new[] { "*.ff", "*.iwd" })
                foreach (var p in Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly)) Add(p);
        }
        return set.ToList();
    }

    private static async Task<string> GetFileHashAsync(string path, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        lock (HashGate)
        {
            if (HashMemo.TryGetValue(full, out var memo) && memo.Length == info.Length && memo.LastWriteTicks == info.LastWriteTimeUtc.Ticks)
                return memo.Sha256;
        }

        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        lock (HashGate) HashMemo[full] = new FileHashMemo(info.Length, info.LastWriteTimeUtc.Ticks, hash);
        return hash;
    }

    private static string Sanitize(string s) => new(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());
}
