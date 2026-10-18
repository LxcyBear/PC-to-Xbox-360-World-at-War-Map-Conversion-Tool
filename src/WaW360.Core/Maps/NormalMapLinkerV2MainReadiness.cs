using System.Text;
using System.Text.Json;

namespace WaW360.Core.Maps;

public sealed class NormalMapLinkerV2MainReadinessResult
{
    public required string MapName { get; init; }
    public required bool ScriptStringRemapReady { get; init; }
    public required bool RootAccountingReady { get; init; }
    public required bool ConcreteIdentityClosureReady { get; init; }
    public required bool TypedDependencyClosureReady { get; init; }
    public required bool PackedPointerEncodingReady { get; init; }
    public required bool SharedTargetOwnershipReady { get; init; }
    public required bool RealXboxBlockAllocationReady { get; init; }
    public required bool AudioStrictReady { get; init; }
    public required bool StructuralHardwareDiagnosticReady { get; init; }
    public required bool StrictHardwareReady { get; init; }
    public required int PrimaryRoots { get; init; }
    public required int ProofNamedRoots { get; init; }
    public required int IdentityCollisionGroups { get; init; }
    public required int PackedPointerReferences { get; init; }
    public required int UniquePackedTargets { get; init; }
    public required int InvalidPackedPointers { get; init; }
    public required int CanonicalScriptStringEntries { get; init; }
    public required int? GoldenMainRoots { get; init; }
    public required int? GoldenMainScriptStrings { get; init; }
    public required IReadOnlyList<string> ClearedGates { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 4 consolidated main-FF gate.
/// This stage emits no fastfile. It distinguishes a first structural/silent hardware diagnostic
/// from strict-fidelity output so audio work cannot hide structural linker progress, while still
/// refusing native main emission until identity, dependency and relocation ownership are real.
/// </summary>
public static class NormalMapLinkerV2MainReadiness
{
    public static async Task<NormalMapLinkerV2MainReadinessResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null,
        NormalMapLinkerV2CanonicalConvergenceResult? convergenceOverride = null,
        NormalMapLinkerV2ScriptStringMergeResult? scriptOverride = null,
        NormalMapLinkerV2IdentityEvidenceResult? identityOverride = null,
        NormalMapLinkerV2RelocationObligationsResult? relocationOverride = null,
        CancellationToken cancellationToken = default)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var convergence = convergenceOverride ?? await NormalMapLinkerV2CanonicalConvergence.RunAsync(map, outputRoot, source, cancellationToken);
        var script = scriptOverride ?? await NormalMapLinkerV2ScriptStringMerge.RunAsync(map, outputRoot, source, cancellationToken);
        var identity = identityOverride ?? await NormalMapLinkerV2IdentityEvidence.RunAsync(map, outputRoot, source, cancellationToken);
        var relocation = relocationOverride ?? await NormalMapLinkerV2RelocationObligations.RunAsync(map, outputRoot, source, cancellationToken);

        var scriptReady = script.Verified && script.FullRemapReady && (script.CanonicalNullEntries >= 0 && script.CanonicalNullEntries <= 1);
        var rootAccountingReady = identity.Verified && identity.PrimaryRoots == source.PrimaryGraphSourceRoots;

        // Alpha 4 has concrete identities for the proof-backed tracked families, but it has not yet
        // migrated deterministic identity walkers for every source root nor applied type-specific
        // collision/override semantics. Do not infer closure merely from a golden count.
        var identityClosureReady = identity.NamedProofRoots == source.PrimaryGraphSourceRoots &&
                                   identity.ExactNameCollisionGroups == 0 && identity.MissingProofFiles == 0;

        // A golden oracle can tell us WHICH families still need canonicalize/promote work, not which
        // concrete roots to remove/add. Unknown maps likewise remain pending until the generic walkers
        // actually close the graph. Therefore count deltas alone can never set this gate true.
        var typedClosureReady = false;

        var pointerEncodingReady = relocation.Verified && relocation.PackedEncodingVerified && relocation.InvalidPackedPointers == 0;
        var sharedTargetOwnershipReady = relocation.SharedTargetOwnershipResolved && relocation.NestedPointerWalkComplete;
        var blockAllocationReady = false; // destination ownership/allocation follows shared-target closure.
        var audioReady = false; // SDNS/XMA production path remains future strict-fidelity work.

        var structuralReady = scriptReady && rootAccountingReady && identityClosureReady && typedClosureReady &&
                              pointerEncodingReady && sharedTargetOwnershipReady && blockAllocationReady;
        var strictReady = structuralReady && audioReady;

        var cleared = new List<string>();
        var blockers = new List<string>();
        if (scriptReady) cleared.Add($"Script-string remap is source-derived and complete ({script.CanonicalTableEntries:N0} canonical table entries; {script.SourceNullEntriesCollapsed:N0} duplicate source null sentinel(s) collapsed safely).");
        else blockers.Add("Script-string remap still has unresolved packed/alias entries or failed invariants.");

        if (rootAccountingReady) cleared.Add($"Authoritative primary-root accounting closes exactly at {source.PrimaryGraphSourceRoots:N0} source roots.");
        else blockers.Add("Authoritative source-root accounting does not reconcile with the identity ledger.");

        if (pointerEncodingReady) cleared.Add($"Known proof-backed packed-pointer encoding/range validation passes ({relocation.PackedPointerReferences:N0} packed reference(s), invalid=0).");
        else blockers.Add($"Packed-pointer encoding/range validation is not clean (invalid={relocation.InvalidPackedPointers:N0}).");

        if (!identityClosureReady)
            blockers.Add($"Concrete identity closure pending: proof-backed names cover {identity.NamedProofRoots:N0}/{source.PrimaryGraphSourceRoots:N0} primary roots; exact-name collision groups requiring type-specific policy={identity.ExactNameCollisionGroups:N0}; missing proof files={identity.MissingProofFiles:N0}.");

        if (!typedClosureReady)
        {
            if (convergence.GoldenOracleApplied)
                blockers.Add($"Typed dependency/override closure pending: golden validation still flags {convergence.CanonicalizeFamilies:N0} canonicalize family/families and {convergence.PromoteFamilies:N0} promotion family/families; concrete identities/edges must prove every edit.");
            else
                blockers.Add("Typed dependency/override closure pending: generic map has no oracle target, so deterministic walkers must derive the final graph directly.");
        }

        if (!sharedTargetOwnershipReady)
            blockers.Add($"Shared-target ownership pending: {relocation.UniquePackedTargets:N0} unique proof-backed packed target(s) plus nested child-pointer traversal must be assigned to concrete owners before rebasing.");
        if (!blockAllocationReady)
            blockers.Add("Real Xbox destination block allocation is pending; no source/golden block-size table is copied as a shortcut.");
        if (!audioReady)
            blockers.Add("Strict audio is pending: final Sound assets still require Xbox SDNS/XMA emission. This will NOT block a future explicitly labeled silent structural hardware diagnostic once all structural gates pass.");

        var notes = new List<string>
        {
            "Alpha 4 deliberately separates STRUCTURAL hardware readiness from STRICT fidelity. Audio is strict-only; identity/dependency/relocation/block correctness is mandatory for both.",
            "A golden count mismatch by itself is never a hardware blocker and never authorizes count-forcing.",
            "No main FF is emitted while StructuralHardwareDiagnosticReady is false."
        };

        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_main_gate");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_main_gate.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_main_gate.json");

        var result = new NormalMapLinkerV2MainReadinessResult
        {
            MapName = map.Name,
            ScriptStringRemapReady = scriptReady,
            RootAccountingReady = rootAccountingReady,
            ConcreteIdentityClosureReady = identityClosureReady,
            TypedDependencyClosureReady = typedClosureReady,
            PackedPointerEncodingReady = pointerEncodingReady,
            SharedTargetOwnershipReady = sharedTargetOwnershipReady,
            RealXboxBlockAllocationReady = blockAllocationReady,
            AudioStrictReady = audioReady,
            StructuralHardwareDiagnosticReady = structuralReady,
            StrictHardwareReady = strictReady,
            PrimaryRoots = source.PrimaryGraphSourceRoots,
            ProofNamedRoots = identity.NamedProofRoots,
            IdentityCollisionGroups = identity.ExactNameCollisionGroups,
            PackedPointerReferences = relocation.PackedPointerReferences,
            UniquePackedTargets = relocation.UniquePackedTargets,
            InvalidPackedPointers = relocation.InvalidPackedPointers,
            CanonicalScriptStringEntries = script.CanonicalTableEntries,
            GoldenMainRoots = convergence.GoldenMainRoots,
            GoldenMainScriptStrings = convergence.GoldenMainScriptStrings,
            ClearedGates = cleared,
            Blockers = blockers,
            Notes = notes,
            Verified = script.Verified && identity.Verified && relocation.Verified && convergence.Verified,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private static string Render(NormalMapLinkerV2MainReadinessResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 4 consolidated main-FF native gate");
        sb.AppendLine(new string('=', 98));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"verified={r.Verified}; structuralHardwareReady={r.StructuralHardwareDiagnosticReady}; strictHardwareReady={r.StrictHardwareReady}");
        sb.AppendLine($"sourceRoots={r.PrimaryRoots:N0}; proofNamed={r.ProofNamedRoots:N0}; identityCollisions={r.IdentityCollisionGroups:N0}; canonicalScriptStrings={r.CanonicalScriptStringEntries:N0}");
        sb.AppendLine($"packedRefs={r.PackedPointerReferences:N0}; uniquePackedTargets={r.UniquePackedTargets:N0}; invalidPacked={r.InvalidPackedPointers:N0}");
        if (r.GoldenMainRoots is not null)
            sb.AppendLine($"goldenValidation: roots={r.GoldenMainRoots:N0}; scriptStrings={r.GoldenMainScriptStrings:N0} (validation only)");
        sb.AppendLine();
        sb.AppendLine("GATES");
        sb.AppendLine($"  scriptStringRemapReady       = {r.ScriptStringRemapReady}");
        sb.AppendLine($"  rootAccountingReady          = {r.RootAccountingReady}");
        sb.AppendLine($"  concreteIdentityClosureReady = {r.ConcreteIdentityClosureReady}");
        sb.AppendLine($"  typedDependencyClosureReady  = {r.TypedDependencyClosureReady}");
        sb.AppendLine($"  packedPointerEncodingReady   = {r.PackedPointerEncodingReady}");
        sb.AppendLine($"  sharedTargetOwnershipReady   = {r.SharedTargetOwnershipReady}");
        sb.AppendLine($"  realXboxBlockAllocationReady = {r.RealXboxBlockAllocationReady}");
        sb.AppendLine($"  audioStrictReady             = {r.AudioStrictReady}");
        if (r.ClearedGates.Count != 0)
        {
            sb.AppendLine();
            sb.AppendLine("CLEARED");
            foreach (var line in r.ClearedGates) sb.AppendLine("  PASS: " + line);
        }
        if (r.Blockers.Count != 0)
        {
            sb.AppendLine();
            sb.AppendLine("REMAINING BLOCKERS");
            foreach (var line in r.Blockers) sb.AppendLine("  BLOCKER: " + line);
        }
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        return sb.ToString();
    }
}
