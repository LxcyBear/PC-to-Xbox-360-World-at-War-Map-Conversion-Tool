using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed class T4XboxXAnimTransformProofResult
{
    public required string SourceFastFile { get; init; }
    public required string OutputFile { get; init; }
    public required int DeclaredXAnim { get; init; }
    public required int InlineCandidates { get; init; }
    public required int ConvertedXAnim { get; init; }
    public required int DeltaXAnim { get; init; }
    public required long SourceSerializedBytes { get; init; }
    public required long XboxSerializedBytes { get; init; }
    public required long NormalDynamicKeys { get; init; }
    public required long PrecisionDynamicKeys { get; init; }
    public required int NormalConstantBones { get; init; }
    public required int PrecisionConstantBones { get; init; }
    public required bool Verified { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// Full-fastfile proof for the real Xbox 12-bucket XAnim transform.
/// This is an internal proof container, not a game fastfile.
/// </summary>
public static class T4XboxXAnimTransformProof
{
    // Reuse the established Stage-4 XAnim proof-container contract so Stage 5
    // consumes the real 0x5C Xbox payloads without a parallel container format.
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("W360A4X\0");
    private const uint Version = 12;

    public static async Task<T4XboxXAnimTransformProofResult> BuildAsync(
        string pcFastFile,
        string outputFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pcFastFile))
            throw new FileNotFoundException("PC fastfile not found.", pcFastFile);

        var cached = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, cancellationToken);
        if (cached.Platform != WaWPlatform.PC || cached.ZoneHeader.Endianness != Endian.Little)
            throw new InvalidDataException("Xbox XAnim transform proof requires a PC/little-endian WaW fastfile.");

        var zone = cached.Bytes;
        var document = cached.Document;
        var declared = document.Assets.Count(a =>
            a.TypeName.Equals("xanim", StringComparison.OrdinalIgnoreCase));

        var candidates = T4XAnimSerializerProof
            .DiscoverDiagnosticCandidates(zone, Endian.Little)
            .OrderBy(x => x.Offset)
            .ToList();

        if (declared == 0)
            throw new InvalidDataException("Source fastfile declares no XAnim assets.");

        if (candidates.Count != declared)
        {
            throw new InvalidDataException(
                $"Real Xbox XAnim transform currently requires inline top-level XAnim names: " +
                $"XAssetList declares {declared:N0}, inline typed scan resolved {candidates.Count:N0}. " +
                $"Packed-name XAnims remain fail-closed until their asset names are resolved for the viewmodel precision rule.");
        }

        string ResolveScriptString(ushort id)
        {
            if (id >= document.ScriptStrings.Count)
                throw new InvalidDataException($"XAnim ScriptString {id} is outside table size {document.ScriptStrings.Count}.");
            var value = document.ScriptStrings[id].Value;
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("<alias ", StringComparison.Ordinal))
                throw new InvalidDataException($"XAnim ScriptString {id} is unresolved ({value ?? "<null>"}).");
            return value;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);

        var converted = new List<T4XboxXAnimConvertedAsset>(candidates.Count);
        long sourceBytes = 0;
        long xboxBytes = 0;
        long normalKeys = 0;
        long precisionKeys = 0;
        var normalConstants = 0;
        var precisionConstants = 0;
        var deltaCount = 0;

        await using (var fs = new FileStream(
                         outputFile,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         1024 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await fs.WriteAsync(Magic, cancellationToken);
            WriteU32(fs, Version);
            WriteU32(fs, checked((uint)candidates.Count));

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var asset = T4XboxXAnimAssetConverter.ConvertInlinePcAsset(
                    zone,
                    candidate.Offset,
                    ResolveScriptString);

                if (asset.SourceEnd != candidate.End)
                    throw new InvalidDataException(
                        $"XAnim '{asset.Name}' typed source decode ended at 0x{asset.SourceEnd:X8}, " +
                        $"legacy strict measure ended at 0x{candidate.End:X8}.");

                T4XboxXAnimAssetConverter.ValidateXboxBlob(asset.Bytes, asset.Name);

                WriteU32(fs, checked((uint)asset.SourceStart));
                WriteU32(fs, checked((uint)asset.Bytes.Length));
                await fs.WriteAsync(asset.Bytes, cancellationToken);

                converted.Add(asset);
                sourceBytes += asset.SourceEnd - asset.SourceStart;
                xboxBytes += asset.Bytes.Length;
                normalKeys += asset.NormalDynamicKeys;
                precisionKeys += asset.PrecisionDynamicKeys;
                normalConstants += asset.NormalConstantBones;
                precisionConstants += asset.PrecisionConstantBones;
                if (asset.HasDelta) deltaCount++;
            }
        }

        var verified = VerifyContainer(outputFile, converted);
        if (!verified)
            throw new InvalidDataException("Xbox XAnim transform proof container failed independent read-back.");

        var sb = new StringBuilder();
        sb.AppendLine("WaW360 Xbox XAnim 12-bucket transform proof");
        sb.AppendLine($"Source: {pcFastFile}");
        sb.AppendLine($"Declared XAnim:                 {declared:N0}");
        sb.AppendLine($"Inline typed candidates:        {candidates.Count:N0}");
        sb.AppendLine($"Converted + read-back verified: {converted.Count:N0}/{declared:N0}");
        sb.AppendLine($"Delta XAnim:                    {deltaCount:N0}");
        sb.AppendLine($"PC serialized XAnim bytes:      {sourceBytes:N0}");
        sb.AppendLine($"Xbox serialized XAnim bytes:    {xboxBytes:N0}");
        sb.AppendLine($"NORMAL_QUAT dynamic keys:        {normalKeys:N0}");
        sb.AppendLine($"PRECISION_QUAT dynamic keys:     {precisionKeys:N0}");
        sb.AppendLine($"NORMAL_QUAT_NO_SIZE bones:       {normalConstants:N0}");
        sb.AppendLine($"PRECISION_QUAT_NO_SIZE bones:    {precisionConstants:N0}");
        sb.AppendLine($"Verified:                        {verified}");
        sb.AppendLine();
        sb.AppendLine("This proof uses the retail-proven Xbox console transform:");
        sb.AppendLine("  0x58 PC XAnimParts -> 0x5C Xbox XAnimParts");
        sb.AppendLine("  10 PC bone buckets -> 12 Xbox bone buckets");
        sb.AppendLine("  NORMAL_QUAT -> packed 32-bit randomDataInt");
        sb.AppendLine("  PRECISION_QUAT -> packed 48-bit randomDataShort");
        sb.AppendLine("  SIMPLE_QUAT -> packed 16-bit randomDataShort");
        sb.AppendLine("  translation bone indices remapped after quaternion-order sort");

        return new T4XboxXAnimTransformProofResult
        {
            SourceFastFile = pcFastFile,
            OutputFile = outputFile,
            DeclaredXAnim = declared,
            InlineCandidates = candidates.Count,
            ConvertedXAnim = converted.Count,
            DeltaXAnim = deltaCount,
            SourceSerializedBytes = sourceBytes,
            XboxSerializedBytes = xboxBytes,
            NormalDynamicKeys = normalKeys,
            PrecisionDynamicKeys = precisionKeys,
            NormalConstantBones = normalConstants,
            PrecisionConstantBones = precisionConstants,
            Verified = verified,
            Summary = sb.ToString()
        };
    }

    private static bool VerifyContainer(
        string path,
        IReadOnlyList<T4XboxXAnimConvertedAsset> expected)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 16 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != Version)
            return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12, 4)) != (uint)expected.Count)
            return false;

        var cursor = 16;
        foreach (var asset in expected)
        {
            if (cursor > bytes.Length - 8) return false;
            var sourceOffset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor, 4));
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor + 4, 4)));
            cursor += 8;

            if (sourceOffset != (uint)asset.SourceStart || length != asset.Bytes.Length)
                return false;
            if (length <= 0 || cursor > bytes.Length - length)
                return false;

            var blob = bytes.AsSpan(cursor, length).ToArray();
            try
            {
                T4XboxXAnimAssetConverter.ValidateXboxBlob(blob, asset.Name);
            }
            catch
            {
                return false;
            }

            if (!blob.AsSpan().SequenceEqual(asset.Bytes))
                return false;

            cursor += length;
        }

        return cursor == bytes.Length;
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }
}
