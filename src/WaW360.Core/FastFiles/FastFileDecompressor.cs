using System.IO.Compression;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record DecompressionResult(
    WaWPlatform Platform,
    WaWCompressionKind Compression,
    long DecompressedSize,
    ZoneHeaderSummary ZoneHeader,
    string OutputPath);

public static class FastFileDecompressor
{
    public static async Task<DecompressionResult> DecompressAutoAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(inputPath);
        if (input.Length < FastFileConstants.StandardHeaderSize)
            throw new InvalidDataException("File is too small to be a WaW fastfile.");

        var magicBytes = new byte[8];
        await ReadExactlyAsync(input, magicBytes, cancellationToken);
        var magic = Encoding.ASCII.GetString(magicBytes);
        var versionBytes = new byte[4];
        await ReadExactlyAsync(input, versionBytes, cancellationToken);

        if (magic == FastFileConstants.SignedMagic)
            return await DecompressXboxSignedAsync(inputPath, outputPath, cancellationToken);

        if (magic != FastFileConstants.UnsignedMagic)
            throw new InvalidDataException($"Unsupported fastfile magic '{magic}'.");

        // PC and unsigned Xbox WaW share the same magic/version. Probe PC's single-zlib
        // layout first; if it fails validation, retry using Xbox block-compressed raw deflate.
        var probePc = outputPath + ".pcprobe";
        try
        {
            await DecompressPcSingleZlibAsync(inputPath, probePc, cancellationToken);
            var pcHeader = await ReadZoneHeaderAsync(probePc, Endian.Little, cancellationToken);
            var xboxSingleHeader = await ReadZoneHeaderAsync(probePc, Endian.Big, cancellationToken);

            if (pcHeader.Plausible && pcHeader.PlausibilityScore >= xboxSingleHeader.PlausibilityScore)
            {
                ReplaceFile(probePc, outputPath);
                return new DecompressionResult(WaWPlatform.PC, WaWCompressionKind.PcSingleZlib,
                    new FileInfo(outputPath).Length, pcHeader, outputPath);
            }

            // codxe patch zones can be Xbox/big-endian zone data wrapped in one normal
            // zlib stream (78 9C) instead of the retail 64 KiB Xbox block layout.
            if (xboxSingleHeader.Plausible)
            {
                ReplaceFile(probePc, outputPath);
                return new DecompressionResult(WaWPlatform.Xbox360, WaWCompressionKind.XboxSingleZlib,
                    new FileInfo(outputPath).Length, xboxSingleHeader, outputPath);
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Expected while probing Xbox files.
        }
        finally
        {
            SafeDelete(probePc);
        }

        var probeXbox = outputPath + ".xboxprobe";
        try
        {
            await DecompressXboxBlocksAsync(inputPath, probeXbox, cancellationToken);
            var xboxHeader = await ReadZoneHeaderAsync(probeXbox, Endian.Big, cancellationToken);
            if (!xboxHeader.Plausible)
                throw new InvalidDataException("Xbox block decompression completed, but the resulting WaW zone header is not plausible.");

            ReplaceFile(probeXbox, outputPath);
            return new DecompressionResult(WaWPlatform.Xbox360, WaWCompressionKind.XboxBlockDeflate,
                new FileInfo(outputPath).Length, xboxHeader, outputPath);
        }
        finally
        {
            SafeDelete(probeXbox);
        }
    }

    public static async Task DecompressPcSingleZlibAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(inputPath);
        input.Position = FastFileConstants.StandardHeaderSize;
        await using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
        await using var output = File.Create(outputPath);
        await zlib.CopyToAsync(output, 1024 * 128, cancellationToken);
    }

    public static async Task DecompressXboxBlocksAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(inputPath);
        input.Position = FastFileConstants.StandardHeaderSize;
        await using var output = File.Create(outputPath);

        var lenBytes = new byte[2];
        var blockIndex = 0;
        while (input.Position + 2 <= input.Length)
        {
            await ReadExactlyAsync(input, lenBytes, cancellationToken);
            var compressedLength = EndianIO.ReadUInt16(lenBytes, Endian.Big);

            // Community tooling uses a tiny final marker; older notes also describe zero-length termination.
            if (compressedLength <= 1)
                break;

            if (input.Position + compressedLength > input.Length)
                throw new InvalidDataException($"Xbox block {blockIndex}: compressed length 0x{compressedLength:X} exceeds remaining file size.");

            var compressed = new byte[compressedLength];
            await ReadExactlyAsync(input, compressed, cancellationToken);

            await using var blockStream = new MemoryStream(compressed, writable: false);
            await using var deflate = new DeflateStream(blockStream, CompressionMode.Decompress, leaveOpen: false);
            await deflate.CopyToAsync(output, 1024 * 64, cancellationToken);
            blockIndex++;
        }

        if (blockIndex == 0)
            throw new InvalidDataException("No Xbox compressed blocks were found after the 12-byte fastfile header.");
    }

    public static async Task<DecompressionResult> DecompressXboxSignedAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(inputPath);
        if (input.Length < FastFileConstants.StandardHeaderSize + 8 + FastFileConstants.XboxSignedHashTableSize)
            throw new InvalidDataException("Signed Xbox fastfile is too small for the expected streaming header/hash table.");

        input.Position = FastFileConstants.StandardHeaderSize;
        var innerMagicBytes = new byte[8];
        await ReadExactlyAsync(input, innerMagicBytes, cancellationToken);
        var innerMagic = Encoding.ASCII.GetString(innerMagicBytes);
        if (innerMagic != FastFileConstants.StreamingMagic)
            throw new InvalidDataException($"Signed Xbox fastfile does not contain '{FastFileConstants.StreamingMagic}' at 0x0C.");

        input.Position += FastFileConstants.XboxSignedHashTableSize;
        await using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
        await using (var output = File.Create(outputPath))
            await zlib.CopyToAsync(output, 1024 * 128, cancellationToken);

        var header = await ReadZoneHeaderAsync(outputPath, Endian.Big, cancellationToken);
        if (!header.Plausible)
            throw new InvalidDataException("Signed Xbox fastfile decompressed, but its WaW zone header was not plausible.");

        return new DecompressionResult(WaWPlatform.Xbox360, WaWCompressionKind.XboxSignedSingleZlib,
            new FileInfo(outputPath).Length, header, outputPath);
    }

    private static async Task<ZoneHeaderSummary> ReadZoneHeaderAsync(string zonePath, Endian expectedEndian, CancellationToken cancellationToken)
    {
        await using var zone = File.OpenRead(zonePath);
        var headerBytes = new byte[FastFileConstants.WaWZoneHeaderSize];
        await ReadExactlyAsync(zone, headerBytes, cancellationToken);
        return ZoneHeaderParser.Parse(headerBytes, expectedEndian, zone.Length);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
    }

    private static void ReplaceFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(source, destination);
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
