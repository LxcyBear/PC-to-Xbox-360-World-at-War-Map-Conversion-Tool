using System.Buffers.Binary;

namespace WaW360.Core.Linker;

public sealed record T4XboxConvertedImage(
    ushort Width,
    ushort Height,
    ushort Depth,
    byte LevelCount,
    byte Flags,
    uint XenosFormat,
    int AllocationBytes,
    int BaseLevelBytes,
    byte[] PlatformPayload,
    byte[] LargeRuntimeBytes);

/// <summary>
/// Narrow Linker V1 Xbox image path proven against the Aztec load.ff golden reference.
/// It intentionally supports only IWI v6 DXT5 load-screen input for the first hardware branch.
/// The output is 1280x720 BC1/DXT1, 16-bit word swapped and Xenos 2D tiled.
/// Unsupported formats fail closed; there is no image-shaped byte scanning fallback.
/// </summary>
public static class T4XboxImageCodec
{
    public const int XboxLoadscreenWidth = 1280;
    public const int XboxLoadscreenHeight = 720;
    public const int XboxLoadscreenAllocation = 0x78000;
    public const uint XboxDxt1Format = 0x1A200152u;

    private const int IwiV6HeaderBytes = 28;
    private const byte IwiDxt5Format = 0x0D;

    // Measured 56-byte Xbox D3D texture-object tails from the known-working Aztec load.ff.
    // These are structure/format metadata only; image pixels are generated from the PC IWI.
    private static readonly byte[] LoadscreenD3dTail = Convert.FromHexString(
        "FFFFFFFF03000000010000000000000000000000000000000000FFFF0000FFFF0200008A52000000FFE45900100D00000000000000020000");

    private static readonly byte[] DefeatD3dTail = Convert.FromHexString(
        "FFFFFFFF03000000010000000000000000000000000000000000FFFF0000FFFF0200008252000000FFE01F00100D000000020000008A0000");

    public static T4XboxConvertedImage ConvertLoadscreenIwi(ReadOnlySpan<byte> iwi)
    {
        if (iwi.Length < IwiV6HeaderBytes)
            throw new InvalidDataException("IWI is too small for a v6 header.");
        if (iwi[0] != (byte)'I' || iwi[1] != (byte)'W' || iwi[2] != (byte)'i')
            throw new InvalidDataException("IWI magic is not 'IWi'.");
        if (iwi[3] != 6)
            throw new InvalidDataException($"Linker V1 alpha 2 supports IWI v6 load screens only; source version is {iwi[3]}.");
        if (iwi[4] != IwiDxt5Format)
            throw new InvalidDataException($"Linker V1 alpha 2 supports DXT5 IWI load screens only; source format is 0x{iwi[4]:X2}.");

        var width = BinaryPrimitives.ReadUInt16LittleEndian(iwi.Slice(6, 2));
        var height = BinaryPrimitives.ReadUInt16LittleEndian(iwi.Slice(8, 2));
        var depth = BinaryPrimitives.ReadUInt16LittleEndian(iwi.Slice(10, 2));
        if (width == 0 || height == 0 || depth != 1)
            throw new InvalidDataException($"Unsupported IWI dimensions {width}x{height}x{depth}; alpha 2 expects a 2D load-screen image.");

        var blockWidth = (width + 3) / 4;
        var blockHeight = (height + 3) / 4;
        var expectedPayload = checked(blockWidth * blockHeight * 16);
        if (iwi.Length - IwiV6HeaderBytes != expectedPayload)
            throw new InvalidDataException(
                $"DXT5 IWI payload length mismatch: expected {expectedPayload:N0}, found {iwi.Length - IwiV6HeaderBytes:N0}.");

        var sourceRgb = DecodeBc3Rgb(iwi.Slice(IwiV6HeaderBytes), width, height);
        var resized = ResizeRgbBilinear(sourceRgb, width, height, XboxLoadscreenWidth, XboxLoadscreenHeight);
        var linearBc1 = EncodeBc1(resized, XboxLoadscreenWidth, XboxLoadscreenHeight);
        var tiled = TileBc1ForXenos(linearBc1, XboxLoadscreenWidth, XboxLoadscreenHeight);

        if (tiled.Length != XboxLoadscreenAllocation)
            throw new InvalidDataException($"Xenos tiled load-screen allocation is 0x{tiled.Length:X}, expected 0x{XboxLoadscreenAllocation:X}.");

        return new T4XboxConvertedImage(
            XboxLoadscreenWidth,
            XboxLoadscreenHeight,
            1,
            LevelCount: 1,
            Flags: 2,
            XboxDxt1Format,
            XboxLoadscreenAllocation,
            XboxLoadscreenAllocation,
            BuildPlatformPayload(1, 2, XboxLoadscreenWidth, XboxLoadscreenHeight, 1, XboxDxt1Format, LoadscreenD3dTail),
            tiled);
    }

    public static T4XboxConvertedImage CreateAztecLoadscreenReference(ReadOnlySpan<byte> runtimeBytes)
    {
        if (runtimeBytes.Length != XboxLoadscreenAllocation)
            throw new InvalidDataException($"Reference Aztec loadscreen runtime is 0x{runtimeBytes.Length:X} bytes; expected 0x{XboxLoadscreenAllocation:X}.");

        return new T4XboxConvertedImage(
            XboxLoadscreenWidth,
            XboxLoadscreenHeight,
            1,
            LevelCount: 1,
            Flags: 2,
            XboxDxt1Format,
            XboxLoadscreenAllocation,
            XboxLoadscreenAllocation,
            BuildPlatformPayload(1, 2, XboxLoadscreenWidth, XboxLoadscreenHeight, 1, XboxDxt1Format, LoadscreenD3dTail),
            runtimeBytes.ToArray());
    }

    public static T4XboxConvertedImage CreateDefeatSmokeSubstitute()
    {
        // Structurally valid BC1 surface used only when no verified retail Xbox donor is available.
        // The caller must mark this output as a smoke substitute, never as donor-verified/final.
        const ushort width = 256;
        const ushort height = 256;
        const int allocation = 0x10000;
        const int baseBytes = 0x8000;
        return new T4XboxConvertedImage(
            width,
            height,
            1,
            LevelCount: 9,
            Flags: 0,
            XboxDxt1Format,
            allocation,
            baseBytes,
            BuildPlatformPayload(9, 0, width, height, 1, XboxDxt1Format, DefeatD3dTail),
            new byte[allocation]);
    }

    public static T4XboxConvertedImage CreateDefeatReferenceDonor(ReadOnlySpan<byte> runtimeBytes)
    {
        const ushort width = 256;
        const ushort height = 256;
        const int allocation = 0x10000;
        const int baseBytes = 0x8000;
        if (runtimeBytes.Length != allocation)
            throw new InvalidDataException($"Reference 'defeat' runtime is 0x{runtimeBytes.Length:X} bytes; expected 0x{allocation:X}.");

        return new T4XboxConvertedImage(
            width,
            height,
            1,
            LevelCount: 9,
            Flags: 0,
            XboxDxt1Format,
            allocation,
            baseBytes,
            BuildPlatformPayload(9, 0, width, height, 1, XboxDxt1Format, DefeatD3dTail),
            runtimeBytes.ToArray());
    }

    public static byte[] BuildXboxImageFixedBody(T4XboxConvertedImage image)
    {
        var output = new byte[40];
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0, 4), 3u); // mapType: 2D
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(4, 4), 0xFFFFFFFEu); // INSERT platform/load payload
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(8, 4), 0u); // cardMemory
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(12, 4), checked((uint)image.AllocationBytes));
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(16, 2), image.Width);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(18, 2), image.Height);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(20, 2), image.Depth);
        output[22] = 3; // category measured from working load.ff
        output[23] = 1; // streaming
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(24, 4), 0xFFFFFFFFu); // data follows/runtime binding
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(28, 4), checked((uint)image.BaseLevelBytes));
        output[32] = 0xFF;
        output[33] = 0xFF;
        output[34] = 0;
        output[35] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(36, 4), 0xFFFFFFFFu); // inline name
        return output;
    }

    private static byte[] BuildPlatformPayload(
        byte levels,
        byte flags,
        ushort width,
        ushort height,
        ushort depth,
        uint format,
        ReadOnlySpan<byte> d3dTail)
    {
        if (d3dTail.Length != 56) throw new ArgumentException("Xbox D3D texture-object tail must be 56 bytes.", nameof(d3dTail));
        var output = new byte[68];
        output[0] = levels;
        output[1] = flags;
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(2, 2), width);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4, 2), height);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(6, 2), depth);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(8, 4), format);
        d3dTail.CopyTo(output.AsSpan(12));
        return output;
    }

    private static byte[] DecodeBc3Rgb(ReadOnlySpan<byte> bc3, int width, int height)
    {
        var output = new byte[checked(width * height * 3)];
        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;

        for (var by = 0; by < blocksHigh; by++)
        for (var bx = 0; bx < blocksWide; bx++)
        {
            var block = bc3.Slice((by * blocksWide + bx) * 16, 16);
            var c0 = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(8, 2));
            var c1 = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(10, 2));
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(12, 4));
            Span<Rgb24> palette = stackalloc Rgb24[4];
            BuildBc1Palette(c0, c1, palette, forceFourColor: true);

            for (var py = 0; py < 4; py++)
            for (var px = 0; px < 4; px++)
            {
                var x = bx * 4 + px;
                var y = by * 4 + py;
                if (x >= width || y >= height) continue;
                var pi = py * 4 + px;
                var color = palette[(int)((bits >> (pi * 2)) & 3u)];
                var dst = (y * width + x) * 3;
                output[dst + 0] = color.R;
                output[dst + 1] = color.G;
                output[dst + 2] = color.B;
            }
        }

        return output;
    }

    private static byte[] ResizeRgbBilinear(byte[] source, int srcWidth, int srcHeight, int dstWidth, int dstHeight)
    {
        var output = new byte[checked(dstWidth * dstHeight * 3)];
        for (var y = 0; y < dstHeight; y++)
        {
            var sy = (y + 0.5) * srcHeight / dstHeight - 0.5;
            var fyFloor = Math.Floor(sy);
            var y0 = Math.Clamp((int)fyFloor, 0, srcHeight - 1);
            var y1 = Math.Min(srcHeight - 1, y0 + 1);
            var fy = Math.Clamp(sy - fyFloor, 0.0, 1.0);

            for (var x = 0; x < dstWidth; x++)
            {
                var sx = (x + 0.5) * srcWidth / dstWidth - 0.5;
                var fxFloor = Math.Floor(sx);
                var x0 = Math.Clamp((int)fxFloor, 0, srcWidth - 1);
                var x1 = Math.Min(srcWidth - 1, x0 + 1);
                var fx = Math.Clamp(sx - fxFloor, 0.0, 1.0);

                var dst = (y * dstWidth + x) * 3;
                for (var channel = 0; channel < 3; channel++)
                {
                    var p00 = source[(y0 * srcWidth + x0) * 3 + channel];
                    var p10 = source[(y0 * srcWidth + x1) * 3 + channel];
                    var p01 = source[(y1 * srcWidth + x0) * 3 + channel];
                    var p11 = source[(y1 * srcWidth + x1) * 3 + channel];
                    var top = p00 + (p10 - p00) * fx;
                    var bottom = p01 + (p11 - p01) * fx;
                    output[dst + channel] = (byte)Math.Clamp((int)Math.Round(top + (bottom - top) * fy), 0, 255);
                }
            }
        }
        return output;
    }

    private static byte[] EncodeBc1(byte[] rgb, int width, int height)
    {
        if ((width & 3) != 0 || (height & 3) != 0)
            throw new InvalidDataException("BC1 alpha-2 encoder requires dimensions divisible by four.");

        var blocksWide = width / 4;
        var blocksHigh = height / 4;
        var output = new byte[checked(blocksWide * blocksHigh * 8)];
        Span<Rgb24> pixels = stackalloc Rgb24[16];
        Span<Rgb24> palette = stackalloc Rgb24[4];

        for (var by = 0; by < blocksHigh; by++)
        for (var bx = 0; bx < blocksWide; bx++)
        {
            byte minR = 255, minG = 255, minB = 255;
            byte maxR = 0, maxG = 0, maxB = 0;
            for (var py = 0; py < 4; py++)
            for (var px = 0; px < 4; px++)
            {
                var src = ((by * 4 + py) * width + (bx * 4 + px)) * 3;
                var p = new Rgb24(rgb[src], rgb[src + 1], rgb[src + 2]);
                pixels[py * 4 + px] = p;
                if (p.R < minR) minR = p.R; if (p.G < minG) minG = p.G; if (p.B < minB) minB = p.B;
                if (p.R > maxR) maxR = p.R; if (p.G > maxG) maxG = p.G; if (p.B > maxB) maxB = p.B;
            }

            var c0 = Pack565(new Rgb24(maxR, maxG, maxB));
            var c1 = Pack565(new Rgb24(minR, minG, minB));
            if (c0 == c1)
            {
                if (c0 == 0) c0 = 1;
                else c1 = (ushort)(c0 - 1);
            }
            if (c0 < c1) (c0, c1) = (c1, c0);
            BuildBc1Palette(c0, c1, palette, forceFourColor: true);

            uint indices = 0;
            for (var i = 0; i < 16; i++)
            {
                var best = 0;
                var bestDistance = int.MaxValue;
                for (var p = 0; p < 4; p++)
                {
                    var dr = pixels[i].R - palette[p].R;
                    var dg = pixels[i].G - palette[p].G;
                    var db = pixels[i].B - palette[p].B;
                    var distance = dr * dr + dg * dg + db * db;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = p;
                }
                indices |= (uint)best << (i * 2);
            }

            var dst = (by * blocksWide + bx) * 8;
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(dst, 2), c0);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(dst + 2, 2), c1);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(dst + 4, 4), indices);
        }
        return output;
    }

    private static byte[] TileBc1ForXenos(byte[] linearBc1, int width, int height)
    {
        var blocksWide = width / 4;
        var blocksHigh = height / 4;
        var alignedBlocksHigh = (blocksHigh + 31) & ~31;
        var output = new byte[checked(blocksWide * alignedBlocksHigh * 8)];

        for (var y = 0; y < blocksHigh; y++)
        for (var x = 0; x < blocksWide; x++)
        {
            var src = (y * blocksWide + x) * 8;
            var dst = checked(TiledOffset(x, y, blocksWide, 8) * 8);
            if (dst < 0 || dst > output.Length - 8)
                throw new InvalidDataException($"Xenos tiled BC1 offset 0x{dst:X} is outside the 0x{output.Length:X} allocation.");
            for (var i = 0; i < 8; i += 2)
            {
                output[dst + i] = linearBc1[src + i + 1];
                output[dst + i + 1] = linearBc1[src + i];
            }
        }
        return output;
    }

    private static int TiledOffset(int x, int y, int blockWidth, int texelPitch)
    {
        var alignedWidth = (blockWidth + 31) & ~31;
        var logBpp = (texelPitch >> 2) + ((texelPitch >> 1) >> (texelPitch >> 2));
        var macro = ((x >> 5) + (y >> 5) * (alignedWidth >> 5)) << (logBpp + 7);
        var micro = ((x & 7) + ((y & 6) << 2)) << logBpp;
        var offset = macro + ((micro & ~15) << 1) + (micro & 15) + ((y & 8) << (3 + logBpp)) + ((y & 1) << 4);
        return (((offset & ~511) << 3) + ((offset & 448) << 2) + (offset & 63) + ((y & 16) << 7) + (((((y & 8) >> 2) + (x >> 3)) & 3) << 6)) >> logBpp;
    }

    private static ushort Pack565(Rgb24 c) => (ushort)(
        ((c.R * 31 + 127) / 255 << 11) |
        ((c.G * 63 + 127) / 255 << 5) |
        ((c.B * 31 + 127) / 255));

    private static Rgb24 Unpack565(ushort value) => new(
        (byte)(((value >> 11) & 31) * 255 / 31),
        (byte)(((value >> 5) & 63) * 255 / 63),
        (byte)((value & 31) * 255 / 31));

    private static void BuildBc1Palette(ushort c0, ushort c1, Span<Rgb24> palette, bool forceFourColor)
    {
        palette[0] = Unpack565(c0);
        palette[1] = Unpack565(c1);
        if (forceFourColor || c0 > c1)
        {
            palette[2] = new Rgb24(
                (byte)((2 * palette[0].R + palette[1].R + 1) / 3),
                (byte)((2 * palette[0].G + palette[1].G + 1) / 3),
                (byte)((2 * palette[0].B + palette[1].B + 1) / 3));
            palette[3] = new Rgb24(
                (byte)((palette[0].R + 2 * palette[1].R + 1) / 3),
                (byte)((palette[0].G + 2 * palette[1].G + 1) / 3),
                (byte)((palette[0].B + 2 * palette[1].B + 1) / 3));
        }
        else
        {
            palette[2] = new Rgb24(
                (byte)((palette[0].R + palette[1].R) / 2),
                (byte)((palette[0].G + palette[1].G) / 2),
                (byte)((palette[0].B + palette[1].B) / 2));
            palette[3] = new Rgb24(0, 0, 0);
        }
    }

    private readonly record struct Rgb24(byte R, byte G, byte B);
}
