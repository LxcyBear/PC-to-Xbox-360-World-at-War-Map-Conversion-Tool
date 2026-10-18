using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4FxFastFileProof(
    string Role,
    string Source,
    string ProofFile,
    int DeclaredFx,
    int SerializedFx,
    int AliasFx,
    int CoveredFx,
    long SerializedBytes,
    long DeclaredTotalBytes,
    long FxOwnedBytes,
    long InlineDependencyBytes,
    int ElemDefs,
    int VelocitySamples,
    int VisualSamples,
    int InlineStrings,
    int MaterialVisuals,
    int ModelVisuals,
    int SoundVisuals,
    int RunnerVisuals,
    int DecalMarkArrays,
    int EffectRefNames,
    int TrailDefs,
    int TrailVertices,
    int TrailIndices,
    int InlineMaterials,
    int InlineXModels,
    bool Verified,
    IReadOnlyList<string> Warnings);

public sealed class NormalMapFxSerializerProofResult
{
    public required string MapName { get; init; }
    public required string Profile { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4FxFastFileProof> FastFiles { get; init; }
    public required int DeclaredFx { get; init; }
    public required int SerializedFx { get; init; }
    public required int AliasFx { get; init; }
    public required int CoveredFx { get; init; }
    public required long SerializedBytes { get; init; }
    public required long DeclaredTotalBytes { get; init; }
    public required long FxOwnedBytes { get; init; }
    public required long InlineDependencyBytes { get; init; }
    public required int ElemDefs { get; init; }
    public required int VelocitySamples { get; init; }
    public required int VisualSamples { get; init; }
    public required int InlineStrings { get; init; }
    public required int MaterialVisuals { get; init; }
    public required int ModelVisuals { get; init; }
    public required int SoundVisuals { get; init; }
    public required int RunnerVisuals { get; init; }
    public required int DecalMarkArrays { get; init; }
    public required int EffectRefNames { get; init; }
    public required int TrailDefs { get; init; }
    public required int TrailVertices { get; init; }
    public required int TrailIndices { get; init; }
    public required int InlineMaterials { get; init; }
    public required int InlineXModels { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 4 alpha 9b: cross-map-stable typed FxEffectDef serializer proof.
/// Weapon integration deliberately waits until this proof passes independently.
/// </summary>
public static class NormalMapFxSerializerProof
{
    public static async Task<NormalMapFxSerializerProofResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        if (!analysis.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{map.Name}' classified as {analysis.Profile}. Alpha 9b NORMAL FX proof refuses UGX/ZCT profile rules.");

        var candidates = analysis.FastFiles
            .Where(ff => ff.Readable && ff.AssetTypes.TryGetValue("fx", out var count) && count > 0)
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidDataException($"'{map.Name}' has no readable fastfiles declaring FxEffectDef assets.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage4_alpha9b_fx");
        Directory.CreateDirectory(workspace);
        var proofs = new List<T4FxFastFileProof>();
        var warnings = new List<string>();

        foreach (var ff in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var proofPath = Path.Combine(
                workspace,
                Path.GetFileNameWithoutExtension(ff.Path) + $"_{SanitizeRole(ff.Role)}.xbox_fx_proof.bin");
            var proof = await T4FxSerializerProof.BuildProofAsync(ff.Path, proofPath, cancellationToken);
            if (!proof.Verified || proof.CoveredFxCount != proof.DeclaredFxCount)
                throw new InvalidDataException($"FX Alpha 9b proof for '{Path.GetFileName(ff.Path)}' did not cover every declared top-level FX XAsset.");

            proofs.Add(new T4FxFastFileProof(
                ff.Role,
                ff.Path,
                proof.OutputFile,
                proof.DeclaredFxCount,
                proof.SerializedBodyCount,
                proof.AliasReferenceCount,
                proof.CoveredFxCount,
                proof.Assets.Sum(x => (long)x.XboxSerializedBytes),
                proof.Assets.Sum(x => (long)x.DeclaredTotalSize),
                proof.Assets.Sum(x => (long)x.FxOwnedBytes),
                proof.Assets.Sum(x => (long)x.InlineDependencyBytes),
                proof.Assets.Sum(x => x.ElemDefs),
                proof.Assets.Sum(x => x.VelocitySamples),
                proof.Assets.Sum(x => x.VisualSamples),
                proof.Assets.Sum(x => x.InlineStrings),
                proof.Assets.Sum(x => x.MaterialVisuals),
                proof.Assets.Sum(x => x.ModelVisuals),
                proof.Assets.Sum(x => x.SoundVisuals),
                proof.Assets.Sum(x => x.RunnerVisuals),
                proof.Assets.Sum(x => x.DecalMarkArrays),
                proof.Assets.Sum(x => x.EffectRefNames),
                proof.Assets.Sum(x => x.TrailDefs),
                proof.Assets.Sum(x => x.TrailVertices),
                proof.Assets.Sum(x => x.TrailIndices),
                proof.Assets.Sum(x => x.InlineMaterials),
                proof.Assets.Sum(x => x.InlineXModels),
                proof.Verified,
                proof.Warnings));
            warnings.AddRange(proof.Warnings.Select(w => $"{Path.GetFileName(ff.Path)}: {w}"));
        }

        var declared = proofs.Sum(x => x.DeclaredFx);
        var covered = proofs.Sum(x => x.CoveredFx);
        if (declared != covered || proofs.Any(x => !x.Verified))
            throw new InvalidDataException($"Stage 4 alpha 9b FX proof is incomplete ({covered:N0}/{declared:N0} top-level FX covered).");

        var txt = Path.Combine(workspace, $"{map.Name}_stage4_alpha9b_fx.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage4_alpha9b_fx.json");
        var result = new NormalMapFxSerializerProofResult
        {
            MapName = map.Name,
            Profile = analysis.Profile,
            Workspace = workspace,
            FastFiles = proofs,
            DeclaredFx = declared,
            SerializedFx = proofs.Sum(x => x.SerializedFx),
            AliasFx = proofs.Sum(x => x.AliasFx),
            CoveredFx = covered,
            SerializedBytes = proofs.Sum(x => x.SerializedBytes),
            DeclaredTotalBytes = proofs.Sum(x => x.DeclaredTotalBytes),
            FxOwnedBytes = proofs.Sum(x => x.FxOwnedBytes),
            InlineDependencyBytes = proofs.Sum(x => x.InlineDependencyBytes),
            ElemDefs = proofs.Sum(x => x.ElemDefs),
            VelocitySamples = proofs.Sum(x => x.VelocitySamples),
            VisualSamples = proofs.Sum(x => x.VisualSamples),
            InlineStrings = proofs.Sum(x => x.InlineStrings),
            MaterialVisuals = proofs.Sum(x => x.MaterialVisuals),
            ModelVisuals = proofs.Sum(x => x.ModelVisuals),
            SoundVisuals = proofs.Sum(x => x.SoundVisuals),
            RunnerVisuals = proofs.Sum(x => x.RunnerVisuals),
            DecalMarkArrays = proofs.Sum(x => x.DecalMarkArrays),
            EffectRefNames = proofs.Sum(x => x.EffectRefNames),
            TrailDefs = proofs.Sum(x => x.TrailDefs),
            TrailVertices = proofs.Sum(x => x.TrailVertices),
            TrailIndices = proofs.Sum(x => x.TrailIndices),
            InlineMaterials = proofs.Sum(x => x.InlineMaterials),
            InlineXModels = proofs.Sum(x => x.InlineXModels),
            Verified = true,
            Warnings = warnings,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static string Render(NormalMapFxSerializerProofResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 4 alpha 9b FxEffectDef serializer proof");
        sb.AppendLine("==============================================================");
        sb.AppendLine("INTERNAL PROOF ONLY: .xbox_fx_proof.bin files are NOT game fastfiles.");
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Profile: {r.Profile}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine();

        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"[{ff.Role}] {ff.Source}");
            sb.AppendLine($"  declared FxEffectDef:    {ff.DeclaredFx:N0}");
            sb.AppendLine($"  physical bodies:         {ff.SerializedFx:N0}");
            sb.AppendLine($"  alias/reference XAsset:  {ff.AliasFx:N0}");
            sb.AppendLine($"  top-level covered:       {ff.CoveredFx:N0}/{ff.DeclaredFx:N0}");
            sb.AppendLine($"  physical stream bytes:   {ff.SerializedBytes:N0}");
            sb.AppendLine($"  header totalSize bytes:  {ff.DeclaredTotalBytes:N0}");
            sb.AppendLine($"  reconstructed FX-owned:  {ff.FxOwnedBytes:N0}");
            sb.AppendLine($"  inline dependency bytes: {ff.InlineDependencyBytes:N0}");
            sb.AppendLine($"  FxElemDef bodies:        {ff.ElemDefs:N0}");
            sb.AppendLine($"  velocity samples:        {ff.VelocitySamples:N0}");
            sb.AppendLine($"  visual-state samples:    {ff.VisualSamples:N0}");
            sb.AppendLine($"  inline strings:          {ff.InlineStrings:N0}");
            sb.AppendLine($"  visual deps M/X/S/R:     {ff.MaterialVisuals:N0}/{ff.ModelVisuals:N0}/{ff.SoundVisuals:N0}/{ff.RunnerVisuals:N0}");
            sb.AppendLine($"  decal mark arrays:       {ff.DecalMarkArrays:N0}");
            sb.AppendLine($"  effect-ref names:        {ff.EffectRefNames:N0}");
            sb.AppendLine($"  trails verts/indices:    {ff.TrailDefs:N0} / {ff.TrailVertices:N0} / {ff.TrailIndices:N0}");
            sb.AppendLine($"  inline Material/XModel:  {ff.InlineMaterials:N0} / {ff.InlineXModels:N0}");
            sb.AppendLine($"  read-back:               {(ff.Verified ? "PASS" : "FAIL")}");
            sb.AppendLine($"  proof:                   {ff.ProofFile}");
            foreach (var warning in ff.Warnings) sb.AppendLine("  NOTE: " + warning);
            sb.AppendLine();
        }

        sb.AppendLine("TOTALS");
        sb.AppendLine($"  declared FxEffectDef:    {r.DeclaredFx:N0}");
        sb.AppendLine($"  physical bodies:         {r.SerializedFx:N0}");
        sb.AppendLine($"  alias/reference:         {r.AliasFx:N0}");
        sb.AppendLine($"  top-level covered:       {r.CoveredFx:N0}/{r.DeclaredFx:N0}");
        sb.AppendLine($"  physical stream bytes:   {r.SerializedBytes:N0}");
        sb.AppendLine($"  header totalSize bytes:  {r.DeclaredTotalBytes:N0}");
        sb.AppendLine($"  reconstructed FX-owned:  {r.FxOwnedBytes:N0}");
        sb.AppendLine($"  inline dependency bytes: {r.InlineDependencyBytes:N0}");
        sb.AppendLine($"  FxElemDef bodies:        {r.ElemDefs:N0}");
        sb.AppendLine($"  velocity samples:        {r.VelocitySamples:N0}");
        sb.AppendLine($"  visual-state samples:    {r.VisualSamples:N0}");
        sb.AppendLine($"  inline strings:          {r.InlineStrings:N0}");
        sb.AppendLine($"  visual deps M/X/S/R:     {r.MaterialVisuals:N0}/{r.ModelVisuals:N0}/{r.SoundVisuals:N0}/{r.RunnerVisuals:N0}");
        sb.AppendLine($"  effect-ref names:        {r.EffectRefNames:N0}");
        sb.AppendLine($"  trails verts/indices:    {r.TrailDefs:N0} / {r.TrailVertices:N0} / {r.TrailIndices:N0}");
        sb.AppendLine($"  inline Material/XModel:  {r.InlineMaterials:N0} / {r.InlineXModels:N0}");
        sb.AppendLine();
        sb.AppendLine("Alpha 9b conclusion");
        sb.AppendLine("  Every strong-header physical FxEffectDef was re-emitted in Xbox big-endian form with its T4-owned FxElemDef stream, state samples, conditional visuals, FX reference names and trail payloads, then independently traversed again to the exact byte boundary.");
        sb.AppendLine("  Inline Material/XModel visual dependencies reuse the already-proven Alpha 5 nested serializers. WeaponDef integration remains intentionally separate until this standalone FX proof passes.");
        sb.AppendLine("  No game fastfile was emitted.");
        return sb.ToString();
    }

    private static string SanitizeRole(string role) =>
        new string(role.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
