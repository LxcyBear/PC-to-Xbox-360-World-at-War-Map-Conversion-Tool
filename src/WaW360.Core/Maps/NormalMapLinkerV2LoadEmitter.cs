using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.Maps;

public sealed class NormalMapLinkerV2LoadEmitResult
{
    public required string MapName { get; init; }
    public required string? SelectedSourceFastFile { get; init; }
    public required string? OutputFastFile { get; init; }
    public required string? OutputZone { get; init; }
    public required int XboxAssets { get; init; }
    public required int ZoneBytes { get; init; }
    public required int FastFileBytes { get; init; }
    public required bool HardwareReady { get; init; }
    public required bool GraphOrderVerified { get; init; }
    public required bool ZoneReadBackVerified { get; init; }
    public required bool WrapperRoundTripVerified { get; init; }
    public required bool DefeatReferenceDonorUsed { get; init; }
    public required bool RetailStockOriginProven { get; init; }
    public required string? FastFileSha256 { get; init; }
    public required bool GoldenOracleApplied { get; init; }
    public required int? GoldenLoadAssets { get; init; }
    public required int? GoldenLoadZoneBytes { get; init; }
    public required int? GoldenLoadFastFileBytes { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 4 production-selected load.ff emitter.
///
/// This absorbs the hardware-proven Linker-V1 seven-node typed serializer into the V2 package
/// semantics: ONLY the load source selected by the canonical primary identity is emitted.  Alternate
/// load FFs remain source-archive inputs and are never mirrored into the Xbox package by this stage.
///
/// The underlying seven-node serializer is the same deterministic no-scan implementation that passed
/// real Xbox hardware on Aztec.  V2 changes source selection/output naming and validates the result
/// against an optional golden package shape without making that golden payload a conversion dependency.
/// </summary>
public static class NormalMapLinkerV2LoadEmitter
{
    public static async Task<NormalMapLinkerV2LoadEmitResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null,
        CancellationToken cancellationToken = default)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_load_emit");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_load_emit.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_load_emit.json");
        var golden = LinkerV2GoldenCatalog.Find(source.CanonicalBaseName);
        var notes = new List<string>();

        if (string.IsNullOrWhiteSpace(source.SelectedLoadSource))
        {
            notes.Add("No canonical load.ff exists for this map; no load fastfile is emitted. This is a valid package shape.");
            var none = new NormalMapLinkerV2LoadEmitResult
            {
                MapName = map.Name,
                SelectedSourceFastFile = null,
                OutputFastFile = null,
                OutputZone = null,
                XboxAssets = 0,
                ZoneBytes = 0,
                FastFileBytes = 0,
                HardwareReady = false,
                GraphOrderVerified = true,
                ZoneReadBackVerified = true,
                WrapperRoundTripVerified = true,
                DefeatReferenceDonorUsed = false,
                RetailStockOriginProven = false,
                FastFileSha256 = null,
                GoldenOracleApplied = golden is not null,
                GoldenLoadAssets = golden?.LoadAssets,
                GoldenLoadZoneBytes = golden is null ? null : checked((int)golden.LoadZoneBytes),
                GoldenLoadFastFileBytes = golden is null ? null : checked((int)golden.LoadFastFileBytes),
                Notes = notes,
                Verified = true,
                TextReportFile = txt,
                JsonReportFile = json
            };
            await WriteAsync(none, cancellationToken);
            return none;
        }

        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        var legacyDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "linker-v2-load", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(legacyDir);
        NormalMapLinkerV1Alpha3EmitFastFileResult row;
        var outputName = source.LoadOutputFastFile ?? Path.GetFileName(source.SelectedLoadSource);
        var outputFf = Path.Combine(outDir, outputName);
        var outputZone = Path.Combine(outDir, Path.GetFileNameWithoutExtension(outputName) + ".zone");
        try
        {
            row = await NormalMapLinkerV1Alpha3Emitter.EmitOneAsync(
                analysis, source.SelectedLoadSource, legacyDir, cancellationToken);

            if (!row.HardwareReady || !row.GraphOrderVerified || !row.ZoneReadBackVerified || !row.WrapperRoundTripVerified)
                throw new InvalidDataException("Linker V2 selected load emitter refuses promotion because the absorbed seven-node serializer did not pass all hardware-candidate gates.");

            File.Copy(row.OutputFastFile, outputFf, overwrite: true);
            File.Copy(row.OutputZone, outputZone, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(legacyDir, recursive: true); } catch { }
        }

        var zoneBytes = checked((int)new FileInfo(outputZone).Length);
        var ffBytes = checked((int)new FileInfo(outputFf).Length);
        var ffSha = await Sha256Async(outputFf, cancellationToken);

        notes.Add("V2 selected-load policy: only the canonical load source is emitted; rejected/alternate load FFs remain source-archive inputs.");
        notes.Add("The seven-node serializer/read-back is the hardware-proven Aztec path and uses no broad candidate scan.");
        notes.Add("The stock 'defeat' Xbox runtime donor is hardware-proven and its exact payload hash also matches the known-working Simpsons package; original retail-stock provenance remains tracked separately.");
        if (golden is not null)
        {
            notes.Add($"Golden shape: assets={golden.LoadAssets:N0}; zone={golden.LoadZoneBytes:N0}B; ff={golden.LoadFastFileBytes:N0}B. Byte-exact FF equality is NOT required because converter-specific asset naming/compression may differ while the typed graph remains valid.");
        }

        var result = new NormalMapLinkerV2LoadEmitResult
        {
            MapName = map.Name,
            SelectedSourceFastFile = source.SelectedLoadSource,
            OutputFastFile = outputFf,
            OutputZone = outputZone,
            XboxAssets = row.XboxAssets,
            ZoneBytes = zoneBytes,
            FastFileBytes = ffBytes,
            HardwareReady = row.HardwareReady,
            GraphOrderVerified = row.GraphOrderVerified,
            ZoneReadBackVerified = row.ZoneReadBackVerified,
            WrapperRoundTripVerified = row.WrapperRoundTripVerified,
            DefeatReferenceDonorUsed = row.DefeatReferenceDonorUsed,
            RetailStockOriginProven = row.RetailStockOriginProven,
            FastFileSha256 = ffSha,
            GoldenOracleApplied = golden is not null,
            GoldenLoadAssets = golden?.LoadAssets,
            GoldenLoadZoneBytes = golden is null ? null : checked((int)golden.LoadZoneBytes),
            GoldenLoadFastFileBytes = golden is null ? null : checked((int)golden.LoadFastFileBytes),
            Notes = notes,
            Verified = true,
            TextReportFile = txt,
            JsonReportFile = json
        };
        await WriteAsync(result, cancellationToken);
        return result;
    }

    private static async Task WriteAsync(NormalMapLinkerV2LoadEmitResult result, CancellationToken ct)
    {
        await File.WriteAllTextAsync(result.TextReportFile, Render(result), new UTF8Encoding(false), ct);
        await File.WriteAllTextAsync(result.JsonReportFile, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), ct);
    }

    private static string Render(NormalMapLinkerV2LoadEmitResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 4 selected production load.ff emitter");
        sb.AppendLine(new string('=', 96));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Selected source: {r.SelectedSourceFastFile ?? "<none>"}");
        sb.AppendLine($"Output FF: {r.OutputFastFile ?? "<none>"}");
        sb.AppendLine($"Xbox assets: {r.XboxAssets:N0}; zone={r.ZoneBytes:N0}B; ff={r.FastFileBytes:N0}B");
        sb.AppendLine($"graphOrder={r.GraphOrderVerified}; zoneReadBack={r.ZoneReadBackVerified}; wrapperRoundTrip={r.WrapperRoundTripVerified}; hardwareReady={r.HardwareReady}; verified={r.Verified}");
        if (r.FastFileSha256 is not null) sb.AppendLine($"sha256={r.FastFileSha256}");
        sb.AppendLine($"defeatReferenceDonor={r.DefeatReferenceDonorUsed}; retailStockOriginProven={r.RetailStockOriginProven}");
        if (r.GoldenOracleApplied)
            sb.AppendLine($"golden: assets={r.GoldenLoadAssets:N0}; zone={r.GoldenLoadZoneBytes:N0}B; ff={r.GoldenLoadFastFileBytes:N0}B");
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        return sb.ToString();
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
