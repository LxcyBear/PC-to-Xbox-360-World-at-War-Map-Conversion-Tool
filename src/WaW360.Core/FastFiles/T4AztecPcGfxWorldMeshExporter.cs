using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Exports actual PC-owned T4 Aztec GfxWorld surface topology as a neutral
/// Wavefront OBJ mesh for independent geometric inspection. No reference
/// donor geometry, fake GfxWorld FF or unproven Xbox-ready assertion.
/// </summary>
public static class T4AztecPcGfxWorldMeshExporter
{
    private const int WorldOffset = 0x049122C0;
    private const int IndexStart = 0x049125F2;
    private const int IndexCount = 58653;
    private const int VertexStart = 0x057359F9;
    private const int VertexCount = 32713;
    private const int VertexStride = 44;
    private const int SurfaceStart = 0x058E361E;
    private const int SurfaceCount = 735;
    private const int SurfaceStride = 52;
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string IndexSha =
        "E89621777CB47C46BB47FE0CA948645D6813151E02A706C2A2325C01BDEFD32D";
    private const string VertexSha =
        "6303269A8170F6F06233DCCE0BF1230EC6A659C4D0EE78CFDF7C14EE242D8041";
    private const string SurfaceSha =
        "919FF1CDB1EC5FCD57ED4020F1908AE813C1FB570C8631C15135E07C5DF852E5";

    public static async Task<AztecPcMeshExportReport> ExportAsync(
        string pcFastFile,
        string surfaceProofJson,
        string outputObj,
        string outputReport,
        CancellationToken ct = default)
    {
        pcFastFile = Path.GetFullPath(pcFastFile);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(surfaceProofJson, ct));
        var j = doc.RootElement;
        if (j.GetProperty("Schema").GetString() != "WaW360.AztecPcGfxSurfaceSourceProbe.v1" ||
            j.GetProperty("Status").GetString() != "AZTEC_PC_GFXWORLD_735_SURFACE_ARRAY_CANDIDATE_PASS" ||
            j.GetProperty("SurfaceCount").GetInt32() != SurfaceCount ||
            j.GetProperty("SurfaceStride").GetInt32() != SurfaceStride ||
            j.GetProperty("FullGfxWorldSerialized").GetBoolean() ||
            j.GetProperty("SurfaceMaterialOwnershipProven").GetBoolean() ||
            j.GetProperty("XboxHardwareReady").GetBoolean() ||
            !string.Equals(j.GetProperty("OriginalPCZoneSha256").GetString(),
                ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(j.GetProperty("SourcePCVertexSha256").GetString(),
                VertexSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(j.GetProperty("SourcePCIndexSha256").GetString(),
                IndexSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Require exact PC Aztec 735-surface topology PASS, not a placeholder or Xbox candidate.");

        var sourceCandidates = j.GetProperty("TopCandidates").EnumerateArray()
            .Where(c => c.GetProperty("SourceOffset").GetInt32() == SurfaceStart &&
                        c.GetProperty("ValidSurfaceRecords").GetInt32() == SurfaceCount &&
                        c.GetProperty("ValidBoundingBoxes").GetInt32() == SurfaceCount &&
                        c.GetProperty("ValidMaterialPointers").GetInt32() == SurfaceCount &&
                        c.GetProperty("TotalTriangleCount").GetInt64() == IndexCount / 3 &&
                        c.GetProperty("UncoveredIndexEntries").GetInt32() == 0 &&
                        c.GetProperty("IndexSpanOverlapEntries").GetInt32() == 0 &&
                        c.GetProperty("LocalIndicesInRange").GetInt32() == IndexCount &&
                        c.GetProperty("InspectedIndexEntries").GetInt32() == IndexCount &&
                        string.Equals(c.GetProperty("OriginalPCArraySha256").GetString(),
                            SurfaceSha, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sourceCandidates.Length != 1)
            throw new InvalidDataException("Exact SHA-pinned 735 original PC GfxSurface array is missing or ambiguous.");

        var source = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        if (source.Platform != WaWPlatform.PC ||
            source.Bytes.Length != 113701260 ||
            !string.Equals(source.SourceSha256,
                j.GetProperty("OriginalPCFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Sha(source.Bytes), ZoneSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC map.ff file/zone identity changed.");

        var b = source.Bytes;
        if (U32(b, WorldOffset + 16) != IndexCount ||
            U32(b, WorldOffset + 24) != SurfaceCount ||
            U32(b, WorldOffset + 52) != VertexCount ||
            !Sha(b.AsSpan(IndexStart, IndexCount * 2)).Equals(IndexSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(VertexStart, VertexCount * VertexStride)).Equals(VertexSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(SurfaceStart, SurfaceCount * SurfaceStride)).Equals(SurfaceSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PC GfxWorld header/triangle index/vertex/surface source SHAs changed.");

        // Preflight every triangle before writing any OBJ bytes. Every
        // source index belongs to exactly one surface-local draw span.
        var occupied = new bool[IndexCount];
        var materials = new Dictionary<uint, int>();
        var usedVertex = new bool[VertexCount];
        var triangles = 0;
        var degenerateFaces = 0;
        var badSurfaceBounds = 0;
        for (var s = 0; s < SurfaceCount; s++)
        {
            var p = SurfaceStart + SurfaceStride * s;
            var first = I32(b, p + 4);
            var count = U16(b, p + 8);
            var faces = U16(b, p + 10);
            var baseIndex = I32(b, p + 12);
            var material = U32(b, p + 20);
            if (first < 0 || first + count > VertexCount || count < 3 ||
                faces < 1 || baseIndex < 0 || baseIndex + faces * 3 > IndexCount)
                throw new InvalidDataException($"Original PC surface #{s} has invalid vertex/index bounds.");

            if (!T4ZonePointer.IsWithinBlock(material, source.ZoneHeader))
                throw new InvalidDataException($"Original PC surface #{s} material packed pointer escapes zone block.");
            materials.TryGetValue(material, out var materialUses);
            materials[material] = materialUses + 1;
            for (var k = 0; k < faces * 3; k += 3)
            {
                Span<int> verts = stackalloc int[3];
                for (var c = 0; c < 3; c++)
                {
                    var idx = baseIndex + k + c;
                    if (occupied[idx])
                        throw new InvalidDataException($"Duplicate PC GfxWorld index-buffer slot #{idx}.");
                    occupied[idx] = true;
                    var local = U16(b, IndexStart + 2 * idx);
                    if (local >= count)
                        throw new InvalidDataException($"Surface #{s} index {idx} is outside its local vertex range.");
                    var absolute = first + local;
                    if (absolute < 0 || absolute >= VertexCount)
                        throw new InvalidDataException($"Surface #{s} index {idx} resolves outside the global vertex array.");
                    verts[c] = absolute;
                    usedVertex[absolute] = true;
                }

                if (verts[0] == verts[1] || verts[1] == verts[2] || verts[0] == verts[2])
                    degenerateFaces++;
                triangles++;
            }
            // This is useful independent evidence; do not make derived
            // bounds a blocker until the game's surf-culling semantics
            // are reconciled with its vertex-space convention.
            for (var ax = 0; ax < 3; ax++)
            {
                var lower = F(b, p + 28 + 4 * ax);
                var upper = F(b, p + 40 + 4 * ax);
                if (!float.IsFinite(lower) || !float.IsFinite(upper) || lower > upper)
                    badSurfaceBounds++;
            }
        }
        if (triangles != IndexCount / 3 ||
            occupied.Any(v => !v) || materials.Count == 0)
            throw new InvalidDataException("Original PC GfxWorld triangle coverage or material-reference census changed.");

        var output = Path.GetFullPath(outputObj);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await using (var fs = new FileStream(output, FileMode.Create, FileAccess.Write))
        await using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
        {
            await writer.WriteLineAsync("# World at War nazi_zombie_aztec — ORIGINAL PC source-mesh inspection only");
            await writer.WriteLineAsync("# 32713 vertices, 58653 indices, 735 surfaces, 19551 triangles");
            await writer.WriteLineAsync("# No donor data; no game textures. Surface groups carry source material pointer IDs.");
            await writer.WriteLineAsync("# NOT an Xbox FastFile, not a proof of Xbox GfxWorld rendering.");
            await writer.WriteLineAsync("o nazi_zombie_aztec_PC_GfxWorld");
            for (var i = 0; i < VertexCount; i++)
            {
                if ((i & 2047) == 0) ct.ThrowIfCancellationRequested();
                var p = VertexStart + i * VertexStride;
                var x = F(b, p); var y = F(b, p + 4); var z = F(b, p + 8);
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                    throw new InvalidDataException($"Nonfinite original PC vertex #{i}.");
                await writer.WriteLineAsync("v " + Format(x) + " " + Format(y) + " " + Format(z));
            }
            for (var i = 0; i < VertexCount; i++)
            {
                var p = VertexStart + i * VertexStride;
                var u = F(b, p + 20); var v = F(b, p + 24);
                if (!float.IsFinite(u) || !float.IsFinite(v))
                    throw new InvalidDataException($"Nonfinite original PC vertex UV #{i}.");
                await writer.WriteLineAsync("vt " + Format(u) + " " + Format(1f - v));
            }
            for (var s = 0; s < SurfaceCount; s++)
            {
                var p = SurfaceStart + s * SurfaceStride;
                var first = I32(b, p + 4);
                var faceCount = U16(b, p + 10);
                var baseIndex = I32(b, p + 12);
                var material = U32(b, p + 20);
                await writer.WriteLineAsync($"g surface_{s:D3}_source_matptr_{material:X8}");
                for (var t = 0; t < faceCount; t++)
                {
                    var a = first + U16(b, IndexStart + 2 * (baseIndex + t * 3));
                    var c = first + U16(b, IndexStart + 2 * (baseIndex + t * 3 + 1));
                    var d = first + U16(b, IndexStart + 2 * (baseIndex + t * 3 + 2));
                    await writer.WriteLineAsync($"f {a + 1}/{a + 1} {c + 1}/{c + 1} {d + 1}/{d + 1}");
                }
            }
            await writer.FlushAsync(ct);
        }
        using var objStream = File.OpenRead(output);
        var objSha = Convert.ToHexString(await SHA256.HashDataAsync(objStream, ct));
        var report = new AztecPcMeshExportReport(
            "WaW360.AztecPcGfxWorldMeshExport.v1",
            "AZTEC_PC_GFXWORLD_19551_TRIANGLE_OBJ_EXPORT_PASS",
            pcFastFile, source.SourceSha256, ZoneSha,
            WorldOffset, IndexStart, IndexSha, VertexStart, VertexSha,
            SurfaceStart, SurfaceSha, VertexCount, IndexCount, SurfaceCount,
            triangles, usedVertex.Count(v => v),
            materials.Count, degenerateFaces, badSurfaceBounds,
            materials.OrderBy(x => x.Key).Select(x =>
                new AztecPcMeshMaterialPointer($"0x{x.Key:X8}", x.Value)).ToArray(),
            output, new FileInfo(output).Length, objSha,
            false, false, false,
            "Source-PC actual GfxWorld geometry is exported to neutral OBJ with surface-local " +
            "index rebasing and source-packed material-pointer labels. There is no texturing " +
            "claim, no Xbox/renderer output, and no GfxWorld FF mutation. Independent " +
            "in-game material/lightmap and DPVS serialization remain pending.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputReport))!);
        await File.WriteAllTextAsync(outputReport, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }), ct);
        return report;
    }

    private static uint U32(byte[] b, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset, 4));
    private static int I32(byte[] b, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset, 4));
    private static ushort U16(byte[] b, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset, 2));
    private static float F(byte[] b, int offset) =>
        BitConverter.Int32BitsToSingle(I32(b, offset));
    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
    private static string Format(float n) =>
        n.ToString("G9", CultureInfo.InvariantCulture);
}

public sealed record AztecPcMeshMaterialPointer(string PackedPCPointer, int SurfaceCount);

public sealed record AztecPcMeshExportReport(
    string Schema, string Status, string OriginalPCFastFile,
    string OriginalPCFastFileSha256, string OriginalPCZoneSha256,
    int OriginalPCGfxWorldHeaderOffset, int OriginalPCIndexOffset,
    string OriginalPCIndexSha256, int OriginalPCVertexOffset,
    string OriginalPCVertexSha256, int OriginalPCSurfaceOffset,
    string OriginalPCSurfaceSha256, int VertexCount, int IndexCount,
    int SurfaceCount, int TriangleCount, int ReferencedVertexCount,
    int DistinctPackedMaterialPointers, int DegenerateFaceIndexTriples,
    int InvalidSurfaceBounds, IReadOnlyList<AztecPcMeshMaterialPointer> Materials,
    string ObjPath, long ObjBytes, string ObjSha256,
    bool MaterialTextureBindingsProven, bool XenonGfxWorldSerialized,
    bool XboxHardwareReady, string Note);
