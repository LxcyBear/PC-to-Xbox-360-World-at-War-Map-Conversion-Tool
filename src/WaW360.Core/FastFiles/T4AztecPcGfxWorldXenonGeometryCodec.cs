using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Original-PC Aztec GfxWorld -> endian-aware Xenon geometry components.
/// This deliberately stops short of writing a GfxWorld XAsset: the 39 source
/// packed Material pointers are PC-only and must be resolved into live Xbox
/// Material roots before the world can be safely loaded by the console.
/// </summary>
public static class T4AztecPcGfxWorldXenonGeometryCodec
{
    private const int RootOffset = 0x049122C0;
    private const int IndexOffset = 0x049125F2;
    private const int IndexCount = 58653;
    private const int VertexOffset = 0x057359F9;
    private const int VertexCount = 32713;
    private const int VertexStride = 44;
    private const int SurfaceOffset = 0x058E361E;
    private const int SurfaceCount = 735;
    private const int SurfaceStride = 52;
    private const string ZoneSha = "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string IndexSha = "E89621777CB47C46BB47FE0CA948645D6813151E02A706C2A2325C01BDEFD32D";
    private const string VertexSha = "6303269A8170F6F06233DCCE0BF1230EC6A659C4D0EE78CFDF7C14EE242D8041";
    private const string SurfaceSha = "919FF1CDB1EC5FCD57ED4020F1908AE813C1FB570C8631C15135E07C5DF852E5";

    public static async Task<AztecXenonGeometryComponentReport> ConvertAsync(
        string originalPcFastFile, string pcMeshReport,
        string outputDirectory, CancellationToken ct = default)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        originalPcFastFile = Path.GetFullPath(originalPcFastFile);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(pcMeshReport, ct));
        var report = document.RootElement;
        if (report.GetProperty("Schema").GetString() != "WaW360.AztecPcGfxWorldMeshExport.v1" ||
            report.GetProperty("Status").GetString() != "AZTEC_PC_GFXWORLD_19551_TRIANGLE_OBJ_EXPORT_PASS" ||
            report.GetProperty("XboxHardwareReady").GetBoolean() ||
            report.GetProperty("XenonGfxWorldSerialized").GetBoolean() ||
            report.GetProperty("MaterialTextureBindingsProven").GetBoolean() ||
            report.GetProperty("VertexCount").GetInt32() != VertexCount ||
            report.GetProperty("IndexCount").GetInt32() != IndexCount ||
            report.GetProperty("SurfaceCount").GetInt32() != SurfaceCount ||
            report.GetProperty("TriangleCount").GetInt32() != IndexCount / 3 ||
            report.GetProperty("DegenerateFaceIndexTriples").GetInt32() != 0 ||
            !Eq(report.GetProperty("OriginalPCZoneSha256").GetString(), ZoneSha) ||
            !Eq(report.GetProperty("OriginalPCIndexSha256").GetString(), IndexSha) ||
            !Eq(report.GetProperty("OriginalPCVertexSha256").GetString(), VertexSha) ||
            !Eq(report.GetProperty("OriginalPCSurfaceSha256").GetString(), SurfaceSha))
            throw new InvalidDataException("Require the exact original PC Aztec OBJ mesh PASS with pinned source buffers.");

        var source = await FastFileAnalysisCache.GetZoneAsync(originalPcFastFile, ct);
        var b = source.Bytes;
        if (source.Platform != WaWPlatform.PC || b.Length != 113701260 ||
            !Eq(source.SourceSha256, report.GetProperty("OriginalPCFastFileSha256").GetString()) ||
            !Eq(Sha(b), ZoneSha) ||
            U32(b, RootOffset + 16) != IndexCount ||
            U32(b, RootOffset + 24) != SurfaceCount ||
            U32(b, RootOffset + 52) != VertexCount ||
            !Eq(Sha(b.AsSpan(IndexOffset, IndexCount * 2)), IndexSha) ||
            !Eq(Sha(b.AsSpan(VertexOffset, VertexCount * VertexStride)), VertexSha) ||
            !Eq(Sha(b.AsSpan(SurfaceOffset, SurfaceCount * SurfaceStride)), SurfaceSha))
            throw new InvalidDataException("Original-PC Aztec GfxWorld exact source data changed.");

        var indices = new byte[IndexCount * 2];
        var vertices = new byte[VertexCount * VertexStride];
        var surfaces = new byte[SurfaceCount * SurfaceStride];
        var materialRecords = new List<AztecXenonGeometryMaterialSlot>(SurfaceCount);
        var materials = new Dictionary<uint, int>();
        var indexOwner = new bool[IndexCount];
        var relativeIndices = 0;
        var zeroMaterialPtrs = 0;
        var lastIndex = 0;
        for (var i = 0; i < IndexCount; i++)
        {
            if ((i & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            BinaryPrimitives.WriteUInt16BigEndian(indices.AsSpan(i * 2, 2),
                U16(b, IndexOffset + i * 2));
        }

        // T4 vertex: xyz/binormalSign float32, color bytes[4],
        // texCoord[2]/lmapCoord[2] float32, packed normal/tangent uint32.
        // Retains the source 4-byte color ordering exactly, and changes
        // the endian of only numeric fields. Packed normal/tangent format
        // still requires hardware-level vertex declaration verification.
        for (var i = 0; i < VertexCount; i++)
        {
            if ((i & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
            var src = VertexOffset + i * VertexStride;
            var dest = i * VertexStride;
            for (var off = 0; off < VertexStride; off += 4)
            {
                if (off == 16)
                {
                    b.AsSpan(src + off, 4).CopyTo(vertices.AsSpan(dest + off, 4));
                    continue;
                }
                BinaryPrimitives.WriteUInt32BigEndian(vertices.AsSpan(dest + off, 4),
                    U32(b, src + off));
            }
        }

        for (var s = 0; s < SurfaceCount; s++)
        {
            ct.ThrowIfCancellationRequested();
            var src = SurfaceOffset + s * SurfaceStride;
            var dst = s * SurfaceStride;
            var first = checked((int)U32(b, src + 4));
            var count = U16(b, src + 8);
            var faces = U16(b, src + 10);
            var baseIndex = checked((int)U32(b, src + 12));
            var material = U32(b, src + 20);
            if (count < 3 || first < 0 || first + count > VertexCount ||
                faces < 1 || baseIndex < 0 || baseIndex + faces * 3 > IndexCount ||
                !T4ZonePointer.IsWithinBlock(material, source.ZoneHeader))
                throw new InvalidDataException($"PC GfxSurface#{s} source geometry/material pointer invalid.");
            // BIG-ENDIAN 52-byte ORIGINAL-PC surface layout, NOT a native
            // 76-byte T4Xenon GfxSurface: Xbox adds six topMip floats,
            // shifting Material* from +20 to +44. PC pointer stays null.
            // Emit null as an explicit blocker until root identities are
            // rebound; keep the original pointer in the relocation ledger.
            foreach (var off in new[] { 0, 4, 12, 16, 20, 28, 32, 36, 40, 44, 48 })
                BinaryPrimitives.WriteUInt32BigEndian(surfaces.AsSpan(dst + off, 4),
                    off == 20 ? 0 : U32(b, src + off));
            BinaryPrimitives.WriteUInt16BigEndian(surfaces.AsSpan(dst + 8, 2), count);
            BinaryPrimitives.WriteUInt16BigEndian(surfaces.AsSpan(dst + 10, 2), faces);
            b.AsSpan(src + 24, 4).CopyTo(surfaces.AsSpan(dst + 24, 4));
            zeroMaterialPtrs++;
            materials.TryGetValue(material, out var usage);
            materials[material] = usage + 1;
            materialRecords.Add(new AztecXenonGeometryMaterialSlot(
                s, $"0x{material:X8}", "", false));
            for (var k = 0; k < faces * 3; k++)
            {
                var i = baseIndex + k;
                if (indexOwner[i])
                    throw new InvalidDataException($"GfxSurface index slot #{i} owned twice.");
                indexOwner[i] = true;
                if (U16(b, IndexOffset + i * 2) >= count)
                    throw new InvalidDataException($"GfxSurface#{s} has out-of-range local index.");
                relativeIndices++;
                lastIndex = Math.Max(lastIndex, i + 1);
            }
        }
        if (indexOwner.Any(owned => !owned) ||
            relativeIndices != IndexCount || lastIndex != IndexCount ||
            materials.Count != 39 || zeroMaterialPtrs != SurfaceCount)
            throw new InvalidDataException("Original-PC 735 surface/39 material source topology changed.");

        // Independent full-array big-endian readback back to the *original*
        // source words. The null Material field is the sole planned
        // difference and is captured in the relocation ledger.
        for (var i = 0; i < IndexCount; i++)
            if (BinaryPrimitives.ReadUInt16BigEndian(indices.AsSpan(i * 2, 2)) !=
                U16(b, IndexOffset + i * 2))
                throw new InvalidDataException($"Xenon index readback failed #{i}.");
        for (var i = 0; i < VertexCount; i++)
        {
            var src = VertexOffset + i * VertexStride;
            var dst = i * VertexStride;
            for (var off = 0; off < VertexStride; off += 4)
            {
                if (off == 16)
                {
                    if (!b.AsSpan(src + off, 4).SequenceEqual(vertices.AsSpan(dst + off, 4)))
                        throw new InvalidDataException($"Xenon vertex color readback failed #{i}.");
                }
                else if (BinaryPrimitives.ReadUInt32BigEndian(vertices.AsSpan(dst + off, 4)) !=
                         U32(b, src + off))
                    throw new InvalidDataException($"Xenon vertex numeric readback failed #{i}, offset {off}.");
            }
        }
        for (var s = 0; s < SurfaceCount; s++)
        {
            var src = SurfaceOffset + s * SurfaceStride;
            var dst = s * SurfaceStride;
            for (var off = 0; off < SurfaceStride; off += 4)
            {
                if (off is 8 or 24) continue;
                var actual = BinaryPrimitives.ReadUInt32BigEndian(surfaces.AsSpan(dst + off, 4));
                if (actual != (off == 20 ? 0u : U32(b, src + off)))
                    throw new InvalidDataException($"Xenon surface#{s} 32-bit field readback failed +{off}.");
            }
            if (BinaryPrimitives.ReadUInt16BigEndian(surfaces.AsSpan(dst + 8, 2)) != U16(b, src + 8) ||
                BinaryPrimitives.ReadUInt16BigEndian(surfaces.AsSpan(dst + 10, 2)) != U16(b, src + 10) ||
                !surfaces.AsSpan(dst + 24, 4).SequenceEqual(b.AsSpan(src + 24, 4)))
                throw new InvalidDataException($"Xenon surface#{s} 16-bit/count/flag readback failed.");
        }

        Directory.CreateDirectory(outputDirectory);
        var indexPath = Path.Combine(outputDirectory, "gfxworld_indices_58653_xenon_be.bin");
        var vertexPath = Path.Combine(outputDirectory, "gfxworld_vertices_32713_xenon_be.bin");
        var surfacePath = Path.Combine(outputDirectory, "gfxworld_surfaces_735_xenon_be_unbound_materials.bin");
        var ledgerPath = Path.Combine(outputDirectory, "gfxworld_surface_735_pc_material_relocation_ledger.json");
        var manifest = Path.Combine(outputDirectory, "gfxworld_source_to_xenon_geometry_components.json");
        await File.WriteAllBytesAsync(indexPath, indices, ct);
        await File.WriteAllBytesAsync(vertexPath, vertices, ct);
        await File.WriteAllBytesAsync(surfacePath, surfaces, ct);
        await File.WriteAllTextAsync(ledgerPath,
            JsonSerializer.Serialize(materialRecords, new JsonSerializerOptions { WriteIndented = true }), ct);

        var final = new AztecXenonGeometryComponentReport(
            "WaW360.AztecPcGfxWorldXenonGeometryComponents.v1",
            "AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS",
            originalPcFastFile, source.SourceSha256, ZoneSha,
            IndexCount, VertexCount, SurfaceCount, IndexCount / 3,
            IndexSha, VertexSha, SurfaceSha, indexPath, Sha(indices),
            vertexPath, Sha(vertices), surfacePath, Sha(surfaces),
            ledgerPath, materialRecords.Count, materials.Count,
            relativeIndices, zeroMaterialPtrs,
            false, false, false,
            "Original-PC bytes only. 52B surface is an endian-converted PC-layout DIAGNOSTIC, NOT a 76B Xbox ABI surface. " +
            "readback passes, but all 735 source material pointers are intentionally unbound (null) " +
            "in the endian-only surface buffer. Packed normals/vertex semantics, OAT GfxWorld root " +
            "allocation, DPVS, runtime resources and Material/lightmaps remain unverified. NO FF.");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(final,
            new JsonSerializerOptions { WriteIndented = true }), ct);
        return final;
    }

    private static bool Eq(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static ushort U16(byte[] b, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset, 2));
    private static uint U32(byte[] b, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset, 4));
    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}

public sealed record AztecXenonGeometryMaterialSlot(
    int SurfaceIndex, string OriginalPcPackedPointer, string XboxMaterialRootName,
    bool BoundToXboxMaterialRoot);

public sealed record AztecXenonGeometryComponentReport(
    string Schema, string Status, string OriginalPCFastFile,
    string OriginalPCFastFileSha256, string OriginalPCZoneSha256,
    int IndexCount, int VertexCount, int SurfaceCount, int TriangleCount,
    string OriginalPCIndicesSha256, string OriginalPCVerticesSha256,
    string OriginalPCSurfacesSha256, string XenonIndicesFile,
    string XenonIndicesSha256, string XenonVerticesFile,
    string XenonVerticesSha256, string XenonSurfacesFile,
    string XenonSurfacesSha256, string MaterialRelocationLedger,
    int SurfacesRequiringMaterialBinding, int DistinctPcMaterialPointers,
    int SurfaceLocalIndicesChecked, int ZeroMaterialPointerPlaceholders,
    bool XenonGfxWorldRootEmitted, bool OatGfxWorldBodyLoaded,
    bool XboxHardwareReady, string Note);
