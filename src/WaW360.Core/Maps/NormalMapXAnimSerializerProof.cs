using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4XAnimFastFileProof(
    string Role,
    string Source,
    string ProofFile,
    int DeclaredXAnim,
    int SerializedXAnim,
    int DeltaXAnim,
    long SerializedBytes,
    bool Verified,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapXAnimSerializerProofResult
{
    public required string MapName { get; init; }
    public required string Profile { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4XAnimFastFileProof> FastFiles { get; init; }
    public required int DeclaredXAnim { get; init; }
    public required int SerializedXAnim { get; init; }
    public required int DeltaXAnim { get; init; }
    public required long SerializedBytes { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 4 alpha 9g: retain the proven alpha-4 XAnim serializer and add a
/// fail-closed packed-name top-level bridge for the few authoritative XAnim slots
/// whose name strings are reused through packed zone pointers.
/// Every top-level XAnim in every NORMAL-map fastfile that declares XAnim assets is
/// parsed from the PC serialization, re-emitted in Xbox big-endian form, and then
/// parsed back independently. Proof containers are internal artifacts, never game FFs.
/// </summary>
public static class NormalMapXAnimSerializerProof
{
    public static async Task<NormalMapXAnimSerializerProofResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        if (!analysis.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{map.Name}' classified as {analysis.Profile}. Alpha 9g NORMAL XAnim proof refuses UGX/ZCT profile rules.");

        var candidates = analysis.FastFiles
            .Where(ff => ff.Readable && ff.AssetTypes.TryGetValue("xanim", out var count) && count > 0)
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidDataException($"'{map.Name}' has no readable fastfiles declaring XAnim assets.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage4_alpha4_xanim");
        Directory.CreateDirectory(workspace);

        var proofs = new List<T4XAnimFastFileProof>();
        var warnings = new List<string>();

        foreach (var ff in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stem = Path.GetFileNameWithoutExtension(ff.Path) + $"_{SanitizeRole(ff.Role)}";
            var proofName = stem + ".xbox_xanim_proof.bin";
            var proofPath = Path.Combine(workspace, proofName);
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
                        warnings.Add($"{Path.GetFileName(ff.Path)}: Alpha 9g could not create a complete Weapon-owned XAnim alignment anchor; continuing without it.");
                    }
                }
                catch (Exception ex)
                {
                    try { File.Delete(weaponProof); } catch { }
                    warnings.Add($"{Path.GetFileName(ff.Path)}: Weapon-owned XAnim alignment anchor unavailable ({ex.Message}); continuing without it.");
                }
            }

            var proof = await T4XAnimSerializerProof.BuildProofAsync(
                ff.Path, proofPath, File.Exists(weaponProof) ? weaponProof : null, cancellationToken);
            var serializedBytes = proof.Assets.Sum(x => (long)x.XboxSerializedBytes);
            var deltaCount = proof.Assets.Count(x => x.HasDelta);

            if (!proof.Verified || proof.DeclaredXAnimCount != proof.ResolvedXAnimCount || proof.Assets.Count != proof.DeclaredXAnimCount)
                throw new InvalidDataException($"XAnim proof for '{Path.GetFileName(ff.Path)}' did not cover every declared animation.");

            proofs.Add(new T4XAnimFastFileProof(
                ff.Role,
                ff.Path,
                proof.OutputFile,
                proof.DeclaredXAnimCount,
                proof.Assets.Count,
                deltaCount,
                serializedBytes,
                proof.Verified,
                proof.Warnings));

            warnings.AddRange(proof.Warnings.Select(w => $"{Path.GetFileName(ff.Path)}: {w}"));
        }

        var declared = proofs.Sum(x => x.DeclaredXAnim);
        var serialized = proofs.Sum(x => x.SerializedXAnim);
        var deltas = proofs.Sum(x => x.DeltaXAnim);
        var bytes = proofs.Sum(x => x.SerializedBytes);
        var verified = proofs.Count == candidates.Count && proofs.All(x => x.Verified) && declared == serialized;

        if (!verified)
            throw new InvalidDataException($"Stage 4 alpha 9g XAnim proof is incomplete ({serialized:N0}/{declared:N0}).");

        var txt = Path.Combine(workspace, $"{map.Name}_stage4_alpha4_xanim.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage4_alpha4_xanim.json");

        var result = new NormalMapXAnimSerializerProofResult
        {
            MapName = map.Name,
            Profile = analysis.Profile,
            Workspace = workspace,
            FastFiles = proofs,
            DeclaredXAnim = declared,
            SerializedXAnim = serialized,
            DeltaXAnim = deltas,
            SerializedBytes = bytes,
            Verified = true,
            Warnings = warnings,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static string Render(NormalMapXAnimSerializerProofResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 9g XAnim serializer proof");
        sb.AppendLine("=========================================================");
        sb.AppendLine("INTERNAL PROOF ONLY: .xbox_xanim_proof.bin files are NOT game fastfiles.");
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Profile: {r.Profile}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine();

        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"[{ff.Role}] {ff.Source}");
            sb.AppendLine($"  declared XAnim:   {ff.DeclaredXAnim:N0}");
            sb.AppendLine($"  serialized XAnim: {ff.SerializedXAnim:N0}");
            sb.AppendLine($"  delta XAnim:      {ff.DeltaXAnim:N0}");
            sb.AppendLine($"  payload bytes:    {ff.SerializedBytes:N0}");
            sb.AppendLine($"  read-back:        {(ff.Verified ? "PASS" : "FAIL")}");
            sb.AppendLine($"  proof:            {ff.ProofFile}");
            foreach (var warning in ff.Warnings) sb.AppendLine("  NOTE: " + warning);
            sb.AppendLine();
        }

        sb.AppendLine("TOTALS");
        sb.AppendLine($"  declared XAnim:   {r.DeclaredXAnim:N0}");
        sb.AppendLine($"  serialized XAnim: {r.SerializedXAnim:N0}");
        sb.AppendLine($"  delta XAnim:      {r.DeltaXAnim:N0}");
        sb.AppendLine($"  payload bytes:    {r.SerializedBytes:N0}");
        sb.AppendLine();

        if (r.Warnings.Count > 0)
        {
            sb.AppendLine("WARNINGS");
            foreach (var warning in r.Warnings) sb.AppendLine("  - " + warning);
            sb.AppendLine();
        }

        sb.AppendLine("Alpha 9g conclusion");
        sb.AppendLine("  Every declared XAnim in the participating NORMAL-map fastfiles was re-emitted in Xbox big-endian T4 serialization and parsed back to the exact record boundary, including any authoritative packed-name top-level bodies recovered by the alpha 9g bridge.");
        sb.AppendLine("  XAnim can now move from CRITICAL BLOCKER to PROVEN SERIALIZER FOUNDATION for the final zone writer.");
        sb.AppendLine("  No game fastfile was emitted; XModel, Weapon, world/collision and high-priority serializers still gate hardware output.");
        return sb.ToString();
    }

    private static string SanitizeRole(string role) =>
        new string(role.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
