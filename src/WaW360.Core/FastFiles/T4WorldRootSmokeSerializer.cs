using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4WorldRootSmokeAsset(
    int AssetIndex,
    string TypeName,
    string SmokeName,
    int FixedSize,
    byte[] XboxPayload);

public sealed class T4WorldRootSmokeScan
{
    public required IReadOnlyDictionary<int, T4WorldRootSmokeAsset> AssetsByIndex { get; init; }
    public required int DeclaredClipMaps { get; init; }
    public required int PromotedClipMaps { get; init; }
    public required int DeclaredGfxWorlds { get; init; }
    public required int PromotedGfxWorlds { get; init; }
    public required long XboxPayloadBytes { get; init; }
    public required int ReadBackErrors { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Alpha 10r hardware-smoke roots for T4 clipMap_t and GfxWorld.
///
/// These are deliberately reduced world roots, not full PC->Xbox world conversion:
/// - clipMap_t uses the 32-bit T4 fixed root (332 bytes), a private inline name, and zero
///   counts/pointers so collision/static-model/dynent payloads are absent on the smoke path.
/// - GfxWorld uses the 32-bit T4 fixed root (804 bytes), private inline name/baseName strings,
///   and zero counts/pointers so render/light/grid/surface payloads are absent.
///
/// The purpose is to make the FIRST Xbox parser/loader smoke candidate self-contained without
/// copying stale PC packed pointers. Final gameplay/visual fidelity must replace both roots.
/// </summary>
public static class T4WorldRootSmokeSerializer
{
    // 32-bit T4 layouts. Keep these constants explicit so read-back can fail closed if the
    // smoke serializer is edited accidentally.
    private const int ClipMapFixedSize = 332;
    private const int GfxWorldFixedSize = 804;

    public static T4WorldRootSmokeScan Build(T4ZoneDocument document)
    {
        var warnings = new List<string>();
        var rows = new Dictionary<int, T4WorldRootSmokeAsset>();
        var readBackErrors = 0;

        var clips = Physical(document, "clipmap");
        var gfx = Physical(document, "gfxworld");

        if (document.Endianness != Endian.Little && clips.Count + gfx.Count != 0)
        {
            warnings.Add("alpha 10r world-root smoke promotion expects a PC/little-endian source fastfile.");
            return new T4WorldRootSmokeScan
            {
                AssetsByIndex = rows,
                DeclaredClipMaps = clips.Count,
                PromotedClipMaps = 0,
                DeclaredGfxWorlds = gfx.Count,
                PromotedGfxWorlds = 0,
                XboxPayloadBytes = 0,
                ReadBackErrors = 0,
                Verified = false,
                Warnings = warnings
            };
        }

        foreach (var asset in clips)
        {
            var name = $"__waw360_clip_{asset.Index:D5}";
            var payload = BuildClipMap(name);
            if (!VerifyClipMap(payload, name)) readBackErrors++;
            rows[asset.Index] = new T4WorldRootSmokeAsset(asset.Index, "clipmap", name, ClipMapFixedSize, payload);
        }

        foreach (var asset in gfx)
        {
            var name = $"__waw360_gfxworld_{asset.Index:D5}";
            var baseName = $"__waw360_gfxbase_{asset.Index:D5}";
            var payload = BuildGfxWorld(name, baseName);
            if (!VerifyGfxWorld(payload, name, baseName)) readBackErrors++;
            rows[asset.Index] = new T4WorldRootSmokeAsset(asset.Index, "gfxworld", name, GfxWorldFixedSize, payload);
        }

        var promotedClip = clips.Count(a => rows.ContainsKey(a.Index));
        var promotedGfx = gfx.Count(a => rows.ContainsKey(a.Index));
        var verified = promotedClip == clips.Count && promotedGfx == gfx.Count && readBackErrors == 0;
        if (verified && rows.Count != 0)
            warnings.Add($"INFO: alpha 10r emitted {promotedClip:N0}/{clips.Count:N0} clipMap_t and {promotedGfx:N0}/{gfx.Count:N0} GfxWorld reduced Xbox smoke root(s) with zero independent read-back errors.");

        return new T4WorldRootSmokeScan
        {
            AssetsByIndex = rows,
            DeclaredClipMaps = clips.Count,
            PromotedClipMaps = promotedClip,
            DeclaredGfxWorlds = gfx.Count,
            PromotedGfxWorlds = promotedGfx,
            XboxPayloadBytes = rows.Values.Sum(x => (long)x.XboxPayload.LongLength),
            ReadBackErrors = readBackErrors,
            Verified = verified,
            Warnings = warnings
        };
    }

    private static List<T4AssetEntry> Physical(T4ZoneDocument document, string typeName) =>
        document.Assets
            .Where(a => a.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase) &&
                        a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .OrderBy(a => a.Index)
            .ToList();

    private static byte[] BuildClipMap(string name)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var payload = new byte[ClipMapFixedSize + n.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), T4ZonePointer.Following);
        n.CopyTo(payload.AsSpan(ClipMapFixedSize));
        return payload;
    }

    private static byte[] BuildGfxWorld(string name, string baseName)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var b = Encoding.ASCII.GetBytes(baseName);
        var payload = new byte[GfxWorldFixedSize + n.Length + 1 + b.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), T4ZonePointer.Following);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), T4ZonePointer.Following);
        var cursor = GfxWorldFixedSize;
        n.CopyTo(payload.AsSpan(cursor));
        cursor += n.Length + 1;
        b.CopyTo(payload.AsSpan(cursor));
        return payload;
    }

    private static bool VerifyClipMap(ReadOnlySpan<byte> payload, string name)
    {
        var n = Encoding.ASCII.GetBytes(name);
        if (payload.Length != ClipMapFixedSize + n.Length + 1) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(0, 4)) != T4ZonePointer.Following) return false;
        for (var i = 4; i < ClipMapFixedSize; i++) if (payload[i] != 0) return false;
        if (!payload.Slice(ClipMapFixedSize, n.Length).SequenceEqual(n)) return false;
        return payload[^1] == 0;
    }

    private static bool VerifyGfxWorld(ReadOnlySpan<byte> payload, string name, string baseName)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var b = Encoding.ASCII.GetBytes(baseName);
        if (payload.Length != GfxWorldFixedSize + n.Length + 1 + b.Length + 1) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(0, 4)) != T4ZonePointer.Following) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)) != T4ZonePointer.Following) return false;
        for (var i = 8; i < GfxWorldFixedSize; i++) if (payload[i] != 0) return false;
        var cursor = GfxWorldFixedSize;
        if (!payload.Slice(cursor, n.Length).SequenceEqual(n) || payload[cursor + n.Length] != 0) return false;
        cursor += n.Length + 1;
        if (!payload.Slice(cursor, b.Length).SequenceEqual(b) || payload[cursor + b.Length] != 0) return false;
        return true;
    }
}
