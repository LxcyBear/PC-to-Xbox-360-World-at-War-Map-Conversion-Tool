namespace WaW360.Core.FastFiles;

public static class ZoneHeaderParser
{
    public static ZoneHeaderSummary Parse(ReadOnlySpan<byte> header, Endian endian, long decompressedLength)
    {
        if (header.Length < FastFileConstants.T4XFileHeaderSize)
            throw new InvalidDataException($"T4 XFile header requires {FastFileConstants.T4XFileHeaderSize} bytes.");

        var words = new List<uint>(FastFileConstants.T4XFileHeaderSize / 4);
        for (var i = 0; i < FastFileConstants.T4XFileHeaderSize; i += 4)
            words.Add(EndianIO.ReadUInt32(header.Slice(i, 4), endian));

        var zoneSize = words[0];
        var externalSize = words[1];
        var blockSizes = words.Skip(2).Take(7).ToArray();

        var score = 0;
        if (zoneSize >= 16 && zoneSize <= 1024u * 1024u * 1024u) score += 2;

        // T4 zoneSize is the serialized non-runtime XFile stream AFTER the 36-byte XFile
        // header. LargeRuntime/PhysicalRuntime arenas, when present, are appended to the
        // decompressed file but are not included in zoneSize. This is independently proven by
        // both known-working Simpsons main/load Xbox FFs. The old heuristic compared zoneSize
        // directly with the complete decompressed file and therefore mis-scored legitimate Xbox
        // zones with large runtime arenas.
        if (decompressedLength > 0)
        {
            var expected = (long)FastFileConstants.T4XFileHeaderSize + zoneSize +
                           blockSizes[(int)T4ZoneBlock.LargeRuntime] +
                           blockSizes[(int)T4ZoneBlock.PhysicalRuntime];
            if (Math.Abs(expected - decompressedLength) <= 0x10000) score += 3;
            else if (Math.Abs((long)zoneSize - decompressedLength) <= 0x10000) score += 1; // legacy tolerance
        }

        // Block capacities are independent allocation budgets. A reduced/self-contained output
        // can legitimately have capacities much larger than its serialized byte stream, so do not
        // compare every block against zoneSize. Only reject absurd >1 GiB values here.
        if (blockSizes.All(v => v <= 1024u * 1024u * 1024u)) score += 2;
        if (externalSize == 0 || externalSize <= 1024u * 1024u * 1024u) score += 1;

        // Helpful but not mandatory: stock/converted zones often use one of these temp values.
        var temp = blockSizes[(int)T4ZoneBlock.Temp];
        if (temp == FastFileConstants.XboxTypicalTempBlockSize || temp == FastFileConstants.PcTypicalTempBlockSize)
            score += 1;

        return new ZoneHeaderSummary(
            endian,
            zoneSize,
            externalSize,
            blockSizes,
            words,
            score >= 5,
            score);
    }

    public static ZoneHeaderSummary ParseBest(ReadOnlySpan<byte> header, long decompressedLength)
    {
        var little = Parse(header, Endian.Little, decompressedLength);
        var big = Parse(header, Endian.Big, decompressedLength);
        return little.PlausibilityScore >= big.PlausibilityScore ? little : big;
    }
}
