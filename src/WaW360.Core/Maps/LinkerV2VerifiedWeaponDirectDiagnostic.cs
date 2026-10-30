using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WaW360.Core.Maps;

/// <summary>
/// Read-only, fail-closed import of OAT's DIRECT loaded-VIRTUAL-pointer witness.
/// Produces a separate, inert-WeaponDef identity diagnostic. It does NOT alter
/// typed proof containers, production candidates, or authorize Xbox deployment.
/// Source root ordinals are used only to locate the correct proof record; they
/// are NEVER used to infer the ten missing names.
/// </summary>
internal static class LinkerV2VerifiedWeaponDirectDiagnostic
{
    internal sealed record Result(
        List<NormalMapLinkerV2StructuralMainEmitter.StructuralPayload> Payloads,
        int KnownNames,
        int IndependentlyProvenNames,
        int TotalWeaponRoots,
        string WitnessSha256);

    private sealed record WitnessRow(
        string Source,
        string Role,
        string Proof,
        string SourceSha256,
        long ProofOffset,
        uint PointerRaw,
        string Name);

    internal static async Task<Result> ApplyAsync(
        string witnessJson,
        string outputRoot,
        string mapName,
        IReadOnlyList<LinkerV2CanonicalRoot> roots,
        IReadOnlyList<NormalMapLinkerV2StructuralMainEmitter.StructuralPayload> payloads,
        CancellationToken ct)
    {
        if (!mapName.Equals("nazi_zombie_aztec_V2.2", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The ten-pointer direct proof is Aztec-specific; other maps are fenced.");
        if (roots.Count != payloads.Count)
            throw new InvalidDataException("Direct WeaponDef diagnostic requires one payload per source root.");
        if (!Path.IsPathFullyQualified(witnessJson) || !File.Exists(witnessJson))
            throw new FileNotFoundException("Supply an existing absolute direct-pointer witness JSON path.", witnessJson);

        var json = await File.ReadAllTextAsync(witnessJson, ct);
        var witnessSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "WaW360.WeaponDirectVirtualPointerWitness.v1" ||
            !string.Equals(root.GetProperty("map").GetString(), mapName, StringComparison.OrdinalIgnoreCase) ||
            root.GetProperty("requested").GetInt32() != 10 ||
            root.GetProperty("directMatches").GetInt32() != 10 ||
            !root.GetProperty("allDirectNamesResolved").GetBoolean() ||
            root.GetProperty("namesAppliedToProduction").GetBoolean() ||
            root.GetProperty("nativeXboxReleaseAuthorized").GetBoolean())
        {
            throw new InvalidDataException("Direct WeaponDef witness does not meet its complete read-only 10/10 contract.");
        }

        var rows = new Dictionary<string, WitnessRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var evidence in root.GetProperty("rows").EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            if (evidence.GetProperty("status").GetString() != "DIRECT_POINTER_NAME_IN_OAT" ||
                !evidence.GetProperty("resolved").GetBoolean() ||
                evidence.GetProperty("ordinalInferenceUsed").GetBoolean())
                throw new InvalidDataException("A witness row is unresolved or used ordinal inference.");

            var source = Path.GetFullPath(evidence.GetProperty("source").GetString() ??
                throw new InvalidDataException("Missing original PC source path."));
            var role = evidence.GetProperty("sourceRole").GetString() ?? "";
            var proof = Path.GetFullPath(evidence.GetProperty("proof").GetString() ??
                throw new InvalidDataException("Missing source proof path."));
            var sourceSha = evidence.GetProperty("sourceSha256").GetString() ?? "";
            var name = evidence.GetProperty("directOatLoadedName").GetString() ?? "";
            var rawText = evidence.GetProperty("raw").GetString() ?? "";
            var offset = evidence.GetProperty("proofOffset").GetInt64();
            if (!Regex.IsMatch(sourceSha, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant) ||
                !Regex.IsMatch(rawText, "^[a-fA-F0-9]{8}$", RegexOptions.CultureInvariant) ||
                !Regex.IsMatch(name, "^[A-Za-z0-9_./-]+$", RegexOptions.CultureInvariant) ||
                name.Length > 96 || offset < 0)
                throw new InvalidDataException("Malformed original-PC weapon name/pointer/source hash evidence.");
            var pointer = uint.Parse(rawText, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (pointer == 0 || pointer >= 0xFFFFFFFEu || ((pointer - 1u) >> 29) != 4)
                throw new InvalidDataException($"Invalid packed VIRTUAL block-4 name pointer 0x{pointer:X8}.");
            var row = new WitnessRow(source, role, proof, sourceSha, offset, pointer, name);
            if (!rows.TryAdd(Key(source, offset, pointer), row))
                throw new InvalidDataException("Duplicate source-offset/pointer evidence in direct witness.");
        }
        if (rows.Count != 10)
            throw new InvalidDataException($"Direct evidence has {rows.Count}/10 unique proof records.");

        var sourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var output = new List<NormalMapLinkerV2StructuralMainEmitter.StructuralPayload>(payloads.Count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var known = 0;
        var total = 0;
        var proven = 0;

        for (var i = 0; i < roots.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var rootAsset = roots[i];
            var baseline = payloads[i];
            if (!rootAsset.XboxTypeName.Equals("weapon", StringComparison.OrdinalIgnoreCase))
            {
                output.Add(baseline);
                continue;
            }
            total++;
            var source = Path.GetFullPath(rootAsset.SourceFastFile);
            if (!sourceNames.TryGetValue(source, out var names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                sourceNames.Add(source, names);
            }
            var label = Regex.Match(
                baseline.Name,
                @"^<packed-name@0x([a-fA-F0-9]+):0x([a-fA-F0-9]{8})>$",
                RegexOptions.CultureInvariant);
            if (!label.Success)
            {
                if (baseline.Name.StartsWith("<packed-name@", StringComparison.OrdinalIgnoreCase) ||
                    !names.Add(baseline.Name))
                    throw new InvalidDataException($"Unknown/duplicate known WeaponDef name from {source}.");
                known++;
                output.Add(baseline);
                continue;
            }

            var offset = long.Parse(label.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var pointer = uint.Parse(label.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var key = Key(source, offset, pointer);
            if (!rows.TryGetValue(key, out var row) || !used.Add(key))
                throw new InvalidDataException(
                    $"No unique DIRECT pointer proof for {Path.GetFileName(source)}:0x{offset:X}, raw=0x{pointer:X8}.");
            if (!string.Equals(rootAsset.SourceRole, row.Role, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Source-role mismatch for {row.Name}.");
            var expectedProof = Path.GetFullPath(LinkerV2ProofEvidence.GetProofPath(
                outputRoot, mapName, source, rootAsset.SourceRole, "weapon"));
            if (!string.Equals(expectedProof, row.Proof, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(expectedProof))
                throw new InvalidDataException($"Typed original-PC WeaponDef proof path mismatch for {row.Name}.");

            if (!sourceHashes.TryGetValue(source, out var actualSha))
            {
                await using var file = File.OpenRead(source);
                actualSha = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
                sourceHashes.Add(source, actualSha);
            }
            if (!string.Equals(row.SourceSha256, actualSha, StringComparison.OrdinalIgnoreCase) ||
                !names.Add(row.Name))
                throw new InvalidDataException($"Source FF SHA or unique weapon identity mismatch for {row.Name}.");

            // Only the name-bearing 0x9AC diagnostic shell is replaced.
            // Original variable WeaponDef ownership is NOT reconstructed here.
            var nameBytes = Encoding.ASCII.GetBytes(row.Name);
            var blob = new byte[0x9AC + nameBytes.Length + 1];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(0, 4), 0xFFFFFFFDu);
            nameBytes.CopyTo(blob.AsSpan(0x9AC));
            output.Add(new NormalMapLinkerV2StructuralMainEmitter.StructuralPayload(
                "weapon", row.Name, blob, true,
                "DIRECT_PC_VIRTUAL_POINTER_PROVEN / Xbox 0x9AC inert WeaponDef identity shell; NOT gameplay content"));
            proven++;
        }

        if (total != 71 || known != 61 || proven != 10 || used.Count != rows.Count)
            throw new InvalidDataException(
                $"Aztec WeaponDef identity closure incomplete: total={total}/71 known={known}/61 direct={proven}/10 consumed={used.Count}/{rows.Count}.");

        return new Result(output, known, proven, total, witnessSha);
    }

    private static string Key(string source, long offset, uint pointer) =>
        $"{Path.GetFullPath(source)}|{offset:X}|{pointer:X8}";
}
