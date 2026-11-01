using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Opt-in original Xbox T4Xenon sound graft. All alias fields are sourced
/// from the real OAT-loaded 184-byte Xbox retail sound graph, and all
/// LoadedSound metadata is from the same owned 160-byte retail asset.
/// Packet bytes are the genuine Xbox XMA "data" RIFF chunk (not the RIFF
/// container), with original seek words, rates, loop metadata and XMA
/// decode-buffer fields preserved. This is not an audio encoder or a
/// silent/identity-shell replacement.
/// </summary>
internal sealed class T4XenonAztecRealFlameAudioGraft
{
    private sealed record RiffPacket(byte[] Data, byte[] Seek, ushort Streams, byte Loops, int DurationMsec);
    private sealed record Row(
        string SoundName, int SoundRoot, string AliasName,
        string? Subtitle, string? Secondary, string? Chain,
        string LoadedName, int? LoadedRoot,
        byte[] AliasHost, byte[] LoadedHost, RiffPacket Packet,
        string XmaSha256);

    private readonly Dictionary<int, Row> _sounds = new();
    private readonly Dictionary<int, Row> _loaded = new();

    public int RealSoundRoots => _sounds.Count;
    public int RealLoadedSoundRoots => _loaded.Count;
    public int InlineLoadedSoundChildren => _sounds.Values.Count(x => x.LoadedRoot is null);
    public long OriginalXmaPacketBytes => _sounds.Values.Sum(x => (long)x.Packet.Data.Length);

    private T4XenonAztecRealFlameAudioGraft() { }

    public static T4XenonAztecRealFlameAudioGraft? LoadFromEnvironment(
        IReadOnlyList<T4XenonNativeIdentityEntry> roots)
    {
        var path = Environment.GetEnvironmentVariable("WAW360_AZTEC_FLAME6_NATIVE_METADATA");
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path))
            throw new FileNotFoundException("Six original Xbox sound source metadata manifest is missing", path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        if (String(r, "schema") != "WaW360.AztecOriginalXboxFlameSixSoundAliasLoadedMetadata.v6" ||
            Int(r, "verifiedActualOriginalXboxAliasRootCount") != 6 ||
            Int(r, "verifiedActualOriginalXboxLoadedSoundMetadataCount") != 6 ||
            !r.GetProperty("originalPcSourceAndNativeDiagnosticFfUnmodified").GetBoolean() ||
            r.GetProperty("actualNativeSoundMetadataAndXmaFFGraftCompleted").GetBoolean() ||
            r.GetProperty("xboxCodXeAudioRuntimeProven").GetBoolean())
            throw new InvalidDataException("Six authentic Xbox sound alias/packet metadata witnesses are not complete.");

        var sourceXboxFf = String(r, "originalXboxRetailFf");
        if (!File.Exists(sourceXboxFf) ||
            !Sha(sourceXboxFf).Equals(String(r, "originalXboxRetailFfSha256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Genuine original Xbox retail sound source FF has changed.");

        var allSoundRoots = roots.Where(x => Norm(x.TypeName) == "sound")
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var allLoadedRoots = roots.Where(x => Norm(x.TypeName) == "loadedsound")
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var result = new T4XenonAztecRealFlameAudioGraft();

        foreach (var item in r.GetProperty("exactOriginalXboxSoundAliases").EnumerateArray())
        {
            var soundName = String(item, "originalXboxSoundRoot");
            var aliasName = String(item, "originalXboxAliasName");
            var loadedName = String(item, "sourceOwnedLoadedSoundName");
            if (!allSoundRoots.TryGetValue(soundName, out var soundRoot))
                throw new InvalidDataException(
                    $"Actual original Xbox sound '{soundName}' is not in native Aztec root identities. No partial FF graft.");
            if (Int(item, "originalXboxSoundAliasIndex") != 0 ||
                Int(item, "originalXboxSoundFileType") != 1 ||
                Int(item, "originalXboxSoundFileExists") != 1 ||
                !item.GetProperty("exactOriginalXboxSourceMetadataCaptured").GetBoolean())
                throw new InvalidDataException($"{soundName}: source is not one real SAT_LOADED alias.");
            if (!string.Equals(soundName, aliasName, StringComparison.Ordinal))
                throw new InvalidDataException($"{soundName}: genuine Xbox alias name differs from its Sound root.");

            var aliasHost = Convert.FromHexString(String(item, "originalXboxX86SoundAlias184Hex"));
            var loadedHost = Convert.FromHexString(String(item, "originalXboxX86LoadedSound160Hex"));
            if (aliasHost.Length != 184 || loadedHost.Length != 160)
                throw new InvalidDataException($"{soundName}: actual native 184B/160B audio source metadata lengths drifted.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(aliasHost.AsSpan(0, 4)) == 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(aliasHost.AsSpan(20, 4)) == 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(loadedHost.AsSpan(0, 4)) == 0)
                throw new InvalidDataException($"{soundName}: original Xbox source alias/LoadedSound typed pointer fields invalid.");

            var audioPath = String(item, "authenticSourceXma");
            var xmaSha = String(item, "authenticSourceXmaSha256");
            if (!File.Exists(audioPath) || !Sha(audioPath).Equals(xmaSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{soundName}: genuine Xbox sample SHA is missing/mismatched.");
            var packet = ReadAuthenticXmaRiff(audioPath, loadedHost, soundName);
            if (new FileInfo(audioPath).Length != item.GetProperty("actualRiffXmaFileBytes").GetInt64())
                throw new InvalidDataException($"{soundName}: genuine Xbox RIFF length drift.");

            var subtitle = Optional(item, "originalXboxSubtitle");
            var secondary = Optional(item, "originalXboxSecondaryAlias");
            var chain = Optional(item, "originalXboxChainAlias");
            RequirePointerString(aliasHost, 8, subtitle, soundName, "subtitle");
            RequirePointerString(aliasHost, 12, secondary, soundName, "secondary");
            RequirePointerString(aliasHost, 16, chain, soundName, "chain");

            int? loadedRootIndex = allLoadedRoots.TryGetValue(loadedName, out var loadedRoot)
                ? loadedRoot.Index : null;
            var row = new Row(soundName, soundRoot.Index, aliasName, subtitle,
                secondary, chain, loadedName, loadedRootIndex, aliasHost, loadedHost,
                packet, xmaSha);
            if (!result._sounds.TryAdd(row.SoundRoot, row))
                throw new InvalidDataException($"Duplicate genuine Sound asset root {soundName}.");
            if (loadedRootIndex.HasValue)
            {
                if (result._loaded.TryGetValue(loadedRootIndex.Value, out var prior))
                {
                    if (!prior.XmaSha256.Equals(row.XmaSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Same original Xbox LoadedSound root has conflicting XMA source: {loadedName}");
                }
                else result._loaded.Add(loadedRootIndex.Value, row);
            }
        }
        if (result._sounds.Count != 6)
            throw new InvalidDataException($"Expected 6 real Xbox Sound roots; got {result._sounds.Count}.");
        return result;
    }

    // Verify that all six ORIGINAL encoded source Xbox XMA packet buffers
    // are present BIT-FOR-BIT in the emitted native zone. The OAT 8509
    // root/child pointer readback is a separate required gate.
    public int VerifyNativeZoneOriginalPacketBytes(ReadOnlySpan<byte> bytes)
    {
        var verified = 0;
        foreach (var row in _sounds.Values)
        {
            var source = row.Packet.Data.AsSpan();
            var first = bytes.IndexOf(source);
            if (first < 0)
                throw new InvalidDataException(
                    $"{row.SoundName}: actual source Xbox XMA packet bytes missing from native FF zone.");
            // Each of these six samples has a unique LoadedSound origin.
            if (bytes[(first + 1)..].IndexOf(source) >= 0)
                throw new InvalidDataException(
                    $"{row.SoundName}: unexpected duplicate original Xbox XMA packet in native FF zone.");
            verified++;
        }
        return verified;
    }

    public bool IsSound(int rootIndex) => _sounds.ContainsKey(rootIndex);
    public bool IsLoaded(int rootIndex) => _loaded.ContainsKey(rootIndex);

    public void PlanSound(int rootIndex,
        Action<long, int, bool> virtualAlloc, Action<int> tempRoot,
        Action<long, int, bool> largeAlloc)
    {
        var row = _sounds[rootIndex];
        tempRoot(12);
        virtualAlloc(Utf8Z(row.SoundName), 1, true);
        // "set reusable head" in pinned T4Xenon ZoneCode.
        virtualAlloc(4, 4, false);
        virtualAlloc(184, 4, true);
        PlanAliasChildren(row, virtualAlloc);
        // "set reusable soundFile" in the same ZoneCode.
        virtualAlloc(4, 4, false);
        virtualAlloc(20, 4, true);
        if (!row.LoadedRoot.HasValue)
        {
            // Inline owned LoadedSound for usermap roots that do not have a
            // separate native XAsset root; not a fake sample or stock claim.
            virtualAlloc(160, 4, true);
            PlanLoadedChildren(row, virtualAlloc, largeAlloc);
        }
    }

    public void PlanLoaded(int rootIndex,
        Action<long, int, bool> virtualAlloc, Action<int> tempRoot,
        Action<long, int, bool> largeAlloc)
    {
        var row = _loaded[rootIndex];
        tempRoot(160);
        PlanLoadedChildren(row, virtualAlloc, largeAlloc);
    }

    private static void PlanAliasChildren(Row row, Action<long, int, bool> virtualAlloc)
    {
        virtualAlloc(Utf8Z(row.AliasName), 1, true);
        foreach (var extra in new[] { row.Subtitle, row.Secondary, row.Chain })
            if (extra is not null) virtualAlloc(Utf8Z(extra), 1, true);
    }

    private static void PlanLoadedChildren(Row row,
        Action<long, int, bool> virtualAlloc, Action<long, int, bool> largeAlloc)
    {
        virtualAlloc(Utf8Z(row.LoadedName), 1, true);
        // Source pBuffer belongs in Xbox LARGE, with 2048 alignment.
        // Reusable block pointer uses an INSERT slot before first data.
        largeAlloc(4, 4, false);
        largeAlloc(row.Packet.Data.Length, 2048, true);
        if (row.Packet.Seek.Length > 0)
        {
            virtualAlloc(4, 4, false);
            virtualAlloc(row.Packet.Seek.Length, 4, true);
        }
    }

    public void EmitSound(int rootIndex, Stream zone,
        Action<long, int, bool> virtualAlloc, Action<int> tempRoot,
        Action<long, int, bool> largeAlloc,
        IReadOnlyDictionary<int, uint> stableByRoot)
    {
        var row = _sounds[rootIndex];
        var list = new byte[12];
        PutBe(list, 0, T4ZonePointer.Following);
        PutBe(list, 4, T4ZonePointer.Insert);
        PutBe(list, 8, 1u);
        tempRoot(12);
        zone.Write(list);
        EmitString(zone, virtualAlloc, row.SoundName);
        virtualAlloc(4, 4, false);
        virtualAlloc(184, 4, true);
        zone.Write(BuildAliasBody(row));
        EmitString(zone, virtualAlloc, row.AliasName);
        foreach (var extra in new[] { row.Subtitle, row.Secondary, row.Chain })
            if (extra is not null) EmitString(zone, virtualAlloc, extra);
        virtualAlloc(4, 4, false);
        virtualAlloc(20, 4, true);
        var sf = new byte[20];
        sf[0] = 1; // SAT_LOADED, source witnessed
        sf[1] = 1; // exists, source witnessed
        PutBe(sf, 4, row.LoadedRoot.HasValue
            ? stableByRoot[row.LoadedRoot.Value]
            : T4ZonePointer.Following);
        zone.Write(sf);
        if (!row.LoadedRoot.HasValue)
        {
            virtualAlloc(160, 4, true);
            EmitLoadedBody(zone, virtualAlloc, largeAlloc, row);
        }
    }

    public void EmitLoaded(int rootIndex, Stream zone,
        Action<long, int, bool> virtualAlloc, Action<int> tempRoot,
        Action<long, int, bool> largeAlloc)
    {
        var row = _loaded[rootIndex];
        tempRoot(160);
        EmitLoadedBody(zone, virtualAlloc, largeAlloc, row);
    }

    private static void EmitLoadedBody(Stream zone,
        Action<long, int, bool> virtualAlloc,
        Action<long, int, bool> largeAlloc, Row row)
    {
        var b = (byte[])row.LoadedHost.Clone();
        // Original X86 source scalar bytes -> original Xbox 360 big-endian
        // fields, preserving byte fields of XMA loop regions/stream formats.
        foreach (var offset in new[] { 8, 12, 148, 152 })
            Swap32(b, offset);
        for (var i = 0; i < 6; i++)
        {
            Swap32(b, 16 + 12 * i);
            Swap32(b, 20 + 12 * i);
            Swap32(b, 100 + 8 * i);
        }
        PutBe(b, 0, T4ZonePointer.Following); // LoadedSound.name
        PutBe(b, 4, T4ZonePointer.Insert);   // XMA packet pBuffer
        PutBe(b, 88, 0u);                   // transient pContext
        PutBe(b, 156, row.Packet.Seek.Length == 0
            ? 0u : T4ZonePointer.Insert);
        zone.Write(b);
        EmitString(zone, virtualAlloc, row.LoadedName);
        largeAlloc(4, 4, false);
        largeAlloc(row.Packet.Data.Length, 2048, true);
        zone.Write(row.Packet.Data); // native original Xbox XMA frames, unchanged
        if (row.Packet.Seek.Length > 0)
        {
            virtualAlloc(4, 4, false);
            virtualAlloc(row.Packet.Seek.Length, 4, true);
            var seek = (byte[])row.Packet.Seek.Clone();
            for (var i = 0; i < seek.Length; i += 4) Swap32(seek, i);
            zone.Write(seek);
        }
    }

    private static byte[] BuildAliasBody(Row row)
    {
        var b = (byte[])row.AliasHost.Clone();
        for (var i = 0; i < b.Length; i += 4) Swap32(b, i);
        PutBe(b, 0, T4ZonePointer.Following);
        PutBe(b, 8, row.Subtitle is null ? 0u : T4ZonePointer.Following);
        PutBe(b, 12, row.Secondary is null ? 0u : T4ZonePointer.Following);
        PutBe(b, 16, row.Chain is null ? 0u : T4ZonePointer.Following);
        PutBe(b, 20, T4ZonePointer.Insert);
        return b;
    }

    private static RiffPacket ReadAuthenticXmaRiff(string path, byte[] host, string asset)
    {
        var wave = File.ReadAllBytes(path);
        if (wave.Length < 64 || !FourCc(wave, 0, "RIFF") || !FourCc(wave, 8, "WAVE"))
            throw new InvalidDataException($"{asset}: original Xbox sample is not RIFF/WAVE.");
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(4, 4));
        if (riffSize + 8L > wave.LongLength || riffSize < 50)
            throw new InvalidDataException($"{asset}: original Xbox RIFF length is corrupt.");
        var fmt = ReadChunk(wave, "fmt ");
        var seek = ReadChunk(wave, "seek");
        var iwxi = ReadChunk(wave, "IWXI");
        var packet = ReadChunk(wave, "data");
        if (fmt.Length < 32 || iwxi.Length != 4 || seek.Length % 4 != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(0, 2)) != 0x0165)
            throw new InvalidDataException($"{asset}: original Xbox XMA format/seek chunks mismatch.");
        var streams = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(8, 2));
        if (streams is < 1 or > 6 || fmt.Length != 12 + 20 * streams ||
            host[92] != 3 || host[96] != streams)
            throw new InvalidDataException($"{asset}: original Xbox XMA stream count/sample type mismatch.");
        var originalSize = BinaryPrimitives.ReadUInt32LittleEndian(host.AsSpan(8, 4));
        var originalSeekCount = BinaryPrimitives.ReadInt32LittleEndian(host.AsSpan(152, 4));
        var duration = BinaryPrimitives.ReadInt32LittleEndian(iwxi);
        if (originalSize != packet.Length || originalSize == 0 ||
            originalSeekCount < 0 || originalSeekCount != seek.Length / 4 ||
            BinaryPrimitives.ReadInt32LittleEndian(host.AsSpan(148, 4)) != duration ||
            BinaryPrimitives.ReadUInt32LittleEndian(host.AsSpan(12, 4)) != fmt[10])
            throw new InvalidDataException($"{asset}: XMA packet size, loop count, duration or seek count is inconsistent with OAT-owned LoadedSound.");
        for (var i = 0; i < streams; i++)
        {
            var o = 12 + 20 * i;
            var rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(o + 4, 4));
            var loopBegin = BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(o + 8, 4));
            var loopEnd = BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(o + 12, 4));
            if (rate != BinaryPrimitives.ReadUInt32LittleEndian(host.AsSpan(100 + i * 8, 4)) ||
                fmt[o + 17] != host[104 + i * 8] ||
                loopBegin != BinaryPrimitives.ReadUInt32LittleEndian(host.AsSpan(16 + i * 12, 4)) ||
                loopEnd != BinaryPrimitives.ReadUInt32LittleEndian(host.AsSpan(20 + i * 12, 4)) ||
                fmt[o + 16] != ((host[24 + i * 12] << 4) | host[25 + i * 12]))
                throw new InvalidDataException($"{asset}: genuine Xbox source XMA stream format or loop metadata differs from packet RIFF.");
        }
        return new(packet, seek, streams, fmt[10], duration);
    }

    private static byte[] ReadChunk(byte[] wave, string kind)
    {
        var size = BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(4, 4));
        var end = checked((int)(size + 8));
        for (var offset = 12; offset + 8 <= end;)
        {
            var count = BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(offset + 4, 4));
            var start = offset + 8;
            if (count > end - start) throw new InvalidDataException("Original Xbox source RIFF chunk exceeds container.");
            if (FourCc(wave, offset, kind))
                return wave.AsSpan(start, checked((int)count)).ToArray();
            offset = checked(start + (int)count + ((int)count & 1));
        }
        throw new InvalidDataException($"Missing original OAT XMA RIFF chunk {kind}.");
    }

    private static bool FourCc(byte[] bytes, int offset, string id) =>
        bytes.AsSpan(offset, 4).SequenceEqual(Encoding.ASCII.GetBytes(id));

    private static void RequirePointerString(byte[] host, int off, string? value,
        string asset, string field)
    {
        var pointer = BinaryPrimitives.ReadUInt32LittleEndian(host.AsSpan(off, 4));
        if ((pointer == 0) != (value is null))
            throw new InvalidDataException($"{asset}: genuine Xbox {field} pointer/string mismatch.");
    }

    private static string Sha(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Norm(string value) =>
        value.Replace("_", "", StringComparison.Ordinal)
             .Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
    private static string String(JsonElement row, string name) =>
        row.GetProperty(name).GetString() ??
        throw new InvalidDataException($"Native source audio metadata '{name}' is null.");
    private static string? Optional(JsonElement row, string name) =>
        row.GetProperty(name).ValueKind == JsonValueKind.Null
        ? null : String(row, name);
    private static int Int(JsonElement row, string name) => row.GetProperty(name).GetInt32();
    private static int Utf8Z(string v) => Encoding.UTF8.GetByteCount(v) + 1;
    private static void PutBe(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
    private static void Swap32(byte[] b, int offset) =>
        (b[offset], b[offset + 1], b[offset + 2], b[offset + 3]) =
        (b[offset + 3], b[offset + 2], b[offset + 1], b[offset]);

    private static void EmitString(Stream zone, Action<long, int, bool> virtualAlloc, string name)
    {
        var b = Encoding.UTF8.GetBytes(name);
        virtualAlloc(b.Length + 1L, 1, true);
        zone.Write(b);
        zone.WriteByte(0);
    }
}
