using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

/// <summary>
/// Opt-in, non-deployable WeaponDef scalar experiment. Uses the existing
/// source-owned Xbox-endian Alpha 7b proof, NOT scalar fingerprints, to copy
/// only individually whitelisted 32-bit scalar fields into the proven 71-name
/// inert shells. All XString, script-string, model, material, FX, sound and
/// variable-data ownership remains unconverted/zero; never a playable weapon.
/// </summary>
internal static class LinkerV2VerifiedWeaponScalarDiagnostic
{
    // Exact 32-bit nonpointer numeric slots already independently measured
    // by Run-WaWMultiMapWeaponScalarWitness.ps1 and bounded by the Alpha 6f
    // WeaponDef scanner. NO pointer fields or script-string arrays here.
    private static readonly int[] ScalarOffsets =
    {
        320,324,328,332,336,340,344,348,352,368,372,376,
        688,988,996,1000,1316,1320,1556,1568,1668,1704,1996,
        1244,1248,1252,1268,1280,1484,1488,1492,1496,1500,
        1512,1516,1520,1524,1528,1532,1536,1540,1544,1560,
        1572,1576,1580,1608,1612,1676,1700,1708,1712,1716,
        1720,1724,2200,2204,2208,2212
    };

    internal sealed record Result(
        List<NormalMapLinkerV2StructuralMainEmitter.StructuralPayload> Payloads,
        int SourceWeaponBodies,
        int ScalarFieldsPerBody,
        int ScalarFieldsCopied,
        int NonZeroScalarFields,
        int SourceProofContainers);

    internal static async Task<Result> ApplyAsync(
        string outputRoot,
        string mapName,
        IReadOnlyList<LinkerV2CanonicalRoot> roots,
        IReadOnlyList<NormalMapLinkerV2StructuralMainEmitter.StructuralPayload> sourcePayloads,
        IReadOnlyList<NormalMapLinkerV2StructuralMainEmitter.StructuralPayload> directNamedPayloads,
        CancellationToken ct)
    {
        if (!mapName.Equals("nazi_zombie_aztec_V2.2", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Real-scalar experiment is scoped to the 71-body Aztec source proof.");
        if (roots.Count != sourcePayloads.Count || roots.Count != directNamedPayloads.Count)
            throw new InvalidDataException("Scalar diagnostic root/payload counts disagree.");
        if (ScalarOffsets.Length < 45 || ScalarOffsets.Distinct().Count() != ScalarOffsets.Length ||
            ScalarOffsets.Any(o => o < 320 || o + 4 > 0x9AC || o % 4 != 0))
            throw new InvalidDataException("Scalar whitelist is malformed.");

        var outPayloads = directNamedPayloads.ToList();
        var copied = 0;
        var nonzero = 0;
        var bodies = 0;
        var proofs = 0;

        var weaponGroups = roots
            .Select((root, index) => (Root: root, Index: index))
            .Where(x => x.Root.XboxTypeName.Equals("weapon", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.Root.SourceFastFile, StringComparer.OrdinalIgnoreCase);

        foreach (var fileGroup in weaponGroups)
        {
            ct.ThrowIfCancellationRequested();
            var fileRoots = fileGroup.OrderBy(x => x.Root.SourceAssetIndex).ToArray();
            var first = fileRoots[0].Root;
            if (fileRoots.Any(x => !string.Equals(x.Root.SourceRole, first.SourceRole, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Weapon source role changed within one source FF.");
            var proofPath = LinkerV2ProofEvidence.GetProofPath(
                outputRoot, mapName, first.SourceFastFile, first.SourceRole, "weapon");
            var proof = await T4NativeProofContainerReader.IndexAsync(proofPath, "weapon", ct);
            if (proof.Version != 8 || proof.Records.Count != fileRoots.Length)
                throw new InvalidDataException(
                    $"Weapon Alpha 7b proof version/count mismatch for {Path.GetFileName(first.SourceFastFile)}.");
            proofs++;

            var cached = await FastFileAnalysisCache.GetZoneAsync(first.SourceFastFile, ct);
            if (cached.Document.Endianness != Endian.Little)
                throw new InvalidDataException("Scalar source must be the genuine PC/little-endian FF.");

            // Physical source offset, original proof label AND source XAsset
            // record ordinal must all agree; ordinal is ONLY for locating the
            // record, never a guessed name. Direct pointer mapping already
            // established real names separately and is NOT changed here.
            for (var ordinal = 0; ordinal < fileRoots.Length; ordinal++)
            {
                ct.ThrowIfCancellationRequested();
                var (root, i) = fileRoots[ordinal];
                var record = proof.Records[ordinal];
                var before = sourcePayloads[i];
                var resolved = directNamedPayloads[i];
                if (!string.Equals(before.Name, record.Name, StringComparison.Ordinal) ||
                    root.SourceAssetIndex < 0 || record.SourceOffset < 0 ||
                    record.SourceOffset > cached.Bytes.Length - 0x9AC)
                    throw new InvalidDataException(
                        $"Source-owned WeaponDef identity/offset mismatch: {Path.GetFileName(first.SourceFastFile)}#{root.SourceAssetIndex}.");

                var raw = BinaryPrimitives.ReadUInt32LittleEndian(
                    cached.Bytes.AsSpan(checked((int)record.SourceOffset), 4));
                var synthetic = Regex.Match(before.Name,
                    @"^<packed-name@0x([0-9A-Fa-f]+):0x([0-9A-Fa-f]{8})>$",
                    RegexOptions.CultureInvariant);
                if (before.Name.StartsWith("<packed-name@", StringComparison.Ordinal))
                {
                    if (!synthetic.Success ||
                        long.Parse(synthetic.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) != record.SourceOffset ||
                        uint.Parse(synthetic.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) != raw ||
                        resolved.Name.StartsWith("<packed-name@", StringComparison.Ordinal))
                        throw new InvalidDataException("Directly identified weapon changed source offset/pointer.");
                }
                else if (!string.Equals(before.Name, resolved.Name, StringComparison.Ordinal))
                    throw new InvalidDataException("Previously known source weapon name changed.");

                var nameBytes = Encoding.ASCII.GetBytes(resolved.Name);
                if (nameBytes.Length == 0 || nameBytes.Length > 96 ||
                    !Regex.IsMatch(resolved.Name, "^[A-Za-z0-9_./-]+$", RegexOptions.CultureInvariant) ||
                    resolved.Bytes.Length != 0x9AC + nameBytes.Length + 1 ||
                    BinaryPrimitives.ReadUInt32BigEndian(resolved.Bytes.AsSpan(0, 4)) != T4ZonePointer.Following ||
                    !resolved.Bytes.AsSpan(4, 0x9AC - 4).SequenceEqual(new byte[0x9AC - 4]) ||
                    !resolved.Bytes.AsSpan(0x9AC, nameBytes.Length).SequenceEqual(nameBytes) ||
                    resolved.Bytes[^1] != 0)
                    throw new InvalidDataException($"Weapon '{resolved.Name}' is not the expected untouched name-only shell.");

                var src = await T4NativeProofContainerReader.ReadBlobPrefixAsync(record, 0x9AC, ct);
                if (src.Length != 0x9AC)
                    throw new InvalidDataException($"Weapon '{resolved.Name}' source fixed body was truncated.");

                var owned = resolved.Bytes.ToArray();
                foreach (var offset in ScalarOffsets)
                {
                    src.AsSpan(offset, 4).CopyTo(owned.AsSpan(offset, 4));
                    if (BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(offset, 4)) != 0) nonzero++;
                    copied++;
                }
                // No unlisted bytes changed. This also walks the complete,
                // self-contained Xbox-endian diagnostic's owned stream.
                if (!T4WeaponVariableSerializerProof.TryValidateSelfContainedXboxBlob(
                        owned, resolved.Name, out var reason))
                    throw new InvalidDataException($"Weapon '{resolved.Name}' scalar candidate is not self-contained: {reason}");

                outPayloads[i] = new NormalMapLinkerV2StructuralMainEmitter.StructuralPayload(
                    "weapon", resolved.Name, owned, true,
                    "DIAGNOSTIC ONLY: real source-owned endian-verified numeric scalars; strings/models/FX/audio/animations not converted");
                bodies++;
            }
        }
        if (bodies != 71 || proofs != 2 || copied != bodies * ScalarOffsets.Length)
            throw new InvalidDataException(
                $"Aztec scalar proof coverage incomplete: {bodies}/71 bodies, {proofs}/2 source proof containers.");

        return new Result(outPayloads, bodies, ScalarOffsets.Length, copied, nonzero, proofs);
    }
}
