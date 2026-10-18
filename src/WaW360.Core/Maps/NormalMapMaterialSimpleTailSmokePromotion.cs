using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4MaterialSimpleTailPromotionFastFile(
    string Role,
    string Source,
    int DeclaredMaterials,
    int PromotedMaterials,
    int PromotedPackedLocalizeFallbacks,
    int PromotedPackedNameRawFiles,
    int PromotedComWorlds,
    int PromotedGameWorldSp,
    long XboxPayloadBytes,
    int ReadBackErrors,
    int ProjectedUnhandledAfterSimpleTail,
    IReadOnlyDictionary<string, int> ProjectedBlockersAfterSimpleTail,
    int StrictNestedSharedTargetsPending,
    bool StrictNestedRebasePending,
    bool SmokeTopLevelReady,
    bool StrictHardwareReady,
    string? ProofFile,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapMaterialSimpleTailSmokePromotionResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4MaterialSimpleTailPromotionFastFile> FastFiles { get; init; }
    public required int DeclaredMaterials { get; init; }
    public required int PromotedMaterials { get; init; }
    public required int PromotedPackedLocalizeFallbacks { get; init; }
    public required int PromotedPackedNameRawFiles { get; init; }
    public required int PromotedComWorlds { get; init; }
    public required int PromotedGameWorldSp { get; init; }
    public required long XboxPayloadBytes { get; init; }
    public required int ReadBackErrors { get; init; }
    public required int ProjectedUnhandledAfterSimpleTail { get; init; }
    public required IReadOnlyDictionary<string, int> ProjectedBlockersAfterSimpleTail { get; init; }
    public required int StrictNestedSharedTargetsPending { get; init; }
    public required bool StrictNestedRebasePending { get; init; }
    public required int SmokeTopLevelReadyFastFiles { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5u / alpha 10q: move the smoke path beyond diagnostics by creating concrete Xbox-endian
/// top-level payloads for Material and the simple remaining tail (packed Localize, packed-name
/// RawFile, ComWorld, GameWorldSp). clipMap_t and GfxWorld remain explicit blockers because their
/// world/collision/render roots are not safely representable as empty fixed shells.
///
/// This stage also carries the older Stage-5 packed/shared+nested obligations into the report.
/// Top-level blocker count reaching zero is NOT sufficient to claim a hardware FF while those
/// obligations remain unresolved.
/// </summary>
public static class NormalMapMaterialSimpleTailSmokePromotion
{
    private static readonly string[] TrackedTypes = ["xanim", "xmodel", "weapon", "fx"];

    public static async Task<NormalMapMaterialSimpleTailSmokePromotionResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var priorJson = Path.Combine(
            outputRoot, map.Name, "stage5_alpha10p_techset_nullgraph_promotion",
            $"{map.Name}_stage5_alpha10p_techset_nullgraph_promotion.json");
        if (!File.Exists(priorJson))
            throw new InvalidDataException("Stage 5u10q requires the verified Stage 5t10p TechniqueSet promotion report.");

        var prior = JsonSerializer.Deserialize<NormalMapTechniqueSetNullGraphPromotionResult>(
            await File.ReadAllTextAsync(priorJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5t10p TechniqueSet promotion report.");
        // Alpha 4MP3 diagnostic continuation: TechniqueSet issues in one FF no longer hide
        // downstream Material/tail blockers in the rest of the map. Per-FF serializers and final
        // hardware emitters still fail closed on their own verification state.
        var techniqueAccountingGloballyVerified = prior.Verified;

        var nativeReadinessJson = Path.Combine(
            outputRoot, map.Name, "stage5_alpha10d_readiness",
            $"{map.Name}_stage5_alpha10d_readiness.json");
        if (!File.Exists(nativeReadinessJson))
            throw new InvalidDataException("Stage 5u10q requires the Stage 5g10d strict readiness report so packed/shared obligations cannot be hidden.");
        var strictReadiness = JsonSerializer.Deserialize<NormalMapNativeReadinessInventoryResult>(
            await File.ReadAllTextAsync(nativeReadinessJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5g10d strict readiness report.");
        if (!strictReadiness.Verified)
            throw new InvalidDataException("Stage 5g10d strict readiness accounting is not verified; alpha 10q refused.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10q_material_simple_tail_promotion");
        Directory.CreateDirectory(workspace);
        var proofDir = Path.Combine(workspace, "material_tail_proofs");
        Directory.CreateDirectory(proofDir);

        var rows = new List<T4MaterialSimpleTailPromotionFastFile>();
        var aggregate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var detail = new List<string>();
        if (!techniqueAccountingGloballyVerified) detail.Add("DIAGNOSTIC CONTINUATION: upstream TechniqueSet accounting is partial/unverified; this stage continues per fastfile but hardware emission remains fail-closed for affected FFs.");

        foreach (var ff in prior.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            var occupied = await BuildOccupiedSpansAsync(
                outputRoot, map.Name, ff.Source, ff.Role, cached.Document, cached.Bytes, cancellationToken);
            var rawScan = T4RawFileOccupiedSpanSerializer.Build(cached.Document, cached.Bytes, occupied, cancellationToken);
            var scan = T4MaterialSimpleTailSmokeSerializer.Build(cached.Document, cached.Bytes, rawScan);

            var projected = ff.ProjectedBlockersAfterTechniqueSet
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            RemovePromoted(projected, "material", scan.PromotedMaterials);
            RemovePromoted(projected, "localize", scan.PromotedPackedLocalizeFallbacks);
            RemovePromoted(projected, "rawfile", scan.PromotedPackedNameRawFiles);
            RemovePromoted(projected, "comworld", scan.PromotedComWorlds);
            RemovePromoted(projected, "gameworld_sp", scan.PromotedGameWorldSp);

            var projectedUnhandled = projected.Values.Sum();
            foreach (var kv in projected)
                aggregate[kv.Key] = aggregate.TryGetValue(kv.Key, out var old) ? old + kv.Value : kv.Value;

            var strictFf = strictReadiness.FastFiles.FirstOrDefault(x =>
                Path.GetFullPath(x.Source).Equals(Path.GetFullPath(ff.Source), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"10q could not match strict-readiness accounting for '{Path.GetFileName(ff.Source)}'.");
            var sharedPending = strictFf.PackedSharedTargetsPending;
            var nestedPending = strictFf.NestedPointerRebasePending;

            string? proofFile = null;
            if (scan.AssetsByIndex.Count != 0)
            {
                proofFile = Path.Combine(proofDir, Path.GetFileNameWithoutExtension(ff.Source) + ".material_simple_tail_proof.bin");
                await WriteProofAsync(proofFile, scan, cancellationToken);
            }

            var smokeTopLevelReady = projectedUnhandled == 0;
            var strictReady = smokeTopLevelReady && sharedPending == 0 && !nestedPending && ff.FunctionalShaderGraphsPending == 0;
            rows.Add(new T4MaterialSimpleTailPromotionFastFile(
                ff.Role, ff.Source,
                scan.DeclaredMaterials,
                scan.PromotedMaterials,
                scan.PromotedPackedLocalizeFallbacks,
                scan.PromotedPackedNameRawFiles,
                scan.PromotedComWorlds,
                scan.PromotedGameWorldSp,
                scan.XboxPayloadBytes,
                scan.ReadBackErrors,
                projectedUnhandled,
                projected.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                sharedPending,
                nestedPending,
                smokeTopLevelReady,
                strictReady,
                proofFile,
                scan.Warnings));

            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: material={scan.PromotedMaterials:N0}/{scan.DeclaredMaterials:N0}; localizeFallback={scan.PromotedPackedLocalizeFallbacks:N0}; rawPackedName={scan.PromotedPackedNameRawFiles:N0}; comworld={scan.PromotedComWorlds:N0}; gameworld_sp={scan.PromotedGameWorldSp:N0}; payloadBytes={scan.XboxPayloadBytes:N0}; readBackErrors={scan.ReadBackErrors:N0}; projectedUnhandled={projectedUnhandled:N0}; sharedPending={sharedPending:N0}; nestedPending={nestedPending}; smokeTopLevelReady={smokeTopLevelReady}; strictReady={strictReady}");
            foreach (var warning in scan.Warnings.Take(16)) detail.Add("  TAIL: " + warning);
            foreach (var asset in scan.AssetsByIndex.Values.OrderBy(x => x.AssetIndex).Take(32))
                detail.Add($"  PROMOTE XAsset#{asset.AssetIndex} {asset.TypeName}: {asset.SmokeName}; {asset.FidelityObligation}");
            foreach (var kv in projected.Take(16)) detail.Add($"  REMAIN {kv.Key}: {kv.Value:N0}");
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10q_material_simple_tail_promotion.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10q_material_simple_tail_promotion.json");
        var result = new NormalMapMaterialSimpleTailSmokePromotionResult
        {
            MapName = map.Name,
            Workspace = workspace,
            FastFiles = rows,
            DeclaredMaterials = rows.Sum(x => x.DeclaredMaterials),
            PromotedMaterials = rows.Sum(x => x.PromotedMaterials),
            PromotedPackedLocalizeFallbacks = rows.Sum(x => x.PromotedPackedLocalizeFallbacks),
            PromotedPackedNameRawFiles = rows.Sum(x => x.PromotedPackedNameRawFiles),
            PromotedComWorlds = rows.Sum(x => x.PromotedComWorlds),
            PromotedGameWorldSp = rows.Sum(x => x.PromotedGameWorldSp),
            XboxPayloadBytes = rows.Sum(x => x.XboxPayloadBytes),
            ReadBackErrors = rows.Sum(x => x.ReadBackErrors),
            ProjectedUnhandledAfterSimpleTail = rows.Sum(x => x.ProjectedUnhandledAfterSimpleTail),
            ProjectedBlockersAfterSimpleTail = aggregate
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            StrictNestedSharedTargetsPending = rows.Sum(x => x.StrictNestedSharedTargetsPending),
            StrictNestedRebasePending = rows.Any(x => x.StrictNestedRebasePending),
            SmokeTopLevelReadyFastFiles = rows.Count(x => x.SmokeTopLevelReady),
            Verified = rows.All(x => x.ReadBackErrors == 0) && rows.All(x => x.PromotedMaterials == x.DeclaredMaterials),
            HardwareReady = rows.All(x => x.StrictHardwareReady),
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static void RemovePromoted(Dictionary<string, int> blockers, string type, int promoted)
    {
        if (promoted <= 0 || !blockers.TryGetValue(type, out var blocked) || blocked <= 0) return;
        var remain = Math.Max(0, blocked - promoted);
        if (remain == 0) blockers.Remove(type);
        else blockers[type] = remain;
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
                throw new InvalidDataException($"10q occupied-span proof mismatch for {Path.GetFileName(sourcePath)} {type}: expected {needed:N0}, proof={proof.Records.Count:N0}.");
            spans.AddRange(proof.Records.Select(r => (r.SourceOffset, r.SourceOffset + (long)r.BlobLength, $"{type} proof")));
        }

        var sound = T4SoundAliasSmokeSerializer.Build(doc, zone, cancellationToken);
        if (sound.DeclaredPhysicalSoundAssets != 0 && !sound.Verified)
            throw new InvalidDataException($"10q requires verified 10n Sound source spans for {Path.GetFileName(sourcePath)}.");
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

    private static async Task WriteProofAsync(string path, T4MaterialSimpleTailSmokeScan scan, CancellationToken cancellationToken)
    {
        await using var fs = File.Create(path);
        await fs.WriteAsync("W360TAIL"u8.ToArray().AsMemory(), cancellationToken);
        var ordered = scan.AssetsByIndex.Values.OrderBy(x => x.AssetIndex).ToList();
        var h = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(4, 4), checked((uint)ordered.Count));
        await fs.WriteAsync(h.AsMemory(), cancellationToken);
        foreach (var asset in ordered)
        {
            var type = Encoding.ASCII.GetBytes(asset.TypeName);
            var row = new byte[12];
            BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(0, 4), checked((uint)asset.AssetIndex));
            BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(4, 4), checked((uint)type.Length));
            BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(8, 4), checked((uint)asset.XboxPayload.Length));
            await fs.WriteAsync(row.AsMemory(), cancellationToken);
            await fs.WriteAsync(type.AsMemory(), cancellationToken);
            await fs.WriteAsync(asset.XboxPayload.AsMemory(), cancellationToken);
        }
    }

    private static string Render(NormalMapMaterialSimpleTailSmokePromotionResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5u alpha 10q Material + simple-tail Xbox smoke promotion");
        sb.AppendLine(new string('=', 98));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Material smoke payloads: {r.PromotedMaterials:N0}/{r.DeclaredMaterials:N0}");
        sb.AppendLine($"Packed Localize fallbacks promoted: {r.PromotedPackedLocalizeFallbacks:N0}");
        sb.AppendLine($"Packed-name RawFile fallbacks promoted: {r.PromotedPackedNameRawFiles:N0}");
        sb.AppendLine($"ComWorld smoke roots promoted: {r.PromotedComWorlds:N0}");
        sb.AppendLine($"GameWorldSp smoke roots promoted: {r.PromotedGameWorldSp:N0}");
        sb.AppendLine($"Xbox payload bytes: {r.XboxPayloadBytes:N0}");
        sb.AppendLine($"Independent read-back errors: {r.ReadBackErrors:N0}");
        sb.AppendLine($"Projected top-level blockers after 10q: {r.ProjectedUnhandledAfterSimpleTail:N0}");
        sb.AppendLine($"Older packed/shared targets still pending: {r.StrictNestedSharedTargetsPending:N0}");
        sb.AppendLine($"Older nested-pointer rebase still pending: {r.StrictNestedRebasePending}");
        sb.AppendLine($"Verified smoke payload accounting: {r.Verified}");
        sb.AppendLine($"Strict HardwareReady: {r.HardwareReady}");
        sb.AppendLine();
        sb.AppendLine("PROJECTED TOP-LEVEL BLOCKERS AFTER 10Q");
        foreach (var kv in r.ProjectedBlockersAfterSimpleTail) sb.AppendLine($"  {kv.Key,-20} {kv.Value,8:N0}");
        if (r.ProjectedBlockersAfterSimpleTail.Count == 0) sb.AppendLine("  (none)");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: 10q is an actual Xbox-endian top-level smoke-payload stage, not a scanner-only diagnostic. It intentionally uses visually/functionally reduced Material/Localize/RawFile/ComWorld/GameWorldSp smoke objects. clipMap_t and GfxWorld remain fail-closed. Even after those two roots are solved, the final hardware emitter MUST resolve/materialize the older packed/shared and nested pointer obligations before claiming a complete native FF.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
