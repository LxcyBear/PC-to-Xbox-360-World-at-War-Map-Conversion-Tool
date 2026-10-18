using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4MaterialSimpleTailSmokeAsset(
    int AssetIndex,
    string TypeName,
    string SmokeName,
    string FidelityObligation,
    byte[] XboxPayload);

public sealed class T4MaterialSimpleTailSmokeScan
{
    public required IReadOnlyDictionary<int, T4MaterialSimpleTailSmokeAsset> AssetsByIndex { get; init; }
    public required int DeclaredMaterials { get; init; }
    public required int PromotedMaterials { get; init; }
    public required int PromotedPackedLocalizeFallbacks { get; init; }
    public required int PromotedPackedNameRawFiles { get; init; }
    public required int PromotedComWorlds { get; init; }
    public required int PromotedGameWorldSp { get; init; }
    public required long XboxPayloadBytes { get; init; }
    public required int ReadBackErrors { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Alpha 10q hardware-smoke top-level payloads for the simple tail left after 10p.
///
/// This deliberately separates top-level structural readiness from content fidelity:
/// - Material: emits a real 96-byte Xbox Material fixed body, null technique/texture/constant/state
///   pointers and a deterministic private name. It is visually inert, but it is not a PC Material
///   blob being relabeled as Xbox data.
/// - packed-value Localize: preserves the proven localization KEY but replaces the unresolved shared
///   source VALUE with an explicit smoke placeholder. The PC packed pointer is never copied.
/// - packed-name final RawFile: preserves the byte-proven source buffer but gives the smoke object a
///   deterministic private name. The unresolved PC packed name pointer is never copied.
/// - ComWorld/GameWorldSp: emits the real 32-bit T4 fixed structures with all optional counts/pointers
///   zero and a deterministic private name. This intentionally removes lighting/water/burn/path data
///   for the smoke path and must not be confused with final world fidelity.
///
/// clipMap_t and GfxWorld are intentionally NOT synthesized here. Their fixed roots contain large,
/// interdependent world/collision/render state and remain fail-closed for the next stage.
/// </summary>
public static class T4MaterialSimpleTailSmokeSerializer
{
    private const int XboxMaterialFixedSize = 96;
    private const int ComWorldFixedSize = 64;
    private const int GameWorldSpFixedSize = 44;

    public static T4MaterialSimpleTailSmokeScan Build(
        T4ZoneDocument document,
        byte[] zone,
        T4RawFileOccupiedSpanScan rawScan)
    {
        var warnings = new List<string>();
        var output = new Dictionary<int, T4MaterialSimpleTailSmokeAsset>();
        var readBackErrors = 0;

        if (document.Endianness != Endian.Little)
        {
            warnings.Add("alpha 10q simple-tail smoke promotion expects a PC/little-endian source fastfile.");
            return Empty(warnings);
        }

        var materials = Physical(document, "material");
        foreach (var asset in materials)
        {
            var name = $"__waw360_mat_{asset.Index:D5}";
            var payload = BuildFixedWithName(XboxMaterialFixedSize, name);
            if (!VerifyFixedWithName(payload, XboxMaterialFixedSize, name)) readBackErrors++;
            output[asset.Index] = new T4MaterialSimpleTailSmokeAsset(
                asset.Index, "material", name,
                "smoke Material has null TechniqueSet/texture/constant/state tables; final Xbox material fidelity remains pending",
                payload);
        }

        var packedLocalize = 0;
        var payloadScan = T4PayloadScanner.Scan(document, zone);
        var localizeAssets = Physical(document, "localize");
        if (localizeAssets.Count != 0 && payloadScan.LocalizeEntries.Count == localizeAssets.Count)
        {
            for (var i = 0; i < localizeAssets.Count; i++)
            {
                var loc = payloadScan.LocalizeEntries[i];
                if (loc.SourceValuePointer is T4ZonePointer.Following or T4ZonePointer.Insert) continue;
                var asset = localizeAssets[i];
                var value = "[waw360 smoke unresolved shared localize]";
                var payload = BuildLocalize(loc.Name, value);
                if (!VerifyLocalize(payload, loc.Name, value)) readBackErrors++;
                output[asset.Index] = new T4MaterialSimpleTailSmokeAsset(
                    asset.Index, "localize", loc.Name,
                    $"source VALUE {T4ZonePointer.Describe(loc.SourceValuePointer)} replaced by explicit smoke placeholder; final shared-value rebase remains pending",
                    payload);
                packedLocalize++;
            }
        }
        else if (localizeAssets.Count != 0)
        {
            warnings.Add($"10q localize fallback refused: physical={localizeAssets.Count:N0}, scanner={payloadScan.LocalizeEntries.Count:N0}.");
        }

        var packedRaw = 0;
        foreach (var raw in rawScan.AssetsByIndex.Values.Where(x => x.SourceNamePacked && !x.NativePromotable))
        {
            if (raw.SourceOffset < 0 || raw.SourceOffset > zone.LongLength - 12L - raw.Length - 1L)
            {
                warnings.Add($"10q packed-name RawFile XAsset#{raw.AssetIndex} source span is outside the decompressed zone; fallback refused.");
                continue;
            }

            var bufferOffset = checked((int)raw.SourceOffset + 12);
            var buffer = zone.AsSpan(bufferOffset, checked(raw.Length + 1)).ToArray();
            var name = $"__waw360_raw_{raw.AssetIndex:D5}";
            var payload = BuildRawFile(name, raw.Length, buffer);
            if (!VerifyRawFile(payload, name, raw.Length, buffer)) readBackErrors++;
            output[raw.AssetIndex] = new T4MaterialSimpleTailSmokeAsset(
                raw.AssetIndex, "rawfile", name,
                $"source packed name {T4ZonePointer.Describe(raw.SourceNamePointer)} replaced by deterministic smoke name; original {raw.Length:N0}-byte body preserved",
                payload);
            packedRaw++;
        }

        var comWorlds = 0;
        foreach (var asset in Physical(document, "comworld"))
        {
            var name = $"__waw360_comworld_{asset.Index:D5}";
            var payload = BuildFixedWithName(ComWorldFixedSize, name);
            if (!VerifyFixedWithName(payload, ComWorldFixedSize, name)) readBackErrors++;
            output[asset.Index] = new T4MaterialSimpleTailSmokeAsset(
                asset.Index, "comworld", name,
                "smoke ComWorld zeros primary lights, water cells and burnable cells; final world data remains pending",
                payload);
            comWorlds++;
        }

        var gameWorlds = 0;
        foreach (var asset in Physical(document, "gameworld_sp"))
        {
            var name = $"__waw360_gameworld_sp_{asset.Index:D5}";
            var payload = BuildFixedWithName(GameWorldSpFixedSize, name);
            if (!VerifyFixedWithName(payload, GameWorldSpFixedSize, name)) readBackErrors++;
            output[asset.Index] = new T4MaterialSimpleTailSmokeAsset(
                asset.Index, "gameworld_sp", name,
                "smoke GameWorldSp zeros PathData; final pathnode/navigation data remains pending",
                payload);
            gameWorlds++;
        }

        var payloadBytes = output.Values.Sum(x => (long)x.XboxPayload.Length);
        var verified = readBackErrors == 0 && materials.All(m => output.ContainsKey(m.Index));
        if (verified)
            warnings.Add($"INFO: alpha 10q built {output.Count:N0} Xbox-endian Material/simple-tail smoke payload(s) with zero independent read-back errors.");

        return new T4MaterialSimpleTailSmokeScan
        {
            AssetsByIndex = output,
            DeclaredMaterials = materials.Count,
            PromotedMaterials = materials.Count(m => output.ContainsKey(m.Index)),
            PromotedPackedLocalizeFallbacks = packedLocalize,
            PromotedPackedNameRawFiles = packedRaw,
            PromotedComWorlds = comWorlds,
            PromotedGameWorldSp = gameWorlds,
            XboxPayloadBytes = payloadBytes,
            ReadBackErrors = readBackErrors,
            Verified = verified,
            Warnings = warnings.Concat(payloadScan.Warnings.Where(x => x.StartsWith("INFO: localize", StringComparison.OrdinalIgnoreCase))).ToList()
        };
    }

    private static List<T4AssetEntry> Physical(T4ZoneDocument document, string typeName) =>
        document.Assets
            .Where(a => a.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase) &&
                        a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .OrderBy(a => a.Index)
            .ToList();

    private static byte[] BuildFixedWithName(int fixedSize, string name)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        var payload = new byte[fixedSize + nameBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), T4ZonePointer.Following);
        nameBytes.CopyTo(payload.AsSpan(fixedSize));
        return payload;
    }

    private static bool VerifyFixedWithName(ReadOnlySpan<byte> payload, int fixedSize, string name)
    {
        if (payload.Length != fixedSize + Encoding.ASCII.GetByteCount(name) + 1) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(0, 4)) != T4ZonePointer.Following) return false;
        for (var i = 4; i < fixedSize; i++) if (payload[i] != 0) return false;
        var actual = Encoding.ASCII.GetString(payload.Slice(fixedSize, payload.Length - fixedSize - 1));
        return payload[^1] == 0 && actual.Equals(name, StringComparison.Ordinal);
    }

    private static byte[] BuildLocalize(string name, string value)
    {
        var v = Encoding.Latin1.GetBytes(value);
        var n = Encoding.ASCII.GetBytes(name);
        var payload = new byte[8 + v.Length + 1 + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), T4ZonePointer.Following);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), T4ZonePointer.Following);
        v.CopyTo(payload.AsSpan(8));
        n.CopyTo(payload.AsSpan(8 + v.Length + 1));
        return payload;
    }

    private static bool VerifyLocalize(ReadOnlySpan<byte> payload, string name, string value)
    {
        if (payload.Length < 11 || BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(0, 4)) != T4ZonePointer.Following ||
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)) != T4ZonePointer.Following) return false;
        var cursor = 8;
        if (!ReadLatin1Z(payload, ref cursor).Equals(value, StringComparison.Ordinal)) return false;
        if (!ReadAsciiZ(payload, ref cursor).Equals(name, StringComparison.Ordinal)) return false;
        return cursor == payload.Length;
    }

    private static byte[] BuildRawFile(string name, int length, byte[] buffer)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var payload = new byte[12 + n.Length + 1 + buffer.Length];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), T4ZonePointer.Following);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), checked((uint)length));
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8, 4), buffer.Length == 0 ? 0u : T4ZonePointer.Following);
        n.CopyTo(payload.AsSpan(12));
        buffer.CopyTo(payload.AsSpan(12 + n.Length + 1));
        return payload;
    }

    private static bool VerifyRawFile(ReadOnlySpan<byte> payload, string name, int length, ReadOnlySpan<byte> buffer)
    {
        if (payload.Length < 13) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(0, 4)) != T4ZonePointer.Following) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)) != checked((uint)length)) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8, 4)) != (buffer.Length == 0 ? 0u : T4ZonePointer.Following)) return false;
        var cursor = 12;
        if (!ReadAsciiZ(payload, ref cursor).Equals(name, StringComparison.Ordinal)) return false;
        return payload.Slice(cursor).SequenceEqual(buffer);
    }

    private static string ReadAsciiZ(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        var start = cursor;
        while (cursor < bytes.Length && bytes[cursor] != 0) cursor++;
        if (cursor >= bytes.Length) return string.Empty;
        var s = Encoding.ASCII.GetString(bytes.Slice(start, cursor - start));
        cursor++;
        return s;
    }

    private static string ReadLatin1Z(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        var start = cursor;
        while (cursor < bytes.Length && bytes[cursor] != 0) cursor++;
        if (cursor >= bytes.Length) return string.Empty;
        var s = Encoding.Latin1.GetString(bytes.Slice(start, cursor - start));
        cursor++;
        return s;
    }

    private static T4MaterialSimpleTailSmokeScan Empty(IReadOnlyList<string> warnings) => new()
    {
        AssetsByIndex = new Dictionary<int, T4MaterialSimpleTailSmokeAsset>(),
        DeclaredMaterials = 0,
        PromotedMaterials = 0,
        PromotedPackedLocalizeFallbacks = 0,
        PromotedPackedNameRawFiles = 0,
        PromotedComWorlds = 0,
        PromotedGameWorldSp = 0,
        XboxPayloadBytes = 0,
        ReadBackErrors = 0,
        Verified = false,
        Warnings = warnings
    };
}
