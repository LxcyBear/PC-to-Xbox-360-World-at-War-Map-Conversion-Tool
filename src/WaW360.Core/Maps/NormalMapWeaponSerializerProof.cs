using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4WeaponFastFileProof(
    string Role,
    string Source,
    string ProofFile,
    int DeclaredWeapon,
    int SerializedWeapon,
    int AliasWeapon,
    int CoveredWeapon,
    int FixedBodyBytes,
    long SerializedFixedBytes,
    int WeaponsWithVariableOwnership,
    bool Verified,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapWeaponSerializerProofResult
{
    public required string MapName { get; init; }
    public required string Profile { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4WeaponFastFileProof> FastFiles { get; init; }
    public required int DeclaredWeapon { get; init; }
    public required int SerializedWeapon { get; init; }
    public required int AliasWeapon { get; init; }
    public required int CoveredWeapon { get; init; }
    public required long SerializedFixedBytes { get; init; }
    public required int FixedBodyBytes { get; init; }
    public required int WeaponsWithVariableOwnership { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

public static class NormalMapWeaponSerializerProof
{
    public static async Task<NormalMapWeaponSerializerProofResult> RunAsync(MapCandidate map, string outputRoot, CancellationToken cancellationToken = default)
    {
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        if (!analysis.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{map.Name}' classified as {analysis.Profile}. Alpha 6f NORMAL Weapon proof refuses UGX/ZCT profile rules.");

        var candidates = analysis.FastFiles.Where(ff => ff.Readable && ff.AssetTypes.TryGetValue("weapon", out var c) && c > 0).ToList();
        if (candidates.Count == 0) throw new InvalidDataException($"'{map.Name}' has no readable fastfiles declaring WeaponDef assets.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage4_alpha6f_weapon");
        Directory.CreateDirectory(workspace);
        var proofs = new List<T4WeaponFastFileProof>();
        var warnings = new List<string>();

        foreach (var ff in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var proofPath = Path.Combine(workspace, Path.GetFileNameWithoutExtension(ff.Path) + $"_{SanitizeRole(ff.Role)}.xbox_weapon_fixed_proof.bin");
            var proof = await T4WeaponSerializerProof.BuildProofAsync(ff.Path, proofPath, cancellationToken);
            if (!proof.Verified || proof.DeclaredWeaponCount != proof.ResolvedWeaponCount)
                throw new InvalidDataException($"Weapon proof for '{Path.GetFileName(ff.Path)}' did not cover every declared top-level weapon XAsset.");

            var variable = proof.Assets.Count(x => x.HasBounceSoundTable || x.AccuracyGraphKnotCount > 0 || x.HasFirstPersonFlameTable || x.HasThirdPersonFlameTable);
            proofs.Add(new T4WeaponFastFileProof(ff.Role, ff.Path, proof.OutputFile, proof.DeclaredWeaponCount, proof.SerializedBodyCount,
                proof.AliasReferenceCount, proof.ResolvedWeaponCount, proof.InferredFixedSize,
                (long)proof.InferredFixedSize * proof.SerializedBodyCount, variable, proof.Verified, proof.Warnings));
            warnings.AddRange(proof.Warnings.Select(w => $"{Path.GetFileName(ff.Path)}: {w}"));
        }

        var declared = proofs.Sum(x => x.DeclaredWeapon);
        var serialized = proofs.Sum(x => x.SerializedWeapon);
        var aliases = proofs.Sum(x => x.AliasWeapon);
        var covered = proofs.Sum(x => x.CoveredWeapon);
        if (declared != covered || proofs.Any(x => !x.Verified))
            throw new InvalidDataException($"Stage 4 alpha 6f Weapon proof is incomplete ({covered:N0}/{declared:N0} top-level weapons covered).");

        var fixedSizes = proofs.Select(x => x.FixedBodyBytes).Distinct().ToList();
        if (fixedSizes.Count != 1)
            throw new InvalidDataException("WeaponDef fixed-body size differed between participating fastfiles; refusing to merge incompatible layouts.");

        var txt = Path.Combine(workspace, $"{map.Name}_stage4_alpha6f_weapon.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage4_alpha6f_weapon.json");
        var result = new NormalMapWeaponSerializerProofResult
        {
            MapName = map.Name,
            Profile = analysis.Profile,
            Workspace = workspace,
            FastFiles = proofs,
            DeclaredWeapon = declared,
            SerializedWeapon = serialized,
            AliasWeapon = aliases,
            CoveredWeapon = covered,
            SerializedFixedBytes = proofs.Sum(x => x.SerializedFixedBytes),
            FixedBodyBytes = fixedSizes[0],
            WeaponsWithVariableOwnership = proofs.Sum(x => x.WeaponsWithVariableOwnership),
            Verified = true,
            Warnings = warnings,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static string Render(NormalMapWeaponSerializerProofResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 6f WeaponDef body/reference proof");
        sb.AppendLine("=================================================================");
        sb.AppendLine("INTERNAL PROOF ONLY: .xbox_weapon_fixed_proof.bin files are NOT game fastfiles.");
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Profile: {r.Profile}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine();
        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"[{ff.Role}] {ff.Source}");
            sb.AppendLine($"  declared WeaponDef:   {ff.DeclaredWeapon:N0}");
            sb.AppendLine($"  serialized fixed body:{ff.SerializedWeapon:N0}");
            sb.AppendLine($"  alias/reference XAsset:{ff.AliasWeapon:N0}");
            sb.AppendLine($"  top-level covered:    {ff.CoveredWeapon:N0}/{ff.DeclaredWeapon:N0}");
            sb.AppendLine($"  fixed body size:      0x{ff.FixedBodyBytes:X} ({ff.FixedBodyBytes:N0} bytes)");
            sb.AppendLine($"  fixed payload bytes:  {ff.SerializedFixedBytes:N0}");
            sb.AppendLine($"  variable ownership:   {ff.WeaponsWithVariableOwnership:N0} weapon(s)");
            sb.AppendLine($"  read-back:            {(ff.Verified ? "PASS" : "FAIL")}");
            sb.AppendLine($"  proof:                {ff.ProofFile}");
            foreach (var warning in ff.Warnings) sb.AppendLine("  NOTE: " + warning);
            sb.AppendLine();
        }
        sb.AppendLine("TOTALS");
        sb.AppendLine($"  declared WeaponDef:  {r.DeclaredWeapon:N0}");
        sb.AppendLine($"  serialized fixed:    {r.SerializedWeapon:N0}");
        sb.AppendLine($"  alias/reference:     {r.AliasWeapon:N0}");
        sb.AppendLine($"  top-level covered:   {r.CoveredWeapon:N0}/{r.DeclaredWeapon:N0}");
        sb.AppendLine($"  fixed body size:     0x{r.FixedBodyBytes:X} ({r.FixedBodyBytes:N0} bytes)");
        sb.AppendLine($"  fixed payload bytes: {r.SerializedFixedBytes:N0}");
        sb.AppendLine();
        sb.AppendLine("Alpha 6f conclusion");
        sb.AppendLine("  Every declared top-level weapon XAsset was classified as either a physical inline/shared WeaponDef body or a validated packed alias/reference. Every physical body was re-emitted Xbox big-endian and read back with its model/XAnim/script-string layout intact.");
        sb.AppendLine("  This proves the fixed WeaponDef layout. Variable owned payloads (strings, bounce-sound names, accuracy graphs, optional flame tables) remain intentionally gated for the next integration step.");
        sb.AppendLine("  No game fastfile was emitted; world/collision and remaining high-priority serializers still gate hardware output.");
        return sb.ToString();
    }

    private static string SanitizeRole(string role) => new string(role.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
