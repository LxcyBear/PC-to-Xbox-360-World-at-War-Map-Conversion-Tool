namespace WaW360.Core.FastFiles;

public static class FastFileConstants
{
    public const string UnsignedMagic = "IWffu100";
    public const string SignedMagic = "IWff0100";
    public const string StreamingMagic = "IWffs100";

    public const int StandardHeaderSize = 12;
    public const int WaWVersion = 0x183;

    // T4's decompressed zone starts with a 36-byte XFile header followed by the
    // 16-byte XAssetList. Older notes often call the combined 52 bytes the
    // "zone header"; keep this combined constant for compatibility with Stage 1.
    public const int T4XFileHeaderSize = 36;
    public const int T4XAssetListSize = 16;
    public const int WaWZoneHeaderSize = T4XFileHeaderSize + T4XAssetListSize;

    public const int XboxSignedHashTableSize = 0x3FF8;
    public const int XboxZoneBlockSize = 0x10000;

    // Typical values seen in existing tools/zones. Do not hard-code these into
    // the future writer: real codxe zones can legitimately use other values.
    public const uint XboxTypicalTempBlockSize = 0x00000A90;
    public const uint PcTypicalTempBlockSize = 0x000010B0;
}
