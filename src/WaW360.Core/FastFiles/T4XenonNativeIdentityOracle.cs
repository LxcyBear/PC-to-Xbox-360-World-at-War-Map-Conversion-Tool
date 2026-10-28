using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed record T4XenonNativeIdentityEntry(
    int Index,
    int TypeId,
    string TypeName,
    string OatTypeName,
    string Name);

public sealed record T4XenonOatExternalReference(
    string OatTypeName,
    string Name);

public sealed class T4XenonIdentityGateException : Exception
{
    public T4XenonIdentityGateException(
        string message,
        int loadedAssets,
        int reconciledRoots,
        int seenExternalReferences,
        int expectedExternalReferences)
        : base(message)
    {
        LoadedAssets = loadedAssets;
        ReconciledRoots = reconciledRoots;
        SeenExternalReferences = seenExternalReferences;
        ExpectedExternalReferences = expectedExternalReferences;
    }

    public int LoadedAssets { get; }
    public int ReconciledRoots { get; }
    public int SeenExternalReferences { get; }
    public int ExpectedExternalReferences { get; }
}

public sealed record T4XenonNativeIdentityTypeSummary(
    int TypeId,
    string TypeName,
    string OatTypeName,
    int Count,
    int UniqueNames,
    int DuplicateIdentities);

public sealed class T4XenonNativeIdentityOracleResult
{
    public required string NativeFastFile { get; init; }
    public required string UnlinkerExe { get; init; }
    public required int Assets { get; init; }
    public required int LoadedAssets { get; init; }
    public required int ExternalReferenceAssets { get; init; }
    public required int ScriptStrings { get; init; }
    public required string IdentitySequenceSha256 { get; init; }
    public required bool TypeSequenceVerified { get; init; }
    public required int DuplicateIdentityCount { get; init; }
    public required IReadOnlyList<T4XenonNativeIdentityEntry> Entries { get; init; }
    public required IReadOnlyList<T4XenonNativeIdentityTypeSummary> Types { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonManifestFile { get; init; }
    public required string RawUnlinkerLogFile { get; init; }
}

/// <summary>
/// Uses the pinned Xenon OpenAssetTools Unlinker as the authoritative loader/fixup oracle
/// for a known-working T4 Xbox 360 fastfile. OAT's T4Xenon loader performs the real
/// big-endian per-asset load traversal, then --list prints every resolved asset name in
/// ZoneAssetPools insertion order.
///
/// This class does not copy donor payload bytes into converter output. It extracts identity
/// evidence only: exact root order, Xbox type, and resolved native asset name.
/// </summary>
public static class T4XenonNativeIdentityOracle
{
    private static readonly string[] OatXboxTypeNames =
    {
        "xmodelpieces",
        "physpreset",
        "physconstraints",
        "destructibledef",
        "xanim",
        "xmodel",
        "material",
        "pixelshader",
        "techniqueset",
        "image",
        "sound",
        "loadedsound",
        "clipmap_unused",
        "clipmap",
        "comworld",
        "gameworldsp",
        "gameworldmp",
        "mapents",
        "gfxworld",
        "lightdef",
        "uimap",
        "font",
        "menulist",
        "menu",
        "localize",
        "weapon",
        "snddriverglobals",
        "fx",
        "impactfx",
        "aitype",
        "mptype",
        "character",
        "xmodelalias",
        "rawfile",
        "stringtable",
        "packindex"
    };

    public static async Task<T4XenonNativeIdentityOracleResult> RunAsync(
        string nativeFastFile,
        string outputDirectory,
        string? explicitUnlinkerExe = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<T4XenonNativeIdentityEntry>? expectedRootIdentity = null,
        IReadOnlyCollection<T4XenonOatExternalReference>? allowedExternalReferences = null)
    {
        if (!File.Exists(nativeFastFile))
            throw new FileNotFoundException("Native Xbox fastfile not found.", nativeFastFile);

        var native = await FastFileAnalysisCache.GetZoneAsync(nativeFastFile, cancellationToken);
        if (native.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException(
                $"Native identity oracle requires an Xbox360 fastfile; parsed {native.Platform}.");

        var unlinker = ResolveUnlinker(explicitUnlinkerExe);
        Directory.CreateDirectory(outputDirectory);

        var stem = Path.GetFileNameWithoutExtension(nativeFastFile);
        var rawLog = Path.Combine(outputDirectory, stem + "_oat_xenon_list.log");
        var jsonPath = Path.Combine(outputDirectory, stem + "_native_identity_manifest.json");
        var txtPath = Path.Combine(outputDirectory, stem + "_native_identity_manifest.txt");

        var psi = new ProcessStartInfo
        {
            FileName = unlinker,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(nativeFastFile)) ?? Environment.CurrentDirectory
        };
        psi.ArgumentList.Add("--list");
        psi.ArgumentList.Add("--no-color");
        psi.ArgumentList.Add(Path.GetFullPath(nativeFastFile));

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start Xenon OAT Unlinker: {unlinker}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
        // Ensure asynchronous stdout/stderr event handlers have drained before parsing.
        process.WaitForExit();

        var combined = new StringBuilder();
        combined.AppendLine("STDOUT");
        combined.Append(stdout);
        combined.AppendLine();
        combined.AppendLine("STDERR");
        combined.Append(stderr);
        await File.WriteAllTextAsync(rawLog, combined.ToString(), new UTF8Encoding(false), cancellationToken);

        if (process.ExitCode != 0)
        {
            var tail = string.Join(
                " | ",
                combined.ToString()
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .TakeLast(16)
                    .Select(x => x.Trim()));

            throw new InvalidDataException(
                $"Xenon OAT Unlinker exited with code {process.ExitCode}. See {rawLog}" +
                (string.IsNullOrWhiteSpace(tail) ? "" : " Tail: " + tail));
        }

        var stdoutList = ParseAssetList(stdout.ToString()).ToList();
        var stderrList = ParseAssetList(stderr.ToString()).ToList();
        var listed = SelectPrimaryAssetList(
            stdoutList,
            stderrList,
            native.Document.AssetCount);

        var externalRows = new List<(string Type, string Name)>();
        List<(string Type, string Name)> rootRows;

        if (expectedRootIdentity is not null)
        {
            if (expectedRootIdentity.Count != native.Document.AssetCount)
                throw new InvalidDataException(
                    $"Expected root identity count {expectedRootIdentity.Count:N0} != candidate XAsset table {native.Document.AssetCount:N0}.");

            var allowed = (allowedExternalReferences ?? Array.Empty<T4XenonOatExternalReference>())
                .Select(x => (Type: x.OatTypeName, x.Name))
                .ToList();

            if (!TryReconcileExpectedRoots(
                    listed,
                    expectedRootIdentity,
                    allowed,
                    out rootRows,
                    out externalRows,
                    out var reconciledRootCount,
                    out var seenExternalCount,
                    out var reconciliationError))
            {
                throw new T4XenonIdentityGateException(
                    $"OAT Xenon root/reference reconciliation failed: {reconciliationError}. " +
                    $"loaded={listed.Count:N0}, roots={reconciledRootCount:N0}/{native.Document.AssetCount:N0}, " +
                    $"external={seenExternalCount:N0}/{allowed.Count:N0}. See {rawLog}",
                    listed.Count,
                    reconciledRootCount,
                    seenExternalCount,
                    allowed.Count);
            }
        }
        else
        {
            if (listed.Count != native.Document.AssetCount)
                throw new InvalidDataException(
                    $"OAT Xenon listed {listed.Count:N0} assets but the native XAsset table declares " +
                    $"{native.Document.AssetCount:N0}. See {rawLog}");
            rootRows = listed;
        }

        var entries = new List<T4XenonNativeIdentityEntry>(rootRows.Count);
        var typeSequenceVerified = true;
        for (var i = 0; i < rootRows.Count; i++)
        {
            var expected = native.Document.Assets[i];
            var expectedOat = OatTypeName(expected.TypeId);
            var row = rootRows[i];

            if (!row.Type.Equals(expectedOat, StringComparison.OrdinalIgnoreCase))
            {
                typeSequenceVerified = false;
                throw new InvalidDataException(
                    $"OAT/native root type mismatch at index {i:N0}: OAT='{row.Type}', " +
                    $"native typeId={expected.TypeId} expectedOAT='{expectedOat}' " +
                    $"converter='{expected.TypeName}'. See {rawLog}");
            }

            if (expectedRootIdentity is not null)
            {
                var expectedIdentity = expectedRootIdentity[i];
                if (expectedIdentity.TypeId != expected.TypeId ||
                    !row.Name.Equals(expectedIdentity.Name, StringComparison.Ordinal))
                {
                    typeSequenceVerified = false;
                    throw new InvalidDataException(
                        $"OAT/native root identity mismatch at index {i:N0}: " +
                        $"OAT='{row.Type}:{row.Name}', expected='{expectedIdentity.OatTypeName}:{expectedIdentity.Name}'. See {rawLog}");
                }
            }

            entries.Add(new T4XenonNativeIdentityEntry(
                i,
                expected.TypeId,
                expected.TypeName,
                row.Type,
                row.Name));
        }

        var duplicateIdentities = entries
            .GroupBy(e => (e.TypeId, e.Name), new TypeNameIdentityComparer())
            .Sum(g => Math.Max(0, g.Count() - 1));

        var types = entries
            .GroupBy(e => e.TypeId)
            .OrderBy(g => g.Key)
            .Select(g => new T4XenonNativeIdentityTypeSummary(
                g.Key,
                T4AssetTypeCatalog.GetName(g.Key, WaWPlatform.Xbox360),
                OatTypeName(g.Key),
                g.Count(),
                g.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count(),
                g.GroupBy(x => x.Name, StringComparer.Ordinal).Sum(x => Math.Max(0, x.Count() - 1))))
            .ToList();

        var identityHash = HashIdentitySequence(entries);

        var manifest = new
        {
            format = "WaW360.T4Xenon.NativeIdentityManifest.v1",
            nativeFastFile = Path.GetFullPath(nativeFastFile),
            unlinkerExe = unlinker,
            assets = entries.Count,
            loadedAssets = listed.Count,
            externalReferenceAssets = externalRows.Count,
            scriptStrings = native.Document.ScriptStringCount,
            typeSequenceVerified,
            duplicateIdentityCount = duplicateIdentities,
            identitySequenceSha256 = identityHash,
            types,
            entries
        };
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);

        var sb = new StringBuilder(128 * 1024);
        sb.AppendLine("WaW360Converter — T4 XENON NATIVE IDENTITY ORACLE");
        sb.AppendLine(new string('=', 92));
        sb.AppendLine($"Native FF: {Path.GetFullPath(nativeFastFile)}");
        sb.AppendLine($"OAT Unlinker: {unlinker}");
        sb.AppendLine($"Top-level roots: {entries.Count:N0}");
        sb.AppendLine($"OAT loaded assets: {listed.Count:N0}");
        sb.AppendLine($"External reference assets: {externalRows.Count:N0}");
        sb.AppendLine($"ScriptStrings: {native.Document.ScriptStringCount:N0}");
        sb.AppendLine($"Type sequence verified root-for-root: {typeSequenceVerified}");
        sb.AppendLine($"Duplicate (type,name) identities: {duplicateIdentities:N0}");
        sb.AppendLine($"Identity-sequence SHA-256: {identityHash}");
        sb.AppendLine();
        sb.AppendLine("TYPE COUNTS");
        foreach (var type in types)
            sb.AppendLine(
                $"  {type.TypeId,2} {type.TypeName,-18} OAT={type.OatTypeName,-16} " +
                $"count={type.Count,6:N0} unique={type.UniqueNames,6:N0} duplicate={type.DuplicateIdentities,5:N0}");
        sb.AppendLine();
        sb.AppendLine("NATIVE ROOT IDENTITY SEQUENCE");
        foreach (var e in entries)
            sb.AppendLine($"  [{e.Index,5}] type={e.TypeId,2}/{e.TypeName,-18} name={Escape(e.Name)}");

        await File.WriteAllTextAsync(txtPath, sb.ToString(), new UTF8Encoding(false), cancellationToken);

        return new T4XenonNativeIdentityOracleResult
        {
            NativeFastFile = Path.GetFullPath(nativeFastFile),
            UnlinkerExe = unlinker,
            Assets = entries.Count,
            LoadedAssets = listed.Count,
            ExternalReferenceAssets = externalRows.Count,
            ScriptStrings = native.Document.ScriptStringCount,
            IdentitySequenceSha256 = identityHash,
            TypeSequenceVerified = typeSequenceVerified,
            DuplicateIdentityCount = duplicateIdentities,
            Entries = entries,
            Types = types,
            TextReportFile = txtPath,
            JsonManifestFile = jsonPath,
            RawUnlinkerLogFile = rawLog
        };
    }

    private static List<(string Type, string Name)> SelectPrimaryAssetList(
        IReadOnlyList<(string Type, string Name)> stdout,
        IReadOnlyList<(string Type, string Name)> stderr,
        int rootCount)
    {
        if (stdout.Count >= rootCount && stderr.Count < rootCount)
            return stdout.ToList();
        if (stderr.Count >= rootCount && stdout.Count < rootCount)
            return stderr.ToList();
        if (stdout.Count == rootCount)
            return stdout.ToList();
        if (stderr.Count == rootCount)
            return stderr.ToList();

        // OAT normally emits asset listings on one stream. If both streams have
        // asset rows, prefer the larger stream rather than concatenating and
        // accidentally duplicating identities.
        return stdout.Count >= stderr.Count
            ? stdout.ToList()
            : stderr.ToList();
    }

    private static bool TryReconcileExpectedRoots(
        IReadOnlyList<(string Type, string Name)> listed,
        IReadOnlyList<T4XenonNativeIdentityEntry> expectedRoots,
        IReadOnlyList<(string Type, string Name)> allowedExternalReferences,
        out List<(string Type, string Name)> rootRows,
        out List<(string Type, string Name)> externalRows,
        out int reconciledRootCount,
        out int seenExternalCount,
        out string error)
    {
        rootRows = new List<(string Type, string Name)>(expectedRoots.Count);
        externalRows = new List<(string Type, string Name)>();
        reconciledRootCount = 0;
        seenExternalCount = 0;

        var allowed = allowedExternalReferences
            .GroupBy(
                x => NormalizeIdentityKey(x.Type, x.Name),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Count(),
                StringComparer.OrdinalIgnoreCase);
        var seenExternal = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var expectedIndex = 0;
        foreach (var row in listed)
        {
            if (expectedIndex < expectedRoots.Count)
            {
                var expected = expectedRoots[expectedIndex];
                if (row.Type.Equals(expected.OatTypeName, StringComparison.OrdinalIgnoreCase) &&
                    row.Name.Equals(expected.Name, StringComparison.Ordinal))
                {
                    rootRows.Add(row);
                    expectedIndex++;
                    reconciledRootCount = expectedIndex;
                    continue;
                }
            }

            var key = NormalizeIdentityKey(row.Type, row.Name);
            if (allowed.TryGetValue(key, out var allowedCount))
            {
                seenExternal.TryGetValue(key, out var seenCount);
                if (seenCount >= allowedCount)
                {
                    error =
                        $"external reference occurrence overflow '{row.Type}:{row.Name}': " +
                        $"seen={seenCount + 1}, expected={allowedCount}";
                    return false;
                }

                seenExternal[key] = seenCount + 1;
                externalRows.Add(row);
                seenExternalCount = externalRows.Count;
                continue;
            }

            var expectedText = expectedIndex < expectedRoots.Count
                ? $"{expectedRoots[expectedIndex].OatTypeName}:{expectedRoots[expectedIndex].Name}"
                : "<end-of-roots>";
            error =
                $"unexpected loaded asset '{row.Type}:{row.Name}' while expecting root #{expectedIndex:N0} '{expectedText}'";
            return false;
        }

        if (expectedIndex != expectedRoots.Count)
        {
            error =
                $"missing root identities: reconciled {expectedIndex:N0}/{expectedRoots.Count:N0}";
            return false;
        }

        var expectedExternalTotal = allowed.Values.Sum();
        if (externalRows.Count != expectedExternalTotal)
        {
            var missing = allowed
                .Select(pair =>
                {
                    seenExternal.TryGetValue(pair.Key, out var seen);
                    return (pair.Key, Missing: pair.Value - seen);
                })
                .Where(x => x.Missing > 0)
                .Take(16)
                .Select(x => $"{x.Key} x{x.Missing}")
                .ToArray();

            error =
                $"external reference multiset mismatch: OAT={externalRows.Count:N0}, " +
                $"expected={expectedExternalTotal:N0}, missing=[{string.Join(" | ", missing)}]";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string NormalizeIdentityKey(string type, string name)
    {
        var normalizedName = name.Trim();
        while (normalizedName.StartsWith(",", StringComparison.Ordinal))
            normalizedName = normalizedName[1..];

        return type.Trim().ToLowerInvariant() + "\n" + normalizedName;
    }

    private static IEnumerable<(string Type, string Name)> ParseAssetList(string text)
    {
        var validTypes = OatXboxTypeNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = StripLogPrefix(line.Trim());
            var comma = trimmed.IndexOf(',');
            if (comma <= 0) continue;

            var type = trimmed[..comma].Trim();
            if (!validTypes.Contains(type)) continue;

            var name = trimmed[(comma + 1)..].Trim();
            // Empty names are meaningful evidence too; do not silently drop them.
            yield return (type, name);
        }
    }

    private static string StripLogPrefix(string line)
    {
        // OAT logging formats have changed over time. Handle common "[info] " and
        // "info: " prefixes without depending on one exact logger version.
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
            catch { }
        }

        throw new FileNotFoundException(
            "Pinned Xenon OAT Unlinker.exe was not found. Build it with " +
            "powershell -ExecutionPolicy Bypass -File .\\tools\\oat-xenon\\Prepare-OatXenon.ps1 -Build, " +
            "or pass the explicit Unlinker.exe path as the final command argument.");
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

    private static string OatTypeName(int typeId)
    {
        if (typeId < 0 || typeId >= OatXboxTypeNames.Length)
            throw new ArgumentOutOfRangeException(nameof(typeId), typeId, null);
        return OatXboxTypeNames[typeId];
    }

    private static string HashIdentitySequence(IReadOnlyList<T4XenonNativeIdentityEntry> entries)
    {
        using var ms = new MemoryStream();
        foreach (var e in entries)
        {
            var line = Encoding.UTF8.GetBytes($"{e.Index}\t{e.TypeId}\t{e.Name}\n");
            ms.Write(line);
        }
        return Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    private sealed class TypeNameIdentityComparer : IEqualityComparer<(int TypeId, string Name)>
    {
        public bool Equals((int TypeId, string Name) x, (int TypeId, string Name) y) =>
            x.TypeId == y.TypeId && string.Equals(x.Name, y.Name, StringComparison.Ordinal);

        public int GetHashCode((int TypeId, string Name) obj) =>
            HashCode.Combine(obj.TypeId, StringComparer.Ordinal.GetHashCode(obj.Name));
    }
}
