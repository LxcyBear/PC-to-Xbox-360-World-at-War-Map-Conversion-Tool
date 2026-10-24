using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record LinkerV2IwdPromotionCandidate(string AssetType, string Name, string Archive, string Entry, string Evidence);
public sealed record LinkerV2ResidentRootCandidate(string AssetType, string Name, string SourceFastFile, int AssetIndex, string Evidence);

public sealed class NormalMapLinkerV2TypedClosurePlanResult
{
    public required string MapName { get; init; }
    public required int ExistingNamedXAnims { get; init; }
    public required int ExistingNamedRawFiles { get; init; }
    public required int IwdWeaponSources { get; init; }
    public required int IwdScriptSources { get; init; }
    public required int PromotedXAnimCandidates { get; init; }
    public required int PromotedRawFileCandidates { get; init; }
    public required int ResidentMenuCandidates { get; init; }
    public required int XboxDonorRequiredTechniqueSets { get; init; }
    public required int ExactXboxDonorIdentityMatches { get; init; }
    public required int UnresolvedXboxDonorIdentities { get; init; }
    public required int AmbiguousXboxDonorIdentities { get; init; }
    public required bool DonorIdentityResolutionComplete { get; init; }
    public required IReadOnlyList<LinkerV2IwdPromotionCandidate> Promotions { get; init; }
    public required IReadOnlyList<LinkerV2ResidentRootCandidate> ResidentRoots { get; init; }
    public required int? GoldenExpectedXAnimPromotions { get; init; }
    public required int? GoldenExpectedRawFilePromotions { get; init; }
    public required int? GoldenExpectedMenuCanonicalizations { get; init; }
    public required bool XAnimGoldenCountMatched { get; init; }
    public required bool RawFileGoldenCountMatched { get; init; }
    public required bool MenuGoldenCountMatched { get; init; }
    public required bool PromotionEvidenceReady { get; init; }
    public required bool FullTypedClosureReady { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Linker V2 alpha 7 typed source-archive + Xbox TechniqueSet donor closure plan.
///
/// This consumes IWD content as SOURCE material instead of copying the archive to Xbox. It derives
/// missing RawFile script candidates and XAnim names referenced by weapon source files, then compares
/// those source-derived promotions against a golden oracle when one exists. Golden counts never add
/// an asset by themselves.
///
/// The stage also identifies the two canonical stock UI MenuLists that should remain resident rather
/// than be duplicated in a map package. Production removal remains gated on a typed resident/donor
/// proof; this report is a closure plan, not permission to force counts.
/// </summary>
public static class NormalMapLinkerV2TypedClosurePlan
{
    private static readonly HashSet<string> ResidentMenuNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ui/main.menu",
        "ui/xboxlive_lobby.menu"
    };

    public static async Task<NormalMapLinkerV2TypedClosurePlanResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        NormalMapLinkerV2SourceArchiveResult? sourceOverride = null,
        CancellationToken cancellationToken = default)
    {
        var source = sourceOverride ?? await NormalMapLinkerV2SourceArchive.RunAsync(map, outputRoot, cancellationToken);
        var outDir = Path.Combine(outputRoot, map.Name, "linker_v2_typed_closure");
        Directory.CreateDirectory(outDir);
        var txt = Path.Combine(outDir, $"{map.Name}_linker_v2_typed_closure.txt");
        var json = Path.Combine(outDir, $"{map.Name}_linker_v2_typed_closure.json");

        var primary = source.FastFiles
            .Where(x => x.GraphParticipation is "PRIMARY_GRAPH_SOURCE" or "AUXILIARY_GRAPH_SOURCE")
            .OrderBy(x => LinkerV2ProofEvidence.RoleRank(x.Role))
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var xanimNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rawNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var residentMenus = new List<LinkerV2ResidentRootCandidate>();
        var notes = new List<string>();
        var blockers = new List<string>();

        foreach (var container in primary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(container.Path, cancellationToken);
            var doc = cached.Document;

            var xanims = doc.Assets
                .Where(a => a.TypeName.Equals("xanim", StringComparison.OrdinalIgnoreCase) &&
                            a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                .OrderBy(a => a.Index).ToList();
            if (xanims.Count != 0)
            {
                var proofPath = LinkerV2ProofEvidence.GetProofPath(outputRoot, map.Name, container.Path, container.Role, "xanim");
                if (File.Exists(proofPath))
                {
                    var proof = await T4NativeProofContainerReader.IndexAsync(proofPath, "xanim", cancellationToken);
                    if (proof.Records.Count == xanims.Count)
                    {
                        foreach (var record in proof.Records)
                        {
                            var name = await LinkerV2ProofEvidence.TryReadNameAsync(record, cancellationToken);
                            if (!string.IsNullOrWhiteSpace(name)) xanimNames.Add(name);
                        }
                    }
                    else
                    {
                        blockers.Add($"{container.FileName} XAnim proof count mismatch while building IWD closure names: roots={xanims.Count:N0}, proof={proof.Records.Count:N0}.");
                    }
                }
                else
                {
                    blockers.Add($"Existing XAnim identity proof is missing for {container.FileName}; IWD XAnim promotion de-duplication cannot close safely.");
                }
            }

            // Scanner evidence is allowed here only as a closure diagnostic. It does not authorize
            // production root removal/emission by itself.
            var scan = T4PayloadScanner.Scan(doc, cached.Bytes);
            foreach (var raw in scan.RawFiles) if (!string.IsNullOrWhiteSpace(raw.Name)) rawNames.Add(NormalizeAssetName(raw.Name));
            var menus = doc.Assets.Where(a => a.TypeName.Equals("menulist", StringComparison.OrdinalIgnoreCase)).OrderBy(a => a.Index).ToList();
            if (scan.MenuLists.Count == menus.Count)
            {
                for (var i = 0; i < menus.Count; i++)
                {
                    var name = NormalizeAssetName(scan.MenuLists[i].Name);
                    if (ResidentMenuNames.Contains(name))
                        residentMenus.Add(new LinkerV2ResidentRootCandidate("menulist", name, container.Path, menus[i].Index,
                            "known T4 resident UI MenuList identity; keep resident rather than duplicate in canonical map graph after donor proof"));
                }
            }
        }

        var promotions = new List<LinkerV2IwdPromotionCandidate>();
        var promotedRawNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var promotedXAnimNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var weaponSources = 0;
        var scriptSources = 0;

        foreach (var archive in source.SourceArchive.Where(x => x.Role.Equals("iwd", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(archive.Path))
            {
                blockers.Add($"IWD source archive disappeared: {archive.Path}");
                continue;
            }

            using var zip = ZipFile.OpenRead(archive.Path);
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryName = NormalizeAssetName(entry.FullName);
                if (entryName.Length == 0 || entryName.EndsWith('/')) continue;

                if (IsScriptEntry(entryName))
                {
                    scriptSources++;
                    if (!rawNames.Contains(entryName) && promotedRawNames.Add(entryName))
                        promotions.Add(new LinkerV2IwdPromotionCandidate("rawfile", entryName, archive.Path, entry.FullName,
                            "script/source file exists only in IWD archive and is not represented by any currently resolved source RawFile identity"));
                    continue;
                }

                if (!entryName.StartsWith("weapons/sp/", StringComparison.OrdinalIgnoreCase)) continue;
                weaponSources++;
                string text;
                try
                {
                    await using var stream = entry.Open();
                    using var reader = new StreamReader(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
                    text = await reader.ReadToEndAsync(cancellationToken);
                }
                catch
                {
                    continue;
                }

                foreach (var anim in ExtractAnimationValues(text))
                {
                    if (xanimNames.Contains(anim) || !promotedXAnimNames.Add(anim)) continue;
                    promotions.Add(new LinkerV2IwdPromotionCandidate("xanim", anim, archive.Path, entry.FullName,
                        "weapon source animation field references an XAnim absent from the current primary-root XAnim identity set"));
                }
            }
        }

        var rawPromotions = promotions.Count(x => x.AssetType.Equals("rawfile", StringComparison.OrdinalIgnoreCase));
        var xanimPromotions = promotions.Count(x => x.AssetType.Equals("xanim", StringComparison.OrdinalIgnoreCase));
        var golden = LinkerV2GoldenCatalog.Find(source.CanonicalBaseName);
        int? goldenX = null, goldenR = null, goldenM = null;
        if (golden is not null)
        {
            var sourceX = source.PrimaryGraphRootCounts.GetValueOrDefault("xanim");
            var sourceR = source.PrimaryGraphRootCounts.GetValueOrDefault("rawfile");
            var sourceM = source.PrimaryGraphRootCounts.GetValueOrDefault("menulist");
            goldenX = golden.MainAssetCounts.GetValueOrDefault("xanim") - sourceX;
            goldenR = golden.MainAssetCounts.GetValueOrDefault("rawfile") - sourceR;
            goldenM = sourceM - golden.MainAssetCounts.GetValueOrDefault("menulist");
            notes.Add($"Golden validation only: source-derived promotion/filter counts are compared with working Xbox deltas but the oracle never creates/deletes a root.");
        }

        var promotionReady = blockers.Count == 0;
        var xMatch = goldenX is not null && xanimPromotions == goldenX;
        var rMatch = goldenR is not null && rawPromotions == goldenR;
        var mMatch = goldenM is not null && residentMenus.Count == goldenM;
        if (xMatch) notes.Add($"INDEPENDENT XANIM CONVERGENCE: weapon-source closure derived exactly {xanimPromotions:N0} missing XAnim name(s), matching the golden family delta without count forcing.");
        if (rMatch) notes.Add($"INDEPENDENT RAWFILE CONVERGENCE: IWD source closure derived exactly {rawPromotions:N0} missing RawFile name(s), matching the golden family delta without count forcing.");
        if (mMatch) notes.Add($"INDEPENDENT MENULIST CONVERGENCE: resident UI classification identified exactly {residentMenus.Count:N0} source MenuList root(s) absent from the golden map graph.");

        // Alpha 6 replaces the old hardcoded TechniqueSet blocker with a real deterministic
        // source walk. Non-null PC shader graphs are classified as Xbox-donor requirements;
        // they are never copied into an Xbox candidate.
        var technique = await NormalMapLinkerV2TechniqueClosure.RunAsync(
            map, outputRoot, source, cancellationToken);
        notes.Add($"Technique closure: declared={technique.DeclaredTechniqueSets:N0}, walked={technique.DeterministicallyWalkedTechniqueSets:N0}, donorRequired={technique.XboxDonorRequiredTechniqueSets:N0}, donorMatched={technique.ExactXboxDonorIdentityMatches:N0}, donorUnresolved={technique.UnresolvedXboxDonorIdentities:N0}, donorAmbiguous={technique.AmbiguousXboxDonorIdentities:N0}, blocked={technique.BlockedTechniqueSets:N0}; report={technique.TextReportFile}.");
        foreach (var blocker in technique.Blockers)
            blockers.Add("TechniqueSet: " + blocker);

        // This stage is now derived from concrete work rather than a permanent false constant.
        // IWD promotions and resident MenuList removals are still plans until their assets are
        // actually inserted/removed from the canonical root graph, so they keep the gate closed.
        if (promotions.Count != 0)
            blockers.Add($"{promotions.Count:N0} source-derived IWD promotion(s) are proven but not yet materialized into the canonical root graph.");
        if (residentMenus.Count != 0)
            blockers.Add($"{residentMenus.Count:N0} resident MenuList root(s) are identified but not yet removed by typed always-loaded/resident policy.");

        var fullClosure = promotionReady &&
                          promotions.Count == 0 &&
                          residentMenus.Count == 0 &&
                          technique.FullTechniqueClosureReady;

        var result = new NormalMapLinkerV2TypedClosurePlanResult
        {
            MapName = map.Name,
            ExistingNamedXAnims = xanimNames.Count,
            ExistingNamedRawFiles = rawNames.Count,
            IwdWeaponSources = weaponSources,
            IwdScriptSources = scriptSources,
            PromotedXAnimCandidates = xanimPromotions,
            PromotedRawFileCandidates = rawPromotions,
            ResidentMenuCandidates = residentMenus.Count,
            XboxDonorRequiredTechniqueSets = technique.XboxDonorRequiredTechniqueSets,
            ExactXboxDonorIdentityMatches = technique.ExactXboxDonorIdentityMatches,
            UnresolvedXboxDonorIdentities = technique.UnresolvedXboxDonorIdentities,
            AmbiguousXboxDonorIdentities = technique.AmbiguousXboxDonorIdentities,
            DonorIdentityResolutionComplete = technique.DonorIdentityResolutionComplete,
            Promotions = promotions.OrderBy(x => x.AssetType, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            ResidentRoots = residentMenus,
            GoldenExpectedXAnimPromotions = goldenX,
            GoldenExpectedRawFilePromotions = goldenR,
            GoldenExpectedMenuCanonicalizations = goldenM,
            XAnimGoldenCountMatched = xMatch,
            RawFileGoldenCountMatched = rMatch,
            MenuGoldenCountMatched = mMatch,
            PromotionEvidenceReady = promotionReady,
            FullTypedClosureReady = fullClosure,
            Notes = notes,
            Blockers = blockers,
            Verified = promotionReady && technique.Verified,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private static bool IsScriptEntry(string name) =>
        name.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".csc", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ExtractAnimationValues(string text)
    {
        var output = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        text = text.Replace("\0", string.Empty);

        // Classic weapon files frequently use backslash-delimited key/value pairs.
        var slash = text.Split('\\');
        for (var i = 0; i + 1 < slash.Length; i++)
        {
            var key = slash[i].Trim();
            var value = slash[i + 1].Trim();
            if (LooksLikeAnimationKey(key) && LooksLikeAssetName(value)) output.Add(value);
        }

        // Also support CSV / key-value textual exports used by community tools.
        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            var separators = new[] { ',', '=', '\t' };
            foreach (var sep in separators)
            {
                var at = trimmed.IndexOf(sep);
                if (at <= 0 || at >= trimmed.Length - 1) continue;
                var key = trimmed[..at].Trim().Trim('"');
                var value = trimmed[(at + 1)..].Trim().Trim('"');
                if (LooksLikeAnimationKey(key) && LooksLikeAssetName(value)) output.Add(value);
                break;
            }
        }
        return output;
    }

    private static bool LooksLikeAnimationKey(string key) =>
        key.Contains("anim", StringComparison.OrdinalIgnoreCase) &&
        !key.Contains("animationType", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeAssetName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512) return false;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || value == "0") return false;
        foreach (var ch in value)
            if (ch < 0x20 || ch > 0x7E || char.IsWhiteSpace(ch)) return false;
        return value.Any(char.IsLetter);
    }

    private static string NormalizeAssetName(string value) => value.Replace('\\', '/').Trim().TrimStart('/');

    private static string Render(NormalMapLinkerV2TypedClosurePlanResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — LINKER V2 alpha 7 typed source-archive + Xbox TechniqueSet donor closure plan");
        sb.AppendLine(new string('=', 104));
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"existingNamedXAnim={r.ExistingNamedXAnims:N0}; existingNamedRawFile={r.ExistingNamedRawFiles:N0}; iwdWeapons={r.IwdWeaponSources:N0}; iwdScripts={r.IwdScriptSources:N0}");
        sb.AppendLine($"derivedPromotions: xanim={r.PromotedXAnimCandidates:N0}; rawfile={r.PromotedRawFileCandidates:N0}; residentMenuCandidates={r.ResidentMenuCandidates:N0}");
        sb.AppendLine($"techniqueDonors: required={r.XboxDonorRequiredTechniqueSets:N0}; exact={r.ExactXboxDonorIdentityMatches:N0}; unresolved={r.UnresolvedXboxDonorIdentities:N0}; ambiguous={r.AmbiguousXboxDonorIdentities:N0}; identityComplete={r.DonorIdentityResolutionComplete}");
        if (r.GoldenExpectedXAnimPromotions is not null)
            sb.AppendLine($"goldenValidation: xanimExpected={r.GoldenExpectedXAnimPromotions:+#;-#;0} match={r.XAnimGoldenCountMatched}; rawExpected={r.GoldenExpectedRawFilePromotions:+#;-#;0} match={r.RawFileGoldenCountMatched}; menuExpected={r.GoldenExpectedMenuCanonicalizations:+#;-#;0} match={r.MenuGoldenCountMatched}");
        sb.AppendLine($"promotionEvidenceReady={r.PromotionEvidenceReady}; fullTypedClosureReady={r.FullTypedClosureReady}; verified={r.Verified}");
        sb.AppendLine();
        if (r.ResidentRoots.Count != 0)
        {
            sb.AppendLine("RESIDENT MENU ROOT CANDIDATES");
            foreach (var x in r.ResidentRoots) sb.AppendLine($"  {x.Name} <- {Path.GetFileName(x.SourceFastFile)}#{x.AssetIndex}");
            sb.AppendLine();
        }
        sb.AppendLine("IWD PROMOTION CANDIDATES");
        foreach (var x in r.Promotions.Take(512)) sb.AppendLine($"  {x.AssetType,-10} {x.Name} <- {Path.GetFileName(x.Archive)}::{x.Entry}");
        if (r.Promotions.Count > 512) sb.AppendLine($"  ... {r.Promotions.Count - 512:N0} additional candidate(s) omitted from TXT; JSON contains the full list.");
        sb.AppendLine();
        foreach (var note in r.Notes) sb.AppendLine("NOTE: " + note);
        foreach (var blocker in r.Blockers) sb.AppendLine("BLOCKER: " + blocker);
        return sb.ToString();
    }
}
