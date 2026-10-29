using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Read-only, bounded audit of the first Aztec PC image LoadDef candidate.
/// LevelCount=0 and ResourceSize=0 are reported as observations, NOT
/// interpreted as valid pixels, proven image ownership, or Xbox bindings.
/// </summary>
public static class T4AztecPcFirstImageLoadDefBoundaryProbe
{
    private const int ImageAt = 0x057318DC;
    private const int LoadDefAt = 0x05731927;
    private const int LoadDefHeaderBytes = 16;
    private const int LookAheadBytes = 96;
    private const string ZoneSha = "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";

    public static async Task<AztecFirstImageLoadDefBoundaryReport> ProbeAsync(
        string pcFastFile, string imageCursorJson, string outputJson,
        CancellationToken ct = default)
    {
        using var priorDoc = JsonDocument.Parse(await File.ReadAllTextAsync(imageCursorJson, ct));
        var prior = priorDoc.RootElement;
        if (prior.GetProperty("Schema").GetString() != "WaW360.AztecPcFirstMaterialInlineImageCursor.v1" ||
            prior.GetProperty("Status").GetString() != "AZTEC_PC_FIRST_MATERIAL_IMAGE_CHILD_NAME_BOUNDARY_CANDIDATE" ||
            prior.GetProperty("FirstMaterialHeaderAt").GetString() != "0x05731803" ||
            prior.GetProperty("FirstMaterialTextureTableAt").GetString() != "0x0573187C" ||
            prior.GetProperty("PhysicalCursorAfterTextureTable").GetString() != "0x057318DC" ||
            prior.GetProperty("FollowingImageReferences").GetInt32() != 3 ||
            prior.GetProperty("PackedImageReferences").GetInt32() != 3 ||
            prior.GetProperty("ParentMaterialOwnedByGfxWorldProven").GetBoolean() ||
            prior.GetProperty("SourceMaterialNamesProven").GetBoolean() ||
            prior.GetProperty("XboxImageOrMaterialBindingProven").GetBoolean() ||
            prior.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Requires original first PC material inline image cursor with no ownership claims.");

        var plausible = prior.GetProperty("CandidateImageHeaders").EnumerateArray()
            .Where(x => x.GetProperty("StructurePlausible").GetBoolean()).ToArray();
        if (plausible.Length != 1 || plausible[0].GetProperty("PhysicalOffset").GetString() != "0x057318DC" ||
            plausible[0].GetProperty("TexturePointer").GetString() != "0xFFFFFFFE" ||
            plausible[0].GetProperty("NamePointer").GetString() != "0xFFFFFFFF" ||
            plausible[0].GetProperty("Category").GetInt32() != 3 ||
            plausible[0].GetProperty("Width").GetInt32() != 512 ||
            plausible[0].GetProperty("Height").GetInt32() != 512 ||
            plausible[0].GetProperty("Depth").GetInt32() != 1)
            throw new InvalidDataException("First image candidate changed.");

        var names = plausible[0].GetProperty("InlineNameCandidates").EnumerateArray().ToArray();
        if (names.Length != 1 ||
            names[0].GetProperty("PhysicalOffset").GetString() != "0x05731900" ||
            names[0].GetProperty("AfterName").GetString() != "0x05731927")
            throw new InvalidDataException("Exact first image inline name boundary changed.");

        var defs = names[0].GetProperty("LoadDefCandidates").EnumerateArray().ToArray();
        if (defs.Length != 2 ||
            !defs.Any(x => x.GetProperty("PhysicalOffset").GetString() == "0x05731927" &&
                           x.GetProperty("MipLevelCount").GetInt32() == 0 &&
                           x.GetProperty("Width").GetInt32() == 512 &&
                           x.GetProperty("Height").GetInt32() == 512 &&
                           x.GetProperty("Depth").GetInt32() == 1 &&
                           x.GetProperty("ResourceSize").GetUInt32() == 0))
            throw new InvalidDataException("Expected 0-mip, 0-resource candidate not present in prior report.");

        var zone = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var b = zone.Bytes;
        if (zone.Platform != WaWPlatform.PC ||
            zone.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Convert.ToHexString(SHA256.HashData(b)).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(prior.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            U32(b, ImageAt) != 3 ||
            U32(b, ImageAt + 4) != 0xFFFFFFFEu ||
            U32(b, ImageAt + 32) != 0xFFFFFFFFu ||
            b[ImageAt + 30] != 3)
            throw new InvalidDataException("Original-PC source image identity or SHA has changed.");

        var level = b[LoadDefAt];
        var flags = b[LoadDefAt + 1];
        var width = U16(b, LoadDefAt + 2);
        var height = U16(b, LoadDefAt + 4);
        var depth = U16(b, LoadDefAt + 6);
        var format = U32(b, LoadDefAt + 8);
        var resource = U32(b, LoadDefAt + 12);
        if (level != 0 || width != 512 || height != 512 || depth != 1 ||
            format != 0x35545844u || resource != 0)
            throw new InvalidDataException("Exact DXT5-shaped zero-payload LoadDef prefix changed.");

        // GfxImageLoadDef::data starts at byte +16. This is a
        // PHYSICAL SOURCE CURSOR HYPOTHESIS, not proof the image owns
        // the following bytes. No pixel payload is read or generated.
        var after = checked(LoadDefAt + LoadDefHeaderBytes + (int)resource);
        var candidateOffsets = new[] {
            after, Align4(after), Align16(after), after + 4, after + 8, after + 16
        }.Distinct().OrderBy(x => x).ToArray();
        var next = new List<AztecPostLoadDefImageCandidate>();
        foreach (var pos in candidateOffsets)
        {
            if (pos < 0 || pos + 36 > b.Length) continue;
            var mapType = U32(b, pos);
            var texturePtr = U32(b, pos + 4);
            var namePtr = U32(b, pos + 32);
            var iw = U16(b, pos + 24);
            var ih = U16(b, pos + 26);
            var id = U16(b, pos + 28);
            var category = b[pos + 30];
            var plausibleHeader = mapType is >= 3 and <= 5 &&
                iw is >= 1 and <= 16384 && ih is >= 1 and <= 16384 &&
                id is >= 1 and <= 16384 &&
                b[pos + 10] <= 1 && b[pos + 11] <= 11 && b[pos + 12] <= 64 &&
                category <= 7 && b[pos + 31] <= 1 &&
                ValidPointer(texturePtr, zone.ZoneHeader) &&
                ValidPointer(namePtr, zone.ZoneHeader);
            next.Add(new AztecPostLoadDefImageCandidate(
                "0x" + pos.ToString("X8"), plausibleHeader, mapType,
                iw, ih, id, category, "0x" + texturePtr.ToString("X8"),
                "0x" + namePtr.ToString("X8")));
        }

        var lookAhead = b.AsSpan(after, LookAheadBytes);
        var output = new AztecFirstImageLoadDefBoundaryReport(
            "WaW360.AztecPcFirstImageLoadDefBoundary.v1",
            "AZTEC_PC_FIRST_IMAGE_DXT5_ZERO_PAYLOAD_BOUNDARY_CANDIDATE",
            Path.GetFullPath(pcFastFile), zone.SourceSha256, ZoneSha,
            "0x" + ImageAt.ToString("X8"), "0x" + LoadDefAt.ToString("X8"),
            level, flags, width, height, depth,
            "0x" + format.ToString("X8"), resource, true,
            "0x" + after.ToString("X8"),
            Convert.ToHexString(SHA256.HashData(lookAhead)), Convert.ToHexString(lookAhead),
            next.ToArray(),
            false, false, false, false,
            "T4 category 3 is LOAD_FROM_FILE; image texture pointer INSERT and inline " +
            "name FOLLOWING. 0-mip/0-byte DXT5-shaped 16-byte prefix is observed, " +
            "NOT proof that an empty LoadDef is valid or that physical ownership ends " +
            "at the candidate cursor. Six bounded next-header hypotheses only. " +
            "No image pixels, source Material ownership, 39 names, Xbox bindings or FF.");
        var full = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full,
            JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }), ct);
        return output;
    }

    private static int Align4(int x) => (x + 3) & ~3;
    private static int Align16(int x) => (x + 15) & ~15;
    private static ushort U16(byte[] b, int pos) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pos, 2));
    private static uint U32(byte[] b, int pos) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos, 4));
    private static bool ValidPointer(uint raw, ZoneHeaderSummary header)
    {
        if (raw is 0 or 0xFFFFFFFFu or 0xFFFFFFFEu) return true;
        var p = T4ZonePointer.Decode(raw);
        return p.Kind == T4ZonePointerKind.Offset && p.Block is not null &&
               T4ZonePointer.IsWithinBlock(raw, header);
    }
}

public sealed record AztecPostLoadDefImageCandidate(
    string Offset, bool StructurallyPlausible, uint MapType,
    int Width, int Height, int Depth, int Category,
    string TexturePointer, string NamePointer);

public sealed record AztecFirstImageLoadDefBoundaryReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string ImageHeaderAt, string LoadDefPrefixAt,
    int MipLevelCount, int Flags, int Width, int Height, int Depth,
    string FormatFourCC, uint ResourceSize, bool ZeroPayload,
    string CandidatePhysicalCursorAfterLoadDef,
    string PostCursor96BytesSha256, string PostCursor96BytesHex,
    IReadOnlyList<AztecPostLoadDefImageCandidate> BoundedNextImageCandidates,
    bool PhysicalChildOwnershipProven, bool All39MaterialNamesProven,
    bool XboxBindingsProven, bool XboxHardwareReady, string Note);
