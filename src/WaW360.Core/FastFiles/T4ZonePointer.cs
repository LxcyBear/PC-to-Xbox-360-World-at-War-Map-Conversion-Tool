namespace WaW360.Core.FastFiles;

public enum T4ZonePointerKind
{
    Null,
    Following,
    Insert,
    Offset
}

public readonly record struct T4DecodedZonePointer(
    uint Raw,
    T4ZonePointerKind Kind,
    T4ZoneBlock? Block,
    uint Offset)
{
    public override string ToString() => Kind switch
    {
        T4ZonePointerKind.Null => "null",
        T4ZonePointerKind.Following => "following (-1)",
        T4ZonePointerKind.Insert => "insert (-2)",
        T4ZonePointerKind.Offset when Block is not null => $"{Block}+0x{Offset:X8}",
        _ => $"0x{Raw:X8}"
    };
}

/// <summary>
/// T4 uses 32-bit packed zone offsets with three high bits selecting one of seven XFile blocks.
/// Stored offsets are biased by +1 so block 0/offset 0 does not collide with nullptr.
/// 0xFFFFFFFF and 0xFFFFFFFE are the special FOLLOWING/INSERT sentinels.
/// </summary>
public static class T4ZonePointer
{
    public const uint Following = 0xFFFFFFFFu;
    public const uint Insert = 0xFFFFFFFEu;
    private const int BlockBits = 3;
    private const int BlockShift = 32 - BlockBits; // 29
    private const uint OffsetMask = (1u << BlockShift) - 1u;

    public static T4DecodedZonePointer Decode(uint raw)
    {
        if (raw == 0)
            return new T4DecodedZonePointer(raw, T4ZonePointerKind.Null, null, 0);
        if (raw == Following)
            return new T4DecodedZonePointer(raw, T4ZonePointerKind.Following, null, 0);
        if (raw == Insert)
            return new T4DecodedZonePointer(raw, T4ZonePointerKind.Insert, null, 0);

        var unbiased = raw - 1u;
        var blockId = (int)(unbiased >> BlockShift);
        var offset = unbiased & OffsetMask;
        var block = Enum.IsDefined(typeof(T4ZoneBlock), blockId)
            ? (T4ZoneBlock?)blockId
            : null;
        return new T4DecodedZonePointer(raw, T4ZonePointerKind.Offset, block, offset);
    }


    public static uint Encode(T4ZoneBlock block, uint offset)
    {
        var blockId = (int)block;
        if (blockId < 0 || blockId >= (1 << BlockBits))
            throw new ArgumentOutOfRangeException(nameof(block));
        if (offset > OffsetMask)
            throw new ArgumentOutOfRangeException(nameof(offset), $"T4 packed zone offsets are limited to 0x{OffsetMask:X8} bytes per block.");

        var unbiased = ((uint)blockId << BlockShift) | offset;
        var raw = checked(unbiased + 1u);
        if (raw is 0 or Following or Insert)
            throw new InvalidOperationException($"Packed pointer encoding collided with a T4 sentinel: {block}+0x{offset:X8} -> 0x{raw:X8}.");
        return raw;
    }

    public static bool IsWithinBlock(uint raw, ZoneHeaderSummary header)
    {
        var decoded = Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null)
            return decoded.Kind is T4ZonePointerKind.Null or T4ZonePointerKind.Following or T4ZonePointerKind.Insert;
        var index = (int)decoded.Block.Value;
        return index >= 0 && index < header.BlockSizes.Count && decoded.Offset < header.BlockSizes[index];
    }

    public static string Describe(uint raw)
    {
        var decoded = Decode(raw);
        return decoded.Kind == T4ZonePointerKind.Offset
            ? $"<ptr {decoded}>"
            : $"<{decoded}>";
    }
}
