using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Linker;

public sealed record T4NuclearTechniqueClosureRow(
    int PcAssetIndex,
    string PcTechniqueSet,
    string Resolution,
    string? XboxTechniqueSet,
    int SupportingMaterials,
    int? XboxDonorAssetIndex,
    int? XboxDonorSourceOffset,
    int? XboxNonNullTechniqueSlots,
    int? XboxInlineTechniqueSlots,
    int? XboxPackedTechniqueSlots,
    string Evidence);

public sealed class T4NuclearTechniqueClosurePlanResult
{
    public required string PcFastFile { get; init; }
    public required string XboxFastFile { get; init; }
    public required int PcTechniqueRoots { get; init; }
    public required int MaterialBridgeResolved { get; init; }
    public required int ExactNameFallbackResolved { get; init; }
    public required int DonorBoundTechniqueRoots { get; init; }
    public required int AmbiguousBridgeTechniqueRoots { get; init; }
    public required int MissingDonorTargets { get; init; }
    public required int UnresolvedTechniqueRoots { get; init; }
    public required int DistinctXboxTechniqueTargets { get; init; }
    public required int TotalXboxNonNullTechniqueSlots { get; init; }
    public required int TotalXboxInlineTechniqueSlots { get; init; }
    public required int TotalXboxPackedTechniqueSlots { get; init; }
    public required IReadOnlyList<T4NuclearTechniqueClosureRow> Rows { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required bool IdentityClosureComplete { get; init; }
    public required bool GraphExtractionReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Nuclear Stage 2: bind every physical PC TechniqueSet identity to a concrete Xenon
/// TechniqueSet donor in a known-good native fastfile.
///
/// Resolution order:
///  1. material-derived PC -> Xenon mapping from T4OatMaterialTechsetBridgeOracle;
///  2. exact-name native TechniqueSet fallback when the bridge has no observation;
///  3. fail closed for bridge conflicts, missing native targets, or unknown names.
///
/// This stage does NOT copy graph bytes. It proves which of the 426 PC TechniqueSet roots
/// can immediately be backed by one concrete native Xenon graph and records the verified
/// native root identity for the typed graph extractor that follows.
/// </summary>
public static class T4NuclearTechniqueClosurePlanner
{
    public static async Task<T4NuclearTechniqueClosurePlanResult> BuildAsync(
        string pcFastFile,
        string xboxFastFile,
        string outputDirectory,
        string? explicitUnlinkerExe = null,
        CancellationToken cancellationToken = default)
    {
        pcFastFile = Path.GetFullPath(pcFastFile);
        xboxFastFile = Path.GetFullPath(xboxFastFile);
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var bridgeDir = Path.Combine(outputDirectory, "material_bridge");
        var bridge = await T4OatMaterialTechsetBridgeOracle.CompareAsync(
            pcFastFile,
            xboxFastFile,
            bridgeDir,
            explicitUnlinkerExe,
            cancellationToken);
        if (!bridge.Verified)
            throw new InvalidDataException("Nuclear material/TechniqueSet bridge is not verified.");

        var pcCatalog = await T4PcOatTechniqueSetIdentityCatalog.BuildAsync(
            pcFastFile,
            explicitUnlinkerExe,
            cancellationToken);
        if (!pcCatalog.Verified)
            throw new InvalidDataException(
                "Full-main PC TechniqueSet identity catalog is not verified: " +
                string.Join(" | ", pcCatalog.Notes));

        // Identity closure must use the authoritative Xenon loader/fixup oracle,
        // not the older conservative raw-body scanner. Native T4Xenon zones can reuse
        // or pack TechniqueSet graph objects in ways that make a flat aligned byte scan
        // deliberately incomplete. OAT, however, resolves the real XAsset pool and has
        // already proven exact root-for-root type order against the native XAsset table.
        var nativeIdentityDir = Path.Combine(outputDirectory, "native_identity");
        var nativeIdentity = await T4XenonNativeIdentityOracle.RunAsync(
            xboxFastFile,
            nativeIdentityDir,
            explicitUnlinkerExe,
            cancellationToken);
        if (!nativeIdentity.TypeSequenceVerified)
            throw new InvalidDataException("Known-good native identity oracle did not verify the complete root type sequence.");

        var nativeTechsets = nativeIdentity.Entries
            .Where(x => x.OatTypeName.Equals("techniqueset", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Index)
            .ToList();
        if (nativeTechsets.Count == 0)
            throw new InvalidDataException("Known-good native identity oracle returned zero TechniqueSet roots.");

        var duplicateNativeNames = nativeTechsets
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateNativeNames.Count != 0)
            throw new InvalidDataException(
                "Known-good native TechniqueSet identity list contains duplicate names and cannot be bound by identity: " +
                string.Join(", ", duplicateNativeNames.Take(12)));

        var bridgeByPc = bridge.Mappings.ToDictionary(
            x => x.PcTechniqueSet,
            StringComparer.OrdinalIgnoreCase);
        var conflictByPc = bridge.Conflicts.ToDictionary(
            x => x.PcTechniqueSet,
            StringComparer.OrdinalIgnoreCase);
        var donorByName = nativeTechsets.ToDictionary(
            x => x.Name,
            StringComparer.OrdinalIgnoreCase);

        var rows = new List<T4NuclearTechniqueClosureRow>(pcCatalog.ByAssetIndex.Count);
        foreach (var pc in pcCatalog.ByAssetIndex.Values.OrderBy(x => x.AssetIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string resolution;
            string? xboxName = null;
            var support = 0;
            var evidence = string.Empty;

            if (bridgeByPc.TryGetValue(pc.Name, out var mapping))
            {
                resolution = "MATERIAL_BRIDGE";
                xboxName = mapping.XboxTechniqueSet;
                support = mapping.SupportingMaterials;
                evidence =
                    $"{mapping.SupportingMaterials:N0} shared material(s) unanimously map PC '{pc.Name}' -> Xbox '{mapping.XboxTechniqueSet}'.";
            }
            else if (conflictByPc.TryGetValue(pc.Name, out var conflict))
            {
                rows.Add(new T4NuclearTechniqueClosureRow(
                    pc.AssetIndex,
                    pc.Name,
                    "AMBIGUOUS_MATERIAL_BRIDGE",
                    null,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    $"Shared-material evidence maps this PC TechniqueSet to multiple Xbox targets: {string.Join(", ", conflict.XboxTechniqueSets)}"));
                continue;
            }
            else if (donorByName.ContainsKey(pc.Name))
            {
                resolution = "EXACT_NAME_NATIVE_FALLBACK";
                xboxName = pc.Name;
                evidence =
                    $"No material-bridge observation exists, but the known-good native map contains the exact TechniqueSet identity '{pc.Name}'.";
            }
            else
            {
                rows.Add(new T4NuclearTechniqueClosureRow(
                    pc.AssetIndex,
                    pc.Name,
                    "UNRESOLVED_NO_NATIVE_MAPPING",
                    null,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "No deterministic material bridge mapping and no exact-name TechniqueSet exists in the known-good native map."));
                continue;
            }

            if (!donorByName.TryGetValue(xboxName, out var donor))
            {
                rows.Add(new T4NuclearTechniqueClosureRow(
                    pc.AssetIndex,
                    pc.Name,
                    "MAPPED_TARGET_MISSING_DONOR",
                    xboxName,
                    support,
                    null,
                    null,
                    null,
                    null,
                    null,
                    evidence + " The mapped Xbox identity is absent from the OAT-verified native TechniqueSet root set."));
                continue;
            }

            rows.Add(new T4NuclearTechniqueClosureRow(
                pc.AssetIndex,
                pc.Name,
                resolution,
                xboxName,
                support,
                donor.Index,
                null,
                null,
                null,
                null,
                evidence +
                $" OAT/native root identity #{donor.Index:N0} is the concrete Xenon TechniqueSet target. " +
                "Raw serialized body ownership is intentionally deferred to the typed graph extractor."));
        }

        var materialResolved = rows.Count(x => x.Resolution == "MATERIAL_BRIDGE");
        var exactResolved = rows.Count(x => x.Resolution == "EXACT_NAME_NATIVE_FALLBACK");
        var bound = materialResolved + exactResolved;
        var ambiguous = rows.Count(x => x.Resolution == "AMBIGUOUS_MATERIAL_BRIDGE");
        var missingDonor = rows.Count(x => x.Resolution == "MAPPED_TARGET_MISSING_DONOR");
        var unresolved = rows.Count - bound;
        var distinctTargets = rows
            .Where(x => x.XboxDonorAssetIndex is not null && !string.IsNullOrWhiteSpace(x.XboxTechniqueSet))
            .Select(x => x.XboxTechniqueSet!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var totalNonNull = rows.Sum(x => x.XboxNonNullTechniqueSlots ?? 0);
        var totalInline = rows.Sum(x => x.XboxInlineTechniqueSlots ?? 0);
        var totalPacked = rows.Sum(x => x.XboxPackedTechniqueSlots ?? 0);

        var notes = new List<string>
        {
            "NUCLEAR STAGE 2: every row is one physical PC TechniqueSet root from the OAT-verified 426-root identity catalog.",
            "MATERIAL_BRIDGE rows are the strongest mapping class: one or more shared material identities directly prove the PC -> Xenon TechniqueSet transform.",
            "EXACT_NAME_NATIVE_FALLBACK is used only when the material bridge has no observation and the exact TechniqueSet identity exists in the same known-good native map.",
            "No donor graph bytes are copied at this stage. The OAT-verified native root index authorizes the next typed graph extraction/rebase stage; raw body offsets are deliberately not guessed.",
            $"Immediate graph-extraction coverage: {bound:N0}/{rows.Count:N0} PC TechniqueSet roots bind by semantic identity to {distinctTargets:N0} distinct Xenon TechniqueSet graph target(s)."
        };

        var identityComplete = rows.Count != 0 && unresolved == 0;
        var graphReady = bound != 0 && missingDonor == 0;

        var txt = Path.Combine(outputDirectory, "nuclear_techset_closure_plan.txt");
        var json = Path.Combine(outputDirectory, "nuclear_techset_closure_plan.json");

        var result = new T4NuclearTechniqueClosurePlanResult
        {
            PcFastFile = pcFastFile,
            XboxFastFile = xboxFastFile,
            PcTechniqueRoots = rows.Count,
            MaterialBridgeResolved = materialResolved,
            ExactNameFallbackResolved = exactResolved,
            DonorBoundTechniqueRoots = bound,
            AmbiguousBridgeTechniqueRoots = ambiguous,
            MissingDonorTargets = missingDonor,
            UnresolvedTechniqueRoots = unresolved,
            DistinctXboxTechniqueTargets = distinctTargets,
            TotalXboxNonNullTechniqueSlots = totalNonNull,
            TotalXboxInlineTechniqueSlots = totalInline,
            TotalXboxPackedTechniqueSlots = totalPacked,
            Rows = rows,
            Notes = notes,
            IdentityClosureComplete = identityComplete,
            GraphExtractionReady = graphReady,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(
            json,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        return result;
    }

    private static string Render(T4NuclearTechniqueClosurePlanResult r)
    {
        var sb = new StringBuilder(256 * 1024);
        sb.AppendLine("WaW360Converter — NUCLEAR STAGE 2 TECHNIQUESET CLOSURE PLAN");
        sb.AppendLine(new string('=', 92));
        sb.AppendLine($"PC:   {r.PcFastFile}");
        sb.AppendLine($"Xbox: {r.XboxFastFile}");
        sb.AppendLine();
        sb.AppendLine($"PC TechniqueSet roots       : {r.PcTechniqueRoots:N0}");
        sb.AppendLine($"material-bridge resolved     : {r.MaterialBridgeResolved:N0}");
        sb.AppendLine($"exact-name fallback resolved : {r.ExactNameFallbackResolved:N0}");
        sb.AppendLine($"DONOR-BOUND TOTAL            : {r.DonorBoundTechniqueRoots:N0}");
        sb.AppendLine($"ambiguous bridge             : {r.AmbiguousBridgeTechniqueRoots:N0}");
        sb.AppendLine($"mapped target missing donor  : {r.MissingDonorTargets:N0}");
        sb.AppendLine($"UNRESOLVED TOTAL             : {r.UnresolvedTechniqueRoots:N0}");
        sb.AppendLine($"distinct Xenon graph targets : {r.DistinctXboxTechniqueTargets:N0}");
        sb.AppendLine("Xenon graph body/slot census : <typed extractor pending>");
        sb.AppendLine($"identityClosureComplete      : {r.IdentityClosureComplete}");
        sb.AppendLine($"graphExtractionReady         : {r.GraphExtractionReady}");
        sb.AppendLine();

        sb.AppendLine("RESOLUTION CENSUS");
        foreach (var group in r.Rows.GroupBy(x => x.Resolution).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            sb.AppendLine($"  {group.Key,-34} {group.Count(),6:N0}");
        sb.AppendLine();

        sb.AppendLine("UNRESOLVED / AMBIGUOUS ROOTS");
        foreach (var row in r.Rows.Where(x => x.XboxDonorAssetIndex is null))
            sb.AppendLine($"  pcAsset#{row.PcAssetIndex,5} {row.PcTechniqueSet} | {row.Resolution} | {row.Evidence}");
        if (r.Rows.All(x => x.XboxDonorAssetIndex is not null))
            sb.AppendLine("  <none>");
        sb.AppendLine();

        sb.AppendLine("DONOR-BOUND ROOTS");
        foreach (var row in r.Rows.Where(x => x.XboxDonorAssetIndex is not null))
        {
            var donorLocation = row.XboxDonorSourceOffset is int offset
                ? $"nativeRoot#{row.XboxDonorAssetIndex,5} @0x{offset:X8}"
                : $"nativeRoot#{row.XboxDonorAssetIndex,5} via OAT identity";
            var slotInfo = row.XboxNonNullTechniqueSlots is int slots
                ? $"slots={slots}"
                : "slots=<typed extractor pending>";
            sb.AppendLine(
                $"  pcAsset#{row.PcAssetIndex,5} {row.PcTechniqueSet} -> {row.XboxTechniqueSet} | " +
                $"{row.Resolution} | {donorLocation} | {slotInfo}");
        }
        sb.AppendLine();

        foreach (var note in r.Notes)
            sb.AppendLine("NOTE: " + note);

        return sb.ToString();
    }
}
