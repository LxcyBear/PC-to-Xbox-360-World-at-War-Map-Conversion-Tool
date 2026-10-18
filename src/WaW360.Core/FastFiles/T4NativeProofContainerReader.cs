using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4ProofBlobDescriptor(
    string TypeName,
    string ContainerFile,
    long ContainerBlobOffset,
    long SourceOffset,
    int BlobLength,
    string? Name);

public sealed class T4ProofContainerIndex
{
    public required string TypeName { get; init; }
    public required string ContainerFile { get; init; }
    public required uint Version { get; init; }
    public required IReadOnlyList<T4ProofBlobDescriptor> Records { get; init; }
    public required long FileSize { get; init; }
    public required string Sha256 { get; init; }
}

/// <summary>
/// Reads the already-verified Stage 4 proof containers without loading their payloads into RAM.
/// Stage 5 uses these descriptors as a zero-copy source for the native writer plan.
/// </summary>
public static class T4NativeProofContainerReader
{
    private const int MagicSize = 8;

    public static async Task<T4ProofContainerIndex> IndexAsync(
        string path,
        string expectedType,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Serializer proof container not found.", path);

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var magicBytes = new byte[MagicSize];
        await ReadExactlyAsync(fs, magicBytes, cancellationToken);
        var magic = Encoding.ASCII.GetString(magicBytes).TrimEnd('\0');

        var type = magic switch
        {
            "W360A4X" => "xanim",
            "W360A5M" => "xmodel",
            "W360A7W" => "weapon",
            "W360A9B" => "fx",
            _ => throw new InvalidDataException($"Unknown Stage 4 proof-container magic '{magic}' in '{path}'.")
        };

        if (!type.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Proof-container type mismatch for '{path}': expected {expectedType}, found {type}.");

        var version = await ReadU32Async(fs, cancellationToken);
        var count = checked((int)await ReadU32Async(fs, cancellationToken));
        if (count < 0 || count > 250_000)
            throw new InvalidDataException($"Implausible proof record count {count:N0} in '{path}'.");

        if (type.Equals("weapon", StringComparison.OrdinalIgnoreCase))
        {
            var fixedBytes = await ReadU32Async(fs, cancellationToken);
            if (fixedBytes != 0x9AC)
                throw new InvalidDataException($"Unexpected WeaponDef fixed size 0x{fixedBytes:X} in '{path}'.");
        }

        var records = new List<T4ProofBlobDescriptor>(count);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceOffset = await ReadU32Async(fs, cancellationToken);
            var blobLengthU32 = await ReadU32Async(fs, cancellationToken);
            if (blobLengthU32 > int.MaxValue)
                throw new InvalidDataException($"Proof blob #{i} in '{path}' exceeds 2 GiB.");
            var blobLength = checked((int)blobLengthU32);

            string? name = null;
            if (type is "weapon" or "fx")
            {
                var nameLength = checked((int)await ReadU32Async(fs, cancellationToken));
                if (nameLength < 0 || nameLength > 1024 * 1024)
                    throw new InvalidDataException($"Proof record #{i} has implausible name length {nameLength:N0}.");
                var nameBytes = new byte[nameLength];
                await ReadExactlyAsync(fs, nameBytes, cancellationToken);
                name = Encoding.UTF8.GetString(nameBytes);
            }

            var blobOffset = fs.Position;
            if (blobLength < 0 || blobOffset > fs.Length - blobLength)
                throw new InvalidDataException($"Proof record #{i} in '{path}' overruns the container.");

            records.Add(new T4ProofBlobDescriptor(type, path, blobOffset, sourceOffset, blobLength, name));
            fs.Seek(blobLength, SeekOrigin.Current);
        }

        if (fs.Position != fs.Length)
            throw new InvalidDataException($"Proof container '{path}' has {fs.Length - fs.Position:N0} trailing byte(s); refusing an ambiguous index.");

        fs.Position = 0;
        using var sha = SHA256.Create();
        var digest = await sha.ComputeHashAsync(fs, cancellationToken);

        return new T4ProofContainerIndex
        {
            TypeName = type,
            ContainerFile = path,
            Version = version,
            Records = records,
            FileSize = fs.Length,
            Sha256 = Convert.ToHexString(digest).ToLowerInvariant()
        };
    }

    public static async Task<byte[]> ReadBlobPrefixAsync(
        T4ProofBlobDescriptor record,
        int prefixBytes,
        CancellationToken cancellationToken = default)
    {
        var count = Math.Min(record.BlobLength, Math.Max(0, prefixBytes));
        var buffer = new byte[count];
        await using var fs = new FileStream(record.ContainerFile, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        fs.Seek(record.ContainerBlobOffset, SeekOrigin.Begin);
        await ReadExactlyAsync(fs, buffer, cancellationToken);
        return buffer;
    }


    public static async Task<byte[]> ReadBlobAsync(
        T4ProofBlobDescriptor record,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[record.BlobLength];
        await using var fs = new FileStream(record.ContainerFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        fs.Seek(record.ContainerBlobOffset, SeekOrigin.Begin);
        await ReadExactlyAsync(fs, buffer, cancellationToken);
        return buffer;
    }

    private static async Task<uint> ReadU32Async(Stream s, CancellationToken cancellationToken)
    {
        var b = new byte[4];
        await ReadExactlyAsync(s, b, cancellationToken);
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    private static async Task ReadExactlyAsync(Stream s, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await s.ReadAsync(buffer[total..], cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            total += read;
        }
    }
}
