using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Maps;

public sealed record T4NativeWritePlanAsset(
    int XAssetIndex,
    string TypeName,
    int XboxTypeId,
    T4AssetReferenceKind ReferenceKind,
    uint SourceRawHeader,
    long? SourceBodyOffset,
    string? ProofContainer,
    long? ProofBlobOffset,
    int BlobLength,
    T4ZoneBlock DefaultBlock,
    bool RequiresSplitBlockTraversal);

public sealed record T4NativeWritePlanFastFile(
    string Role,
    string Source,
    string SourceSha256,
    string PlanFile,
    string BinaryPlanFile,
    int DeclaredAssets,
    int PlannedTrackedAssets,
    int PhysicalTrackedAssets,
    int AliasTrackedAssets,
    long PlannedPayloadBytes,
    int UnhandledAssetClasses,
    int UnhandledAssets,
    IReadOnlyList<string> UnhandledTypes,
    bool Verified);

public sealed class NormalMapNativeWriterFoundationResult
{
    public required string MapName { get; init; }
    public required string Workspace { get; init; }
    public required IReadOnlyList<T4NativeWritePlanFastFile> FastFiles { get; init; }
    public required int PlannedTrackedAssets { get; init; }
    public required int PhysicalTrackedAssets { get; init; }
    public required int AliasTrackedAssets { get; init; }
    public required long PlannedPayloadBytes { get; init; }
    public required int UnhandledAssetClasses { get; init; }
    public required int UnhandledAssets { get; init; }
    public required bool Verified { get; init; }
    public required bool HardwareReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Stage 5a / alpha 10a: native writer foundation.
///
/// This is deliberately a zero-copy WRITE PLAN rather than a game .ff. It merges the
/// already-read-back-verified Xbox big-endian XAnim/XModel/Weapon/FX proof blobs into
/// authoritative XAsset-table order and records the exact proof-file byte ranges that
/// a final streaming writer will consume. This avoids duplicating hundreds of megabytes
/// under the user's OneDrive output folder while proving the writer's ordering/type-map
/// layer now.
///
/// Unsupported asset classes remain explicit blockers. No PC payload is silently copied
/// into a native Xbox zone.
/// </summary>
public static class NormalMapNativeWriterFoundation
{
    private static readonly HashSet<string> TrackedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "xanim", "xmodel", "weapon", "fx"
    };

    private static readonly byte[] BinaryMagic = Encoding.ASCII.GetBytes("W360N5A\0");

    public static async Task<NormalMapNativeWriterFoundationResult> RunAsync(
        MapCandidate map,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var analysis = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot, cancellationToken);
        if (!analysis.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{map.Name}' classified as {analysis.Profile}. Stage 5a native writer foundation accepts Normal maps only.");

        var workspace = Path.Combine(outputRoot, map.Name, "stage5_alpha10a_native_writer");
        Directory.CreateDirectory(workspace);

        var ffResults = new List<T4NativeWritePlanFastFile>();
        foreach (var ff in analysis.FastFiles.Where(x => x.Readable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await FastFileAnalysisCache.GetZoneAsync(ff.Path, cancellationToken);
            var document = cached.Document;

            var proofIndexes = new Dictionary<string, T4ProofContainerIndex>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in TrackedTypes)
            {
                var physicalNeeded = document.Assets.Count(a =>
                    a.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                    a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared);
                if (physicalNeeded == 0) continue;

                var proofPath = GetProofPath(outputRoot, map.Name, ff.Path, ff.Role, type);
                var proof = await T4NativeProofContainerReader.IndexAsync(proofPath, type, cancellationToken);
                if (type.Equals("xanim", StringComparison.OrdinalIgnoreCase) && proof.Version != 12)
                    throw new InvalidDataException(
                        $"Stage 5a refuses stale XAnim proof version {proof.Version} in '{proofPath}'. " +
                        "Run the native Xbox XAnim proof so Stage 5 consumes 0x5C/12-bucket payloads.");
                if (proof.Records.Count != physicalNeeded)
                    throw new InvalidDataException(
                        $"Stage 5a '{Path.GetFileName(ff.Path)}' {type} proof/body mismatch: " +
                        $"XAsset table requires {physicalNeeded:N0} physical bod{(physicalNeeded == 1 ? "y" : "ies")}, proof contains {proof.Records.Count:N0}.");
                proofIndexes[type] = proof;
            }

            var queues = proofIndexes.ToDictionary(
                kv => kv.Key,
                kv => new Queue<T4ProofBlobDescriptor>(kv.Value.Records.OrderBy(r => r.SourceOffset)),
                StringComparer.OrdinalIgnoreCase);

            var planned = new List<T4NativeWritePlanAsset>();
            foreach (var asset in document.Assets.OrderBy(a => a.Index))
            {
                if (!TrackedTypes.Contains(asset.TypeName)) continue;
                if (!T4XboxAssetTypeMap.TryGetXboxTypeId(asset.TypeName, out var xboxTypeId))
                    throw new InvalidDataException($"No Xbox asset-type mapping exists for tracked type '{asset.TypeName}'.");

                T4ProofBlobDescriptor? blob = null;
                if (asset.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
                {
                    if (!queues.TryGetValue(asset.TypeName, out var q) || q.Count == 0)
                        throw new InvalidDataException($"Stage 5a ran out of {asset.TypeName} proof bodies at XAsset #{asset.Index}.");
                    blob = q.Dequeue();
                }

                planned.Add(new T4NativeWritePlanAsset(
                    asset.Index,
                    asset.TypeName,
                    xboxTypeId,
                    asset.ReferenceKind,
                    asset.RawHeader,
                    blob?.SourceOffset,
                    blob?.ContainerFile,
                    blob?.ContainerBlobOffset,
                    blob?.BlobLength ?? 0,
                    T4ZoneBlock.Temp,
                    RequiresSplitBlockTraversal(asset.TypeName)));
            }

            foreach (var kv in queues)
                if (kv.Value.Count != 0)
                    throw new InvalidDataException($"Stage 5a left {kv.Value.Count:N0} unconsumed {kv.Key} proof bodies for '{Path.GetFileName(ff.Path)}'.");

            var unhandledGroups = document.Assets
                .Where(a => !TrackedTypes.Contains(a.TypeName))
                .GroupBy(a => a.TypeName, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Type: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Type, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var stem = Path.GetFileNameWithoutExtension(ff.Path) + $"_{SanitizeRole(ff.Role)}";
            var jsonPlan = Path.Combine(workspace, stem + ".native_write_plan.json");
            var binaryPlan = Path.Combine(workspace, stem + ".native_write_plan.bin");

            var planDocument = new
            {
                format = "WaW360Converter Stage 5a native write plan",
                version = 1,
                source = Path.GetFullPath(ff.Path),
                sourceSha256 = cached.SourceSha256,
                sourcePlatform = cached.Platform.ToString(),
                sourceCompression = cached.Compression.ToString(),
                sourceHeader = new
                {
                    cached.ZoneHeader.ZoneSize,
                    cached.ZoneHeader.ExternalSize,
                    blocks = Enum.GetValues<T4ZoneBlock>().ToDictionary(
                        b => b.ToString(),
                        b => cached.ZoneHeader.BlockSizes[(int)b])
                },
                policy = new
                {
                    trackedTypes = TrackedTypes.OrderBy(x => x).ToArray(),
                    topLevelDefaultBlock = T4ZoneBlock.Temp.ToString(),
                    note = "T4 ZoneCode places XAnim/XModel/Weapon/Fx top-level objects in TEMP. XModel/Weapon/Fx can own children that switch blocks; those are relocation-stage obligations, not flattened into TEMP."
                },
                proofContainers = proofIndexes.Values.Select(p => new
                {
                    p.TypeName,
                    p.ContainerFile,
                    p.Version,
                    p.FileSize,
                    p.Sha256,
                    recordCount = p.Records.Count
                }).OrderBy(x => x.TypeName).ToArray(),
                assets = planned,
                unhandled = unhandledGroups.Select(x => new { type = x.Type, count = x.Count }).ToArray()
            };

            await File.WriteAllTextAsync(jsonPlan, JsonSerializer.Serialize(planDocument, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
            await WriteBinaryPlanAsync(binaryPlan, planned, cancellationToken);
            VerifyBinaryPlan(binaryPlan, planned);

            ffResults.Add(new T4NativeWritePlanFastFile(
                ff.Role,
                ff.Path,
                cached.SourceSha256,
                jsonPlan,
                binaryPlan,
                document.AssetCount,
                planned.Count,
                planned.Count(a => a.SourceBodyOffset is not null),
                planned.Count(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias),
                planned.Sum(a => (long)a.BlobLength),
                unhandledGroups.Length,
                unhandledGroups.Sum(x => x.Count),
                unhandledGroups.Select(x => $"{x.Type} ({x.Count:N0})").ToArray(),
                true));
        }

        if (ffResults.Count == 0)
            throw new InvalidDataException($"'{map.Name}' has no readable Normal-map fastfiles for Stage 5a.");

        var txt = Path.Combine(workspace, $"{map.Name}_stage5_alpha10a_native_writer.txt");
        var json = Path.Combine(workspace, $"{map.Name}_stage5_alpha10a_native_writer.json");
        var result = new NormalMapNativeWriterFoundationResult
        {
            MapName = map.Name,
            Workspace = workspace,
            FastFiles = ffResults,
            PlannedTrackedAssets = ffResults.Sum(x => x.PlannedTrackedAssets),
            PhysicalTrackedAssets = ffResults.Sum(x => x.PhysicalTrackedAssets),
            AliasTrackedAssets = ffResults.Sum(x => x.AliasTrackedAssets),
            PlannedPayloadBytes = ffResults.Sum(x => x.PlannedPayloadBytes),
            UnhandledAssetClasses = ffResults.Sum(x => x.UnhandledAssetClasses),
            UnhandledAssets = ffResults.Sum(x => x.UnhandledAssets),
            Verified = ffResults.All(x => x.Verified),
            HardwareReady = false,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
        return result;
    }

    private static bool RequiresSplitBlockTraversal(string type) =>
        type.Equals("xmodel", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("weapon", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("fx", StringComparison.OrdinalIgnoreCase);

    private static string GetProofPath(string outputRoot, string mapName, string sourcePath, string role, string type)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath) + $"_{SanitizeRole(role)}";
        return type.ToLowerInvariant() switch
        {
            "xanim" => Path.Combine(outputRoot, mapName, "stage4_alpha4_xanim", stem + ".xbox_xanim_proof.bin"),
            "xmodel" => Path.Combine(outputRoot, mapName, "stage4_alpha9g_xmodel", stem + ".xbox_xmodel_proof.bin"),
            "weapon" => Path.Combine(outputRoot, mapName, "stage4_alpha7b_weapon_owned", stem + ".xbox_weapon_owned_proof.bin"),
            "fx" => Path.Combine(outputRoot, mapName, "stage4_alpha9b_fx", stem + ".xbox_fx_proof.bin"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private static async Task WriteBinaryPlanAsync(string path, IReadOnlyList<T4NativeWritePlanAsset> assets, CancellationToken cancellationToken)
    {
        await using var fs = File.Create(path);
        await fs.WriteAsync(BinaryMagic, cancellationToken);
        WriteU32(fs, 1);
        WriteU32(fs, checked((uint)assets.Count));
        foreach (var a in assets)
        {
            WriteU32(fs, checked((uint)a.XAssetIndex));
            WriteU32(fs, checked((uint)a.XboxTypeId));
            WriteU32(fs, checked((uint)a.ReferenceKind));
            WriteU32(fs, a.SourceRawHeader);
            WriteU32(fs, a.SourceBodyOffset is null ? 0xFFFFFFFFu : checked((uint)a.SourceBodyOffset.Value));
            WriteU32(fs, checked((uint)a.BlobLength));
            WriteU32(fs, checked((uint)a.DefaultBlock));
            WriteU32(fs, a.RequiresSplitBlockTraversal ? 1u : 0u);
        }
    }

    private static void VerifyBinaryPlan(string path, IReadOnlyList<T4NativeWritePlanAsset> assets)
    {
        var bytes = File.ReadAllBytes(path);
        var expected = 16 + assets.Count * 32;
        if (bytes.Length != expected)
            throw new InvalidDataException($"Native binary write plan length mismatch: {bytes.Length:N0} != {expected:N0}.");
        if (!bytes.AsSpan(0, 8).SequenceEqual(BinaryMagic))
            throw new InvalidDataException("Native binary write plan magic mismatch.");
        var version = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4));
        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12, 4));
        if (version != 1 || count != assets.Count)
            throw new InvalidDataException("Native binary write plan header failed read-back.");
    }

    private static string Render(NormalMapNativeWriterFoundationResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — Stage 5a alpha 10a native writer foundation");
        sb.AppendLine("============================================================");
        sb.AppendLine("INTERNAL WRITE PLAN: this stage does NOT emit a hardware-ready game fastfile.");
        sb.AppendLine("It proves authoritative Xbox type/order mapping and zero-copy access to the verified Stage 4 big-endian payloads.");
        sb.AppendLine($"Map: {r.MapName}");
        sb.AppendLine($"Verified: {r.Verified}");
        sb.AppendLine($"Hardware ready: {r.HardwareReady} (expected FALSE at this milestone)");
        sb.AppendLine();
        foreach (var ff in r.FastFiles)
        {
            sb.AppendLine($"[{ff.Role}] {ff.Source}");
            sb.AppendLine($"  tracked XAssets planned: {ff.PlannedTrackedAssets:N0}; physical={ff.PhysicalTrackedAssets:N0}; aliases={ff.AliasTrackedAssets:N0}");
            sb.AppendLine($"  zero-copy proof payload: {ff.PlannedPayloadBytes:N0} bytes");
            sb.AppendLine($"  unhandled: {ff.UnhandledAssets:N0} assets across {ff.UnhandledAssetClasses:N0} classes");
            sb.AppendLine($"  plan JSON: {ff.PlanFile}");
            sb.AppendLine($"  plan BIN:  {ff.BinaryPlanFile}");
            if (ff.UnhandledTypes.Count > 0)
                sb.AppendLine("  blockers: " + string.Join(", ", ff.UnhandledTypes));
            sb.AppendLine();
        }
        sb.AppendLine("TOTALS");
        sb.AppendLine($"  tracked XAssets planned: {r.PlannedTrackedAssets:N0}");
        sb.AppendLine($"  physical typed bodies:  {r.PhysicalTrackedAssets:N0}");
        sb.AppendLine($"  tracked aliases:         {r.AliasTrackedAssets:N0}");
        sb.AppendLine($"  referenced proof bytes:  {r.PlannedPayloadBytes:N0}");
        sb.AppendLine($"  unhandled assets:        {r.UnhandledAssets:N0} across {r.UnhandledAssetClasses:N0} per-FF class groups");
        sb.AppendLine();
        sb.AppendLine("Alpha 10a conclusion");
        sb.AppendLine("  The final writer can now consume verified Xbox payloads in deterministic XAsset-table order without recopying proof containers.");
        sb.AppendLine("  Final FF emission remains fail-closed until unhandled asset serializers and split-block relocation are integrated.");
        return sb.ToString();
    }

    private static string SanitizeRole(string role) =>
        new string(role.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }
}
