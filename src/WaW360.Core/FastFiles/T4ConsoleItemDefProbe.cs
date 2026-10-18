using System.Buffers.Binary;

namespace WaW360.Core.FastFiles;

public sealed record T4ConsoleItemDefCandidate(
    long SerializedOffset,
    int FixedSize,
    int Type,
    int DataType,
    int Alignment,
    int FontEnum,
    int TextAlignMode,
    float X,
    float Y,
    float Width,
    float Height,
    int HorizontalAlign,
    int VerticalAlign,
    uint RawWindowNamePointer,
    uint RawTextPointer,
    uint RawParentPointer,
    uint RawActionPointer,
    int StructuralChecksPassed,
    int StructuralChecksTotal,
    string Confidence);

/// <summary>
/// Conservative reader for the fixed portion of an Xbox 360 T4 itemDef_s.
///
/// T4's 32-bit PC structure is 376 bytes. The console build has four local-client
/// window dynamicFlags entries instead of one (+12 bytes) and four cursorPos entries
/// instead of one (+12 bytes), producing a 400-byte fixed structure.
///
/// This probe does not follow inline pointers yet. It only validates fields that are
/// physically present in the fixed structure so Stage 2 can establish trustworthy
/// item boundaries before the logical-block object loader is used by Stage 3.
/// </summary>
public static class T4ConsoleItemDefProbe
{
    public const int FixedSize = 400;

    private const uint InlineShared = 0xFFFFFFFFu;
    private const uint Inline = 0xFFFFFFFEu;

    public static bool TryRead(byte[] zone, int offset, Endian endian, out T4ConsoleItemDefCandidate? item)
    {
        item = null;
        if (endian != Endian.Big || offset < 0 || offset + FixedSize > zone.Length)
            return false;

        var passed = 0;
        var total = 0;

        bool Check(bool condition)
        {
            total++;
            if (condition) passed++;
            return condition;
        }

        var windowName = ReadU32(zone, offset + 0, endian);
        var x = ReadF32(zone, offset + 4, endian);
        var y = ReadF32(zone, offset + 8, endian);
        var width = ReadF32(zone, offset + 12, endian);
        var height = ReadF32(zone, offset + 16, endian);
        var hAlign = ReadI32(zone, offset + 20, endian);
        var vAlign = ReadI32(zone, offset + 24, endian);

        Check(IsSerializedPointer(windowName));
        Check(FiniteRange(x, -5000, 5000));
        Check(FiniteRange(y, -5000, 5000));
        Check(FiniteRange(width, 0, 5000));
        Check(FiniteRange(height, 0, 5000));
        Check(hAlign is >= 0 and <= 7);
        Check(vAlign is >= 0 and <= 7);

        foreach (var fieldOffset in new[] { 28, 32, 36, 40 })
            Check(FiniteRange(ReadF32(zone, offset + fieldOffset, endian), -10000, 10000));
        Check(ReadI32(zone, offset + 44, endian) is >= 0 and <= 7);
        Check(ReadI32(zone, offset + 48, endian) is >= 0 and <= 7);

        Check(IsSerializedPointer(ReadU32(zone, offset + 52, endian))); // group
        Check(ReadI32(zone, offset + 56, endian) is >= 0 and <= 32);    // style
        Check(ReadI32(zone, offset + 60, endian) is >= 0 and <= 32);    // border
        Check(FiniteRange(ReadF32(zone, offset + 72, endian), 0, 32));  // borderSize
        Check(IsSerializedPointer(ReadU32(zone, offset + 164, endian))); // background

        // textRect starts immediately after the 168-byte console windowDef_t.
        foreach (var fieldOffset in new[] { 168, 172, 176, 180 })
            Check(FiniteRange(ReadF32(zone, offset + fieldOffset, endian), -10000, 10000));
        Check(ReadI32(zone, offset + 184, endian) is >= 0 and <= 7);
        Check(ReadI32(zone, offset + 188, endian) is >= 0 and <= 7);

        var type = ReadI32(zone, offset + 192, endian);
        var dataType = ReadI32(zone, offset + 196, endian);
        var alignment = ReadI32(zone, offset + 200, endian);
        var fontEnum = ReadI32(zone, offset + 204, endian);
        var textAlignMode = ReadI32(zone, offset + 208, endian);

        Check(type is >= 0 and <= 0x13);
        Check(dataType is >= -1 and <= 64);
        Check(alignment is >= -1 and <= 64);
        Check(fontEnum is >= -1 and <= 32);
        Check(textAlignMode is >= -1 and <= 32);
        Check(FiniteRange(ReadF32(zone, offset + 212, endian), -10000, 10000));
        Check(FiniteRange(ReadF32(zone, offset + 216, endian), -10000, 10000));
        Check(FiniteRange(ReadF32(zone, offset + 220, endian), -20, 20));
        Check(ReadI32(zone, offset + 224, endian) is >= -1 and <= 64);
        Check(ReadI32(zone, offset + 228, endian) is >= -1 and <= 128);
        Check(ReadI32(zone, offset + 232, endian) is >= -1 and <= 128);

        var textPtr = ReadU32(zone, offset + 236, endian);
        var parentPtr = ReadU32(zone, offset + 244, endian);
        var actionPtr = ReadU32(zone, offset + 264, endian);
        Check(IsSerializedPointer(textPtr));
        Check(IsSerializedPointer(parentPtr));

        // Pointer/string fields through enableDvar. These may be null, inline sentinels,
        // or packed block offsets; anything else is suspicious for a fixed item body.
        foreach (var pointerOffset in new[]
                 {
                     248, 252, 256, 260, 264, 268, 272, 276,
                     280, 284, 288, 292, 296, 304, 328
                 })
            Check(IsSerializedPointer(ReadU32(zone, offset + pointerOffset, endian)));

        Check(FiniteRange(ReadF32(zone, offset + 308, endian), -100000, 100000));
        foreach (var cursorOffset in new[] { 312, 316, 320, 324 })
            Check(ReadI32(zone, offset + cursorOffset, endian) is >= -1 and <= 100000);
        Check(ReadI32(zone, offset + 332, endian) is >= -100000 and <= 100000);

        // Eight statement_s values: count + entries pointer.
        for (var statementOffset = 336; statementOffset < 400; statementOffset += 8)
        {
            Check(ReadI32(zone, offset + statementOffset, endian) is >= 0 and <= 10000);
            Check(IsSerializedPointer(ReadU32(zone, offset + statementOffset + 4, endian)));
        }

        // Require a very high percentage: this is a structure probe, not a fuzzy byte search.
        var ratio = total == 0 ? 0 : (double)passed / total;
        if (ratio < 0.92)
            return false;

        item = new T4ConsoleItemDefCandidate(
            offset,
            FixedSize,
            type,
            dataType,
            alignment,
            fontEnum,
            textAlignMode,
            x,
            y,
            width,
            height,
            hAlign,
            vAlign,
            windowName,
            textPtr,
            parentPtr,
            actionPtr,
            passed,
            total,
            ratio >= 0.98 ? "Very high" : "High");
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
