using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4XboxSerializedProofAsset(
    string TypeName,
    int XboxTypeId,
    string Name,
    int SerializedBytes,
    string Note);

public sealed class T4XboxSerializerProofResult
{
    public required string SourceFastFile { get; init; }
    public required string OutputFile { get; init; }
    public required IReadOnlyList<T4XboxSerializedProofAsset> Assets { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool Verified { get; init; }
}

/// <summary>
/// First real Xbox serializer code used by Stage 4 alpha 2. This deliberately
/// emits a proof stream, NOT a fastfile. It lets us validate big-endian struct
/// emission, Xbox XAsset type remapping, strings, counts and pointer sentinels
/// before the still-missing world serializers are allowed to produce a map FF.
/// </summary>
public static class T4XboxSerializerFoundation
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("W360A2\0\0");

    public static async Task<T4XboxSerializerProofResult> BuildProofAsync(
        string sourceFastFile,
        string outputFile,
        CancellationToken cancellationToken = default)
    {
        var scan = await T4PayloadScanner.ScanFastFileAsync(sourceFastFile, cancellationToken);
        var assets = new List<T4XboxSerializedProofAsset>();
        var warnings = scan.Warnings.ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
        await using (var fs = File.Create(outputFile))
        {
            await fs.WriteAsync(Magic, cancellationToken);
            WriteU32(fs, 2); // format version

            var selected = new List<Action<Stream>>();

            foreach (var raw in scan.RawFiles)
            {
                selected.Add(s => WriteRawFile(s, raw));
                assets.Add(new T4XboxSerializedProofAsset(
                    "rawfile", T4XboxAssetTypeMap.GetXboxTypeId("rawfile"), raw.Name,
                    12 + Utf8Z(raw.Name).Length + raw.Buffer.Length,
                    "TEMP fixed struct; inline name and len+1 buffer proof"));
            }

            foreach (var loc in scan.LocalizeEntries)
            {
                selected.Add(s => WriteLocalize(s, loc));
                assets.Add(new T4XboxSerializedProofAsset(
                    "localize", T4XboxAssetTypeMap.GetXboxTypeId("localize"), loc.Name,
                    8 + Utf8Z(loc.Name).Length + Utf8Z(loc.Value).Length,
                    "TEMP fixed struct; inline name/value strings proof"));
            }

            foreach (var table in scan.StringTables)
            {
                selected.Add(s => WriteStringTable(s, table));
                assets.Add(new T4XboxSerializedProofAsset(
                    "stringtable", T4XboxAssetTypeMap.GetXboxTypeId("stringtable"), table.Name,
                    16 + Utf8Z(table.Name).Length + table.Values.Count * 4 + table.Values.Where(v => v is not null).Sum(v => Utf8Z(v!).Length),
                    $"{table.RowCount}x{table.ColumnCount}; big-endian count/pointer proof"));
            }

            // MenuList contains subasset menu references whose Xbox serialization is being handled
            // separately with the Stage 3 menu work. Record it, but do not pretend its child menu
            // graph is converted by this foundation writer yet.
            foreach (var menu in scan.MenuLists)
                warnings.Add($"menulist '{menu.Name}' resolved, but child menu serialization remains blocked until the menu writer is integrated.");

            WriteU32(fs, checked((uint)selected.Count));
            foreach (var writer in selected)
                writer(fs);
        }

        var verified = VerifyProof(outputFile, assets.Count);
        if (!verified)
            throw new InvalidDataException("Stage 4 alpha 2 serializer proof did not read back correctly.");

        return new T4XboxSerializerProofResult
        {
            SourceFastFile = sourceFastFile,
            OutputFile = outputFile,
            Assets = assets,
            Warnings = warnings,
            Verified = true
        };
    }

    private static void WriteRawFile(Stream s, T4RawFileData raw)
    {
        WriteRecordHeader(s, "rawfile");
        WriteU32(s, T4ZonePointer.Following);
        WriteU32(s, checked((uint)raw.Length));
        WriteU32(s, raw.Buffer.Length == 0 ? 0u : T4ZonePointer.Following);
        WriteBytes(s, Utf8Z(raw.Name));

        // Payload is copied byte-for-byte from the PC rawfile; only the fixed struct/count/pointers are re-emitted.
        if (raw.Buffer.Length > 0)
            WriteBytes(s, raw.Buffer);
    }

    private static void WriteLocalize(Stream s, T4LocalizeData loc)
    {
        WriteRecordHeader(s, "localize");
        WriteU32(s, T4ZonePointer.Following); // value
        WriteU32(s, T4ZonePointer.Following); // name
        WriteBytes(s, Utf8Z(loc.Value));
        WriteBytes(s, Utf8Z(loc.Name));
    }

    private static void WriteStringTable(Stream s, T4StringTableData table)
    {
        WriteRecordHeader(s, "stringtable");
        WriteU32(s, T4ZonePointer.Following); // name
        WriteU32(s, checked((uint)table.ColumnCount));
        WriteU32(s, checked((uint)table.RowCount));
        WriteU32(s, T4ZonePointer.Following); // values
        WriteBytes(s, Utf8Z(table.Name));
        foreach (var value in table.Values)
            WriteU32(s, value is null ? 0u : T4ZonePointer.Following);
        foreach (var value in table.Values)
            if (value is not null)
                WriteBytes(s, Utf8Z(value));
    }

    private static void WriteRecordHeader(Stream s, string typeName)
    {
        var typeBytes = Encoding.ASCII.GetBytes(typeName);
        WriteU32(s, checked((uint)T4XboxAssetTypeMap.GetXboxTypeId(typeName)));
        WriteU32(s, checked((uint)typeBytes.Length));
        WriteBytes(s, typeBytes);
    }

    private static bool VerifyProof(string path, int expectedRecords)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 16 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return false;
        var version = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4));
        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12, 4));
        return version == 2 && count == (uint)expectedRecords;
    }

    private static byte[] Utf8Z(string value)
    {
        var data = Encoding.UTF8.GetBytes(value ?? string.Empty);
        var output = new byte[data.Length + 1];
        data.CopyTo(output, 0);
        return output;
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }

    private static void WriteBytes(Stream stream, ReadOnlySpan<byte> value) => stream.Write(value);
}
