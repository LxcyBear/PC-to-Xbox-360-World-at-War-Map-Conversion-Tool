using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed record T4XModelPartClassificationRow(
    string BoneName,
    byte Classification,
    string HitLocation,
    int Observations);

public sealed class T4XModelPartClassificationResult
{
    public required string SourceFastFile { get; init; }
    public required int DeclaredXModels { get; init; }
    public required int AlignedXModels { get; init; }
    public required int ModelsWithBones { get; init; }
    public required int ModelsObserved { get; init; }
    public required int ModelsSkippedPackedBoneNames { get; init; }
    public required int ModelsSkippedPackedClassification { get; init; }
    public required int BoneObservations { get; init; }
    public required int ResolvedBoneObservations { get; init; }
    public required int UnresolvedScriptStrings { get; init; }
    public required int Conflicts { get; init; }
    public required bool Complete { get; init; }
    public required IReadOnlyList<T4XModelPartClassificationRow> Rows { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required string CsvFile { get; init; }
    public required string ManifestFile { get; init; }
}

/// <summary>
/// NUCLEAR Stage 4V prerequisite: recover WaW's original XModel
/// partClassification[] mapping from the PC fastfile itself.
///
/// OAT's canonical XModel export intentionally reconstructs partClassification
/// from partclassification.csv during import. This extractor derives that CSV
/// from the authoritative PC XModel skeleton arrays, preserving gameplay hit
/// locations without a retail-PC install or Xbox donor payload.
/// </summary>
public static class T4XModelPartClassificationExtractor
{
    private const uint Following = 0xFFFFFFFFu;
    private const uint Insert = 0xFFFFFFFEu;
    private const int XModelFixedSize = 0xE4;

    // Must match T4/T4Xenon XModelConstants HITLOC_NAMES exactly.
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

    private sealed class Observation
    {
        public required byte Classification { get; init; }
        public int Count { get; set; }
        public HashSet<int> SourceModels { get; } = new();
    }

    public static async Task<T4XModelPartClassificationResult> RunAsync(
        string sourceFastFile,
        string csvFile,
        string manifestFile,
        string? xanimProofFile = null,
        string? weaponOwnedProofFile = null,
        CancellationToken cancellationToken = default)
    {
        sourceFastFile = Path.GetFullPath(sourceFastFile);
        csvFile = Path.GetFullPath(csvFile);
        manifestFile = Path.GetFullPath(manifestFile);

        if (!File.Exists(sourceFastFile))
            throw new FileNotFoundException("PC fastfile not found.", sourceFastFile);

        var cached = await FastFileAnalysisCache.GetZoneAsync(sourceFastFile, cancellationToken);
        if (cached.Platform != WaWPlatform.PC || cached.Document.Endianness != Endian.Little)
            throw new InvalidDataException(
                $"Part-classification extraction requires PC/little-endian T4 source; parsed {cached.Platform}/{cached.Document.Endianness}.");

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

        if (aligned.ExpectedXModel != declared ||
            aligned.MatchedXModel != declared ||
            aligned.MissingXModel != 0)
            throw new InvalidDataException(
                $"Cannot recover part classifications from an incomplete XModel alignment: " +
                $"declared={declared:N0}, expected={aligned.ExpectedXModel:N0}, matched={aligned.MatchedXModel:N0}, missing={aligned.MissingXModel:N0}.");

        var scriptStrings = document.ScriptStrings.ToDictionary(x => x.Index);
        var observations = new Dictionary<string, Observation>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();

        var modelsWithBones = 0;
        var modelsObserved = 0;
        var packedBoneNames = 0;
        var packedPartClass = 0;
        var boneObservations = 0;
        var resolvedBoneObservations = 0;
        var unresolvedStrings = 0;
        var conflicts = 0;

        for (var modelOrdinal = 0; modelOrdinal < aligned.Offsets.Count; modelOrdinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = aligned.Offsets[modelOrdinal];
            Ensure(zone, start, XModelFixedSize, "XModel fixed body");

            var numBones = zone[start + 4];
            var numRootBones = zone[start + 5];
            if (numRootBones > numBones)
                throw new InvalidDataException($"XModel at 0x{start:X8} has numRootBones={numRootBones} > numBones={numBones}.");

            if (numBones == 0)
                continue;

            modelsWithBones++;

            var namePtr = ReadU32(zone, start + 0);
            var boneNamesPtr = ReadU32(zone, start + 8);
            var parentPtr = ReadU32(zone, start + 12);
            var quatsPtr = ReadU32(zone, start + 16);
            var transPtr = ReadU32(zone, start + 20);
            var partClassPtr = ReadU32(zone, start + 24);

            var cursor = checked(start + XModelFixedSize);
            if (IsInline(namePtr))
                cursor = SkipCString(zone, cursor);

            if (!IsInline(boneNamesPtr))
            {
                packedBoneNames++;
                diagnostics.Add($"model#{modelOrdinal} @0x{start:X8}: boneNames uses packed pointer 0x{boneNamesPtr:X8}");
                continue;
            }

            Ensure(zone, cursor, checked(numBones * 2), "XModel boneNames");
            var boneIds = new ushort[numBones];
            for (var i = 0; i < numBones; i++)
                boneIds[i] = BinaryPrimitives.ReadUInt16LittleEndian(zone.AsSpan(cursor + i * 2, 2));
            cursor += numBones * 2;

            var childBones = numBones - numRootBones;
            if (IsInline(parentPtr))
                cursor = Advance(zone, cursor, childBones, "XModel parentList");
            if (IsInline(quatsPtr))
                cursor = Advance(zone, cursor, checked(childBones * 8), "XModel quats");
            if (IsInline(transPtr))
                cursor = Advance(zone, cursor, checked(childBones * 16), "XModel trans");

            if (!IsInline(partClassPtr))
            {
                packedPartClass++;
                diagnostics.Add($"model#{modelOrdinal} @0x{start:X8}: partClassification uses packed pointer 0x{partClassPtr:X8}");
                continue;
            }

            Ensure(zone, cursor, numBones, "XModel partClassification");
            modelsObserved++;

            for (var i = 0; i < numBones; i++)
            {
                boneObservations++;
                var classification = zone[cursor + i];
                if (classification >= HitLocations.Length)
                    throw new InvalidDataException(
                        $"XModel model#{modelOrdinal} bone[{i}] has invalid T4 hit-location classification {classification}.");

                if (!scriptStrings.TryGetValue(boneIds[i], out var script) ||
                    string.IsNullOrWhiteSpace(script.Value) ||
                    script.Value.StartsWith("<alias ", StringComparison.Ordinal))
                {
                    unresolvedStrings++;
                    diagnostics.Add(
                        $"model#{modelOrdinal} @0x{start:X8} bone[{i}]: ScriptString #{boneIds[i]} unresolved.");
                    continue;
                }

                var boneName = script.Value;
                resolvedBoneObservations++;

                if (!observations.TryGetValue(boneName, out var observed))
                {
                    observed = new Observation { Classification = classification, Count = 0 };
                    observations.Add(boneName, observed);
                }
                else if (observed.Classification != classification)
                {
                    conflicts++;
                    diagnostics.Add(
                        $"CONFLICT bone '{boneName}': existing={observed.Classification}({HitLocations[observed.Classification]}) " +
                        $"new={classification}({HitLocations[classification]}) at model#{modelOrdinal}.");
                    continue;
                }

                observed.Count++;
                observed.SourceModels.Add(modelOrdinal);
            }
        }

        var rows = observations
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new T4XModelPartClassificationRow(
                x.Key,
                x.Value.Classification,
                HitLocations[x.Value.Classification],
                x.Value.Count))
            .ToList();

        // Production fidelity is fail-closed. Every model with bones must expose
        // both arrays, all ScriptStrings must resolve, and the global mapping
        // must be conflict-free.
        var complete =
            aligned.MatchedXModel == declared &&
            modelsObserved == modelsWithBones &&
            packedBoneNames == 0 &&
            packedPartClass == 0 &&
            unresolvedStrings == 0 &&
            conflicts == 0 &&
            resolvedBoneObservations == boneObservations;

        Directory.CreateDirectory(Path.GetDirectoryName(csvFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestFile)!);

        // OAT defaults absent names to HITLOC_NONE. Emit all observed mappings,
        // including explicit none, so the artifact is auditable against the PC FF.
        var csv = new StringBuilder();
        foreach (var row in rows)
        {
            var safeBone = CsvEscape(row.BoneName.ToLowerInvariant());
            csv.Append(safeBone);
            csv.Append(',');
            csv.Append(CsvEscape(row.HitLocation));
            csv.AppendLine();
        }
        await File.WriteAllTextAsync(csvFile, csv.ToString(), new UTF8Encoding(false), cancellationToken);

        var result = new T4XModelPartClassificationResult
        {
            SourceFastFile = sourceFastFile,
            DeclaredXModels = declared,
            AlignedXModels = aligned.MatchedXModel,
            ModelsWithBones = modelsWithBones,
            ModelsObserved = modelsObserved,
            ModelsSkippedPackedBoneNames = packedBoneNames,
            ModelsSkippedPackedClassification = packedPartClass,
            BoneObservations = boneObservations,
            ResolvedBoneObservations = resolvedBoneObservations,
            UnresolvedScriptStrings = unresolvedStrings,
            Conflicts = conflicts,
            Complete = complete,
            Rows = rows,
            Diagnostics = diagnostics,
            CsvFile = csvFile,
            ManifestFile = manifestFile
        };

        await File.WriteAllTextAsync(
            manifestFile,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        return result;
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
