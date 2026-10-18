using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4XboxSingleZlibWriteResult(
    string OutputFile,
    int ZoneBytes,
    int CompressedBytes,
    int OutputBytes,
    string Backend,
    bool NativeLevel6,
    bool RoundTripVerified);

/// <summary>
/// Central Xbox fastfile wrapper used by Linker V1.
/// The preferred single-zlib backend is classic zlib compress2(level=6), which is
/// the byte-stream contract independently observed on the known-working Aztec load.ff.
/// If zlib1.dll is unavailable, the writer falls back to .NET ZLibStream so generic
/// maps are never coupled to the existence of a golden reference or a native DLL.
/// </summary>
public static class T4XboxFastFileWriter
{
    private static readonly byte[] UnsignedXboxHeader =
        Encoding.ASCII.GetBytes("IWffu100").Concat(new byte[] { 0x00, 0x00, 0x01, 0x83 }).ToArray();

    public static async Task<T4XboxSingleZlibWriteResult> WriteUnsignedSingleZlibAsync(
        string outputPath,
        ReadOnlyMemory<byte> zone,
        bool preferNativeLevel6 = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        byte[] compressed;
        string backend;
        var native = false;

        if (preferNativeLevel6 && NativeZlibLevel6.TryCompress(zone.Span, out var nativeBytes, out var nativeBackend))
        {
            compressed = nativeBytes;
            backend = nativeBackend;
            native = true;
        }
        else
        {
            await using var ms = new MemoryStream();
            await using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                await zlib.WriteAsync(zone, cancellationToken);
            compressed = ms.ToArray();
            backend = "dotnet-ZLibStream-Optimal";
        }

        await using (var output = File.Create(outputPath))
        {
            await output.WriteAsync(UnsignedXboxHeader, cancellationToken);
            await output.WriteAsync(compressed, cancellationToken);
        }

        var verifyPath = Path.Combine(Path.GetTempPath(), "WaW360Converter", "linker-zlib", Guid.NewGuid().ToString("N") + ".zone");
        Directory.CreateDirectory(Path.GetDirectoryName(verifyPath)!);
        var verified = false;
        try
        {
            var decomp = await FastFileDecompressor.DecompressAutoAsync(outputPath, verifyPath, cancellationToken);
            var readBack = await File.ReadAllBytesAsync(verifyPath, cancellationToken);
            verified = decomp.Platform == WaWPlatform.Xbox360 &&
                       decomp.Compression == WaWCompressionKind.XboxSingleZlib &&
                       readBack.AsSpan().SequenceEqual(zone.Span);
        }
        finally
        {
            try { if (File.Exists(verifyPath)) File.Delete(verifyPath); } catch { }
        }

        if (!verified)
            throw new InvalidDataException("Deterministic Xbox single-zlib writer did not round-trip to the original zone bytes.");

        return new T4XboxSingleZlibWriteResult(
            outputPath,
            zone.Length,
            compressed.Length,
            checked(UnsignedXboxHeader.Length + compressed.Length),
            backend,
            native,
            true);
    }

    private static class NativeZlibLevel6
    {
        private const int ZOk = 0;
        private const int Level = 6;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint CompressBoundDelegate(uint sourceLen);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Compress2Delegate(
            IntPtr dest,
            ref uint destLen,
            IntPtr source,
            uint sourceLen,
            int level);

        public static bool TryCompress(ReadOnlySpan<byte> source, out byte[] compressed, out string backend)
        {
            compressed = Array.Empty<byte>();
            backend = string.Empty;
            if (source.Length > uint.MaxValue) return false;

            foreach (var candidate in CandidateLibraries())
            {
                if (!TryLoad(candidate, out var handle)) continue;
                try
                {
                    if (!NativeLibrary.TryGetExport(handle, "compressBound", out var boundPtr) ||
                        !NativeLibrary.TryGetExport(handle, "compress2", out var compressPtr))
                        continue;

                    var compressBound = Marshal.GetDelegateForFunctionPointer<CompressBoundDelegate>(boundPtr);
                    var compress2 = Marshal.GetDelegateForFunctionPointer<Compress2Delegate>(compressPtr);
                    var src = source.ToArray();
                    var bound = compressBound(checked((uint)src.Length));
                    if (bound == 0 || bound > int.MaxValue) continue;
                    var dst = new byte[checked((int)bound)];
                    var dstLen = bound;

                    var srcHandle = GCHandle.Alloc(src, GCHandleType.Pinned);
                    var dstHandle = GCHandle.Alloc(dst, GCHandleType.Pinned);
                    try
                    {
                        var rc = compress2(
                            dstHandle.AddrOfPinnedObject(),
                            ref dstLen,
                            srcHandle.AddrOfPinnedObject(),
                            checked((uint)src.Length),
                            Level);
                        if (rc != ZOk || dstLen > dst.Length) continue;
                    }
                    finally
                    {
                        dstHandle.Free();
                        srcHandle.Free();
                    }

                    compressed = dst.AsSpan(0, checked((int)dstLen)).ToArray();
                    backend = $"native-zlib-level6:{Path.GetFileName(candidate)}";
                    return true;
                }
                catch
                {
                    // Try the next candidate. Native zlib is an optimization/oracle,
                    // never a hard dependency for generic conversion.
                }
                finally
                {
                    try { NativeLibrary.Free(handle); } catch { }
                }
            }
            return false;
        }

        private static bool TryLoad(string candidate, out IntPtr handle)
        {
            handle = IntPtr.Zero;
            try { return NativeLibrary.TryLoad(candidate, out handle); }
            catch { return false; }
        }

        private static IEnumerable<string> CandidateLibraries()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(List<string> list, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (seen.Add(value)) list.Add(value);
            }

            var list = new List<string>();
            Add(list, Path.Combine(AppContext.BaseDirectory, "zlib1.dll"));
            Add(list, "zlib1.dll");

            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(pf))
                Add(list, Path.Combine(pf, "Git", "mingw64", "bin", "zlib1.dll"));
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(pfx86))
                Add(list, Path.Combine(pfx86, "Git", "mingw64", "bin", "zlib1.dll"));

            return list;
        }
    }
}
