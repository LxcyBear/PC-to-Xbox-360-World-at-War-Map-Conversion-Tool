using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4MenuStringHit(long Offset, string Value);

public static class T4MenuStringProbe
{
    public static async Task<IReadOnlyList<T4MenuStringHit>> ProbeFastFileAsync(
        string fastFilePath,
        CancellationToken cancellationToken = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "menuprobe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "probe.zone");

        try
        {
            await FastFileDecompressor.DecompressAutoAsync(fastFilePath, zonePath, cancellationToken);
            var data = await File.ReadAllBytesAsync(zonePath, cancellationToken);
            return Probe(data);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public static IReadOnlyList<T4MenuStringHit> Probe(ReadOnlySpan<byte> zone)
    {
        var results = new List<T4MenuStringHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var start = -1;
        for (var i = 0; i <= zone.Length; i++)
        {
            var printable = i < zone.Length && zone[i] >= 0x20 && zone[i] <= 0x7E;
            if (printable)
            {
                if (start < 0) start = i;
                continue;
            }

            if (start >= 0)
            {
                var length = i - start;
                if (length >= 4 && length <= 512)
                {
                    var value = Encoding.ASCII.GetString(zone.Slice(start, length));
                    if (LooksMenuRelated(value) && seen.Add(value))
                        results.Add(new T4MenuStringHit(start, value));
                }
                start = -1;
            }
        }

        return results
            .OrderBy(r => r.Offset)
            .ToList();
    }

    private static bool LooksMenuRelated(string value)
    {
        return value.Contains("menu", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("zombie", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("map_custom", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("custom map", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("mapstable", StringComparison.OrdinalIgnoreCase);
    }
}
