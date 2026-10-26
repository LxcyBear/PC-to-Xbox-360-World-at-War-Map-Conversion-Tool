using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed record T4XenonTechniqueSetBody(
    int NativeRootIndex,
    string Name,
    int SourceOffset,
    byte WorldVertFormat,
    uint RemappedTechniqueSet,
    int NonNullTechniqueSlots,
    int InlineTechniqueSlots,
    int PackedTechniqueSlots,
    IReadOnlyList<uint> TechniquePointers);

public sealed class T4XenonTechniqueSetBodyCatalogResult
{
    public required string XboxFastFile { get; init; }
    public required int ExpectedTechniqueSets { get; init; }
    public required int RecoveredTechniqueSets { get; init; }
    public required int InlineOwnedTechniqueRoots { get; init; }
    public required int AliasTechniqueRoots { get; init; }
    public required int DistinctTechniqueBodies { get; init; }
    public required int TotalNonNullTechniqueSlots { get; init; }
    public required int TotalInlineTechniqueSlots { get; init; }
    public required int TotalPackedTechniqueSlots { get; init; }
    public required IReadOnlyList<T4XenonTechniqueSetBody> Bodies { get; init; }
    public required IReadOnlyList<string> MissingNames { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required bool Verified { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Nuclear Stage 3A: recover every native T4 Xenon MaterialTechniqueSet fixed body.
///
/// Unlike the retired donor scanner, this resolver accepts BOTH inline names and packed
/// TEMP name pointers. Candidate bodies are not trusted by shape alone: their resolved
/// names must exactly match OAT's authoritative Xenon root identity list.
///
/// The output is still graph metadata only. It exposes the exact 51 raw Technique pointers
/// for each native TechniqueSet and becomes the deterministic seed for the typed
/// MaterialTechnique / MaterialPass / shader walker.
/// </summary>
public static class T4XenonTechniqueSetBodyCatalog
{
    private const int TechniqueSetSize = 216;
    private const int TechniqueSlots = 51;

    public static async Task<T4XenonTechniqueSetBodyCatalogResult> BuildAsync(
        string xboxFastFile,
        string outputDirectory,
        string? explicitUnlinkerExe = null,
        CancellationToken cancellationToken = default)
    {
        xboxFastFile = Path.GetFullPath(xboxFastFile);
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var cached = await FastFileAnalysisCache.GetZoneAsync(xboxFastFile, cancellationToken);
        if (cached.Platform != WaWPlatform.Xbox360 || cached.Document.Endianness != Endian.Big)
            throw new InvalidDataException(
                $"Xenon TechniqueSet body catalog requires Xbox360/big-endian input; parsed {cached.Platform}/{cached.Document.Endianness}.");

        var identityDir = Path.Combine(outputDirectory, "native_identity");
        var identity = await T4XenonNativeIdentityOracle.RunAsync(
            xboxFastFile,
            identityDir,
            explicitUnlinkerExe,
            cancellationToken);
        if (!identity.TypeSequenceVerified)
            throw new InvalidDataException("Native identity oracle did not verify the complete Xenon root sequence.");

        var expected = identity.Entries
            .Where(x => x.OatTypeName.Equals("techniqueset", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Index)
            .ToList();

        if (expected.Count == 0)
            throw new InvalidDataException("Native identity oracle contains zero TechniqueSet roots.");

        var expectedByName = expected.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var nativeAssetsByIndex = cached.Document.Assets.ToDictionary(x => x.Index);
        var payloadStart = checked((int)(cached.Document.AssetTableOffset + cached.Document.AssetCount * 8L));
        var zone = cached.Bytes;

        // First recover only the inline-owned TechniqueSet bodies. Offset/alias roots do
        // not own another copy; their XAsset.header is itself the packed pointer to the
        // previously serialized TEMP body.
        var scannedInlineBodies = new Dictionary<string, List<T4XenonTechniqueSetBody>>(StringComparer.OrdinalIgnoreCase);
        var first = (payloadStart + 3) & ~3;
        for (var offset = first; offset <= zone.Length - TechniqueSetSize; offset += 4)
        {
            if ((offset & 0x7FFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            if (!TryParseCandidate(
                    zone,
                    cached.Document.Header,
                    offset,
                    expectedByName,
                    out var body))
                continue;

            if (!scannedInlineBodies.TryGetValue(body!.Name, out var list))
            {
                list = new List<T4XenonTechniqueSetBody>();
                scannedInlineBodies[body.Name] = list;
            }
            list.Add(body);
        }

        var resolved = new List<T4XenonTechniqueSetBody>(expected.Count);
        var missing = new List<string>();
        var ambiguous = new List<string>();
        var inlineRootCount = 0;
        var aliasRootCount = 0;

        foreach (var root in expected)
        {
            if (!nativeAssetsByIndex.TryGetValue(root.Index, out var asset))
            {
                missing.Add(root.Name + " [native XAsset row missing]");
                continue;
            }

            if (asset.TypeId != 8)
            {
                missing.Add(root.Name + $" [native XAsset type={asset.TypeId}, expected 8]");
                continue;
            }

            if (asset.ReferenceKind is T4AssetReferenceKind.OffsetOrAlias)
            {
                aliasRootCount++;
                var decoded = T4ZonePointer.Decode(asset.RawHeader);
                if (decoded.Kind != T4ZonePointerKind.Offset ||
                    decoded.Block != T4ZoneBlock.Temp ||
                    decoded.Offset > int.MaxValue)
                {
                    missing.Add(root.Name + $" [alias header {T4ZonePointer.Describe(asset.RawHeader)} is not TEMP]");
                    continue;
                }

                var absolute = FastFileConstants.T4XFileHeaderSize + checked((int)decoded.Offset);
                if (!TryParseCandidate(
                        zone,
                        cached.Document.Header,
                        absolute,
                        expectedByName,
                        out var aliasBody))
                {
                    missing.Add(root.Name + $" [TEMP alias target 0x{absolute:X8} did not parse as a TechniqueSet body]");
                    continue;
                }

                if (!string.Equals(aliasBody!.Name, root.Name, StringComparison.OrdinalIgnoreCase))
                {
                    missing.Add(root.Name + $" [TEMP alias target resolves to '{aliasBody.Name}']");
                    continue;
                }

                resolved.Add(aliasBody with { NativeRootIndex = root.Index });
                continue;
            }

            if (asset.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            {
                inlineRootCount++;
                if (!scannedInlineBodies.TryGetValue(root.Name, out var matches) || matches.Count == 0)
                {
                    missing.Add(root.Name + " [inline-owned body not found]");
                    continue;
                }

                var distinct = matches
                    .GroupBy(x => x.SourceOffset)
                    .Select(g => g.First())
                    .OrderBy(x => x.SourceOffset)
                    .ToList();
                if (distinct.Count != 1)
                {
                    ambiguous.Add($"{root.Name} ({distinct.Count} inline body candidates: {string.Join(", ", distinct.Take(8).Select(x => $"0x{x.SourceOffset:X}"))})");
                    continue;
                }

                resolved.Add(distinct[0] with { NativeRootIndex = root.Index });
                continue;
            }

            missing.Add(root.Name + $" [unsupported XAsset header kind {asset.ReferenceKind}, raw=0x{asset.RawHeader:X8}]");
        }

        var totalNonNull = resolved.Sum(x => x.NonNullTechniqueSlots);
        var totalInline = resolved.Sum(x => x.InlineTechniqueSlots);
        var totalPacked = resolved.Sum(x => x.PackedTechniqueSlots);

        var notes = new List<string>
        {
            "NUCLEAR STAGE 3A: XAsset.header drives ownership. Inline/InlineShared roots own serialized bodies; OffsetOrAlias roots resolve their packed TEMP header directly to an existing body.",
            "Fixed-body candidates are accepted only when their resolved name matches one OAT-verified native TechniqueSet identity.",
            "Packed TEMP pointers resolve as XFileHeader(36 bytes) + logical TEMP offset; inline FOLLOWING/INSERT names are read immediately after the 216-byte fixed body.",
            "Technique pointers are preserved as raw serialized values for the next typed graph walker; no donor payload bytes are emitted by this stage.",
            $"Pointer census across recovered sets: nonNull={totalNonNull:N0}; inline={totalInline:N0}; packed={totalPacked:N0}."
        };
        if (ambiguous.Count != 0)
            notes.Add("Ambiguous native body matches: " + string.Join(" | ", ambiguous.Take(16)));

        var verified = resolved.Count == expected.Count && missing.Count == 0 && ambiguous.Count == 0;

        var txt = Path.Combine(outputDirectory, "xenon_techset_body_catalog.txt");
        var json = Path.Combine(outputDirectory, "xenon_techset_body_catalog.json");

        var result = new T4XenonTechniqueSetBodyCatalogResult
        {
            XboxFastFile = xboxFastFile,
            ExpectedTechniqueSets = expected.Count,
            RecoveredTechniqueSets = resolved.Count,
            InlineOwnedTechniqueRoots = inlineRootCount,
            AliasTechniqueRoots = aliasRootCount,
            DistinctTechniqueBodies = resolved.Select(x => x.SourceOffset).Distinct().Count(),
            TotalNonNullTechniqueSlots = totalNonNull,
            TotalInlineTechniqueSlots = totalInline,
            TotalPackedTechniqueSlots = totalPacked,
            Bodies = resolved.OrderBy(x => x.NativeRootIndex).ToList(),
            MissingNames = missing,
            Notes = notes,
            Verified = verified,
            TextReportFile = txt,
            JsonReportFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result, ambiguous), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(
            json,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        return result;
    }

    private static bool TryParseCandidate(
        byte[] zone,
        ZoneHeaderSummary header,
        int offset,
        IReadOnlyDictionary<string, T4XenonNativeIdentityEntry> expectedByName,
        out T4XenonTechniqueSetBody? body)
    {
        body = null;
        if (offset < 0 || offset > zone.Length - TechniqueSetSize)
            return false;

        var namePtr = U32(zone, offset);
        if (!PointerValid(namePtr, header))
            return false;

        var worldVert = zone[offset + 4];
        if (worldVert > 0x0B)
            return false;

        var remapped = U32(zone, offset + 8);
        // OAT's T4Xenon zone code marks remappedTechniqueSet "never". Stock/native
        // zones are expected to serialize zero here; accepting a non-zero value would
        // make a shape-only false positive far easier.
        if (remapped != 0)
            return false;

        var pointers = new uint[TechniqueSlots];
        for (var i = 0; i < pointers.Length; i++)
        {
            var raw = U32(zone, offset + 12 + i * 4);
            if (!PointerValid(raw, header))
                return false;
            pointers[i] = raw;
        }

        if (!TryResolveName(zone, header, namePtr, offset + TechniqueSetSize, out var name))
            return false;
        if (!expectedByName.ContainsKey(name))
            return false;

        var nonNull = pointers.Count(x => x != 0);
        var inline = pointers.Count(x => x is T4ZonePointer.Following or T4ZonePointer.Insert);
        var packed = pointers.Count(x => x != 0 && x is not (T4ZonePointer.Following or T4ZonePointer.Insert));

        body = new T4XenonTechniqueSetBody(
            -1,
            name,
            offset,
            worldVert,
            remapped,
            nonNull,
            inline,
            packed,
            pointers);
        return true;
    }

    private static bool TryResolveName(
        byte[] zone,
        ZoneHeaderSummary header,
        uint raw,
        int inlineOffset,
        out string name)
    {
        name = string.Empty;

        if (raw is T4ZonePointer.Following or T4ZonePointer.Insert)
            return TryReadAsciiZ(zone, inlineOffset, out name);

        if (raw == 0 || !PointerValid(raw, header))
            return false;

        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset ||
            decoded.Block != T4ZoneBlock.Temp ||
            decoded.Offset > int.MaxValue)
            return false;

        var absolute = FastFileConstants.T4XFileHeaderSize + checked((int)decoded.Offset);
        return TryReadAsciiZ(zone, absolute, out name);
    }

    private static bool TryReadAsciiZ(byte[] zone, int offset, out string value)
    {
        value = string.Empty;
        if (offset < 0 || offset >= zone.Length)
            return false;

        var end = offset;
        var limit = Math.Min(zone.Length, offset + 1024);
        while (end < limit && zone[end] != 0)
        {
            var b = zone[end];
            if (b < 0x20 || b > 0x7E)
                return false;
            end++;
        }

        if (end == offset || end >= limit || zone[end] != 0)
            return false;

        value = Encoding.ASCII.GetString(zone, offset, end - offset);
        return LooksLikeName(value);
    }

    private static bool LooksLikeName(string value) =>
        value.Length is > 0 and <= 512 &&
        value.Any(char.IsLetterOrDigit) &&
        value.All(ch =>
            char.IsLetterOrDigit(ch) ||
            ch is '_' or '-' or '.' or '/' or '\\' or '$' or '@' or '+' or '#');

    private static bool PointerValid(uint raw, ZoneHeaderSummary header) =>
        raw == 0 ||
        raw is T4ZonePointer.Following or T4ZonePointer.Insert ||
        T4ZonePointer.IsWithinBlock(raw, header);

    private static uint U32(byte[] zone, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(zone.AsSpan(offset, 4));

    private static string Render(
        T4XenonTechniqueSetBodyCatalogResult result,
        IReadOnlyList<string> ambiguous)
    {
        var sb = new StringBuilder(256 * 1024);
        sb.AppendLine("WaW360Converter — NUCLEAR STAGE 3A XENON TECHNIQUESET BODY CATALOG");
        sb.AppendLine(new string('=', 96));
        sb.AppendLine($"Xbox FF: {result.XboxFastFile}");
        sb.AppendLine($"expected TechniqueSets : {result.ExpectedTechniqueSets:N0}");
        sb.AppendLine($"recovered TechniqueSets: {result.RecoveredTechniqueSets:N0}");
        sb.AppendLine($"VERIFIED               : {result.Verified}");
        sb.AppendLine($"root ownership         : inlineOwned={result.InlineOwnedTechniqueRoots:N0}; alias={result.AliasTechniqueRoots:N0}; distinctBodies={result.DistinctTechniqueBodies:N0}");
        sb.AppendLine($"technique slots        : nonNull={result.TotalNonNullTechniqueSlots:N0}; inline={result.TotalInlineTechniqueSlots:N0}; packed={result.TotalPackedTechniqueSlots:N0}");
        sb.AppendLine();

        if (result.MissingNames.Count != 0)
        {
            sb.AppendLine("MISSING OAT IDENTITIES");
            foreach (var name in result.MissingNames)
                sb.AppendLine("  " + name);
            sb.AppendLine();
        }

        if (ambiguous.Count != 0)
        {
            sb.AppendLine("AMBIGUOUS BODY MATCHES");
            foreach (var item in ambiguous)
                sb.AppendLine("  " + item);
            sb.AppendLine();
        }

        sb.AppendLine("RECOVERED NATIVE TECHNIQUESET BODIES");
        foreach (var body in result.Bodies)
        {
            sb.AppendLine(
                $"  nativeRoot#{body.NativeRootIndex,5} @0x{body.SourceOffset:X8} " +
                $"worldVert={body.WorldVertFormat,2} slots={body.NonNullTechniqueSlots,2} " +
                $"inline={body.InlineTechniqueSlots,2} packed={body.PackedTechniqueSlots,2} {body.Name}");
        }
        sb.AppendLine();

        foreach (var note in result.Notes)
            sb.AppendLine("NOTE: " + note);

        return sb.ToString();
    }
}
