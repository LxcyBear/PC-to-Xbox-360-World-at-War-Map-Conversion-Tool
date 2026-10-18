using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4WeaponVariableFastFileProof(
    string Role,
    string Source,
    string ProofFile,
    int DeclaredWeapon,
    int SerializedWeapon,
    int AliasWeapon,
    int CoveredWeapon,
    long SerializedBytes,
    long OwnedPayloadBytes,
    int InlineStrings,
    int SoundNameRecords,
    int BounceSoundTables,
    int AccuracyGraphArrays,
    int AccuracyGraphKnots,
    int FlameTables,
    int InlineXModels,
    int InlineMaterials,
    bool Verified,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapWeaponVariableSerializerProofResult
{
    public required string MapName { get; init; }
    public required string Profile { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4WeaponVariableFastFileProof> FastFiles { get; init; }
    public required int DeclaredWeapon { get; init; }
    public required int SerializedWeapon { get; init; }
    public required int AliasWeapon { get; init; }
    public required int CoveredWeapon { get; init; }
    public required int FixedBodyBytes { get; init; }
    public required long SerializedBytes { get; init; }
    public required long OwnedPayloadBytes { get; init; }
    public required int InlineStrings { get; init; }
    public required int SoundNameRecords { get; init; }
    public required int BounceSoundTables { get; init; }
    public required int AccuracyGraphArrays { get; init; }
    public required int AccuracyGraphKnots { get; init; }
    public required int FlameTables { get; init; }
    public required int InlineXModels { get; init; }
    public required int InlineMaterials { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

public static class NormalMapWeaponVariableSerializerProof
{
    public static async Task<NormalMapWeaponVariableSerializerProofResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        if (!analysis.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{map.Name}' classified as {analysis.Profile}. Alpha 7b NORMAL Weapon proof refuses UGX/ZCT profile rules.");

        var candidates = analysis.FastFiles
            .Where(ff => ff.Readable && ff.AssetTypes.TryGetValue("weapon", out var c) && c > 0)
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidDataException($"'{map.Name}' has no readable fastfiles declaring WeaponDef assets.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage4_alpha7b_weapon_owned");
        Directory.CreateDirectory(workspace);
        var proofs = new List<T4WeaponVariableFastFileProof>();
        var warnings = new List<string>();

        foreach (var ff in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var proofPath = Path.Combine(
                workspace,
                Path.GetFileNameWithoutExtension(ff.Path) + $"_{SanitizeRole(ff.Role)}.xbox_weapon_owned_proof.bin");
            var fixedProofPath = Path.Combine(
                outputRoot,
                map.Name,
                "stage4_alpha6f_weapon",
                Path.GetFileNameWithoutExtension(ff.Path) + $"_{SanitizeRole(ff.Role)}.xbox_weapon_fixed_proof.bin");
            var proof = await T4WeaponVariableSerializerProof.BuildProofAsync(
                ff.Path, proofPath, fixedProofPath, cancellationToken);
            if (!proof.Verified || proof.CoveredWeaponCount != proof.DeclaredWeaponCount)
                throw new InvalidDataException($"Weapon Alpha 7b proof for '{Path.GetFileName(ff.Path)}' did not cover every declared top-level weapon XAsset.");

            var serializedBytes = proof.Assets.Sum(x => (long)x.XboxSerializedBytes);
            var fixedBytes = (long)proof.FixedBodyBytes * proof.SerializedBodyCount;
            var ownedBytes = serializedBytes - fixedBytes;
            if (ownedBytes < 0)
                throw new InvalidDataException($"Weapon Alpha 7b proof for '{Path.GetFileName(ff.Path)}' produced a negative owned-payload byte count.");

            proofs.Add(new T4WeaponVariableFastFileProof(
                ff.Role,
                ff.Path,
                proof.OutputFile,
                proof.DeclaredWeaponCount,
                proof.SerializedBodyCount,
                proof.AliasReferenceCount,
                proof.CoveredWeaponCount,
                serializedBytes,
                ownedBytes,
                proof.Assets.Sum(x => x.InlineStrings),
                proof.Assets.Sum(x => x.SoundNameRecords),
                proof.Assets.Sum(x => x.BounceSoundTables),
                proof.Assets.Sum(x => x.AccuracyGraphArrays),
                proof.Assets.Sum(x => x.AccuracyGraphKnots),
                proof.Assets.Sum(x => x.FlameTables),
                proof.Assets.Sum(x => x.InlineXModels),
                proof.Assets.Sum(x => x.InlineMaterials),
                proof.Verified,
                proof.Warnings));
            warnings.AddRange(proof.Warnings.Select(w => $"{Path.GetFileName(ff.Path)}: {w}"));
        }

        var declared = proofs.Sum(x => x.DeclaredWeapon);
        var covered = proofs.Sum(x => x.CoveredWeapon);
        if (declared != covered || proofs.Any(x => !x.Verified))
            throw new InvalidDataException($"Stage 4 alpha 7b Weapon proof is incomplete ({covered:N0}/{declared:N0} top-level weapons covered).");

        var txt = Path.Combine(workspace, $"{map.Name}_stage4_alpha7b_weapon_owned.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage4_alpha7b_weapon_owned.json");
        var result = new NormalMapWeaponVariableSerializerProofResult
        {
            MapName = map.Name,
            Profile = analysis.Profile,
            Workspace = workspace,
            FastFiles = proofs,
            DeclaredWeapon = declared,
            SerializedWeapon = proofs.Sum(x => x.SerializedWeapon),
            AliasWeapon = proofs.Sum(x => x.AliasWeapon),
            CoveredWeapon = covered,
            FixedBodyBytes = 0x9AC,
            SerializedBytes = proofs.Sum(x => x.SerializedBytes),
            OwnedPayloadBytes = proofs.Sum(x => x.OwnedPayloadBytes),
            InlineStrings = proofs.Sum(x => x.InlineStrings),
            SoundNameRecords = proofs.Sum(x => x.SoundNameRecords),
            BounceSoundTables = proofs.Sum(x => x.BounceSoundTables),
            AccuracyGraphArrays = proofs.Sum(x => x.AccuracyGraphArrays),
            AccuracyGraphKnots = proofs.Sum(x => x.AccuracyGraphKnots),
            FlameTables = proofs.Sum(x => x.FlameTables),
            InlineXModels = proofs.Sum(x => x.InlineXModels),
            InlineMaterials = proofs.Sum(x => x.InlineMaterials),
            Verified = true,
            Warnings = warnings,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(
            json,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8,
            cancellationToken);
        return result;
    }

    private static string Render(NormalMapWeaponVariableSerializerProofResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 7b WeaponDef owned-payload proof");
        sb.AppendLine("================================================================");
        sb.AppendLine("INTERNAL PROOF ONLY: .xbox_weapon_owned_proof.bin files are NOT game fastfiles.");
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Profile: {r.Profile}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine();

        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"[{ff.Role}] {ff.Source}");
            sb.AppendLine($"  declared WeaponDef:       {ff.DeclaredWeapon:N0}");
            sb.AppendLine($"  physical bodies serialized:{ff.SerializedWeapon:N0}");
            sb.AppendLine($"  alias/reference XAsset:   {ff.AliasWeapon:N0}");
            sb.AppendLine($"  top-level covered:        {ff.CoveredWeapon:N0}/{ff.DeclaredWeapon:N0}");
            sb.AppendLine($"  full serialized bytes:    {ff.SerializedBytes:N0}");
            sb.AppendLine($"  variable/owned bytes:     {ff.OwnedPayloadBytes:N0}");
            sb.AppendLine($"  inline strings:           {ff.InlineStrings:N0}");
            sb.AppendLine($"  sound-name records:       {ff.SoundNameRecords:N0}");
            sb.AppendLine($"  bounceSound tables:       {ff.BounceSoundTables:N0}");
            sb.AppendLine($"  accuracy arrays/knots:    {ff.AccuracyGraphArrays:N0} / {ff.AccuracyGraphKnots:N0}");
            sb.AppendLine($"  FlameTable bodies:        {ff.FlameTables:N0}");
            sb.AppendLine($"  inline XModel deps:       {ff.InlineXModels:N0}");
            sb.AppendLine($"  inline Material deps:     {ff.InlineMaterials:N0}");
            sb.AppendLine($"  read-back:                {(ff.Verified ? "PASS" : "FAIL")}");
            sb.AppendLine($"  proof:                    {ff.ProofFile}");
            foreach (var warning in ff.Warnings) sb.AppendLine("  NOTE: " + warning);
            sb.AppendLine();
        }

        sb.AppendLine("TOTALS");
        sb.AppendLine($"  declared WeaponDef:       {r.DeclaredWeapon:N0}");
        sb.AppendLine($"  physical bodies serialized:{r.SerializedWeapon:N0}");
        sb.AppendLine($"  alias/reference:          {r.AliasWeapon:N0}");
        sb.AppendLine($"  top-level covered:        {r.CoveredWeapon:N0}/{r.DeclaredWeapon:N0}");
        sb.AppendLine($"  full serialized bytes:    {r.SerializedBytes:N0}");
        sb.AppendLine($"  variable/owned bytes:     {r.OwnedPayloadBytes:N0}");
        sb.AppendLine($"  inline strings:           {r.InlineStrings:N0}");
        sb.AppendLine($"  sound-name records:       {r.SoundNameRecords:N0}");
        sb.AppendLine($"  bounceSound tables:       {r.BounceSoundTables:N0}");
        sb.AppendLine($"  accuracy arrays/knots:    {r.AccuracyGraphArrays:N0} / {r.AccuracyGraphKnots:N0}");
        sb.AppendLine($"  FlameTable bodies:        {r.FlameTables:N0}");
        sb.AppendLine($"  inline XModel deps:       {r.InlineXModels:N0}");
        sb.AppendLine($"  inline Material deps:     {r.InlineMaterials:N0}");
        sb.AppendLine();
        sb.AppendLine("Alpha 7b conclusion");
        sb.AppendLine("  Alpha 6f body coverage was reused unchanged. Each physical WeaponDef was walked through its T4 owned-payload order and re-emitted in Xbox big-endian form, including strings, reusable sound-name records, bounceSound[31], accuracy graph arrays, FlameTable payloads and already-proven inline XModel/Material dependencies.");
        sb.AppendLine("  Every proof record was independently traversed again as big-endian and had to end at the exact serialized boundary with identical ownership counts.");
        sb.AppendLine("  Inline FxEffectDef still fails closed until the typed FX serializer is integrated. No game fastfile was emitted.");
        return sb.ToString();
    }

    private static string SanitizeRole(string role) =>
        new string(role.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
