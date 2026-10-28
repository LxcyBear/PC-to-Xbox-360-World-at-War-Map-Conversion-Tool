using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Fail-closed, source-hashed native-root bindings for the NUCLEAR-5R
/// PC RawFile production graft. The oracle supplies ONLY identity and order.
/// </summary>
public sealed record T4XenonRawFileProductionEntry(
    int RootIndex, string Name, string SourcePath, byte[] Bytes, string Sha256);

public static class T4XenonRawFileProductionGraft
{
    private static readonly IReadOnlyDictionary<int, string> Expected = new Dictionary<int, string>
    {
        [6362] = "maps/frumansion_j_p.gsc",
        [6363] = "maps/nazi_zombie_frumansion_nozct_fx.gsc",
        [6364] = "maps/createfx/nazi_zombie_frumansion_nozct_fx.gsc",
        [6365] = "clientscripts/frumansion_j_p.csc",
        [6366] = "clientscripts/nazi_zombie_frumansion_nozct_fx.csc",
        [6367] = "clientscripts/createfx/nazi_zombie_frumansion_nozct_fx.csc",
        [6808] = "nazi_zombie_frumansion_nozct",
        [6809] = "shock/zombie_death.shock"
    };

    public static IReadOnlyDictionary<int, T4XenonRawFileProductionEntry> Load(
        string? manifestPath,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            return new Dictionary<int, T4XenonRawFileProductionEntry>();

        var path = Path.GetFullPath(manifestPath);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var json = document.RootElement;
        if (json.GetProperty("format").GetString() != "WaW360.Nuclear5R.RawFileGraftInput.v1")
            throw new InvalidDataException("Unexpected NUCLEAR-5R RawFile manifest format.");
        if (roots.Count != 6811)
            throw new InvalidDataException($"NUCLEAR-5R requires 6,811 native roots, got {roots.Count}.");

        var entries = json.GetProperty("entries");
        if (entries.GetArrayLength() != Expected.Count)
            throw new InvalidDataException($"NUCLEAR-5R requires exactly {Expected.Count} RawFile entries.");

        var result = new Dictionary<int, T4XenonRawFileProductionEntry>();
        foreach (var item in entries.EnumerateArray())
        {
            var index = item.GetProperty("rootIndex").GetInt32();
            var name = item.GetProperty("name").GetString()
                ?? throw new InvalidDataException("Null RawFile identity.");
            var sourcePath = Path.GetFullPath(item.GetProperty("sourcePath").GetString()
                ?? throw new InvalidDataException("Null PC source path."));
            var expectedSha = item.GetProperty("sha256").GetString()
                ?? throw new InvalidDataException("Missing PC source SHA-256.");
            var expectedBytes = item.GetProperty("bytes").GetInt64();

            if (!Expected.TryGetValue(index, out var expectedName) ||
                !name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unexpected RawFile identity #{index}: '{name}'.");
            if (index < 0 || index >= roots.Count ||
                !roots[index].TypeName.Equals("rawfile", StringComparison.OrdinalIgnoreCase) ||
                !roots[index].Name.Replace('\\', '/').Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Native identity mismatch for RawFile root #{index}.");
            if (sourcePath.Contains("native_rawfile_validation_dump", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Oracle payload source forbidden at root #{index}.");

            var bytes = File.ReadAllBytes(sourcePath);
            var actualSha = Convert.ToHexString(SHA256.HashData(bytes));
            if (bytes.LongLength != expectedBytes ||
                !actualSha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"PC source SHA-256/length mismatch at RawFile root #{index}.");
            if (!result.TryAdd(index, new(index, name, sourcePath, bytes, actualSha)))
                throw new InvalidDataException($"Duplicate RawFile root #{index}.");
        }

        if (result.Count != Expected.Count)
            throw new InvalidDataException("Incomplete NUCLEAR-5R RawFile binding.");
        return result;
    }
}
