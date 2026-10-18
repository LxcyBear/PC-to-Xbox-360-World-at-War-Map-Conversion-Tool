using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4XModelFastFileProof(
    string Role,
    string Source,
    string ProofFile,
    int DeclaredXModel,
    int SerializedXModel,
    long SerializedBytes,
    int TotalSurfaces,
    int TotalBones,
    int CollisionModels,
    int InlineMaterials,
    int InlineImages,
    int PhysPresetModels,
    int PhysGeomModels,
    int CollmapModels,
    bool Verified,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapXModelSerializerProofResult
{
    public required string MapName { get; init; }
    public required string Profile { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4XModelFastFileProof> FastFiles { get; init; }
    public required int DeclaredXModel { get; init; }
    public required int SerializedXModel { get; init; }
    public required long SerializedBytes { get; init; }
    public required int TotalSurfaces { get; init; }
    public required int TotalBones { get; init; }
    public required int CollisionModels { get; init; }
    public required int InlineMaterials { get; init; }
    public required int InlineImages { get; init; }
    public required int PhysPresetModels { get; init; }
    public required int PhysGeomModels { get; init; }
    public required int CollmapModels { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 4 alpha 9g: prove the XAsset-order-filtered T4 XModel PC -> Xbox big-endian serializer.
/// Every top-level XModel in every participating NORMAL-map fastfile is measured,
/// endian-converted including owned model/material/image/collision/physics payloads,
/// and parsed back independently. Proof containers are never game fastfiles.
/// </summary>
public static class NormalMapXModelSerializerProof
{
    public static async Task<NormalMapXModelSerializerProofResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        if (!analysis.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{map.Name}' classified as {analysis.Profile}. Alpha 9g NORMAL XModel proof refuses UGX/ZCT profile rules.");

        var candidates = analysis.FastFiles
            .Where(ff => ff.Readable && ff.AssetTypes.TryGetValue("xmodel", out var count) && count > 0)
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidDataException($"'{map.Name}' has no readable fastfiles declaring XModel assets.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage4_alpha9g_xmodel");
        Directory.CreateDirectory(workspace);

        var proofs = new List<T4XModelFastFileProof>();
        var warnings = new List<string>();

        foreach (var ff in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stem = Path.GetFileNameWithoutExtension(ff.Path) + $"_{SanitizeRole(ff.Role)}";
            var proofName = stem + ".xbox_xmodel_proof.bin";
            var proofPath = Path.Combine(workspace, proofName);
            var xanimProof = Path.Combine(outputRoot, map.Name, "stage4_alpha4_xanim", stem + ".xbox_xanim_proof.bin");
            var weaponProof = Path.Combine(outputRoot, map.Name, "stage4_alpha7b_weapon_owned", stem + ".xbox_weapon_owned_proof.bin");

            if (!File.Exists(weaponProof) && ff.AssetTypes.TryGetValue("weapon", out var weaponCount) && weaponCount > 0)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(weaponProof)!);
                    var anchor = await T4WeaponVariableSerializerProof.BuildProofAsync(ff.Path, weaponProof, cancellationToken);
                    if (!anchor.Verified || anchor.CoveredWeaponCount != anchor.DeclaredWeaponCount)
                    {
                        try { File.Delete(weaponProof); } catch { }
                        warnings.Add($"{Path.GetFileName(ff.Path)}: Alpha 9g could not create a complete Weapon-owned alignment anchor; continuing with XAnim/FX anchors.");
                    }
                }
                catch (Exception ex)
                {
                    try { File.Delete(weaponProof); } catch { }
                    warnings.Add($"{Path.GetFileName(ff.Path)}: Weapon-owned alignment anchor unavailable ({ex.Message}); continuing with XAnim/FX anchors.");
                }
            }

            var proof = await T4XModelSerializerProof.BuildProofAsync(
                ff.Path,
                proofPath,
                File.Exists(xanimProof) ? xanimProof : null,
                File.Exists(weaponProof) ? weaponProof : null,
                cancellationToken);
            if (!proof.Verified || proof.DeclaredXModelCount != proof.ResolvedXModelCount || proof.Assets.Count != proof.DeclaredXModelCount)
                throw new InvalidDataException($"XModel proof for '{Path.GetFileName(ff.Path)}' did not cover every declared top-level model.");

            var serializedBytes = proof.Assets.Sum(x => (long)x.XboxSerializedBytes);
            var totalSurfaces = proof.Assets.Sum(x => (int)x.SurfaceCount);
            var totalBones = proof.Assets.Sum(x => (int)x.BoneCount);
            var collisionModels = proof.Assets.Count(x => x.CollisionSurfaceCount > 0);
            var inlineMaterials = proof.Assets.Sum(x => x.InlineMaterials);
            var inlineImages = proof.Assets.Sum(x => x.InlineImages);
            var physPresetModels = proof.Assets.Count(x => x.HasPhysPreset);
            var physGeomModels = proof.Assets.Count(x => x.HasPhysGeoms);
            var collmapModels = proof.Assets.Count(x => x.HasCollmap);

            proofs.Add(new T4XModelFastFileProof(
                ff.Role,
                ff.Path,
                proof.OutputFile,
                proof.DeclaredXModelCount,
                proof.Assets.Count,
                serializedBytes,
                totalSurfaces,
                totalBones,
                collisionModels,
                inlineMaterials,
                inlineImages,
                physPresetModels,
                physGeomModels,
                collmapModels,
                proof.Verified,
                proof.Warnings));

            warnings.AddRange(proof.Warnings.Select(w => $"{Path.GetFileName(ff.Path)}: {w}"));
        }

        var declared = proofs.Sum(x => x.DeclaredXModel);
        var serialized = proofs.Sum(x => x.SerializedXModel);
        var bytes = proofs.Sum(x => x.SerializedBytes);
        var verified = proofs.Count == candidates.Count && proofs.All(x => x.Verified) && declared == serialized;
        if (!verified)
            throw new InvalidDataException($"Stage 4 alpha 9g XModel proof is incomplete ({serialized:N0}/{declared:N0}).");

        var txt = Path.Combine(workspace, $"{map.Name}_stage4_alpha9g_xmodel.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage4_alpha9g_xmodel.json");

        var result = new NormalMapXModelSerializerProofResult
        {
            MapName = map.Name,
            Profile = analysis.Profile,
            Workspace = workspace,
            FastFiles = proofs,
            DeclaredXModel = declared,
            SerializedXModel = serialized,
            SerializedBytes = bytes,
            TotalSurfaces = proofs.Sum(x => x.TotalSurfaces),
            TotalBones = proofs.Sum(x => x.TotalBones),
            CollisionModels = proofs.Sum(x => x.CollisionModels),
            InlineMaterials = proofs.Sum(x => x.InlineMaterials),
            InlineImages = proofs.Sum(x => x.InlineImages),
            PhysPresetModels = proofs.Sum(x => x.PhysPresetModels),
            PhysGeomModels = proofs.Sum(x => x.PhysGeomModels),
            CollmapModels = proofs.Sum(x => x.CollmapModels),
            Verified = true,
            Warnings = warnings,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static string Render(NormalMapXModelSerializerProofResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 9g XModel serializer proof");
        sb.AppendLine("==========================================================");
        sb.AppendLine("INTERNAL PROOF ONLY: .xbox_xmodel_proof.bin files are NOT game fastfiles.");
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Profile: {r.Profile}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine();

        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"[{ff.Role}] {ff.Source}");
            sb.AppendLine($"  declared XModel:   {ff.DeclaredXModel:N0}");
            sb.AppendLine($"  serialized XModel: {ff.SerializedXModel:N0}");
            sb.AppendLine($"  payload bytes:      {ff.SerializedBytes:N0}");
            sb.AppendLine($"  total bones:        {ff.TotalBones:N0}");
            sb.AppendLine($"  total surfaces:     {ff.TotalSurfaces:N0}");
            sb.AppendLine($"  collision models:   {ff.CollisionModels:N0}");
            sb.AppendLine($"  inline materials:   {ff.InlineMaterials:N0}");
            sb.AppendLine($"  inline images:      {ff.InlineImages:N0}");
            sb.AppendLine($"  physPreset models:  {ff.PhysPresetModels:N0}");
            sb.AppendLine($"  physGeom models:    {ff.PhysGeomModels:N0}");
            sb.AppendLine($"  collmap models:     {ff.CollmapModels:N0}");
            sb.AppendLine($"  read-back:          {(ff.Verified ? "PASS" : "FAIL")}");
            sb.AppendLine($"  proof:              {ff.ProofFile}");
            foreach (var warning in ff.Warnings) sb.AppendLine("  NOTE: " + warning);
            sb.AppendLine();
        }

        sb.AppendLine("TOTALS");
        sb.AppendLine($"  declared XModel:   {r.DeclaredXModel:N0}");
        sb.AppendLine($"  serialized XModel: {r.SerializedXModel:N0}");
        sb.AppendLine($"  payload bytes:      {r.SerializedBytes:N0}");
        sb.AppendLine($"  total bones:        {r.TotalBones:N0}");
        sb.AppendLine($"  total surfaces:     {r.TotalSurfaces:N0}");
        sb.AppendLine($"  collision models:   {r.CollisionModels:N0}");
        sb.AppendLine($"  inline materials:   {r.InlineMaterials:N0}");
        sb.AppendLine($"  inline images:      {r.InlineImages:N0}");
        sb.AppendLine($"  physPreset models:  {r.PhysPresetModels:N0}");
        sb.AppendLine($"  physGeom models:    {r.PhysGeomModels:N0}");
        sb.AppendLine($"  collmap models:     {r.CollmapModels:N0}");
        sb.AppendLine();

        if (r.Warnings.Count > 0)
        {
            sb.AppendLine("WARNINGS");
            foreach (var warning in r.Warnings) sb.AppendLine("  - " + warning);
            sb.AppendLine();
        }

        sb.AppendLine("Alpha 9g conclusion");
        sb.AppendLine("  The mixed XAsset-order resolver first filters raw typed XModel candidates against authoritative table order; every selected top-level XModel is then re-emitted as Xbox big-endian T4 serialization and parsed back to the exact record boundary.");
        sb.AppendLine("  XModel can move from CRITICAL BLOCKER to PROVEN SERIALIZER FOUNDATION once this proof passes on the target map.");
        sb.AppendLine("  No game fastfile was emitted; Weapon, world/collision integration and remaining high-priority serializers still gate hardware output.");
        return sb.ToString();
    }

    private static string SanitizeRole(string role) =>
        new string(role.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
