using System.ComponentModel;
using System.Text.RegularExpressions;
using WaW360.Core.Configuration;
using WaW360.Core.Diagnostics;
using WaW360.Core.FastFiles;
using WaW360.Core.Maps;

namespace WaW360.Gui;

public sealed partial class MainForm : Form
{
    private ConverterSettings _settings = new();
    private T4ZoneDocument? _lastZoneDocument;
    private T4PayloadScanResult? _lastPayloadScan;
    private readonly Dictionary<string, List<MapTestIssue>> _mapTestIssues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToolTip _mapIssueToolTip = new()
    {
        InitialDelay = 250,
        ReshowDelay = 100,
        AutoPopDelay = 15000,
        ShowAlways = true
    };
    private ListViewItem? _issueHoverItem;

    private sealed record MapTestIssue(string Stage, string Description);

    public MainForm()
    {
        InitializeComponent();

        // The WinForms designer instantiates the form. Keep file-system/settings work
        // out of design-time so MainForm renders reliably in Visual Studio.
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
            return;

        _settings = SettingsStore.Load();
        ApplySettingsToUi();
        WireRuntimeEvents();
    }

    private void ApplySettingsToUi()
    {
        _mapSource.Text = MapDiscovery.ResolveConfiguredPcMapsFolder(_settings.MapSourceFolder);
        _settings.MapSourceFolder = _mapSource.Text;
        _pcGame.Text = _settings.PcGameFolder;
        _xboxGame.Text = _settings.XboxGameFolder;
        _codxe.Text = _settings.CodxeFolder;
        _output.Text = string.IsNullOrWhiteSpace(_settings.OutputFolder) ? ConverterSettings.DefaultOutputFolder : _settings.OutputFolder;
        _memoryBudget.Value = Math.Clamp(_settings.MemoryBudgetMb, (int)_memoryBudget.Minimum, (int)_memoryBudget.Maximum);
        _parallelMaps.Value = Math.Clamp(_settings.MaxParallelMaps, (int)_parallelMaps.Minimum, (int)_parallelMaps.Maximum);
        _batchMode.SelectedItem = string.Equals(_settings.BatchMode, "Safe Mode (Full Validation)", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(_settings.BatchMode, "Full Regression", StringComparison.OrdinalIgnoreCase)
            ? "Safe Mode (Full Validation)" : "Fast Mode";
        if (_batchMode.SelectedIndex < 0) _batchMode.SelectedIndex = 0;
        _runBatchTestsButton.Text = IsSafeModeSelected()
            ? "Run checked Normal maps - SAFE full validation"
            : "Run checked Normal maps - FAST mode";
        ConfigureAnalysisCache();
        UpdateMapsHint();
    }

    private void WireRuntimeEvents()
    {
        _openFastFileMenuItem.Click += (_, _) => BrowseFastFile();
        _exitMenuItem.Click += (_, _) => Close();
        _refreshMapsMenuItem.Click += (_, _) => RefreshMaps();
        _aboutMenuItem.Click += (_, _) => MessageBox.Show(
            "Linker V2 is now the active Fast Mode architecture branch.\n\n" +
            "Fast Mode treats every PC FF/IWD as source-archive provenance, builds a canonical package graph, selects the canonical load FF by map identity, and refuses to mirror PC container boundaries into Xbox output. The old Stage 4a/5/10 proof marathon is retained only under Safe Mode (Full Validation) while its proven serializers are absorbed into deterministic V2 walkers. Simpsons and Aztec are validation oracles, never conversion dependencies.",
            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

        _refreshMapsButton.Click += (_, _) => RefreshMaps();
        _normalMapAnalyzeButton.Click += async (_, _) => await AnalyzeSelectedNormalMapAsync();
        _normalMapBuildButton.Click += async (_, _) => await BuildSelectedNormalMapXboxAsync();
        _stockResolveButton.Click += async (_, _) => await ResolveSelectedNormalMapStockDonorsAsync();
        _xanimProofButton.Click += async (_, _) => await ProveSelectedNormalMapXAnimAsync();
        _xmodelProofButton.Click += async (_, _) => await ProveSelectedNormalMapXModelAsync();
        _weaponProofButton.Click += async (_, _) => await ProveSelectedNormalMapFxAsync();
        _checkAllNormalButton.Click += (_, _) => CheckAllNormalMaps();
        _runBatchTestsButton.Click += async (_, _) => await RunCheckedNormalMapTestSuiteAsync();
        _batchMode.SelectedIndexChanged += (_, _) =>
        {
            _settings.BatchMode = _batchMode.SelectedItem?.ToString() ?? "Fast Mode";
            _runBatchTestsButton.Text = IsSafeModeSelected()
                ? "Run checked Normal maps - SAFE full validation"
                : "Run checked Normal maps - FAST mode";
        };
        _clearAnalysisCacheButton.Click += async (_, _) => await ClearAnalysisCacheAsync();
        _maps.MouseMove += Maps_MouseMove;
        _maps.MouseLeave += (_, _) => HideMapIssueToolTip();
        _maps.DoubleClick += (_, _) =>
        {
            if (_maps.SelectedItems.Count == 0) return;
            if (_maps.SelectedItems[0].Tag is not MapCandidate map) return;
            var path = map.MapFastFile ?? map.ModFastFile;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                _inputFastFile.Text = path;
                _tabs.SelectedTab = _inspectTab;
            }
        };

        _browseFastFileButton.Click += (_, _) => BrowseFastFile();
        _inspectButton.Click += async (_, _) => await InspectSelectedAsync();
        _decompressButton.Click += async (_, _) => await DecompressSelectedAsync();
        _compareButton.Click += async (_, _) => await CompareSelectedAsync();
        _analyzeAssetsButton.Click += async (_, _) => await AnalyzeAssetsAsync();
        _exportManifestButton.Click += async (_, _) => await ExportManifestAsync();
        _menuProbeButton.Click += async (_, _) => await MenuProbeAsync();
        _roundTripButton.Click += async (_, _) => await RoundTripFastFileAsync();
        _resolvePayloadsButton.Click += async (_, _) => await ResolvePayloadsAsync();
        _showMapstableButton.Click += (_, _) => ShowMapstable();
        _menuStructuresButton.Click += async (_, _) => await ProbeMenuStructuresAsync();
        _traceCustomMapsButton.Click += async (_, _) => await TraceCustomMapsAsync();
        _stage3LabelProofButton.Click += async (_, _) => await CreateStage3LabelProofAsync();
        _stage3AddLxcyButton.Click += async (_, _) => await RunStage3DualPointerRunAuditAsync();

        _browseMapSourceButton.Click += (_, _) =>
        {
            BrowseFolderInto(_mapSource);
            UpdateMapsHint();
            RefreshMaps();
        };
        _browsePcGameButton.Click += (_, _) => BrowseFolderInto(_pcGame);
        _browseXboxGameButton.Click += (_, _) => BrowseFolderInto(_xboxGame);
        _browseCodxeButton.Click += (_, _) => BrowseFolderInto(_codxe);
        _browseOutputButton.Click += (_, _) => BrowseFolderInto(_output);
        _importSettingsButton.Click += (_, _) => ImportIni();
        _saveSettingsButton.Click += (_, _) =>
        {
            SaveSettingsFromUi();
            SetStatus("Settings saved.");
        };

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        Shown += (_, _) => RefreshMaps();
        FormClosing += (_, _) => SaveSettingsFromUi();
        FormClosed += (_, _) => _mapIssueToolTip.Dispose();
    }

    private bool IsSafeModeSelected() =>
        string.Equals(_batchMode.SelectedItem?.ToString(), "Safe Mode (Full Validation)", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_batchMode.SelectedItem?.ToString(), "Full Regression", StringComparison.OrdinalIgnoreCase);

    private void ConfigureAnalysisCache()
    {
        var outputRoot = string.IsNullOrWhiteSpace(_output.Text)
            ? ConverterSettings.DefaultOutputFolder
            : _output.Text.Trim();
        FastFileAnalysisCache.Configure(outputRoot, (int)_memoryBudget.Value);
        var fastMode = !IsSafeModeSelected();
        FastFileAnalysisCache.SetDerivedCachePolicy(persistentReads: fastMode, clearMemory: false);
    }

    private async Task ClearAnalysisCacheAsync()
    {
        try
        {
            SaveSettingsFromUi();
            SetBusy(true, "Clearing analysis cache...");
            BatchStageCache.ClearStageEntries(_settings.OutputFolder);
            NormalMapAnalyzer.ClearMemoryCache();
            await FastFileAnalysisCache.ClearAsync();
            Log($"Analysis caches cleared: FF cache={FastFileAnalysisCache.CacheRoot}; stage cache={Path.Combine(_settings.OutputFolder, "_analysis_cache", "stage_results")}");
            SetStatus("Analysis cache cleared.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private void BrowseFolderInto(TextBox box)
    {
        using var dialog = new FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(box.Text) ? box.Text : string.Empty
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            box.Text = dialog.SelectedPath;
    }

    private void UpdateMapsHint()
    {
        var source = MapDiscovery.ResolveConfiguredPcMapsFolder(_mapSource.Text.Trim());
        var roots = MapDiscovery.GetListingRoots(source);
        _mapsHint.Text = roots.Count == 0
            ? "PC map source folders are not configured."
            : $"Scanning PC Maps + CoDWaW mods: {string.Join("  |  ", roots)}";
    }

    private void RefreshMaps()
    {
        _maps.BeginUpdate();
        _maps.Items.Clear();
        try
        {
            var source = MapDiscovery.ResolveConfiguredPcMapsFolder(_mapSource.Text.Trim());
            if (!string.Equals(source, _mapSource.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _mapSource.Text = source;
                _settings.MapSourceFolder = source;
            }
            UpdateMapsHint();
            LoadLatestBatchIssues();
            var roots = MapDiscovery.GetListingRoots(source);
            var candidates = MapDiscovery.DiscoverSourceFolders(roots);
            foreach (var map in candidates)
            {
                var item = new ListViewItem(map.Name)
                {
                    Checked = false,
                    Tag = map,
                    UseItemStyleForSubItems = false
                };
                var issues = GetMapIssues(map.Name);
                var issueCell = item.SubItems.Add(issues.Count == 0 ? string.Empty : issues.Count == 1 ? "⚠" : $"⚠ {issues.Count}");
                if (issues.Count > 0)
                {
                    issueCell.ForeColor = Color.DarkOrange;
                    issueCell.Font = new Font(_maps.Font, FontStyle.Bold);
                }
                item.SubItems.Add(map.Profile);
                item.SubItems.Add(map.Tags);
                item.SubItems.Add(map.DisplayLocation);
                if (issues.Count == 0 && map.ProfileSignals.Count > 0)
                    item.ToolTipText = string.Join(Environment.NewLine, map.ProfileSignals.Take(8));
                _maps.Items.Add(item);
            }
            var existingRoots = roots.Count(Directory.Exists);
            SetStatus($"Found {candidates.Count} map candidate(s) across {existingRoots}/{roots.Count} source root(s).");
            Log($"Map scan: {candidates.Count} candidates from {string.Join(" | ", roots)}.");
            foreach (var map in candidates.Where(m => !m.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase)))
                Log($"  {map.Name}: detected {map.Profile} ({string.Join("; ", map.ProfileSignals.Take(3))})");
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            SetStatus("Map scan failed.");
        }
        finally
        {
            _maps.EndUpdate();
        }
    }

    private string ResolveOutputRoot() => string.IsNullOrWhiteSpace(_output.Text)
        ? ConverterSettings.DefaultOutputFolder
        : _output.Text.Trim();

    private IReadOnlyList<MapTestIssue> GetMapIssues(string mapName) =>
        _mapTestIssues.TryGetValue(mapName, out var issues) ? issues : Array.Empty<MapTestIssue>();

    private void ClearMapIssues(string mapName) => _mapTestIssues.Remove(mapName);

    private void UpsertMapIssue(string mapName, string stage, string description)
    {
        if (!_mapTestIssues.TryGetValue(mapName, out var issues))
        {
            issues = new List<MapTestIssue>();
            _mapTestIssues[mapName] = issues;
        }
        issues.RemoveAll(x => x.Stage.Equals(stage, StringComparison.OrdinalIgnoreCase));
        issues.Add(new MapTestIssue(stage, description));
    }

    private void RemoveMapIssue(string mapName, string stage)
    {
        if (!_mapTestIssues.TryGetValue(mapName, out var issues)) return;
        issues.RemoveAll(x => x.Stage.Equals(stage, StringComparison.OrdinalIgnoreCase));
        if (issues.Count == 0) _mapTestIssues.Remove(mapName);
    }

    private void LoadLatestBatchIssues()
    {
        _mapTestIssues.Clear();
        try
        {
            var batchDir = Path.Combine(ResolveOutputRoot(), "_batch_reports");
            if (!Directory.Exists(batchDir)) return;
            var latest = Directory.EnumerateFiles(batchDir, "stage4_*_batch_*.txt", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (latest is null) return;

            string? mapName = null;
            string? failedStage = null;
            foreach (var raw in File.ReadLines(latest))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("MAP: ", StringComparison.Ordinal))
                {
                    mapName = line[5..].Trim();
                    failedStage = null;
                    continue;
                }
                if (line.StartsWith("[FAIL] ", StringComparison.Ordinal))
                {
                    failedStage = line[7..].Trim();
                    continue;
                }
                if (mapName is null || failedStage is null) continue;
                if (!raw.StartsWith("  ", StringComparison.Ordinal)) continue;
                var details = line.Trim();
                if (details.Length == 0 || details.StartsWith("elapsed=", StringComparison.OrdinalIgnoreCase) || details.Equals("exception:", StringComparison.OrdinalIgnoreCase))
                    continue;
                UpsertMapIssue(mapName, failedStage, SummarizeBatchIssue(failedStage, details));
                failedStage = null;
            }
        }
        catch
        {
            // Issue badges are advisory only; a damaged/locked old report must never block map discovery.
        }
    }

    private static string SummarizeBatchIssue(string stage, string details)
    {
        var xmodel = Regex.Match(details, @"XModel discovery mismatch: XAssetList declares ([\d,]+), typed top-level scan resolved ([\d,]+)", RegexOptions.IgnoreCase);
        if (xmodel.Success && int.TryParse(xmodel.Groups[1].Value.Replace(",", string.Empty), out var declaredModel) && int.TryParse(xmodel.Groups[2].Value.Replace(",", string.Empty), out var foundModel))
            return $"XModel discovery over-detected by {Math.Max(0, foundModel - declaredModel):N0}: table declares {declaredModel:N0}, raw scan found {foundModel:N0}.";

        var xmodelPacked = Regex.Match(details, @"XModel aligned discovery incomplete:.*?matched ([\d,]+), missing ([\d,]+), skipped ([\d,]+).*?Packed-name bridge candidates=([\d,]+), matchedPacked=([\d,]+)", RegexOptions.IgnoreCase);
        if (xmodelPacked.Success)
            return $"XModel order alignment still has {xmodelPacked.Groups[2].Value} unmatched body/bodies; packed-name bridge found {xmodelPacked.Groups[4].Value} bounded candidate(s), {xmodelPacked.Groups[5].Value} selected.";

        var aligned = Regex.Match(details, @"XModel aligned discovery incomplete:.*?matched ([\d,]+), missing ([\d,]+), skipped ([\d,]+)", RegexOptions.IgnoreCase);
        if (aligned.Success)
            return $"XModel order alignment still has {aligned.Groups[2].Value} unmatched body/bodies; {aligned.Groups[3].Value} false-positive candidate(s) were skipped.";

        var xanimAligned = Regex.Match(details, @"XAnim aligned discovery incomplete:.*?matched ([\d,]+), missing ([\d,]+).*?Packed-name bridge candidates=([\d,]+), matchedPacked=([\d,]+)", RegexOptions.IgnoreCase);
        if (xanimAligned.Success)
            return $"XAnim order alignment still has {xanimAligned.Groups[2].Value} unmatched body/bodies; packed-name bridge found {xanimAligned.Groups[3].Value} bounded candidate(s), {xanimAligned.Groups[4].Value} selected.";

        var xanim = Regex.Match(details, @"XAnim discovery mismatch: XAssetList declares ([\d,]+), typed scan resolved ([\d,]+)", RegexOptions.IgnoreCase);
        if (xanim.Success && int.TryParse(xanim.Groups[1].Value.Replace(",", string.Empty), out var declaredAnim) && int.TryParse(xanim.Groups[2].Value.Replace(",", string.Empty), out var foundAnim))
            return $"XAnim discovery is short by {Math.Max(0, declaredAnim - foundAnim):N0}: table declares {declaredAnim:N0}, scan resolved {foundAnim:N0}.";

        var oneLine = Regex.Replace(details.Replace('\r', ' ').Replace('\n', ' '), @"\s+", " ").Trim();
        if (oneLine.Length > 180) oneLine = oneLine[..177] + "...";
        return oneLine.Length == 0 ? $"{stage} failed." : oneLine;
    }

    private void UpdateMapIssueCell(string mapName)
    {
        foreach (ListViewItem item in _maps.Items)
        {
            if (item.Tag is not MapCandidate map || !map.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase)) continue;
            while (item.SubItems.Count < 2) item.SubItems.Add(string.Empty);
            var issues = GetMapIssues(mapName);
            var sub = item.SubItems[1];
            sub.Text = issues.Count == 0 ? string.Empty : issues.Count == 1 ? "⚠" : $"⚠ {issues.Count}";
            sub.ForeColor = issues.Count == 0 ? _maps.ForeColor : Color.DarkOrange;
            sub.Font = issues.Count == 0 ? _maps.Font : new Font(_maps.Font, FontStyle.Bold);
            item.ToolTipText = issues.Count == 0 && map.ProfileSignals.Count > 0
                ? string.Join(Environment.NewLine, map.ProfileSignals.Take(8))
                : string.Empty;
            break;
        }
    }

    private void Maps_MouseMove(object? sender, MouseEventArgs e)
    {
        var hit = _maps.HitTest(e.Location);
        if (hit.Item is null || hit.SubItem is null || hit.Item.SubItems.Count < 2 || !ReferenceEquals(hit.SubItem, hit.Item.SubItems[1]))
        {
            HideMapIssueToolTip();
            return;
        }
        if (hit.Item.Tag is not MapCandidate map) { HideMapIssueToolTip(); return; }
        var issues = GetMapIssues(map.Name);
        if (issues.Count == 0) { HideMapIssueToolTip(); return; }
        if (ReferenceEquals(_issueHoverItem, hit.Item)) return;

        _issueHoverItem = hit.Item;
        var text = "Current test issue(s):" + Environment.NewLine + string.Join(Environment.NewLine, issues.Select(x => $"• {x.Stage}: {x.Description}"));
        _mapIssueToolTip.Show(text, _maps, e.X + 16, e.Y + 18, 15000);
    }

    private void HideMapIssueToolTip()
    {
        if (_issueHoverItem is null) return;
        _mapIssueToolTip.Hide(_maps);
        _issueHoverItem = null;
    }

    private void CheckAllNormalMaps()
    {
        var checkedCount = 0;
        var skippedCount = 0;
        foreach (ListViewItem item in _maps.Items)
        {
            if (item.Tag is MapCandidate map && map.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            {
                item.Checked = true;
                checkedCount++;
            }
            else
            {
                item.Checked = false;
                skippedCount++;
            }
        }

        Log($"Batch selection: checked {checkedCount} Normal map(s); left {skippedCount} non-Normal map(s) unchecked.");
        SetStatus($"Checked {checkedCount} Normal map(s) for batch testing.");
    }

    private async Task RunCheckedNormalMapTestSuiteAsync()
    {
        var checkedItems = _maps.CheckedItems.Cast<ListViewItem>().ToList();
        var maps = checkedItems
            .Select(i => i.Tag as MapCandidate)
            .Where(m => m is not null && m.Profile.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            .Cast<MapCandidate>()
            .ToList();
        var skipped = checkedItems.Count - maps.Count;

        if (maps.Count == 0)
        {
            Log("BATCH: no checked Normal maps. Use 'Check all Normal' or check one or more Normal rows first.");
            SetStatus("No checked Normal maps for batch testing.");
            _tabs.SelectedTab = _logsTab;
            return;
        }

        SaveSettingsFromUi();
        var outputRoot = string.IsNullOrWhiteSpace(_output.Text) ? ConverterSettings.DefaultOutputFolder : _output.Text.Trim();
        var xboxRoot = string.IsNullOrWhiteSpace(_xboxGame.Text) ? null : _xboxGame.Text.Trim();
        var fastMode = !IsSafeModeSelected();
        var parallelMaps = fastMode ? Math.Clamp((int)_parallelMaps.Value, 1, 2) : 1;

        Directory.CreateDirectory(outputRoot);
        ConfigureAnalysisCache();
        FastFileAnalysisCache.SetDerivedCachePolicy(persistentReads: fastMode, clearMemory: !fastMode);
        FastFileAnalysisCache.ResetStats();

        // Linker V2 Fast Mode is intentionally compact. The legacy 35-stage proof/regression
        // pipeline now runs only in Safe Mode while its proven serializers are migrated into V2.
        if (fastMode)
        {
            await RunLinkerV2FastBatchAsync(maps, skipped, outputRoot);
            return;
        }

        var batchDir = Path.Combine(outputRoot, "_batch_reports");
        Directory.CreateDirectory(batchDir);
        var started = DateTime.Now;
        var reportPath = Path.Combine(batchDir, $"linker_v1_alpha4mp4_{started:yyyyMMdd_HHmmss}.txt");
        var report = new List<string>
        {
            "WaW360Converter — Linker V1 alpha 4MP4 all-Normal master diagnostics + optimized triage suite",
            new string('=', 62),
            $"Started: {started:O}",
            $"Mode: {(fastMode ? "Fast Mode" : "Safe Mode (Full Validation)")}",
            $"Parallel maps: {parallelMaps}",
            $"Maps: {maps.Count}",
            $"Skipped checked non-Normal rows: {skipped}",
            $"Output root: {outputRoot}",
            $"Shared FF cache (LocalAppData): {FastFileAnalysisCache.CacheRoot}",
            $"Fast-stage cache: {Path.Combine(outputRoot, "_analysis_cache", "stage_results")}",
            "",
            "Policy: proof PASS criteria are unchanged. Safe Mode (Full Validation) executes every proof/native stage fresh; Fast Mode may reuse only content-hash/version-matched PASS results whose output files still exist.",
            "Report policy: parallel maps execute concurrently, but each MAP block is committed atomically so issue badges cannot be misattributed.",
            "Stages: Analyze | LINKER V1 deterministic load graph | LINKER V1 alpha 2 Xbox load emitter/read-back | LINKER V1 alpha 3 reference-donor hardware candidate | LINKER V1 alpha 3b reference-loadscreen A/B proof | LINKER V1 alpha 3c deterministic zlib/oracle | LINKER V1 alpha 4 whole-map XAsset root plan | Xbox workspace | Stock donors | Weapon fixed | Weapon owned | XAnim | XModel | FX | Mixed XAsset order | Native write plan | Relocation preflight | Destination layout/rebase | Staging image read-back | Payload materialization | Native zone reconstruction | FF readiness/wrapper | foundation promotion/readiness/wrapper | Material+TechniqueSet block-coordinate retry | Incremental primary-FF promotion plan (10m packed-Localize chain proof) | Sound structural + silent-smoke serialization proof (10n) | RawFile occupied-span + packed-tail proof (10o) | TechniqueSet Xbox null-graph promotion (10p) | Material + simple-tail Xbox smoke promotion (10q) | World-root + integrated Xbox hardware-smoke emitter (10r) | Hardware smoke packaging gate | LINKER V1 alpha 4MP full-proof hybrid hardware emitter",
            ""
        };

        const int stagesPerMap = 35;
        var totalSteps = maps.Count * stagesPerMap;
        var completedSteps = 0;
        var passCount = 0;
        var failCount = 0;
        var cachedCount = 0;
        var reportGate = new SemaphoreSlim(1, 1);
        var mapGate = new SemaphoreSlim(parallelMaps, parallelMaps);
        var timingGate = new object();
        var timings = new Dictionary<string, List<(double Seconds, bool Cached)>>(StringComparer.OrdinalIgnoreCase);

        BeginBatchProgress(totalSteps, $"Batch test: 0/{totalSteps} stages complete...");
        Log("");
        Log($"================ BATCH START: {maps.Count} Normal map(s), {totalSteps} stage(s), {(fastMode ? "FAST MODE" : "SAFE MODE")} ================");
        if (skipped > 0) Log($"BATCH: skipped {skipped} checked row(s) because their profile hint is not Normal.");
        Log($"BATCH report: {reportPath}");
        Log($"BATCH output: {outputRoot}");
        Log($"BATCH shared FF cache: {FastFileAnalysisCache.CacheRoot}");
        Log($"BATCH stage-result cache: {Path.Combine(outputRoot, "_analysis_cache", "stage_results")}");
        _tabs.SelectedTab = _logsTab;

        async Task FlushReportAsync()
        {
            await reportGate.WaitAsync();
            try
            {
                var snapshot = report.ToArray();
                await File.WriteAllLinesAsync(reportPath, snapshot);
            }
            finally { reportGate.Release(); }
        }

        async Task ProcessMapAsync(MapCandidate map)
        {
            await mapGate.WaitAsync();
            try
            {
                var mapFingerprint = fastMode ? await BatchStageCache.ComputeMapFingerprintAsync(map) : string.Empty;
                ClearMapIssues(map.Name);
                UpdateMapIssueCell(map.Name);
                var mapReport = new List<string>
                {
                    $"MAP: {map.Name}",
                    new string('-', Math.Max(12, map.Name.Length + 5))
                };
                Log($"BATCH MAP: {map.Name} ({map.DisplayLocation})");

                async Task Stage(
                    string stageKey,
                    string stageVersion,
                    string name,
                    Func<Task<string>> action,
                    bool cacheable = true,
                    string contextFingerprint = "")
                {
                    SetStatus($"{map.Name}: {name} [{completedSteps + 1}/{totalSteps}]...");
                    BatchStageResult result;
                    if (fastMode && cacheable)
                    {
                        var cached = await BatchStageCache.TryGetAsync(
                            outputRoot, map.Name, stageKey, stageVersion, mapFingerprint, contextFingerprint);
                        if (cached is not null)
                        {
                            Log($"  -> {name}...");
                            Log($"     PASS (cached) {name}: {cached.Details}");
                            result = new BatchStageResult(true, cached.Details, TimeSpan.Zero, null, true);
                        }
                        else
                        {
                            var mapOutputRoot = Path.Combine(outputRoot, map.Name);
                            var beforeOutputs = BatchStageCache.SnapshotStageOutputs(mapOutputRoot);
                            result = await RunBatchStageAsync(map, name, action);
                            if (result.Passed)
                            {
                                var outputs = BatchStageCache.CaptureStageOutputs(mapOutputRoot, beforeOutputs);
                                await BatchStageCache.PutPassAsync(
                                    outputRoot, map.Name, stageKey, stageVersion, mapFingerprint,
                                    result.Details, outputs, contextFingerprint);
                            }
                        }
                    }
                    else
                    {
                        result = await RunBatchStageAsync(map, name, action);
                    }

                    completedSteps++;
                    if (result.Passed)
                    {
                        passCount++;
                        if (result.Cached) cachedCount++;
                        RemoveMapIssue(map.Name, name);
                    }
                    else
                    {
                        failCount++;
                        UpsertMapIssue(map.Name, name, SummarizeBatchIssue(name, result.Details));
                    }
                    UpdateMapIssueCell(map.Name);

                    lock (timingGate)
                    {
                        if (!timings.TryGetValue(name, out var list)) timings[name] = list = new();
                        list.Add((result.Elapsed.TotalSeconds, result.Cached));
                    }

                    mapReport.Add($"[{(result.Passed ? (result.Cached ? "PASS CACHED" : "PASS") : "FAIL")}] {name}");
                    mapReport.Add($"  {result.Details.Replace(Environment.NewLine, Environment.NewLine + "  ")}");
                    mapReport.Add(result.Cached ? "  elapsed=0.0s (cached)" : $"  elapsed={result.Elapsed.TotalSeconds:F1}s");
                    if (!result.Passed && !string.IsNullOrWhiteSpace(result.StackTrace))
                    {
                        mapReport.Add("  exception:");
                        foreach (var line in result.StackTrace.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                            mapReport.Add("    " + line);
                    }
                    UpdateBatchProgress(completedSteps, totalSteps, $"{map.Name}: {name} [{completedSteps}/{totalSteps}]");
                }

                await Stage("analysis", "a1b", "Stage 4 analysis", async () =>
                {
                    var r = await NormalMapAnalyzer.AnalyzeAsync(map, outputRoot);
                    return $"profile={r.Profile}; fastfiles={r.FastFiles.Count}; IWDs={r.Iwds.Count}; assets={r.TotalAssetTypes.Values.Sum():N0}; report={r.TextReportFile}";
                });

                await Stage("linker-v1-load-graph", "linker-v1|walk:xasset-order|closure:image-promotion", "LINKER V1 deterministic load.ff graph", async () =>
                {
                    var r = await NormalMapLinkerV1Stage.RunAsync(map, outputRoot);
                    return $"loadFF={r.LoadFastFiles:N0}; roots={r.PcRootAssets:N0}; linked={r.LinkedAssets:N0}; promoted={r.PromotedNestedAssets:N0}; imageIWD={r.IwdImageDependencies:N0}; imageEmbedded={r.EmbeddedImageDependencies:N0}; donorPending={r.DonorImageDependenciesPending:N0}; exactWalk={r.DeterministicWalkVerified}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("linker-v1-alpha2-load-emit", "linker-v1-alpha2|graph:v1|xbox-load:emit-readback", "LINKER V1 alpha 2 Xbox load.ff emitter + read-back", async () =>
                {
                    var r = await NormalMapLinkerV1Emitter.RunAsync(map, outputRoot);
                    return $"loadFF={r.LoadFastFiles:N0}; emitted={r.EmittedFastFiles:N0}; smokeSub={r.SmokeSubstitutes:N0}; hardwareReady={r.HardwareReadyFastFiles:N0}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("linker-v1-alpha3-load-emit", "linker-v1-alpha3|graph:v1|reference-donor:defeat|xbox-load:emit-readback", "LINKER V1 alpha 3 reference-donor hardware candidate", async () =>
                {
                    var r = await NormalMapLinkerV1Alpha3Emitter.RunAsync(map, outputRoot);
                    return $"loadFF={r.LoadFastFiles:N0}; emitted={r.EmittedFastFiles:N0}; refDonor={r.ReferenceDonors:N0}; retailOriginUnproven={r.UnprovenRetailOrigins:N0}; hardwareReady={r.HardwareReadyFastFiles:N0}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("linker-v1-alpha3b-load-emit", "linker-v1-alpha3b|graph:v1|reference-donor:defeat|reference-loadscreen:aztec|zone-byte-exact", "LINKER V1 alpha 3b reference-loadscreen A/B proof", async () =>
                {
                    var r = await NormalMapLinkerV1Alpha3bEmitter.RunAsync(map, outputRoot);
                    return $"loadFF={r.LoadFastFiles:N0}; emitted={r.EmittedFastFiles:N0}; defeatRef={r.ReferenceDonors:N0}; loadscreenRef={r.ReferenceLoadscreens:N0}; zoneExact={r.ReferenceZoneExactFastFiles:N0}; abReady={r.HardwareReadyFastFiles:N0}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("linker-v1-alpha3c-zlib", "linker-v1-alpha3c|writer:zlib6-native-preferred|golden:optional", "LINKER V1 alpha 3c deterministic zlib + optional golden oracle", async () =>
                {
                    var r = await NormalMapLinkerV1Alpha3cEmitter.RunAsync(map, outputRoot);
                    return $"zones={r.InputZones:N0}; emitted={r.EmittedFastFiles:N0}; nativeLevel6={r.NativeLevel6FastFiles:N0}; ffExact={r.GoldenExactFastFiles:N0}; verified={r.Verified}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("linker-v1-alpha4-wholemap-roots", "linker-v1-alpha4|xasset-root-table|no-candidate-scan", "LINKER V1 alpha 4 whole-map deterministic XAsset root plan", async () =>
                {
                    var r = await NormalMapLinkerV1WholeMapStage.RunAsync(map, outputRoot);
                    return $"fastfiles={r.FastFileCount:N0}; roots={r.TotalRootAssets:N0}; physical={r.PhysicalRootAssets:N0}; aliases={r.AliasRootAssets:N0}; nulls={r.NullRootAssets:N0}; runs={r.RootRunCount:N0}; exactWalk={r.ExactRootWalkVerified}; closure={r.DependencyClosureComplete}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("xbox-workspace", "a2|analysis:a1b", "Stage 4a2 Xbox workspace", async () =>
                {
                    var r = await NormalMapXboxBuildAttempt.BuildAsync(map, outputRoot);
                    return $"proofFF={r.FastFiles.Count(x => x.ProofVerified)}/{r.FastFiles.Count}; critical={r.CriticalBlockers.Count}; high={r.HighBlockers.Count}; canEmit={r.CanEmitXboxMapFastFile}; report={r.TextReportFile}";
                });

                // Stock donor discovery depends on an external Xbox tree. Keep it live even in Fast Mode.
                await Stage("stock-donors", "a3", "Stage 4a3 stock donors", async () =>
                {
                    var r = await NormalMapStockResolutionAudit.RunAsync(map, outputRoot, xboxRoot);
                    return $"zones={r.XboxZonesScanned.Count}; XAnim donors={r.XAnimDonors.Count}; XModel donors={r.XModelDonors.Count}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("weapon-fixed", "a6f", "Stage 4a6f Weapon fixed proof", async () =>
                {
                    var r = await NormalMapWeaponSerializerProof.RunAsync(map, outputRoot);
                    return $"coverage={r.CoveredWeapon:N0}/{r.DeclaredWeapon:N0}; physical={r.SerializedWeapon:N0}; aliases={r.AliasWeapon:N0}; fixedBytes={r.SerializedFixedBytes:N0}; verified={r.Verified}; report={r.TextReportFile}";
                });

                await Stage("weapon-owned", "a7b|fixed:a6f", "Stage 4a7b Weapon owned proof", async () =>
                {
                    var r = await NormalMapWeaponVariableSerializerProof.RunAsync(map, outputRoot);
                    return $"coverage={r.CoveredWeapon:N0}/{r.DeclaredWeapon:N0}; physical={r.SerializedWeapon:N0}; aliases={r.AliasWeapon:N0}; bytes={r.SerializedBytes:N0}; owned={r.OwnedPayloadBytes:N0}; verified={r.Verified}; report={r.TextReportFile}";
                });

                await Stage("xanim", "a9g|weapon:a7b", "Stage 4a9j XAnim + bounded recovery", async () =>
                {
                    var r = await NormalMapXAnimSerializerProof.RunAsync(map, outputRoot);
                    return $"serialized={r.SerializedXAnim:N0}/{r.DeclaredXAnim:N0}; delta={r.DeltaXAnim:N0}; bytes={r.SerializedBytes:N0}; verified={r.Verified}; report={r.TextReportFile}";
                });

                await Stage("xmodel", "a9g|xanim:a9g|weapon:a7b", "Stage 4a9j XModel + bounded recovery", async () =>
                {
                    var r = await NormalMapXModelSerializerProof.RunAsync(map, outputRoot);
                    return $"serialized={r.SerializedXModel:N0}/{r.DeclaredXModel:N0}; bones={r.TotalBones:N0}; surfaces={r.TotalSurfaces:N0}; bytes={r.SerializedBytes:N0}; verified={r.Verified}; report={r.TextReportFile}";
                });

                await Stage("fx", "a9b|nested-xmodel:a9g", "Stage 4a9b FX zero-total reference proof", async () =>
                {
                    var r = await NormalMapFxSerializerProof.RunAsync(map, outputRoot);
                    return $"coverage={r.CoveredFx:N0}/{r.DeclaredFx:N0}; physical={r.SerializedFx:N0}; aliases={r.AliasFx:N0}; elems={r.ElemDefs:N0}; bytes={r.SerializedBytes:N0}; verified={r.Verified}; report={r.TextReportFile}";
                });

                await Stage("mixed-order", "a9a|xanim:a9g|weapon:a7b|xmodel:a9g|fx:a9b", "Stage 4a9a mixed XAsset-order probe", async () =>
                {
                    var r = await NormalMapMixedAssetOrderProbe.RunAsync(map, outputRoot);
                    return $"missingFX={r.MissingFx:N0}; tightFXWindows={r.TightFxWindows:N0}; missingXModel={r.MissingXModel:N0}; skippedXModel={r.SkippedXModel:N0}; report={r.TextReportFile}";
                });

                await Stage("native-write-plan", "a10a|xanim:a9g|weapon:a7b|xmodel:a9g|fx:a9b|mixed:a9a", "Stage 5a10a native Xbox write-plan foundation", async () =>
                {
                    var r = await NormalMapNativeWriterFoundation.RunAsync(map, outputRoot);
                    return $"planned={r.PlannedTrackedAssets:N0}; physical={r.PhysicalTrackedAssets:N0}; aliases={r.AliasTrackedAssets:N0}; payloadRefs={r.PlannedPayloadBytes:N0}B; unhandled={r.UnhandledAssets:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("relocation-preflight", "a10a|native:a10a", "Stage 5b10a relocation + rebasing preflight", async () =>
                {
                    var r = await NormalMapNativeRelocationAudit.RunAsync(map, outputRoot);
                    return $"records={r.PhysicalRecordsAudited:N0}; ptrFields={r.KnownPointerFieldsAudited:N0}; packed={r.PackedPointers:N0}; aliases={r.AliasPointers:N0}; invalid={r.InvalidPointers:N0}; nestedPending={r.NestedPointerAuditPending}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("destination-layout", "a10b|native:a10a|preflight:a10a", "Stage 5c10b destination block layout + pointer rebase", async () =>
                {
                    var r = await NormalMapNativeDestinationLayout.RunAsync(map, outputRoot);
                    return $"physical={r.PhysicalAssets:N0}; aliases={r.AliasAssets:N0}; uniqueTargets={r.UniquePackedTargets:N0}; patched={r.PatchedPackedPointers:N0}; nestedPending={r.NestedPayloadMaterializationPending}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("staging-emitter", "a10b|layout:a10b", "Stage 5d10b relocation staging image + independent read-back", async () =>
                {
                    var r = await NormalMapNativeStagingEmitter.RunAsync(map, outputRoot);
                    return $"assets={r.Assets:N0}; relocations={r.Relocations:N0}; patchedVerified={r.PatchedPointersVerified:N0}; readBackErrors={r.ReadBackErrors:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("payload-materialization", "a10c|layout:a10b|staging:a10b", "Stage 5e10c tracked payload materialization + read-back", async () =>
                {
                    var r = await NormalMapNativePayloadMaterializer.RunAsync(map, outputRoot);
                    return $"assets={r.Assets:N0}; physical={r.PhysicalAssets:N0}; payloadBytes={r.MaterializedPayloadBytes:N0}; patchedVerified={r.PatchedPointersVerified:N0}; sharedPending={r.PackedSharedTargetsPending:N0}; nestedPending={r.NestedPointerRebasePending}; readBackErrors={r.ReadBackErrors:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("zone-reconstruction", "a10c|payload:a10c|layout:a10b", "Stage 5f10c native T4 zone reconstruction + independent read-back", async () =>
                {
                    var r = await NormalMapNativeZoneReconstruction.RunAsync(map, outputRoot);
                    return $"assets={r.AssetCount:N0}; strings={r.ScriptStringCount:N0}; trackedPayloads={r.TrackedPhysicalPayloads:N0}; payloadBytes={r.TrackedPayloadBytes:N0}; unhandledNull={r.UnhandledAssetsSuppressed:N0}; scriptAliasesPending={r.ScriptStringAliasesPending:N0}; sharedPending={r.PackedSharedTargetsPending:N0}; nestedPending={r.NestedPointerRebasePending}; readBackErrors={r.ReadBackErrors:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("native-readiness", "a10d|zone:a10c", "Stage 5g10d hardware-readiness blocker inventory", async () =>
                {
                    var r = await NormalMapNativeReadinessInventory.RunAsync(map, outputRoot);
                    return $"unhandled={r.UnhandledNonNullAssets:N0}; blockerTypes={r.BlockersByType.Count:N0}; scriptAliases={r.ScriptStringAliasesPending:N0}; sharedPending={r.PackedSharedTargetsPending:N0}; nestedPending={r.NestedPointerRebasePending}; readyFF={r.HardwareReadyFastFiles:N0}/{r.FastFiles.Count:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("ff-emitter", "a10d|readiness:a10d|zone:a10c", "Stage 5h10d Xbox FF wrapper + compression gate", async () =>
                {
                    var r = await NormalMapNativeFastFileEmitter.RunAsync(map, outputRoot);
                    return $"ffprobe={r.ProbeFastFiles:N0}; roundTrip={r.ProbeRoundTripVerified:N0}/{r.ProbeFastFiles:N0}; hardwareFF={r.HardwareFastFilesEmitted:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("foundation-promotion", "a10e|zone:a10c|payload:a10c|layout:a10b", "Stage 5i10e RawFile + Localize + StringTable native promotion", async () =>
                {
                    var r = await NormalMapFoundationAssetPromotion.RunAsync(map, outputRoot);
                    return $"foundation={r.FoundationPhysicalPayloads:N0}; foundationBytes={r.FoundationPayloadBytes:N0}; promotedTypes={r.PromotedByType.Count:N0}; unresolvedFoundation={r.UnresolvedFoundationByType.Values.Sum():N0}; unhandledNull={r.UnhandledAssetsSuppressed:N0}; readBackErrors={r.ReadBackErrors:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("foundation-readiness", "a10e|promotion:a10e|preflight:a10a", "Stage 5j10e foundation-aware hardware readiness", async () =>
                {
                    var r = await NormalMapFoundationReadinessInventory.RunAsync(map, outputRoot);
                    return $"promoted={r.FoundationAssetsPromoted:N0}; unhandled={r.UnhandledNonNullAssets:N0}; blockerTypes={r.BlockersByType.Count:N0}; materialTechCandidates={r.MaterialTechniqueCandidateFastFiles:N0}; sharedPending={r.PackedSharedTargetsPending:N0}; nestedPending={r.NestedPointerRebasePending}; readyFF={r.HardwareReadyFastFiles:N0}/{r.FastFiles.Count:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("foundation-ff-emitter", "a10e|readiness:a10e|promotion:a10e", "Stage 5k10e foundation-aware Xbox FF wrapper gate", async () =>
                {
                    var r = await NormalMapFoundationFastFileEmitter.RunAsync(map, outputRoot);
                    return $"ffprobe={r.ProbeFastFiles:N0}; roundTrip={r.ProbeRoundTripVerified:N0}/{r.ProbeFastFiles:N0}; materialTechCandidates={r.MaterialTechniqueCandidateFastFiles:N0}; hardwareFF={r.HardwareFastFilesEmitted:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("material-techset-hardware-attempt", "a10j-block-coordinate-proof|readiness:a10e|promotion:a10e|ff:a10e", "Stage 5p10j block-coordinate-proof Material + TechniqueSet hardware-FF retry", async () =>
                {
                    var r = await NormalMapMaterialTechniqueHardwareAttempt.RunAsync(map, outputRoot);
                    return $"candidates={r.CandidateFastFiles:N0}; fullyConverted={r.FullyConvertedFastFiles:N0}; techniqueGraphSlotsPending={r.TechniqueGraphSlotsPending:N0}; hardwareFF={r.HardwareFastFilesEmitted:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("incremental-primary-promotion", "a10m|readiness:a10e|promotion:a10e|mt:a10j|scanner:a10m", "Stage 5q10m incremental primary-FF promotion plan", async () =>
                {
                    var r = await NormalMapIncrementalPrimaryPromotion.RunAsync(map, outputRoot);
                    return $"foundation={r.FoundationAssetsPromotable:N0}; materialPlanned={r.MaterialAssetsPlanned:N0}; techsetPlanned={r.TechniqueSetAssetsPlanned:N0}; techniqueGraphSlotsPending={r.TechniqueGraphSlotsPending:N0}; projectedUnhandled={r.ProjectedUnhandledNonNullAssets:N0}; projectedReadyFF={r.ProjectedHardwareReadyFastFiles:N0}/{r.FastFiles.Count:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("sound-smoke-promotion", "a10n|incremental:a10m|sound-layout:t4", "Stage 5r10n Sound structural + silent-smoke serialization proof", async () =>
                {
                    var r = await NormalMapSoundSmokePromotion.RunAsync(map, outputRoot);
                    return $"soundParsed={r.ParsedSoundAssets:N0}/{r.DeclaredSoundAssets:N0}; aliases={r.AliasEntries:N0}; loaded={r.LoadedSoundNodes:N0}; streamed={r.StreamedSoundNodes:N0}; primed={r.PrimedSoundNodes:N0}; sourceAudioSkipped={r.LoadedAudioBytesSkipped:N0}B; stubBytes={r.SilentStubPayloadBytes:N0}; readBackErrors={r.ReadBackErrors:N0}; projectedUnhandled={r.ProjectedUnhandledAfterSoundSmoke:N0}; projectedReadyFF={r.ProjectedHardwareReadyFastFiles:N0}/{r.FastFiles.Count:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("rawfile-occupied-span-promotion", "a10o|sound:a10n|raw-layout:t4", "Stage 5s10o RawFile occupied-span + packed-tail proof", async () =>
                {
                    var r = await NormalMapRawFileOccupiedSpanPromotion.RunAsync(map, outputRoot);
                    return $"rawStructural={r.StructuralRawFiles:N0}/{r.DeclaredRawFiles:N0}; nativePromotable={r.NativePromotableRawFiles:N0}; packedNamePending={r.PackedNameRawFilesPending:N0}; rejectedOccupied={r.RejectedOccupiedCandidates:N0}; rejectedWeakName={r.RejectedWeakNameCandidates:N0}; sourceBufferBytes={r.SourceBufferBytes:N0}; xboxPayloadBytes={r.XboxPayloadBytes:N0}; readBackErrors={r.ReadBackErrors:N0}; projectedUnhandled={r.ProjectedUnhandledAfterRawFile:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("techset-nullgraph-promotion", "a10p|rawfile:a10o|techset-layout:xbox212", "Stage 5t10p TechniqueSet Xbox null-graph payload promotion", async () =>
                {
                    var r = await NormalMapTechniqueSetNullGraphPromotion.RunAsync(map, outputRoot);
                    return $"techset={r.NativeSmokeTechniqueSets:N0}/{r.DeclaredTechniqueSets:N0}; xboxSlotsNulled={r.XboxTechniqueSlotsNulled:N0}; payloadBytes={r.XboxPayloadBytes:N0}; readBackErrors={r.ReadBackErrors:N0}; functionalShaderGraphsPending={r.FunctionalShaderGraphsPending:N0}; projectedUnhandled={r.ProjectedUnhandledAfterTechniqueSet:N0}; smokeTopLevelReadyFF={r.SmokeTopLevelReadyFastFiles:N0}/{r.FastFiles.Count:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("material-simple-tail-promotion", "a10q|techset:a10p|material:xbox96|simple-tail:smoke", "Stage 5u10q Material + simple-tail Xbox smoke promotion", async () =>
                {
                    var r = await NormalMapMaterialSimpleTailSmokePromotion.RunAsync(map, outputRoot);
                    return $"material={r.PromotedMaterials:N0}/{r.DeclaredMaterials:N0}; localizeFallback={r.PromotedPackedLocalizeFallbacks:N0}; rawPackedName={r.PromotedPackedNameRawFiles:N0}; comworld={r.PromotedComWorlds:N0}; gameworldSp={r.PromotedGameWorldSp:N0}; payloadBytes={r.XboxPayloadBytes:N0}; readBackErrors={r.ReadBackErrors:N0}; projectedUnhandled={r.ProjectedUnhandledAfterSimpleTail:N0}; sharedPending={r.StrictNestedSharedTargetsPending:N0}; nestedPending={r.StrictNestedRebasePending}; smokeTopLevelReadyFF={r.SmokeTopLevelReadyFastFiles:N0}/{r.FastFiles.Count:N0}; verified={r.Verified}; hardwareReady={r.HardwareReady}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("world-integrated-smoke-emitter", "a10r|world:reduced32|tracked:self-contained-shell|wrapper:xbox-block", "Stage 5v10r World-root + integrated Xbox hardware-smoke emitter", async () =>
                {
                    var r = await NormalMapWorldRootIntegratedSmokeEmitter.RunAsync(map, outputRoot);
                    return $"worldRoots={r.WorldRootsPromoted:N0}; smokePayloads={r.SmokePayloadAssets:N0}; legacySharedRetired={r.LegacyPackedSharedTargetsRetiredFromSmokePath:N0}; missing={r.MissingPhysicalPayloads:N0}; readBackErrors={r.ReadBackErrors:N0}; wrapperRoundTrip={r.WrapperRoundTripVerifiedFastFiles:N0}/{r.FastFileCount:N0}; smokeFF={r.SmokeHardwareFastFilesEmitted:N0}/{r.FastFileCount:N0}; COMPLETE_SMOKE_SET={r.CompleteSmokeSet}; verified={r.Verified}; strictFidelity={r.StrictFidelityReady}; folder={r.HardwareCandidateRoot}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("hardware-smoke-package", "linker-v1-fasttrack|source:a10r|metadata:safe", "LINKER V1 hardware smoke packaging gate", async () =>
                {
                    var r = await NormalMapHardwareSmokePackager.RunAsync(map, outputRoot);
                    return $"ff={r.FastFilesCopied:N0}; metadata={r.MetadataFilesCopied:N0}; IWDs={r.SourceIwdCount:N0}; COMPLETE_SMOKE_SET={r.CompleteSmokeSet}; DROP_IN_SMOKE_TEST_READY={r.DropInSmokeTestReady}; folder={r.PackageFolder}; report={r.TextReportFile}";
                }, cacheable: false);

                await Stage("linker-v1-alpha4mp-minplay", "linker-v1-alpha4mp4|family-triage-reuse|master-dataset|load-compat-skip|rawfile-partial-continuation|deployment-assets|xbox-single-zlib6|one-file-diag", "LINKER V1 alpha 4MP4 minimum-playable + optimized tracked-family triage emitter", async () =>
                {
                    var r = await NormalMapMinimumPlayableHybridEmitter.RunAsync(map, outputRoot);
                    var sizes = string.Join(", ", r.FastFiles.Select(ff =>
                    {
                        var sourceBytes = File.Exists(ff.Source) ? new FileInfo(ff.Source).Length : 0;
                        var outputBytes = ff.HardwareFastFile is not null && File.Exists(ff.HardwareFastFile) ? new FileInfo(ff.HardwareFastFile).Length : 0;
                        var ratio = sourceBytes > 0 ? 100.0 * outputBytes / sourceBytes : 0.0;
                        var check = outputBytes <= 12 ? "REVIEW_EMPTY_OR_HEADER_ONLY" : sourceBytes >= 1024L * 1024L && outputBytes < 256L * 1024L ? "REVIEW_TOO_SMALL" : "OK";
                        return $"{Path.GetFileName(ff.Source)}={outputBytes:N0}B/{outputBytes / (1024.0 * 1024.0):F2}MiB ({ratio:F1}% src,{check})";
                    }));
                    return $"worldRoots={r.WorldRootsPromoted:N0}; hybridPayloads={r.SmokePayloadAssets:N0}; missing={r.MissingPhysicalPayloads:N0}; readBackErrors={r.ReadBackErrors:N0}; wrapperRoundTrip={r.WrapperRoundTripVerifiedFastFiles:N0}/{r.FastFileCount:N0}; hybridFF={r.SmokeHardwareFastFilesEmitted:N0}/{r.FastFileCount:N0}; COMPLETE_HYBRID_SET={r.CompleteSmokeSet}; verified={r.Verified}; strictFidelity={r.StrictFidelityReady}; sizes=[{sizes}]; folder={r.HardwareCandidateRoot}; report={r.TextReportFile}";
                }, cacheable: false);

                mapReport.Add("");
                await reportGate.WaitAsync();
                try
                {
                    report.AddRange(mapReport);
                    await File.WriteAllLinesAsync(reportPath, report.ToArray());
                }
                finally
                {
                    reportGate.Release();
                }
            }
            finally
            {
                mapGate.Release();
            }
        }

        try
        {
            var tasks = maps.Select(ProcessMapAsync).ToArray();
            await Task.WhenAll(tasks);
        }
        finally
        {
            var ended = DateTime.Now;
            var cacheStats = FastFileAnalysisCache.GetStats();
            report.Add(new string('=', 62));
            report.Add($"Finished: {ended:O}");
            report.Add($"Elapsed: {(ended - started)}");
            report.Add($"Stage results: PASS={passCount}; FAIL={failCount}; TOTAL={passCount + failCount}; CACHED_PASS={cachedCount}");
            report.Add("");
            report.Add("TIMING TELEMETRY");
            foreach (var kv in timings.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var executed = kv.Value.Where(x => !x.Cached).ToList();
                var cached = kv.Value.Count(x => x.Cached);
                var seconds = executed.Sum(x => x.Seconds);
                var avg = executed.Count == 0 ? 0 : seconds / executed.Count;
                report.Add($"  {kv.Key}: executed={executed.Count}; cached={cached}; total={seconds:F1}s; avg={avg:F1}s");
            }
            report.Add("");
            report.Add("SHARED FF ANALYSIS CACHE");
            report.Add($"  memory hits: {cacheStats.MemoryHits:N0}");
            report.Add($"  persistent hits: {cacheStats.PersistentHits:N0}");
            report.Add($"  decompression misses: {cacheStats.Misses:N0}");
            report.Add($"  memory: {cacheStats.MemoryBytes / (1024.0 * 1024.0):F1}/{cacheStats.MemoryBudgetBytes / (1024.0 * 1024.0):F0} MB");
            report.Add($"  decompressed bytes avoided: {cacheStats.DecompressedBytesAvoided / (1024.0 * 1024.0):F1} MB");
            report.Add($"  reused derived candidate scans (memory): {cacheStats.DerivedHits:N0}");
            report.Add($"  reused derived candidate scans (persistent): {cacheStats.DerivedPersistentHits:N0}");
            await FlushReportAsync();

            foreach (var map in maps)
            {
                try
                {
                    var oneFile = await NormalMapConsolidatedDiagnostics.WriteAsync(map, outputRoot, report, started, fastMode);
                    Log($"ONE-FILE DIAGNOSTICS ({map.Name}): {oneFile}");
                }
                catch (Exception ex)
                {
                    Log($"ONE-FILE DIAGNOSTICS FAILED ({map.Name}): {ex.GetType().Name}: {ex.Message}");
                }
            }

            try
            {
                var master = await NormalMapMasterDiagnostics.WriteAsync(maps, outputRoot, report, reportPath, started, fastMode);
                Log($"ALL-NORMAL MASTER DATASET: {master}");
            }
            catch (Exception ex)
            {
                Log($"ALL-NORMAL MASTER DATASET FAILED: {ex.GetType().Name}: {ex.Message}");
            }

            EndBatchProgress($"Batch complete: {passCount} PASS / {failCount} FAIL ({cachedCount} cached). Report: {reportPath}");
            Log($"================ BATCH COMPLETE: PASS={passCount}, FAIL={failCount}, CACHED={cachedCount} ================");
            Log($"BATCH consolidated report: {reportPath}");
            Log($"CACHE: memoryHits={cacheStats.MemoryHits}, persistentHits={cacheStats.PersistentHits}, misses={cacheStats.Misses}, derivedHits={cacheStats.DerivedHits}, derivedDiskHits={cacheStats.DerivedPersistentHits}, avoided={cacheStats.DecompressedBytesAvoided / (1024.0 * 1024.0):F1} MB decompression/I/O.");
            _tabs.SelectedTab = _logsTab;
        }
    }

    private async Task<BatchStageResult> RunBatchStageAsync(MapCandidate map, string stageName, Func<Task<string>> action)
    {
        var started = DateTime.Now;
        Log($"  -> {stageName}...");
        try
        {
            // Each stage is isolated on the worker pool. Any exception is captured and logged;
            // it never escapes far enough to abort the map or the rest of the batch.
            var details = await Task.Run(action);
            var elapsed = DateTime.Now - started;
            Log($"     PASS {stageName} ({elapsed.TotalSeconds:F1}s): {details}");
            return new BatchStageResult(true, details, elapsed, null, false);
        }
        catch (Exception ex)
        {
            var elapsed = DateTime.Now - started;
            var detail = ex.Message.Replace("\r", " ").Replace("\n", " ");
            Log($"     FAIL {stageName} ({elapsed.TotalSeconds:F1}s): {detail}");
            Log($"          {ex.GetType().Name}: {detail}");
            return new BatchStageResult(false, detail, elapsed, ex.ToString(), false);
        }
    }

    private void BeginBatchProgress(int totalSteps, string status)
    {
        UseWaitCursor = true;
        _maps.Enabled = false;
        _refreshMapsButton.Enabled = false;
        _checkAllNormalButton.Enabled = false;
        _runBatchTestsButton.Enabled = false;
        _batchMode.Enabled = false;
        _clearAnalysisCacheButton.Enabled = false;
        _normalMapAnalyzeButton.Enabled = false;
        _normalMapBuildButton.Enabled = false;
        _stockResolveButton.Enabled = false;
        _xanimProofButton.Enabled = false;
        _xmodelProofButton.Enabled = false;
        _weaponProofButton.Enabled = false;
        _progress.Visible = true;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.MarqueeAnimationSpeed = 0;
        _progress.Minimum = 0;
        _progress.Maximum = Math.Max(1, totalSteps);
        _progress.Value = 0;
        SetStatus(status);
        Application.DoEvents();
    }

    private void UpdateBatchProgress(int completed, int total, string status)
    {
        _progress.Maximum = Math.Max(1, total);
        _progress.Value = Math.Min(Math.Max(0, completed), _progress.Maximum);
        SetStatus(status);
        Application.DoEvents();
    }

    private void EndBatchProgress(string status)
    {
        UseWaitCursor = false;
        _maps.Enabled = true;
        _refreshMapsButton.Enabled = true;
        _checkAllNormalButton.Enabled = true;
        _runBatchTestsButton.Enabled = true;
        _batchMode.Enabled = true;
        _clearAnalysisCacheButton.Enabled = true;
        _normalMapAnalyzeButton.Enabled = true;
        _normalMapBuildButton.Enabled = true;
        _stockResolveButton.Enabled = true;
        _xanimProofButton.Enabled = true;
        _xmodelProofButton.Enabled = true;
        _weaponProofButton.Enabled = true;
        _progress.Value = 0;
        _progress.Visible = false;
        SetStatus(status);
    }

    private sealed record BatchStageResult(bool Passed, string Details, TimeSpan Elapsed, string? StackTrace, bool Cached);

    private async Task AnalyzeSelectedNormalMapAsync()
    {
        var mapItem = _maps.CheckedItems.Count > 0
            ? _maps.CheckedItems[0]
            : (_maps.SelectedItems.Count > 0 ? _maps.SelectedItems[0] : null);

        if (mapItem is null)
        {
            MessageBox.Show("Check small_map (or another normal map) in the Maps to convert list first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (mapItem.Tag is not MapCandidate map)
        {
            MessageBox.Show("The checked/selected row does not contain a map candidate. Refresh the map list and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SaveSettingsFromUi();
            var outputRoot = ResolveOutputRoot();

            SetBusy(true, $"Stage 4: analyzing {map.Name} as a normal map...");
            // Run the complete analyzer on a worker thread so FF decompression/parsing and IWD enumeration
            // cannot block the WinForms message pump.
            var result = await Task.Run(() => NormalMapAnalyzer.AnalyzeAsync(map, outputRoot));

            Log($"Stage 4 alpha 1b NORMAL MAP analysis: {map.Name}");
            Log($"  profile: {result.Profile}");
            Log($"  inputs: {result.InputFiles.Count}; fastfiles: {result.FastFiles.Count}; IWDs: {result.Iwds.Count}");
            foreach (var ff in result.FastFiles)
                Log(ff.Readable
                    ? $"  {ff.Role}: {ff.Platform}/{ff.Compression}; strings={ff.ScriptStringCount:N0}; assets={ff.AssetCount:N0}"
                    : $"  {ff.Role}: ERROR {ff.Error}");
            foreach (var kv in result.TotalAssetTypes.Take(12))
                Log($"    {kv.Key,-20} {kv.Value,7:N0}");
            foreach (var warning in result.Warnings)
                Log("  WARNING: " + warning);
            Log($"  report: {result.TextReportFile}");
            Log($"  json:   {result.JsonReportFile}");
            Log("  READ-ONLY: no source FF/IWD was modified and no Xbox FF was written.");

            MessageBox.Show(
                $"Stage 4 alpha 1b analysis PASS for {map.Name}.\n\n" +
                $"Profile: {result.Profile}\n" +
                $"Input files: {result.InputFiles.Count}\n" +
                $"Fastfiles: {result.FastFiles.Count}\n" +
                $"IWD archives: {result.Iwds.Count}\n" +
                $"Declared FF assets: {result.TotalAssetTypes.Values.Sum():N0}\n\n" +
                $"Text report:\n{result.TextReportFile}\n\n" +
                "READ-ONLY: nothing was modified. Send me the TXT report for the first Xbox build stage.",
                "Stage 4 alpha 1b PASS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            SetStatus($"Stage 4 alpha 1b PASS: {map.Name} inventory complete.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }


    private async Task BuildSelectedNormalMapXboxAsync()
    {
        var mapItem = _maps.CheckedItems.Count > 0
            ? _maps.CheckedItems[0]
            : (_maps.SelectedItems.Count > 0 ? _maps.SelectedItems[0] : null);

        if (mapItem is null)
        {
            MessageBox.Show("Check small_map (or another NORMAL-profile map) first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (mapItem.Tag is not MapCandidate map)
        {
            MessageBox.Show("The checked/selected row does not contain a map candidate. Refresh the map list and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SaveSettingsFromUi();
            var outputRoot = ResolveOutputRoot();

            SetBusy(true, $"Stage 4 alpha 2: building Xbox serializer workspace for {map.Name}...");
            var result = await Task.Run(() => NormalMapXboxBuildAttempt.BuildAsync(map, outputRoot));

            Log($"Stage 4 alpha 2 Xbox build attempt: {map.Name}");
            foreach (var ff in result.FastFiles)
                Log($"  {ff.Role}: assets={ff.AssetCount:N0}; proof={ff.ProofAssets:N0}; verified={ff.ProofVerified}");
            Log($"  critical blockers: {result.CriticalBlockers.Count}");
            Log($"  high blockers: {result.HighBlockers.Count}");
            Log($"  report: {result.TextReportFile}");
            Log($"  workspace: {result.Workspace}");
            Log("  NO GAME FF EMITTED unless all critical/high serializers are complete.");

            MessageBox.Show(
                $"Stage 4 alpha 2 writer pass complete for {map.Name}.\n\n" +
                $"Serializer proof streams verified: {result.FastFiles.Count(x => x.ProofVerified)}\n" +
                $"Critical serializer blockers: {result.CriticalBlockers.Count}\n" +
                $"High serializer blockers: {result.HighBlockers.Count}\n" +
                $"Final Xbox map FF emitted: {(result.CanEmitXboxMapFastFile ? "YES" : "NO (fail closed)")}\n\n" +
                $"Report:\n{result.TextReportFile}\n\n" +
                "Send me this TXT. The .bin files are internal big-endian serializer proofs, NOT files to copy to the Xbox.",
                "Stage 4 alpha 2 writer pass",
                MessageBoxButtons.OK,
                result.CanEmitXboxMapFastFile ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            SetStatus($"Stage 4 alpha 2 complete: {map.Name}; {result.CriticalBlockers.Count} critical blocker(s).");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }


    private async Task ResolveSelectedNormalMapStockDonorsAsync()
    {
        var mapItem = _maps.CheckedItems.Count > 0
            ? _maps.CheckedItems[0]
            : (_maps.SelectedItems.Count > 0 ? _maps.SelectedItems[0] : null);

        if (mapItem is null)
        {
            MessageBox.Show("Check small_map (or another NORMAL-profile map) first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (mapItem.Tag is not MapCandidate map)
        {
            MessageBox.Show("The checked/selected row does not contain a map candidate. Refresh the map list and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SaveSettingsFromUi();
            var outputRoot = ResolveOutputRoot();
            var xboxRoot = string.IsNullOrWhiteSpace(_xboxGame.Text) ? null : _xboxGame.Text.Trim();

            SetBusy(true, $"Stage 4 alpha 3: resolving critical names / Xbox donors for {map.Name}...");
            var result = await Task.Run(() => NormalMapStockResolutionAudit.RunAsync(map, outputRoot, xboxRoot));

            var uniqueAnim = result.FastFiles.SelectMany(x => x.XAnimNames).Select(x => x.TrimStart(',')).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var uniqueModel = result.FastFiles.SelectMany(x => x.XModelNames).Select(x => x.TrimStart(',')).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            Log($"Stage 4 alpha 3 stock-donor resolution: {map.Name}");
            Log($"  critical names: xanim={uniqueAnim:N0}; xmodel={uniqueModel:N0}; IWD weapons={result.IwdWeaponNames.Count:N0}");
            Log($"  Xbox donor zones scanned: {result.XboxZonesScanned.Count:N0}");
            Log($"  exact donor candidates: xanim={result.XAnimDonors.Count:N0}; xmodel={result.XModelDonors.Count:N0}");
            foreach (var warning in result.Warnings) Log("  NOTE: " + warning);
            Log($"  report: {result.TextReportFile}");
            Log("  READ-ONLY: no PC/Xbox FF was modified and no donor was applied.");

            MessageBox.Show(
                $"Stage 4 alpha 3 donor resolution PASS for {map.Name}.\n\n" +
                $"Unique XAnim names: {uniqueAnim:N0}\n" +
                $"Unique XModel names: {uniqueModel:N0}\n" +
                $"IWD weapon names: {result.IwdWeaponNames.Count:N0}\n" +
                $"Xbox donor zones scanned: {result.XboxZonesScanned.Count:N0}\n" +
                $"Exact XAnim donor candidates: {result.XAnimDonors.Count:N0}\n" +
                $"Exact XModel donor candidates: {result.XModelDonors.Count:N0}\n\n" +
                $"Report:\n{result.TextReportFile}\n\n" +
                "READ-ONLY: exact-name matches are candidates only; nothing was replaced.",
                "Stage 4 alpha 3 donor resolution",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            SetStatus($"Stage 4 alpha 3 complete: {result.XAnimDonors.Count:N0} XAnim and {result.XModelDonors.Count:N0} XModel donor candidate(s).");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private async Task ProveSelectedNormalMapXAnimAsync()
    {
        var mapItem = _maps.CheckedItems.Count > 0
            ? _maps.CheckedItems[0]
            : (_maps.SelectedItems.Count > 0 ? _maps.SelectedItems[0] : null);

        if (mapItem is null)
        {
            MessageBox.Show("Check small_map (or another NORMAL-profile map) first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (mapItem.Tag is not MapCandidate map)
        {
            MessageBox.Show("The checked/selected row does not contain a map candidate. Refresh the map list and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SaveSettingsFromUi();
            var outputRoot = ResolveOutputRoot();

            SetBusy(true, $"Stage 4 alpha 9g: proving packed-name Xbox XAnim serializer for {map.Name}...");
            var result = await Task.Run(() => NormalMapXAnimSerializerProof.RunAsync(map, outputRoot));

            Log($"Stage 4 alpha 9g XAnim serializer proof: {map.Name}");
            foreach (var ff in result.FastFiles)
                Log($"  {ff.Role}: {ff.SerializedXAnim:N0}/{ff.DeclaredXAnim:N0} XAnim; delta={ff.DeltaXAnim:N0}; bytes={ff.SerializedBytes:N0}; verified={ff.Verified}");
            foreach (var warning in result.Warnings) Log("  NOTE: " + warning);
            Log($"  total: {result.SerializedXAnim:N0}/{result.DeclaredXAnim:N0}; delta={result.DeltaXAnim:N0}; bytes={result.SerializedBytes:N0}");
            Log($"  report: {result.TextReportFile}");
            Log("  INTERNAL PROOF ONLY: no Xbox game fastfile was emitted.");

            MessageBox.Show(
                $"Stage 4 alpha 9g XAnim serializer proof PASS for {map.Name}.\n\n" +
                $"Fastfiles proved: {result.FastFiles.Count:N0}\n" +
                $"XAnim declared: {result.DeclaredXAnim:N0}\n" +
                $"XAnim serialized: {result.SerializedXAnim:N0}\n" +
                $"Delta animations: {result.DeltaXAnim:N0}\n" +
                $"Serialized payload bytes: {result.SerializedBytes:N0}\n" +
                $"Read-back verified: {result.Verified}\n\n" +
                $"Report:\n{result.TextReportFile}\n\n" +
                "The .xbox_xanim_proof.bin files are internal proof streams, NOT files to copy to the Xbox. XModel, Weapon, world/collision and high-priority serializers still gate a real map FF.",
                "Stage 4 alpha 9g XAnim proof",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            SetStatus($"Stage 4 alpha 9g PASS: {result.SerializedXAnim:N0}/{result.DeclaredXAnim:N0} XAnim serialized and read back.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }


    private async Task ProveSelectedNormalMapXModelAsync()
    {
        var mapItem = _maps.CheckedItems.Count > 0
            ? _maps.CheckedItems[0]
            : (_maps.SelectedItems.Count > 0 ? _maps.SelectedItems[0] : null);

        if (mapItem is null)
        {
            MessageBox.Show("Check small_map (or another NORMAL-profile map) first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (mapItem.Tag is not MapCandidate map)
        {
            MessageBox.Show("The checked/selected row does not contain a map candidate. Refresh the map list and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SaveSettingsFromUi();
            var outputRoot = ResolveOutputRoot();

            SetBusy(true, $"Stage 4 alpha 9g: proving XAsset-order-filtered Xbox XModel serializer for {map.Name}...");
            var result = await Task.Run(() => NormalMapXModelSerializerProof.RunAsync(map, outputRoot));

            Log($"Stage 4 alpha 9g XModel serializer proof: {map.Name}");
            foreach (var ff in result.FastFiles)
                Log($"  {ff.Role}: {ff.SerializedXModel:N0}/{ff.DeclaredXModel:N0} XModel; bones={ff.TotalBones:N0}; surfs={ff.TotalSurfaces:N0}; materials={ff.InlineMaterials:N0}; images={ff.InlineImages:N0}; bytes={ff.SerializedBytes:N0}; verified={ff.Verified}");
            foreach (var warning in result.Warnings) Log("  NOTE: " + warning);
            Log($"  total: {result.SerializedXModel:N0}/{result.DeclaredXModel:N0}; bones={result.TotalBones:N0}; surfs={result.TotalSurfaces:N0}; bytes={result.SerializedBytes:N0}");
            Log($"  report: {result.TextReportFile}");
            Log("  INTERNAL PROOF ONLY: no Xbox game fastfile was emitted.");

            MessageBox.Show(
                $"Stage 4 alpha 9g XModel serializer proof PASS for {map.Name}.\n\n" +
                $"Fastfiles proved: {result.FastFiles.Count:N0}\n" +
                $"XModel declared: {result.DeclaredXModel:N0}\n" +
                $"XModel serialized: {result.SerializedXModel:N0}\n" +
                $"Total model bones: {result.TotalBones:N0}\n" +
                $"Total model surfaces: {result.TotalSurfaces:N0}\n" +
                $"Inline materials/images: {result.InlineMaterials:N0} / {result.InlineImages:N0}\n" +
                $"Physics geometry/collmap models: {result.PhysGeomModels:N0} / {result.CollmapModels:N0}\n" +
                $"Serialized payload bytes: {result.SerializedBytes:N0}\n" +
                $"Read-back verified: {result.Verified}\n\n" +
                $"Report:\n{result.TextReportFile}\n\n" +
                "The .xbox_xmodel_proof.bin files are internal proof streams, NOT files to copy to the Xbox. Weapon, world/collision integration and remaining high-priority serializers still gate a real map FF.",
                "Stage 4 alpha 9g XModel proof",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            SetStatus($"Stage 4 alpha 9g PASS: {result.SerializedXModel:N0}/{result.DeclaredXModel:N0} XModel serialized and read back.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }


    private async Task ProveSelectedNormalMapFxAsync()
    {
        var mapItem = _maps.CheckedItems.Count > 0
            ? _maps.CheckedItems[0]
            : (_maps.SelectedItems.Count > 0 ? _maps.SelectedItems[0] : null);

        if (mapItem is null)
        {
            MessageBox.Show("Check small_map (or another NORMAL-profile map) first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (mapItem.Tag is not MapCandidate map)
        {
            MessageBox.Show("The checked/selected row does not contain a map candidate. Refresh the map list and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SaveSettingsFromUi();
            var outputRoot = ResolveOutputRoot();

            SetBusy(true, $"Stage 4 alpha 9b: proving strict zero-total FX reference recovery for {map.Name}...");
            var result = await Task.Run(() => NormalMapFxSerializerProof.RunAsync(map, outputRoot));

            Log($"Stage 4 alpha 9b strict zero-total FX reference proof: {map.Name}");
            foreach (var ff in result.FastFiles)
                Log($"  {ff.Role}: bodies={ff.SerializedFx:N0}, aliases={ff.AliasFx:N0}, covered={ff.CoveredFx:N0}/{ff.DeclaredFx:N0}; bytes={ff.SerializedBytes:N0}; elems={ff.ElemDefs:N0}; vel/vis={ff.VelocitySamples:N0}/{ff.VisualSamples:N0}; M/X/S/R={ff.MaterialVisuals:N0}/{ff.ModelVisuals:N0}/{ff.SoundVisuals:N0}/{ff.RunnerVisuals:N0}; trails={ff.TrailDefs:N0}; verified={ff.Verified}");
            foreach (var warning in result.Warnings) Log("  NOTE: " + warning);
            Log($"  total: covered={result.CoveredFx:N0}/{result.DeclaredFx:N0}; bytes={result.SerializedBytes:N0}; elems={result.ElemDefs:N0}");
            Log($"  report: {result.TextReportFile}");
            Log("  INTERNAL PROOF ONLY: no Xbox game fastfile was emitted.");

            MessageBox.Show(
                $"Stage 4 alpha 9b FxEffectDef serializer proof PASS for {map.Name}.\n\n" +
                $"Fastfiles proved: {result.FastFiles.Count:N0}\n" +
                $"FxEffectDef declared: {result.DeclaredFx:N0}\n" +
                $"Physical bodies serialized: {result.SerializedFx:N0}\n" +
                $"Alias/reference XAssets: {result.AliasFx:N0}\n" +
                $"Top-level FX coverage: {result.CoveredFx:N0}/{result.DeclaredFx:N0}\n" +
                $"Physical stream bytes: {result.SerializedBytes:N0}\n" +
                $"Header totalSize bytes: {result.DeclaredTotalBytes:N0}\n" +
                $"Reconstructed FX-owned bytes: {result.FxOwnedBytes:N0}\n" +
                $"Inline dependency bytes: {result.InlineDependencyBytes:N0}\n" +
                $"FxElemDef bodies: {result.ElemDefs:N0}\n" +
                $"Velocity / visual samples: {result.VelocitySamples:N0} / {result.VisualSamples:N0}\n" +
                $"Inline strings: {result.InlineStrings:N0}\n" +
                $"Visual deps Material / XModel / Sound / Runner: {result.MaterialVisuals:N0} / {result.ModelVisuals:N0} / {result.SoundVisuals:N0} / {result.RunnerVisuals:N0}\n" +
                $"Effect-ref names: {result.EffectRefNames:N0}\n" +
                $"Trail defs / verts / indices: {result.TrailDefs:N0} / {result.TrailVertices:N0} / {result.TrailIndices:N0}\n" +
                $"Inline Material / XModel bodies: {result.InlineMaterials:N0} / {result.InlineXModels:N0}\n" +
                $"Read-back verified: {result.Verified}\n\n" +
                $"Report:\n{result.TextReportFile}\n\n" +
                "This proves standalone FxEffectDef ownership and endian conversion. WeaponDef integration is deliberately the next checkpoint. The proof .bin files are NOT files to copy to the Xbox.",
                "Stage 4 alpha 9b FxEffectDef serializer proof",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            SetStatus($"Stage 4 alpha 9b PASS: {result.CoveredFx:N0}/{result.DeclaredFx:N0} FX XAssets; {result.SerializedBytes:N0} bytes verified.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }


    private async Task InspectSelectedAsync()
    {
        var path = SelectedFastFilePath();
        if (path is null) return;

        try
        {
            SetBusy(true, "Inspecting / decompressing probe...");
            var info = await FastFileInspector.InspectAsync(path);
            _propertyGrid.SelectedObject = new FastFileView(info);
            Log($"Inspected: {path}");
            Log($"  {info.Platform} / {info.Compression} / {info.ZoneHeader}");
            foreach (var warning in info.Warnings) Log("  WARNING: " + warning);
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private async Task DecompressSelectedAsync()
    {
        var path = SelectedFastFilePath();
        if (path is null) return;

        using var save = new SaveFileDialog
        {
            Filter = "Raw zone (*.zone)|*.zone|All files (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(path) + ".zone"
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SetBusy(true, "Decompressing...");
            var result = await FastFileDecompressor.DecompressAutoAsync(path, save.FileName);
            Log($"Decompressed: {path}");
            Log($"  -> {save.FileName} ({result.DecompressedSize:N0} bytes, {result.Platform}/{result.Compression})");
            _propertyGrid.SelectedObject = new DecompressionView(result);
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private async Task CompareSelectedAsync()
    {
        var leftPath = SelectedFastFilePath();
        if (leftPath is null) return;

        using var dialog = new OpenFileDialog
        {
            Filter = "Call of Duty FastFiles (*.ff)|*.ff|All files (*.*)|*.*",
            Title = "Choose the comparison fastfile"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SetBusy(true, "Comparing fastfiles...");
            var result = await ZoneComparer.CompareAsync(leftPath, dialog.FileName);
            var text = string.Join(Environment.NewLine, result.Items.Select(i =>
                $"{i.Field,-22} {i.Left,-24} | {i.Right,-24} {(i.Equal ? "=" : "!=")}"));
            Log($"Comparison: {leftPath} <-> {dialog.FileName}");
            foreach (var line in text.Split(Environment.NewLine)) Log("  " + line);
            _tabs.SelectedTab = _logsTab;
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private async Task AnalyzeAssetsAsync()
    {
        var path = SelectedFastFilePath();
        if (path is null) return;

        try
        {
            SetBusy(true, "Parsing T4 XAssetList...");
            var document = await T4ZoneParser.ParseFastFileAsync(path);
            _lastZoneDocument = document;
            PopulateAssetList(document);
            _exportManifestButton.Enabled = true;
            _tabs.SelectedTab = _assetsTab;

            Log($"Stage 2 XAssetList: {Path.GetFileName(path)}");
            Log($"  script strings: {document.ScriptStringCount:N0}");
            Log($"  assets: {document.AssetCount:N0} at zone offset 0x{document.AssetTableOffset:X}");
            foreach (var pair in document.AssetCountsByType.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
                Log($"  {pair.Key,-20} {pair.Value,6:N0}");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private void PopulateAssetList(T4ZoneDocument document)
    {
        PopulateAssetList(document, null);
    }

    private void PopulateAssetList(T4ZoneDocument document, IReadOnlyDictionary<int, T4ResolvedAsset>? resolved)
    {
        _assets.SuspendLayout();
        try
        {
            _assets.Rows.Clear();
            foreach (var asset in document.Assets)
            {
                string resolvedName = string.Empty;
                string payloadOffset = string.Empty;
                string details = string.Empty;

                if (resolved is not null && resolved.TryGetValue(asset.Index, out var info))
                {
                    resolvedName = info.Name;
                    payloadOffset = $"0x{info.PayloadOffset:X}";
                    details = info.Details;
                }

                _assets.Rows.Add(
                    asset.Index,
                    asset.TypeId,
                    asset.TypeName,
                    asset.ReferenceKind.ToString(),
                    $"0x{asset.RawHeader:X8}",
                    resolvedName,
                    payloadOffset,
                    details);
            }

            var topTypes = string.Join(", ", document.AssetCountsByType
                .OrderByDescending(p => p.Value)
                .Take(6)
                .Select(p => $"{p.Key}={p.Value}"));

            var declared = document.AssetCount;
            var parsed = document.Assets.Count;
            var displayed = _assets.Rows.Count;
            var firstDisplayed = displayed > 0 ? Convert.ToString(_assets.Rows[0].Cells[0].Value) : "-";
            var lastDisplayed = displayed > 0 ? Convert.ToString(_assets.Rows[displayed - 1].Cells[0].Value) : "-";
            var integrity = declared == parsed && parsed == displayed && firstDisplayed == "0" && lastDisplayed == (declared - 1).ToString()
                ? "PASS"
                : "MISMATCH";

            _assetSummary.Text =
                $"{Path.GetFileName(document.SourceFastFile)} — {document.Platform}, {document.Endianness}-endian — " +
                $"declared={declared:N0}, parsed={parsed:N0}, grid={displayed:N0}, range=#{firstDisplayed}..#{lastDisplayed} [{integrity}] — " +
                $"XAsset table @ 0x{document.AssetTableOffset:X}. {topTypes}";

            if (integrity != "PASS")
            {
                Log($"ERROR: XAsset grid mismatch: declared={declared:N0}, parsed={parsed:N0}, grid={displayed:N0}, range=#{firstDisplayed}..#{lastDisplayed}.");
                SetStatus("XAsset grid mismatch - see Logs.");
            }
        }
        finally
        {
            _assets.ResumeLayout();
        }
    }

    private async Task ExportManifestAsync()
    {
        if (_lastZoneDocument is null)
        {
            MessageBox.Show("Analyze a fastfile first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var save = new SaveFileDialog
        {
            Filter = "JSON manifest (*.json)|*.json|All files (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(_lastZoneDocument.SourceFastFile) + "_assets.json"
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SetBusy(true, "Writing asset manifest...");
            await T4ZoneManifestWriter.WriteAsync(_lastZoneDocument, save.FileName);
            Log($"Asset manifest written: {save.FileName}");
            SetStatus("Asset manifest exported.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private async Task ResolvePayloadsAsync()
    {
        var path = SelectedFastFilePath() ?? _lastZoneDocument?.SourceFastFile;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show("Choose a fastfile first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetBusy(true, "Resolving Localize/RawFile/StringTable/MenuList payloads...");
            var result = await T4PayloadScanner.ScanFastFileAsync(path);
            _lastPayloadScan = result;
            _lastZoneDocument = result.Document;
            PopulateAssetList(result.Document, result.ResolvedByAssetIndex);
            _exportManifestButton.Enabled = true;
            _showMapstableButton.Enabled = result.StringTables.Any(t =>
                t.Name.Equals("maps/mapstable.csv", StringComparison.OrdinalIgnoreCase));

            Log($"Stage 2 payload resolution: {Path.GetFileName(path)}");
            Log($"  resolved {result.ResolvedAssets.Count:N0}/{result.Document.AssetCount:N0} total XAsset entries");
            Log($"  stringtable: {result.StringTables.Count:N0}; menulist: {result.MenuLists.Count:N0}; " +
                $"localize: {result.LocalizeEntries.Count:N0}; rawfile: {result.RawFiles.Count:N0}");
            foreach (var warning in result.Warnings)
                Log("  WARNING: " + warning);

            foreach (var table in result.StringTables)
                Log($"  STRINGTABLE 0x{table.PayloadOffset:X8} {table.Name} ({table.RowCount}x{table.ColumnCount})");
            foreach (var list in result.MenuLists)
            {
                Log($"  MENULIST    0x{list.PayloadOffset:X8} {list.Name} ({list.MenuCount} menus)");
                for (var i = 0; i < list.MenuPointers.Count; i++)
                    Log($"               [{i,2}] {T4ZonePointer.Describe(list.MenuPointers[i])}");
            }

            _tabs.SelectedTab = _assetsTab;
            SetStatus($"Resolved {result.ResolvedAssets.Count:N0} simple payload(s). Menus themselves are next.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private void ShowMapstable()
    {
        var table = _lastPayloadScan?.StringTables.FirstOrDefault(t =>
            t.Name.Equals("maps/mapstable.csv", StringComparison.OrdinalIgnoreCase));
        if (table is null)
        {
            MessageBox.Show("Resolve simple payloads on patch.ff first; maps/mapstable.csv was not found.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var window = new Form
        {
            Text = $"{table.Name} — {table.RowCount} rows x {table.ColumnCount} columns",
            StartPosition = FormStartPosition.CenterParent,
            Width = 1300,
            Height = 720,
            MinimizeBox = true,
            MaximizeBox = true
        };

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            RowHeadersVisible = true,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
        };

        for (var column = 0; column < table.ColumnCount; column++)
        {
            var header = table.GetValue(0, column);
            if (string.IsNullOrWhiteSpace(header) || header.StartsWith("<", StringComparison.Ordinal))
                header = $"Column {column}";
            grid.Columns.Add($"c{column}", header);
        }

        for (var row = 1; row < table.RowCount; row++)
        {
            var values = new object?[table.ColumnCount];
            for (var column = 0; column < table.ColumnCount; column++)
                values[column] = table.GetValue(row, column) ?? string.Empty;
            grid.Rows.Add(values);
            grid.Rows[^1].HeaderCell.Value = row.ToString();
        }

        var note = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Stage 2 reads inline StringTable cells. Packed/aliased cells are decoded to their T4 block + offset. Resolving that logical block address back to a serialized string is the next pointer-walk step.",
            Padding = new Padding(6, 6, 0, 0)
        };
        window.Controls.Add(grid);
        window.Controls.Add(note);
        window.Show(this);
    }

    private async Task RoundTripFastFileAsync()
    {
        var path = SelectedFastFilePath();
        if (path is null) return;

        using var save = new SaveFileDialog
        {
            Filter = "Call of Duty FastFiles (*.ff)|*.ff|All files (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(path) + "_roundtrip.ff"
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SetBusy(true, "Repacking and verifying fastfile...");
            var result = await FastFileRepacker.RoundTripAsync(path, save.FileName);
            Log($"Round-trip rebuild: {path}");
            Log($"  -> {save.FileName}");
            Log($"  {result.Platform}/{result.Compression}, raw zone {result.ZoneBytes:N0} bytes, output {result.OutputBytes:N0} bytes");
            Log($"  byte-for-byte decompressed verification: {(result.VerifiedByteForByte ? "PASS" : "FAIL")}");
            SetStatus("Round-trip FF verified byte-for-byte after decompression.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }


    private async Task ProbeMenuStructuresAsync()
    {
        var path = SelectedFastFilePath() ?? _lastZoneDocument?.SourceFastFile;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show("Choose an Xbox 360 WaW fastfile first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetBusy(true, "Probing 312-byte T4 console menuDef_t structures...");
            var result = await T4MenuStructureProbe.ProbeFastFileAsync(path);
            var declaredMenus = result.Document.Assets.Count(a =>
                a.TypeName.Equals("menu", StringComparison.OrdinalIgnoreCase));

            Log($"Stage 2 menuDef_t structure probe: {Path.GetFileName(path)}");
            Log($"  declared menu XAssets: {declaredMenus:N0}");
            Log($"  high-confidence inline-named menuDef_t structures: {result.InlineNamedMenus.Count:N0}");
            foreach (var marker in result.MarkerHits)
                Log($"  MARKER 0x{marker.Offset:X8} {marker.Value}");
            foreach (var warning in result.Warnings)
                Log("  NOTE: " + warning);

            var window = new Form
            {
                Text = $"T4 menuDef_t probe — {Path.GetFileName(path)} — {result.InlineNamedMenus.Count:N0} proven inline menu(s)",
                StartPosition = FormStartPosition.CenterParent,
                Width = 1280,
                Height = 720,
                MinimizeBox = true,
                MaximizeBox = true
            };

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(8, 7, 8, 0),
                Text = $"Declared menu assets: {declaredMenus:N0}    Proven inline-named structures: {result.InlineNamedMenus.Count:N0}\r\n" +
                       "This Stage 2 probe intentionally leaves pre-linked/reused menu names unresolved instead of guessing."
            };

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Serialized offset", Width = 125 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Window/menu name", Width = 260 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Items", Width = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fullscreen", Width = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rect", Width = 210 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Align", Width = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name ptr", Width = 115 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Items ptr", Width = 115 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Confidence", Width = 320 });

            foreach (var menu in result.InlineNamedMenus)
            {
                grid.Rows.Add(
                    $"0x{menu.SerializedOffset:X8}",
                    menu.Name,
                    menu.ItemCount,
                    menu.FullScreen ? "Yes" : "No",
                    $"{menu.X:0.##}, {menu.Y:0.##}, {menu.Width:0.##}, {menu.Height:0.##}",
                    $"{menu.HorizontalAlign}/{menu.VerticalAlign}",
                    $"0x{menu.RawNamePointer:X8}",
                    $"0x{menu.RawItemsPointer:X8}",
                    menu.Confidence);
            }

            var markers = new ListBox
            {
                Dock = DockStyle.Bottom,
                Height = Math.Min(120, Math.Max(46, 22 + result.MarkerHits.Count * 18))
            };
            if (result.MarkerHits.Count == 0)
                markers.Items.Add("No Stage-3 marker strings found in this fastfile.");
            else
                foreach (var marker in result.MarkerHits)
                    markers.Items.Add($"Marker 0x{marker.Offset:X8}  {marker.Value}");

            window.Controls.Add(grid);
            window.Controls.Add(markers);
            window.Controls.Add(header);
            window.Show(this);

            SetStatus($"Menu probe found {result.InlineNamedMenus.Count:N0} proven inline menuDef_t structure(s). Block-linked menus remain intentionally unresolved.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private async Task TraceCustomMapsAsync()
    {
        var path = SelectedFastFilePath() ?? _lastZoneDocument?.SourceFastFile;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show("Choose patch_ui.ff (or another Xbox 360 WaW fastfile) first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetBusy(true, "Tracing codxe Custom Maps menu items...");
            var result = await T4CustomMapsTrace.TraceFastFileAsync(path);

            Log($"Stage 2 alpha 8 Custom Maps pointer-array trace: {Path.GetFileName(path)}");
            foreach (var trace in result.Markers)
            {
                var item = trace.CandidateItemOffset is long itemOffset ? $"0x{itemOffset:X8}" : "-";
                var prev = trace.NearestPreviousProvenMenuOffset is long prevOffset
                    ? $"{trace.NearestPreviousProvenMenu} @ 0x{prevOffset:X8}"
                    : "-";
                var next = trace.NextProvenMenuOffset is long nextOffset
                    ? $"{trace.NextProvenMenu} @ 0x{nextOffset:X8}"
                    : "-";
                Log($"  CUSTOM MAPS 0x{trace.MarkerOffset:X8} mode={trace.UsermapMode ?? "-"} item={item} strong={trace.StrongInlineItemCandidate}");
                if (trace.Item is not null)
                {
                    Log($"              item type={trace.Item.Type} parent=0x{trace.Item.RawParentPointer:X8} text=0x{trace.Item.RawTextPointer:X8} action=0x{trace.Item.RawActionPointer:X8}");
                    Log($"              structural checks={trace.Item.StructuralChecksPassed}/{trace.Item.StructuralChecksTotal} ({trace.Item.Confidence})");
                    Log($"              exact-parent items: all={trace.SharedParentValidItemCount}, region={trace.RegionSiblingCount}, serialized ordinal={(trace.SerializedSiblingOrdinal is int ord ? ord.ToString() : "-")}");
                    Log($"              item pointer array: {(trace.ItemPointerArrayOffset is long pa ? $"0x{pa:X8}" : "-")}, entries={trace.ItemPointerArrayCount}, logical index={(trace.LogicalItemIndex is int li ? li.ToString() : "-")}");
                    Log($"              array proof: {trace.ItemPointerArrayConfidence}");
                    Log($"              owner: {(trace.ResolvedOwnerMenu ?? "-")} {(trace.ResolvedOwnerMenuOffset is long oo ? $"@ 0x{oo:X8}" : "")} — {trace.OwnerConfidence}");
                }
                Log($"              previous proven menu: {prev}");
                Log($"              next proven menu:     {next}");
                for (var i = 0; i < trace.FollowingStrings.Count; i++)
                    Log($"              string[{i}] {trace.FollowingStrings[i]}");
            }
            foreach (var warning in result.Warnings)
                Log("  NOTE: " + warning);

            var window = new Form
            {
                Text = $"Custom Maps structural trace — {Path.GetFileName(path)}",
                StartPosition = FormStartPosition.CenterParent,
                Width = 1120,
                Height = 680,
                MinimumSize = new Size(860, 520),
                MinimizeBox = true,
                MaximizeBox = true
            };

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 68,
                Padding = new Padding(8, 7, 8, 0),
                Text = "Stage 2 ownership proof only — nothing is modified.\r\n" +
                       "Alpha 8 additionally proves the inline item pointer array and reports the logical menu item index when all counts agree."
            };

            var details = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 235,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9f)
            };

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Text offset", Width = 105 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mode", Width = 62 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "itemDef_s", Width = 105 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Strong", Width = 62 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", Width = 52 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Parent ptr", Width = 105 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Region siblings", Width = 92 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Serialized", Width = 68 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ptr array", Width = 105 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Item index", Width = 72 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Owner", Width = 150 });

            foreach (var trace in result.Markers)
            {
                grid.Rows.Add(
                    $"0x{trace.MarkerOffset:X8}",
                    trace.UsermapMode ?? "-",
                    trace.CandidateItemOffset is long itemOffset ? $"0x{itemOffset:X8}" : "-",
                    trace.StrongInlineItemCandidate ? "YES" : "No",
                    trace.Item?.Type.ToString() ?? "-",
                    trace.Item is not null ? $"0x{trace.Item.RawParentPointer:X8}" : "-",
                    trace.RegionSiblingCount.ToString(),
                    trace.SerializedSiblingOrdinal is int ordinal ? ordinal.ToString() : "-",
                    trace.ItemPointerArrayOffset is long ptrArray ? $"0x{ptrArray:X8}" : "-",
                    trace.LogicalItemIndex is int logicalIndex ? logicalIndex.ToString() : "-",
                    trace.ResolvedOwnerMenu ?? "-");
            }

            void ShowTraceDetails(int rowIndex)
            {
                if (rowIndex < 0 || rowIndex >= result.Markers.Count)
                {
                    details.Clear();
                    return;
                }

                var trace = result.Markers[rowIndex];
                var lines = new List<string>
                {
                    $"Text marker : 0x{trace.MarkerOffset:X8}   mode={trace.UsermapMode ?? "-"}   strong={trace.StrongInlineItemCandidate}",
                    $"itemDef_s   : {(trace.CandidateItemOffset is long itemOffset ? $"0x{itemOffset:X8}" : "-")}",
                    $"Confidence  : {trace.Confidence}"
                };

                if (trace.Item is not null)
                {
                    lines.Add($"Layout      : {trace.Item.FixedSize} bytes, checks {trace.Item.StructuralChecksPassed}/{trace.Item.StructuralChecksTotal} ({trace.Item.Confidence})");
                    lines.Add($"Type/data   : type={trace.Item.Type}, dataType={trace.Item.DataType}, alignment={trace.Item.Alignment}, font={trace.Item.FontEnum}, textAlign={trace.Item.TextAlignMode}");
                    lines.Add($"Rect        : {trace.Item.X:0.##}, {trace.Item.Y:0.##}, {trace.Item.Width:0.##}, {trace.Item.Height:0.##}  align={trace.Item.HorizontalAlign}/{trace.Item.VerticalAlign}");
                    lines.Add($"Pointers    : window.name=0x{trace.Item.RawWindowNamePointer:X8}  text=0x{trace.Item.RawTextPointer:X8}  parent=0x{trace.Item.RawParentPointer:X8}  action=0x{trace.Item.RawActionPointer:X8}");
                    lines.Add($"Parent group: {trace.SharedParentValidItemCount} structurally valid item(s) zone-wide; {trace.RegionSiblingCount} inside the surrounding proven-menu region");
                    lines.Add($"Serialized  : {(trace.SerializedSiblingOrdinal is int ord ? ord.ToString() : "-")} (sibling body order)");
                    lines.Add($"Ptr array   : {(trace.ItemPointerArrayOffset is long ptrArray ? $"0x{ptrArray:X8}, entries={trace.ItemPointerArrayCount}" : "unresolved")}");
                    lines.Add($"Item index  : {(trace.LogicalItemIndex is int logical ? logical.ToString() : "-")} (logical menu item index; only claimed when pointer-array/body/menu counts all agree)");
                    lines.Add($"Array proof : {trace.ItemPointerArrayConfidence}");
                    lines.Add($"Owner       : {(trace.ResolvedOwnerMenuOffset is long ownerOffset ? $"{trace.ResolvedOwnerMenu} @ 0x{ownerOffset:X8}, declared items={trace.ResolvedOwnerDeclaredItemCount}" : "unresolved")}");
                    lines.Add($"Owner proof : {trace.OwnerConfidence}");
                }

                lines.Add($"Previous    : {(trace.NearestPreviousProvenMenuOffset is long prevOffset ? $"{trace.NearestPreviousProvenMenu} @ 0x{prevOffset:X8}" : "-")}");
                lines.Add($"Next        : {(trace.NextProvenMenuOffset is long nextOffset ? $"{trace.NextProvenMenu} @ 0x{nextOffset:X8}" : "-")}");
                lines.Add(string.Empty);
                lines.Add("Serialized inline strings:");
                for (var i = 0; i < trace.FollowingStrings.Count; i++)
                    lines.Add($"  [{i}] {trace.FollowingStrings[i]}");

                details.Text = string.Join(Environment.NewLine, lines);
            }

            grid.SelectionChanged += (_, _) =>
            {
                if (grid.CurrentRow is not null)
                    ShowTraceDetails(grid.CurrentRow.Index);
            };

            window.Controls.Add(grid);
            window.Controls.Add(details);
            window.Controls.Add(header);
            if (grid.Rows.Count > 0)
            {
                grid.Rows[0].Selected = true;
                grid.CurrentCell = grid.Rows[0].Cells[0];
                ShowTraceDetails(0);
            }
            window.Show(this);

            var solo = result.Markers.Count(m => m.UsermapMode == "solo" && m.StrongInlineItemCandidate);
            var lobby = result.Markers.Count(m => m.UsermapMode == "lobby" && m.StrongInlineItemCandidate);
            SetStatus($"Custom Maps trace: {solo} structurally proven solo button(s), {lobby} lobby button(s). Nothing modified.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private async Task CreateStage3LabelProofAsync()
    {
        var path = SelectedFastFilePath() ?? _lastZoneDocument?.SourceFastFile;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show("Choose the tested Xbox 360 patch_ui.ff first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "WaW fastfile (*.ff)|*.ff|All files (*.*)|*.*",
            FileName = $"{Path.GetFileNameWithoutExtension(path)}_stage3_labelproof.ff",
            InitialDirectory = Path.GetDirectoryName(path)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            SetBusy(true, "Creating Stage 3 equal-length label proof...");
            var result = await T4Stage3LabelProofWriter.CreateAsync(path, dialog.FileName);

            Log($"Stage 3 alpha 1 label proof: {Path.GetFileName(path)} -> {Path.GetFileName(dialog.FileName)}");
            Log($"  target: {result.OwnerMenu} item #{result.LogicalItemIndex} @ 0x{result.MarkerOffset:X8}");
            Log($"  label:  \"{result.OriginalText}\" -> \"{result.ReplacementText}\"");
            Log($"  raw-zone changed bytes: {result.RawZoneChangedByteCount}; only target span changed={result.OnlyTargetSpanChanged}");
            Log($"  asset count remains {result.AssetCount}; {result.OwnerMenu}.itemCount remains {result.MenuItemCount}");
            Log($"  output reopened and raw-zone verified: {result.ReopenedAndVerified}");

            MessageBox.Show(
                $"Stage 3 alpha 1 proof created successfully.\n\n" +
                $"Target: {result.OwnerMenu} item #{result.LogicalItemIndex}\n" +
                $"Label: {result.OriginalText} -> {result.ReplacementText}\n" +
                $"Raw-zone bytes changed: {result.RawZoneChangedByteCount}\n" +
                $"Assets: {result.AssetCount} (unchanged)\n" +
                $"Menu items: {result.MenuItemCount} (unchanged)\n\n" +
                $"This is a proof copy only. It renames the existing solo button; it does NOT yet add the second Lxcy's Maps button.",
                "Stage 3 label proof PASS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            SetStatus("Stage 3 label proof PASS: output reopened and only the proven label span changed.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private async Task RunStage3DualPointerRunAuditAsync()
    {
        var path = SelectedFastFilePath() ?? _lastZoneDocument?.SourceFastFile;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(
                "Choose the restored known-good Xbox 360 patch_ui.ff containing Custom Maps first.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Text report (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{Path.GetFileNameWithoutExtension(path)}_stage3_alpha4e_dual_pointer_run_audit.txt",
            InitialDirectory = Path.GetDirectoryName(path)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            SetBusy(true, "Running Stage 3 alpha 4e read-only dual-pointer-run audit...");
            var result = await T4Stage3DualPointerRunAudit.CreateAsync(path, dialog.FileName);

            Log($"Stage 3 alpha 4e read-only dual-pointer-run audit: {Path.GetFileName(path)}");
            Log($"  report: {dialog.FileName}");
            Log($"  levels_unlock @ 0x{result.LevelsUnlockOffset:X8}; items={result.ItemCount}");
            Log($"  proven pointer array @ 0x{result.ProvenPointerArrayOffset:X8}");
            Log($"  second 62-pointer run @ 0x{result.SecondPointerRunOffset:X8}");
            Log($"  Custom Maps #59 @ 0x{result.CustomMapsItemOffset:X8}; insertion=0x{result.InsertionPoint:X8}; clone span=0x{result.CloneSpan:X}");
            Log($"  second-run nearby anonymous menu candidates={result.SecondRunAnonymousMenuCandidates}; count=62 fields={result.SecondRunNearbyCountFields}; following high-confidence items={result.SecondRunFollowingHighConfidenceItems}");
            Log("  READ-ONLY: no .ff was created or modified.");

            MessageBox.Show(
                $"Stage 3 alpha 4e dual-pointer-run audit PASS.\n\n" +
                $"Proven levels_unlock array: 0x{result.ProvenPointerArrayOffset:X8}\n" +
                $"Second exact 62-pointer run: 0x{result.SecondPointerRunOffset:X8}\n" +
                $"Nearby anonymous menu candidates: {result.SecondRunAnonymousMenuCandidates}\n" +
                $"Nearby count=62 fields: {result.SecondRunNearbyCountFields}\n" +
                $"High-confidence items following second run: {result.SecondRunFollowingHighConfidenceItems}\n\n" +
                $"Report: {dialog.FileName}\n\n" +
                "READ-ONLY: patch_ui.ff was not modified. Send the report back; this decides whether Alpha 2 missed a second linked 62-item structure.",
                "Stage 3 alpha 4e audit PASS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            SetStatus("Stage 3 alpha 4e audit PASS: second 62-pointer run classified.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private async Task MenuProbeAsync()
    {
        var path = SelectedFastFilePath() ?? _lastZoneDocument?.SourceFastFile;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show("Choose a fastfile first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetBusy(true, "Probing menu-related strings...");
            var hits = await T4MenuStringProbe.ProbeFastFileAsync(path);
            Log($"Menu/string probe: {path} — {hits.Count:N0} unique hit(s)");
            foreach (var hit in hits.Take(500))
                Log($"  0x{hit.Offset:X8}  {hit.Value}");
            if (hits.Count > 500)
                Log($"  ... {hits.Count - 500:N0} more hit(s) omitted from the log.");
            _tabs.SelectedTab = _logsTab;
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private string? SelectedFastFilePath()
    {
        var path = _inputFastFile.Text.Trim(' ', '"');
        if (File.Exists(path)) return path;
        MessageBox.Show("Choose an existing .ff file first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return null;
    }

    private void BrowseFastFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Call of Duty FastFiles (*.ff)|*.ff|All files (*.*)|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _inputFastFile.Text = dialog.FileName;
            _lastZoneDocument = null;
            _lastPayloadScan = null;
            _showMapstableButton.Enabled = false;
            _exportManifestButton.Enabled = false;
            _tabs.SelectedTab = _inspectTab;
        }
    }

    private void ImportIni()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "WaW360Tool settings (settings.ini)|settings.ini|INI files (*.ini)|*.ini|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _settings = WaW360ToolIniImporter.Import(dialog.FileName, _settings);
            _mapSource.Text = MapDiscovery.ResolveConfiguredPcMapsFolder(_settings.MapSourceFolder);
            _settings.MapSourceFolder = _mapSource.Text;
            _pcGame.Text = _settings.PcGameFolder;
            _xboxGame.Text = _settings.XboxGameFolder;
            _codxe.Text = _settings.CodxeFolder;
            _output.Text = string.IsNullOrWhiteSpace(_settings.OutputFolder) ? ConverterSettings.DefaultOutputFolder : _settings.OutputFolder;
            _memoryBudget.Value = Math.Clamp(_settings.MemoryBudgetMb, (int)_memoryBudget.Minimum, (int)_memoryBudget.Maximum);
            _parallelMaps.Value = Math.Clamp(_settings.MaxParallelMaps, (int)_parallelMaps.Minimum, (int)_parallelMaps.Maximum);
            _batchMode.SelectedItem = string.Equals(_settings.BatchMode, "Safe Mode (Full Validation)", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(_settings.BatchMode, "Full Regression", StringComparison.OrdinalIgnoreCase)
                ? "Safe Mode (Full Validation)" : "Fast Mode";
            ConfigureAnalysisCache();
            UpdateMapsHint();
            SetStatus("Imported paths/memory budget from WaW360Tool settings.ini.");
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
    }

    private void SaveSettingsFromUi()
    {
        if (_pcGame is null) return;
        _settings.MapSourceFolder = _mapSource.Text.Trim();
        _settings.PcGameFolder = _pcGame.Text.Trim();
        _settings.XboxGameFolder = _xboxGame.Text.Trim();
        _settings.CodxeFolder = _codxe.Text.Trim();
        _settings.OutputFolder = _output.Text.Trim();
        _settings.MemoryBudgetMb = (int)_memoryBudget.Value;
        _settings.BatchMode = _batchMode.SelectedItem?.ToString() ?? "Fast Mode";
        _settings.MaxParallelMaps = (int)_parallelMaps.Value;
        if (string.IsNullOrWhiteSpace(_settings.OutputFolder))
        {
            _settings.OutputFolder = ConverterSettings.DefaultOutputFolder;
            _output.Text = _settings.OutputFolder;
        }
        SettingsStore.Save(_settings);
        ConfigureAnalysisCache();
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var ff = files.FirstOrDefault(f => File.Exists(f) && Path.GetExtension(f).Equals(".ff", StringComparison.OrdinalIgnoreCase));
        if (ff is null) return;
        _inputFastFile.Text = ff;
        _lastZoneDocument = null;
        _exportManifestButton.Enabled = false;
        _tabs.SelectedTab = _inspectTab;
    }

    private void Log(string message)
    {
        _logs.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void HandleError(Exception ex)
    {
        Log(ex.ToString());
        MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SetStatus(string message) => _status.Text = message;

    private void SetBusy(bool busy, string status)
    {
        UseWaitCursor = busy;
        _tabs.Enabled = !busy;
        _progress.Visible = busy;
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = busy ? 28 : 0;
        SetStatus(status);
        Application.DoEvents();
    }

    private sealed class FastFileView
    {
        public FastFileView(FastFileMetadata m)
        {
            File = m.FilePath;
            Magic = m.Magic;
            Version = $"0x{m.Version:X}";
            Signature = m.Signature.ToString();
            Platform = m.Platform.ToString();
            Compression = m.Compression.ToString();
            CompressedBytes = m.CompressedSize;
            DecompressedBytes = m.DecompressedSize;
            ZoneEndianness = m.ZoneHeader?.Endianness.ToString() ?? "Unknown";
            ZoneSize = Hex(m.ZoneHeader?.ZoneSize);
            ExternalSize = Hex(m.ZoneHeader?.ExternalSize);
            TempBlock = Hex(m.ZoneHeader?.TempBlockSize);
            RuntimeBlock = Hex(m.ZoneHeader?.RuntimeBlockSize);
            LargeRuntimeBlock = Hex(m.ZoneHeader?.LargeRuntimeBlockSize);
            PhysicalRuntimeBlock = Hex(m.ZoneHeader?.PhysicalRuntimeBlockSize);
            VirtualBlock = Hex(m.ZoneHeader?.VirtualBlockSize);
            LargeBlock = Hex(m.ZoneHeader?.LargeBlockSize);
            PhysicalBlock = Hex(m.ZoneHeader?.PhysicalBlockSize);
            Warnings = string.Join(Environment.NewLine, m.Warnings);
        }

        public string File { get; }
        public string Magic { get; }
        public string Version { get; }
        public string Signature { get; }
        public string Platform { get; }
        public string Compression { get; }
        public long CompressedBytes { get; }
        public long DecompressedBytes { get; }
        public string ZoneEndianness { get; }
        public string ZoneSize { get; }
        public string ExternalSize { get; }
        public string TempBlock { get; }
        public string RuntimeBlock { get; }
        public string LargeRuntimeBlock { get; }
        public string PhysicalRuntimeBlock { get; }
        public string VirtualBlock { get; }
        public string LargeBlock { get; }
        public string PhysicalBlock { get; }
        public string Warnings { get; }

        private static string Hex(uint? value) => value is null ? "Unknown" : $"0x{value.Value:X8}";
    }

    private sealed class DecompressionView
    {
        public DecompressionView(DecompressionResult r)
        {
            Platform = r.Platform.ToString();
            Compression = r.Compression.ToString();
            DecompressedBytes = r.DecompressedSize;
            Output = r.OutputPath;
            ZoneHeader = r.ZoneHeader.ToString();
        }

        public string Platform { get; }
        public string Compression { get; }
        public long DecompressedBytes { get; }
        public string Output { get; }
        public string ZoneHeader { get; }
    }
}
