using System.Diagnostics;

namespace Plutao;

public sealed class MainForm : Form
{
    private readonly ToolManager _tools = new();
    private readonly YtDlpRunner _runner;
    private CancellationTokenSource? _cts;

    private readonly TextBox txtUrls = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly RadioButton rbVideo = new() { Text = "Vídeo", Checked = true, AutoSize = true };
    private readonly RadioButton rbAudio = new() { Text = "Áudio", AutoSize = true };
    private readonly ComboBox cmbQuality = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cmbVideoFormat = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cmbAudioFormat = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cmbCookies = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cmbExisting = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cmbCollectionLimit = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly TextBox txtVideoOutput = new();
    private readonly TextBox txtAudioOutput = new();
    private readonly TextBox txtTemp = new();

    private readonly CheckBox chkPlaylist = new() { Text = "Permitir página/conta/canal/perfil completo", Checked = true, AutoSize = true };
    private readonly CheckBox chkOrganize = new() { Text = "Organizar por canal/criador", AutoSize = true };
    private readonly CheckBox chkMetadata = new() { Text = "Incorporar metadados", Checked = true, AutoSize = true };
    private readonly CheckBox chkThumb = new() { Text = "Salvar miniatura", AutoSize = true };
    private readonly CheckBox chkJson = new() { Text = "Salvar info JSON", AutoSize = true };
    private readonly CheckBox chkArchive = new() { Text = "Evitar repetir o mesmo link (histórico)", Checked = false, AutoSize = true };
    private readonly CheckBox chkCompatibleMp4 = new() { Text = "MP4 compatível (garantir H.264/AAC)", Checked = true, AutoSize = true };

    private readonly Button btnDownload = new() { Text = "BAIXAR", Height = 44 };
    private readonly Button btnStop = new() { Text = "PARAR", Height = 44, Enabled = false };
    private readonly Button btnPaste = new() { Text = "Colar" };
    private readonly Button btnBrowseVideo = new() { Text = "Procurar" };
    private readonly Button btnOpenVideo = new() { Text = "Abrir" };
    private readonly Button btnBrowseAudio = new() { Text = "Procurar" };
    private readonly Button btnOpenAudio = new() { Text = "Abrir" };
    private readonly Button btnBrowseTemp = new() { Text = "Procurar" };
    private readonly Button btnUpdate = new() { Text = "Atualizar componentes" };
    private readonly Button btnClearLog = new() { Text = "Limpar log" };
    private readonly Button btnAdvanced = new() { Text = "OPÇÕES AVANÇADAS", AutoSize = true };
    private readonly Button btnToggleLog = new() { Text = "MOSTRAR LOG", AutoSize = true };
    private readonly GroupBox grpAdvanced = new();
    private readonly GroupBox grpLog = new();
    private readonly ProfileSelectionPanel profileSelection = new();
    private readonly Button btnAnalyzeCollection = new() { Text = "VER / ANALISAR PERFIL", AutoSize = true };
    private readonly Label lblCollectionInfo = new() { Text = "Não analisado", AutoSize = true };
    private readonly Button btnOpenLastFile = new() { Text = "ABRIR ARQUIVO", Height = 44, Enabled = false };
    private readonly Button btnOpenLastFolder = new() { Text = "ABRIR PASTA", Height = 44, Enabled = false };

    private readonly DarkProgressBar progress = new() { Dock = DockStyle.Fill };
    private readonly TextBox txtLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly Label lblStatus = new() { Text = "Pronto", AutoSize = true };
    private readonly Label lblCurrent = new() { Text = "Aguardando download", AutoEllipsis = true, Dock = DockStyle.Fill };

    private string? _analyzedCollectionUrl;
    private string _selectedPlaylistItems = string.Empty;
    private readonly List<string> _selectedCollectionUrls = new();
    private bool _advancedVisible;
    private bool _logVisible;

    private static readonly Color Bg = Color.FromArgb(10, 10, 10);
    private static readonly Color Panel = Color.FromArgb(18, 18, 18);
    private static readonly Color Field = Color.FromArgb(28, 28, 28);
    private static readonly Color Border = Color.FromArgb(62, 62, 62);
    private static readonly Color TextMain = Color.FromArgb(240, 240, 240);
    private static readonly Color TextMuted = Color.FromArgb(180, 180, 180);
    private static readonly Color Accent = Color.FromArgb(111, 71, 255);

    public MainForm()
    {
        _runner = new YtDlpRunner(_tools);

        Text = "Plutao - Downloader Universal";
        Width = 1180;
        Height = 780;
        MinimumSize = new Size(860, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Bg;
        ForeColor = TextMain;

        BuildUi();
        ApplyDarkTheme(this);
        WireEvents();
        LoadDefaults();
    }

    private void BuildUi()
    {
        // O painel externo permite que a interface continue utilizável em telas
        // menores sem esmagar controles. Em telas grandes ele não mostra rolagem.
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Bg
        };
        Controls.Add(scrollHost);

        var root = new TableLayoutPanel
        {
            Name = "rootLayout",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 9,
            BackColor = Bg
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < root.RowCount; i++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        scrollHost.Controls.Add(root);

        // Links: mantém a área grande, mas um pouco mais compacta.
        var urlBox = CreateGroup("Links (um por linha)");
        urlBox.Name = "grpLinks";
        urlBox.AutoSize = false;
        urlBox.Height = 155;
        var urlLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };
        urlLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        urlLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        txtUrls.Dock = DockStyle.Fill;
        btnPaste.Dock = DockStyle.Top;
        btnPaste.Height = 32;
        urlLayout.Controls.Add(txtUrls, 0, 0);
        urlLayout.Controls.Add(btnPaste, 1, 0);
        urlBox.Controls.Add(urlLayout);
        root.Controls.Add(urlBox);

        // Formato usa FlowLayout: quando a largura/DPI muda, os controles quebram
        // de linha em vez de ficarem espremidos ou cortados.
        var formatBox = CreateGroup("Formato");
        formatBox.Name = "grpFormat";
        var format = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 7, 8, 7)
        };
        rbVideo.Margin = new Padding(4, 6, 8, 4);
        rbAudio.Margin = new Padding(4, 6, 18, 4);
        cmbQuality.Width = 145;
        cmbVideoFormat.Width = 135;
        cmbAudioFormat.Width = 165;
        format.Controls.Add(rbVideo);
        format.Controls.Add(rbAudio);
        format.Controls.Add(LabelFor("Qualidade:"));
        format.Controls.Add(cmbQuality);
        format.Controls.Add(LabelFor("Contêiner:"));
        format.Controls.Add(cmbVideoFormat);
        format.Controls.Add(LabelFor("Áudio:"));
        format.Controls.Add(cmbAudioFormat);
        formatBox.Controls.Add(format);
        root.Controls.Add(formatBox);

        // Na tela principal ficam apenas as escolhas usadas o tempo todo.
        var mainOptions = CreateGroup("Destino e conta/página");
        mainOptions.Name = "grpMainOptions";
        var opts = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 4,
            Padding = new Padding(8)
        };
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));

        AddFolderRow(opts, 0, "Vídeos:", txtVideoOutput, btnBrowseVideo, btnOpenVideo);
        AddFolderRow(opts, 1, "Áudios:", txtAudioOutput, btnBrowseAudio, btnOpenAudio);

        opts.Controls.Add(LabelFor("Conta/página:"), 0, 2);
        opts.Controls.Add(chkPlaylist, 1, 2);
        var collectionLimitPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        collectionLimitPanel.Controls.Add(LabelFor("Limite:"));
        cmbCollectionLimit.Width = 90;
        collectionLimitPanel.Controls.Add(cmbCollectionLimit);
        opts.Controls.Add(collectionLimitPanel, 2, 2);
        opts.SetColumnSpan(collectionLimitPanel, 2);

        opts.Controls.Add(LabelFor("Seleção:"), 0, 3);
        var collectionSelectPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0)
        };
        collectionSelectPanel.Controls.Add(btnAnalyzeCollection);
        collectionSelectPanel.Controls.Add(lblCollectionInfo);
        opts.Controls.Add(collectionSelectPanel, 1, 3);
        opts.SetColumnSpan(collectionSelectPanel, 3);

        mainOptions.Controls.Add(opts);
        root.Controls.Add(mainOptions);

        // Resultado da análise fica dentro da própria janela. Não abre mais uma janela separada.
        profileSelection.Name = "profileSelection";
        profileSelection.Visible = false;
        root.Controls.Add(profileSelection);

        // Opções menos usadas ficam recolhidas para limpar a tela principal.
        grpAdvanced.Text = "Opções avançadas";
        grpAdvanced.Dock = DockStyle.Top;
        grpAdvanced.AutoSize = true;
        grpAdvanced.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        grpAdvanced.Padding = new Padding(4);
        var advanced = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 5,
            Padding = new Padding(8)
        };
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));

        advanced.Controls.Add(LabelFor("Temporários:"), 0, 0);
        txtTemp.Dock = DockStyle.Fill;
        advanced.Controls.Add(txtTemp, 1, 0);
        btnBrowseTemp.Dock = DockStyle.Fill;
        advanced.Controls.Add(btnBrowseTemp, 2, 0);

        advanced.Controls.Add(LabelFor("Cookies:"), 0, 1);
        cmbCookies.Width = 180;
        cmbCookies.Dock = DockStyle.Left;
        advanced.Controls.Add(cmbCookies, 1, 1);
        advanced.SetColumnSpan(cmbCookies, 2);

        advanced.Controls.Add(LabelFor("Se já existir:"), 0, 2);
        cmbExisting.Width = 240;
        cmbExisting.Dock = DockStyle.Left;
        advanced.Controls.Add(cmbExisting, 1, 2);
        advanced.SetColumnSpan(cmbExisting, 2);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 0)
        };
        checks.Controls.Add(chkOrganize);
        checks.Controls.Add(chkMetadata);
        checks.Controls.Add(chkThumb);
        checks.Controls.Add(chkJson);
        checks.Controls.Add(chkArchive);
        checks.Controls.Add(chkCompatibleMp4);
        advanced.Controls.Add(checks, 0, 3);
        advanced.SetColumnSpan(checks, 3);

        var advancedActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 0)
        };
        advancedActions.Controls.Add(btnUpdate);
        advancedActions.Controls.Add(btnClearLog);
        advanced.Controls.Add(advancedActions, 0, 4);
        advanced.SetColumnSpan(advancedActions, 3);
        grpAdvanced.Controls.Add(advanced);
        root.Controls.Add(grpAdvanced);

        // Uma linha pequena concentra comandos de visualização e status.
        var toolsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Padding = new Padding(0, 4, 0, 4)
        };
        toolsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolsRow.Controls.Add(btnAdvanced, 0, 0);
        toolsRow.Controls.Add(btnToggleLog, 1, 0);
        lblStatus.Anchor = AnchorStyles.Right;
        toolsRow.Controls.Add(lblStatus, 3, 0);
        root.Controls.Add(toolsRow);

        var progressBox = CreateGroup("Progresso");
        progressBox.Name = "grpProgress";
        progressBox.AutoSize = false;
        progressBox.Height = 102;
        var progressLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lblCurrent.ForeColor = TextMuted;
        progressLayout.Controls.Add(lblCurrent, 0, 0);
        progressLayout.Controls.Add(progress, 0, 1);
        progressBox.Controls.Add(progressLayout);
        root.Controls.Add(progressBox);

        var actions = new TableLayoutPanel
        {
            Name = "actionsRow",
            Dock = DockStyle.Top,
            Height = 62,
            ColumnCount = 4,
            Padding = new Padding(0, 4, 0, 4)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        btnDownload.Dock = DockStyle.Fill;
        btnStop.Dock = DockStyle.Fill;
        btnOpenLastFile.Dock = DockStyle.Fill;
        btnOpenLastFolder.Dock = DockStyle.Fill;
        actions.Controls.Add(btnDownload, 0, 0);
        actions.Controls.Add(btnStop, 1, 0);
        actions.Controls.Add(btnOpenLastFile, 2, 0);
        actions.Controls.Add(btnOpenLastFolder, 3, 0);
        root.Controls.Add(actions);

        grpLog.Name = "grpLog";
        grpLog.Text = "Log";
        grpLog.Dock = DockStyle.Top;
        grpLog.AutoSize = false;
        grpLog.Height = 220;
        grpLog.Padding = new Padding(4);
        txtLog.Dock = DockStyle.Fill;
        grpLog.Controls.Add(txtLog);
        root.Controls.Add(grpLog);

        SetAdvancedVisible(false);
        SetLogVisible(false);
    }

    private void SetAdvancedVisible(bool visible)
    {
        _advancedVisible = visible;
        grpAdvanced.Visible = visible;
        btnAdvanced.Text = visible ? "OCULTAR OPÇÕES" : "OPÇÕES AVANÇADAS";
    }

    private void SetLogVisible(bool visible)
    {
        _logVisible = visible;
        grpLog.Visible = visible;
        btnToggleLog.Text = visible ? "OCULTAR LOG" : "MOSTRAR LOG";
    }

    private void ApplyResponsiveSpacing()
    {
        if (!IsHandleCreated)
            return;

        var area = Screen.FromControl(this).WorkingArea;
        var compact = area.Height <= 820 || area.Width <= 1400;
        var roomy = area.Height >= 1000 && area.Width >= 1600;

        SetNamedHeight("grpLinks", compact ? 125 : roomy ? 175 : 155);
        SetNamedHeight("grpProgress", compact ? 86 : roomy ? 112 : 102);
        SetNamedHeight("actionsRow", compact ? 54 : roomy ? 66 : 62);
        SetNamedHeight("grpLog", compact ? 200 : roomy ? 300 : 240);
        profileSelection.ApplyResponsiveHeight(compact, roomy);

        var root = Controls.Find("rootLayout", true).FirstOrDefault();
        if (root is TableLayoutPanel layout)
        {
            layout.Padding = new Padding(compact ? 10 : 14);
            foreach (Control child in layout.Controls)
                child.Margin = new Padding(0, 0, 0, compact ? 5 : 8);
        }
    }

    private void SetNamedHeight(string name, int height)
    {
        var control = Controls.Find(name, true).FirstOrDefault();
        if (control is not null)
            control.Height = height;
    }

    private void FitToCurrentScreen()
    {
        if (!IsHandleCreated)
            return;

        ApplyResponsiveSpacing();

        if (WindowState != FormWindowState.Normal)
            return;

        var area = Screen.FromControl(this).WorkingArea;
        var maxWidth = Math.Max(MinimumSize.Width, area.Width - 24);
        var maxHeight = Math.Max(MinimumSize.Height, area.Height - 24);
        var width = Math.Min(Width, maxWidth);
        var height = Math.Min(Height, maxHeight);

        if (width != Width || height != Height)
            Size = new Size(width, height);

        var x = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        var y = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        if (x != Left || y != Top)
            Location = new Point(x, y);
    }

    private static void AddFolderRow(TableLayoutPanel layout, int row, string label, TextBox textBox, Button browse, Button open)
    {
        layout.Controls.Add(LabelFor(label), 0, row);
        textBox.Dock = DockStyle.Fill;
        layout.Controls.Add(textBox, 1, row);
        browse.Dock = DockStyle.Fill;
        open.Dock = DockStyle.Fill;
        layout.Controls.Add(browse, 2, row);
        layout.Controls.Add(open, 3, row);
    }

    private void LoadDefaults()
    {
        cmbQuality.Items.AddRange(new object[] { "Melhor", "2160p", "1440p", "1080p", "720p", "480p", "360p" });
        cmbQuality.SelectedItem = "1080p";

        cmbVideoFormat.Items.AddRange(new object[] { "mp4", "mkv", "webm" });
        cmbVideoFormat.SelectedItem = "mp4";

        cmbAudioFormat.Items.AddRange(new object[] { "mp3", "m4a", "aac", "flac", "wav", "opus" });
        cmbAudioFormat.SelectedItem = "mp3";

        cmbCookies.Items.AddRange(new object[] { "Nenhum", "Edge", "Chrome", "Firefox" });
        cmbCookies.SelectedIndex = 0;

        cmbExisting.Items.AddRange(new object[] { "Manter existente", "Substituir", "Manter os dois (novo nome)" });
        cmbExisting.SelectedIndex = 2;

        cmbCollectionLimit.Items.AddRange(new object[] { "Todos", "10", "25", "50", "100", "200" });
        cmbCollectionLimit.SelectedIndex = 0;

        // Destinos de Vídeo e Áudio não são mais presos a nenhum disco.
        // Em uma instalação nova eles ficam vazios; depois que o usuário escolhe
        // manualmente uma pasta, o Plutao lembra dela nas próximas aberturas.
        var savedSettings = AppSettings.Load();
        txtVideoOutput.PlaceholderText = "Escolha uma pasta para os vídeos...";
        txtAudioOutput.PlaceholderText = "Escolha uma pasta para os áudios...";
        txtVideoOutput.Text = savedSettings.VideoOutputDirectory;
        txtAudioOutput.Text = savedSettings.AudioOutputDirectory;
        txtTemp.Text = _tools.TemporaryDirectory;

        foreach (var box in new[] { txtVideoOutput, txtAudioOutput, txtTemp })
        {
            box.AutoSize = false;
            box.Height = 30;
            box.Margin = new Padding(3, 4, 3, 4);
        }

        chkOrganize.Checked = false;
        chkPlaylist.Checked = true;
        chkArchive.Checked = false;
        chkCompatibleMp4.Checked = true;
        lblCollectionInfo.Text = "Não analisado";
        lblCollectionInfo.ForeColor = TextMuted;
        progress.Value = 0;
        progress.DisplayText = "0%";
        UpdateModeUi();
    }

    private void WireEvents()
    {
        rbVideo.CheckedChanged += (_, _) => UpdateModeUi();
        rbAudio.CheckedChanged += (_, _) => UpdateModeUi();
        cmbVideoFormat.SelectedIndexChanged += (_, _) => UpdateModeUi();
        chkPlaylist.CheckedChanged += (_, _) =>
        {
            UpdateModeUi();
            if (!chkPlaylist.Checked) ResetCollectionSelection();
        };
        cmbCollectionLimit.SelectedIndexChanged += (_, _) => ResetCollectionSelection();
        cmbCookies.SelectedIndexChanged += (_, _) => ResetCollectionSelection();
        txtUrls.TextChanged += (_, _) => ResetCollectionSelection();
        btnPaste.Click += (_, _) => { if (Clipboard.ContainsText()) txtUrls.Text = Clipboard.GetText(); };
        btnAnalyzeCollection.Click += async (_, _) => await AnalyzeCollectionAsync();
        profileSelection.SelectionChanged += (_, _) => ApplyProfileSelection();
        profileSelection.HideRequested += (_, _) => profileSelection.Visible = false;

        btnBrowseVideo.Click += (_, _) =>
        {
            BrowseFolder(txtVideoOutput, "Escolha a pasta dos vídeos");
            SaveDestinationSettings();
        };
        btnOpenVideo.Click += (_, _) => OpenFolder(txtVideoOutput.Text);
        btnBrowseAudio.Click += (_, _) =>
        {
            BrowseFolder(txtAudioOutput, "Escolha a pasta dos áudios");
            SaveDestinationSettings();
        };
        btnOpenAudio.Click += (_, _) => OpenFolder(txtAudioOutput.Text);
        btnBrowseTemp.Click += (_, _) => BrowseFolder(txtTemp, "Escolha a pasta para arquivos temporários");
        txtVideoOutput.Leave += (_, _) => SaveDestinationSettings();
        txtAudioOutput.Leave += (_, _) => SaveDestinationSettings();

        btnDownload.Click += async (_, _) => await StartDownloadAsync();
        btnStop.Click += (_, _) => StopDownload();
        btnUpdate.Click += async (_, _) => await RunToolActionAsync();
        btnClearLog.Click += (_, _) => txtLog.Clear();
        btnAdvanced.Click += (_, _) => SetAdvancedVisible(!_advancedVisible);
        btnToggleLog.Click += (_, _) => SetLogVisible(!_logVisible);
        Shown += (_, _) => FitToCurrentScreen();
        DpiChanged += (_, _) => BeginInvoke(new Action(FitToCurrentScreen));
        LocationChanged += (_, _) => BeginInvoke(new Action(ApplyResponsiveSpacing));
        ResizeEnd += (_, _) => ApplyResponsiveSpacing();
        btnOpenLastFile.Click += (_, _) => OpenLastFile();
        btnOpenLastFolder.Click += (_, _) => OpenLastFileFolder();
        FormClosing += (_, _) =>
        {
            SaveDestinationSettings();
            StopDownload();
        };
    }

    private void SaveDestinationSettings()
    {
        AppSettings.Save(new AppSettings
        {
            VideoOutputDirectory = txtVideoOutput.Text.Trim(),
            AudioOutputDirectory = txtAudioOutput.Text.Trim()
        });
    }

    private void UpdateModeUi()
    {
        cmbQuality.Enabled = rbVideo.Checked;
        cmbVideoFormat.Enabled = rbVideo.Checked;
        cmbAudioFormat.Enabled = rbAudio.Checked;
        chkCompatibleMp4.Enabled = rbVideo.Checked && string.Equals(cmbVideoFormat.SelectedItem?.ToString(), "mp4", StringComparison.OrdinalIgnoreCase);
        cmbCollectionLimit.Enabled = chkPlaylist.Checked;
        btnAnalyzeCollection.Enabled = chkPlaylist.Checked;

        txtVideoOutput.ForeColor = rbVideo.Checked ? TextMain : TextMuted;
        txtAudioOutput.ForeColor = rbAudio.Checked ? TextMain : TextMuted;
    }

    private void BrowseFolder(TextBox target, string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            InitialDirectory = target.Text,
            UseDescriptionForTitle = true,
            Description = description
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            target.Text = dialog.SelectedPath;
    }

    private void OpenFolder(string path)
    {
        try
        {
            path = path.Trim();
            if (string.IsNullOrWhiteSpace(path)) return;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetCollectionSelection()
    {
        _analyzedCollectionUrl = null;
        _selectedPlaylistItems = string.Empty;
        _selectedCollectionUrls.Clear();
        profileSelection.ClearData();
        if (!IsDisposed)
            lblCollectionInfo.Text = "Não analisado";
    }

    private void ApplyProfileSelection()
    {
        if (!profileSelection.HasData || string.IsNullOrWhiteSpace(_analyzedCollectionUrl))
            return;

        var selectedItems = profileSelection.SelectedItems.OrderBy(x => x.Index).ToArray();
        _selectedPlaylistItems = string.Empty;
        _selectedCollectionUrls.Clear();

        if (selectedItems.Length > 0)
        {
            if (selectedItems.All(item => !string.IsNullOrWhiteSpace(item.Url)))
            {
                _selectedCollectionUrls.AddRange(
                    selectedItems.Select(item => item.Url).Distinct(StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                _selectedPlaylistItems = string.Join(",", selectedItems.Select(item => item.Index));
            }
        }

        lblCollectionInfo.Text = $"{profileSelection.TotalItems} encontrados • {selectedItems.Length} selecionados";
    }

    private async Task AnalyzeCollectionAsync()
    {
        var urls = txtUrls.Lines
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        if (urls.Count != 1)
        {
            MessageBox.Show(this, "Para analisar uma conta/página, deixe exatamente um link na caixa de links.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!chkPlaylist.Checked)
            chkPlaylist.Checked = true;

        _tools.TemporaryDirectory = txtTemp.Text.Trim();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        lblStatus.Text = "Analisando...";
        lblStatus.ForeColor = TextMuted;
        lblCurrent.Text = "Listando vídeos da página/conta...";
        AppendLog($"[Análise] Iniciando: {urls[0]}");

        try
        {
            var items = await _runner.AnalyzeCollectionAsync(
                urls[0],
                cmbCookies.SelectedItem?.ToString() ?? "Nenhum",
                SelectedCollectionLimit(),
                LogProgress(),
                new Progress<DownloadProgressInfo>(SetProgressUi),
                _cts.Token);

            if (items.Count == 0)
            {
                lblCollectionInfo.Text = "0 vídeos encontrados";
                SetLogVisible(true);
                MessageBox.Show(this, "Nenhum vídeo foi encontrado. Confira o log; alguns perfis exigem Cookies do navegador.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _analyzedCollectionUrl = urls[0];
            profileSelection.LoadData(items, _runner.LastAnalyzedProfile);
            ApplyResponsiveSpacing();
            ApplyProfileSelection();

            lblCollectionInfo.Text = $"{items.Count} encontrados • {items.Count} selecionados";
            AppendLog($"[Análise] {items.Count} encontrados; seleção exibida na tela principal.");

            lblStatus.Text = "Análise concluída";
            lblStatus.ForeColor = Color.FromArgb(102, 220, 145);
            lblCurrent.Text = $"Encontrados {items.Count} vídeo(s).";
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = "Cancelado";
            lblStatus.ForeColor = Color.FromArgb(255, 184, 77);
            lblCurrent.Text = "Análise cancelada.";
            AppendLog("[Análise] Cancelada.");
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Erro";
            lblStatus.ForeColor = Color.FromArgb(255, 92, 92);
            lblCurrent.Text = "Não foi possível listar os vídeos.";
            SetLogVisible(true);
            AppendLog("[Análise] ERRO: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task StartDownloadAsync()
    {
        var urls = txtUrls.Lines
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        if (urls.Count == 0)
        {
            MessageBox.Show(this, "Cole pelo menos um link.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sameAnalyzedProfile = chkPlaylist.Checked && urls.Count == 1 &&
                                  string.Equals(urls[0], _analyzedCollectionUrl, StringComparison.Ordinal) &&
                                  profileSelection.HasData;

        if (sameAnalyzedProfile && profileSelection.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "Selecione pelo menos um vídeo do perfil antes de baixar.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var usingSelectedUrls = sameAnalyzedProfile && _selectedCollectionUrls.Count > 0;
        var downloadUrls = usingSelectedUrls ? _selectedCollectionUrls.ToList() : urls;

        var selectedOutput = rbVideo.Checked ? txtVideoOutput.Text.Trim() : txtAudioOutput.Text.Trim();
        if (string.IsNullOrWhiteSpace(selectedOutput) || string.IsNullOrWhiteSpace(txtTemp.Text))
        {
            MessageBox.Show(this, "Escolha a pasta de destino e a pasta de temporários.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var options = new DownloadOptions
        {
            Mode = rbVideo.Checked ? DownloadMode.Video : DownloadMode.Audio,
            Quality = cmbQuality.SelectedItem?.ToString() ?? "1080p",
            VideoContainer = cmbVideoFormat.SelectedItem?.ToString() ?? "mp4",
            AudioFormat = cmbAudioFormat.SelectedItem?.ToString() ?? "mp3",
            OutputDirectory = selectedOutput,
            TemporaryDirectory = txtTemp.Text.Trim(),
            BrowserCookies = cmbCookies.SelectedItem?.ToString() ?? "Nenhum",
            AllowPlaylists = usingSelectedUrls ? false : chkPlaylist.Checked,
            CollectionLimit = SelectedCollectionLimit(),
            SelectedPlaylistItems = !usingSelectedUrls && sameAnalyzedProfile
                ? _selectedPlaylistItems
                : string.Empty,
            EmbedMetadata = chkMetadata.Checked,
            SaveThumbnail = chkThumb.Checked,
            SaveInfoJson = chkJson.Checked,
            UseArchive = chkArchive.Checked,
            OrganizeByCreator = chkOrganize.Checked,
            PreferCompatibleMp4 = chkCompatibleMp4.Checked,
            ExistingFileBehavior = SelectedExistingFileBehavior()
        };

        _tools.TemporaryDirectory = options.TemporaryDirectory;
        _cts = new CancellationTokenSource();
        SetBusy(true);
        SetProgressUi(new DownloadProgressInfo(0, urls.Count, 0, "", "", "", "Preparando"));
        AppendLog($"Iniciando {downloadUrls.Count} link(s)...");
        if (downloadUrls.Count == 1 && YtDlpRunner.IsLikelySingleMediaUrl(downloadUrls[0]))
            AppendLog("Detecção automática: mídia individual; o modo de página/playlist será ignorado para acelerar o início.");
        if (usingSelectedUrls)
            AppendLog($"Seleção individual: {downloadUrls.Count} mídia(s) da conta serão baixadas por URL individual.");
        AppendLog($"Destino: {options.OutputDirectory}");
        AppendLog($"Organização por canal/criador: {(options.OrganizeByCreator ? "ativada" : "desativada")}");
        AppendLog(options.AllowPlaylists
            ? $"Página/conta/canal/perfil completo: ativado (limite: {(options.CollectionLimit > 0 ? options.CollectionLimit.ToString() : "todos")})"
            : "Página/conta/canal/perfil completo: desativado");
        if (!string.IsNullOrWhiteSpace(options.SelectedPlaylistItems))
            AppendLog($"Seleção individual ativa: {options.SelectedPlaylistItems.Split(',').Length} item(ns).");
        AppendLog($"Arquivo existente: {ExistingBehaviorLabel(options.ExistingFileBehavior)}");
        if (options.UseArchive)
            AppendLog("Histórico anti-repetição: ativado (o mesmo ID pode ser ignorado mesmo ao mudar qualidade/formato).");
        if (options.Mode == DownloadMode.Video && string.Equals(options.VideoContainer, "mp4", StringComparison.OrdinalIgnoreCase))
            AppendLog($"Compatibilidade MP4: {(options.PreferCompatibleMp4 ? "garantir H.264/AAC" : "melhor codec disponível")}");

        try
        {
            var code = await _runner.DownloadAsync(
                downloadUrls,
                options,
                LogProgress(),
                new Progress<DownloadProgressInfo>(SetProgressUi),
                _cts.Token);

            if (code == 0)
            {
                lblStatus.Text = "Concluído";
                lblStatus.ForeColor = Color.FromArgb(102, 220, 145);
                progress.Value = 100;
                progress.DisplayText = "100% - concluído";
                progress.Invalidate();
                lblCurrent.Text = "Download concluído.";
                AppendLog("Fila concluída com sucesso.");
                if (!string.IsNullOrWhiteSpace(_runner.LastCompletedFilePath))
                    AppendLog($"Último arquivo: {_runner.LastCompletedFilePath}");
            }
            else
            {
                lblStatus.Text = "Concluído com erro";
                lblStatus.ForeColor = Color.FromArgb(255, 184, 77);
                lblCurrent.Text = "Um ou mais links falharam. Confira o log.";
                SetLogVisible(true);
                AppendLog("Fila concluída com erro(s). Confira as mensagens acima.");
            }
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = "Cancelado";
            lblStatus.ForeColor = Color.FromArgb(255, 184, 77);
            lblCurrent.Text = "Download cancelado.";
            AppendLog("Download cancelado.");
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Erro";
            lblStatus.ForeColor = Color.FromArgb(255, 92, 92);
            lblCurrent.Text = "Erro durante o download.";
            SetLogVisible(true);
            AppendLog("ERRO: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private int SelectedCollectionLimit()
    {
        if (!chkPlaylist.Checked) return 0;
        var text = cmbCollectionLimit.SelectedItem?.ToString();
        return int.TryParse(text, out var value) ? value : 0;
    }

    private ExistingFileBehavior SelectedExistingFileBehavior()
        => cmbExisting.SelectedIndex switch
        {
            0 => ExistingFileBehavior.KeepExisting,
            1 => ExistingFileBehavior.Replace,
            _ => ExistingFileBehavior.KeepBoth
        };

    private static string ExistingBehaviorLabel(ExistingFileBehavior behavior)
        => behavior switch
        {
            ExistingFileBehavior.KeepExisting => "manter existente",
            ExistingFileBehavior.Replace => "substituir",
            _ => "manter os dois"
        };

    private void OpenLastFile()
    {
        var path = _runner.LastCompletedFilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(this, "O último arquivo concluído não foi encontrado.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenLastFileFolder()
    {
        var path = _runner.LastCompletedFilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(this, "O último arquivo concluído não foi encontrado.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopDownload()
    {
        _cts?.Cancel();
        _runner.Stop();
    }

    private async Task RunToolActionAsync()
    {
        try
        {
            _tools.TemporaryDirectory = txtTemp.Text.Trim();
            SetBusy(true);
            lblStatus.Text = "Atualizando...";
            lblStatus.ForeColor = TextMuted;
            lblCurrent.Text = "Atualizando yt-dlp, FFmpeg e Deno...";
            _cts = new CancellationTokenSource();
            await _tools.UpdateAllAsync(
                LogProgress(),
                new Progress<DownloadProgressInfo>(SetProgressUi),
                _cts.Token);
            lblStatus.Text = "Componentes prontos";
            lblStatus.ForeColor = Color.FromArgb(102, 220, 145);
            lblCurrent.Text = "yt-dlp, FFmpeg e Deno estão prontos.";
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = "Cancelado";
            lblStatus.ForeColor = Color.FromArgb(255, 184, 77);
            lblCurrent.Text = "Atualização de componentes cancelada.";
            AppendLog("Atualização de componentes cancelada.");
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Erro";
            lblStatus.ForeColor = Color.FromArgb(255, 92, 92);
            SetLogVisible(true);
            AppendLog("ERRO: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private IProgress<string> LogProgress() => new Progress<string>(AppendLog);

    private void AppendLog(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(text));
            return;
        }

        txtLog.AppendText(text + Environment.NewLine);
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.ScrollToCaret();
    }

    private void SetProgressUi(DownloadProgressInfo info)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetProgressUi(info));
            return;
        }

        var overall = info.ItemIndex <= 0 ? info.Percent : info.OverallPercent;
        progress.Value = overall;

        var parts = new List<string>();
        if (info.ItemIndex > 0 && info.ItemCount > 0)
            parts.Add($"Link {info.ItemIndex}/{info.ItemCount}");
        if (info.CollectionIndex > 0)
            parts.Add(info.CollectionCount > 0
                ? $"Mídia {info.CollectionIndex}/{info.CollectionCount}"
                : $"Mídia {info.CollectionIndex}");
        parts.Add($"{info.Percent:0.0}%");
        if (!string.IsNullOrWhiteSpace(info.Speed)) parts.Add(info.Speed);
        if (!string.IsNullOrWhiteSpace(info.Eta)) parts.Add("ETA " + info.Eta);

        progress.DisplayText = string.Join("  •  ", parts);
        progress.Invalidate();

        var title = string.IsNullOrWhiteSpace(info.Title) ? string.Empty : $" — {info.Title}";
        lblCurrent.Text = $"{info.Stage}{title}";
        lblStatus.Text = info.Stage;
        lblStatus.ForeColor = TextMuted;
    }

    private void SetBusy(bool busy)
    {
        btnDownload.Enabled = !busy;
        btnStop.Enabled = busy;
        btnUpdate.Enabled = !busy;
        btnBrowseVideo.Enabled = !busy;
        btnBrowseAudio.Enabled = !busy;
        btnBrowseTemp.Enabled = !busy;
        btnAnalyzeCollection.Enabled = !busy && chkPlaylist.Checked;
        btnAdvanced.Enabled = !busy;
        var hasLastFile = !string.IsNullOrWhiteSpace(_runner.LastCompletedFilePath) && File.Exists(_runner.LastCompletedFilePath);
        btnOpenLastFile.Enabled = !busy && hasLastFile;
        btnOpenLastFolder.Enabled = !busy && hasLastFile;
    }

    private static GroupBox CreateGroup(string title, bool fill = false)
        => new()
        {
            Text = title,
            Dock = fill ? DockStyle.Fill : DockStyle.Top,
            AutoSize = !fill,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4)
        };

    private static Label LabelFor(string text)
        => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 7, 6, 3) };

    private static void SetFill(params ComboBox[] controls)
    {
        foreach (var control in controls)
            control.Dock = DockStyle.Fill;
    }

    private static void ApplyDarkTheme(Control root)
    {
        root.BackColor = root is TextBox or ComboBox ? Field : Bg;
        root.ForeColor = TextMain;

        switch (root)
        {
            case GroupBox:
            case TableLayoutPanel:
            case FlowLayoutPanel:
                root.BackColor = Panel;
                break;

            case TextBox textBox:
                textBox.BackColor = Field;
                textBox.ForeColor = TextMain;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;

            case ComboBox comboBox:
                comboBox.BackColor = Field;
                comboBox.ForeColor = TextMain;
                comboBox.FlatStyle = FlatStyle.Flat;
                break;

            case Button button:
                button.BackColor = Field;
                button.ForeColor = TextMain;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 42, 42);
                button.FlatAppearance.MouseDownBackColor = Accent;
                break;

            case CheckBox checkBox:
                checkBox.BackColor = Panel;
                checkBox.ForeColor = TextMain;
                break;

            case RadioButton radioButton:
                radioButton.BackColor = Panel;
                radioButton.ForeColor = TextMain;
                break;
        }

        foreach (Control child in root.Controls)
            ApplyDarkTheme(child);
    }
}
