using System.Buffers.Binary;
using System.Text;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Linker;

public sealed record T4XboxLoadAssetReadback(
    int Index,
    int TypeId,
    string TypeName,
    string Name,
    int SerializedStart,
    int SerializedEnd,
    int RuntimeOffset,
    int RuntimeLength,
    ushort Width,
    ushort Height);

public sealed class T4XboxLoadReadbackResult
{
    public required int AssetCount { get; init; }
    public required int NonRuntimeEnd { get; init; }
    public required int ZoneLength { get; init; }
    public required int LargeRuntimeBytes { get; init; }
    public required bool ExactSerializedWalk { get; init; }
    public required IReadOnlyList<T4XboxLoadAssetReadback> Assets { get; init; }
}

/// <summary>
/// Deterministic reader for the small Xbox load-zone subset emitted by Linker V1 alpha 2.
/// It walks the XAsset table and typed payloads; it never searches for names or structures.
/// </summary>
public static class T4XboxLoadZoneReader
{
    private const int TechniqueSetSize = 216;
    private const int TechniqueSlots = 51;
    private const int MaterialSize = 96;
    private const int TextureDefSize = 16;
    private const int ConstantDefSize = 32;
    private const int StateBitsSize = 8;
    private const int ImageSize = 40;
    private const int ImagePlatformPayloadSize = 68;
    private const int RawFileSize = 12;

    public static T4XboxLoadReadbackResult Read(ReadOnlySpan<byte> zone)
    {
        var document = T4ZoneParser.ParseZoneBytes(
            "<linker-v1-alpha2-generated>", zone, WaWPlatform.Xbox360,
            WaWCompressionKind.XboxSingleZlib, Endian.Big);

        var largeRuntime = checked((int)document.Header.BlockSizes[(int)T4ZoneBlock.LargeRuntime]);
        var nonRuntimeEnd = checked(zone.Length - largeRuntime);
        if (nonRuntimeEnd < 0)
            throw new InvalidDataException("Xbox load-zone LargeRuntime size exceeds zone length.");

        var cursor = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));
        var runtimeCursor = 0;
        var rows = new List<T4XboxLoadAssetReadback>(document.AssetCount);

        foreach (var asset in document.Assets.OrderBy(a => a.Index))
        {
            if (asset.ReferenceKind != T4AssetReferenceKind.Inline)
                throw new InvalidDataException($"Xbox load XAsset#{asset.Index} is not an INSERT physical asset.");

            var start = cursor;
            var name = string.Empty;
            var runtimeOffset = 0;
            var runtimeLength = 0;
            ushort width = 0, height = 0;

            switch (asset.TypeName.ToLowerInvariant())
            {
                case "techset":
                {
                    Ensure(zone, cursor, TechniqueSetSize, "Xbox TechniqueSet");
                    var namePtr = U32(zone, cursor);
                    if (namePtr != T4ZonePointer.Following)
                        throw new InvalidDataException("Alpha-2 Xbox TechniqueSet name must be FOLLOWING/inline.");
                    var remapped = U32(zone, cursor + 8);
                    if (remapped != 0)
                        throw new InvalidDataException("Alpha-2 Xbox TechniqueSet remappedTechniqueSet must be null.");
                    for (var i = 0; i < TechniqueSlots; i++)
                        if (U32(zone, cursor + 12 + i * 4) != 0)
                            throw new InvalidDataException($"Alpha-2 Xbox TechniqueSet technique[{i}] is non-null; shader graph walking is outside this stage.");
                    cursor += TechniqueSetSize;
                    name = ReadAsciiZ(zone, ref cursor, nonRuntimeEnd, "TechniqueSet.name");
                    break;
                }
                case "material":
                {
                    Ensure(zone, cursor, MaterialSize, "Xbox Material");
                    var namePtr = U32(zone, cursor);
                    var textureCount = zone[cursor + 75];
                    var constantCount = zone[cursor + 76];
                    var stateCount = zone[cursor + 77];
                    var texturePtr = U32(zone, cursor + 84);
                    var constantPtr = U32(zone, cursor + 88);
                    var statePtr = U32(zone, cursor + 92);
                    if (namePtr != T4ZonePointer.Following)
                        throw new InvalidDataException("Alpha-2 Xbox Material name must be FOLLOWING/inline.");
                    cursor += MaterialSize;
                    name = ReadAsciiZ(zone, ref cursor, nonRuntimeEnd, "Material.name");

                    if (texturePtr != 0)
                    {
                        if (texturePtr != T4ZonePointer.Following)
                            throw new InvalidDataException($"Material '{name}' texture table is not FOLLOWING.");
                        Ensure(zone, cursor, checked(textureCount * TextureDefSize), $"Material '{name}' texture table");
                        cursor += textureCount * TextureDefSize;
                    }
                    if (constantPtr != 0)
                    {
                        if (constantPtr != T4ZonePointer.Following)
                            throw new InvalidDataException($"Material '{name}' constant table is not FOLLOWING.");
                        Ensure(zone, cursor, checked(constantCount * ConstantDefSize), $"Material '{name}' constant table");
                        cursor += constantCount * ConstantDefSize;
                    }
                    if (statePtr != 0)
                    {
                        if (statePtr != T4ZonePointer.Following)
                            throw new InvalidDataException($"Material '{name}' state-bits table is not FOLLOWING.");
                        Ensure(zone, cursor, checked(stateCount * StateBitsSize), $"Material '{name}' state-bits table");
                        cursor += stateCount * StateBitsSize;
                    }
                    break;
                }
                case "image":
                {
                    Ensure(zone, cursor, ImageSize, "Xbox GfxImage");
                    var platformPtr = U32(zone, cursor + 4);
                    runtimeLength = checked((int)U32(zone, cursor + 12));
                    width = U16(zone, cursor + 16);
                    height = U16(zone, cursor + 18);
                    var namePtr = U32(zone, cursor + 36);
                    if (namePtr != T4ZonePointer.Following)
                        throw new InvalidDataException("Alpha-2 Xbox GfxImage name must be FOLLOWING/inline.");
                    cursor += ImageSize;
                    name = ReadAsciiZ(zone, ref cursor, nonRuntimeEnd, "GfxImage.name");
                    if (platformPtr != T4ZonePointer.Insert && platformPtr != T4ZonePointer.Following)
                        throw new InvalidDataException($"Image '{name}' platform payload pointer is {T4ZonePointer.Describe(platformPtr)}, expected INSERT/FOLLOWING.");
                    Ensure(zone, cursor, ImagePlatformPayloadSize, $"Image '{name}' platform payload");
                    cursor += ImagePlatformPayloadSize;
                    runtimeOffset = runtimeCursor;
                    runtimeCursor = checked(runtimeCursor + runtimeLength);
                    break;
                }
                case "rawfile":
                {
                    Ensure(zone, cursor, RawFileSize, "Xbox RawFile");
                    var namePtr = U32(zone, cursor);
                    var length = checked((int)U32(zone, cursor + 4));
                    var bufferPtr = U32(zone, cursor + 8);
                    if (namePtr != T4ZonePointer.Following)
                        throw new InvalidDataException("Alpha-2 Xbox RawFile name must be FOLLOWING/inline.");
                    cursor += RawFileSize;
                    name = ReadAsciiZ(zone, ref cursor, nonRuntimeEnd, "RawFile.name");
                    if (bufferPtr != 0)
                    {
                        if (bufferPtr != T4ZonePointer.Following)
                            throw new InvalidDataException($"RawFile '{name}' buffer is not FOLLOWING.");
                        Ensure(zone, cursor, checked(length + 1), $"RawFile '{name}' buffer");
                        cursor += length + 1;
                    }
                    else if (length != 0)
                    {
                        throw new InvalidDataException($"RawFile '{name}' has null buffer with non-zero length {length}.");
                    }
                    break;
                }
                default:
                    throw new InvalidDataException($"Alpha-2 Xbox load read-back has no deterministic walker for '{asset.TypeName}'.");
            }

            rows.Add(new T4XboxLoadAssetReadback(
                asset.Index, asset.TypeId, asset.TypeName, name, start, cursor,
                runtimeOffset, runtimeLength, width, height));
        }

        var exact = cursor == nonRuntimeEnd && runtimeCursor == largeRuntime;
        return new T4XboxLoadReadbackResult
        {
            AssetCount = document.AssetCount,
            NonRuntimeEnd = nonRuntimeEnd,
            ZoneLength = zone.Length,
            LargeRuntimeBytes = largeRuntime,
            ExactSerializedWalk = exact,
            Assets = rows
        };
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset)
    {
        Ensure(data, offset, 4, "uint32");
        return BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    }

    private static ushort U16(ReadOnlySpan<byte> data, int offset)
    {
        Ensure(data, offset, 2, "uint16");
        return BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    }

    private static string ReadAsciiZ(ReadOnlySpan<byte> data, ref int cursor, int limit, string label)
    {
        var start = cursor;
        while (cursor < limit && data[cursor] != 0) cursor++;
        if (cursor >= limit) throw new InvalidDataException($"Unterminated {label} at 0x{start:X}.");
        var value = Encoding.ASCII.GetString(data.Slice(start, cursor - start));
        cursor++;
        return value;
    }

    private static void Ensure(ReadOnlySpan<byte> data, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException($"{label} at 0x{offset:X} ({length:N0} bytes) exceeds zone length 0x{data.Length:X}.");
    }
}
