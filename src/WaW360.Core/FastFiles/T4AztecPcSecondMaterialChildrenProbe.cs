using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Source-only bounded continuation of the original-PC Aztec second
/// structurally plausible 112-byte Material. Material.txt distinguishes
/// TS_WATER_MAP (11) from ordinary image pointers; do not interpret a
/// water_t union as GfxImage. No asset ownership, relocations, or FF.
/// </summary>
public static class T4AztecPcSecondMaterialChildrenProbe
{
    private const int HeaderAt = 0x05731AC1;
    private const int TextureAt = 0x05731B38;
    private const int HeaderBytes = 112;
    private const int TextureStride = 16;
    private const int ConstantStride = 32;
    private const int StateBitsStride = 8;
    private const int ImageBytes = 36;
    private const int LoadDefBytes = 16;
    private const int MaxImageResourceBytes = 32 * 1024 * 1024;
    private const string ZoneSha = "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";

    public static async Task<AztecSecondMaterialChildrenReport> ProbeAsync(
        string pcFastFile, string priorJson, string outputJson,
        CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(priorJson, ct));
        var p = doc.RootElement;
        if (p.GetProperty("Schema").GetString() != "WaW360.AztecPcFirstMaterialStateNextHeader.v1" ||
            p.GetProperty("Status").GetString() !=
                "AZTEC_PC_FIRST_MATERIAL_STATEBITS_SECOND_HEADER_SHAPE_CANDIDATE" ||
            p.GetProperty("FirstConstantTableAt").GetString() != "0x057319E1" ||
            p.GetProperty("FirstStateBitsTableAt").GetString() != "0x05731AA1" ||
            p.GetProperty("SecondMaterialHeaderAt").GetString() != "0x05731AC1" ||
            !p.GetProperty("SecondHeaderShapePlausible").GetBoolean() ||
            p.GetProperty("SecondNamePointer").GetString() != "0xFFFFFFFF" ||
            p.GetProperty("SecondUnverifiedName").GetString() != "*3n_4n" ||
            p.GetProperty("SecondAfterName").GetString() != "0x05731B38" ||
            p.GetProperty("SecondTextureCount").GetInt32() != 6 ||
            p.GetProperty("SecondConstantCount").GetInt32() != 6 ||
            p.GetProperty("SecondStateBitsCount").GetInt32() != 8 ||
            !p.GetProperty("SecondPointerFieldsCompatible").GetBoolean() ||
            p.GetProperty("WorldMaterialMemoryOwnershipProven").GetBoolean() ||
            p.GetProperty("All39MaterialNamesProven").GetBoolean() ||
            p.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            p.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Requires original-PC second Material structural boundary with no Xbox claims.");

        var candidates = p.GetProperty("SecondTextureCandidates").EnumerateArray().ToArray();
        var exact = candidates.Where(t =>
            t.GetProperty("SourceOffset").GetString() == "0x05731B38").ToArray();
        if (exact.Length != 1 ||
            exact[0].GetProperty("TextureCount").GetInt32() != 6 ||
            exact[0].GetProperty("PlausibleDescriptors").GetInt32() != 6 ||
            exact[0].GetProperty("FollowingImagePointers").GetInt32() != 6)
            throw new InvalidDataException("Exactly six source FOLLOWING texture descriptors required.");

        var z = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var b = z.Bytes;
        if (z.Platform != WaWPlatform.PC ||
            z.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Sha(b).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !z.SourceSha256.Equals(p.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            U32(b, HeaderAt) != 0xFFFFFFFFu ||
            b[HeaderAt + 91] != 6 || b[HeaderAt + 92] != 6 ||
            b[HeaderAt + 93] != 8 ||
            !Sha(b.AsSpan(TextureAt, 6 * TextureStride)).Equals(
                exact[0].GetProperty("Sha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC source zone, second Material header or descriptor array SHA changed.");

        var textures = new List<AztecSecondMaterialTexture>();
        var allImageDescriptors = true;
        for (var i = 0; i < 6; i++)
        {
            var at = TextureAt + i * TextureStride;
            var semantic = b[at + 7];
            var mature = b[at + 8];
            var pointer = U32(b, at + 12);
            var isWater = semantic == 11;
            var plausible = U32(b, at) != 0 && semantic <= 11 &&
                mature <= 1 && pointer == 0xFFFFFFFFu;
            if (!plausible || isWater) allImageDescriptors = false;
            textures.Add(new AztecSecondMaterialTexture(
                i, Hex(at), semantic, mature, Hex(pointer), isWater, plausible));
        }

        var images = new List<AztecSecondMaterialImage>();
        var cursor = TextureAt + 6 * TextureStride;
        if (allImageDescriptors)
        {
            for (var i = 0; i < 6; i++)
            {
                ct.ThrowIfCancellationRequested();
                var image = DecodeImageCandidate(b, z.ZoneHeader, i, cursor);
                images.Add(image);
                if (!image.HeaderPlausible || !image.NamePlausible ||
                    !image.LoadDefDimensionsMatch || !image.ResourceFits ||
                    image.AfterLoadDef == null)
                    break;
                cursor = Convert.ToInt32(image.AfterLoadDef[2..], 16);
            }
        }
        var completeSixImages = images.Count == 6 &&
            images.All(i => i.HeaderPlausible && i.NamePlausible &&
                i.LoadDefDimensionsMatch && i.ResourceFits &&
                i.AfterLoadDef != null);

        var constantCandidates = new List<AztecSecondMaterialConstantCandidate>();
        if (completeSixImages)
        {
            // Source-relative bounded offsets. Do not assume decompressed
            // absolute alignment equals a Zone stream's alloc alignment.
            for (var delta = 0; delta < 32; delta++)
            {
                var at = cursor + delta;
                if (at < 0 || (long)at + 6 * ConstantStride +
                    8 * StateBitsStride + HeaderBytes > b.Length)
                    continue;
                var valid = 0;
                var names = new List<string>();
                for (var j = 0; j < 6; j++)
                {
                    var off = at + j * ConstantStride;
                    var nameBytes = b.AsSpan(off + 4, 12).ToArray();
                    var ascii = nameBytes.All(x => x == 0 || (x >= 32 && x <= 126));
                    var printable = nameBytes.Any(x => x >= 33 && x <= 126);
                    var finite = true;
                    for (var k = 0; k < 4; k++)
                    {
                        var f = BitConverter.Int32BitsToSingle(I32(b, off + 16 + k * 4));
                        if (!float.IsFinite(f) || Math.Abs(f) > 100000000f)
                            finite = false;
                    }
                    var name = ascii && printable
                        ? Encoding.ASCII.GetString(nameBytes).TrimEnd('\0') : "";
                    names.Add(name);
                    if (U32(b, off) != 0 && ascii && printable && finite)
                        valid++;
                }
                var stateAt = at + 6 * ConstantStride;
                var nextAt = stateAt + 8 * StateBitsStride;
                var nextName = U32(b, nextAt) == 0xFFFFFFFFu;
                var nextCounts = b[nextAt + 91] <= 64 &&
                    b[nextAt + 92] <= 64 &&
                    b[nextAt + 93] <= 64 && b[nextAt + 95] <= 4;
                constantCandidates.Add(new AztecSecondMaterialConstantCandidate(
                    Hex(at), delta, valid, names,
                    Hex(stateAt), Sha(b.AsSpan(stateAt, 8 * StateBitsStride)),
                    Hex(nextAt), nextName, nextCounts));
            }
        }
        var ordered = constantCandidates
            .OrderByDescending(x => x.PlausibleConstants)
            .ThenByDescending(x => x.NextNameFollowing && x.NextCountsPlausible)
            .ThenBy(x => x.Delta).Take(12).ToArray();
        var sixOfSix = constantCandidates.Count(x => x.PlausibleConstants == 6);
        var status = completeSixImages && sixOfSix == 1
            ? "AZTEC_PC_SECOND_MATERIAL_SIX_IMAGES_UNIQUE_CONSTANTS_CANDIDATE"
            : completeSixImages
                ? "AZTEC_PC_SECOND_MATERIAL_SIX_IMAGES_SOURCE_CHAIN_CANDIDATE"
                : images.Count > 0 && images[0].HeaderPlausible
                    ? "AZTEC_PC_SECOND_MATERIAL_PARTIAL_IMAGE_CHAIN_CANDIDATE"
                    : "AZTEC_PC_SECOND_MATERIAL_CHILD_CHAIN_UNRESOLVED";
        var result = new AztecSecondMaterialChildrenReport(
            "WaW360.AztecPcSecondMaterialChildren.v1", status,
            Path.GetFullPath(pcFastFile), z.SourceSha256, ZoneSha,
            Hex(HeaderAt), Hex(TextureAt), textures.ToArray(),
            allImageDescriptors, images.ToArray(), completeSixImages,
            completeSixImages ? Hex(cursor) : null,
            constantCandidates.Count, sixOfSix, ordered,
            false, false, false, false,
            "Source-only continuation from a SHAPE candidate at 0x05731AC1. " +
            "Texture semantic 11 is water_t, never decoded as GfxImage. " +
            "Traverses up to six physical inline image/name/loadDef candidates, " +
            "then at most 32 nearby physical six-constant + eight-statebit " +
            "hypotheses. Loader block alignment, ownership, material root " +
            "identity and Xbox dependencies remain UNPROVEN. NO FF.");
        var output = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }

    private static AztecSecondMaterialImage DecodeImageCandidate(
        byte[] b, ZoneHeaderSummary block, int index, int at)
    {
        var fail = new AztecSecondMaterialImage(
            index, Hex(at), false, 0, 0, 0, 0, 0,
            "", "", false, null, null, null,
            false, false, 0, 0, "", 0, null);
        if (at < 0 || (long)at + ImageBytes + 128 > b.Length) return fail;
        var mapType = U32(b, at);
        var texture = U32(b, at + 4);
        var namePtr = U32(b, at + 32);
        var width = U16(b, at + 24);
        var height = U16(b, at + 26);
        var depth = U16(b, at + 28);
        var category = b[at + 30];
        var plausible = mapType is >= 3 and <= 5 &&
            width is >= 1 and <= 16384 && height is >= 1 and <= 16384 &&
            depth is >= 1 and <= 16384 &&
            b[at + 10] <= 1 && b[at + 11] <= 11 && b[at + 12] <= 64 &&
            category <= 7 && b[at + 31] <= 1 &&
            ValidPointer(texture, block) && ValidPointer(namePtr, block);
        var row = fail with {
            HeaderPlausible = plausible, MapType = mapType,
            Width = width, Height = height, Depth = depth, Category = category,
            TexturePointer = Hex(texture), NamePointer = Hex(namePtr)
        };
        if (!plausible || namePtr != 0xFFFFFFFFu ||
            !TryReadName(b, at + ImageBytes, out var name, out var afterName))
            return row;
        row = row with {
            NamePlausible = true, NameCandidate = name,
            NameAt = Hex(at + ImageBytes), LoadDefAt = Hex(afterName)
        };
        if ((long)afterName + LoadDefBytes > b.Length) return row;
        var mip = b[afterName];
        var flags = b[afterName + 1];
        var dx = U16(b, afterName + 2);
        var dy = U16(b, afterName + 4);
        var dz = U16(b, afterName + 6);
        var format = U32(b, afterName + 8);
        var resource = U32(b, afterName + 12);
        var dimensionsMatch = dx == width && dy == height && dz == depth;
        var resourceFits = resource <= MaxImageResourceBytes &&
            (long)afterName + LoadDefBytes + resource <= b.Length;
        return row with {
            LoadDefDimensionsMatch = dimensionsMatch, ResourceFits = resourceFits,
            MipLevelCount = mip, LoadDefFlags = flags,
            FormatFourCC = Hex(format), ResourceSize = resource,
            AfterLoadDef = texture == 0xFFFFFFFEu && dimensionsMatch && resourceFits
                ? Hex(checked(afterName + LoadDefBytes + (int)resource))
                : null
        };
    }

    private static bool TryReadName(byte[] b, int at, out string name, out int after)
    {
        name = "";
        after = at;
        if (at < 0 || at >= b.Length - 128) return false;
        var pos = at;
        while (pos - at < 127 && b[pos] != 0)
        {
            if (b[pos] is < 32 or > 126) return false;
            pos++;
        }
        if (pos == at || pos - at >= 127 || b[pos] != 0) return false;
        name = Encoding.ASCII.GetString(b, at, pos - at);
        if (name.Length < 4 || !name.Any(char.IsLetter)) return false;
        after = pos + 1;
        return true;
    }
    private static bool ValidPointer(uint value, ZoneHeaderSummary block)
    {
        if (value is 0 or 0xFFFFFFFFu or 0xFFFFFFFEu) return true;
        var p = T4ZonePointer.Decode(value);
        return p.Kind == T4ZonePointerKind.Offset && p.Block is not null &&
            T4ZonePointer.IsWithinBlock(value, block);
    }
    private static ushort U16(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2));
    private static uint U32(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
    private static int I32(byte[] b, int at) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at, 4));
    private static string Hex(int n) => "0x" + n.ToString("X8");
    private static string Hex(uint n) => "0x" + n.ToString("X8");
    private static string Sha(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(SHA256.HashData(b));
}

public sealed record AztecSecondMaterialTexture(
    int Index, string Offset, int Semantic, int IsMature, string ImagePointer,
    bool IsWater, bool DescriptorPlausible);

public sealed record AztecSecondMaterialImage(
    int Index, string HeaderAt, bool HeaderPlausible,
    uint MapType, int Width, int Height, int Depth, int Category,
    string TexturePointer, string NamePointer,
    bool NamePlausible, string? NameCandidate, string? NameAt, string? LoadDefAt,
    bool LoadDefDimensionsMatch, bool ResourceFits,
    int MipLevelCount, int LoadDefFlags,
    string FormatFourCC, uint ResourceSize, string? AfterLoadDef);

public sealed record AztecSecondMaterialConstantCandidate(
    string ConstantTableAt, int Delta, int PlausibleConstants,
    IReadOnlyList<string> CandidateNames, string StateBitsAt,
    string StateBitsSha256, string NextHeaderAt,
    bool NextNameFollowing, bool NextCountsPlausible);

public sealed record AztecSecondMaterialChildrenReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string SecondMaterialHeaderAt, string SecondTextureTableAt,
    IReadOnlyList<AztecSecondMaterialTexture> Textures,
    bool AllSixAreImageDescriptors,
    IReadOnlyList<AztecSecondMaterialImage> InlineImageCandidates,
    bool SixInlineImageChainCandidate, string? PostSixImagesCursor,
    int TestedConstantTableStarts, int FullyPlausibleConstantTableStarts,
    IReadOnlyList<AztecSecondMaterialConstantCandidate> TopConstantTableCandidates,
    bool MaterialMemoryOwnershipProven, bool All39MaterialNamesProven,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady, string Note);
