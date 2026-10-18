using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4SimpleNativeAsset(
    int AssetIndex,
    string TypeName,
    string Name,
    byte[] Payload,
    T4AssetReferenceKind ReferenceKind,
    T4ZoneBlock Block,
    uint Alignment);

public sealed class T4SimpleNativeAssetScan
{
    public required IReadOnlyDictionary<int, T4SimpleNativeAsset> AssetsByIndex { get; init; }
    public required IReadOnlyDictionary<string, int> PromotedByType { get; init; }
    public required IReadOnlyDictionary<string, int> UnresolvedByType { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Alpha 10e promotion of the Stage-4 foundation serializers into the native writer.
/// These T4 asset layouts are identical 32-bit logical structures on PC/Xbox and only
/// require big-endian integer/pointer emission plus their inline-owned strings/data.
///
/// Fail-closed rules:
/// - only physical Inline/InlineShared XAsset entries are promoted;
/// - source payload count must exactly match the physical XAsset count for the type;
/// - StringTable cells may only be null or inline; packed/shared cell aliases remain blocked;
/// - no guessed index mapping is performed.
/// </summary>
public static class T4SimpleNativeAssetSerializer
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "rawfile", "localize", "stringtable"
    };

    public static bool IsSupported(string typeName) => Supported.Contains(typeName);

    public static T4SimpleNativeAssetScan Build(T4ZoneDocument document, byte[] zone)
    {
        var scan = T4PayloadScanner.Scan(document, zone);
        var output = new Dictionary<int, T4SimpleNativeAsset>();
        var promoted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var warnings = scan.Warnings.ToList();

        PromoteRawFiles(document, scan.RawFiles, output, promoted, unresolved, warnings);
        PromoteLocalize(document, scan.LocalizeEntries, output, promoted, unresolved, warnings);
        PromoteStringTables(document, scan.StringTables, output, promoted, unresolved, warnings);

        return new T4SimpleNativeAssetScan
        {
            AssetsByIndex = output,
            PromotedByType = promoted,
            UnresolvedByType = unresolved,
            Warnings = warnings
        };
    }

    private static void PromoteRawFiles(
        T4ZoneDocument document,
        IReadOnlyList<T4RawFileData> payloads,
        Dictionary<int, T4SimpleNativeAsset> output,
        Dictionary<string, int> promoted,
        Dictionary<string, int> unresolved,
        List<string> warnings)
    {
        const string type = "rawfile";
        var assets = PhysicalAssets(document, type);
        if (assets.Count == 0) return;
        if (payloads.Count != assets.Count)
        {
            unresolved[type] = assets.Count;
            warnings.Add($"native rawfile promotion: physical XAsset count={assets.Count:N0}, scanner payload count={payloads.Count:N0}; type left blocked.");
            return;
        }

        for (var i = 0; i < assets.Count; i++)
        {
            var a = assets[i];
            var raw = payloads[i];
            using var ms = new MemoryStream(12 + raw.Name.Length + 1 + raw.Buffer.Length);
            WriteU32(ms, T4ZonePointer.Following); // name
            WriteU32(ms, checked((uint)raw.Length));
            WriteU32(ms, raw.Buffer.Length == 0 ? 0u : T4ZonePointer.Following);
            WriteBytes(ms, AsciiZ(raw.Name));
            if (raw.Buffer.Length != 0) WriteBytes(ms, raw.Buffer);
            output[a.Index] = new T4SimpleNativeAsset(a.Index, type, raw.Name, ms.ToArray(), a.ReferenceKind, T4ZoneBlock.Temp, 4);
        }
        promoted[type] = assets.Count;
    }

    private static void PromoteLocalize(
        T4ZoneDocument document,
        IReadOnlyList<T4LocalizeData> payloads,
        Dictionary<int, T4SimpleNativeAsset> output,
        Dictionary<string, int> promoted,
        Dictionary<string, int> unresolved,
        List<string> warnings)
    {
        const string type = "localize";
        var assets = PhysicalAssets(document, type);
        if (assets.Count == 0) return;
        if (payloads.Count != assets.Count)
        {
            unresolved[type] = assets.Count;
            warnings.Add($"native localize promotion: physical XAsset count={assets.Count:N0}, scanner payload count={payloads.Count:N0}; type left blocked.");
            return;
        }

        var built = 0;
        for (var i = 0; i < assets.Count; i++)
        {
            var a = assets[i];
            var loc = payloads[i];
            // A source packed value aliases some other zone-block bytes. Its content
            // must be resolved and explicitly rebased before emission. Do not
            // serialize an empty value or an unrebased PC packed pointer.
            if (loc.SourceValuePointer is not (T4ZonePointer.Following or T4ZonePointer.Insert))
            {
                warnings.Add($"native localize promotion: '{loc.Name}' has unresolved shared VALUE {T4ZonePointer.Describe(loc.SourceValuePointer)}; XAsset#{a.Index} remains blocked.");
                continue;
            }
            using var ms = new MemoryStream(8 + loc.Name.Length + loc.Value.Length + 2);
            WriteU32(ms, T4ZonePointer.Following); // value
            WriteU32(ms, T4ZonePointer.Following); // name
            WriteBytes(ms, Latin1Z(loc.Value));
            WriteBytes(ms, AsciiZ(loc.Name));
            output[a.Index] = new T4SimpleNativeAsset(a.Index, type, loc.Name, ms.ToArray(), a.ReferenceKind, T4ZoneBlock.Temp, 4);
            built++;
        }
        if (built > 0) promoted[type] = built;
        if (built != assets.Count) unresolved[type] = assets.Count - built;
    }

    private static void PromoteStringTables(
        T4ZoneDocument document,
        IReadOnlyList<T4StringTableData> payloads,
        Dictionary<int, T4SimpleNativeAsset> output,
        Dictionary<string, int> promoted,
        Dictionary<string, int> unresolved,
        List<string> warnings)
    {
        const string type = "stringtable";
        var assets = PhysicalAssets(document, type);
        if (assets.Count == 0) return;
        if (payloads.Count != assets.Count)
        {
            unresolved[type] = assets.Count;
            warnings.Add($"native stringtable promotion: physical XAsset count={assets.Count:N0}, scanner payload count={payloads.Count:N0}; type left blocked.");
            return;
        }

        var built = 0;
        for (var i = 0; i < assets.Count; i++)
        {
            var a = assets[i];
            var table = payloads[i];
            if (table.RawPointers.Any(p => p != 0 && p is not (T4ZonePointer.Following or T4ZonePointer.Insert)))
            {
                warnings.Add($"native stringtable promotion: '{table.Name}' contains packed/shared cell pointer(s); XAsset#{a.Index} remains blocked.");
                continue;
            }

            using var ms = new MemoryStream();
            WriteU32(ms, T4ZonePointer.Following); // name
            WriteU32(ms, checked((uint)table.ColumnCount));
            WriteU32(ms, checked((uint)table.RowCount));
            WriteU32(ms, table.Values.Count == 0 ? 0u : T4ZonePointer.Following); // values
            WriteBytes(ms, AsciiZ(table.Name));
            foreach (var value in table.Values)
                WriteU32(ms, value is null ? 0u : T4ZonePointer.Following);
            foreach (var value in table.Values)
                if (value is not null)
                    WriteBytes(ms, AsciiZ(value));

            output[a.Index] = new T4SimpleNativeAsset(a.Index, type, table.Name, ms.ToArray(), a.ReferenceKind, T4ZoneBlock.Temp, 4);
            built++;
        }
        if (built != 0) promoted[type] = built;
        if (built != assets.Count) unresolved[type] = assets.Count - built;
    }

    private static List<T4AssetEntry> PhysicalAssets(T4ZoneDocument document, string typeName) =>
        document.Assets
            .Where(a => a.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase) &&
                        a.ReferenceKind is (T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared))
            .OrderBy(a => a.Index)
            .ToList();

    private static byte[] AsciiZ(string value)
    {
        if (value.Any(c => c > 0x7F))
            throw new InvalidDataException($"Expected ASCII T4 asset string but found non-ASCII data in '{value}'.");
        var src = Encoding.ASCII.GetBytes(value);
        var dst = new byte[src.Length + 1];
        src.CopyTo(dst, 0);
        return dst;
    }

    private static byte[] Latin1Z(string value)
    {
        if (value.Any(c => c > 0xFF))
            throw new InvalidDataException("Localized value contains a character outside the single-byte Latin-1 range used by this T4 source zone.");
        var src = Encoding.Latin1.GetBytes(value);
        var dst = new byte[src.Length + 1];
        src.CopyTo(dst, 0);
        return dst;
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }

    private static void WriteBytes(Stream stream, ReadOnlySpan<byte> value) => stream.Write(value);
}
