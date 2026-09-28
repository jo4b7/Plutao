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

    private readonly TextBox txtVideoOutput = new();
    private readonly TextBox txtAudioOutput = new();
    private readonly TextBox txtTemp = new();

    private readonly CheckBox chkPlaylist = new() { Text = "Permitir playlist/canal/perfil", AutoSize = true };
    private readonly CheckBox chkOrganize = new() { Text = "Organizar por canal/criador", AutoSize = true };
    private readonly CheckBox chkMetadata = new() { Text = "Incorporar metadados", Checked = true, AutoSize = true };
    private readonly CheckBox chkThumb = new() { Text = "Salvar miniatura", AutoSize = true };
    private readonly CheckBox chkJson = new() { Text = "Salvar info JSON", AutoSize = true };
    private readonly CheckBox chkArchive = new() { Text = "Evitar repetir o mesmo link (histórico)", Checked = false, AutoSize = true };
    private readonly CheckBox chkCompatibleMp4 = new() { Text = "MP4 compatível (H.264/AAC)", Checked = true, AutoSize = true };

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
    private readonly Button btnOpenLastFile = new() { Text = "ABRIR ARQUIVO", Height = 44, Enabled = false };
    private readonly Button btnOpenLastFolder = new() { Text = "ABRIR PASTA", Height = 44, Enabled = false };

    private readonly DarkProgressBar progress = new() { Dock = DockStyle.Fill };
    private readonly TextBox txtLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly Label lblStatus = new() { Text = "Pronto", AutoSize = true };
    private readonly Label lblCurrent = new() { Text = "Aguardando download", AutoEllipsis = true, Dock = DockStyle.Fill };

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
        Width = 1060;
        Height = 860;
        MinimumSize = new Size(920, 720);
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
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Bg
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 175));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var urlBox = CreateGroup("Links (um por linha)", fill: true);
        var urlLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        urlLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        urlLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        txtUrls.Dock = DockStyle.Fill;
        btnPaste.Dock = DockStyle.Top;
        btnPaste.Height = 34;
        urlLayout.Controls.Add(txtUrls, 0, 0);
        urlLayout.Controls.Add(btnPaste, 1, 0);
        urlBox.Controls.Add(urlLayout);
        root.Controls.Add(urlBox);

        var formatBox = CreateGroup("Formato");
        var format = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 8,
            Padding = new Padding(10)
        };
        format.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        format.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        format.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        format.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        format.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        format.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        format.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        format.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));

        format.Controls.Add(rbVideo, 0, 0);
        format.Controls.Add(rbAudio, 1, 0);
        format.Controls.Add(LabelFor("Qualidade:"), 2, 0);
        format.Controls.Add(cmbQuality, 3, 0);
        format.Controls.Add(LabelFor("Contêiner:"), 4, 0);
        format.Controls.Add(cmbVideoFormat, 5, 0);
        format.Controls.Add(LabelFor("Áudio:"), 6, 0);
        format.Controls.Add(cmbAudioFormat, 7, 0);
        SetFill(cmbQuality, cmbVideoFormat, cmbAudioFormat);
        formatBox.Controls.Add(format);
        root.Controls.Add(formatBox);

        var optsBox = CreateGroup("Destino e opções");
        var opts = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 6,
            Padding = new Padding(10)
        };
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));

        AddFolderRow(opts, 0, "Vídeos:", txtVideoOutput, btnBrowseVideo, btnOpenVideo);
        AddFolderRow(opts, 1, "Áudios:", txtAudioOutput, btnBrowseAudio, btnOpenAudio);

        opts.Controls.Add(LabelFor("Temporários:"), 0, 2);
        txtTemp.Dock = DockStyle.Fill;
        opts.Controls.Add(txtTemp, 1, 2);
        btnBrowseTemp.Dock = DockStyle.Fill;
        opts.Controls.Add(btnBrowseTemp, 2, 2);
        opts.SetColumnSpan(btnBrowseTemp, 2);

        opts.Controls.Add(LabelFor("Cookies:"), 0, 3);
        cmbCookies.Width = 160;
        opts.Controls.Add(cmbCookies, 1, 3);
        opts.Controls.Add(chkPlaylist, 2, 3);
        opts.SetColumnSpan(chkPlaylist, 2);

        opts.Controls.Add(LabelFor("Se já existir:"), 0, 4);
        cmbExisting.Dock = DockStyle.Left;
        cmbExisting.Width = 220;
        opts.Controls.Add(cmbExisting, 1, 4);
        opts.SetColumnSpan(cmbExisting, 3);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 6, 0, 0)
        };
        checks.Controls.Add(chkOrganize);
        checks.Controls.Add(chkMetadata);
        checks.Controls.Add(chkThumb);
        checks.Controls.Add(chkJson);
        checks.Controls.Add(chkArchive);
        checks.Controls.Add(chkCompatibleMp4);
        opts.Controls.Add(checks, 0, 5);
        opts.SetColumnSpan(checks, 4);

        optsBox.Controls.Add(opts);
        root.Controls.Add(optsBox);

        var toolsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Padding = new Padding(0, 8, 0, 4)
        };
        toolsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolsRow.Controls.Add(btnUpdate, 0, 0);
        toolsRow.Controls.Add(btnClearLog, 1, 0);
        lblStatus.Anchor = AnchorStyles.Right;
        toolsRow.Controls.Add(lblStatus, 3, 0);
        root.Controls.Add(toolsRow);

        var progressBox = CreateGroup("Progresso", fill: true);
        var progressLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lblCurrent.ForeColor = TextMuted;
        progressLayout.Controls.Add(lblCurrent, 0, 0);
        progressLayout.Controls.Add(progress, 0, 1);
        progressBox.Controls.Add(progressLayout);
        root.Controls.Add(progressBox);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(0, 8, 0, 8) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        btnDownload.Dock = DockStyle.Fill;
        btnStop.Dock = DockStyle.Fill;
        btnOpenLastFile.Dock = DockStyle.Fill;
        btnOpenLastFolder.Dock = DockStyle.Fill;
        actions.Controls.Add(btnDownload, 0, 0);
        actions.Controls.Add(btnStop, 1, 0);
        actions.Controls.Add(btnOpenLastFile, 2, 0);
        actions.Controls.Add(btnOpenLastFolder, 3, 0);
        root.Controls.Add(actions);

        var logBox = CreateGroup("Log", fill: true);
        txtLog.Dock = DockStyle.Fill;
        logBox.Controls.Add(txtLog);
        root.Controls.Add(logBox);
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

        var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var plutaoRoot = Path.Combine(downloads, "Downloads", "Plutao");
        txtVideoOutput.Text = Path.Combine(plutaoRoot, "Videos");
        txtAudioOutput.Text = Path.Combine(plutaoRoot, "Audios");
        txtTemp.Text = _tools.TemporaryDirectory;

        chkOrganize.Checked = false;
        chkArchive.Checked = false;
        chkCompatibleMp4.Checked = true;
        progress.Value = 0;
        progress.DisplayText = "0%";
        UpdateModeUi();
    }

    private void WireEvents()
    {
        rbVideo.CheckedChanged += (_, _) => UpdateModeUi();
        rbAudio.CheckedChanged += (_, _) => UpdateModeUi();
        cmbVideoFormat.SelectedIndexChanged += (_, _) => UpdateModeUi();
        btnPaste.Click += (_, _) => { if (Clipboard.ContainsText()) txtUrls.Text = Clipboard.GetText(); };

        btnBrowseVideo.Click += (_, _) => BrowseFolder(txtVideoOutput, "Escolha a pasta dos vídeos");
        btnOpenVideo.Click += (_, _) => OpenFolder(txtVideoOutput.Text);
        btnBrowseAudio.Click += (_, _) => BrowseFolder(txtAudioOutput, "Escolha a pasta dos áudios");
        btnOpenAudio.Click += (_, _) => OpenFolder(txtAudioOutput.Text);
        btnBrowseTemp.Click += (_, _) => BrowseFolder(txtTemp, "Escolha a pasta para arquivos temporários");

        btnDownload.Click += async (_, _) => await StartDownloadAsync();
        btnStop.Click += (_, _) => StopDownload();
        btnUpdate.Click += async (_, _) => await RunToolActionAsync();
        btnClearLog.Click += (_, _) => txtLog.Clear();
        btnOpenLastFile.Click += (_, _) => OpenLastFile();
        btnOpenLastFolder.Click += (_, _) => OpenLastFileFolder();
        FormClosing += (_, _) => StopDownload();
    }

    private void UpdateModeUi()
    {
        cmbQuality.Enabled = rbVideo.Checked;
        cmbVideoFormat.Enabled = rbVideo.Checked;
        cmbAudioFormat.Enabled = rbAudio.Checked;
        chkCompatibleMp4.Enabled = rbVideo.Checked && string.Equals(cmbVideoFormat.SelectedItem?.ToString(), "mp4", StringComparison.OrdinalIgnoreCase);

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
            AllowPlaylists = chkPlaylist.Checked,
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
        AppendLog($"Iniciando {urls.Count} link(s)...");
        AppendLog($"Destino: {options.OutputDirectory}");
        AppendLog($"Organização por canal/criador: {(options.OrganizeByCreator ? "ativada" : "desativada")}");
        AppendLog($"Arquivo existente: {ExistingBehaviorLabel(options.ExistingFileBehavior)}");
        if (options.UseArchive)
            AppendLog("Histórico anti-repetição: ativado (o mesmo ID pode ser ignorado mesmo ao mudar qualidade/formato).");
        if (options.Mode == DownloadMode.Video && string.Equals(options.VideoContainer, "mp4", StringComparison.OrdinalIgnoreCase))
            AppendLog($"Compatibilidade MP4: {(options.PreferCompatibleMp4 ? "H.264/AAC" : "melhor codec disponível")}");

        try
        {
            var code = await _runner.DownloadAsync(
                urls,
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
            parts.Add($"Item {info.ItemIndex}/{info.ItemCount}");
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
