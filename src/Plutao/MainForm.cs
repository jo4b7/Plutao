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

    private readonly Button btnDownload = new() { Text = "BAIXAR", Height = 44 };
    private readonly Button btnStop = new() { Text = "PARAR", Height = 44, Enabled = false };
    private readonly Button btnPaste = new() { Text = "Colar" };
    private readonly Button btnBrowseVideo = new() { Text = "Procurar" };
    private readonly Button btnOpenVideo = new() { Text = "Abrir" };
    private readonly Button btnBrowseAudio = new() { Text = "Procurar" };
    private readonly Button btnOpenAudio = new() { Text = "Abrir" };
    private readonly Button btnBrowseTemp = new() { Text = "Procurar" };
    private readonly Button btnUpdate = new() { Text = "Atualizar componentes", AutoSize = true, MinimumSize = new Size(180, 34) };
    private readonly Button btnClearLog = new() { Text = "Limpar log" };
    private readonly Button btnAdvanced = new() { Text = "Configurações", AutoSize = true };
    private readonly Button btnToggleLog = new() { Text = "Diagnóstico", AutoSize = true };
    private readonly Button btnToggleResults = new() { Text = "Mostrar resultados", AutoSize = true, Visible = false };
    private readonly Button btnCloseSettings = new() { Text = "FECHAR", AutoSize = true };
    private readonly GroupBox grpAdvanced = new();
    private readonly GroupBox grpLog = new();
    private readonly ProfileSelectionPanel profileSelection = new();
    private readonly TableLayoutPanel profileWorkspace = new();
    private readonly Panel settingsDrawer = new();
    private readonly Label lblSettingsBusyHint = new()
    {
        Text = "Execução em andamento • a tarefa atual mantém as opções do início; alterações valem para a próxima.",
        AutoSize = true,
        ForeColor = Color.FromArgb(255, 194, 92),
        Visible = false
    };
    private readonly Button btnAnalyzeCollection = new() { Text = "ANALISAR PERFIL / CANAL", AutoSize = true };
    private readonly Label lblCollectionInfo = new() { Text = "Não analisado", AutoSize = true };
    private readonly Button btnOpenLastFile = new() { Text = "ABRIR ARQUIVO", Height = 44, AutoSize = true, MinimumSize = new Size(150, 44), Enabled = false };
    private readonly Button btnOpenLastFolder = new() { Text = "ABRIR PASTA", Height = 44, AutoSize = true, MinimumSize = new Size(140, 44), Enabled = false };

    private readonly DarkProgressBar progress = new() { Dock = DockStyle.Fill };
    private readonly TextBox txtLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly Label lblStatus = new() { Text = "Pronto", AutoSize = true };
    private readonly Label lblCurrent = new() { Text = "Aguardando download", AutoEllipsis = true, Dock = DockStyle.Fill };

    private readonly Label lblLinkHint = new() { Text = "Cole um link para começar", AutoSize = true };
    private readonly PictureBox picPreview = new()
    {
        Width = 150,
        Height = 86,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(26, 26, 26),
        Visible = false
    };
    private readonly Label lblPreviewTitle = new() { Text = "Cole um link para começar", AutoSize = true, AutoEllipsis = true };
    private readonly Label lblPreviewMeta = new() { Text = "Vídeo, áudio, perfil, canal ou playlist", AutoSize = true };
    private readonly Label lblPreviewDetails = new() { Text = "", AutoSize = true };
    private readonly Label lblDestination = new() { Text = "Destino não definido", AutoSize = false, AutoEllipsis = true };
    private readonly Button btnChangeDestination = new() { Text = "Alterar", AutoSize = true };
    private readonly Button btnOpenDownloads = new() { Text = "Abrir downloads", AutoSize = true };
    private readonly Label lblQualityCaption = new() { Text = "Qualidade", AutoSize = true };
    private readonly Label lblVideoFormatCaption = new() { Text = "Formato", AutoSize = true };
    private readonly Label lblAudioFormatCaption = new() { Text = "Formato", AutoSize = true };
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 700 };
    private readonly System.Windows.Forms.Timer _layoutTimer = new() { Interval = 80 };
    private readonly List<string> _previewThumbnailCandidates = new();
    private int _previewThumbnailIndex;
    private CancellationTokenSource? _previewCts;
    private Task? _previewTask;

    private string? _analyzedCollectionUrl;
    private string _selectedPlaylistItems = string.Empty;
    private readonly List<string> _selectedCollectionUrls = new();
    private string? _activeOutputDirectory;
    private bool _advancedVisible;
    private bool _logVisible;
    private bool _resultsVisible;
    private bool _drawerAnimating;
    private bool _workspaceAnimating;
    private int _drawerAnimationGeneration;
    private int _workspaceAnimationGeneration;
    private FormWindowState _lastWindowState = FormWindowState.Normal;
    private bool _analyzingCollection;
    private bool _busy;

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

        Text = "Plutao";
        Width = 1120;
        Height = 820;
        MinimumSize = new Size(820, 620);
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
            Padding = new Padding(24, 18, 24, 24),
            ColumnCount = 1,
            RowCount = 10,
            BackColor = Bg
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < root.RowCount; i++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        scrollHost.Controls.Add(root);

        // Cabeçalho simples, com cara de aplicativo pronto.
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 14),
            BackColor = Bg
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var brand = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Bg,
            Margin = new Padding(0)
        };
        var brandTitle = new Label
        {
            Text = "PLUTAO",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold),
            ForeColor = TextMain,
            Margin = new Padding(0)
        };
        var brandSub = new Label
        {
            Text = "Baixe vídeos, áudios, perfis e playlists  •  v0.5.9",
            AutoSize = true,
            ForeColor = TextMuted,
            Margin = new Padding(2, 0, 0, 0)
        };
        brand.Controls.Add(brandTitle);
        brand.Controls.Add(brandSub);
        header.Controls.Add(brand, 0, 0);

        var headerActions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Bg,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Margin = new Padding(0, 8, 0, 0)
        };
        headerActions.Controls.Add(btnOpenDownloads);
        headerActions.Controls.Add(btnToggleResults);
        headerActions.Controls.Add(btnToggleLog);
        headerActions.Controls.Add(btnAdvanced);
        header.Controls.Add(headerActions, 1, 0);
        root.Controls.Add(header);

        // Entrada principal: é a primeira coisa que o usuário vê e entende.
        var linkCard = CreateCardPanel("linkCard");
        linkCard.Height = 142;
        var linkLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 14, 18, 12),
            BackColor = Panel
        };
        linkLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        linkLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        linkLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        linkLayout.Controls.Add(new Label
        {
            Text = "O que você quer baixar?",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 12F),
            ForeColor = TextMain,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);

        var linkRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Panel,
            Margin = new Padding(0)
        };
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        txtUrls.Dock = DockStyle.Fill;
        txtUrls.BorderStyle = BorderStyle.FixedSingle;
        txtUrls.Font = new Font("Segoe UI", 11F);
        txtUrls.PlaceholderText = "Cole um link do YouTube, Instagram, TikTok...";
        btnPaste.Dock = DockStyle.Fill;
        btnPaste.Text = "COLAR";
        linkRow.Controls.Add(txtUrls, 0, 0);
        linkRow.Controls.Add(btnPaste, 1, 0);
        linkLayout.Controls.Add(linkRow, 0, 1);
        lblLinkHint.ForeColor = TextMuted;
        lblLinkHint.Margin = new Padding(0, 7, 0, 0);
        linkLayout.Controls.Add(lblLinkHint, 0, 2);
        linkCard.Controls.Add(linkLayout);
        root.Controls.Add(linkCard);

        // Cartão de prévia: aparece imediatamente após colar um link individual.
        var previewCard = CreateCardPanel("previewCard");
        previewCard.Height = 120;
        var previewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(14),
            BackColor = Panel
        };
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        picPreview.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        previewLayout.Controls.Add(picPreview, 0, 0);

        var previewText = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Panel,
            Margin = new Padding(0, 4, 0, 0)
        };
        lblPreviewTitle.Font = new Font("Segoe UI Semibold", 12F);
        lblPreviewTitle.ForeColor = TextMain;
        lblPreviewTitle.MaximumSize = new Size(760, 44);
        lblPreviewMeta.ForeColor = TextMuted;
        lblPreviewDetails.ForeColor = Color.FromArgb(205, 205, 205);
        previewText.Controls.Add(lblPreviewTitle);
        previewText.Controls.Add(lblPreviewMeta);
        previewText.Controls.Add(lblPreviewDetails);
        previewLayout.Controls.Add(previewText, 1, 0);
        previewCard.Controls.Add(previewLayout);
        root.Controls.Add(previewCard);

        // Ações essenciais: modo, qualidade, destino e baixar.
        var actionCard = CreateCardPanel("actionCard");
        actionCard.AutoSize = true;
        actionCard.MinimumSize = new Size(0, 188);
        var actionLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 14, 18, 14),
            BackColor = Panel
        };

        var modeRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Panel,
            Margin = new Padding(0)
        };
        rbVideo.Font = new Font("Segoe UI Semibold", 10F);
        rbAudio.Font = new Font("Segoe UI Semibold", 10F);
        rbVideo.Margin = new Padding(0, 6, 18, 6);
        rbAudio.Margin = new Padding(0, 6, 26, 6);
        modeRow.Controls.Add(rbVideo);
        modeRow.Controls.Add(rbAudio);

        lblQualityCaption.Margin = new Padding(0, 9, 7, 0);
        cmbQuality.Width = 205;
        cmbQuality.Margin = new Padding(0, 4, 18, 4);
        modeRow.Controls.Add(lblQualityCaption);
        modeRow.Controls.Add(cmbQuality);

        lblVideoFormatCaption.Margin = new Padding(0, 9, 7, 0);
        cmbVideoFormat.Width = 105;
        cmbVideoFormat.Margin = new Padding(0, 4, 18, 4);
        modeRow.Controls.Add(lblVideoFormatCaption);
        modeRow.Controls.Add(cmbVideoFormat);

        lblAudioFormatCaption.Margin = new Padding(0, 9, 7, 0);
        cmbAudioFormat.Width = 115;
        cmbAudioFormat.Margin = new Padding(0, 4, 18, 4);
        modeRow.Controls.Add(lblAudioFormatCaption);
        modeRow.Controls.Add(cmbAudioFormat);
        actionLayout.Controls.Add(modeRow, 0, 0);

        var destinationRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            BackColor = Panel,
            Margin = new Padding(0, 7, 0, 7)
        };
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        destinationRow.Controls.Add(new Label
        {
            Text = "Salvar em",
            AutoSize = true,
            ForeColor = TextMuted,
            Margin = new Padding(0, 7, 10, 0)
        }, 0, 0);
        lblDestination.Dock = DockStyle.Fill;
        lblDestination.ForeColor = TextMain;
        lblDestination.Margin = new Padding(0, 7, 8, 0);
        destinationRow.Controls.Add(lblDestination, 1, 0);
        destinationRow.Controls.Add(btnChangeDestination, 2, 0);
        actionLayout.Controls.Add(destinationRow, 0, 1);

        var accountRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Panel,
            Margin = new Padding(0, 2, 0, 8)
        };
        btnAnalyzeCollection.Visible = false;
        lblCollectionInfo.ForeColor = TextMuted;
        lblCollectionInfo.Margin = new Padding(10, 8, 0, 0);
        accountRow.Controls.Add(btnAnalyzeCollection);
        accountRow.Controls.Add(lblCollectionInfo);
        actionLayout.Controls.Add(accountRow, 0, 2);

        var downloadRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 54,
            ColumnCount = 2,
            BackColor = Panel,
            Margin = new Padding(0)
        };
        downloadRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        downloadRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        btnDownload.Dock = DockStyle.Fill;
        btnDownload.Text = "BAIXAR";
        btnDownload.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
        btnDownload.BackColor = Accent;
        btnDownload.FlatStyle = FlatStyle.Flat;
        btnDownload.FlatAppearance.BorderColor = Accent;
        btnStop.Dock = DockStyle.Fill;
        btnStop.Margin = new Padding(8, 0, 0, 0);
        downloadRow.Controls.Add(btnDownload, 0, 0);
        downloadRow.Controls.Add(btnStop, 1, 0);
        actionLayout.Controls.Add(downloadRow, 0, 3);
        actionCard.Controls.Add(actionLayout);
        root.Controls.Add(actionCard);

        // Perfil e diagnóstico dividem a mesma faixa. O diagnóstico só ocupa
        // espaço quando o usuário o abre; fechado, a lista volta a usar 100%.
        grpLog.Name = "grpLog";
        grpLog.Text = "Diagnóstico";
        grpLog.Dock = DockStyle.Fill;
        grpLog.AutoSize = false;
        grpLog.Padding = new Padding(8);
        grpLog.Margin = new Padding(8, 0, 0, 0);
        txtLog.Dock = DockStyle.Fill;
        grpLog.Controls.Add(txtLog);

        profileWorkspace.Name = "profileWorkspace";
        profileWorkspace.Dock = DockStyle.Top;
        profileWorkspace.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        profileWorkspace.AutoSize = false;
        profileWorkspace.Height = 520;
        profileWorkspace.ColumnCount = 2;
        profileWorkspace.RowCount = 1;
        profileWorkspace.Margin = new Padding(0, 0, 0, 10);
        profileWorkspace.BackColor = Bg;
        profileWorkspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        profileWorkspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        profileWorkspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        profileSelection.Dock = DockStyle.Fill;
        profileSelection.Margin = new Padding(0);
        profileWorkspace.Controls.Add(profileSelection, 0, 0);
        profileWorkspace.Controls.Add(grpLog, 1, 0);
        root.Controls.Add(profileWorkspace);

        var progressCard = CreateCardPanel("progressCard");
        progressCard.Height = 92;
        var progressLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14, 10, 14, 10),
            BackColor = Panel
        };
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var progressHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Panel
        };
        progressHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lblCurrent.ForeColor = TextMuted;
        progressHeader.Controls.Add(lblCurrent, 0, 0);
        lblStatus.Anchor = AnchorStyles.Right;
        progressHeader.Controls.Add(lblStatus, 1, 0);
        progressLayout.Controls.Add(progressHeader, 0, 0);
        progressLayout.Controls.Add(progress, 0, 1);
        progressCard.Controls.Add(progressLayout);
        root.Controls.Add(progressCard);

        var completedRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Bg,
            Margin = new Padding(0, 0, 0, 8)
        };
        completedRow.Controls.Add(btnOpenLastFile);
        completedRow.Controls.Add(btnOpenLastFolder);
        root.Controls.Add(completedRow);

        // Configurações ficam fora do caminho principal. O usuário comum não precisa vê-las.
        grpAdvanced.Name = "grpAdvanced";
        grpAdvanced.Text = "Configurações";
        grpAdvanced.Dock = DockStyle.Top;
        grpAdvanced.AutoSize = true;
        grpAdvanced.Padding = new Padding(10);
        var advanced = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 8,
            Padding = new Padding(6),
            BackColor = Panel
        };
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));

        AddFolderRow(advanced, 0, "Vídeos:", txtVideoOutput, btnBrowseVideo, btnOpenVideo);
        AddFolderRow(advanced, 1, "Áudios:", txtAudioOutput, btnBrowseAudio, btnOpenAudio);
        advanced.Controls.Add(LabelFor("Temporários:"), 0, 2);
        txtTemp.Dock = DockStyle.Fill;
        advanced.Controls.Add(txtTemp, 1, 2);
        btnBrowseTemp.Dock = DockStyle.Fill;
        advanced.Controls.Add(btnBrowseTemp, 2, 2);
        advanced.SetColumnSpan(btnBrowseTemp, 2);

        advanced.Controls.Add(LabelFor("Cookies:"), 0, 3);
        cmbCookies.Width = 180;
        advanced.Controls.Add(cmbCookies, 1, 3);
        advanced.SetColumnSpan(cmbCookies, 3);

        advanced.Controls.Add(LabelFor("Arquivo existente:"), 0, 4);
        cmbExisting.Width = 240;
        advanced.Controls.Add(cmbExisting, 1, 4);
        advanced.SetColumnSpan(cmbExisting, 3);

        var collectionSettings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Panel
        };
        collectionSettings.Controls.Add(chkPlaylist);
        collectionSettings.Controls.Add(LabelFor("Limite:"));
        cmbCollectionLimit.Width = 90;
        collectionSettings.Controls.Add(cmbCollectionLimit);
        advanced.Controls.Add(collectionSettings, 0, 5);
        advanced.SetColumnSpan(collectionSettings, 4);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Panel
        };
        checks.Controls.Add(chkOrganize);
        checks.Controls.Add(chkMetadata);
        checks.Controls.Add(chkThumb);
        checks.Controls.Add(chkJson);
        checks.Controls.Add(chkArchive);
        advanced.Controls.Add(checks, 0, 6);
        advanced.SetColumnSpan(checks, 4);

        var advancedActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Panel
        };
        advancedActions.Controls.Add(btnUpdate);
        advancedActions.Controls.Add(btnClearLog);
        advanced.Controls.Add(advancedActions, 0, 7);
        advanced.SetColumnSpan(advancedActions, 4);
        grpAdvanced.Controls.Add(advanced);

        // Configurações em gaveta lateral: não empurram o usuário para o fim da
        // página e continuam acessíveis durante análise/download.
        settingsDrawer.Name = "settingsDrawer";
        settingsDrawer.BackColor = Panel;
        settingsDrawer.BorderStyle = BorderStyle.FixedSingle;
        settingsDrawer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
        settingsDrawer.Visible = false;

        var settingsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Panel,
            Padding = new Padding(10)
        };
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var settingsHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Panel
        };
        settingsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsHeader.Controls.Add(new Label
        {
            Text = "Configurações",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
            ForeColor = TextMain,
            Margin = new Padding(2, 10, 0, 0)
        }, 0, 0);
        btnCloseSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        settingsHeader.Controls.Add(btnCloseSettings, 1, 0);
        settingsLayout.Controls.Add(settingsHeader, 0, 0);

        var settingsScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Panel,
            Padding = new Padding(0, 4, 0, 0)
        };
        var settingsContent = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Panel,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        settingsContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        settingsContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lblSettingsBusyHint.MaximumSize = new Size(460, 44);
        lblSettingsBusyHint.Margin = new Padding(8, 2, 8, 8);
        settingsContent.Controls.Add(lblSettingsBusyHint, 0, 0);
        grpAdvanced.Dock = DockStyle.Top;
        grpAdvanced.AutoSize = true;
        grpAdvanced.Margin = new Padding(0);
        settingsContent.Controls.Add(grpAdvanced, 0, 1);
        settingsScroll.Controls.Add(settingsContent);
        settingsLayout.Controls.Add(settingsScroll, 0, 1);
        settingsDrawer.Controls.Add(settingsLayout);
        Controls.Add(settingsDrawer);
        settingsDrawer.BringToFront();

        EnableDoubleBuffering(settingsDrawer);
        EnableDoubleBuffering(profileWorkspace);
        EnableDoubleBuffering(profileSelection);
        EnableDoubleBuffering(grpLog);

        SetAdvancedVisible(false);
        SetLogVisible(false);
    }

    private void SetAdvancedVisible(bool visible)
    {
        _advancedVisible = visible;
        settingsDrawer.Visible = visible;
        btnAdvanced.Text = visible ? "Fechar configurações" : "Configurações";
        if (visible)
        {
            PositionSettingsDrawer();
            settingsDrawer.BringToFront();
        }
    }

    private async Task AnimateAdvancedVisibleAsync(bool visible)
    {
        if (_drawerAnimating || _advancedVisible == visible || IsDisposed)
            return;

        var generation = ++_drawerAnimationGeneration;
        _drawerAnimating = true;
        _advancedVisible = visible;
        btnAdvanced.Text = visible ? "Fechar configurações" : "Configurações";
        profileSelection.SetVisualWorkPaused(true);

        try
        {
            var width = SettingsDrawerWidth();
            settingsDrawer.Width = width;
            settingsDrawer.Height = ClientSize.Height;
            settingsDrawer.Top = 0;

            var shownLeft = Math.Max(0, ClientSize.Width - width);
            var startLeft = visible ? ClientSize.Width : settingsDrawer.Left;
            var endLeft = visible ? shownLeft : ClientSize.Width;

            if (visible)
            {
                settingsDrawer.Left = startLeft;
                settingsDrawer.Visible = true;
                settingsDrawer.BringToFront();
            }

            var timer = Stopwatch.StartNew();
            const double durationMs = 140d;
            while (timer.Elapsed.TotalMilliseconds < durationMs &&
                   !IsDisposed &&
                   generation == _drawerAnimationGeneration)
            {
                var t = Math.Clamp(timer.Elapsed.TotalMilliseconds / durationMs, 0d, 1d);
                var eased = 1d - Math.Pow(1d - t, 3d);
                var nextLeft = (int)Math.Round(startLeft + ((endLeft - startLeft) * eased));
                if (settingsDrawer.Left != nextLeft)
                    settingsDrawer.Left = nextLeft;

                await Task.Delay(16);
            }

            if (IsDisposed || generation != _drawerAnimationGeneration)
                return;

            settingsDrawer.Left = endLeft;
            if (!visible)
                settingsDrawer.Visible = false;
        }
        finally
        {
            if (generation == _drawerAnimationGeneration)
            {
                _drawerAnimating = false;
                profileSelection.SetVisualWorkPaused(false);
            }
        }
    }

    private void SetLogVisible(bool visible)
    {
        _logVisible = visible;
        btnToggleLog.Text = visible ? "Ocultar diagnóstico" : "Diagnóstico";
        UpdateWorkspaceLayout();
    }

    private async Task SetLogVisibleAnimatedAsync(bool visible)
    {
        if (_workspaceAnimating || _logVisible == visible)
            return;

        _logVisible = visible;
        btnToggleLog.Text = visible ? "Ocultar diagnóstico" : "Diagnóstico";
        await AnimateWorkspaceTransitionAsync();
    }

    private void UpdateWorkspaceLayout()
    {
        var profileVisible = _resultsVisible && profileSelection.HasData;
        var workspaceVisible = profileVisible || _logVisible;

        profileSelection.Visible = profileVisible;
        grpLog.Visible = _logVisible;
        profileWorkspace.Visible = workspaceVisible;

        if (workspaceVisible)
            profileWorkspace.Height = profileSelection.Height;

        ConfigureWorkspaceColumns(profileVisible);
        profileWorkspace.PerformLayout();
    }

    private void ConfigureWorkspaceColumns(bool profileVisible)
    {
        if (_logVisible && profileVisible)
        {
            var diagnosticPercent = ClientSize.Width >= 1600 ? 34 : ClientSize.Width >= 1100 ? 30 : 28;
            profileWorkspace.ColumnStyles[0].SizeType = SizeType.Percent;
            profileWorkspace.ColumnStyles[0].Width = 100 - diagnosticPercent;
            profileWorkspace.ColumnStyles[1].SizeType = SizeType.Percent;
            profileWorkspace.ColumnStyles[1].Width = diagnosticPercent;
            grpLog.Margin = new Padding(8, 0, 0, 0);
        }
        else if (_logVisible)
        {
            profileWorkspace.ColumnStyles[0].SizeType = SizeType.Absolute;
            profileWorkspace.ColumnStyles[0].Width = 0;
            profileWorkspace.ColumnStyles[1].SizeType = SizeType.Percent;
            profileWorkspace.ColumnStyles[1].Width = 100;
            grpLog.Margin = new Padding(0);
        }
        else
        {
            profileWorkspace.ColumnStyles[0].SizeType = SizeType.Percent;
            profileWorkspace.ColumnStyles[0].Width = 100;
            profileWorkspace.ColumnStyles[1].SizeType = SizeType.Absolute;
            profileWorkspace.ColumnStyles[1].Width = 0;
            grpLog.Margin = new Padding(0);
        }
    }

    private async Task AnimateWorkspaceTransitionAsync()
    {
        if (_workspaceAnimating || IsDisposed)
        {
            UpdateWorkspaceLayout();
            return;
        }

        var generation = ++_workspaceAnimationGeneration;
        _workspaceAnimating = true;
        profileSelection.SetVisualWorkPaused(true);

        try
        {
            var profileVisible = _resultsVisible && profileSelection.HasData;
            var shouldShow = profileVisible || _logVisible;
            var wasVisible = profileWorkspace.Visible;
            var targetHeight = Math.Max(1, profileSelection.Height);

            profileSelection.Visible = profileVisible;
            grpLog.Visible = _logVisible;

            // DataGridView grande custa muito para relayout a cada quadro.
            // Nesses casos a transição anima apenas o contêiner vazio e revela
            // a grade no final: mantém movimento suave sem redesenhar 4 mil linhas.
            var largeCollection = profileSelection.TotalItems > 300;
            if (largeCollection)
            {
                if (shouldShow && !wasVisible)
                {
                    profileSelection.Visible = false;
                    grpLog.Visible = false;
                    ConfigureWorkspaceColumns(profileVisible);
                    profileWorkspace.Height = 1;
                    profileWorkspace.Visible = true;

                    var timer = Stopwatch.StartNew();
                    const double durationMs = 135d;
                    while (timer.Elapsed.TotalMilliseconds < durationMs &&
                           !IsDisposed &&
                           generation == _workspaceAnimationGeneration)
                    {
                        var t = Math.Clamp(timer.Elapsed.TotalMilliseconds / durationMs, 0d, 1d);
                        var eased = 1d - Math.Pow(1d - t, 3d);
                        profileWorkspace.Height = Math.Max(1, (int)Math.Round(targetHeight * eased));
                        await Task.Delay(16);
                    }

                    if (!IsDisposed && generation == _workspaceAnimationGeneration)
                    {
                        profileWorkspace.Height = targetHeight;
                        profileSelection.Visible = profileVisible;
                        grpLog.Visible = _logVisible;
                        ConfigureWorkspaceColumns(profileVisible);
                    }
                }
                else if (!shouldShow && wasVisible)
                {
                    profileSelection.Visible = false;
                    grpLog.Visible = false;
                    var startHeight = Math.Max(1, profileWorkspace.Height);
                    var timer = Stopwatch.StartNew();
                    const double durationMs = 110d;
                    while (timer.Elapsed.TotalMilliseconds < durationMs &&
                           !IsDisposed &&
                           generation == _workspaceAnimationGeneration)
                    {
                        var t = Math.Clamp(timer.Elapsed.TotalMilliseconds / durationMs, 0d, 1d);
                        profileWorkspace.Height = Math.Max(1, (int)Math.Round(startHeight * (1d - (t * t))));
                        await Task.Delay(16);
                    }

                    if (!IsDisposed && generation == _workspaceAnimationGeneration)
                    {
                        profileWorkspace.Visible = false;
                        profileWorkspace.Height = targetHeight;
                    }
                }
                else
                {
                    profileWorkspace.Visible = shouldShow;
                    if (shouldShow)
                        profileWorkspace.Height = targetHeight;
                    profileSelection.Visible = profileVisible;
                    grpLog.Visible = _logVisible;
                    ConfigureWorkspaceColumns(profileVisible);
                }

                return;
            }

            if (shouldShow && !wasVisible)
            {
                ConfigureWorkspaceColumns(profileVisible);
                profileWorkspace.Height = 1;
                profileWorkspace.Visible = true;

                var timer = Stopwatch.StartNew();
                const double durationMs = 125d;
                while (timer.Elapsed.TotalMilliseconds < durationMs &&
                       !IsDisposed &&
                       generation == _workspaceAnimationGeneration)
                {
                    var t = Math.Clamp(timer.Elapsed.TotalMilliseconds / durationMs, 0d, 1d);
                    var eased = 1d - Math.Pow(1d - t, 3d);
                    profileWorkspace.Height = Math.Max(1, (int)Math.Round(targetHeight * eased));
                    await Task.Delay(16);
                }

                if (!IsDisposed && generation == _workspaceAnimationGeneration)
                    profileWorkspace.Height = targetHeight;
            }
            else if (!shouldShow && wasVisible)
            {
                var startHeight = Math.Max(1, profileWorkspace.Height);
                var timer = Stopwatch.StartNew();
                const double durationMs = 115d;
                while (timer.Elapsed.TotalMilliseconds < durationMs &&
                       !IsDisposed &&
                       generation == _workspaceAnimationGeneration)
                {
                    var t = Math.Clamp(timer.Elapsed.TotalMilliseconds / durationMs, 0d, 1d);
                    var eased = t * t;
                    profileWorkspace.Height = Math.Max(1, (int)Math.Round(startHeight * (1d - eased)));
                    await Task.Delay(16);
                }

                if (!IsDisposed && generation == _workspaceAnimationGeneration)
                {
                    profileWorkspace.Visible = false;
                    profileWorkspace.Height = targetHeight;
                }
            }
            else if (shouldShow)
            {
                profileWorkspace.Visible = true;
                profileWorkspace.Height = targetHeight;

                var widths = profileWorkspace.GetColumnWidths();
                var totalWidth = Math.Max(1, profileWorkspace.ClientSize.Width);
                var startLogWidth = widths.Length > 1 ? widths[1] : 0;
                int endLogWidth;
                if (!_logVisible)
                {
                    endLogWidth = 0;
                }
                else if (!profileVisible)
                {
                    endLogWidth = totalWidth;
                }
                else
                {
                    var diagnosticPercent = ClientSize.Width >= 1600 ? 34 : ClientSize.Width >= 1100 ? 30 : 28;
                    endLogWidth = (int)Math.Round(totalWidth * diagnosticPercent / 100d);
                }

                grpLog.Visible = _logVisible;
                var timer = Stopwatch.StartNew();
                const double durationMs = 120d;
                while (timer.Elapsed.TotalMilliseconds < durationMs &&
                       !IsDisposed &&
                       generation == _workspaceAnimationGeneration)
                {
                    var t = Math.Clamp(timer.Elapsed.TotalMilliseconds / durationMs, 0d, 1d);
                    var eased = 1d - Math.Pow(1d - t, 3d);
                    var logWidth = (int)Math.Round(startLogWidth + ((endLogWidth - startLogWidth) * eased));
                    profileWorkspace.ColumnStyles[0].SizeType = SizeType.Absolute;
                    profileWorkspace.ColumnStyles[0].Width = Math.Max(0, totalWidth - logWidth);
                    profileWorkspace.ColumnStyles[1].SizeType = SizeType.Absolute;
                    profileWorkspace.ColumnStyles[1].Width = Math.Max(0, logWidth);
                    await Task.Delay(16);
                }
            }

            if (!IsDisposed && generation == _workspaceAnimationGeneration)
                UpdateWorkspaceLayout();
        }
        finally
        {
            if (generation == _workspaceAnimationGeneration)
            {
                _workspaceAnimating = false;
                profileSelection.SetVisualWorkPaused(false);
            }
        }
    }

    private void CancelUiAnimationsForResize()
    {
        _drawerAnimationGeneration++;
        _workspaceAnimationGeneration++;
        _drawerAnimating = false;
        _workspaceAnimating = false;

        if (_advancedVisible)
        {
            settingsDrawer.Visible = true;
            settingsDrawer.BringToFront();
        }
    }

    private int SettingsDrawerWidth()
    {
        var width = Math.Clamp((int)(ClientSize.Width * 0.46), 460, 660);
        return Math.Min(width, Math.Max(360, ClientSize.Width - 32));
    }

    private void PositionSettingsDrawer()
    {
        if (!IsHandleCreated || _drawerAnimating)
            return;

        var width = SettingsDrawerWidth();
        settingsDrawer.Width = width;
        settingsDrawer.Height = ClientSize.Height;
        settingsDrawer.Left = Math.Max(0, ClientSize.Width - width);
        settingsDrawer.Top = 0;
    }

    private static void EnableDoubleBuffering(Control control)
    {
        try
        {
            var property = typeof(Control).GetProperty(
                "DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            property?.SetValue(control, true);
        }
        catch
        {
        }

        foreach (Control child in control.Controls)
        {
            if (child is System.Windows.Forms.Panel or TableLayoutPanel or UserControl or GroupBox)
                EnableDoubleBuffering(child);
        }
    }

    private void ApplyResponsiveSpacing()
    {
        if (!IsHandleCreated)
            return;

        var clientWidth = Math.Max(1, ClientSize.Width);
        var clientHeight = Math.Max(1, ClientSize.Height);
        var compact = clientHeight <= 760 || clientWidth <= 1100;
        var roomy = clientHeight >= 900 && clientWidth >= 1600;

        SetNamedHeight("linkCard", compact ? 130 : roomy ? 150 : 142);
        SetNamedHeight("previewCard", compact ? 124 : roomy ? 132 : 126);
        SetNamedHeight("progressCard", compact ? 84 : roomy ? 100 : 92);
        profileSelection.ApplyResponsiveHeight(compact, roomy);
        profileWorkspace.Height = profileSelection.Height;
        profileWorkspace.Dock = DockStyle.Top;
        profileWorkspace.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        profileSelection.Dock = DockStyle.Fill;
        grpLog.Dock = DockStyle.Fill;
        UpdateWorkspaceLayout();
        profileWorkspace.PerformLayout();
        PositionSettingsDrawer();

        var root = Controls.Find("rootLayout", true).FirstOrDefault();
        if (root is TableLayoutPanel layout)
        {
            layout.Padding = new Padding(compact ? 14 : 24, compact ? 12 : 18, compact ? 14 : 24, 24);

            // AutoSize mantinha a largura preferida antiga em algumas trocas
            // entre janela e maximizado. A largura mínima acompanha o host atual.
            if (layout.Parent is Control host)
            {
                var availableWidth = Math.Max(1, host.ClientSize.Width -
                    (host is System.Windows.Forms.Panel panel && panel.VerticalScroll.Visible
                        ? SystemInformation.VerticalScrollBarWidth
                        : 0));
                layout.Width = availableWidth;
                layout.MinimumSize = new Size(availableWidth, 0);
                layout.MaximumSize = new Size(availableWidth, 0);
            }

            foreach (Control child in layout.Controls)
                child.Margin = new Padding(0, 0, 0, compact ? 8 : 12);

            layout.PerformLayout();
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
        ResetQualityChoices();

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
        lblCollectionInfo.Text = "Não analisado";
        lblCollectionInfo.ForeColor = TextMuted;
        progress.Value = 0;
        progress.DisplayText = "Pronto";
        UpdateModeUi();
        UpdateDestinationSummary();
        UpdateLinkPresentation();
    }

    private void WireEvents()
    {
        rbVideo.CheckedChanged += (_, _) => UpdateModeUi();
        rbAudio.CheckedChanged += (_, _) => UpdateModeUi();
        cmbVideoFormat.SelectedIndexChanged += (_, _) => UpdateModeUi();
        chkPlaylist.CheckedChanged += (_, _) =>
        {
            CancelCollectionAnalysisForChangedInput();
            UpdateModeUi();
            if (!chkPlaylist.Checked)
            {
                ResetCollectionSelection();
            }
            else
            {
                RestartPreviewAnalysis();
            }
        };
        cmbCollectionLimit.SelectedIndexChanged += (_, _) =>
        {
            CancelCollectionAnalysisForChangedInput();
            ResetCollectionSelection();
            RestartPreviewAnalysis();
        };
        cmbCookies.SelectedIndexChanged += (_, _) =>
        {
            CancelCollectionAnalysisForChangedInput();
            ResetCollectionSelection();
            RestartPreviewAnalysis();
        };

        txtUrls.TextChanged += (_, _) => OnUrlsChanged();
        btnPaste.Click += (_, _) =>
        {
            if (Clipboard.ContainsText())
                txtUrls.Text = Clipboard.GetText().Trim();
        };
        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            if (_busy)
                return;

            var urls = CurrentUrls();
            if (urls.Count != 1)
                return;

            if (YtDlpRunner.IsLikelySingleMediaUrl(urls[0]))
            {
                _previewTask = AnalyzeCurrentPreviewAsync();
                try { await _previewTask; } catch { }
            }
            else if (chkPlaylist.Checked && IsLikelyCollectionUrl(urls[0]))
            {
                try { await AnalyzeCollectionAsync(automatic: true); } catch { }
            }
        };

        btnAnalyzeCollection.Click += async (_, _) => await AnalyzeCollectionAsync(automatic: false);
        profileSelection.SelectionChanged += (_, _) => ApplyProfileSelection();
        profileSelection.HideRequested += async (_, _) =>
        {
            _resultsVisible = false;
            btnToggleResults.Visible = profileSelection.HasData;
            btnToggleResults.Text = "Mostrar resultados";
            await AnimateWorkspaceTransitionAsync();
        };
        btnToggleResults.Click += async (_, _) =>
        {
            if (!profileSelection.HasData || _workspaceAnimating)
                return;

            _resultsVisible = !_resultsVisible;
            btnToggleResults.Text = _resultsVisible ? "Ocultar resultados" : "Mostrar resultados";
            await AnimateWorkspaceTransitionAsync();
        };

        btnBrowseVideo.Click += (_, _) =>
        {
            BrowseFolder(txtVideoOutput, "Escolha a pasta dos vídeos");
            SaveDestinationSettings();
            UpdateDestinationSummary();
        };
        btnOpenVideo.Click += (_, _) => OpenFolder(txtVideoOutput.Text);
        btnBrowseAudio.Click += (_, _) =>
        {
            BrowseFolder(txtAudioOutput, "Escolha a pasta dos áudios");
            SaveDestinationSettings();
            UpdateDestinationSummary();
        };
        btnOpenAudio.Click += (_, _) => OpenFolder(txtAudioOutput.Text);
        btnBrowseTemp.Click += (_, _) => BrowseFolder(txtTemp, "Escolha a pasta para arquivos temporários");
        txtVideoOutput.TextChanged += (_, _) => UpdateDestinationSummary();
        txtAudioOutput.TextChanged += (_, _) => UpdateDestinationSummary();
        txtVideoOutput.Leave += (_, _) => SaveDestinationSettings();
        txtAudioOutput.Leave += (_, _) => SaveDestinationSettings();

        btnChangeDestination.Click += (_, _) =>
        {
            var target = rbVideo.Checked ? txtVideoOutput : txtAudioOutput;
            BrowseFolder(target, rbVideo.Checked ? "Escolha a pasta dos vídeos" : "Escolha a pasta dos áudios");
            SaveDestinationSettings();
            UpdateDestinationSummary();
        };
        btnOpenDownloads.Click += (_, _) =>
        {
            var path = _busy && !string.IsNullOrWhiteSpace(_activeOutputDirectory)
                ? _activeOutputDirectory
                : rbVideo.Checked ? txtVideoOutput.Text : txtAudioOutput.Text;

            if (string.IsNullOrWhiteSpace(path))
            {
                if (!_busy)
                    btnChangeDestination.PerformClick();
                return;
            }

            OpenFolder(path);
        };

        btnDownload.Click += async (_, _) => await StartDownloadAsync();
        btnStop.Click += (_, _) => StopDownload();
        btnUpdate.Click += async (_, _) => await RunToolActionAsync();
        btnClearLog.Click += (_, _) => txtLog.Clear();
        btnAdvanced.Click += async (_, _) => await AnimateAdvancedVisibleAsync(!_advancedVisible);
        btnCloseSettings.Click += async (_, _) => await AnimateAdvancedVisibleAsync(false);
        btnToggleLog.Click += async (_, _) => await SetLogVisibleAnimatedAsync(!_logVisible);
        btnOpenLastFile.Click += (_, _) => OpenLastFile();
        btnOpenLastFolder.Click += (_, _) => OpenLastFileFolder();
        picPreview.LoadCompleted += (_, e) =>
        {
            if (e.Cancelled || e.Error is null)
                return;

            var failedUrl = picPreview.ImageLocation ?? string.Empty;
            if (_previewThumbnailIndex < _previewThumbnailCandidates.Count &&
                !string.Equals(
                    failedUrl,
                    _previewThumbnailCandidates[_previewThumbnailIndex],
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _previewThumbnailIndex++;
            StartNextPreviewThumbnail();
        };

        _layoutTimer.Tick += (_, _) =>
        {
            _layoutTimer.Stop();
            ApplyResponsiveSpacing();
            profileSelection.SetVisualWorkPaused(false);
        };

        Shown += (_, _) =>
        {
            _lastWindowState = WindowState;
            FitToCurrentScreen();
            UpdateLinkPresentation();
        };
        DpiChanged += (_, _) => BeginInvoke(new Action(() =>
        {
            CancelUiAnimationsForResize();
            ApplyResponsiveSpacing();
            profileSelection.SetVisualWorkPaused(false);
        }));
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                return;

            profileSelection.SetVisualWorkPaused(true);
            var stateChanged = WindowState != _lastWindowState;
            _lastWindowState = WindowState;

            _layoutTimer.Stop();
            if (stateChanged)
            {
                CancelUiAnimationsForResize();
                BeginInvoke(new Action(() =>
                {
                    ApplyResponsiveSpacing();
                    profileSelection.SetVisualWorkPaused(false);
                }));
            }
            else
            {
                _layoutTimer.Start();
            }
        };
        ResizeEnd += (_, _) =>
        {
            _layoutTimer.Stop();
            CancelUiAnimationsForResize();
            ApplyResponsiveSpacing();
            profileSelection.SetVisualWorkPaused(false);
        };
        FormClosing += (_, _) =>
        {
            SaveDestinationSettings();
            CancelPreview();
            StopDownload();
            _previewTimer.Dispose();
            _layoutTimer.Dispose();
        };
    }

    private List<string> CurrentUrls()
        => txtUrls.Lines
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void OnUrlsChanged()
    {
        CancelCollectionAnalysisForChangedInput();
        ResetCollectionSelection();
        ResetQualityChoices();
        CancelPreview();
        UpdateLinkPresentation();
        RestartPreviewAnalysis();
    }

    private void RestartPreviewAnalysis()
    {
        _previewTimer.Stop();
        var urls = CurrentUrls();
        if (_busy || urls.Count != 1)
            return;

        if (YtDlpRunner.IsLikelySingleMediaUrl(urls[0]))
            _previewTimer.Start();
        else if (chkPlaylist.Checked && IsLikelyCollectionUrl(urls[0]))
            _previewTimer.Start();
    }

    private void CancelCollectionAnalysisForChangedInput()
    {
        if (!_analyzingCollection)
            return;

        try { _cts?.Cancel(); } catch { }
        _runner.Stop();
    }

    private void CancelPreview()
    {
        _previewTimer.Stop();
        try { picPreview.CancelAsync(); } catch { }
        _previewThumbnailCandidates.Clear();
        _previewThumbnailIndex = 0;
        try { _previewCts?.Cancel(); } catch { }
        _previewCts?.Dispose();
        _previewCts = null;
    }

    private async Task StopPreviewBeforeRunnerUseAsync()
    {
        var task = _previewTask;
        CancelPreview();
        if (task is not null && !task.IsCompleted)
        {
            try { await task; } catch { }
        }
        _previewTask = null;
    }

    private void UpdateLinkPresentation()
    {
        var urls = CurrentUrls();
        picPreview.Visible = false;
        picPreview.ImageLocation = null;
        lblPreviewDetails.Text = string.Empty;

        if (urls.Count == 0)
        {
            lblLinkHint.Text = "Cole um link para começar. O Plutao detecta o tipo automaticamente.";
            lblPreviewTitle.Text = "Cole um link para começar";
            lblPreviewMeta.Text = "Vídeo, áudio, perfil, canal ou playlist";
            btnAnalyzeCollection.Visible = false;
            lblCollectionInfo.Visible = false;
            return;
        }

        if (urls.Count > 1)
        {
            lblLinkHint.Text = $"{urls.Count} links detectados • serão processados em sequência";
            lblPreviewTitle.Text = $"{urls.Count} links prontos para baixar";
            lblPreviewMeta.Text = "Escolha Vídeo ou Áudio, confirme a qualidade e clique em BAIXAR.";
            btnAnalyzeCollection.Visible = false;
            lblCollectionInfo.Visible = false;
            return;
        }

        var url = urls[0];
        if (YtDlpRunner.IsLikelySingleMediaUrl(url))
        {
            lblLinkHint.Text = $"{PlatformHint(url)} • mídia individual detectada";
            lblPreviewTitle.Text = "Lendo informações do vídeo...";
            lblPreviewMeta.Text = "A prévia fica salva em cache para abrir mais rápido depois.";
            btnAnalyzeCollection.Visible = false;
            lblCollectionInfo.Visible = false;
        }
        else if (IsLikelyCollectionUrl(url))
        {
            lblLinkHint.Text = $"{PlatformHint(url)} • conta, canal, página ou playlist detectada";
            if (IsLikelyYouTubeChannelUrl(url))
            {
                lblPreviewTitle.Text = "Canal do YouTube detectado";
                lblPreviewMeta.Text = "O Plutao vai analisar automaticamente Vídeos, Shorts e Lives.";
            }
            else
            {
                lblPreviewTitle.Text = "Conta / página detectada";
                lblPreviewMeta.Text = "O Plutao vai analisar automaticamente o perfil e listar as mídias.";
            }
            btnAnalyzeCollection.Visible = false;
            btnAnalyzeCollection.Text = "TENTAR NOVAMENTE";
            lblCollectionInfo.Visible = true;
            lblCollectionInfo.Text = "Análise automática aguardando...";
        }
        else
        {
            lblLinkHint.Text = $"{PlatformHint(url)} • link pronto";
            lblPreviewTitle.Text = "Link pronto para baixar";
            lblPreviewMeta.Text = "Escolha Vídeo ou Áudio e clique em BAIXAR.";
            btnAnalyzeCollection.Visible = false;
            lblCollectionInfo.Visible = false;
        }
    }

    private async Task AnalyzeCurrentPreviewAsync()
    {
        var urls = CurrentUrls();
        if (urls.Count != 1 || !YtDlpRunner.IsLikelySingleMediaUrl(urls[0]))
            return;

        CancelPreview();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;
        var url = urls[0];

        try
        {
            var preview = await _runner.AnalyzeMediaAsync(
                url,
                cmbCookies.SelectedItem?.ToString() ?? "Nenhum",
                LogProgress(),
                new Progress<DownloadProgressInfo>(info =>
                {
                    if (!string.IsNullOrWhiteSpace(info.Stage) && info.Percent < 100)
                        lblPreviewMeta.Text = info.Stage + "...";
                }),
                ct);

            if (preview is null || ct.IsCancellationRequested || IsDisposed)
                return;
            if (!string.Equals(AppCache.NormalizeUrl(url), AppCache.NormalizeUrl(CurrentUrls().FirstOrDefault() ?? string.Empty), StringComparison.OrdinalIgnoreCase))
                return;

            SetPreview(preview);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                lblPreviewTitle.Text = "Link pronto para baixar";
                lblPreviewMeta.Text = "Não foi possível carregar a prévia, mas o download ainda pode funcionar.";
                lblPreviewDetails.Text = ex.Message;
            }
        }
    }

    private void SetPreview(MediaPreviewInfo preview)
    {
        lblPreviewTitle.Text = preview.Title;

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(preview.Platform)) meta.Add(preview.Platform);
        if (!string.IsNullOrWhiteSpace(preview.Creator)) meta.Add(preview.Creator);
        if (!string.IsNullOrWhiteSpace(preview.Duration)) meta.Add(preview.Duration);
        lblPreviewMeta.Text = string.Join("  •  ", meta);

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(preview.PublishedDate)) details.Add($"Publicado em {preview.PublishedDate}");
        if (preview.Views.HasValue) details.Add($"{FormatCount(preview.Views.Value)} visualizações");
        if (preview.Likes.HasValue) details.Add($"{FormatCount(preview.Likes.Value)} curtidas");
        if (!string.IsNullOrWhiteSpace(preview.Resolution)) details.Add(preview.Resolution);
        lblPreviewDetails.Text = string.Join("  •  ", details);
        ApplyAvailableQualities(preview);

        try { picPreview.CancelAsync(); } catch { }
        _previewThumbnailCandidates.Clear();
        _previewThumbnailIndex = 0;

        void AddCandidate(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;
            if (!_previewThumbnailCandidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                _previewThumbnailCandidates.Add(candidate);
        }

        AddCandidate(preview.ThumbnailUrl);
        if (preview.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(preview.Id))
        {
            AddCandidate($"https://i.ytimg.com/vi/{preview.Id}/hqdefault.jpg");
            AddCandidate($"https://i.ytimg.com/vi/{preview.Id}/mqdefault.jpg");
            AddCandidate($"https://i.ytimg.com/vi/{preview.Id}/sddefault.jpg");
            AddCandidate($"https://i.ytimg.com/vi/{preview.Id}/maxresdefault.jpg");
            AddCandidate($"https://i.ytimg.com/vi/{preview.Id}/0.jpg");
        }

        StartNextPreviewThumbnail();
    }

    private void StartNextPreviewThumbnail()
    {
        if (IsDisposed)
            return;

        if (_previewThumbnailIndex < 0 ||
            _previewThumbnailIndex >= _previewThumbnailCandidates.Count)
        {
            picPreview.ImageLocation = null;
            picPreview.Visible = false;
            return;
        }

        try
        {
            picPreview.ErrorImage = null;
            picPreview.InitialImage = null;
            picPreview.Visible = true;
            picPreview.ImageLocation = _previewThumbnailCandidates[_previewThumbnailIndex];
            picPreview.LoadAsync();
        }
        catch
        {
            _previewThumbnailIndex++;
            StartNextPreviewThumbnail();
        }
    }

    private void ResetQualityChoices()
    {
        if (cmbQuality.IsDisposed)
            return;

        cmbQuality.BeginUpdate();
        try
        {
            cmbQuality.Items.Clear();
            cmbQuality.Items.AddRange(new object[]
            {
                "Melhor disponível",
                "2160p",
                "1440p",
                "1080p",
                "720p",
                "480p",
                "360p"
            });
            cmbQuality.SelectedIndex = 0;
        }
        finally
        {
            cmbQuality.EndUpdate();
        }

        lblQualityCaption.Text = "Qualidade";
    }

    private void ApplyAvailableQualities(MediaPreviewInfo preview)
    {
        var heights = (preview.AvailableHeights ?? Array.Empty<int>())
            .Where(height => height > 0)
            .Distinct()
            .OrderByDescending(height => height)
            .ToList();

        if (heights.Count == 0 && preview.Height is > 0)
            heights.Add(preview.Height.Value);

        if (heights.Count == 0)
        {
            lblQualityCaption.Text = "Qualidade";
            return;
        }

        var previous = SelectedDownloadQuality();
        cmbQuality.BeginUpdate();
        try
        {
            cmbQuality.Items.Clear();
            for (var index = 0; index < heights.Count; index++)
            {
                var height = heights[index];
                cmbQuality.Items.Add(index == 0
                    ? $"{height}p (Melhor disponível)"
                    : $"{height}p");
            }

            var selectedIndex = 0;
            if (!previous.Equals("Melhor", StringComparison.OrdinalIgnoreCase))
            {
                var requestedDigits = new string(previous.TakeWhile(char.IsDigit).ToArray());
                if (int.TryParse(requestedDigits, out var requestedHeight))
                {
                    var matchIndex = heights.FindIndex(height => height == requestedHeight);
                    if (matchIndex >= 0)
                        selectedIndex = matchIndex;
                }
            }

            cmbQuality.SelectedIndex = selectedIndex;
        }
        finally
        {
            cmbQuality.EndUpdate();
        }

        lblQualityCaption.Text = $"Qualidade (máx. {heights[0]}p)";
    }

    private string SelectedDownloadQuality()
    {
        var text = cmbQuality.SelectedItem?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(text) ||
            text.StartsWith("Melhor", StringComparison.OrdinalIgnoreCase))
            return "Melhor";

        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var height) && height > 0
            ? $"{height}p"
            : text;
    }

    private void UpdateDestinationSummary()
    {
        var path = rbVideo.Checked ? txtVideoOutput.Text.Trim() : txtAudioOutput.Text.Trim();
        lblDestination.Text = string.IsNullOrWhiteSpace(path)
            ? "Escolher pasta no primeiro download"
            : path;
    }

    private static bool IsLikelyCollectionUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.Trim('/');
        var lowerPath = path.ToLowerInvariant();

        if (host.Contains("youtube.com"))
            return lowerPath.StartsWith("@") ||
                   lowerPath.StartsWith("channel/") ||
                   lowerPath.StartsWith("c/") ||
                   lowerPath.StartsWith("user/") ||
                   lowerPath.Equals("playlist") ||
                   uri.Query.Contains("list=", StringComparison.OrdinalIgnoreCase) && !lowerPath.Equals("watch");

        if (host.Contains("instagram.com"))
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 1 &&
                   !parts[0].Equals("explore", StringComparison.OrdinalIgnoreCase) &&
                   !parts[0].Equals("stories", StringComparison.OrdinalIgnoreCase);
        }

        if (host.Contains("tiktok.com"))
            return lowerPath.StartsWith("@") && !lowerPath.Contains("/video/");

        return false;
    }

    private static bool IsLikelyYouTubeChannelUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = uri.AbsolutePath.Trim('/');
        return path.StartsWith("@", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("channel/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("c/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("user/", StringComparison.OrdinalIgnoreCase);
    }

    private static string PlatformHint(string url)
    {
        if (url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)) return "YouTube";
        if (url.Contains("instagram.com", StringComparison.OrdinalIgnoreCase)) return "Instagram";
        if (url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase)) return "TikTok";
        if (url.Contains("x.com", StringComparison.OrdinalIgnoreCase) || url.Contains("twitter.com", StringComparison.OrdinalIgnoreCase)) return "X / Twitter";
        if (url.Contains("facebook.com", StringComparison.OrdinalIgnoreCase)) return "Facebook";
        return "Link detectado";
    }

    private static string FormatCount(long value)
    {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000d:0.#} bi";
        if (value >= 1_000_000) return $"{value / 1_000_000d:0.#} mi";
        if (value >= 1_000) return $"{value / 1_000d:0.#} mil";
        return value.ToString("N0");
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
        cmbQuality.Visible = rbVideo.Checked;
        lblQualityCaption.Visible = rbVideo.Checked;
        cmbVideoFormat.Visible = rbVideo.Checked;
        lblVideoFormatCaption.Visible = rbVideo.Checked;
        cmbAudioFormat.Visible = rbAudio.Checked;
        lblAudioFormatCaption.Visible = rbAudio.Checked;
        cmbCollectionLimit.Enabled = chkPlaylist.Checked;

        txtVideoOutput.ForeColor = rbVideo.Checked ? TextMain : TextMuted;
        txtAudioOutput.ForeColor = rbAudio.Checked ? TextMain : TextMuted;
        UpdateDestinationSummary();
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
        _resultsVisible = false;
        profileSelection.ClearData();
        btnToggleResults.Visible = false;
        btnToggleResults.Text = "Mostrar resultados";
        UpdateWorkspaceLayout();
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

    private async Task AnalyzeCollectionAsync(bool automatic = false)
    {
        if (_busy && automatic)
            return;

        var urls = CurrentUrls();

        if (urls.Count != 1)
        {
            if (!automatic)
                MessageBox.Show(this, "Para analisar uma conta/página, deixe exatamente um link na caixa de links.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var targetUrl = urls[0];
        if (automatic &&
            profileSelection.HasData &&
            !string.IsNullOrWhiteSpace(_analyzedCollectionUrl) &&
            string.Equals(AppCache.NormalizeUrl(targetUrl), AppCache.NormalizeUrl(_analyzedCollectionUrl), StringComparison.OrdinalIgnoreCase))
            return;

        if (!chkPlaylist.Checked)
        {
            if (automatic)
                return;
            chkPlaylist.Checked = true;
        }

        var analysisCookies = cmbCookies.SelectedItem?.ToString() ?? "Nenhum";
        var analysisLimit = SelectedCollectionLimit();

        await StopPreviewBeforeRunnerUseAsync();
        _tools.TemporaryDirectory = txtTemp.Text.Trim();
        _cts = new CancellationTokenSource();
        _analyzingCollection = true;
        SetBusy(true);
        btnAnalyzeCollection.Visible = false;
        lblCollectionInfo.Visible = true;
        lblCollectionInfo.Text = automatic ? "Analisando automaticamente..." : "Analisando...";
        lblStatus.Text = "Analisando...";
        lblStatus.ForeColor = TextMuted;
        lblCurrent.Text = "Listando vídeos da página/conta...";
        AppendLog($"[Análise] Iniciando: {targetUrl}");

        try
        {
            var items = await _runner.AnalyzeCollectionAsync(
                targetUrl,
                analysisCookies,
                analysisLimit,
                LogProgress(),
                new Progress<DownloadProgressInfo>(SetProgressUi),
                _cts.Token);

            if (items.Count == 0)
            {
                lblCollectionInfo.Text = "0 vídeos encontrados";
                lblStatus.Text = "Nenhum vídeo encontrado";
                lblStatus.ForeColor = Color.FromArgb(255, 184, 77);
                lblCurrent.Text = "A plataforma não devolveu mídias para esse link.";
                btnAnalyzeCollection.Text = "TENTAR NOVAMENTE";
                btnAnalyzeCollection.Visible = true;
                if (!automatic)
                    MessageBox.Show(this, "Nenhum vídeo foi encontrado. Confira o diagnóstico; alguns perfis exigem Cookies do navegador.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Se link, cookies, limite ou modo mudaram durante a análise,
            // ignora o resultado antigo. O finally agenda nova análise quando aplicável.
            var currentUrls = CurrentUrls();
            var currentUrl = currentUrls.Count == 1 ? currentUrls[0] : string.Empty;
            var currentCookies = cmbCookies.SelectedItem?.ToString() ?? "Nenhum";
            var currentLimit = SelectedCollectionLimit();
            if (!chkPlaylist.Checked ||
                !string.Equals(AppCache.NormalizeUrl(targetUrl), AppCache.NormalizeUrl(currentUrl), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(analysisCookies, currentCookies, StringComparison.OrdinalIgnoreCase) ||
                analysisLimit != currentLimit)
                return;

            _analyzedCollectionUrl = targetUrl;
            profileSelection.LoadData(items, _runner.LastAnalyzedProfile);
            _resultsVisible = true;
            btnToggleResults.Visible = true;
            btnToggleResults.Text = "Ocultar resultados";
            await AnimateWorkspaceTransitionAsync();
            ApplyResponsiveSpacing();
            ApplyProfileSelection();

            lblCollectionInfo.Text = $"{items.Count} encontrados • {items.Count} selecionados";
            btnAnalyzeCollection.Visible = false;
            AppendLog($"[Análise] {items.Count} encontrados; seleção exibida na tela principal.");

            lblStatus.Text = "Análise concluída";
            lblStatus.ForeColor = Color.FromArgb(102, 220, 145);
            lblCurrent.Text = $"Encontrados {items.Count} vídeos.";
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = "Cancelado";
            lblStatus.ForeColor = Color.FromArgb(255, 184, 77);
            lblCurrent.Text = "Análise cancelada.";
            lblCollectionInfo.Text = "Análise cancelada";
            btnAnalyzeCollection.Text = "TENTAR NOVAMENTE";
            var current = CurrentUrls();
            btnAnalyzeCollection.Visible = current.Count == 1 && IsLikelyCollectionUrl(current[0]);
            AppendLog("[Análise] Cancelada.");
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Erro";
            lblStatus.ForeColor = Color.FromArgb(255, 92, 92);
            lblCurrent.Text = "Não foi possível listar os vídeos.";
            lblCollectionInfo.Text = "Falha na análise automática";
            btnAnalyzeCollection.Text = "TENTAR NOVAMENTE";
            btnAnalyzeCollection.Visible = true;
            AppendLog("[Análise] ERRO: " + ex.Message);
            if (!automatic)
                MessageBox.Show(this, ex.Message, "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _analyzingCollection = false;
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
            RestartPreviewAnalysis();
        }
    }

    private async Task StartDownloadAsync()
    {
        var urls = CurrentUrls();

        if (urls.Count == 0)
        {
            MessageBox.Show(this, "Cole pelo menos um link.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sameAnalyzedProfile = chkPlaylist.Checked && urls.Count == 1 &&
                                  !string.IsNullOrWhiteSpace(_analyzedCollectionUrl) &&
                                  string.Equals(
                                      AppCache.NormalizeUrl(urls[0]),
                                      AppCache.NormalizeUrl(_analyzedCollectionUrl),
                                      StringComparison.OrdinalIgnoreCase) &&
                                  profileSelection.HasData;

        if (sameAnalyzedProfile && profileSelection.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "Selecione pelo menos um vídeo do perfil antes de baixar.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var usingSelectedUrls = sameAnalyzedProfile && _selectedCollectionUrls.Count > 0;
        var downloadUrls = usingSelectedUrls ? _selectedCollectionUrls.ToList() : urls;

        var selectedTarget = rbVideo.Checked ? txtVideoOutput : txtAudioOutput;
        var selectedOutput = selectedTarget.Text.Trim();
        if (string.IsNullOrWhiteSpace(selectedOutput))
        {
            BrowseFolder(selectedTarget, rbVideo.Checked ? "Escolha a pasta dos vídeos" : "Escolha a pasta dos áudios");
            selectedOutput = selectedTarget.Text.Trim();
            if (string.IsNullOrWhiteSpace(selectedOutput))
                return;
            SaveDestinationSettings();
            UpdateDestinationSummary();
        }

        if (string.IsNullOrWhiteSpace(txtTemp.Text))
            txtTemp.Text = _tools.TemporaryDirectory;

        var options = new DownloadOptions
        {
            Mode = rbVideo.Checked ? DownloadMode.Video : DownloadMode.Audio,
            Quality = SelectedDownloadQuality(),
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
            PreferCompatibleMp4 = true,
            ExistingFileBehavior = SelectedExistingFileBehavior()
        };

        _activeOutputDirectory = options.OutputDirectory;
        await StopPreviewBeforeRunnerUseAsync();
        _tools.TemporaryDirectory = options.TemporaryDirectory;
        _cts = new CancellationTokenSource();
        SetBusy(true);
        SetProgressUi(new DownloadProgressInfo(0, downloadUrls.Count, 0, "", "", "", "Preparando"));
        AppendLog($"Iniciando {downloadUrls.Count} link(s)...");
        if (downloadUrls.Count == 1 && YtDlpRunner.IsLikelySingleMediaUrl(downloadUrls[0]))
            AppendLog("Detecção automática: mídia individual; o modo de página/playlist será ignorado para acelerar o início.");
        if (usingSelectedUrls)
            AppendLog($"Seleção individual: {downloadUrls.Count} mídias da conta serão baixadas por URL individual.");
        AppendLog($"Destino: {options.OutputDirectory}");
        AppendLog($"Organização por canal/criador: {(options.OrganizeByCreator ? "ativada" : "desativada")}");
        AppendLog(options.AllowPlaylists
            ? $"Página/conta/canal/perfil completo: ativado (limite: {(options.CollectionLimit > 0 ? options.CollectionLimit.ToString() : "todos")})"
            : "Página/conta/canal/perfil completo: desativado");
        if (!string.IsNullOrWhiteSpace(options.SelectedPlaylistItems))
            AppendLog($"Seleção individual ativa: {options.SelectedPlaylistItems.Split(',').Length} itens.");
        AppendLog($"Arquivo existente: {ExistingBehaviorLabel(options.ExistingFileBehavior)}");
        if (options.UseArchive)
            AppendLog("Histórico anti-repetição: ativado (o mesmo ID pode ser ignorado mesmo ao mudar qualidade/formato).");
        if (options.Mode == DownloadMode.Video && string.Equals(options.VideoContainer, "mp4", StringComparison.OrdinalIgnoreCase))
            AppendLog("Compatibilidade MP4: automática — H.264 até 1080p / HEVC acima");

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
                progress.DisplayText = "Concluído";
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
                lblCurrent.Text = "Um ou mais links falharam. Abra Diagnóstico para ver os detalhes.";
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
            lblCurrent.Text = "Erro durante o download. Abra Diagnóstico para ver os detalhes.";
            AppendLog("ERRO: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
            _activeOutputDirectory = null;
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
            await StopPreviewBeforeRunnerUseAsync();
            _tools.TemporaryDirectory = txtTemp.Text.Trim();
            SetBusy(true);
            lblStatus.Text = "Atualizando...";
            lblStatus.ForeColor = TextMuted;
            lblCurrent.Text = "Atualizando yt-dlp, FFmpeg, Deno e gallery-dl...";
            _cts = new CancellationTokenSource();
            await _tools.UpdateAllAsync(
                LogProgress(),
                new Progress<DownloadProgressInfo>(SetProgressUi),
                _cts.Token);
            lblStatus.Text = "Componentes prontos";
            lblStatus.ForeColor = Color.FromArgb(102, 220, 145);
            lblCurrent.Text = "yt-dlp, FFmpeg, Deno e gallery-dl estão prontos.";
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

        const int maxLogChars = 900_000;
        const int trimLogChars = 180_000;

        if (txtLog.TextLength > maxLogChars)
        {
            var cut = Math.Min(trimLogChars, txtLog.TextLength);
            var current = txtLog.Text;
            var lineEnd = current.IndexOf('\n', cut);
            if (lineEnd >= 0)
                cut = lineEnd + 1;

            txtLog.Select(0, cut);
            txtLog.SelectedText = string.Empty;
        }

        txtLog.AppendText(text + Environment.NewLine);
        txtLog.SelectionStart = txtLog.TextLength;
        if (_logVisible)
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
        _busy = busy;
        btnDownload.Enabled = !busy;
        btnStop.Enabled = busy;

        // Atualizar binários em uso continua bloqueado, mas a gaveta de
        // configurações e seus campos permanecem acessíveis. Mudanças feitas
        // durante uma execução valem para a próxima tarefa.
        btnUpdate.Enabled = !busy;
        btnBrowseVideo.Enabled = true;
        btnBrowseAudio.Enabled = true;
        btnBrowseTemp.Enabled = true;
        btnChangeDestination.Enabled = true;
        btnPaste.Enabled = !busy;
        btnAnalyzeCollection.Enabled = !busy && chkPlaylist.Checked;
        btnAdvanced.Enabled = true;
        btnToggleLog.Enabled = true;
        btnCloseSettings.Enabled = true;
        lblSettingsBusyHint.Visible = busy;

        var hasLastFile = !string.IsNullOrWhiteSpace(_runner.LastCompletedFilePath) && File.Exists(_runner.LastCompletedFilePath);
        btnOpenLastFile.Enabled = !busy && hasLastFile;
        btnOpenLastFolder.Enabled = !busy && hasLastFile;
    }

    private static Panel CreateCardPanel(string name)
        => new()
        {
            Name = name,
            Dock = DockStyle.Top,
            BackColor = Panel,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 12)
        };

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
        switch (root)
        {
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

            case GroupBox:
            case TableLayoutPanel:
            case FlowLayoutPanel:
                if (root.BackColor == default || root.BackColor == SystemColors.Control)
                    root.BackColor = Panel;
                root.ForeColor = TextMain;
                break;

            case Panel panel:
                if (panel.Name is "linkCard" or "previewCard" or "actionCard" or "progressCard")
                    panel.BackColor = Panel;
                else if (panel.BackColor == default || panel.BackColor == SystemColors.Control)
                    panel.BackColor = Bg;
                panel.ForeColor = TextMain;
                break;

            case Button button:
                if (button.BackColor != Accent)
                    button.BackColor = Field;
                button.ForeColor = TextMain;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = button.BackColor == Accent ? Accent : Border;
                button.FlatAppearance.MouseOverBackColor = button.BackColor == Accent
                    ? Color.FromArgb(128, 93, 255)
                    : Color.FromArgb(42, 42, 42);
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

            default:
                root.ForeColor = root.ForeColor == SystemColors.ControlText ? TextMain : root.ForeColor;
                break;
        }

        foreach (Control child in root.Controls)
            ApplyDarkTheme(child);
    }
}
