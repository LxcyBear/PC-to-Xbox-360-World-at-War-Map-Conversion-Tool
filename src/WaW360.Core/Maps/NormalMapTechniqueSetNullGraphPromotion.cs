using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4TechniqueSetNullGraphPromotionFastFile(
    string Role,
    string Source,
    int DeclaredTechniqueSets,
    int NativeSmokeTechniqueSets,
    int XboxTechniqueSlotsNulled,
    long XboxPayloadBytes,
    int ReadBackErrors,
    int ProjectedUnhandledAfterTechniqueSet,
    IReadOnlyDictionary<string, int> ProjectedBlockersAfterTechniqueSet,
    int FunctionalShaderGraphsPending,
    bool SmokeTopLevelReady,
    bool StrictHardwareReady,
    string? ProofFile,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapTechniqueSetNullGraphPromotionResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4TechniqueSetNullGraphPromotionFastFile> FastFiles { get; init; }
    public required int DeclaredTechniqueSets { get; init; }
    public required int NativeSmokeTechniqueSets { get; init; }
    public required int XboxTechniqueSlotsNulled { get; init; }
    public required long XboxPayloadBytes { get; init; }
    public required int ReadBackErrors { get; init; }
    public required int ProjectedUnhandledAfterTechniqueSet { get; init; }
    public required IReadOnlyDictionary<string, int> ProjectedBlockersAfterTechniqueSet { get; init; }
    public required int FunctionalShaderGraphsPending { get; init; }
    public required int SmokeTopLevelReadyFastFiles { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5t / alpha 10p: real Xbox-endian top-level TechniqueSet promotion for the smoke path.
/// 220 Tower TechniqueSet XAssets become concrete 216-byte Xbox objects rather than unresolved
/// top-level blockers. Their remappedTechniqueSet pointer and 51 technique graph slots are intentionally null because PC D3D
/// shader/pass graphs cannot be copied as Xbox/Xenos bytecode. This lets the converter move on
/// to Material + the tiny world/foundation tail while retaining an explicit functional-graphics
/// obligation instead of hiding it behind the top-level XAsset blocker count.
/// </summary>
public static class NormalMapTechniqueSetNullGraphPromotion
{
    public static async Task<NormalMapTechniqueSetNullGraphPromotionResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var priorJson = Path.Combine(
            outputRoot, map.Name, "stage5_alpha10o_rawfile_occupied_span_promotion",
            $"{map.Name}_stage5_alpha10o_rawfile_occupied_span_promotion.json");
        if (!File.Exists(priorJson))
            throw new InvalidDataException("Stage 5t10p requires the verified Stage 5s10o RawFile promotion report.");

        var prior = JsonSerializer.Deserialize<NormalMapRawFileOccupiedSpanPromotionResult>(
            await File.ReadAllTextAsync(priorJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5s10o RawFile promotion report.");
        // Alpha 4MP3 diagnostic continuation: RawFile accounting is a per-fastfile obligation.
        // A mismatch in one FF must not prevent TechniqueSet/Material/world analysis on the other
        // FFs.  We keep the RawFile blockers in each projected blocker map and hardware emission
        // remains fail-closed later unless that FF's RawFile scan is independently verified.
        var rawAccountingGloballyVerified = prior.Verified;

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10p_techset_nullgraph_promotion");
        Directory.CreateDirectory(workspace);
        var proofDir = Path.Combine(workspace, "techset_proofs");
        Directory.CreateDirectory(proofDir);

        var rows = new List<T4TechniqueSetNullGraphPromotionFastFile>();
        var aggregate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var detail = new List<string>();
        if (!rawAccountingGloballyVerified)
            detail.Add("DIAGNOSTIC CONTINUATION: one or more fastfiles have unverified RawFile accounting. TechniqueSet promotion continues per fastfile so later blockers can be exposed in the same run; hardware emission stays fail-closed for every affected fastfile.");

        foreach (var ff in prior.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            var scan = T4TechniqueSetNullGraphSerializer.Build(cached.Document);

            var projected = ff.ProjectedBlockersAfterRawFile
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            var techBlocked = projected.TryGetValue("techset", out var tb) ? tb : 0;
            if (techBlocked != 0 && scan.Verified && scan.DeclaredPhysicalTechniqueSets == techBlocked)
            {
                var remain = techBlocked - scan.NativeSmokeTechniqueSets;
                if (remain <= 0) projected.Remove("techset");
                else projected["techset"] = remain;
            }

            var projectedUnhandled = projected.Values.Sum();
            foreach (var kv in projected)
                aggregate[kv.Key] = aggregate.TryGetValue(kv.Key, out var old) ? old + kv.Value : kv.Value;

            string? proofFile = null;
            if (scan.NativeSmokeTechniqueSets != 0)
            {
                proofFile = Path.Combine(proofDir, Path.GetFileNameWithoutExtension(ff.Source) + ".techset_nullgraph_proof.bin");
                await WriteProofAsync(proofFile, scan, cancellationToken);
            }

            // Every smoke TechniqueSet still lacks a real Xbox technique/pass/shader graph.
            // This no longer blocks top-level XAsset serialization, but it DOES prevent a
            // strict/fidelity hardware-ready claim. A later explicit smoke-emission mode may
            // choose to test these null-graph objects on hardware.
            var graphPending = scan.NativeSmokeTechniqueSets;
            var smokeTopLevelReady = projectedUnhandled == 0;
            rows.Add(new T4TechniqueSetNullGraphPromotionFastFile(
                ff.Role, ff.Source,
                scan.DeclaredPhysicalTechniqueSets,
                scan.NativeSmokeTechniqueSets,
                scan.XboxTechniqueSlotsNulled,
                scan.XboxPayloadBytes,
                scan.ReadBackErrors,
                projectedUnhandled,
                projected.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                graphPending,
                smokeTopLevelReady,
                false,
                proofFile,
                scan.Warnings));

            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: techset={scan.NativeSmokeTechniqueSets:N0}/{scan.DeclaredPhysicalTechniqueSets:N0}; xboxSlotsNulled={scan.XboxTechniqueSlotsNulled:N0}; payloadBytes={scan.XboxPayloadBytes:N0}; readBackErrors={scan.ReadBackErrors:N0}; functionalShaderGraphsPending={graphPending:N0}; projectedUnhandled={projectedUnhandled:N0}; smokeTopLevelReady={smokeTopLevelReady}");
            foreach (var warning in scan.Warnings.Take(12)) detail.Add("  TECHSET: " + warning);
            foreach (var kv in projected.Take(16)) detail.Add($"  REMAIN {kv.Key}: {kv.Value:N0}");
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10p_techset_nullgraph_promotion.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10p_techset_nullgraph_promotion.json");
        var result = new NormalMapTechniqueSetNullGraphPromotionResult
        {
            MapName = map.Name,
            Workspace = workspace,
            FastFiles = rows,
            DeclaredTechniqueSets = rows.Sum(x => x.DeclaredTechniqueSets),
            NativeSmokeTechniqueSets = rows.Sum(x => x.NativeSmokeTechniqueSets),
            XboxTechniqueSlotsNulled = rows.Sum(x => x.XboxTechniqueSlotsNulled),
            XboxPayloadBytes = rows.Sum(x => x.XboxPayloadBytes),
            ReadBackErrors = rows.Sum(x => x.ReadBackErrors),
            ProjectedUnhandledAfterTechniqueSet = rows.Sum(x => x.ProjectedUnhandledAfterTechniqueSet),
            ProjectedBlockersAfterTechniqueSet = aggregate
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            FunctionalShaderGraphsPending = rows.Sum(x => x.FunctionalShaderGraphsPending),
            SmokeTopLevelReadyFastFiles = rows.Count(x => x.SmokeTopLevelReady),
            Verified = rows.All(x => x.DeclaredTechniqueSets == x.NativeSmokeTechniqueSets) && rows.All(x => x.ReadBackErrors == 0),
            HardwareReady = false,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static async Task WriteProofAsync(string path, T4TechniqueSetNullGraphScan scan, CancellationToken cancellationToken)
    {
        await using var fs = File.Create(path);
        await fs.WriteAsync("W360TSPP"u8.ToArray().AsMemory(), cancellationToken);
        var ordered = scan.AssetsByIndex.Values.OrderBy(x => x.AssetIndex).ToList();
        var h = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(4, 4), checked((uint)ordered.Count));
        await fs.WriteAsync(h.AsMemory(), cancellationToken);
        foreach (var asset in ordered)
        {
            var row = new byte[12];
            BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(0, 4), checked((uint)asset.AssetIndex));
            BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(4, 4), checked((uint)asset.XboxTechniqueSlots));
            BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(8, 4), checked((uint)asset.XboxPayload.Length));
            await fs.WriteAsync(row.AsMemory(), cancellationToken);
            await fs.WriteAsync(asset.XboxPayload.AsMemory(), cancellationToken);
        }
    }

    private static string Render(NormalMapTechniqueSetNullGraphPromotionResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5t alpha 10p TechniqueSet Xbox null-graph promotion");
        sb.AppendLine(new string('=', 94));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Physical TechniqueSets promoted to Xbox payloads: {r.NativeSmokeTechniqueSets:N0}/{r.DeclaredTechniqueSets:N0}");
        sb.AppendLine($"Xbox technique slots deliberately nulled: {r.XboxTechniqueSlotsNulled:N0}");
        sb.AppendLine($"Xbox TechniqueSet payload bytes: {r.XboxPayloadBytes:N0}");
        sb.AppendLine($"Independent TechniqueSet read-back errors: {r.ReadBackErrors:N0}");
        sb.AppendLine($"Projected top-level blockers after TechniqueSet promotion: {r.ProjectedUnhandledAfterTechniqueSet:N0}");
        sb.AppendLine($"Functional shader graphs still pending: {r.FunctionalShaderGraphsPending:N0}");
        sb.AppendLine($"Verified native top-level accounting: {r.Verified}");
        sb.AppendLine();
        sb.AppendLine("PROJECTED TOP-LEVEL BLOCKERS AFTER TECHNIQUESET PROMOTION");
        foreach (var kv in r.ProjectedBlockersAfterTechniqueSet) sb.AppendLine($"  {kv.Key,-20} {kv.Value,8:N0}");
        if (r.ProjectedBlockersAfterTechniqueSet.Count == 0) sb.AppendLine("  (none)");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: 10p is a real Xbox-endian top-level TechniqueSet payload promotion, not a scanner-only diagnostic. For the first hardware-smoke path its 51 technique graph pointers are null; PC D3D shader/pass bytecode is never copied as Xbox/Xenos shader code. Therefore 10p can remove TechniqueSet from the top-level XAsset blocker list while strict visual-fidelity HardwareReady remains false until Xbox technique graphs/donors are supplied. The next stage can now concentrate on Material + the tiny remaining world/foundation tail.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
