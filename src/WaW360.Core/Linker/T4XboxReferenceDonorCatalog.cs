using System.Security.Cryptography;

namespace WaW360.Core.Linker;

public sealed record T4XboxReferenceImageDonor(
    string Name,
    byte[] RuntimeBytes,
    string RuntimeSha256,
    string Provenance,
    bool XboxPayloadVerified,
    bool RetailStockOriginProven);

/// <summary>
/// Hardware-proven reference-donor catalog used to unblock deterministic linker validation
/// while retail-source provenance is still being established.
///
/// XboxPayloadVerified and RetailStockOriginProven are deliberately separate.
/// </summary>
public static class T4XboxReferenceDonorCatalog
{
    private const string DefeatResource = "WaW360.Core.ReferenceAssets.defeat_reference_runtime.bin";
    private const string DefeatRuntimeSha256 = "340853759d2a38087d3429bdeb17a0aeae515407c3ef4a5bdd93f0a733d39d9d";
    private const int DefeatRuntimeBytes = 0x10000;
    private const string AztecLoadscreenResource = "WaW360.Core.ReferenceAssets.loadscreen_nazi_zombie_aztec_reference_runtime.bin";
    private const string AztecLoadscreenRuntimeSha256 = "87477481f84c9e57ad82ffb6bac408a23ba2a2f79f1a4a97fe6d90574100f7f3";
    private const int AztecLoadscreenRuntimeBytes = 0x78000;

    public static T4XboxReferenceImageDonor GetDefeatReference()
    {
        var assembly = typeof(T4XboxReferenceDonorCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(DefeatResource)
            ?? throw new InvalidDataException($"Embedded Xbox reference donor resource '{DefeatResource}' is missing.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        if (bytes.Length != DefeatRuntimeBytes)
            throw new InvalidDataException($"Embedded 'defeat' reference donor is 0x{bytes.Length:X} bytes; expected 0x{DefeatRuntimeBytes:X}.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!hash.Equals(DefeatRuntimeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Embedded 'defeat' reference donor hash mismatch: expected {DefeatRuntimeSha256}, got {hash}.");

        return new T4XboxReferenceImageDonor(
            "defeat",
            bytes,
            hash,
            "Known-working Xbox Aztec load.ff reference: first 0x10000 bytes of the 0x88000 LargeRuntime image arena.",
            XboxPayloadVerified: true,
            RetailStockOriginProven: false);
    }
    public static T4XboxReferenceImageDonor GetAztecLoadscreenReference()
    {
        var assembly = typeof(T4XboxReferenceDonorCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(AztecLoadscreenResource)
            ?? throw new InvalidDataException($"Embedded Xbox reference loadscreen resource '{AztecLoadscreenResource}' is missing.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        if (bytes.Length != AztecLoadscreenRuntimeBytes)
            throw new InvalidDataException($"Embedded Aztec loadscreen reference is 0x{bytes.Length:X} bytes; expected 0x{AztecLoadscreenRuntimeBytes:X}.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!hash.Equals(AztecLoadscreenRuntimeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Embedded Aztec loadscreen reference hash mismatch: expected {AztecLoadscreenRuntimeSha256}, got {hash}.");

        return new T4XboxReferenceImageDonor(
            "loadscreen_nazi_zombie_aztec",
            bytes,
            hash,
            "Known-working Xbox Aztec load.ff reference: second 0x78000 bytes of the 0x88000 LargeRuntime image arena.",
            XboxPayloadVerified: true,
            RetailStockOriginProven: false);
    }

}
