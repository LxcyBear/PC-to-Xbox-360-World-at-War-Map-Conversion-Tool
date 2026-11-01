using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Source-only cross-corpus adjacency proof: GfxWorld header's candidate
/// MaterialMemory[39], the 39 decoded physical Material-shaped bodies, and
/// the SHA-pinned 32,713-vertex stream. Compares but NEVER equates virtual
/// GfxSurface.Material pointer slots with physical MaterialMemory rows or
/// individually named Material objects. NO Xbox roots or FF emission.
/// </summary>
public static class T4AztecPcMaterialWorldBoundaryBridge
{
    private const int WorldAt = 0x049122C0;
    private const int MemoryAt = 0x057316CB;
    private const int ShiftedMemoryAt = MemoryAt + 8;
    private const int FirstMaterialAt = 0x05731803;
    private const int VerticesAt = 0x057359F9;
    private const int SurfaceAt = 0x058E361E;
    private const int SurfaceCount = 735;
    private const int SurfaceStride = 52;
    private const int MaterialField = 20;
    private const int Count = 39;
    private const int MemoryStride = 8;
    private const int MaterialHeaderBytes = 112;
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string SurfaceSha =
        "919FF1CDB1EC5FCD57ED4020F1908AE813C1FB570C8631C15135E07C5DF852E5";
    private const string VertexSha =
        "6303269A8170F6F06233DCCE0BF1230EC6A659C4D0EE78CFDF7C14EE242D8041";

    public static async Task<AztecPcMaterialWorldBoundaryBridgeReport> ProbeAsync(
        string pcFastFile, string walkerJson, string memoryJson,
        string aliasJson, string geometryJson, string outputJson,
        CancellationToken ct = default)
    {
        using var wd = JsonDocument.Parse(await File.ReadAllTextAsync(walkerJson, ct));
        using var md = JsonDocument.Parse(await File.ReadAllTextAsync(memoryJson, ct));
        using var ad = JsonDocument.Parse(await File.ReadAllTextAsync(aliasJson, ct));
        using var gd = JsonDocument.Parse(await File.ReadAllTextAsync(geometryJson, ct));
        var w = wd.RootElement;
        var m = md.RootElement;
        var a = ad.RootElement;
        var g = gd.RootElement;

        if (w.GetProperty("Schema").GetString() != "WaW360.AztecPcMaterialStreamWalker.v1" ||
            w.GetProperty("Status").GetString() !=
                "AZTEC_PC_SOURCE_MATERIAL_WALK_39_STRUCTURAL_CANDIDATES" ||
            w.GetProperty("DecodedMaterialCandidates").GetInt32() != Count ||
            w.GetProperty("MaterialCap").GetInt32() != Count ||
            w.GetProperty("StartHeader").GetString() != Hex(FirstMaterialAt) ||
            w.GetProperty("StopReason").GetString() != "CAP_39_REACHED" ||
            w.GetProperty("NextCursor").GetString() != Hex(VerticesAt) ||
            w.GetProperty("InlineImages").GetInt32() != 69 ||
            w.GetProperty("TextureDescriptors").GetInt32() != 87 ||
            w.GetProperty("ConstantRecords").GetInt32() != 93 ||
            w.GetProperty("StateBitsRecords").GetInt32() != 192 ||
            w.GetProperty("GfxWorldMaterialMemoryOwnershipProven").GetBoolean() ||
            w.GetProperty("All39SourceMaterialNamesProven").GetBoolean() ||
            w.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            w.GetProperty("XboxHardwareReady").GetBoolean() ||
            m.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcGfxWorldMaterialMemorySourceProbe.v3" ||
            m.GetProperty("Status").GetString() !=
                "AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_TWO_OVERLAPPING_WINDOWS_40_RECORD_RUN" ||
            m.GetProperty("MaterialMemoryCount").GetInt32() != Count ||
            m.GetProperty("MaterialMemoryArrayBytes").GetInt32() != Count * MemoryStride ||
            m.GetProperty("Full39RecordSignatures").GetInt32() != 2 ||
            !m.GetProperty("TwoFullWindowsOverlapIntoFortyRecordRun").GetBoolean() ||
            m.GetProperty("ExactPhysicalOwnershipProven").GetBoolean() ||
            m.GetProperty("PCMaterialNamesResolved").GetBoolean() ||
            m.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            m.GetProperty("XboxHardwareReady").GetBoolean() ||
            a.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcGfxWorldMaterialAliasTrace.v1" ||
            a.GetProperty("Status").GetString() !=
                "AZTEC_PC_GFXWORLD_39_POINTER_8BYTE_ALIAS_LATTICE_PASS" ||
            a.GetProperty("DistinctPackedWorldMaterialPointers").GetInt32() != Count ||
            a.GetProperty("Surfaces").GetInt32() != SurfaceCount ||
            a.GetProperty("Triangles").GetInt32() != 19551 ||
            !a.GetProperty("SharedEightByteVirtualOffsetLattice").GetBoolean() ||
            a.GetProperty("VirtualSpanEightByteSlots").GetInt32() != Count ||
            a.GetProperty("UnreferencedSlotsWithinSpan").GetInt32() != 0 ||
            a.GetProperty("SourceMaterialNamesResolved").GetBoolean() ||
            a.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            a.GetProperty("XboxHardwareReady").GetBoolean() ||
            g.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcGfxWorldXenonGeometryComponents.v1" ||
            g.GetProperty("Status").GetString() !=
                "AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS" ||
            g.GetProperty("VertexCount").GetInt32() != 32713 ||
            g.GetProperty("SurfaceCount").GetInt32() != 735 ||
            g.GetProperty("IndexCount").GetInt32() != 58653 ||
            g.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Require 39/39 source structural walk, strict 39-window, alias lattice, geometry, and zero-binding safety gates.");

        var windows = m.GetProperty("FullWindowBoundaryEvidence").EnumerateArray().ToArray();
        if (windows.Length != 2 ||
            windows.Count(x => x.GetProperty("SourceOffset").GetInt32() == MemoryAt) != 1 ||
            windows.Count(x => x.GetProperty("SourceOffset").GetInt32() == ShiftedMemoryAt) != 1)
            throw new InvalidDataException("Original two overlapping 39-slot memory windows changed.");

        var materialNodes = w.GetProperty("Materials").EnumerateArray().ToArray();
        var aliases = a.GetProperty("Groups").EnumerateArray().ToArray();
        if (materialNodes.Length != Count || aliases.Length != Count)
            throw new InvalidDataException("39 physical material entries or 39 virtual pointer groups absent.");

        var zone = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var b = zone.Bytes;
        if (zone.Platform != WaWPlatform.PC ||
            zone.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Sha(b).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(SurfaceAt, SurfaceCount * SurfaceStride)).Equals(
                SurfaceSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(VerticesAt, 32713 * 44)).Equals(
                VertexSha, StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(w.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(m.GetProperty("OriginalPCFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(a.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(g.GetProperty("OriginalPCFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            U32(b, WorldAt + 0x18C) != Count ||
            U32(b, WorldAt + 0x190) != 0xFFFFFFFFu)
            throw new InvalidDataException("Original Aztec GfxWorld source identity or pinned geometry changed.");

        foreach (var window in windows)
        {
            var offset = window.GetProperty("SourceOffset").GetInt32();
            var sha = window.GetProperty("Source39WindowSha256").GetString();
            if (!Sha(b.AsSpan(offset, Count * MemoryStride)).Equals(
                sha, StringComparison.OrdinalIgnoreCase) ||
                window.GetProperty("FollowingOrInsertDirectiveFields").GetInt32() != Count ||
                window.GetProperty("FieldMatchesWithSourceGfxSurfaceMaterialPointers").GetInt32() != 0)
                throw new InvalidDataException("One of the two prior 39-entry source windows changed.");
        }

        var firstWindowEnd = MemoryAt + Count * MemoryStride;
        var shiftedWindowEnd = ShiftedMemoryAt + Count * MemoryStride;
        if (firstWindowEnd != FirstMaterialAt ||
            shiftedWindowEnd != FirstMaterialAt + MemoryStride)
            throw new InvalidDataException("39x8 array-to-first-Material candidate physical adjacency changed.");

        var aliasGroups = aliases.Select(x => new
        {
            PackedPointer = x.GetProperty("PcMaterialPointer").GetString()!,
            Virtual = ParseHex(x.GetProperty("SourceVirtualOffset").GetString()!),
            Surfaces = x.GetProperty("SurfaceCount").GetInt32(),
            Triangles = x.GetProperty("TriangleCount").GetInt32()
        }).OrderBy(x => x.Virtual).ToArray();
        var firstVirtual = aliasGroups[0].Virtual;
        for (var i = 0; i < Count; i++)
        {
            if ((long)aliasGroups[i].Virtual != (long)firstVirtual + i * 8L ||
                aliasGroups[i].Surfaces <= 0 || aliasGroups[i].Triangles < 0 ||
                !T4ZonePointer.IsWithinBlock(ParseHex(aliasGroups[i].PackedPointer),
                    zone.ZoneHeader))
                throw new InvalidDataException("Original GfxSurface packed 8-byte virtual lattice changed.");
        }
        var observedSurfaces = aliasGroups.ToDictionary(x => x.PackedPointer, _ => 0,
            StringComparer.OrdinalIgnoreCase);
        var observedTriangles = aliasGroups.ToDictionary(x => x.PackedPointer, _ => 0,
            StringComparer.OrdinalIgnoreCase);
        for (var s = 0; s < SurfaceCount; s++)
        {
            var pos = SurfaceAt + s * SurfaceStride;
            var key = Hex(U32(b, pos + MaterialField));
            if (!observedSurfaces.ContainsKey(key))
                throw new InvalidDataException($"GfxSurface #{s} references an unknown virtual alias {key}.");
            observedSurfaces[key]++;
            observedTriangles[key] += U16(b, pos + 10);
        }
        for (var i = 0; i < Count; i++)
        {
            var key = aliasGroups[i].PackedPointer;
            if (observedSurfaces[key] != aliasGroups[i].Surfaces ||
                observedTriangles[key] != aliasGroups[i].Triangles)
                throw new InvalidDataException("Surface counts/triangle totals drifted for a virtual pointer group.");
        }
        if (observedSurfaces.Values.Sum() != SurfaceCount ||
            observedTriangles.Values.Sum() != 19551)
            throw new InvalidDataException("The source 735-surface / 19,551-triangle pointer grouping changed.");

        var rows = new List<AztecPcMaterialWorldBoundaryRow>();
        var previous = FirstMaterialAt;
        for (var i = 0; i < Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var node = materialNodes[i];
            // Material header and next-cursor are PHYSICAL byte-array offsets.
            // Keep them as int; packed GfxSurface pointers remain uint elsewhere.
            var current = checked((int)ParseHex(node.GetProperty("HeaderAt").GetString()!));
            var next = checked((int)ParseHex(node.GetProperty("NextCursor").GetString()!));
            var name = node.GetProperty("CandidateName").GetString()!;
            var memAt = MemoryAt + i * MemoryStride;
            var directive = U32(b, memAt);
            var memoryValue = I32(b, memAt + 4);
            if (node.GetProperty("Ordinal").GetInt32() != i ||
                current != previous ||
                next <= current || next > VerticesAt ||
                current + MaterialHeaderBytes > next ||
                directive != 0xFFFFFFFFu ||
                !Sha(b.AsSpan(current, MaterialHeaderBytes)).Equals(
                    node.GetProperty("HeaderSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Material #{i} physical source concatenation/identity changed.");

            rows.Add(new AztecPcMaterialWorldBoundaryRow(
                i, Hex(memAt), Hex(directive), memoryValue,
                Hex(current), Hex(next), next - current, name,
                node.GetProperty("TextureCount").GetInt32(),
                node.GetProperty("ConstantCount").GetInt32(),
                node.GetProperty("StateBitsCount").GetInt32(),
                node.GetProperty("InlineImages").GetInt32(),
                node.GetProperty("HeaderSha256").GetString()!,
                aliasGroups[i].PackedPointer,
                Hex(aliasGroups[i].Virtual),
                aliasGroups[i].Surfaces, aliasGroups[i].Triangles,
                false));
            previous = next;
        }
        if (previous != VerticesAt)
            throw new InvalidDataException("The 39th Material source end did not meet the independently pinned vertex start.");
        if (rows.Sum(x => x.InlineImages) != 69 ||
            rows.Sum(x => x.TextureCount) != 87 ||
            rows.Sum(x => x.ConstantCount) != 93 ||
            rows.Sum(x => x.StateBitsCount) != 192)
            throw new InvalidDataException("The decoded Material records/inline children no longer match the 39-asset report.");

        var report = new AztecPcMaterialWorldBoundaryBridgeReport(
            "WaW360.AztecPcMaterialWorldBoundaryBridge.v1",
            "AZTEC_PC_39_MATERIALS_EXACT_MEMORY_TO_VERTICES_PHYSICAL_CHAIN",
            Path.GetFullPath(pcFastFile), zone.SourceSha256, ZoneSha,
            Hex(WorldAt), Count, SurfaceCount, 19551,
            Hex(MemoryAt), Hex(ShiftedMemoryAt),
            Count * MemoryStride, Hex(firstWindowEnd),
            Hex(shiftedWindowEnd), Hex(FirstMaterialAt),
            Hex(VerticesAt), previous == VerticesAt,
            rows.Count, aliasGroups.Length,
            rows.Sum(x => (long)x.MemoryValue),
            rows.Sum(x => x.MaterialPhysicalBytes),
            Sha(b.AsSpan(MemoryAt, Count * MemoryStride)),
            Sha(b.AsSpan(FirstMaterialAt, VerticesAt - FirstMaterialAt)),
            true, false, false, false, false,
            rows.ToArray(),
            "The selected 39x8 source MaterialMemory-shaped window ends EXACTLY " +
            "at the first of 39 contiguous 112B Material-shaped serialized " +
            "objects, whose final cursor meets the independently SHA-pinned " +
            "vertex buffer. The +8 shifted overlapping 39x8 window does NOT " +
            "end at any decoded Material header. Separately, all 735 " +
            "GfxSurface.Material pointers form the exact 39-slot, 8-byte " +
            "VIRTUAL alias lattice, with per-group counts verified against " +
            "source bytes. Each row contains an ORDINAL PAIRING HYPOTHESIS " +
            "only (zero rows have binding proven); the virtual offsets have " +
            "NOT been relocated to this physical stream. Thus physical " +
            "adjacency is strong but NOT a virtual-to-physical pointer " +
            "relocation, unique GfxWorld MaterialMemory ownership proof, " +
            "39 Xbox Material root matches, or hardware-ready FF. NO FF.");

        var dest = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllTextAsync(dest,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        return report;
    }

    private static uint ParseHex(string s)
    {
        if (!s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || s.Length != 10)
            throw new InvalidDataException("Expected canonical 8-digit packed hex pointer.");
        return Convert.ToUInt32(s.Substring(2), 16);
    }
    private static string Hex(int value) => "0x" + value.ToString("X8");
    private static string Hex(uint value) => "0x" + value.ToString("X8");
    private static uint U32(byte[] b, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset, 4));
    private static int I32(byte[] b, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset, 4));
    private static ushort U16(byte[] b, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset, 2));
    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}

public sealed record AztecPcMaterialWorldBoundaryRow(
    int Ordinal, string MemoryPhysicalOffset, string MemoryPointerMarker,
    int MemoryValue, string MaterialHeaderPhysicalOffset,
    string MaterialNextCursor, int MaterialPhysicalBytes,
    string UnverifiedMaterialName, int TextureCount, int ConstantCount,
    int StateBitsCount, int InlineImages, string MaterialHeaderSha256,
    string VirtualAliasPointerHypothesis, string VirtualAliasOffsetHypothesis,
    int AliasSurfaceCount, int AliasTriangleCount,
    bool VirtualAliasToMaterialBindingProven);

public sealed record AztecPcMaterialWorldBoundaryBridgeReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string SourceWorldHeaderAt, int MaterialMemoryEntries,
    int SourceSurfaceCount, int SourceTriangleCount,
    string PreferredMemoryWindowAt, string AlternativeShiftedMemoryWindowAt,
    int MemoryWindowBytes, string PreferredMemoryWindowEnd,
    string ShiftedMemoryWindowEnd, string FirstMaterialHeaderAt,
    string VertexBufferAt, bool ContiguousPhysicalChainValidated,
    int ConsecutiveMaterialCandidates, int VirtualAliasGroups,
    long SumMemoryValues, int SerializedMaterialStreamBytes,
    string SelectedMemoryWindowSha256, string SerializedMaterialStreamSha256,
    bool PreferredWindowDistinguishedByFirstHeaderAdjacency,
    bool WorldMaterialMemoryOwnershipProven,
    bool VirtualAliasToPhysicalMaterialRelocationProven,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady,
    IReadOnlyList<AztecPcMaterialWorldBoundaryRow> OrdinalPairingHypotheses,
    string Note);
