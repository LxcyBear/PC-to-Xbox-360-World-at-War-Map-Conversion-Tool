namespace WaW360.Gui;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    private MenuStrip _menu = null!;
    private ToolStripMenuItem _fileMenuItem = null!;
    private ToolStripMenuItem _openFastFileMenuItem = null!;
    private ToolStripMenuItem _exitMenuItem = null!;
    private ToolStripMenuItem _toolsMenuItem = null!;
    private ToolStripMenuItem _refreshMapsMenuItem = null!;
    private ToolStripMenuItem _helpMenuItem = null!;
    private ToolStripMenuItem _aboutMenuItem = null!;

    private TabControl _tabs = null!;
    private TabPage _mapsTab = null!;
    private TabPage _inspectTab = null!;
    private TabPage _assetsTab = null!;
    private TabPage _settingsTab = null!;
    private TabPage _logsTab = null!;

    private ListView _maps = null!;
    private Button _refreshMapsButton = null!;
    private Button _normalMapAnalyzeButton = null!;
    private Button _normalMapBuildButton = null!;
    private Button _stockResolveButton = null!;
    private Button _xanimProofButton = null!;
    private Button _xmodelProofButton = null!;
    private Button _weaponProofButton = null!;
    private Button _checkAllNormalButton = null!;
    private Button _runBatchTestsButton = null!;
    private ComboBox _batchMode = null!;
    private Button _clearAnalysisCacheButton = null!;
    private Label _mapsHint = null!;

    private TextBox _inputFastFile = null!;
    private Button _browseFastFileButton = null!;
    private Button _inspectButton = null!;
    private Button _decompressButton = null!;
    private Button _compareButton = null!;
    private Button _analyzeAssetsButton = null!;
    private Button _fullConvertButton = null!;
    private PropertyGrid _propertyGrid = null!;

    private Label _assetSummary = null!;
    private DataGridView _assets = null!;
    private Button _exportManifestButton = null!;
    private Button _menuProbeButton = null!;
    private Button _roundTripButton = null!;
    private Button _resolvePayloadsButton = null!;
    private Button _showMapstableButton = null!;
    private Button _menuStructuresButton = null!;
    private Button _traceCustomMapsButton = null!;
    private Button _stage3LabelProofButton = null!;
    private Button _stage3AddLxcyButton = null!;

    private TextBox _mapSource = null!;
    private TextBox _pcGame = null!;
    private TextBox _xboxGame = null!;
    private TextBox _codxe = null!;
    private TextBox _output = null!;
    private NumericUpDown _memoryBudget = null!;
    private NumericUpDown _parallelMaps = null!;
    private Button _browseMapSourceButton = null!;
    private Button _browsePcGameButton = null!;
    private Button _browseXboxGameButton = null!;
    private Button _browseCodxeButton = null!;
    private Button _browseOutputButton = null!;
    private Button _importSettingsButton = null!;
    private Button _saveSettingsButton = null!;

    private RichTextBox _logs = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _status = null!;
    private ToolStripProgressBar _progress = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        _menu = new MenuStrip();
        _fileMenuItem = new ToolStripMenuItem();
        _openFastFileMenuItem = new ToolStripMenuItem();
        _exitMenuItem = new ToolStripMenuItem();
        _toolsMenuItem = new ToolStripMenuItem();
        _refreshMapsMenuItem = new ToolStripMenuItem();
        _helpMenuItem = new ToolStripMenuItem();
        _aboutMenuItem = new ToolStripMenuItem();

        _tabs = new TabControl();
        _mapsTab = new TabPage();
        _inspectTab = new TabPage();
        _assetsTab = new TabPage();
        _settingsTab = new TabPage();
        _logsTab = new TabPage();

        _maps = new ListView();
        _refreshMapsButton = new Button();
        _normalMapAnalyzeButton = new Button();
        _normalMapBuildButton = new Button();
        _stockResolveButton = new Button();
        _xanimProofButton = new Button();
        _xmodelProofButton = new Button();
        _weaponProofButton = new Button();
        _checkAllNormalButton = new Button();
        _runBatchTestsButton = new Button();
        _batchMode = new ComboBox();
        _clearAnalysisCacheButton = new Button();
        _mapsHint = new Label();

        _inputFastFile = new TextBox();
        _browseFastFileButton = new Button();
        _inspectButton = new Button();
        _decompressButton = new Button();
        _compareButton = new Button();
        _analyzeAssetsButton = new Button();
        _fullConvertButton = new Button();
        _propertyGrid = new PropertyGrid();

        _assetSummary = new Label();
        _assets = new DataGridView();
        _exportManifestButton = new Button();
        _menuProbeButton = new Button();
        _roundTripButton = new Button();
        _resolvePayloadsButton = new Button();
        _showMapstableButton = new Button();
        _menuStructuresButton = new Button();
        _traceCustomMapsButton = new Button();
        _stage3LabelProofButton = new Button();
        _stage3AddLxcyButton = new Button();

        _mapSource = new TextBox();
        _pcGame = new TextBox();
        _xboxGame = new TextBox();
        _codxe = new TextBox();
        _output = new TextBox();
        _memoryBudget = new NumericUpDown();
        _parallelMaps = new NumericUpDown();
        _browseMapSourceButton = new Button();
        _browsePcGameButton = new Button();
        _browseXboxGameButton = new Button();
        _browseCodxeButton = new Button();
        _browseOutputButton = new Button();
        _importSettingsButton = new Button();
        _saveSettingsButton = new Button();

        _logs = new RichTextBox();
        _statusStrip = new StatusStrip();
        _status = new ToolStripStatusLabel();
        _progress = new ToolStripProgressBar();

        ((System.ComponentModel.ISupportInitialize)_memoryBudget).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_parallelMaps).BeginInit();
        SuspendLayout();

        // ----- Main menu -----
        _menu.Items.AddRange(new ToolStripItem[] { _fileMenuItem, _toolsMenuItem, _helpMenuItem });
        _menu.Location = new Point(0, 0);
        _menu.Name = "mainMenu";
        _menu.Size = new Size(1180, 24);
        _menu.TabIndex = 0;

        _fileMenuItem.Text = "File";
        _fileMenuItem.DropDownItems.AddRange(new ToolStripItem[]
        {
            _openFastFileMenuItem,
            new ToolStripSeparator(),
            _exitMenuItem
        });
        _openFastFileMenuItem.Text = "Open fastfile...";
        _exitMenuItem.Text = "Exit";

        _toolsMenuItem.Text = "Tools";
        _toolsMenuItem.DropDownItems.Add(_refreshMapsMenuItem);
        _refreshMapsMenuItem.Text = "Refresh PC maps";

        _helpMenuItem.Text = "Help";
        _helpMenuItem.DropDownItems.Add(_aboutMenuItem);
        _aboutMenuItem.Text = "Stage 2 scope";

        // ----- Tabs -----
        _tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _tabs.Controls.Add(_mapsTab);
        _tabs.Controls.Add(_inspectTab);
        _tabs.Controls.Add(_assetsTab);
        _tabs.Controls.Add(_settingsTab);
        _tabs.Controls.Add(_logsTab);
        _tabs.Location = new Point(0, 24);
        _tabs.Name = "mainTabs";
        _tabs.SelectedIndex = 0;
        _tabs.Size = new Size(1180, 704);
        _tabs.TabIndex = 1;

        // ----- Maps tab -----
        _mapsTab.Name = "mapsTab";
        _mapsTab.Padding = new Padding(8);
        _mapsTab.Text = "Maps to convert";
        _mapsTab.UseVisualStyleBackColor = true;

        _refreshMapsButton.Location = new Point(12, 12);
        _refreshMapsButton.Name = "refreshMapsButton";
        _refreshMapsButton.Size = new Size(90, 30);
        _refreshMapsButton.Text = "Refresh";
        _refreshMapsButton.UseVisualStyleBackColor = true;

        _mapsHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _mapsHint.AutoEllipsis = true;
        _normalMapAnalyzeButton.Location = new Point(112, 12);
        _normalMapAnalyzeButton.Name = "normalMapAnalyzeButton";
        _normalMapAnalyzeButton.Size = new Size(205, 30);
        _normalMapAnalyzeButton.Text = "Stage 4: Analyze normal map";
        _normalMapAnalyzeButton.UseVisualStyleBackColor = true;

        _normalMapBuildButton.Location = new Point(325, 12);
        _normalMapBuildButton.Name = "normalMapBuildButton";
        _normalMapBuildButton.Size = new Size(215, 30);
        _normalMapBuildButton.Text = "Stage 4a2: Start Xbox build";
        _normalMapBuildButton.UseVisualStyleBackColor = true;

        _stockResolveButton.Location = new Point(548, 12);
        _stockResolveButton.Name = "stockResolveButton";
        _stockResolveButton.Size = new Size(240, 30);
        _stockResolveButton.Text = "Stage 4a3: Resolve stock donors";
        _stockResolveButton.UseVisualStyleBackColor = true;

        _xanimProofButton.Location = new Point(796, 12);
        _xanimProofButton.Name = "xanimProofButton";
        _xanimProofButton.Size = new Size(240, 30);
        _xanimProofButton.Text = "Stage 4a9j: XAnim + Bounded Recovery";
        _xanimProofButton.UseVisualStyleBackColor = true;

        _xmodelProofButton.Location = new Point(796, 48);
        _xmodelProofButton.Name = "xmodelProofButton";
        _xmodelProofButton.Size = new Size(240, 30);
        _xmodelProofButton.Text = "Stage 4a9j: XModel + Bounded Recovery";
        _xmodelProofButton.UseVisualStyleBackColor = true;

        _checkAllNormalButton.Location = new Point(112, 12);
        _checkAllNormalButton.Name = "checkAllNormalButton";
        _checkAllNormalButton.Size = new Size(155, 30);
        _checkAllNormalButton.Text = "Check all Normal";
        _checkAllNormalButton.UseVisualStyleBackColor = true;

        _runBatchTestsButton.Location = new Point(548, 12);
        _runBatchTestsButton.Name = "runBatchTestsButton";
        _runBatchTestsButton.Size = new Size(365, 30);
        _runBatchTestsButton.Text = "Convert checked maps - FAST mode";
        _runBatchTestsButton.UseVisualStyleBackColor = true;

        _weaponProofButton.Location = new Point(548, 48);
        _weaponProofButton.Name = "weaponProofButton";
        _weaponProofButton.Size = new Size(240, 30);
        _weaponProofButton.Text = "Stage 4a9b: Recover zero-total FX refs";
        _weaponProofButton.UseVisualStyleBackColor = true;

        _mapsHint.Location = new Point(12, 52);
        _mapsHint.Name = "mapsHint";
        _mapsHint.Size = new Size(520, 22);
        _mapsHint.Text = "Scanning configured PC map source folder...";

        _batchMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _batchMode.Items.AddRange(new object[] { "Fast Mode", "Safe Mode (Full Validation)" });
        _batchMode.Location = new Point(275, 15);
        _batchMode.Name = "batchMode";
        _batchMode.Size = new Size(240, 23);

        _clearAnalysisCacheButton.Location = new Point(796, 80);
        _clearAnalysisCacheButton.Name = "clearAnalysisCacheButton";
        _clearAnalysisCacheButton.Size = new Size(240, 27);
        _clearAnalysisCacheButton.Text = "Clear analysis cache";
        _clearAnalysisCacheButton.UseVisualStyleBackColor = true;

        _maps.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _maps.CheckBoxes = true;
        _maps.FullRowSelect = true;
        _maps.HideSelection = false;
        _maps.Location = new Point(12, 80);
        _maps.Name = "mapsList";
        _maps.Size = new Size(1140, 580);
        _maps.ShowItemToolTips = true;
        _maps.UseCompatibleStateImageBehavior = false;
        _maps.View = View.Details;
        _maps.Columns.Add("Map", 220);
        _maps.Columns.Add("Issues", 72, HorizontalAlignment.Center);
        _maps.Columns.Add("Profile hint", 110);
        _maps.Columns.Add("Detected files", 235);
        _maps.Columns.Add("Location", 470);

        _mapsTab.Controls.Add(_maps);
        _mapsTab.Controls.Add(_runBatchTestsButton);
        _mapsTab.Controls.Add(_checkAllNormalButton);
        _mapsTab.Controls.Add(_batchMode);
        _mapsTab.Controls.Add(_mapsHint);
        _mapsTab.Controls.Add(_refreshMapsButton);

        // ----- Inspect tab -----
        _inspectTab.Name = "inspectTab";
        _inspectTab.Padding = new Padding(8);
        _inspectTab.Text = "Inspect / Convert";
        _inspectTab.UseVisualStyleBackColor = true;

        var ffLabel = new Label();
        ffLabel.Location = new Point(12, 17);
        ffLabel.Name = "ffLabel";
        ffLabel.Size = new Size(78, 23);
        ffLabel.Text = "Fastfile:";
        ffLabel.TextAlign = ContentAlignment.MiddleRight;

        _inputFastFile.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _inputFastFile.Location = new Point(96, 16);
        _inputFastFile.Name = "inputFastFile";
        _inputFastFile.Size = new Size(926, 23);

        _browseFastFileButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _browseFastFileButton.Location = new Point(1030, 14);
        _browseFastFileButton.Name = "browseFastFileButton";
        _browseFastFileButton.Size = new Size(122, 28);
        _browseFastFileButton.Text = "Browse...";
        _browseFastFileButton.UseVisualStyleBackColor = true;

        _inspectButton.Location = new Point(96, 53);
        _inspectButton.Name = "inspectButton";
        _inspectButton.Size = new Size(145, 32);
        _inspectButton.Text = "Inspect wrapper";
        _inspectButton.UseVisualStyleBackColor = true;

        _decompressButton.Location = new Point(249, 53);
        _decompressButton.Name = "decompressButton";
        _decompressButton.Size = new Size(155, 32);
        _decompressButton.Text = "Decompress .zone...";
        _decompressButton.UseVisualStyleBackColor = true;

        _compareButton.Location = new Point(412, 53);
        _compareButton.Name = "compareButton";
        _compareButton.Size = new Size(180, 32);
        _compareButton.Text = "Compare with FF...";
        _compareButton.UseVisualStyleBackColor = true;

        _analyzeAssetsButton.Location = new Point(600, 53);
        _analyzeAssetsButton.Name = "analyzeAssetsButton";
        _analyzeAssetsButton.Size = new Size(170, 32);
        _analyzeAssetsButton.Text = "Analyze XAssetList";
        _analyzeAssetsButton.UseVisualStyleBackColor = true;

        _fullConvertButton.Location = new Point(778, 53);
        _fullConvertButton.Name = "fullConvertButton";
        _fullConvertButton.Size = new Size(244, 32);
        _fullConvertButton.Text = "PC -> Xbox conversion (later)";
        _fullConvertButton.Enabled = false;
        _fullConvertButton.UseVisualStyleBackColor = true;

        var inspectNote = new Label();
        inspectNote.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        inspectNote.AutoEllipsis = true;
        inspectNote.Location = new Point(96, 95);
        inspectNote.Name = "inspectNote";
        inspectNote.Size = new Size(1056, 35);
        inspectNote.Text = "Stage 2 understands T4 XFile + XAssetList, script strings and the serialized XAsset table. Asset payload/name decoding is being added type-by-type.";

        _propertyGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _propertyGrid.HelpVisible = true;
        _propertyGrid.Location = new Point(12, 140);
        _propertyGrid.Name = "propertyGrid";
        _propertyGrid.Size = new Size(1140, 520);
        _propertyGrid.ToolbarVisible = false;

        _inspectTab.Controls.Add(_propertyGrid);
        _inspectTab.Controls.Add(inspectNote);
        _inspectTab.Controls.Add(_fullConvertButton);
        _inspectTab.Controls.Add(_analyzeAssetsButton);
        _inspectTab.Controls.Add(_compareButton);
        _inspectTab.Controls.Add(_decompressButton);
        _inspectTab.Controls.Add(_inspectButton);
        _inspectTab.Controls.Add(_browseFastFileButton);
        _inspectTab.Controls.Add(_inputFastFile);
        _inspectTab.Controls.Add(ffLabel);

        // ----- Assets tab -----
        _assetsTab.Name = "assetsTab";
        _assetsTab.Padding = new Padding(8);
        _assetsTab.Text = "Zone Assets";
        _assetsTab.UseVisualStyleBackColor = true;

        _assetSummary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _assetSummary.AutoEllipsis = true;
        _assetSummary.Location = new Point(12, 12);
        _assetSummary.Name = "assetSummary";
        _assetSummary.Size = new Size(1140, 24);
        _assetSummary.Text = "Analyze a fastfile to enumerate its T4 XAssetList.";

        _exportManifestButton.Location = new Point(12, 42);
        _exportManifestButton.Name = "exportManifestButton";
        _exportManifestButton.Size = new Size(160, 32);
        _exportManifestButton.Text = "Export manifest JSON...";
        _exportManifestButton.Enabled = false;
        _exportManifestButton.UseVisualStyleBackColor = true;

        _menuProbeButton.Location = new Point(180, 42);
        _menuProbeButton.Name = "menuProbeButton";
        _menuProbeButton.Size = new Size(180, 32);
        _menuProbeButton.Text = "Menu/string probe";
        _menuProbeButton.UseVisualStyleBackColor = true;

        _roundTripButton.Location = new Point(368, 42);
        _roundTripButton.Name = "roundTripButton";
        _roundTripButton.Size = new Size(190, 32);
        _roundTripButton.Text = "Round-trip rebuild FF...";
        _roundTripButton.UseVisualStyleBackColor = true;

        _resolvePayloadsButton.Location = new Point(566, 42);
        _resolvePayloadsButton.Name = "resolvePayloadsButton";
        _resolvePayloadsButton.Size = new Size(175, 32);
        _resolvePayloadsButton.Text = "Resolve simple payloads";
        _resolvePayloadsButton.UseVisualStyleBackColor = true;

        _showMapstableButton.Location = new Point(749, 42);
        _showMapstableButton.Name = "showMapstableButton";
        _showMapstableButton.Size = new Size(160, 32);
        _showMapstableButton.Text = "Open mapstable";
        _showMapstableButton.Enabled = false;
        _showMapstableButton.UseVisualStyleBackColor = true;

        _menuStructuresButton.Location = new Point(917, 42);
        _menuStructuresButton.Name = "menuStructuresButton";
        _menuStructuresButton.Size = new Size(190, 32);
        _menuStructuresButton.Text = "Probe menuDef_t structs";
        _menuStructuresButton.UseVisualStyleBackColor = true;

        _traceCustomMapsButton.Location = new Point(12, 80);
        _traceCustomMapsButton.Name = "traceCustomMapsButton";
        _traceCustomMapsButton.Size = new Size(190, 32);
        _traceCustomMapsButton.Text = "Trace Custom Maps";
        _traceCustomMapsButton.UseVisualStyleBackColor = true;

        _stage3LabelProofButton.Location = new Point(210, 80);
        _stage3LabelProofButton.Name = "stage3LabelProofButton";
        _stage3LabelProofButton.Size = new Size(225, 32);
        _stage3LabelProofButton.Text = "Stage 3 label proof...";
        _stage3LabelProofButton.UseVisualStyleBackColor = true;

        _stage3AddLxcyButton.Location = new Point(443, 80);
        _stage3AddLxcyButton.Name = "stage3AddLxcyButton";
        _stage3AddLxcyButton.Size = new Size(225, 32);
        _stage3AddLxcyButton.Text = "Alpha 4e: classify second 62-pointer run...";
        _stage3AddLxcyButton.UseVisualStyleBackColor = true;

        _assets.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _assets.AllowUserToAddRows = false;
        _assets.AllowUserToDeleteRows = false;
        _assets.AllowUserToResizeRows = false;
        _assets.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _assets.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _assets.Location = new Point(12, 120);
        _assets.MultiSelect = false;
        _assets.Name = "assetsGrid";
        _assets.ReadOnly = true;
        _assets.RowHeadersVisible = false;
        _assets.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _assets.Size = new Size(1140, 540);
        _assets.Columns.Add(new DataGridViewTextBoxColumn { Name = "Index", HeaderText = "#", Width = 75, SortMode = DataGridViewColumnSortMode.NotSortable });
        _assets.Columns.Add(new DataGridViewTextBoxColumn { Name = "TypeId", HeaderText = "Type ID", Width = 80, SortMode = DataGridViewColumnSortMode.NotSortable });
        _assets.Columns.Add(new DataGridViewTextBoxColumn { Name = "AssetType", HeaderText = "Asset type", Width = 230, SortMode = DataGridViewColumnSortMode.NotSortable });
        _assets.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reference", HeaderText = "Header/reference", Width = 190, SortMode = DataGridViewColumnSortMode.NotSortable });
        _assets.Columns.Add(new DataGridViewTextBoxColumn { Name = "RawHeader", HeaderText = "Raw header", Width = 125, SortMode = DataGridViewColumnSortMode.NotSortable });
        _assets.Columns.Add(new DataGridViewTextBoxColumn { Name = "ResolvedName", HeaderText = "Resolved name", Width = 280, SortMode = DataGridViewColumnSortMode.NotSortable });
        _assets.Columns.Add(new DataGridViewTextBoxColumn { Name = "PayloadOffset", HeaderText = "Payload offset", Width = 120, SortMode = DataGridViewColumnSortMode.NotSortable });
        _assets.Columns.Add(new DataGridViewTextBoxColumn { Name = "Details", HeaderText = "Details", Width = 300, SortMode = DataGridViewColumnSortMode.NotSortable });

        _assetsTab.Controls.Add(_assets);
        _assetsTab.Controls.Add(_stage3AddLxcyButton);
        _assetsTab.Controls.Add(_stage3LabelProofButton);
        _assetsTab.Controls.Add(_traceCustomMapsButton);
        _assetsTab.Controls.Add(_menuStructuresButton);
        _assetsTab.Controls.Add(_showMapstableButton);
        _assetsTab.Controls.Add(_resolvePayloadsButton);
        _assetsTab.Controls.Add(_roundTripButton);
        _assetsTab.Controls.Add(_menuProbeButton);
        _assetsTab.Controls.Add(_exportManifestButton);
        _assetsTab.Controls.Add(_assetSummary);

        // ----- Settings tab -----
        _settingsTab.Name = "settingsTab";
        _settingsTab.Padding = new Padding(8);
        _settingsTab.Text = "Settings";
        _settingsTab.UseVisualStyleBackColor = true;

        var mapSourceLabel = MakeSettingsLabel("PC maps source folder", 18);
        var pcLabel = MakeSettingsLabel("PC World at War folder", 62);
        var xboxLabel = MakeSettingsLabel("Extracted Xbox 360 game folder", 106);
        var codxeLabel = MakeSettingsLabel("codxe folder", 150);
        var outputLabel = MakeSettingsLabel("Output folder", 194);
        var memoryLabel = MakeSettingsLabel("Analysis cache memory (MB)", 238);
        var parallelLabel = MakeSettingsLabel("Fast mode parallel maps (1-2)", 272);

        ConfigurePathTextBox(_mapSource, 18);
        ConfigurePathTextBox(_pcGame, 62);
        ConfigurePathTextBox(_xboxGame, 106);
        ConfigurePathTextBox(_codxe, 150);
        ConfigurePathTextBox(_output, 194);

        ConfigureBrowseButton(_browseMapSourceButton, 18);
        ConfigureBrowseButton(_browsePcGameButton, 62);
        ConfigureBrowseButton(_browseXboxGameButton, 106);
        ConfigureBrowseButton(_browseCodxeButton, 150);
        ConfigureBrowseButton(_browseOutputButton, 194);

        _memoryBudget.Location = new Point(238, 238);
        _memoryBudget.Minimum = 64;
        _memoryBudget.Maximum = 512;
        _memoryBudget.Value = 210;
        _memoryBudget.Name = "memoryBudget";
        _memoryBudget.Size = new Size(120, 23);

        _parallelMaps.Location = new Point(238, 272);
        _parallelMaps.Minimum = 1;
        _parallelMaps.Maximum = 2;
        _parallelMaps.Value = 2;
        _parallelMaps.Name = "parallelMaps";
        _parallelMaps.Size = new Size(120, 23);

        _importSettingsButton.Location = new Point(238, 318);
        _importSettingsButton.Name = "importSettingsButton";
        _importSettingsButton.Size = new Size(235, 34);
        _importSettingsButton.Text = "Import WaW360Tool settings.ini";
        _importSettingsButton.UseVisualStyleBackColor = true;

        _saveSettingsButton.Location = new Point(481, 318);
        _saveSettingsButton.Name = "saveSettingsButton";
        _saveSettingsButton.Size = new Size(130, 34);
        _saveSettingsButton.Text = "Save settings";
        _saveSettingsButton.UseVisualStyleBackColor = true;

        var settingsNote = new Label();
        settingsNote.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        settingsNote.AutoEllipsis = true;
        settingsNote.Location = new Point(238, 372);
        settingsNote.Name = "settingsNote";
        settingsNote.Size = new Size(914, 48);
        settingsNote.Text = "Linker V1: the batch now runs a deterministic load.ff graph stage immediately after analysis. XAsset order drives typed ownership; nested Images are promoted and exact IWD identities are resolved without global candidate scanning. Alpha10r remains as the legacy smoke-regression path. Output defaults to the Converter\\output folder.";

        _settingsTab.Controls.Add(settingsNote);
        _settingsTab.Controls.Add(_saveSettingsButton);
        _settingsTab.Controls.Add(_importSettingsButton);
        _settingsTab.Controls.Add(_parallelMaps);
        _settingsTab.Controls.Add(parallelLabel);
        _settingsTab.Controls.Add(_memoryBudget);
        _settingsTab.Controls.Add(memoryLabel);
        _settingsTab.Controls.Add(_browseOutputButton);
        _settingsTab.Controls.Add(_output);
        _settingsTab.Controls.Add(outputLabel);
        _settingsTab.Controls.Add(_browseCodxeButton);
        _settingsTab.Controls.Add(_codxe);
        _settingsTab.Controls.Add(codxeLabel);
        _settingsTab.Controls.Add(_browseXboxGameButton);
        _settingsTab.Controls.Add(_xboxGame);
        _settingsTab.Controls.Add(xboxLabel);
        _settingsTab.Controls.Add(_browsePcGameButton);
        _settingsTab.Controls.Add(_pcGame);
        _settingsTab.Controls.Add(pcLabel);
        _settingsTab.Controls.Add(_browseMapSourceButton);
        _settingsTab.Controls.Add(_mapSource);
        _settingsTab.Controls.Add(mapSourceLabel);

        // ----- Logs tab -----
        _logsTab.Name = "logsTab";
        _logsTab.Padding = new Padding(8);
        _logsTab.Text = "Logs";
        _logsTab.UseVisualStyleBackColor = true;

        _logs.BackColor = SystemColors.Window;
        _logs.BorderStyle = BorderStyle.FixedSingle;
        _logs.Dock = DockStyle.Fill;
        _logs.Font = new Font("Consolas", 9F);
        _logs.Name = "logs";
        _logs.ReadOnly = true;
        _logsTab.Controls.Add(_logs);

        // ----- Status -----
        _status.Text = "Ready";
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _progress.Name = "progress";
        _progress.Size = new Size(180, 16);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 28;
        _progress.Visible = false;
        _statusStrip.Items.AddRange(new ToolStripItem[] { _status, _progress });
        _statusStrip.Location = new Point(0, 728);
        _statusStrip.Name = "statusStrip";
        _statusStrip.Size = new Size(1180, 22);
        _statusStrip.TabIndex = 2;

        // ----- Form -----
        AllowDrop = true;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1180, 750);
        Controls.Add(_tabs);
        Controls.Add(_statusStrip);
        Controls.Add(_menu);
        Font = new Font("Segoe UI", 9F);
        MainMenuStrip = _menu;
        MinimumSize = new Size(980, 640);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "WaW360Converter - Linker V1 Alpha 4 Fast Track";

        ((System.ComponentModel.ISupportInitialize)_memoryBudget).EndInit();
        ((System.ComponentModel.ISupportInitialize)_parallelMaps).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    private static Label MakeSettingsLabel(string text, int y)
    {
        return new Label
        {
            Location = new Point(12, y),
            Size = new Size(215, 23),
            Text = text,
            TextAlign = ContentAlignment.MiddleRight
        };
    }

    private static void ConfigurePathTextBox(TextBox textBox, int y)
    {
        textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        textBox.Location = new Point(238, y);
        textBox.Size = new Size(784, 23);
    }

    private static void ConfigureBrowseButton(Button button, int y)
    {
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        button.Location = new Point(1030, y - 2);
        button.Size = new Size(122, 28);
        button.Text = "Browse...";
        button.UseVisualStyleBackColor = true;
    }
}
