using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Follows at most TWO additional original-PC Aztec inline image-header
/// candidates, starting from the SHA-pinned first image zero-payload
/// LoadDef end. Tests nearby constant/statebit layouts only after a
/// coherent three-image candidate chain. Source-only; no asset binding.
/// </summary>
public static class T4AztecPcFirstMaterialImageChainProbe
{
    private const int SecondImageAt = 0x05731937;
    private const int MaxChildImageBytes = 8 * 1024 * 1024;
    private const int ImageHeaderBytes = 36;
    private const int LoadDefPrefixBytes = 16;
    private const int ConstantCount = 6;
    private const int StateCount = 4;
    private const string ZoneSha = "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";

    public static async Task<AztecPcFirstMaterialImageChainReport> ProbeAsync(
        string pcFastFile, string firstBoundaryJson, string outputJson,
        CancellationToken ct = default)
    {
        using var priorDoc = JsonDocument.Parse(await File.ReadAllTextAsync(firstBoundaryJson, ct));
        var p = priorDoc.RootElement;
        if (p.GetProperty("Schema").GetString() != "WaW360.AztecPcFirstImageLoadDefBoundary.v1" ||
            p.GetProperty("Status").GetString() != "AZTEC_PC_FIRST_IMAGE_DXT5_ZERO_PAYLOAD_BOUNDARY_CANDIDATE" ||
            p.GetProperty("ImageHeaderAt").GetString() != "0x057318DC" ||
            p.GetProperty("LoadDefPrefixAt").GetString() != "0x05731927" ||
            p.GetProperty("FormatFourCC").GetString() != "0x35545844" ||
            p.GetProperty("MipLevelCount").GetInt32() != 0 ||
            p.GetProperty("ResourceSize").GetUInt32() != 0 ||
            p.GetProperty("CandidatePhysicalCursorAfterLoadDef").GetString() != "0x05731937" ||
            p.GetProperty("PhysicalChildOwnershipProven").GetBoolean() ||
            p.GetProperty("All39MaterialNamesProven").GetBoolean() ||
            p.GetProperty("XboxBindingsProven").GetBoolean() ||
            p.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Expected unchanged source-only first-image DXT5 zero-resource boundary.");

        var nextCandidates = p.GetProperty("BoundedNextImageCandidates").EnumerateArray().ToArray();
        var plausible = nextCandidates.Where(x => x.GetProperty("StructurallyPlausible").GetBoolean()).ToArray();
        if (plausible.Length != 1 ||
            plausible[0].GetProperty("Offset").GetString() != "0x05731937" ||
            plausible[0].GetProperty("MapType").GetUInt32() != 3 ||
            plausible[0].GetProperty("Width").GetInt32() != 512 ||
            plausible[0].GetProperty("Height").GetInt32() != 512 ||
            plausible[0].GetProperty("Depth").GetInt32() != 1 ||
            plausible[0].GetProperty("Category").GetInt32() != 3 ||
            plausible[0].GetProperty("TexturePointer").GetString() != "0xFFFFFFFE" ||
            plausible[0].GetProperty("NamePointer").GetString() != "0xFFFFFFFF")
            throw new InvalidDataException("Exactly-one plausible second-image boundary evidence changed.");

        var zone = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var b = zone.Bytes;
        if (zone.Platform != WaWPlatform.PC ||
            zone.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Sha(b).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(p.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(SecondImageAt, 96)).Equals(
                p.GetProperty("PostCursor96BytesSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC Aztec fastfile or proven 96-byte second-image window changed.");

        var images = new List<AztecPcMaterialInlineImageChainRow>();
        var cursor = SecondImageAt;
        for (var index = 2; index <= 3; index++)
        {
            ct.ThrowIfCancellationRequested();
            var child = InspectImage(b, zone.ZoneHeader, index, cursor);
            images.Add(child);
            if (!child.ImageHeaderPlausible || !child.InlineNamePlausible ||
                !child.LoadDefPrefixDimensionsMatch || !child.ResourceFitsSource ||
                child.CandidateAfterLoadDef == null)
                break;
            cursor = Convert.ToInt32(child.CandidateAfterLoadDef[2..], 16);
        }

        var threeImageChain = images.Count == 2 &&
            images.All(x => x.ImageHeaderPlausible && x.InlineNamePlausible &&
                x.LoadDefPrefixDimensionsMatch && x.ResourceFitsSource &&
                x.CandidateAfterLoadDef != null);
        var constantCandidates = new List<AztecPcMaterialPostImageConstantCandidate>();
        if (threeImageChain)
        {
            // This is a bounded HYPOTHESIS only: T4 nested image/water
            // records can alter serialized child order. No free scan.
            var starts = new[] {
                Align16(cursor), Align16(cursor + 4),
                Align16(cursor + 16), Align16(cursor + 32)
            }.Distinct().OrderBy(x => x);
            foreach (var constantAt in starts)
            {
                if (constantAt < 0 || (long)constantAt + 6 * 32 + 4 * 8 + 112 > b.Length)
                    continue;
                var good = 0;
                for (var i = 0; i < ConstantCount; i++)
                {
                    var at = constantAt + i * 32;
                    var ascii = true;
                    for (var j = 4; j < 16; j++)
                    {
                        var c = b[at + j];
                        if (c != 0 && (c < 32 || c > 126)) ascii = false;
                    }
                    var finite = true;
                    for (var j = 0; j < 4; j++)
                    {
                        var f = BitConverter.Int32BitsToSingle(I32(b, at + 16 + j * 4));
                        if (!float.IsFinite(f) || Math.Abs(f) > 100_000_000f) finite = false;
                    }
                    if (U32(b, at) != 0 && ascii && finite) good++;
                }
                var stateAt = constantAt + ConstantCount * 32;
                var nextAt = stateAt + StateCount * 8;
                constantCandidates.Add(new AztecPcMaterialPostImageConstantCandidate(
                    Hex(constantAt), good, Hex(stateAt),
                    Sha(b.AsSpan(constantAt, ConstantCount * 32)),
                    Sha(b.AsSpan(stateAt, StateCount * 8)),
                    Hex(nextAt), U32(b, nextAt) == 0xFFFFFFFFu,
                    b[nextAt + 91] <= 64 && b[nextAt + 92] <= 64 &&
                    b[nextAt + 93] <= 64 && b[nextAt + 95] <= 4));
            }
        }

        var status = threeImageChain
            ? "AZTEC_PC_FIRST_MATERIAL_THREE_IMAGE_SERIALIZATION_CHAIN_CANDIDATE"
            : images.Count > 0 && images[0].ImageHeaderPlausible
                ? "AZTEC_PC_FIRST_MATERIAL_SECOND_IMAGE_HEADER_CANDIDATE"
                : "AZTEC_PC_FIRST_MATERIAL_NEXT_IMAGE_CHAIN_UNRESOLVED";
        var report = new AztecPcFirstMaterialImageChainReport(
            "WaW360.AztecPcFirstMaterialImageChain.v1",
            status, Path.GetFullPath(pcFastFile), zone.SourceSha256, ZoneSha,
            "0x057318DC", Hex(SecondImageAt), 3,
            images.ToArray(), threeImageChain,
            threeImageChain ? Hex(cursor) : null,
            constantCandidates.ToArray(),
            false, false, false, false,
            "First image header + zero-level/zero-byte DXT5-shaped LoadDef and " +
            "second header boundary are prior structural candidates. We inspect " +
            "at most two further image headers, their FOLLOWING names and exact " +
            "16-byte LoadDef prefixes. Calculated image ends and nearby 16-aligned " +
            "constant/statebit layouts are HYPOTHESES, not ownership proof. " +
            "No source material names promoted, no Xbox binding, FF or pixels.");
        var full = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        return report;
    }

    private static AztecPcMaterialInlineImageChainRow InspectImage(
        byte[] b, ZoneHeaderSummary header, int index, int at)
    {
        var defaultRow = new AztecPcMaterialInlineImageChainRow(
            index, Hex(at), false, 0, 0, 0, 0, -1, "",
            "", false, null, null, false, null,
            false, false, null, "", 0, 0, 0, 0, 0);
        if (at < 0 || (long)at + ImageHeaderBytes >= b.Length)
            return defaultRow;

        var mapType = U32(b, at);
        var texture = U32(b, at + 4);
        var namePtr = U32(b, at + 32);
        var width = U16(b, at + 24);
        var height = U16(b, at + 26);
        var depth = U16(b, at + 28);
        var category = b[at + 30];
        var plausible = mapType <= 5 &&
            b[at + 10] <= 1 && b[at + 11] <= 11 &&
            b[at + 12] <= 64 && category <= 7 && b[at + 31] <= 1 &&
            width is >= 1 and <= 16384 &&
            height is >= 1 and <= 16384 &&
            depth is >= 1 and <= 16384 &&
            ValidPointer(texture, header) && ValidPointer(namePtr, header);
        if (!plausible)
            return defaultRow with {
                MapType = mapType, Width = width, Height = height,
                Depth = depth, Category = category,
                TexturePointer = Hex(texture), NamePointer = Hex(namePtr)
            };
        var row = defaultRow with {
            ImageHeaderPlausible = true, MapType = mapType,
            Width = width, Height = height, Depth = depth,
            Category = category, TexturePointer = Hex(texture),
            NamePointer = Hex(namePtr)
        };

        // A FOLLOWING image-name pointer is needed to advance in this
        // tiny source-only loader-order experiment.
        var nameAt = at + ImageHeaderBytes;
        if (namePtr != 0xFFFFFFFFu || !TryReadAsciiName(b, nameAt, out var name, out var afterName))
            return row;
        row = row with {
            InlineNamePlausible = true, CandidateName = name,
            NameAt = Hex(nameAt), CandidateLoadDefAt = Hex(afterName)
        };
        if ((long)afterName + LoadDefPrefixBytes > b.Length)
            return row;
        var mip = b[afterName];
        var flags = b[afterName + 1];
        var dw = U16(b, afterName + 2);
        var dh = U16(b, afterName + 4);
        var dd = U16(b, afterName + 6);
        var format = U32(b, afterName + 8);
        var resource = U32(b, afterName + 12);
        var dimsMatch = dw == width && dh == height && dd == depth;
        var resourceFits = resource <= MaxChildImageBytes &&
            (long)afterName + LoadDefPrefixBytes + resource <= b.Length;
        var formatRecognized = format is 0x31545844u or 0x33545844u or 0x35545844u;
        // No mipCount >= 1 requirement: we only observe whether the
        // shape yields a coherent physical boundary, not runtime validity.
        return row with {
            LoadDefPrefixDimensionsMatch = dimsMatch,
            ResourceFitsSource = resourceFits,
            CandidateAfterLoadDef = dimsMatch && resourceFits && texture == 0xFFFFFFFEu
                ? Hex(checked(afterName + LoadDefPrefixBytes + (int)resource))
                : null,
            FormatFourCC = Hex(format), MipLevelCount = mip,
            LoadDefFlags = flags, LoadDefResourceBytes = resource,
            FormatIsDXT1DXT3OrDXT5 = formatRecognized
        };
    }

    private static bool TryReadAsciiName(byte[] b, int at, out string name, out int after)
    {
        name = "";
        after = at;
        if (at < 0 || at > b.Length - 128) return false;
        var end = at;
        while (end - at < 127 && b[end] != 0)
        {
            if (b[end] is < 32 or > 126) return false;
            end++;
        }
        if (end - at < 4 || end - at >= 127 || b[end] != 0)
            return false;
        name = Encoding.ASCII.GetString(b, at, end - at);
        if (!name.Any(char.IsLetter)) return false;
        after = end + 1;
        return true;
    }

    private static bool ValidPointer(uint raw, ZoneHeaderSummary header)
    {
        if (raw is 0 or 0xFFFFFFFFu or 0xFFFFFFFEu) return true;
        var p = T4ZonePointer.Decode(raw);
        return p.Kind == T4ZonePointerKind.Offset && p.Block is not null &&
            T4ZonePointer.IsWithinBlock(raw, header);
    }
    private static int Align16(int v) => (v + 15) & ~15;
    private static string Hex(int v) => "0x" + v.ToString("X8");
    private static string Hex(uint v) => "0x" + v.ToString("X8");
    private static string Sha(ReadOnlySpan<byte> b) => Convert.ToHexString(SHA256.HashData(b));
    private static int I32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at, 4));
    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
    private static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2));
}

public sealed record AztecPcMaterialInlineImageChainRow(
    int ImageIndex, string HeaderAt, bool ImageHeaderPlausible,
    uint MapType, int Width, int Height, int Depth, int Category,
    string TexturePointer, string NamePointer,
    bool InlineNamePlausible, string? CandidateName, string? NameAt,
    bool LoadDefPrefixDimensionsMatch, string? CandidateLoadDefAt,
    bool ResourceFitsSource, bool FormatIsDXT1DXT3OrDXT5,
    string? CandidateAfterLoadDef, string FormatFourCC,
    int MipLevelCount, int LoadDefFlags, uint LoadDefResourceBytes);

public sealed record AztecPcMaterialPostImageConstantCandidate(
    string ConstantTableAt, int PlausibleConstants,
    string StateBitsTableAt, string ConstantArraySha256,
    string StateBitsArraySha256, string HypotheticalNextMaterialAt,
    bool NextNameFollowing, bool NextCountsPlausible);

public sealed record AztecPcFirstMaterialImageChainReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string FirstImageHeaderAt, string SecondImageHeaderAt,
    int ExpectedFollowingImageReferences,
    IReadOnlyList<AztecPcMaterialInlineImageChainRow> AdditionalImageCandidates,
    bool ThreeImageSerializationChainCandidate,
    string? HypotheticalPostThirdImageCursor,
    IReadOnlyList<AztecPcMaterialPostImageConstantCandidate> NearbyAlignedConstantTableCandidates,
    bool MaterialMemoryOwnershipProven, bool All39SourceNamesProven,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady, string Note);
