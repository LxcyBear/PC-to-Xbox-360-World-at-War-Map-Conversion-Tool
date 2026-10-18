using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace WaW360.Core.FastFiles;

public sealed record T4MenuStructureCandidate(
    long SerializedOffset,
    string Name,
    int ItemCount,
    bool FullScreen,
    float X,
    float Y,
    float Width,
    float Height,
    int HorizontalAlign,
    int VerticalAlign,
    uint RawNamePointer,
    uint RawItemsPointer,
    string Confidence);

public sealed record T4MenuMarkerHit(long Offset, string Value);

public sealed class T4MenuStructureProbeResult
{
    public required T4ZoneDocument Document { get; init; }
    public required IReadOnlyList<T4MenuStructureCandidate> InlineNamedMenus { get; init; }
    public required IReadOnlyList<T4MenuMarkerHit> MarkerHits { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Stage 2 alpha menu-structure probe for T4/WaW console zones.
///
/// IMPORTANT: this is intentionally not the Stage 3 menu writer. It only reports menuDef_t
/// structures that can be proven from the serialized bytes without resolving pre-linked block
/// pointers. Console T4 menuDef_t is 312 bytes (four local-client dynamicFlags and cursorItem
/// slots). PC WaW uses the smaller 288-byte layout and is not handled by this console probe yet.
///
/// The probe recognizes the safest subset: an inline menuDef_t whose window.name and items
/// pointers use the normal Treyarch inline sentinel and whose fixed fields fit the console
/// structure. Other menu assets can legitimately reuse/pre-link strings; those remain unresolved
/// until the block-address loader is implemented.
/// </summary>
public static partial class T4MenuStructureProbe
{
    private const int ConsoleMenuDefSize = 312;
    private const uint InlineShared = 0xFFFFFFFFu;
    private const uint Inline = 0xFFFFFFFEu;

    [GeneratedRegex("^[A-Za-z0-9_./\\\\-]{3,96}$", RegexOptions.CultureInvariant)]
    private static partial Regex MenuNameRegex();

    public static async Task<T4MenuStructureProbeResult> ProbeFastFileAsync(
        string fastFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(fastFilePath))
            throw new FileNotFoundException("Fastfile not found.", fastFilePath);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "menustruct", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "menu.zone");

        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(fastFilePath, zonePath, cancellationToken);
            var bytes = await File.ReadAllBytesAsync(zonePath, cancellationToken);
            var document = T4ZoneParser.ParseZoneBytes(
                fastFilePath,
                bytes,
                decomp.Platform,
                decomp.Compression,
                decomp.ZoneHeader.Endianness);

            return Probe(document, bytes);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public static T4MenuStructureProbeResult Probe(T4ZoneDocument document, byte[] zone)
    {
        var warnings = new List<string>();
        var output = new List<T4MenuStructureCandidate>();

        if (document.Endianness != Endian.Big)
        {
            warnings.Add("The Stage 2 alpha 4 menuDef_t structure probe currently targets the 312-byte big-endian console layout only. PC WaW uses a 288-byte layout.");
        }
        else
        {
            var start = checked((int)(document.AssetTableOffset + document.AssetCount * 8L));
            var end = zone.Length - ConsoleMenuDefSize - 1;

            // Treyarch's serialized console data is commonly 2-byte aligned after variable-length
            // strings, so deliberately probe every two bytes instead of assuming 4-byte alignment.
            for (var offset = Math.Max(0, start); offset <= end; offset += 2)
            {
                if (!TryReadInlineNamedConsoleMenu(zone, offset, document.Endianness, out var menu))
                    continue;

                output.Add(menu!);
            }
        }

        var declaredMenus = document.Assets.Count(a => a.TypeName.Equals("menu", StringComparison.OrdinalIgnoreCase));
        if (output.Count < declaredMenus)
        {
            warnings.Add($"Located {output.Count:N0} high-confidence inline-named menuDef_t structure(s), while XAssetList declares {declaredMenus:N0} menu assets. " +
                         "This is expected at this stage: menus whose window names are pre-linked/reused are intentionally not guessed. Stage 2's block-address loader will resolve those next.");
        }

        return new T4MenuStructureProbeResult
        {
            Document = document,
            InlineNamedMenus = output,
            MarkerHits = FindMenuMarkers(zone),
            Warnings = warnings
        };
    }

    private static bool TryReadInlineNamedConsoleMenu(
        byte[] zone,
        int offset,
        Endian endian,
        out T4MenuStructureCandidate? menu)
    {
        menu = null;
        if (offset < 0 || offset + ConsoleMenuDefSize >= zone.Length)
            return false;

        var namePtr = ReadU32(zone, offset, endian);
        var itemsPtr = ReadU32(zone, offset + 308, endian);

        // Safest form: fixed struct followed by inline window.name, plus inline items array when
        // the menu actually has items. 0xFFFFFFFF is the ordinary shared/following sentinel.
        if (namePtr is not (InlineShared or Inline))
            return false;

        var fullScreen = ReadI32(zone, offset + 172, endian);
        var itemCount = ReadI32(zone, offset + 176, endian);
        if (fullScreen is not (0 or 1) || itemCount is < 1 or > 500)
            return false;
        if (itemsPtr is not (InlineShared or Inline) && !IsPackedPointer(itemsPtr))
            return false;

        var x = ReadF32(zone, offset + 4, endian);
        var y = ReadF32(zone, offset + 8, endian);
        var width = ReadF32(zone, offset + 12, endian);
        var height = ReadF32(zone, offset + 16, endian);
        if (!FiniteRange(x, -1000, 1000) || !FiniteRange(y, -1000, 1000) ||
            !FiniteRange(width, 1, 2000) || !FiniteRange(height, 1, 2000))
            return false;

        var hAlign = ReadI32(zone, offset + 20, endian);
        var vAlign = ReadI32(zone, offset + 24, endian);
        if (hAlign is < 0 or > 7 || vAlign is < 0 or > 7)
            return false;

        // rectClient must also look sane.
        foreach (var fieldOffset in new[] { 28, 32, 36, 40 })
        {
            if (!FiniteRange(ReadF32(zone, offset + fieldOffset, endian), -5000, 5000))
                return false;
        }
        if (ReadI32(zone, offset + 44, endian) is < 0 or > 7 ||
            ReadI32(zone, offset + 48, endian) is < 0 or > 7)
            return false;

        if (!IsSerializedPointer(ReadU32(zone, offset + 52, endian)) ||
            !IsSerializedPointer(ReadU32(zone, offset + 164, endian)) ||
            !IsSerializedPointer(ReadU32(zone, offset + 168, endian)))
            return false;

        var borderSize = ReadF32(zone, offset + 72, endian);
        if (!FiniteRange(borderSize, 0, 10))
            return false;

        // Six RGBA groups exist across window/menuDef_t (four window colors, focus, disable).
        foreach (var baseOffset in new[] { 100, 116, 132, 148, 260, 276 })
        {
            for (var component = 0; component < 4; component++)
            {
                if (!FiniteRange(ReadF32(zone, offset + baseOffset + component * 4, endian), -0.01f, 1.5f))
                    return false;
            }
        }

        var fontIndex = ReadI32(zone, offset + 180, endian);
        if (fontIndex is < -1 or > 20)
            return false;
        foreach (var cursorOffset in new[] { 184, 188, 192, 196 })
        {
            if (ReadI32(zone, offset + cursorOffset, endian) is < -1 or > 500)
                return false;
        }

        foreach (var floatOffset in new[] { 204, 208, 212, 216 })
        {
            if (!FiniteRange(ReadF32(zone, offset + floatOffset, endian), -100, 100))
                return false;
        }

        foreach (var pointerOffset in new[] { 220, 224, 228, 232, 236, 244, 248, 252, 296, 304 })
        {
            if (!IsSerializedPointer(ReadU32(zone, offset + pointerOffset, endian)))
                return false;
        }

        if (ReadI32(zone, offset + 240, endian) is < 0 or > 1000 ||
            ReadI32(zone, offset + 292, endian) is < 0 or > 1000 ||
            ReadI32(zone, offset + 300, endian) is < 0 or > 1000)
            return false;

        if (!TryReadCString(zone, offset + ConsoleMenuDefSize, 128, out var name))
            return false;
        if (!MenuNameRegex().IsMatch(name))
            return false;

        menu = new T4MenuStructureCandidate(
            offset,
            name,
            itemCount,
            fullScreen != 0,
            x,
            y,
            width,
            height,
            hAlign,
            vAlign,
            namePtr,
            itemsPtr,
            "High: fixed 312-byte console menuDef_t + inline window.name");
        return true;
    }

    private static IReadOnlyList<T4MenuMarkerHit> FindMenuMarkers(byte[] zone)
    {
        // These are diagnostics only; they help correlate the structural parser with the known
        // Zombies menu data while the pre-linked block resolver is still being built.
        var needles = new[]
        {
            "menu_map_custom_zombies_menu",
            "custom_zombies",
            "Custom Maps",
            "maps/mapstable.csv"
        };

        var hits = new List<T4MenuMarkerHit>();
        foreach (var needle in needles)
        {
            var pattern = Encoding.ASCII.GetBytes(needle);
            var start = 0;
            while (start <= zone.Length - pattern.Length)
            {
                var relative = zone.AsSpan(start).IndexOf(pattern);
                if (relative < 0) break;
                var offset = start + relative;
                hits.Add(new T4MenuMarkerHit(offset, needle));
                start = offset + Math.Max(1, pattern.Length);
            }
        }

        return hits.OrderBy(x => x.Offset).ToList();
    }

    private static bool TryReadCString(byte[] data, int offset, int maxLength, out string value)
    {
        value = string.Empty;
        if (offset < 0 || offset >= data.Length)
            return false;

        var limit = Math.Min(data.Length, offset + maxLength);
        var end = offset;
        while (end < limit && data[end] != 0)
        {
            var b = data[end];
            if (b < 0x20 || b == 0x7F)
                return false;
            end++;
        }

        if (end == offset || end >= limit || data[end] != 0)
            return false;

        value = Encoding.Latin1.GetString(data, offset, end - offset);
        return true;
    }

    private static bool IsSerializedPointer(uint value) =>
        value == 0 || value == InlineShared || value == Inline || IsPackedPointer(value);

    private static bool IsPackedPointer(uint value)
    {
        if (value == 0 || value is InlineShared or Inline)
            return false;
        var decoded = T4ZonePointer.Decode(value);
        return decoded.Kind == T4ZonePointerKind.Offset && decoded.Block is not null;
    }

    private static bool FiniteRange(float value, float minimum, float maximum) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum && value <= maximum;

    private static uint ReadU32(byte[] data, int offset, Endian endian)
    {
        var span = data.AsSpan(offset, 4);
        return endian == Endian.Big
            ? BinaryPrimitives.ReadUInt32BigEndian(span)
            : BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    private static int ReadI32(byte[] data, int offset, Endian endian) =>
        unchecked((int)ReadU32(data, offset, endian));

    private static float ReadF32(byte[] data, int offset, Endian endian) =>
        BitConverter.Int32BitsToSingle(ReadI32(data, offset, endian));
}
