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
        // The prior v1 "any valid packed block" heuristic was unsound:
        // low integer array values such as 0x00008E00 trivially fall in
        // the Temp block, yielding >700,000 overlapping 39-row windows.
        //
        // v2 is intentionally narrower, NOT an assertion of ownership:
        // original PC Material* fields are required to be explicit
        // Following/Insert loaders or bounded VIRTUAL-block offsets.
        // Reject low Temp-block numbers. Require an isolated run of
        // exactly 39 entries (entry before AND entry after invalid).
        // Null entries, other block types, and non-isolated arrays may
        // still be legal elsewhere, so no-match is NOT proof of absence.
        const int MaxReportCandidates = 20;
        var startMin = Math.Max(HeaderOffset + 900, VertexOffset - MaxBeforeVertices);
        var startMax = VertexOffset - ArrayBytes;
        var matched = new List<AztecMaterialMemorySourceArrayCandidate>();
        var threeRecordHits = 0;
        var thirtyNineHits = 0;
        var isolated39Hits = 0;
        var fullWindowEvidence = new List<AztecMaterialMemoryWindowBoundaryEvidence>();
        const int MaxFullWindowSamples = 32;

        var worldSurfaceMaterialPointers = a.GetProperty("Groups")
            .EnumerateArray()
            .Select(item => Convert.ToUInt32(
                (item.GetProperty("PcMaterialPointer").GetString() ?? "")[2..], 16))
            .ToHashSet();
        if (worldSurfaceMaterialPointers.Count != 39)
            throw new InvalidDataException("Alias manifest does not contain 39 source surface Material pointers.");

        bool StrongEntry(int offset)
        {
            if (offset < startMin || offset > VertexOffset - EntrySize)
                return false;

            var material = U32(b, offset);
            var memory = I32(b, offset + 4);
            if (material == 0 || memory < 0 || memory > 64 * 1024 * 1024)
                return false;

            var decoded = T4ZonePointer.Decode(material);
            if (decoded.Kind is T4ZonePointerKind.Following or T4ZonePointerKind.Insert)
                return true;

            return decoded.Kind == T4ZonePointerKind.Offset &&
                   decoded.Block == T4ZoneBlock.Virtual &&
                   T4ZonePointer.IsWithinBlock(material, source.ZoneHeader);
        }

        // Fixed-size, non-allocating scan. v1 eagerly created and SHA'd
        // every false positive; v2 computes a SHA only for an isolated
        // full-length candidate and caps returned samples.
        for (var start = startMin; start <= startMax; start++)
        {
            if ((start & 0x3FFFF) == 0) ct.ThrowIfCancellationRequested();
            if (!StrongEntry(start) ||
                !StrongEntry(start + EntrySize) ||
                !StrongEntry(start + 2 * EntrySize))
                continue;

            threeRecordHits++;
            var good = 3;
            for (var i = 3; i < ArrayCount; i++)
            {
                if (!StrongEntry(start + i * EntrySize))
                    break;
                good++;
            }

            if (good != ArrayCount)
                continue;
            thirtyNineHits++;

            // Record EVERY surviving full-39 start (up to 32), including
            // non-isolated windows. The previous v2 saved only isolated
            // candidates and therefore hid the two real surviving windows.
            // The source pointer/member statistics are observational only;
            // an adjacent second window means these are overlapping views
            // of >=40 records, NOT two different owned MaterialMemory arrays.
            if (fullWindowEvidence.Count < MaxFullWindowSamples)
            {
                var overlapPtrs = 0;
                var inlineMarkers = 0;
                var virtualRefs = 0;
                var distinct = new HashSet<uint>();
                var totalMemory = 0L;
                for (var j = 0; j < ArrayCount; j++)
                {
                    var at = start + j * EntrySize;
                    var raw = U32(b, at);
                    var kind = T4ZonePointer.Decode(raw).Kind;
                    if (kind is T4ZonePointerKind.Following or T4ZonePointerKind.Insert)
                        inlineMarkers++;
                    else if (kind == T4ZonePointerKind.Offset)
                        virtualRefs++;
                    if (worldSurfaceMaterialPointers.Contains(raw))
                        overlapPtrs++;
                    distinct.Add(raw);
                    totalMemory += I32(b, at + 4);
                }
                fullWindowEvidence.Add(new AztecMaterialMemoryWindowBoundaryEvidence(
                    start, $"0x{start:X8}", start - startMin,
                    start >= startMin + EntrySize && StrongEntry(start - EntrySize),
                    start + ArrayBytes <= VertexOffset - EntrySize && StrongEntry(start + ArrayBytes),
                    overlapPtrs, inlineMarkers, virtualRefs, distinct.Count, totalMemory,
                    $"0x{U32(b, start):X8}", $"0x{U32(b, start + 38 * EntrySize):X8}",
                    Sha(b.AsSpan(start, ArrayBytes))));
            }

            // A longer valid run gives multiple indistinguishable
            // 39-row windows. Excluding them avoids misidentifying a
            // sliding window as an independent MaterialMemory table.
            var prev = start >= startMin + EntrySize &&
                       StrongEntry(start - EntrySize);
            var next = start + ArrayBytes + EntrySize <= VertexOffset &&
                       StrongEntry(start + ArrayBytes);
            if (prev || next)
                continue;

            isolated39Hits++;
            if (matched.Count >= MaxReportCandidates)
                continue;

            var following = 0;
            var insert = 0;
            var virtualFields = 0;
            var positiveMemory = 0;
            var surfacePointerMatches = 0;
            var totalMemory = 0L;
            var distinctPointers = new HashSet<uint>();
            for (var i = 0; i < ArrayCount; i++)
            {
                var at = start + i * EntrySize;
                var material = U32(b, at);
                var size = I32(b, at + 4);
                var kind = T4ZonePointer.Decode(material).Kind;
                if (kind == T4ZonePointerKind.Following) following++;
                else if (kind == T4ZonePointerKind.Insert) insert++;
                else if (kind == T4ZonePointerKind.Offset) virtualFields++;
                if (size != 0) positiveMemory++;
                if (worldSurfaceMaterialPointers.Contains(material))
                    surfacePointerMatches++;
                totalMemory += size;
                distinctPointers.Add(material);
            }

            matched.Add(new AztecMaterialMemorySourceArrayCandidate(
                start, $"0x{start:X8}", ArrayCount, following, insert,
                virtualFields, distinctPointers.Count, positiveMemory,
                totalMemory, $"0x{U32(b, start):X8}",
                $"0x{U32(b, start + 19 * 8):X8}",
                $"0x{U32(b, start + 38 * 8):X8}",
                Sha(b.AsSpan(start, ArrayBytes)),
                VertexOffset - (start + ArrayBytes),
                surfacePointerMatches));
        }

        var ordered = matched
            .OrderByDescending(x => x.OriginalSurfaceMaterialPointerMatches)
            .ThenByDescending(x => x.DistinctMaterialPointerFields)
            .ThenBy(x => x.BytesBeforeVertexBuffer)
            .ToArray();
        var exactPreVertex = matched.Any(x => x.SourceOffset == VertexOffset - ArrayBytes);
        var adjacentTwoWindowsFortyRecordRun =
            thirtyNineHits == 2 && fullWindowEvidence.Count == 2 &&
            Math.Abs(fullWindowEvidence[0].SourceOffset -
                     fullWindowEvidence[1].SourceOffset) == EntrySize &&
            isolated39Hits == 0;
        var status = adjacentTwoWindowsFortyRecordRun
            ? "AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_TWO_OVERLAPPING_WINDOWS_40_RECORD_RUN"
            : isolated39Hits == 1
            ? "AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_STRICT_ISOLATED_CANDIDATE"
            : isolated39Hits > 1
                ? "AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_STRICT_AMBIGUOUS"
                : "AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_STRICT_NOT_LOCATED";

        var result = new AztecMaterialMemorySourceProbeReport(
            "WaW360.AztecPcGfxWorldMaterialMemorySourceProbe.v3",
            status,
            Path.GetFullPath(pcFastFile), source.SourceSha256, ZoneSha,
            HeaderOffset, "0x18C", ArrayCount, "0x190", "0xFFFFFFFF",
            VertexOffset, ArrayBytes, MaxBeforeVertices, startMin,
            startMax, threeRecordHits, thirtyNineHits, isolated39Hits, exactPreVertex,
            adjacentTwoWindowsFortyRecordRun, fullWindowEvidence.ToArray(),
            ordered,
            false, false, false, false,
            "Original-PC source-bound 39x8 boundary analysis. Version 1's " +
            "714,674 false positives were arbitrary Temp block numeric data. " +
            "Version 2 found 47 strong 3-row signatures, 2 full windows and 0 " +
            "isolated 39-row runs but hid non-isolated locations. Version 3 " +
            "records bounded exact starts, preceding/following record validity, " +
            "39-slot pointer-field overlap with the real GfxSurface material " +
            "reference lattice, inline directive count, distinct references, " +
            "and SHA256 for each full window. Adjacent windows imply a " +
            "40-record run, not a proved 39-record MaterialMemory asset. " +
            "Null/other-block pointers are deliberately excluded; no-hit " +
            "does NOT establish absence. No names/Xbox bindings/FF.");

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
    string LastMaterialField, string SourceArraySha256, int BytesBeforeVertexBuffer,
    int OriginalSurfaceMaterialPointerMatches);

public sealed record AztecMaterialMemoryWindowBoundaryEvidence(
    int SourceOffset, string SourceOffsetHex, int DistanceFromScanStart,
    bool PreviousEightByteRecordPlausible,
    bool NextEightByteRecordPlausible,
    int FieldMatchesWithSourceGfxSurfaceMaterialPointers,
    int FollowingOrInsertDirectiveFields, int PackedVirtualPointerFields,
    int DistinctMaterialPointerFields, long SumMemoryFields,
    string FirstMaterialWord, string LastMaterialWord, string Source39WindowSha256);

public sealed record AztecMaterialMemorySourceProbeReport(
    string Schema, string Status, string OriginalPCFastFile,
    string OriginalPCFastFileSha256, string OriginalPCZoneSha256,
    int OriginalGfxWorldHeaderOffset, string SourceMaterialCountFieldOffset,
    int MaterialMemoryCount, string SourceMaterialMemoryFieldOffset,
    string SourceMaterialMemoryPointer, int SourceVertexBufferOffset,
    int MaterialMemoryArrayBytes, int MaximumScanBackBytes,
    int ScanStart, int ScanEnd, int ThreeRecordSignatures, int Full39RecordSignatures,
    int Isolated39RecordSignatures,
    bool ExactArrayImmediatelyBeforeVertexBuffer,
    bool TwoFullWindowsOverlapIntoFortyRecordRun,
    IReadOnlyList<AztecMaterialMemoryWindowBoundaryEvidence> FullWindowBoundaryEvidence,
    IReadOnlyList<AztecMaterialMemorySourceArrayCandidate> Candidates,
    bool ExactPhysicalOwnershipProven, bool PCMaterialNamesResolved,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady, string Note);
