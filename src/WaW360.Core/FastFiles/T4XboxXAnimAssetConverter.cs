using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4XboxXAnimConvertedAsset(
    string Name,
    int SourceStart,
    int SourceEnd,
    byte[] Bytes,
    byte[] BoneCounts,
    int NormalDynamicKeys,
    int PrecisionDynamicKeys,
    int NormalConstantBones,
    int PrecisionConstantBones,
    bool HasDelta);

/// <summary>
/// Typed PC T4 XAnimParts -> Xbox 360 T4 XAnimParts converter.
///
/// PC layout:    0x58 fixed bytes, 10 boneCount buckets.
/// Xbox layout:  0x5C fixed bytes, 12 boneCount buckets.
///
/// Console quaternion buckets:
///   0 NO_QUAT
///   1 SIMPLE_QUAT
///   2 NORMAL_QUAT
///   3 PRECISION_QUAT
///   4 SIMPLE_QUAT_NO_SIZE
///   5 NORMAL_QUAT_NO_SIZE
///   6 PRECISION_QUAT_NO_SIZE
///   7 SMALL_TRANS
///   8 TRANS
///   9 TRANS_NO_SIZE
///  10 NO_TRANS
///  11 ALL
///
/// NORMAL/PRECISION classification is the retail-proven rule:
/// every viewmodel_ animation is precision; otherwise the legacy 18 body bones
/// are precision and all other FULL_QUAT/FULL_QUAT_NO_SIZE bones are normal.
/// </summary>
public static class T4XboxXAnimAssetConverter
{
    private const uint Following = 0xFFFFFFFFu;
    private const uint Insert = 0xFFFFFFFEu;
    private const int PcFixedSize = 0x58;
    private const int XboxFixedSize = 0x5C;

    private static readonly HashSet<string> LegacyHighPrecisionBones = new(StringComparer.OrdinalIgnoreCase)
    {
        "j_ankle_le",
        "j_ankle_ri",
        "j_clavicle_le",
        "j_clavicle_ri",
        "j_elbow_le",
        "j_elbow_ri",
        "j_hip_le",
        "j_hip_ri",
        "j_knee_le",
        "j_knee_ri",
        "j_mainroot",
        "j_shoulder_le",
        "j_shoulder_ri",
        "j_spine4",
        "j_spinelower",
        "j_spineupper",
        "j_wrist_le",
        "j_wrist_ri"
    };

    private enum PcQuatType
    {
        None = 0,
        Simple = 1,
        Full = 2,
        SimpleConstant = 3,
        FullConstant = 4
    }

    private enum XboxQuatType
    {
        None = 0,
        Simple = 1,
        Normal = 2,
        Precision = 3,
        SimpleConstant = 4,
        NormalConstant = 5,
        PrecisionConstant = 6
    }

    private enum PcTransType
    {
        Small = 5,
        Full = 6,
        Constant = 7,
        None = 8
    }

    private sealed class Bone
    {
        public int OriginalIndex;
        public int NewIndex;
        public ushort ScriptString;
        public required string Name;
        public PcQuatType PcQuat;
        public XboxQuatType XboxQuat;
        public ushort[] QuatIndices = Array.Empty<ushort>();
        public short[] SimpleQuatFrames = Array.Empty<short>();
        public short[] FullQuatFrames = Array.Empty<short>();
        public PcTransType Trans;
        public ushort[] TransIndices = Array.Empty<ushort>();
        public uint[] TransMins = Array.Empty<uint>();
        public uint[] TransSize = Array.Empty<uint>();
        public uint[] TransConstant = Array.Empty<uint>();
        public byte[] SmallTransFrames = Array.Empty<byte>();
        public ushort[] FullTransFrames = Array.Empty<ushort>();
    }

    private sealed record Header(
        uint NamePointer,
        ushort DataByteCount,
        ushort DataShortCount,
        ushort DataIntCount,
        ushort RandomDataByteCount,
        ushort RandomDataIntCount,
        ushort NumFrames,
        byte Flag0,
        byte Flag1,
        byte[] BoneCounts,
        byte NotifyCount,
        byte AssetType,
        byte IsDefault,
        byte Pad,
        uint RandomDataShortCount,
        uint IndexCount,
        uint FrameRateBits,
        uint FrequencyBits,
        uint NamesPointer,
        uint DataBytePointer,
        uint DataShortPointer,
        uint DataIntPointer,
        uint RandomDataShortPointer,
        uint RandomDataBytePointer,
        uint RandomDataIntPointer,
        uint IndicesPointer,
        uint NotifyPointer,
        uint DeltaPointer);

    private sealed record Notify(ushort Name, ushort Pad, uint TimeBits);

    private sealed record SourceStreams(
        byte[] DataByte,
        short[] DataShort,
        uint[] DataInt,
        short[] RandomShort,
        byte[] RandomByte,
        uint[] RandomInt,
        ushort[] Indices);

    private sealed class ReadCursor
    {
        private readonly SourceStreams _s;
        private readonly bool _byteIndices;
        public int Db;
        public int Ds;
        public int Di;
        public int Rs;
        public int Rb;
        public int Ri;
        public int Ix;

        public ReadCursor(SourceStreams streams, bool byteIndices)
        {
            _s = streams;
            _byteIndices = byteIndices;
        }

        public byte PopDataByte()
        {
            if (Db >= _s.DataByte.Length) throw new InvalidDataException("XAnim exhausted dataByte.");
            return _s.DataByte[Db++];
        }

        public short PopDataShort()
        {
            if (Ds >= _s.DataShort.Length) throw new InvalidDataException("XAnim exhausted dataShort.");
            return _s.DataShort[Ds++];
        }

        public uint PopDataInt()
        {
            if (Di >= _s.DataInt.Length) throw new InvalidDataException("XAnim exhausted dataInt.");
            return _s.DataInt[Di++];
        }

        public ushort[] ReadPackedIndices(ushort storedSize)
        {
            var count = checked((int)storedSize + 1);
            var result = new ushort[count];

            if (_byteIndices)
            {
                for (var i = 0; i < count; i++) result[i] = PopDataByte();
                return result;
            }

            if (storedSize >= 64)
            {
                if (Ix > _s.Indices.Length - count) throw new InvalidDataException("XAnim exhausted indices.");
                Array.Copy(_s.Indices, Ix, result, 0, count);
                Ix += count;
                var checkpoints = ((count - 2) / 256) + 2;
                if (Ds > _s.DataShort.Length - checkpoints) throw new InvalidDataException("XAnim exhausted dataShort checkpoints.");
                Ds += checkpoints;
                return result;
            }

            if (Ds > _s.DataShort.Length - count) throw new InvalidDataException("XAnim exhausted short indices.");
            for (var i = 0; i < count; i++) result[i] = unchecked((ushort)_s.DataShort[Ds++]);
            return result;
        }

        public short[] ReadRandomShort(int count)
        {
            if (Rs > _s.RandomShort.Length - count) throw new InvalidDataException("XAnim exhausted randomDataShort.");
            var result = new short[count];
            Array.Copy(_s.RandomShort, Rs, result, 0, count);
            Rs += count;
            return result;
        }

        public byte[] ReadRandomByte(int count)
        {
            if (Rb > _s.RandomByte.Length - count) throw new InvalidDataException("XAnim exhausted randomDataByte.");
            var result = new byte[count];
            Array.Copy(_s.RandomByte, Rb, result, 0, count);
            Rb += count;
            return result;
        }

        public ushort[] ReadRandomU16(int count)
        {
            var raw = ReadRandomShort(count);
            var result = new ushort[count];
            for (var i = 0; i < count; i++) result[i] = unchecked((ushort)raw[i]);
            return result;
        }

        public void ExpectEnd()
        {
            if (Db != _s.DataByte.Length ||
                Ds != _s.DataShort.Length ||
                Di != _s.DataInt.Length ||
                Rs != _s.RandomShort.Length ||
                Rb != _s.RandomByte.Length ||
                Ri != _s.RandomInt.Length ||
                Ix != _s.Indices.Length)
            {
                throw new InvalidDataException(
                    $"PC XAnim flat-stream decode did not consume every entry: " +
                    $"dataByte={Db}/{_s.DataByte.Length}, dataShort={Ds}/{_s.DataShort.Length}, " +
                    $"dataInt={Di}/{_s.DataInt.Length}, randomShort={Rs}/{_s.RandomShort.Length}, " +
                    $"randomByte={Rb}/{_s.RandomByte.Length}, randomInt={Ri}/{_s.RandomInt.Length}, " +
                    $"indices={Ix}/{_s.Indices.Length}.");
            }
        }
    }

    private sealed class WriteCursor
    {
        public readonly List<byte> DataByte = new();
        public readonly List<short> DataShort = new();
        public readonly List<uint> DataInt = new();
        public readonly List<short> RandomShort = new();
        public readonly List<byte> RandomByte = new();
        public readonly List<uint> RandomInt = new();
        public readonly List<ushort> Indices = new();

        private readonly bool _byteIndices;

        public WriteCursor(bool byteIndices) => _byteIndices = byteIndices;

        public void WritePackedIndices(IReadOnlyList<ushort> indices)
        {
            if (indices.Count <= 0) throw new InvalidDataException("Dynamic XAnim track has no frame indices.");
            var storedSize = checked((ushort)(indices.Count - 1));
            DataShort.Add(unchecked((short)storedSize));

            if (_byteIndices)
            {
                foreach (var index in indices)
                {
                    if (index > byte.MaxValue) throw new InvalidDataException($"Byte-indexed XAnim frame {index} exceeds 255.");
                    DataByte.Add((byte)index);
                }
                return;
            }

            if (storedSize >= 64)
            {
                foreach (var index in indices) Indices.Add(index);
                var longTableSize = ((indices.Count - 2) / 256) + 1;
                for (var i = 0; i < longTableSize; i++)
                    DataShort.Add(unchecked((short)indices[256 * i]));
                DataShort.Add(unchecked((short)indices[^1]));
                return;
            }

            foreach (var index in indices)
                DataShort.Add(unchecked((short)index));
        }
    }

    public static T4XboxXAnimConvertedAsset ConvertInlinePcAsset(
        byte[] zone,
        int start,
        Func<ushort, string> resolveScriptString)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(resolveScriptString);

        var header = ReadPcHeader(zone, start);
        if (!IsInline(header.NamePointer))
            throw new InvalidDataException($"XAnim at 0x{start:X} has a packed top-level name; console transform requires a resolved inline name.");
        RequireOwnedInlinePointers(header, start);

        var cursor = checked(start + PcFixedSize);
        var (assetName, nameBytes) = ReadCString(zone, cursor);
        cursor += nameBytes;

        var boneCount = header.BoneCounts[9];
        var scriptIds = new ushort[boneCount];
        if (header.NamesPointer != 0)
        {
            Ensure(zone, cursor, checked(boneCount * 2), "XAnim.names");
            for (var i = 0; i < boneCount; i++)
            {
                scriptIds[i] = ReadU16Le(zone, cursor);
                cursor += 2;
            }
        }
        else if (boneCount != 0)
        {
            throw new InvalidDataException($"XAnim '{assetName}' has {boneCount} bones but no names pointer.");
        }

        var notifies = new List<Notify>(header.NotifyCount);
        if (header.NotifyPointer != 0)
        {
            Ensure(zone, cursor, checked(header.NotifyCount * 8), "XAnim.notify");
            for (var i = 0; i < header.NotifyCount; i++)
            {
                notifies.Add(new Notify(
                    ReadU16Le(zone, cursor),
                    ReadU16Le(zone, cursor + 2),
                    ReadU32Le(zone, cursor + 4)));
                cursor += 8;
            }
        }

        byte[] deltaXbox = Array.Empty<byte>();
        if (header.DeltaPointer != 0)
        {
            using var delta = new MemoryStream();
            cursor = ConvertDelta(zone, cursor, delta, header.NumFrames);
            deltaXbox = delta.ToArray();
        }

        var streams = ReadSourceStreams(zone, ref cursor, header);
        var sourceEnd = cursor;

        var bones = new Bone[boneCount];
        for (var i = 0; i < boneCount; i++)
        {
            var name = resolveScriptString(scriptIds[i]);
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("<alias ", StringComparison.Ordinal))
                throw new InvalidDataException($"XAnim '{assetName}' cannot resolve ScriptString {scriptIds[i]} for bone {i}.");

            bones[i] = new Bone
            {
                OriginalIndex = i,
                ScriptString = scriptIds[i],
                Name = name
            };
        }

        DecodePcTracks(assetName, header, streams, bones);

        var wholeAnimationPrecision = assetName.StartsWith("viewmodel_", StringComparison.OrdinalIgnoreCase);
        foreach (var bone in bones)
        {
            var precision = wholeAnimationPrecision || LegacyHighPrecisionBones.Contains(bone.Name);
            bone.XboxQuat = bone.PcQuat switch
            {
                PcQuatType.None => XboxQuatType.None,
                PcQuatType.Simple => XboxQuatType.Simple,
                PcQuatType.Full => precision ? XboxQuatType.Precision : XboxQuatType.Normal,
                PcQuatType.SimpleConstant => XboxQuatType.SimpleConstant,
                PcQuatType.FullConstant => precision ? XboxQuatType.PrecisionConstant : XboxQuatType.NormalConstant,
                _ => throw new InvalidDataException($"XAnim '{assetName}' has unsupported PC quaternion type {bone.PcQuat}.")
            };
        }

        var xboxOrder = bones
            .OrderBy(b => (int)b.XboxQuat)
            .ThenBy(b => b.OriginalIndex)
            .ToArray();
        for (var i = 0; i < xboxOrder.Length; i++)
            xboxOrder[i].NewIndex = i;

        var byNewIndex = xboxOrder.OrderBy(b => b.NewIndex).ToArray();
        var outStreams = new WriteCursor(header.NumFrames < 256);

        var normalDynamicKeys = 0;
        var precisionDynamicKeys = 0;
        var normalConstantBones = 0;
        var precisionConstantBones = 0;

        foreach (var bone in byNewIndex)
        {
            switch (bone.XboxQuat)
            {
                case XboxQuatType.None:
                    break;

                case XboxQuatType.Simple:
                    outStreams.WritePackedIndices(bone.QuatIndices);
                    for (var frame = 0; frame < bone.QuatIndices.Length; frame++)
                    {
                        var q = checked(frame * 2);
                        outStreams.RandomShort.Add(unchecked((short)T4XboxXAnimQuaternionCodec.EncodeSimple(
                            bone.SimpleQuatFrames[q + 0],
                            bone.SimpleQuatFrames[q + 1])));
                    }
                    break;

                case XboxQuatType.Normal:
                    outStreams.WritePackedIndices(bone.QuatIndices);
                    for (var frame = 0; frame < bone.QuatIndices.Length; frame++)
                    {
                        var q = checked(frame * 4);
                        outStreams.RandomInt.Add(T4XboxXAnimQuaternionCodec.EncodeNormal(
                            bone.FullQuatFrames[q + 0],
                            bone.FullQuatFrames[q + 1],
                            bone.FullQuatFrames[q + 2],
                            bone.FullQuatFrames[q + 3]));
                        normalDynamicKeys++;
                    }
                    break;

                case XboxQuatType.Precision:
                    outStreams.WritePackedIndices(bone.QuatIndices);
                    for (var frame = 0; frame < bone.QuatIndices.Length; frame++)
                    {
                        var q = checked(frame * 4);
                        var packed = T4XboxXAnimQuaternionCodec.EncodePrecision(
                            bone.FullQuatFrames[q + 0],
                            bone.FullQuatFrames[q + 1],
                            bone.FullQuatFrames[q + 2],
                            bone.FullQuatFrames[q + 3]);
                        outStreams.RandomShort.Add(unchecked((short)packed.Word0));
                        outStreams.RandomShort.Add(unchecked((short)packed.Word1));
                        outStreams.RandomShort.Add(unchecked((short)packed.Word2));
                        precisionDynamicKeys++;
                    }
                    break;

                case XboxQuatType.SimpleConstant:
                    outStreams.DataShort.Add(unchecked((short)T4XboxXAnimQuaternionCodec.EncodeSimple(
                        bone.SimpleQuatFrames[0],
                        bone.SimpleQuatFrames[1])));
                    break;

                case XboxQuatType.NormalConstant:
                {
                    var packed = T4XboxXAnimQuaternionCodec.EncodeNormal(
                        bone.FullQuatFrames[0],
                        bone.FullQuatFrames[1],
                        bone.FullQuatFrames[2],
                        bone.FullQuatFrames[3]);
                    outStreams.DataInt.Add(packed);
                    normalConstantBones++;
                    break;
                }

                case XboxQuatType.PrecisionConstant:
                {
                    var packed = T4XboxXAnimQuaternionCodec.EncodePrecision(
                        bone.FullQuatFrames[0],
                        bone.FullQuatFrames[1],
                        bone.FullQuatFrames[2],
                        bone.FullQuatFrames[3]);
                    outStreams.DataShort.Add(unchecked((short)packed.Word0));
                    outStreams.DataShort.Add(unchecked((short)packed.Word1));
                    outStreams.DataShort.Add(unchecked((short)packed.Word2));
                    precisionConstantBones++;
                    break;
                }
            }
        }

        var transOrder = bones
            .OrderBy(b => (int)b.Trans)
            .ThenBy(b => b.NewIndex)
            .ToArray();

        foreach (var bone in transOrder)
        {
            if ((uint)bone.NewIndex > byte.MaxValue)
                throw new InvalidDataException($"XAnim '{assetName}' bone index {bone.NewIndex} exceeds uint8 translation index storage.");
            outStreams.DataByte.Add((byte)bone.NewIndex);

            switch (bone.Trans)
            {
                case PcTransType.Small:
                    outStreams.WritePackedIndices(bone.TransIndices);
                    foreach (var value in bone.TransMins) outStreams.DataInt.Add(value);
                    foreach (var value in bone.TransSize) outStreams.DataInt.Add(value);
                    outStreams.RandomByte.AddRange(bone.SmallTransFrames);
                    break;

                case PcTransType.Full:
                    outStreams.WritePackedIndices(bone.TransIndices);
                    foreach (var value in bone.TransMins) outStreams.DataInt.Add(value);
                    foreach (var value in bone.TransSize) outStreams.DataInt.Add(value);
                    foreach (var value in bone.FullTransFrames) outStreams.RandomShort.Add(unchecked((short)value));
                    break;

                case PcTransType.Constant:
                    foreach (var value in bone.TransConstant) outStreams.DataInt.Add(value);
                    break;

                case PcTransType.None:
                    break;

                default:
                    throw new InvalidDataException($"XAnim '{assetName}' has unsupported translation type {bone.Trans}.");
            }
        }

        var xboxCounts = BuildXboxCounts(byNewIndex);
        var blob = WriteXboxBlob(
            assetName,
            header,
            xboxCounts,
            byNewIndex,
            notifies,
            deltaXbox,
            outStreams);

        ValidateXboxBlob(blob, assetName);

        return new T4XboxXAnimConvertedAsset(
            assetName,
            start,
            sourceEnd,
            blob,
            xboxCounts,
            normalDynamicKeys,
            precisionDynamicKeys,
            normalConstantBones,
            precisionConstantBones,
            header.DeltaPointer != 0);
    }

    public static void ValidateXboxBlob(byte[] data, string? expectedName = null)
    {
        Ensure(data, 0, XboxFixedSize, "Xbox XAnim fixed body");

        var namePtr = ReadU32Be(data, 0x00);
        var dataByteCount = ReadU16Be(data, 0x04);
        var dataShortCount = ReadU16Be(data, 0x06);
        var dataIntCount = ReadU16Be(data, 0x08);
        var randomByteCount = ReadU16Be(data, 0x0A);
        var randomIntCount = ReadU16Be(data, 0x0C);
        var numFrames = ReadU16Be(data, 0x0E);
        var boneCounts = data.AsSpan(0x12, 12).ToArray();
        var notifyCount = data[0x1E];
        var randomShortCount = ReadU32Be(data, 0x24);
        var indexCount = ReadU32Be(data, 0x28);

        var ptrNames = ReadU32Be(data, 0x34);
        var ptrDataByte = ReadU32Be(data, 0x38);
        var ptrDataShort = ReadU32Be(data, 0x3C);
        var ptrDataInt = ReadU32Be(data, 0x40);
        var ptrRandomShort = ReadU32Be(data, 0x44);
        var ptrRandomByte = ReadU32Be(data, 0x48);
        var ptrRandomInt = ReadU32Be(data, 0x4C);
        var ptrIndices = ReadU32Be(data, 0x50);
        var ptrNotify = ReadU32Be(data, 0x54);
        var ptrDelta = ReadU32Be(data, 0x58);

        var quatTotal = boneCounts.Take(7).Sum(x => (int)x);
        var transTotal = boneCounts.Skip(7).Take(4).Sum(x => (int)x);
        if (quatTotal != boneCounts[11] || transTotal != boneCounts[11])
            throw new InvalidDataException($"Xbox XAnim bucket totals do not equal ALL ({quatTotal}/{transTotal}/{boneCounts[11]}).");

        var cursor = XboxFixedSize;
        string name = "<packed>";
        if (IsInline(namePtr))
        {
            var readName = ReadCString(data, cursor);
            name = readName.Value;
            cursor += readName.Bytes;
        }

        if (expectedName is not null && !name.Equals(expectedName, StringComparison.Ordinal))
            throw new InvalidDataException($"Xbox XAnim read-back name '{name}' != '{expectedName}'.");

        if (ptrNames != 0) cursor = Advance(data, cursor, boneCounts[11] * 2, "Xbox names");
        if (ptrNotify != 0) cursor = Advance(data, cursor, notifyCount * 8, "Xbox notify");

        if (ptrDelta != 0)
        {
            Ensure(data, cursor, 8, "Xbox deltaPart");
            var transPtr = ReadU32Be(data, cursor);
            var quatPtr = ReadU32Be(data, cursor + 4);
            cursor += 8;
            if (transPtr != 0) cursor = SkipDeltaTransBe(data, cursor, numFrames);
            if (quatPtr != 0) cursor = SkipDeltaQuatBe(data, cursor, numFrames);
        }

        if (ptrDataByte != 0) cursor = Advance(data, cursor, dataByteCount, "Xbox dataByte");
        if (ptrDataShort != 0) cursor = Advance(data, cursor, dataShortCount * 2, "Xbox dataShort");
        if (ptrDataInt != 0) cursor = Advance(data, cursor, dataIntCount * 4, "Xbox dataInt");
        if (ptrRandomShort != 0) cursor = Advance(data, cursor, checked((int)randomShortCount * 2), "Xbox randomDataShort");
        if (ptrRandomByte != 0) cursor = Advance(data, cursor, randomByteCount, "Xbox randomDataByte");
        if (ptrRandomInt != 0) cursor = Advance(data, cursor, randomIntCount * 4, "Xbox randomDataInt");
        if (ptrIndices != 0) cursor = Advance(data, cursor, checked((int)indexCount * (numFrames < 256 ? 1 : 2)), "Xbox indices");

        if (cursor != data.Length)
            throw new InvalidDataException($"Xbox XAnim '{name}' read-back ended at 0x{cursor:X}, blob is 0x{data.Length:X} bytes.");
    }

    private static Header ReadPcHeader(byte[] data, int start)
    {
        Ensure(data, start, PcFixedSize, "PC XAnim fixed body");
        return new Header(
            ReadU32Le(data, start + 0x00),
            ReadU16Le(data, start + 0x04),
            ReadU16Le(data, start + 0x06),
            ReadU16Le(data, start + 0x08),
            ReadU16Le(data, start + 0x0A),
            ReadU16Le(data, start + 0x0C),
            ReadU16Le(data, start + 0x0E),
            data[start + 0x10],
            data[start + 0x11],
            data.AsSpan(start + 0x12, 10).ToArray(),
            data[start + 0x1C],
            data[start + 0x1D],
            data[start + 0x1E],
            data[start + 0x1F],
            ReadU32Le(data, start + 0x20),
            ReadU32Le(data, start + 0x24),
            ReadU32Le(data, start + 0x28),
            ReadU32Le(data, start + 0x2C),
            ReadU32Le(data, start + 0x30),
            ReadU32Le(data, start + 0x34),
            ReadU32Le(data, start + 0x38),
            ReadU32Le(data, start + 0x3C),
            ReadU32Le(data, start + 0x40),
            ReadU32Le(data, start + 0x44),
            ReadU32Le(data, start + 0x48),
            ReadU32Le(data, start + 0x4C),
            ReadU32Le(data, start + 0x50),
            ReadU32Le(data, start + 0x54));
    }

    private static void RequireOwnedInlinePointers(Header h, int start)
    {
        foreach (var (label, value) in new[]
        {
            ("names", h.NamesPointer),
            ("dataByte", h.DataBytePointer),
            ("dataShort", h.DataShortPointer),
            ("dataInt", h.DataIntPointer),
            ("randomDataShort", h.RandomDataShortPointer),
            ("randomDataByte", h.RandomDataBytePointer),
            ("randomDataInt", h.RandomDataIntPointer),
            ("indices", h.IndicesPointer),
            ("notify", h.NotifyPointer),
            ("delta", h.DeltaPointer)
        })
        {
            if (value != 0 && !IsInline(value))
                throw new InvalidDataException($"XAnim at 0x{start:X} has non-inline owned pointer {label}=0x{value:X8}.");
        }
    }

    private static SourceStreams ReadSourceStreams(byte[] zone, ref int cursor, Header h)
    {
        byte[] db = Array.Empty<byte>();
        short[] ds = Array.Empty<short>();
        uint[] di = Array.Empty<uint>();
        short[] rs = Array.Empty<short>();
        byte[] rb = Array.Empty<byte>();
        uint[] ri = Array.Empty<uint>();
        ushort[] ix = Array.Empty<ushort>();

        if (h.DataBytePointer != 0)
        {
            Ensure(zone, cursor, h.DataByteCount, "PC dataByte");
            db = zone.AsSpan(cursor, h.DataByteCount).ToArray();
            cursor += h.DataByteCount;
        }
        else if (h.DataByteCount != 0) throw new InvalidDataException("PC XAnim dataByte count has null pointer.");

        if (h.DataShortPointer != 0)
        {
            ds = ReadI16ArrayLe(zone, ref cursor, h.DataShortCount);
        }
        else if (h.DataShortCount != 0) throw new InvalidDataException("PC XAnim dataShort count has null pointer.");

        if (h.DataIntPointer != 0)
        {
            di = ReadU32ArrayLe(zone, ref cursor, h.DataIntCount);
        }
        else if (h.DataIntCount != 0) throw new InvalidDataException("PC XAnim dataInt count has null pointer.");

        if (h.RandomDataShortPointer != 0)
        {
            rs = ReadI16ArrayLe(zone, ref cursor, checked((int)h.RandomDataShortCount));
        }
        else if (h.RandomDataShortCount != 0) throw new InvalidDataException("PC XAnim randomDataShort count has null pointer.");

        if (h.RandomDataBytePointer != 0)
        {
            Ensure(zone, cursor, h.RandomDataByteCount, "PC randomDataByte");
            rb = zone.AsSpan(cursor, h.RandomDataByteCount).ToArray();
            cursor += h.RandomDataByteCount;
        }
        else if (h.RandomDataByteCount != 0) throw new InvalidDataException("PC XAnim randomDataByte count has null pointer.");

        if (h.RandomDataIntPointer != 0)
        {
            ri = ReadU32ArrayLe(zone, ref cursor, h.RandomDataIntCount);
        }
        else if (h.RandomDataIntCount != 0) throw new InvalidDataException("PC XAnim randomDataInt count has null pointer.");

        if (h.IndicesPointer != 0)
        {
            if (h.NumFrames < 256)
            {
                Ensure(zone, cursor, checked((int)h.IndexCount), "PC byte indices");
                ix = zone.AsSpan(cursor, checked((int)h.IndexCount)).ToArray().Select(x => (ushort)x).ToArray();
                cursor += checked((int)h.IndexCount);
            }
            else
            {
                ix = ReadU16ArrayLe(zone, ref cursor, checked((int)h.IndexCount));
            }
        }
        else if (h.IndexCount != 0) throw new InvalidDataException("PC XAnim indices count has null pointer.");

        return new SourceStreams(db, ds, di, rs, rb, ri, ix);
    }

    private static void DecodePcTracks(string assetName, Header h, SourceStreams streams, Bone[] bones)
    {
        var read = new ReadCursor(streams, h.NumFrames < 256);
        var bone = 0;

        void NeedBone()
        {
            if ((uint)bone >= (uint)bones.Length)
                throw new InvalidDataException($"XAnim '{assetName}' quaternion buckets exceed bone count.");
        }

        for (var i = 0; i < h.BoneCounts[0]; i++, bone++)
        {
            NeedBone();
            bones[bone].PcQuat = PcQuatType.None;
        }

        for (var i = 0; i < h.BoneCounts[1]; i++, bone++)
        {
            NeedBone();
            var b = bones[bone];
            b.PcQuat = PcQuatType.Simple;
            var stored = unchecked((ushort)read.PopDataShort());
            b.QuatIndices = read.ReadPackedIndices(stored);
            b.SimpleQuatFrames = read.ReadRandomShort(checked(b.QuatIndices.Length * 2));
        }

        for (var i = 0; i < h.BoneCounts[2]; i++, bone++)
        {
            NeedBone();
            var b = bones[bone];
            b.PcQuat = PcQuatType.Full;
            var stored = unchecked((ushort)read.PopDataShort());
            b.QuatIndices = read.ReadPackedIndices(stored);
            b.FullQuatFrames = read.ReadRandomShort(checked(b.QuatIndices.Length * 4));
        }

        for (var i = 0; i < h.BoneCounts[3]; i++, bone++)
        {
            NeedBone();
            var b = bones[bone];
            b.PcQuat = PcQuatType.SimpleConstant;
            b.SimpleQuatFrames = new[] { read.PopDataShort(), read.PopDataShort() };
        }

        for (var i = 0; i < h.BoneCounts[4]; i++, bone++)
        {
            NeedBone();
            var b = bones[bone];
            b.PcQuat = PcQuatType.FullConstant;
            b.FullQuatFrames = new[]
            {
                read.PopDataShort(), read.PopDataShort(), read.PopDataShort(), read.PopDataShort()
            };
        }

        if (bone != bones.Length)
            throw new InvalidDataException($"XAnim '{assetName}' quaternion buckets total {bone}, expected {bones.Length}.");

        var transAssigned = new bool[bones.Length];

        for (var type = 5; type <= 8; type++)
        {
            var count = h.BoneCounts[type];
            for (var i = 0; i < count; i++)
            {
                var boneIndex = read.PopDataByte();
                if (boneIndex >= bones.Length || transAssigned[boneIndex])
                    throw new InvalidDataException($"XAnim '{assetName}' invalid/duplicate translation bone {boneIndex}.");

                var b = bones[boneIndex];
                transAssigned[boneIndex] = true;
                b.Trans = (PcTransType)type;

                switch (b.Trans)
                {
                    case PcTransType.Small:
                    case PcTransType.Full:
                    {
                        var stored = unchecked((ushort)read.PopDataShort());
                        b.TransMins = new[] { read.PopDataInt(), read.PopDataInt(), read.PopDataInt() };
                        b.TransSize = new[] { read.PopDataInt(), read.PopDataInt(), read.PopDataInt() };
                        b.TransIndices = read.ReadPackedIndices(stored);
                        if (b.Trans == PcTransType.Small)
                            b.SmallTransFrames = read.ReadRandomByte(checked(b.TransIndices.Length * 3));
                        else
                            b.FullTransFrames = read.ReadRandomU16(checked(b.TransIndices.Length * 3));
                        break;
                    }

                    case PcTransType.Constant:
                        b.TransConstant = new[] { read.PopDataInt(), read.PopDataInt(), read.PopDataInt() };
                        break;

                    case PcTransType.None:
                        break;
                }
            }
        }

        if (transAssigned.Any(x => !x))
            throw new InvalidDataException($"XAnim '{assetName}' did not assign every translation track.");

        read.ExpectEnd();
    }

    private static byte[] BuildXboxCounts(IReadOnlyList<Bone> bones)
    {
        var counts = new byte[12];
        foreach (var b in bones)
        {
            counts[(int)b.XboxQuat] = checked((byte)(counts[(int)b.XboxQuat] + 1));
            var trans = b.Trans switch
            {
                PcTransType.Small => 7,
                PcTransType.Full => 8,
                PcTransType.Constant => 9,
                PcTransType.None => 10,
                _ => throw new InvalidDataException($"Unsupported translation type {b.Trans}.")
            };
            counts[trans] = checked((byte)(counts[trans] + 1));
        }
        counts[11] = checked((byte)bones.Count);
        return counts;
    }

    private static byte[] WriteXboxBlob(
        string assetName,
        Header h,
        byte[] xboxCounts,
        IReadOnlyList<Bone> bones,
        IReadOnlyList<Notify> notifies,
        byte[] deltaXbox,
        WriteCursor s)
    {
        using var ms = new MemoryStream();

        uint Ptr(uint original, int count) => count == 0 ? 0u : (IsInline(original) ? original : Following);

        WriteU32Be(ms, h.NamePointer);
        WriteU16Be(ms, checked((ushort)s.DataByte.Count));
        WriteU16Be(ms, checked((ushort)s.DataShort.Count));
        WriteU16Be(ms, checked((ushort)s.DataInt.Count));
        WriteU16Be(ms, checked((ushort)s.RandomByte.Count));
        WriteU16Be(ms, checked((ushort)s.RandomInt.Count));
        WriteU16Be(ms, h.NumFrames);
        ms.WriteByte(h.Flag0);
        ms.WriteByte(h.Flag1);
        ms.Write(xboxCounts);
        ms.WriteByte(h.NotifyCount);
        ms.WriteByte(h.AssetType);
        ms.WriteByte(h.IsDefault);
        ms.WriteByte(h.Pad);
        WriteU16Be(ms, 0); // realign 32-bit fields after 12-byte boneCount table
        WriteU32Be(ms, checked((uint)s.RandomShort.Count));
        WriteU32Be(ms, checked((uint)s.Indices.Count));
        WriteU32Be(ms, h.FrameRateBits);
        WriteU32Be(ms, h.FrequencyBits);

        WriteU32Be(ms, Ptr(h.NamesPointer, bones.Count));
        WriteU32Be(ms, Ptr(h.DataBytePointer, s.DataByte.Count));
        WriteU32Be(ms, Ptr(h.DataShortPointer, s.DataShort.Count));
        WriteU32Be(ms, Ptr(h.DataIntPointer, s.DataInt.Count));
        WriteU32Be(ms, Ptr(h.RandomDataShortPointer, s.RandomShort.Count));
        WriteU32Be(ms, Ptr(h.RandomDataBytePointer, s.RandomByte.Count));
        WriteU32Be(ms, Ptr(h.RandomDataIntPointer, s.RandomInt.Count));
        WriteU32Be(ms, Ptr(h.IndicesPointer, s.Indices.Count));
        WriteU32Be(ms, Ptr(h.NotifyPointer, notifies.Count));
        WriteU32Be(ms, Ptr(h.DeltaPointer, deltaXbox.Length));

        if (ms.Position != XboxFixedSize)
            throw new InvalidDataException($"Xbox XAnim header wrote {ms.Position} bytes, expected {XboxFixedSize}.");

        var nameBytes = Encoding.ASCII.GetBytes(assetName + "\0");
        ms.Write(nameBytes);

        foreach (var bone in bones)
            WriteU16Be(ms, bone.ScriptString);

        foreach (var n in notifies)
        {
            WriteU16Be(ms, n.Name);
            WriteU16Be(ms, n.Pad);
            WriteU32Be(ms, n.TimeBits);
        }

        ms.Write(deltaXbox);

        ms.Write(s.DataByte.ToArray());
        foreach (var value in s.DataShort) WriteU16Be(ms, unchecked((ushort)value));
        foreach (var value in s.DataInt) WriteU32Be(ms, value);
        foreach (var value in s.RandomShort) WriteU16Be(ms, unchecked((ushort)value));
        ms.Write(s.RandomByte.ToArray());
        foreach (var value in s.RandomInt) WriteU32Be(ms, value);

        if (h.NumFrames < 256)
        {
            foreach (var value in s.Indices)
            {
                if (value > byte.MaxValue) throw new InvalidDataException($"Xbox XAnim byte index {value} exceeds 255.");
                ms.WriteByte((byte)value);
            }
        }
        else
        {
            foreach (var value in s.Indices) WriteU16Be(ms, value);
        }

        return ms.ToArray();
    }

    private static int ConvertDelta(byte[] src, int cursor, Stream dst, ushort numFrames)
    {
        Ensure(src, cursor, 8, "PC XAnim.deltaPart");
        var transPtr = ReadU32Le(src, cursor);
        var quatPtr = ReadU32Le(src, cursor + 4);
        WriteU32Be(dst, transPtr);
        WriteU32Be(dst, quatPtr);
        cursor += 8;

        if (transPtr != 0)
            cursor = ConvertDeltaTrans(src, cursor, dst, numFrames);
        if (quatPtr != 0)
            cursor = ConvertDeltaQuat(src, cursor, dst, numFrames);
        return cursor;
    }

    private static int ConvertDeltaTrans(byte[] src, int cursor, Stream dst, ushort numFrames)
    {
        Ensure(src, cursor, 4, "PC delta trans");
        var size = ReadU16Le(src, cursor);
        var small = src[cursor + 2] != 0;
        WriteU16Be(dst, size);
        dst.WriteByte(src[cursor + 2]);
        dst.WriteByte(src[cursor + 3]);

        if (size == 0)
        {
            for (var p = 4; p < 16; p += 4) WriteU32Be(dst, ReadU32Le(src, cursor + p));
            return cursor + 16;
        }

        for (var p = 4; p < 28; p += 4) WriteU32Be(dst, ReadU32Le(src, cursor + p));
        var framesPtr = ReadU32Le(src, cursor + 28);
        WriteU32Be(dst, framesPtr);
        cursor += 32;

        var count = checked((int)size + 1);
        if (numFrames < 256)
        {
            Ensure(src, cursor, count, "PC delta trans indices");
            dst.Write(src.AsSpan(cursor, count));
            cursor += count;
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                WriteU16Be(dst, ReadU16Le(src, cursor));
                cursor += 2;
            }
        }

        if (framesPtr != 0)
        {
            if (small)
            {
                var bytes = checked(count * 3);
                Ensure(src, cursor, bytes, "PC delta trans small frames");
                dst.Write(src.AsSpan(cursor, bytes));
                cursor += bytes;
            }
            else
            {
                for (var i = 0; i < count * 3; i++)
                {
                    WriteU16Be(dst, ReadU16Le(src, cursor));
                    cursor += 2;
                }
            }
        }
        return cursor;
    }

    private static int ConvertDeltaQuat(byte[] src, int cursor, Stream dst, ushort numFrames)
    {
        Ensure(src, cursor, 8, "PC delta quat");
        var size = ReadU16Le(src, cursor);
        WriteU16Be(dst, size);
        WriteU16Be(dst, ReadU16Le(src, cursor + 2));

        if (size == 0)
        {
            WriteU16Be(dst, ReadU16Le(src, cursor + 4));
            WriteU16Be(dst, ReadU16Le(src, cursor + 6));
            return cursor + 8;
        }

        var framesPtr = ReadU32Le(src, cursor + 4);
        WriteU32Be(dst, framesPtr);
        cursor += 8;
        var count = checked((int)size + 1);

        if (numFrames < 256)
        {
            Ensure(src, cursor, count, "PC delta quat indices");
            dst.Write(src.AsSpan(cursor, count));
            cursor += count;
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                WriteU16Be(dst, ReadU16Le(src, cursor));
                cursor += 2;
            }
        }

        if (framesPtr != 0)
        {
            for (var i = 0; i < count * 2; i++)
            {
                WriteU16Be(dst, ReadU16Le(src, cursor));
                cursor += 2;
            }
        }
        return cursor;
    }

    private static int SkipDeltaTransBe(byte[] data, int cursor, ushort numFrames)
    {
        Ensure(data, cursor, 4, "Xbox delta trans");
        var size = ReadU16Be(data, cursor);
        var small = data[cursor + 2] != 0;
        if (size == 0) return Advance(data, cursor, 16, "Xbox delta trans constant");

        Ensure(data, cursor, 32, "Xbox delta trans header");
        var framesPtr = ReadU32Be(data, cursor + 28);
        cursor += 32;
        var count = checked((int)size + 1);
        cursor = Advance(data, cursor, count * (numFrames < 256 ? 1 : 2), "Xbox delta trans indices");
        if (framesPtr != 0)
            cursor = Advance(data, cursor, count * (small ? 3 : 6), "Xbox delta trans frames");
        return cursor;
    }

    private static int SkipDeltaQuatBe(byte[] data, int cursor, ushort numFrames)
    {
        Ensure(data, cursor, 8, "Xbox delta quat");
        var size = ReadU16Be(data, cursor);
        if (size == 0) return cursor + 8;

        var framesPtr = ReadU32Be(data, cursor + 4);
        cursor += 8;
        var count = checked((int)size + 1);
        cursor = Advance(data, cursor, count * (numFrames < 256 ? 1 : 2), "Xbox delta quat indices");
        if (framesPtr != 0) cursor = Advance(data, cursor, count * 4, "Xbox delta quat frames");
        return cursor;
    }

    private static (string Value, int Bytes) ReadCString(byte[] data, int offset)
    {
        if (offset < 0 || offset >= data.Length) throw new InvalidDataException("XAnim string offset outside zone.");
        var end = offset;
        while (end < data.Length && data[end] != 0)
        {
            if (end - offset > 4096) throw new InvalidDataException("XAnim string exceeds 4096 bytes.");
            end++;
        }
        if (end >= data.Length) throw new InvalidDataException("Unterminated XAnim string.");
        return (Encoding.ASCII.GetString(data, offset, end - offset), end - offset + 1);
    }

    private static short[] ReadI16ArrayLe(byte[] data, ref int cursor, int count)
    {
        Ensure(data, cursor, checked(count * 2), "i16 array");
        var result = new short[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = unchecked((short)ReadU16Le(data, cursor));
            cursor += 2;
        }
        return result;
    }

    private static ushort[] ReadU16ArrayLe(byte[] data, ref int cursor, int count)
    {
        Ensure(data, cursor, checked(count * 2), "u16 array");
        var result = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = ReadU16Le(data, cursor);
            cursor += 2;
        }
        return result;
    }

    private static uint[] ReadU32ArrayLe(byte[] data, ref int cursor, int count)
    {
        Ensure(data, cursor, checked(count * 4), "u32 array");
        var result = new uint[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = ReadU32Le(data, cursor);
            cursor += 4;
        }
        return result;
    }

    private static uint ReadU32Le(byte[] data, int offset)
    {
        Ensure(data, offset, 4, "u32-le");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private static ushort ReadU16Le(byte[] data, int offset)
    {
        Ensure(data, offset, 2, "u16-le");
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        Ensure(data, offset, 4, "u32-be");
        return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    }

    private static ushort ReadU16Be(byte[] data, int offset)
    {
        Ensure(data, offset, 2, "u16-be");
        return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
    }

    private static void WriteU32Be(Stream s, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        s.Write(b);
    }

    private static void WriteU16Be(Stream s, ushort value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, value);
        s.Write(b);
    }

    private static bool IsInline(uint value) => value is Following or Insert;

    private static int Advance(byte[] data, int cursor, int count, string label)
    {
        Ensure(data, cursor, count, label);
        return checked(cursor + count);
    }

    private static void Ensure(byte[] data, int offset, int count, string label)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
            throw new InvalidDataException($"{label} exceeds serialized zone bounds at 0x{offset:X} (+0x{count:X}).");
    }
}
