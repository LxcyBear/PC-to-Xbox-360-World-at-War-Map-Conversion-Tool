using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Read-only exact-offset proof attempt for the first unsupported
/// original-PC Aztec Material image: MAPTYPE_NONE, null texture pointer,
/// zero dimensions, FOLLOWING name. Do not read a LoadDef when the image's
/// texture pointer is NULL. Does not modify the existing material walker.
/// </summary>
public static class T4AztecPcNullImageBoundaryProbe
{
    private const int MaterialAt = 0x057337C5;
    private const int InlineHeaderAt = 0x05733869;
    private const int GfxImageBytes = 36;
    private const int ConstantCount = 2;
    private const int ConstantBytes = 32;
    private const int StateBitsCount = 8;
    private const int StateBitsBytes = 8;
    private const int MaterialBytes = 112;
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";

    public static async Task<AztecPcNullImageBoundaryReport> ProbeAsync(
        string pcFastFile, string stopJson, string outputJson,
        CancellationToken ct = default)
    {
        using var priorDoc = JsonDocument.Parse(await File.ReadAllTextAsync(stopJson, ct));
        var p = priorDoc.RootElement;
        if (p.GetProperty("Schema").GetString() != "WaW360.AztecPcMaterialStopMicroscope.v1" ||
            p.GetProperty("Status").GetString() !=
                "AZTEC_PC_MATERIAL_15TH_SUCCESSOR_IMAGE_REJECTION_EXPLAINED" ||
            p.GetProperty("PendingMaterialHeader").GetString() != "0x057337C5" ||
            p.GetProperty("StopImageAt").GetString() != "0x05733869" ||
            p.GetProperty("PendingMaterialNameCandidate").GetString() != "wc/global_invisible" ||
            p.GetProperty("TextureCount").GetInt32() != 2 ||
            p.GetProperty("ConstantCount").GetInt32() != 2 ||
            p.GetProperty("StateBitsCount").GetInt32() != 8 ||
            p.GetProperty("GfxWorldMaterialMemoryOwnershipProven").GetBoolean() ||
            p.GetProperty("All39SourceMaterialNamesProven").GetBoolean() ||
            p.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            p.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException(
                "Expected unchanged SHA-pinned first stop microscope at Material #16.");

        var desc = p.GetProperty("TextureDescriptors").EnumerateArray().ToArray();
        if (desc.Length != 2 ||
            desc[0].GetProperty("Semantic").GetInt32() != 5 ||
            desc[0].GetProperty("ImagePointer").GetString() != "0x82A22215" ||
            desc[0].GetProperty("IsFollowing").GetBoolean() ||
            desc[1].GetProperty("Semantic").GetInt32() != 2 ||
            desc[1].GetProperty("ImagePointer").GetString() != "0xFFFFFFFF" ||
            !desc[1].GetProperty("IsFollowing").GetBoolean() ||
            desc.Any(x => x.GetProperty("IsWater").GetBoolean()))
            throw new InvalidDataException("Packed normal + inline color texture pair changed.");

        var exact = p.GetProperty("ImageHeaderCandidates").EnumerateArray()
            .Where(x => x.GetProperty("DeltaFromStop").GetInt32() == 0).ToArray();
        if (exact.Length != 1 ||
            exact[0].GetProperty("MapType").GetString() != "0x00000000" ||
            exact[0].GetProperty("TexturePointer").GetString() != "0x00000000" ||
            exact[0].GetProperty("NamePointer").GetString() != "0xFFFFFFFF" ||
            exact[0].GetProperty("Width").GetInt32() != 0 ||
            exact[0].GetProperty("Height").GetInt32() != 0 ||
            exact[0].GetProperty("Depth").GetInt32() != 0 ||
            exact[0].GetProperty("Category").GetInt32() != 0)
            throw new InvalidDataException("Exact zero-dimension, name-only GfxImage-shape candidate changed.");

        var zone = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var b = zone.Bytes;
        if (zone.Platform != WaWPlatform.PC ||
            zone.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Sha(b).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(
                p.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha(b.AsSpan(InlineHeaderAt - 48, 192)).Equals(
                p.GetProperty("RawWindowSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Exact source-zone or rejected-image microscope SHA changed.");

        if (U32(b, InlineHeaderAt) != 0 ||
            U32(b, InlineHeaderAt + 4) != 0 ||
            U32(b, InlineHeaderAt + 32) != 0xFFFFFFFFu ||
            U16(b, InlineHeaderAt + 24) != 0 ||
            U16(b, InlineHeaderAt + 26) != 0 ||
            U16(b, InlineHeaderAt + 28) != 0 ||
            b[InlineHeaderAt + 30] != 0)
            throw new InvalidDataException("Null-texture/no-dimensions GfxImage candidate changed.");

        var imageNameAt = InlineHeaderAt + GfxImageBytes;
        if (!TryReadName(b, imageNameAt, out var imageName, out var endName) ||
            imageName != ",global_black_c" ||
            endName != 0x0573389D)
            throw new InvalidDataException("Exact FOLLOWING image name or end boundary changed.");

        // When GfxTexture.loadDef is null, the T4 ZoneCode rules do not
        // establish a populated nested loadDef. Test this candidate
        // physical cursor WITHOUT consuming a 16B LoadDef.
        var afterName = endName;
        var starts = new[] {
            afterName, afterName + 1, afterName + 2,
            afterName + 3, afterName + 4,
            (afterName + 3) & ~3,
            (afterName + 15) & ~15,
            afterName + 8, afterName + 16, afterName + 32
        }.Distinct().OrderBy(x => x).ToArray();
        var candidates = new List<AztecPcNullImagePostNameLayout>();
        foreach (var at in starts)
        {
            ct.ThrowIfCancellationRequested();
            if (!Fits(b, at, ConstantCount * ConstantBytes +
                    StateBitsCount * StateBitsBytes + MaterialBytes + 4))
                continue;
            var good = 0;
            var constantNames = new List<string>();
            for (var j = 0; j < ConstantCount; j++)
            {
                var off = at + j * ConstantBytes;
                var nameBytes = b.AsSpan(off + 4, 12).ToArray();
                var readable = nameBytes.Any(x => x is >= 33 and <= 126) &&
                    nameBytes.All(x => x == 0 || x is >= 32 and <= 126);
                var boundedFloats = true;
                for (var f = 0; f < 4; f++)
                {
                    var value = BitConverter.Int32BitsToSingle(I32(b, off + 16 + f * 4));
                    if (!float.IsFinite(value) || Math.Abs(value) > 100000000f)
                        boundedFloats = false;
                }
                if (U32(b, off) != 0 && readable && boundedFloats)
                    good++;
                constantNames.Add(readable
                    ? Encoding.ASCII.GetString(nameBytes).TrimEnd('\0') : "");
            }
            var statesAt = at + ConstantCount * ConstantBytes;
            var nextAt = statesAt + StateBitsCount * StateBitsBytes;
            var pointer = U32(b, nextAt);
            var tx = b[nextAt + 91];
            var co = b[nextAt + 92];
            var st = b[nextAt + 93];
            var camera = b[nextAt + 95];
            var counts = tx <= 64 && co <= 64 && st <= 64 && camera <= 4;
            var nameValid = pointer == 0xFFFFFFFFu &&
                TryReadName(b, nextAt + MaterialBytes, out var nextName, out _);
            candidates.Add(new AztecPcNullImagePostNameLayout(
                Hex(at), at - afterName, good, constantNames,
                Hex(statesAt), Sha(b.AsSpan(statesAt, StateBitsCount * StateBitsBytes)),
                Hex(nextAt), Hex(pointer), tx, co, st, camera,
                counts, nameValid, nameValid ? nextName : null));
        }
        var exactStart = candidates.Single(x => x.DeltaAfterName == 0);
        var structurallyConvincing = exactStart.PlausibleConstants == 2 &&
            exactStart.NextNameFollowingAndReadable &&
            exactStart.NextCountsPlausible;
        var status = structurallyConvincing
            ? "AZTEC_PC_NULL_IMAGE_NAME_ONLY_EXACT_MATERIAL_CONTINUATION_CANDIDATE"
            : "AZTEC_PC_NULL_IMAGE_NAME_ONLY_CHILD_BOUNDARY_UNRESOLVED";
        var report = new AztecPcNullImageBoundaryReport(
            "WaW360.AztecPcNullImageBoundary.v1", status,
            Path.GetFullPath(pcFastFile), zone.SourceSha256, ZoneSha,
            Hex(MaterialAt), Hex(InlineHeaderAt), Hex(imageNameAt),
            imageName, Hex(endName), false,
            candidates.Count, candidates.ToArray(), structurallyConvincing,
            false, false, false, false,
            "MAPTYPE_NONE=0 and null GfxTexture pointer=0 were read at the " +
            "exact source cursor, with an inline FOLLOWING name. This is a " +
            "hypothetical no-loadDef image child, NOT proof it is a runtime-" +
            "usable image or that an omitted data child always uses this ABI. " +
            "Only 10 bounded after-name hypotheses evaluated. We did NOT " +
            "advance the generic walker, forge a 16-byte LoadDef, establish " +
            "GfxWorld MaterialMemory ownership, bind native material roots, " +
            "emit pixels, or generate a FF.");
        var output = Path.GetFullPath(outputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        return report;
    }

    private static bool TryReadName(byte[] b, int at, out string name, out int end)
    {
        name = "";
        end = at;
        if (!Fits(b, at, 128)) return false;
        var p = at;
        while (p - at < 127 && b[p] != 0)
        {
            if (b[p] is < 32 or > 126) return false;
            p++;
        }
        if (p - at < 4 || p - at >= 127 || b[p] != 0)
            return false;
        name = Encoding.ASCII.GetString(b, at, p - at);
        if (!name.Any(char.IsLetter)) return false;
        end = p + 1;
        return true;
    }
    private static bool Fits(byte[] b, int off, int length) =>
        off >= 0 && length >= 0 && (long)off + length <= b.LongLength;
    private static string Hex(int v) => "0x" + v.ToString("X8");
    private static string Hex(uint v) => "0x" + v.ToString("X8");
    private static string Sha(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(SHA256.HashData(b));
    private static ushort U16(byte[] b, int off) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(off, 2));
    private static uint U32(byte[] b, int off) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(off, 4));
    private static int I32(byte[] b, int off) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(off, 4));
}

public sealed record AztecPcNullImagePostNameLayout(
    string ConstantTableAt, int DeltaAfterName, int PlausibleConstants,
    IReadOnlyList<string> ConstantNames, string StateBitsAt, string StateBitsSha256,
    string NextHeaderAt, string NextNamePointer,
    int NextTextureCount, int NextConstantCount, int NextStateBitsCount,
    int NextCameraRegion, bool NextCountsPlausible,
    bool NextNameFollowingAndReadable, string? NextNameCandidate);

public sealed record AztecPcNullImageBoundaryReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string PendingMaterialHeaderAt, string NullImageHeaderAt,
    string NullImageNameAt, string NullImageName,
    string CandidateAfterInlineName, bool NestedLoadDefProven,
    int TestedConstantStarts, IReadOnlyList<AztecPcNullImagePostNameLayout> Candidates,
    bool ExactPostNameMaterialContinuationPlausible,
    bool GfxWorldMaterialMemoryOwnershipProven, bool All39SourceMaterialNamesProven,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady, string Note);
