using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4TechniqueSetNullGraphAsset(
    int AssetIndex,
    string SmokeName,
    byte WorldVertFormat,
    int XboxTechniqueSlots,
    byte[] XboxPayload);

public sealed class T4TechniqueSetNullGraphScan
{
    public required IReadOnlyDictionary<int, T4TechniqueSetNullGraphAsset> AssetsByIndex { get; init; }
    public required int DeclaredPhysicalTechniqueSets { get; init; }
    public required int NativeSmokeTechniqueSets { get; init; }
    public required int XboxTechniqueSlotsNulled { get; init; }
    public required long XboxPayloadBytes { get; init; }
    public required int ReadBackErrors { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Alpha 10p Xbox TechniqueSet promotion for the hardware-smoke path.
///
/// T4 PC MaterialTechniqueSet contains 59 technique pointers. The hardware-proven Xbox fixed body used by WaW is 216 bytes: name pointer + world-vertex byte/padding + remappedTechniqueSet pointer + 51 technique pointers.
/// PC shader/pass graphs are Direct3D payloads and are NOT copied to Xbox. Instead 10p emits
/// a structurally valid Xbox TechniqueSet with the remappedTechniqueSet pointer and all 51 graph slots null. This is an actual
/// Xbox-endian top-level payload, not a scanner-only diagnostic, but it is deliberately a
/// smoke representation: final visual fidelity still requires Xbox/Xenos technique graphs
/// or donor resolution.
///
/// The source TechniqueSet name/body does not need to be guessed to construct the smoke
/// object. Each top-level physical XAsset receives a deterministic private name keyed by its
/// authoritative XAsset index. References in the later native writer are index/destination
/// based rather than name based.
/// </summary>
public static class T4TechniqueSetNullGraphSerializer
{
    private const int XboxTechniqueSetFixedSize = 216;
    private const int XboxTechniqueCount = 51;

    public static T4TechniqueSetNullGraphScan Build(T4ZoneDocument document)
    {
        var warnings = new List<string>();
        var physical = document.Assets
            .Where(a => a.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase) &&
                        a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .OrderBy(a => a.Index)
            .ToList();

        if (physical.Count == 0)
        {
            return new T4TechniqueSetNullGraphScan
            {
                AssetsByIndex = new Dictionary<int, T4TechniqueSetNullGraphAsset>(),
                DeclaredPhysicalTechniqueSets = 0,
                NativeSmokeTechniqueSets = 0,
                XboxTechniqueSlotsNulled = 0,
                XboxPayloadBytes = 0,
                ReadBackErrors = 0,
                Verified = true,
                Warnings = warnings
            };
        }

        if (document.Endianness != Endian.Little)
        {
            warnings.Add("alpha 10p TechniqueSet smoke promotion expects a PC/little-endian source fastfile.");
            return Failure(physical.Count, warnings);
        }

        var rows = new Dictionary<int, T4TechniqueSetNullGraphAsset>();
        var readBackErrors = 0;
        long bytes = 0;
        foreach (var asset in physical)
        {
            var name = $"__waw360_ts_{asset.Index:D5}";
            const byte worldVert = 0; // smoke-safe canonical T4 world vertex format
            var payload = BuildXboxPayload(name, worldVert);
            if (!VerifyXboxPayload(payload, name, worldVert)) readBackErrors++;
            rows[asset.Index] = new T4TechniqueSetNullGraphAsset(
                asset.Index, name, worldVert, XboxTechniqueCount, payload);
            bytes += payload.LongLength;
        }

        var verified = rows.Count == physical.Count && readBackErrors == 0;
        if (verified)
            warnings.Add($"INFO: alpha 10p emitted {rows.Count:N0}/{physical.Count:N0} Xbox-endian TechniqueSet smoke payloads; all {rows.Count * XboxTechniqueCount:N0} Xbox graph slots are deliberately null.");

        return new T4TechniqueSetNullGraphScan
        {
            AssetsByIndex = rows,
            DeclaredPhysicalTechniqueSets = physical.Count,
            NativeSmokeTechniqueSets = rows.Count,
            XboxTechniqueSlotsNulled = rows.Count * XboxTechniqueCount,
            XboxPayloadBytes = bytes,
            ReadBackErrors = readBackErrors,
            Verified = verified,
            Warnings = warnings
        };
    }

    private static byte[] BuildXboxPayload(string name, byte worldVert)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        var payload = new byte[XboxTechniqueSetFixedSize + nameBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), T4ZonePointer.Following);
        payload[4] = worldVert;
        // 5..7 are zero; 8..11 is null remappedTechniqueSet; 12..215 are the 51 null Xbox technique pointers.
        nameBytes.CopyTo(payload.AsSpan(XboxTechniqueSetFixedSize));
        payload[^1] = 0;
        return payload;
    }

    private static bool VerifyXboxPayload(ReadOnlySpan<byte> payload, string expectedName, byte expectedWorldVert)
    {
        if (payload.Length < XboxTechniqueSetFixedSize + 2) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(0, 4)) != T4ZonePointer.Following) return false;
        if (payload[4] != expectedWorldVert) return false;
        if (payload[5] != 0 || payload[6] != 0 || payload[7] != 0) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8, 4)) != 0) return false; // remappedTechniqueSet
        for (var i = 0; i < XboxTechniqueCount; i++)
            if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(12 + i * 4, 4)) != 0) return false;
        var end = XboxTechniqueSetFixedSize;
        while (end < payload.Length && payload[end] != 0) end++;
        if (end != payload.Length - 1) return false;
        var actual = Encoding.ASCII.GetString(payload.Slice(XboxTechniqueSetFixedSize, end - XboxTechniqueSetFixedSize));
        return actual.Equals(expectedName, StringComparison.Ordinal);
    }

    private static T4TechniqueSetNullGraphScan Failure(int declared, IReadOnlyList<string> warnings) => new()
    {
        AssetsByIndex = new Dictionary<int, T4TechniqueSetNullGraphAsset>(),
        DeclaredPhysicalTechniqueSets = declared,
        NativeSmokeTechniqueSets = 0,
        XboxTechniqueSlotsNulled = 0,
        XboxPayloadBytes = 0,
        ReadBackErrors = 0,
        Verified = false,
        Warnings = warnings
    };
}
