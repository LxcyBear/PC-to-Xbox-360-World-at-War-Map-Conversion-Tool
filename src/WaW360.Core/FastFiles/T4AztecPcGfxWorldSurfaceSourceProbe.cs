using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Diagnostic evidence for original-PC T4 GfxWorld's fixed 52-byte
/// GfxSurface array. This NEVER infers runtime/Xbox safety from geometry.
/// </summary>
public static class T4AztecPcGfxWorldSurfaceSourceProbe
{
    private const int WorldOffset = 0x049122C0;
    private const int IndicesOffset = 0x049125F2;
    private const int IndicesCount = 58653;
    private const int VerticesOffset = 0x057359F9;
    private const int VerticesCount = 32713;
    private const int VertexStride = 44;
    private const int SurfaceCount = 735;
    private const int SurfaceStride = 52;
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string VertexSha =
        "6303269A8170F6F06233DCCE0BF1230EC6A659C4D0EE78CFDF7C14EE242D8041";
    private const string IndexSha =
        "E89621777CB47C46BB47FE0CA948645D6813151E02A706C2A2325C01BDEFD32D";

    public static async Task<AztecPcGfxSurfaceReport> ScanAsync(
        string originalPcMapFF, string vertexProofJson, string reportJson,
        CancellationToken ct = default)
    {
        originalPcMapFF = Path.GetFullPath(originalPcMapFF);
        using var proof = JsonDocument.Parse(
            await File.ReadAllTextAsync(vertexProofJson, ct));
        var j = proof.RootElement;
        if (j.GetProperty("Schema").GetString() != "WaW360.AztecPcGfxWorldVertexSourceProbe.v1" ||
            j.GetProperty("Status").GetString() != "AZTEC_PC_GFXWORLD_32713_VERTEX_ARRAY_CANDIDATE_PASS" ||
            j.GetProperty("HardwareReady").GetBoolean() ||
            j.GetProperty("FullXenonWorldEmitted").GetBoolean() ||
            j.GetProperty("FullGfxWorldNestedGraphProven").GetBoolean() ||
            j.GetProperty("SourcePCIndexCount").GetInt32() != IndicesCount ||
            j.GetProperty("SourcePCVertexCount").GetInt32() != VerticesCount ||
            j.GetProperty("VertexStride").GetInt32() != VertexStride ||
            j.GetProperty("VertexArrayBytes").GetInt32() != VerticesCount * VertexStride ||
            j.GetProperty("OriginalPCWorldHeaderOffset").GetInt32() != WorldOffset ||
            !j.GetProperty("OriginalPCZoneSha256").GetString()!.Equals(ZoneSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Exact original-PC Aztec GfxWorld 32713-vertex proof changed.");

        var one = j.GetProperty("TopCandidates").EnumerateArray().Where(e =>
            e.GetProperty("SourceOffset").GetInt32() == VerticesOffset &&
            e.GetProperty("ValidBinormalSigns").GetInt32() == VerticesCount &&
            e.GetProperty("ValidPositions").GetInt32() == VerticesCount &&
            e.GetProperty("ValidTexCoords").GetInt32() == VerticesCount &&
            e.GetProperty("OriginalPCArraySha256").GetString()!.Equals(VertexSha, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (one.Length != 1)
            throw new InvalidDataException("Original-PC vertex offset/source SHA winner changed.");

        var ff = await FastFileAnalysisCache.GetZoneAsync(originalPcMapFF, ct);
        if (ff.Platform != WaWPlatform.PC ||
            ff.Bytes.Length != 113701260 ||
            !ff.SourceSha256.Equals(j.GetProperty("OriginalPCFastFileSha256").GetString(), StringComparison.OrdinalIgnoreCase) ||
            !Sha(ff.Bytes).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original-PC map.ff compressed/decompressed SHA changed.");

        var b = ff.Bytes;
        if (U32(b, WorldOffset + 24) != SurfaceCount ||
            U32(b, WorldOffset + 52) != VerticesCount ||
            U32(b, WorldOffset + 16) != IndicesCount ||
            !Sha(b.AsSpan(IndicesOffset, IndicesCount * 2)).Equals(IndexSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(VerticesOffset, VerticesCount * VertexStride)).Equals(VertexSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original-PC GfxWorld 735-surface header/index/vertex source drifted.");

        // PC GfxSurface = srfTriangles_t { int layer, int firstVertex,
        // uint16 vertexCount, uint16 triCount, int baseIndex }, int pad,
        // Material* material, 4 bytes lightmap/reflection/light/flags,
        // then bounds[2][3] floats. 52 bytes on T4 x86.
        // GfxWorld DPVS surface child arrays are not adjacent to root name;
        // scan with strong structural gates instead of shared-string guesses.
        var best = new List<AztecPcGfxSurfaceCandidate>();
        long firstGates = 0, sequenceGates = 0;
        var maxStart = b.Length - SurfaceCount * SurfaceStride;
        for (var start = WorldOffset + 804; start <= maxStart; start++)
        {
            if ((start & 0x3FFFFF) == 0) ct.ThrowIfCancellationRequested();
            if (!PossibleSurface(b, start) ||
                !PossibleSurface(b, start + SurfaceStride) ||
                !PossibleSurface(b, start + SurfaceStride * 2))
                continue;
            firstGates++;
            if (!PossibleSurface(b, start + SurfaceStride * 7) ||
                !PossibleSurface(b, start + SurfaceStride * 17) ||
                !PossibleSurface(b, start + SurfaceStride * 31))
                continue;
            sequenceGates++;

            var valid = 0; var boundsValid = 0; var materialPointers = 0;
            var usable = 0; long tris = 0;
            var minBase = int.MaxValue; var maxIndexEnd = 0;
            var sample = new List<AztecPcGfxSurfaceSample>();
            var prevBase = -1;
            var monotonicIndexBases = 0;
            for (var n = 0; n < SurfaceCount; n++)
            {
                var off = start + n * SurfaceStride;
                var firstVertex = I32(b, off + 4);
                var vertexCount = U16(b, off + 8);
                var triCount = U16(b, off + 10);
                var baseIndex = I32(b, off + 12);
                var matPtr = U32(b, off + 20);
                var okay = PossibleSurface(b, off);
                if (okay) valid++;
                if (MaterialPointer(matPtr)) materialPointers++;
                if (ValidBounds(b, off)) boundsValid++;
                if (okay && triCount > 0 && vertexCount >= 3) usable++;
                tris += triCount;
                if (baseIndex < minBase) minBase = baseIndex;
                if (baseIndex + (long)triCount * 3 > maxIndexEnd)
                    maxIndexEnd = baseIndex + triCount * 3;
                if (prevBase >= 0 && baseIndex >= prevBase) monotonicIndexBases++;
                prevBase = baseIndex;
                if (n < 8)
                    sample.Add(new AztecPcGfxSurfaceSample(n, firstVertex, vertexCount,
                        triCount, baseIndex, $"0x{matPtr:X8}", okay, ValidBounds(b, off)));
                // Even with a strong six-record signature, random false
                // positives should be abandoned quickly.
                if (n >= 32 && valid < n - 4) break;
            }
            if (valid < 650) continue;
            var candidate = new AztecPcGfxSurfaceCandidate(
                start, $"0x{start:X8}", valid, boundsValid, materialPointers,
                usable, tris, minBase, maxIndexEnd, monotonicIndexBases,
                Sha(b.AsSpan(start, SurfaceCount * SurfaceStride)),
                sample.ToArray(), false, false);
            best.Add(candidate);
            best = best.OrderByDescending(c => c.ValidSurfaceRecords)
                .ThenByDescending(c => c.ValidBoundingBoxes)
                .ThenByDescending(c => c.MonotonicIndexBasePairs)
                .ThenBy(c => c.SourceOffset).Take(24).ToList();
        }

        var ordered = best.OrderByDescending(c => c.ValidSurfaceRecords)
            .ThenByDescending(c => c.ValidBoundingBoxes)
            .ThenByDescending(c => c.MonotonicIndexBasePairs)
            .ThenBy(c => c.SourceOffset).ToArray();

        // Strict enough for a *candidate*, not for a renderable FF:
        // verify coherent 735 records, nonzero geometry, and in-range
        // index spans; requiring exact total global index coverage would
        // wrongly reject worlds with unused/padded index data.
        var strong = ordered.Length > 0 &&
            ordered[0].ValidSurfaceRecords == SurfaceCount &&
            ordered[0].ValidBoundingBoxes >= 730 &&
            ordered[0].ValidMaterialPointers >= 700 &&
            ordered[0].UsableSurfaceRecords >= 500 &&
            ordered[0].TotalTriangleCount >= 5000 &&
            ordered[0].TotalTriangleCount <= IndicesCount / 3 &&
            ordered[0].MaximumIndexEnd <= IndicesCount &&
            ordered[0].MonotonicIndexBasePairs >= 700;

        var result = new AztecPcGfxSurfaceReport(
            "WaW360.AztecPcGfxSurfaceSourceProbe.v1",
            strong ? "AZTEC_PC_GFXWORLD_735_SURFACE_ARRAY_CANDIDATE_PASS" :
                "AZTEC_PC_GFXWORLD_SURFACE_LAYOUT_UNRESOLVED",
            originalPcMapFF, ff.SourceSha256, ZoneSha,
            VerticesOffset, VertexSha, IndicesOffset, IndexSha,
            SurfaceCount, SurfaceStride, firstGates, sequenceGates,
            ordered, false, false, false,
            "735 original PC GfxSurface 52-byte structure signatures, counts and " +
            "index coverage only. No authenticated material assignments, triangle " +
            "pointer rebasing, DPVS, GfxWorld Xenon serialization or Xbox visuals. " +
            "Original successful six-family FF is unchanged.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportJson))!);
        await File.WriteAllTextAsync(reportJson, JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }

    private static bool PossibleSurface(byte[] b, int at)
    {
        var first = I32(b, at + 4);
        var count = U16(b, at + 8);
        var triangles = U16(b, at + 10);
        var baseIndex = I32(b, at + 12);
        return first >= 0 && first < VerticesCount &&
               count >= 3 && first + count <= VerticesCount &&
               triangles >= 1 && triangles <= 19551 &&
               baseIndex >= 0 && baseIndex + (long)triangles * 3 <= IndicesCount;
    }

    private static bool ValidBounds(byte[] b, int at)
    {
        for (var k = 0; k < 3; k++)
        {
            var min = F(b, at + 28 + k * 4);
            var max = F(b, at + 40 + k * 4);
            if (!float.IsFinite(min) || !float.IsFinite(max) ||
                MathF.Abs(min) > 1000000f ||
                MathF.Abs(max) > 1000000f || min > max)
                return false;
        }
        return true;
    }

    private static bool MaterialPointer(uint v) =>
        v == 0 || v == 0xFFFFFFFFu || v == 0xFFFFFFFEu ||
        (v > 0 && ((v - 1) >> 29) <= 6);

    private static uint U32(byte[] b, int p) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4));
    private static int I32(byte[] b, int p) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p, 4));
    private static ushort U16(byte[] b, int p) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p, 2));
    private static float F(byte[] b, int p) =>
        BitConverter.Int32BitsToSingle(I32(b, p));
    private static string Sha(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(SHA256.HashData(b));
}

public sealed record AztecPcGfxSurfaceSample(
    int Index, int FirstVertex, int VertexCount, int TriangleCount, int BaseIndex,
    string MaterialPointer, bool RecordPlausible, bool BoundsPlausible);
public sealed record AztecPcGfxSurfaceCandidate(
    int SourceOffset, string SourceOffsetHex, int ValidSurfaceRecords,
    int ValidBoundingBoxes, int ValidMaterialPointers, int UsableSurfaceRecords,
    long TotalTriangleCount, int MinimumBaseIndex, int MaximumIndexEnd,
    int MonotonicIndexBasePairs, string OriginalPCArraySha256,
    IReadOnlyList<AztecPcGfxSurfaceSample> FirstEightRecords,
    bool SourceOwnedByGfxWorldProven, bool RenderableXenonMeshEmitted);
public sealed record AztecPcGfxSurfaceReport(
    string Schema, string Status, string OriginalPCFastFile,
    string OriginalPCFastFileSha256, string OriginalPCZoneSha256,
    int SourcePCVertexOffset, string SourcePCVertexSha256,
    int SourcePCIndexOffset, string SourcePCIndexSha256,
    int SurfaceCount, int SurfaceStride, long ThreeRecordGates,
    long SixRecordGates, IReadOnlyList<AztecPcGfxSurfaceCandidate> TopCandidates,
    bool FullGfxWorldSerialized, bool SurfaceMaterialOwnershipProven,
    bool XboxHardwareReady, string Note);
