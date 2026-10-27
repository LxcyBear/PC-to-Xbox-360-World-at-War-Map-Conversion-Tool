using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed class T4XenonImageGraphReplayResult
{
    public required string ReferenceFastFile { get; init; }
    public required string OutputFastFile { get; init; }
    public required string OutputZoneFile { get; init; }
    public required int Images { get; init; }
    public required long CardMemoryBytes { get; init; }
    public required long ZoneBytes { get; init; }
    public required long MainZoneBytes { get; init; }
    public required long LargeRuntimeBytes { get; init; }
    public required long TempBytes { get; init; }
    public required long VirtualBytes { get; init; }
    public required IReadOnlyList<uint> ReferenceBlockSizes { get; init; }
    public required IReadOnlyList<uint> OutputBlockSizes { get; init; }
    public required uint ReferenceZoneSize { get; init; }
    public required uint OutputZoneSize { get; init; }
    public required bool ExactBlockReplay { get; init; }
    public required bool ExactZoneSizeReplay { get; init; }
    public required bool ExactDecompressedSizeReplay { get; init; }
    public required bool ExactIdentitySequence { get; init; }
    public required int OatListedRoots { get; init; }
    public required bool WrapperRoundTripVerified { get; init; }
    public required bool ReplayPass { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

/// <summary>
/// Replays an OAT-produced T4 Xenon image-only fastfile from the fixed-up image graph.
///
/// This is intentionally a serializer proof, not a production asset source. The input graph
/// may be oracle-derived for validation. Once this exact replay passes, the same serializer
/// can consume graphs produced from user-owned PC IWI/DDS inputs through OAT's T4Xenon loader.
/// </summary>
public static class T4XenonImageGraphReplayEmitter
{
    private const int GfxImageSize = 40;
    private const int GfxImageLoadDefSize = 16;
    private const int D3DTextureSize = 52;

    public static async Task<T4XenonImageGraphReplayResult> EmitAsync(
        string imageGraphDumpRoot,
        string referenceFastFile,
        string outputDirectory,
        string? explicitUnlinkerExe = null,
        int expectedImages = 378,
        CancellationToken cancellationToken = default)
    {
        imageGraphDumpRoot = Path.GetFullPath(imageGraphDumpRoot);
        referenceFastFile = Path.GetFullPath(referenceFastFile);
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var graph = T4XenonImageGraphImporter.Load(imageGraphDumpRoot, expectedImages);
        if (!graph.Verified)
            throw new InvalidDataException(
                "Image graph import is not verified: " + string.Join(" | ", graph.Errors.Take(20)));

        var reference = await FastFileAnalysisCache.GetZoneAsync(referenceFastFile, cancellationToken);
        if (reference.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException("Image graph replay requires an Xbox360 reference FF.");
        if (reference.ZoneHeader.BlockSizes.Count != 7)
            throw new InvalidDataException("Reference XFile header does not expose seven T4 block sizes.");
        if (reference.Document.ScriptStringCount != 0)
            throw new InvalidDataException(
                $"Image-only replay expects zero ScriptStrings; reference has {reference.Document.ScriptStringCount:N0}.");

        var identity = await T4XenonNativeIdentityOracle.RunAsync(
            referenceFastFile,
            Path.Combine(outputDirectory, "reference_identity"),
            explicitUnlinkerExe,
            cancellationToken);

        if (identity.Entries.Count != expectedImages ||
            identity.Entries.Any(x => !x.OatTypeName.Equals("image", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                $"Reference identity is not an image-only {expectedImages:N0}-root zone.");

        var blockers = new List<string>();
        var missingGraphs = identity.Entries
            .Where(x => !graph.ImagesByName.ContainsKey(x.Name))
            .Select(x => x.Name)
            .ToList();
        if (missingGraphs.Count != 0)
            blockers.Add(
                $"Image graph is missing {missingGraphs.Count:N0} reference identities: " +
                string.Join(", ", missingGraphs.Take(12)));

        var extraGraphs = graph.ImagesByName.Keys
            .Where(name => !identity.Entries.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (extraGraphs.Count != 0)
            blockers.Add(
                $"Image graph contains {extraGraphs.Count:N0} identities absent from reference: " +
                string.Join(", ", extraGraphs.Take(12)));

        if (blockers.Count != 0)
            return await WriteBlockedResult(
                graph, reference, identity, referenceFastFile, outputDirectory, blockers, cancellationToken);

        var state = new ReplayState();
        using var main = new MemoryStream();
        using var largeRuntime = new MemoryStream();

        // XFile header placeholder.
        main.Write(new byte[FastFileConstants.T4XFileHeaderSize]);

        // XAssetList: no ScriptStrings, N image roots, inline XAsset[] follows.
        WriteU32(main, 0);
        WriteU32(main, 0);
        WriteU32(main, checked((uint)identity.Entries.Count));
        WriteU32(main, T4ZonePointer.Following);

        state.VirtualAlloc(identity.Entries.Count * 8L, 4, serialized: true);
        foreach (var root in identity.Entries)
        {
            WriteU32(main, checked((uint)root.TypeId));
            WriteU32(main, T4ZonePointer.Insert);
        }

        foreach (var root in identity.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Top-level reusable XAsset INSERT slot lives in VIRTUAL but is not serialized.
            state.VirtualAlloc(4, 4, serialized: false);

            var image = graph.ImagesByName[root.Name];

            // OAT's T4Xenon image writer scopes GfxImage (40) + GfxImageLoadDef (16)
            // together in TEMP, yielding the observed max TEMP size of 56 bytes.
            state.TempRoot(GfxImageSize + GfxImageLoadDefSize);

            main.Write(BuildImageBody(image));

            // ZoneCode reorder: name, pixels, texture.
            EmitString(main, state, image.Name);

            if (image.PixelsBytes != 0)
            {
                state.LargeRuntimeAlloc(image.PixelsBytes, 4096, serialized: true);
                var pixels = await File.ReadAllBytesAsync(image.PixelBlobFile, cancellationToken);
                if (pixels.Length != image.PixelsBytes)
                    throw new InvalidDataException(
                        $"{image.Name}: pixel blob length {pixels.Length:N0} != graph {image.PixelsBytes:N0}.");
                largeRuntime.Write(pixels);
            }

            main.Write(BuildLoadDefBody(image));

            // GfxImageLoadDef's texture child is serialized in VIRTUAL.
            state.VirtualAlloc(D3DTextureSize, 4, serialized: true);
            var texture = await File.ReadAllBytesAsync(image.TextureHeaderBlobFile, cancellationToken);
            if (texture.Length != D3DTextureSize)
                throw new InvalidDataException(
                    $"{image.Name}: D3D texture header is {texture.Length:N0} bytes; expected {D3DTextureSize}.");
            // Xenos D3D resource/fetch words are already in serialized GPU little-endian order.
            main.Write(texture);
        }

        if (state.LargeRuntime != largeRuntime.Length)
            blockers.Add(
                $"LARGE_RUNTIME cursor/file mismatch: cursor={state.LargeRuntime:N0}, bytes={largeRuntime.Length:N0}.");

        PatchHeader(
            main,
            checked((uint)(main.Length - FastFileConstants.T4XFileHeaderSize)),
            checked((uint)state.TempMax),
            checked((uint)state.LargeRuntime),
            checked((uint)state.Virtual));

        var mainBytes = main.ToArray();
        var runtimeBytes = largeRuntime.ToArray();
        var zoneBytes = new byte[checked(mainBytes.Length + runtimeBytes.Length)];
        Buffer.BlockCopy(mainBytes, 0, zoneBytes, 0, mainBytes.Length);
        Buffer.BlockCopy(runtimeBytes, 0, zoneBytes, mainBytes.Length, runtimeBytes.Length);

        var zonePath = Path.Combine(outputDirectory, "nuclear_4o_image_replay.zone");
        await File.WriteAllBytesAsync(zonePath, zoneBytes, cancellationToken);

        var parsed = ZoneHeaderParser.Parse(
            zoneBytes.AsSpan(0, FastFileConstants.T4XFileHeaderSize),
            Endian.Big,
            zoneBytes.LongLength);
        if (!parsed.Plausible)
            blockers.Add("Replayed XFile header is not plausible.");

        var outputFf = Path.Combine(outputDirectory, "nuclear_4o_image_replay.ff");
        var wrapped = await T4XboxFastFileWriter.WriteUnsignedSingleZlibAsync(
            outputFf,
            zoneBytes,
            preferNativeLevel6: true,
            cancellationToken);

        var outputIdentity = await T4XenonNativeIdentityOracle.RunAsync(
            outputFf,
            Path.Combine(outputDirectory, "output_identity"),
            explicitUnlinkerExe,
            cancellationToken);

        var exactIdentity =
            outputIdentity.Entries.Count == identity.Entries.Count &&
            outputIdentity.Entries.Zip(identity.Entries).All(pair =>
                pair.First.TypeId == pair.Second.TypeId &&
                pair.First.Name.Equals(pair.Second.Name, StringComparison.Ordinal));

        if (!exactIdentity)
            blockers.Add("OAT root identity sequence differs from the 4L reference.");

        var referenceBlocks = reference.ZoneHeader.BlockSizes.ToArray();
        var outputBlocks = parsed.BlockSizes.ToArray();
        var exactBlocks = referenceBlocks.SequenceEqual(outputBlocks);
        var exactZoneSize = reference.ZoneHeader.ZoneSize == parsed.ZoneSize;
        var exactDecompressed = reference.Bytes.LongLength == zoneBytes.LongLength;

        if (!exactBlocks)
            blockers.Add(
                "Block replay differs: reference=[" + string.Join(",", referenceBlocks) +
                "] output=[" + string.Join(",", outputBlocks) + "].");
        if (!exactZoneSize)
            blockers.Add(
                $"zoneSize differs: reference={reference.ZoneHeader.ZoneSize:N0}, output={parsed.ZoneSize:N0}.");
        if (!exactDecompressed)
            blockers.Add(
                $"Decompressed bytes differ: reference={reference.Bytes.LongLength:N0}, output={zoneBytes.LongLength:N0}.");

        var pass =
            blockers.Count == 0 &&
            wrapped.RoundTripVerified &&
            exactIdentity &&
            exactBlocks &&
            exactZoneSize &&
            exactDecompressed;

        var result = new T4XenonImageGraphReplayResult
        {
            ReferenceFastFile = referenceFastFile,
            OutputFastFile = outputFf,
            OutputZoneFile = zonePath,
            Images = graph.Images,
            CardMemoryBytes = graph.CardMemoryBytes,
            ZoneBytes = zoneBytes.LongLength,
            MainZoneBytes = mainBytes.LongLength,
            LargeRuntimeBytes = runtimeBytes.LongLength,
            TempBytes = state.TempMax,
            VirtualBytes = state.Virtual,
            ReferenceBlockSizes = referenceBlocks,
            OutputBlockSizes = outputBlocks,
            ReferenceZoneSize = reference.ZoneHeader.ZoneSize,
            OutputZoneSize = parsed.ZoneSize,
            ExactBlockReplay = exactBlocks,
            ExactZoneSizeReplay = exactZoneSize,
            ExactDecompressedSizeReplay = exactDecompressed,
            ExactIdentitySequence = exactIdentity,
            OatListedRoots = outputIdentity.Entries.Count,
            WrapperRoundTripVerified = wrapped.RoundTripVerified,
            ReplayPass = pass,
            Blockers = blockers,
            TextReportFile = Path.Combine(outputDirectory, "nuclear_4o_image_replay.txt"),
            JsonReportFile = Path.Combine(outputDirectory, "nuclear_4o_image_replay.json")
        };

        await WriteResult(result, cancellationToken);
        return result;
    }

    private static async Task<T4XenonImageGraphReplayResult> WriteBlockedResult(
        T4XenonImageGraphImportResult graph,
        FastFileAnalysisCache.CachedZone reference,
        T4XenonNativeIdentityOracleResult identity,
        string referenceFastFile,
        string outputDirectory,
        IReadOnlyList<string> blockers,
        CancellationToken cancellationToken)
    {
        var result = new T4XenonImageGraphReplayResult
        {
            ReferenceFastFile = referenceFastFile,
            OutputFastFile = string.Empty,
            OutputZoneFile = string.Empty,
            Images = graph.Images,
            CardMemoryBytes = graph.CardMemoryBytes,
            ZoneBytes = 0,
            MainZoneBytes = 0,
            LargeRuntimeBytes = 0,
            TempBytes = 0,
            VirtualBytes = 0,
            ReferenceBlockSizes = reference.ZoneHeader.BlockSizes.ToArray(),
            OutputBlockSizes = Array.Empty<uint>(),
            ReferenceZoneSize = reference.ZoneHeader.ZoneSize,
            OutputZoneSize = 0,
            ExactBlockReplay = false,
            ExactZoneSizeReplay = false,
            ExactDecompressedSizeReplay = false,
            ExactIdentitySequence = false,
            OatListedRoots = identity.Entries.Count,
            WrapperRoundTripVerified = false,
            ReplayPass = false,
            Blockers = blockers,
            TextReportFile = Path.Combine(outputDirectory, "nuclear_4o_image_replay.txt"),
            JsonReportFile = Path.Combine(outputDirectory, "nuclear_4o_image_replay.json")
        };
        await WriteResult(result, cancellationToken);
        return result;
    }

    private static byte[] BuildImageBody(T4XenonImageGraph image)
    {
        var b = new byte[GfxImageSize];
        PutU32(b, 0, checked((uint)image.MapType));
        PutU32(b, 4, T4ZonePointer.Following);
        b[8] = image.Semantic;
        PutU32(b, 12, checked((uint)image.CardMemoryBytes));
        PutU16(b, 16, image.Width);
        PutU16(b, 18, image.Height);
        PutU16(b, 20, image.Depth);
        b[22] = image.Category;
        b[23] = image.DelayLoadPixels ? (byte)1 : (byte)0;
        PutU32(b, 24, image.PixelsBytes == 0 ? 0u : T4ZonePointer.Following);
        PutU32(b, 28, image.BaseSize);
        PutU16(b, 32, image.StreamSlot);
        b[34] = image.Streaming ? (byte)1 : (byte)0;
        PutU32(b, 36, string.IsNullOrEmpty(image.Name) ? 0u : T4ZonePointer.Following);
        return b;
    }

    private static byte[] BuildLoadDefBody(T4XenonImageGraph image)
    {
        var d = image.LoadDef;
        var b = new byte[GfxImageLoadDefSize];
        b[0] = d.LevelCount;
        b[1] = d.Flags;
        PutI16(b, 2, d.Dimensions[0]);
        PutI16(b, 4, d.Dimensions[1]);
        PutI16(b, 6, d.Dimensions[2]);
        PutU32(b, 8, unchecked((uint)d.Format));
        PutU32(b, 12, T4ZonePointer.Following);
        return b;
    }

    private static void EmitString(Stream main, ReplayState state, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        state.VirtualAlloc(bytes.Length + 1L, 1, serialized: true);
        main.Write(bytes);
        main.WriteByte(0);
    }

    private static void PatchHeader(
        MemoryStream main,
        uint zoneSize,
        uint temp,
        uint largeRuntime,
        uint virtualSize)
    {
        var pos = main.Position;
        main.Position = 0;
        WriteU32(main, zoneSize);
        WriteU32(main, 0);
        WriteU32(main, temp);
        WriteU32(main, 0);
        WriteU32(main, largeRuntime);
        WriteU32(main, 0);
        WriteU32(main, virtualSize);
        WriteU32(main, 0);
        WriteU32(main, 0);
        main.Position = pos;
    }

    private static async Task WriteResult(
        T4XenonImageGraphReplayResult result,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("NUCLEAR STAGE 4O - C# XENON IMAGE SERIALIZER REPLAY");
        sb.AppendLine("====================================================");
        sb.AppendLine($"Reference FF:              {result.ReferenceFastFile}");
        sb.AppendLine($"Output FF:                 {result.OutputFastFile}");
        sb.AppendLine($"Images:                    {result.Images:N0}");
        sb.AppendLine($"CardMemory bytes:          {result.CardMemoryBytes:N0}");
        sb.AppendLine($"Output decompressed bytes: {result.ZoneBytes:N0}");
        sb.AppendLine($"Main-zone bytes:           {result.MainZoneBytes:N0}");
        sb.AppendLine($"LARGE_RUNTIME bytes:       {result.LargeRuntimeBytes:N0}");
        sb.AppendLine($"TEMP:                      {result.TempBytes:N0}");
        sb.AppendLine($"VIRTUAL:                   {result.VirtualBytes:N0}");
        sb.AppendLine($"Reference zoneSize:        {result.ReferenceZoneSize:N0}");
        sb.AppendLine($"Output zoneSize:           {result.OutputZoneSize:N0}");
        sb.AppendLine($"Reference blocks:          [{string.Join(", ", result.ReferenceBlockSizes)}]");
        sb.AppendLine($"Output blocks:             [{string.Join(", ", result.OutputBlockSizes)}]");
        sb.AppendLine($"Exact block replay:        {result.ExactBlockReplay}");
        sb.AppendLine($"Exact zoneSize replay:     {result.ExactZoneSizeReplay}");
        sb.AppendLine($"Exact decompressed size:   {result.ExactDecompressedSizeReplay}");
        sb.AppendLine($"OAT listed roots:          {result.OatListedRoots:N0}");
        sb.AppendLine($"Exact identity sequence:   {result.ExactIdentitySequence}");
        sb.AppendLine($"Wrapper round-trip:        {result.WrapperRoundTripVerified}");
        sb.AppendLine($"REPLAY PASS:               {result.ReplayPass}");
        sb.AppendLine();
        foreach (var blocker in result.Blockers)
            sb.AppendLine("BLOCKER: " + blocker);

        await File.WriteAllTextAsync(
            result.TextReportFile,
            sb.ToString(),
            new UTF8Encoding(false),
            cancellationToken);
        await File.WriteAllTextAsync(
            result.JsonReportFile,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        stream.Write(b);
    }

    private static void PutU32(byte[] b, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(offset, 4), value);

    private static void PutU16(byte[] b, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(offset, 2), value);

    private static void PutI16(byte[] b, int offset, short value) =>
        BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(offset, 2), value);

    private sealed class ReplayState
    {
        private long _virtual;
        private long _largeRuntime;

        public long Virtual => _virtual;
        public long LargeRuntime => _largeRuntime;
        public long TempMax { get; private set; }
        public long SerializedMain { get; private set; }
        public long SerializedLargeRuntime { get; private set; }

        public long VirtualAlloc(long size, int alignment, bool serialized) =>
            Allocate(ref _virtual, size, alignment, serialized, runtime: false);

        public long LargeRuntimeAlloc(long size, int alignment, bool serialized) =>
            Allocate(ref _largeRuntime, size, alignment, serialized, runtime: true);

        public void TempRoot(int serializedScopeBytes)
        {
            TempMax = Math.Max(TempMax, serializedScopeBytes);
            SerializedMain = checked(SerializedMain + serializedScopeBytes);
        }

        private long Allocate(
            ref long cursor,
            long size,
            int alignment,
            bool serialized,
            bool runtime)
        {
            var mask = alignment - 1L;
            cursor = checked((cursor + mask) & ~mask);
            var result = cursor;
            cursor = checked(cursor + size);

            if (serialized)
            {
                if (runtime)
                    SerializedLargeRuntime = checked(SerializedLargeRuntime + size);
                else
                    SerializedMain = checked(SerializedMain + size);
            }
            return result;
        }
    }
}
