using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed class T4XenonMaterialRootBindingResult
{
    public required string GraphRoot { get; init; }
    public required string BridgeManifest { get; init; }
    public required string NativeIdentityManifest { get; init; }
    public required int CorpusMaterials { get; init; }
    public required int NativeMaterialRoots { get; init; }
    public required int DirectIdentityMatches { get; init; }
    public required int SemanticMatches { get; init; }
    public required int UnmatchedMaterials { get; init; }
    public required int AmbiguousMaterials { get; init; }
    public required int DuplicateRootClaims { get; init; }
    public required int SharedCalibrationPairs { get; init; }
    public required int SharedCalibrationMatches { get; init; }
    public required int SharedCalibrationWrong { get; init; }
    public required int SharedCalibrationAmbiguousOrMissing { get; init; }
    public required double SharedCalibrationAgreement { get; init; }
    public required double SharedCalibrationCoverage { get; init; }
    public required string SemanticFingerprintMode { get; init; }
    public required bool SemanticFallbackEnabled { get; init; }
    public required bool BindingPass { get; init; }
    public required IReadOnlyList<T4XenonMaterialRootBindingEntry> OwnedBindings { get; init; }
    public required IReadOnlyList<string> Unmatched { get; init; }
    public required IReadOnlyList<T4XenonMaterialBindingAmbiguity> Ambiguities { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonManifestFile { get; init; }
}

public sealed record T4XenonMaterialRootBindingEntry(
    int NativeRootIndex,
    string NativeName,
    int NativeTypeId,
    string NativeTypeName,
    string CorpusName,
    string MatchKind,
    string? SemanticHash);

public sealed record T4XenonMaterialBindingAmbiguity(
    string CorpusName,
    string Kind,
    IReadOnlyList<int> NativeIndices,
    IReadOnlyList<string> NativeNames);

public static class T4XenonMaterialRootBindingPlanner
{
    private enum FingerprintMode
    {
        Strong,
        ImageAgnostic,
        RenderStructure,
        RenderStructureImageAgnostic,
        DependencyGraph,
        DependencyShape
    }

    public static async Task<T4XenonMaterialRootBindingResult> BuildAsync(
        string graphRoot,
        string bridgeManifest,
        string nativeIdentityManifest,
        string outputDirectory,
        int expectedMaterials = 502,
        CancellationToken cancellationToken = default)
    {
        graphRoot = Path.GetFullPath(graphRoot);
        bridgeManifest = Path.GetFullPath(bridgeManifest);
        nativeIdentityManifest = Path.GetFullPath(nativeIdentityManifest);
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        if (!Directory.Exists(graphRoot))
            throw new DirectoryNotFoundException($"Material graph root not found: {graphRoot}");
        if (!File.Exists(bridgeManifest))
            throw new FileNotFoundException("Material bridge manifest not found.", bridgeManifest);
        if (!File.Exists(nativeIdentityManifest))
            throw new FileNotFoundException("Native identity manifest not found.", nativeIdentityManifest);

        var graphNames = LoadGraphNames(graphRoot);
        if (graphNames.Count != expectedMaterials)
            throw new InvalidDataException(
                $"Material graph count {graphNames.Count:N0} != expected {expectedMaterials:N0}.");

        using var bridgeDoc = JsonDocument.Parse(File.ReadAllText(bridgeManifest));
        var bridge = bridgeDoc.RootElement;
        if (!bridge.GetProperty("Verified").GetBoolean())
            throw new InvalidDataException("Material bridge manifest is not verified.");

        var pcDump = bridge.GetProperty("PcDumpDirectory").GetString()
            ?? throw new InvalidDataException("Material bridge has no PC dump directory.");
        var xboxDump = bridge.GetProperty("XboxDumpDirectory").GetString()
            ?? throw new InvalidDataException("Material bridge has no Xbox dump directory.");

        var techniqueMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (bridge.TryGetProperty("Mappings", out var mappings) &&
            mappings.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in mappings.EnumerateArray())
            {
                var pc = row.GetProperty("PcTechniqueSet").GetString() ?? "";
                var xbox = row.GetProperty("XboxTechniqueSet").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(pc) || string.IsNullOrWhiteSpace(xbox))
                    continue;
                if (techniqueMap.TryGetValue(pc, out var prior) &&
                    !prior.Equals(xbox, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Conflicting bridge TechniqueSet mapping for '{pc}': '{prior}' vs '{xbox}'.");
                techniqueMap[pc] = xbox;
            }
        }

        var pcMaterials = LoadMaterialJson(pcDump);
        var xboxMaterials = LoadMaterialJson(xboxDump);
        var roots = LoadNativeIdentity(nativeIdentityManifest);
        var nativeMaterialRoots = roots
            .Where(x => T4XenonXModelCorpus.NormalizeType(x.OatTypeName) == "material")
            .OrderBy(x => x.Index)
            .ToList();

        var calibration = ChooseFingerprintMode(
            pcMaterials,
            xboxMaterials,
            techniqueMap);

        var nativeFingerprintIndex = BuildNativeFingerprintIndex(
            xboxMaterials,
            roots,
            calibration.Mode);

        var bindings = new List<T4XenonMaterialRootBindingEntry>();
        var unmatched = new List<string>();
        var ambiguities = new List<T4XenonMaterialBindingAmbiguity>();
        var usedRoots = new Dictionary<int, string>();
        var direct = 0;
        var semantic = 0;
        var duplicateClaims = 0;

        foreach (var graphName in graphNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directCandidates = T4XenonXModelCorpus.FindReferenceCandidates(
                roots,
                "material",
                graphName);

            if (directCandidates.Count == 1)
            {
                var root = directCandidates[0];
                if (usedRoots.TryGetValue(root.Index, out var prior))
                {
                    duplicateClaims++;
                    ambiguities.Add(new(
                        graphName,
                        $"DIRECT_ROOT_REUSED_BY:{prior}",
                        new[] { root.Index },
                        new[] { root.Name }));
                    continue;
                }

                usedRoots[root.Index] = graphName;
                bindings.Add(new(
                    root.Index,
                    root.Name,
                    root.TypeId,
                    root.TypeName,
                    graphName,
                    "DIRECT_IDENTITY",
                    null));
                direct++;
                continue;
            }

            if (directCandidates.Count > 1)
            {
                ambiguities.Add(new(
                    graphName,
                    "DIRECT_IDENTITY_AMBIGUOUS",
                    directCandidates.Select(x => x.Index).ToArray(),
                    directCandidates.Select(x => x.Name).ToArray()));
                continue;
            }

            if (!calibration.Enabled)
            {
                unmatched.Add(graphName);
                continue;
            }

            if (!pcMaterials.TryGetValue(graphName, out var pcFile))
            {
                unmatched.Add(graphName);
                continue;
            }

            var hash = Fingerprint(
                pcFile,
                isPc: true,
                techniqueMap,
                calibration.Mode);

            if (!nativeFingerprintIndex.TryGetValue(hash, out var semanticCandidates))
            {
                unmatched.Add(graphName);
                continue;
            }

            var available = semanticCandidates
                .Where(x => !usedRoots.ContainsKey(x.Root.Index))
                .GroupBy(x => x.Root.Index)
                .Select(g => g.First())
                .OrderBy(x => x.Root.Index)
                .ToList();

            if (available.Count != 1)
            {
                ambiguities.Add(new(
                    graphName,
                    available.Count == 0
                        ? "SEMANTIC_MATCH_ROOT_ALREADY_USED"
                        : "SEMANTIC_MATCH_AMBIGUOUS",
                    available.Select(x => x.Root.Index).ToArray(),
                    available.Select(x => x.Root.Name).ToArray()));
                continue;
            }

            var selected = available[0].Root;
            usedRoots[selected.Index] = graphName;
            bindings.Add(new(
                selected.Index,
                selected.Name,
                selected.TypeId,
                selected.TypeName,
                graphName,
                "SEMANTIC_FINGERPRINT",
                hash));
            semantic++;
        }

        var pass =
            graphNames.Count == expectedMaterials &&
            bindings.Count == expectedMaterials &&
            unmatched.Count == 0 &&
            ambiguities.Count == 0 &&
            duplicateClaims == 0 &&
            usedRoots.Count == expectedMaterials;

        var txt = Path.Combine(
            outputDirectory,
            "nuclear_4zd_material_root_binding.txt");
        var json = Path.Combine(
            outputDirectory,
            "nuclear_4zd_material_root_binding.json");

        var report = new StringBuilder();
        report.AppendLine("NUCLEAR STAGE 4Z-D0 - MATERIAL ROOT BINDING");
        report.AppendLine("============================================");
        report.AppendLine($"Corpus materials:            {graphNames.Count:N0}");
        report.AppendLine($"Native Material roots:       {nativeMaterialRoots.Count:N0}");
        report.AppendLine($"Direct identity matches:     {direct:N0}");
        report.AppendLine($"Semantic fingerprint matches:{semantic,10:N0}");
        report.AppendLine($"Unmatched materials:         {unmatched.Count:N0}");
        report.AppendLine($"Ambiguous materials:         {ambiguities.Count:N0}");
        report.AppendLine($"Duplicate root claims:       {duplicateClaims:N0}");
        report.AppendLine($"Calibration shared pairs:    {calibration.Pairs:N0}");
        report.AppendLine($"Calibration correct unique:  {calibration.Matches:N0}");
        report.AppendLine($"Calibration wrong unique:    {calibration.Wrong:N0}");
        report.AppendLine($"Calibration ambiguous/miss:  {calibration.AmbiguousOrMissing:N0}");
        report.AppendLine($"Calibration precision:       {calibration.Agreement:P2}");
        report.AppendLine($"Calibration coverage:        {calibration.Coverage:P2}");
        report.AppendLine($"Fingerprint mode:            {calibration.Mode}");
        report.AppendLine($"Semantic fallback enabled:   {calibration.Enabled}");
        report.AppendLine($"BINDING PASS:                {pass}");
        report.AppendLine();
        report.AppendLine("Bindings:");
        foreach (var row in bindings.OrderBy(x => x.NativeRootIndex))
            report.AppendLine(
                $"  [{row.NativeRootIndex,5}] {row.MatchKind,-20} native={row.NativeName} <= pc={row.CorpusName}");
        if (unmatched.Count != 0)
        {
            report.AppendLine();
            report.AppendLine("Unmatched:");
            foreach (var name in unmatched)
                report.AppendLine("  " + name);
        }
        if (ambiguities.Count != 0)
        {
            report.AppendLine();
            report.AppendLine("Ambiguities:");
            foreach (var row in ambiguities)
                report.AppendLine(
                    $"  {row.CorpusName}: {row.Kind}; roots=[{string.Join(",", row.NativeIndices)}]; native=[{string.Join(" | ", row.NativeNames)}]");
        }

        var result = new T4XenonMaterialRootBindingResult
        {
            GraphRoot = graphRoot,
            BridgeManifest = bridgeManifest,
            NativeIdentityManifest = nativeIdentityManifest,
            CorpusMaterials = graphNames.Count,
            NativeMaterialRoots = nativeMaterialRoots.Count,
            DirectIdentityMatches = direct,
            SemanticMatches = semantic,
            UnmatchedMaterials = unmatched.Count,
            AmbiguousMaterials = ambiguities.Count,
            DuplicateRootClaims = duplicateClaims,
            SharedCalibrationPairs = calibration.Pairs,
            SharedCalibrationMatches = calibration.Matches,
            SharedCalibrationWrong = calibration.Wrong,
            SharedCalibrationAmbiguousOrMissing = calibration.AmbiguousOrMissing,
            SharedCalibrationAgreement = calibration.Agreement,
            SharedCalibrationCoverage = calibration.Coverage,
            SemanticFingerprintMode = calibration.Mode.ToString(),
            SemanticFallbackEnabled = calibration.Enabled,
            BindingPass = pass,
            OwnedBindings = bindings.OrderBy(x => x.NativeRootIndex).ToArray(),
            Unmatched = unmatched.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            Ambiguities = ambiguities,
            TextReportFile = txt,
            JsonManifestFile = json
        };

        await File.WriteAllTextAsync(
            txt,
            report.ToString(),
            new UTF8Encoding(false),
            cancellationToken);
        await File.WriteAllTextAsync(
            json,
            JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        return result;
    }

    private static (
        FingerprintMode Mode,
        int Pairs,
        int Matches,
        int Wrong,
        int AmbiguousOrMissing,
        double Agreement,
        double Coverage,
        bool Enabled)
        ChooseFingerprintMode(
            IReadOnlyDictionary<string, string> pcMaterials,
            IReadOnlyDictionary<string, string> xboxMaterials,
            IReadOnlyDictionary<string, string> techniqueMap)
    {
        var shared = pcMaterials.Keys
            .Intersect(xboxMaterials.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var modes = new[]
        {
            FingerprintMode.Strong,
            FingerprintMode.ImageAgnostic,
            FingerprintMode.RenderStructure,
            FingerprintMode.RenderStructureImageAgnostic,
            FingerprintMode.DependencyGraph,
            FingerprintMode.DependencyShape
        };

        var candidates = new List<(
            FingerprintMode Mode,
            int Matches,
            int Wrong,
            int AmbiguousOrMissing,
            double Precision,
            double Coverage)>();

        foreach (var mode in modes)
        {
            var xboxIndex = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var pair in xboxMaterials)
            {
                var hash = Fingerprint(
                    pair.Value,
                    isPc: false,
                    new Dictionary<string, string>(),
                    mode);
                if (!xboxIndex.TryGetValue(hash, out var bucket))
                {
                    bucket = new List<string>();
                    xboxIndex.Add(hash, bucket);
                }
                bucket.Add(pair.Key);
            }

            var matches = 0;
            var wrong = 0;
            var ambiguousOrMissing = 0;

            foreach (var name in shared)
            {
                var hash = Fingerprint(
                    pcMaterials[name],
                    isPc: true,
                    techniqueMap,
                    mode);

                if (!xboxIndex.TryGetValue(hash, out var bucket) ||
                    bucket.Count != 1)
                {
                    ambiguousOrMissing++;
                    continue;
                }

                if (bucket[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                    matches++;
                else
                    wrong++;
            }

            var resolved = matches + wrong;
            var precision = resolved == 0
                ? 0d
                : (double)matches / resolved;
            var coverage = shared.Length == 0
                ? 0d
                : (double)matches / shared.Length;

            candidates.Add((
                mode,
                matches,
                wrong,
                ambiguousOrMissing,
                precision,
                coverage));
        }

        // Safety priority:
        //  1. zero known wrong unique resolutions,
        //  2. maximum number of correctly proven unique resolutions,
        //  3. strongest projection on ties.
        var strength = modes
            .Select((mode, index) => new { mode, index })
            .ToDictionary(x => x.mode, x => x.index);

        var best = candidates
            .OrderBy(x => x.Wrong)
            .ThenByDescending(x => x.Matches)
            .ThenBy(x => strength[x.Mode])
            .First();

        var enabled =
            shared.Length >= 25 &&
            best.Wrong == 0 &&
            best.Precision >= 0.999999d &&
            best.Matches >= 50 &&
            best.Coverage >= 0.10d;

        return (
            best.Mode,
            shared.Length,
            best.Matches,
            best.Wrong,
            best.AmbiguousOrMissing,
            best.Precision,
            best.Coverage,
            enabled);
    }

    private static Dictionary<string, List<NativeFingerprintCandidate>>
        BuildNativeFingerprintIndex(
            IReadOnlyDictionary<string, string> xboxMaterials,
            IReadOnlyList<T4XenonNativeIdentityEntry> roots,
            FingerprintMode mode)
    {
        var result = new Dictionary<string, List<NativeFingerprintCandidate>>(StringComparer.Ordinal);

        foreach (var pair in xboxMaterials)
        {
            var rootMatches = T4XenonXModelCorpus.FindReferenceCandidates(
                roots,
                "material",
                pair.Key);
            if (rootMatches.Count != 1)
                continue;

            var root = rootMatches[0];
            // Semantic replacement must target an owned/native slot, not an
            // OAT reference-only root.
            if (root.Name.StartsWith(",", StringComparison.Ordinal))
                continue;

            var hash = Fingerprint(
                pair.Value,
                isPc: false,
                new Dictionary<string, string>(),
                mode);
            if (!result.TryGetValue(hash, out var bucket))
            {
                bucket = new List<NativeFingerprintCandidate>();
                result.Add(hash, bucket);
            }
            bucket.Add(new(root, pair.Key));
        }

        return result;
    }

    private static string Fingerprint(
        string jsonFile,
        bool isPc,
        IReadOnlyDictionary<string, string> techniqueMap,
        FingerprintMode mode)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonFile));
        var sb = new StringBuilder();
        Canonicalize(
            doc.RootElement,
            sb,
            propertyName: null,
            isPc,
            techniqueMap,
            mode);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))
            .ToLowerInvariant();
    }

    private static void Canonicalize(
        JsonElement value,
        StringBuilder output,
        string? propertyName,
        bool isPc,
        IReadOnlyDictionary<string, string> techniqueMap,
        FingerprintMode mode,
        int depth = 0)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append('{');
                var firstProperty = true;
                foreach (var property in value.EnumerateObject()
                             .OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    if (ShouldSkipProperty(mode, depth, property.Name))
                        continue;

                    if (!firstProperty)
                        output.Append(',');
                    firstProperty = false;
                    output.Append(JsonSerializer.Serialize(property.Name));
                    output.Append(':');
                    Canonicalize(
                        property.Value,
                        output,
                        property.Name,
                        isPc,
                        techniqueMap,
                        mode,
                        depth + 1);
                }
                output.Append('}');
                break;

            case JsonValueKind.Array:
                output.Append('[');
                var firstArray = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!firstArray)
                        output.Append(',');
                    firstArray = false;
                    Canonicalize(
                        item,
                        output,
                        propertyName,
                        isPc,
                        techniqueMap,
                        mode,
                        depth + 1);
                }
                output.Append(']');
                break;

            case JsonValueKind.String:
            {
                var text = value.GetString() ?? "";
                if (propertyName?.Equals("techniqueSet", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (isPc && techniqueMap.TryGetValue(text, out var mapped))
                        text = mapped;
                    text = NormalizeAssetName(text);
                }
                else if (propertyName?.Equals("image", StringComparison.OrdinalIgnoreCase) == true)
                {
                    text = IsImageAgnostic(mode)
                        ? "<image>"
                        : NormalizeAssetName(text);
                }
                output.Append(JsonSerializer.Serialize(text));
                break;
            }

            case JsonValueKind.Number:
                output.Append(value.GetRawText());
                break;
            case JsonValueKind.True:
                output.Append("true");
                break;
            case JsonValueKind.False:
                output.Append("false");
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                output.Append("null");
                break;
            default:
                output.Append(value.GetRawText());
                break;
        }
    }

    private static bool IsImageAgnostic(FingerprintMode mode) =>
        mode is FingerprintMode.ImageAgnostic
            or FingerprintMode.RenderStructureImageAgnostic
            or FingerprintMode.DependencyShape;

    private static bool ShouldSkipProperty(
        FingerprintMode mode,
        int depth,
        string propertyName)
    {
        // stateBitsEntry indexes the platform TechniqueSet slot layout and the
        // stateBits array is the platform render-state payload itself. They are
        // valid serializer output, but not stable PC<->Xenon identity keys.
        if (mode is FingerprintMode.RenderStructure
                or FingerprintMode.RenderStructureImageAgnostic
                or FingerprintMode.DependencyGraph
                or FingerprintMode.DependencyShape)
        {
            if (depth == 0 &&
                (propertyName.Equals("stateBitsEntry", StringComparison.OrdinalIgnoreCase) ||
                 propertyName.Equals("stateBits", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        // Dependency projections deliberately retain only the semantic graph
        // that should survive platform compilation: TechniqueSet mapping,
        // texture declarations and constants. Root metadata/render routing is
        // omitted and uniqueness is proven by calibration before use.
        if (mode is FingerprintMode.DependencyGraph
                or FingerprintMode.DependencyShape)
        {
            if (depth == 0)
            {
                return propertyName is not "techniqueSet"
                    and not "textures"
                    and not "constants";
            }
        }

        return false;
    }

    private static string NormalizeAssetName(string value)
    {
        var result = value.Trim().Replace('\\', '/');
        while (result.StartsWith(",", StringComparison.Ordinal))
            result = result[1..];
        if (result.EndsWith("_wj", StringComparison.OrdinalIgnoreCase))
            result = result[..^3];
        return result.ToLowerInvariant();
    }

    private static IReadOnlyList<string> LoadGraphNames(string graphRoot)
    {
        var dir = Path.Combine(graphRoot, "xenon_material_graphs");
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException(
                $"Material graph directory missing: {dir}");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var name = doc.RootElement.GetProperty("name").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException($"Material graph has an empty name: {file}");
            if (!names.Add(name))
                throw new InvalidDataException($"Duplicate Material graph identity: {name}");
        }
        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<string, string> LoadMaterialJson(string dumpRoot)
    {
        var materialsRoot = Directory.EnumerateDirectories(
                dumpRoot,
                "materials",
                SearchOption.AllDirectories)
            .OrderBy(x => x.Length)
            .FirstOrDefault()
            ?? throw new DirectoryNotFoundException(
                $"No OAT materials directory under: {dumpRoot}");

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(
                     materialsRoot,
                     "*.json",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(materialsRoot, file)
                .Replace('\\', '/');
            var name = relative[..^5];
            if (!result.TryAdd(name, file))
                throw new InvalidDataException(
                    $"Duplicate OAT material identity '{name}' under {materialsRoot}.");
        }
        return result;
    }

    private static IReadOnlyList<T4XenonNativeIdentityEntry> LoadNativeIdentity(
        string manifest)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        var root = doc.RootElement;
        if (root.GetProperty("format").GetString() !=
                "WaW360.T4Xenon.NativeIdentityManifest.v1" ||
            !root.GetProperty("typeSequenceVerified").GetBoolean())
            throw new InvalidDataException(
                "Native identity manifest is not verified.");

        return root.GetProperty("entries")
            .EnumerateArray()
            .Select(e => new T4XenonNativeIdentityEntry(
                e.GetProperty("Index").GetInt32(),
                e.GetProperty("TypeId").GetInt32(),
                e.GetProperty("TypeName").GetString() ?? "",
                e.GetProperty("OatTypeName").GetString() ?? "",
                e.GetProperty("Name").GetString() ?? ""))
            .OrderBy(x => x.Index)
            .ToArray();
    }

    private sealed record NativeFingerprintCandidate(
        T4XenonNativeIdentityEntry Root,
        string DumpName);
}
