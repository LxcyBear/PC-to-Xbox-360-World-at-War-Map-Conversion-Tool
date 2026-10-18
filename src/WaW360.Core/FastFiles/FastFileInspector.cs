using System.Text;

namespace WaW360.Core.FastFiles;

public static class FastFileInspector
{
    public static async Task<FastFileMetadata> InspectAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("Fastfile not found.", filePath);

        await using var stream = File.OpenRead(filePath);
        if (stream.Length < FastFileConstants.StandardHeaderSize)
            throw new InvalidDataException("File is smaller than a standard CoD fastfile header.");

        var header = new byte[FastFileConstants.StandardHeaderSize];
        var read = await stream.ReadAsync(header, cancellationToken);
        if (read != header.Length) throw new EndOfStreamException();

        var magic = Encoding.ASCII.GetString(header, 0, 8);
        var versionBytes = header[8..12];
        var beVersion = (int)EndianIO.ReadUInt32(versionBytes, Endian.Big);
        var leVersion = (int)EndianIO.ReadUInt32(versionBytes, Endian.Little);
        var version = beVersion == FastFileConstants.WaWVersion ? beVersion : leVersion;

        var signature = magic switch
        {
            FastFileConstants.UnsignedMagic => FastFileSignatureKind.Unsigned,
            FastFileConstants.SignedMagic => FastFileSignatureKind.Signed,
            FastFileConstants.StreamingMagic => FastFileSignatureKind.Streaming,
            _ => FastFileSignatureKind.Unknown
        };

        var warnings = new List<string>();
        if (version != FastFileConstants.WaWVersion)
            warnings.Add($"Version 0x{version:X} is not the expected WaW version 0x{FastFileConstants.WaWVersion:X}.");
        if (signature == FastFileSignatureKind.Unknown)
            warnings.Add($"Unknown fastfile magic '{magic}'.");

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(filePath) + ".zone");

        try
        {
            var decompressed = await FastFileDecompressor.DecompressAutoAsync(filePath, zonePath, cancellationToken);
            return new FastFileMetadata(
                filePath,
                magic,
                versionBytes,
                version,
                signature,
                decompressed.Platform,
                decompressed.Compression,
                stream.Length,
                decompressed.DecompressedSize,
                decompressed.ZoneHeader,
                warnings);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            warnings.Add("Decompression probe failed: " + ex.Message);
            return new FastFileMetadata(filePath, magic, versionBytes, version, signature,
                WaWPlatform.Unknown, WaWCompressionKind.Unknown, stream.Length, 0, null, warnings);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
