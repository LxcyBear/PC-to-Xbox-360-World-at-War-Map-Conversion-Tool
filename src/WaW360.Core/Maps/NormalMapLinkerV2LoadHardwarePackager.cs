using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.Maps;

public sealed class NormalMapLinkerV2LoadHardwarePackageResult
{
    public required string MapName { get; init; }
    public required bool Ready { get; init; }
    public required string? PackageDirectory { get; init; }
    public required string? FastFile { get; init; }
    public required string? Sha256 { get; init; }
    public required bool CombinedMainLoadReady { get; init; }
    public required string? CombinedPackageDirectory { get; init; }
    public required string? CombinedMainFastFile { get; init; }
    public required string? CombinedLoadFastFile { get; init; }
    public required bool TrackedShellPairReady { get; init; }
    public required string? TrackedShellPairDirectory { get; init; }
    public required string? TrackedShellMainFastFile { get; init; }
    public required string? TrackedShellLoadFastFile { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Creates a deliberately load-only native A/B drop folder. It never copies or fabricates a main FF.
/// The user combines this one generated load FF with an otherwise known-good Xbox package, which
/// isolates the V2 load linker from all still-pending main-graph work.
/// </summary>
public static class NormalMapLinkerV2LoadHardwarePackager
{
    public static async Task<NormalMapLinkerV2LoadHardwarePackageResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2LoadEmitResult emitter,
        CancellationToken cancellationToken = default)
    {
        var reportDir = Path.Combine(outputRoot, map.Name, "linker_v2_load_hardware_package");
        Directory.CreateDirectory(reportDir);
        var txt = Path.Combine(reportDir, $"{map.Name}_linker_v2_load_hardware_package.txt");
        var json = Path.Combine(reportDir, $"{map.Name}_linker_v2_load_hardware_package.json");
        var notes = new List<string>();

        if (!emitter.HardwareReady || string.IsNullOrWhiteSpace(emitter.OutputFastFile) || !File.Exists(emitter.OutputFastFile))
        {
            notes.Add(emitter.SelectedSourceFastFile is null
                ? "No load FF belongs in this map's package; no load-only hardware drop was created."
                : "Selected load output did not pass every hardware-candidate gate; fail-closed package creation skipped it.");
            var none = new NormalMapLinkerV2LoadHardwarePackageResult
            {
                MapName = map.Name,
                Ready = false,
                PackageDirectory = null,
                FastFile = null,
                Sha256 = null,
                CombinedMainLoadReady = false,
                CombinedPackageDirectory = null,
                CombinedMainFastFile = null,
                CombinedLoadFastFile = null,
                TrackedShellPairReady = false,
                TrackedShellPairDirectory = null,
                TrackedShellMainFastFile = null,
                TrackedShellLoadFastFile = null,
                Notes = notes,
                TextReportFile = txt,
                JsonReportFile = json
            };
            await WriteAsync(none, cancellationToken);
            return none;
        }

        var packageDir = Path.Combine(outputRoot, map.Name, "hardware_test_load_only");
        if (Directory.Exists(packageDir)) Directory.Delete(packageDir, recursive: true);
        Directory.CreateDirectory(packageDir);
        var dest = Path.Combine(packageDir, Path.GetFileName(emitter.OutputFastFile));
        File.Copy(emitter.OutputFastFile, dest, overwrite: true);
        var sha = await Sha256Async(dest, cancellationToken);

        notes.Add("LOAD-ONLY A/B TEST: replace ONLY the matching load FF in an otherwise known-good Xbox package. Do not replace the main FF for this test.");
        notes.Add("If the known-good package boots/loads with this file, the V2 canonical load selector + dependency resolution + Xbox load serializer are independently native-proven for this map.");
        notes.Add("Restore the known-good load FF after the A/B test before testing future main-FF candidates unless explicitly instructed otherwise.");

        var readme = new StringBuilder();
        readme.AppendLine("WaW360Converter — LINKER V2 LOAD-ONLY NATIVE A/B TEST");
        readme.AppendLine(new string('=', 72));
        readme.AppendLine($"Map: {map.Name}");
        readme.AppendLine($"Generated file: {Path.GetFileName(dest)}");
        readme.AppendLine($"SHA-256: {sha}");
        readme.AppendLine();
        foreach (var note in notes) readme.AppendLine(note);
        await File.WriteAllTextAsync(Path.Combine(packageDir, "LOAD_ONLY_NATIVE_TEST.txt"), readme.ToString(), new UTF8Encoding(false), cancellationToken);

        var combinedReady = false;
        string? combinedDir = null;
        string? combinedMain = null;
        string? combinedLoad = null;
        var mainJson = Path.Combine(
            outputRoot, map.Name, "linker_v2_main_structural",
            $"{map.Name}_linker_v2_main_structural.json");
        if (File.Exists(mainJson))
        {
            try
            {
                var main = JsonSerializer.Deserialize<NormalMapLinkerV2StructuralMainEmitResult>(
                    await File.ReadAllTextAsync(mainJson, cancellationToken));
                if (main is not null &&
                    main.StructuralNativeCandidateReady &&
                    main.ReadBackErrors == 0 &&
                    main.CorrectZoneSizeSemantics &&
                    main.PerBlockCapacityHeader &&
                    main.WrapperRoundTripVerified &&
                    !string.IsNullOrWhiteSpace(main.OutputFastFile) &&
                    File.Exists(main.OutputFastFile))
                {
                    combinedDir = Path.Combine(outputRoot, map.Name, "hardware_test_main_plus_load");
                    if (Directory.Exists(combinedDir)) Directory.Delete(combinedDir, recursive: true);
                    Directory.CreateDirectory(combinedDir);

                    combinedMain = Path.Combine(combinedDir, Path.GetFileName(main.OutputFastFile));
                    combinedLoad = Path.Combine(combinedDir, Path.GetFileName(emitter.OutputFastFile));
                    File.Copy(main.OutputFastFile, combinedMain, overwrite: true);
                    File.Copy(emitter.OutputFastFile, combinedLoad, overwrite: true);

                    var mainSha = await Sha256Async(combinedMain, cancellationToken);
                    var loadSha = await Sha256Async(combinedLoad, cancellationToken);
                    var pairReadme = new StringBuilder();
                    pairReadme.AppendLine("WaW360Converter — LINKER V2 MAIN + LOAD STRUCTURAL NATIVE TEST");
                    pairReadme.AppendLine(new string('=', 82));
                    pairReadme.AppendLine($"Map: {map.Name}");
                    pairReadme.AppendLine($"Main: {Path.GetFileName(combinedMain)}");
                    pairReadme.AppendLine($"Main SHA-256: {mainSha}");
                    pairReadme.AppendLine($"Load: {Path.GetFileName(combinedLoad)}");
                    pairReadme.AppendLine($"Load SHA-256: {loadSha}");
                    pairReadme.AppendLine();
                    pairReadme.AppendLine("BOTH FF FILES IN THIS FOLDER WERE GENERATED BY THE CONVERTER.");
                    pairReadme.AppendLine("This remains a structural smoke test, NOT a production/fidelity-ready conversion.");
                    pairReadme.AppendLine("Keep the map's existing sounds folder, preview/description/t4ff metadata and other companion files in place.");
                    pairReadme.AppendLine("Back up the currently working/original FFs before replacing them.");
                    pairReadme.AppendLine("A farther load proves progress in native main+load integration; graphics/audio/gameplay fidelity is still gated separately.");
                    await File.WriteAllTextAsync(
                        Path.Combine(combinedDir, "MAIN_PLUS_LOAD_NATIVE_TEST.txt"),
                        pairReadme.ToString(),
                        new UTF8Encoding(false),
                        cancellationToken);

                    combinedReady = true;
                    notes.Add($"MAIN+LOAD STRUCTURAL TEST READY: converter-generated pair emitted to {combinedDir}.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notes.Add($"Main+load paired package skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Mirror the proof-heavy pair into a single hardware-matrix parent so A/B testing
        // does not require choosing between several similarly named top-level folders.
        var matrixRoot = Path.Combine(outputRoot, map.Name, "hardware_test_main_matrix");
        string? matrixProofHeavyDir = null;
        if (combinedReady && combinedMain is not null && combinedLoad is not null)
        {
            matrixProofHeavyDir = Path.Combine(matrixRoot, "proof_heavy");
            if (Directory.Exists(matrixProofHeavyDir)) Directory.Delete(matrixProofHeavyDir, recursive: true);
            Directory.CreateDirectory(matrixProofHeavyDir);
            File.Copy(combinedMain, Path.Combine(matrixProofHeavyDir, Path.GetFileName(combinedMain)), overwrite: true);
            File.Copy(combinedLoad, Path.Combine(matrixProofHeavyDir, Path.GetFileName(combinedLoad)), overwrite: true);
        }

        var trackedShellPairReady = false;
        string? trackedShellPairDir = null;
        string? trackedShellMain = null;
        string? trackedShellLoad = null;

        if (File.Exists(mainJson))
        {
            try
            {
                var main = JsonSerializer.Deserialize<NormalMapLinkerV2StructuralMainEmitResult>(
                    await File.ReadAllTextAsync(mainJson, cancellationToken));
                if (main is not null &&
                    main.ConservativeCandidateReady &&
                    main.ConservativeReadBackErrors == 0 &&
                    main.ConservativeWrapperRoundTripVerified &&
                    !string.IsNullOrWhiteSpace(main.ConservativeOutputFastFile) &&
                    File.Exists(main.ConservativeOutputFastFile))
                {
                    trackedShellPairDir = Path.Combine(matrixRoot, "tracked_shell");
                    if (Directory.Exists(trackedShellPairDir)) Directory.Delete(trackedShellPairDir, recursive: true);
                    Directory.CreateDirectory(trackedShellPairDir);

                    trackedShellMain = Path.Combine(trackedShellPairDir, Path.GetFileName(main.ConservativeOutputFastFile));
                    trackedShellLoad = Path.Combine(trackedShellPairDir, Path.GetFileName(emitter.OutputFastFile));
                    File.Copy(main.ConservativeOutputFastFile, trackedShellMain, overwrite: true);
                    File.Copy(emitter.OutputFastFile, trackedShellLoad, overwrite: true);

                    var mainSha = await Sha256Async(trackedShellMain, cancellationToken);
                    var loadSha = await Sha256Async(trackedShellLoad, cancellationToken);
                    var matrixReadme = new StringBuilder();
                    matrixReadme.AppendLine("WaW360Converter — HARDWARE MATRIX: TRACKED-SHELL MAIN + GENERATED LOAD");
                    matrixReadme.AppendLine(new string('=', 86));
                    matrixReadme.AppendLine($"Map: {map.Name}");
                    matrixReadme.AppendLine($"Main SHA-256: {mainSha}");
                    matrixReadme.AppendLine($"Load SHA-256: {loadSha}");
                    matrixReadme.AppendLine();
                    matrixReadme.AppendLine("This pair is generated entirely by the converter.");
                    matrixReadme.AppendLine("Compared with hardware_test_main_plus_load, only XAnim/XModel/Weapon/FX are reduced to named fixed shells.");
                    matrixReadme.AppendLine("World/material/sound/simple-root behavior is intentionally identical.");
                    matrixReadme.AppendLine();
                    matrixReadme.AppendLine("INTERPRETATION");
                    matrixReadme.AppendLine("  Same exception address as proof-heavy pair => prioritize world/material/simple-root initialization.");
                    matrixReadme.AppendLine("  Different/later exception => promoted tracked payloads contributed to the earlier frontier.");
                    matrixReadme.AppendLine("  This is diagnostic only; it is not a production/fidelity-ready conversion.");
                    await File.WriteAllTextAsync(
                        Path.Combine(trackedShellPairDir, "TRACKED_SHELL_PAIR_TEST.txt"),
                        matrixReadme.ToString(),
                        new UTF8Encoding(false),
                        cancellationToken);

                    trackedShellPairReady = true;
                    notes.Add($"TRACKED-SHELL MATRIX PAIR READY: {trackedShellPairDir}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notes.Add($"Tracked-shell pair skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (combinedReady || trackedShellPairReady)
        {
            Directory.CreateDirectory(matrixRoot);
            var matrix = new StringBuilder();
            matrix.AppendLine("WaW360Converter — MAIN+LOAD HARDWARE MATRIX");
            matrix.AppendLine(new string('=', 72));
            matrix.AppendLine($"Map: {map.Name}");
            matrix.AppendLine();
            matrix.AppendLine("Both variants use the SAME converter-generated load FF.");
            matrix.AppendLine("The only intended A/B difference is the tracked main-asset payload policy.");
            matrix.AppendLine();
            matrix.AppendLine("TEST A — proof_heavy");
            matrix.AppendLine("  Uses every self-contained XAnim/XModel/FX payload currently proven safe, plus smoke-promoted XModels.");
            matrix.AppendLine("  This matches the main policy used at the 0x82168594 / 0xC0000005 hardware frontier.");
            matrix.AppendLine();
            matrix.AppendLine("TEST B — tracked_shell");
            matrix.AppendLine("  XAnim/XModel/Weapon/FX are reduced to named fixed shells; world/material/sound/simple roots stay identical.");
            matrix.AppendLine();
            matrix.AppendLine("INTERPRETATION");
            matrix.AppendLine("  Same first exception address in A and B => tracked promoted payloads are probably not the frontier; prioritize world/material/simple roots.");
            matrix.AppendLine("  B gets farther or faults elsewhere => at least one promoted tracked payload participates in A's frontier.");
            matrix.AppendLine();
            matrix.AppendLine("For each test, stop/break xbWatson on the FIRST exception and record exception code, instruction address, registers/call stack if available.");
            matrix.AppendLine("Back up the known-working FFs; keep sounds, previews, description, t4ff metadata and all other companion files unchanged.");
            await File.WriteAllTextAsync(
                Path.Combine(matrixRoot, "TEST_ORDER_AND_INTERPRETATION.txt"),
                matrix.ToString(),
                new UTF8Encoding(false),
                cancellationToken);

            var latest = new StringBuilder();
            latest.AppendLine("LATEST HARDWARE TEST");
            latest.AppendLine(new string('=', 72));
            latest.AppendLine($"Map: {map.Name}");
            latest.AppendLine($"Use folder: {matrixRoot}");
            latest.AppendLine();
            latest.AppendLine("TEST ORDER");
            latest.AppendLine("  1) hardware_test_main_matrix\\tracked_shell");
            latest.AppendLine("  2) hardware_test_main_matrix\\proof_heavy");
            latest.AppendLine();
            latest.AppendLine("For each folder, copy BOTH generated FFs as a pair.");
            latest.AppendLine("Keep sounds/preview/description/t4ff and all other companion files unchanged.");
            latest.AppendLine("Set xbWatson to stop/break on the FIRST exception and record the exception code + PC address.");
            latest.AppendLine();
            latest.AppendLine("Previous hardware frontier: 0xC0000005 access violation at PC 0x82168594.");
            await File.WriteAllTextAsync(
                Path.Combine(outputRoot, map.Name, "LATEST_HARDWARE_TEST.txt"),
                latest.ToString(),
                new UTF8Encoding(false),
                cancellationToken);
        }

        var result = new NormalMapLinkerV2LoadHardwarePackageResult
        {
            MapName = map.Name,
            Ready = true,
            PackageDirectory = packageDir,
            FastFile = dest,
            Sha256 = sha,
            CombinedMainLoadReady = combinedReady,
            CombinedPackageDirectory = combinedDir,
            CombinedMainFastFile = combinedMain,
            CombinedLoadFastFile = combinedLoad,
            TrackedShellPairReady = trackedShellPairReady,
            TrackedShellPairDirectory = trackedShellPairDir,
            TrackedShellMainFastFile = trackedShellMain,
            TrackedShellLoadFastFile = trackedShellLoad,
            Notes = notes,
            TextReportFile = txt,
            JsonReportFile = json
        };
        await WriteAsync(result, cancellationToken);
        return result;
    }

    private static async Task WriteAsync(NormalMapLinkerV2LoadHardwarePackageResult r, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 4 load-only hardware package gate");
        sb.AppendLine(new string('=', 96));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"ready={r.Ready}; package={r.PackageDirectory ?? "<none>"}; ff={r.FastFile ?? "<none>"}");
        if (r.Sha256 is not null) sb.AppendLine($"sha256={r.Sha256}");
        sb.AppendLine($"combinedMainLoadReady={r.CombinedMainLoadReady}; combinedPackage={r.CombinedPackageDirectory ?? "<none>"}; main={r.CombinedMainFastFile ?? "<none>"}; load={r.CombinedLoadFastFile ?? "<none>"}");
        sb.AppendLine($"trackedShellPairReady={r.TrackedShellPairReady}; trackedShellPair={r.TrackedShellPairDirectory ?? "<none>"}; main={r.TrackedShellMainFastFile ?? "<none>"}; load={r.TrackedShellLoadFastFile ?? "<none>"}");
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        await File.WriteAllTextAsync(r.TextReportFile, sb.ToString(), new UTF8Encoding(false), ct);
        await File.WriteAllTextAsync(r.JsonReportFile, JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), ct);
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
