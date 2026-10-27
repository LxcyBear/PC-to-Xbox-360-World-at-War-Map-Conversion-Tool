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
        if (listedNames.Count != declared)
            throw new InvalidDataException(
                $"PC OAT --list exposed {listedNames.Count:N0} XModels, but the source XAsset table declares {declared:N0}.");

        Directory.CreateDirectory(outputDirectory);
        var entries = new List<T4XModelPerModelClassificationEntry>(declared);
        var modelsWithBones = 0;
        var totalBones = 0;

        for (var ordinal = 0; ordinal < declared; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mapping = mappings[ordinal];
            var name = listedNames[ordinal];
            var modelDir = Path.Combine(outputDirectory, $"{ordinal:D3}_{SanitizeFileName(name)}");
            Directory.CreateDirectory(modelDir);
            var csvFile = Path.Combine(modelDir, "partclassification.csv");

            try
            {
                var classifications = ReadClassificationVector(zone, mapping.Offset, out var numBones);
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

                var local = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < numBones; i++)
                {
                    var boneName = boneNames[i];
                    // OAT's canonical default armature intentionally uses an empty
                    // root-bone name. Preserve it: PartClassificationState defaults
                    // unknown/empty names to HITLOC_NONE, so a nonzero source class
                    // for that unnamed root would be a real fidelity blocker.
                    if (boneName.Length == 0 && cls != 0)
                        throw new InvalidDataException(
                            $"PC XModel '{name}' has unnamed canonical root bone with nonzero classification " +
                            $"{cls}({HitLocations[cls]}), which cannot be represented safely by OAT's default-armature lookup.");

                    var cls = classifications[i];
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
