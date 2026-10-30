using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed record T4XModelPerModelClassificationEntry(
    int XModelOrdinal,
    int AssetIndex,
    string Name,
    int SourceOffset,
    int BoneCount,
    int ClassificationCount,
    int UniqueBoneNames,
    bool Complete,
    string CsvFile,
    string? Error);

public sealed class T4XModelPerModelClassificationResult
{
    public required string SourceFastFile { get; init; }
    public required string PcDumpRoot { get; init; }
    public required string PcOatListLog { get; init; }
    public required int DeclaredXModels { get; init; }
    public required int ListedXModels { get; init; }
    public required int AlignedXModels { get; init; }
    public required int CompleteModels { get; init; }
    public required int FailedModels { get; init; }
    public required int ModelsWithBones { get; init; }
    public required int TotalBones { get; init; }
    public required int ResolvedSidecarModels { get; init; }
    public required bool UsedResolvedSidecars { get; init; }
    public required bool Complete { get; init; }
    public required IReadOnlyList<T4XModelPerModelClassificationEntry> Models { get; init; }
    public required string OutputDirectory { get; init; }
    public required string ManifestFile { get; init; }
}

/// <summary>
/// Recovers PC XModel partClassification[] per model and pairs it with the
/// canonical GLTF bone names emitted by OAT. This intentionally avoids a global
/// bone-name classification table: WaW zones can classify the same bone name
/// differently in different models, while OAT's importer consumes one CSV.
/// Production conversion therefore rebuilds each XModel with its own CSV.
/// </summary>
public static class T4XModelPerModelClassificationExtractor
{
    private const uint Following = 0xFFFFFFFFu;
    private const uint Insert = 0xFFFFFFFEu;
    private const int XModelFixedSize = 0xE4;

    private sealed record ResolvedClassificationBone(
        int Index,
        string Name,
        byte Classification);

    private sealed record ResolvedClassificationModel(
        string Name,
        int NumBones,
        IReadOnlyList<ResolvedClassificationBone> Bones);

    private static readonly string[] HitLocations =
    {
        "none",
        "helmet",
        "head",
        "neck",
        "torso_upper",
        "torso_lower",
        "right_arm_upper",
        "left_arm_upper",
        "right_arm_lower",
        "left_arm_lower",
        "right_hand",
        "left_hand",
        "right_leg_upper",
        "left_leg_upper",
        "right_leg_lower",
        "left_leg_lower",
        "right_foot",
        "left_foot",
        "gun"
    };

    public static async Task<T4XModelPerModelClassificationResult> RunAsync(
        string sourceFastFile,
        string pcDumpRoot,
        string pcOatListLog,
        string outputDirectory,
        string manifestFile,
        string? xanimProofFile = null,
        string? weaponOwnedProofFile = null,
        CancellationToken cancellationToken = default)
    {
        sourceFastFile = Path.GetFullPath(sourceFastFile);
        pcDumpRoot = Path.GetFullPath(pcDumpRoot);
        pcOatListLog = Path.GetFullPath(pcOatListLog);
        outputDirectory = Path.GetFullPath(outputDirectory);
        manifestFile = Path.GetFullPath(manifestFile);

        if (!File.Exists(sourceFastFile))
            throw new FileNotFoundException("PC fastfile not found.", sourceFastFile);
        if (!Directory.Exists(pcDumpRoot))
            throw new DirectoryNotFoundException($"PC OAT dump root not found: {pcDumpRoot}");
        if (!File.Exists(pcOatListLog))
            throw new FileNotFoundException("PC OAT --list log not found.", pcOatListLog);

        var cached = await FastFileAnalysisCache.GetZoneAsync(sourceFastFile, cancellationToken);
        if (cached.Platform != WaWPlatform.PC || cached.Document.Endianness != Endian.Little)
            throw new InvalidDataException(
                $"Per-model classification recovery requires PC/little-endian T4 source; parsed {cached.Platform}/{cached.Document.Endianness}.");

        var zone = cached.Bytes;
        var document = cached.Document;
        var declared = document.Assets.Count(a =>
            a.TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase));

        var aligned = T4MixedAssetOrderProbe.SelectAlignedXModels(
            zone,
            document,
            xanimProofFile,
            weaponOwnedProofFile,
            cancellationToken);

        var mappings = aligned.Mappings.OrderBy(x => x.AssetIndex).ToList();
        if (aligned.ExpectedXModel != declared ||
            aligned.MatchedXModel != declared ||
            aligned.MissingXModel != 0 ||
            mappings.Count != declared)
        {
            throw new InvalidDataException(
                $"Incomplete XModel alignment: declared={declared:N0}, expected={aligned.ExpectedXModel:N0}, " +
                $"matched={aligned.MatchedXModel:N0}, missing={aligned.MissingXModel:N0}, mappings={mappings.Count:N0}.");
        }

        var listedNames = ParseXModelNames(await File.ReadAllTextAsync(pcOatListLog, cancellationToken));
        // OAT --list can enumerate XModels outside the source root XAsset table
        // (e.g. models brought in by other loaded assets). For the exact
        // N==N case, preserve the established original source/OAT ordinal path.
        // For N>declared, do NOT trim 14 entries or shift source ordinals:
        // the typed mixed-order probe has already recovered each source-owned
        // XModel's exact zone offset AND source name, which must be present in
        // OAT's full list with at least the same occurrence multiplicity.
        var rootNames = ReconcileSourceModelNames(mappings, listedNames, declared);

        var resolvedSidecars = LoadResolvedClassificationCorpus(pcDumpRoot);

        Directory.CreateDirectory(outputDirectory);
        var entries = new List<T4XModelPerModelClassificationEntry>(declared);
        var modelsWithBones = 0;
        var totalBones = 0;

        for (var ordinal = 0; ordinal < declared; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mapping = mappings[ordinal];
            var name = rootNames[ordinal];
            var modelDir = Path.Combine(outputDirectory, $"{ordinal:D3}_{SanitizeFileName(name)}");
            Directory.CreateDirectory(modelDir);
            var csvFile = Path.Combine(modelDir, "partclassification.csv");

            try
            {
                byte[] classifications;
                IReadOnlyList<string>? resolvedBoneNames = null;
                int numBones;

                if (resolvedSidecars.TryGetValue(name, out var resolved))
                {
                    numBones = resolved.NumBones;
                    if (resolved.Bones.Count != numBones)
                        throw new InvalidDataException(
                            $"Resolved OAT sidecar for '{name}' has {resolved.Bones.Count:N0} bone rows; expected {numBones:N0}.");

                    var ordered = resolved.Bones.OrderBy(x => x.Index).ToArray();
                    for (var i = 0; i < ordered.Length; i++)
                    {
                        if (ordered[i].Index != i)
                            throw new InvalidDataException(
                                $"Resolved OAT sidecar for '{name}' has non-contiguous bone index {ordered[i].Index} at row {i}.");
                    }

                    resolvedBoneNames = ordered.Select(x => x.Name).ToArray();
                    classifications = ordered.Select(x => x.Classification).ToArray();
                }
                else
                {
                    classifications = ReadClassificationVector(zone, mapping.Offset, out numBones);
                }

                if (numBones > 0)
                {
                    modelsWithBones++;
                    totalBones += numBones;
                }

                var boneNames = LoadCanonicalImportedBoneNames(pcDumpRoot, name, numBones);
                if (boneNames.Count != numBones)
                    throw new InvalidDataException(
                        $"OAT canonical skeleton for '{name}' has {boneNames.Count:N0} effective bones; PC XModel body declares {numBones:N0}.");

                if (classifications.Length != numBones)
                    throw new InvalidDataException(
                        $"PC classification vector for '{name}' has {classifications.Length:N0} entries; expected {numBones:N0}.");

                if (resolvedBoneNames is not null)
                {
                    if (resolvedBoneNames.Count != boneNames.Count)
                        throw new InvalidDataException(
                            $"Resolved source/canonical bone-count mismatch for '{name}': {resolvedBoneNames.Count:N0} != {boneNames.Count:N0}.");

                    // OAT applies partclassification.csv by bone name after GLTF import,
                    // not by source index. GLTF loading may legitimately reorder bones
                    // into XModel parent-before-child order, so require name-multiset
                    // equivalence rather than index-for-index equality.
                    var sourceNameCounts = resolvedBoneNames
                        .GroupBy(x => x, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                    var canonicalNameCounts = boneNames
                        .GroupBy(x => x, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

                    if (sourceNameCounts.Count != canonicalNameCounts.Count ||
                        sourceNameCounts.Any(pair =>
                            !canonicalNameCounts.TryGetValue(pair.Key, out var count) ||
                            count != pair.Value))
                    {
                        var sourceOnly = sourceNameCounts
                            .Where(pair => !canonicalNameCounts.TryGetValue(pair.Key, out var count) || count != pair.Value)
                            .Select(pair => $"{pair.Key}x{pair.Value}")
                            .Take(8);
                        var canonicalOnly = canonicalNameCounts
                            .Where(pair => !sourceNameCounts.TryGetValue(pair.Key, out var count) || count != pair.Value)
                            .Select(pair => $"{pair.Key}x{pair.Value}")
                            .Take(8);

                        throw new InvalidDataException(
                            $"Resolved source/canonical bone-name multiset mismatch for '{name}'. " +
                            $"source-only/different=[{string.Join(", ", sourceOnly)}] " +
                            $"canonical-only/different=[{string.Join(", ", canonicalOnly)}].");
                    }
                }

                var local = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < numBones; i++)
                {
                    var boneName = boneNames[i];
                    var cls = classifications[i];

                    // OAT's canonical default armature intentionally uses an empty
                    // root-bone name. Preserve it: PartClassificationState defaults
                    // unknown/empty names to HITLOC_NONE, so a nonzero source class
                    // for that unnamed root would be a real fidelity blocker.
                    if (boneName.Length == 0 && cls != 0)
                        throw new InvalidDataException(
                            $"PC XModel '{name}' has unnamed canonical root bone with nonzero classification " +
                            $"{cls}({HitLocations[cls]}), which cannot be represented safely by OAT's default-armature lookup.");
                    if (cls >= HitLocations.Length)
                        throw new InvalidDataException(
                            $"PC XModel '{name}' bone[{i}] '{boneName}' has invalid T4 hit-location classification {cls}.");

                    if (local.TryGetValue(boneName, out var prior) && prior != cls)
                        throw new InvalidDataException(
                            $"PC XModel '{name}' repeats bone '{boneName}' with conflicting classifications " +
                            $"{prior}({HitLocations[prior]}) and {cls}({HitLocations[cls]}).");

                    local[boneName] = cls;
                }

                var sb = new StringBuilder();
                foreach (var pair in local
                    .Where(x => x.Key.Length != 0)
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    sb.Append(CsvEscape(pair.Key.ToLowerInvariant()));
                    sb.Append(',');
                    sb.Append(CsvEscape(HitLocations[pair.Value]));
                    sb.AppendLine();
                }
                await File.WriteAllTextAsync(csvFile, sb.ToString(), new UTF8Encoding(false), cancellationToken);

                entries.Add(new T4XModelPerModelClassificationEntry(
                    ordinal,
                    mapping.AssetIndex,
                    name,
                    mapping.Offset,
                    numBones,
                    classifications.Length,
                    local.Count,
                    true,
                    csvFile,
                    null));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or ArgumentOutOfRangeException)
            {
                entries.Add(new T4XModelPerModelClassificationEntry(
                    ordinal,
                    mapping.AssetIndex,
                    name,
                    mapping.Offset,
                    zone[mapping.Offset + 4],
                    0,
                    0,
                    false,
                    csvFile,
                    ex.Message));
            }
        }

        var failed = entries.Count(x => !x.Complete);
        var result = new T4XModelPerModelClassificationResult
        {
            SourceFastFile = sourceFastFile,
            PcDumpRoot = pcDumpRoot,
            PcOatListLog = pcOatListLog,
            DeclaredXModels = declared,
            ListedXModels = listedNames.Count,
            AlignedXModels = mappings.Count,
            CompleteModels = entries.Count - failed,
            FailedModels = failed,
            ModelsWithBones = modelsWithBones,
            TotalBones = totalBones,
            ResolvedSidecarModels = resolvedSidecars.Count,
            UsedResolvedSidecars = resolvedSidecars.Count != 0,
            Complete = failed == 0 && entries.Count == declared,
            Models = entries,
            OutputDirectory = outputDirectory,
            ManifestFile = manifestFile
        };

        Directory.CreateDirectory(Path.GetDirectoryName(manifestFile)!);
        await File.WriteAllTextAsync(
            manifestFile,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        return result;
    }

    private static Dictionary<string, ResolvedClassificationModel> LoadResolvedClassificationCorpus(string pcDumpRoot)
    {
        var root = Path.Combine(pcDumpRoot, "xmodel_classification");
        var output = new Dictionary<string, ResolvedClassificationModel>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
            return output;

        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var json = doc.RootElement;

            if (!json.TryGetProperty("format", out var format) ||
                !string.Equals(format.GetString(), "WaW360.T4.XModelClassification.v1", StringComparison.Ordinal))
                continue;

            var name = json.GetProperty("name").GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException($"Resolved XModel classification sidecar has an empty name: {file}");

            var numBones = json.GetProperty("numBones").GetInt32();
            var bones = new List<ResolvedClassificationBone>();
            if (json.TryGetProperty("bones", out var boneArray) && boneArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var bone in boneArray.EnumerateArray())
                {
                    var index = bone.GetProperty("index").GetInt32();
                    var boneName = bone.GetProperty("name").GetString() ?? string.Empty;
                    var classificationValue = bone.GetProperty("classification").GetInt32();
                    if (classificationValue < 0 || classificationValue >= HitLocations.Length)
                        throw new InvalidDataException(
                            $"Resolved XModel classification sidecar '{name}' has invalid classification {classificationValue} at bone {index}.");
                    bones.Add(new ResolvedClassificationBone(index, boneName, (byte)classificationValue));
                }
            }

            var model = new ResolvedClassificationModel(name, numBones, bones);
            if (!output.TryAdd(name, model))
                throw new InvalidDataException($"Duplicate resolved XModel classification sidecar for '{name}'.");
        }

        return output;
    }

    private static byte[] ReadClassificationVector(byte[] zone, int start, out int numBones)
    {
        Ensure(zone, start, XModelFixedSize, "XModel fixed body");

        numBones = zone[start + 4];
        var numRootBones = zone[start + 5];
        if (numRootBones > numBones)
            throw new InvalidDataException(
                $"XModel at 0x{start:X8} has numRootBones={numRootBones} > numBones={numBones}.");

        if (numBones == 0)
            return Array.Empty<byte>();

        var namePtr = ReadU32(zone, start + 0);
        var boneNamesPtr = ReadU32(zone, start + 8);
        var parentPtr = ReadU32(zone, start + 12);
        var quatsPtr = ReadU32(zone, start + 16);
        var transPtr = ReadU32(zone, start + 20);
        var partClassPtr = ReadU32(zone, start + 24);

        var cursor = checked(start + XModelFixedSize);

        if (IsInline(namePtr))
            cursor = SkipCString(zone, cursor);

        if (IsInline(boneNamesPtr))
            cursor = Advance(zone, cursor, checked(numBones * 2), "XModel boneNames");

        var childBones = numBones - numRootBones;
        if (IsInline(parentPtr))
            cursor = Advance(zone, cursor, childBones, "XModel parentList");
        if (IsInline(quatsPtr))
            cursor = Advance(zone, cursor, checked(childBones * 8), "XModel quats");
        if (IsInline(transPtr))
            cursor = Advance(zone, cursor, checked(childBones * 16), "XModel trans");

        if (!IsInline(partClassPtr))
            throw new InvalidDataException(
                $"XModel at 0x{start:X8} uses non-inline partClassification pointer 0x{partClassPtr:X8}; " +
                "per-model fidelity recovery refuses to guess the reusable-array target.");

        Ensure(zone, cursor, numBones, "XModel partClassification");
        return zone.AsSpan(cursor, numBones).ToArray();
    }

    private static IReadOnlyList<string> LoadCanonicalImportedBoneNames(string pcDumpRoot, string assetName, int pcBoneCount)
    {
        var jsonPath = Path.Combine(
            pcDumpRoot,
            "xmodel",
            assetName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar) + ".json");

        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"Exported OAT XModel JSON not found for '{assetName}'.", jsonPath);

        using var modelDoc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var modelRoot = modelDoc.RootElement;
        if (!modelRoot.TryGetProperty("_type", out var type) ||
            !string.Equals(type.GetString(), "xmodel", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Exported JSON for '{assetName}' is not an xmodel.");

        if (!modelRoot.TryGetProperty("lods", out var lods) ||
            lods.ValueKind != JsonValueKind.Array ||
            lods.GetArrayLength() == 0)
            throw new InvalidDataException($"Exported XModel '{assetName}' has no LOD files.");

        var lodArray = lods.EnumerateArray().ToArray();
        var lodFile = lodArray[0].GetProperty("file").GetString();
        if (string.IsNullOrWhiteSpace(lodFile))
            throw new InvalidDataException($"Exported XModel '{assetName}' has an empty first LOD path.");

        var gltfPath = Path.Combine(
            pcDumpRoot,
            lodFile.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

        if (!File.Exists(gltfPath))
            throw new FileNotFoundException($"Exported GLTF for '{assetName}' not found.", gltfPath);

        using var gltfDoc = JsonDocument.Parse(File.ReadAllText(gltfPath));
        var root = gltfDoc.RootElement;

        if (!root.TryGetProperty("skins", out var skins) ||
            skins.ValueKind != JsonValueKind.Array ||
            skins.GetArrayLength() == 0)
        {
            // OAT intentionally omits a trivial one-bone/default armature from
            // GLTF/GLB. LoaderXModel::AutoGenerateArmature restores it on import,
            // using JsonXModel.rootBoneName or the default empty root name.
            if (pcBoneCount == 1)
            {
                var rootBoneName = modelRoot.TryGetProperty("rootBoneName", out var rootBoneElement)
                    ? rootBoneElement.GetString() ?? string.Empty
                    : string.Empty;
                return new[] { rootBoneName };
            }

            return Array.Empty<string>();
        }

        if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"GLTF for '{assetName}' has a skin but no nodes array.");

        var skinArray = skins.EnumerateArray().ToArray();
        var skin = skinArray[0];
        if (!skin.TryGetProperty("joints", out var jointsElement) ||
            jointsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"GLTF for '{assetName}' skin has no joints array.");

        var joints = jointsElement.EnumerateArray().Select(x => x.GetInt32()).ToArray();
        if (joints.Length == 0)
            return Array.Empty<string>();

        var nodeArray = nodes.EnumerateArray().ToArray();
        var jointPositionByNode = new Dictionary<int, int>();
        for (var i = 0; i < joints.Length; i++)
        {
            if (joints[i] < 0 || joints[i] >= nodeArray.Length)
                throw new InvalidDataException($"GLTF for '{assetName}' joint[{i}] references invalid node {joints[i]}.");
            if (!jointPositionByNode.TryAdd(joints[i], i))
                throw new InvalidDataException($"GLTF for '{assetName}' repeats node {joints[i]} in skin.joints.");
        }

        var names = new string[joints.Length];
        var parent = Enumerable.Repeat(-1, joints.Length).ToArray();

        for (var i = 0; i < joints.Length; i++)
        {
            var node = nodeArray[joints[i]];
            names[i] = node.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;

            if (!node.TryGetProperty("children", out var children) ||
                children.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var childElement in children.EnumerateArray())
            {
                var childNode = childElement.GetInt32();
                if (!jointPositionByNode.TryGetValue(childNode, out var childPosition))
                    continue;
                if (parent[childPosition] != -1)
                    throw new InvalidDataException(
                        $"GLTF for '{assetName}' joint node {childNode} has multiple joint parents.");
                parent[childPosition] = i;
            }
        }

        // Mirror OAT GltfLoader::ReorderBonesForXModels exactly:
        // roots in joint-index order, then breadth-first children in joint-index order.
        var reordered = new List<int>(joints.Length);
        var queue = new Queue<int>();
        for (var i = 0; i < parent.Length; i++)
        {
            if (parent[i] != -1) continue;
            reordered.Add(i);
            queue.Enqueue(i);
        }

        while (queue.Count != 0)
        {
            var parentIndex = queue.Dequeue();
            for (var i = 0; i < parent.Length; i++)
            {
                if (parent[i] != parentIndex) continue;
                reordered.Add(i);
                queue.Enqueue(i);
            }
        }

        if (reordered.Count != joints.Length || reordered.Distinct().Count() != joints.Length)
            throw new InvalidDataException(
                $"GLTF for '{assetName}' did not form a complete XModel-compatible joint forest.");

        return reordered.Select(i => names[i]).ToArray();
    }

    /// <summary>
    /// Assign source-root names using the exact PC XModel mapping recovered
    /// from source bytes, not raw OAT asset-list position when additional
    /// models are printed by the PC Unlinker. This does NOT infer an identity
    /// from the unordered extras or authorize a missing source root.
    /// </summary>
    private static List<string> ReconcileSourceModelNames(
        IReadOnlyList<T4AlignedXModelMapping> mappings,
        IReadOnlyList<string> listedNames,
        int declared)
    {
        if (mappings.Count != declared)
            throw new InvalidDataException(
                $"PC source XModel mapping cardinality changed: mapped={mappings.Count}, declared={declared}.");

        if (listedNames.Count == declared)
            return listedNames.ToList(); // retain historical exact source/OAT order

        if (listedNames.Count < declared)
            throw new InvalidDataException(
                $"PC OAT --list exposed only {listedNames.Count:N0} XModels for {declared:N0} source XAsset roots.");

        // Asset names are compared without case sensitivity by the
        // existing OAT/PC loaders; preserve source spelling for the actual
        // classification CSV and JSON paths.
        var expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.ObservedName))
                throw new InvalidDataException(
                    $"Source XModel at asset #{mapping.AssetIndex} / 0x{mapping.Offset:X8} has no recovered name; " +
                    "cannot reconcile additional OAT-listed models.");

            if (mapping.Offset < 0 || mapping.End <= mapping.Offset ||
                string.IsNullOrWhiteSpace(mapping.Source))
                throw new InvalidDataException(
                    $"Source XModel at asset #{mapping.AssetIndex} has an incomplete typed source witness.");

            var name = mapping.ObservedName;
            expected.TryGetValue(name, out var count);
            expected[name] = checked(count + 1);
        }

        var available = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in listedNames)
        {
            available.TryGetValue(name, out var count);
            available[name] = checked(count + 1);
        }

        var missing = expected
            .Where(p => !available.TryGetValue(p.Key, out var count) || count < p.Value)
            .Select(p => p.Key + " expected=" + p.Value +
                " listed=" + (available.TryGetValue(p.Key, out var count) ? count : 0))
            .Take(12)
            .ToArray();
        if (missing.Length != 0)
            throw new InvalidDataException(
                $"PC source-owned XModel names cannot be matched to OAT --list " +
                $"({listedNames.Count:N0} listed vs {declared:N0} actual roots). " +
                $"Missing/underrepresented source identities: [{string.Join("; ", missing)}]. " +
                "No positional trimming or guest-name substitution is permitted.");

        var additional = new Dictionary<string, int>(available, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, count) in expected)
            additional[name] -= count;
        var extra = additional.Values.Sum();
        if (extra != listedNames.Count - declared || additional.Values.Any(n => n < 0))
            throw new InvalidDataException(
                "PC OAT additional XModel occurrence multiset did not reconcile with source-owned names.");

        Console.WriteLine(
            $"[XMODEL-CLASS] OAT listed {listedNames.Count:N0} XModels; " +
            $"source XAsset roots={declared:N0}; exact source-name occurrence " +
            $"reconciliation PASS; additional OAT rows={extra:N0}.");
        Console.WriteLine(
            "[XMODEL-CLASS] Unattributed extra OAT XModel names (first 12): " +
            string.Join(" | ", additional
                .Where(p => p.Value > 0)
                .Take(12)
                .Select(p => $"{p.Key} x{p.Value}")));
        Console.WriteLine(
            "[XMODEL-CLASS] Source-owned XModel ordinals and offsets remain " +
            "those independently recovered from original PC zone bytes.");

        return mappings.Select(m => m.ObservedName).ToList();
    }

    private static List<string> ParseXModelNames(string log)
    {
        var output = new List<string>();
        using var reader = new StringReader(log);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = StripLogPrefix(line.Trim());
            var comma = trimmed.IndexOf(',');
            if (comma <= 0) continue;

            var type = trimmed[..comma].Trim();
            if (!type.Equals("xmodel", StringComparison.OrdinalIgnoreCase))
                continue;

            output.Add(trimmed[(comma + 1)..].Trim());
        }
        return output;
    }

    private static string StripLogPrefix(string line)
    {
        if (line.StartsWith("[", StringComparison.Ordinal))
        {
            var close = line.IndexOf(']');
            if (close >= 0 && close + 1 < line.Length)
                return line[(close + 1)..].TrimStart();
        }

        var colon = line.IndexOf(':');
        if (colon > 0 && colon < 12)
        {
            var prefix = line[..colon];
            if (prefix.All(ch => char.IsLetter(ch) || char.IsWhiteSpace(ch)))
                return line[(colon + 1)..].TrimStart();
        }

        return line;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (invalid.Contains(ch) || ch is '/' or '\\' or ':')
                sb.Append('_');
            else
                sb.Append(ch);
        }

        var sanitized = sb.ToString().Trim();
        if (sanitized.Length == 0)
            sanitized = "unnamed";
        if (sanitized.Length > 96)
            sanitized = sanitized[..96];
        return sanitized;
    }

    private static bool IsInline(uint value) => value is Following or Insert;

    private static uint ReadU32(byte[] data, int offset)
    {
        Ensure(data, offset, 4, "uint32");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private static int SkipCString(byte[] data, int cursor)
    {
        if (cursor < 0 || cursor >= data.Length)
            throw new InvalidDataException($"XModel inline name starts outside zone at 0x{cursor:X8}.");

        var max = Math.Min(data.Length, cursor + 1024 * 1024);
        while (cursor < max && data[cursor] != 0)
            cursor++;

        if (cursor >= max || data[cursor] != 0)
            throw new InvalidDataException("XModel inline name is unterminated.");

        return cursor + 1;
    }

    private static int Advance(byte[] data, int cursor, int count, string label)
    {
        Ensure(data, cursor, count, label);
        return checked(cursor + count);
    }

    private static void Ensure(byte[] data, int offset, int count, string label)
    {
        if (offset < 0 || count < 0 || (long)offset + count > data.Length)
            throw new InvalidDataException(
                $"{label} at 0x{offset:X8} (+0x{count:X}) exceeds decompressed zone length 0x{data.Length:X}.");
    }

    private static string CsvEscape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
