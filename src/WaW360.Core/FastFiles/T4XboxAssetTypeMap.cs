namespace WaW360.Core.FastFiles;

/// <summary>
/// Maps T4 PC asset names to the Xbox 360 XAsset type table. Xbox inserts
/// pixelshader at slot 7, so most types at/after PC techset shift by one.
/// </summary>
public static class T4XboxAssetTypeMap
{
    private static readonly IReadOnlyDictionary<string, int> XboxIds =
        T4AssetTypeCatalog.GetAll(WaWPlatform.Xbox360)
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetXboxTypeId(string typeName, out int typeId)
    {
        // T4 PC exposes the map collision root as `clipmap`, while the Xbox 360
        // asset table consumes that platform form through the `clipmap_pvs` slot.
        // Treat this as a platform type mapping, not as a delete/promote count hack.
        if (typeName.Equals("clipmap", StringComparison.OrdinalIgnoreCase))
            return XboxIds.TryGetValue("clipmap_pvs", out typeId);

        return XboxIds.TryGetValue(typeName, out typeId);
    }

    public static int GetXboxTypeId(string typeName) =>
        TryGetXboxTypeId(typeName, out var value)
            ? value
            : throw new InvalidDataException($"No Xbox T4 XAsset id is known for '{typeName}'.");
}
