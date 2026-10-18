using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4RawFileOccupiedPromotionFastFile(
    string Role,
    string Source,
    int DeclaredRawFiles,
    int StructuralRawFiles,
    int NativePromotableRawFiles,
    int PackedNameRawFilesPending,
    int RejectedOccupiedCandidates,
    int RejectedWeakNameCandidates,
    long SourceBufferBytes,
    long XboxPayloadBytes,
    int ReadBackErrors,
    int ProjectedUnhandledAfterRawFile,
    IReadOnlyDictionary<string, int> ProjectedBlockersAfterRawFile,
    bool ProjectedHardwareReady,
    string? ProofFile,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapRawFileOccupiedSpanPromotionResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4RawFileOccupiedPromotionFastFile> FastFiles { get; init; }
    public required int DeclaredRawFiles { get; init; }
    public required int StructuralRawFiles { get; init; }
    public required int NativePromotableRawFiles { get; init; }
    public required int PackedNameRawFilesPending { get; init; }
    public required int RejectedOccupiedCandidates { get; init; }
    public required int RejectedWeakNameCandidates { get; init; }
    public required long SourceBufferBytes { get; init; }
    public required long XboxPayloadBytes { get; init; }
    public required int ReadBackErrors { get; init; }
    public required int ProjectedUnhandledAfterRawFile { get; init; }
    public required IReadOnlyDictionary<string, int> ProjectedBlockersAfterRawFile { get; init; }
    public required int ProjectedHardwareReadyFastFiles { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5s / alpha 10o: subtract byte-verified Stage-4 + 10n Sound ownership from
/// the RawFile signature space, recover the exact remaining RawFile bodies, and emit
/// independently-readable Xbox-endian proof payloads for the subset whose names are
/// owned inline. Packed/shared names stay blocked and are never guessed.
/// </summary>
public static class NormalMapRawFileOccupiedSpanPromotion
{
    private static readonly string[] TrackedTypes = ["xanim", "xmodel", "weapon", "fx"];

    public static async Task<NormalMapRawFileOccupiedSpanPromotionResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var priorJson = Path.Combine(
            outputRoot, map.Name, "stage5_alpha10n_sound_smoke_promotion",
            $"{map.Name}_stage5_alpha10n_sound_smoke_promotion.json");
        if (!File.Exists(priorJson))
            throw new InvalidDataException("Stage 5s10o requires the verified Stage 5r10n Sound smoke report.");

        var prior = JsonSerializer.Deserialize<NormalMapSoundSmokePromotionResult>(
            await File.ReadAllTextAsync(priorJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5r10n Sound smoke report.");
        if (!prior.Verified)
            throw new InvalidDataException("Stage 5r10n Sound structural accounting is not verified; alpha 10o RawFile proof refused.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10o_rawfile_occupied_span_promotion");
        Directory.CreateDirectory(workspace);
        var proofDir = Path.Combine(workspace, "rawfile_proofs");
        Directory.CreateDirectory(proofDir);

        var rows = new List<T4RawFileOccupiedPromotionFastFile>();
        var aggregate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var detail = new List<string>();

        foreach (var ff in prior.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            var doc = cached.Document;

            var occupied = await BuildOccupiedSpansAsync(
                outputRoot, map.Name, ff.Source, ff.Role, doc, cached.Bytes, cancellationToken);
            var scan = T4RawFileOccupiedSpanSerializer.Build(doc, cached.Bytes, occupied, cancellationToken);

            var projected = ff.ProjectedBlockersAfterSoundSmoke
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            var rawBlocked = projected.TryGetValue("rawfile", out var rb) ? rb : 0;
            if (rawBlocked != 0 && scan.Verified && scan.DeclaredPhysicalRawFiles == rawBlocked)
            {
                var remain = rawBlocked - scan.NativePromotableRawFiles;
                if (remain <= 0) projected.Remove("rawfile");
                else projected["rawfile"] = remain;
            }

            var projectedUnhandled = projected.Values.Sum();
            foreach (var kv in projected)
                aggregate[kv.Key] = aggregate.TryGetValue(kv.Key, out var old) ? old + kv.Value : kv.Value;

            // 10n/10o remain proof/planning stages. Even if blockers hit zero, Sound is still a
            // silent structural smoke payload and packed/shared obligations must pass the final native writer.
            var projectedReady = projectedUnhandled == 0 && ff.ProjectedHardwareReady;

            string? proofFile = null;
            if (scan.NativePromotableRawFiles != 0)
            {
                proofFile = Path.Combine(proofDir, Path.GetFileNameWithoutExtension(ff.Source) + ".rawfile_occupied_proof.bin");
                await WriteProofAsync(proofFile, scan, cancellationToken);
            }

            rows.Add(new T4RawFileOccupiedPromotionFastFile(
                ff.Role, ff.Source,
                scan.DeclaredPhysicalRawFiles,
                scan.StructuralRawFiles,
                scan.NativePromotableRawFiles,
                scan.PackedNameRawFilesPending,
                scan.RejectedOccupiedCandidates,
                scan.RejectedWeakNameCandidates,
                scan.SourceBufferBytes,
                scan.XboxPayloadBytes,
                scan.ReadBackErrors,
                projectedUnhandled,
                projected.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                projectedReady,
                proofFile,
                scan.Warnings));

            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: rawStructural={scan.StructuralRawFiles:N0}/{scan.DeclaredPhysicalRawFiles:N0}; nativePromotable={scan.NativePromotableRawFiles:N0}; packedNamePending={scan.PackedNameRawFilesPending:N0}; rejectedOccupied={scan.RejectedOccupiedCandidates:N0}; rejectedWeakName={scan.RejectedWeakNameCandidates:N0}; sourceBufferBytes={scan.SourceBufferBytes:N0}; xboxPayloadBytes={scan.XboxPayloadBytes:N0}; readBackErrors={scan.ReadBackErrors:N0}; projectedUnhandled={projectedUnhandled:N0}");
            foreach (var warning in scan.Warnings.Take(16)) detail.Add("  RAW: " + warning);
            foreach (var asset in scan.AssetsByIndex.Values.Where(x => x.SourceNamePacked).Take(4))
                detail.Add($"  RAW PACKED-NAME XAsset#{asset.AssetIndex}: source=0x{asset.SourceOffset:X8}..0x{asset.SourceEndOffset:X8}; name={T4ZonePointer.Describe(asset.SourceNamePointer)}; length={asset.Length:N0}; nativePromotable={asset.NativePromotable}");
            foreach (var kv in projected.Take(16)) detail.Add($"  REMAIN {kv.Key}: {kv.Value:N0}");
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10o_rawfile_occupied_span_promotion.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10o_rawfile_occupied_span_promotion.json");
        var result = new NormalMapRawFileOccupiedSpanPromotionResult
        {
            MapName = map.Name,
            Workspace = workspace,
            FastFiles = rows,
            DeclaredRawFiles = rows.Sum(x => x.DeclaredRawFiles),
            StructuralRawFiles = rows.Sum(x => x.StructuralRawFiles),
            NativePromotableRawFiles = rows.Sum(x => x.NativePromotableRawFiles),
            PackedNameRawFilesPending = rows.Sum(x => x.PackedNameRawFilesPending),
            RejectedOccupiedCandidates = rows.Sum(x => x.RejectedOccupiedCandidates),
            RejectedWeakNameCandidates = rows.Sum(x => x.RejectedWeakNameCandidates),
            SourceBufferBytes = rows.Sum(x => x.SourceBufferBytes),
            XboxPayloadBytes = rows.Sum(x => x.XboxPayloadBytes),
            ReadBackErrors = rows.Sum(x => x.ReadBackErrors),
            ProjectedUnhandledAfterRawFile = rows.Sum(x => x.ProjectedUnhandledAfterRawFile),
            ProjectedBlockersAfterRawFile = aggregate
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            ProjectedHardwareReadyFastFiles = rows.Count(x => x.ProjectedHardwareReady),
            Verified = rows.All(x => x.DeclaredRawFiles == 0 || x.StructuralRawFiles == x.DeclaredRawFiles) && rows.All(x => x.ReadBackErrors == 0),
            HardwareReady = false,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static async Task<List<(long Start, long End, string Owner)>> BuildOccupiedSpansAsync(
        string outputRoot,
        string mapName,
        string sourcePath,
        string role,
        T4ZoneDocument doc,
        byte[] zone,
        CancellationToken cancellationToken)
    {
        var spans = new List<(long Start, long End, string Owner)>();
        foreach (var type in TrackedTypes)
        {
            var needed = doc.Assets.Count(a => a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                                               a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared);
            if (needed == 0) continue;
            var path = GetProofPath(outputRoot, mapName, sourcePath, role, type);
            var proof = await T4NativeProofContainerReader.IndexAsync(path, type, cancellationToken);
            if (proof.Records.Count != needed)
                throw new InvalidDataException($"10o occupied-span proof mismatch for {Path.GetFileName(sourcePath)} {type}: expected {needed:N0}, proof={proof.Records.Count:N0}.");
            spans.AddRange(proof.Records.Select(r => (r.SourceOffset, r.SourceOffset + (long)r.BlobLength, $"{type} proof")));
        }

        var sound = T4SoundAliasSmokeSerializer.Build(doc, zone, cancellationToken);
        if (sound.DeclaredPhysicalSoundAssets != 0 && !sound.Verified)
            throw new InvalidDataException($"10o requires verified 10n Sound source spans for {Path.GetFileName(sourcePath)}.");
        spans.AddRange(sound.AssetsByIndex.Values.Select(s => (s.SourceOffset, s.SourceEndOffset, "sound proof")));

        return spans.OrderBy(x => x.Item1).Select(x => (x.Item1, x.Item2, x.Item3)).ToList();
    }

    private static string GetProofPath(string outputRoot, string mapName, string sourcePath, string role, string type)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath) + $"_{SanitizeRole(role)}";
        return type.ToLowerInvariant() switch
        {
            "xanim" => Path.Combine(outputRoot, mapName, "stage4_alpha4_xanim", stem + ".xbox_xanim_proof.bin"),
            "xmodel" => Path.Combine(outputRoot, mapName, "stage4_alpha9g_xmodel", stem + ".xbox_xmodel_proof.bin"),
            "weapon" => Path.Combine(outputRoot, mapName, "stage4_alpha7b_weapon_owned", stem + ".xbox_weapon_owned_proof.bin"),
            "fx" => Path.Combine(outputRoot, mapName, "stage4_alpha9b_fx", stem + ".xbox_fx_proof.bin"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private static string SanitizeRole(string role)
    {
        var chars = role.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars).Trim('_').ToLowerInvariant();
    }

    private static async Task WriteProofAsync(string path, T4RawFileOccupiedSpanScan scan, CancellationToken cancellationToken)
    {
        await using var fs = File.Create(path);
        await fs.WriteAsync("W360RAWO"u8.ToArray().AsMemory(), cancellationToken);
        var rows = scan.AssetsByIndex.Values.Where(x => x.NativePromotable && x.XboxPayload is not null).OrderBy(x => x.AssetIndex).ToList();
        var h = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(4, 4), checked((uint)rows.Count));
        await fs.WriteAsync(h.AsMemory(), cancellationToken);
        foreach (var row in rows)
        {
            var payload = row.XboxPayload!;
            var b = new byte[16];
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0, 4), checked((uint)row.AssetIndex));
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4, 4), checked((uint)row.SourceOffset));
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8, 4), checked((uint)row.SourceEndOffset));
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12, 4), checked((uint)payload.Length));
            await fs.WriteAsync(b.AsMemory(), cancellationToken);
            await fs.WriteAsync(payload.AsMemory(), cancellationToken);
        }
    }

    private static string Render(NormalMapRawFileOccupiedSpanPromotionResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5s alpha 10o RawFile occupied-span + packed-tail proof");
        sb.AppendLine(new string('=', 94));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Physical RawFile bodies structurally proven: {r.StructuralRawFiles:N0}/{r.DeclaredRawFiles:N0}");
        sb.AppendLine($"RawFiles with Xbox-endian native payload: {r.NativePromotableRawFiles:N0}");
        sb.AppendLine($"Packed-name RawFiles still pending: {r.PackedNameRawFilesPending:N0}");
        sb.AppendLine($"False candidates rejected inside proven owned spans: {r.RejectedOccupiedCandidates:N0}");
        sb.AppendLine($"Weak-name signatures rejected: {r.RejectedWeakNameCandidates:N0}");
        sb.AppendLine($"Source RawFile buffer bytes accounted: {r.SourceBufferBytes:N0}");
        sb.AppendLine($"Xbox-endian RawFile proof bytes: {r.XboxPayloadBytes:N0}");
        sb.AppendLine($"Independent RawFile read-back errors: {r.ReadBackErrors:N0}");
        sb.AppendLine($"Projected non-null blockers after RawFile promotion: {r.ProjectedUnhandledAfterRawFile:N0}");
        sb.AppendLine($"Verified structural accounting: {r.Verified}");
        sb.AppendLine();
        sb.AppendLine("PROJECTED BLOCKERS AFTER BYTE-VERIFIED RAWFILE PROMOTION");
        foreach (var kv in r.ProjectedBlockersAfterRawFile) sb.AppendLine($"  {kv.Key,-20} {kv.Value,8:N0}");
        if (r.ProjectedBlockersAfterRawFile.Count == 0) sb.AppendLine("  (none)");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: 10o does not guess packed/shared RawFile names. Inline-name RawFiles receive Xbox-endian proof payloads and independent read-back; a structurally proven packed-name tail body remains blocked until its source block target is resolved/rebased. 10o still does not emit the mixed primary FF to hardware_candidates.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
