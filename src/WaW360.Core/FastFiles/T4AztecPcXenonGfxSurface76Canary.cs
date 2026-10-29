using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Genuine PC original Aztec -> 76B T4Xenon GfxSurface ABI structural canary.
/// Source geometry is copied BE into T4Xenon 76-byte fields (vs PC 52B).
/// The 6 Xbox-only topMip float words are derived and validated only when
/// native OAT-loaded 735/735 float32 equality already proved an algorithm.
/// Otherwise these 24 bytes are ZERO placeholders (INVALID FOR GAME LOAD).
/// Material* is ALWAYS ZERO; root indices go into JSON, NEVER pointer words.
/// This artifact is not a FastFile, root, DPVS, or hardware payload.
/// </summary>
public static class T4AztecPcXenonGfxSurface76Canary
{
    private const int Count = 735, PcStride = 52, XboxStride = 76;
    private const int SurfaceAt = 0x058E361E;
    private const int VertexAt = 0x057359F9, VertexCount = 32713, VertexStride = 44;
    private const int IndexAt = 0x049125F2, IndexCount = 58653;
    private const string ZoneSha = "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string SurfaceSha = "919FF1CDB1EC5FCD57ED4020F1908AE813C1FB570C8631C15135E07C5DF852E5";
    private const string VertexSha = "6303269A8170F6F06233DCCE0BF1230EC6A659C4D0EE78CFDF7C14EE242D8041";
    private const string IndexSha = "E89621777CB47C46BB47FE0CA948645D6813151E02A706C2A2325C01BDEFD32D";

    public static async Task<AztecPcXenonGfxSurface76CanaryReport> BuildAsync(
        string topMipCalibrationJson, string verifiedMaterialWorklistJson,
        string originalGeometryJson, string outputDirectory,
        CancellationToken ct = default)
    {
        using var td = JsonDocument.Parse(await File.ReadAllTextAsync(topMipCalibrationJson, ct));
        using var wd = JsonDocument.Parse(await File.ReadAllTextAsync(verifiedMaterialWorklistJson, ct));
        using var gd = JsonDocument.Parse(await File.ReadAllTextAsync(originalGeometryJson, ct));
        var t = td.RootElement; var w = wd.RootElement; var g = gd.RootElement;
        if (t.GetProperty("Schema").GetString() != "WaW360.AztecPcXenonSurfaceTopMipCalibration.v1" ||
            t.GetProperty("Status").GetString() !=
                "AZTEC_PC_VS_NATIVE_XENON_735_SURFACE_TOPMIP_SOURCE_HYPOTHESIS_AUDIT" ||
            t.GetProperty("SurfacesCompared").GetInt32() != Count ||
            t.GetProperty("NativeXenonSurfaceStride").GetInt32() != XboxStride ||
            t.GetProperty("OriginalPcSurfaceStride").GetInt32() != PcStride ||
            t.GetProperty("NativeMaterialFieldAt").GetInt32() != 44 ||
            t.GetProperty("NativeTopMipStart").GetInt32() != 16 ||
            t.GetProperty("NativeTopMipEndExclusive").GetInt32() != 40 ||
            t.GetProperty("XenonGfxSurfaceMaterialPointersWritten").GetBoolean() ||
            t.GetProperty("XboxHardwareReady").GetBoolean() ||
            w.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcVerifiedSurfaceMaterialRelocationPlan.v1" ||
            w.GetProperty("Status").GetString() !=
                "AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY" ||
            w.GetProperty("ExactRelocationSites").GetInt32() != Count ||
            w.GetProperty("VerifiedTypedPcSurfaceIdentities").GetInt32() != Count ||
            w.GetProperty("UniqueGoldenXboxNativeMaterialRootNames").GetInt32() != 39 ||
            w.GetProperty("XenonSurfaceMaterialPointersWritten").GetInt32() != 0 ||
            w.GetProperty("XboxHardwareReady").GetBoolean() ||
            g.GetProperty("Status").GetString() !=
                "AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS" ||
            g.GetProperty("SurfaceCount").GetInt32() != Count ||
            g.GetProperty("VertexCount").GetInt32() != VertexCount ||
            g.GetProperty("IndexCount").GetInt32() != IndexCount ||
            g.GetProperty("ZeroMaterialPointerPlaceholders").GetInt32() != Count ||
            g.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Require exact PC 735 OAT-typed Material identities, native Xenon ABI and source topMip calibration.");

        var derivation = t.GetProperty("CompleteVerifiedTopMipDerivation").GetString()!;
        var topMipReady = t.GetProperty("SourceOnlyTopMipCandidateReadyFor76ByteSerialization").GetBoolean();
        var isKnown = derivation is "PC_SURFACE_BOUNDS_FLOAT32_EXACT"
            or "PC_VERTEX_SPAN_AABB_FLOAT32_EXACT"
            or "PC_TRIANGLE_VERTEX_AABB_FLOAT32_EXACT";
        if ((topMipReady && !isKnown) ||
            (topMipReady && (t.GetProperty("NativeGeometryFieldDisagreements").GetInt32() != 0 ||
                             t.GetProperty("BoundsFloat32ExactMatches").GetInt32() != Count ||
                             t.GetProperty("FiniteNativeTopMipSurfaces").GetInt32() != Count)))
            throw new InvalidDataException("TopMip source-completeness flag contradicts actual calibration totals.");

        var originalPc = g.GetProperty("OriginalPCFastFile").GetString()!;
        if (!Path.GetFullPath(originalPc).Equals(
                Path.GetFullPath(t.GetProperty("OriginalPcFastFile").GetString()!),
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(originalPc).Equals(
                Path.GetFullPath(w.GetProperty("OriginalPcFastFile").GetString()!),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC fastfile differs across the three independent proofs.");

        var zone = await FastFileAnalysisCache.GetZoneAsync(originalPc, ct);
        var b = zone.Bytes;
        if (zone.Platform != WaWPlatform.PC || b.Length != 113701260 ||
            !Sha(b).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(SurfaceAt, Count * PcStride)).Equals(SurfaceSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(VertexAt, VertexCount * VertexStride)).Equals(VertexSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(IndexAt, IndexCount * 2)).Equals(IndexSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(zone.SourceSha256, g.GetProperty("OriginalPCFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC Aztec source geometry identity changed.");

        var intents = w.GetProperty("RelocationIntents").EnumerateArray().ToArray();
        if (intents.Length != Count)
            throw new InvalidDataException("Expected 735 OAT-verified Material root/name references.");

        JsonDocument? nativeDoc = null;
        JsonElement[]? nativeRows = null;
        try
        {
            if (topMipReady)
            {
                // Never trust a stale/partial summary: re-verify EVERY native
                // topMip float against source-derived float32 bits.
                var nativePath = t.GetProperty("RawNativeXboxGfxWorldWitness").GetString()!;
                nativeDoc = JsonDocument.Parse(await File.ReadAllTextAsync(nativePath, ct));
                var native = nativeDoc.RootElement;
                if (native.GetProperty("schema").GetString() !=
                        "WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1" ||
                    native.GetProperty("status").GetString() !=
                        "NATIVE_XENON_GFXWORLD_SURFACE_ABI_LOADER_WITNESS" ||
                    native.GetProperty("surfaceCount").GetInt32() != Count ||
                    native.GetProperty("directMatches").GetInt32() != Count)
                    throw new InvalidDataException("Raw native Xbox GfxWorld witness changed.");
                nativeRows = native.GetProperty("surfaces").EnumerateArray().ToArray();
                if (nativeRows.Length != Count)
                    throw new InvalidDataException("Native Xbox topMip 735-surface source array changed.");
            }
            var xbox = new byte[Count * XboxStride];
            var sites = new List<AztecPcXenon76SurfaceMaterialSite>(Count);
            var validTopMips = 0;
            var triangleCount = 0;
            var distinct = new HashSet<int>();
            for (var i = 0; i < Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var from = SurfaceAt + i * PcStride;
                var to = i * XboxStride;
                var intent = intents[i];
                var first = checked((int)U32(b, from + 4));
                var vertCount = U16(b, from + 8);
                var tris = U16(b, from + 10);
                var startIndex = checked((int)U32(b, from + 12));
                if (first < 0 || vertCount < 3 || first + vertCount > VertexCount ||
                    tris <= 0 || startIndex < 0 || startIndex + tris * 3 > IndexCount ||
                    intent.GetProperty("SurfaceIndex").GetInt32() != i ||
                    intent.GetProperty("TriangleCount").GetInt32() != tris ||
                    !intent.GetProperty("PcLoadedMaterialPointerIdentityProven").GetBoolean() ||
                    !intent.GetProperty("XboxMaterialRootNameIdentityUnique").GetBoolean() ||
                    intent.GetProperty("XenonMaterialPointerWritten").GetBoolean() ||
                    intent.GetProperty("XboxRuntimeMaterialGraphBindingProven").GetBoolean())
                    throw new InvalidDataException($"Source PC surface/witness mismatch at {i}.");

                // Original-PC srfTriangles_t fields that are byte-identical in
                // PC/Xbox ABIs, but endian-swapped to BE. The newly inserted
                // topMip[6] is 16..39 (source derived or UNRESOLVED ZERO).
                Put32(xbox, to, U32(b, from));
                Put32(xbox, to + 4, U32(b, from + 4));
                Put16(xbox, to + 8, (ushort)vertCount);
                Put16(xbox, to + 10, (ushort)tris);
                Put32(xbox, to + 12, U32(b, from + 12));

                if (topMipReady)
                {
                    var min = new float[3] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
                    var max = new float[3] { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
                    if (derivation == "PC_SURFACE_BOUNDS_FLOAT32_EXACT")
                    {
                        for (var a = 0; a < 3; a++)
                        {
                            min[a] = F32(b, from + 28 + 4 * a);
                            max[a] = F32(b, from + 40 + 4 * a);
                        }
                    }
                    else if (derivation == "PC_VERTEX_SPAN_AABB_FLOAT32_EXACT")
                    {
                        for (var k = 0; k < vertCount; k++)
                            Extend(b, first + k, min, max);
                    }
                    else if (derivation == "PC_TRIANGLE_VERTEX_AABB_FLOAT32_EXACT")
                    {
                        for (var k = 0; k < tris * 3; k++)
                        {
                            var local = U16(b, IndexAt + (startIndex + k) * 2);
                            if (local >= vertCount)
                                throw new InvalidDataException($"Index out of range in PC surface {i}.");
                            Extend(b, first + local, min, max);
                        }
                    }
                    var row = nativeRows![i];
                    if (row.GetProperty("index").GetInt32() != i ||
                        row.GetProperty("materialMemoryOrdinal").GetInt32() !=
                            intent.GetProperty("LoadedPcMaterialMemoryOrdinal").GetInt32() ||
                        !string.Equals(row.GetProperty("loadedMaterialName").GetString(),
                            intent.GetProperty("VerifiedPcMaterialName").GetString(), StringComparison.Ordinal))
                        throw new InvalidDataException($"Independent native name/Material pointer row #{i} changed.");
                    var nativeMins = row.GetProperty("topMipMins").EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    var nativeMaxs = row.GetProperty("topMipMaxs").EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    if (nativeMins.Length != 3 || nativeMaxs.Length != 3 ||
                        !Same(min, nativeMins) || !Same(max, nativeMaxs))
                        throw new InvalidDataException($"Source-derived topMip does not exactly equal original Xbox topMip for surface {i}.");
                    for (var a = 0; a < 3; a++)
                    {
                        Put32(xbox, to + 16 + a * 4, unchecked((uint)BitConverter.SingleToInt32Bits(min[a])));
                        Put32(xbox, to + 28 + a * 4, unchecked((uint)BitConverter.SingleToInt32Bits(max[a])));
                    }
                    validTopMips++;
                }

                // Shifts PC GfxSurface.pad (+16) to Xbox +40; Material* remains
                // null at Xbox +44; 4 original attribute bytes move +24 to +48;
                // six bounds float words move PC+28..51 to Xbox+52..75.
                Put32(xbox, to + 40, U32(b, from + 16));
                Put32(xbox, to + 44, 0u);
                b.AsSpan(from + 24, 4).CopyTo(xbox.AsSpan(to + 48, 4));
                for (var f = 0; f < 6; f++)
                    Put32(xbox, to + 52 + f * 4, U32(b, from + 28 + f * 4));

                var beMat = BinaryPrimitives.ReadUInt32BigEndian(xbox.AsSpan(to + 44, 4));
                if (beMat != 0u ||
                    BinaryPrimitives.ReadUInt32BigEndian(xbox.AsSpan(to, 4)) != U32(b, from) ||
                    BinaryPrimitives.ReadUInt32BigEndian(xbox.AsSpan(to + 4, 4)) != (uint)first ||
                    BinaryPrimitives.ReadUInt16BigEndian(xbox.AsSpan(to + 8, 2)) != vertCount ||
                    BinaryPrimitives.ReadUInt16BigEndian(xbox.AsSpan(to + 10, 2)) != tris ||
                    BinaryPrimitives.ReadUInt32BigEndian(xbox.AsSpan(to + 12, 4)) != (uint)startIndex ||
                    BinaryPrimitives.ReadUInt32BigEndian(xbox.AsSpan(to + 40, 4)) != U32(b, from + 16) ||
                    !xbox.AsSpan(to + 48, 4).SequenceEqual(b.AsSpan(from + 24, 4)))
                    throw new InvalidDataException($"Xbox 76B core geometry readback failure at {i}.");

                for (var f = 0; f < 6; f++)
                    if (BinaryPrimitives.ReadUInt32BigEndian(xbox.AsSpan(to + 52 + f * 4, 4)) !=
                        U32(b, from + 28 + f * 4))
                        throw new InvalidDataException($"Xbox bounds readback failure at surface {i}, bound {f}.");

                if (!topMipReady && xbox.AsSpan(to + 16, 24).ToArray().Any(x => x != 0))
                    throw new InvalidDataException($"Unknown Xbox topMip placeholder unexpectedly nonzero at {i}.");

                var rootIndex = intent.GetProperty("UniqueMatchingXboxNativeRootIndex").GetInt32();
                if (rootIndex < 0 || rootIndex >= 8509)
                    throw new InvalidDataException("Golden Xbox Material root index out of range.");
                distinct.Add(rootIndex);
                sites.Add(new AztecPcXenon76SurfaceMaterialSite(
                    i, to + 44, 44, 76, to + 16,
                    intent.GetProperty("VerifiedPcMaterialName").GetString()!,
                    rootIndex, tris, topMipReady, false));
                triangleCount += tris;
            }
            if (triangleCount != 19551 || distinct.Count != 39 || sites.Count != Count ||
                (topMipReady && validTopMips != Count) ||
                (!topMipReady && validTopMips != 0))
                throw new InvalidDataException("Xenon 76B canary source materials/triangles/extra float evidence changed.");

            var dir = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(dir);
            var binPath = Path.Combine(dir, "aztec_735_gfxsurfaces_76byte_be_NON_DEPLOYABLE.bin");
            var reportPath = Path.Combine(dir, "aztec_735_gfxsurfaces_76byte_source_canary.json");
            await File.WriteAllBytesAsync(binPath, xbox, ct);
            var result = new AztecPcXenonGfxSurface76CanaryReport(
                "WaW360.AztecPcXenonGfxSurface76Canary.v1",
                topMipReady
                    ? "AZTEC_PC_735_XENON_76B_GFXSURFACES_SOURCE_DERIVED_TOPMIP_READBACK_ONLY"
                    : "AZTEC_PC_735_XENON_76B_GFXSURFACES_UNRESOLVED_TOPMIP_PLACEHOLDERS_ONLY",
                Path.GetFullPath(originalPc), zone.SourceSha256, ZoneSha,
                Path.GetFullPath(topMipCalibrationJson),
                Path.GetFullPath(verifiedMaterialWorklistJson),
                735, 76, 55860, 44, 39, 19551, validTopMips,
                Count - validTopMips, 735, 0, topMipReady,
                binPath, Sha(xbox), sites.ToArray(),
                false, false, false,
                "Standalone big-endian 76B T4Xenon ABI canary from genuine PC source. " +
                "Only 735/735 bit-exact source-derived Xbox topMip floats can be marked " +
                "resolved; otherwise all six Xbox-only float fields are 0x00000000 " +
                "placeholders and this buffer MUST NOT be used in a real GfxWorld. " +
                "ALL Material* pointers at +44 are zero; Xbox root indices remain in JSON. " +
                "No Xenon pointer relocation, no DPVS, no GfxWorld root, no OAT FF, NO XBOX.");
            await File.WriteAllTextAsync(reportPath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
            return result;
        }
        finally { nativeDoc?.Dispose(); }
    }
    private static void Extend(byte[] b, int v, float[] min, float[] max)
    {
        var at = VertexAt + v * VertexStride;
        for (var a = 0; a < 3; a++)
        {
            var x = F32(b, at + a * 4);
            if (!float.IsFinite(x))
                throw new InvalidDataException($"Non-finite original PC vertex {v}.");
            min[a] = MathF.Min(min[a], x); max[a] = MathF.Max(max[a], x);
        }
    }
    private static bool Same(float[] a, float[] b) =>
        a.Length == 3 && b.Length == 3 &&
        Enumerable.Range(0, 3).All(k =>
            BitConverter.SingleToInt32Bits(a[k]) == BitConverter.SingleToInt32Bits(b[k]));
    private static float F32(byte[] b, int at) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at, 4)));
    private static uint U32(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
    private static int U16(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2));
    private static void Put32(byte[] b, int at, uint v) =>
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at, 4), v);
    private static void Put16(byte[] b, int at, ushort v) =>
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at, 2), v);
    private static string Sha(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(SHA256.HashData(b));
}

public sealed record AztecPcXenon76SurfaceMaterialSite(
    int SurfaceIndex, int FieldOffsetInCanaryBuffer, int FieldOffsetIn76ByteSurface,
    int SurfaceStride, int TopMipDataStart, string VerifiedPcMaterialName,
    int UniqueGoldenXboxRootIndex, int Triangles, bool XboxTopMipSourceProven,
    bool MaterialPointerWritten);

public sealed record AztecPcXenonGfxSurface76CanaryReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string TopMipCalibration, string MaterialIdentityWorklist,
    int Surfaces, int SurfaceStride, int ByteLength, int MaterialOffset,
    int DistinctNamedXboxMaterialRoots, int Triangles,
    int SourceDerivedNativeExactTopMipSurfaces, int UnresolvedTopMipSurfaces,
    int ZeroMaterialPointerFields, int WrittenMaterialPointerFields,
    bool TopMipFullySourceProven, string BinaryFile, string BinarySha256,
    IReadOnlyList<AztecPcXenon76SurfaceMaterialSite> MaterialRelocationSites,
    bool XenonGfxWorldSerialized, bool OatIntegratedFastFileValidated,
    bool XboxHardwareReady, string Note);
