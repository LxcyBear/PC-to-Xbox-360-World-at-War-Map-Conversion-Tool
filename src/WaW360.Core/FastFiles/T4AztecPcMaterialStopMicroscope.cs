using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Read-only microscope at the exact SHA-pinned first unsupported PC Aztec
/// Material image header. Tests bounded offsets and exposes the individual
/// reasons the generic walker rejected the image. Does NOT relax that walker.
/// </summary>
public static class T4AztecPcMaterialStopMicroscope
{
    private const int LastCompleteHeader = 0x057336C8;
    private const int PendingMaterial = 0x057337C5;
    private const int StopImageAt = 0x05733869;
    private const int HeaderBytes = 112;
    private const int TextureBytes = 16;
    private const int ImageBytes = 36;
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";

    public static async Task<AztecPcMaterialStopMicroscopeReport> ProbeAsync(
        string pcFastFile, string walkerReport, string outputReport,
        CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(
            await File.ReadAllTextAsync(walkerReport, ct));
        var w = doc.RootElement;
        if (w.GetProperty("Schema").GetString() != "WaW360.AztecPcMaterialStreamWalker.v1" ||
            w.GetProperty("Status").GetString() !=
                "AZTEC_PC_SOURCE_MATERIAL_WALK_BEYOND_TWO_CANDIDATES" ||
            w.GetProperty("DecodedMaterialCandidates").GetInt32() != 15 ||
            w.GetProperty("StartHeader").GetString() != "0x05731803" ||
            w.GetProperty("StopReason").GetString() !=
                "INLINE_GFXIMAGE_36_HEADER_SHAPE_UNRESOLVED" ||
            w.GetProperty("StopAt").GetString() != "0x05733869" ||
            w.GetProperty("NextCursor").GetString() != "0x057337C5" ||
            w.GetProperty("GfxWorldMaterialMemoryOwnershipProven").GetBoolean() ||
            w.GetProperty("All39SourceMaterialNamesProven").GetBoolean() ||
            w.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            w.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException("Requires original 15/39 stop proof, no ownership or hardware claim.");

        var prior = w.GetProperty("Materials").EnumerateArray().ToArray();
        if (prior.Length != 15 ||
            prior[14].GetProperty("HeaderAt").GetString() != "0x057336C8" ||
            prior[14].GetProperty("NextCursor").GetString() != "0x057337C5")
            throw new InvalidDataException("The last completed original-PC Material anchor changed.");

        var zone = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, ct);
        var b = zone.Bytes;
        if (zone.Platform != WaWPlatform.PC ||
            zone.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Sha(b).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !zone.SourceSha256.Equals(w.GetProperty("OriginalPcFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original PC zone/compressed fastfile SHA changed.");

        if (!Fits(b, PendingMaterial, 256) || !Fits(b, StopImageAt - 48, 192) ||
            U32(b, PendingMaterial) != 0xFFFFFFFFu)
            throw new InvalidDataException("Pending Material header/candidate cursor unavailable.");

        var texCount = b[PendingMaterial + 91];
        var constantCount = b[PendingMaterial + 92];
        var stateCount = b[PendingMaterial + 93];
        var camera = b[PendingMaterial + 95];
        var nameAt = PendingMaterial + HeaderBytes;
        if (!TryReadName(b, nameAt, out var materialName, out var afterName))
            throw new InvalidDataException("Pending Material post-header name no longer readable.");
        var textureTable = afterName;
        if (texCount == 0 || texCount > 64 || !Fits(b, textureTable, texCount * TextureBytes) ||
            textureTable + texCount * TextureBytes != StopImageAt)
            throw new InvalidDataException("Pending Material texture table does not end at the exact walker stop.");

        var rows = new List<AztecPcStopTextureDescriptor>();
        for (var i = 0; i < texCount; i++)
        {
            var offset = textureTable + i * TextureBytes;
            var raw = U32(b, offset + 12);
            rows.Add(new AztecPcStopTextureDescriptor(
                i, Hex(offset), Hex(U32(b, offset)), b[offset + 4],
                b[offset + 5], b[offset + 6], b[offset + 7],
                b[offset + 8], Hex(raw), raw == 0xFFFFFFFFu,
                b[offset + 7] == 11,
                Convert.ToHexString(b.AsSpan(offset, TextureBytes))));
        }

        // No unbounded search. Audit the exact rejected offset plus eight
        // fixed nearby physical hypotheses, printing each validation gate.
        var offsets = new[] {
            StopImageAt - 16, StopImageAt - 8,
            StopImageAt - 4, StopImageAt,
            StopImageAt + 4, StopImageAt + 8,
            StopImageAt + 12, StopImageAt + 16, StopImageAt + 32
        };
        var images = new List<AztecPcStopImageHeaderCandidate>();
        foreach (var at in offsets)
        {
            if (!Fits(b, at, ImageBytes))
                continue;
            var mapType = U32(b, at);
            var imagePtr = U32(b, at + 4);
            var namePtr = U32(b, at + 32);
            var width = U16(b, at + 24);
            var height = U16(b, at + 26);
            var depth = U16(b, at + 28);
            var category = b[at + 30];
            var mapPass = mapType is >= 3 and <= 5;
            var texturePass = imagePtr == 0xFFFFFFFEu;
            var namePass = namePtr == 0xFFFFFFFFu;
            var dimsPass = width is >= 1 and <= 16384 &&
                height is >= 1 and <= 16384 &&
                depth is >= 1 and <= 16384;
            var fieldsPass = b[at + 10] <= 1 &&
                b[at + 11] <= 11 && b[at + 12] <= 64 &&
                category <= 7 && b[at + 31] <= 1;
            string n = "";
            int nameEnd = 0;
            var follows = namePtr == 0xFFFFFFFFu &&
                TryReadName(b, at + ImageBytes, out n, out nameEnd);
            images.Add(new AztecPcStopImageHeaderCandidate(
                Hex(at), at - StopImageAt,
                Hex(mapType), Hex(imagePtr), Hex(namePtr),
                width, height, depth, category,
                b[at + 10], b[at + 11], b[at + 12], b[at + 31],
                mapPass, texturePass, namePass, dimsPass, fieldsPass,
                mapPass && texturePass && namePass && dimsPass && fieldsPass,
                follows ? n : null, follows ? Hex(nameEnd) : null,
                Convert.ToHexString(b.AsSpan(at, ImageBytes))));
        }

        var exact = images.Single(x => x.DeltaFromStop == 0);
        var failures = new List<string>();
        if (!exact.MapTypePlausible) failures.Add("MAP_TYPE");
        if (!exact.TexturePointerIsInsert) failures.Add("TEXTURE_POINTER_NOT_INSERT");
        if (!exact.NamePointerIsFollowing) failures.Add("NAME_POINTER_NOT_FOLLOWING");
        if (!exact.DimensionsPlausible) failures.Add("DIMENSIONS");
        if (!exact.OtherHeaderFieldsPlausible) failures.Add("IMAGE_HEADER_FIELDS");
        var status = failures.Count == 0
            ? "AZTEC_PC_MATERIAL_STOP_IMAGE_HEADER_GATE_MISMATCH"
            : "AZTEC_PC_MATERIAL_15TH_SUCCESSOR_IMAGE_REJECTION_EXPLAINED";
        var result = new AztecPcMaterialStopMicroscopeReport(
            "WaW360.AztecPcMaterialStopMicroscope.v1",
            status, Path.GetFullPath(pcFastFile),
            zone.SourceSha256, ZoneSha,
            Hex(LastCompleteHeader), Hex(PendingMaterial), Hex(StopImageAt),
            Hex(textureTable), materialName, texCount, constantCount,
            stateCount, camera, rows.ToArray(), images.ToArray(),
            failures.ToArray(),
            Hex(StopImageAt - 48),
            Sha(b.AsSpan(StopImageAt - 48, 192)),
            Convert.ToHexString(b.AsSpan(StopImageAt - 48, 192)),
            false, false, false, false,
            "This probes EXACT stop plus eight bounded hypotheses. Failure " +
            "reasons concern the strict 36-byte GfxImage header gates only. " +
            "Even a more plausible offset is NOT proof that alignment/padding " +
            "exists or that byte skipping is safe. No source MaterialMemory " +
            "owner, PC-to-Xbox root mapping, image pixels or FF claimed.");
        var full = Path.GetFullPath(outputReport);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
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

    private static bool Fits(byte[] b, int offset, int length) =>
        offset >= 0 && length >= 0 && (long)offset + length <= b.LongLength;
    private static string Hex(int v) => "0x" + v.ToString("X8");
    private static string Hex(uint v) => "0x" + v.ToString("X8");
    private static string Sha(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(SHA256.HashData(b));
    private static uint U32(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
    private static ushort U16(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2));
}

public sealed record AztecPcStopTextureDescriptor(
    int Index, string Offset, string NameHash, int NameStart,
    int NameEnd, int SamplerState, int Semantic, int IsMature,
    string ImagePointer, bool IsFollowing, bool IsWater, string Raw16Hex);

public sealed record AztecPcStopImageHeaderCandidate(
    string HeaderOffset, int DeltaFromStop, string MapType,
    string TexturePointer, string NamePointer,
    int Width, int Height, int Depth, int Category,
    int NoPicmip, int Semantic, int Track, int DelayLoadPixels,
    bool MapTypePlausible, bool TexturePointerIsInsert,
    bool NamePointerIsFollowing, bool DimensionsPlausible,
    bool OtherHeaderFieldsPlausible, bool StrictWalkerAccepts,
    string? InlineNameCandidate, string? AfterName, string Raw36Hex);

public sealed record AztecPcMaterialStopMicroscopeReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    string LastCompletedMaterialHeader,
    string PendingMaterialHeader, string StopImageAt,
    string PendingTextureTableAt, string PendingMaterialNameCandidate,
    int TextureCount, int ConstantCount, int StateBitsCount, int CameraRegion,
    IReadOnlyList<AztecPcStopTextureDescriptor> TextureDescriptors,
    IReadOnlyList<AztecPcStopImageHeaderCandidate> ImageHeaderCandidates,
    IReadOnlyList<string> ExactStopRejectionReasons,
    string RawWindowStart, string RawWindowSha256, string RawWindowHex,
    bool GfxWorldMaterialMemoryOwnershipProven, bool All39SourceMaterialNamesProven,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady, string Note);
