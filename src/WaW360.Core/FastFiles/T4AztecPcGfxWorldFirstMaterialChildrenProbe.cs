using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// First original-PC Aztec inline Material child-layout proof attempt.
/// Interprets the fixed 112-byte Material header and its exact known
/// name end, then tests bounded aligned destinations for THREE counted
/// child arrays. No scan, no ownership assertion, no Xbox FF emission.
/// </summary>
public static class T4AztecPcGfxWorldFirstMaterialChildrenProbe
{
    private const int World = 0x049122C0;
    private const int MaterialAt = 0x05731803;
    private const int MaterialBytes = 112;
    // T4_Assets.h: uint32 nameHash, nameStart/end/sampler/semantic/mature
    // at +4..+8, 3 bytes padding, MaterialTextureDefInfo u at +12.
    // On 32-bit T4 this is 16 bytes, NOT the previously assumed 12.
    private const int TextureStride = 16;
    private const int TextureImagePointerOffset = 12;
    private const int ConstantStride = 32;
    private const int StateBitsStride = 8;
    private const string ZoneHash =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";

    public static async Task<AztecFirstMaterialChildrenReport> ProbeAsync(
        string pcFastFile, string boundaryJson, string outputJson,
        CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(boundaryJson, ct));
        var prior = doc.RootElement;
        if (prior.GetProperty("Schema").GetString() !=
                "WaW360.AztecPcGfxWorldMaterialInlineBoundary.v2" ||
            prior.GetProperty("Status").GetString() !=
                "AZTEC_PC_GFXWORLD_FIRST_INLINE_MATERIAL_112_ABI_STRUCTURAL_CANDIDATE" ||
            prior.GetProperty("WorldMaterialMemoryArrayOwnershipProven").GetBoolean() ||
            prior.GetProperty("SourceMaterialNamesProven").GetBoolean() ||
            prior.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            prior.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Requires 112-byte original-PC Material ABI candidate with zero ownership claims.");

        var first = prior.GetProperty("Windows").EnumerateArray()
            .Single(w => w.GetProperty("WindowStart").GetString() == "0x057316CB");
        var abi = first.GetProperty("PossibleMaterialHeaders").EnumerateArray()
            .Single(h => h.GetProperty("CandidateMaterialBytes").GetInt32() == 112);
        if (!abi.GetProperty("PlausibleMaterialCountFields").GetBoolean() ||
            !abi.GetProperty("NamePointerIsFollowing").GetBoolean() ||
            !abi.GetProperty("AllDependencyPointerFieldsCompatible").GetBoolean() ||
            !abi.GetProperty("ExactPostHeaderReadableString").GetBoolean() ||
            abi.GetProperty("TextureCount").GetInt32() != 6 ||
            abi.GetProperty("ConstantCount").GetInt32() != 6 ||
            abi.GetProperty("StateBitsCount").GetInt32() != 4)
            throw new InvalidDataException("Actual first original-PC Material candidate counts or pointer proof drifted.");

        var z = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var b = z.Bytes;
        if (z.Platform != WaWPlatform.PC ||
            z.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Sha(b).Equals(ZoneHash, StringComparison.OrdinalIgnoreCase) ||
            !z.SourceSha256.Equals(
                prior.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            U32(b, World + 0x18C) != 39 ||
            U32(b, World + 0x190) != 0xFFFFFFFFu ||
            U32(b, MaterialAt) != 0xFFFFFFFFu ||
            U32(b, MaterialAt + 96) != 0x8000AF55u ||
            U32(b, MaterialAt + 100) != 0xFFFFFFFFu ||
            U32(b, MaterialAt + 104) != 0xFFFFFFFFu ||
            U32(b, MaterialAt + 108) != 0xFFFFFFFFu ||
            b[MaterialAt + 91] != 6 || b[MaterialAt + 92] != 6 ||
            b[MaterialAt + 93] != 4 || b[MaterialAt + 95] != 1)
            throw new InvalidDataException("SHA-pinned PC first Material source 112-byte header drifted.");

        var nameAt = MaterialAt + MaterialBytes;
        var nameEnd = nameAt;
        while (nameEnd - nameAt < 127 && b[nameEnd] != 0)
        {
            var c = b[nameEnd];
            if (c < 32 || c > 126)
                throw new InvalidDataException("First source Material name field contains non-printable bytes.");
            nameEnd++;
        }
        if (nameEnd - nameAt < 4 || nameEnd - nameAt >= 127 || b[nameEnd] != 0)
            throw new InvalidDataException("First source Material ASCII name boundary is invalid.");
        var unprovenName = Encoding.ASCII.GetString(b, nameAt, nameEnd - nameAt);
        if (!abi.GetProperty("NameCandidates").EnumerateArray().Any(n =>
                n.GetProperty("SourceOffset").GetString() == $"0x{nameAt:X8}" &&
                n.GetProperty("UnverifiedName").GetString() == unprovenName))
            throw new InvalidDataException("The first exact post-header source name candidate no longer matches v2.");
        var afterName = nameEnd + 1;
        var textureCount = 6;
        var constantCount = 6;
        var stateCount = 4;
        var layouts = new List<AztecFirstMaterialChildLayoutCandidate>();

        // Test the actual direct child-table sequence permitted by T4
        // Material.txt. Nested water/image child serialization could
        // interrupt it; failure here does NOT rule out the real material.
        foreach (var texAt in AlignOptions(afterName))
        {
            var tex = new List<AztecFirstMaterialTextureSample>();
            var validTextures = 0;
            for (var i = 0; i < textureCount; i++)
            {
                var off = texAt + i * TextureStride;
                var hash = U32(b, off);
                var semantic = b[off + 7];
                var mature = b[off + 8];
                var rawImage = U32(b, off + TextureImagePointerOffset);
                var valid = hash != 0 && semantic <= 11 && mature <= 1 &&
                    (rawImage == 0 || ValidPointer(rawImage, z.ZoneHeader));
                if (valid) validTextures++;
                tex.Add(new AztecFirstMaterialTextureSample(
                    i, $"0x{off:X8}", $"0x{hash:X8}", semantic,
                    $"0x{rawImage:X8}", valid, mature));
            }
            // MaterialConstantDef is explicitly 16-byte-aligned in T4.
            foreach (var conAt in AlignOptions(texAt + textureCount * TextureStride)
                         .Where(x => (x & 15) == 0))
            {
                var validConstants = 0;
                for (var i = 0; i < constantCount; i++)
                {
                    var off = conAt + i * ConstantStride;
                    var hash = U32(b, off);
                    var ascii = true;
                    for (var j = 0; j < 12; j++)
                    {
                        var c = b[off + 4 + j];
                        if (c != 0 && (c < 32 || c > 126)) ascii = false;
                    }
                    var floats = true;
                    for (var j = 0; j < 4; j++)
                    {
                        var f = BitConverter.Int32BitsToSingle(I32(b, off + 16 + j * 4));
                        if (!float.IsFinite(f) || Math.Abs(f) > 100_000_000.0f)
                            floats = false;
                    }
                    if (hash != 0 && ascii && floats) validConstants++;
                }
                foreach (var bitsAt in AlignOptions(conAt + constantCount * ConstantStride)
                             .Where(x => (x & 3) == 0))
                {
                    var nextAt = bitsAt + stateCount * StateBitsStride;
                    var nextHasNameMarker = U32(b, nextAt) == 0xFFFFFFFFu;
                    var next112CountsPlausible =
                        b[nextAt + 91] <= 64 && b[nextAt + 92] <= 64 &&
                        b[nextAt + 93] <= 64 && b[nextAt + 95] <= 4;
                    layouts.Add(new AztecFirstMaterialChildLayoutCandidate(
                        $"0x{texAt:X8}", $"0x{conAt:X8}", $"0x{bitsAt:X8}",
                        $"0x{nextAt:X8}",
                        validTextures, validConstants,
                        nextHasNameMarker, next112CountsPlausible,
                        Sha(b.AsSpan(texAt, TextureStride * textureCount)),
                        Sha(b.AsSpan(conAt, ConstantStride * constantCount)),
                        Sha(b.AsSpan(bitsAt, StateBitsStride * stateCount)),
                        tex.ToArray()));
                }
            }
        }

        var fullyCompatible = layouts.Count(x =>
            x.ValidTextureDescriptors == 6 && x.PlausibleConstantRecords == 6 &&
            x.NextMaterialHeaderNameFollowing && x.NextMaterialHeaderCountsPlausible);
        // Judge the direct six-texture descriptor table independently.
        // Inline image/water bodies can interrupt later child arrays, so
        // failure of the full 6+6+4 contiguous hypothesis does not erase
        // a valid 6x16 descriptor array.
        var fullTextureStartOffsets = layouts
            .Where(x => x.ValidTextureDescriptors == textureCount)
            .Select(x => x.TextureTableAt).Distinct().ToArray();
        var bestTextureCount = layouts.Count == 0 ? 0 :
            layouts.Max(x => x.ValidTextureDescriptors);
        var ordered = layouts
            .OrderByDescending(x => x.ValidTextureDescriptors)
            .ThenByDescending(x => x.PlausibleConstantRecords)
            .ThenByDescending(x => x.NextMaterialHeaderNameFollowing)
            .ThenByDescending(x => x.NextMaterialHeaderCountsPlausible)
            .Take(12).ToArray();
        var status = fullyCompatible == 1
            ? "AZTEC_PC_GFXWORLD_FIRST_MATERIAL_CHILD_LAYOUT_ONE_DIRECT_CANDIDATE"
            : fullyCompatible > 1
                ? "AZTEC_PC_GFXWORLD_FIRST_MATERIAL_CHILD_LAYOUT_MULTIPLE_CANDIDATES"
                : fullTextureStartOffsets.Length == 1
                    ? "AZTEC_PC_GFXWORLD_FIRST_MATERIAL_6X16_TEXTURE_TABLE_CANDIDATE"
                    : "AZTEC_PC_GFXWORLD_FIRST_MATERIAL_CHILD_LAYOUT_NOT_PROVEN";
        var report = new AztecFirstMaterialChildrenReport(
            "WaW360.AztecPcGfxWorldFirstMaterialChildren.v2", status,
            Path.GetFullPath(pcFastFile), z.SourceSha256, ZoneHash,
            $"0x{MaterialAt:X8}", MaterialBytes, $"0x{nameAt:X8}",
            unprovenName, $"0x{afterName:X8}", 6, 6, 4,
            layouts.Count, fullyCompatible, TextureStride, TextureImagePointerOffset,
            bestTextureCount, fullTextureStartOffsets, ordered,
            false, false, false, false,
            "Corrected original-PC T4 MaterialTextureDef layout: 16 bytes, " +
            "semantic at +7, isMatureContent at +8, and image/water pointer +12. " +
            "Prior 12-byte stride misread later textures, so v1 4/6 is invalid. " +
            "Tests 6x16B textures, 16-aligned 6x32B MaterialConstantDef and " +
            "4-aligned 4x8B GfxStateBits. Counted children can have inline " +
            "image/water payloads; only direct candidates were tested. " +
            "or infer a GfxWorld materialMemory pointer from serialized physical " +
            "positions. The readable name is UNVERIFIED and no Material/Xbox root " +
            "bindings or GfxWorld FF are produced.");
        var full = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        return report;
    }

    private static IEnumerable<int> AlignOptions(int offset) =>
        new[] { offset, (offset + 3) & ~3, (offset + 15) & ~15 }.Distinct();
    private static uint U32(byte[] b, int pos) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos, 4));
    private static int I32(byte[] b, int pos) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(pos, 4));
    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
    private static bool ValidPointer(uint raw, ZoneHeaderSummary zone)
    {
        if (raw == 0xFFFFFFFFu || raw == 0xFFFFFFFEu) return true;
        var d = T4ZonePointer.Decode(raw);
        return d.Kind == T4ZonePointerKind.Offset &&
            d.Block is not null && T4ZonePointer.IsWithinBlock(raw, zone);
    }
}
public sealed record AztecFirstMaterialTextureSample(
    int Index, string Offset, string NameHash, byte Semantic, string ImagePointer,
    bool Plausible, int IsMatureContent);
public sealed record AztecFirstMaterialChildLayoutCandidate(
    string TextureTableAt, string ConstantTableAt, string StateBitsTableAt,
    string NextExpectedMaterialHeaderAt,
    int ValidTextureDescriptors, int PlausibleConstantRecords,
    bool NextMaterialHeaderNameFollowing, bool NextMaterialHeaderCountsPlausible,
    string TextureArraySha256, string ConstantArraySha256, string StateBitsArraySha256,
    IReadOnlyList<AztecFirstMaterialTextureSample> TextureSamples);
public sealed record AztecFirstMaterialChildrenReport(
    string Schema, string Status, string SourcePcFastFile,
    string SourcePcFastFileSha256, string SourcePcZoneSha256,
    string FirstMaterialHeaderAt, int MaterialHeaderBytes,
    string UnverifiedNameStart, string UnverifiedName,
    string ExactPostNameOffset, int TextureCount, int ConstantCount,
    int StateBitsCount, int TestedAlignedChildLayouts, int FullyCompatibleDirectLayouts,
    int TextureDescriptorStride, int TextureImagePointerOffset,
    int BestValidTextureDescriptorCount,
    IReadOnlyList<string> FullyValidTextureTableSourceOffsets,
    IReadOnlyList<AztecFirstMaterialChildLayoutCandidate> BestCandidates,
    bool OriginalMaterialOwnershipProven, bool All39SourceNamesProven,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady, string Note);
