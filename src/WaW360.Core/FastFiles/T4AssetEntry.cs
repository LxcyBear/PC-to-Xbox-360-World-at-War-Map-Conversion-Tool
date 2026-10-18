namespace WaW360.Core.FastFiles;

public enum T4AssetReferenceKind
{
    Null,
    InlineShared,
    Inline,
    OffsetOrAlias
}

public sealed record T4AssetEntry(
    int Index,
    int TypeId,
    string TypeName,
    uint RawHeader,
    T4AssetReferenceKind ReferenceKind)
{
    public static T4AssetReferenceKind Classify(uint rawHeader) => rawHeader switch
    {
        0x00000000u => T4AssetReferenceKind.Null,
        0xFFFFFFFFu => T4AssetReferenceKind.InlineShared,
        0xFFFFFFFEu => T4AssetReferenceKind.Inline,
        _ => T4AssetReferenceKind.OffsetOrAlias
    };
}
