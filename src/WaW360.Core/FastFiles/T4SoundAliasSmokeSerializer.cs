using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4SoundAliasSmokeAsset(
    int AssetIndex,
    long SourceOffset,
    long SourceEndOffset,
    string AliasListName,
    int AliasCount,
    int LoadedSoundNodes,
    int StreamedSoundNodes,
    int PrimedSoundNodes,
    long LoadedAudioBytesSkipped,
    int PackedSoundFilePointers,
    int NullSoundFilePointers,
    int PackedListNamePointers,
    byte[] XboxSilentStubPayload);

public sealed class T4SoundAliasSmokeScan
{
    public required IReadOnlyDictionary<int, T4SoundAliasSmokeAsset> AssetsByIndex { get; init; }
    public required int DeclaredPhysicalSoundAssets { get; init; }
    public required int ParsedPhysicalSoundAssets { get; init; }
    public required int AliasEntries { get; init; }
    public required int LoadedSoundNodes { get; init; }
    public required int StreamedSoundNodes { get; init; }
    public required int PrimedSoundNodes { get; init; }
    public required long LoadedAudioBytesSkipped { get; init; }
    public required int PackedSoundFilePointers { get; init; }
    public required int NullSoundFilePointers { get; init; }
    public required int PackedListNamePointers { get; init; }
    public required long SilentStubPayloadBytes { get; init; }
    public required int ReadBackErrors { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Alpha 10n proof serializer for T4 snd_alias_list_t.
///
/// This does NOT claim to convert PC audio to Xbox audio.  It proves the serialized
/// ownership graph for every physical Sound asset, consumes Loaded/Streamed/Primed
/// owned data using the T4 zone rules, and produces an independently readable SILENT
/// structural stub.  The stub deliberately replaces every non-null SoundFile with an
/// exists=0/SAT_UNKNOWN placeholder and strips subtitle/secondary/chain references.
/// It is a smoke-test planning artifact only and is never emitted as a hardware FF.
/// </summary>
public static class T4SoundAliasSmokeSerializer
{
    private const int AliasListSize = 12;
    private const int AliasSize = 0xB8;
    private const int SoundFileSize = 20;
    private const int LoadedSoundSize = 12;
    private const int PrimedSoundSize = 12;
    private const int MaxAliasesPerList = 512;
    private const int MaxStringBytes = 4096;

    private sealed record ParsedList(
        long Start,
        long End,
        string Name,
        int Count,
        int Loaded,
        int Streamed,
        int Primed,
        long LoadedBytes,
        int PackedSoundFiles,
        int NullSoundFiles,
        int PackedListNames,
        IReadOnlyList<string> AliasNames,
        IReadOnlyList<bool> HasSoundFile);

    public static T4SoundAliasSmokeScan Build(T4ZoneDocument doc, ReadOnlySpan<byte> zone, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var physical = doc.Assets
            .Where(a => a.TypeName.Equals("sound", StringComparison.OrdinalIgnoreCase) &&
                        a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .OrderBy(a => a.Index)
            .ToList();

        if (physical.Count == 0)
        {
            return new T4SoundAliasSmokeScan
            {
                AssetsByIndex = new Dictionary<int, T4SoundAliasSmokeAsset>(),
                DeclaredPhysicalSoundAssets = 0,
                ParsedPhysicalSoundAssets = 0,
                AliasEntries = 0,
                LoadedSoundNodes = 0,
                StreamedSoundNodes = 0,
                PrimedSoundNodes = 0,
                LoadedAudioBytesSkipped = 0,
                PackedSoundFilePointers = 0,
                NullSoundFilePointers = 0,
                PackedListNamePointers = 0,
                SilentStubPayloadBytes = 0,
                ReadBackErrors = 0,
                Verified = true,
                Warnings = warnings
            };
        }

        if (doc.Endianness != Endian.Little)
        {
            warnings.Add("Sound smoke proof currently accepts PC/little-endian source zones only.");
            return EmptyFailure(physical.Count, warnings);
        }

        var runs = GroupConsecutiveAssets(physical);
        var candidateMap = DiscoverCandidates(doc, zone, cancellationToken);
        var selected = new List<(T4AssetEntry Asset, ParsedList Parsed)>();
        long minimumOffset = Math.Max(doc.AssetTableOffset + (long)doc.AssetCount * 8L, FastFileConstants.WaWZoneHeaderSize);

        foreach (var run in runs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runLength = run.Count;
            var starts = candidateMap.Keys
                .Where(x => x >= minimumOffset)
                .OrderBy(x => x)
                .Where(x => ChainLength(candidateMap, x, runLength + 1) == runLength)
                .ToList();

            if (starts.Count != 1)
            {
                warnings.Add($"Sound run XAsset#{run[0].Index}..#{run[^1].Index} declares {runLength:N0} physical list(s), but structural chain discovery found {starts.Count:N0} exact candidate run(s) after 0x{minimumOffset:X}. Run left blocked.");
                continue;
            }

            var cursor = starts[0];
            for (var i = 0; i < runLength; i++)
            {
                if (!candidateMap.TryGetValue(cursor, out var parsed))
                {
                    warnings.Add($"Internal sound-chain discontinuity at source offset 0x{cursor:X}; run left blocked.");
                    break;
                }
                selected.Add((run[i], parsed));
                cursor = parsed.End;
            }
            minimumOffset = cursor;
        }

        var byIndex = new Dictionary<int, T4SoundAliasSmokeAsset>();
        var readBackErrors = 0;
        foreach (var item in selected)
        {
            var stub = BuildSilentStub(item.Asset.Index, item.Parsed);
            if (!VerifySilentStub(stub, item.Parsed.Count)) readBackErrors++;
            byIndex[item.Asset.Index] = new T4SoundAliasSmokeAsset(
                item.Asset.Index,
                item.Parsed.Start,
                item.Parsed.End,
                item.Parsed.Name,
                item.Parsed.Count,
                item.Parsed.Loaded,
                item.Parsed.Streamed,
                item.Parsed.Primed,
                item.Parsed.LoadedBytes,
                item.Parsed.PackedSoundFiles,
                item.Parsed.NullSoundFiles,
                item.Parsed.PackedListNames,
                stub);
        }

        var verified = byIndex.Count == physical.Count && readBackErrors == 0;
        if (!verified)
            warnings.Add($"Sound smoke coverage is {byIndex.Count:N0}/{physical.Count:N0}; fail-closed promotion remains active.");

        return new T4SoundAliasSmokeScan
        {
            AssetsByIndex = byIndex,
            DeclaredPhysicalSoundAssets = physical.Count,
            ParsedPhysicalSoundAssets = byIndex.Count,
            AliasEntries = byIndex.Values.Sum(x => x.AliasCount),
            LoadedSoundNodes = byIndex.Values.Sum(x => x.LoadedSoundNodes),
            StreamedSoundNodes = byIndex.Values.Sum(x => x.StreamedSoundNodes),
            PrimedSoundNodes = byIndex.Values.Sum(x => x.PrimedSoundNodes),
            LoadedAudioBytesSkipped = byIndex.Values.Sum(x => x.LoadedAudioBytesSkipped),
            PackedSoundFilePointers = byIndex.Values.Sum(x => x.PackedSoundFilePointers),
            NullSoundFilePointers = byIndex.Values.Sum(x => x.NullSoundFilePointers),
            PackedListNamePointers = byIndex.Values.Sum(x => x.PackedListNamePointers),
            SilentStubPayloadBytes = byIndex.Values.Sum(x => (long)x.XboxSilentStubPayload.LongLength),
            ReadBackErrors = readBackErrors,
            Verified = verified,
            Warnings = warnings
        };
    }

    private static T4SoundAliasSmokeScan EmptyFailure(int declared, IReadOnlyList<string> warnings) => new()
    {
        AssetsByIndex = new Dictionary<int, T4SoundAliasSmokeAsset>(),
        DeclaredPhysicalSoundAssets = declared,
        ParsedPhysicalSoundAssets = 0,
        AliasEntries = 0,
        LoadedSoundNodes = 0,
        StreamedSoundNodes = 0,
        PrimedSoundNodes = 0,
        LoadedAudioBytesSkipped = 0,
        PackedSoundFilePointers = 0,
        NullSoundFilePointers = 0,
        PackedListNamePointers = 0,
        SilentStubPayloadBytes = 0,
        ReadBackErrors = 0,
        Verified = false,
        Warnings = warnings
    };

    private static List<List<T4AssetEntry>> GroupConsecutiveAssets(IReadOnlyList<T4AssetEntry> assets)
    {
        var runs = new List<List<T4AssetEntry>>();
        foreach (var asset in assets)
        {
            if (runs.Count == 0 || runs[^1][^1].Index + 1 != asset.Index)
                runs.Add(new List<T4AssetEntry>());
            runs[^1].Add(asset);
        }
        return runs;
    }

    private static Dictionary<long, ParsedList> DiscoverCandidates(T4ZoneDocument doc, ReadOnlySpan<byte> zone, CancellationToken cancellationToken)
    {
        var found = new Dictionary<long, ParsedList>();
        var start = checked((int)Math.Max(FastFileConstants.WaWZoneHeaderSize, doc.AssetTableOffset + (long)doc.AssetCount * 8L));
        for (var p = start; p <= zone.Length - AliasListSize; p++)
        {
            if ((p & 0x3FFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var head = ReadU32(zone, p + 4);
            if (head is not (T4ZonePointer.Following or T4ZonePointer.Insert)) continue;
            var count = ReadU32(zone, p + 8);
            if (count is 0 or > MaxAliasesPerList) continue;
            if (!PointerPlausible(doc, ReadU32(zone, p))) continue;
            if (TryParseList(doc, zone, p, out var parsed)) found[p] = parsed;
        }
        return found;
    }

    private static int ChainLength(IReadOnlyDictionary<long, ParsedList> map, long start, int stopAfter)
    {
        var n = 0;
        var cursor = start;
        while (n < stopAfter && map.TryGetValue(cursor, out var item))
        {
            n++;
            if (item.End <= cursor) break;
            cursor = item.End;
        }
        return n;
    }

    private static bool TryParseList(T4ZoneDocument doc, ReadOnlySpan<byte> z, int start, out ParsedList parsed)
    {
        parsed = null!;
        try
        {
            var namePtr = ReadU32(z, start);
            var headPtr = ReadU32(z, start + 4);
            var count = checked((int)ReadU32(z, start + 8));
            if (!PointerPlausible(doc, namePtr) || headPtr is not (T4ZonePointer.Following or T4ZonePointer.Insert) || count <= 0 || count > MaxAliasesPerList)
                return false;

            long cursor = start + AliasListSize;
            var packedListNames = IsPacked(namePtr) ? 1 : 0;
            var listName = ConsumeString(doc, z, namePtr, ref cursor) ?? $"__waw360_sound_{start:X}";
            var head = cursor;
            Ensure(z, head, checked((long)count * AliasSize));

            var aliasNames = new List<string>(count);
            var hasSound = new List<bool>(count);
            for (var i = 0; i < count; i++)
                if (!ValidateAliasFixed(doc, z, head + (long)i * AliasSize)) return false;

            cursor = head + (long)count * AliasSize;
            var loaded = 0;
            var streamed = 0;
            var primed = 0;
            long loadedBytes = 0;
            var packedSoundFiles = 0;
            var nullSoundFiles = 0;

            for (var i = 0; i < count; i++)
            {
                var a = head + (long)i * AliasSize;
                var aliasNamePtr = ReadU32(z, a + 0);
                var subtitlePtr = ReadU32(z, a + 8);
                var secondaryPtr = ReadU32(z, a + 12);
                var chainPtr = ReadU32(z, a + 16);
                var soundFilePtr = ReadU32(z, a + 20);

                var aliasName = ConsumeString(doc, z, aliasNamePtr, ref cursor) ?? listName;
                aliasNames.Add(aliasName);
                ConsumeString(doc, z, subtitlePtr, ref cursor);
                ConsumeString(doc, z, secondaryPtr, ref cursor);
                ConsumeString(doc, z, chainPtr, ref cursor);

                hasSound.Add(soundFilePtr != 0);
                if (soundFilePtr == 0) { nullSoundFiles++; continue; }
                if (IsPacked(soundFilePtr)) { packedSoundFiles++; continue; }
                if (!IsFollowing(soundFilePtr)) return false;

                var sf = cursor;
                Ensure(z, sf, SoundFileSize);
                var type = z[checked((int)sf)];
                if (type > 3) return false;
                cursor += SoundFileSize;

                if (type == 1)
                {
                    loaded++;
                    var loadedPtr = ReadU32(z, sf + 4);
                    if (IsPacked(loadedPtr) || loadedPtr == 0) continue;
                    if (!IsFollowing(loadedPtr)) return false;
                    var ls = cursor;
                    Ensure(z, ls, LoadedSoundSize);
                    var loadedNamePtr = ReadU32(z, ls + 0);
                    var dataPtr = ReadU32(z, ls + 4);
                    var dataSize = ReadU32(z, ls + 8);
                    if (!PointerPlausible(doc, loadedNamePtr) || !PointerPlausible(doc, dataPtr) || dataSize > 200_000_000u) return false;
                    cursor += LoadedSoundSize;
                    ConsumeString(doc, z, loadedNamePtr, ref cursor);
                    if (IsFollowing(dataPtr))
                    {
                        Ensure(z, cursor, dataSize);
                        loadedBytes += dataSize;
                        cursor += dataSize;
                    }
                }
                else if (type is 2 or 3)
                {
                    if (type == 2) streamed++; else primed++;
                    var dirPtr = ReadU32(z, sf + 8);
                    var filePtr = ReadU32(z, sf + 12);
                    var primePtr = ReadU32(z, sf + 16);
                    if (!PointerPlausible(doc, dirPtr) || !PointerPlausible(doc, filePtr) || !PointerPlausible(doc, primePtr)) return false;
                    ConsumeString(doc, z, dirPtr, ref cursor);
                    ConsumeString(doc, z, filePtr, ref cursor);
                    if (IsFollowing(primePtr))
                    {
                        var ps = cursor;
                        Ensure(z, ps, PrimedSoundSize);
                        var primeNamePtr = ReadU32(z, ps + 0);
                        var bufferPtr = ReadU32(z, ps + 4);
                        var size = ReadU32(z, ps + 8);
                        if (!PointerPlausible(doc, primeNamePtr) || !PointerPlausible(doc, bufferPtr) || size > 200_000_000u) return false;
                        cursor += PrimedSoundSize;
                        ConsumeString(doc, z, primeNamePtr, ref cursor);
                        if (IsFollowing(bufferPtr))
                        {
                            var aligned = (cursor + 2047L) & ~2047L;
                            if (aligned > cursor && !AllZero(z, cursor, aligned - cursor)) aligned = cursor;
                            Ensure(z, aligned, size);
                            cursor = aligned + size;
                        }
                    }
                }
            }

            parsed = new ParsedList(start, cursor, listName, count, loaded, streamed, primed, loadedBytes, packedSoundFiles, nullSoundFiles, packedListNames, aliasNames, hasSound);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool ValidateAliasFixed(T4ZoneDocument doc, ReadOnlySpan<byte> z, long off)
    {
        Ensure(z, off, AliasSize);
        foreach (var rel in new[] { 0, 8, 12, 16, 20 })
            if (!PointerPlausible(doc, ReadU32(z, off + rel))) return false;

        // These are the scalar float spans in T4's 0xB8 snd_alias_t.  Reject NaN/Inf
        // and absurd false-positive bit patterns while keeping legitimate raw values.
        for (var rel = 28; rel < 104; rel += 4)
            if (!FiniteReasonable(ReadF32(z, off + rel))) return false;
        if (!FiniteReasonable(ReadF32(z, off + 112)) || !FiniteReasonable(ReadF32(z, off + 116))) return false;
        return true;
    }

    private static byte[] BuildSilentStub(int assetIndex, ParsedList src)
    {
        using var ms = new MemoryStream();
        WriteBE(ms, T4ZonePointer.Following); // list aliasName
        WriteBE(ms, T4ZonePointer.Following); // head
        WriteBE(ms, checked((uint)src.Count));
        var listName = SafeName(src.Name, $"__waw360_silent_sound_{assetIndex}");
        WriteCString(ms, listName);

        // Fixed array first, matching T4 reusable-head ownership.  Scalar fields are
        // intentionally zeroed for this SILENT smoke structure; this is not final audio.
        for (var i = 0; i < src.Count; i++)
        {
            var fixedAlias = new byte[AliasSize];
            BinaryPrimitives.WriteUInt32BigEndian(fixedAlias.AsSpan(0, 4), T4ZonePointer.Following);
            BinaryPrimitives.WriteUInt32BigEndian(fixedAlias.AsSpan(20, 4), src.HasSoundFile[i] ? T4ZonePointer.Following : 0u);
            ms.Write(fixedAlias);
        }

        for (var i = 0; i < src.Count; i++)
        {
            WriteCString(ms, SafeName(src.AliasNames[i], listName));
            if (src.HasSoundFile[i]) ms.Write(new byte[SoundFileSize]); // SAT_UNKNOWN + exists=0
        }
        return ms.ToArray();
    }

    private static bool VerifySilentStub(ReadOnlySpan<byte> b, int expectedCount)
    {
        if (b.Length < AliasListSize) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(b.Slice(0, 4)) != T4ZonePointer.Following) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(b.Slice(4, 4)) != T4ZonePointer.Following) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(b.Slice(8, 4)) != checked((uint)expectedCount)) return false;
        var cursor = AliasListSize;
        if (!SkipCString(b, ref cursor)) return false;
        var fixedBytes = checked(expectedCount * AliasSize);
        if (cursor > b.Length - fixedBytes) return false;
        var fixedStart = cursor;
        cursor += fixedBytes;
        for (var i = 0; i < expectedCount; i++)
        {
            var a = fixedStart + i * AliasSize;
            if (BinaryPrimitives.ReadUInt32BigEndian(b.Slice(a, 4)) != T4ZonePointer.Following) return false;
            var sf = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(a + 20, 4));
            if (sf is not (0u or T4ZonePointer.Following)) return false;
            if (!SkipCString(b, ref cursor)) return false;
            if (sf == T4ZonePointer.Following)
            {
                if (cursor > b.Length - SoundFileSize) return false;
                if (b[cursor] != 0 || b[cursor + 1] != 0) return false;
                cursor += SoundFileSize;
            }
        }
        return cursor == b.Length;
    }

    private static string? ConsumeString(T4ZoneDocument doc, ReadOnlySpan<byte> z, uint ptr, ref long cursor)
    {
        if (ptr == 0) return null;
        if (IsFollowing(ptr))
        {
            var s = ReadCString(z, cursor, out var bytes);
            cursor += bytes;
            return s;
        }
        var d = T4ZonePointer.Decode(ptr);
        if (d.Kind == T4ZonePointerKind.Offset && d.Block == T4ZoneBlock.Temp)
        {
            var abs = checked((long)FastFileConstants.T4XFileHeaderSize + d.Offset);
            return ReadCString(z, abs, out _);
        }
        return T4ZonePointer.Describe(ptr);
    }

    private static bool PointerPlausible(T4ZoneDocument doc, uint ptr) => T4ZonePointer.IsWithinBlock(ptr, doc.Header);
    private static bool IsFollowing(uint ptr) => ptr is T4ZonePointer.Following or T4ZonePointer.Insert;
    private static bool IsPacked(uint ptr) => ptr != 0 && !IsFollowing(ptr);

    private static uint ReadU32(ReadOnlySpan<byte> z, long off)
    {
        Ensure(z, off, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(z.Slice(checked((int)off), 4));
    }

    private static float ReadF32(ReadOnlySpan<byte> z, long off)
    {
        var raw = ReadU32(z, off);
        return BitConverter.Int32BitsToSingle(unchecked((int)raw));
    }

    private static bool FiniteReasonable(float value) => float.IsFinite(value) && Math.Abs(value) < 1.0e10f;

    private static string ReadCString(ReadOnlySpan<byte> z, long off, out int bytes)
    {
        Ensure(z, off, 1);
        var start = checked((int)off);
        var end = start;
        while (end < z.Length && z[end] != 0 && end - start < MaxStringBytes) end++;
        if (end >= z.Length || end - start >= MaxStringBytes) throw new InvalidDataException("Unterminated/oversized sound string.");
        bytes = end - start + 1;
        return Encoding.UTF8.GetString(z.Slice(start, end - start));
    }

    private static string SafeName(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("<ptr", StringComparison.Ordinal)) return fallback;
        return value.Replace('\0', '_');
    }

    private static bool AllZero(ReadOnlySpan<byte> z, long off, long len)
    {
        Ensure(z, off, len);
        var s = z.Slice(checked((int)off), checked((int)len));
        foreach (var b in s) if (b != 0) return false;
        return true;
    }

    private static void Ensure(ReadOnlySpan<byte> z, long off, long len)
    {
        if (off < 0 || len < 0 || off > z.Length - len) throw new InvalidDataException("Sound payload exceeds decompressed zone.");
    }

    private static void WriteBE(Stream s, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        s.Write(b);
    }

    private static void WriteCString(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        s.Write(bytes);
        s.WriteByte(0);
    }

    private static bool SkipCString(ReadOnlySpan<byte> b, ref int cursor)
    {
        while (cursor < b.Length && b[cursor] != 0) cursor++;
        if (cursor >= b.Length) return false;
        cursor++;
        return true;
    }
}
