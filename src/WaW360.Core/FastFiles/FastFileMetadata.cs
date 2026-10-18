namespace WaW360.Core.FastFiles;

public enum FastFileSignatureKind
{
    Unknown,
    Unsigned,
    Signed,
    Streaming
}

public enum WaWPlatform
{
    Unknown,
    PC,
    Xbox360
}

public enum WaWCompressionKind
{
    Unknown,
    PcSingleZlib,
    XboxSingleZlib,
    XboxBlockDeflate,
    XboxSignedSingleZlib
}

public sealed record FastFileMetadata(
    string FilePath,
    string Magic,
    byte[] VersionBytes,
    int Version,
    FastFileSignatureKind Signature,
    WaWPlatform Platform,
    WaWCompressionKind Compression,
    long CompressedSize,
    long DecompressedSize,
    ZoneHeaderSummary? ZoneHeader,
    IReadOnlyList<string> Warnings)
{
    public bool IsWorldAtWar => Version == FastFileConstants.WaWVersion;
}
