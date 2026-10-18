using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4FoundationFastFileEmission(
    string Role,
    string Source,
    string ZoneImageFile,
    string ProbeFile,
    long ProbeBytes,
    string ProbeSha256,
    bool ProbeRoundTripVerified,
    bool MaterialTechniqueCandidate,
    bool HardwareReady,
    string? HardwareFastFile,
    string? HardwareFastFileSha256);

public sealed class NormalMapFoundationFastFileEmitterResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required string LocalProbeRoot { get; init; }
    public required IReadOnlyList<T4FoundationFastFileEmission> FastFiles { get; init; }
    public required int ProbeFastFiles { get; init; }
    public required int ProbeRoundTripVerified { get; init; }
    public required int MaterialTechniqueCandidateFastFiles { get; init; }
    public required int HardwareFastFilesEmitted { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5k / alpha 10e: wraps the Stage-5i foundation-promoted native zones with the
/// already-proven Xbox IWffu100 + 64-KiB block-deflate path and round-trips every probe.
/// A real .ff still requires the exact per-fastfile Stage-5j hardware gate to be clear.
/// </summary>
public static class NormalMapFoundationFastFileEmitter
{
    public static async Task<NormalMapFoundationFastFileEmitterResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var jJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10e_readiness", $"{map.Name}_stage5_alpha10e_readiness.json");
        if (!File.Exists(jJson))
            throw new InvalidDataException("Stage 5k requires a verified Stage 5j readiness inventory.");

        var gate = JsonSerializer.Deserialize<NormalMapFoundationReadinessInventoryResult>(
            await File.ReadAllTextAsync(jJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5j result.");
        if (!gate.Verified)
            throw new InvalidDataException("Stage 5j accounting is not verified; FF emission refused.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10e_ff_emitter");
        Directory.CreateDirectory(workspace);
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WaW360Converter", "ff_probes_10e", Sanitize(map.Name));
        Directory.CreateDirectory(localRoot);
        var hardwareRoot = Path.Combine(outputRoot, map.Name, "hardware_candidates");

        var rows = new List<T4FoundationFastFileEmission>();
        var detail = new List<string>();

        foreach (var ff in gate.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(ff.ZoneImageFile))
                throw new InvalidDataException($"Stage 5k cannot find foundation-promoted native zone image '{ff.ZoneImageFile}'.");

            var stem = Path.GetFileNameWithoutExtension(ff.Source) + "_" + Sanitize(ff.Role);
            var probe = Path.Combine(localRoot, stem + ".ffprobe");
            await FastFileRepacker.RepackZoneFileAsync(ff.Source, ff.ZoneImageFile, probe, WaWCompressionKind.XboxBlockDeflate, cancellationToken);

            var verifyZone = Path.Combine(localRoot, stem + ".verify.zone");
            try
            {
                var decomp = await FastFileDecompressor.DecompressAutoAsync(probe, verifyZone, cancellationToken);
                if (decomp.Platform != WaWPlatform.Xbox360 || decomp.Compression != WaWCompressionKind.XboxBlockDeflate)
                    throw new InvalidDataException($"Stage 5k probe '{Path.GetFileName(probe)}' did not identify as unsigned Xbox block-deflate.");
                if (!await FilesEqualAsync(ff.ZoneImageFile, verifyZone, cancellationToken))
                    throw new InvalidDataException($"Stage 5k probe '{Path.GetFileName(probe)}' did not decompress byte-for-byte to the Stage-5i zone.");
            }
            finally
            {
                try { File.Delete(verifyZone); } catch { }
            }

            string? realFf = null;
            string? realSha = null;
            if (ff.HardwareReady)
            {
                Directory.CreateDirectory(hardwareRoot);
                realFf = Path.Combine(hardwareRoot, Path.GetFileName(ff.Source));
                File.Copy(probe, realFf, overwrite: true);
                realSha = await Sha256Async(realFf, cancellationToken);
            }

            var probeSha = await Sha256Async(probe, cancellationToken);
            var probeBytes = new FileInfo(probe).Length;
            rows.Add(new T4FoundationFastFileEmission(ff.Role, ff.Source, ff.ZoneImageFile, probe,
                probeBytes, probeSha, true, ff.MaterialTechniqueCandidate, ff.HardwareReady, realFf, realSha));
            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: ffprobe={probeBytes:N0}B; xboxBlockDeflate=True; roundTrip=True; materialTechCandidate={ff.MaterialTechniqueCandidate}; hardwareReady={ff.HardwareReady}; emittedFF={(realFf is null ? "NO (gate blocked)" : realFf)}");
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10e_ff_emitter.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10e_ff_emitter.json");
        var result = new NormalMapFoundationFastFileEmitterResult
        {
            MapName = map.Name,
            Workspace = workspace,
            LocalProbeRoot = localRoot,
            FastFiles = rows,
            ProbeFastFiles = rows.Count,
            ProbeRoundTripVerified = rows.Count(x => x.ProbeRoundTripVerified),
            MaterialTechniqueCandidateFastFiles = rows.Count(x => x.MaterialTechniqueCandidate),
            HardwareFastFilesEmitted = rows.Count(x => x.HardwareFastFile is not null),
            Verified = rows.Count != 0 && rows.All(x => x.ProbeRoundTripVerified),
            HardwareReady = gate.HardwareReady && rows.Count != 0 && rows.All(x => x.HardwareFastFile is not null),
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static async Task<bool> FilesEqualAsync(string a, string b, CancellationToken ct)
    {
        var ai = new FileInfo(a);
        var bi = new FileInfo(b);
        if (ai.Length != bi.Length) return false;

        await using var x = File.OpenRead(a);
        await using var y = File.OpenRead(b);
        var xb = new byte[1024 * 1024];
        var yb = new byte[xb.Length];
        while (true)
        {
            var xn = await x.ReadAsync(xb.AsMemory(), ct);
            var yn = await y.ReadAsync(yb.AsMemory(), ct);
            if (xn != yn) return false;
            if (xn == 0) return true;
            if (!xb.AsSpan(0, xn).SequenceEqual(yb.AsSpan(0, yn))) return false;
        }
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(fs, ct)).ToLowerInvariant();
    }

    private static string Render(NormalMapFoundationFastFileEmitterResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5k alpha 10e foundation-aware Xbox FF gate");
        sb.AppendLine(new string('=', 84));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Xbox .ffprobe containers: {r.ProbeFastFiles:N0}");
        sb.AppendLine($"Byte-exact decompression round-trips: {r.ProbeRoundTripVerified:N0}/{r.ProbeFastFiles:N0}");
        sb.AppendLine($"Material/Techset-only candidates: {r.MaterialTechniqueCandidateFastFiles:N0}");
        sb.AppendLine($"Real hardware .ff emitted: {r.HardwareFastFilesEmitted:N0}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine($"Hardware ready: {r.HardwareReady}");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: this wraps the Stage-5i zone after RawFile/Localize/StringTable promotion. Material/TechniqueSet remain deliberately gated until their typed PC->Xbox conversion is implemented. .ffprobe is never a hardware candidate.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }

    private static string Sanitize(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
