using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// SOURCE-ONLY GfxSurface MATERIAL CANDIDATE LEDGER.
/// Creates a 735-surface, 39-slot candidate table by a deliberately
/// UNPROVEN ordinal correspondence: GfxSurface packed VIRTUAL 8B slot i
/// -> 39 x 8B physical MaterialMemory row i -> 39 contiguous Material
/// source body i -> unique exact NAME in the pinned 8,509-root Xbox
/// Material catalog. This is NOT pointer relocation, an owned Xbox
/// binding, an OAT-verified dependency graph, or a deployable fastfile.
/// </summary>
public static class T4AztecPcWorldSurfaceMaterialCandidateLedger
{
    private const int SurfaceStart = 0x058E361E;
    private const int SurfaceStride = 52;
    private const int MaterialField = 20;
    private const int SurfaceCount = 735;
    private const int TriangleCount = 19551;
    private const int MaterialCount = 39;
    private const int MemoryStart = 0x057316CB;
    private const int MemoryStride = 8;
    private const int FirstMaterial = 0x05731803;
    private const int VertexStart = 0x057359F9;
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string SurfaceSha =
        "919FF1CDB1EC5FCD57ED4020F1908AE813C1FB570C8631C15135E07C5DF852E5";
    private const string MemorySha =
        "935806A3451003FA1E9AD25363032702358DCB50228A20F6E67B4ABA28FDC9D7";
    private const string MaterialStreamSha =
        "B20EC7CDA1790158ABEE40929F9ABFA14DD6C75CAFF7BB8A4F3FD032A7DDB1E7";

    public static async Task<AztecPcSurfaceMaterialCandidateLedgerReport> ProbeAsync(
        string pcFastFile, string boundaryBridgeJson, string nativeNameJson,
        string outputJson, CancellationToken ct = default)
    {
        using var bd = JsonDocument.Parse(
            await File.ReadAllTextAsync(boundaryBridgeJson, ct));
        using var nd = JsonDocument.Parse(
            await File.ReadAllTextAsync(nativeNameJson, ct));
        var b = bd.RootElement;
        var n = nd.RootElement;

        if (b.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcMaterialWorldBoundaryBridge.v1" ||
            b.GetProperty("Status").GetString() !=
                "AZTEC_PC_39_MATERIALS_EXACT_MEMORY_TO_VERTICES_PHYSICAL_CHAIN" ||
            !b.GetProperty("ContiguousPhysicalChainValidated").GetBoolean() ||
            !b.GetProperty("PreferredWindowDistinguishedByFirstHeaderAdjacency").GetBoolean() ||
            b.GetProperty("PreferredMemoryWindowAt").GetString() != Hex(MemoryStart) ||
            b.GetProperty("FirstMaterialHeaderAt").GetString() != Hex(FirstMaterial) ||
            b.GetProperty("VertexBufferAt").GetString() != Hex(VertexStart) ||
            b.GetProperty("MaterialMemoryEntries").GetInt32() != MaterialCount ||
            b.GetProperty("ConsecutiveMaterialCandidates").GetInt32() != MaterialCount ||
            b.GetProperty("VirtualAliasGroups").GetInt32() != MaterialCount ||
            b.GetProperty("SourceSurfaceCount").GetInt32() != SurfaceCount ||
            b.GetProperty("SourceTriangleCount").GetInt32() != TriangleCount ||
            b.GetProperty("SelectedMemoryWindowSha256").GetString() != MemorySha ||
            b.GetProperty("SerializedMaterialStreamSha256").GetString() != MaterialStreamSha ||
            b.GetProperty("WorldMaterialMemoryOwnershipProven").GetBoolean() ||
            b.GetProperty("VirtualAliasToPhysicalMaterialRelocationProven").GetBoolean() ||
            b.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            b.GetProperty("XboxHardwareReady").GetBoolean() ||
            n.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcWorldMaterialNativeNameReconciliation.v1" ||
            n.GetProperty("Status").GetString() !=
                "AZTEC_PC_39_MATERIAL_NAMES_VS_GOLDEN_NATIVE_ROOTS_INVENTORIED" ||
            n.GetProperty("GoldenNativeRoots").GetInt32() != 8509 ||
            n.GetProperty("SourceMaterialCandidates").GetInt32() != MaterialCount ||
            n.GetProperty("UniqueExactNameCandidates").GetInt32() != MaterialCount ||
            n.GetProperty("NoExactNameCandidates").GetInt32() != 0 ||
            n.GetProperty("AmbiguousExactNameCandidates").GetInt32() != 0 ||
            n.GetProperty("MaterialMemoryOwnershipProven").GetBoolean() ||
            n.GetProperty("SourceVirtualToPhysicalMaterialRelocationProven").GetBoolean() ||
            n.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            n.GetProperty("All735SurfaceMaterialRefsBound").GetBoolean() ||
            n.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Require exact 39 physical Material chain, all 39 unique name matches, and zero binding/FF claims.");

        var physicalRows = b.GetProperty("OrdinalPairingHypotheses")
            .EnumerateArray().ToArray();
        var nameRows = n.GetProperty("Rows").EnumerateArray().ToArray();
        if (physicalRows.Length != MaterialCount || nameRows.Length != MaterialCount)
            throw new InvalidDataException("39 source material or native-name rows missing.");

        var zone = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var source = zone.Bytes;
        if (zone.Platform != WaWPlatform.PC ||
            zone.Document.Endianness != Endian.Little ||
            source.Length != 113701260 ||
            !Sha(source).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(source.AsSpan(SurfaceStart, SurfaceCount * SurfaceStride))
                .Equals(SurfaceSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(source.AsSpan(MemoryStart, MaterialCount * MemoryStride))
                .Equals(MemorySha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(source.AsSpan(FirstMaterial, VertexStart - FirstMaterial))
                .Equals(MaterialStreamSha, StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(b.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pinned original-PC Aztec compressed/source-zone, world material stream or 735 surfaces changed.");

        var proposedGroups = new List<AztecPcSurfaceMaterialCandidateGroup>(MaterialCount);
        var indexByPointer = new Dictionary<uint, int>();
        var uniqueRootIndices = new HashSet<int>();
        var uniqueSourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseVirtualOffset = 0L;
        for (var i = 0; i < MaterialCount; i++)
        {
            var row = physicalRows[i];
            var names = nameRows[i];
            var pointer = ParseHex(row.GetProperty("VirtualAliasPointerHypothesis").GetString()!);
            var decoded = T4ZonePointer.Decode(pointer);
            var nextPhysical = ParseHex(row.GetProperty("MaterialNextCursor").GetString()!);
            var physical = ParseHex(row.GetProperty("MaterialHeaderPhysicalOffset").GetString()!);
            var materialName = row.GetProperty("UnverifiedMaterialName").GetString() ?? "";
            var exactMatches = names.GetProperty("NativeNameMatches").EnumerateArray().ToArray();
            if (i == 0)
                baseVirtualOffset = decoded.Offset;
            if (row.GetProperty("Ordinal").GetInt32() != i ||
                names.GetProperty("Ordinal").GetInt32() != i ||
                names.GetProperty("ExactNativeNameMatches").GetInt32() != 1 ||
                names.GetProperty("MatchKind").GetString() != "UNIQUE_EXACT_NAME_ONLY" ||
                exactMatches.Length != 1 ||
                !string.Equals(materialName,
                    names.GetProperty("UnverifiedPcMaterialName").GetString(),
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(materialName,
                    exactMatches[0].GetProperty("Name").GetString(),
                    StringComparison.OrdinalIgnoreCase) ||
                !uniqueSourceNames.Add(materialName) ||
                row.GetProperty("VirtualAliasToMaterialBindingProven").GetBoolean() ||
                names.GetProperty("PcSourceMaterialOwnershipProven").GetBoolean() ||
                names.GetProperty("VirtualAliasToPcMaterialProven").GetBoolean() ||
                names.GetProperty("XboxMaterialRootBindingProven").GetBoolean() ||
                physical != (i == 0 ? FirstMaterial : proposedGroups[i - 1].MaterialNextOffsetAsInt) ||
                (long)nextPhysical <= physical || nextPhysical > VertexStart ||
                decoded.Kind != T4ZonePointerKind.Offset ||
                decoded.Block != T4ZoneBlock.Virtual ||
                decoded.Offset != baseVirtualOffset + i * MemoryStride ||
                !indexByPointer.TryAdd(pointer, i) ||
                U32(source, MemoryStart + i * MemoryStride) != T4ZonePointer.Following)
                throw new InvalidDataException($"Material/source-pointer ordinal witness {i} changed.");

            var rootIndex = exactMatches[0].GetProperty("NativeRootIndex").GetInt32();
            if (rootIndex < 0 || rootIndex >= 8509 || !uniqueRootIndices.Add(rootIndex))
                throw new InvalidDataException("Non-unique or out-of-range exact native material name matching.");
            proposedGroups.Add(new AztecPcSurfaceMaterialCandidateGroup(
                i, Hex(pointer), Hex(decoded.Offset),
                Hex(MemoryStart + i * MemoryStride),
                I32(source, MemoryStart + i * MemoryStride + 4),
                Hex(physical), Hex(nextPhysical),
                materialName, rootIndex,
                row.GetProperty("AliasSurfaceCount").GetInt32(),
                row.GetProperty("AliasTriangleCount").GetInt32(),
                0, 0, false, false));
        }
        if (proposedGroups[^1].MaterialNextOffsetAsInt != VertexStart ||
            proposedGroups.Sum(x => x.ReportedSourceSurfaceCount) != SurfaceCount ||
            proposedGroups.Sum(x => x.ReportedSourceTriangleCount) != TriangleCount)
            throw new InvalidDataException("Material source physical chain or prior source-alias census no longer matches.");

        var observations = new List<AztecPcSurfaceMaterialCandidateObservation>(SurfaceCount);
        var observedSurfaceCounts = new int[MaterialCount];
        var observedTriangleCounts = new int[MaterialCount];
        for (var i = 0; i < SurfaceCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var at = SurfaceStart + i * SurfaceStride;
            var raw = U32(source, at + MaterialField);
            var tris = U16(source, at + 10);
            if (!indexByPointer.TryGetValue(raw, out var ordinal) || tris <= 0)
                throw new InvalidDataException($"Original source GfxSurface#{i} Material pointer or triangles changed.");
            observedSurfaceCounts[ordinal]++;
            observedTriangleCounts[ordinal] += tris;
            var group = proposedGroups[ordinal];
            observations.Add(new AztecPcSurfaceMaterialCandidateObservation(
                i, Hex(at), tris,
                Hex(raw), ordinal, group.SourceMaterialNameCandidate,
                group.UniqueExactNativeMaterialNameRootIndex,
                false, false,
                "HYPOTHESIS: virtual 8-byte ordinal slot -> corresponding " +
                "physical MaterialMemory row -> following Material -> unique " +
                "native material root name. No independent virtual relocation " +
                "proves this surface's actual Material identity."));
        }
        if (observations.Count != SurfaceCount ||
            observedTriangleCounts.Sum() != TriangleCount)
            throw new InvalidDataException("Source material-surface coverage changed.");

        for (var i = 0; i < MaterialCount; i++)
        {
            var g = proposedGroups[i];
            if (observedSurfaceCounts[i] != g.ReportedSourceSurfaceCount ||
                observedTriangleCounts[i] != g.ReportedSourceTriangleCount ||
                observedSurfaceCounts[i] == 0)
                throw new InvalidDataException($"39-slot alias/735-surface per-group counts changed for material candidate {i}.");
            proposedGroups[i] = g with
            {
                RecountedSourceSurfaceCount = observedSurfaceCounts[i],
                RecountedSourceTriangleCount = observedTriangleCounts[i]
            };
        }

        var result = new AztecPcSurfaceMaterialCandidateLedgerReport(
            "WaW360.AztecPcWorldSurfaceMaterialCandidateLedger.v1",
            "AZTEC_PC_735_SURFACE_39_NAME_ORDINAL_CANDIDATE_LEDGER",
            Path.GetFullPath(pcFastFile), zone.SourceSha256, ZoneSha,
            Path.GetFullPath(boundaryBridgeJson), Path.GetFullPath(nativeNameJson),
            MaterialCount, SurfaceCount, TriangleCount, 8509,
            Hex(baseVirtualOffset), Hex(MemoryStart), Hex(FirstMaterial),
            Hex(VertexStart), MemorySha, MaterialStreamSha, SurfaceSha,
            39, 39, SurfaceCount, TriangleCount,
            proposedGroups.ToArray(), observations.ToArray(),
            false, false, false, false, false,
            "SOURCE-ONLY ordinal candidate ledger. The 39 packed source " +
            "GfxSurface.Material values form an 8-byte VIRTUAL lattice. " +
            "MaterialMemory is a (Material*, int) 8-byte struct, but a " +
            "GfxSurface.Material is typed Material* and virtual packed " +
            "offsets cannot be equated with physical decompressed-zone " +
            "offsets. We have NOT proved those 39 pointers target the " +
            "39 candidate memory rows, nor that a row's FOLLOWING material " +
            "pointer is the object its GfxSurface value means. All 39 source " +
            "names have unique exact root-name matches; the same name " +
            "does not establish an XDK-compatible material dependency " +
            "graph. All 735 labels here are proposals only, not binding. " +
            "No material pointer written, no hardware FF, no Gate 7 PASS.");
        var path = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(result, new JsonSerializerOptions
            { WriteIndented = true }), ct);
        return result;
    }

    private static uint ParseHex(string value)
    {
        if (value.Length != 10 || !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Expected canonical eight-digit hex source pointer.");
        return Convert.ToUInt32(value[2..], 16);
    }
    private static uint U32(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
    private static int I32(byte[] b, int at) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at, 4));
    private static int U16(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2));
    private static string Hex(int x) => "0x" + x.ToString("X8");
    private static string Hex(uint x) => "0x" + x.ToString("X8");
    private static string Hex(long x) => "0x" + x.ToString("X8");
    private static string Sha(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(SHA256.HashData(b));
}

public sealed record AztecPcSurfaceMaterialCandidateGroup(
    int Ordinal, string SourceVirtualAliasPointer, string SourceVirtualOffset,
    string PhysicalMemoryRowAt, int MemorySizeField,
    string MaterialHeaderAt, string MaterialNextAt,
    string SourceMaterialNameCandidate,
    int UniqueExactNativeMaterialNameRootIndex,
    int ReportedSourceSurfaceCount, int ReportedSourceTriangleCount,
    int RecountedSourceSurfaceCount, int RecountedSourceTriangleCount,
    bool SourceVirtualToPhysicalBindingProven,
    bool XboxMaterialBindingProven)
{
    public int MaterialNextOffsetAsInt => checked((int)
        Convert.ToUInt32(MaterialNextAt[2..], 16));
}

public sealed record AztecPcSurfaceMaterialCandidateObservation(
    int SurfaceIndex, string SourceSurfaceAt, int Triangles,
    string SourcePackedMaterialPointer, int OrdinalSlotHypothesis,
    string ProposedPcMaterialName, int ProposedUniqueXboxNativeRootIndex,
    bool SourcePointerToMaterialProven, bool XboxMaterialBindingProven,
    string Note);

public sealed record AztecPcSurfaceMaterialCandidateLedgerReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string PhysicalBoundaryBridgeReport, string NativeNameAuditReport,
    int SourceMaterialNameCandidates, int SourceSurfaces, int SourceTriangles,
    int GoldenNativeRootInventorySize,
    string FirstVirtualAliasOffset, string PhysicalMemoryArrayStart,
    string FirstPhysicalMaterial, string PhysicalMaterialStreamEnd,
    string SelectedMemorySha256, string MaterialStreamSha256,
    string SurfaceArraySha256,
    int UniqueExactNativeNameCandidates, int DistinctVirtualAliasSlots,
    int HypotheticalSurfacesLabeled, int HypotheticalTrianglesLabeled,
    IReadOnlyList<AztecPcSurfaceMaterialCandidateGroup> CandidateGroups,
    IReadOnlyList<AztecPcSurfaceMaterialCandidateObservation> SurfaceCandidateRows,
    bool PhysicalMemoryArrayOwnershipProven,
    bool VirtualAliasToMaterialNameProven,
    bool XboxMaterialBindingsProven, bool All735XboxSurfacesBound,
    bool XboxHardwareReady, string Note);
