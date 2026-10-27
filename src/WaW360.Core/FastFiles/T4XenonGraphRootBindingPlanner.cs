using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed record T4XenonGraphRootBindingEntry(
    string Kind,
    string Name,
    int NativeRootIndex,
    int NativeTypeId,
    string NativeTypeName);

public sealed class T4XenonGraphRootBindingResult
{
    public required string DumpRoot { get; init; }
    public required string NativeFastFile { get; init; }
    public required int GraphTechniqueSets { get; init; }
    public required int MatchedTechniqueSets { get; init; }
    public required int NativeTechniqueSets { get; init; }
    public required int UnboundNativeTechniqueSets { get; init; }
    public required int GraphPixelShaders { get; init; }
    public required int MatchedPixelShaders { get; init; }
    public required int NativePixelShaders { get; init; }
    public required int AmbiguousBindings { get; init; }
    public required int MissingBindings { get; init; }
    public required bool ExactRootBindingReady { get; init; }
    public required IReadOnlyList<T4XenonGraphRootBindingEntry> TechniqueSetBindings { get; init; }
    public required IReadOnlyList<T4XenonGraphRootBindingEntry> PixelShaderBindings { get; init; }
    public required IReadOnlyList<T4XenonNativeIdentityEntry> UnboundNativeTechniqueSetRoots { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonManifestFile { get; init; }
}

/// <summary>
/// NUCLEAR Stage 4C.
///
/// Proves that every fixed-up map-local TechniqueSet graph and every PixelShader
/// reference resolves to one and only one native XAsset root before pointer allocation
/// begins. The native fastfile contributes identity/order evidence only; no donor payload
/// bytes are copied into converter output.
///
/// This gate matters because MaterialPass.pixelShader is an asset reference, while T4
/// Xenon top-level PixelShader/TechniqueSet writers live in TEMP. OpenAssetTools gives
/// reusable TEMP assets stable references through VIRTUAL insert slots, so allocation
/// must start from exact destination identities rather than guessed TEMP offsets.
/// </summary>
public static class T4XenonGraphRootBindingPlanner
{
    public static async Task<T4XenonGraphRootBindingResult> BuildAsync(
        string dumpRoot,
        string nativeFastFile,
        string outputDirectory,
        int expectedTechniqueSets = 87,
        int expectedPixelShaders = 897,
        string? explicitUnlinkerExe = null,
        CancellationToken cancellationToken = default)
    {
        dumpRoot = Path.GetFullPath(dumpRoot);
        nativeFastFile = Path.GetFullPath(nativeFastFile);
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var graph = T4XenonTechniqueGraphImporter.Load(
            dumpRoot,
            expectedTechniqueSets,
            expectedPixelShaders);
        if (!graph.Verified)
            throw new InvalidDataException(
                "Fixed-up Xenon graph import is not verified; Stage 4C root binding refuses to continue.");

        var nativeIdentityDirectory = Path.Combine(outputDirectory, "native_identity");
        var native = await T4XenonNativeIdentityOracle.RunAsync(
            nativeFastFile,
            nativeIdentityDirectory,
            explicitUnlinkerExe,
            cancellationToken);

        if (!native.TypeSequenceVerified)
            throw new InvalidDataException("Native identity oracle type sequence is not verified.");

        var nativeTechsets = native.Entries
            .Where(IsTechniqueSetType)
            .OrderBy(x => x.Index)
            .ToList();
        var nativePixelShaders = native.Entries
            .Where(IsPixelShaderType)
            .OrderBy(x => x.Index)
            .ToList();

        var graphPixelShaderNames = CollectPixelShaderNames(graph)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var techniqueBindings = new List<T4XenonGraphRootBindingEntry>();
        var pixelBindings = new List<T4XenonGraphRootBindingEntry>();
        var ambiguous = 0;
        var missing = 0;
        var notes = new List<string>();

        var techByName = BuildIdentityIndex(nativeTechsets);
        foreach (var techniqueSet in graph.TechniqueSetsByName.Values
                     .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Bind(
                "TechniqueSet",
                techniqueSet.Name,
                techByName,
                techniqueBindings,
                ref ambiguous,
                ref missing,
                notes);
        }

        var pixelByName = BuildIdentityIndex(nativePixelShaders);
        foreach (var pixelShaderName in graphPixelShaderNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Bind(
                "PixelShader",
                pixelShaderName,
                pixelByName,
                pixelBindings,
                ref ambiguous,
                ref missing,
                notes);
        }

        var boundTechRootIndices = techniqueBindings
            .Select(x => x.NativeRootIndex)
            .ToHashSet();
        var unboundNativeTechsets = nativeTechsets
            .Where(x => !boundTechRootIndices.Contains(x.Index))
            .OrderBy(x => x.Index)
            .ToList();

        var duplicateBoundTechRoots = techniqueBindings
            .GroupBy(x => x.NativeRootIndex)
            .Sum(g => Math.Max(0, g.Count() - 1));
        var duplicateBoundPixelRoots = pixelBindings
            .GroupBy(x => x.NativeRootIndex)
            .Sum(g => Math.Max(0, g.Count() - 1));
        if (duplicateBoundTechRoots != 0 || duplicateBoundPixelRoots != 0)
        {
            ambiguous += duplicateBoundTechRoots + duplicateBoundPixelRoots;
            notes.Add(
                $"Duplicate root reuse detected after binding: TechniqueSet={duplicateBoundTechRoots:N0}; " +
                $"PixelShader={duplicateBoundPixelRoots:N0}.");
        }

        var pixelCoverageExact =
            graphPixelShaderNames.Count == expectedPixelShaders &&
            nativePixelShaders.Count == expectedPixelShaders &&
            pixelBindings.Count == expectedPixelShaders;

        var techniqueCoverageExact =
            graph.TechniqueSets == expectedTechniqueSets &&
            techniqueBindings.Count == expectedTechniqueSets;

        var ready =
            graph.Verified &&
            native.TypeSequenceVerified &&
            ambiguous == 0 &&
            missing == 0 &&
            techniqueCoverageExact &&
            pixelCoverageExact;

        notes.Add(
            $"OAT identity-only binding: {techniqueBindings.Count:N0}/{graph.TechniqueSets:N0} map-local TechniqueSets " +
            $"and {pixelBindings.Count:N0}/{graphPixelShaderNames.Count:N0} PixelShaders resolve to exact native XAsset roots.");
        notes.Add(
            $"{unboundNativeTechsets.Count:N0} native TechniqueSet root(s) are intentionally outside the map-local graph dump " +
            "and remain resident/common/oracle-only shell territory for the serializer bridge.");
        notes.Add(
            "No native payload bytes are copied. Stage 4C consumes only resolved native root identity/order evidence.");
        notes.Add(
            ready
                ? "ROOT BINDING CLOSURE: PASS. The next serializer stage may allocate OAT-style VIRTUAL insert slots and TEMP/PHYSICAL graph bodies against these exact roots."
                : "ROOT BINDING CLOSURE: BLOCKED. Do not emit renderer pointers until every binding is unique and complete.");

        var txtPath = Path.Combine(outputDirectory, "nuclear_xenon_root_binding.txt");
        var jsonPath = Path.Combine(outputDirectory, "nuclear_xenon_root_binding.json");

        var report = new StringBuilder();
        report.AppendLine("NUCLEAR STAGE 4C - XENON GRAPH -> NATIVE ROOT BINDING");
        report.AppendLine("======================================================");
        report.AppendLine($"Graph root:             {dumpRoot}");
        report.AppendLine($"Native oracle:          {nativeFastFile}");
        report.AppendLine($"Native XAsset roots:    {native.Entries.Count:N0}");
        report.AppendLine();
        report.AppendLine($"Graph TechniqueSets:    {graph.TechniqueSets:N0}");
        report.AppendLine($"Matched TechniqueSets:  {techniqueBindings.Count:N0}");
        report.AppendLine($"Native TechniqueSets:   {nativeTechsets.Count:N0}");
        report.AppendLine($"Unbound native TS:      {unboundNativeTechsets.Count:N0}");
        report.AppendLine();
        report.AppendLine($"Graph PixelShaders:     {graphPixelShaderNames.Count:N0}");
        report.AppendLine($"Matched PixelShaders:   {pixelBindings.Count:N0}");
        report.AppendLine($"Native PixelShaders:    {nativePixelShaders.Count:N0}");
        report.AppendLine();
        report.AppendLine($"Ambiguous bindings:     {ambiguous:N0}");
        report.AppendLine($"Missing bindings:       {missing:N0}");
        report.AppendLine($"Exact root binding:     {(ready ? "PASS" : "FAIL")}");
        report.AppendLine();
        report.AppendLine("TechniqueSet root bindings:");
        foreach (var row in techniqueBindings.OrderBy(x => x.NativeRootIndex))
            report.AppendLine($"  [{row.NativeRootIndex,5}] {row.Name}");
        report.AppendLine();
        report.AppendLine("PixelShader root bindings:");
        foreach (var row in pixelBindings.OrderBy(x => x.NativeRootIndex))
            report.AppendLine($"  [{row.NativeRootIndex,5}] {row.Name}");
        report.AppendLine();
        report.AppendLine("Unbound native TechniqueSet roots:");
        foreach (var row in unboundNativeTechsets)
            report.AppendLine($"  [{row.Index,5}] {row.Name}");
        report.AppendLine();
        foreach (var note in notes)
            report.AppendLine(note);

        await File.WriteAllTextAsync(
            txtPath,
            report.ToString(),
            new UTF8Encoding(false),
            cancellationToken);

        var manifest = new
        {
            format = "WaW360.T4Xenon.GraphRootBinding.v1",
            dumpRoot,
            nativeFastFile,
            graphTechniqueSets = graph.TechniqueSets,
            matchedTechniqueSets = techniqueBindings.Count,
            nativeTechniqueSets = nativeTechsets.Count,
            unboundNativeTechniqueSets = unboundNativeTechsets.Count,
            graphPixelShaders = graphPixelShaderNames.Count,
            matchedPixelShaders = pixelBindings.Count,
            nativePixelShaders = nativePixelShaders.Count,
            ambiguousBindings = ambiguous,
            missingBindings = missing,
            exactRootBindingReady = ready,
            techniqueSetBindings = techniqueBindings.OrderBy(x => x.NativeRootIndex),
            pixelShaderBindings = pixelBindings.OrderBy(x => x.NativeRootIndex),
            unboundNativeTechniqueSetRoots = unboundNativeTechsets.Select(x => new
            {
                x.Index,
                x.TypeId,
                x.TypeName,
                x.OatTypeName,
                x.Name
            }),
            notes
        };

        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        return new T4XenonGraphRootBindingResult
        {
            DumpRoot = dumpRoot,
            NativeFastFile = nativeFastFile,
            GraphTechniqueSets = graph.TechniqueSets,
            MatchedTechniqueSets = techniqueBindings.Count,
            NativeTechniqueSets = nativeTechsets.Count,
            UnboundNativeTechniqueSets = unboundNativeTechsets.Count,
            GraphPixelShaders = graphPixelShaderNames.Count,
            MatchedPixelShaders = pixelBindings.Count,
            NativePixelShaders = nativePixelShaders.Count,
            AmbiguousBindings = ambiguous,
            MissingBindings = missing,
            ExactRootBindingReady = ready,
            TechniqueSetBindings = techniqueBindings,
            PixelShaderBindings = pixelBindings,
            UnboundNativeTechniqueSetRoots = unboundNativeTechsets,
            Notes = notes,
            TextReportFile = txtPath,
            JsonManifestFile = jsonPath
        };
    }

    private static Dictionary<string, List<T4XenonNativeIdentityEntry>> BuildIdentityIndex(
        IEnumerable<T4XenonNativeIdentityEntry> rows)
    {
        var result = new Dictionary<string, List<T4XenonNativeIdentityEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.Name, out var bucket))
            {
                bucket = new List<T4XenonNativeIdentityEntry>();
                result.Add(row.Name, bucket);
            }
            bucket.Add(row);
        }
        return result;
    }

    private static void Bind(
        string kind,
        string name,
        IReadOnlyDictionary<string, List<T4XenonNativeIdentityEntry>> nativeByName,
        ICollection<T4XenonGraphRootBindingEntry> output,
        ref int ambiguous,
        ref int missing,
        ICollection<string> notes)
    {
        if (!nativeByName.TryGetValue(name, out var matches) || matches.Count == 0)
        {
            missing++;
            notes.Add($"MISSING {kind} native root: '{name}'.");
            return;
        }

        if (matches.Count != 1)
        {
            ambiguous++;
            notes.Add(
                $"AMBIGUOUS {kind} native root: '{name}' -> " +
                string.Join(", ", matches.Select(x => $"#{x.Index}")));
            return;
        }

        var row = matches[0];
        output.Add(new T4XenonGraphRootBindingEntry(
            kind,
            name,
            row.Index,
            row.TypeId,
            row.TypeName));
    }

    private static HashSet<string> CollectPixelShaderNames(T4XenonGraphImportResult graph)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var techniqueSet in graph.TechniqueSetsByName.Values)
        {
            foreach (var slot in techniqueSet.Slots)
            {
                var technique = slot.Technique;
                if (technique is null)
                    continue;

                foreach (var pass in technique.Passes)
                {
                    var name = pass.PixelShader?.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
            }
        }
        return names;
    }

    private static bool IsTechniqueSetType(T4XenonNativeIdentityEntry row) =>
        row.OatTypeName.Equals("techniqueset", StringComparison.OrdinalIgnoreCase) ||
        row.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase) ||
        row.TypeName.Equals("techniqueset", StringComparison.OrdinalIgnoreCase);

    private static bool IsPixelShaderType(T4XenonNativeIdentityEntry row) =>
        row.OatTypeName.Equals("pixelshader", StringComparison.OrdinalIgnoreCase) ||
        row.TypeName.Equals("pixelshader", StringComparison.OrdinalIgnoreCase);
}
