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
    private static readonly SemaphoreSlim MaterializeGate = new(1, 1);

    internal sealed record MaterializeResult(
        IReadOnlyList<string> GeneratedFamilies,
        IReadOnlyList<string> FailedFamilies,
        IReadOnlyList<string> Notes);

    internal static async Task<MaterializeResult> EnsureTrackedProofsAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult source,
        CancellationToken cancellationToken = default)
    {
        var generated = new List<string>();
        var failed = new List<string>();
        var notes = new List<string>();

        var primary = source.FastFiles
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .ToList();

        var missingByType = TrackedTypes.ToDictionary(x => x, _ => false, StringComparer.OrdinalIgnoreCase);
        foreach (var container in primary)
        {
            var doc = (await FastFileAnalysisCache.GetZoneAsync(container.Path, cancellationToken)).Document;
            foreach (var type in TrackedTypes)
            {
                var count = doc.Assets.Count(a =>
                    a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                    a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared);
                if (count == 0) continue;
                if (!File.Exists(GetProofPath(outputRoot, map.Name, container.Path, container.Role, type)))
                    missingByType[type] = true;
            }
        }

        // Proof serializers are intentionally serialized across parallel Fast-Mode maps. They are
        // CPU/memory-heavy and share the immutable FF analysis cache; running two proof marathons
        // at once only increases memory pressure without reducing wall time reliably.
        await MaterializeGate.WaitAsync(cancellationToken);
        try
        {
            var needWeapon = missingByType["weapon"];
            var needXAnim = missingByType["xanim"];
            var needXModel = missingByType["xmodel"];
            var needFx = missingByType["fx"];

            async Task TryFamilyAsync(string family, Func<Task> build)
            {
                try
                {
                    await build();
                    generated.Add(family);
                    notes.Add($"Fast Mode materialized missing {family} typed proof evidence.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed.Add(family);
                    notes.Add($"Fast Mode could not materialize {family} proof evidence: {ex.Message}");
                }
            }

            // Weapon fixed -> owned first because XAnim/XModel can use Weapon ownership as an
            // alignment anchor. Each wrapper remains fail-closed and independently verifies its
            // output before Linker V2 consumes it.
            if (needWeapon)
            {
                await TryFamilyAsync("weapon", async () =>
                {
                    await NormalMapWeaponSerializerProof.RunAsync(map, outputRoot, cancellationToken);
                    await NormalMapWeaponVariableSerializerProof.RunAsync(map, outputRoot, cancellationToken);
                });
            }

            if (needXAnim)
                await TryFamilyAsync("xanim", async () =>
                    await NormalMapXAnimSerializerProof.RunAsync(map, outputRoot, cancellationToken));

            if (needXModel)
                await TryFamilyAsync("xmodel", async () =>
                    await NormalMapXModelSerializerProof.RunAsync(map, outputRoot, cancellationToken));

            if (needFx)
                await TryFamilyAsync("fx", async () =>
                    await NormalMapFxSerializerProof.RunAsync(map, outputRoot, cancellationToken));
        }
        finally
        {
            MaterializeGate.Release();
        }

        return new MaterializeResult(generated, failed, notes);
    }

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

        var fixedSize = record.TypeName.ToLowerInvariant() switch
        {
            "xanim" => 92,
            "xmodel" => 228,
            _ => 0
        };
        if (fixedSize == 0 || record.BlobLength <= fixedSize)
            return null;

        var prefix = await T4NativeProofContainerReader.ReadBlobPrefixAsync(
            record,
            Math.Min(record.BlobLength, fixedSize + 1024),
            cancellationToken);
        if (prefix.Length < fixedSize + 1)
            return null;

        var nameRaw = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(0, 4));
        var decoded = T4ZonePointer.Decode(nameRaw);
        if (decoded.Kind is not (T4ZonePointerKind.Following or T4ZonePointerKind.Insert))
            return null;

        var end = fixedSize;
        while (end < prefix.Length && prefix[end] != 0) end++;
        if (end == fixedSize || end >= prefix.Length)
            return null;

        var value = Encoding.UTF8.GetString(prefix, fixedSize, end - fixedSize);
        return LooksLikeAssetName(value) ? value : null;
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
