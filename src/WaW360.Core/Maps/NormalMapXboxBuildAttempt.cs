using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4SerializerCoverage(
    string AssetType,
    int Count,
    string Status,
    string Importance,
    int? XboxTypeId,
    string Note);

public sealed record T4FastFileBuildAttempt(
    string Role,
    string Source,
    int AssetCount,
    IReadOnlyList<T4SerializerCoverage> Coverage,
    string? ProofStream,
    int ProofAssets,
    bool ProofVerified,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapXboxBuildAttemptResult
{
    public required string MapName { get; init; }
    public required string Profile { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4FastFileBuildAttempt> FastFiles { get; init; }
    public required IReadOnlyList<string> CriticalBlockers { get; init; }
    public required IReadOnlyList<string> HighBlockers { get; init; }
    public required bool CanEmitXboxMapFastFile { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 4 alpha 2: first real writer pass for NORMAL maps.
///
/// This stage deliberately does not emit a final .ff until every critical asset class used by
/// the selected map has a typed Xbox serializer. It DOES exercise real big-endian serialization
/// for the small asset classes whose payloads are already proven by Stage 2, writes those proof
/// streams into the conversion workspace, and remaps PC XAsset ids to the Xbox table.
/// </summary>
public static class NormalMapXboxBuildAttempt
{
    private static readonly HashSet<string> FoundationReady = new(StringComparer.OrdinalIgnoreCase)
    {
        "rawfile", "localize", "stringtable"
    };

    // These are critical because a custom Zombies level cannot function without them when present.
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "clipmap", "clipmap_pvs", "comworld", "gameworld_sp", "gameworld_mp", "map_ents", "gfxworld",
        "xanim", "xmodel", "weapon", "rawfile", "stringtable"
    };

    private static readonly HashSet<string> High = new(StringComparer.OrdinalIgnoreCase)
    {
        "material", "techset", "image", "sound", "loaded_sound", "fx", "impactfx", "font", "localize",
        "menulist", "menu", "lightdef"
    };

    public static async Task<NormalMapXboxBuildAttemptResult> BuildAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        if (!analysis.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{map.Name}' classified as {analysis.Profile}. Alpha 2 NORMAL writer refuses to apply Normal rules to a mod profile.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage4_alpha2_xbox_build");
        Directory.CreateDirectory(workspace);

        var attempts = new List<T4FastFileBuildAttempt>();
        var criticalBlockers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var highBlockers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ff in analysis.FastFiles.Where(x => x.Readable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var coverage = new List<T4SerializerCoverage>();

            foreach (var kv in ff.AssetTypes.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var type = kv.Key;
                var count = kv.Value;
                var mapped = T4XboxAssetTypeMap.TryGetXboxTypeId(type, out var xboxId);
                var ready = FoundationReady.Contains(type);
                var importance = Critical.Contains(type) ? "CRITICAL" : High.Contains(type) ? "HIGH" : "MEDIUM";
                var status = ready ? "FOUNDATION SERIALIZER" : mapped ? "NEEDS TYPED XBOX SERIALIZER" : "NO XBOX TYPE MAPPING";
                var note = ready
                    ? "Big-endian proof serializer enabled; final zone integration still gated by the full asset graph."
                    : type.Equals("techset", StringComparison.OrdinalIgnoreCase)
                        ? "Xbox has a shifted type id and console shader dependencies; cannot copy the PC payload."
                        : "PC payload must be walked and re-emitted with Xbox block/alignment/endian rules.";

                coverage.Add(new T4SerializerCoverage(type, count, status, importance, mapped ? xboxId : null, note));

                if (!ready)
                {
                    var label = $"{type} ({count:N0}) in {Path.GetFileName(ff.Path)}";
                    if (importance == "CRITICAL") criticalBlockers.Add(label);
                    else if (importance == "HIGH") highBlockers.Add(label);
                }
            }

            string? proofPath = null;
            var proofCount = 0;
            var proofVerified = false;
            var proofWarnings = new List<string>();

            // The conservative payload scanner is byte-oriented and intentionally expensive.
            // Keep the first writer proof to the smaller patch/mod layers; the 54+ MB world FF
            // is blocked by world serializers anyway and gains nothing from a simple-payload scan yet.
            var size = new FileInfo(ff.Path).Length;
            if (ff.AssetTypes.Keys.Any(FoundationReady.Contains) && size <= 20L * 1024 * 1024)
            {
                proofPath = Path.Combine(workspace, Path.GetFileNameWithoutExtension(ff.Path) + $"_{SanitizeRole(ff.Role)}.xbox_serializer_proof.bin");
                try
                {
                    var proof = await T4XboxSerializerFoundation.BuildProofAsync(ff.Path, proofPath, cancellationToken);
                    proofCount = proof.Assets.Count;
                    proofVerified = proof.Verified;
                    proofWarnings.AddRange(proof.Warnings);
                }
                catch (Exception ex)
                {
                    proofWarnings.Add("Serializer proof failed closed: " + ex.Message);
                    proofPath = null;
                }
            }
            else if (size > 20L * 1024 * 1024)
            {
                proofWarnings.Add("Simple-payload proof skipped for this large FF; critical world serializers block final output first.");
            }

            attempts.Add(new T4FastFileBuildAttempt(
                ff.Role, ff.Path, ff.AssetCount, coverage, proofPath, proofCount, proofVerified, proofWarnings));
        }

        // A final map can only be written when there are no missing critical serializers.
        // High blockers are also kept as blockers for Alpha 2 because dropping materials/FX/sound/UI
        // would violate the conversion rules even if the geometry itself could technically load.
        var canEmit = criticalBlockers.Count == 0 && highBlockers.Count == 0 && attempts.Count > 0;

        var txt = Path.Combine(workspace, $"{map.Name}_stage4_alpha2_xbox_build.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage4_alpha2_xbox_build.json");
        var result = new NormalMapXboxBuildAttemptResult
        {
            MapName = map.Name,
            Profile = analysis.Profile,
            Workspace = workspace,
            FastFiles = attempts,
            CriticalBlockers = criticalBlockers.ToList(),
            HighBlockers = highBlockers.ToList(),
            CanEmitXboxMapFastFile = canEmit,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static string Render(NormalMapXboxBuildAttemptResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 2 Xbox build attempt");
        sb.AppendLine("=====================================================");
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Profile: {r.Profile}");
        sb.AppendLine($"Final Xbox map FF emitted: {(r.CanEmitXboxMapFastFile ? "YES" : "NO — fail closed")}");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: .xbox_serializer_proof.bin files are internal serializer proofs, NOT game fastfiles.");
        sb.AppendLine();

        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"[{ff.Role}] {ff.Source}");
            sb.AppendLine($"  assets={ff.AssetCount:N0}; proofAssets={ff.ProofAssets:N0}; proofVerified={ff.ProofVerified}");
            if (!string.IsNullOrWhiteSpace(ff.ProofStream)) sb.AppendLine($"  proof={ff.ProofStream}");
            foreach (var c in ff.Coverage)
                sb.AppendLine($"  {c.AssetType,-20} {c.Count,6:N0}  xboxId={(c.XboxTypeId?.ToString() ?? "-").PadLeft(2)}  {c.Importance,-8} {c.Status}");
            foreach (var w in ff.Warnings) sb.AppendLine("  NOTE: " + w);
            sb.AppendLine();
        }

        sb.AppendLine("CRITICAL SERIALIZER BLOCKERS");
        if (r.CriticalBlockers.Count == 0) sb.AppendLine("  (none)");
        foreach (var b in r.CriticalBlockers) sb.AppendLine("  - " + b);
        sb.AppendLine();
        sb.AppendLine("HIGH SERIALIZER BLOCKERS");
        if (r.HighBlockers.Count == 0) sb.AppendLine("  (none)");
        foreach (var b in r.HighBlockers) sb.AppendLine("  - " + b);
        sb.AppendLine();
        sb.AppendLine("Alpha 2 conclusion");
        sb.AppendLine("  Real Xbox-format serializer primitives are now active for proven simple payloads.");
        sb.AppendLine("  The converter intentionally refuses to create a .ff while any critical/high map asset class lacks a typed Xbox serializer.");
        sb.AppendLine("  This prevents a syntactically plausible but hardware-invalid fastfile from being mistaken for a converted map.");
        return sb.ToString();
    }

    private static string SanitizeRole(string role) =>
        new string(role.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
