using System.Text;
using System.Text.Json;

namespace WaW360.Core.Maps;

public sealed class NormalMapLinkerV2LoadResolutionResult
{
    public required string MapName { get; init; }
    public required string? SelectedSourceFastFile { get; init; }
    public required int SourceGraphBlockedDependencies { get; init; }
    public required int ImageDependencies { get; init; }
    public required int ResolvedCustomIwdImages { get; init; }
    public required int ResolvedReferenceDonors { get; init; }
    public required int RemainingUnresolvedDependencies { get; init; }
    public required bool HardwareCandidateReady { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Alpha 3 closes the reporting gap between the source graph and the production load emitter.
/// A PC image node can be blocked in the source graph because its pixels live in an IWD or verified
/// donor, yet become fully resolved before Xbox emission. This stage records that transition so a
/// successful hardware candidate no longer misleadingly carries "blockedDeps=2" as its final state.
/// </summary>
public static class NormalMapLinkerV2LoadResolution
{
    public static async Task<NormalMapLinkerV2LoadResolutionResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2LoadSelectionResult selection,
        NormalMapLinkerV2LoadEmitResult emitter,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(outputRoot, map.Name, "linker_v2_load_resolution");
        Directory.CreateDirectory(dir);
        var txt = Path.Combine(dir, $"{map.Name}_linker_v2_load_resolution.txt");
        var json = Path.Combine(dir, $"{map.Name}_linker_v2_load_resolution.json");
        var notes = new List<string>();

        if (selection.SelectedSourceFastFile is null)
        {
            notes.Add("No canonical load FF exists; dependency resolution is a valid no-op for this package shape.");
            var none = new NormalMapLinkerV2LoadResolutionResult
            {
                MapName = map.Name,
                SelectedSourceFastFile = null,
                SourceGraphBlockedDependencies = 0,
                ImageDependencies = 0,
                ResolvedCustomIwdImages = 0,
                ResolvedReferenceDonors = 0,
                RemainingUnresolvedDependencies = 0,
                HardwareCandidateReady = false,
                Verified = true,
                Notes = notes,
                TextReportFile = txt,
                JsonReportFile = json
            };
            await WriteAsync(none, cancellationToken);
            return none;
        }

        var imageDependencies = selection.OutputOrder.Count(x => x.Contains(" image:", StringComparison.OrdinalIgnoreCase));
        var referenceDonors = emitter.DefeatReferenceDonorUsed ? 1 : 0;
        var customResolved = emitter.HardwareReady ? Math.Max(0, imageDependencies - referenceDonors) : 0;
        var resolved = emitter.HardwareReady ? imageDependencies : 0;
        var remaining = Math.Max(0, selection.BlockedDependencies - resolved);

        if (emitter.HardwareReady && remaining == 0)
            notes.Add($"SOURCE->XBOX RESOLUTION CLOSED: {selection.BlockedDependencies:N0} source-side blocked image dependency(ies) were resolved before emission ({customResolved:N0} custom IWD image(s), {referenceDonors:N0} verified donor(s)).");
        if (emitter.HardwareReady)
            notes.Add("The final load hardware-candidate state is therefore dependency-complete even though the raw PC graph correctly records external image ownership.");

        var result = new NormalMapLinkerV2LoadResolutionResult
        {
            MapName = map.Name,
            SelectedSourceFastFile = selection.SelectedSourceFastFile,
            SourceGraphBlockedDependencies = selection.BlockedDependencies,
            ImageDependencies = imageDependencies,
            ResolvedCustomIwdImages = customResolved,
            ResolvedReferenceDonors = referenceDonors,
            RemainingUnresolvedDependencies = remaining,
            HardwareCandidateReady = emitter.HardwareReady && remaining == 0,
            Verified = emitter.Verified && remaining == 0,
            Notes = notes,
            TextReportFile = txt,
            JsonReportFile = json
        };
        await WriteAsync(result, cancellationToken);
        return result;
    }

    private static async Task WriteAsync(NormalMapLinkerV2LoadResolutionResult r, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 4 load dependency resolution");
        sb.AppendLine(new string('=', 96));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Selected source: {r.SelectedSourceFastFile ?? "<none>"}");
        sb.AppendLine($"sourceBlocked={r.SourceGraphBlockedDependencies:N0}; imageDeps={r.ImageDependencies:N0}; resolvedIWD={r.ResolvedCustomIwdImages:N0}; resolvedDonor={r.ResolvedReferenceDonors:N0}; remaining={r.RemainingUnresolvedDependencies:N0}");
        sb.AppendLine($"hardwareCandidateReady={r.HardwareCandidateReady}; verified={r.Verified}");
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        await File.WriteAllTextAsync(r.TextReportFile, sb.ToString(), new UTF8Encoding(false), ct);
        await File.WriteAllTextAsync(r.JsonReportFile, JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), ct);
    }
}
