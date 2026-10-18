using WaW360.Core.FastFiles;

namespace WaW360.Core.Diagnostics;

public sealed record ComparisonItem(string Field, string Left, string Right, bool Equal);

public sealed record FastFileComparison(
    FastFileMetadata Left,
    FastFileMetadata Right,
    IReadOnlyList<ComparisonItem> Items);

public static class ZoneComparer
{
    public static async Task<FastFileComparison> CompareAsync(string leftPath, string rightPath, CancellationToken cancellationToken = default)
    {
        var left = await FastFileInspector.InspectAsync(leftPath, cancellationToken);
        var right = await FastFileInspector.InspectAsync(rightPath, cancellationToken);

        var items = new List<ComparisonItem>
        {
            Item("Magic", left.Magic, right.Magic),
            Item("Version", $"0x{left.Version:X}", $"0x{right.Version:X}"),
            Item("Platform", left.Platform.ToString(), right.Platform.ToString()),
            Item("Compression", left.Compression.ToString(), right.Compression.ToString()),
            Item("Compressed bytes", left.CompressedSize.ToString("N0"), right.CompressedSize.ToString("N0")),
            Item("Decompressed bytes", left.DecompressedSize.ToString("N0"), right.DecompressedSize.ToString("N0"))
        };

        if (left.ZoneHeader is not null && right.ZoneHeader is not null)
        {
            items.Add(Item("Zone endianness", left.ZoneHeader.Endianness.ToString(), right.ZoneHeader.Endianness.ToString()));
            items.Add(Item("Zone size", Hex(left.ZoneHeader.ZoneSize), Hex(right.ZoneHeader.ZoneSize)));
            items.Add(Item("External size", Hex(left.ZoneHeader.ExternalSize), Hex(right.ZoneHeader.ExternalSize)));
            items.Add(Item("Temp block", Hex(left.ZoneHeader.TempBlockSize), Hex(right.ZoneHeader.TempBlockSize)));
            items.Add(Item("Runtime block", Hex(left.ZoneHeader.RuntimeBlockSize), Hex(right.ZoneHeader.RuntimeBlockSize)));
            items.Add(Item("Large runtime", Hex(left.ZoneHeader.LargeRuntimeBlockSize), Hex(right.ZoneHeader.LargeRuntimeBlockSize)));
            items.Add(Item("Physical runtime", Hex(left.ZoneHeader.PhysicalRuntimeBlockSize), Hex(right.ZoneHeader.PhysicalRuntimeBlockSize)));
            items.Add(Item("Virtual block", Hex(left.ZoneHeader.VirtualBlockSize), Hex(right.ZoneHeader.VirtualBlockSize)));
            items.Add(Item("Large block", Hex(left.ZoneHeader.LargeBlockSize), Hex(right.ZoneHeader.LargeBlockSize)));
            items.Add(Item("Physical block", Hex(left.ZoneHeader.PhysicalBlockSize), Hex(right.ZoneHeader.PhysicalBlockSize)));
        }

        return new FastFileComparison(left, right, items);
    }

    private static string Hex(uint value) => $"0x{value:X8}";

    private static ComparisonItem Item(string field, string left, string right) =>
        new(field, left, right, left == right);
}
