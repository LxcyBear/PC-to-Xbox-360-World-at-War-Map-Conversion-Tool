namespace WaW360.Core.FastFiles;

public sealed record T4ScriptStringEntry(
    int Index,
    uint RawPointer,
    string? Value,
    bool Inline);

public sealed class T4ZoneDocument
{
    public required string SourceFastFile { get; init; }
    public required WaWPlatform Platform { get; init; }
    public required WaWCompressionKind Compression { get; init; }
    public required Endian Endianness { get; init; }
    public required ZoneHeaderSummary Header { get; init; }

    public int ScriptStringCount { get; init; }
    public uint ScriptStringsPointer { get; init; }
    public int AssetCount { get; init; }
    public uint AssetsPointer { get; init; }
    public long ScriptStringPointerTableOffset { get; init; }
    public long AssetTableOffset { get; init; }

    public required IReadOnlyList<T4ScriptStringEntry> ScriptStrings { get; init; }
    public required IReadOnlyList<T4AssetEntry> Assets { get; init; }

    public IReadOnlyDictionary<string, int> AssetCountsByType =>
        Assets.GroupBy(a => a.TypeName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}
