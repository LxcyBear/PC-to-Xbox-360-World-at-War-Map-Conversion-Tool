using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// FIRST SOURCE-ONLY T4 PC Aztec Material stream walker. Begins at an
/// independently investigated 112-byte Material header, checks all child
/// boundaries in order, and STOPS on the first unsupported layout. A
/// contiguous run is not proof that these are the 39 GfxWorld MaterialMemory
/// owners or native Xbox Material root identities.
/// </summary>
public static class T4AztecPcMaterialStreamWalker
{
    private const int FirstMaterialAt = 0x05731803;
    private const int FirstExpectedSecondAt = 0x05731AC1;
    private const int SecondExpectedThirdAt = 0x05731EA2;
    private const int NullImageMaterialAt = 0x057337C5;
    private const int NullImageAt = 0x05733869;
    private const int NullImageNameEndsAt = 0x0573389D;
    private const int AfterNullImageMaterialAt = 0x0573391D;
    private const int MaxMaterials = 39;
    private const int MaxStreamBytes = 16 * 1024 * 1024;
    private const int MaterialBytes = 112;
    private const int TextureBytes = 16;
    private const int ImageBytes = 36;
    private const int LoadDefBytes = 16;
    private const int ConstantBytes = 32;
    private const int StateBytes = 8;
    private const int MaxCount = 64;
    private const int MaxImageDataBytes = 8 * 1024 * 1024;
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";

    public static async Task<AztecPcMaterialStreamWalkReport> ProbeAsync(
        string pcFastFile, string secondMaterialReportJson, string outputJson,
        CancellationToken ct = default, string? nullImageProofJson = null)
    {
        // This optional replay is isolated from the historical 15-material
        // run. The old three-argument CLI command never enables it.
        var nullImageReplay = nullImageProofJson != null;
        if (nullImageReplay)
        {
            using var nullDoc = JsonDocument.Parse(
                await File.ReadAllTextAsync(nullImageProofJson!, ct));
            var n = nullDoc.RootElement;
            if (n.GetProperty("Schema").GetString() != "WaW360.AztecPcNullImageBoundary.v1" ||
                n.GetProperty("Status").GetString() !=
                    "AZTEC_PC_NULL_IMAGE_NAME_ONLY_EXACT_MATERIAL_CONTINUATION_CANDIDATE" ||
                n.GetProperty("PendingMaterialHeaderAt").GetString() != "0x057337C5" ||
                n.GetProperty("NullImageHeaderAt").GetString() != "0x05733869" ||
                n.GetProperty("NullImageNameAt").GetString() != "0x0573388D" ||
                n.GetProperty("NullImageName").GetString() != ",global_black_c" ||
                n.GetProperty("CandidateAfterInlineName").GetString() != "0x0573389D" ||
                n.GetProperty("NestedLoadDefProven").GetBoolean() ||
                !n.GetProperty("ExactPostNameMaterialContinuationPlausible").GetBoolean() ||
                n.GetProperty("GfxWorldMaterialMemoryOwnershipProven").GetBoolean() ||
                n.GetProperty("All39SourceMaterialNamesProven").GetBoolean() ||
                n.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
                n.GetProperty("XboxHardwareReady").GetBoolean())
                throw new InvalidDataException("Required source-only exact null-image continuation witness changed.");
            var exactRows = n.GetProperty("Candidates").EnumerateArray()
                .Where(x => x.GetProperty("DeltaAfterName").GetInt32() == 0).ToArray();
            if (exactRows.Length != 1 ||
                exactRows[0].GetProperty("ConstantTableAt").GetString() != "0x0573389D" ||
                exactRows[0].GetProperty("PlausibleConstants").GetInt32() != 2 ||
                exactRows[0].GetProperty("StateBitsAt").GetString() != "0x057338DD" ||
                exactRows[0].GetProperty("NextHeaderAt").GetString() != "0x0573391D" ||
                !exactRows[0].GetProperty("NextNameFollowingAndReadable").GetBoolean() ||
                exactRows[0].GetProperty("NextNameCandidate").GetString() != "wc/goal" ||
                !exactRows[0].GetProperty("NextCountsPlausible").GetBoolean() ||
                n.GetProperty("OriginalPcZoneSha256").GetString() != ZoneSha)
                throw new InvalidDataException("Two-constant/eight-statebit next Material anchor changed.");
        }
        using var doc = JsonDocument.Parse(
            await File.ReadAllTextAsync(secondMaterialReportJson, ct));
        var prior = doc.RootElement;
        if (prior.GetProperty("Schema").GetString() != "WaW360.AztecPcSecondMaterialChildren.v1" ||
            prior.GetProperty("Status").GetString() !=
                "AZTEC_PC_SECOND_MATERIAL_SIX_IMAGES_UNIQUE_CONSTANTS_CANDIDATE" ||
            prior.GetProperty("SecondMaterialHeaderAt").GetString() != "0x05731AC1" ||
            prior.GetProperty("SecondTextureTableAt").GetString() != "0x05731B38" ||
            !prior.GetProperty("AllSixAreImageDescriptors").GetBoolean() ||
            !prior.GetProperty("SixInlineImageChainCandidate").GetBoolean() ||
            prior.GetProperty("PostSixImagesCursor").GetString() != "0x05731DA2" ||
            prior.GetProperty("FullyPlausibleConstantTableStarts").GetInt32() != 1 ||
            prior.GetProperty("MaterialMemoryOwnershipProven").GetBoolean() ||
            prior.GetProperty("All39MaterialNamesProven").GetBoolean() ||
            prior.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            prior.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException(
                "Requires successful SHA-pinned second material child chain with no binding/ownership claims.");

        var firstAndSecond = prior.GetProperty("TopConstantTableCandidates")
            .EnumerateArray().ToArray();
        if (firstAndSecond.Length == 0 ||
            !firstAndSecond.Any(x =>
                x.GetProperty("ConstantTableAt").GetString() == "0x05731DA2" &&
                x.GetProperty("PlausibleConstants").GetInt32() == 6 &&
                x.GetProperty("StateBitsAt").GetString() == "0x05731E62" &&
                x.GetProperty("NextHeaderAt").GetString() == "0x05731EA2" &&
                x.GetProperty("NextNameFollowing").GetBoolean() &&
                x.GetProperty("NextCountsPlausible").GetBoolean()))
            throw new InvalidDataException(
                "Second material six-constant and predicted third-header evidence changed.");

        var z = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var b = z.Bytes;
        if (z.Platform != WaWPlatform.PC ||
            z.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Sha(b).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !z.SourceSha256.Equals(prior.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original-PC Aztec compressed/decompressed SHA identity mismatch.");

        var rows = new List<AztecPcWalkMaterial>();
        var cursor = FirstMaterialAt;
        var stopReason = "CAP_39_REACHED";
        var stopAt = Hex(cursor);
        for (var i = 0; i < MaxMaterials; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (cursor < FirstMaterialAt ||
                (long)cursor - FirstMaterialAt >= MaxStreamBytes ||
                (long)cursor + MaterialBytes > b.Length)
            {
                stopReason = "SOURCE_CURSOR_OUT_OF_BOUNDED_RANGE";
                stopAt = Hex(cursor);
                break;
            }

            var one = TryWalkOne(b, z.ZoneHeader, i, cursor, nullImageReplay);
            if (one.Row == null)
            {
                stopReason = one.Reason;
                stopAt = Hex(one.FaultAt);
                break;
            }
            rows.Add(one.Row);
            cursor = one.After;

            // Cross-check both independently tested material boundaries.
            // Mismatch is a hard identity regression, NOT a recoverable
            // "third Material not found" diagnosis.
            if (i == 0 && cursor != FirstExpectedSecondAt)
                throw new InvalidDataException(
                    "Reusable walker did not replay prior first Material exact end 0x05731AC1.");
            if (i == 1 && cursor != SecondExpectedThirdAt)
                throw new InvalidDataException(
                    "Reusable walker did not replay prior second Material exact end 0x05731EA2.");
            if (nullImageReplay && i == 15 && cursor != AfterNullImageMaterialAt)
                throw new InvalidDataException(
                    "Null-image replay did not end Material #15 at exactly 0x0573391D.");
            stopAt = Hex(cursor);
        }
        if (nullImageReplay && (rows.Count < 16 ||
            rows[15].HeaderAt != Hex(NullImageMaterialAt) ||
            rows[15].NextCursor != Hex(AfterNullImageMaterialAt) ||
            rows[15].Images.Count(x => x.NameOnlyNullImageCandidate) != 1))
            throw new InvalidDataException(
                "Guarded null-image replay did not preserve material #15 and its exact next header.");
        if (rows.Count < 2)
            throw new InvalidDataException(
                "Reusable decoder regressed before independently verified two-material structural chain.");

        var status = rows.Count == MaxMaterials
            ? "AZTEC_PC_SOURCE_MATERIAL_WALK_39_STRUCTURAL_CANDIDATES"
            : rows.Count > 2
                ? "AZTEC_PC_SOURCE_MATERIAL_WALK_BEYOND_TWO_CANDIDATES"
                : "AZTEC_PC_SOURCE_MATERIAL_WALK_TWO_ANCHORS_THEN_STOP";
        var report = new AztecPcMaterialStreamWalkReport(
            "WaW360.AztecPcMaterialStreamWalker.v1", status,
            Path.GetFullPath(pcFastFile), z.SourceSha256, ZoneSha,
            Hex(FirstMaterialAt), MaxMaterials, MaxStreamBytes,
            rows.Count, rows.Sum(x => x.InlineImages),
            rows.Sum(x => x.TextureCount), rows.Sum(x => x.ConstantCount),
            rows.Sum(x => x.StateBitsCount),
            stopReason, stopAt, Hex(cursor), rows.ToArray(),
            false, false, false, false,
            "Diagnostic PC-source contiguous Material/texture/image/constant/statebit " +
            "shape replay only. 112B headers; inline FOLLOWING name; 16B descriptors; " +
            "TS_WATER_MAP=11 is an unimplemented branch (never decoded as image); " +
            "ordinary inline GfxImages with INSERT texture+FOLLOWING name and DXT1/3/5 " +
            "loadDef-shaped prefixes are walked. The optional separate CLI replay " +
            "allows only the SHA-pinned MAPTYPE_NONE/null-texture/name-only child " +
            "at 0x05733869, explicitly WITHOUT a LoadDef. Counted tables using other " +
            "pointer modes, reused streams, non-inline loadDefs, unsupported " +
            "image formats and other source layouts STOP safely and are not " +
            "automatically skipped. Material names are UNVERIFIED candidates; " +
            "no GfxWorld MaterialMemory owner, 39 PC material identities, native " +
            "Xbox material binding, pixel payload, FF, or runtime proof.");

        var full = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        return report;
    }

    private static (AztecPcWalkMaterial? Row, int After, string Reason, int FaultAt)
        TryWalkOne(byte[] b, ZoneHeaderSummary block, int ordinal, int start,
            bool allowWitnessedNullImage)
    {
        (AztecPcWalkMaterial?, int, string, int) Fail(string why, int at) =>
            (null, start, why, at);

        if (!Fits(b, start, MaterialBytes))
            return Fail("MATERIAL_HEADER_OUT_OF_BOUNDS", start);

        var namePtr = U32(b, start);
        var texCount = b[start + 91];
        var constantCount = b[start + 92];
        var stateCount = b[start + 93];
        var flags = b[start + 94];
        var camera = b[start + 95];
        var technique = U32(b, start + 96);
        var texPtr = U32(b, start + 100);
        var constantPtr = U32(b, start + 104);
        var statePtr = U32(b, start + 108);
        if (namePtr != 0xFFFFFFFFu ||
            texCount == 0 || texCount > MaxCount ||
            constantCount > MaxCount || stateCount > MaxCount ||
            camera > 4 ||
            !ValidPointer(technique, block))
            return Fail("NEXT_MATERIAL_112_HEADER_SHAPE_UNRESOLVED", start);
        if (texPtr != 0xFFFFFFFFu ||
            (constantCount != 0 && constantPtr != 0xFFFFFFFFu) ||
            (stateCount != 0 && statePtr != 0xFFFFFFFFu))
            return Fail("COUNTED_CHILD_TABLE_POINTER_MODE_NOT_INLINE_FOLLOWING", start);

        var nameAt = start + MaterialBytes;
        if (!TryReadName(b, nameAt, out var name, out var afterName))
            return Fail("MATERIAL_INLINE_NAME_UNRESOLVED", nameAt);

        var textureAt = afterName;
        if (!Fits(b, textureAt, texCount * TextureBytes))
            return Fail("TEXTURE_DESCRIPTOR_TABLE_OUT_OF_BOUNDS", textureAt);

        var textures = new List<AztecPcWalkTexture>();
        var inlineCount = 0;
        for (var t = 0; t < texCount; t++)
        {
            var at = textureAt + t * TextureBytes;
            var hash = U32(b, at);
            var sem = b[at + 7];
            var mature = b[at + 8];
            var ptr = U32(b, at + 12);
            if (hash == 0 || sem > 11 || mature > 1 ||
                !ValidPointer(ptr, block))
                return Fail("TEXTURE_DESCRIPTOR_NOT_PLAUSIBLE", at);
            if (sem == 11)
                return Fail("TS_WATER_MAP_CHILD_NOT_IMPLEMENTED", at);
            if (ptr == 0xFFFFFFFEu || ptr == 0)
                return Fail("TEXTURE_IMAGE_POINTER_MODE_NOT_IMPLEMENTED", at);
            var isInline = ptr == 0xFFFFFFFFu;
            if (isInline) inlineCount++;
            textures.Add(new AztecPcWalkTexture(
                t, Hex(at), sem, Hex(ptr), isInline));
        }
        var imageAt = textureAt + texCount * TextureBytes;
        var images = new List<AztecPcWalkImage>();
        var cursor = imageAt;
        for (var j = 0; j < textures.Count; j++)
        {
            if (!textures[j].Inline)
                continue;
            if (!Fits(b, cursor, ImageBytes))
                return Fail("INLINE_IMAGE_HEADER_OUT_OF_BOUNDS", cursor);
            var mapType = U32(b, cursor);
            var imageTex = U32(b, cursor + 4);
            var imageNamePtr = U32(b, cursor + 32);
            var width = U16(b, cursor + 24);
            var height = U16(b, cursor + 26);
            var depth = U16(b, cursor + 28);
            var category = b[cursor + 30];
            // At exactly the independently proved stop, T4 MAPTYPE_NONE
            // carries a null GfxTexture with a FOLLOWING name, not a
            // 16-byte LoadDef. No other null-image instance is trusted.
            if (allowWitnessedNullImage && ordinal == 15 &&
                start == NullImageMaterialAt && cursor == NullImageAt &&
                textures[j].DescriptorIndex == 1 &&
                mapType == 0 && imageTex == 0 &&
                imageNamePtr == 0xFFFFFFFFu &&
                width == 0 && height == 0 && depth == 0 && category == 0 &&
                b[cursor + 10] <= 1 && b[cursor + 11] <= 11 &&
                b[cursor + 12] <= 64 && b[cursor + 31] <= 1)
            {
                if (!TryReadName(b, cursor + ImageBytes, out var nullName,
                        out var afterNullName) ||
                    nullName != ",global_black_c" ||
                    afterNullName != NullImageNameEndsAt)
                    return Fail("WITNESSED_NULL_IMAGE_INLINE_NAME_CHANGED", cursor);
                images.Add(new AztecPcWalkImage(
                    j, Hex(cursor), nullName, 0, 0, 0,
                    "MAPTYPE_NONE_NO_LOADDEF", 0, 0,
                    "NOT_PRESENT", Hex(afterNullName), true));
                cursor = afterNullName;
                continue;
            }
            if (mapType is < 3 or > 5 ||
                imageTex != 0xFFFFFFFEu ||
                imageNamePtr != 0xFFFFFFFFu ||
                width is < 1 or > 16384 ||
                height is < 1 or > 16384 ||
                depth is < 1 or > 16384 ||
                b[cursor + 10] > 1 || b[cursor + 11] > 11 ||
                b[cursor + 12] > 64 ||
                category > 7 || b[cursor + 31] > 1)
                return Fail("INLINE_GFXIMAGE_36_HEADER_SHAPE_UNRESOLVED", cursor);

            var imageHeader = cursor;
            if (!TryReadName(b, cursor + ImageBytes, out var imageName, out var loadDefAt))
                return Fail("INLINE_GFXIMAGE_NAME_UNRESOLVED", cursor + ImageBytes);
            if (!Fits(b, loadDefAt, LoadDefBytes))
                return Fail("GFXIMAGE_LOADDEF_16_PREFIX_OUT_OF_BOUNDS", loadDefAt);
            var ldWidth = U16(b, loadDefAt + 2);
            var ldHeight = U16(b, loadDefAt + 4);
            var ldDepth = U16(b, loadDefAt + 6);
            var format = U32(b, loadDefAt + 8);
            var size = U32(b, loadDefAt + 12);
            if (ldWidth != width || ldHeight != height || ldDepth != depth)
                return Fail("GFXIMAGE_LOADDEF_DIMENSIONS_MISMATCH", loadDefAt);
            if (format is not (0x31545844u or 0x33545844u or 0x35545844u))
                return Fail("GFXIMAGE_LOADDEF_FORMAT_NOT_DXT1_3_5", loadDefAt);
            if (size > MaxImageDataBytes ||
                !Fits(b, loadDefAt + LoadDefBytes, (int)size))
                return Fail("GFXIMAGE_LOADDEF_RESOURCE_OVER_LIMIT_OR_BOUNDS", loadDefAt);
            var end = checked(loadDefAt + LoadDefBytes + (int)size);
            if ((long)end - FirstMaterialAt > MaxStreamBytes)
                return Fail("GFXIMAGE_CHILD_STREAM_CAP_REACHED", loadDefAt);
            images.Add(new AztecPcWalkImage(
                j, Hex(imageHeader), imageName, width, height, depth,
                Hex(format), b[loadDefAt], size, Hex(loadDefAt), Hex(end)));
            cursor = end;
        }

        var constAt = cursor;
        if (!Fits(b, constAt, constantCount * ConstantBytes))
            return Fail("MATERIAL_CONSTANTS_OUT_OF_BOUNDS", constAt);
        var constants = new List<string>();
        for (var c = 0; c < constantCount; c++)
        {
            var at = constAt + c * ConstantBytes;
            var hash = U32(b, at);
            var raw = b.AsSpan(at + 4, 12).ToArray();
            if (hash == 0 || !raw.All(v => v == 0 || v is >= 32 and <= 126) ||
                !raw.Any(v => v is >= 33 and <= 126))
                return Fail("MATERIAL_CONSTANT_NAME_SHAPE_UNRESOLVED", at);
            for (var f = 0; f < 4; f++)
            {
                var n = BitConverter.Int32BitsToSingle(I32(b, at + 16 + f * 4));
                if (!float.IsFinite(n) || Math.Abs(n) > 100000000f)
                    return Fail("MATERIAL_CONSTANT_FLOAT_SHAPE_UNRESOLVED", at);
            }
            constants.Add(Encoding.ASCII.GetString(raw).TrimEnd('\0'));
        }
        cursor += constantCount * ConstantBytes;
        var stateAt = cursor;
        if (!Fits(b, stateAt, stateCount * StateBytes))
            return Fail("MATERIAL_STATES_OUT_OF_BOUNDS", stateAt);
        cursor += stateCount * StateBytes;

        return (new AztecPcWalkMaterial(
            ordinal, Hex(start), name, texCount, constantCount, stateCount,
            flags, camera, Hex(technique), Hex(nameAt),
            Hex(textureAt), Hex(imageAt), Hex(constAt), Hex(stateAt),
            Hex(cursor), inlineCount, textures.ToArray(), images.ToArray(),
            constants.ToArray(),
            Sha(b.AsSpan(start, MaterialBytes)),
            Sha(b.AsSpan(textureAt, texCount * TextureBytes)),
            Sha(b.AsSpan(constAt, constantCount * ConstantBytes)),
            Sha(b.AsSpan(stateAt, stateCount * StateBytes))),
            cursor, "", cursor);
    }

    private static bool TryReadName(byte[] b, int at, out string name, out int after)
    {
        name = "";
        after = at;
        if (!Fits(b, at, 128))
            return false;
        var p = at;
        while (p - at < 127 && b[p] != 0)
        {
            if (b[p] is < 32 or > 126)
                return false;
            p++;
        }
        if (p == at || p - at >= 127 || b[p] != 0)
            return false;
        name = Encoding.ASCII.GetString(b, at, p - at);
        if (name.Length < 4 || !name.Any(char.IsLetter))
            return false;
        after = p + 1;
        return true;
    }

    private static bool Fits(byte[] b, int offset, int count) =>
        offset >= 0 && count >= 0 && (long)offset + count <= b.LongLength;
    private static bool ValidPointer(uint raw, ZoneHeaderSummary zone)
    {
        if (raw == 0 || raw is 0xFFFFFFFFu or 0xFFFFFFFEu)
            return true;
        var p = T4ZonePointer.Decode(raw);
        return p.Kind == T4ZonePointerKind.Offset &&
            p.Block is not null && T4ZonePointer.IsWithinBlock(raw, zone);
    }
    private static string Hex(int v) => "0x" + v.ToString("X8");
    private static string Hex(uint v) => "0x" + v.ToString("X8");
    private static ushort U16(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2));
    private static uint U32(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
    private static int I32(byte[] b, int at) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at, 4));
    private static string Sha(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(SHA256.HashData(b));
}

public sealed record AztecPcWalkTexture(
    int DescriptorIndex, string Offset, int Semantic,
    string ImagePointer, bool Inline);
public sealed record AztecPcWalkImage(
    int TextureDescriptorIndex, string HeaderAt, string CandidateName,
    int Width, int Height, int Depth, string Format, int MipLevelCount,
    uint ResourceBytes, string LoadDefAt, string After,
    bool NameOnlyNullImageCandidate = false);
public sealed record AztecPcWalkMaterial(
    int Ordinal, string HeaderAt, string CandidateName,
    int TextureCount, int ConstantCount, int StateBitsCount,
    int StateFlags, int CameraRegion, string TechniqueSetPointer,
    string InlineNameAt, string TextureTableAt, string ImageChildrenStartAt,
    string ConstantTableAt, string StateBitsTableAt, string NextCursor,
    int InlineImages, IReadOnlyList<AztecPcWalkTexture> Textures,
    IReadOnlyList<AztecPcWalkImage> Images, IReadOnlyList<string> ConstantNames,
    string HeaderSha256, string TextureTableSha256,
    string ConstantTableSha256, string StateBitsSha256);
public sealed record AztecPcMaterialStreamWalkReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string StartHeader, int MaterialCap, int ByteCap,
    int DecodedMaterialCandidates, int InlineImages,
    int TextureDescriptors, int ConstantRecords, int StateBitsRecords,
    string StopReason, string StopAt, string NextCursor,
    IReadOnlyList<AztecPcWalkMaterial> Materials,
    bool GfxWorldMaterialMemoryOwnershipProven,
    bool All39SourceMaterialNamesProven,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady,
    string Note);
