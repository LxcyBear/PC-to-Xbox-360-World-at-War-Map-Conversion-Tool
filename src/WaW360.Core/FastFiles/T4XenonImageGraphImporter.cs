using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed record T4XenonImageLoadDefGraph(
    byte LevelCount,
    byte Flags,
    IReadOnlyList<short> Dimensions,
    int Format,
    string TextureHeaderFile,
    int TextureHeaderBytes);

public sealed record T4XenonImageGraph(
    string Name,
    int MapType,
    byte Semantic,
    int CardMemoryBytes,
    ushort Width,
    ushort Height,
    ushort Depth,
    byte Category,
    bool DelayLoadPixels,
    uint BaseSize,
    ushort StreamSlot,
    bool Streaming,
    string PixelsFile,
    int PixelsBytes,
    T4XenonImageLoadDefGraph LoadDef,
    string GraphFile,
    string PixelBlobFile,
    string TextureHeaderBlobFile);

public sealed class T4XenonImageGraphImportResult
{
    public required string DumpRoot { get; init; }
    public required int Images { get; init; }
    public required long CardMemoryBytes { get; init; }
    public required long PixelBlobBytes { get; init; }
    public required int DelayedImages { get; init; }
    public required int ImmediateImages { get; init; }
    public required int StreamingImages { get; init; }
    public required int CubeImages { get; init; }
    public required IReadOnlyDictionary<string, T4XenonImageGraph> ImagesByName { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required bool Verified { get; init; }
}

/// <summary>
/// Imports the fixed-up T4 Xenon image graph emitted by the specialized OAT Unlinker.
///
/// The graph is intentionally downstream of OAT's real T4Xenon image encoder. It contains
/// destination-side GfxImage/GfxImageLoadDef metadata plus the exact 52-byte Xenos
/// D3DBaseTexture header and tiled pixel blob that the block-aware serializer must emit.
///
/// Native/oracle-derived graphs are validation vectors only. Production callers must feed
/// this same format from user-owned PC IWI/DDS inputs passed through OAT's T4Xenon loader.
/// </summary>
public static class T4XenonImageGraphImporter
{
    private sealed class GraphDoc
    {
        public string Format { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int MapType { get; set; }
        public byte Semantic { get; set; }
        public int CardMemoryBytes { get; set; }
        public ushort Width { get; set; }
        public ushort Height { get; set; }
        public ushort Depth { get; set; }
        public byte Category { get; set; }
        public bool DelayLoadPixels { get; set; }
        public uint BaseSize { get; set; }
        public ushort StreamSlot { get; set; }
        public bool Streaming { get; set; }
        public string? PixelsFile { get; set; }
        public int PixelsBytes { get; set; }
        public LoadDefDoc? LoadDef { get; set; }
    }

    private sealed class LoadDefDoc
    {
        public byte LevelCount { get; set; }
        public byte Flags { get; set; }
        public short[] Dimensions { get; set; } = Array.Empty<short>();
        public int Format { get; set; }
        public string? TextureHeaderFile { get; set; }
        public int TextureHeaderBytes { get; set; }
    }

    public static T4XenonImageGraphImportResult Load(
        string dumpRoot,
        int? expectedImages = null)
    {
        dumpRoot = Path.GetFullPath(dumpRoot);
        if (!Directory.Exists(dumpRoot))
            throw new DirectoryNotFoundException($"Xenon image graph root not found: {dumpRoot}");

        var graphRoot = Path.Combine(dumpRoot, "xenon_image_graphs");
        if (!Directory.Exists(graphRoot))
            throw new DirectoryNotFoundException(
                $"Xenon image graph directory not found: {graphRoot}");

        var errors = new List<string>();
        var byName = new Dictionary<string, T4XenonImageGraph>(StringComparer.OrdinalIgnoreCase);
        long totalCard = 0;
        long totalPixels = 0;
        var delayed = 0;
        var immediate = 0;
        var streaming = 0;
        var cubes = 0;

        var json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        foreach (var graphFile in Directory.EnumerateFiles(
                     graphRoot, "*.json", SearchOption.AllDirectories)
                 .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            GraphDoc? doc;
            try
            {
                doc = JsonSerializer.Deserialize<GraphDoc>(
                    File.ReadAllText(graphFile), json);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(graphFile)}: JSON parse failed: {ex.Message}");
                continue;
            }

            if (doc is null)
            {
                errors.Add($"{Path.GetFileName(graphFile)}: empty graph document.");
                continue;
            }
            if (!doc.Format.Equals("WaW360.T4Xenon.ImageGraph.v1", StringComparison.Ordinal))
            {
                errors.Add($"{Path.GetFileName(graphFile)}: unexpected format '{doc.Format}'.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(doc.Name))
            {
                errors.Add($"{Path.GetFileName(graphFile)}: image name is empty.");
                continue;
            }
            if (doc.LoadDef is null)
            {
                errors.Add($"{doc.Name}: loadDef is null.");
                continue;
            }
            if (doc.LoadDef.Dimensions.Length != 3)
            {
                errors.Add($"{doc.Name}: loadDef dimensions count is {doc.LoadDef.Dimensions.Length}; expected 3.");
                continue;
            }
            if (doc.LoadDef.TextureHeaderBytes != 52)
            {
                errors.Add($"{doc.Name}: texture header size is {doc.LoadDef.TextureHeaderBytes}; expected 52.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(doc.LoadDef.TextureHeaderFile))
            {
                errors.Add($"{doc.Name}: texture header path is missing.");
                continue;
            }
            if (doc.CardMemoryBytes < 0 || doc.PixelsBytes < 0)
            {
                errors.Add($"{doc.Name}: negative image byte count.");
                continue;
            }
            if (doc.CardMemoryBytes != doc.PixelsBytes)
            {
                errors.Add(
                    $"{doc.Name}: cardMemory={doc.CardMemoryBytes:N0} != pixelsBytes={doc.PixelsBytes:N0}.");
                continue;
            }

            var texturePath = ResolveGraphBlob(dumpRoot, doc.LoadDef.TextureHeaderFile);
            if (!File.Exists(texturePath))
            {
                errors.Add($"{doc.Name}: texture header blob missing: {doc.LoadDef.TextureHeaderFile}");
                continue;
            }
            if (new FileInfo(texturePath).Length != 52)
            {
                errors.Add($"{doc.Name}: texture header blob is not 52 bytes.");
                continue;
            }

            string pixelPath = string.Empty;
            if (doc.PixelsBytes != 0)
            {
                if (string.IsNullOrWhiteSpace(doc.PixelsFile))
                {
                    errors.Add($"{doc.Name}: nonzero pixel size but no pixelsFile.");
                    continue;
                }

                pixelPath = ResolveGraphBlob(dumpRoot, doc.PixelsFile);
                if (!File.Exists(pixelPath))
                {
                    errors.Add($"{doc.Name}: pixel blob missing: {doc.PixelsFile}");
                    continue;
                }
                if (new FileInfo(pixelPath).Length != doc.PixelsBytes)
                {
                    errors.Add(
                        $"{doc.Name}: pixel blob length {new FileInfo(pixelPath).Length:N0} != {doc.PixelsBytes:N0}.");
                    continue;
                }
            }

            var loadDef = new T4XenonImageLoadDefGraph(
                doc.LoadDef.LevelCount,
                doc.LoadDef.Flags,
                doc.LoadDef.Dimensions,
                doc.LoadDef.Format,
                doc.LoadDef.TextureHeaderFile,
                doc.LoadDef.TextureHeaderBytes);

            var graph = new T4XenonImageGraph(
                doc.Name,
                doc.MapType,
                doc.Semantic,
                doc.CardMemoryBytes,
                doc.Width,
                doc.Height,
                doc.Depth,
                doc.Category,
                doc.DelayLoadPixels,
                doc.BaseSize,
                doc.StreamSlot,
                doc.Streaming,
                doc.PixelsFile ?? string.Empty,
                doc.PixelsBytes,
                loadDef,
                graphFile,
                pixelPath,
                texturePath);

            if (!byName.TryAdd(doc.Name, graph))
            {
                errors.Add($"{doc.Name}: duplicate fixed-up image graph identity.");
                continue;
            }

            totalCard += doc.CardMemoryBytes;
            totalPixels += doc.PixelsBytes;
            if (doc.DelayLoadPixels) delayed++; else immediate++;
            if (doc.Streaming) streaming++;
            if (doc.MapType == 5) cubes++;
        }

        if (expectedImages is not null && byName.Count != expectedImages.Value)
            errors.Add($"Image graph count {byName.Count:N0} != expected {expectedImages.Value:N0}.");

        if (totalCard != totalPixels)
            errors.Add($"Aggregate cardMemory={totalCard:N0} != pixel bytes={totalPixels:N0}.");

        return new T4XenonImageGraphImportResult
        {
            DumpRoot = dumpRoot,
            Images = byName.Count,
            CardMemoryBytes = totalCard,
            PixelBlobBytes = totalPixels,
            DelayedImages = delayed,
            ImmediateImages = immediate,
            StreamingImages = streaming,
            CubeImages = cubes,
            ImagesByName = byName,
            Errors = errors,
            Verified = errors.Count == 0
        };
    }

    private static string ResolveGraphBlob(string root, string relative)
    {
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(root, normalized));
    }
}
