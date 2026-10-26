using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed record T4PcNativeSemanticIdentity(
    int PcPoolIndex,
    int NativeIndex,
    string Type,
    string PcName,
    string NativeName,
    string Rule);

public sealed record T4PcNativeSemanticTypeSummary(
    string Type,
    int PcPool,
    int Native,
    int Matched,
    int PcOnly,
    int NativeOnly,
    int Delta);

public sealed record T4PcNativeScriptStringRemap(
    int PcIndex,
    int NativeIndex,
    string? Value);

public sealed class T4PcNativeSemanticOracleResult
{
    public required string PcFastFile { get; init; }
    public required string NativeFastFile { get; init; }
    public required string UnlinkerExe { get; init; }
    public required int PcRootAssets { get; init; }
    public required int PcOatPoolAssets { get; init; }
    public required int NativeRootAssets { get; init; }
    public required int MatchedSemanticIdentities { get; init; }
    public required int PcOnlySemanticIdentities { get; init; }
    public required int NativeOnlySemanticIdentities { get; init; }
    public required int PcEmbeddedDependencyAssets { get; init; }
    public required int NativeVsPcSemanticDelta { get; init; }
    public required bool ScriptStringMultisetEqual { get; init; }
    public required int ScriptStringExactIndexMatches { get; init; }
    public required IReadOnlyList<T4PcNativeSemanticIdentity> Matches { get; init; }
    public required IReadOnlyList<T4PcNativeSemanticTypeSummary> Types { get; init; }
    public required IReadOnlyList<T4PcNativeScriptStringRemap> ScriptStringRemap { get; init; }
    public required string Summary { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonManifestFile { get; init; }
    public required string PcOatLogFile { get; init; }
}

/// <summary>
/// Compares OAT's fully loaded PC asset pool against a known-working native Xenon
/// root identity manifest. This intentionally distinguishes:
///
///   PC XAssetList roots -> OAT-loaded PC semantic pool -> native Xbox root pool.
///
/// OAT --list iterates ZoneAssetPools insertion order, so on PC it can contain
/// dependencies discovered while loading that are not explicit XAssetList roots.
/// The native Xenon oracle separately proves when that loaded pool is root-for-root
/// identical to the native XAsset table.
///
/// No donor payload bytes are copied. This is identity/remap evidence only.
/// </summary>
public static class T4PcNativeSemanticOracle
{
    private static readonly HashSet<string> OatPcTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "xmodelpieces", "physpreset", "physconstraints", "destructibledef",
        "xanim", "xmodel", "material", "techniqueset", "image", "sound",
        "loadedsound", "clipmap", "clipmap_pvs", "comworld", "gameworldsp",
        "gameworldmp", "mapents", "gfxworld", "lightdef", "uimap", "font",
        "menulist", "menu", "localize", "weapon", "snddriverglobals", "fx",
        "impactfx", "aitype", "mptype", "character", "xmodelalias", "rawfile",
        "stringtable", "packindex"
    };

    private sealed record OatIdentity(int Index, string Type, string Name);

    public static async Task<T4PcNativeSemanticOracleResult> CompareAsync(
        string pcFastFile,
        string nativeFastFile,
        string outputDirectory,
        string? explicitUnlinkerExe = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pcFastFile))
            throw new FileNotFoundException("PC source fastfile not found.", pcFastFile);
        if (!File.Exists(nativeFastFile))
            throw new FileNotFoundException("Native Xbox fastfile not found.", nativeFastFile);

        var pc = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, cancellationToken);
        if (pc.Platform != WaWPlatform.PC)
            throw new InvalidDataException($"Source parsed as {pc.Platform}, expected PC.");

        Directory.CreateDirectory(outputDirectory);

        // Reuse the already-proven native oracle. Its result also gives us the exact
        // pinned Unlinker.exe that should be used for the PC side of this comparison.
        var native = await T4XenonNativeIdentityOracle.RunAsync(
            nativeFastFile,
            outputDirectory,
            explicitUnlinkerExe,
            cancellationToken);

        var pcStem = Path.GetFileNameWithoutExtension(pcFastFile);
        var pcOatLog = Path.Combine(outputDirectory, pcStem + "_oat_pc_list.log");
        var pcList = await RunOatListAsync(
            native.UnlinkerExe,
            pcFastFile,
            pcOatLog,
            cancellationToken);

        if (pcList.Count == 0)
            throw new InvalidDataException($"OAT listed no PC assets. See {pcOatLog}");

        var nativeIdentities = native.Entries
            .Select(e => new OatIdentity(e.Index, e.OatTypeName, e.Name))
            .ToList();

        var nativeLookup = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in nativeIdentities)
            nativeLookup[IdentityKey(e.Type, e.Name)] = e.Index;

        var usedNative = new HashSet<int>();
        var matches = new List<T4PcNativeSemanticIdentity>();
        var pcOnly = new List<OatIdentity>();

        foreach (var pcEntry in pcList)
        {
            var matched = false;
            foreach (var candidate in CandidateNativeNames(pcEntry.Type, pcEntry.Name))
            {
                if (!nativeLookup.TryGetValue(IdentityKey(pcEntry.Type, candidate.Name), out var nativeIndex))
                    continue;
                if (!usedNative.Add(nativeIndex))
                    continue;

                matches.Add(new T4PcNativeSemanticIdentity(
                    pcEntry.Index,
                    nativeIndex,
                    pcEntry.Type,
                    pcEntry.Name,
                    candidate.Name,
                    candidate.Rule));
                matched = true;
                break;
            }

            if (!matched)
                pcOnly.Add(pcEntry);
        }

        var nativeOnly = nativeIdentities
            .Where(e => !usedNative.Contains(e.Index))
            .ToList();

        var allTypes = pcList.Select(x => x.Type)
            .Union(nativeIdentities.Select(x => x.Type), StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var typeSummaries = new List<T4PcNativeSemanticTypeSummary>(allTypes.Count);
        foreach (var type in allTypes)
        {
            var pcCount = pcList.Count(x => x.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
            var nativeCount = nativeIdentities.Count(x => x.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
            var matched = matches.Count(x => x.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
            typeSummaries.Add(new T4PcNativeSemanticTypeSummary(
                type,
                pcCount,
                nativeCount,
                matched,
                pcCount - matched,
                nativeCount - matched,
                nativeCount - pcCount));
        }

        var pcStrings = pc.Document.ScriptStrings.Select(x => x.Value).ToList();
        var nativeZone = await FastFileAnalysisCache.GetZoneAsync(nativeFastFile, cancellationToken);
        var nativeStrings = nativeZone.Document.ScriptStrings.Select(x => x.Value).ToList();
        var scriptRemap = BuildScriptStringRemap(pcStrings, nativeStrings, out var scriptMultisetEqual);
        var exactScriptIndices = Math.Min(pcStrings.Count, nativeStrings.Count) == 0
            ? 0
            : Enumerable.Range(0, Math.Min(pcStrings.Count, nativeStrings.Count))
                .Count(i => string.Equals(pcStrings[i], nativeStrings[i], StringComparison.Ordinal));

        var pcEmbeddedDependencies = pcList.Count - pc.Document.AssetCount;
        var nativeVsPcSemanticDelta = native.Assets - pcList.Count;

        var textPath = Path.Combine(outputDirectory, pcStem + "_pc_native_semantic_oracle.txt");
        var jsonPath = Path.Combine(outputDirectory, pcStem + "_pc_native_semantic_oracle.json");

        var ruleCounts = matches
            .GroupBy(x => x.Rule, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var manifest = new
        {
            format = "WaW360.T4.PcNativeSemanticOracle.v1",
            pcFastFile = Path.GetFullPath(pcFastFile),
            nativeFastFile = Path.GetFullPath(nativeFastFile),
            unlinkerExe = native.UnlinkerExe,
            pcRootAssets = pc.Document.AssetCount,
            pcOatPoolAssets = pcList.Count,
            pcEmbeddedDependencyAssets = pcEmbeddedDependencies,
            nativeRootAssets = native.Assets,
            nativeVsPcSemanticDelta,
            matchedSemanticIdentities = matches.Count,
            pcOnlySemanticIdentities = pcOnly.Count,
            nativeOnlySemanticIdentities = nativeOnly.Count,
            identityRuleCounts = ruleCounts,
            scriptStrings = new
            {
                pc = pc.Document.ScriptStringCount,
                native = nativeZone.Document.ScriptStringCount,
                multisetEqual = scriptMultisetEqual,
                exactIndexMatches = exactScriptIndices,
                remapCount = scriptRemap.Count
            },
            types = typeSummaries,
            matches,
            pcOnly,
            nativeOnly,
            scriptStringRemap = scriptRemap
        };

        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("WaW360Converter — PC OAT SEMANTIC POOL -> NATIVE XBOX IDENTITY ORACLE");
        sb.AppendLine(new string('=', 100));
        sb.AppendLine($"PC:     {Path.GetFullPath(pcFastFile)}");
        sb.AppendLine($"native: {Path.GetFullPath(nativeFastFile)}");
        sb.AppendLine($"OAT:    {native.UnlinkerExe}");
        sb.AppendLine();
        sb.AppendLine("GRAPH SHAPE");
        sb.AppendLine($"  PC raw XAssetList roots:       {pc.Document.AssetCount,6:N0}");
        sb.AppendLine($"  PC OAT loaded semantic pool:   {pcList.Count,6:N0}  ({pcEmbeddedDependencies:+#;-#;0} vs raw roots)");
        sb.AppendLine($"  Native Xbox root/pool assets:  {native.Assets,6:N0}  ({nativeVsPcSemanticDelta:+#;-#;0} vs PC semantic pool)");
        sb.AppendLine($"  Matched semantic identities:   {matches.Count,6:N0}");
        sb.AppendLine($"  PC-only after normalization:   {pcOnly.Count,6:N0}");
        sb.AppendLine($"  Xbox-only after normalization: {nativeOnly.Count,6:N0}");
        sb.AppendLine();
        sb.AppendLine("IDENTITY NORMALIZATION RULES");
        foreach (var pair in ruleCounts)
            sb.AppendLine($"  {pair.Key,-24} {pair.Value,6:N0}");
        sb.AppendLine();
        sb.AppendLine("TYPE DELTAS: PC OAT semantic pool -> native Xbox");
        sb.AppendLine("  type                 PC pool  native  matched  PC-only  XB-only   delta");
        foreach (var row in typeSummaries.OrderByDescending(x => Math.Abs(x.Delta)).ThenBy(x => x.Type, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine(
                $"  {row.Type,-20} {row.PcPool,7:N0} {row.Native,7:N0} {row.Matched,8:N0} {row.PcOnly,8:N0} {row.NativeOnly,8:N0} {row.Delta,7:+#;-#;0}");

        sb.AppendLine();
        sb.AppendLine("SCRIPTSTRINGS");
        sb.AppendLine($"  PC={pc.Document.ScriptStringCount:N0}; native={nativeZone.Document.ScriptStringCount:N0}; multisetEqual={scriptMultisetEqual}; exactIndexMatches={exactScriptIndices:N0}; remap={scriptRemap.Count:N0}");
        if (scriptMultisetEqual)
            sb.AppendLine("  Complete source-index -> native-index remap is stored in the JSON manifest.");

        sb.AppendLine();
        sb.AppendLine("PC-ONLY SAMPLE (first 40 per type)");
        foreach (var group in pcOnly.GroupBy(x => x.Type, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"  [{group.Key}] count={group.Count():N0}");
            foreach (var e in group.Take(40))
                sb.AppendLine($"    PC#{e.Index,5} {Escape(e.Name)}");
        }

        sb.AppendLine();
        sb.AppendLine("XBOX-ONLY SAMPLE (first 40 per type)");
        foreach (var group in nativeOnly.GroupBy(x => x.Type, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"  [{group.Key}] count={group.Count():N0}");
            foreach (var e in group.Take(40))
                sb.AppendLine($"    XB#{e.Index,5} {Escape(e.Name)}");
        }

        await File.WriteAllTextAsync(textPath, sb.ToString(), new UTF8Encoding(false), cancellationToken);

        return new T4PcNativeSemanticOracleResult
        {
            PcFastFile = Path.GetFullPath(pcFastFile),
            NativeFastFile = Path.GetFullPath(nativeFastFile),
            UnlinkerExe = native.UnlinkerExe,
            PcRootAssets = pc.Document.AssetCount,
            PcOatPoolAssets = pcList.Count,
            NativeRootAssets = native.Assets,
            MatchedSemanticIdentities = matches.Count,
            PcOnlySemanticIdentities = pcOnly.Count,
            NativeOnlySemanticIdentities = nativeOnly.Count,
            PcEmbeddedDependencyAssets = pcEmbeddedDependencies,
            NativeVsPcSemanticDelta = nativeVsPcSemanticDelta,
            ScriptStringMultisetEqual = scriptMultisetEqual,
            ScriptStringExactIndexMatches = exactScriptIndices,
            Matches = matches,
            Types = typeSummaries,
            ScriptStringRemap = scriptRemap,
            Summary = sb.ToString(),
            TextReportFile = textPath,
            JsonManifestFile = jsonPath,
            PcOatLogFile = pcOatLog
        };
    }

    private static async Task<List<OatIdentity>> RunOatListAsync(
        string unlinkerExe,
        string fastFile,
        string rawLog,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = unlinkerExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(fastFile)) ?? Environment.CurrentDirectory
        };
        psi.ArgumentList.Add("--list");
        psi.ArgumentList.Add("--no-color");
        psi.ArgumentList.Add(Path.GetFullPath(fastFile));

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start OAT Unlinker: {unlinkerExe}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        process.WaitForExit();

        var combined = new StringBuilder();
        combined.AppendLine("STDOUT");
        combined.Append(stdout);
        combined.AppendLine();
        combined.AppendLine("STDERR");
        combined.Append(stderr);
        await File.WriteAllTextAsync(rawLog, combined.ToString(), new UTF8Encoding(false), cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidDataException($"OAT Unlinker exited with code {process.ExitCode}. See {rawLog}");

        var stdoutList = ParseAssetList(stdout.ToString()).ToList();
        var stderrList = ParseAssetList(stderr.ToString()).ToList();
        var selected = stdoutList.Count >= stderrList.Count ? stdoutList : stderrList;
        return selected.Select((x, i) => new OatIdentity(i, x.Type, x.Name)).ToList();
    }

    private static IEnumerable<(string Type, string Name)> ParseAssetList(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = StripLogPrefix(line.Trim());
            var comma = trimmed.IndexOf(',');
            if (comma <= 0) continue;

            var type = trimmed[..comma].Trim();
            if (!OatPcTypes.Contains(type)) continue;

            yield return (type, trimmed[(comma + 1)..].Trim());
        }
    }

    private static IEnumerable<(string Name, string Rule)> CandidateNativeNames(string type, string pcName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        IEnumerable<(string Name, string Rule)> Add(string name, string rule)
        {
            if (seen.Add(name))
                yield return (name, rule);
        }

        foreach (var x in Add(pcName, "exact"))
            yield return x;

        if (type.Equals("loadedsound", StringComparison.OrdinalIgnoreCase))
        {
            var withoutWav = pcName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                ? pcName[..^4]
                : pcName;

            foreach (var x in Add(withoutWav, "strip-.wav"))
                yield return x;
            foreach (var x in Add(withoutWav + "_wj", "strip-.wav + _wj"))
                yield return x;

            var slashNormalized = withoutWav.Replace("//", "/", StringComparison.Ordinal);
            foreach (var x in Add(slashNormalized, "strip-.wav + slash-normalize"))
                yield return x;
            foreach (var x in Add(slashNormalized + "_wj", "strip-.wav + slash-normalize + _wj"))
                yield return x;

            foreach (var x in Add("," + withoutWav, "strip-.wav + leading-comma"))
                yield return x;
            foreach (var x in Add("," + withoutWav + "_wj", "strip-.wav + leading-comma + _wj"))
                yield return x;
            yield break;
        }

        foreach (var x in Add(pcName + "_wj", "append-_wj"))
            yield return x;
        foreach (var x in Add("," + pcName, "leading-comma"))
            yield return x;
        foreach (var x in Add("," + pcName + "_wj", "leading-comma + _wj"))
            yield return x;
    }

    private static List<T4PcNativeScriptStringRemap> BuildScriptStringRemap(
        IReadOnlyList<string?> pc,
        IReadOnlyList<string?> native,
        out bool multisetEqual)
    {
        static string Key(string? value) => value is null ? "\0<NULL>" : "\u0001" + value;

        var queues = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (var i = 0; i < native.Count; i++)
        {
            var key = Key(native[i]);
            if (!queues.TryGetValue(key, out var queue))
            {
                queue = new Queue<int>();
                queues.Add(key, queue);
            }
            queue.Enqueue(i);
        }

        var remap = new List<T4PcNativeScriptStringRemap>(pc.Count);
        var allPcMapped = true;
        for (var i = 0; i < pc.Count; i++)
        {
            var key = Key(pc[i]);
            if (!queues.TryGetValue(key, out var queue) || queue.Count == 0)
            {
                allPcMapped = false;
                continue;
            }

            remap.Add(new T4PcNativeScriptStringRemap(i, queue.Dequeue(), pc[i]));
        }

        multisetEqual = allPcMapped
            && pc.Count == native.Count
            && queues.Values.All(q => q.Count == 0);

        return remap;
    }

    private static string IdentityKey(string type, string name) =>
        type.ToLowerInvariant() + "\0" + name;

    private static string StripLogPrefix(string line)
    {
        if (line.StartsWith("[", StringComparison.Ordinal))
        {
            var close = line.IndexOf(']');
            if (close >= 0 && close + 1 < line.Length)
                return line[(close + 1)..].TrimStart();
        }

        var colon = line.IndexOf(':');
        if (colon > 0 && colon < 12)
        {
            var prefix = line[..colon];
            if (prefix.All(ch => char.IsLetter(ch) || char.IsWhiteSpace(ch)))
                return line[(colon + 1)..].TrimStart();
        }

        return line;
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
}
