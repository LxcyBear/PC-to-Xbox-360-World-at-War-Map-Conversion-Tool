using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4SoundSmokePromotionFastFile(
    string Role,
    string Source,
    int DeclaredSoundAssets,
    int ParsedSoundAssets,
    int AliasEntries,
    int LoadedSoundNodes,
    int StreamedSoundNodes,
    int PrimedSoundNodes,
    long LoadedAudioBytesSkipped,
    int PackedSoundFilePointers,
    int NullSoundFilePointers,
    int PackedListNamePointers,
    long SilentStubPayloadBytes,
    int ReadBackErrors,
    bool SoundSmokePromotable,
    int ProjectedUnhandledAfterSoundSmoke,
    IReadOnlyDictionary<string, int> ProjectedBlockersAfterSoundSmoke,
    bool ProjectedHardwareReady,
    string? StubProofFile,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapSoundSmokePromotionResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4SoundSmokePromotionFastFile> FastFiles { get; init; }
    public required int DeclaredSoundAssets { get; init; }
    public required int ParsedSoundAssets { get; init; }
    public required int AliasEntries { get; init; }
    public required int LoadedSoundNodes { get; init; }
    public required int StreamedSoundNodes { get; init; }
    public required int PrimedSoundNodes { get; init; }
    public required long LoadedAudioBytesSkipped { get; init; }
    public required long SilentStubPayloadBytes { get; init; }
    public required int ReadBackErrors { get; init; }
    public required int ProjectedUnhandledAfterSoundSmoke { get; init; }
    public required IReadOnlyDictionary<string, int> ProjectedBlockersAfterSoundSmoke { get; init; }
    public required int ProjectedHardwareReadyFastFiles { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5r / alpha 10n: byte-verified T4 Sound ownership proof and Xbox-endian
/// SILENT smoke-stub serialization.  No hardware FF is emitted here.  Sound is
/// removed from the *projected* blocker inventory only when every physical sound
/// list in that FF is structurally parsed and every generated stub passes an
/// independent read-back.  Real PC WAV/ADPCM is not copied into Xbox output.
/// </summary>
public static class NormalMapSoundSmokePromotion
{
    public static async Task<NormalMapSoundSmokePromotionResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var priorJson = Path.Combine(
            outputRoot, map.Name, "stage5_alpha10m_incremental_primary_promotion",
            $"{map.Name}_stage5_alpha10m_incremental_primary_promotion.json");
        if (!File.Exists(priorJson))
            throw new InvalidDataException("Stage 5r10n requires the verified Stage 5q10m incremental-primary report.");

        var prior = JsonSerializer.Deserialize<NormalMapIncrementalPrimaryPromotionResult>(
            await File.ReadAllTextAsync(priorJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5q10m incremental-primary report.");
        if (!prior.Verified)
            throw new InvalidDataException("Stage 5q10m accounting is not verified; alpha 10n Sound proof refused.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10n_sound_smoke_promotion");
        Directory.CreateDirectory(workspace);
        var proofDir = Path.Combine(workspace, "silent_stub_proofs");
        Directory.CreateDirectory(proofDir);

        var rows = new List<T4SoundSmokePromotionFastFile>();
        var aggregate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var detail = new List<string>();

        foreach (var ff in prior.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            var scan = T4SoundAliasSmokeSerializer.Build(cached.Document, cached.Bytes, cancellationToken);

            var projected = ff.ProjectedBlockersByType.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            var blockedSound = projected.TryGetValue("sound", out var snd) ? snd : 0;
            var promotable = blockedSound == 0
                ? scan.DeclaredPhysicalSoundAssets == 0 && scan.Verified
                : scan.Verified && scan.DeclaredPhysicalSoundAssets == blockedSound && scan.ParsedPhysicalSoundAssets == blockedSound;

            if (promotable && blockedSound != 0) projected.Remove("sound");
            var projectedUnhandled = projected.Values.Sum();
            foreach (var kv in projected)
                aggregate[kv.Key] = aggregate.TryGetValue(kv.Key, out var old) ? old + kv.Value : kv.Value;

            var projectedReady = projectedUnhandled == 0 &&
                                 ff.ScriptStringAliasesPending == 0 &&
                                 ff.PackedSharedTargetsPending == 0 &&
                                 !ff.NestedPointerRebasePending;

            string? proofFile = null;
            if (scan.ParsedPhysicalSoundAssets != 0)
            {
                proofFile = Path.Combine(proofDir, Path.GetFileNameWithoutExtension(ff.Source) + ".sound_stub_proof.bin");
                await WriteProofAsync(proofFile, scan, cancellationToken);
            }

            rows.Add(new T4SoundSmokePromotionFastFile(
                ff.Role,
                ff.Source,
                scan.DeclaredPhysicalSoundAssets,
                scan.ParsedPhysicalSoundAssets,
                scan.AliasEntries,
                scan.LoadedSoundNodes,
                scan.StreamedSoundNodes,
                scan.PrimedSoundNodes,
                scan.LoadedAudioBytesSkipped,
                scan.PackedSoundFilePointers,
                scan.NullSoundFilePointers,
                scan.PackedListNamePointers,
                scan.SilentStubPayloadBytes,
                scan.ReadBackErrors,
                promotable,
                projectedUnhandled,
                projected.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                projectedReady,
                proofFile,
                scan.Warnings));

            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: sound={scan.ParsedPhysicalSoundAssets:N0}/{scan.DeclaredPhysicalSoundAssets:N0}; aliases={scan.AliasEntries:N0}; " +
                       $"loaded={scan.LoadedSoundNodes:N0}; streamed={scan.StreamedSoundNodes:N0}; primed={scan.PrimedSoundNodes:N0}; " +
                       $"sourceAudioSkipped={scan.LoadedAudioBytesSkipped:N0}B; packedSoundFile={scan.PackedSoundFilePointers:N0}; " +
                       $"stubBytes={scan.SilentStubPayloadBytes:N0}; readBackErrors={scan.ReadBackErrors:N0}; smokePromotable={promotable}; " +
                       $"projectedUnhandled={projectedUnhandled:N0}; projectedReady={projectedReady}");
            foreach (var warning in scan.Warnings.Take(12)) detail.Add("  SOUND: " + warning);
            foreach (var kv in projected.Take(16)) detail.Add($"  REMAIN {kv.Key}: {kv.Value:N0}");
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10n_sound_smoke_promotion.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10n_sound_smoke_promotion.json");
        var result = new NormalMapSoundSmokePromotionResult
        {
            MapName = map.Name,
            Workspace = workspace,
            FastFiles = rows,
            DeclaredSoundAssets = rows.Sum(x => x.DeclaredSoundAssets),
            ParsedSoundAssets = rows.Sum(x => x.ParsedSoundAssets),
            AliasEntries = rows.Sum(x => x.AliasEntries),
            LoadedSoundNodes = rows.Sum(x => x.LoadedSoundNodes),
            StreamedSoundNodes = rows.Sum(x => x.StreamedSoundNodes),
            PrimedSoundNodes = rows.Sum(x => x.PrimedSoundNodes),
            LoadedAudioBytesSkipped = rows.Sum(x => x.LoadedAudioBytesSkipped),
            SilentStubPayloadBytes = rows.Sum(x => x.SilentStubPayloadBytes),
            ReadBackErrors = rows.Sum(x => x.ReadBackErrors),
            ProjectedUnhandledAfterSoundSmoke = rows.Sum(x => x.ProjectedUnhandledAfterSoundSmoke),
            ProjectedBlockersAfterSoundSmoke = aggregate
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            ProjectedHardwareReadyFastFiles = rows.Count(x => x.ProjectedHardwareReady),
            Verified = rows.All(x => x.ReadBackErrors == 0 && (x.DeclaredSoundAssets == 0 || x.ParsedSoundAssets == x.DeclaredSoundAssets)),
            HardwareReady = false, // proof-only: 10n NEVER promotes a mixed primary FF into hardware_candidates
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static async Task WriteProofAsync(string path, T4SoundAliasSmokeScan scan, CancellationToken cancellationToken)
    {
        await using var fs = File.Create(path);
        await fs.WriteAsync("W360SNDN"u8.ToArray().AsMemory(), cancellationToken);
        var ordered = scan.AssetsByIndex.Values.OrderBy(x => x.AssetIndex).ToList();
        var h = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(0, 4), checked((uint)ordered.Count));
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(4, 4), checked((uint)scan.AliasEntries));
        await fs.WriteAsync(h.AsMemory(), cancellationToken);
        foreach (var asset in ordered)
        {
            var row = new byte[12];
            BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(0, 4), checked((uint)asset.AssetIndex));
            BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(4, 4), checked((uint)asset.AliasCount));
            BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(8, 4), checked((uint)asset.XboxSilentStubPayload.Length));
            await fs.WriteAsync(row.AsMemory(), cancellationToken);
            await fs.WriteAsync(asset.XboxSilentStubPayload.AsMemory(), cancellationToken);
        }
    }

    private static string Render(NormalMapSoundSmokePromotionResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5r alpha 10n Sound structural + silent-smoke serialization proof");
        sb.AppendLine(new string('=', 96));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Physical Sound assets parsed: {r.ParsedSoundAssets:N0}/{r.DeclaredSoundAssets:N0}");
        sb.AppendLine($"snd_alias_t entries traversed: {r.AliasEntries:N0}");
        sb.AppendLine($"Owned source nodes: loaded={r.LoadedSoundNodes:N0}; streamed={r.StreamedSoundNodes:N0}; primed={r.PrimedSoundNodes:N0}");
        sb.AppendLine($"PC loaded-audio bytes structurally skipped (not copied): {r.LoadedAudioBytesSkipped:N0}");
        sb.AppendLine($"Xbox-endian SILENT proof payload bytes: {r.SilentStubPayloadBytes:N0}");
        sb.AppendLine($"Independent silent-stub read-back errors: {r.ReadBackErrors:N0}");
        sb.AppendLine($"Projected non-null blockers after Sound smoke promotion: {r.ProjectedUnhandledAfterSoundSmoke:N0}");
        sb.AppendLine($"Projected hardware-ready FFs: {r.ProjectedHardwareReadyFastFiles:N0}/{r.FastFiles.Count:N0}");
        sb.AppendLine($"Verified structural accounting: {r.Verified}");
        sb.AppendLine();
        sb.AppendLine("PROJECTED BLOCKERS AFTER BYTE-VERIFIED SOUND SMOKE PROMOTION");
        foreach (var kv in r.ProjectedBlockersAfterSoundSmoke) sb.AppendLine($"  {kv.Key,-20} {kv.Value,8:N0}");
        if (r.ProjectedBlockersAfterSoundSmoke.Count == 0) sb.AppendLine("  (none)");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: 10n is a proof stage, not final audio conversion. It walks T4 snd_alias_list_t/snd_alias_t/SoundFile ownership, proves physical Sound run boundaries, discards PC WAV/ADPCM payloads from the candidate path, and builds an Xbox-endian SILENT structural stub with independent read-back. No 10n stub is emitted to hardware_candidates. Full audio later requires Xbox-compatible encoded/streamed sound data.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
