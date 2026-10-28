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
        if (json.GetProperty("format").GetString() == "WaW360.AztecPcRawFileGraftInput.v1")
            return LoadAztecPcRoots(json, roots);

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

    /// <summary>
    /// Native Aztec roots are IDENTITY evidence only: every promoted RawFile
    /// body must come from a SHA-pinned PC fastfile's extracted RawFile corpus.
    /// The historical FRUmansion 5R contract above remains unchanged.
    /// </summary>
    private static IReadOnlyDictionary<int, T4XenonRawFileProductionEntry> LoadAztecPcRoots(
        JsonElement json,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots)
    {
        if (json.GetProperty("mapFolder").GetString() != "nazi_zombie_aztec_V2.2" ||
            json.GetProperty("canonicalBaseName").GetString() != "nazi_zombie_aztec" ||
            roots.Count != 8509 ||
            json.GetProperty("nativeRootCount").GetInt32() != roots.Count)
            throw new InvalidDataException("Aztec RawFile graft source-map/root identity mismatch.");

        var entries = json.GetProperty("entries");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() is < 1 or > 120)
            throw new InvalidDataException("Aztec graft requires 1..120 independent PC RawFile root matches.");

        var result = new Dictionary<int, T4XenonRawFileProductionEntry>();
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in entries.EnumerateArray())
        {
            var index = item.GetProperty("rootIndex").GetInt32();
            var name = item.GetProperty("name").GetString()
                ?? throw new InvalidDataException("Null Aztec RawFile name.");
            var sourcePath = Path.GetFullPath(item.GetProperty("sourcePath").GetString()
                ?? throw new InvalidDataException("Null PC RawFile source path."));
            var sourceFastFile = Path.GetFullPath(item.GetProperty("sourceFastFile").GetString()
                ?? throw new InvalidDataException("Null PC RawFile origin fastfile."));
            var zoneRole = item.GetProperty("sourceRole").GetString();
            var sourceSha = item.GetProperty("sourceFastFileSha256").GetString()
                ?? throw new InvalidDataException("Missing PC source FF SHA-256.");
            var expectedSha = item.GetProperty("sha256").GetString()
                ?? throw new InvalidDataException("Missing RawFile SHA-256.");
            var expectedBytes = item.GetProperty("bytes").GetInt64();

            if (index < 0 || index >= roots.Count ||
                !roots[index].TypeName.Equals("rawfile", StringComparison.OrdinalIgnoreCase) ||
                !roots[index].Name.Replace('\\', '/').Equals(name.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Aztec RawFile native-root identity mismatch #{index}: {name}.");
            if (!uniqueNames.Add(name.Replace('\\', '/')))
                throw new InvalidDataException($"Duplicate PC RawFile identity: {name}.");
            if (zoneRole is not ("map.ff" or "map_patch.ff" or "mod.ff"))
                throw new InvalidDataException($"Unrecognized PC fastfile role for {name}: {zoneRole}.");

            // Never promote native Xbox dumps, an oracle, or the output candidate
            // as a "PC" source. The only supported corpus is the explicit
            // pc_rawfile_corpus output created from input PC .ff files.
            var normalizedSource = sourcePath.Replace('\\', '/');
            if (!normalizedSource.Contains("/pc_rawfile_corpus/", StringComparison.OrdinalIgnoreCase) ||
                normalizedSource.Contains("/native_", StringComparison.OrdinalIgnoreCase) ||
                normalizedSource.Contains("/oracle/", StringComparison.OrdinalIgnoreCase) ||
                sourceFastFile.Replace('\\', '/').Contains("/native_", StringComparison.OrdinalIgnoreCase) ||
                sourceFastFile.Replace('\\', '/').Contains("/oracle/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsafe PC RawFile provenance path: {sourcePath}.");
            if (!sourceFastFile.EndsWith(".ff", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(sourceFastFile))
                throw new InvalidDataException($"Missing PC source fastfile for {name}.");
            if (!sourceHashes.TryGetValue(sourceFastFile, out var actualFfSha))
            {
                actualFfSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourceFastFile)));
                sourceHashes.Add(sourceFastFile, actualFfSha);
            }
            if (!actualFfSha.Equals(sourceSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"PC fastfile changed since extraction: {sourceFastFile}");

            var bytes = File.ReadAllBytes(sourcePath);
            var actualSha = Convert.ToHexString(SHA256.HashData(bytes));
            if (bytes.LongLength != expectedBytes || !actualSha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"PC extracted RawFile bytes changed for {name}.");
            if (!result.TryAdd(index, new(index, name, sourcePath, bytes, actualSha)))
                throw new InvalidDataException($"Duplicate native RawFile root index #{index}.");
        }
        return result;
    }

}
