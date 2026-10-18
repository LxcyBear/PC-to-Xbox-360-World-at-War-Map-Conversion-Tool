using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Content-addressed, read-only cache for decompressed WaW fastfiles.
///
/// Safety rules:
/// - Cache identity is the SHA-256 of the compressed source file.
/// - Cached zone bytes are never mutated by callers.
/// - A cached zone is accepted only when metadata, size and parsed zone header are valid.
/// - Proof/verification outputs are NOT cached here; this only removes repeated decompression/I/O.
/// </summary>
public static class FastFileAnalysisCache
{
    private const int SchemaVersion = 1;
    private const long MaxPersistentZoneBytes = 8L * 1024 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, MemoryEntry> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SourceHashEntry> SourceHashes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<object>> DerivedAnalysis = new(StringComparer.OrdinalIgnoreCase);

    private static string _cacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WaW360Converter", "analysis_cache");
    private static long _memoryBudgetBytes = 200L * 1024 * 1024;
    private static long _memoryBytes;
    private static long _memoryHits;
    private static long _diskHits;
    private static long _misses;
    private static long _decompressedBytesAvoided;
    private static long _derivedHits;
    private static long _derivedPersistentHits;
    private static bool _allowPersistentDerivedReads = true;

    public sealed record CachedZone(
        string SourceFastFile,
        string SourceSha256,
        byte[] Bytes,
        WaWPlatform Platform,
        WaWCompressionKind Compression,
        ZoneHeaderSummary ZoneHeader,
        T4ZoneDocument Document,
        bool MemoryCacheHit,
        bool PersistentCacheHit);

    public sealed record CacheStats(
        long MemoryHits,
        long PersistentHits,
        long Misses,
        long MemoryBytes,
        long MemoryBudgetBytes,
        long DecompressedBytesAvoided,
        long DerivedHits,
        long DerivedPersistentHits);

    private sealed record CacheMetadata(
        int SchemaVersion,
        string SourceSha256,
        long SourceLength,
        long DecompressedLength,
        WaWPlatform Platform,
        WaWCompressionKind Compression);

    private sealed class MemoryEntry
    {
        public required CachedZone Zone { get; init; }
        public DateTime LastAccessUtc { get; set; }
    }

    private sealed record SourceHashEntry(long Length, long LastWriteTicks, string Sha256);

    public static string CacheRoot
    {
        get { lock (Gate) return _cacheRoot; }
    }

    public static void Configure(string outputRoot, int memoryBudgetMb)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("Output root is required for the analysis cache.", nameof(outputRoot));

        // Keep the large decompressed/derived cache out of the output tree. The
        // user's output currently lives under OneDrive, and syncing multi-hundred-MB
        // disposable zone caches would destroy performance. Stage PASS metadata remains
        // under outputRoot; only this large read-only analysis cache lives in LocalAppData.
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WaW360Converter", "analysis_cache");
        var budget = Math.Max(32, memoryBudgetMb) * 1024L * 1024L;
        lock (Gate)
        {
            if (!string.Equals(_cacheRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                Memory.Clear();
                SourceHashes.Clear();
                DerivedAnalysis.Clear();
                _memoryBytes = 0;
                _cacheRoot = root;
            }
            _memoryBudgetBytes = budget;
            TrimMemory_NoLock(0);
        }
        Directory.CreateDirectory(Path.Combine(root, "zones"));
    }

    /// <summary>
    /// Safe Mode (Full Validation) calls this with persistentReads=false and clearMemory=true,
    /// forcing the current scanner code to execute once per content hash while still
    /// allowing stages in the same run to share that fresh immutable result.
    /// Fast Mode enables persistent content/version-matched derived reads.
    /// </summary>
    public static void SetDerivedCachePolicy(bool persistentReads, bool clearMemory)
    {
        lock (Gate)
        {
            _allowPersistentDerivedReads = persistentReads;
            if (clearMemory) DerivedAnalysis.Clear();
        }
    }

    public static void ResetStats()
    {
        Interlocked.Exchange(ref _memoryHits, 0);
        Interlocked.Exchange(ref _diskHits, 0);
        Interlocked.Exchange(ref _misses, 0);
        Interlocked.Exchange(ref _decompressedBytesAvoided, 0);
        Interlocked.Exchange(ref _derivedHits, 0);
        Interlocked.Exchange(ref _derivedPersistentHits, 0);
    }

    public static CacheStats GetStats()
    {
        long bytes;
        long budget;
        lock (Gate)
        {
            bytes = _memoryBytes;
            budget = _memoryBudgetBytes;
        }
        return new CacheStats(
            Interlocked.Read(ref _memoryHits),
            Interlocked.Read(ref _diskHits),
            Interlocked.Read(ref _misses),
            bytes,
            budget,
            Interlocked.Read(ref _decompressedBytesAvoided),
            Interlocked.Read(ref _derivedHits),
            Interlocked.Read(ref _derivedPersistentHits));
    }

    public static async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        string root;
        lock (Gate)
        {
            Memory.Clear();
            SourceHashes.Clear();
            DerivedAnalysis.Clear();
            _memoryBytes = 0;
            root = _cacheRoot;
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            Directory.CreateDirectory(Path.Combine(root, "zones"));
        }, cancellationToken);
        ResetStats();
    }

    public static async Task<string> GetSourceSha256Async(string sourceFastFile, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(sourceFastFile);
        var info = new FileInfo(full);
        if (!info.Exists) throw new FileNotFoundException("Fastfile not found.", full);

        lock (Gate)
        {
            if (SourceHashes.TryGetValue(full, out var known) &&
                known.Length == info.Length && known.LastWriteTicks == info.LastWriteTimeUtc.Ticks)
                return known.Sha256;
        }

        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var hex = Convert.ToHexString(hash);
        lock (Gate)
            SourceHashes[full] = new SourceHashEntry(info.Length, info.LastWriteTimeUtc.Ticks, hex);
        return hex;
    }

    /// <summary>
    /// Reuses immutable derived scan results (candidate lists, etc.) for the exact
    /// compressed-source hash already loaded by GetZoneAsync. If no source hash is
    /// known yet, the factory simply runs normally. Exceptions are never cached.
    /// </summary>
    public static T GetOrCreateDerived<T>(string sourceFastFile, string analysisKey, Func<T> factory) where T : class
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));
        var full = Path.GetFullPath(sourceFastFile);
        string? hash = null;
        lock (Gate)
        {
            if (SourceHashes.TryGetValue(full, out var known)) hash = known.Sha256;
        }
        if (hash is null) return factory();

        var key = hash + "|" + analysisKey;
        var candidate = new Lazy<object>(
            () => LoadOrCreatePersistentDerived(hash, analysisKey, factory),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = DerivedAnalysis.GetOrAdd(key, candidate);
        try
        {
            var value = (T)lazy.Value;
            if (!ReferenceEquals(lazy, candidate)) Interlocked.Increment(ref _derivedHits);
            return value;
        }
        catch
        {
            DerivedAnalysis.TryRemove(key, out _);
            throw;
        }
    }

    private static T LoadOrCreatePersistentDerived<T>(string sourceHash, string analysisKey, Func<T> factory) where T : class
    {
        string root;
        lock (Gate) root = _cacheRoot;
        var safeKey = new string(analysisKey.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());
        var dir = Path.Combine(root, "derived_v1", sourceHash);
        var path = Path.Combine(dir, safeKey + ".json");

        bool allowPersistentRead;
        lock (Gate) allowPersistentRead = _allowPersistentDerivedReads;
        if (allowPersistentRead && File.Exists(path))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
                if (cached is not null)
                {
                    Interlocked.Increment(ref _derivedPersistentHits);
                    return cached;
                }
            }
            catch
            {
                SafeDelete(path);
            }
        }

        var value = factory();
        try
        {
            Directory.CreateDirectory(dir);
            var tmp = path + ".tmp." + Guid.NewGuid().ToString("N");
            File.WriteAllText(tmp, JsonSerializer.Serialize(value));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch
        {
            // Persistent derived caching is optional. The freshly computed, immutable
            // result remains valid even when antivirus/OneDrive prevents cache writes.
        }
        return value;
    }

    public static async Task<CachedZone> GetZoneAsync(string sourceFastFile, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(sourceFastFile);
        var info = new FileInfo(full);
        if (!info.Exists) throw new FileNotFoundException("Fastfile not found.", full);

        var hash = await GetSourceSha256Async(full, cancellationToken);
        var key = hash;

        lock (Gate)
        {
            if (Memory.TryGetValue(key, out var entry))
            {
                entry.LastAccessUtc = DateTime.UtcNow;
                Interlocked.Increment(ref _memoryHits);
                Interlocked.Add(ref _decompressedBytesAvoided, entry.Zone.Bytes.LongLength);
                return entry.Zone with { MemoryCacheHit = true, PersistentCacheHit = false };
            }
        }

        var keyLock = KeyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(cancellationToken);
        try
        {
            lock (Gate)
            {
                if (Memory.TryGetValue(key, out var entry))
                {
                    entry.LastAccessUtc = DateTime.UtcNow;
                    Interlocked.Increment(ref _memoryHits);
                    Interlocked.Add(ref _decompressedBytesAvoided, entry.Zone.Bytes.LongLength);
                    return entry.Zone with { MemoryCacheHit = true, PersistentCacheHit = false };
                }
            }

            string root;
            lock (Gate) root = _cacheRoot;
            var zonesDir = Path.Combine(root, "zones");
            Directory.CreateDirectory(zonesDir);
            var zonePath = Path.Combine(zonesDir, hash + ".zone");
            var metaPath = Path.Combine(zonesDir, hash + ".json");

            if (File.Exists(zonePath) && File.Exists(metaPath))
            {
                try
                {
                    var metadata = JsonSerializer.Deserialize<CacheMetadata>(await File.ReadAllTextAsync(metaPath, cancellationToken));
                    if (metadata is not null && metadata.SchemaVersion == SchemaVersion &&
                        metadata.SourceSha256.Equals(hash, StringComparison.OrdinalIgnoreCase) &&
                        metadata.SourceLength == info.Length && new FileInfo(zonePath).Length == metadata.DecompressedLength)
                    {
                        var bytes = await File.ReadAllBytesAsync(zonePath, cancellationToken);
                        var endian = metadata.Platform == WaWPlatform.PC ? Endian.Little : Endian.Big;
                        var header = ZoneHeaderParser.Parse(bytes.AsSpan(0, Math.Min(bytes.Length, FastFileConstants.WaWZoneHeaderSize)), endian, bytes.LongLength);
                        if (!header.Plausible) throw new InvalidDataException("Cached zone header is not plausible.");
                        var document = T4ZoneParser.ParseZoneBytes(full, bytes, metadata.Platform, metadata.Compression, header.Endianness);
                        var cached = new CachedZone(full, hash, bytes, metadata.Platform, metadata.Compression, header, document, false, true);
                        AddMemory(key, cached);
                        try
                        {
                            File.SetLastWriteTimeUtc(zonePath, DateTime.UtcNow);
                            File.SetLastWriteTimeUtc(metaPath, DateTime.UtcNow);
                        }
                        catch { }
                        Interlocked.Increment(ref _diskHits);
                        Interlocked.Add(ref _decompressedBytesAvoided, bytes.LongLength);
                        return cached;
                    }
                }
                catch when (!cancellationToken.IsCancellationRequested)
                {
                    SafeDelete(zonePath);
                    SafeDelete(metaPath);
                }
            }

            Interlocked.Increment(ref _misses);
            var tempPath = zonePath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                var decomp = await FastFileDecompressor.DecompressAutoAsync(full, tempPath, cancellationToken);
                var bytes = await File.ReadAllBytesAsync(tempPath, cancellationToken);
                if (bytes.LongLength != decomp.DecompressedSize)
                    throw new InvalidDataException("Decompressed zone size changed while populating the cache.");

                if (File.Exists(zonePath)) File.Delete(zonePath);
                File.Move(tempPath, zonePath);
                var metadata = new CacheMetadata(SchemaVersion, hash, info.Length, bytes.LongLength, decomp.Platform, decomp.Compression);
                await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

                var document = T4ZoneParser.ParseZoneBytes(full, bytes, decomp.Platform, decomp.Compression, decomp.ZoneHeader.Endianness);
                var fresh = new CachedZone(full, hash, bytes, decomp.Platform, decomp.Compression, decomp.ZoneHeader, document, false, false);
                AddMemory(key, fresh);
                PrunePersistentZones(zonesDir, hash);
                return fresh;
            }
            finally
            {
                SafeDelete(tempPath);
            }
        }
        finally
        {
            keyLock.Release();
        }
    }

    private static void PrunePersistentZones(string zonesDir, string keepHash)
    {
        try
        {
            var zones = Directory.EnumerateFiles(zonesDir, "*.zone", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();
            long total = zones.Sum(fi => fi.Length);
            if (total <= MaxPersistentZoneBytes) return;

            foreach (var fi in zones.OrderBy(fi => fi.LastWriteTimeUtc))
            {
                var hash = Path.GetFileNameWithoutExtension(fi.Name);
                if (hash.Equals(keepHash, StringComparison.OrdinalIgnoreCase)) continue;
                var len = fi.Length;
                SafeDelete(fi.FullName);
                SafeDelete(Path.Combine(zonesDir, hash + ".json"));
                total -= len;
                if (total <= MaxPersistentZoneBytes) break;
            }
        }
        catch
        {
            // Cache pruning is best-effort and can never affect proof correctness.
        }
    }

    private static void AddMemory(string key, CachedZone zone)
    {
        lock (Gate)
        {
            if (zone.Bytes.LongLength > _memoryBudgetBytes) return;
            if (Memory.TryGetValue(key, out var existing))
            {
                existing.LastAccessUtc = DateTime.UtcNow;
                return;
            }
            TrimMemory_NoLock(zone.Bytes.LongLength);
            Memory[key] = new MemoryEntry { Zone = zone with { MemoryCacheHit = false }, LastAccessUtc = DateTime.UtcNow };
            _memoryBytes += zone.Bytes.LongLength;
        }
    }

    private static void TrimMemory_NoLock(long incomingBytes)
    {
        while (_memoryBytes + incomingBytes > _memoryBudgetBytes && Memory.Count > 0)
        {
            var oldest = Memory.OrderBy(kv => kv.Value.LastAccessUtc).First();
            _memoryBytes -= oldest.Value.Zone.Bytes.LongLength;
            Memory.Remove(oldest.Key);
        }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
