using System.Text;

namespace WaW360.Core.FastFiles;

public sealed class T4PcNativeRootOrderOracleResult
{
    public required string PcFastFile { get; init; }
    public required string NativeFastFile { get; init; }
    public required int PcAssets { get; init; }
    public required int NativeAssets { get; init; }
    public required int ExactMappedTypePrefix { get; init; }
    public required int? FirstMismatch { get; init; }
    public required bool PcMappedTypeMajor { get; init; }
    public required bool NativeTypeMajor { get; init; }
    public required int ScriptStringExactPrefix { get; init; }
    public required bool ScriptStringSetsEqual { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// Read-only PC-main vs native-Xbox root-order oracle.
///
/// This does not assume the PC XAsset table is already the correct final Xbox graph.
/// It asks a narrower question: does the known-working native linker preserve the
/// source root TYPE sequence for the roots that already exist on PC, after the one
/// proven T4 platform type rewrite clipmap -> clipmap_pvs?
/// </summary>
public static class T4PcNativeRootOrderOracle
{
    public static async Task<T4PcNativeRootOrderOracleResult> CompareAsync(
        string pcFastFile,
        string nativeFastFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pcFastFile))
            throw new FileNotFoundException("PC source fastfile not found.", pcFastFile);
        if (!File.Exists(nativeFastFile))
            throw new FileNotFoundException("Native Xbox fastfile not found.", nativeFastFile);

        var pc = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, cancellationToken);
        var native = await FastFileAnalysisCache.GetZoneAsync(nativeFastFile, cancellationToken);

        if (pc.Platform != WaWPlatform.PC)
            throw new InvalidDataException($"Source parsed as {pc.Platform}, expected PC.");
        if (native.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException($"Oracle parsed as {native.Platform}, expected Xbox360.");

        var pdoc = pc.Document;
        var ndoc = native.Document;

        var mappedPcTypes = pdoc.Assets
            .Select(a => MapPcType(a.TypeName))
            .ToArray();
        var nativeTypes = ndoc.Assets
            .Select(a => a.TypeName)
            .ToArray();

        var common = Math.Min(mappedPcTypes.Length, nativeTypes.Length);
        var prefix = 0;
        while (prefix < common &&
               mappedPcTypes[prefix].Equals(nativeTypes[prefix], StringComparison.OrdinalIgnoreCase))
            prefix++;

        int? mismatch = prefix < common
            ? prefix
            : mappedPcTypes.Length == nativeTypes.Length ? null : common;

        var pcStrings = pdoc.ScriptStrings.Select(s => s.Value ?? "<NULL>").ToList();
        var nativeStrings = ndoc.ScriptStrings.Select(s => s.Value ?? "<NULL>").ToList();
        var scriptCommon = Math.Min(pcStrings.Count, nativeStrings.Count);
        var scriptPrefix = 0;
        while (scriptPrefix < scriptCommon &&
               string.Equals(pcStrings[scriptPrefix], nativeStrings[scriptPrefix], StringComparison.Ordinal))
            scriptPrefix++;

        var pcSet = pcStrings.ToHashSet(StringComparer.Ordinal);
        var nativeSet = nativeStrings.ToHashSet(StringComparer.Ordinal);
        var sameSet = pcSet.SetEquals(nativeSet);

        var sb = new StringBuilder(32 * 1024);
        sb.AppendLine("WaW360 PC main -> native Xbox root-order oracle");
        sb.AppendLine(new string('=', 82));
        sb.AppendLine($"PC:     {pcFastFile}");
        sb.AppendLine($"native: {nativeFastFile}");
        sb.AppendLine();
        sb.AppendLine($"assets: PC={pdoc.AssetCount:N0}; native={ndoc.AssetCount:N0}; delta={ndoc.AssetCount - pdoc.AssetCount:+#;-#;0}");
        sb.AppendLine($"mapped PC type-major={IsTypeMajor(mappedPcTypes)}; native type-major={IsTypeMajor(nativeTypes)}");
        sb.AppendLine($"exact mapped TYPE prefix: {prefix:N0}/{common:N0}");
        if (mismatch is int at)
        {
            sb.AppendLine($"FIRST PC/NATIVE TYPE MISMATCH: index={at:N0}");
            AppendWindow(sb, mappedPcTypes, nativeTypes, at);
        }
        else
        {
            sb.AppendLine("PC/NATIVE TYPE-SEQUENCE MATCH across the full common range.");
        }

        sb.AppendLine();
        sb.AppendLine($"ScriptStrings: PC={pdoc.ScriptStringCount:N0}; native={ndoc.ScriptStringCount:N0}; exact-index prefix={scriptPrefix:N0}; same-set={sameSet}");
        if (!sameSet)
        {
            var nativeOnly = nativeSet.Except(pcSet, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).Take(30).ToList();
            var pcOnly = pcSet.Except(nativeSet, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).Take(30).ToList();
            if (nativeOnly.Count != 0)
            {
                sb.AppendLine("  native-only ScriptStrings (first 30):");
                foreach (var s in nativeOnly) sb.AppendLine("    " + s);
            }
            if (pcOnly.Count != 0)
            {
                sb.AppendLine("  PC-only ScriptStrings (first 30):");
                foreach (var s in pcOnly) sb.AppendLine("    " + s);
            }
        }

        sb.AppendLine();
        sb.AppendLine("TYPE COUNTS: PC main -> native Xbox");
        var types = mappedPcTypes
            .Union(nativeTypes, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => new
            {
                Type = t,
                Pc = mappedPcTypes.Count(x => x.Equals(t, StringComparison.OrdinalIgnoreCase)),
                Native = nativeTypes.Count(x => x.Equals(t, StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(x => Math.Abs(x.Native - x.Pc))
            .ThenBy(x => x.Type, StringComparer.OrdinalIgnoreCase);

        foreach (var row in types)
            sb.AppendLine($"  {row.Type,-20} PC={row.Pc,6:N0} native={row.Native,6:N0} delta={row.Native - row.Pc,7:+#;-#;0}");

        sb.AppendLine();
        sb.AppendLine("INTERPRETATION");
        if (prefix > 16)
            sb.AppendLine($"  PC provenance order follows native farther than the generated type-major candidate did (candidate diverged at #16); source-order preservation is the stronger current hypothesis.");
        if (prefix <= 16)
            sb.AppendLine("  Native ordering diverges from raw PC provenance just as early as the type-major candidate; a dependency-driven linker traversal/order must be reconstructed.");
        if (sameSet && scriptPrefix < scriptCommon)
            sb.AppendLine("  PC/native ScriptStrings contain the same values but in a different index order: typed payloads with ScriptString fields require a deterministic source->Xbox index remap.");
        else if (!sameSet)
            sb.AppendLine("  PC/native ScriptString content differs as well as order; final linker closure must account for additions/removals before index remap.");

        return new T4PcNativeRootOrderOracleResult
        {
            PcFastFile = Path.GetFullPath(pcFastFile),
            NativeFastFile = Path.GetFullPath(nativeFastFile),
            PcAssets = pdoc.AssetCount,
            NativeAssets = ndoc.AssetCount,
            ExactMappedTypePrefix = prefix,
            FirstMismatch = mismatch,
            PcMappedTypeMajor = IsTypeMajor(mappedPcTypes),
            NativeTypeMajor = IsTypeMajor(nativeTypes),
            ScriptStringExactPrefix = scriptPrefix,
            ScriptStringSetsEqual = sameSet,
            Summary = sb.ToString()
        };
    }

    private static string MapPcType(string type) =>
        type.Equals("clipmap", StringComparison.OrdinalIgnoreCase)
            ? "clipmap_pvs"
            : type;

    private static bool IsTypeMajor(IReadOnlyList<string> types)
    {
        var last = int.MinValue;
        foreach (var type in types)
        {
            if (!T4XboxAssetTypeMap.TryGetXboxTypeId(type, out var id))
                return false;
            if (id < last) return false;
            last = id;
        }
        return true;
    }

    private static void AppendWindow(StringBuilder sb, IReadOnlyList<string> pc, IReadOnlyList<string> native, int center)
    {
        var start = Math.Max(0, center - 6);
        var end = Math.Min(Math.Max(pc.Count, native.Count), center + 10);
        sb.AppendLine("  idx    PC mapped                        native");
        for (var i = start; i < end; i++)
        {
            var left = i < pc.Count ? pc[i] : "<end>";
            var right = i < native.Count ? native[i] : "<end>";
            sb.AppendLine($"  {i,5}  {left,-32} {right}");
        }
    }
}
