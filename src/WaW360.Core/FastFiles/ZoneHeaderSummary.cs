namespace WaW360.Core.FastFiles;

public sealed record ZoneHeaderSummary(
    Endian Endianness,
    uint ZoneSize,
    uint ExternalSize,
    IReadOnlyList<uint> BlockSizes,
    IReadOnlyList<uint> RawWords,
    bool Plausible,
    int PlausibilityScore)
{
    public uint TempBlockSize => BlockSizes[(int)T4ZoneBlock.Temp];
    public uint RuntimeBlockSize => BlockSizes[(int)T4ZoneBlock.Runtime];
    public uint LargeRuntimeBlockSize => BlockSizes[(int)T4ZoneBlock.LargeRuntime];
    public uint PhysicalRuntimeBlockSize => BlockSizes[(int)T4ZoneBlock.PhysicalRuntime];
    public uint VirtualBlockSize => BlockSizes[(int)T4ZoneBlock.Virtual];
    public uint LargeBlockSize => BlockSizes[(int)T4ZoneBlock.Large];
    public uint PhysicalBlockSize => BlockSizes[(int)T4ZoneBlock.Physical];

    public override string ToString() =>
        $"{Endianness}-endian, zone=0x{ZoneSize:X8}, temp=0x{TempBlockSize:X8}, virtual=0x{VirtualBlockSize:X8}, physical=0x{PhysicalBlockSize:X8}";
}
