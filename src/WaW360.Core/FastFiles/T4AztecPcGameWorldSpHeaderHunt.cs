using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Read-only bounded original-PC T4 GameWorldSp PathData header evidence.
///
/// A root-level XAsset table marker is NOT a physical root-body offset.
/// All three source worlds can alias the same name string, so never infer a
/// GameWorldSp or GfxWorld body from that one shared map-name occurrence.
/// </summary>
public static class T4AztecPcGameWorldSpHeaderHunt
{
    public static async Task<AztecPcGameWorldSpHuntReport> ScanAsync(
        string pcFastFile, string outputJson, CancellationToken ct = default)
    {
        pcFastFile = Path.GetFullPath(pcFastFile);
        var sourceSha = await HashFileAsync(pcFastFile, ct);
        var source = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        if (source.Platform != WaWPlatform.PC ||
            source.Document.Endianness != Endian.Little ||
            source.Bytes.LongLength != 113701260 ||
            !Sha(source.Bytes).Equals("7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Require the exact SHA-pinned 113701260-byte original-PC Aztec map.ff zone.");

        var bytes = source.Bytes;
        const int comworld = 0x0479B3B5;
        if (!Sha(bytes.AsSpan(comworld, 64)).Equals(
                "907071E579C77CFF430D1FCF931D83F7937143C4BF9C5A40ED19C858BDC61475",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC ComWorld 64-byte source anchor changed.");

        // The PC XAsset entries tell us which root TYPES are physically
        // present and whether their top-level header is inline / an alias.
        // They do NOT specify the bytes' positions in the packed stream.
        var worldRoots = source.Document.Assets
            .Where(a => Normalize(a.TypeName) is "comworld" or "gameworldsp" or "gfxworld")
            .Select(a => new AztecPcWorldTableRoot(
                a.Index, a.TypeName, $"0x{a.RawHeader:X8}", a.ReferenceKind.ToString()))
            .ToArray();

        // T4 GameWorldSp is exactly 44 bytes on the PC 32-bit ABI:
        // name* + PathData { nodeCount, nodes*, basenodes*, chainNodeCount,
        // chainNodeForNode*, nodeForChainNode*, visBytes, pathVis*,
        // nodeTreeCount, nodeTree* }. Search for the *path* pointer
        // signature rather than the shared name string.
        var candidates = new List<AztecPcGameWorldSpCandidate>();
        var hits = 0;
        var nodesPtrHit = 0;
        for (var offset = 0; offset <= bytes.Length - 44; offset++)
        {
            // Fast gate: +8 is a following/insert pathnode_t array pointer.
            if (bytes[offset + 9] != 0xFF ||
                bytes[offset + 10] != 0xFF ||
                bytes[offset + 11] != 0xFF ||
                bytes[offset + 8] < 0xFE)
                continue;
            nodesPtrHit++;

            var nodeCount = U(bytes, offset + 4);
            if (nodeCount == 0 || nodeCount > 200000)
                continue;
            var namePtr = U(bytes, offset);
            var nodesPtr = U(bytes, offset + 8);
            var basePtr = U(bytes, offset + 12);
            var chainCount = U(bytes, offset + 16);
            var forNodePtr = U(bytes, offset + 20);
            var forChainPtr = U(bytes, offset + 24);
            var visBytes = U(bytes, offset + 28);
            var visPtr = U(bytes, offset + 32);
            var treeCount = U(bytes, offset + 36);
            var treePtr = U(bytes, offset + 40);

            // Require independent, bounded counts and a real base-node
            // stream pointer. This intentionally may miss alias-only
            // roots; absence of hits is NOT absence of the source body.
            if (!PointerValid(namePtr, source.Document, false) ||
                !PointerValid(basePtr, source.Document, true) ||
                basePtr == 0 ||
                chainCount > 250000 || visBytes > 20000000 ||
                treeCount > 150000 ||
                !PointerValid(forNodePtr, source.Document, false) ||
                !PointerValid(forChainPtr, source.Document, false) ||
                !PointerValid(visPtr, source.Document, false) ||
                !PointerValid(treePtr, source.Document, false) ||
                (chainCount > 0 && (forNodePtr == 0 || forChainPtr == 0)) ||
                (visBytes > 0 && visPtr == 0) ||
                (treeCount > 0 && treePtr == 0))
                continue;
            hits++;

            var score = 0;
            if (IsInline(namePtr)) score += 3;
            else if (namePtr != 0) score += 1;
            if (IsInline(nodesPtr)) score += 4;
            if (IsInline(basePtr)) score += 4;
            if (chainCount <= nodeCount) score += 2;
            if (chainCount > 0 && IsInline(forNodePtr)) score += 2;
            if (chainCount > 0 && IsInline(forChainPtr)) score += 2;
            if (visBytes > 0 && IsInline(visPtr)) score += 2;
            if (treeCount > 0 && IsInline(treePtr)) score += 2;
            if (treeCount > 0 && treeCount <= nodeCount) score += 2;
            if (offset % 4 == 0) score++;
            var candidate = new AztecPcGameWorldSpCandidate(
                offset, $"0x{offset:X8}", score,
                nodeCount, chainCount, visBytes, treeCount,
                $"0x{namePtr:X8}", $"0x{nodesPtr:X8}", $"0x{basePtr:X8}",
                $"0x{forNodePtr:X8}", $"0x{forChainPtr:X8}",
                $"0x{visPtr:X8}", $"0x{treePtr:X8}",
                Sha(bytes.AsSpan(offset, 44)),
                false);

            // Bounded memory with deterministic ranking. Keep 128 most
            // plausible; do not claim the highest-ranked hit is the root.
            if (candidates.Count < 128) candidates.Add(candidate);
            else
            {
                var lowIndex = 0;
                for (var j = 1; j < candidates.Count; j++)
                    if (candidates[j].Score < candidates[lowIndex].Score ||
                        (candidates[j].Score == candidates[lowIndex].Score &&
                         candidates[j].Offset > candidates[lowIndex].Offset))
                        lowIndex = j;
                if (candidate.Score > candidates[lowIndex].Score ||
                    (candidate.Score == candidates[lowIndex].Score &&
                     candidate.Offset < candidates[lowIndex].Offset))
                    candidates[lowIndex] = candidate;
            }
        }

        var result = new AztecPcGameWorldSpHuntReport(
            "WaW360.AztecPcGameWorldSpHeaderHunt.v1",
            "AZTEC_PC_GAMEWORLDSP_HEADER_HUNT_COMPLETE",
            pcFastFile,
            sourceSha,
            Sha(bytes),
            bytes.Length,
            source.Document.AssetCount,
            source.Document.AssetTableOffset,
            worldRoots,
            nodesPtrHit,
            hits,
            candidates.OrderByDescending(x => x.Score).ThenBy(x => x.Offset).ToArray(),
            false, false, false,
            "PC world root table and 44-byte path-shaped byte candidates only. " +
            "Neither root stream ownership, nested path arrays, or physical GfxWorld source is proven. " +
            "This emits no FastFile. All six-family scratch FF outputs remain NOT FOR XBOX.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputJson))!);
        await File.WriteAllTextAsync(outputJson,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }

    private static uint U(ReadOnlySpan<byte> b, int p) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(p, 4));

    private static bool IsInline(uint p) => p is 0xFFFFFFFFu or 0xFFFFFFFEu;

    private static bool PointerValid(uint raw, T4ZoneDocument doc, bool required)
    {
        if (raw == 0) return !required;
        if (IsInline(raw)) return true;
        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset ||
            decoded.Block is not T4ZoneBlock block)
            return false;
        var idx = (int)block;
        return idx >= 0 && idx < doc.Header.BlockSizes.Count &&
               decoded.Offset < doc.Header.BlockSizes[idx];
    }

    private static string Normalize(string s) => s.Replace("_", "", StringComparison.Ordinal)
        .Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream, ct));
    }
}

public sealed record AztecPcWorldTableRoot(
    int PcAssetIndex, string PcTypeName, string RawHeader, string ReferenceKind);

public sealed record AztecPcGameWorldSpCandidate(
    int Offset, string OffsetHex, int Score, uint NodeCount, uint ChainNodeCount,
    uint VisBytes, uint NodeTreeCount, string NamePtr, string NodesPtr,
    string BaseNodesPtr, string ChainNodeForNodePtr, string NodeForChainNodePtr,
    string PathVisPtr, string NodeTreePtr, string Fixed44ByteSha256, bool Proven);

public sealed record AztecPcGameWorldSpHuntReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    long OriginalPcZoneBytes, int PcAssetCount, long PcAssetTableOffset,
    IReadOnlyList<AztecPcWorldTableRoot> PcWorldAssetRoots,
    long RawNodesArrayPointerMarkerHits, long StructurallyPlausibleHeaderHits,
    IReadOnlyList<AztecPcGameWorldSpCandidate> TopCandidates,
    bool ActualGameWorldSpSourceRootLocated,
    bool NestedPathDataDecoded,
    bool ReadyForXbox,
    string Note);
