using System.Text.Json;

namespace WaW360.Core.FastFiles;

public static class T4ZoneManifestWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static async Task WriteAsync(
        T4ZoneDocument document,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var manifest = new
        {
            sourceFastFile = document.SourceFastFile,
            platform = document.Platform.ToString(),
            compression = document.Compression.ToString(),
            endianness = document.Endianness.ToString(),
            xfile = new
            {
                size = document.Header.ZoneSize,
                externalSize = document.Header.ExternalSize,
                blocks = new
                {
                    temp = document.Header.TempBlockSize,
                    runtime = document.Header.RuntimeBlockSize,
                    largeRuntime = document.Header.LargeRuntimeBlockSize,
                    physicalRuntime = document.Header.PhysicalRuntimeBlockSize,
                    @virtual = document.Header.VirtualBlockSize,
                    large = document.Header.LargeBlockSize,
                    physical = document.Header.PhysicalBlockSize
                }
            },
            xassetList = new
            {
                scriptStringCount = document.ScriptStringCount,
                scriptStringsPointer = $"0x{document.ScriptStringsPointer:X8}",
                assetCount = document.AssetCount,
                assetsPointer = $"0x{document.AssetsPointer:X8}",
                scriptStringPointerTableOffset = document.ScriptStringPointerTableOffset,
                assetTableOffset = document.AssetTableOffset
            },
            scriptStrings = document.ScriptStrings.Select(s => new
            {
                s.Index,
                rawPointer = $"0x{s.RawPointer:X8}",
                s.Inline,
                s.Value
            }),
            assetCounts = document.AssetCountsByType,
            assets = document.Assets.Select(a => new
            {
                a.Index,
                a.TypeId,
                a.TypeName,
                rawHeader = $"0x{a.RawHeader:X8}",
                referenceKind = a.ReferenceKind.ToString()
            })
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, manifest, Options, cancellationToken);
    }
}
