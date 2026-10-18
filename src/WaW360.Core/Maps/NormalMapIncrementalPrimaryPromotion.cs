using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4IncrementalPrimaryPromotionFastFile(
    string Role,
    string Source,
    int AssetCount,
    int FoundationAssetsPromotable,
    int DeclaredPhysicalMaterial,
    int ConvertedMaterialTemplates,
    int MaterialPointersResolved,
    int DeclaredPhysicalTechniqueSet,
    int ConvertedTechniqueSets,
    int TechniqueGraphSlotsPending,
    int DroppedPcTechniqueSlotsNonNull,
    IReadOnlyList<int> PlannedMaterialAssetIndices,
    IReadOnlyList<int> PlannedTechniqueSetAssetIndices,
    int ProjectedUnhandledNonNullAssets,
    IReadOnlyDictionary<string, int> ProjectedBlockersByType,
    int ScriptStringAliasesPending,
    int PackedSharedTargetsPending,
    bool NestedPointerRebasePending,
    bool ProjectedHardwareReady,
    string Status);

public sealed class NormalMapIncrementalPrimaryPromotionResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4IncrementalPrimaryPromotionFastFile> FastFiles { get; init; }
    public required int FoundationAssetsPromotable { get; init; }
    public required int MaterialAssetsPlanned { get; init; }
    public required int TechniqueSetAssetsPlanned { get; init; }
    public required int TechniqueGraphSlotsPending { get; init; }
    public required int ProjectedUnhandledNonNullAssets { get; init; }
    public required IReadOnlyDictionary<string, int> ProjectedBlockersByType { get; init; }
    public required int ProjectedHardwareReadyFastFiles { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5q / alpha 10m: removes alpha 10j's "Material/Techset-only candidate" blind spot.
///
/// Every unresolved fastfile is inspected, including the primary map.ff.  The stage re-runs
/// the fail-closed foundation serializers, then evaluates Material/TechniqueSet payloads even
/// when Sound/world/etc. blockers are still present.  Material -> TechniqueSet packed pointers
/// are accepted only when the source TEMP coordinate maps to exactly one discovered top-level
/// TechniqueSet whose Xbox fixed payload is itself safe.
///
/// 10l intentionally emits a promotion MANIFEST rather than a hardware .ff for mixed primary
/// zones.  This is the safe bridge between the existing native writer and the next typed asset
/// family: no unresolved asset is silently nulled and no PC D3D technique/pass/shader graph is
/// copied into an Xbox candidate.  Existing 10j hardware candidates remain untouched.
/// </summary>
public static class NormalMapIncrementalPrimaryPromotion
{
    private static readonly HashSet<string> TrackedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "xanim", "xmodel", "weapon", "fx"
    };

    public static async Task<NormalMapIncrementalPrimaryPromotionResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var readinessJson = Path.Combine(
            outputRoot, map.Name, "stage5_alpha10e_readiness",
            $"{map.Name}_stage5_alpha10e_readiness.json");
        if (!File.Exists(readinessJson))
            throw new InvalidDataException("Stage 5q10m requires the verified Stage 5j10e readiness inventory.");

        var gate = JsonSerializer.Deserialize<NormalMapFoundationReadinessInventoryResult>(
            await File.ReadAllTextAsync(readinessJson, cancellationToken), JsonOptions())
            ?? throw new InvalidDataException("Could not read Stage 5j10e readiness inventory.");
        if (!gate.Verified)
            throw new InvalidDataException("Stage 5j10e accounting is not verified; alpha 10m incremental promotion refused.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10m_incremental_primary_promotion");
        Directory.CreateDirectory(workspace);

        var rows = new List<T4IncrementalPrimaryPromotionFastFile>();
        var detail = new List<string>();
        var aggregateBlockers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var ff in gate.FastFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Source, cancellationToken);
            var doc = cached.Document;
            var simple = T4SimpleNativeAssetSerializer.Build(doc, cached.Bytes);

            var nonNullMaterials = doc.Assets
                .Where(a => a.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase) &&
                            a.ReferenceKind != T4AssetReferenceKind.Null)
                .ToList();
            var physicalMaterials = nonNullMaterials.Where(IsPhysical).ToList();
            var nonNullTechsets = doc.Assets
                .Where(a => a.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase) &&
                            a.ReferenceKind != T4AssetReferenceKind.Null)
                .ToList();
            var physicalTechsets = nonNullTechsets.Where(IsPhysical).ToList();

            T4MaterialTechniqueLoadScan? mt = null;
            string? scanFailure = null;
            if (nonNullMaterials.Count != 0 || nonNullTechsets.Count != 0)
            {
                try
                {
                    mt = T4MaterialTechniqueLoadSerializer.Build(doc, cached.Bytes, cancellationToken);
                }
                catch (Exception ex) when (ex is InvalidDataException or OverflowException)
                {
                    scanFailure = ex.Message;
                }
            }

            var safeTechsets = new HashSet<int>();
            var safeMaterials = new HashSet<int>();
            var graphPending = 0;
            var droppedPending = 0;
            var convertedMaterials = 0;
            var convertedTechsets = 0;
            var pointersResolved = 0;

            if (mt is not null)
            {
                convertedMaterials = mt.MaterialsByIndex.Count;
                convertedTechsets = mt.TechniqueSetsByIndex.Values.Count(x => x.XboxPayload is not null);
                graphPending = mt.TechniqueSetsByIndex.Values.Sum(x => x.NonNullConsoleTechniqueSlots);
                droppedPending = mt.TechniqueSetsByIndex.Values.Sum(x => x.NonNullDroppedPcSlots);

                foreach (var tech in mt.TechniqueSetsByIndex.Values)
                {
                    if (tech.XboxPayload is not null)
                        safeTechsets.Add(tech.AssetIndex);
                }

                // Packed TEMP pointers use TEMP logical offset 0 at serialized file offset 0x24.
                // Build an exact offset -> top-level TechniqueSet map and reject ambiguity.
                var techByTempOffset = mt.TechniqueSetsByIndex.Values
                    .Where(t => t.SourceOffset >= FastFileConstants.T4XFileHeaderSize)
                    .Select(t => new
                    {
                        Tech = t,
                        Offset = checked((uint)(t.SourceOffset - FastFileConstants.T4XFileHeaderSize))
                    })
                    .Where(x => x.Offset < doc.Header.TempBlockSize)
                    .GroupBy(x => x.Offset)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Tech).ToList());

                foreach (var material in mt.MaterialsByIndex.Values)
                {
                    if (material.SourceTechniquePointer == 0)
                    {
                        safeMaterials.Add(material.AssetIndex);
                        pointersResolved++;
                        continue;
                    }

                    var decoded = T4ZonePointer.Decode(material.SourceTechniquePointer);
                    if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block != T4ZoneBlock.Temp)
                        continue;
                    if (!techByTempOffset.TryGetValue(decoded.Offset, out var targets) || targets.Count != 1)
                        continue;
                    if (!safeTechsets.Contains(targets[0].AssetIndex))
                        continue;

                    safeMaterials.Add(material.AssetIndex);
                    pointersResolved++;
                }
            }

            var projectedBlockers = doc.Assets
                .Where(a => a.ReferenceKind != T4AssetReferenceKind.Null)
                .Where(a => !TrackedTypes.Contains(a.TypeName))
                .Where(a => !simple.AssetsByIndex.ContainsKey(a.Index))
                .Where(a => !safeMaterials.Contains(a.Index))
                .Where(a => !safeTechsets.Contains(a.Index))
                .GroupBy(a => a.TypeName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var kv in projectedBlockers)
                aggregateBlockers[kv.Key] = aggregateBlockers.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;

            var projectedUnhandled = projectedBlockers.Values.Sum();
            var projectedReady = projectedUnhandled == 0 &&
                                 ff.ScriptStringAliasesPending == 0 &&
                                 ff.PackedSharedTargetsPending == 0 &&
                                 !ff.NestedPointerRebasePending;

            string status;
            if (ff.HardwareReady)
                status = "already hardware-ready in Stage 5k10e; preserved";
            else if (scanFailure is not null)
                status = "primary promotion scan blocked: " + scanFailure;
            else if (projectedReady)
                status = "all top-level asset families covered by current serializers; shared/nested gate is clear";
            else
                status = "incremental plan built; remaining typed serializers listed in ProjectedBlockersByType";

            rows.Add(new T4IncrementalPrimaryPromotionFastFile(
                ff.Role,
                ff.Source,
                doc.AssetCount,
                simple.AssetsByIndex.Count,
                physicalMaterials.Count,
                convertedMaterials,
                pointersResolved,
                physicalTechsets.Count,
                convertedTechsets,
                graphPending,
                droppedPending,
                safeMaterials.OrderBy(x => x).ToList(),
                safeTechsets.OrderBy(x => x).ToList(),
                projectedUnhandled,
                projectedBlockers,
                ff.ScriptStringAliasesPending,
                ff.PackedSharedTargetsPending,
                ff.NestedPointerRebasePending,
                projectedReady,
                status));

            detail.Add($"[{ff.Role}] {Path.GetFileName(ff.Source)}: foundation={simple.AssetsByIndex.Count:N0}; " +
                       $"materialTemplates={convertedMaterials:N0}/{physicalMaterials.Count:N0}; materialPointersResolved={pointersResolved:N0}; " +
                       $"techsetSafe={safeTechsets.Count:N0}/{physicalTechsets.Count:N0}; graphSlots={graphPending:N0}; " +
                       $"projectedUnhandled={projectedUnhandled:N0}; sharedPending={ff.PackedSharedTargetsPending:N0}; " +
                       $"nestedPending={ff.NestedPointerRebasePending}; projectedReady={projectedReady}");
            if (scanFailure is not null)
                detail.Add("  BLOCK Material/TechniqueSet scan: " + scanFailure);
            if (mt is not null)
            {
                foreach (var warning in mt.Warnings.Take(16))
                    detail.Add("  MT: " + warning);
            }
            foreach (var warning in simple.Warnings.Take(12))
                detail.Add("  FOUNDATION: " + warning);
            foreach (var kv in projectedBlockers.Take(16))
                detail.Add($"  REMAIN {kv.Key}: {kv.Value:N0}");
        }

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10m_incremental_primary_promotion.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10m_incremental_primary_promotion.json");
        var result = new NormalMapIncrementalPrimaryPromotionResult
        {
            MapName = map.Name,
            Workspace = workspace,
            FastFiles = rows,
            FoundationAssetsPromotable = rows.Sum(x => x.FoundationAssetsPromotable),
            MaterialAssetsPlanned = rows.Sum(x => x.PlannedMaterialAssetIndices.Count),
            TechniqueSetAssetsPlanned = rows.Sum(x => x.PlannedTechniqueSetAssetIndices.Count),
            TechniqueGraphSlotsPending = rows.Sum(x => x.TechniqueGraphSlotsPending),
            ProjectedUnhandledNonNullAssets = rows.Sum(x => x.ProjectedUnhandledNonNullAssets),
            ProjectedBlockersByType = aggregateBlockers
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            ProjectedHardwareReadyFastFiles = rows.Count(x => x.ProjectedHardwareReady),
            Verified = true,
            HardwareReady = rows.Count != 0 && rows.All(x => x.ProjectedHardwareReady),
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, detail), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, PrettyJson()), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static bool IsPhysical(T4AssetEntry asset) =>
        asset.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared;

    private static string Render(NormalMapIncrementalPrimaryPromotionResult r, IReadOnlyList<string> detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5q alpha 10m incremental primary-FF promotion plan");
        sb.AppendLine(new string('=', 92));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Foundation assets covered after 10m packed-Localize chain proof: {r.FoundationAssetsPromotable:N0}");
        sb.AppendLine($"Material assets with safe pointer plan: {r.MaterialAssetsPlanned:N0}");
        sb.AppendLine($"TechniqueSet assets with Xbox-safe fixed payload: {r.TechniqueSetAssetsPlanned:N0}");
        sb.AppendLine($"Technique graph slots still pending: {r.TechniqueGraphSlotsPending:N0}");
        sb.AppendLine($"Projected non-null blockers after incremental promotion: {r.ProjectedUnhandledNonNullAssets:N0}");
        sb.AppendLine($"Projected hardware-ready FFs: {r.ProjectedHardwareReadyFastFiles:N0}/{r.FastFiles.Count:N0}");
        sb.AppendLine($"Verified fail-closed accounting: {r.Verified}");
        sb.AppendLine();
        sb.AppendLine("PROJECTED BLOCKERS AFTER CURRENT SAFE PROMOTIONS");
        foreach (var kv in r.ProjectedBlockersByType)
            sb.AppendLine($"  {kv.Key,-20} {kv.Value,8:N0}");
        if (r.ProjectedBlockersByType.Count == 0) sb.AppendLine("  (none)");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: 10m preserves 10k's mixed-FF promotion model and no longer requires a Material/Techset-only fastfile before inspecting/promoting its safe families. Mixed primary map.ff files are included. This stage writes a verified promotion manifest only; it does NOT emit a mixed primary hardware .ff until every remaining family and tracked shared/nested pointer obligation is resolved. PC D3D technique/pass/shader graphs are never copied into Xbox output.");
        sb.AppendLine();
        foreach (var line in detail) sb.AppendLine(line);
        return sb.ToString();
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
