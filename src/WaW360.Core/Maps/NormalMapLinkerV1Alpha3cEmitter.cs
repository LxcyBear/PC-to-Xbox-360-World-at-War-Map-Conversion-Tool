using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record NormalMapLinkerV1Alpha3cFastFileResult(
    string SourceFastFile,
    string SourceZone,
    string OutputFastFile,
    int ZoneBytes,
    int OutputBytes,
    string CompressionBackend,
    bool NativeZlibLevel6,
    bool WrapperRoundTripVerified,
    bool GoldenReferenceAvailable,
    bool GoldenFastFileByteExact,
    string ZoneSha256,
    string FastFileSha256);

public sealed class NormalMapLinkerV1Alpha3cResult
{
    public required string MapName { get; init; }
    public required int InputZones { get; init; }
    public required int EmittedFastFiles { get; init; }
    public required int NativeLevel6FastFiles { get; init; }
    public required int GoldenExactFastFiles { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<NormalMapLinkerV1Alpha3cFastFileResult> FastFiles { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V1 alpha 3c: generic deterministic Xbox single-zlib writer + optional golden oracle.
/// Golden-reference matching is diagnostic only. Future maps do not need a CoDXe reference.
/// </summary>
public static class NormalMapLinkerV1Alpha3cEmitter
{
    private const string AztecZoneSha256 = "31fe0e1122f5729405fcb1a47dd03f5d18e8064528754d232ada0da79ad8750e";
    private const string AztecFastFileSha256 = "5b3e4433b245139db957f6e3ae9de9d3ef6170d9db3ed2f8f185456bed68093c";
    private const int AztecFastFileBytes = 364367;

    public static async Task<NormalMapLinkerV1Alpha3cResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var alpha3bDir = Path.Combine(outputRoot, map.Name, "linker_v1_alpha3b");
        var alpha3bJson = Path.Combine(alpha3bDir, $"{map.Name}_linker_v1_alpha3b_emitter.json");
        NormalMapLinkerV1Alpha3bEmitResult alpha3b;
        if (!File.Exists(alpha3bJson))
        {
            alpha3b = await NormalMapLinkerV1Alpha3bEmitter.RunAsync(map, outputRoot, cancellationToken);
        }
        else
        {
            alpha3b = JsonSerializer.Deserialize<NormalMapLinkerV1Alpha3bEmitResult>(
                await File.ReadAllTextAsync(alpha3bJson, cancellationToken),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Could not read Linker V1 alpha-3b report.");
        }

        var outputDir = Path.Combine(outputRoot, map.Name, "linker_v1_alpha3c");
        Directory.CreateDirectory(outputDir);
        var txt = Path.Combine(outputDir, $"{map.Name}_linker_v1_alpha3c_compression_oracle.txt");
        var json = Path.Combine(outputDir, $"{map.Name}_linker_v1_alpha3c_compression_oracle.json");
        var rows = new List<NormalMapLinkerV1Alpha3cFastFileResult>();
        var warnings = new List<string>();

        foreach (var prior in alpha3b.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(prior.OutputZone))
                throw new FileNotFoundException("Alpha-3b byte-exact zone is missing.", prior.OutputZone);

            var zone = await File.ReadAllBytesAsync(prior.OutputZone, cancellationToken);
            var zoneSha = Sha256(zone);
            var baseName = Path.GetFileNameWithoutExtension(prior.SourceFastFile);
            var outFf = Path.Combine(outputDir, $"{baseName}_linker_v1_alpha3c_deterministic_zlib.ff");
            var write = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
                outFf, zone, preferNativeLevel6: true, cancellationToken: cancellationToken);
            var ffBytes = await File.ReadAllBytesAsync(outFf, cancellationToken);
            var ffSha = Sha256(ffBytes);

            var goldenAvailable = zoneSha.Equals(AztecZoneSha256, StringComparison.OrdinalIgnoreCase);
            var goldenExact = goldenAvailable &&
                              ffBytes.Length == AztecFastFileBytes &&
                              ffSha.Equals(AztecFastFileSha256, StringComparison.OrdinalIgnoreCase);

            if (goldenAvailable && !goldenExact)
            {
                warnings.Add(
                    $"{Path.GetFileName(prior.SourceFastFile)}: zone is the Aztec golden zone but final FF is not byte-exact. " +
                    $"backend={write.Backend}; size={ffBytes.Length:N0} vs {AztecFastFileBytes:N0}. This does not block generic maps; it only means the optional compressed-byte oracle was not reproduced on this host.");
            }

            rows.Add(new NormalMapLinkerV1Alpha3cFastFileResult(
                prior.SourceFastFile,
                prior.OutputZone,
                outFf,
                zone.Length,
                ffBytes.Length,
                write.Backend,
                write.NativeLevel6,
                write.RoundTripVerified,
                goldenAvailable,
                goldenExact,
                zoneSha,
                ffSha));
        }

        if (alpha3b.FastFiles.Count == 0)
            warnings.Add("No alpha-3b load zone exists for this map; alpha-3c compression oracle is a no-op. The generic writer remains available to whole-map emitters.");

        var result = new NormalMapLinkerV1Alpha3cResult
        {
            MapName = map.Name,
            InputZones = alpha3b.FastFiles.Count,
            EmittedFastFiles = rows.Count,
            NativeLevel6FastFiles = rows.Count(x => x.NativeZlibLevel6),
            GoldenExactFastFiles = rows.Count(x => x.GoldenFastFileByteExact),
            Verified = rows.All(x => x.WrapperRoundTripVerified),
            FastFiles = rows,
            Warnings = warnings,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static string Render(NormalMapLinkerV1Alpha3cResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Linker V1 alpha 3c deterministic zlib + optional golden oracle");
        sb.AppendLine(new string('=', 88));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Input byte-exact zones: {r.InputZones:N0}");
        sb.AppendLine($"Emitted deterministic FFs: {r.EmittedFastFiles:N0}");
        sb.AppendLine($"Native zlib level-6 FFs: {r.NativeLevel6FastFiles:N0}");
        sb.AppendLine($"Golden-reference byte-exact FFs: {r.GoldenExactFastFiles:N0}");
        sb.AppendLine($"Wrapper/read-back verified: {r.Verified}");
        sb.AppendLine();
        sb.AppendLine("POLICY");
        sb.AppendLine("  Native zlib level 6 is the preferred production single-zlib backend when available.");
        sb.AppendLine("  Golden-reference byte equality is an OPTIONAL Aztec validation oracle, never a dependency for a new map.");
        sb.AppendLine("  Maps with no known Xbox conversion still use the same deterministic writer and independent round-trip validation.");
        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine();
            sb.AppendLine($"[{Path.GetFileName(ff.SourceFastFile)}]");
            sb.AppendLine($"  zoneBytes={ff.ZoneBytes:N0}; ffBytes={ff.OutputBytes:N0}; backend={ff.CompressionBackend}; nativeLevel6={ff.NativeZlibLevel6}");
            sb.AppendLine($"  roundTrip={ff.WrapperRoundTripVerified}; goldenAvailable={ff.GoldenReferenceAvailable}; ffExact={ff.GoldenFastFileByteExact}");
            sb.AppendLine($"  zoneSha256={ff.ZoneSha256}");
            sb.AppendLine($"  ffSha256={ff.FastFileSha256}");
            sb.AppendLine($"  fastfile={ff.OutputFastFile}");
        }
        foreach (var warning in r.Warnings) sb.AppendLine("WARNING: " + warning);
        return sb.ToString();
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
