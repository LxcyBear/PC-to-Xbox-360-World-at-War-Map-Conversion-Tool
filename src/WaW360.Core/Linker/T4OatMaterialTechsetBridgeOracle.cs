using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Linker;

public sealed record T4MaterialTechniqueObservation(
    string MaterialName,
    string TechniqueSet);

public sealed record T4MaterialTechniqueBridgeEntry(
    string MaterialName,
    string PcTechniqueSet,
    string XboxTechniqueSet);

public sealed record T4TechniqueSetPlatformMapping(
    string PcTechniqueSet,
    string XboxTechniqueSet,
    int SupportingMaterials,
    IReadOnlyList<string> ExampleMaterials);

public sealed record T4TechniqueSetPlatformConflict(
    string PcTechniqueSet,
    IReadOnlyList<string> XboxTechniqueSets,
    IReadOnlyList<string> ExampleMaterials);

public sealed class T4OatMaterialTechsetBridgeResult
{
    public required string PcFastFile { get; init; }
    public required string XboxFastFile { get; init; }
    public required string UnlinkerExe { get; init; }
    public required int PcMaterialsDumped { get; init; }
    public required int XboxMaterialsDumped { get; init; }
    public required int SharedMaterials { get; init; }
    public required int SharedMaterialsWithTechniqueSets { get; init; }
    public required int PcOnlyMaterials { get; init; }
    public required int XboxOnlyMaterials { get; init; }
    public required int PcTechniqueSetsObserved { get; init; }
    public required int XboxTechniqueSetsObserved { get; init; }
    public required int DeterministicTechniqueMappings { get; init; }
    public required int AmbiguousTechniqueMappings { get; init; }
    public required int DistinctMappedXboxTechniqueSets { get; init; }
    public required IReadOnlyList<T4MaterialTechniqueObservation> PcMaterialTechniques { get; init; }
    public required IReadOnlyList<T4MaterialTechniqueObservation> XboxMaterialTechniques { get; init; }
    public required IReadOnlyList<T4MaterialTechniqueBridgeEntry> MaterialPairs { get; init; }
    public required IReadOnlyList<T4TechniqueSetPlatformMapping> Mappings { get; init; }
    public required IReadOnlyList<T4TechniqueSetPlatformConflict> Conflicts { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required bool Verified { get; init; }
    public required string PcDumpDirectory { get; init; }
    public required string XboxDumpDirectory { get; init; }
    public required string PcOatLogFile { get; init; }
    public required string XboxOatLogFile { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Cross-platform TechniqueSet bridge derived from MATERIAL identity.
///
/// This oracle deliberately avoids trying to translate a PC shader graph into Xenon.
/// Instead, OAT semantically loads both zones and dumps their material descriptions.
/// For each material present on both platforms:
///
///     PC material -> PC TechniqueSet
///     Xbox material -> Xbox TechniqueSet
///
/// The shared material identity then proves a platform mapping from the PC TechniqueSet
/// to the native Xenon TechniqueSet. Multiple materials can vote for the same mapping.
/// Conflicting targets remain explicit and are never guessed.
///
/// This is development/oracle evidence. It is especially powerful when a known-working
/// native build of the same map is available, because it exposes the platform transform
/// the production converter must learn without copying any donor payload bytes.
/// </summary>
public static class T4OatMaterialTechsetBridgeOracle
{
    public static async Task<T4OatMaterialTechsetBridgeResult> CompareAsync(
        string pcFastFile,
        string xboxFastFile,
        string outputDirectory,
        string? explicitUnlinkerExe = null,
        CancellationToken cancellationToken = default)
    {
        pcFastFile = Path.GetFullPath(pcFastFile);
        xboxFastFile = Path.GetFullPath(xboxFastFile);
        outputDirectory = Path.GetFullPath(outputDirectory);

        if (!File.Exists(pcFastFile))
            throw new FileNotFoundException("PC fastfile not found.", pcFastFile);
        if (!File.Exists(xboxFastFile))
            throw new FileNotFoundException("Xbox fastfile not found.", xboxFastFile);

        var pc = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, cancellationToken);
        var xbox = await FastFileAnalysisCache.GetZoneAsync(xboxFastFile, cancellationToken);
        if (pc.Platform != WaWPlatform.PC || pc.Document.Endianness != Endian.Little)
            throw new InvalidDataException(
                $"Material bridge PC input must be PC/little-endian; parsed {pc.Platform}/{pc.Document.Endianness}.");
        if (xbox.Platform != WaWPlatform.Xbox360 || xbox.Document.Endianness != Endian.Big)
            throw new InvalidDataException(
                $"Material bridge Xbox input must be Xbox360/big-endian; parsed {xbox.Platform}/{xbox.Document.Endianness}.");

        Directory.CreateDirectory(outputDirectory);
        var dumpRoot = Path.Combine(outputDirectory, "oat_material_bridge");
        var pcDump = Path.Combine(dumpRoot, "pc");
        var xboxDump = Path.Combine(dumpRoot, "xbox");
        ResetDirectory(pcDump);
        ResetDirectory(xboxDump);

        var unlinker = ResolveUnlinker(explicitUnlinkerExe);
        var pcLog = Path.Combine(outputDirectory, "pc_material_unlinker.log");
        var xboxLog = Path.Combine(outputDirectory, "xbox_material_unlinker.log");

        await DumpMaterialsAsync(unlinker, pcFastFile, pcDump, pcLog, cancellationToken);
        await DumpMaterialsAsync(unlinker, xboxFastFile, xboxDump, xboxLog, cancellationToken);

        var pcMaterials = ReadMaterialDump(pcDump);
        var xboxMaterials = ReadMaterialDump(xboxDump);
        if (pcMaterials.Count == 0)
            throw new InvalidDataException(
                $"OAT material dump produced zero PC materials. See: {pcLog}");
        if (xboxMaterials.Count == 0)
            throw new InvalidDataException(
                $"OAT material dump produced zero Xbox materials. See: {xboxLog}");

        var sharedNames = pcMaterials.Keys
            .Intersect(xboxMaterials.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pairs = new List<T4MaterialTechniqueBridgeEntry>();
        foreach (var name in sharedNames)
        {
            var pcTech = pcMaterials[name];
            var xboxTech = xboxMaterials[name];
            if (string.IsNullOrWhiteSpace(pcTech) || string.IsNullOrWhiteSpace(xboxTech))
                continue;

            pairs.Add(new T4MaterialTechniqueBridgeEntry(name, pcTech, xboxTech));
        }

        var mappings = new List<T4TechniqueSetPlatformMapping>();
        var conflicts = new List<T4TechniqueSetPlatformConflict>();
        foreach (var group in pairs
                     .GroupBy(x => x.PcTechniqueSet, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var targets = group
                .GroupBy(x => x.XboxTechniqueSet, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Name = g.Key,
                    Count = g.Count(),
                    Materials = g.Select(x => x.MaterialName)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .Take(12)
                        .ToList()
                })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (targets.Count == 1)
            {
                var target = targets[0];
                mappings.Add(new T4TechniqueSetPlatformMapping(
                    group.Key,
                    target.Name,
                    target.Count,
                    target.Materials));
            }
            else
            {
                conflicts.Add(new T4TechniqueSetPlatformConflict(
                    group.Key,
                    targets.Select(x => $"{x.Name} ({x.Count:N0})").ToList(),
                    group.Select(x => x.MaterialName)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .Take(16)
                        .ToList()));
            }
        }

        var pcOnly = pcMaterials.Keys.Except(xboxMaterials.Keys, StringComparer.OrdinalIgnoreCase).Count();
        var xboxOnly = xboxMaterials.Keys.Except(pcMaterials.Keys, StringComparer.OrdinalIgnoreCase).Count();
        var pcTechsets = pcMaterials.Values.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var xboxTechsets = xboxMaterials.Values.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var mappedXbox = mappings.Select(x => x.XboxTechniqueSet)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();

        var pcMaterialTechniques = pcMaterials
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new T4MaterialTechniqueObservation(x.Key, x.Value))
            .ToList();
        var xboxMaterialTechniques = xboxMaterials
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new T4MaterialTechniqueObservation(x.Key, x.Value))
            .ToList();

        var notes = new List<string>
        {
            "NUCLEAR BRIDGE: shared material identity is used as the cross-platform semantic key; PC shader/pass bytecode is never copied to Xbox.",
            "A deterministic PC TechniqueSet -> Xbox TechniqueSet row is accepted only when every shared material observation for that PC TechniqueSet agrees on one Xbox target.",
            "Conflicting PC TechniqueSet mappings remain explicit and fail closed; they are evidence for context-sensitive material/platform rules.",
            $"Compression signal: {mappings.Count:N0} deterministic PC TechniqueSet identity/identities currently collapse onto {mappedXbox:N0} distinct Xbox TechniqueSet identity/identities through shared materials."
        };

        var verified = sharedNames.Count != 0 && pairs.Count != 0 && mappings.Count != 0;
        if (!verified)
            notes.Add("Bridge did not recover enough shared material evidence to establish any deterministic platform mapping.");

        var txt = Path.Combine(outputDirectory, "material_techset_bridge.txt");
        var json = Path.Combine(outputDirectory, "material_techset_bridge.json");

        var result = new T4OatMaterialTechsetBridgeResult
        {
            PcFastFile = pcFastFile,
            XboxFastFile = xboxFastFile,
            UnlinkerExe = unlinker,
            PcMaterialsDumped = pcMaterials.Count,
            XboxMaterialsDumped = xboxMaterials.Count,
            SharedMaterials = sharedNames.Count,
            SharedMaterialsWithTechniqueSets = pairs.Count,
            PcOnlyMaterials = pcOnly,
            XboxOnlyMaterials = xboxOnly,
            PcTechniqueSetsObserved = pcTechsets,
            XboxTechniqueSetsObserved = xboxTechsets,
            DeterministicTechniqueMappings = mappings.Count,
            AmbiguousTechniqueMappings = conflicts.Count,
            DistinctMappedXboxTechniqueSets = mappedXbox,
            PcMaterialTechniques = pcMaterialTechniques,
            XboxMaterialTechniques = xboxMaterialTechniques,
            MaterialPairs = pairs,
            Mappings = mappings,
            Conflicts = conflicts,
            Notes = notes,
            Verified = verified,
            PcDumpDirectory = pcDump,
            XboxDumpDirectory = xboxDump,
            PcOatLogFile = pcLog,
            XboxOatLogFile = xboxLog,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(
            json,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        return result;
    }

    private static Dictionary<string, string> ReadMaterialDump(string dumpRoot)
    {
        var materialsRoot = Directory.EnumerateDirectories(dumpRoot, "materials", SearchOption.AllDirectories)
            .OrderBy(x => x.Length)
            .FirstOrDefault();
        if (materialsRoot is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(materialsRoot, "*.json", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(materialsRoot, file)
                .Replace('\\', '/');
            if (!relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            var materialName = relative[..^5];
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var techset = json.RootElement.TryGetProperty("techniqueSet", out var property) &&
                          property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;

            if (!result.TryAdd(materialName, techset))
                throw new InvalidDataException(
                    $"OAT material dump contains duplicate material identity '{materialName}' under {materialsRoot}.");
        }

        return result;
    }

    private static async Task DumpMaterialsAsync(
        string unlinkerExe,
        string fastFile,
        string dumpDirectory,
        string logFile,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = unlinkerExe,
            WorkingDirectory = Path.GetDirectoryName(fastFile) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("--no-color");
        psi.ArgumentList.Add("--include-assets");
        psi.ArgumentList.Add("material");
        psi.ArgumentList.Add("--output-folder");
        psi.ArgumentList.Add(dumpDirectory);
        psi.ArgumentList.Add(fastFile);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start OAT Unlinker: {unlinkerExe}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var combined = string.IsNullOrEmpty(stderr)
            ? stdout
            : string.IsNullOrEmpty(stdout) ? stderr : stdout + Environment.NewLine + stderr;

        await File.WriteAllTextAsync(logFile, combined, new UTF8Encoding(false), cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidDataException(
                $"OAT material dump failed for {Path.GetFileName(fastFile)} with exit code {process.ExitCode}. See: {logFile}");
    }

    private static string Render(T4OatMaterialTechsetBridgeResult r)
    {
        var sb = new StringBuilder(128 * 1024);
        sb.AppendLine("WaW360Converter — NUCLEAR PC -> XENON MATERIAL / TECHNIQUESET BRIDGE");
        sb.AppendLine(new string('=', 92));
        sb.AppendLine($"PC:   {r.PcFastFile}");
        sb.AppendLine($"Xbox: {r.XboxFastFile}");
        sb.AppendLine($"OAT:  {r.UnlinkerExe}");
        sb.AppendLine();
        sb.AppendLine($"materials: PC={r.PcMaterialsDumped:N0}; Xbox={r.XboxMaterialsDumped:N0}; shared={r.SharedMaterials:N0}; withTechniqueSets={r.SharedMaterialsWithTechniqueSets:N0}; pcOnly={r.PcOnlyMaterials:N0}; xboxOnly={r.XboxOnlyMaterials:N0}");
        sb.AppendLine($"techsets observed: PC={r.PcTechniqueSetsObserved:N0}; Xbox={r.XboxTechniqueSetsObserved:N0}; deterministicMappings={r.DeterministicTechniqueMappings:N0}; ambiguousMappings={r.AmbiguousTechniqueMappings:N0}; distinctMappedXbox={r.DistinctMappedXboxTechniqueSets:N0}");
        sb.AppendLine($"verified={r.Verified}");
        sb.AppendLine();

        sb.AppendLine("DETERMINISTIC PC -> XBOX TECHNIQUESET MAPPINGS");
        foreach (var m in r.Mappings)
        {
            sb.AppendLine($"  {m.PcTechniqueSet} -> {m.XboxTechniqueSet} | materials={m.SupportingMaterials:N0}");
            foreach (var material in m.ExampleMaterials.Take(4))
                sb.AppendLine($"      {material}");
        }
        if (r.Mappings.Count == 0)
            sb.AppendLine("  <none>");
        sb.AppendLine();

        sb.AppendLine("AMBIGUOUS / CONTEXT-SENSITIVE PC TECHNIQUESETS");
        foreach (var c in r.Conflicts)
        {
            sb.AppendLine($"  {c.PcTechniqueSet} -> {string.Join(", ", c.XboxTechniqueSets)}");
            foreach (var material in c.ExampleMaterials.Take(6))
                sb.AppendLine($"      {material}");
        }
        if (r.Conflicts.Count == 0)
            sb.AppendLine("  <none>");
        sb.AppendLine();

        foreach (var note in r.Notes)
            sb.AppendLine("NOTE: " + note);

        return sb.ToString();
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
    }

    private static string ResolveUnlinker(string? explicitPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitPath))
            candidates.Add(explicitPath);

        var env = Environment.GetEnvironmentVariable("WAW360_OAT_UNLINKER");
        if (!string.IsNullOrWhiteSpace(env))
            candidates.Add(env);

        foreach (var root in CandidateRoots())
        {
            candidates.Add(Path.Combine(root, ".deps", "OpenAssetTools-xenon-prebuilt", "Release_x86", "Unlinker.exe"));
            candidates.Add(Path.Combine(root, ".deps", "OpenAssetTools-xenon", "build", "bin", "Release_x86", "Unlinker.exe"));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
            }
        }

        throw new FileNotFoundException("Pinned OAT Xenon Unlinker.exe was not found.");
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
            {
                if (seen.Add(dir.FullName))
                    yield return dir.FullName;
            }
        }
    }
}
