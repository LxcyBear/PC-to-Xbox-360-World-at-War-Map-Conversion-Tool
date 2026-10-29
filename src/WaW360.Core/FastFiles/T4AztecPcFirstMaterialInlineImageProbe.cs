using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Read-only T4 PC Aztec first inline Material image-child cursor experiment.
/// Exact source boundaries ONLY. Does not infer Xbox root identity or GfxWorld
/// ownership from structural similarity.
/// </summary>
public static class T4AztecPcFirstMaterialInlineImageProbe
{
    private const int MaterialAt = 0x05731803;
    private const int TextureAt = 0x0573187C;
    private const int ImageChildAfterTextureTable = TextureAt + 6 * 16; // 0x057318DC
    private const int ImageStructBytes = 36; // 32-bit T4 GfxImage
    private const int LoadDefPrefixBytes = 16; // GfxImageLoadDef up to data[1]
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";

    public static async Task<AztecInlineImageCursorReport> ProbeAsync(
        string pcFastFile, string materialChildProof, string outputJson, CancellationToken ct = default)
    {
        using var priorDoc = JsonDocument.Parse(await File.ReadAllTextAsync(materialChildProof, ct));
        var prior = priorDoc.RootElement;
        if (prior.GetProperty("Schema").GetString() != "WaW360.AztecPcGfxWorldFirstMaterialChildren.v2" ||
            prior.GetProperty("Status").GetString() !=
                "AZTEC_PC_GFXWORLD_FIRST_MATERIAL_6X16_TEXTURE_TABLE_CANDIDATE" ||
            prior.GetProperty("FirstMaterialHeaderAt").GetString() != "0x05731803" ||
            prior.GetProperty("TextureDescriptorStride").GetInt32() != 16 ||
            prior.GetProperty("TextureImagePointerOffset").GetInt32() != 12 ||
            prior.GetProperty("BestValidTextureDescriptorCount").GetInt32() != 6 ||
            prior.GetProperty("FullyValidTextureTableSourceOffsets").EnumerateArray().Count() != 1 ||
            prior.GetProperty("FullyValidTextureTableSourceOffsets")[0].GetString() != "0x0573187C" ||
            prior.GetProperty("OriginalMaterialOwnershipProven").GetBoolean() ||
            prior.GetProperty("All39SourceNamesProven").GetBoolean() ||
            prior.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            prior.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Expected SHA-pinned 112-byte candidate and the unique 6x16 PC texture-table diagnostic.");

        var zone = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var data = zone.Bytes;
        if (zone.Platform != WaWPlatform.PC || zone.Document.Endianness != Endian.Little ||
            data.Length != 113701260 ||
            !Sha(data).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(prior.GetProperty("SourcePcFastFileSha256").GetString(), StringComparison.OrdinalIgnoreCase) ||
            U32(data, MaterialAt) != T4ZonePointer.Following ||
            U32(data, MaterialAt + 100) != T4ZonePointer.Following)
            throw new InvalidDataException("Original PC Aztec first Material source identity changed.");

        var textures = new List<AztecSourceMaterialTextureRef>();
        var followingIndexes = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            var off = TextureAt + i * 16;
            var ptr = U32(data, off + 12);
            var semantic = data[off + 7];
            if (semantic > 11 || data[off + 8] > 1)
                throw new InvalidDataException("Source texture semantic/maturity drifted.");
            if (ptr == T4ZonePointer.Following)
                followingIndexes.Add(i);
            textures.Add(new AztecSourceMaterialTextureRef(
                i, $"0x{off:X8}", semantic, $"0x{ptr:X8}",
                T4ZonePointer.Decode(ptr).ToString()));
        }
        if (!followingIndexes.SequenceEqual(new[] { 0, 1, 3 }) ||
            U32(data, TextureAt + 2 * 16 + 12) != 0x84D55F19u ||
            U32(data, TextureAt + 4 * 16 + 12) != 0x84D55F39u ||
            U32(data, TextureAt + 5 * 16 + 12) != 0x84D55F09u)
            throw new InvalidDataException("Source 3 inline / 3 packed texture reference pattern changed.");

        // GfxImage.txt: "set block TEMP", "reorder: name texture".
        // This is not necessarily a direct contiguous physical stream,
        // because the T4 loader manages block-specific alignment and
        // nested loadDef pixels. Test only the earliest 0,4,8,12,16
        // physical boundary candidates after the 6 x 16 table.
        var candidates = new List<AztecInlineImageHeaderCandidate>();
        foreach (var start in new[] {
            ImageChildAfterTextureTable, Align4(ImageChildAfterTextureTable),
            Align4(ImageChildAfterTextureTable) + 4,
            Align4(ImageChildAfterTextureTable) + 8,
            Align4(ImageChildAfterTextureTable) + 12
        }.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var mapType = U32(data, start);
            var texturePtr = U32(data, start + 4);
            var noPicmip = data[start + 10];
            var semantic = data[start + 11];
            var track = data[start + 12];
            var width = U16(data, start + 24);
            var height = U16(data, start + 26);
            var depth = U16(data, start + 28);
            var category = data[start + 30];
            var delayed = data[start + 31];
            var namePtr = U32(data, start + 32);
            var validHeader = mapType <= 5 &&
                noPicmip <= 1 && semantic <= 11 &&
                track <= 64 && width >= 1 && width <= 16384 &&
                height >= 1 && height <= 16384 &&
                depth >= 1 && depth <= 16384 &&
                category <= 7 && delayed <= 1 &&
                ValidPointer(texturePtr, zone.ZoneHeader) &&
                ValidPointer(namePtr, zone.ZoneHeader);

            var nameHits = new List<AztecInlineImageNameBoundary>();
            if (namePtr == T4ZonePointer.Following)
            {
                foreach (var nameStart in new[] { start + ImageStructBytes, Align4(start + ImageStructBytes) }.Distinct())
                {
                    if (ReadAsciiName(data, nameStart, out var name, out var afterName))
                    {
                        // If texture.loadDef is FOLLOWING, a serialized
                        // GfxImageLoadDef might follow the inline image
                        // name. Evaluate its fixed header and resource
                        // length at that bounded exact cursor; do NOT
                        // fetch/parse pixels or promote image ownership.
                        var defs = new List<AztecInlineLoadDefCandidate>();
                        foreach (var defAt in new[] { afterName, Align4(afterName) }.Distinct())
                        {
                            var mipLevels = data[defAt];
                            var flags = data[defAt + 1];
                            var dx = U16(data, defAt + 2);
                            var dy = U16(data, defAt + 4);
                            var dz = U16(data, defAt + 6);
                            var format = I32(data, defAt + 8);
                            var resource = U32(data, defAt + 12);
                            var fits = resource <= 32 * 1024 * 1024 &&
                                (long)defAt + LoadDefPrefixBytes + resource <= data.Length &&
                                mipLevels is >= 1 and <= 16 &&
                                dx >= 1 && dy >= 1 && dz >= 1 &&
                                dx <= 16384 && dy <= 16384 && dz <= 16384 &&
                                dx == width && dy == height && dz == depth;
                            defs.Add(new AztecInlineLoadDefCandidate(
                                $"0x{defAt:X8}", mipLevels, flags, dx, dy, dz,
                                format, resource, fits,
                                fits ? $"0x{(defAt + LoadDefPrefixBytes + resource):X8}" : null));
                        }
                        nameHits.Add(new AztecInlineImageNameBoundary(
                            $"0x{nameStart:X8}", name, $"0x{afterName:X8}", defs.ToArray()));
                    }
                }
            }

            candidates.Add(new AztecInlineImageHeaderCandidate(
                $"0x{start:X8}", mapType, $"0x{texturePtr:X8}",
                noPicmip, semantic, track, width, height, depth,
                category, delayed, $"0x{namePtr:X8}", validHeader,
                nameHits.ToArray(), Sha(data.AsSpan(start, ImageStructBytes))));
        }

        var plausibleHeaderCount = candidates.Count(c => c.StructurePlausible);
        var withName = candidates.Count(c => c.StructurePlausible && c.InlineNameCandidates.Count > 0);
        var complete = candidates.Count(c => c.StructurePlausible &&
            c.InlineNameCandidates.Any(n => n.LoadDefCandidates.Any(d => d.FitsImageDimensionsAndSourceBounds)));
        var status = complete == 1
            ? "AZTEC_PC_FIRST_MATERIAL_IMAGE_CHILD_INLINE_LOADDEF_CANDIDATE"
            : withName == 1
                ? "AZTEC_PC_FIRST_MATERIAL_IMAGE_CHILD_NAME_BOUNDARY_CANDIDATE"
                : plausibleHeaderCount > 0
                    ? "AZTEC_PC_FIRST_MATERIAL_IMAGE_CHILD_HEADER_CANDIDATES"
                    : "AZTEC_PC_FIRST_MATERIAL_IMAGE_CHILD_BOUNDARY_UNRESOLVED";
        var result = new AztecInlineImageCursorReport(
            "WaW360.AztecPcFirstMaterialInlineImageCursor.v1", status,
            Path.GetFullPath(pcFastFile), zone.SourceSha256, ZoneSha,
            $"0x{MaterialAt:X8}", $"0x{TextureAt:X8}",
            $"0x{ImageChildAfterTextureTable:X8}", 6, 3, 3,
            textures.ToArray(), candidates.ToArray(),
            plausibleHeaderCount, withName, complete,
            false, false, false, false,
            "Original PC source-only T4 GfxImage header (36-byte) with " +
            "reordered inline name then GfxImageLoadDef. Tests only 5 exact " +
            "post-table physical positions, 3 known FOLLOWING references. " +
            "Does NOT assert serialized GfxWorld ownership, image name " +
            "identity, 3 image decodes, constants location, 39 Material " +
            "names, Xbox binding or hardware readiness.");
        var full = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }

    private static bool ReadAsciiName(byte[] b, int start, out string name, out int after)
    {
        name = ""; after = start;
        if (start < 0 || start > b.Length - 128) return false;
        int end = start;
        while (end - start < 127 && b[end] != 0)
        {
            if (b[end] < 32 || b[end] > 126) return false;
            end++;
        }
        if (end == start || end - start >= 127 || b[end] != 0) return false;
        name = Encoding.ASCII.GetString(b, start, end - start);
        if (name.Length < 4 || !name.Any(char.IsLetter)) return false;
        after = end + 1;
        return true;
    }
    private static int Align4(int x) => (x + 3) & ~3;
    private static ushort U16(byte[] b, int i) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(i, 2));
    private static uint U32(byte[] b, int i) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i, 4));
    private static int I32(byte[] b, int i) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i, 4));
    private static string Sha(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data));
    private static bool ValidPointer(uint p, ZoneHeaderSummary header)
    {
        if (p == 0 || p is 0xFFFFFFFFu or 0xFFFFFFFEu) return true;
        var d = T4ZonePointer.Decode(p);
        return d.Kind == T4ZonePointerKind.Offset && d.Block is not null &&
            T4ZonePointer.IsWithinBlock(p, header);
    }
}
public sealed record AztecSourceMaterialTextureRef(
    int Index, string PhysicalOffset, byte Semantic,
    string SourceImagePointer, string DecodedImagePointer);
public sealed record AztecInlineLoadDefCandidate(
    string PhysicalOffset, int MipLevelCount, int Flags,
    int Width, int Height, int Depth, int Format, uint ResourceSize,
    bool FitsImageDimensionsAndSourceBounds, string? EndOfPayload);
public sealed record AztecInlineImageNameBoundary(
    string PhysicalOffset, string UnverifiedName, string AfterName,
    IReadOnlyList<AztecInlineLoadDefCandidate> LoadDefCandidates);
public sealed record AztecInlineImageHeaderCandidate(
    string PhysicalOffset, uint MapType, string TexturePointer,
    int NoPicmip, int Semantic, int Track, int Width, int Height,
    int Depth, int Category, int DelayedPixels, string NamePointer,
    bool StructurePlausible,
    IReadOnlyList<AztecInlineImageNameBoundary> InlineNameCandidates,
    string HeaderSha256);
public sealed record AztecInlineImageCursorReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string FirstMaterialHeaderAt, string FirstMaterialTextureTableAt,
    string PhysicalCursorAfterTextureTable,
    int TexturesInMaterial, int FollowingImageReferences,
    int PackedImageReferences,
    IReadOnlyList<AztecSourceMaterialTextureRef> TextureReferences,
    IReadOnlyList<AztecInlineImageHeaderCandidate> CandidateImageHeaders,
    int PlausibleHeaderCandidates, int HeaderWithInlineNameCandidates,
    int HeaderWithInlineLoadDefCandidates,
    bool ParentMaterialOwnedByGfxWorldProven,
    bool SourceMaterialNamesProven, bool XboxImageOrMaterialBindingProven,
    bool XboxHardwareReady, string Note);
