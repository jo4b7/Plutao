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
    private readonly TextBox txtOutput = new();
    private readonly CheckBox chkPlaylist = new() { Text = "Permitir playlist/canal/perfil", AutoSize = true };
    private readonly CheckBox chkMetadata = new() { Text = "Incorporar metadados", Checked = true, AutoSize = true };
    private readonly CheckBox chkThumb = new() { Text = "Salvar miniatura", AutoSize = true };
    private readonly CheckBox chkJson = new() { Text = "Salvar info JSON", AutoSize = true };
    private readonly CheckBox chkArchive = new() { Text = "Evitar downloads repetidos (arquivo de histórico)", Checked = true, AutoSize = true };
    private readonly Button btnDownload = new() { Text = "BAIXAR", Height = 42 };
    private readonly Button btnStop = new() { Text = "PARAR", Height = 42, Enabled = false };
    private readonly Button btnPaste = new() { Text = "Colar" };
    private readonly Button btnBrowse = new() { Text = "Procurar..." };
    private readonly Button btnUpdate = new() { Text = "Atualizar yt-dlp" };
    private readonly Button btnFfmpeg = new() { Text = "Instalar FFmpeg" };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100 };
    private readonly TextBox txtLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly Label lblStatus = new() { Text = "Pronto", AutoSize = true };

    public MainForm()
    {
        _runner = new YtDlpRunner(_tools);
        Text = "Plutao - Downloader Universal";
        Width = 950;
        Height = 760;
        MinimumSize = new Size(820, 650);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);

        BuildUi();
        WireEvents();
        LoadDefaults();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 6
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var urlBox = new GroupBox { Text = "Links (um por linha)", Dock = DockStyle.Fill };
        var urlLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8) };
        urlLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        urlLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        txtUrls.Dock = DockStyle.Fill;
        btnPaste.Dock = DockStyle.Top;
        urlLayout.Controls.Add(txtUrls, 0, 0);
        urlLayout.Controls.Add(btnPaste, 1, 0);
        urlBox.Controls.Add(urlLayout);
        root.Controls.Add(urlBox);

        var formatBox = new GroupBox { Text = "Formato", Dock = DockStyle.Fill };
        var format = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, Padding = new Padding(8), AutoSize = true };
        for (int i = 0; i < 8; i++) format.ColumnStyles.Add(new ColumnStyle(i % 2 == 0 ? SizeType.AutoSize : SizeType.Percent, i % 2 == 0 ? 0 : 25));
        format.Controls.Add(rbVideo, 0, 0);
        format.Controls.Add(rbAudio, 1, 0);
        format.Controls.Add(new Label { Text = "Qualidade:", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        format.Controls.Add(cmbQuality, 3, 0);
        format.Controls.Add(new Label { Text = "Contêiner:", AutoSize = true, Anchor = AnchorStyles.Left }, 4, 0);
        format.Controls.Add(cmbVideoFormat, 5, 0);
        format.Controls.Add(new Label { Text = "Áudio:", AutoSize = true, Anchor = AnchorStyles.Left }, 6, 0);
        format.Controls.Add(cmbAudioFormat, 7, 0);
        formatBox.Controls.Add(format);
        root.Controls.Add(formatBox);

        var optsBox = new GroupBox { Text = "Destino e opções", Dock = DockStyle.Fill };
        var opts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(8) };
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        opts.Controls.Add(new Label { Text = "Pasta:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        txtOutput.Dock = DockStyle.Fill;
        opts.Controls.Add(txtOutput, 1, 0);
        opts.Controls.Add(btnBrowse, 2, 0);
        opts.Controls.Add(new Label { Text = "Cookies:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        opts.Controls.Add(cmbCookies, 1, 1);
        opts.Controls.Add(chkPlaylist, 2, 1);
        opts.SetColumnSpan(chkPlaylist, 2);
        var checks = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        checks.Controls.Add(chkMetadata); checks.Controls.Add(chkThumb); checks.Controls.Add(chkJson); checks.Controls.Add(chkArchive);
        opts.Controls.Add(checks, 0, 2); opts.SetColumnSpan(checks, 4);
        optsBox.Controls.Add(opts);
        root.Controls.Add(optsBox);

        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 0) };
        tools.Controls.Add(btnUpdate);
        tools.Controls.Add(btnFfmpeg);
        tools.Controls.Add(lblStatus);
        root.Controls.Add(tools);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        btnDownload.Dock = DockStyle.Fill;
        btnStop.Dock = DockStyle.Fill;
        progress.Dock = DockStyle.Fill;
        actions.Controls.Add(btnDownload, 0, 0);
        actions.Controls.Add(btnStop, 1, 0);
        actions.Controls.Add(progress, 2, 0);
        root.Controls.Add(actions);

        var logBox = new GroupBox { Text = "Log", Dock = DockStyle.Fill };
        txtLog.Dock = DockStyle.Fill;
        logBox.Controls.Add(txtLog);
        root.Controls.Add(logBox);
    }

    private void LoadDefaults()
    {
        cmbQuality.Items.AddRange(new object[] { "Melhor", "2160p", "1440p", "1080p", "720p", "480p", "360p" });
        cmbQuality.SelectedItem = "1080p";
        cmbVideoFormat.Items.AddRange(new object[] { "mp4", "mkv", "webm" });
        cmbVideoFormat.SelectedItem = "mp4";
        cmbAudioFormat.Items.AddRange(new object[] { "mp3", "m4a", "aac", "flac", "wav", "opus" });
        cmbAudioFormat.SelectedItem = "mp3";
        cmbCookies.Items.AddRange(new object[] { "Nenhum", "Chrome", "Edge", "Firefox" });
        cmbCookies.SelectedIndex = 0;
        txtOutput.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Plutao");
        UpdateModeUi();
    }

    private void WireEvents()
    {
        rbVideo.CheckedChanged += (_, _) => UpdateModeUi();
        rbAudio.CheckedChanged += (_, _) => UpdateModeUi();
        btnPaste.Click += (_, _) => { if (Clipboard.ContainsText()) txtUrls.Text = Clipboard.GetText(); };
        btnBrowse.Click += (_, _) => BrowseFolder();
        btnDownload.Click += async (_, _) => await StartDownloadAsync();
        btnStop.Click += (_, _) => StopDownload();
        btnUpdate.Click += async (_, _) => await RunToolActionAsync(() => _tools.DownloadYtDlpAsync(LogProgress()));
        btnFfmpeg.Click += async (_, _) => await RunToolActionAsync(() => _tools.DownloadFfmpegAsync(LogProgress()));
        FormClosing += (_, _) => StopDownload();
    }

    private void UpdateModeUi()
    {
        cmbQuality.Enabled = rbVideo.Checked;
        cmbVideoFormat.Enabled = rbVideo.Checked;
        cmbAudioFormat.Enabled = rbAudio.Checked;
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog { InitialDirectory = txtOutput.Text, UseDescriptionForTitle = true, Description = "Escolha onde salvar os downloads" };
        if (dialog.ShowDialog(this) == DialogResult.OK) txtOutput.Text = dialog.SelectedPath;
    }

    private async Task StartDownloadAsync()
    {
        var urls = txtUrls.Lines.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (urls.Count == 0)
        {
            MessageBox.Show(this, "Cole pelo menos um link.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(txtOutput.Text))
        {
            MessageBox.Show(this, "Escolha a pasta de destino.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var options = new DownloadOptions
        {
            Mode = rbVideo.Checked ? DownloadMode.Video : DownloadMode.Audio,
            Quality = cmbQuality.SelectedItem?.ToString() ?? "1080p",
            VideoContainer = cmbVideoFormat.SelectedItem?.ToString() ?? "mp4",
            AudioFormat = cmbAudioFormat.SelectedItem?.ToString() ?? "mp3",
            OutputDirectory = txtOutput.Text.Trim(),
            BrowserCookies = cmbCookies.SelectedItem?.ToString() ?? "Nenhum",
            AllowPlaylists = chkPlaylist.Checked,
            EmbedMetadata = chkMetadata.Checked,
            SaveThumbnail = chkThumb.Checked,
            SaveInfoJson = chkJson.Checked,
            UseArchive = chkArchive.Checked
        };

        _cts = new CancellationTokenSource();
        SetBusy(true);
        progress.Value = 0;
        AppendLog($"Iniciando {urls.Count} link(s)...");

        try
        {
            var code = await _runner.DownloadAsync(urls, options, LogProgress(), new Progress<int>(SetProgress), _cts.Token);
            lblStatus.Text = code == 0 ? "Concluído" : "Concluído com erro(s)";
            AppendLog(code == 0 ? "Fila concluída." : "Fila concluída; confira o log para erros.");
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = "Cancelado";
            AppendLog("Download cancelado.");
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Erro";
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

    private void StopDownload()
    {
        _cts?.Cancel();
        _runner.Stop();
    }

    private async Task RunToolActionAsync(Func<Task> action)
    {
        try
        {
            SetBusy(true);
            await action();
            lblStatus.Text = "Ferramentas prontas";
        }
        catch (Exception ex)
        {
            AppendLog("ERRO: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private IProgress<string> LogProgress() => new Progress<string>(AppendLog);

    private void AppendLog(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(text)); return; }
        txtLog.AppendText(text + Environment.NewLine);
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.ScrollToCaret();
    }

    private void SetProgress(int value)
    {
        if (InvokeRequired) { BeginInvoke(() => SetProgress(value)); return; }
        progress.Value = Math.Clamp(value, progress.Minimum, progress.Maximum);
    }

    private void SetBusy(bool busy)
    {
        btnDownload.Enabled = !busy;
        btnStop.Enabled = busy;
        btnUpdate.Enabled = !busy;
        btnFfmpeg.Enabled = !busy;
        lblStatus.Text = busy ? "Trabalhando..." : lblStatus.Text;
    }
}
