namespace WaW360.Core.Maps;

public sealed record LinkerV2GoldenSpec(
    string Name,
    IReadOnlyList<string> CanonicalBaseNames,
    long MainFastFileBytes,
    long MainZoneBytes,
    string MainSha256,
    int MainScriptStrings,
    int MainAssets,
    int MainInlineSharedRoots,
    int MainInsertRoots,
    int MainPackedAliasRoots,
    IReadOnlyDictionary<string, int> MainAssetCounts,
    IReadOnlyDictionary<string, uint> MainBlockSizes,
    long LoadFastFileBytes,
    long LoadZoneBytes,
    string LoadSha256,
    int LoadAssets,
    int XmaFiles,
    long XmaBytes);

/// <summary>
/// Derived fingerprints only. No reference fastfile/audio payload bytes are embedded.
/// These known-working packages are validation oracles, never conversion dependencies.
/// </summary>
public static class LinkerV2GoldenCatalog
{
    private static readonly LinkerV2GoldenSpec Simpsons = new(
        "Working Xbox Simpsons (friend converter / t4ff)",
        new[] { "simpsons" },
        81_854_217,
        169_856_466,
        "1d3d7c2c111b123e38193d4049157921d341a281f74770fdb9d40a60d0e2977a",
        714,
        5_686,
        5_659,
        0,
        27,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["sound"] = 3747,
            ["xanim"] = 814,
            ["xmodel"] = 304,
            ["fx"] = 253,
            ["localize"] = 181,
            ["rawfile"] = 151,
            ["techset"] = 126,
            ["weapon"] = 72,
            ["material"] = 30,
            ["stringtable"] = 2,
            ["comworld"] = 1,
            ["gameworld_sp"] = 1,
            ["gfxworld"] = 1,
            ["lightdef"] = 1,
            ["clipmap_pvs"] = 1,
            ["menulist"] = 1
        },
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Temp"] = 2_688,
            ["Runtime"] = 371_872,
            ["LargeRuntime"] = 84_750_336,
            ["PhysicalRuntime"] = 0,
            ["Virtual"] = 56_899_836,
            ["Large"] = 26_517_504,
            ["Physical"] = 1_023_440
        },
        310_744,
        558_049,
        "ad2e54f2eff425f2482ba33e3de5ae1c90ea17469e7a117f3a95c058411f60bc",
        7,
        1_881,
        94_115_840);

    private static readonly LinkerV2GoldenSpec Aztec = new(
        "Known-working Xbox Aztec (CoDXe/t4ff)",
        new[] { "nazi_zombie_aztec" },
        82_739_469,
        164_876_805,
        "e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f",
        645,
        8_509,
        1,
        8_508,
        0,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["sound"] = 3774,
            ["loaded_sound"] = 1532,
            ["image"] = 666,
            ["xanim"] = 602,
            ["material"] = 537,
            ["pixelshader"] = 436,
            ["xmodel"] = 284,
            ["fx"] = 233,
            ["localize"] = 177,
            ["rawfile"] = 120,
            ["techset"] = 70,
            ["weapon"] = 69,
            ["physpreset"] = 3,
            ["stringtable"] = 1,
            ["comworld"] = 1,
            ["gfxworld"] = 1,
            ["gameworld_sp"] = 1,
            ["map_ents"] = 1,
            ["clipmap_pvs"] = 1
        },
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Temp"] = 2_476,
            ["Runtime"] = 174_864,
            ["LargeRuntime"] = 67_379_200,
            ["PhysicalRuntime"] = 0,
            ["Virtual"] = 59_892_484,
            ["Large"] = 36_446_208,
            ["Physical"] = 559_280
        },
        364_367,
        558_061,
        "5b3e4433b245139db957f6e3ae9de9d3ef6170d9db3ed2f8f185456bed68093c",
        7,
        16,
        1_880_064);

    public static IReadOnlyList<LinkerV2GoldenSpec> All { get; } = new[] { Simpsons, Aztec };

    public static LinkerV2GoldenSpec? Find(string canonicalBaseName) =>
        All.FirstOrDefault(x => x.CanonicalBaseNames.Any(a => a.Equals(canonicalBaseName, StringComparison.OrdinalIgnoreCase)));
}
