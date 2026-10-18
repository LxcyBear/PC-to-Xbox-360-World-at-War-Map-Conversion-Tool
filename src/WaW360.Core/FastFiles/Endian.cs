using System.Buffers.Binary;

namespace WaW360.Core.FastFiles;

public enum Endian
{
    Little,
    Big
}

public static class EndianIO
{
    public static ushort ReadUInt16(ReadOnlySpan<byte> bytes, Endian endian) =>
        endian == Endian.Big
            ? BinaryPrimitives.ReadUInt16BigEndian(bytes)
            : BinaryPrimitives.ReadUInt16LittleEndian(bytes);

    public static uint ReadUInt32(ReadOnlySpan<byte> bytes, Endian endian) =>
        endian == Endian.Big
            ? BinaryPrimitives.ReadUInt32BigEndian(bytes)
            : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
}
