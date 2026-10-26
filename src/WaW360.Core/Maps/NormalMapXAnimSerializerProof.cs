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
/// Stage 4 native Xbox XAnim serializer proof.
///
/// Every top-level XAnim in each participating NORMAL-map fastfile is decoded from
/// the PC 0x58/10-bucket layout and re-emitted in the Xbox 0x5C/12-bucket layout,
/// including the retail-proven simple/normal/precision quaternion encodings and
/// translation bone-index remapping. Every output body is parsed back independently.
/// Packed-name top-level XAnims remain fail-closed until their asset names can be
/// resolved for the viewmodel precision rule. Proof containers are internal artifacts.
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

            // Real console transform: 0x58 PC/10-bucket XAnimParts -> 0x5C Xbox/12-bucket
            // XAnimParts, including simple/normal/precision quaternion packing and
            // translation bone-index remapping after quaternion-order sorting.
            var proof = await T4XboxXAnimTransformProof.BuildAsync(
                ff.Path, proofPath, cancellationToken);

            if (!proof.Verified ||
                proof.DeclaredXAnim != proof.ConvertedXAnim ||
                proof.InlineCandidates != proof.DeclaredXAnim)
            {
                throw new InvalidDataException(
                    $"Real Xbox XAnim proof for '{Path.GetFileName(ff.Path)}' did not cover every declared animation " +
                    $"({proof.ConvertedXAnim:N0}/{proof.DeclaredXAnim:N0}).");
            }

            proofs.Add(new T4XAnimFastFileProof(
                ff.Role,
                ff.Path,
                proof.OutputFile,
                proof.DeclaredXAnim,
                proof.ConvertedXAnim,
                proof.DeltaXAnim,
                proof.XboxSerializedBytes,
                proof.Verified,
                Array.Empty<string>()));
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
        sb.AppendLine("WaW360Converter — Stage 4 native Xbox XAnim serializer proof");
        sb.AppendLine("============================================================");
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

        sb.AppendLine("Native XAnim conclusion");
        sb.AppendLine("  Every declared XAnim was transformed from the PC 0x58/10-bucket form into the real Xbox 0x5C/12-bucket form and independently parsed back.");
        sb.AppendLine("  NORMAL_QUAT uses packed 32-bit randomDataInt, PRECISION_QUAT uses packed 48-bit randomDataShort, SIMPLE_QUAT uses packed 16-bit storage, and translation bone indices are remapped after Xbox quaternion sorting.");
        sb.AppendLine("  These proof blobs are now suitable as the XAnim payload source for the Stage-5 native writer.");
        return sb.ToString();
    }

    private static string SanitizeRole(string role) =>
        new string(role.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
