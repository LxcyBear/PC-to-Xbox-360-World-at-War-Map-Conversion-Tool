using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed record T4XenonNativeXModelLayoutRow(
    int RootIndex,
    string Name,
    int D4CandidateCount,
    int E4CandidateCount,
    long? D4BodyOffset,
    byte NumBones,
    byte NumRootBones,
    byte NumSurfs,
    byte LodRampType,
    ushort NumLods,
    short CollLod,
    uint NamePointer);

public sealed class T4XenonNativeXModelLayoutProbeResult
{
    public required string NativeFastFile { get; init; }
    public required string NativeIdentityManifest { get; init; }
    public required int NativeXModelRoots { get; init; }
    public required int UniqueD4Matches { get; init; }
    public required int MissingD4Matches { get; init; }
    public required int AmbiguousD4Matches { get; init; }
    public required int LegacyE4Matches { get; init; }
    public required int D4OnlyMatches { get; init; }
    public required bool ExactD4BoundaryProof { get; init; }
    public required IReadOnlyList<T4XenonNativeXModelLayoutRow> Rows { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// NUCLEAR Stage 4T: empirical Xbox TU7 XModel fixed-body boundary proof.
///
/// The PC T4 XModel proof uses the 0xE4 layout. Hardware evidence says Xbox TU7
/// DB_GetXAssetTypeSize(XMODEL) is 0xD4. This probe refuses to infer the missing
/// 16 bytes from PC headers. Instead it searches the known-working native Xbox
/// oracle for each exact native XModel identity and asks which body boundary is
/// structurally valid immediately before that owned name string.
///
/// A 0xD4 match requires:
/// - inline/insert name pointer at +0x00;
/// - sane bone/surface/LOD fields at the known common prefix offsets;
/// - every common-prefix pointer field to be a valid serialized T4 pointer;
/// - finite radius/bounds; and
/// - the exact oracle XModel name at body+0xD4.
///
/// 0xE4 is tested as a control using the legacy PC-shaped tail pointers.
/// No game fastfile is emitted.
/// </summary>
public static class T4XenonNativeXModelLayoutProbe
{
    private const int XboxFixedSize = 0xD4;
    private const int LegacyPcFixedSize = 0xE4;
    private const uint Following = 0xFFFFFFFFu;
    private const uint Insert = 0xFFFFFFFEu;

    private sealed record NativeIdentity(int Index, string Name);
    private sealed record NamePattern(NativeIdentity Identity, byte[] Bytes);

    public static async Task<T4XenonNativeXModelLayoutProbeResult> RunAsync(
        string nativeFastFile,
        string nativeIdentityManifest,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        nativeFastFile = Path.GetFullPath(nativeFastFile);
        nativeIdentityManifest = Path.GetFullPath(nativeIdentityManifest);
        outputDirectory = Path.GetFullPath(outputDirectory);

        if (!File.Exists(nativeFastFile))
            throw new FileNotFoundException("Native Xbox oracle fastfile not found.", nativeFastFile);
        if (!File.Exists(nativeIdentityManifest))
            throw new FileNotFoundException("Native identity manifest not found.", nativeIdentityManifest);

        var cached = await FastFileAnalysisCache.GetZoneAsync(nativeFastFile, cancellationToken);
        if (cached.Platform != WaWPlatform.Xbox360 || cached.Document.Endianness != Endian.Big)
            throw new InvalidDataException(
                $"Stage 4T requires an Xbox360/big-endian native oracle; parsed {cached.Platform}/{cached.Document.Endianness}.");

        var identities = LoadNativeXModels(nativeIdentityManifest);
        var declared = cached.Document.Assets.Count(a =>
            a.TypeName.Equals("xmodel", StringComparison.OrdinalIgnoreCase));
        if (identities.Count != declared)
            throw new InvalidDataException(
                $"Native XModel identity count {identities.Count:N0} != XAssetList declaration {declared:N0}.");

        var patterns = identities
            .Select(x => new NamePattern(x, Encoding.UTF8.GetBytes(x.Name)))
            .ToList();
        if (patterns.Any(x => x.Bytes.Length < 4))
            throw new InvalidDataException("Stage 4T requires XModel identities of at least four UTF-8 bytes.");

        var byPrefix = patterns
            .GroupBy(x => BinaryPrimitives.ReadUInt32LittleEndian(x.Bytes.AsSpan(0, 4)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var d4ByIndex = identities.ToDictionary(x => x.Index, _ => new List<int>());
        var e4ByIndex = identities.ToDictionary(x => x.Index, _ => new List<int>());
        var zone = cached.Bytes;

        for (var offset = 0; offset <= zone.Length - 5; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan(offset, 4));
            if (!byPrefix.TryGetValue(key, out var candidates))
                continue;

            foreach (var pattern in candidates)
            {
                var bytes = pattern.Bytes;
                if (offset > zone.Length - bytes.Length - 1)
                    continue;
                if (zone[offset + bytes.Length] != 0)
                    continue;
                if (!zone.AsSpan(offset, bytes.Length).SequenceEqual(bytes))
                    continue;

                var d4Start = offset - XboxFixedSize;
                if (ValidateD4(zone, d4Start))
                    d4ByIndex[pattern.Identity.Index].Add(d4Start);

                var e4Start = offset - LegacyPcFixedSize;
                if (ValidateE4(zone, e4Start))
                    e4ByIndex[pattern.Identity.Index].Add(e4Start);
            }
        }

        var rows = new List<T4XenonNativeXModelLayoutRow>(identities.Count);
        foreach (var identity in identities)
        {
            var d4 = d4ByIndex[identity.Index].Distinct().OrderBy(x => x).ToList();
            var e4 = e4ByIndex[identity.Index].Distinct().OrderBy(x => x).ToList();

            byte bones = 0, roots = 0, surfs = 0, lodRamp = 0;
            ushort numLods = 0;
            short collLod = 0;
            uint namePointer = 0;
            long? bodyOffset = null;

            if (d4.Count == 1)
            {
                var p = d4[0];
                bodyOffset = p;
                namePointer = ReadU32(zone, p);
                bones = zone[p + 4];
                roots = zone[p + 5];
                surfs = zone[p + 6];
                lodRamp = zone[p + 7];
                numLods = ReadU16(zone, p + 0xC4);
                collLod = ReadI16(zone, p + 0xC6);
            }

            rows.Add(new T4XenonNativeXModelLayoutRow(
                identity.Index,
                identity.Name,
                d4.Count,
                e4.Count,
                bodyOffset,
                bones,
                roots,
                surfs,
                lodRamp,
                numLods,
                collLod,
                namePointer));
        }

        var uniqueD4 = rows.Count(x => x.D4CandidateCount == 1);
        var missingD4 = rows.Count(x => x.D4CandidateCount == 0);
        var ambiguousD4 = rows.Count(x => x.D4CandidateCount > 1);
        var legacyE4 = rows.Count(x => x.E4CandidateCount > 0);
        var d4Only = rows.Count(x => x.D4CandidateCount == 1 && x.E4CandidateCount == 0);
        var exact = uniqueD4 == identities.Count && missingD4 == 0 &&
                    ambiguousD4 == 0 && legacyE4 == 0;

        var notes = new List<string>
        {
            "This is a read-only native-oracle boundary proof; it emits no game fastfile.",
            "0xD4 candidates validate only fields common to the proven PC prefix; no Xbox-only tail layout is invented.",
            "0xE4 is tested as a negative control using the four legacy PC-shaped tail pointers at +0xD4/+0xD8/+0xDC/+0xE0.",
            exact
                ? "EXACT 0xD4 BOUNDARY PROOF: every native XModel identity has exactly one structurally valid 0xD4 body/name boundary and none has a valid legacy 0xE4 boundary."
                : "0xD4 boundary is not yet exact for every native XModel; unresolved/ambiguous rows remain diagnostic blockers."
        };

        Directory.CreateDirectory(outputDirectory);
        var txt = Path.Combine(outputDirectory, "nuclear_4t_native_xmodel_layout.txt");
        var json = Path.Combine(outputDirectory, "nuclear_4t_native_xmodel_layout.json");

        var result = new T4XenonNativeXModelLayoutProbeResult
        {
            NativeFastFile = nativeFastFile,
            NativeIdentityManifest = nativeIdentityManifest,
            NativeXModelRoots = identities.Count,
            UniqueD4Matches = uniqueD4,
            MissingD4Matches = missingD4,
            AmbiguousD4Matches = ambiguousD4,
            LegacyE4Matches = legacyE4,
            D4OnlyMatches = d4Only,
            ExactD4BoundaryProof = exact,
            Rows = rows,
            Notes = notes,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(
            json,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        return result;
    }

    private static List<NativeIdentity> LoadNativeXModels(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.GetProperty("format").GetString() != "WaW360.T4Xenon.NativeIdentityManifest.v1" ||
            !root.GetProperty("typeSequenceVerified").GetBoolean())
            throw new InvalidDataException("Stage 4T requires a verified native identity manifest.");

        return root.GetProperty("entries")
            .EnumerateArray()
            .Where(e =>
                (e.TryGetProperty("TypeName", out var type) &&
                 string.Equals(type.GetString(), "xmodel", StringComparison.OrdinalIgnoreCase)) ||
                (e.TryGetProperty("OatTypeName", out var oat) &&
                 string.Equals(oat.GetString(), "xmodel", StringComparison.OrdinalIgnoreCase)))
            .Select(e => new NativeIdentity(
                e.GetProperty("Index").GetInt32(),
                e.GetProperty("Name").GetString() ?? string.Empty))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.Index)
            .ToList();
    }

    private static bool ValidateD4(byte[] data, int p)
    {
        if (p < 0 || p > data.Length - XboxFixedSize)
            return false;
        if (!IsInline(ReadU32(data, p)))
            return false;

        var numBones = data[p + 4];
        var numRootBones = data[p + 5];
        var numSurfs = data[p + 6];
        var lodRamp = data[p + 7];
        if (numRootBones > numBones || numSurfs > 128 || lodRamp > 3)
            return false;

        foreach (var off in new[] { 0x08, 0x0C, 0x10, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x98, 0xA4 })
            if (!IsSerializedPointer(ReadU32(data, p + off)))
                return false;

        var numLods = ReadU16(data, p + 0xC4);
        var collLod = ReadI16(data, p + 0xC6);
        if (numLods > 4 || collLod < -1 || collLod > 3 || data[p + 0xD1] > 1)
            return false;

        if (!FiniteRange(ReadF32(data, p + 0xA8), 0f, 10_000_000f))
            return false;
        for (var off = 0xAC; off <= 0xC0; off += 4)
            if (!FiniteRange(ReadF32(data, p + off), -100_000_000f, 100_000_000f))
                return false;

        return true;
    }

    private static bool ValidateE4(byte[] data, int p)
    {
        if (p < 0 || p > data.Length - LegacyPcFixedSize)
            return false;
        if (!ValidateD4(data, p))
            return false;

        foreach (var off in new[] { 0xD4, 0xD8, 0xDC, 0xE0 })
            if (!IsSerializedPointer(ReadU32(data, p + off)))
                return false;

        return true;
    }

    private static bool IsInline(uint value) => value is Following or Insert;

    private static bool IsSerializedPointer(uint value)
    {
        if (value == 0 || IsInline(value))
            return true;
        var decoded = T4ZonePointer.Decode(value);
        return decoded.Kind == T4ZonePointerKind.Offset && decoded.Block is not null;
    }

    private static uint ReadU32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static ushort ReadU16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));

    private static short ReadI16(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2));

    private static float ReadF32(byte[] data, int offset) =>
        BitConverter.Int32BitsToSingle(unchecked((int)ReadU32(data, offset)));

    private static bool FiniteRange(float value, float min, float max) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;

    private static string Render(T4XenonNativeXModelLayoutProbeResult r)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("NUCLEAR STAGE 4T - NATIVE XBOX XMODEL FIXED-BODY BOUNDARY PROOF");
        sb.AppendLine(new string('=', 86));
        sb.AppendLine($"Native oracle:              {r.NativeFastFile}");
        sb.AppendLine($"Native XModel roots:        {r.NativeXModelRoots:N0}");
        sb.AppendLine($"Unique 0xD4 matches:        {r.UniqueD4Matches:N0}");
        sb.AppendLine($"Missing 0xD4 matches:       {r.MissingD4Matches:N0}");
        sb.AppendLine($"Ambiguous 0xD4 matches:     {r.AmbiguousD4Matches:N0}");
        sb.AppendLine($"Legacy 0xE4 matches:        {r.LegacyE4Matches:N0}");
        sb.AppendLine($"0xD4-only identities:       {r.D4OnlyMatches:N0}");
        sb.AppendLine($"EXACT 0xD4 BOUNDARY PROOF:  {r.ExactD4BoundaryProof}");
        sb.AppendLine();
        sb.AppendLine("ROOT EVIDENCE");
        sb.AppendLine(" idx    d4 e4  bodyOffset   bones roots surfs lodRamp numLods collLod  name");
        foreach (var row in r.Rows)
        {
            var off = row.D4BodyOffset.HasValue ? $"0x{row.D4BodyOffset.Value:X8}" : "<none>";
            sb.AppendLine(
                $"{row.RootIndex,5}  {row.D4CandidateCount,2} {row.E4CandidateCount,2}  {off,-10}  " +
                $"{row.NumBones,5} {row.NumRootBones,5} {row.NumSurfs,5} {row.LodRampType,7} " +
                $"{row.NumLods,7} {row.CollLod,7}  {row.Name}");
        }
        sb.AppendLine();
        sb.AppendLine("NOTES");
        foreach (var note in r.Notes)
            sb.AppendLine("  - " + note);
        return sb.ToString();
    }
}
