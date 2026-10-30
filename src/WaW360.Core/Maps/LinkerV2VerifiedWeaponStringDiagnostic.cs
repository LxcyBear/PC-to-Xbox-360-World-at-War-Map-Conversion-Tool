using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

/// <summary>
/// THIRD, opt-in Xbox WeaponDef diagnostic. Reuses the OAT-green real-scalar
/// fixed bodies and promotes only exact source-authoritative IWD weapon-file
/// XStrings or the leading inline PC-owned XStrings proved by Alpha 7b.
/// It neither produces models/FX/audio nor restores runtime relationships.
/// No original FF, proof or previous diagnostic is modified.
/// </summary>
internal static class LinkerV2VerifiedWeaponStringDiagnostic
{
    private const int FixedBytes = 0x9AC;
    private static readonly (string Label, int Offset)[] Fields = BuildFieldOrder();

    internal sealed record Result(
        List<NormalMapLinkerV2StructuralMainEmitter.StructuralPayload> Payloads,
        int Bodies,
        int StringsInserted,
        int FromOriginalInlineProof,
        int FromIwdWeaponFiles,
        int BodiesWithAddedStrings,
        int ResolvedAnimNameReferences,
        int SourceProofContainers);

    private static (string Label, int Offset)[] BuildFieldOrder()
    {
        var ordered = new List<(string, int)>
        {
            ("szDisplayName", 4),
            ("szOverlayName", 8)
        };
        for (var slot = 0; slot < 35; slot++)
            ordered.Add(($"szXAnims[{slot}]", 80 + slot * 4));
        ordered.AddRange(new (string, int)[]
        {
            ("szModeName", 220),
            ("standMountedWeapdef", 628),
            ("crouchMountedWeapdef", 632),
            ("proneMountedWeapdef", 636),
            ("szAmmoName", 1008),
            ("szClipName", 1016),
            ("szSharedAmmoCapName", 1040),
            ("szAltWeaponName", 1592),
            ("szUseHintString", 2288),
            ("dropHintString", 2292),
            ("szScript", 2312),
            ("fireRumble", 2428),
            ("meleeImpactRumble", 2432),
            ("flameTableFirstPerson", 2452),
            ("flameTableThirdPerson", 2456)
        });
        return ordered.ToArray();
    }

    internal static async Task<Result> ApplyAsync(
        string outputRoot,
        string mapName,
        IReadOnlyList<LinkerV2CanonicalRoot> roots,
        IReadOnlyList<NormalMapLinkerV2StructuralMainEmitter.StructuralPayload> originalShells,
        IReadOnlyList<NormalMapLinkerV2StructuralMainEmitter.StructuralPayload> scalarShells,
        IReadOnlyDictionary<string, LinkerV2IwdWeaponStringCatalog.WeaponStrings> iwdWeapons,
        CancellationToken ct)
    {
        if (!mapName.Equals("nazi_zombie_aztec_V2.2", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Weapon XString canary only targets the source-proven Aztec 71-body map.");
        if (roots.Count != originalShells.Count || roots.Count != scalarShells.Count)
            throw new InvalidDataException("Weapon XString root/body cardinality changed.");
        if (Fields.Select(x => x.Offset).Distinct().Count() != Fields.Length ||
            Fields.Any(x => x.Offset < 4 || x.Offset + 4 > FixedBytes || x.Offset % 4 != 0))
            throw new InvalidDataException("Weapon XString zone traversal order is malformed.");

        var result = scalarShells.ToList();
        var bodyCount = 0;
        var inserted = 0;
        var fromInline = 0;
        var fromIwd = 0;
        var bodiesWithStrings = 0;
        var anims = 0;
        var proofCount = 0;

        var groups = roots.Select((root, i) => (Root: root, Index: i))
            .Where(x => x.Root.XboxTypeName.Equals("weapon", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.Root.SourceFastFile, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var ordered = group.OrderBy(x => x.Root.SourceAssetIndex).ToArray();
            var first = ordered[0].Root;
            if (ordered.Any(x => x.Root.SourceRole != first.SourceRole))
                throw new InvalidDataException("Mixed original-PC roles within one weapon proof.");
            var proofPath = LinkerV2ProofEvidence.GetProofPath(
                outputRoot, mapName, first.SourceFastFile, first.SourceRole, "weapon");
            var proof = await T4NativeProofContainerReader.IndexAsync(proofPath, "weapon", ct);
            if (proof.Version != 8 || proof.Records.Count != ordered.Length)
                throw new InvalidDataException("Original-PC WeaponDef owner proof count/version mismatch.");
            proofCount++;

            for (var ordinal = 0; ordinal < ordered.Length; ordinal++)
            {
                ct.ThrowIfCancellationRequested();
                var (root, i) = ordered[ordinal];
                var record = proof.Records[ordinal];
                var before = originalShells[i];
                var scalar = scalarShells[i];
                if (!string.Equals(before.Name, record.Name, StringComparison.Ordinal) ||
                    !string.Equals(scalar.TypeName, "weapon", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Original physical WeaponDef name/proof record mismatch #{root.SourceAssetIndex}.");

                var resolvedName = scalar.Name;
                if (resolvedName.StartsWith("<packed-name@", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Packed weapon source identity not resolved.");
                var nameBytes = Encoding.ASCII.GetBytes(resolvedName);
                if (!Regex.IsMatch(resolvedName, "^[A-Za-z0-9_./-]+$", RegexOptions.CultureInvariant) ||
                    nameBytes.Length is 0 or > 96 ||
                    scalar.Bytes.Length != FixedBytes + nameBytes.Length + 1 ||
                    !scalar.Bytes.AsSpan(FixedBytes, nameBytes.Length).SequenceEqual(nameBytes) ||
                    scalar.Bytes[^1] != 0 ||
                    BinaryPrimitives.ReadUInt32BigEndian(scalar.Bytes.AsSpan(0, 4)) != T4ZonePointer.Following)
                    throw new InvalidDataException($"Weapon '{resolvedName}' has no expected, source-named scalar shell.");

                if (!T4WeaponVariableSerializerProof.TryValidateSelfContainedXboxBlob(
                        scalar.Bytes, resolvedName, out var precheckReason))
                    throw new InvalidDataException(
                        $"Weapon '{resolvedName}' scalar baseline invalid: {precheckReason}");

                // Read only the first 4 KiB after fixed body; the Alpha 7b
                // serializer already validated exact endian/variable ownership.
                var src = await T4NativeProofContainerReader.ReadBlobPrefixAsync(
                    record, FixedBytes + 4096, ct);
                if (src.Length < FixedBytes)
                    throw new InvalidDataException("Truncated original-PC Xbox-endian WeaponDef proof.");
                var inline = ExtractOpeningInlineXStrings(src, resolvedName);

                var iwd = iwdWeapons.TryGetValue(resolvedName, out var fromArchive)
                    ? fromArchive.ByProofLabel
                    : null;
                var patched = new byte[FixedBytes];
                scalar.Bytes.AsSpan(0, FixedBytes).CopyTo(patched);
                using var owned = new MemoryStream(FixedBytes + nameBytes.Length + 256);
                owned.Write(patched);
                owned.Write(nameBytes);
                owned.WriteByte(0);

                var thisBody = 0;
                foreach (var (label, offset) in Fields)
                {
                    var raw = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(offset, 4));
                    if (raw == 0)
                        continue; // No source field: never manufacture new data.
                    string? value = null;
                    var fromProof = false;
                    if (inline.TryGetValue(label, out var inlineValue))
                    {
                        value = inlineValue;
                        fromProof = true;
                    }
                    else if (iwd is not null && iwd.TryGetValue(label, out var iwdValue))
                    {
                        value = iwdValue;
                    }
                    if (string.IsNullOrEmpty(value) || !SafeLatin1CString(value))
                        continue;

                    // Verify original source field is either serialized INLINE
                    // or a valid non-null PC zone pointer. The exact raw PC
                    // pointer is NEVER carried into the Xbox shell.
                    var decoded = T4ZonePointer.Decode(raw);
                    if (decoded.Kind is not (T4ZonePointerKind.Following or
                         T4ZonePointerKind.Insert or T4ZonePointerKind.Offset))
                        continue;
                    if (decoded.Kind == T4ZonePointerKind.Offset &&
                        decoded.Block != T4ZoneBlock.Virtual)
                        continue;

                    BinaryPrimitives.WriteUInt32BigEndian(
                        patched.AsSpan(offset, 4), T4ZonePointer.Following);
                    // The already-produced fixed body resides at stream start:
                    // update it after the XString decisions are complete.
                    var bytes = Encoding.Latin1.GetBytes(value);
                    owned.Write(bytes);
                    owned.WriteByte(0);
                    thisBody++;
                    inserted++;
                    if (fromProof) fromInline++; else fromIwd++;
                    if (label.StartsWith("szXAnims[", StringComparison.Ordinal))
                        anims++;
                }

                // The memory stream's header was written before patching. Write
                // only the adjusted fixed prefix; strings remain serialized in
                // the ZoneCode order represented by Fields.
                var dest = owned.ToArray();
                patched.CopyTo(dest, 0);
                if (!T4WeaponVariableSerializerProof.TryValidateSelfContainedXboxBlob(
                        dest, resolvedName, out var reason))
                    throw new InvalidDataException(
                        $"Weapon '{resolvedName}' real strings violate typed Xbox ZoneCode: {reason}");

                result[i] = new NormalMapLinkerV2StructuralMainEmitter.StructuralPayload(
                    "weapon", resolvedName, dest, true,
                    "DIAGNOSTIC ONLY: original-PC inline/IWD strings + 59 numeric scalar slots; no live child asset closure");
                if (thisBody > 0) bodiesWithStrings++;
                bodyCount++;
            }
        }

        if (bodyCount != 71 || proofCount != 2 || inserted < 1)
            throw new InvalidDataException(
                $"Source-owned XString proof insufficient: bodies={bodyCount}/71, source proofs={proofCount}/2, verified strings={inserted}.");
        return new Result(result, bodyCount, inserted, fromInline, fromIwd,
            bodiesWithStrings, anims, proofCount);
    }

    private static Dictionary<string, string> ExtractOpeningInlineXStrings(byte[] proof, string name)
    {
        var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pos = FixedBytes;
        foreach (var (label, offset) in new[]
            { ("szInternalName", 0), ("szDisplayName", 4), ("szOverlayName", 8) })
        {
            var raw = BinaryPrimitives.ReadUInt32BigEndian(proof.AsSpan(offset, 4));
            if (raw != T4ZonePointer.Following && raw != T4ZonePointer.Insert)
                continue;
            var end = Array.IndexOf(proof, (byte)0, pos);
            if (end < pos || end - pos > 256)
                throw new InvalidDataException(
                    $"Weapon '{name}' inline {label} lacks a bounded exact string in Alpha 7b.");
            if (label != "szInternalName")
                captured.Add(label, Encoding.Latin1.GetString(proof, pos, end - pos));
            pos = end + 1;
        }
        return captured;
    }

    private static bool SafeLatin1CString(string value)
    {
        if (value.Length is < 1 or > 256) return false;
        foreach (var ch in value)
            if (ch is < ' ' or > '\u00FF' || ch == '\u007F')
                return false;
        return true;
    }
}
