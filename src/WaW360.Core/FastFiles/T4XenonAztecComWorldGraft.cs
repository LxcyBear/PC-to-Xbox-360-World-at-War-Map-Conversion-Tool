using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Genuine original-PC Aztec ComWorld fixed source parent/children, transformed
/// to the T4 Xbox 360 big-endian ABI. This is a scratch OAT parser diagnostic:
/// neither GfxWorld nor GameWorldSP is reconstructed, and the source layout
/// still has no independent native-runtime proof.
/// </summary>
internal sealed record T4XenonAztecComWorldSource(
    int NativeRootIndex,
    string Name,
    byte[] ParentXenon,
    byte[] LightsXenon,
    byte[] BurnableCellsXenon,
    byte[] BurnableSamples,
    string SourceParentSha256,
    string SourceLightsSha256,
    string SourceCellsSha256,
    string SourceSamplesSha256);

internal static class T4XenonAztecComWorldGraft
{
    private const int RootIndex = 7178;
    private const string RootName = "maps/nazi_zombie_aztec.d3dbsp";
    private const int ParentOffset = 0x0479B3B5;
    private const int ParentSize = 64;
    private const int LightsSize = 2 * 72;
    private const int CellCount = 88;
    private const int CellsSize = CellCount * 12;
    private const int SamplesSize = CellCount * 32;

    public static async Task<T4XenonAztecComWorldSource?> LoadAsync(
        string? proofManifest,
        IReadOnlyList<T4XenonNativeIdentityEntry> roots,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(proofManifest))
            return null;
        var manifestPath = Path.GetFullPath(proofManifest);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        var j = doc.RootElement;
        if (j.GetProperty("schema").GetString() != "WaW360.AztecPcComWorldBurnableSource.v1" ||
            j.GetProperty("status").GetString() != "AZTEC_PC_COMWORLD_88_BURNABLE_CELL_HEADERS_PASS" ||
            j.GetProperty("hardwareTestAuthorized").GetBoolean() ||
            j.GetProperty("exactSourceWorldSerializationProven").GetBoolean() ||
            j.GetProperty("child32ByteSamplesAuthenticated").GetBoolean() ||
            j.GetProperty("xboxWorldConverted").GetBoolean() ||
            j.GetProperty("nativeRootIndex").GetInt32() != RootIndex ||
            j.GetProperty("expectedBurnableCellCount").GetInt32() != CellCount ||
            roots.Count != 8509 ||
            roots[RootIndex].Index != RootIndex ||
            !roots[RootIndex].Name.Equals(RootName, StringComparison.OrdinalIgnoreCase) ||
            !roots[RootIndex].TypeName.Replace("_", "", StringComparison.Ordinal)
                .Equals("comworld", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Aztec ComWorld source proof, root #7178 or scratch-only flags changed.");

        var record = j.GetProperty("bestCellArray");
        var cellOffset = record.GetProperty("serializedCellArrayOffset").GetInt32();
        if (cellOffset != ParentOffset + ParentSize + Encoding.UTF8.GetByteCount(RootName) + 1 + LightsSize ||
            record.GetProperty("bytePaddingAfterLights").GetInt32() != 0 ||
            record.GetProperty("cellRecords").GetInt32() != CellCount ||
            record.GetProperty("cellRecordBytes").GetInt32() != 12 ||
            record.GetProperty("boundsMatchingCoordinates").GetInt32() != CellCount ||
            record.GetProperty("recognizedPointers").GetInt32() != CellCount ||
            record.GetProperty("uniqueCoordinates").GetInt32() != CellCount ||
            record.GetProperty("inlinePointers").GetInt32() != CellCount ||
            record.GetProperty("nullPointers").GetInt32() != 0 ||
            record.GetProperty("score").GetInt32() != 264)
            throw new InvalidDataException("ComWorld 88-cell source stream not uniquely pinned to zero padding and all-inline pointers.");

        var samples = record.GetProperty("adjacent32ByteSampleCandidates");
        if (samples.GetArrayLength() == 0 ||
            samples[0].GetProperty("paddingAfterCellArray").GetInt32() != 0 ||
            samples[0].GetProperty("sourceOffset").GetInt32() != cellOffset + CellsSize ||
            samples[0].GetProperty("inlineCellCount").GetInt32() != CellCount ||
            samples[0].GetProperty("sampleBytes").GetInt32() != SamplesSize ||
            samples[0].GetProperty("nonzeroSampleBytes").GetInt32() != 1300)
            throw new InvalidDataException("Original-PC 88 x 32-byte cell sample layout hypothesis changed.");

        var ff = Path.GetFullPath(j.GetProperty("originalPCMapFastFile").GetString() ?? "");
        var pcFastFileSha = j.GetProperty("originalPCMapSha256").GetString() ?? "";
        if (!File.Exists(ff) || !ff.EndsWith(".ff", StringComparison.OrdinalIgnoreCase) ||
            !Sha256(await File.ReadAllBytesAsync(ff, cancellationToken)).Equals(
                pcFastFileSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original Aztec PC map.ff source hash changed.");

        var zone = await FastFileAnalysisCache.GetZoneAsync(ff, cancellationToken);
        if (zone.Platform != WaWPlatform.PC ||
            zone.Bytes.LongLength != 113701260L ||
            !Sha256(zone.Bytes).Equals(j.GetProperty("originalPCZoneSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Aztec original PC decompressed zone SHA/size/platform changed.");

        var bytes = zone.Bytes;
        var name = Encoding.UTF8.GetBytes(RootName + "\0");
        var lightsOffset = ParentOffset + ParentSize + name.Length;
        var parent = Slice(bytes, ParentOffset, ParentSize);
        if (!name.AsSpan().SequenceEqual(bytes.AsSpan(ParentOffset + ParentSize, name.Length)) ||
            !Sha256(parent).Equals(j.GetProperty("comWorldHeaderSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            ReadU32(parent, 0) != uint.MaxValue ||
            ReadU32(parent, 4) != 1 ||
            ReadU32(parent, 8) != 2 ||
            ReadU32(parent, 12) != uint.MaxValue ||
            ReadU32(parent, 32) != 0 ||
            ReadU32(parent, 36) != 0 ||
            ReadU32(parent, 56) != CellCount ||
            ReadU32(parent, 60) != uint.MaxValue ||
            ReadI32(parent, 40) != -6 || ReadI32(parent, 44) != -19 ||
            ReadI32(parent, 48) != 22 || ReadI32(parent, 52) != 5)
            throw new InvalidDataException("Original-PC ComWorld parent fields, name, lights or burnable bounds changed.");

        var lights = Slice(bytes, lightsOffset, LightsSize);
        if (!Sha256(lights).Equals(j.GetProperty("twoLights144ByteSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Two original-PC ComPrimaryLight 72-byte records changed.");
        for (var i = 0; i != 2; i++)
        {
            var light = i * 72;
            if (lights[light] > 16 || lights[light + 1] > 2 ||
                ReadU32(lights, light + 68) != 0)
                throw new InvalidDataException($"ComPrimaryLight #{i} invalid type/shadow/defName pointer.");
            for (var f = 8; f <= 64; f += 4)
                if (!float.IsFinite(BitConverter.ToSingle(lights, light + f)))
                    throw new InvalidDataException($"ComPrimaryLight #{i} contains an invalid float.");
        }

        var cells = Slice(bytes, cellOffset, CellsSize);
        if (!Sha256(cells).Equals(record.GetProperty("cellArraySha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("88 original-PC ComBurnableCell records SHA changed.");
        var unique = new HashSet<(int X, int Y)>();
        for (var i = 0; i < CellCount; i++)
        {
            var o = i * 12;
            var x = ReadI32(cells, o);
            var y = ReadI32(cells, o + 4);
            if (x < -6 || x > 22 || y < -19 || y > 5 ||
                ReadU32(cells, o + 8) != uint.MaxValue ||
                !unique.Add((x, y)))
                throw new InvalidDataException($"ComBurnableCell #{i} source coordinates/pointer ambiguous.");
        }
        if (unique.Count != CellCount)
            throw new InvalidDataException("Original PC burnable-cell positions not unique.");

        var sampleBytes = Slice(bytes, cellOffset + CellsSize, SamplesSize);
        if (!Sha256(sampleBytes).Equals(
                samples[0].GetProperty("sourceSampleSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            sampleBytes.Count(x => x != 0) != 1300)
            throw new InvalidDataException("Original-PC 2,816 source burnable sample bytes failed pinned SHA.");

        // Original PC structure fields require word endian transformation.
        // Sample payloads are unsigned char[32] and must remain byte-exact.
        var xboxParent = Swap32(parent);
        var xboxLights = new byte[LightsSize];
        for (var i = 0; i < 2; i++)
        {
            var p = i * 72;
            Array.Copy(lights, p, xboxLights, p, 4);
            xboxLights[p + 4] = lights[p + 5];
            xboxLights[p + 5] = lights[p + 4];
            xboxLights[p + 6] = lights[p + 6];
            xboxLights[p + 7] = lights[p + 7];
            for (var f = 8; f <= 68; f += 4)
                Array.Copy(Swap32(lights.AsSpan(p + f, 4)), 0, xboxLights, p + f, 4);
        }

        return new T4XenonAztecComWorldSource(
            RootIndex, RootName,
            xboxParent, xboxLights, Swap32(cells), sampleBytes,
            Sha256(parent), Sha256(lights), Sha256(cells), Sha256(sampleBytes));
    }

    private static byte[] Slice(byte[] b, int offset, int length) =>
        b.AsSpan(offset, length).ToArray();

    private static uint ReadU32(ReadOnlySpan<byte> b, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(offset, 4));

    private static int ReadI32(ReadOnlySpan<byte> b, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.Slice(offset, 4));

    private static byte[] Swap32(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length % 4 != 0) throw new InvalidDataException("Unaligned 32-bit endian conversion.");
        var result = new byte[bytes.Length];
        for (var i = 0; i < bytes.Length; i += 4)
        {
            result[i] = bytes[i + 3];
            result[i + 1] = bytes[i + 2];
            result[i + 2] = bytes[i + 1];
            result[i + 3] = bytes[i];
        }
        return result;
    }

    private static string Sha256(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(SHA256.HashData(b));
}
