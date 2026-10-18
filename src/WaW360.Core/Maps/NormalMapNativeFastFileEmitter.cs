using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4NativeFastFileEmission(
    string Role,
    string Source,
    string ZoneImageFile,
    string ProbeFile,
    long ProbeBytes,
    string ProbeSha256,
    bool ProbeRoundTripVerified,
    bool HardwareReady,
    string? HardwareFastFile,
    string? HardwareFastFileSha256);

public sealed class NormalMapNativeFastFileEmitterResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required string LocalProbeRoot { get; init; }
    public required IReadOnlyList<T4NativeFastFileEmission> FastFiles { get; init; }
    public required int ProbeFastFiles { get; init; }
    public required int ProbeRoundTripVerified { get; init; }
    public required int HardwareFastFilesEmitted { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5h / alpha 10d: proves the real unsigned Xbox fastfile wrapper/compressor on
/// every Stage-5f reconstructed zone. Incomplete zones are written with a .ffprobe
/// extension and must decompress byte-for-byte. A real .ff is emitted only when Stage 5g
/// certifies that exact fastfile has zero readiness blockers.
/// </summary>
public static class NormalMapNativeFastFileEmitter
{
    public static async Task<NormalMapNativeFastFileEmitterResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var gJson = Path.Combine(outputRoot, map.Name, "stage5_alpha10d_readiness", $"{map.Name}_stage5_alpha10d_readiness.json");
        if (!File.Exists(gJson))
            throw new InvalidDataException("Stage 5h requires a verified Stage 5g readiness inventory.");
        var gate = JsonSerializer.Deserialize<NormalMapNativeReadinessInventoryResult>(
            await File.ReadAllTextAsync(gJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5g result.");
        if (!gate.Verified)
            throw new InvalidDataException("Stage 5g accounting is not verified; FF emission refused.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10d_ff_emitter");
        Directory.CreateDirectory(workspace);
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WaW360Converter", "ff_probes", Sanitize(map.Name));
        Directory.CreateDirectory(localRoot);
        var hardwareRoot = Path.Combine(outputRoot, map.Name, "hardware_candidates");

        var rows = new List<T4NativeFastFileEmission>();
        var detail = new List<string>();

        foreach (var ff in gate.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(ff.ZoneImageFile))
                throw new InvalidDataException($"Stage 5h cannot find native zone image '{ff.ZoneImageFile}'.");

            var stem = Path.GetFileNameWithoutExtension(ff.Source) + "_" + Sanitize(ff.Role);
            var probe = Path.Combine(localRoot, stem + ".ffprobe");
            await FastFileRepacker.RepackZoneFileAsync(ff.Source, ff.ZoneImageFile, probe, WaWCompressionKind.XboxBlockDeflate, cancellationToken);

            var verifyZone = Path.Combine(localRoot, stem + ".verify.zone");
            try
            {
                var decomp = await FastFileDecompressor.DecompressAutoAsync(probe, verifyZone, cancellationToken);
                if (decomp.Platform != WaWPlatform.Xbox360 || decomp.Compression != WaWCompressionKind.XboxBlockDeflate)
                    throw new InvalidDataException($"Stage 5h probe '{Path.GetFileName(probe)}' did not identify as unsigned Xbox block-deflate.");
                if (!await FilesEqualAsync(ff.ZoneImageFile, verifyZone, cancellationToken))
                    throw new InvalidDataException($"Stage 5h probe '{Path.GetFileName(probe)}' did not decompress byte-for-byte to the Stage-5f zone.");
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
            rows.Add(new T4NativeFastFileEmission(ff.Role, ff.Source, ff.ZoneImageFile, probe, probeBytes,
                probeSha, true, ff.HardwareReady, realFf, realSha));
            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: ffprobe={probeBytes:N0}B; xboxBlockDeflate=True; roundTrip=True; hardwareReady={ff.HardwareReady}; emittedFF={(realFf is null ? "NO (gate blocked)" : realFf)}");
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10d_ff_emitter.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10d_ff_emitter.json");
        var result = new NormalMapNativeFastFileEmitterResult
        {
            MapName = map.Name,
            Workspace = workspace,
            LocalProbeRoot = localRoot,
            FastFiles = rows,
            ProbeFastFiles = rows.Count,
            ProbeRoundTripVerified = rows.Count(x => x.ProbeRoundTripVerified),
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
        var ai = new FileInfo(a); var bi = new FileInfo(b);
        if (ai.Length != bi.Length) return false;
        await using var x = File.OpenRead(a); await using var y = File.OpenRead(b);
        var xb = new byte[1024 * 1024]; var yb = new byte[xb.Length];
        while (true)
        {
            var xn = await x.ReadAsync(xb, ct); var yn = await y.ReadAsync(yb, ct);
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
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Render(NormalMapNativeFastFileEmitterResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5h alpha 10d Xbox FF wrapper/compression gate");
        sb.AppendLine(new string('=', 82));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Xbox .ffprobe containers: {r.ProbeFastFiles:N0}");
        sb.AppendLine($"Byte-exact decompression round-trips: {r.ProbeRoundTripVerified:N0}/{r.ProbeFastFiles:N0}");
        sb.AppendLine($"Real hardware .ff emitted: {r.HardwareFastFilesEmitted:N0}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine($"Hardware ready: {r.HardwareReady}");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: .ffprobe uses the REAL unsigned Xbox WaW wrapper and 64-KiB block-deflate layout, but it is NOT hardware-testable while Stage 5g reports blockers. The same byte path emits a .ff automatically only after the readiness gate reaches zero blockers.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }

    private static string Sanitize(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
