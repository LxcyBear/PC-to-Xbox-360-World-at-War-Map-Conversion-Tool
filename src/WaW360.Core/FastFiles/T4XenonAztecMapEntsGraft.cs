using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// One source-owned Aztec MapEnts payload, never a retail/oracle donor.
/// This isolated 8,509-root diagnostic mode keeps every existing 4-payload
/// production and FRUmansion binding unchanged.
/// </summary>
internal sealed record T4XenonAztecMapEntsEntry(
    int NativeRootIndex, string AssetName, byte[] SourceBytes, string Sha256);

internal static class T4XenonAztecMapEntsGraft
{
    public static T4XenonAztecMapEntsEntry? Load(
        string? canaryManifest,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots)
    {
        if (string.IsNullOrWhiteSpace(canaryManifest))
            return null;

        var manifestPath = Path.GetFullPath(canaryManifest);
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var j = doc.RootElement;

        if (j.GetProperty("schema").GetString() != "WaW360.AztecPcMapEntsXenonCanary.v1" ||
            j.GetProperty("status").GetString() != "AZTEC_PC_MAPENTS_XENON_CANARY_PASS" ||
            j.GetProperty("readyForXbox").GetBoolean() ||
            j.GetProperty("fullMapIntegrated").GetBoolean() ||
            roots.Count != 8509 ||
            j.GetProperty("xboxFormatOatRootListed").GetInt32() != 1)
            throw new InvalidDataException("Aztec MapEnts standalone source/OAT proof is not passing.");

        var name = j.GetProperty("asset").GetString() ?? "";
        var root = j.GetProperty("nativeRootIndex").GetInt32();
        if (name != "maps/nazi_zombie_aztec.d3dbsp" ||
            root < 0 || root >= roots.Count ||
            roots[root].Index != root ||
            !roots[root].TypeName.Replace("_", string.Empty, StringComparison.Ordinal)
                .Equals("mapents", StringComparison.OrdinalIgnoreCase) ||
            !roots[root].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Aztec MapEnts root/identity is missing or ambiguous.");

        var originalFastFile = Path.GetFullPath(
            j.GetProperty("sourceFastFile").GetString() ?? "");
        var originalFastFileSha = j.GetProperty("sourceFastFileSha256").GetString() ?? "";
        if (!originalFastFile.EndsWith(".ff", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(originalFastFile) ||
            !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalFastFile)))
                .Equals(originalFastFileSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC Aztec map.ff source SHA no longer matches.");

        // Only the original-PC MapEnts OAT export is authorized for this
        // diagnostic graft. No native reference and no scratch FF is a body donor.
        var baseFolder = Path.GetDirectoryName(Path.GetDirectoryName(manifestPath)!)
            ?? throw new InvalidDataException("Invalid MapEnts proof directory.");
        var export = Path.Combine(
            baseFolder,
            "tonight_pc_critical_body_export_probe",
            "source_exports", "mapents", "map_ff", "maps",
            "nazi_zombie_aztec.d3dbsp.ents");
        var data = File.ReadAllBytes(export);
        var sha = Convert.ToHexString(SHA256.HashData(data));
        if (data.Length != 571011 ||
            j.GetProperty("originalPcMapEntsBytes").GetInt64() != data.LongLength ||
            !sha.Equals(j.GetProperty("originalPcMapEntsSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !sha.Equals(j.GetProperty("xenonMapEntsSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Original-PC 571011-byte MapEnts SHA does not match standalone Xenon readback.");

        var standalone = j.GetProperty("standaloneValidationFF").GetString() ?? "";
        if (!standalone.EndsWith("_VALIDATION_ONLY.ff", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(standalone))
            throw new InvalidDataException("Standalone MapEnts Xenon canary FF missing.");

        return new T4XenonAztecMapEntsEntry(root, name, data, sha);
    }
}
