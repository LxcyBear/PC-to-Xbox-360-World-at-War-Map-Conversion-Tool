using System.IO.Compression;

namespace WaW360.Core.FastFiles;

public sealed record FastFileRepackResult(
    string InputFastFile,
    string OutputFastFile,
    WaWPlatform Platform,
    WaWCompressionKind Compression,
    long ZoneBytes,
    long OutputBytes,
    bool VerifiedByteForByte);

public static class FastFileRepacker
{

    public static async Task RepackZoneFileAsync(
        string templateFastFile,
        string rawZoneFile,
        string outputFastFile,
        WaWCompressionKind compression,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(templateFastFile))
            throw new FileNotFoundException("Template fastfile not found.", templateFastFile);
        if (!File.Exists(rawZoneFile))
            throw new FileNotFoundException("Raw zone file not found.", rawZoneFile);
        if (compression == WaWCompressionKind.XboxSignedSingleZlib)
            throw new NotSupportedException("Signed/authenticated Xbox fastfiles are not rewritten yet.");

        var wrapperHeader = await BuildWrapperHeaderAsync(templateFastFile, compression, cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFastFile))!);
        await using var output = File.Create(outputFastFile);
        await output.WriteAsync(wrapperHeader, cancellationToken);

        switch (compression)
        {
            case WaWCompressionKind.PcSingleZlib:
            case WaWCompressionKind.XboxSingleZlib:
                await WriteSingleZlibAsync(rawZoneFile, output, cancellationToken);
                break;

            case WaWCompressionKind.XboxBlockDeflate:
                await WriteXboxBlocksAsync(rawZoneFile, output, cancellationToken);
                break;

            default:
                throw new NotSupportedException($"Fastfile writing is not implemented for {compression}.");
        }
    }
    public static async Task<FastFileRepackResult> RoundTripAsync(
        string inputFastFile,
        string outputFastFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFastFile))
            throw new FileNotFoundException("Fastfile not found.", inputFastFile);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "roundtrip", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var originalZone = Path.Combine(tempDir, "original.zone");
        var verifyZone = Path.Combine(tempDir, "verify.zone");

        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(inputFastFile, originalZone, cancellationToken);
            if (decomp.Compression == WaWCompressionKind.XboxSignedSingleZlib)
                throw new NotSupportedException("Stage 2 round-trip writing does not rewrite signed/authenticated Xbox fastfiles yet.");

            var wrapperHeader = new byte[FastFileConstants.StandardHeaderSize];
            await using (var input = File.OpenRead(inputFastFile))
                await ReadExactlyAsync(input, wrapperHeader, cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFastFile))!);
            await using (var output = File.Create(outputFastFile))
            {
                await output.WriteAsync(wrapperHeader, cancellationToken);

                switch (decomp.Compression)
                {
                    case WaWCompressionKind.PcSingleZlib:
                    case WaWCompressionKind.XboxSingleZlib:
                        await WriteSingleZlibAsync(originalZone, output, cancellationToken);
                        break;

                    case WaWCompressionKind.XboxBlockDeflate:
                        await WriteXboxBlocksAsync(originalZone, output, cancellationToken);
                        break;

                    default:
                        throw new NotSupportedException($"Round-trip writing is not implemented for {decomp.Compression}.");
                }
            }

            var verify = await FastFileDecompressor.DecompressAutoAsync(outputFastFile, verifyZone, cancellationToken);
            var equal = await FilesEqualAsync(originalZone, verifyZone, cancellationToken);
            if (!equal)
                throw new InvalidDataException("The repacked fastfile decompressed successfully, but its raw zone bytes differ from the original.");

            return new FastFileRepackResult(
                inputFastFile,
                outputFastFile,
                verify.Platform,
                verify.Compression,
                new FileInfo(originalZone).Length,
                new FileInfo(outputFastFile).Length,
                true);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static async Task<byte[]> BuildWrapperHeaderAsync(
        string templateFastFile,
        WaWCompressionKind compression,
        CancellationToken cancellationToken)
    {
        if (compression is WaWCompressionKind.XboxSingleZlib or WaWCompressionKind.XboxBlockDeflate)
        {
            var header = new byte[FastFileConstants.StandardHeaderSize];
            System.Text.Encoding.ASCII.GetBytes(FastFileConstants.UnsignedMagic).CopyTo(header, 0);
            // Xbox T4 stores 0x183 big-endian in the wrapper. Never inherit the little-endian
            // PC version bytes (83 01 00 00) from a PC source fastfile.
            header[8] = 0x00;
            header[9] = 0x00;
            header[10] = 0x01;
            header[11] = 0x83;
            return header;
        }

        var original = new byte[FastFileConstants.StandardHeaderSize];
        await using var input = File.OpenRead(templateFastFile);
        await ReadExactlyAsync(input, original, cancellationToken);
        return original;
    }

    private static async Task WriteSingleZlibAsync(string zonePath, Stream output, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(zonePath);
        await using var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true);
        await input.CopyToAsync(zlib, 128 * 1024, cancellationToken);
    }

    private static async Task WriteXboxBlocksAsync(string zonePath, Stream output, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(zonePath);
        var rawBlock = new byte[FastFileConstants.XboxZoneBlockSize];

        while (true)
        {
            var count = await ReadUpToAsync(input, rawBlock, cancellationToken);
            if (count == 0) break;

            await using var compressed = new MemoryStream();
            await using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                await deflate.WriteAsync(rawBlock.AsMemory(0, count), cancellationToken);

            if (compressed.Length > ushort.MaxValue)
                throw new InvalidDataException(
                    $"A 64 KiB Xbox block compressed to {compressed.Length:N0} bytes, which does not fit the two-byte block length field.");

            var length = new byte[2];
            var value = checked((ushort)compressed.Length);
            length[0] = (byte)(value >> 8);
            length[1] = (byte)value;
            await output.WriteAsync(length, cancellationToken);

            compressed.Position = 0;
            await compressed.CopyToAsync(output, 64 * 1024, cancellationToken);
        }

        // Explicit zero-length terminator; the Stage 1 reader also accepts EOF.
        await output.WriteAsync(new byte[] { 0, 0 }, cancellationToken);
    }

    private static async Task<int> ReadUpToAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    private static async Task<bool> FilesEqualAsync(string leftPath, string rightPath, CancellationToken cancellationToken)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);
        if (leftInfo.Length != rightInfo.Length) return false;

        await using var left = File.OpenRead(leftPath);
        await using var right = File.OpenRead(rightPath);
        var leftBuffer = new byte[128 * 1024];
        var rightBuffer = new byte[leftBuffer.Length];

        while (true)
        {
            var leftRead = await left.ReadAsync(leftBuffer, cancellationToken);
            var rightRead = await right.ReadAsync(rightBuffer, cancellationToken);
            if (leftRead != rightRead) return false;
            if (leftRead == 0) return true;
            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead))) return false;
        }
    }
}
