using System.Buffers.Binary;
using System.Text;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

/// <summary>
/// Read-only migration bridge for already VERIFIED Stage-4 typed proof containers.
/// Linker V2 may use these files as diagnostic evidence while the equivalent deterministic
/// source-stream walkers are migrated. They are never treated as permission to re-run a broad
/// candidate scan and they never make a main fastfile hardware-ready by themselves.
/// </summary>
internal static class LinkerV2ProofEvidence
{
    internal static readonly string[] TrackedTypes = { "xanim", "xmodel", "weapon", "fx" };

    internal static string GetProofPath(string outputRoot, string mapName, string sourcePath, string role, string type)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath) + $"_{SanitizeRole(role)}";
        return type.ToLowerInvariant() switch
        {
            "xanim" => Path.Combine(outputRoot, mapName, "stage4_alpha4_xanim", stem + ".xbox_xanim_proof.bin"),
            "xmodel" => Path.Combine(outputRoot, mapName, "stage4_alpha9g_xmodel", stem + ".xbox_xmodel_proof.bin"),
            "weapon" => Path.Combine(outputRoot, mapName, "stage4_alpha7b_weapon_owned", stem + ".xbox_weapon_owned_proof.bin"),
            "fx" => Path.Combine(outputRoot, mapName, "stage4_alpha9b_fx", stem + ".xbox_fx_proof.bin"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    internal static string SanitizeRole(string role) =>
        new(role.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    internal static async Task<string?> TryReadNameAsync(
        T4ProofBlobDescriptor record,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(record.Name))
            return record.Name;

        var fixedSizes = record.TypeName.ToLowerInvariant() switch
        {
            // v5 proof blobs use the hardware-proven TU7 0x5C root. Keep 0x58
            // as a read-only compatibility fallback for pre-v5 proof artifacts.
            "xanim" => new[] { T4XAnimSerializerProof.XboxFixedSize, 0x58 },
            "xmodel" => new[] { 228 },
            _ => Array.Empty<int>()
        };
        if (fixedSizes.Length == 0)
            return null;

        var maxFixed = fixedSizes.Max();
        if (record.BlobLength <= fixedSizes.Min())
            return null;

        var prefix = await T4NativeProofContainerReader.ReadBlobPrefixAsync(
            record,
            Math.Min(record.BlobLength, maxFixed + 1024),
            cancellationToken);
        if (prefix.Length < 5)
            return null;

        var nameRaw = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(0, 4));
        var decoded = T4ZonePointer.Decode(nameRaw);
        if (decoded.Kind is not (T4ZonePointerKind.Following or T4ZonePointerKind.Insert))
            return null;

        foreach (var fixedSize in fixedSizes)
        {
            if (prefix.Length <= fixedSize) continue;
            var end = fixedSize;
            while (end < prefix.Length && prefix[end] != 0) end++;
            if (end == fixedSize || end >= prefix.Length) continue;

            var value = Encoding.UTF8.GetString(prefix, fixedSize, end - fixedSize);
            if (LooksLikeAssetName(value))
                return value;
        }

        return null;
    }

    internal static int RoleRank(string role) => role.ToLowerInvariant() switch
    {
        "map.ff" => 0,
        "map_patch.ff" => 1,
        "mod.ff" => 2,
        "extra.ff" => 3,
        _ => 9
    };

    private static bool LooksLikeAssetName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 768) return false;
        foreach (var ch in value)
        {
            if (ch < 0x20 || ch > 0x7E) return false;
        }
        return true;
    }
}
