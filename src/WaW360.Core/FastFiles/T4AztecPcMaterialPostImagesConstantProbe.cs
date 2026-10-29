using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Exhaustively tests each byte offset in a 512-byte physical source window
/// immediately after the three-image structural chain. A 16-byte ALLOCATION
/// alignment does not prove that the decompressed file's absolute offset is
/// aligned. This is a diagnostic, never a name or Material ownership proof.
/// </summary>
public static class T4AztecPcMaterialPostImagesConstantProbe
{
    private const int PostImages = 0x057319E1;
    private const int SearchBytes = 512;
    private const int ConstantCount = 6;
    private const int ConstantStride = 32;
    private const int StateBitsCount = 4;
    private const int StateBitsStride = 8;
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";

    public static async Task<AztecPcPostImageConstantsReport> ProbeAsync(
        string pcFastFile, string chainJson, string outputJson,
        CancellationToken ct = default)
    {
        using var priorDoc = JsonDocument.Parse(await File.ReadAllTextAsync(chainJson, ct));
        var p = priorDoc.RootElement;
        if (p.GetProperty("Schema").GetString() != "WaW360.AztecPcFirstMaterialImageChain.v1" ||
            p.GetProperty("Status").GetString() !=
                "AZTEC_PC_FIRST_MATERIAL_THREE_IMAGE_SERIALIZATION_CHAIN_CANDIDATE" ||
            !p.GetProperty("ThreeImageSerializationChainCandidate").GetBoolean() ||
            p.GetProperty("HypotheticalPostThirdImageCursor").GetString() != "0x057319E1" ||
            p.GetProperty("ExpectedFollowingImageReferences").GetInt32() != 3 ||
            p.GetProperty("MaterialMemoryOwnershipProven").GetBoolean() ||
            p.GetProperty("All39SourceNamesProven").GetBoolean() ||
            p.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            p.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Require unchanged SHA-bound first-material three-image structural chain.");

        var imgs = p.GetProperty("AdditionalImageCandidates").EnumerateArray().ToArray();
        if (imgs.Length != 2 ||
            imgs[0].GetProperty("HeaderAt").GetString() != "0x05731937" ||
            imgs[1].GetProperty("HeaderAt").GetString() != "0x0573198C" ||
            imgs[0].GetProperty("CandidateAfterLoadDef").GetString() != "0x0573198C" ||
            imgs[1].GetProperty("CandidateAfterLoadDef").GetString() != "0x057319E1" ||
            imgs.Any(x => !x.GetProperty("ImageHeaderPlausible").GetBoolean() ||
                !x.GetProperty("InlineNamePlausible").GetBoolean() ||
                !x.GetProperty("LoadDefPrefixDimensionsMatch").GetBoolean() ||
                !x.GetProperty("ResourceFitsSource").GetBoolean() ||
                !x.GetProperty("FormatIsDXT1DXT3OrDXT5").GetBoolean() ||
                x.GetProperty("LoadDefResourceBytes").GetUInt32() != 0))
            throw new InvalidDataException("Three exact serial DXT image-child candidate reports drifted.");

        var zone = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var b = zone.Bytes;
        if (zone.Platform != WaWPlatform.PC ||
            zone.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Convert.ToHexString(SHA256.HashData(b)).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(p.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source decompressed zone or compressed fastfile identity mismatch.");

        var rows = new List<AztecPcPostImageConstantWindow>();
        for (var delta = 0; delta < SearchBytes; delta++)
        {
            ct.ThrowIfCancellationRequested();
            var start = PostImages + delta;
            var good = 0;
            var validAscii = 0;
            var validFloats = 0;
            var nonzeroHashes = 0;
            var names = new string[ConstantCount];
            for (var i = 0; i < ConstantCount; i++)
            {
                var off = start + i * ConstantStride;
                var hash = U32(b, off);
                var nameBytes = b.AsSpan(off + 4, 12).ToArray();
                var isAscii = true;
                var printable = false;
                for (var j = 0; j < nameBytes.Length; j++)
                {
                    var c = nameBytes[j];
                    if (c != 0 && (c < 32 || c > 126)) isAscii = false;
                    if (c >= 33 && c <= 126) printable = true;
                }
                var floatsOk = true;
                for (var j = 0; j < 4; j++)
                {
                    var bits = I32(b, off + 16 + j * 4);
                    var v = BitConverter.Int32BitsToSingle(bits);
                    if (!float.IsFinite(v) || Math.Abs(v) > 100000000.0f)
                        floatsOk = false;
                }
                if (hash != 0) nonzeroHashes++;
                if (isAscii && printable) validAscii++;
                if (floatsOk) validFloats++;
                if (hash != 0 && isAscii && printable && floatsOk) good++;
                // Only diagnostic raw ASCII; do not promote to a Material name.
                names[i] = isAscii && printable
                    ? Encoding.ASCII.GetString(nameBytes).TrimEnd('\0')
                    : "";
            }
            var stateAt = start + ConstantCount * ConstantStride;
            var nextAt = stateAt + StateBitsCount * StateBitsStride;
            var marker = U32(b, nextAt) == 0xFFFFFFFFu;
            var plausibleCounts = b[nextAt + 91] <= 64 &&
                b[nextAt + 92] <= 64 && b[nextAt + 93] <= 64 &&
                b[nextAt + 95] <= 4;
            rows.Add(new AztecPcPostImageConstantWindow(
                Hex(start), delta, start & 15,
                good, nonzeroHashes, validAscii, validFloats,
                names, Sha(b.AsSpan(start, ConstantCount * ConstantStride)),
                Hex(stateAt), Sha(b.AsSpan(stateAt, StateBitsCount * StateBitsStride)),
                Hex(nextAt), marker, plausibleCounts));
        }
        var hits = rows.Where(x => x.PlausibleConstantRecords == 6).ToArray();
        var leaders = rows
            .OrderByDescending(x => x.PlausibleConstantRecords)
            .ThenByDescending(x => x.PrintableNames)
            .ThenByDescending(x => x.FiniteFloatRecords)
            .ThenBy(x => x.RelativeByteOffset)
            .Take(20).ToArray();

        var status = hits.Length == 1
            ? "AZTEC_PC_POST_IMAGES_ONE_6X32_CONSTANT_WINDOW_CANDIDATE"
            : hits.Length > 1
                ? "AZTEC_PC_POST_IMAGES_MULTIPLE_6X32_CONSTANT_WINDOW_CANDIDATES"
                : "AZTEC_PC_POST_IMAGES_6X32_CONSTANT_WINDOW_UNRESOLVED";
        var result = new AztecPcPostImageConstantsReport(
            "WaW360.AztecPcPostImagesConstants.v1",
            status, Path.GetFullPath(pcFastFile), zone.SourceSha256, ZoneSha,
            Hex(PostImages), SearchBytes, ConstantCount, ConstantStride,
            StateBitsCount, StateBitsStride,
            rows.Count, hits.Length,
            rows.Max(x => x.PlausibleConstantRecords),
            hits.Select(x => x.ConstantTableAt).ToArray(),
            leaders, false, false, false, false,
            "Compares all 512 exact physical byte starts after a source-only three-image " +
            "candidate, not just absolute 16-byte aligned starts. A record scores when " +
            "nameHash != 0, 12-byte name ASCII+nonempty, and four finite bounded floats. " +
            "This is heuristic: false positives are possible, alignment may be block-" +
            "relative, stateBits may be elsewhere and next Material adjacency isn't known. " +
            "Do not infer physical owner, six constants, 39 names, pixels, Xbox binding or FF.");
        var output = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }

    private static uint U32(byte[] bytes, int pos) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos, 4));
    private static int I32(byte[] bytes, int pos) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos, 4));
    private static string Hex(int value) => "0x" + value.ToString("X8");
    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}

public sealed record AztecPcPostImageConstantWindow(
    string ConstantTableAt, int RelativeByteOffset, int PhysicalOffsetModulo16,
    int PlausibleConstantRecords, int NonzeroNameHashes,
    int PrintableNames, int FiniteFloatRecords,
    IReadOnlyList<string> CandidateRecordNames,
    string ConstantsSha256, string HypotheticalStateBitsAt,
    string StateBitsSha256, string HypotheticalNextMaterialAt,
    bool NextNameFollowing, bool NextCountsPlausible);

public sealed record AztecPcPostImagesConstantsReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string SourceAnchorAfterThirdImage, int WindowBytes,
    int ConstantCount, int ConstantStride,
    int StateBitsCount, int StateBitsStride,
    int TestedPhysicalStarts, int FullyPlausibleStarts,
    int BestPlausibleRecordCount,
    IReadOnlyList<string> SixOfSixCandidateOffsets,
    IReadOnlyList<AztecPcPostImageConstantWindow> TopCandidateWindows,
    bool OriginalMaterialOwnershipProven, bool All39SourceNamesProven,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady, string Note);
