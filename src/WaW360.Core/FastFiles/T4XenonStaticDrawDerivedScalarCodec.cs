using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Generic source-owned T4 Xenon static-draw scalar arithmetic encoder.
/// Allows a bounded uint8 or big-endian uint16 destination derived from an
/// ORIGINAL PC typed integer field by (source + fixedAddend).
/// The calling gate must independently verify semantic compatibility.
/// This is only a candidate: it cannot release or modify a GfxWorld FF.
/// </summary>
public static class T4XenonStaticDrawDerivedScalarCodec
{
    public const string ManifestSchema = "WaW360.T4Xenon.StaticDrawDerivedScalarSource.v1";

    public sealed record DerivedField(string Name, int NativeByteOffset,
        int NativeWidthBytes, int Addend, IReadOnlyList<int> OriginalPcValues);

    public sealed record Result(byte[] Candidate, int Rows, int FieldsWritten,
        IReadOnlyList<string> Names);

    public static Result Encode(byte[] previousCandidate, int stride,
        int modelPointerAt, IReadOnlyList<DerivedField> sourceFields)
    {
        ArgumentNullException.ThrowIfNull(previousCandidate);
        ArgumentNullException.ThrowIfNull(sourceFields);
        if (stride < 8 || modelPointerAt < 0 ||
            (long)modelPointerAt + 4 > stride ||
            previousCandidate.Length == 0 ||
            previousCandidate.Length % stride != 0 ||
            sourceFields.Count == 0)
            throw new InvalidDataException("Invalid T4 native draw count/stride or mapped scalar fields.");
        var rows = previousCandidate.Length / stride;
        var nativeFields = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var candidate = (byte[])previousCandidate.Clone();

        foreach (var f in sourceFields)
        {
            if (string.IsNullOrWhiteSpace(f.Name) || !names.Add(f.Name) ||
                f.NativeWidthBytes is not (1 or 2) ||
                f.NativeByteOffset < 0 ||
                (long)f.NativeByteOffset + f.NativeWidthBytes > stride ||
                f.OriginalPcValues.Count != rows)
                throw new InvalidDataException($"Bad PC scalar-to-native conversion field {f.Name}.");
            for (var byteAt = f.NativeByteOffset;
                 byteAt < f.NativeByteOffset + f.NativeWidthBytes; byteAt++)
            {
                if (byteAt >= modelPointerAt && byteAt < modelPointerAt + 4 ||
                    !nativeFields.Add(byteAt))
                    throw new InvalidDataException(
                        $"Field {f.Name} would overwrite XModel* or another serialized field.");
            }

            for (var i = 0; i < rows; i++)
            {
                var converted = checked((long)f.OriginalPcValues[i] + f.Addend);
                var max = f.NativeWidthBytes == 1 ? 255 : 65535;
                if (converted < 0 || converted > max)
                    throw new InvalidDataException(
                        $"Original PC {f.Name}[{i}] plus addend cannot fit Xenon uint{f.NativeWidthBytes * 8}.");
                var at = checked(i * stride + f.NativeByteOffset);
                for (var j = 0; j < f.NativeWidthBytes; j++)
                    if (previousCandidate[at + j] != 0)
                        throw new InvalidDataException(
                            $"Refusing to overwrite nonzero unproven byte in {f.Name}[{i}].");
                if (f.NativeWidthBytes == 1)
                    candidate[at] = (byte)converted;
                else
                    BinaryPrimitives.WriteUInt16BigEndian(candidate.AsSpan(at, 2),
                        (ushort)converted);
            }
        }

        for (var i = 0; i < candidate.Length; i++)
            if (!nativeFields.Contains(i % stride) &&
                previousCandidate[i] != candidate[i])
                throw new InvalidDataException(
                    $"Source-owned model pointer / geometry / flags mutated at native byte {i}.");

        return new Result(candidate, rows, checked(rows * sourceFields.Count),
            sourceFields.Select(x => x.Name).ToArray());
    }

    public static async Task<Result> EncodeFromManifestAsync(string inputManifest,
        string outputBinary, string outputReport, CancellationToken ct = default)
    {
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(inputManifest, ct));
        var json = doc.RootElement;
        if (json.GetProperty("schema").GetString() != ManifestSchema ||
            !json.GetProperty("sourceOnlyDiagnostic").GetBoolean() ||
            json.GetProperty("productionApproved").GetBoolean())
            throw new InvalidDataException("Production release or invalid Xenon scalar derivation schema denied.");
        var originalFile = Path.GetFullPath(json.GetProperty("sourceCandidate").GetString()!);
        var oldSha = json.GetProperty("sourceCandidateSha256").GetString()!;
        var auditSha = json.GetProperty("sourceAuditSha256").GetString()!;
        var nativeSha = json.GetProperty("originalXboxWitnessSha256").GetString()!;
        if (!ValidSha(oldSha) || !ValidSha(auditSha) || !ValidSha(nativeSha))
            throw new InvalidDataException("Source/audit/native proof SHA fields required.");
        var input = await File.ReadAllBytesAsync(originalFile, ct);
        if (!Sha(input).Equals(oldSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Previously verified PC-derived draw candidate mutated.");

        var fields = new List<DerivedField>();
        foreach (var f in json.GetProperty("fields").EnumerateArray())
        {
            if (!f.GetProperty("allRowsComparedToIndependentXboxWitness").GetBoolean() ||
                f.GetProperty("nativeSemanticsIndependentlyProven").GetBoolean())
                throw new InvalidDataException(
                    "A diagnostic transform requires per-row comparison, with semantics explicitly unproven.");
            fields.Add(new DerivedField(
                f.GetProperty("name").GetString()!,
                f.GetProperty("nativeByteOffset").GetInt32(),
                f.GetProperty("nativeWidthBytes").GetInt32(),
                f.GetProperty("addend").GetInt32(),
                f.GetProperty("originalPcValues").EnumerateArray().Select(x => x.GetInt32()).ToArray()));
        }
        var result = Encode(input,
            json.GetProperty("recordStride").GetInt32(),
            json.GetProperty("modelPointerByteOffset").GetInt32(), fields);
        var output = Path.GetFullPath(outputBinary);
        var report = Path.GetFullPath(outputReport);
        if (File.Exists(output) || File.Exists(report))
            throw new IOException("Refusing to overwrite an earlier tested diagnostic draw candidate.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        await File.WriteAllBytesAsync(output, result.Candidate, ct);
        var summary = new
        {
            schema = "WaW360.T4Xenon.StaticDrawDerivedScalarCandidate.v1",
            status = "PC_OWNED_LIGHT_SCALARS_DERIVED_SOURCE_ONLY_NOT_FF",
            inputSourceCandidateSha256 = oldSha,
            independentScalarAuditSha256 = auditSha,
            independentOriginalXboxWitnessSha256 = nativeSha,
            outputBinary = output,
            outputSha256 = Sha(result.Candidate),
            sourceRows = result.Rows,
            perFieldRowsWritten = result.FieldsWritten,
            sourceFieldNames = result.Names,
            pointerAndAllOtherBytesPreserved = true,
            runtimeLightingSemanticsIndependentlyProven = false,
            originalXboxReferenceBodyBytesImported = false,
            nativeGfxWorldPhysicalDrawLinks = 0,
            productionApproved = false
        };
        await File.WriteAllTextAsync(report,
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false), ct);
        return result;
    }

    public static void SelfTest()
    {
        const int stride = 52, ptrAt = 28;
        var input = new byte[stride * 2];
        input[5] = 27;
        input[ptrAt] = 0xA0;
        input[stride + ptrAt] = 0xB0;
        input[40] = 0x80;
        input[stride + 40] = 0x30;
        var fields = new[]
        {
            new DerivedField("reflectionProbe", 36, 1, -1, new[]{1,2}),
            new DerivedField("lightingHandle",38,2,256,new[]{0,1})
        };
        var outBytes = Encode(input, stride, ptrAt, fields);
        if (outBytes.Rows != 2 || outBytes.FieldsWritten != 4 ||
            outBytes.Candidate[36] != 0 ||
            outBytes.Candidate[stride + 36] != 1 ||
            BinaryPrimitives.ReadUInt16BigEndian(outBytes.Candidate.AsSpan(38,2)) != 256 ||
            BinaryPrimitives.ReadUInt16BigEndian(outBytes.Candidate.AsSpan(stride+38,2)) != 257 ||
            outBytes.Candidate[ptrAt] != 0xA0 ||
            outBytes.Candidate[stride + ptrAt] != 0xB0 ||
            outBytes.Candidate[40] != 0x80 ||
            outBytes.Candidate[stride + 40] != 0x30 ||
            outBytes.Candidate[5] != 27 || input[38] != 0)
            throw new InvalidDataException("Generic 52B native scalar endian/immutability selftest failed.");

        try
        {
            _ = Encode(input, stride, ptrAt,
                new[]{new DerivedField("overlapping_pointer",ptrAt,2,0,new[]{0,0})});
            throw new InvalidOperationException("Model pointer overwrite unexpectedly allowed.");
        }
        catch (InvalidDataException) { }

        try
        {
            _ = Encode(input, stride, ptrAt,
                new[]{new DerivedField("overflow",38,2,70000,new[]{0,1})});
            throw new InvalidOperationException("Unsigned field overflow unexpectedly allowed.");
        }
        catch (InvalidDataException) { }
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool ValidSha(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
