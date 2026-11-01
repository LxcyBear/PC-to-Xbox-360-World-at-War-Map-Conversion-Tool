using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.Maps;

// Recipe source: WaW_PC_to_Xbox360_codxe_porting_knowledge.txt
// Part 1, Step 1 and Part 2, sections 0, 3 and 16.
// Read-only first stage. This is a DETERMINISTIC INPUT PRECEDENCE INDEX,
// not yet the script-payload rewriter or a complete asset dependency closure.
public sealed record RecipeIwdArchive(
    string Path,
    string FileName,
    int Order,
    int Entries,
    long ArchiveBytes,
    string? Error);

public sealed record RecipeIwdEntry(
    string VirtualPath,
    string Category,
    string ArchivePath,
    long UncompressedBytes,
    long CompressedBytes,
    int ArchiveOrder,
    bool WinningLooseScript,
    string PrecedenceDecision);

public sealed record RecipeIwdScriptResolution(
    string VirtualPath,
    string WinnerArchive,
    IReadOnlyList<string> OverriddenArchives,
    string Policy);

public sealed class NormalMapRecipeIwdPrecedenceResult
{
    public required string MapName { get; init; }
    public required string RuleSource { get; init; }
    public required IReadOnlyList<RecipeIwdArchive> Archives { get; init; }
    public required IReadOnlyList<RecipeIwdEntry> Entries { get; init; }
    public required IReadOnlyList<RecipeIwdScriptResolution> ScriptPrecedence { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required int ScriptPathCollisions { get; init; }
    public required int ScriptWinners { get; init; }
    public required bool PrecedenceIndexed { get; init; }
    public required bool AppliedToXenonPayload { get; init; }
    public required string JsonReportFile { get; init; }
    public required string TextReportFile { get; init; }
}

public static class NormalMapRecipeIwdPrecedence
{
    private sealed record Candidate(
        string VirtualPath,
        string Category,
        string ArchivePath,
        long Size,
        long PackedSize,
        int ArchiveOrder,
        int EntryOrder);

    public static async Task<NormalMapRecipeIwdPrecedenceResult> BuildAsync(
        NormalMapAnalysisResult analysis,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var outputDir = Path.Combine(outputRoot, analysis.MapName, "recipe_source_precedence");
        Directory.CreateDirectory(outputDir);
        var json = Path.Combine(outputDir, analysis.MapName + "_recipe_iwd_precedence.json");
        var txt = Path.Combine(outputDir, analysis.MapName + "_recipe_iwd_precedence.txt");

        // An IWD is a ZIP. Index names and sizes only, without decompressing,
        // extracting, or changing the source or an FF. The alphabetically later
        // IWD wins for duplicate script paths, even if the FF has that script.
        var inputs = analysis.InputFiles
            .Where(f => f.Role.Equals("iwd", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var archives = new List<RecipeIwdArchive>();
        var candidates = new List<Candidate>();
        var errors = new List<string>();
        for (var rank = 0; rank < inputs.Count; rank++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = inputs[rank];
            var read = 0;
            try
            {
                using var zip = ZipFile.OpenRead(input.Path);
                var entryOrder = 0;
                foreach (var entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var original = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (original.EndsWith("/", StringComparison.Ordinal)) continue;
                    var parts = original.Split('/');
                    if (parts.Length == 0 ||
                        parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.Contains(':')))
                    {
                        errors.Add("Invalid virtual path (ZIP traversal/absolute): " + input.Path + " => " + entry.FullName);
                        continue;
                    }

                    var name = string.Join("/", parts);
                    var extension = Path.GetExtension(name);
                    var category = extension.ToLowerInvariant() switch
                    {
                        ".gsc" or ".csc" or ".atr" or ".cfg" or ".txt" => "script/raw",
                        ".csv" => "stringtable",
                        ".iwi" => "texture",
                        ".wav" or ".mp3" or ".xma" => "sound",
                        ".str" => "localizedstring",
                        _ when name.StartsWith("weapons/", StringComparison.OrdinalIgnoreCase) => "weapon",
                        _ => "source/other"
                    };
                    candidates.Add(new Candidate(
                        name, category, input.Path, entry.Length,
                        entry.CompressedLength, rank, entryOrder++));
                    read++;
                }
                archives.Add(new RecipeIwdArchive(
                    input.Path, Path.GetFileName(input.Path), rank, read, input.SizeBytes, null));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                errors.Add("IWD archive could not be indexed: " + input.Path + " : " + ex.Message);
                archives.Add(new RecipeIwdArchive(
                    input.Path, Path.GetFileName(input.Path), rank, read, input.SizeBytes, ex.Message));
            }
        }

        var winningIndices = new HashSet<(int ArchiveOrder, int EntryOrder)>();
        var scriptWinners = new List<RecipeIwdScriptResolution>();
        // Only actual GSC/CSC script bodies override their compiled FF
        // RawFile. Config, text and ATR files are inventory inputs; marking
        // them as loose script overrides would overstate conversion coverage.
        var scriptGroups = candidates
            .Where(x => x.Category == "script/raw" &&
                (x.VirtualPath.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) ||
                 x.VirtualPath.EndsWith(".csc", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(x => x.VirtualPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var collisions = 0;
        foreach (var group in scriptGroups)
        {
            var sources = group
                .OrderByDescending(x => x.ArchiveOrder)
                .ThenByDescending(x => x.EntryOrder)
                .ToList();
            var winner = sources[0];
            winningIndices.Add((winner.ArchiveOrder, winner.EntryOrder));
            if (sources.Count > 1) collisions++;
            scriptWinners.Add(new RecipeIwdScriptResolution(
                group.Key,
                winner.ArchivePath,
                sources.Skip(1).Select(x => x.ArchivePath).ToList(),
                "Loose IWD script wins over the FF version; later alphabetical IWD overrides earlier IWD."));
        }

        var entries = candidates
            .OrderBy(x => x.ArchiveOrder)
            .ThenBy(x => x.EntryOrder)
            .Select(x =>
            {
                var selected = winningIndices.Contains((x.ArchiveOrder, x.EntryOrder));
                var eligible = x.Category == "script/raw" &&
                    (x.VirtualPath.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) ||
                     x.VirtualPath.EndsWith(".csc", StringComparison.OrdinalIgnoreCase));
                var decision = eligible
                    ? selected ? "LOOSE_SCRIPT_WINNER_OVERRIDES_FF" : "SUPERSEDED_LOOSE_SCRIPT"
                    : "SOURCE_ASSET_CANDIDATE_NOT_YET_RESOLVED";
                return new RecipeIwdEntry(
                    x.VirtualPath, x.Category, x.ArchivePath, x.Size,
                    x.PackedSize, x.ArchiveOrder, selected, decision);
            }).ToList();

        var result = new NormalMapRecipeIwdPrecedenceResult
        {
            MapName = analysis.MapName,
            RuleSource = "WaW_PC_to_Xbox360_codxe_porting_knowledge.txt: Part 1 Step 1; Part 2 rules 0, 3, 16",
            Archives = archives,
            Entries = entries,
            ScriptPrecedence = scriptWinners,
            Errors = errors,
            ScriptPathCollisions = collisions,
            ScriptWinners = scriptWinners.Count,
            PrecedenceIndexed = errors.Count == 0,
            AppliedToXenonPayload = false,
            JsonReportFile = json,
            TextReportFile = txt
        };
        await File.WriteAllTextAsync(json,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false), cancellationToken);

        var report = new StringBuilder();
        report.AppendLine("RECIPE STEP 1 - IWD SOURCE AND LOOSE SCRIPT PRECEDENCE");
        report.AppendLine("Map: " + analysis.MapName);
        report.AppendLine("Policy: own map/patch/mod/IWD before stock sources; IWD script beats FF script;");
        report.AppendLine("        alphabetically later map IWD beats earlier IWD (within same virtual path).");
        report.AppendLine($"IWDs={archives.Count}; entries={entries.Count}; unique loose scripts={scriptWinners.Count}; script collisions={collisions}; errors={errors.Count}");
        report.AppendLine("IMPORTANT: this indexes winners only. Linker has NOT YET injected IWD script bodies into the Xbox FF.");
        foreach (var archive in archives)
            report.AppendLine($"IWD #{archive.Order + 1} {archive.FileName}: {archive.Entries} entries; path={archive.Path}; error={archive.Error ?? "none"}");
        foreach (var s in scriptWinners.Where(x => x.OverriddenArchives.Count > 0))
            report.AppendLine($"OVERRIDE {s.VirtualPath}: WIN={s.WinnerArchive}; SUPERSEDES={string.Join("; ", s.OverriddenArchives)}");
        foreach (var e in errors) report.AppendLine("BLOCKER " + e);
        await File.WriteAllTextAsync(txt, report.ToString(), new UTF8Encoding(false), cancellationToken);
        return result;
    }
}
