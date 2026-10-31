using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Map-independent T4 Xenon static draw BYTE scalar overlay.
/// Caller provides explicitly source-owned values for a previously verified
/// candidate draw buffer. This component cannot prove PC/Xbox field semantic
/// equivalence: the source proof must be independently audited. No FF write.
/// Never substitute original Xbox reference bytes or synthesize light fields.
/// </summary>
public static class T4XenonStaticDrawScalarCodec
{
    public const string InputSchema = "WaW360.T4Xenon.StaticDrawScalarSource.v1";

    public sealed record Field(string Name, int ByteOffset,
        IReadOnlyList<int> SourceOwnedValues);

    public sealed record Result(byte[] Candidate, int RecordCount,
        int FieldSlotsWritten, IReadOnlyList<string> FieldNames);

    public static Result Encode(byte[] input, int stride, int modelPointerOffset,
        IReadOnlyList<Field> fields)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(fields);
        if (stride <= 0 || modelPointerOffset < 0 ||
            (long)modelPointerOffset + 4 > stride ||
            input.Length == 0 || input.Length % stride != 0)
            throw new InvalidDataException("Static draw layout or candidate length invalid.");
        if (fields.Count == 0)
            throw new InvalidDataException("No individually proven source-owned scalar fields.");

        var rowCount = input.Length / stride;
        var result = (byte[])input.Clone();
        var offsets = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fields)
        {
            if (string.IsNullOrWhiteSpace(f.Name) ||
                !names.Add(f.Name) || f.ByteOffset < 0 || f.ByteOffset >= stride ||
                (f.ByteOffset >= modelPointerOffset &&
                 f.ByteOffset < modelPointerOffset + 4) ||
                !offsets.Add(f.ByteOffset) ||
                f.SourceOwnedValues.Count != rowCount)
                throw new InvalidDataException(
                    $"Native scalar field '{f.Name}' violates layout, owner or row count.");

            for (var row = 0; row < rowCount; row++)
            {
                var value = f.SourceOwnedValues[row];
                var offset = checked(row * stride + f.ByteOffset);
                if (value is < 0 or > 255)
                    throw new InvalidDataException(
                        $"Source scalar {f.Name}[{row}] cannot fit an Xbox uint8 field.");
                if (input[offset] != 0)
                    throw new InvalidDataException(
                        $"Refusing to overwrite existing byte {f.Name}[{row}] in source draw candidate.");
                result[offset] = (byte)value;
            }
        }

        for (var at = 0; at < input.Length; at++)
        {
            if (!offsets.Contains(at % stride) && result[at] != input[at])
                throw new InvalidDataException(
                    $"Attempted mutation of non-scalar source draw byte at {at}.");
        }

        return new Result(result, rowCount, checked(rowCount * fields.Count),
            fields.Select(f => f.Name).ToArray());
    }

    /// <summary>
    /// Input must be built from real PC typed source values and cross-checked
    /// with an independently OAT-derived Xbox semantic witness. This routine
    /// deliberately never treats its output as a complete/approved game FF.
    /// </summary>
    public static async Task<Result> EncodeFromManifestAsync(
        string manifestPath, string outputBinaryPath,
        string outputReportPath, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
        var root = doc.RootElement;
        if (root.GetProperty("schema").GetString() != InputSchema)
            throw new InvalidDataException("Unknown source-owned native scalar manifest schema.");
        var sourceFile = Path.GetFullPath(root.GetProperty("sourceCandidate").GetString()!);
        var sourceSha = root.GetProperty("sourceCandidateSha256").GetString()!;
        var stride = root.GetProperty("recordStride").GetInt32();
        var pointerAt = root.GetProperty("modelPointerOffset").GetInt32();
        var proofSha = root.GetProperty("independentReferenceAuditSha256").GetString()!;
        if (proofSha.Length != 64 || !proofSha.All(Uri.IsHexDigit))
            throw new InvalidDataException("A source/reference scalar audit SHA is required.");
        var source = await File.ReadAllBytesAsync(sourceFile, ct);
        if (!Sha(source).Equals(sourceSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Previously verified pointer candidate SHA drift.");

        var fields = new List<Field>();
        foreach (var item in root.GetProperty("sourceFields").EnumerateArray())
        {
            if (!item.GetProperty("auditedExactPerOrdinalMatch").GetBoolean())
                throw new InvalidDataException("Source-owned scalar field lacks exact comparison evidence.");
            var vals = new List<int>();
            foreach (var val in item.GetProperty("values").EnumerateArray())
                vals.Add(val.GetInt32());
            fields.Add(new Field(
                item.GetProperty("name").GetString()!,
                item.GetProperty("byteOffset").GetInt32(), vals));
        }
        var result = Encode(source, stride, pointerAt, fields);

        var outPath = Path.GetFullPath(outputBinaryPath);
        var reportPath = Path.GetFullPath(outputReportPath);
        if (File.Exists(outPath) || File.Exists(reportPath))
            throw new IOException("Refusing to overwrite an existing scalar candidate or report.");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllBytesAsync(outPath, result.Candidate, ct);
        var report = new
        {
            schema = "WaW360.T4Xenon.StaticDrawScalarResult.v1",
            status = "SOURCE_BYTE_SCALARS_OVERLAID_NOT_GAME_DRAW_ARRAY",
            sourceCandidateSha256 = sourceSha,
            sourceReferenceAuditSha256 = proofSha,
            sourceManifest = Path.GetFullPath(manifestPath),
            candidateBinary = outPath,
            candidateSha256 = Sha(result.Candidate),
            recordCount = result.RecordCount,
            scalarFieldSlotsWritten = result.FieldSlotsWritten,
            fieldNames = result.FieldNames,
            pointerBytesPreserved = true,
            allOtherUnprovenBytesPreserved = true,
            fullGfxWorldSerialized = false,
            productionReady = false
        };
        await File.WriteAllTextAsync(reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false), ct);
        return result;
    }

    public static void SelfTest()
    {
        const int stride = 48;
        var src = new byte[stride * 3];
        // Include nonzero "other" bytes, and 4-byte pointer fields to
        // prove a flag-only source overlay does not mutate any other data.
        for (var r = 0; r < 3; r++)
        {
            src[r * stride + 8] = (byte)(10 + r);
            src[r * stride + 24] = (byte)(20 + r);
        }

        var result = Encode(src, stride, 24,
            new[] { new Field("flags", 40, new[] { 2, 0, 7 }) });
        if (result.RecordCount != 3 || result.FieldSlotsWritten != 3 ||
            result.Candidate[40] != 2 ||
            result.Candidate[stride + 40] != 0 ||
            result.Candidate[2 * stride + 40] != 7 ||
            result.Candidate[24] != src[24] ||
            result.Candidate[stride + 24] != src[stride + 24] ||
            result.Candidate[8] != src[8] ||
            src[40] != 0)
            throw new InvalidDataException("Generic scalar flags/endianness immutability proof failed.");

        try
        {
            _ = Encode(src, stride, 24,
                new[] { new Field("unsafe_xmodel_byte", 24, new[] { 1, 2, 3 }) });
        }
        catch (InvalidDataException)
        {
            // Expected rejection of model pointer overlap.
            goto TestOverflow;
        }
        throw new InvalidDataException("Scalar encoder allowed writing over XModel* pointer.");
    TestOverflow:
        try
        {
            _ = Encode(src, stride, 24,
                new[] { new Field("overflow", 40, new[] { 0, 256, 0 }) });
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidDataException("Scalar encoder accepted out-of-range Xbox uint8.");
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
