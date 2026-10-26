using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4XAssetRootOracleTypeRow(
    string TypeName,
    int CandidateCount,
    int NativeCount,
    int Delta,
    string CandidateReferences,
    string NativeReferences);

public sealed class T4XAssetRootOracleResult
{
    public required string CandidateFastFile { get; init; }
    public required string NativeFastFile { get; init; }
    public required int CandidateAssets { get; init; }
    public required int NativeAssets { get; init; }
    public required int CandidateScriptStrings { get; init; }
    public required int NativeScriptStrings { get; init; }
    public required bool CandidateTypeMajor { get; init; }
    public required bool NativeTypeMajor { get; init; }
    public required int ExactTypeSequencePrefix { get; init; }
    public required int? FirstTypeSequenceMismatch { get; init; }
    public required int ScriptStringExactPrefix { get; init; }
    public required int CandidateResolvedSimpleNames { get; init; }
    public required int NativeResolvedSimpleNames { get; init; }
    public required IReadOnlyList<T4XAssetRootOracleTypeRow> Types { get; init; }
    public required IReadOnlyList<string> CandidateOnlySimpleNames { get; init; }
    public required IReadOnlyList<string> NativeOnlySimpleNames { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// Compares a generated Xbox main FF against a known-working native Xbox main FF at
/// the DB/XAsset-root layer. This deliberately avoids claiming payload equivalence.
/// It focuses on the things DB activation sees first: root count, type order, root
/// reference form, script-string table and conservatively resolved simple asset names.
/// </summary>
public static class T4XAssetRootOracle
{
    public static async Task<T4XAssetRootOracleResult> CompareAsync(
        string candidateFastFile,
        string nativeFastFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(candidateFastFile))
            throw new FileNotFoundException("Candidate fastfile not found.", candidateFastFile);
        if (!File.Exists(nativeFastFile))
            throw new FileNotFoundException("Native oracle fastfile not found.", nativeFastFile);

        var candidate = await FastFileAnalysisCache.GetZoneAsync(candidateFastFile, cancellationToken);
        var native = await FastFileAnalysisCache.GetZoneAsync(nativeFastFile, cancellationToken);

        if (candidate.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException($"Candidate parsed as {candidate.Platform}, not Xbox360.");
        if (native.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException($"Native oracle parsed as {native.Platform}, not Xbox360.");

        var cdoc = candidate.Document;
        var ndoc = native.Document;

        var types = cdoc.Assets.Select(a => a.TypeName)
            .Union(ndoc.Assets.Select(a => a.TypeName), StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => Math.Min(
                FirstTypeIndex(cdoc, x),
                FirstTypeIndex(ndoc, x)))
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(type =>
            {
                var ca = cdoc.Assets.Where(a => a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase)).ToList();
                var na = ndoc.Assets.Where(a => a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase)).ToList();
                return new T4XAssetRootOracleTypeRow(
                    type,
                    ca.Count,
                    na.Count,
                    ca.Count - na.Count,
                    ReferenceSummary(ca),
                    ReferenceSummary(na));
            })
            .ToList();

        var commonCount = Math.Min(cdoc.Assets.Count, ndoc.Assets.Count);
        var prefix = 0;
        while (prefix < commonCount &&
               cdoc.Assets[prefix].TypeId == ndoc.Assets[prefix].TypeId)
            prefix++;

        int? firstMismatch = prefix < commonCount ? prefix :
            cdoc.Assets.Count == ndoc.Assets.Count ? null : commonCount;

        var scriptCommon = Math.Min(cdoc.ScriptStrings.Count, ndoc.ScriptStrings.Count);
        var scriptPrefix = 0;
        while (scriptPrefix < scriptCommon &&
               string.Equals(cdoc.ScriptStrings[scriptPrefix].Value,
                             ndoc.ScriptStrings[scriptPrefix].Value,
                             StringComparison.Ordinal))
            scriptPrefix++;

        var candidateScan = T4PayloadScanner.Scan(cdoc, candidate.Bytes);
        var nativeScan = T4PayloadScanner.Scan(ndoc, native.Bytes);

        static string Key(T4ResolvedAsset x) =>
            $"{x.TypeName.ToLowerInvariant()}:{x.Name}";

        var candidateNames = candidateScan.ResolvedAssets
            .Select(Key)
            .ToHashSet(StringComparer.Ordinal);
        var nativeNames = nativeScan.ResolvedAssets
            .Select(Key)
            .ToHashSet(StringComparer.Ordinal);

        var candidateOnly = candidateNames.Except(nativeNames, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Take(120)
            .ToList();
        var nativeOnly = nativeNames.Except(candidateNames, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Take(120)
            .ToList();

        var candidateTypeMajor = IsTypeMajor(cdoc.Assets);
        var nativeTypeMajor = IsTypeMajor(ndoc.Assets);

        var sb = new StringBuilder(32 * 1024);
        sb.AppendLine("WaW360 Xbox XAsset root oracle");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"candidate: {candidateFastFile}");
        sb.AppendLine($"native:    {nativeFastFile}");
        sb.AppendLine();
        sb.AppendLine($"assets: candidate={cdoc.AssetCount:N0}; native={ndoc.AssetCount:N0}; delta={cdoc.AssetCount - ndoc.AssetCount:+#;-#;0}");
        sb.AppendLine($"script strings: candidate={cdoc.ScriptStringCount:N0}; native={ndoc.ScriptStringCount:N0}; exact-index prefix={scriptPrefix:N0}");
        sb.AppendLine($"type-major order: candidate={candidateTypeMajor}; native={nativeTypeMajor}");
        sb.AppendLine($"exact root TYPE sequence prefix: {prefix:N0}/{commonCount:N0}");
        if (firstMismatch is int mismatch)
        {
            sb.AppendLine($"FIRST TYPE-SEQUENCE MISMATCH: index={mismatch:N0}");
            AppendWindow(sb, cdoc, ndoc, mismatch);
        }
        else
        {
            sb.AppendLine("TYPE-SEQUENCE MATCH: both XAsset tables have identical type IDs at every root index.");
        }

        sb.AppendLine();
        sb.AppendLine("TYPE COUNTS / ROOT REFERENCE FORMS");
        sb.AppendLine("  type                  candidate   native   delta   candidate refs                     native refs");
        foreach (var row in types)
        {
            sb.AppendLine(
                $"  {row.TypeName,-20} {row.CandidateCount,9:N0} {row.NativeCount,8:N0} {row.Delta,7:+#;-#;0}   {row.CandidateReferences,-34} {row.NativeReferences}");
        }

        sb.AppendLine();
        sb.AppendLine("CONSERVATIVELY RESOLVED SIMPLE NAMES");
        sb.AppendLine($"  candidate={candidateScan.ResolvedAssets.Count:N0}; native={nativeScan.ResolvedAssets.Count:N0}");
        sb.AppendLine($"  candidate-only={candidateNames.Except(nativeNames, StringComparer.Ordinal).Count():N0}; native-only={nativeNames.Except(candidateNames, StringComparer.Ordinal).Count():N0}");
        if (nativeOnly.Count != 0)
        {
            sb.AppendLine("  NATIVE-ONLY (first 120):");
            foreach (var name in nativeOnly) sb.AppendLine("    " + name);
        }
        if (candidateOnly.Count != 0)
        {
            sb.AppendLine("  CANDIDATE-ONLY (first 120):");
            foreach (var name in candidateOnly) sb.AppendLine("    " + name);
        }

        if (candidateScan.Warnings.Count != 0 || nativeScan.Warnings.Count != 0)
        {
            sb.AppendLine();
            sb.AppendLine("SCANNER NOTES");
            foreach (var w in candidateScan.Warnings.Take(12)) sb.AppendLine("  candidate: " + w);
            foreach (var w in nativeScan.Warnings.Take(12)) sb.AppendLine("  native: " + w);
        }

        sb.AppendLine();
        sb.AppendLine("INTERPRETATION");
        if (!nativeTypeMajor)
            sb.AppendLine("  IMPORTANT: the known-working native FF is NOT globally type-major. A generated globally type-major table is therefore not a proven native invariant.");
        if (cdoc.AssetCount != ndoc.AssetCount)
            sb.AppendLine("  The candidate and native DB root tables have different cardinality; DB activation sees a different graph before payload fidelity is considered.");
        if (firstMismatch is not null)
            sb.AppendLine("  Root type order diverges before the end of the common table; inspect this frontier before attributing the first hardware crash to owned payload bytes.");
        if (scriptPrefix != scriptCommon || cdoc.ScriptStringCount != ndoc.ScriptStringCount)
            sb.AppendLine("  Script-string tables are not index-identical; any root/body field that stores ScriptString indices needs explicit remap proof.");

        return new T4XAssetRootOracleResult
        {
            CandidateFastFile = Path.GetFullPath(candidateFastFile),
            NativeFastFile = Path.GetFullPath(nativeFastFile),
            CandidateAssets = cdoc.AssetCount,
            NativeAssets = ndoc.AssetCount,
            CandidateScriptStrings = cdoc.ScriptStringCount,
            NativeScriptStrings = ndoc.ScriptStringCount,
            CandidateTypeMajor = candidateTypeMajor,
            NativeTypeMajor = nativeTypeMajor,
            ExactTypeSequencePrefix = prefix,
            FirstTypeSequenceMismatch = firstMismatch,
            ScriptStringExactPrefix = scriptPrefix,
            CandidateResolvedSimpleNames = candidateScan.ResolvedAssets.Count,
            NativeResolvedSimpleNames = nativeScan.ResolvedAssets.Count,
            Types = types,
            CandidateOnlySimpleNames = candidateOnly,
            NativeOnlySimpleNames = nativeOnly,
            Summary = sb.ToString()
        };
    }

    private static int FirstTypeIndex(T4ZoneDocument doc, string type)
    {
        var hit = doc.Assets.FirstOrDefault(a =>
            a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase));
        return hit is null ? int.MaxValue : hit.Index;
    }

    private static bool IsTypeMajor(IReadOnlyList<T4AssetEntry> assets)
    {
        var last = int.MinValue;
        foreach (var asset in assets)
        {
            if (asset.TypeId < last) return false;
            last = asset.TypeId;
        }
        return true;
    }

    private static string ReferenceSummary(IReadOnlyList<T4AssetEntry> assets)
    {
        var groups = assets
            .GroupBy(a => a.ReferenceKind)
            .ToDictionary(g => g.Key, g => g.Count());
        return $"shared={groups.GetValueOrDefault(T4AssetReferenceKind.InlineShared)}, " +
               $"insert={groups.GetValueOrDefault(T4AssetReferenceKind.Inline)}, " +
               $"packed={groups.GetValueOrDefault(T4AssetReferenceKind.OffsetOrAlias)}, " +
               $"null={groups.GetValueOrDefault(T4AssetReferenceKind.Null)}";
    }

    private static void AppendWindow(
        StringBuilder sb,
        T4ZoneDocument candidate,
        T4ZoneDocument native,
        int center)
    {
        var start = Math.Max(0, center - 6);
        var end = Math.Min(Math.Max(candidate.Assets.Count, native.Assets.Count), center + 10);
        sb.AppendLine("  idx    candidate                         native");
        for (var i = start; i < end; i++)
        {
            var left = i < candidate.Assets.Count
                ? $"{candidate.Assets[i].TypeName}(0x{candidate.Assets[i].TypeId:X})/{candidate.Assets[i].ReferenceKind}"
                : "<end>";
            var right = i < native.Assets.Count
                ? $"{native.Assets[i].TypeName}(0x{native.Assets[i].TypeId:X})/{native.Assets[i].ReferenceKind}"
                : "<end>";
            sb.AppendLine($"  {i,5}  {left,-34} {right}");
        }
    }
}
