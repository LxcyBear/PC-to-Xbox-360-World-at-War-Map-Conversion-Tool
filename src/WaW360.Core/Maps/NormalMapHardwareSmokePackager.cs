using System.Text;
using System.Text.Json;

namespace WaW360.Core.Maps;

public sealed class NormalMapHardwareSmokePackageResult
{
    public required string MapName { get; init; }
    public required string PackageFolder { get; init; }
    public required int FastFilesCopied { get; init; }
    public required int MetadataFilesCopied { get; init; }
    public required int SourceIwdCount { get; init; }
    public required bool CompleteSmokeSet { get; init; }
    public required bool DropInSmokeTestReady { get; init; }
    public required IReadOnlyList<string> CopiedFiles { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Packages a verified 10r smoke set into one explicit hardware-test folder.
/// This stage never promotes smoke output to strict fidelity; it only removes deployment ambiguity.
/// </summary>
public static class NormalMapHardwareSmokePackager
{
    private static readonly string[] SafeMetadataNames =
    [
        "mod.arena",
        "map.json",
        "preview.dds",
        "preview.png",
        "preview.jpg",
        "preview.jpeg"
    ];

    public static async Task<NormalMapHardwareSmokePackageResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var smokeJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10r_world_integrated_smoke_emitter",
            $"{map.Name}_stage5_alpha10r_world_integrated_smoke_emitter.json");
        if (!File.Exists(smokeJson))
            return await WriteNoOpAsync(map, outputRoot, "No Stage 5v10r report exists; hardware packaging is a no-op for this map.", cancellationToken);

        var smoke = JsonSerializer.Deserialize<NormalMapWorldRootIntegratedSmokeEmitterResult>(
            await File.ReadAllTextAsync(smokeJson, cancellationToken),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Could not read Stage 5v10r smoke-emitter report.");

        if (!smoke.Verified || !smoke.CompleteSmokeSet)
            return await WriteNoOpAsync(map, outputRoot, "Stage 5v10r did not produce a complete verified smoke set; no hardware-test package was created.", cancellationToken);

        var package = Path.Combine(outputRoot, map.Name, "hardware_test_ready");
        if (Directory.Exists(package)) Directory.Delete(package, recursive: true);
        Directory.CreateDirectory(package);

        var copied = new List<string>();
        foreach (var ff in smoke.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ff.SmokeHardwareCandidate || string.IsNullOrWhiteSpace(ff.HardwareFastFile) || !File.Exists(ff.HardwareFastFile))
                throw new InvalidDataException($"Smoke fastfile '{ff.Role}' is missing from the verified candidate set.");
            var dst = Path.Combine(package, Path.GetFileName(ff.HardwareFastFile));
            File.Copy(ff.HardwareFastFile, dst, overwrite: true);
            copied.Add(Path.GetFileName(dst));
        }

        var metadataCopied = 0;
        foreach (var folder in map.SourceFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var name in SafeMetadataNames)
            {
                var src = Path.Combine(folder, name);
                if (!File.Exists(src)) continue;
                var dst = Path.Combine(package, name);
                if (File.Exists(dst)) continue;
                File.Copy(src, dst, overwrite: false);
                copied.Add(name);
                metadataCopied++;
            }
        }

        var warnings = new List<string>();
        if (map.IwdCount != 0)
            warnings.Add($"Source map has {map.IwdCount:N0} IWD file(s). They are intentionally NOT copied because PC IWD image/audio payloads are not assumed Xbox-native. Use this package only as a loader/parser smoke test until IWD conversion is implemented.");
        if (!copied.Any(x => x.Equals("mod.arena", StringComparison.OrdinalIgnoreCase)) &&
            !copied.Any(x => x.Equals("map.json", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("No mod.arena/map.json metadata was found. If the dashboard/custom-map loader requires listing metadata, keep the original map's metadata files beside these generated FFs.");

        var dropIn = smoke.CompleteSmokeSet && smoke.Verified && map.IwdCount == 0;
        var readme = Path.Combine(package, "TEST_THIS_FOLDER.txt");
        await File.WriteAllTextAsync(readme, RenderReadme(map, smoke, copied, dropIn, warnings), Encoding.UTF8, cancellationToken);
        copied.Add(Path.GetFileName(readme));

        var reportDir = Path.Combine(outputRoot, map.Name, "linker_v1_hardware_package");
        Directory.CreateDirectory(reportDir);
        var txt = Path.Combine(reportDir, $"{map.Name}_hardware_smoke_package.txt");
        var json = Path.Combine(reportDir, $"{map.Name}_hardware_smoke_package.json");
        var result = new NormalMapHardwareSmokePackageResult
        {
            MapName = map.Name,
            PackageFolder = package,
            FastFilesCopied = smoke.FastFiles.Count,
            MetadataFilesCopied = metadataCopied,
            SourceIwdCount = map.IwdCount,
            CompleteSmokeSet = smoke.CompleteSmokeSet,
            DropInSmokeTestReady = dropIn,
            CopiedFiles = copied,
            Warnings = warnings,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }


    private static async Task<NormalMapHardwareSmokePackageResult> WriteNoOpAsync(
        MapCandidate map,
        string outputRoot,
        string warning,
        CancellationToken cancellationToken)
    {
        var reportDir = Path.Combine(outputRoot, map.Name, "linker_v1_hardware_package");
        Directory.CreateDirectory(reportDir);
        var txt = Path.Combine(reportDir, $"{map.Name}_hardware_smoke_package.txt");
        var json = Path.Combine(reportDir, $"{map.Name}_hardware_smoke_package.json");
        var result = new NormalMapHardwareSmokePackageResult
        {
            MapName = map.Name,
            PackageFolder = Path.Combine(outputRoot, map.Name, "hardware_test_ready"),
            FastFilesCopied = 0,
            MetadataFilesCopied = 0,
            SourceIwdCount = map.IwdCount,
            CompleteSmokeSet = false,
            DropInSmokeTestReady = false,
            CopiedFiles = Array.Empty<string>(),
            Warnings = new[] { warning },
            TextReportFile = txt,
            JsonReportFile = json
        };
        await File.WriteAllTextAsync(txt, Render(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static string RenderReadme(
        MapCandidate map,
        NormalMapWorldRootIntegratedSmokeEmitterResult smoke,
        IReadOnlyList<string> files,
        bool dropIn,
        IReadOnlyList<string> warnings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — HARDWARE SMOKE TEST PACKAGE");
        sb.AppendLine(new string('=', 58));
        sb.AppendLine($"Map: {map.Name}");
        sb.AppendLine($"Complete 10r smoke set: {smoke.CompleteSmokeSet}");
        sb.AppendLine($"Smoke emission verified: {smoke.Verified}");
        sb.AppendLine($"Drop-in smoke-test ready: {dropIn}");
        sb.AppendLine($"Strict fidelity ready: {smoke.StrictFidelityReady}");
        sb.AppendLine();
        sb.AppendLine("FILES");
        foreach (var file in files) sb.AppendLine("  " + file);
        sb.AppendLine();
        sb.AppendLine("TEST PROCEDURE");
        sb.AppendLine("  1. Make a separate Xbox-side test copy of this map; do not overwrite your only source/working copy.");
        sb.AppendLine("  2. Use the generated .ff files from this folder in that test copy.");
        sb.AppendLine("  3. Keep any original listing/preview metadata the console loader needs if it was not copied here.");
        sb.AppendLine("  4. Launch the map and record the furthest point reached: listing, load start, load screen, spawn/gameplay, freeze/fatal/dashboard.");
        sb.AppendLine("  5. Save xbWatson/RGH output on any failure.");
        sb.AppendLine();
        sb.AppendLine("EXPECTED LIMITATIONS");
        sb.AppendLine("  This is still the reduced-content 10r smoke set. Silent audio, null shader graphs, inert materials, reduced world data and zero-owned tracked shells remain intentional.");
        sb.AppendLine("  The purpose is to prove how far the real Xbox loader accepts the reconstructed whole-map FF set before strict-fidelity serializers replace those smoke payloads.");
        foreach (var warning in warnings) sb.AppendLine("WARNING: " + warning);
        return sb.ToString();
    }

    private static string Render(NormalMapHardwareSmokePackageResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — hardware smoke packaging gate");
        sb.AppendLine(new string('=', 56));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Package folder: {r.PackageFolder}");
        sb.AppendLine($"FFs copied: {r.FastFilesCopied:N0}");
        sb.AppendLine($"Metadata copied: {r.MetadataFilesCopied:N0}");
        sb.AppendLine($"Source IWDs: {r.SourceIwdCount:N0}");
        sb.AppendLine($"Complete smoke set: {r.CompleteSmokeSet}");
        sb.AppendLine($"DROP_IN_SMOKE_TEST_READY: {r.DropInSmokeTestReady}");
        sb.AppendLine("Files: " + string.Join(", ", r.CopiedFiles));
        foreach (var warning in r.Warnings) sb.AppendLine("WARNING: " + warning);
        return sb.ToString();
    }
}
