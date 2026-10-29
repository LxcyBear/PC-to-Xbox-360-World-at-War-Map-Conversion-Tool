using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Finds plausible serialized 39 x 8-byte T4 MaterialMemory arrays near
/// SHA-proven original-PC Aztec GfxWorld vertices, using real loader order.
/// A structural hit is NOT proof that GfxSurface.Material points to the
/// records; their typed fields differ. No fabricated material binding.
/// </summary>
public static class T4AztecPcGfxWorldMaterialMemorySourceProbe
{
    private const int HeaderOffset = 0x049122C0;
    private const int VertexOffset = 0x057359F9;
    private const int SurfaceOffset = 0x058E361E;
    private const int ArrayCount = 39;
    private const int EntrySize = 8;
    private const int ArrayBytes = ArrayCount * EntrySize;
    private const int MaxBeforeVertices = 8 * 1024 * 1024;
    private const string ZoneSha = "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string VertexSha = "6303269A8170F6F06233DCCE0BF1230EC6A659C4D0EE78CFDF7C14EE242D8041";
    private const string SurfaceSha = "919FF1CDB1EC5FCD57ED4020F1908AE813C1FB570C8631C15135E07C5DF852E5";

    public static async Task<AztecMaterialMemorySourceProbeReport> ScanAsync(
        string pcFastFile, string aliasTraceManifest, string componentManifest,
        string outputManifest, CancellationToken ct = default)
    {
        using var aliasDoc = JsonDocument.Parse(await File.ReadAllTextAsync(aliasTraceManifest, ct));
        using var componentDoc = JsonDocument.Parse(await File.ReadAllTextAsync(componentManifest, ct));
        var a = aliasDoc.RootElement;
        var g = componentDoc.RootElement;
        if (a.GetProperty("Schema").GetString() != "WaW360.AztecPcGfxWorldMaterialAliasTrace.v1" ||
            a.GetProperty("Status").GetString() != "AZTEC_PC_GFXWORLD_39_POINTER_8BYTE_ALIAS_LATTICE_PASS" ||
            a.GetProperty("HeaderMaterialMemoryCountCandidateAt0x18C").GetUInt32() != ArrayCount ||
            a.GetProperty("HeaderFollowingMaterialMemoryPointerCandidateAt0x190").GetString() != "0xFFFFFFFF" ||
            a.GetProperty("SharedEightByteVirtualOffsetLattice").GetBoolean() != true ||
            a.GetProperty("VirtualSpanEightByteSlots").GetInt32() != ArrayCount ||
            a.GetProperty("UnreferencedSlotsWithinSpan").GetInt32() != 0 ||
            a.GetProperty("DistinctPackedWorldMaterialPointers").GetInt32() != ArrayCount ||
            a.GetProperty("Surfaces").GetInt32() != 735 ||
            a.GetProperty("Triangles").GetInt32() != 19551 ||
            a.GetProperty("SourceMaterialNamesResolved").GetBoolean() ||
            a.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            a.GetProperty("XboxHardwareReady").GetBoolean() ||
            g.GetProperty("Schema").GetString() != "WaW360.AztecPcGfxWorldXenonGeometryComponents.v1" ||
            g.GetProperty("Status").GetString() != "AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS" ||
            g.GetProperty("SurfaceCount").GetInt32() != 735 ||
            g.GetProperty("IndexCount").GetInt32() != 58653 ||
            g.GetProperty("VertexCount").GetInt32() != 32713 ||
            g.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Requires original-PC Aztec 39-slot lattice and 3-buffer Xenon geometry proof.");

        var source = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var b = source.Bytes;
        if (source.Platform != WaWPlatform.PC ||
            source.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Sha(b).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(VertexOffset, 32713 * 44)).Equals(VertexSha, StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(SurfaceOffset, 735 * 52)).Equals(SurfaceSha, StringComparison.OrdinalIgnoreCase) ||
            !source.SourceSha256.Equals(a.GetProperty("OriginalPcFastFileSha256").GetString(), StringComparison.OrdinalIgnoreCase) ||
            !source.SourceSha256.Equals(g.GetProperty("OriginalPCFastFileSha256").GetString(), StringComparison.OrdinalIgnoreCase) ||
            U32(b, HeaderOffset + 0x18C) != ArrayCount ||
            U32(b, HeaderOffset + 0x190) != T4ZonePointer.Following)
            throw new InvalidDataException("Original-PC world, parent inline MaterialMemory count or source SHAs changed.");

        // T4_Assets.h: MaterialMemory {Material* material; int memory;}
        // GfxWorld.txt reorders materialMemory just before vd/vld. The
        // physical serialized array may have intervening inline children,
        // so enumerate a small window ending immediately before vertex
        // data instead of assuming (vertexStart - 39 * 8) is guaranteed.
        var startMin = Math.Max(HeaderOffset + 900, VertexOffset - MaxBeforeVertices);
        var startMax = VertexOffset - ArrayBytes;
        var matched = new List<AztecMaterialMemorySourceArrayCandidate>();
        var threeRecordHits = 0;
        var thirtyNineHits = 0;

        bool ValidEntry(int offset, out uint material, out int memory)
        {
            material = U32(b, offset);
            memory = I32(b, offset + 4);
            if (material == 0 || memory < 0 || memory > 64 * 1024 * 1024)
                return false;
            var dec = T4ZonePointer.Decode(material);
            return dec.Kind == T4ZonePointerKind.Following ||
                   dec.Kind == T4ZonePointerKind.Insert ||
                   (dec.Kind == T4ZonePointerKind.Offset &&
                    dec.Block is not null &&
                    T4ZonePointer.IsWithinBlock(material, source.ZoneHeader));
        }

        for (var start = startMin; start <= startMax; start++)
        {
            if ((start & 0x3FFFF) == 0) ct.ThrowIfCancellationRequested();
            if (!ValidEntry(start, out _, out _) ||
                !ValidEntry(start + 8, out _, out _) ||
                !ValidEntry(start + 16, out _, out _))
                continue;

            threeRecordHits++;
            var good = 3;
            var following = 0;
            var insert = 0;
            var blockPtrs = 0;
            var sumMemory = 0L;
            var positiveMemory = 0;
            var distinctPointers = new HashSet<uint>();
            for (var i = 0; i < ArrayCount; i++)
            {
                var off = start + i * EntrySize;
                if (i >= 3 && !ValidEntry(off, out _, out _))
                    break;
                var ptr = U32(b, off);
                var size = I32(b, off + 4);
                var kind = T4ZonePointer.Decode(ptr).Kind;
                if (kind == T4ZonePointerKind.Following) following++;
                else if (kind == T4ZonePointerKind.Insert) insert++;
                else if (kind == T4ZonePointerKind.Offset) blockPtrs++;
                if (size != 0) positiveMemory++;
                sumMemory += size;
                distinctPointers.Add(ptr);
                if (i >= 3) good++;
            }

            if (good != ArrayCount) continue;
            thirtyNineHits++;
            var first = U32(b, start);
            var middle = U32(b, start + 19 * 8);
            var last = U32(b, start + 38 * 8);
            matched.Add(new AztecMaterialMemorySourceArrayCandidate(
                start, $"0x{start:X8}", ArrayCount, following, insert,
                blockPtrs, distinctPointers.Count, positiveMemory, sumMemory,
                $"0x{first:X8}", $"0x{middle:X8}", $"0x{last:X8}",
                Sha(b.AsSpan(start, ArrayBytes)),
                VertexOffset - (start + ArrayBytes)));
        }

        // Adjacent/overlapping 39-record windows may all look reasonable
        // when the source table is longer than 39. Do not declare an
        // exact owned array from those false ambiguities.
        var ordered = matched
            .OrderByDescending(x => x.PositiveMemoryEntries)
            .ThenByDescending(x => x.DistinctMaterialPointerFields)
            .ThenBy(x => x.BytesBeforeVertexBuffer)
            .ToArray();
        var exactPreVertex = matched.Any(x => x.SourceOffset == VertexOffset - ArrayBytes);
        var result = new AztecMaterialMemorySourceProbeReport(
            "WaW360.AztecPcGfxWorldMaterialMemorySourceProbe.v1",
            matched.Count == 1
                ? "AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_ARRAY_CANDIDATE_PASS"
                : matched.Count > 1
                    ? "AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_ARRAY_MULTIPLE_CANDIDATES"
                    : "AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_ARRAY_NOT_LOCATED",
            Path.GetFullPath(pcFastFile), source.SourceSha256, ZoneSha,
            HeaderOffset, "0x18C", ArrayCount, "0x190", "0xFFFFFFFF",
            VertexOffset, ArrayBytes, MaxBeforeVertices, startMin,
            startMax, threeRecordHits, thirtyNineHits, exactPreVertex,
            ordered.Take(20).ToArray(),
            false, false, false, false,
            "39 x 8-byte MaterialMemory-shaped source records are candidates only. " +
            "This is a bounded physical search anchored to authentic PC world vertices; " +
            "the loader's FOLLOWING marker is not a physical offset. GfxSurface.Material* " +
            "and MaterialMemory* point to different ABI types. No name mapping, no " +
            "ownership claim, no Xbox emitter, no OAT world or hardware authorization.");

        var full = Path.GetFullPath(outputManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }

    private static uint U32(byte[] b, int start) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(start, 4));
    private static int I32(byte[] b, int start) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(start, 4));
    private static string Sha(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(SHA256.HashData(b));
}

public sealed record AztecMaterialMemorySourceArrayCandidate(
    int SourceOffset, string SourceOffsetHex, int MaterialMemoryRecords,
    int FollowingMaterialFields, int InsertMaterialFields, int PackedMaterialFields,
    int DistinctMaterialPointerFields, int PositiveMemoryEntries,
    long TotalMemoryBytes, string FirstMaterialField, string MiddleMaterialField,
    string LastMaterialField, string SourceArraySha256, int BytesBeforeVertexBuffer);

public sealed record AztecMaterialMemorySourceProbeReport(
    string Schema, string Status, string OriginalPCFastFile,
    string OriginalPCFastFileSha256, string OriginalPCZoneSha256,
    int OriginalGfxWorldHeaderOffset, string SourceMaterialCountFieldOffset,
    int MaterialMemoryCount, string SourceMaterialMemoryFieldOffset,
    string SourceMaterialMemoryPointer, int SourceVertexBufferOffset,
    int MaterialMemoryArrayBytes, int MaximumScanBackBytes,
    int ScanStart, int ScanEnd, int ThreeRecordSignatures, int Full39RecordSignatures,
    bool ExactArrayImmediatelyBeforeVertexBuffer,
    IReadOnlyList<AztecMaterialMemorySourceArrayCandidate> Candidates,
    bool ExactPhysicalOwnershipProven, bool PCMaterialNamesResolved,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady, string Note);
