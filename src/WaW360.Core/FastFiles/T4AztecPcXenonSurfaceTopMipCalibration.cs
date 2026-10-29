using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Independently tests whether original-PC source geometry can explain the
/// Xbox-only 6 x float GfxSurface.tris.topMipMins/Maxs. The 76-byte Xenon ABI
/// is known from native OAT, but a field's ABI location does not establish its
/// source algorithm. Fail closed: report disagreement, never invent topMip.
/// </summary>
public static class T4AztecPcXenonSurfaceTopMipCalibration
{
    private const int SurfaceCount = 735;
    private const int PCSurfaceStride = 52;
    private const int XenonSurfaceStride = 76;
    private const int MaterialFieldXenon = 44;
    private const int SurfaceAt = 0x058E361E;
    private const int VertexAt = 0x057359F9;
    private const int VertexCount = 32713;
    private const int VertexStride = 44;
    private const int IndicesAt = 0x049125F2;
    private const int IndexCount = 58653;
    private const string ZoneSha = "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string SurfaceSha = "919FF1CDB1EC5FCD57ED4020F1908AE813C1FB570C8631C15135E07C5DF852E5";
    private const string VertexSha = "6303269A8170F6F06233DCCE0BF1230EC6A659C4D0EE78CFDF7C14EE242D8041";
    private const string IndexSha = "E89621777CB47C46BB47FE0CA948645D6813151E02A706C2A2325C01BDEFD32D";

    public static async Task<AztecPcXenonSurfaceTopMipCalibrationReport> ProbeAsync(
        string xenonAbiReport, string verifiedMaterialWorklist,
        string sourceGeometryReport, string outputJson, CancellationToken ct = default)
    {
        using var ad = JsonDocument.Parse(await File.ReadAllTextAsync(xenonAbiReport, ct));
        using var wd = JsonDocument.Parse(await File.ReadAllTextAsync(verifiedMaterialWorklist, ct));
        using var gd = JsonDocument.Parse(await File.ReadAllTextAsync(sourceGeometryReport, ct));
        var a = ad.RootElement;
        var w = wd.RootElement;
        var g = gd.RootElement;
        if (a.GetProperty("schema").GetString() !=
                "WaW360.AztecNativeXenonGfxWorldSurfaceAbiReconciliation.v1" ||
            a.GetProperty("status").GetString() !=
                "AZTEC_NATIVE_XENON_76_BYTE_GFXSURFACE_ABI_LOADED_SOURCE_WITNESS" ||
            a.GetProperty("nativeGfxSurfaceStride").GetInt32() != XenonSurfaceStride ||
            a.GetProperty("nativeGfxSurfaceMaterialFieldOffset").GetInt32() != MaterialFieldXenon ||
            a.GetProperty("nativeGfxSurfaceTopMipFloatStart").GetInt32() != 16 ||
            a.GetProperty("nativeGfxSurfaceTopMipFloatEndExclusive").GetInt32() != 40 ||
            a.GetProperty("nativeWorldSurfaces").GetInt32() != SurfaceCount ||
            a.GetProperty("nativeWorldVertices").GetInt32() != VertexCount ||
            a.GetProperty("nativeWorldIndices").GetInt32() != IndexCount ||
            a.GetProperty("nativeMaterialMemoryEntries").GetInt32() != 39 ||
            a.GetProperty("nativeUniqueDirectMaterialMatches").GetInt32() != SurfaceCount ||
            a.GetProperty("nativeUnmatchedMaterialPointers").GetInt32() != 0 ||
            a.GetProperty("nativeAmbiguousMaterialPointers").GetInt32() != 0 ||
            !a.GetProperty("nativeGeometryAndMaterialCountsSameAsPc").GetBoolean() ||
            a.GetProperty("surfaceTriangleCountsMatchingByOrdinal").GetInt32() != SurfaceCount ||
            a.GetProperty("surfaceMaterialNamesMatchingByOrdinal").GetInt32() != SurfaceCount ||
            a.GetProperty("surfaceMaterialMemorySlotsMatchingByOrdinal").GetInt32() != SurfaceCount ||
            a.GetProperty("completeXenonTopMipDerivationProven").GetBoolean() ||
            a.GetProperty("convertedXenonSurfaceMaterialFieldsWritten").GetInt32() != 0 ||
            a.GetProperty("hardwareReady").GetBoolean() ||
            w.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcVerifiedSurfaceMaterialRelocationPlan.v1" ||
            w.GetProperty("Status").GetString() !=
                "AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY" ||
            w.GetProperty("ExactRelocationSites").GetInt32() != SurfaceCount ||
            w.GetProperty("VerifiedTypedPcSurfaceIdentities").GetInt32() != SurfaceCount ||
            w.GetProperty("UniqueGoldenXboxNativeMaterialRootNames").GetInt32() != 39 ||
            !w.GetProperty("AllPcSourceMaterialIdentitiesVerified").GetBoolean() ||
            w.GetProperty("XenonSurfaceMaterialPointersWritten").GetInt32() != 0 ||
            w.GetProperty("XboxHardwareReady").GetBoolean() ||
            g.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcGfxWorldXenonGeometryComponents.v1" ||
            g.GetProperty("Status").GetString() !=
                "AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS" ||
            g.GetProperty("SurfaceCount").GetInt32() != SurfaceCount ||
            g.GetProperty("VertexCount").GetInt32() != VertexCount ||
            g.GetProperty("IndexCount").GetInt32() != IndexCount ||
            g.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Require matching, proven 735 PC material identities, 76B native Xbox ABI, and unchanged source-layout BE geometry.");

        var nativeRawFile = a.GetProperty("rawNativeOatWitness").GetString()!;
        if (!File.Exists(nativeRawFile))
            throw new FileNotFoundException("The actual OAT-loaded Xbox native GfxWorld witness is missing (do not infer topMip from summary).", nativeRawFile);
        using var nd = JsonDocument.Parse(await File.ReadAllTextAsync(nativeRawFile, ct));
        var n = nd.RootElement;
        if (n.GetProperty("schema").GetString() != "WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1" ||
            n.GetProperty("status").GetString() != "NATIVE_XENON_GFXWORLD_SURFACE_ABI_LOADER_WITNESS" ||
            n.GetProperty("abi").GetProperty("gfxSurfaceSize").GetInt32() != XenonSurfaceStride ||
            n.GetProperty("abi").GetProperty("materialFieldAt").GetInt32() != MaterialFieldXenon ||
            n.GetProperty("surfaceCount").GetInt32() != SurfaceCount ||
            n.GetProperty("vertexCount").GetInt32() != VertexCount ||
            n.GetProperty("indexCount").GetInt32() != IndexCount ||
            n.GetProperty("directMatches").GetInt32() != SurfaceCount ||
            n.GetProperty("unmatched").GetInt32() != 0 ||
            n.GetProperty("ambiguous").GetInt32() != 0 ||
            n.GetProperty("triangles").GetInt64() != 19551)
            throw new InvalidDataException("Raw independent Xbox reference GfxWorld layout/surface identity changed.");
        var nativeSurfaces = n.GetProperty("surfaces").EnumerateArray().ToArray();
        var intents = w.GetProperty("RelocationIntents").EnumerateArray().ToArray();
        if (nativeSurfaces.Length != SurfaceCount || intents.Length != SurfaceCount)
            throw new InvalidDataException("Missing one or more of 735 source/Xbox surface identities.");

        var pcPath = g.GetProperty("OriginalPCFastFile").GetString()!;
        if (!Path.GetFullPath(pcPath).Equals(
                Path.GetFullPath(w.GetProperty("OriginalPcFastFile").GetString()!),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PC source FF identity disagrees with verified material relocation worklist.");
        var source = await FastFileAnalysisCache.GetZoneAsync(pcPath, ct);
        var b = source.Bytes;
        if (source.Platform != WaWPlatform.PC || b.Length != 113701260 ||
            !Sha(b).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !source.SourceSha256.Equals(
                g.GetProperty("OriginalPCFastFileSha256").GetString(), StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(SurfaceAt, SurfaceCount * PCSurfaceStride))
                .Equals(SurfaceSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(VertexAt, VertexCount * VertexStride))
                .Equals(VertexSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(IndicesAt, IndexCount * 2))
                .Equals(IndexSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC Aztec geometry source bytes no longer match pinned hashes.");

        int identicalFirstVertex = 0, identicalVertexCount = 0, identicalBaseIndex = 0;
        int identicalVertexLayerData = 0, identicalSourceBounds = 0;
        int exactBounds = 0, exactVertexSpan = 0, exactTriangleVerts = 0;
        int closeBounds = 0, closeVertexSpan = 0, closeTriangleVerts = 0;
        int finiteNativeTopMip = 0;
        int inconsistentSurfaceGeometry = 0;
        var samples = new List<AztecPcXenonTopMipSample>();
        for (var i = 0; i < SurfaceCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var at = SurfaceAt + i * PCSurfaceStride;
            var row = nativeSurfaces[i];
            var intent = intents[i];
            int first = checked((int)U32(b, at + 4));
            int count = U16(b, at + 8);
            int tris = U16(b, at + 10);
            int firstIndex = checked((int)U32(b, at + 12));
            int layer = unchecked((int)U32(b, at));
            if (first < 0 || count < 3 || first + count > VertexCount ||
                tris == 0 || firstIndex < 0 || firstIndex + tris * 3 > IndexCount ||
                row.GetProperty("index").GetInt32() != i ||
                intent.GetProperty("SurfaceIndex").GetInt32() != i ||
                intent.GetProperty("TriangleCount").GetInt32() != tris ||
                intent.GetProperty("LoadedPcMaterialMemoryOrdinal").GetInt32() !=
                    row.GetProperty("materialMemoryOrdinal").GetInt32() ||
                !string.Equals(intent.GetProperty("VerifiedPcMaterialName").GetString(),
                    row.GetProperty("loadedMaterialName").GetString(), StringComparison.Ordinal) ||
                intent.GetProperty("XenonMaterialPointerWritten").GetBoolean() ||
                row.GetProperty("triangleCount").GetInt32() != tris)
                throw new InvalidDataException($"PC source and loaded Xenon material/triangle identity disagree for surface #{i}.");

            bool firstOk = row.GetProperty("firstVertex").GetInt32() == first;
            bool countOk = row.GetProperty("vertexCount").GetInt32() == count;
            bool indexOk = row.GetProperty("baseIndex").GetInt32() == firstIndex;
            bool layerOk = row.GetProperty("vertexLayerData").GetInt32() == layer;
            if (firstOk) identicalFirstVertex++;
            if (countOk) identicalVertexCount++;
            if (indexOk) identicalBaseIndex++;
            if (layerOk) identicalVertexLayerData++;
            if (!(firstOk && countOk && indexOk && layerOk))
                inconsistentSurfaceGeometry++;

            var pcMin = new float[3];
            var pcMax = new float[3];
            for (int axis = 0; axis < 3; axis++)
            {
                pcMin[axis] = F32(b, at + 28 + axis * 4);
                pcMax[axis] = F32(b, at + 40 + axis * 4);
            }
            var nativeBoundsMin = ReadTriple(row.GetProperty("boundsMins"));
            var nativeBoundsMax = ReadTriple(row.GetProperty("boundsMaxs"));
            bool boundExact = AllSameBits(pcMin, nativeBoundsMin) &&
                AllSameBits(pcMax, nativeBoundsMax);
            if (boundExact) identicalSourceBounds++;
            var topMin = ReadTriple(row.GetProperty("topMipMins"));
            var topMax = ReadTriple(row.GetProperty("topMipMaxs"));
            var hasTop = topMin is not null && topMax is not null;
            var spanMin = new float[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            var spanMax = new float[] { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
            var usedMin = new float[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            var usedMax = new float[] { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
            for (int k = 0; k < count; k++)
                Extend(b, first + k, spanMin, spanMax);
            for (int k = 0; k < tris * 3; k++)
            {
                int localIndex = U16(b, IndicesAt + (firstIndex + k) * 2);
                if (localIndex >= count)
                    throw new InvalidDataException($"Original PC surface #{i} has invalid triangle-local index {localIndex}.");
                Extend(b, first + localIndex, usedMin, usedMax);
            }

            var boundsExact = false;
            var verticesExact = false;
            var triangleExact = false;
            var boundsClose = false;
            var verticesClose = false;
            var triangleClose = false;
            if (hasTop)
            {
                finiteNativeTopMip++;
                boundsExact = AllSameBits(pcMin, topMin) && AllSameBits(pcMax, topMax);
                verticesExact = AllSameBits(spanMin, topMin) && AllSameBits(spanMax, topMax);
                triangleExact = AllSameBits(usedMin, topMin) && AllSameBits(usedMax, topMax);
                boundsClose = AllClose(pcMin, topMin) && AllClose(pcMax, topMax);
                verticesClose = AllClose(spanMin, topMin) && AllClose(spanMax, topMax);
                triangleClose = AllClose(usedMin, topMin) && AllClose(usedMax, topMax);
                if (boundsExact) exactBounds++;
                if (verticesExact) exactVertexSpan++;
                if (triangleExact) exactTriangleVerts++;
                if (boundsClose) closeBounds++;
                if (verticesClose) closeVertexSpan++;
                if (triangleClose) closeTriangleVerts++;
            }
            if (samples.Count < 12 && (i < 4 ||
                 !boundsClose || !verticesClose || !triangleClose ||
                 !firstOk || !countOk || !indexOk || !layerOk))
            {
                samples.Add(new AztecPcXenonTopMipSample(
                    i, first, count, tris, firstIndex, layer,
                    firstOk, countOk, indexOk, layerOk, boundExact,
                    pcMin, pcMax, spanMin, spanMax, usedMin, usedMax,
                    topMin, topMax, boundsExact, verticesExact,
                    triangleExact, boundsClose, verticesClose, triangleClose));
            }
        }

        var allStructural = inconsistentSurfaceGeometry == 0 && identicalSourceBounds == SurfaceCount;
        string supportedDerivation =
            exactBounds == SurfaceCount ? "PC_SURFACE_BOUNDS_FLOAT32_EXACT" :
            exactVertexSpan == SurfaceCount ? "PC_VERTEX_SPAN_AABB_FLOAT32_EXACT" :
            exactTriangleVerts == SurfaceCount ? "PC_TRIANGLE_VERTEX_AABB_FLOAT32_EXACT" :
            "NONE_PROVEN";
        bool canEmit76 = allStructural && finiteNativeTopMip == SurfaceCount &&
            supportedDerivation != "NONE_PROVEN";
        var result = new AztecPcXenonSurfaceTopMipCalibrationReport(
            "WaW360.AztecPcXenonSurfaceTopMipCalibration.v1",
            "AZTEC_PC_VS_NATIVE_XENON_735_SURFACE_TOPMIP_SOURCE_HYPOTHESIS_AUDIT",
            Path.GetFullPath(pcPath), source.SourceSha256,
            ZoneSha, Path.GetFullPath(nativeRawFile), Path.GetFullPath(xenonAbiReport),
            Path.GetFullPath(verifiedMaterialWorklist),
            735, 76, 52, 44, 16, 40,
            identicalFirstVertex, identicalVertexCount,
            identicalBaseIndex, identicalVertexLayerData, identicalSourceBounds,
            inconsistentSurfaceGeometry, finiteNativeTopMip,
            exactBounds, exactVertexSpan, exactTriangleVerts,
            closeBounds, closeVertexSpan, closeTriangleVerts,
            supportedDerivation, canEmit76, false, false,
            samples.ToArray(),
            "Read-only comparison of 735 original-PC surfaces and independent " +
            "loaded native-Xbox topMip float vectors. EXACT is float32 bit " +
            "equality after 9-digit native JSON roundtrip. CLOSE is a diagnostic " +
            "tolerance, not sufficient to authorize authentic Xbox payload. " +
            "Only a 735/735 EXACT derivation plus 735/735 structural/PC-bound " +
            "matches clears the source topMip-derivation evidence gate. " +
            "No 76B buffer is emitted here; no Xenon Material* reference is written; " +
            "the older 52B endian-only preview is NOT Xbox ABI. No FF.");
        var dest = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllTextAsync(dest, JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }

    private static void Extend(byte[] b, int vertex, float[] min, float[] max)
    {
        int at = VertexAt + vertex * VertexStride;
        for (int a = 0; a < 3; a++)
        {
            float x = F32(b, at + 4 * a);
            if (!float.IsFinite(x))
                throw new InvalidDataException($"Nonfinite PC vertex coordinate #{vertex}, axis {a}.");
            min[a] = MathF.Min(min[a], x);
            max[a] = MathF.Max(max[a], x);
        }
    }
    private static float F32(byte[] b, int at) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at, 4)));
    private static uint U32(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
    private static int U16(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2));
    private static float[]? ReadTriple(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Null) return null;
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3)
            throw new InvalidDataException("Native Xbox topMip/bounds vector length changed.");
        var a = e.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        return a.Any(x => !float.IsFinite(x)) ? null : a;
    }
    private static bool AllSameBits(float[]? a, float[]? b)
    {
        if (a is null || b is null) return false;
        return a.Length == 3 && b.Length == 3 &&
            Enumerable.Range(0, 3).All(i =>
                BitConverter.SingleToInt32Bits(a[i]) == BitConverter.SingleToInt32Bits(b[i]));
    }
    private static bool AllClose(float[]? a, float[]? b)
    {
        if (a is null || b is null) return false;
        return a.Length == 3 && b.Length == 3 &&
            Enumerable.Range(0, 3).All(i =>
                MathF.Abs(a[i] - b[i]) <= 0.00001f * MathF.Max(1, MathF.Max(MathF.Abs(a[i]), MathF.Abs(b[i]))));
    }
    private static string Sha(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(SHA256.HashData(b));
}

public sealed record AztecPcXenonTopMipSample(
    int Surface, int PcFirstVertex, int PcVertexCount, int Triangles,
    int PcBaseIndex, int PcVertexLayerData,
    bool NativeFirstVertexMatches, bool NativeVertexCountMatches,
    bool NativeBaseIndexMatches, bool NativeVertexLayerDataMatches,
    bool NativeBoundsExactPc,
    float[] PcSurfaceBoundsMins, float[] PcSurfaceBoundsMaxs,
    float[] PcVertexRangeMins, float[] PcVertexRangeMaxs,
    float[] PcTriangleUsedMins, float[] PcTriangleUsedMaxs,
    float[]? NativeTopMipMins, float[]? NativeTopMipMaxs,
    bool ExactPcBounds, bool ExactPcVertexRange, bool ExactPcTriangleUsed,
    bool ClosePcBounds, bool ClosePcVertexRange, bool ClosePcTriangleUsed);

public sealed record AztecPcXenonSurfaceTopMipCalibrationReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string RawNativeXboxGfxWorldWitness, string NativeAbiReport,
    string VerifiedPcToXboxMaterialNameWorklist,
    int SurfacesCompared, int NativeXenonSurfaceStride,
    int OriginalPcSurfaceStride, int NativeMaterialFieldAt,
    int NativeTopMipStart, int NativeTopMipEndExclusive,
    int FirstVertexExactMatches, int VertexCountExactMatches,
    int BaseIndexExactMatches, int VertexLayerDataExactMatches,
    int BoundsFloat32ExactMatches, int NativeGeometryFieldDisagreements,
    int FiniteNativeTopMipSurfaces,
    int NativeTopMipEqualsPcSurfaceBoundsExact,
    int NativeTopMipEqualsPcVertexSpanAabbExact,
    int NativeTopMipEqualsPcTriangleVertexAabbExact,
    int NativeTopMipCloseToPcSurfaceBounds,
    int NativeTopMipCloseToPcVertexSpanAabb,
    int NativeTopMipCloseToPcTriangleVertexAabb,
    string CompleteVerifiedTopMipDerivation,
    bool SourceOnlyTopMipCandidateReadyFor76ByteSerialization,
    bool XenonGfxSurfaceMaterialPointersWritten,
    bool XboxHardwareReady,
    IReadOnlyList<AztecPcXenonTopMipSample> Samples,
    string Note);
