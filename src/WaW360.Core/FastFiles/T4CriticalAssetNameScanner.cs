using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4CriticalNameScanResult(
    string SourceFastFile,
    WaWPlatform Platform,
    Endian Endianness,
    int DeclaredXAnim,
    int DeclaredXModel,
    IReadOnlyList<string> XAnimNames,
    IReadOnlyList<string> XModelNames,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Stage 4 alpha 3 name-only resolver for the two largest critical T4 asset classes.
/// It intentionally does NOT claim to serialize either asset yet. The fixed 32-bit
/// T4 layouts come from the same zone-code definitions used by the loader:
///   XAnimParts fixed body = 88 bytes
///   XModel fixed body     = 228 bytes
/// Names are accepted only when the surrounding fixed fields are structurally plausible.
/// </summary>
public static class T4CriticalAssetNameScanner
{
    private const uint Following = 0xFFFFFFFFu;
    private const uint Insert = 0xFFFFFFFEu;
    private const int XAnimFixedSize = 88;
    private const int XModelFixedSize = 228;

    public static async Task<T4CriticalNameScanResult> ScanFastFileAsync(
        string fastFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(fastFilePath))
            throw new FileNotFoundException("Fastfile not found.", fastFilePath);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "criticalnames", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "zone.bin");

        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(fastFilePath, zonePath, cancellationToken);
            var bytes = await File.ReadAllBytesAsync(zonePath, cancellationToken);
            var document = T4ZoneParser.ParseZoneBytes(
                fastFilePath,
                bytes,
                decomp.Platform,
                decomp.Compression,
                decomp.ZoneHeader.Endianness);

            var declaredXAnim = document.Assets.Count(x => x.TypeName.Equals("xanim", StringComparison.OrdinalIgnoreCase));
            var declaredXModel = document.Assets.Count(x => x.TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase));
            var warnings = new List<string>();

            var xanims = ScanXAnimNames(bytes, document.Endianness);
            var xmodels = ScanXModelNames(bytes, document.Endianness);

            if (declaredXAnim > 0 && xanims.Count != declaredXAnim)
                warnings.Add($"xanim: declared {declaredXAnim:N0}, structurally resolved {xanims.Count:N0}. Names remain useful for donor discovery, but Alpha 3 does not use this mismatch to rewrite assets.");

            if (declaredXModel > 0 && xmodels.Count != declaredXModel)
                warnings.Add($"xmodel: declared {declaredXModel:N0}, structurally observed {xmodels.Count:N0} unique model/reference names. Nested model references can make this number exceed the top-level XAsset count; no rewrite decision is based on count alone.");

            return new T4CriticalNameScanResult(
                fastFilePath,
                decomp.Platform,
                document.Endianness,
                declaredXAnim,
                declaredXModel,
                xanims,
                xmodels,
                warnings);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static IReadOnlyList<string> ScanXAnimNames(byte[] zone, Endian endian)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var offset = 0; offset <= zone.Length - XAnimFixedSize - 5; offset++)
        {
            if (!IsInline(ReadU32(zone, offset, endian))) continue;

            var numFrames = ReadU16(zone, offset + 14, endian);
            if (numFrames > 10000) continue;
            if (zone[offset + 16] > 1 || zone[offset + 17] > 1 || zone[offset + 30] > 1) continue;

            var framerate = ReadF32(zone, offset + 40, endian);
            var frequency = ReadF32(zone, offset + 44, endian);
            if (!FiniteRange(framerate, 0f, 1000f) || !FiniteRange(frequency, 0f, 1000f)) continue;

            var pointerOffsets = new[] { 48, 52, 56, 60, 64, 68, 72, 76, 80, 84 };
            var pointersOkay = true;
            foreach (var p in pointerOffsets)
            {
                if (!IsSerializedPointer(ReadU32(zone, offset + p, endian)))
                {
                    pointersOkay = false;
                    break;
                }
            }
            if (!pointersOkay) continue;

            if (!TryReadAssetName(zone, offset + XAnimFixedSize, out var name)) continue;
            names.Add(name);
        }

        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ScanXModelNames(byte[] zone, Endian endian)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var offset = 0; offset <= zone.Length - XModelFixedSize - 5; offset++)
        {
            if (!IsInline(ReadU32(zone, offset, endian))) continue;

            var numBones = zone[offset + 4];
            var numRootBones = zone[offset + 5];
            var numSurfs = zone[offset + 6];
            var lodRamp = zone[offset + 7];
            if (numRootBones > numBones || numSurfs > 128 || lodRamp > 3) continue;

            var numLods = ReadU16(zone, offset + 196, endian);
            var collLod = ReadI16(zone, offset + 198, endian);
            if (numLods > 4 || collLod < -1 || collLod > 3 || zone[offset + 209] > 1) continue;

            var pointerOffsets = new[] { 8, 12, 16, 20, 24, 28, 32, 36, 152, 164, 212, 216, 220, 224 };
            var pointersOkay = true;
            foreach (var p in pointerOffsets)
            {
                if (!IsSerializedPointer(ReadU32(zone, offset + p, endian)))
                {
                    pointersOkay = false;
                    break;
                }
            }
            if (!pointersOkay) continue;

            var radius = ReadF32(zone, offset + 168, endian);
            if (!FiniteRange(radius, 0f, 10_000_000f)) continue;
            var boundsOkay = true;
            for (var p = 172; p <= 192; p += 4)
            {
                var value = ReadF32(zone, offset + p, endian);
                if (!FiniteRange(value, -100_000_000f, 100_000_000f))
                {
                    boundsOkay = false;
                    break;
                }
            }
            if (!boundsOkay) continue;

            if (!TryReadAssetName(zone, offset + XModelFixedSize, out var name)) continue;
            names.Add(name);
        }

        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryReadAssetName(byte[] zone, int offset, out string name)
    {
        name = string.Empty;
        if (offset < 0 || offset >= zone.Length) return false;
        var end = offset;
        var max = Math.Min(zone.Length, offset + 256);
        while (end < max && zone[end] != 0)
        {
            var b = zone[end];
            if (b < 0x20 || b > 0x7E) return false;
            end++;
        }
        if (end >= max || end == offset || zone[end] != 0) return false;
        if (end - offset < 4) return false;

        name = Encoding.ASCII.GetString(zone, offset, end - offset);
        if (name.Any(char.IsWhiteSpace)) return false;
        if (!name.Any(char.IsLetter)) return false;
        return true;
    }

    private static bool IsInline(uint value) => value is Following or Insert;

    private static bool IsSerializedPointer(uint value) =>
        value == 0 || value == Following || value == Insert || (value & 0x80000000u) != 0;

    private static bool FiniteRange(float value, float min, float max) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;

    private static uint ReadU32(byte[] bytes, int offset, Endian endian)
    {
        var span = bytes.AsSpan(offset, 4);
        return endian == Endian.Big
            ? BinaryPrimitives.ReadUInt32BigEndian(span)
            : BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    private static ushort ReadU16(byte[] bytes, int offset, Endian endian)
    {
        var span = bytes.AsSpan(offset, 2);
        return endian == Endian.Big
            ? BinaryPrimitives.ReadUInt16BigEndian(span)
            : BinaryPrimitives.ReadUInt16LittleEndian(span);
    }

    private static short ReadI16(byte[] bytes, int offset, Endian endian)
    {
        var span = bytes.AsSpan(offset, 2);
        return endian == Endian.Big
            ? BinaryPrimitives.ReadInt16BigEndian(span)
            : BinaryPrimitives.ReadInt16LittleEndian(span);
    }

    private static float ReadF32(byte[] bytes, int offset, Endian endian)
    {
        var bits = ReadU32(bytes, offset, endian);
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }
}
