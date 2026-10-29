using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Compares the 39 SHA-pinned ORIGINAL-PC contiguous Material name candidates
/// against 8,509 golden native Xbox 360 XAsset-root names. The result is a
/// NAME INVENTORY, not a virtual-pointer relocation, owner identity proof,
/// material serialization, valid hardware reference, or permission to graft.
/// </summary>
public static class T4AztecPcWorldMaterialNativeNameReconciliation
{
    private const string GoldenNativeSha =
        "E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F";
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";
    private const int NativeAssetCount = 8509;
    private const int MaterialCount = 39;
    private const int SurfaceCount = 735;
    private const int TriangleCount = 19551;

    public static async Task<AztecPcWorldMaterialNativeNameReport> ProbeAsync(
        string bridgeJson, string nativeIdentityJson, string atlasJson,
        string outputJson, CancellationToken ct = default)
    {
        using var bd = JsonDocument.Parse(await File.ReadAllTextAsync(bridgeJson, ct));
        using var nd = JsonDocument.Parse(await File.ReadAllTextAsync(nativeIdentityJson, ct));
        using var ad = JsonDocument.Parse(await File.ReadAllTextAsync(atlasJson, ct));
        var b = bd.RootElement;
        var n = nd.RootElement;
        var a = ad.RootElement;
        if (b.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcMaterialWorldBoundaryBridge.v1" ||
            b.GetProperty("Status").GetString() !=
                "AZTEC_PC_39_MATERIALS_EXACT_MEMORY_TO_VERTICES_PHYSICAL_CHAIN" ||
            !b.GetProperty("ContiguousPhysicalChainValidated").GetBoolean() ||
            b.GetProperty("MaterialMemoryEntries").GetInt32() != MaterialCount ||
            b.GetProperty("ConsecutiveMaterialCandidates").GetInt32() != MaterialCount ||
            b.GetProperty("VirtualAliasGroups").GetInt32() != MaterialCount ||
            b.GetProperty("SourceSurfaceCount").GetInt32() != SurfaceCount ||
            b.GetProperty("SourceTriangleCount").GetInt32() != TriangleCount ||
            b.GetProperty("FirstMaterialHeaderAt").GetString() != "0x05731803" ||
            b.GetProperty("VertexBufferAt").GetString() != "0x057359F9" ||
            b.GetProperty("OriginalPcZoneSha256").GetString() != ZoneSha ||
            b.GetProperty("WorldMaterialMemoryOwnershipProven").GetBoolean() ||
            b.GetProperty("VirtualAliasToPhysicalMaterialRelocationProven").GetBoolean() ||
            b.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            b.GetProperty("XboxHardwareReady").GetBoolean() ||
            a.GetProperty("schema").GetString() !=
                "WaW360.AztecPcGfxWorldMaterialBindingAtlas.v1" ||
            a.GetProperty("status").GetString() !=
                "AZTEC_PC_GFXWORLD_39_MATERIAL_GROUP_DEPENDENCY_ATLAS_PASS" ||
            a.GetProperty("originalPcPointerGroups").GetInt32() != MaterialCount ||
            a.GetProperty("totalSurfaceReferences").GetInt32() != SurfaceCount ||
            a.GetProperty("totalTrianglesCovered").GetInt32() != TriangleCount ||
            a.GetProperty("sourcePcMaterialNamesResolved").GetInt32() != 0 ||
            a.GetProperty("materialPointerGroupsBoundToXbox").GetInt32() != 0 ||
            a.GetProperty("materialSurfaceSlotsBoundToXbox").GetInt32() != 0 ||
            a.GetProperty("fullMaterialClosure").GetBoolean() ||
            a.GetProperty("readyForXbox").GetBoolean() ||
            !n.GetProperty("TypeSequenceVerified").GetBoolean())
            throw new InvalidDataException(
                "Require PC physical bridge, 735-source surface inventory, verified native manifest, and zero-bound safety gates.");

        var materialRows = b.GetProperty("OrdinalPairingHypotheses").EnumerateArray().ToArray();
        var groups = a.GetProperty("materialReferenceGroups").EnumerateArray().ToArray();
        var nativeRows = n.GetProperty("Entries").EnumerateArray().ToArray();
        if (materialRows.Length != MaterialCount ||
            groups.Length != MaterialCount || nativeRows.Length != NativeAssetCount ||
            b.GetProperty("OrdinalPairingHypotheses")
                .EnumerateArray().Any(x =>
                    x.GetProperty("VirtualAliasToMaterialBindingProven").GetBoolean()))
            throw new InvalidDataException("The original 39 rows/39 groups/8,509 root sequence changed.");

        var nativeFile = n.GetProperty("NativeFastFile").GetString();
        if (string.IsNullOrWhiteSpace(nativeFile) ||
            !File.Exists(nativeFile) ||
            new FileInfo(nativeFile).Length != 82739469L)
            throw new InvalidDataException("Pinned golden native Xbox fastfile missing or size changed.");
        await using (var stream = File.OpenRead(nativeFile))
        {
            var nativeSha = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, ct));
            if (!nativeSha.Equals(GoldenNativeSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Golden Xbox native fastfile SHA-256 changed.");
        }

        var xboxMaterials = new List<AztecPcNativeMaterialRoot>();
        var byName = new Dictionary<string, List<AztecPcNativeMaterialRoot>>(
            StringComparer.OrdinalIgnoreCase);
        var seenRootIndices = new HashSet<int>();
        foreach (var native in nativeRows)
        {
            ct.ThrowIfCancellationRequested();
            var index = native.GetProperty("Index").GetInt32();
            var type = native.GetProperty("OatTypeName").GetString() ?? "";
            var name = native.GetProperty("Name").GetString() ?? "";
            if (index < 0 || index >= NativeAssetCount ||
                !seenRootIndices.Add(index) || string.IsNullOrWhiteSpace(type) ||
                string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("Golden Xbox native identity root index, type, or name is invalid.");
            if (!type.Equals("material", StringComparison.OrdinalIgnoreCase))
                continue;
            var entry = new AztecPcNativeMaterialRoot(index, name);
            xboxMaterials.Add(entry);
            if (!byName.TryGetValue(name, out var matched))
            {
                matched = new List<AztecPcNativeMaterialRoot>();
                byName.Add(name, matched);
            }
            matched.Add(entry);
        }
        if (seenRootIndices.Count != NativeAssetCount ||
            xboxMaterials.Count < MaterialCount)
            throw new InvalidDataException("Golden native root sequence lacks 8,509 unique IDs or at least 39 Material roots.");

        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<AztecPcWorldMaterialNativeNameRow>();
        for (var i = 0; i < MaterialCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = materialRows[i];
            var name = row.GetProperty("UnverifiedMaterialName").GetString() ?? "";
            var virtualAlias = row.GetProperty("VirtualAliasPointerHypothesis").GetString() ?? "";
            var sourceHeader = row.GetProperty("MaterialHeaderPhysicalOffset").GetString() ?? "";
            if (row.GetProperty("Ordinal").GetInt32() != i ||
                string.IsNullOrWhiteSpace(name) ||
                !sourceNames.Add(name) ||
                row.GetProperty("VirtualAliasToMaterialBindingProven").GetBoolean() ||
                string.IsNullOrWhiteSpace(sourceHeader) ||
                string.IsNullOrWhiteSpace(virtualAlias))
                throw new InvalidDataException($"PC source material row {i} changed or duplicated.");

            var known = byName.TryGetValue(name, out var found)
                ? found.ToArray() : Array.Empty<AztecPcNativeMaterialRoot>();
            var count = known.Length;
            var type = count == 0 ? "NO_EXACT_NATIVE_MATERIAL_NAME" :
                count == 1 ? "UNIQUE_EXACT_NAME_ONLY" :
                "AMBIGUOUS_EXACT_NAME_ONLY";
            results.Add(new AztecPcWorldMaterialNativeNameRow(
                i, sourceHeader, name, virtualAlias,
                row.GetProperty("AliasSurfaceCount").GetInt32(),
                row.GetProperty("AliasTriangleCount").GetInt32(),
                count, type, known,
                false, false, false,
                "The virtual alias is an ordinal hypothesis, NOT a proven " +
                "source Material name/owner. Even a unique Xbox root name " +
                "does not bind any GfxSurface pointer."));
        }

        var unique = results.Count(x => x.ExactNativeNameMatches == 1);
        var absent = results.Count(x => x.ExactNativeNameMatches == 0);
        var ambiguous = results.Count(x => x.ExactNativeNameMatches > 1);
        if (unique + absent + ambiguous != MaterialCount)
            throw new InvalidDataException("Source 39-name reconciliation arithmetic changed.");
        var result = new AztecPcWorldMaterialNativeNameReport(
            "WaW360.AztecPcWorldMaterialNativeNameReconciliation.v1",
            "AZTEC_PC_39_MATERIAL_NAMES_VS_GOLDEN_NATIVE_ROOTS_INVENTORIED",
            Path.GetFullPath(bridgeJson), Path.GetFullPath(nativeIdentityJson),
            Path.GetFullPath(atlasJson), Path.GetFullPath(nativeFile),
            GoldenNativeSha, ZoneSha,
            NativeAssetCount, xboxMaterials.Count,
            MaterialCount, SurfaceCount, TriangleCount,
            unique, absent, ambiguous,
            results.ToArray(), Array.Empty<string>(),
            false, false, false, false, false,
            "A native Xbox Material root with the same NAME is a name-only " +
            "candidate. The source GfxSurface pointers form a 39-slot virtual " +
            "lattice, not a proven pointer-to-physical-Material assignment. " +
            "NO root bindings were applied; none of the 735 original PC " +
            "surface pointers were rewritten or converted to Xbox pointers. " +
            "No native donor material graph was copied. This comparison " +
            "does not prove MaterialMemory ownership, technique/image closure, " +
            "Xbox root relocations, a loadable GfxWorld or hardware-ready FF.");

        var target = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target,
            JsonSerializer.Serialize(result,
                new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }
}

public sealed record AztecPcNativeMaterialRoot(int NativeRootIndex, string Name);

public sealed record AztecPcWorldMaterialNativeNameRow(
    int Ordinal, string SourceMaterialHeaderAt, string UnverifiedPcMaterialName,
    string UnprovenOrdinalVirtualAlias, int HypotheticalAliasSurfaceCount,
    int HypotheticalAliasTriangleCount,
    int ExactNativeNameMatches, string MatchKind,
    IReadOnlyList<AztecPcNativeMaterialRoot> NativeNameMatches,
    bool PcSourceMaterialOwnershipProven,
    bool VirtualAliasToPcMaterialProven,
    bool XboxMaterialRootBindingProven,
    string Caveat);

public sealed record AztecPcWorldMaterialNativeNameReport(
    string Schema, string Status, string PhysicalBridgeReport,
    string NativeIdentityReport, string PriorSurfaceAtlas,
    string GoldenNativeFastFile, string GoldenNativeSha256,
    string OriginalPcZoneSha256,
    int GoldenNativeRoots, int GoldenNativeMaterialRoots,
    int SourceMaterialCandidates, int SourceSurfaces, int SourceTriangles,
    int UniqueExactNameCandidates, int NoExactNameCandidates,
    int AmbiguousExactNameCandidates,
    IReadOnlyList<AztecPcWorldMaterialNativeNameRow> Rows,
    IReadOnlyList<string> UnresolvedOutsideOriginalNames,
    bool MaterialMemoryOwnershipProven,
    bool SourceVirtualToPhysicalMaterialRelocationProven,
    bool XboxMaterialBindingsProven,
    bool All735SurfaceMaterialRefsBound,
    bool XboxHardwareReady,
    string Note);
