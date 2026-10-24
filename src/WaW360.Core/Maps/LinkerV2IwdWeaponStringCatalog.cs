using System.IO.Compression;
using System.Text;

namespace WaW360.Core.Maps;

/// <summary>
/// Reads authoritative custom weapon info strings from map IWD source archives.
///
/// T4/IW3 raw weapon files use the WEAPONFILE\\key\\value... format. This catalog is deliberately
/// narrow: it exposes only string fields whose meaning is identical in the existing typed WeaponDef
/// proof. It never fabricates a weapon and never treats an unrelated stock weapon as a custom one.
/// </summary>
internal static class LinkerV2IwdWeaponStringCatalog
{
    internal sealed record WeaponStrings(
        string AssetName,
        IReadOnlyDictionary<string, string> ByProofLabel,
        string SourceIwd,
        string SourceEntry);

    public static async Task<IReadOnlyDictionary<string, WeaponStrings>> BuildAsync(
        IEnumerable<string> iwdPaths,
        CancellationToken cancellationToken)
    {
        var output = new Dictionary<string, WeaponStrings>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in iwdPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var zip = ZipFile.OpenRead(path);
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = entry.FullName.Replace('\\', '/');
                if (!normalized.StartsWith("weapons/mp/", StringComparison.OrdinalIgnoreCase) &&
                    !normalized.StartsWith("weapons/sp/", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (normalized.EndsWith("/", StringComparison.Ordinal))
                    continue;

                var assetName = Path.GetFileName(normalized);
                if (string.IsNullOrWhiteSpace(assetName))
                    continue;

                string text;
                try
                {
                    await using var stream = entry.Open();
                    using var reader = new StreamReader(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
                    text = await reader.ReadToEndAsync(cancellationToken);
                }
                catch
                {
                    continue;
                }

                if (!TryParseWeaponInfoString(text, out var fields))
                    continue;

                var byLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    // OAT's IW3/T4 loaders always seed szInternalName from the asset filename.
                    ["szInternalName"] = assetName
                };
                Add(fields, byLabel, "displayName", "szDisplayName");
                Add(fields, byLabel, "AIOverlayDescription", "szOverlayName");
                Add(fields, byLabel, "modeName", "szModeName");
                Add(fields, byLabel, "standMountedWeapdef", "standMountedWeapdef");
                Add(fields, byLabel, "crouchMountedWeapdef", "crouchMountedWeapdef");
                Add(fields, byLabel, "proneMountedWeapdef", "proneMountedWeapdef");
                Add(fields, byLabel, "ammoName", "szAmmoName");
                Add(fields, byLabel, "clipName", "szClipName");
                Add(fields, byLabel, "sharedAmmoCapName", "szSharedAmmoCapName");
                Add(fields, byLabel, "altWeapon", "szAltWeaponName");
                Add(fields, byLabel, "script", "szScript");
                Add(fields, byLabel, "useHintString", "szUseHintString");
                Add(fields, byLabel, "dropHintString", "dropHintString");
                Add(fields, byLabel, "fireRumble", "fireRumble");
                Add(fields, byLabel, "meleeImpactRumble", "meleeImpactRumble");
                Add(fields, byLabel, "flameTableFirstPerson", "flameTableFirstPerson");
                Add(fields, byLabel, "flameTableThirdPerson", "flameTableThirdPerson");

                // T4/IW3 share this 35-slot WeaponAnimSlot layout. Slot 0 (ROOT) has no raw-file
                // field; the remaining slots are the exact OAT weapon-field names.
                foreach (var (key, slot) in new (string Key, int Slot)[]
                {
                    ("idleAnim", 1), ("emptyIdleAnim", 2), ("fireAnim", 3), ("holdFireAnim", 4),
                    ("lastShotAnim", 5), ("rechamberAnim", 6), ("meleeAnim", 7), ("meleeChargeAnim", 8),
                    ("reloadAnim", 9), ("reloadEmptyAnim", 10), ("reloadStartAnim", 11), ("reloadEndAnim", 12),
                    ("raiseAnim", 13), ("firstRaiseAnim", 14), ("dropAnim", 15), ("altRaiseAnim", 16),
                    ("altDropAnim", 17), ("quickRaiseAnim", 18), ("quickDropAnim", 19), ("emptyRaiseAnim", 20),
                    ("emptyDropAnim", 21), ("sprintInAnim", 22), ("sprintLoopAnim", 23), ("sprintOutAnim", 24),
                    ("deployAnim", 25), ("breakdownAnim", 26), ("detonateAnim", 27), ("nightVisionWearAnim", 28),
                    ("nightVisionRemoveAnim", 29), ("adsFireAnim", 30), ("adsLastShotAnim", 31),
                    ("adsRechamberAnim", 32), ("adsUpAnim", 33), ("adsDownAnim", 34)
                })
                    Add(fields, byLabel, key, $"szXAnims[{slot}]");

                // Later archives win deterministically. This mirrors ordinary mod search-path override
                // semantics without copying the IWD into the Xbox package.
                output[assetName] = new WeaponStrings(
                    assetName,
                    byLabel,
                    path,
                    normalized);
            }
        }

        return output;
    }

    private static void Add(
        IReadOnlyDictionary<string, string> source,
        Dictionary<string, string> destination,
        string sourceKey,
        string proofLabel)
    {
        if (source.TryGetValue(sourceKey, out var value))
            destination[proofLabel] = value;
    }

    private static bool TryParseWeaponInfoString(
        string text,
        out IReadOnlyDictionary<string, string> fields)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        fields = result;

        // OAT InfoString::FromStream("WEAPONFILE", ...) splits strictly on '\\'.
        var parts = text.TrimEnd('\0', '\r', '\n').Split('\\');
        if (parts.Length == 0 || !parts[0].Equals("WEAPONFILE", StringComparison.Ordinal))
            return false;
        if ((parts.Length - 1) % 2 != 0)
            return false;

        for (var i = 1; i + 1 < parts.Length; i += 2)
        {
            if (parts[i].Length == 0)
                return false;
            result[parts[i]] = parts[i + 1];
        }

        return true;
    }
}
