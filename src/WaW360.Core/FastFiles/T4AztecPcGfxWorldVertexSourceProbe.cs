using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Demo-critical, read-only Aztec PC GfxWorld vertex source locator.
///
/// Scans 44-byte T4 GfxWorldVertex records after the independently pinned
/// 58,653 source index array. No donor/oracle bytes are used. It verifies
/// source bytes and reports candidates only. It does not emit a world or
/// declare any Xbox hardware candidate ready.
/// </summary>
public static class T4AztecPcGfxWorldVertexSourceProbe
{
    private const int PcZoneBytes = 113701260;
    private const int HeaderOffset = 0x049122C0;
    private const int IndicesOffset = 0x049125F2;
    private const int IndexCount = 58653;
    private const int VertexCount = 32713;
    private const int VertexStride = 44;
    private const int VertexArrayBytes = VertexCount * VertexStride;
    private static readonly int[] Samples =
    [
        0, 1, 2, 3, 4, 5, 7, 13, 31, 97, 211, 517, 2048, 9984, 20000, 32712
    ];

    public static async Task<AztecPcGfxWorldVertexReport> ScanAsync(
        string originalPcFastFile, string indexProofPath, string outJson,
        CancellationToken ct = default)
    {
        originalPcFastFile = Path.GetFullPath(originalPcFastFile);
        indexProofPath = Path.GetFullPath(indexProofPath);
        using var proof = JsonDocument.Parse(
            await File.ReadAllTextAsync(indexProofPath, ct));
        var j = proof.RootElement;
        if (j.GetProperty("schema").GetString() != "WaW360.AztecPcGfxWorldIndexSourceProof.v1" ||
            j.GetProperty("status").GetString() != "AZTEC_PC_GFXWORLD_58653_INDEX_ARRAY_CANDIDATE_PASS" ||
            j.GetProperty("readyForXbox").GetBoolean() ||
            j.GetProperty("originalPcGfxWorldBodyFullyDecoded").GetBoolean() ||
            j.GetProperty("gfxWorldHeaderOffset").GetInt32() != HeaderOffset ||
            j.GetProperty("originalPcIndexCount").GetInt32() != IndexCount ||
            j.GetProperty("originalPcVertexCount").GetInt32() != VertexCount)
            throw new InvalidDataException("Require the specific successful, diagnostic-only PC Aztec GfxWorld index proof.");

        var indexed = j.GetProperty("candidateIndexArrays").EnumerateArray()
            .Where(e => e.GetProperty("candidateArrayOffset").GetInt32() == IndicesOffset &&
                        e.GetProperty("paddingAfterName").GetInt32() == 0 &&
                        e.GetProperty("indicesWithin32713").GetInt32() == IndexCount &&
                        e.GetProperty("indicesOutOfRange").GetInt32() == 0)
            .ToArray();
        if (indexed.Length != 1)
            throw new InvalidDataException("Require exact zero-padding PC index array at 0x049125F2.");

        var source = await FastFileAnalysisCache.GetZoneAsync(originalPcFastFile, ct);
        if (source.Platform != WaWPlatform.PC ||
            source.Bytes.Length != PcZoneBytes ||
            !source.SourceSha256.Equals(j.GetProperty("originalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha(source.Bytes).Equals(j.GetProperty("originalPcZoneSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original-PC source compressed/decompressed SHA provenance changed.");

        var bytes = source.Bytes;
        var headSha = Sha(bytes.AsSpan(HeaderOffset, 128));
        if (!headSha.Equals(j.GetProperty("gfxWorldHeader128Sha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha(bytes.AsSpan(IndicesOffset, IndexCount * 2))
                .Equals(indexed[0].GetProperty("sourceIndexBytesSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original-PC GfxWorld root or 58,653 source indices SHA changed.");

        var namePtr = U(bytes, HeaderOffset);
        var basePtr = U(bytes, HeaderOffset + 4);
        var count = U(bytes, HeaderOffset + 52);
        if (namePtr != 0x84702AC9u || basePtr != uint.MaxValue ||
            count != VertexCount || U(bytes, HeaderOffset + 56) != uint.MaxValue)
            throw new InvalidDataException("Pinned PC GfxWorld name/baseName/vertex pointer ABI drifted.");

        // T4 GfxWorldVertex = xyz[3] (float), binormalSign (float),
        // color[4] (bytes), texCoord[2] (float), lmapCoord[2] (float),
        // normal/tangent (two packed uint32). Exactly 44 bytes.
        // The ZoneCode traversal explicitly moves 'vd' near the end:
        // its vertices are NOT immediately after the header or indices.
        var startMin = IndicesOffset + IndexCount * 2;
        var startMax = bytes.Length - VertexArrayBytes;
        var best = new List<AztecPcGfxWorldVertexCandidate>();
        var coarsePasses = 0L;
        var deepPasses = 0L;

        for (var start = startMin; start <= startMax; start++)
        {
            if ((start & 0x1FFFFF) == 0)
                ct.ThrowIfCancellationRequested();

            // First three binormalSign words gate nearly all nonvertex
            // bytes without allocation. The PC struct offsets are exact.
            if (!SignWord(U(bytes, start + 12)) ||
                !SignWord(U(bytes, start + 12 + VertexStride)) ||
                !SignWord(U(bytes, start + 12 + VertexStride * 2)))
                continue;
            coarsePasses++;

            var signHits = 0;
            var xyzHits = 0;
            var texHits = 0;
            var nonzeroPos = 0;
            foreach (var n in Samples)
            {
                var p = start + n * VertexStride;
                if (SignWord(U(bytes, p + 12))) signHits++;
                var xyz = FiniteBounded(F(bytes, p), 1000000) &&
                          FiniteBounded(F(bytes, p + 4), 1000000) &&
                          FiniteBounded(F(bytes, p + 8), 1000000);
                if (xyz)
                {
                    xyzHits++;
                    if (MathF.Abs(F(bytes, p)) + MathF.Abs(F(bytes, p + 4)) +
                        MathF.Abs(F(bytes, p + 8)) > .01f)
                        nonzeroPos++;
                }

                if (FiniteBounded(F(bytes, p + 20), 1000000) &&
                    FiniteBounded(F(bytes, p + 24), 1000000) &&
                    FiniteBounded(F(bytes, p + 28), 1000000) &&
                    FiniteBounded(F(bytes, p + 32), 1000000))
                    texHits++;
            }
            if (signHits < 14 || xyzHits < 14 || texHits < 14 || nonzeroPos < 8)
                continue;
            deepPasses++;

            var validSigns = 0;
            var validXyz = 0;
            var validTex = 0;
            var uniqueXYZ = new HashSet<(int X, int Y, int Z)>();
            var minX = float.PositiveInfinity; var minY = float.PositiveInfinity;
            var minZ = float.PositiveInfinity; var maxX = float.NegativeInfinity;
            var maxY = float.NegativeInfinity; var maxZ = float.NegativeInfinity;
            for (var i = 0; i < VertexCount; i++)
            {
                var p = start + i * VertexStride;
                if (SignWord(U(bytes, p + 12))) validSigns++;
                var x = F(bytes, p);
                var y = F(bytes, p + 4);
                var z = F(bytes, p + 8);
                if (FiniteBounded(x, 1000000) &&
                    FiniteBounded(y, 1000000) &&
                    FiniteBounded(z, 1000000))
                {
                    validXyz++;
                    if (i % 5 == 0)
                        uniqueXYZ.Add((BitConverter.SingleToInt32Bits(x),
                            BitConverter.SingleToInt32Bits(y),
                            BitConverter.SingleToInt32Bits(z)));
                    minX = MathF.Min(minX, x); maxX = MathF.Max(maxX, x);
                    minY = MathF.Min(minY, y); maxY = MathF.Max(maxY, y);
                    minZ = MathF.Min(minZ, z); maxZ = MathF.Max(maxZ, z);
                }
                if (FiniteBounded(F(bytes, p + 20), 1000000) &&
                    FiniteBounded(F(bytes, p + 24), 1000000) &&
                    FiniteBounded(F(bytes, p + 28), 1000000) &&
                    FiniteBounded(F(bytes, p + 32), 1000000))
                    validTex++;
            }

            var candidate = new AztecPcGfxWorldVertexCandidate(
                start, $"0x{start:X8}", VertexCount, VertexStride,
                VertexArrayBytes, signHits, xyzHits, texHits,
                validSigns, validXyz, validTex,
                uniqueXYZ.Count,
                minX, minY, minZ, maxX, maxY, maxZ,
                Sha(bytes.AsSpan(start, VertexArrayBytes)),
                false, false);
            best.Add(candidate);
            // Keep at most the strongest 24 verified candidates.
            best = best.OrderByDescending(c => c.ValidBinormalSigns)
                .ThenByDescending(c => c.ValidPositions)
                .ThenByDescending(c => c.UniqueSampledPositions)
                .ThenBy(c => c.SourceOffset)
                .Take(24).ToList();
        }

        var ranked = best.OrderByDescending(c => c.ValidBinormalSigns)
            .ThenByDescending(c => c.ValidPositions)
            .ThenByDescending(c => c.UniqueSampledPositions)
            .ThenBy(c => c.SourceOffset).ToArray();
        // Strong header-based evidence: all original PC sample records
        // fit the true T4 vertex layout; still no surface binding proof.
        var strong = ranked.Length != 0 &&
                     ranked[0].ValidBinormalSigns >= 32700 &&
                     ranked[0].ValidPositions >= 32700 &&
                     ranked[0].ValidTexCoords >= 32700 &&
                     ranked[0].UniqueSampledPositions >= 500;

        var report = new AztecPcGfxWorldVertexReport(
            "WaW360.AztecPcGfxWorldVertexSourceProbe.v1",
            strong ? "AZTEC_PC_GFXWORLD_32713_VERTEX_ARRAY_CANDIDATE_PASS" :
                "AZTEC_PC_GFXWORLD_VERTEX_LAYOUT_UNRESOLVED",
            originalPcFastFile, source.SourceSha256, Sha(bytes),
            HeaderOffset, IndicesOffset, IndexCount, VertexCount,
            VertexStride, VertexArrayBytes, startMin, startMax,
            coarsePasses, deepPasses, ranked,
            false, false, false,
            "Verifies original-PC 44-byte GfxWorldVertex candidates throughout the source zone " +
            "after 58653 SHA-pinned indices. True world vertex pointer ownership, GfxSurface " +
            "index/baseVertex mapping, DPVS and material/lightmap graph are not yet proven. " +
            "Does not emit any FF. Six-family diagnostic scratch FF is NOT for Xbox.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outJson))!);
        await File.WriteAllTextAsync(outJson,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        return report;
    }

    private static uint U(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));

    private static float F(ReadOnlySpan<byte> bytes, int offset) =>
        BitConverter.Int32BitsToSingle(unchecked((int)U(bytes, offset)));

    private static bool SignWord(uint raw) => raw is 0x3F800000u or 0xBF800000u;

    private static bool FiniteBounded(float f, float max) =>
        float.IsFinite(f) && MathF.Abs(f) <= max;

    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}

public sealed record AztecPcGfxWorldVertexCandidate(
    int SourceOffset, string SourceOffsetHex, int VertexCount, int StrideBytes,
    int SourceByteLength, int SampleSignHits, int SampleValidXYZ,
    int SampleValidTexCoords, int ValidBinormalSigns, int ValidPositions,
    int ValidTexCoords, int UniqueSampledPositions,
    float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ,
    string OriginalPCArraySha256, bool SourceOwnedByGfxWorldProven,
    bool XboxBufferEmitted);

public sealed record AztecPcGfxWorldVertexReport(
    string Schema, string Status, string OriginalPCFastFile,
    string OriginalPCFastFileSha256, string OriginalPCZoneSha256,
    int OriginalPCWorldHeaderOffset, int SourcePCIndexOffset, int SourcePCIndexCount,
    int SourcePCVertexCount, int VertexStride, int VertexArrayBytes,
    int ScanStart, int ScanEnd, long CoarseCandidates,
    long DeepCandidates, IReadOnlyList<AztecPcGfxWorldVertexCandidate> TopCandidates,
    bool FullGfxWorldNestedGraphProven, bool FullXenonWorldEmitted,
    bool HardwareReady, string Note);
