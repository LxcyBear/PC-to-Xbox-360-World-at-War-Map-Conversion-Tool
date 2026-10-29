using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Source-only T4 PC Aztec candidate boundary audit. It reads ONLY the two
/// SHA-pinned v3 full-39 windows and the immediately following 112-byte
/// prospective Material header. This is NOT a world child-stream walker,
/// ownership proof, material decoder, or Xbox converter.
/// </summary>
public static class T4AztecPcGfxWorldMaterialInlineBoundaryProbe
{
    private const int RootOffset = 0x049122C0;
    private const int FirstWindow = 0x057316CB;
    private const int SecondWindow = 0x057316D3;
    private const int Records = 39;
    private const int MaterialMemoryBytes = Records * 8;
    private const string ZoneSha =
        "7B77A415579CDA0DDCF9F14C767C2FD16F5F350018F917EC7B6B30FC3F4036E5";

    public static async Task<AztecMaterialInlineBoundaryReport> ProbeAsync(
        string pcFastFile, string strictV3ReportPath, string outputPath,
        CancellationToken cancellationToken = default)
    {
        using var sourceDoc = JsonDocument.Parse(
            await File.ReadAllTextAsync(strictV3ReportPath, cancellationToken));
        var proof = sourceDoc.RootElement;
        if (proof.GetProperty("Schema").GetString() != "WaW360.AztecPcGfxWorldMaterialMemorySourceProbe.v3" ||
            proof.GetProperty("Status").GetString() !=
                "AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_TWO_OVERLAPPING_WINDOWS_40_RECORD_RUN" ||
            proof.GetProperty("MaterialMemoryCount").GetInt32() != Records ||
            proof.GetProperty("Isolated39RecordSignatures").GetInt32() != 0 ||
            proof.GetProperty("Full39RecordSignatures").GetInt32() != 2 ||
            !proof.GetProperty("TwoFullWindowsOverlapIntoFortyRecordRun").GetBoolean() ||
            proof.GetProperty("ExactPhysicalOwnershipProven").GetBoolean() ||
            proof.GetProperty("PCMaterialNamesResolved").GetBoolean() ||
            proof.GetProperty("XboxMaterialBindingsProven").GetBoolean() ||
            proof.GetProperty("XboxHardwareReady").GetBoolean())
            throw new InvalidDataException(
                "Requires the actual two-overlapping-window 39-row Aztec PC MaterialMemory v3 diagnostic.");

        var original = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, cancellationToken);
        var b = original.Bytes;
        if (original.Platform != WaWPlatform.PC ||
            original.Document.Endianness != Endian.Little ||
            b.Length != 113701260 ||
            !Sha(b).Equals(ZoneSha, StringComparison.OrdinalIgnoreCase) ||
            !original.SourceSha256.Equals(
                proof.GetProperty("OriginalPCFastFileSha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            U32(b, RootOffset + 0x18C) != Records ||
            U32(b, RootOffset + 0x190) != T4ZonePointer.Following)
            throw new InvalidDataException("Original PC world header or source SHA changed.");

        var expectedOffsets = new[] { FirstWindow, SecondWindow };
        var evidence = proof.GetProperty("FullWindowBoundaryEvidence")
            .EnumerateArray().ToArray();
        var offsets = evidence.Select(w => w.GetProperty("SourceOffset").GetInt32())
            .OrderBy(v => v).ToArray();
        if (offsets.Length != 2 ||
            !offsets.SequenceEqual(expectedOffsets))
            throw new InvalidDataException("Source v3 39-record boundary windows changed.");

        var windows = new List<AztecMaterialInlineBoundaryWindow>();
        foreach (var windowStart in expectedOffsets)
        {
            var input = evidence.Single(w => w.GetProperty("SourceOffset").GetInt32() == windowStart);
            var windowSha = Sha(b.AsSpan(windowStart, MaterialMemoryBytes));
            if (!windowSha.Equals(
                    input.GetProperty("Source39WindowSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase) ||
                input.GetProperty("FieldMatchesWithSourceGfxSurfaceMaterialPointers").GetInt32() != 0 ||
                input.GetProperty("FollowingOrInsertDirectiveFields").GetInt32() != Records ||
                input.GetProperty("DistinctMaterialPointerFields").GetInt32() != 1)
                throw new InvalidDataException($"Original-PC 39-record window changed at 0x{windowStart:X8}.");

            var sizes = new int[Records];
            for (var i = 0; i < Records; i++)
            {
                if (U32(b, windowStart + i * 8) != T4ZonePointer.Following)
                    throw new InvalidDataException("Unanticipated non-FOLLOWING candidate material field.");
                sizes[i] = I32(b, windowStart + i * 8 + 4);
            }

            // Critical observation: the apparent 40th 8-byte "record"
            // could instead start a T4 Material object with its name
            // pointer FOLLOWING. Compare both plausible PC 32-bit
            // material layouts, WITHOUT asserting either is the ABI.
            var after = windowStart + MaterialMemoryBytes;
            var shapes = new List<AztecInlineMaterialHeaderShape>();
            foreach (var materialSize in new[] { 108, 112 })
            {
                // MaterialInfo may be 28 or 32 bytes depending on
                // 32-bit alignment for the 8-byte GfxDrawSurf union.
                var infoSize = materialSize - 80;
                var textureAt = infoSize + 59;
                var textureCount = b[after + textureAt];
                var constantCount = b[after + textureAt + 1];
                var stateBitsCount = b[after + textureAt + 2];
                var stateFlags = b[after + textureAt + 3];
                var cameraRegion = b[after + textureAt + 4];
                var pointerAt = infoSize + 64;
                var technique = U32(b, after + pointerAt);
                var texture = U32(b, after + pointerAt + 4);
                var constant = U32(b, after + pointerAt + 8);
                var bits = U32(b, after + pointerAt + 12);

                var plausibleCounts = textureCount <= 64 &&
                    constantCount <= 64 && stateBitsCount <= 64 &&
                    cameraRegion <= 4;
                var stringHits = new List<AztecInlineMaterialNameCandidate>();
                for (var alignment = 0; alignment < 4; alignment++)
                {
                    var stringStart = after + materialSize + alignment;
                    if (TryReadName(b, stringStart, out var name))
                        stringHits.Add(new AztecInlineMaterialNameCandidate(
                            $"0x{stringStart:X8}", name, alignment));
                }

                shapes.Add(new AztecInlineMaterialHeaderShape(
                    materialSize, infoSize, $"0x{after + materialSize:X8}",
                    textureCount, constantCount, stateBitsCount,
                    stateFlags, cameraRegion,
                    $"0x{technique:X8}", $"0x{texture:X8}",
                    $"0x{constant:X8}", $"0x{bits:X8}",
                    plausibleCounts,
                    stringHits.ToArray()));
            }

            windows.Add(new AztecMaterialInlineBoundaryWindow(
                $"0x{windowStart:X8}", windowSha,
                Records, sizes.Min(), sizes.Max(), sizes.Sum(x => (long)x),
                $"0x{after:X8}", $"0x{U32(b, after):X8}",
                U32(b, after) == T4ZonePointer.Following,
                input.GetProperty("PreviousEightByteRecordPlausible").GetBoolean(),
                input.GetProperty("NextEightByteRecordPlausible").GetBoolean(),
                shapes.ToArray()));
        }

        var atLeastOneNameShape = windows.Any(w => w.PossibleMaterialHeaders.Any(h =>
            h.NameCandidates.Count > 0 && h.PlausibleMaterialCountFields));
        var result = new AztecMaterialInlineBoundaryReport(
            "WaW360.AztecPcGfxWorldMaterialInlineBoundary.v1",
            atLeastOneNameShape
                ? "AZTEC_PC_GFXWORLD_39_MEMORY_INLINE_MATERIAL_NAME_CANDIDATE"
                : "AZTEC_PC_GFXWORLD_39_MEMORY_INLINE_MATERIAL_BOUNDARY_UNRESOLVED",
            Path.GetFullPath(pcFastFile), original.SourceSha256, ZoneSha,
            RootOffset, 39, "0xFFFFFFFF", MaterialMemoryBytes,
            expectedOffsets.Select(v => $"0x{v:X8}").ToArray(),
            windows.ToArray(),
            false, false, false, false,
            "Read-only comparison of TWO original-PC SHA-pinned 39x8 windows. " +
            "Their 40th FOLLOWING word can be either the next array record or " +
            "the name pointer in an inline T4 Material body. Counts/strings are " +
            "hypotheses and do NOT establish GfxWorld materialMemory ownership, " +
            "the PC Material* virtual-to-physical mapping, the complete 39 names, " +
            "Xbox Material bindings or a testable GfxWorld FF.");
        var output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
        return result;
    }

    private static bool TryReadName(byte[] b, int start, out string name)
    {
        name = "";
        if (start < 0 || start >= b.Length - 128)
            return false;
        var end = start;
        while (end - start < 128 && b[end] != 0)
        {
            var c = b[end];
            if (c < 32 || c > 126)
                return false;
            end++;
        }
        var len = end - start;
        if (len < 4 || len > 120 || b[end] != 0)
            return false;
        var s = Encoding.ASCII.GetString(b, start, len);
        if (!(char.IsLetter(s[0]) || s[0] is '_' or '*' or ',' or '/'))
            return false;
        if (!s.Any(c => char.IsLetter(c)))
            return false;
        name = s;
        return true;
    }

    private static uint U32(byte[] b, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
    private static int I32(byte[] b, int at) =>
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at, 4));
    private static string Sha(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(SHA256.HashData(b));
}

public sealed record AztecInlineMaterialNameCandidate(
    string SourceOffset, string UnverifiedName, int AlignmentAfterMaterialHeader);
public sealed record AztecInlineMaterialHeaderShape(
    int CandidateMaterialBytes, int CandidateMaterialInfoBytes, string HeaderEndOffset,
    int TextureCount, int ConstantCount, int StateBitsCount,
    int StateFlags, int CameraRegion,
    string TechniqueSetPointer, string TextureTablePointer,
    string ConstantTablePointer, string StateBitsTablePointer,
    bool PlausibleMaterialCountFields,
    IReadOnlyList<AztecInlineMaterialNameCandidate> NameCandidates);
public sealed record AztecMaterialInlineBoundaryWindow(
    string WindowStart, string SourceWindowSha256, int FollowingPointerFields,
    int SmallestMemoryField, int LargestMemoryField, long SumMemoryFields,
    string FollowingHeaderCandidateStart, string FollowingHeaderCandidateFirstWord,
    bool StartsWithFollowing, bool ValidPrecedingEightByteRecord,
    bool ValidFollowingEightByteRecord,
    IReadOnlyList<AztecInlineMaterialHeaderShape> PossibleMaterialHeaders);
public sealed record AztecMaterialInlineBoundaryReport(
    string Schema, string Status, string OriginalPcFastFile,
    string OriginalPcFastFileSha256, string OriginalPcZoneSha256,
    int WorldHeaderOffset, int MaterialMemoryCount, string MaterialMemoryPointerMarker,
    int CandidateMaterialMemoryBytes, IReadOnlyList<string> PhysicalWindowStarts,
    IReadOnlyList<AztecMaterialInlineBoundaryWindow> Windows,
    bool WorldMaterialMemoryArrayOwnershipProven, bool SourceMaterialNamesProven,
    bool XboxMaterialBindingsProven, bool XboxHardwareReady, string Note);
