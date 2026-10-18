namespace WaW360.Core.FastFiles;

public static class T4AssetTypeCatalog
{
    private static readonly string[] PcTypes =
    {
        "xmodelpieces",      // 0
        "physpreset",        // 1
        "physconstraints",   // 2
        "destructibledef",   // 3
        "xanim",             // 4
        "xmodel",            // 5
        "material",          // 6
        "techset",           // 7
        "image",             // 8
        "sound",             // 9
        "loaded_sound",      // 10
        "clipmap",           // 11
        "clipmap_pvs",       // 12
        "comworld",          // 13
        "gameworld_sp",      // 14
        "gameworld_mp",      // 15
        "map_ents",          // 16
        "gfxworld",          // 17
        "lightdef",          // 18
        "ui_map",            // 19
        "font",              // 20
        "menulist",          // 21
        "menu",              // 22
        "localize",          // 23
        "weapon",            // 24
        "snddriver_globals", // 25
        "fx",                // 26
        "impactfx",          // 27
        "aitype",            // 28
        "mptype",            // 29
        "character",         // 30
        "xmodelalias",       // 31
        "rawfile",           // 32
        "stringtable",       // 33
        "packindex"          // 34
    };

    private static readonly string[] XboxTypes =
    {
        "xmodelpieces",      // 0
        "physpreset",        // 1
        "physconstraints",   // 2
        "destructibledef",   // 3
        "xanim",             // 4
        "xmodel",            // 5
        "material",          // 6
        "pixelshader",       // 7 - Xbox-specific insertion
        "techset",           // 8
        "image",             // 9
        "sound",             // 10
        "loaded_sound",      // 11
        "clipmap",           // 12
        "clipmap_pvs",       // 13
        "comworld",          // 14
        "gameworld_sp",      // 15
        "gameworld_mp",      // 16
        "map_ents",          // 17
        "gfxworld",          // 18
        "lightdef",          // 19
        "ui_map",            // 20
        "font",              // 21
        "menulist",          // 22
        "menu",              // 23
        "localize",          // 24
        "weapon",            // 25
        "snddriver_globals", // 26
        "fx",                // 27
        "impactfx",          // 28
        "aitype",            // 29
        "mptype",            // 30
        "character",         // 31
        "xmodelalias",       // 32
        "rawfile",           // 33
        "stringtable",       // 34
        "packindex"          // 35
    };

    public static int MaxTypeId(WaWPlatform platform) =>
        platform == WaWPlatform.Xbox360 ? XboxTypes.Length - 1 : PcTypes.Length - 1;

    public static string GetName(int typeId, WaWPlatform platform)
    {
        var table = platform == WaWPlatform.Xbox360 ? XboxTypes : PcTypes;
        return typeId >= 0 && typeId < table.Length ? table[typeId] : $"unknown_{typeId}";
    }

    public static IReadOnlyList<string> GetAll(WaWPlatform platform) =>
        platform == WaWPlatform.Xbox360 ? XboxTypes : PcTypes;
}
