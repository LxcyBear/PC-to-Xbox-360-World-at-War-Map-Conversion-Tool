using System.Diagnostics;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4PcOatTechniqueSetIdentity(
    int AssetIndex,
    int OatPoolIndex,
    string Name);

public sealed class T4PcOatTechniqueSetIdentityResult
{
    public required string PcFastFile { get; init; }
    public required string UnlinkerExe { get; init; }
    public required int DeclaredPhysicalTechniqueSets { get; init; }
    public required int OatTechniqueSets { get; init; }
    public required IReadOnlyDictionary<int, T4PcOatTechniqueSetIdentity> ByAssetIndex { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
}

/// <summary>
/// Diagnostic-only TechniqueSet identity recovery for full PC T4 zones.
///
/// The tiny-load Material/TechniqueSet walker cannot safely map full-main TEMP logical
/// offsets back to physical serialized offsets. OAT already performs the real semantic
/// load, so for this A/B diagnostic we recover only the final TechniqueSet identity
/// strings from OAT's loaded asset pool.
///
/// Fail-closed contract:
/// - source must parse as PC/little-endian;
/// - OAT must exit successfully;
/// - OAT TechniqueSet count must exactly equal the physical top-level TechniqueSet count;
/// - TechniqueSet names must be non-empty and unique;
/// - only then is per-type insertion order zipped to the ordered physical roots.
///
/// This is identity evidence only. It is not shader/pass/Xenos graph conversion and must
/// never be treated as production TechniqueSet closure.
/// </summary>
public static class T4PcOatTechniqueSetIdentityCatalog
{
    private static readonly HashSet<string> OatPcTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "xmodelpieces", "physpreset", "physconstraints", "destructibledef",
        "xanim", "xmodel", "material", "techniqueset", "image", "sound",
        "loadedsound", "clipmap", "clipmap_pvs", "comworld", "gameworldsp",
        "gameworldmp", "mapents", "gfxworld", "lightdef", "uimap", "font",
        "menulist", "menu", "localize", "weapon", "snddriverglobals", "fx",
        "impactfx", "aitype", "mptype", "character", "xmodelalias", "rawfile",
        "stringtable", "packindex"
    };

    private sealed record OatIdentity(int PoolIndex, string Type, string Name);

    public static async Task<T4PcOatTechniqueSetIdentityResult> BuildAsync(
        string pcFastFile,
        string? explicitUnlinkerExe = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pcFastFile))
            throw new FileNotFoundException("PC source fastfile not found.", pcFastFile);

        var cached = await FastFileAnalysisCache.GetZoneAsync(pcFastFile, cancellationToken);
        if (cached.Platform != WaWPlatform.PC || cached.Document.Endianness != Endian.Little)
            throw new InvalidDataException(
                $"TechniqueSet OAT identity recovery expects PC/little-endian source; parsed {cached.Platform}/{cached.Document.Endianness}.");

        var physical = cached.Document.Assets
            .Where(a => a.TypeName.Equals("techset", StringComparison.OrdinalIgnoreCase) &&
                        a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared)
            .OrderBy(a => a.Index)
            .ToList();

        if (physical.Count == 0)
        {
            return new T4PcOatTechniqueSetIdentityResult
            {
                PcFastFile = Path.GetFullPath(pcFastFile),
                UnlinkerExe = ResolveUnlinker(explicitUnlinkerExe),
                DeclaredPhysicalTechniqueSets = 0,
                OatTechniqueSets = 0,
                ByAssetIndex = new Dictionary<int, T4PcOatTechniqueSetIdentity>(),
                Verified = true,
                Notes = new[] { "No physical TechniqueSet roots are present in this source." }
            };
        }

        var unlinker = ResolveUnlinker(explicitUnlinkerExe);
        var listed = await RunOatListAsync(unlinker, pcFastFile, cancellationToken);
        var techniques = listed
            .Where(x => x.Type.Equals("techniqueset", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var notes = new List<string>
        {
            $"OAT semantic pool listed {techniques.Count:N0} TechniqueSet identity row(s) for {physical.Count:N0} physical top-level TechniqueSet root(s)."
        };

        if (techniques.Count != physical.Count)
        {
            notes.Add("Count mismatch; no root/name mapping accepted.");
            return Failure(pcFastFile, unlinker, physical.Count, techniques.Count, notes);
        }

        if (techniques.Any(x => string.IsNullOrWhiteSpace(x.Name)))
        {
            notes.Add("One or more OAT TechniqueSet names are empty; no mapping accepted.");
            return Failure(pcFastFile, unlinker, physical.Count, techniques.Count, notes);
        }

        var duplicateNames = techniques
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Take(8)
            .ToList();
        if (duplicateNames.Count != 0)
        {
            notes.Add("Duplicate OAT TechniqueSet identities are ambiguous: " + string.Join(", ", duplicateNames));
            return Failure(pcFastFile, unlinker, physical.Count, techniques.Count, notes);
        }

        var byIndex = new Dictionary<int, T4PcOatTechniqueSetIdentity>();
        for (var i = 0; i < physical.Count; i++)
        {
            byIndex[physical[i].Index] = new T4PcOatTechniqueSetIdentity(
                physical[i].Index,
                techniques[i].PoolIndex,
                techniques[i].Name);
        }

        notes.Add(
            $"Identity mapping accepted for diagnostic A/B only: {byIndex.Count:N0}/{physical.Count:N0} TechniqueSet roots mapped by exact per-type OAT insertion order.");
        notes.Add("No OAT/donor payload bytes are copied; only TechniqueSet name strings are used.");

        return new T4PcOatTechniqueSetIdentityResult
        {
            PcFastFile = Path.GetFullPath(pcFastFile),
            UnlinkerExe = unlinker,
            DeclaredPhysicalTechniqueSets = physical.Count,
            OatTechniqueSets = techniques.Count,
            ByAssetIndex = byIndex,
            Verified = true,
            Notes = notes
        };
    }

    private static T4PcOatTechniqueSetIdentityResult Failure(
        string pcFastFile,
        string unlinker,
        int declared,
        int oat,
        IReadOnlyList<string> notes) =>
        new()
        {
            PcFastFile = Path.GetFullPath(pcFastFile),
            UnlinkerExe = unlinker,
            DeclaredPhysicalTechniqueSets = declared,
            OatTechniqueSets = oat,
            ByAssetIndex = new Dictionary<int, T4PcOatTechniqueSetIdentity>(),
            Verified = false,
            Notes = notes
        };

    private static async Task<List<OatIdentity>> RunOatListAsync(
        string unlinkerExe,
        string fastFile,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = unlinkerExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(fastFile)) ?? Environment.CurrentDirectory
        };
        psi.ArgumentList.Add("--list");
        psi.ArgumentList.Add("--no-color");
        psi.ArgumentList.Add(Path.GetFullPath(fastFile));

        using var process = new Process { StartInfo = psi };
        var stdoutTask = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrTask = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) stdoutTask.TrySetResult(stdout.ToString());
            else stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) stderrTask.TrySetResult(stderr.ToString());
            else stderr.AppendLine(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start OAT Unlinker: {unlinkerExe}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        process.WaitForExit();

        var stdoutText = stdoutTask.Task.IsCompleted ? await stdoutTask.Task : stdout.ToString();
        var stderrText = stderrTask.Task.IsCompleted ? await stderrTask.Task : stderr.ToString();

        if (process.ExitCode != 0)
            throw new InvalidDataException(
                $"OAT Unlinker exited with code {process.ExitCode} while recovering TechniqueSet identities.");

        var stdoutList = ParseAssetList(stdoutText).ToList();
        var stderrList = ParseAssetList(stderrText).ToList();
        var selected = stdoutList.Count >= stderrList.Count ? stdoutList : stderrList;

        return selected
            .Select((x, i) => new OatIdentity(i, x.Type, x.Name))
            .ToList();
    }

    private static IEnumerable<(string Type, string Name)> ParseAssetList(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = StripLogPrefix(line.Trim());
            var comma = trimmed.IndexOf(',');
            if (comma <= 0) continue;

            var type = trimmed[..comma].Trim();
            if (!OatPcTypes.Contains(type)) continue;

            yield return (type, trimmed[(comma + 1)..].Trim());
        }
    }

    private static string StripLogPrefix(string line)
    {
        if (line.StartsWith("[", StringComparison.Ordinal))
        {
            var close = line.IndexOf(']');
            if (close >= 0 && close + 1 < line.Length)
                return line[(close + 1)..].TrimStart();
        }

        var colon = line.IndexOf(':');
        if (colon > 0 && colon < 12)
        {
            var prefix = line[..colon];
            if (prefix.All(ch => char.IsLetter(ch) || char.IsWhiteSpace(ch)))
                return line[(colon + 1)..].TrimStart();
        }

        return line;
    }

    private static string ResolveUnlinker(string? explicitPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitPath))
            candidates.Add(explicitPath);

        var env = Environment.GetEnvironmentVariable("WAW360_OAT_UNLINKER");
        if (!string.IsNullOrWhiteSpace(env))
            candidates.Add(env);

        foreach (var root in CandidateRoots())
        {
            candidates.Add(Path.Combine(root, ".deps", "OpenAssetTools-xenon-prebuilt", "Release_x86", "Unlinker.exe"));
            candidates.Add(Path.Combine(root, ".deps", "OpenAssetTools-xenon", "build", "bin", "Release_x86", "Unlinker.exe"));
            candidates.Add(Path.Combine(root, "oat", "build", "bin", "Release_x86", "Unlinker.exe"));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full)) return full;
            }
            catch
            {
            }
        }

        throw new FileNotFoundException(
            "Pinned OAT Unlinker.exe was not found for TechniqueSet identity recovery.");
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
            {
                if (seen.Add(dir.FullName))
                    yield return dir.FullName;
            }
        }
    }
}
