using System.Net.Http.Headers;

namespace Plutao;

public sealed class CollectionPickerForm : Form
{
    private readonly IReadOnlyList<CollectionMediaItem> _items;
    private readonly ProfileInfo? _profile;
    private readonly HttpClient _http = new();
    private readonly List<Image> _loadedImages = new();

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        MultiSelect = false,
        ReadOnly = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        BackgroundColor = Color.FromArgb(18, 18, 18),
        BorderStyle = BorderStyle.FixedSingle,
        GridColor = Color.FromArgb(50, 50, 50),
        EnableHeadersVisualStyles = false
    };

    private readonly PictureBox _avatar = new()
    {
        SizeMode = PictureBoxSizeMode.Zoom,
        Width = 96,
        Height = 96,
        BackColor = Color.FromArgb(28, 28, 28),
        Margin = new Padding(0, 0, 14, 0)
    };

    private readonly Label _profileName = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 14F), ForeColor = Color.White };
    private readonly Label _profileHandle = new() { AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
    private readonly Label _profileStats = new() { AutoSize = true, ForeColor = Color.FromArgb(225, 225, 225) };
    private readonly Label _profileBadges = new() { AutoSize = true, ForeColor = Color.FromArgb(190, 190, 190) };
    private readonly Label _profileBio = new() { AutoSize = true, MaximumSize = new Size(850, 72), ForeColor = Color.FromArgb(205, 205, 205) };
    private readonly Label _summary = new() { AutoSize = true, ForeColor = Color.FromArgb(210, 210, 210) };
    private readonly Button _all = new() { Text = "Marcar todos", AutoSize = true };
    private readonly Button _none = new() { Text = "Desmarcar todos", AutoSize = true };
    private readonly Button _ok = new() { Text = "USAR SELECIONADOS", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancelar", AutoSize = true };
    private readonly Button _openProfile = new() { Text = "Abrir perfil", AutoSize = true };

    public IReadOnlyList<CollectionMediaItem> SelectedItems
        => _grid.Rows.Cast<DataGridViewRow>()
            .Where(row => Convert.ToBoolean(row.Cells[0].Value ?? false))
            .Select(row => (CollectionMediaItem)row.Tag!)
            .ToArray();

    public IReadOnlyList<int> SelectedIndexes
        => SelectedItems.Select(item => item.Index).ToArray();

    public CollectionPickerForm(IReadOnlyList<CollectionMediaItem> items, ProfileInfo? profile = null)
    {
        _items = items;
        _profile = profile;
        Text = "Plutao - Perfil e seleção de vídeos";
        Width = 1040;
        Height = 720;
        MinimumSize = new Size(720, 500);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(10, 10, 10);
        ForeColor = Color.FromArgb(240, 240, 240);

        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));

        BuildUi();
        PopulateProfile();
        PopulateGrid();
        WireEvents();
        UpdateSummary();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var profileBox = new GroupBox
        {
            Text = "Perfil",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(18, 18, 18),
            ForeColor = ForeColor
        };

        var profileLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1
        };
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        profileLayout.Controls.Add(_avatar, 0, 0);

        var profileText = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        profileText.Controls.Add(_profileName);
        profileText.Controls.Add(_profileHandle);
        profileText.Controls.Add(_profileStats);
        profileText.Controls.Add(_profileBadges);
        profileText.Controls.Add(_profileBio);
        profileText.Controls.Add(_openProfile);
        profileLayout.Controls.Add(profileText, 1, 0);
        profileBox.Controls.Add(profileLayout);
        root.Controls.Add(profileBox, 0, 0);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 8)
        };
        top.Controls.Add(_summary);
        top.Controls.Add(_all);
        top.Controls.Add(_none);
        root.Controls.Add(top, 0, 1);

        ConfigureGrid();
        root.Controls.Add(_grid, 0, 2);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0)
        };
        bottom.Controls.Add(_cancel);
        bottom.Controls.Add(_ok);
        root.Controls.Add(bottom, 0, 3);

        foreach (var button in new[] { _all, _none, _ok, _cancel, _openProfile })
        {
            button.BackColor = Color.FromArgb(28, 28, 28);
            button.ForeColor = Color.FromArgb(240, 240, 240);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(62, 62, 62);
            button.MinimumSize = new Size(110, 32);
        }
    }

    private void ConfigureGrid()
    {
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(28, 28, 28);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(235, 235, 235);
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(28, 28, 28);
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(20, 20, 20);
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(235, 235, 235);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 40, 90);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.RowTemplate.Height = 88;

        var check = new DataGridViewCheckBoxColumn
        {
            HeaderText = "✓",
            Width = 42,
            MinimumWidth = 42,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        var thumb = new DataGridViewImageColumn
        {
            HeaderText = "Miniatura",
            Width = 140,
            MinimumWidth = 110,
            ImageLayout = DataGridViewImageCellLayout.Zoom,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        var info = new DataGridViewTextBoxColumn
        {
            HeaderText = "Vídeo",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 260,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        var details = new DataGridViewTextBoxColumn
        {
            HeaderText = "Informações",
            Width = 210,
            MinimumWidth = 150,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _grid.Columns.AddRange(check, thumb, info, details);
    }

    private void PopulateProfile()
    {
        if (_profile is null)
        {
            _profileName.Text = "Informações do perfil indisponíveis";
            _profileHandle.Text = "A lista de vídeos ainda pode ser usada normalmente.";
            _profileStats.Text = $"Vídeos encontrados: {_items.Count:N0}";
            _profileBadges.Visible = false;
            _profileBio.Visible = false;
            _openProfile.Visible = false;
            return;
        }

        _profileName.Text = string.IsNullOrWhiteSpace(_profile.DisplayName)
            ? (!string.IsNullOrWhiteSpace(_profile.Username) ? _profile.Username : _profile.Platform)
            : _profile.DisplayName;
        _profileHandle.Text = string.IsNullOrWhiteSpace(_profile.Username)
            ? _profile.Platform
            : $"@{_profile.Username}  •  {_profile.Platform}";

        var stats = new List<string>();
        if (_profile.Followers.HasValue) stats.Add($"{FormatCount(_profile.Followers.Value)} seguidores");
        if (_profile.Following.HasValue) stats.Add($"{FormatCount(_profile.Following.Value)} seguindo");
        if (_profile.Posts.HasValue) stats.Add($"{FormatCount(_profile.Posts.Value)} publicações");
        stats.Add($"{_profile.FoundVideos:N0} vídeos encontrados");
        _profileStats.Text = string.Join("   •   ", stats);

        var badges = new List<string>();
        if (_profile.IsVerified == true) badges.Add("✓ Verificado");
        if (_profile.IsPrivate == true) badges.Add("Conta privada");
        else if (_profile.IsPrivate == false) badges.Add("Conta pública");
        _profileBadges.Text = string.Join("   •   ", badges);
        _profileBadges.Visible = badges.Count > 0;

        _profileBio.Text = _profile.Biography;
        _profileBio.Visible = !string.IsNullOrWhiteSpace(_profile.Biography);
        _openProfile.Visible = !string.IsNullOrWhiteSpace(_profile.ExternalUrl);
    }

    private void PopulateGrid()
    {
        foreach (var item in _items)
        {
            var infoText = $"{item.Index:000}  —  {item.Title}";
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.Duration)) details.Add(item.Duration);
            if (!string.IsNullOrWhiteSpace(item.Date)) details.Add(item.Date);
            if (!string.IsNullOrWhiteSpace(item.Details)) details.Add(item.Details);

            var rowIndex = _grid.Rows.Add(true, null, infoText, string.Join(Environment.NewLine, details));
            _grid.Rows[rowIndex].Tag = item;
        }
    }

    private void WireEvents()
    {
        _all.Click += (_, _) => SetAll(true);
        _none.Click += (_, _) => SetAll(false);
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.ColumnIndex == 0) UpdateSummary();
        };
        _ok.Click += (_, _) =>
        {
            if (SelectedIndexes.Count == 0)
            {
                MessageBox.Show(this, "Selecione pelo menos um vídeo.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _openProfile.Click += (_, _) =>
        {
            if (_profile is not null && !string.IsNullOrWhiteSpace(_profile.ExternalUrl))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_profile.ExternalUrl) { UseShellExecute = true }); }
                catch { }
            }
        };

        AcceptButton = _ok;
        CancelButton = _cancel;
        Shown += async (_, _) =>
        {
            FitToCurrentScreen();
            await LoadImagesAsync();
        };
        DpiChanged += (_, _) => BeginInvoke(new Action(FitToCurrentScreen));
        FormClosed += (_, _) => DisposeImages();
    }

    private async Task LoadImagesAsync()
    {
        try
        {
            if (_profile is not null && !string.IsNullOrWhiteSpace(_profile.AvatarUrl))
            {
                var avatar = await DownloadImageAsync(_profile.AvatarUrl);
                if (avatar is not null && !IsDisposed)
                {
                    _loadedImages.Add(avatar);
                    _avatar.Image = avatar;
                }
            }

            using var gate = new SemaphoreSlim(6);
            var tasks = _grid.Rows.Cast<DataGridViewRow>()
                .Select(async row =>
                {
                    if (row.Tag is not CollectionMediaItem item || string.IsNullOrWhiteSpace(item.ThumbnailUrl))
                        return;

                    await gate.WaitAsync();
                    try
                    {
                        var image = await DownloadImageAsync(item.ThumbnailUrl);
                        if (image is null || IsDisposed) return;
                        _loadedImages.Add(image);
                        row.Cells[1].Value = image;
                    }
                    catch { }
                    finally { gate.Release(); }
                });

            await Task.WhenAll(tasks);
        }
        catch
        {
            // Miniaturas são um recurso visual opcional; falhas não bloqueiam a seleção.
        }
    }

    private async Task<Image?> DownloadImageAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/154.0.0.0 Safari/537.36");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var ms = new MemoryStream(bytes);
            using var source = Image.FromStream(ms);
            return new Bitmap(source);
        }
        catch
        {
            return null;
        }
    }

    private void DisposeImages()
    {
        foreach (var image in _loadedImages)
        {
            try { image.Dispose(); } catch { }
        }
        _loadedImages.Clear();
        _http.Dispose();
    }

    private void FitToCurrentScreen()
    {
        if (!IsHandleCreated || WindowState != FormWindowState.Normal) return;
        var area = Screen.FromControl(this).WorkingArea;
        var width = Math.Min(Width, Math.Max(MinimumSize.Width, area.Width - 30));
        var height = Math.Min(Height, Math.Max(MinimumSize.Height, area.Height - 30));
        if (width != Width || height != Height) Size = new Size(width, height);
    }

    private void SetAll(bool value)
    {
        foreach (DataGridViewRow row in _grid.Rows)
            row.Cells[0].Value = value;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var selected = _grid.Rows.Cast<DataGridViewRow>()
            .Count(row => Convert.ToBoolean(row.Cells[0].Value ?? false));
        _summary.Text = $"Encontrados: {_items.Count:N0}   •   Selecionados: {selected:N0}";
    }

    private static string FormatCount(long value)
    {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000d:0.#} bi";
        if (value >= 1_000_000) return $"{value / 1_000_000d:0.#} mi";
        if (value >= 1_000) return $"{value / 1_000d:0.#} mil";
        return value.ToString("N0");
    }
}
