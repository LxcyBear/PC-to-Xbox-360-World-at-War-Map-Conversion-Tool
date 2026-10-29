using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Joins independent OAT-loaded PC typed Material* equality for ALL 735
/// GfxSurface records with unique golden native Xbox material ROOT NAMES
/// and the EXACT null fields in the 52B big-endian PC source-layout buffer
/// (NOT native 76B Xenon GfxSurface). Creates a source identity INTENT,
/// NOT an Xbox ABI pointer patch.
/// An XAsset root index is not a native address or valid packed zone pointer.
/// </summary>
public static class T4AztecPcVerifiedSurfaceMaterialRelocationPlan
{
    private const int SurfaceCount = 735;
    private const int MaterialCount = 39;
    private const int SurfaceStride = 52;
    private const int MaterialFieldOffset = 20;
    private const int SourceSurfaceAt = 0x058E361E;
    private const string SourceZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string PcSurfaceSha =
        "919FF1CDB1EC5FCD57ED4020F1908AE813C1FB570C8631C15135E07C5DF852E5";

    public static async Task<AztecPcVerifiedSurfaceMaterialRelocationPlanReport> BuildAsync(
        string witnessPath, string candidateLedgerPath, string nameAuditPath,
        string geometryPath, string outputPath, CancellationToken ct = default)
    {
        using var od = JsonDocument.Parse(await File.ReadAllTextAsync(witnessPath, ct));
        using var ld = JsonDocument.Parse(await File.ReadAllTextAsync(candidateLedgerPath, ct));
        using var nd = JsonDocument.Parse(await File.ReadAllTextAsync(nameAuditPath, ct));
        using var gd = JsonDocument.Parse(await File.ReadAllTextAsync(geometryPath, ct));
        var o = od.RootElement;
        var l = ld.RootElement;
        var n = nd.RootElement;
        var g = gd.RootElement;
        if (o.GetProperty("schema").GetString() !=
                "WaW360.AztecPcOatLoadedGfxWorldMaterialWitnessReconciliation.v1" ||
            o.GetProperty("status").GetString() !=
                "AZTEC_PC_OAT_LOADER_735_TYPED_MATERIAL_POINTER_IDENTITIES_MATCH_ORDINAL_CANDIDATES" ||
            o.GetProperty("loadedSurfaceCount").GetInt32() != SurfaceCount ||
            o.GetProperty("loadedMaterialMemoryCount").GetInt32() != MaterialCount ||
            o.GetProperty("loadedTriangles").GetInt32() != 19551 ||
            o.GetProperty("memoryRowsWithExactSourceCandidateName").GetInt32() != MaterialCount ||
            o.GetProperty("loadedDirectPointerMatches").GetInt32() != SurfaceCount ||
            o.GetProperty("independentlyVerifiedOrdinalAndNameSurfaces").GetInt32() != SurfaceCount ||
            o.GetProperty("directlyMatchedButDifferentOrdinal").GetInt32() != 0 ||
            o.GetProperty("unresolvedOrAmbiguous").GetInt32() != 0 ||
            o.GetProperty("loadedSurfacePointerEqualsMemoryStructAddress").GetInt32() != 0 ||
            o.GetProperty("xboxRootBindingsProven").GetInt32() != 0 ||
            o.GetProperty("xboxHardwareReady").GetBoolean() ||
            l.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcWorldSurfaceMaterialCandidateLedger.v1" ||
            l.GetProperty("Status").GetString() !=
                "AZTEC_PC_735_SURFACE_39_NAME_ORDINAL_CANDIDATE_LEDGER" ||
            l.GetProperty("SourceSurfaces").GetInt32() != SurfaceCount ||
            l.GetProperty("SourceTriangles").GetInt32() != 19551 ||
            l.GetProperty("UniqueExactNativeNameCandidates").GetInt32() != MaterialCount ||
            l.GetProperty("DistinctVirtualAliasSlots").GetInt32() != MaterialCount ||
            l.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            l.GetProperty("XboxHardwareReady").GetBoolean() ||
            n.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcWorldMaterialNativeNameReconciliation.v1" ||
            n.GetProperty("Status").GetString() !=
                "AZTEC_PC_39_MATERIAL_NAMES_VS_GOLDEN_NATIVE_ROOTS_INVENTORIED" ||
            n.GetProperty("UniqueExactNameCandidates").GetInt32() != MaterialCount ||
            n.GetProperty("NoExactNameCandidates").GetInt32() != 0 ||
            n.GetProperty("AmbiguousExactNameCandidates").GetInt32() != 0 ||
            n.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            n.GetProperty("XboxHardwareReady").GetBoolean() ||
            g.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcGfxWorldXenonGeometryComponents.v1" ||
            g.GetProperty("Status").GetString() !=
                "AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS" ||
            g.GetProperty("SurfaceCount").GetInt32() != SurfaceCount ||
            g.GetProperty("DistinctPcMaterialPointers").GetInt32() != MaterialCount ||
            g.GetProperty("ZeroMaterialPointerPlaceholders").GetInt32() != SurfaceCount ||
            g.GetProperty("XenonGfxWorldRootEmitted").GetBoolean() ||
            g.GetProperty("OatGfxWorldBodyLoaded").GetBoolean() ||
            g.GetProperty("XboxHardwareReady").GetBoolean() ||
            !string.Equals(g.GetProperty("OriginalPCZoneSha256").GetString(),
                SourceZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(g.GetProperty("OriginalPCSurfacesSha256").GetString(),
                PcSurfaceSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Require 735 exact typed OAT PC pointer matches, 39 unique golden Material root names, and original null Xenon GfxSurface buffer.");

        var observed = o.GetProperty("perSurface").EnumerateArray().ToArray();
        var hypothesis = l.GetProperty("SurfaceCandidateRows").EnumerateArray().ToArray();
        var groups = l.GetProperty("CandidateGroups").EnumerateArray().ToArray();
        var rootNames = n.GetProperty("Rows").EnumerateArray().ToArray();
        if (observed.Length != SurfaceCount || hypothesis.Length != SurfaceCount ||
            groups.Length != MaterialCount || rootNames.Length != MaterialCount)
            throw new InvalidDataException("735 OAT observations or 39 unique material roots missing.");

        var rawWitnessPath = o.GetProperty("oatGraphFile").GetString()!;
        if (!File.Exists(rawWitnessPath))
            throw new FileNotFoundException(
                "Original post-loader typed pointer witness is missing; do not trust only the summary.", rawWitnessPath);
        using var rd = JsonDocument.Parse(await File.ReadAllTextAsync(rawWitnessPath, ct));
        var raw = rd.RootElement;
        if (raw.GetProperty("schema").GetString() !=
                "WaW360.T4PcOatGfxWorldMaterialPointerWitness.v1" ||
            raw.GetProperty("status").GetString() !=
                "OAT_PC_LOADED_GFXWORLD_POINTER_WITNESS_RECORDED" ||
            raw.GetProperty("materialMemoryCount").GetInt32() != MaterialCount ||
            raw.GetProperty("surfaceCount").GetInt32() != SurfaceCount ||
            raw.GetProperty("directUniqueMaterialPointerMatches").GetInt32() != SurfaceCount ||
            raw.GetProperty("unmatchedSurfaces").GetInt32() != 0 ||
            raw.GetProperty("ambiguousSurfaces").GetInt32() != 0 ||
            raw.GetProperty("totalTriangles").GetInt32() != 19551 ||
            raw.GetProperty("memoryRecordAddressMatches").GetInt32() != 0)
            throw new InvalidDataException("Independent PC OAT raw loaded-pointer witness changed.");

        var rawMem = raw.GetProperty("memoryRows").EnumerateArray().ToArray();
        var rawSurf = raw.GetProperty("surfaces").EnumerateArray().ToArray();
        if (rawMem.Length != MaterialCount || rawSurf.Length != SurfaceCount)
            throw new InvalidDataException("Raw OAT graph has different GfxWorld material/surface arrays.");

        var pc = g.GetProperty("OriginalPCFastFile").GetString()!;
        if (!Path.GetFullPath(pc).Equals(
                Path.GetFullPath(o.GetProperty("originalPcFastFile").GetString()!),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(g.GetProperty("OriginalPCFastFileSha256").GetString(),
                o.GetProperty("sourceSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Independent OAT source and geometry compressed map identities differ.");
        var source = await FastFileAnalysisCache.GetZoneAsync(pc, ct);
        var b = source.Bytes;
        if (source.Platform != WaWPlatform.PC || b.Length != 113701260 ||
            !string.Equals(source.SourceSha256,
                g.GetProperty("OriginalPCFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Sha(b), SourceZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Sha(b.AsSpan(SourceSurfaceAt, SurfaceCount * SurfaceStride)),
                PcSurfaceSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC zone changed since OAT pointer-witness validation.");

        var bePath = g.GetProperty("XenonSurfacesFile").GetString()!;
        var surfaceBytes = await File.ReadAllBytesAsync(bePath, ct);
        if (surfaceBytes.Length != SurfaceCount * SurfaceStride ||
            !string.Equals(Sha(surfaceBytes), g.GetProperty("XenonSurfacesSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original three-buffer endian-only Xenon surface file/SHA changed.");

        var legacyRelocPath = g.GetProperty("MaterialRelocationLedger").GetString()!;
        using var legacyDoc = JsonDocument.Parse(await File.ReadAllTextAsync(legacyRelocPath, ct));
        var legacy = legacyDoc.RootElement.EnumerateArray().ToArray();
        if (legacy.Length != SurfaceCount)
            throw new InvalidDataException("Original 735-slot zero-bound geometry ledger changed.");

        var distinctRoots = new HashSet<int>();
        for (var i = 0; i < MaterialCount; i++)
        {
            var group = groups[i];
            var name = rootNames[i];
            var memory = rawMem[i];
            var matches = name.GetProperty("NativeNameMatches").EnumerateArray().ToArray();
            var candidateName = group.GetProperty("SourceMaterialNameCandidate").GetString();
            var rootIndex = group.GetProperty("UniqueExactNativeMaterialNameRootIndex").GetInt32();
            if (group.GetProperty("Ordinal").GetInt32() != i ||
                name.GetProperty("Ordinal").GetInt32() != i ||
                matches.Length != 1 ||
                name.GetProperty("ExactNativeNameMatches").GetInt32() != 1 ||
                matches[0].GetProperty("NativeRootIndex").GetInt32() != rootIndex ||
                !string.Equals(matches[0].GetProperty("Name").GetString(),
                    candidateName, StringComparison.Ordinal) ||
                !string.Equals(memory.GetProperty("materialName").GetString(),
                    candidateName, StringComparison.Ordinal) ||
                !memory.GetProperty("materialPointerPresent").GetBoolean() ||
                memory.GetProperty("ordinal").GetInt32() != i ||
                !distinctRoots.Add(rootIndex))
                throw new InvalidDataException($"Material root/name/OAT witness mismatch at slot {i}.");
        }
        if (distinctRoots.Count != MaterialCount)
            throw new InvalidDataException("Golden material root names are not unique.");

        var worklist = new List<AztecPcVerifiedSurfaceMaterialFixup>(SurfaceCount);
        var groupedSurfaces = new int[MaterialCount];
        var groupedTriangles = new int[MaterialCount];
        var totalTriangles = 0;
        for (var i = 0; i < SurfaceCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var oRow = observed[i];
            var rawRow = rawSurf[i];
            var candidate = hypothesis[i];
            var ordinal = candidate.GetProperty("OrdinalSlotHypothesis").GetInt32();
            if (ordinal < 0 || ordinal >= MaterialCount)
                throw new InvalidDataException($"Material ordinal out of range at surface {i}.");
            var group = groups[ordinal];
            var name = group.GetProperty("SourceMaterialNameCandidate").GetString()!;
            var rootIndex = group.GetProperty("UniqueExactNativeMaterialNameRootIndex").GetInt32();
            var materialPointer = BinaryPrimitives.ReadUInt32LittleEndian(
                b.AsSpan(SourceSurfaceAt + i * SurfaceStride + MaterialFieldOffset, 4));
            var beFieldOffset = i * SurfaceStride + MaterialFieldOffset;
            var littleTris = BinaryPrimitives.ReadUInt16LittleEndian(
                b.AsSpan(SourceSurfaceAt + i * SurfaceStride + 10, 2));
            var beTris = BinaryPrimitives.ReadUInt16BigEndian(
                surfaceBytes.AsSpan(i * SurfaceStride + 10, 2));
            var field = BinaryPrimitives.ReadUInt32BigEndian(
                surfaceBytes.AsSpan(beFieldOffset, 4));
            if (oRow.GetProperty("surfaceIndex").GetInt32() != i ||
                rawRow.GetProperty("index").GetInt32() != i ||
                candidate.GetProperty("SurfaceIndex").GetInt32() != i ||
                legacy[i].GetProperty("SurfaceIndex").GetInt32() != i ||
                oRow.GetProperty("loadedMemoryPointerMatches").GetInt32() != 1 ||
                rawRow.GetProperty("directMatchingMemoryRows").GetInt32() != 1 ||
                rawRow.GetProperty("directMaterialRow").GetInt32() != ordinal ||
                oRow.GetProperty("loadedDirectPointerMemoryRow").GetInt32() != ordinal ||
                !oRow.GetProperty("sameOrdinalAndName").GetBoolean() ||
                !string.Equals(oRow.GetProperty("loadedMaterialName").GetString(), name,
                    StringComparison.Ordinal) ||
                !string.Equals(rawRow.GetProperty("materialNameOnDirectMatch").GetString(), name,
                    StringComparison.Ordinal) ||
                !string.Equals(candidate.GetProperty("ProposedPcMaterialName").GetString(), name,
                    StringComparison.Ordinal) ||
                candidate.GetProperty("ProposedUniqueXboxNativeRootIndex").GetInt32() != rootIndex ||
                oRow.GetProperty("xboxRootBindingProven").GetBoolean() ||
                rawRow.GetProperty("memoryStructAddressMatchingRows").GetInt32() != 0 ||
                littleTris != beTris ||
                littleTris != oRow.GetProperty("triCount").GetInt32() ||
                littleTris != rawRow.GetProperty("triangleCount").GetInt32() ||
                materialPointer != ParseHex(candidate.GetProperty("SourcePackedMaterialPointer").GetString()!) ||
                materialPointer != ParseHex(oRow.GetProperty("sourcePackedPointer").GetString()!) ||
                materialPointer != ParseHex(legacy[i].GetProperty("OriginalPcPackedPointer").GetString()!) ||
                legacy[i].GetProperty("BoundToXboxMaterialRoot").GetBoolean() ||
                field != 0u)
                throw new InvalidDataException($"Source/OAT/native/BE zero-relocation mismatch at surface {i}.");

            totalTriangles += littleTris;
            groupedSurfaces[ordinal]++;
            groupedTriangles[ordinal] += littleTris;
            worklist.Add(new AztecPcVerifiedSurfaceMaterialFixup(
                i, beFieldOffset, 4, "BIG_ENDIAN_U32_ZERO_PLACEHOLDER",
                $"0x{materialPointer:X8}", ordinal, name, rootIndex,
                littleTris, true, true, false, false));
        }
        if (totalTriangles != 19551 ||
            worklist.Count != SurfaceCount ||
            groupedSurfaces.Count(x => x > 0) != MaterialCount)
            throw new InvalidDataException("PC source material coverage or triangle count changed.");
        for (var i = 0; i < MaterialCount; i++)
        {
            if (groupedSurfaces[i] != groups[i].GetProperty("RecountedSourceSurfaceCount").GetInt32() ||
                groupedTriangles[i] != groups[i].GetProperty("RecountedSourceTriangleCount").GetInt32() ||
                groupedSurfaces[i] != rawMem[i].GetProperty("directSurfaceMatches").GetInt32() ||
                groupedTriangles[i] != rawMem[i].GetProperty("directTriangleMatches").GetInt32())
                throw new InvalidDataException($"OAT/material/root/geometry surface group {i} totals disagree.");
        }

        var result = new AztecPcVerifiedSurfaceMaterialRelocationPlanReport(
            "WaW360.AztecPcVerifiedSurfaceMaterialRelocationPlan.v1",
            "AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY",
            Path.GetFullPath(pc), source.SourceSha256, SourceZoneSha,
            Path.GetFullPath(rawWitnessPath), Path.GetFullPath(witnessPath),
            Path.GetFullPath(candidateLedgerPath), Path.GetFullPath(nameAuditPath),
            Path.GetFullPath(geometryPath), Path.GetFullPath(bePath),
            Sha(surfaceBytes), MaterialCount, SurfaceCount, totalTriangles,
            distinctRoots.Count, SurfaceCount, SurfaceCount, 0, 0,
            true, true, false, false, false,
            worklist.ToArray(),
            "Each of 735 OAT-loaded original-PC GfxSurface.Material typed pointers " +
            "uniquely equals the material pointer in its expected MaterialMemory row, " +
            "and the loaded name matches a unique golden Xbox Material root NAME/INDEX. " +
            "The endian-only 52-byte PC-layout diagnostic buffer remains SHA-identical " +
            "with 735 NULL Material fields; this records exact 4-byte relocation " +
            "SITES in PC layout (+20), NOT native Xbox 76B layout (+44). The Xenon " +
            "serializer must derive six additional topMip floats per surface; only " +
            "root IDENTITIES are known. An XAsset root index is NOT a " +
            "valid Xenon pointer. NO pointer encoded, NO FF produced, NO runtime " +
            "Material graph/DPVS closure confirmed, NO Gate 7 or hardware approval.");
        var dest = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllTextAsync(dest,
            JsonSerializer.Serialize(result,
                new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }
    private static uint ParseHex(string s)
    {
        if (s.Length != 10 || !s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Non-canonical packed PC material pointer.");
        return Convert.ToUInt32(s.Substring(2), 16);
    }
    private static string Sha(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data));
}

public sealed record AztecPcVerifiedSurfaceMaterialFixup(
    int SurfaceIndex, int XenonSurfaceBufferByteOffset, int FieldBytes,
    string ExpectedOriginalXenonField, string OriginalPcPackedMaterialPointer,
    int LoadedPcMaterialMemoryOrdinal, string VerifiedPcMaterialName,
    int UniqueMatchingXboxNativeRootIndex, int TriangleCount,
    bool PcLoadedMaterialPointerIdentityProven,
    bool XboxMaterialRootNameIdentityUnique,
    bool XenonMaterialPointerWritten,
    bool XboxRuntimeMaterialGraphBindingProven);

public sealed record AztecPcVerifiedSurfaceMaterialRelocationPlanReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string RawOatLoadedWorldWitness, string VerifiedOatReconciliation,
    string PriorSourceCandidateLedger, string GoldenNameIdentityAudit,
    string XenonGeometryManifest, string UnmodifiedXenonSurfaceBuffer,
    string XenonSurfaceBufferSha256, int VerifiedPcMaterialMemoryEntries,
    int VerifiedPcSurfaceReferences, int OriginalPcTriangles,
    int UniqueGoldenXboxNativeMaterialRootNames,
    int ExactRelocationSites, int VerifiedTypedPcSurfaceIdentities,
    int XenonSurfaceMaterialPointersWritten,
    int XboxRuntimeSurfaceMaterialBindings,
    bool AllPcSourceMaterialIdentitiesVerified,
    bool AllXboxRootNamesIdentified,
    bool XenonGfxWorldEmitted, bool OatXenonGfxWorldLoaded,
    bool XboxHardwareReady,
    IReadOnlyList<AztecPcVerifiedSurfaceMaterialFixup> RelocationIntents,
    string Note);
