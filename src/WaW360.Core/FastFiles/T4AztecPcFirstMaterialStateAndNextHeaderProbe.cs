using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// SHA-pinned original-PC Aztec first Material statebits and second Material
/// header structural continuation. Read-only: no absolute name/pointer identity,
/// no MaterialMemory ownership, no Xbox graft or FF generation.
/// </summary>
public static class T4AztecPcFirstMaterialStateAndNextHeaderProbe
{
    private const int ConstantsAt = 0x057319E1;
    private const int ConstantsBytes = 6 * 32;
    private const int StateBitsAt = ConstantsAt + ConstantsBytes; // 0x05731AA1
    private const int StateBitsBytes = 4 * 8;
    private const int NextHeaderAt = StateBitsAt + StateBitsBytes; // 0x05731AC1
    private const int MaterialHeaderBytes = 112;
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";

    public static async Task<AztecPcStateNextHeaderReport> ProbeAsync(
        string pcFastFile, string postImageConstantsJson, string outputJson,
        CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(
            await File.ReadAllTextAsync(postImageConstantsJson, ct));
        var p = doc.RootElement;
        if (p.GetProperty("Schema").GetString() != "WaW360.AztecPcPostImagesConstants.v1" ||
            p.GetProperty("Status").GetString() !=
                "AZTEC_PC_POST_IMAGES_ONE_6X32_CONSTANT_WINDOW_CANDIDATE" ||
            p.GetProperty("SourceAnchorAfterThirdImage").GetString() != "0x057319E1" ||
            p.GetProperty("WindowBytes").GetInt32() != 512 ||
            p.GetProperty("TestedPhysicalStarts").GetInt32() != 512 ||
            p.GetProperty("FullyPlausibleStarts").GetInt32() != 1 ||
            p.GetProperty("BestPlausibleRecordCount").GetInt32() != 6 ||
            p.GetProperty("ConstantCount").GetInt32() != 6 ||
            p.GetProperty("ConstantStride").GetInt32() != 32 ||
            p.GetProperty("StateBitsCount").GetInt32() != 4 ||
            p.GetProperty("StateBitsStride").GetInt32() != 8 ||
            p.GetProperty("OriginalMaterialOwnershipProven").GetBoolean() ||
            p.GetProperty("All39SourceNamesProven").GetBoolean() ||
            p.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            p.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Requires unique first-PC-Material 6/6 constant candidate with no ownership claims.");

        var winners = p.GetProperty("SixOfSixCandidateOffsets").EnumerateArray().ToArray();
        var top = p.GetProperty("TopCandidateWindows").EnumerateArray().ToArray();
        if (winners.Length != 1 || winners[0].GetString() != "0x057319E1" ||
            top.Length == 0)
            throw new InvalidDataException("Expected exactly one post-image constant candidate at the image-chain cursor.");
        var candidate = top.Single(x => x.GetProperty("ConstantTableAt").GetString() == "0x057319E1");
        if (candidate.GetProperty("PlausibleConstantRecords").GetInt32() != 6 ||
            candidate.GetProperty("HypotheticalStateBitsAt").GetString() != "0x05731AA1" ||
            candidate.GetProperty("HypotheticalNextMaterialAt").GetString() != "0x05731AC1" ||
            !candidate.GetProperty("NextNameFollowing").GetBoolean() ||
            !candidate.GetProperty("NextCountsPlausible").GetBoolean())
            throw new InvalidDataException("Post-constant statebits or next-header structural markers changed.");

        var zone = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var b = zone.Bytes;
        if (zone.Platform != WaWPlatform.PC ||
            zone.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Sha(b).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(p.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(ConstantsAt, ConstantsBytes)).Equals(
                candidate.GetProperty("ConstantsSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(StateBitsAt, StateBitsBytes)).Equals(
                candidate.GetProperty("StateBitsSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original-PC fastfile, six constants or 32-byte statebits source SHA changed.");

        var states = new List<AztecPcStateBitsPair>();
        for (var i = 0; i < 4; i++)
        {
            var at = StateBitsAt + i * 8;
            states.Add(new AztecPcStateBitsPair(
                i, Hex(at), Hex(U32(b, at)), Hex(U32(b, at + 4))));
        }

        var namePointer = U32(b, NextHeaderAt);
        var textureCount = b[NextHeaderAt + 91];
        var constantCount = b[NextHeaderAt + 92];
        var stateCount = b[NextHeaderAt + 93];
        var flags = b[NextHeaderAt + 94];
        var camera = b[NextHeaderAt + 95];
        var tech = U32(b, NextHeaderAt + 96);
        var textures = U32(b, NextHeaderAt + 100);
        var constants = U32(b, NextHeaderAt + 104);
        var statebits = U32(b, NextHeaderAt + 108);
        var countsPlausible = textureCount <= 64 &&
            constantCount <= 64 && stateCount <= 64 && camera <= 4;
        var pointersCompatible =
            ValidPointer(tech, zone.ZoneHeader) &&
            ValidCountedPointer(textures, textureCount, zone.ZoneHeader) &&
            ValidCountedPointer(constants, constantCount, zone.ZoneHeader) &&
            ValidCountedPointer(statebits, stateCount, zone.ZoneHeader);

        var possibleName = "";
        var nameAt = NextHeaderAt + MaterialHeaderBytes;
        var afterName = nameAt;
        var readableName = namePointer == 0xFFFFFFFFu &&
            TryReadName(b, nameAt, out possibleName, out afterName);

        // Exact post-name and +0/+4/+16 alignment hypotheses only.
        // Counts can differ from those of the first Material. Do not
        // traverse beyond a bounded 64-record descriptor table.
        var tables = new List<AztecPcSecondTextureTableCandidate>();
        if (readableName && countsPlausible && textureCount is > 0 and <= 64)
        {
            foreach (var at in new[] { afterName, Align4(afterName), Align16(afterName) }.Distinct())
            {
                if ((long)at + textureCount * 16 > b.Length)
                    continue;
                var good = 0;
                var following = 0;
                var packed = 0;
                for (var i = 0; i < textureCount; i++)
                {
                    var off = at + i * 16;
                    var image = U32(b, off + 12);
                    if (image == 0xFFFFFFFFu) following++;
                    if (image != 0 && image is not 0xFFFFFFFFu and not 0xFFFFFFFEu) packed++;
                    if (U32(b, off) != 0 && b[off + 7] <= 11 &&
                        b[off + 8] <= 1 && ValidPointer(image, zone.ZoneHeader))
                        good++;
                }
                tables.Add(new AztecPcSecondTextureTableCandidate(
                    Hex(at), textureCount, good, following, packed,
                    Sha(b.AsSpan(at, textureCount * 16))));
            }
        }

        var secondHeaderShape = namePointer == 0xFFFFFFFFu &&
            countsPlausible && pointersCompatible && readableName;
        var status = secondHeaderShape
            ? "AZTEC_PC_FIRST_MATERIAL_STATEBITS_SECOND_HEADER_SHAPE_CANDIDATE"
            : "AZTEC_PC_FIRST_MATERIAL_SECOND_HEADER_UNRESOLVED";
        var result = new AztecPcStateNextHeaderReport(
            "WaW360.AztecPcFirstMaterialStateNextHeader.v1", status,
            Path.GetFullPath(pcFastFile), zone.SourceSha256, ZoneSha,
            Hex(ConstantsAt), Hex(StateBitsAt), Hex(NextHeaderAt),
            candidate.GetProperty("ConstantsSha256").GetString()!,
            candidate.GetProperty("StateBitsSha256").GetString()!,
            states.ToArray(), Hex(namePointer),
            textureCount, constantCount, stateCount, flags, camera,
            Hex(tech), Hex(textures), Hex(constants), Hex(statebits),
            countsPlausible, pointersCompatible, Hex(nameAt),
            readableName ? possibleName : null,
            readableName ? Hex(afterName) : null,
            secondHeaderShape, tables.ToArray(),
            false, false, false, false,
            "Exactly located 6x32 MaterialConstantDef candidate, 4x8 raw statebits " +
            "and following 112-byte Material header SHAPE. Raw statebits are hashed and " +
            "decoded as two uint32 each, NOT validated as Xbox hardware states. " +
            "Second header string and counts are source structure candidates; no " +
            "proof of GfxWorld MaterialMemory ownership, PC material-name identity, " +
            "Xbox material binding, 735 surfaces, or deployable FF.");
        var full = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }

    private static bool TryReadName(byte[] b, int at, out string value, out int after)
    {
        value = "";
        after = at;
        if (at < 0 || at >= b.Length - 128) return false;
        var pos = at;
        while (pos - at < 127 && b[pos] != 0)
        {
            if (b[pos] is < 32 or > 126) return false;
            pos++;
        }
        if (pos == at || pos - at >= 127 || b[pos] != 0) return false;
        value = Encoding.ASCII.GetString(b, at, pos - at);
        if (value.Length < 4 || !value.Any(char.IsLetter)) return false;
        after = pos + 1;
        return true;
    }

    private static bool ValidCountedPointer(uint raw, int count, ZoneHeaderSummary zone) =>
        count == 0 ? (raw == 0 || ValidPointer(raw, zone)) :
        raw != 0 && ValidPointer(raw, zone);
    private static bool ValidPointer(uint raw, ZoneHeaderSummary zone)
    {
        if (raw is 0xFFFFFFFFu or 0xFFFFFFFEu) return true;
        var d = T4ZonePointer.Decode(raw);
        return d.Kind == T4ZonePointerKind.Offset &&
               d.Block is not null && T4ZonePointer.IsWithinBlock(raw, zone);
    }
    private static int Align4(int x) => (x + 3) & ~3;
    private static int Align16(int x) => (x + 15) & ~15;
    private static uint U32(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
    private static string Hex(int val) => "0x" + val.ToString("X8");
    private static string Hex(uint val) => "0x" + val.ToString("X8");
    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}

public sealed record AztecPcStateBitsPair(
    int Index, string Offset, string StateBits0, string StateBits1);
public sealed record AztecPcSecondTextureTableCandidate(
    string SourceOffset, int TextureCount, int PlausibleDescriptors,
    int FollowingImagePointers, int PackedImagePointers, string Sha256);
public sealed record AztecPcStateNextHeaderReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string FirstConstantTableAt, string FirstStateBitsTableAt,
    string SecondMaterialHeaderAt, string ConstantsSha256, string StateBitsSha256,
    IReadOnlyList<AztecPcStateBitsPair> StateBits,
    string SecondNamePointer, int SecondTextureCount, int SecondConstantCount,
    int SecondStateBitsCount, int SecondStateFlags, int SecondCameraRegion,
    string SecondTechniqueSetPointer, string SecondTextureTablePointer,
    string SecondConstantTablePointer, string SecondStateBitsTablePointer,
    bool SecondCountsPlausible, bool SecondPointerFieldsCompatible,
    string SecondNamePhysicalStart, string? SecondUnverifiedName,
    string? SecondAfterName, bool SecondHeaderShapePlausible,
    IReadOnlyList<AztecPcSecondTextureTableCandidate> SecondTextureCandidates,
    bool WorldMaterialMemoryOwnershipProven, bool All39MaterialNamesProven,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady, string Note);
