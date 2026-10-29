using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Source-only proof of how 39 packed GfxSurface.Material pointers are
/// positioned and reused in the original-PC XFile. An eight-byte alias
/// lattice is only a hypothesis, NOT a Material identity binding.
/// </summary>
public static class T4AztecPcGfxWorldMaterialAliasTrace
{
    private const int RootAt = 0x049122C0;
    private const int SurfaceAt = 0x058E361E;
    private const int SurfaceCount = 735;
    private const int SurfaceStride = 52;
    private const int MaterialFieldOffset = 20;
    private const string PcZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const string PcSurfaceSha =
        "919FF1CDB1EC5FCD57ED4020F1908AE813C1FB570C8631C15135E07C5DF852E5";

    public static async Task<AztecPcGfxMaterialAliasTraceReport> ScanAsync(
        string pcFastFile, string atlasPath, string correlationPath,
        string outputJson, CancellationToken ct = default)
    {
        pcFastFile = Path.GetFullPath(pcFastFile);
        using var atlasDoc = JsonDocument.Parse(await File.ReadAllTextAsync(atlasPath, ct));
        using var correlationDoc = JsonDocument.Parse(await File.ReadAllTextAsync(correlationPath, ct));
        var atlas = atlasDoc.RootElement;
        var proof = correlationDoc.RootElement;
        if (atlas.GetProperty("schema").GetString() != "WaW360.AztecPcGfxWorldMaterialBindingAtlas.v1" ||
            atlas.GetProperty("status").GetString() != "AZTEC_PC_GFXWORLD_39_MATERIAL_GROUP_DEPENDENCY_ATLAS_PASS" ||
            atlas.GetProperty("originalPcPointerGroups").GetInt32() != 39 ||
            atlas.GetProperty("totalSurfaceReferences").GetInt32() != SurfaceCount ||
            atlas.GetProperty("totalTrianglesCovered").GetInt32() != 19551 ||
            atlas.GetProperty("materialPointerGroupsBoundToXbox").GetInt32() != 0 ||
            atlas.GetProperty("readyForXbox").GetBoolean() ||
            proof.GetProperty("schema").GetString() != "WaW360.AztecPcGfxWorldMaterialRootAliasProof.v1" ||
            proof.GetProperty("status").GetString() != "AZTEC_PC_GFXWORLD_39_POINTER_ROOT_CORRELATION_COMPLETE" ||
            proof.GetProperty("pcAssetCount").GetInt32() != 4874 ||
            proof.GetProperty("pcMaterialRootCount").GetInt32() != 1 ||
            proof.GetProperty("originalPcGfxWorldMaterialPointers").GetInt32() != 39 ||
            proof.GetProperty("uniqueExactPcMaterialHeaderMatches").GetInt32() != 0 ||
            proof.GetProperty("ambiguousPcMaterialHeaderMatches").GetInt32() != 0 ||
            proof.GetProperty("matchedOtherTypeRootHeaders").GetInt32() != 0 ||
            proof.GetProperty("materialNamesProven").GetInt32() != 0 ||
            proof.GetProperty("xboxMaterialBindingsProven").GetInt32() != 0 ||
            proof.GetProperty("gate7Complete").GetBoolean() ||
            proof.GetProperty("readyForXbox").GetBoolean())
            throw new InvalidDataException("Original-PC Aztec source material atlas/39 alias-only root correlation changed.");

        var source = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var bytes = source.Bytes;
        if (source.Platform != WaWPlatform.PC ||
            source.Document.Endianness != Endian.Little ||
            source.Document.AssetCount != 4874 ||
            source.Document.Assets.Count(a => a.TypeName.Equals("material", StringComparison.OrdinalIgnoreCase)) != 1 ||
            bytes.Length != 113701260 ||
            !string.Equals(Sha(bytes), PcZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.SourceSha256,
                proof.GetProperty("originalPcFastFileSha256").GetString(), StringComparison.OrdinalIgnoreCase) ||
            !Sha(bytes.AsSpan(SurfaceAt, SurfaceCount * SurfaceStride))
                .Equals(PcSurfaceSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC zone, 735 surfaces or single-root material identity drifted.");

        var expected = new Dictionary<uint, (int Surfaces, int Triangles)>();
        foreach (var item in atlas.GetProperty("materialReferenceGroups").EnumerateArray())
        {
            var value = item.GetProperty("PcMaterialPointer").GetString() ?? "";
            if (value.Length != 10 || !value.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected source packed GfxSurface Material pointer string.");
            var ptr = Convert.ToUInt32(value.AsSpan(2), 16);
            var surfaces = item.GetProperty("SurfaceCount").GetInt32();
            var tris = item.GetProperty("TriangleCount").GetInt32();
            if (!T4ZonePointer.IsWithinBlock(ptr, source.ZoneHeader) ||
                !expected.TryAdd(ptr, (surfaces, tris)))
                throw new InvalidDataException($"Bad or repeated source Material pointer {value}.");
        }
        if (expected.Count != 39 || expected.Values.Sum(v => v.Surfaces) != 735 ||
            expected.Values.Sum(v => v.Triangles) != 19551)
            throw new InvalidDataException("Source GfxSurface material group coverage drifted.");

        var observedSurfaces = expected.Keys.ToDictionary(v => v, _ => 0);
        var observedTriangles = expected.Keys.ToDictionary(v => v, _ => 0);
        for (var s = 0; s < SurfaceCount; s++)
        {
            var offset = SurfaceAt + s * SurfaceStride;
            var ptr = U32(bytes, offset + MaterialFieldOffset);
            if (!expected.ContainsKey(ptr))
                throw new InvalidDataException($"Source GfxSurface#{s} Material reference left 39-group inventory.");
            observedSurfaces[ptr]++;
            observedTriangles[ptr] += U16(bytes, offset + 10);
        }
        foreach (var ptr in expected.Keys)
        {
            if (observedSurfaces[ptr] != expected[ptr].Surfaces ||
                observedTriangles[ptr] != expected[ptr].Triangles)
                throw new InvalidDataException($"Source GfxSurface Material {ptr:X8} usage count changed.");
        }

        // Analyze the encoded VIRTUAL offsets numerically. A step size of 8
        // is suggestive of a pointer/record table but proves NO object type
        // or Material name. All offsets must be independently bounded.
        var pointers = expected.Keys.OrderBy(x => x).ToArray();
        var firstBlock = T4ZonePointer.Decode(pointers[0]);
        var sameBlock = pointers.All(p =>
        {
            var d = T4ZonePointer.Decode(p);
            return d.Kind == T4ZonePointerKind.Offset && d.Block == firstBlock.Block;
        });
        var offsets = pointers.Select(p => (long)T4ZonePointer.Decode(p).Offset).ToArray();
        var alignment = offsets[0] % 8L;
        var onEightByteLattice = sameBlock &&
            offsets.All(v => v % 8L == alignment) &&
            pointers.Length == 39;
        var slotSpan = checked((int)((offsets[^1] - offsets[0]) / 8L + 1L));
        var candidateSlotsAbsent = onEightByteLattice
            ? slotSpan - pointers.Length
            : -1;
        var distances = new SortedDictionary<long, int>();
        for (var i = 1; i < offsets.Length; i++)
        {
            var d = offsets[i] - offsets[i - 1];
            distances[d] = distances.TryGetValue(d, out var prior) ? prior + 1 : 1;
        }

        // Scan original SERIALIZED bytes for occurrences outside the known
        // GfxSurface.Material fields. They may indicate linked dependency
        // references elsewhere. Physical offset does not equal decoded
        // VIRTUAL block offset; do not mistake the two coordinate systems.
        var extras = expected.Keys.ToDictionary(v => v, _ => new List<int>());
        var externalCounts = expected.Keys.ToDictionary(v => v, _ => 0);
        for (var i = 0; i <= bytes.Length - 4; i++)
        {
            if ((i & 0x3FFFFF) == 0) ct.ThrowIfCancellationRequested();
            if (bytes[i + 3] != 0x84) continue; // all current 39 references are block 4
            var raw = U32(bytes, i);
            if (!expected.ContainsKey(raw)) continue;
            var rel = i - (SurfaceAt + MaterialFieldOffset);
            var isSurface = rel >= 0 && rel % SurfaceStride == 0 && rel / SurfaceStride < SurfaceCount;
            if (isSurface) continue;
            externalCounts[raw]++;
            if (extras[raw].Count < 12)
                extras[raw].Add(i);
        }
        var offSurfaceTotals = externalCounts.Values.Sum();

        // Search only the fixed source GfxWorld header for numerically
        // interesting material-memory-count candidates. We deliberately
        // avoid labeling offsets as true T4 ABI members until independently
        // validated against the native struct's complete layout.
        var matchingHeaderCountWords = new List<int>();
        var candidates = new List<AztecPcGfxWorldMaterialHeaderCandidate>();
        for (var off = 80; off <= 896; off += 4)
        {
            var val = U32(bytes, RootAt + off);
            if (val == 39u)
                matchingHeaderCountWords.Add(off);
            if (val == 39u || (val >= 30u && val <= 256u))
            {
                var pointer = off <= 892 ? U32(bytes, RootAt + off + 4) : 0u;
                if (pointer != 0 && T4ZonePointer.IsWithinBlock(pointer, source.ZoneHeader))
                    candidates.Add(new AztecPcGfxWorldMaterialHeaderCandidate(
                        off, $"0x{off:X3}", val, $"0x{pointer:X8}"));
            }
        }

        var entries = pointers.Select(ptr =>
            new AztecPcGfxWorldMaterialAliasGroup(
                $"0x{ptr:X8}", $"0x{T4ZonePointer.Decode(ptr).Offset:X8}",
                observedSurfaces[ptr], observedTriangles[ptr], externalCounts[ptr],
                extras[ptr].Select(o => $"0x{o:X8}").ToArray())).ToArray();

        var result = new AztecPcGfxWorldMaterialAliasTraceReport(
            "WaW360.AztecPcGfxWorldMaterialAliasTrace.v1",
            sameBlock && onEightByteLattice
                ? "AZTEC_PC_GFXWORLD_39_POINTER_8BYTE_ALIAS_LATTICE_PASS"
                : "AZTEC_PC_GFXWORLD_39_POINTER_ALIAS_GEOMETRY_UNRESOLVED",
            pcFastFile, source.SourceSha256, PcZoneSha, 4874, 1, 39, 735, 19551,
            firstBlock.Block?.ToString() ?? "Unknown",
            $"0x{offsets[0]:X8}", $"0x{offsets[^1]:X8}", alignment,
            onEightByteLattice, slotSpan, candidateSlotsAbsent,
            distances.Select(d => new AztecPcGfxWorldAliasDistance(d.Key, d.Value)).ToArray(),
            offSurfaceTotals,
            matchingHeaderCountWords.Select(o => $"0x{o:X3}").ToArray(),
            candidates.ToArray(), entries,
            false, false, false,
            "All 39 source GfxSurface.Material packed pointers are traced across the " +
            "verified original PC serialized zone. Eight-byte VIRTUAL offset spacing " +
            "can reflect a reference-slot table, but does NOT prove MaterialMemory, " +
            "Material object identity, filename, Xbox dependency or asset binding. " +
            "Physical file offsets and decoded VIRTUAL offsets must not be equated; " +
            "source header integer matches are only candidates. NO FF.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputJson))!);
        await File.WriteAllTextAsync(outputJson, JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }

    private static uint U32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static ushort U16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}

public sealed record AztecPcGfxWorldMaterialHeaderCandidate(
    int SourceWorldHeaderRelativeOffset, string SourceWorldHeaderRelativeOffsetHex,
    uint CandidateCount, string NextPointerWord);

public sealed record AztecPcGfxWorldAliasDistance(
    long OffsetDelta, int PairCount);

public sealed record AztecPcGfxWorldMaterialAliasGroup(
    string PcMaterialPointer, string SourceVirtualOffset, int SurfaceCount,
    int TriangleCount, int OtherSerializedReferenceCount,
    IReadOnlyList<string> OtherSerializedReferenceOffsets);

public sealed record AztecPcGfxWorldMaterialAliasTraceReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    int OriginalPcAssetCount, int OriginalPcTopLevelMaterialRoots,
    int DistinctPackedWorldMaterialPointers, int Surfaces, int Triangles,
    string SourcePointerBlock, string LowestVirtualOffset,
    string HighestVirtualOffset, long RelativeOffsetMod8,
    bool SharedEightByteVirtualOffsetLattice, int VirtualSpanEightByteSlots,
    int UnreferencedSlotsWithinSpan, IReadOnlyList<AztecPcGfxWorldAliasDistance> PointerDistances,
    int AdditionalNonSurfaceSerializedPointerHits,
    IReadOnlyList<string> FixedWorldHeader39CountCandidateOffsets,
    IReadOnlyList<AztecPcGfxWorldMaterialHeaderCandidate> FixedWorldHeaderCountPointerCandidates,
    IReadOnlyList<AztecPcGfxWorldMaterialAliasGroup> Groups,
    bool SourceMaterialNamesResolved, bool XboxMaterialBindingsProven,
    bool XboxHardwareReady, string Note);
