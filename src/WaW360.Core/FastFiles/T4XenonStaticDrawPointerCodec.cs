using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Map-independent T4 Xenon static-model draw XModel* relocation.
///
/// This is a bounded pointer encoder, NOT a GfxWorld serializer. It never
/// constructs missing XModels, converts lighting, or claims a complete
/// runtime draw array. The caller must supply source-owned body evidence
/// and final Stage 4D root pointers. Unverified stock model references are
/// quarantined; never map a name-only shell to a purported runtime body.
/// </summary>
public static class T4XenonStaticDrawPointerCodec
{
    public const string InputSchema = "WaW360.T4Xenon.StaticDrawPointerInput.v1";

    public sealed record RootAddress(int NativeRootIndex, string TypeName,
        string NativeName, uint StablePointer, bool SourceOwnedBodyPresent);

    public sealed record DrawSite(int Ordinal, int NativeRootIndex,
        string SourceModelName, bool SourceOwnedBodyPresent);

    public sealed record EncodeResult(byte[] Candidate, int OwnedPointersWritten,
        int ExternalReferencesQuarantined, int DistinctOwnedRootBodies,
        IReadOnlyList<int> QuarantinedOrdinals);

    /// <summary>
    /// The exact source-byte buffer and root addresses must already have
    /// independent provenance checks before this method is called.
    /// Validate width, source rows, owner/root identity, packed T4 VIRTUAL
    /// offset kind, and original-byte immutability beyond the pointer field.
    /// </summary>
    public static EncodeResult Encode(
        byte[] sourceDrawBytes,
        int recordStride,
        int modelPointerOffset,
        IReadOnlyList<DrawSite> draws,
        IReadOnlyDictionary<int, RootAddress> roots)
    {
        ArgumentNullException.ThrowIfNull(sourceDrawBytes);
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentNullException.ThrowIfNull(roots);
        if (recordStride <= 0 || modelPointerOffset < 0 ||
            (long)modelPointerOffset + 4 > recordStride ||
            (modelPointerOffset & 3) != 0)
            throw new InvalidDataException(
                "Native draw stride or 4-byte XModel* field offset is invalid.");
        if (draws.Count == 0 || (long)recordStride * draws.Count != sourceDrawBytes.LongLength)
            throw new InvalidDataException(
                "Draw byte length must equal record stride times source draw count.");

        var result = (byte[])sourceDrawBytes.Clone();
        var ownedRoots = new HashSet<int>();
        var quarantined = new List<int>();
        var encoded = 0;
        for (var ordinal = 0; ordinal < draws.Count; ordinal++)
        {
            var draw = draws[ordinal];
            if (draw.Ordinal != ordinal || string.IsNullOrWhiteSpace(draw.SourceModelName))
                throw new InvalidDataException(
                    $"Missing/duplicate/out-of-order source draw #{ordinal}.");
            if (!roots.TryGetValue(draw.NativeRootIndex, out var root) ||
                root.NativeRootIndex != draw.NativeRootIndex ||
                !root.TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase) ||
                Canon(root.NativeName) != Canon(draw.SourceModelName))
                throw new InvalidDataException(
                    $"Draw #{ordinal} does not resolve to an exact typed native XModel root.");

            var at = checked(ordinal * recordStride + modelPointerOffset);
            if (BinaryPrimitives.ReadUInt32BigEndian(sourceDrawBytes.AsSpan(at, 4)) != 0)
                throw new InvalidDataException(
                    $"Draw #{ordinal} source has a pre-existing nonzero model pointer.");

            if (draw.SourceOwnedBodyPresent)
            {
                if (!root.SourceOwnedBodyPresent || root.StablePointer == 0)
                    throw new InvalidDataException(
                        $"Draw #{ordinal} has no verified source-owned XModel body and stable pointer.");
                var decoded = T4ZonePointer.Decode(root.StablePointer);
                if (decoded.Kind != T4ZonePointerKind.Offset ||
                    decoded.Block != T4ZoneBlock.Virtual)
                    throw new InvalidDataException(
                        $"Draw #{ordinal} stable pointer is not a real T4 VIRTUAL offset.");

                BinaryPrimitives.WriteUInt32BigEndian(
                    result.AsSpan(at, 4), root.StablePointer);
                encoded++;
                ownedRoots.Add(draw.NativeRootIndex);
            }
            else
            {
                if (root.SourceOwnedBodyPresent)
                    throw new InvalidDataException(
                        $"Draw #{ordinal} was classified stock-only but has a source-owned body.");
                // This is an explicit missing dependency, NOT a zero-valued
                // valid pointer in a shippable Xbox GfxWorld draw record.
                quarantined.Add(ordinal);
            }
        }

        for (var i = 0; i < sourceDrawBytes.Length; i++)
        {
            var inPointer = i % recordStride >= modelPointerOffset &&
                            i % recordStride < modelPointerOffset + 4;
            if (!inPointer && result[i] != sourceDrawBytes[i])
                throw new InvalidDataException(
                    $"Non-pointer source geometry/lighting byte changed at offset {i}.");
        }

        return new EncodeResult(result, encoded, quarantined.Count,
            ownedRoots.Count, quarantined);
    }

    /// <summary>
    /// Neutral JSON entrypoint. No map names/counts baked into the engine.
    /// JSON producer is responsible for proving that these exact root
    /// addresses came from the same source FF's Stage4D/4E layout.
    /// A digest mismatch or partial schema blocks output.
    /// </summary>
    public static async Task<EncodeResult> EncodeFromManifestAsync(
        string manifestPath, string outputBinaryPath,
        string outputReportPath, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(
            await File.ReadAllTextAsync(manifestPath, ct));
        var root = doc.RootElement;
        if (root.GetProperty("schema").GetString() != InputSchema)
            throw new InvalidDataException("Unrecognized map-independent native draw manifest schema.");

        var inputFile = Path.GetFullPath(root.GetProperty("sourceGeometryFile").GetString()!);
        var expectedSha = root.GetProperty("sourceGeometrySha256").GetString()!;
        var stride = root.GetProperty("recordStride").GetInt32();
        var pointerAt = root.GetProperty("modelPointerOffset").GetInt32();
        var records = root.GetProperty("draws");
        var addresses = root.GetProperty("roots");
        if (records.ValueKind != JsonValueKind.Array ||
            addresses.ValueKind != JsonValueKind.Array ||
            records.GetArrayLength() == 0 ||
            addresses.GetArrayLength() == 0)
            throw new InvalidDataException("Manifest lacks real source draw records or root bindings.");

        var source = await File.ReadAllBytesAsync(inputFile, ct);
        if (!Eq(Sha(source), expectedSha))
            throw new InvalidDataException("Source geometry SHA-256 drifted after manifest authoring.");

        var drawList = new List<DrawSite>();
        foreach (var item in records.EnumerateArray())
            drawList.Add(new DrawSite(
                item.GetProperty("ordinal").GetInt32(),
                item.GetProperty("nativeRootIndex").GetInt32(),
                item.GetProperty("modelName").GetString()!,
                item.GetProperty("sourceOwnedBodyPresent").GetBoolean()));

        var rootMap = new Dictionary<int, RootAddress>();
        foreach (var item in addresses.EnumerateArray())
        {
            var r = new RootAddress(
                item.GetProperty("nativeRootIndex").GetInt32(),
                item.GetProperty("typeName").GetString()!,
                item.GetProperty("nativeName").GetString()!,
                item.GetProperty("stablePointer").GetUInt32(),
                item.GetProperty("sourceOwnedBodyPresent").GetBoolean());
            if (!rootMap.TryAdd(r.NativeRootIndex, r))
                throw new InvalidDataException("Duplicate native root address in pointer manifest.");
        }

        var encoded = Encode(source, stride, pointerAt, drawList, rootMap);
        var output = Path.GetFullPath(outputBinaryPath);
        var report = Path.GetFullPath(outputReportPath);
        if (File.Exists(output) || File.Exists(report))
            throw new IOException("Refusing to overwrite existing static draw diagnostic output.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        await File.WriteAllBytesAsync(output, encoded.Candidate, ct);

        var manifest = new
        {
            schema = "WaW360.T4Xenon.StaticDrawPointerEncodeResult.v1",
            status = "SOURCE_OWNED_POINTERS_ONLY_NOT_FULL_GFXWORLD",
            inputManifest = Path.GetFullPath(manifestPath),
            sourceGeometrySha256 = expectedSha,
            outputFile = output,
            outputSha256 = Sha(encoded.Candidate),
            drawCount = drawList.Count,
            recordStride = stride,
            modelPointerOffset = pointerAt,
            sourceOwnedPointersWritten = encoded.OwnedPointersWritten,
            externalPointersQuarantined = encoded.ExternalReferencesQuarantined,
            distinctSourceOwnedModelRoots = encoded.DistinctOwnedRootBodies,
            quarantinedOrdinals = encoded.QuarantinedOrdinals,
            originalNonPointerBytesPreserved = true,
            physicallyLinkedIntoGfxWorld = false,
            missingLightingFlagsAndDpvsNotSynthesized = true,
            productionReady = false
        };
        await File.WriteAllTextAsync(report,
            JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false), ct);
        return encoded;
    }

    /// <summary>
    /// Tiny synthetic self-test using arbitrary 3x48 layouts, not Aztec's
    /// 1903x60. Asserts big-endian pointer words and fail-closed behavior.
    /// </summary>
    public static void SelfTest()
    {
        const int stride = 48, pointerAt = 24;
        var src = new byte[stride * 3];
        for (var i = 0; i < src.Length; i++)
            if (i % stride < pointerAt || i % stride >= pointerAt + 4)
                src[i] = (byte)(i % 251);
        var rootA = T4ZonePointer.Encode(T4ZoneBlock.Virtual, 0x00012345);
        var rootB = T4ZonePointer.Encode(T4ZoneBlock.Virtual, 0x00023456);
        var map = new Dictionary<int, RootAddress>
        {
            [6] = new(6, "xmodel", "custom_tree", rootA, true),
            [9] = new(9, "xmodel", "custom_wall", rootB, true),
            [12] = new(12, "xmodel", "stock_prop", 0, false)
        };
        var draws = new[]
        {
            new DrawSite(0, 6, "custom_tree", true),
            new DrawSite(1, 9, "custom_wall", true),
            new DrawSite(2, 12, "stock_prop", false)
        };
        var res = Encode(src, stride, pointerAt, draws, map);
        if (res.OwnedPointersWritten != 2 ||
            res.ExternalReferencesQuarantined != 1 ||
            res.QuarantinedOrdinals.Count != 1 ||
            res.QuarantinedOrdinals[0] != 2 ||
            BinaryPrimitives.ReadUInt32BigEndian(res.Candidate.AsSpan(24, 4)) != rootA ||
            BinaryPrimitives.ReadUInt32BigEndian(res.Candidate.AsSpan(stride + 24, 4)) != rootB ||
            BinaryPrimitives.ReadUInt32BigEndian(res.Candidate.AsSpan(2 * stride + 24, 4)) != 0)
            throw new InvalidDataException("Native draw codec synthetic endian/stock reference proof failed.");

        var tampered = (byte[])src.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(tampered.AsSpan(24, 4), 0xF0F0F0F0);
        try
        {
            _ = Encode(tampered, stride, pointerAt, draws, map);
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidDataException("Native draw codec failed to reject pre-populated pointer bytes.");
    }

    private static string Canon(string name)
    {
        var n = name.Replace('\\', '/').Trim().TrimStart(',').ToLowerInvariant();
        return n.EndsWith("_wj", StringComparison.Ordinal)
            ? n[..^3] : n;
    }

    private static bool Eq(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
