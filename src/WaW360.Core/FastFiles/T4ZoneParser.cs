using System.Text;

namespace WaW360.Core.FastFiles;

public static class T4ZoneParser
{
    private const uint InlinePointer = 0xFFFFFFFFu;

    public static async Task<T4ZoneDocument> ParseFastFileAsync(
        string fastFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(fastFilePath))
            throw new FileNotFoundException("Fastfile not found.", fastFilePath);

        var cached = await FastFileAnalysisCache.GetZoneAsync(fastFilePath, cancellationToken);
        return cached.Document;
    }

    public static T4ZoneDocument ParseZoneBytes(
        string sourceFastFile,
        ReadOnlySpan<byte> zoneBytes,
        WaWPlatform platform,
        WaWCompressionKind compression,
        Endian endian)
    {
        if (zoneBytes.Length < FastFileConstants.WaWZoneHeaderSize)
            throw new InvalidDataException("The decompressed T4 zone is too small to contain XFile + XAssetList.");

        var header = ZoneHeaderParser.Parse(zoneBytes[..FastFileConstants.T4XFileHeaderSize], endian, zoneBytes.Length);
        if (!header.Plausible)
            throw new InvalidDataException("The decompressed data does not contain a plausible T4 XFile header.");

        // T4 XAssetList immediately follows the 36-byte XFile header.
        var listOffset = FastFileConstants.T4XFileHeaderSize;
        var scriptStringCount = checked((int)ReadU32(zoneBytes, listOffset + 0, endian));
        var scriptStringsPointer = ReadU32(zoneBytes, listOffset + 4, endian);
        var assetCount = checked((int)ReadU32(zoneBytes, listOffset + 8, endian));
        var assetsPointer = ReadU32(zoneBytes, listOffset + 12, endian);

        ValidateCount("script string", scriptStringCount, 1_000_000);
        ValidateCount("asset", assetCount, 250_000);

        var cursor = FastFileConstants.WaWZoneHeaderSize;
        var pointerTableBytes = checked(scriptStringCount * 4);
        EnsureRange(zoneBytes, cursor, pointerTableBytes, "script-string pointer table");

        var rawStringPointers = new uint[scriptStringCount];
        for (var i = 0; i < scriptStringCount; i++)
            rawStringPointers[i] = ReadU32(zoneBytes, cursor + i * 4, endian);

        var stringEntries = new List<T4ScriptStringEntry>(scriptStringCount);
        cursor += pointerTableBytes;

        // The ScriptString pointer array is the first object written after T4 pushes
        // XFILE_BLOCK_VIRTUAL, so its first byte is VIRTUAL+0. Inline string bodies
        // then follow that pointer array in index order. Reused strings are encoded
        // as packed VIRTUAL offsets to an already-written inline body. Track those
        // exact body starts so aliases can be resolved without guessing a physical
        // file address or treating the pointer value as a string identity.
        var inlineStringsByVirtualOffset = new Dictionary<uint, string>();

        for (var i = 0; i < rawStringPointers.Length; i++)
        {
            var rawPointer = rawStringPointers[i];
            if (rawPointer == 0)
            {
                stringEntries.Add(new T4ScriptStringEntry(i, rawPointer, null, false));
                continue;
            }

            if (rawPointer == InlinePointer)
            {
                var virtualOffset = checked((uint)(cursor - FastFileConstants.WaWZoneHeaderSize));
                var (value, bytesRead) = ReadCString(zoneBytes, cursor);
                stringEntries.Add(new T4ScriptStringEntry(i, rawPointer, value, true));
                inlineStringsByVirtualOffset[virtualOffset] = value;
                cursor += bytesRead;
                continue;
            }

            var decoded = T4ZonePointer.Decode(rawPointer);
            if (decoded.Kind == T4ZonePointerKind.Offset &&
                decoded.Block == T4ZoneBlock.Virtual &&
                inlineStringsByVirtualOffset.TryGetValue(decoded.Offset, out var aliasedValue))
            {
                stringEntries.Add(new T4ScriptStringEntry(i, rawPointer, aliasedValue, false));
                continue;
            }

            // Preserve unresolved offsets faithfully. Callers that require a complete
            // ScriptString index map must still fail closed on this marker.
            stringEntries.Add(new T4ScriptStringEntry(i, rawPointer, $"<alias 0x{rawPointer:X8}>", false));
        }

        var assetTableOffset = cursor;
        var assetBytes = checked(assetCount * 8);
        EnsureRange(zoneBytes, assetTableOffset, assetBytes, "XAsset table");

        var maxType = T4AssetTypeCatalog.MaxTypeId(platform);
        var assets = new List<T4AssetEntry>(assetCount);
        for (var i = 0; i < assetCount; i++)
        {
            var entryOffset = assetTableOffset + i * 8;
            var typeId = checked((int)ReadU32(zoneBytes, entryOffset, endian));
            var rawHeader = ReadU32(zoneBytes, entryOffset + 4, endian);

            if (typeId < 0 || typeId > maxType)
                throw new InvalidDataException(
                    $"Asset #{i} at zone offset 0x{entryOffset:X} has type {typeId}, outside the expected T4 {platform} range 0..{maxType}. " +
                    "This usually means the script-string walk or platform detection is wrong.");

            assets.Add(new T4AssetEntry(
                i,
                typeId,
                T4AssetTypeCatalog.GetName(typeId, platform),
                rawHeader,
                T4AssetEntry.Classify(rawHeader)));
        }

        return new T4ZoneDocument
        {
            SourceFastFile = sourceFastFile,
            Platform = platform,
            Compression = compression,
            Endianness = endian,
            Header = header,
            ScriptStringCount = scriptStringCount,
            ScriptStringsPointer = scriptStringsPointer,
            AssetCount = assetCount,
            AssetsPointer = assetsPointer,
            ScriptStringPointerTableOffset = FastFileConstants.WaWZoneHeaderSize,
            AssetTableOffset = assetTableOffset,
            ScriptStrings = stringEntries,
            Assets = assets
        };
    }

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset, Endian endian)
    {
        EnsureRange(data, offset, 4, "uint32");
        return EndianIO.ReadUInt32(data.Slice(offset, 4), endian);
    }

    private static (string Value, int BytesRead) ReadCString(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset >= data.Length)
            throw new InvalidDataException($"String offset 0x{offset:X} is outside the zone.");

        var end = offset;
        const int maxStringBytes = 1024 * 1024;
        while (end < data.Length && data[end] != 0)
        {
            end++;
            if (end - offset > maxStringBytes)
                throw new InvalidDataException($"Inline script string at 0x{offset:X} exceeds {maxStringBytes:N0} bytes.");
        }

        if (end >= data.Length)
            throw new InvalidDataException($"Unterminated inline script string at zone offset 0x{offset:X}.");

        var value = Encoding.UTF8.GetString(data.Slice(offset, end - offset));
        return (value, (end - offset) + 1);
    }

    private static void ValidateCount(string label, int count, int max)
    {
        if (count < 0 || count > max)
            throw new InvalidDataException($"Implausible T4 {label} count: {count:N0}.");
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string description)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException($"{description} at 0x{offset:X} ({length:N0} bytes) exceeds the decompressed zone ({data.Length:N0} bytes).");
    }
}
