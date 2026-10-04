using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Plutao;

public sealed class ProfileSelectionPanel : UserControl
{
    private static readonly string CacheDirectory = AppCache.ThumbnailDirectory;
    private static readonly string RenderedCacheDirectory = AppCache.RenderedThumbnailDirectory;
    private static readonly ConcurrentDictionary<string, byte[]> MemoryCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte[]> RenderedMemoryCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, DateTime> FailedImageUntil = new(StringComparer.Ordinal);
    private const int MaxMemoryCacheEntries = 512;
    private static readonly HttpClient Http = CreateHttpClient();
    private static int _cacheWrites;

    private IReadOnlyList<CollectionMediaItem> _items = Array.Empty<CollectionMediaItem>();
    private IReadOnlyList<CollectionMediaItem> _visibleItems = Array.Empty<CollectionMediaItem>();
    private readonly HashSet<string> _selectedKeys = new(StringComparer.Ordinal);
    private ProfileInfo? _profile;
    private CancellationTokenSource? _imageCts;
    private readonly List<Image> _loadedImages = new();
    private readonly Dictionary<int, Image> _rowImages = new();
    private readonly ConcurrentDictionary<int, byte> _thumbnailLoaded = new();
    private readonly ConcurrentDictionary<int, byte> _thumbnailLoading = new();
    private readonly ConcurrentDictionary<int, DateTime> _thumbnailRetryAfter = new();
    private readonly SemaphoreSlim _thumbnailGate = new(6, 6);
    private readonly System.Windows.Forms.Timer _thumbnailTimer = new() { Interval = 70 };
    private bool _suppressSelectionEvents;
    private bool _visualWorkPaused;
    private int _currentRowHeight = 92;
    private int _viewGeneration;
    private string _activeCategory = "Todos";
    private readonly Dictionary<string, Button> _categoryButtons = new(StringComparer.OrdinalIgnoreCase);

    private readonly FastDataGridView _grid = new()
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
        EnableHeadersVisualStyles = false,
        ShowCellToolTips = true
    };

    private readonly PictureBox _avatar = new()
    {
        SizeMode = PictureBoxSizeMode.Zoom,
        Width = 82,
        Height = 82,
        BackColor = Color.FromArgb(28, 28, 28),
        Margin = new Padding(0, 0, 12, 0)
    };

    private readonly Label _profileName = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 13F), ForeColor = Color.White };
    private readonly Label _profileHandle = new() { AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
    private readonly Label _profileStats = new() { AutoSize = true, ForeColor = Color.FromArgb(225, 225, 225) };
    private readonly Label _profileBadges = new() { AutoSize = true, ForeColor = Color.FromArgb(190, 190, 190) };
    private readonly Label _profileBio = new() { AutoSize = true, MaximumSize = new Size(920, 54), ForeColor = Color.FromArgb(205, 205, 205) };
    private readonly Label _summary = new() { AutoSize = true, ForeColor = Color.FromArgb(210, 210, 210) };
    private readonly Label _loading = new() { AutoSize = true, ForeColor = Color.FromArgb(165, 165, 165), Text = "" };

    private readonly Button _all = new() { Text = "Selecionar todos", AutoSize = true };
    private readonly Button _none = new() { Text = "Limpar seleção", AutoSize = true };
    private readonly Button _openProfile = new() { Text = "Abrir perfil", AutoSize = true };
    private readonly Button _hide = new() { Text = "Ocultar resultados", AutoSize = true };
    private readonly FlowLayoutPanel _categoryBar = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        Padding = new Padding(4, 0, 4, 6),
        BackColor = Color.FromArgb(18, 18, 18),
        Visible = false
    };

    public event EventHandler? SelectionChanged;
    public event EventHandler? HideRequested;

    public bool HasData => _items.Count > 0;
    public int TotalItems => _items.Count;

    public void SetVisualWorkPaused(bool paused)
    {
        _visualWorkPaused = paused;
        if (paused)
        {
            _thumbnailTimer.Stop();
            return;
        }

        if (Visible && _items.Count > 0 && !IsDisposed && IsHandleCreated)
        {
            try { BeginInvoke(new Action(() => QueueVisibleThumbnails(immediate: false))); }
            catch (InvalidOperationException) { }
        }
    }

    public IReadOnlyList<CollectionMediaItem> SelectedItems
        => _items
            .Where(item => _selectedKeys.Contains(SelectionKey(item)))
            .ToArray();

    public ProfileSelectionPanel()
    {
        Dock = DockStyle.Top;
        Height = 520;
        BackColor = Color.FromArgb(10, 10, 10);
        ForeColor = Color.FromArgb(240, 240, 240);
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Visible = false;

        BuildUi();
        WireEvents();
    }

    public void LoadData(IReadOnlyList<CollectionMediaItem> items, ProfileInfo? profile)
    {
        CancelImageLoading();
        DisposeImages();
        _thumbnailLoaded.Clear();
        _thumbnailLoading.Clear();
        _thumbnailRetryAfter.Clear();

        _items = items;
        _visibleItems = items;
        _selectedKeys.Clear();
        foreach (var item in items)
            _selectedKeys.Add(SelectionKey(item));

        _profile = profile;
        _activeCategory = "Todos";
        _viewGeneration++;
        _suppressSelectionEvents = true;

        _grid.BeginBatchUpdate();
        _grid.SuspendLayout();
        try
        {
            _grid.RowCount = 0;
            PopulateProfile();
            PopulateGrid();
            BuildCategoryButtons();
        }
        finally
        {
            _grid.ResumeLayout(false);
            _grid.EndBatchUpdate();
        }

        _suppressSelectionEvents = false;
        UpdateSummary();
        Visible = true;

        _imageCts = new CancellationTokenSource();
        _ = LoadAvatarAsync(_imageCts.Token);
        BeginInvoke(new Action(() => QueueVisibleThumbnails(immediate: true)));
    }

    public void ClearData()
    {
        CancelImageLoading();
        DisposeImages();
        _thumbnailLoaded.Clear();
        _thumbnailLoading.Clear();
        _thumbnailRetryAfter.Clear();
        _items = Array.Empty<CollectionMediaItem>();
        _visibleItems = Array.Empty<CollectionMediaItem>();
        _selectedKeys.Clear();
        _viewGeneration++;
        _profile = null;
        _grid.RowCount = 0;
        _categoryBar.Controls.Clear();
        _categoryButtons.Clear();
        _categoryBar.Visible = false;
        _activeCategory = "Todos";
        _summary.Text = string.Empty;
        _loading.Text = string.Empty;
        _avatar.Image = null;
        Visible = false;
    }

    public void ApplyResponsiveHeight(bool compact, bool roomy)
    {
        var hostHeight = FindForm()?.ClientSize.Height ?? Height;
        var targetHeight = compact
            ? Math.Clamp((int)(hostHeight * 0.52), 360, 480)
            : roomy
                ? Math.Clamp((int)(hostHeight * 0.68), 640, 840)
                : Math.Clamp((int)(hostHeight * 0.56), 480, 680);

        if (Height != targetHeight)
            Height = targetHeight;

        // Não percorre milhares de linhas durante maximize/restore.
        // O custo dessa operação era a principal causa dos engasgos em canais grandes.
        const int rowHeight = 92;
        if (_currentRowHeight != rowHeight)
        {
            _currentRowHeight = rowHeight;
            _grid.RowTemplate.Height = rowHeight;
        }

        QueueVisibleThumbnails();
    }

    private void BuildUi()
    {
        var box = new GroupBox
        {
            Text = "Perfil e vídeos encontrados",
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(18, 18, 18),
            ForeColor = ForeColor
        };
        Controls.Add(box);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.FromArgb(18, 18, 18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        box.Controls.Add(root);

        var profileRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(4)
        };
        profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        profileRow.Controls.Add(_avatar, 0, 0);

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
        profileRow.Controls.Add(profileText, 1, 0);

        var profileActions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        profileActions.Controls.Add(_openProfile);
        profileActions.Controls.Add(_hide);
        profileRow.Controls.Add(profileActions, 2, 0);
        root.Controls.Add(profileRow, 0, 0);

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(4, 4, 4, 6)
        };
        tools.Controls.Add(_summary);
        tools.Controls.Add(_all);
        tools.Controls.Add(_none);
        tools.Controls.Add(_loading);
        root.Controls.Add(tools, 0, 1);

        root.Controls.Add(_categoryBar, 0, 2);

        ConfigureGrid();
        root.Controls.Add(_grid, 0, 3);

        foreach (var button in new[] { _all, _none, _openProfile, _hide })
        {
            button.BackColor = Color.FromArgb(28, 28, 28);
            button.ForeColor = Color.FromArgb(240, 240, 240);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(62, 62, 62);
            button.MinimumSize = new Size(108, 30);
        }
    }

    private void ConfigureGrid()
    {
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(28, 28, 28);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(235, 235, 235);
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(28, 28, 28);
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(20, 20, 20);
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(235, 235, 235);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(34, 34, 34);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.RowTemplate.Height = 92;
        _grid.VirtualMode = true;

        _grid.Columns.AddRange(
            new DataGridViewCheckBoxColumn
            {
                HeaderText = "Selecionado",
                Width = 78,
                MinimumWidth = 68,
                ReadOnly = true,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable
            },
            new DataGridViewImageColumn
            {
                HeaderText = "Miniatura",
                Width = 100,
                MinimumWidth = 82,
                ImageLayout = DataGridViewImageCellLayout.Zoom,
                SortMode = DataGridViewColumnSortMode.NotSortable
            },
            new DataGridViewTextBoxColumn
            {
                HeaderText = "Vídeo",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 54,
                MinimumWidth = 180,
                ReadOnly = true,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    WrapMode = DataGridViewTriState.True,
                    Alignment = DataGridViewContentAlignment.MiddleLeft
                },
                SortMode = DataGridViewColumnSortMode.NotSortable
            },
            new DataGridViewTextBoxColumn
            {
                HeaderText = "Informações",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 46,
                MinimumWidth = 160,
                ReadOnly = true,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    WrapMode = DataGridViewTriState.True,
                    Alignment = DataGridViewContentAlignment.MiddleLeft
                },
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
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

        var cleanUsername = (_profile.Username ?? string.Empty).Trim().TrimStart('@');
        _profileName.Text = string.IsNullOrWhiteSpace(_profile.DisplayName)
            ? (!string.IsNullOrWhiteSpace(cleanUsername) ? cleanUsername : _profile.Platform)
            : _profile.DisplayName;
        _profileHandle.Text = string.IsNullOrWhiteSpace(cleanUsername)
            ? _profile.Platform
            : $"@{cleanUsername}  •  {_profile.Platform}";

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
        if (!string.IsNullOrWhiteSpace(_profile.CreatedDate)) badges.Add($"Conta criada em {_profile.CreatedDate}");
        _profileBadges.Text = string.Join("   •   ", badges);
        _profileBadges.Visible = badges.Count > 0;

        _profileBio.Text = _profile.Biography;
        _profileBio.Visible = !string.IsNullOrWhiteSpace(_profile.Biography);
        _openProfile.Visible = !string.IsNullOrWhiteSpace(_profile.ExternalUrl);
    }

    private void PopulateGrid()
    {
        _visibleItems = _items;
        _grid.RowCount = _visibleItems.Count;
        _grid.Invalidate();
    }

    private void WireEvents()
    {
        _all.Click += (_, _) => SetAll(true);
        _none.Click += (_, _) => SetAll(false);
        _hide.Click += (_, _) => HideRequested?.Invoke(this, EventArgs.Empty);
        _openProfile.Click += (_, _) =>
        {
            if (_profile is null || string.IsNullOrWhiteSpace(_profile.ExternalUrl)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_profile.ExternalUrl)
                {
                    UseShellExecute = true
                });
            }
            catch { }
        };

        _grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleItems.Count)
                return;

            var item = _visibleItems[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => _selectedKeys.Contains(SelectionKey(item)),
                1 => _rowImages.TryGetValue(e.RowIndex, out var image) ? image : null,
                2 => $"{item.Index:000}  —  {CompactText(item.Title, 240)}",
                3 => CompactDetails(item),
                _ => null
            };
        };

        _grid.RowPrePaint += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleItems.Count)
                return;

            UpdateRowVisual(
                _grid.Rows[e.RowIndex],
                _selectedKeys.Contains(SelectionKey(_visibleItems[e.RowIndex])));
        };

        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
                return;
            ToggleRow(e.RowIndex);
        };
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space || _grid.CurrentRow is null)
                return;
            ToggleRow(_grid.CurrentRow.Index);
            e.Handled = true;
        };
        _grid.Scroll += (_, _) => ScheduleVisibleThumbnailLoad();
        _grid.Resize += (_, _) => ScheduleVisibleThumbnailLoad();
        VisibleChanged += (_, _) =>
        {
            if (Visible && !_visualWorkPaused)
                ScheduleVisibleThumbnailLoad();
        };
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleItems.Count)
                return;

            var item = _visibleItems[e.RowIndex];
            if (e.ColumnIndex == 2)
                e.ToolTipText = item.Title;
            else if (e.ColumnIndex == 3)
                e.ToolTipText = FullDetails(item);
        };

        _thumbnailTimer.Tick += (_, _) =>
        {
            _thumbnailTimer.Stop();
            QueueVisibleThumbnails(immediate: true);
        };
    }

    private void BuildCategoryButtons()
    {
        _categoryBar.Controls.Clear();
        _categoryButtons.Clear();

        var categories = _items
            .Where(item => !string.IsNullOrWhiteSpace(item.Category))
            .GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Name: group.Key, Count: group.Count()))
            .OrderBy(group => CategoryOrder(group.Name))
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (categories.Length <= 1)
        {
            _categoryBar.Visible = false;
            _all.Text = "Selecionar todos";
            _none.Text = "Limpar seleção";
            return;
        }

        AddCategoryButton("Todos", _items.Count);
        foreach (var category in categories)
            AddCategoryButton(category.Name, category.Count);

        _categoryBar.Visible = true;
        UpdateCategoryButtonStyles();
    }

    private void AddCategoryButton(string category, int count)
    {
        var button = new Button
        {
            Text = $"{category}  {count:N0}",
            Tag = category,
            AutoSize = true,
            MinimumSize = new Size(108, 32),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White
        };
        button.Click += (_, _) =>
        {
            _activeCategory = category;
            ApplyCategoryFilter();
        };

        _categoryButtons[category] = button;
        _categoryBar.Controls.Add(button);
    }

    private static int CategoryOrder(string category)
        => category switch
        {
            "Vídeos" => 0,
            "Shorts" => 1,
            "Lives" => 2,
            "Reels" => 3,
            "Posts" => 4,
            _ => 10
        };

    private void ApplyCategoryFilter()
    {
        _thumbnailTimer.Stop();
        _viewGeneration++;

        // A grade é virtual: trocar Vídeos/Shorts altera somente a visão lógica,
        // sem percorrer e esconder milhares de DataGridViewRow na thread da UI.
        _visibleItems = _activeCategory.Equals("Todos", StringComparison.OrdinalIgnoreCase)
            ? _items
            : _items
                .Where(item => string.Equals(item.Category, _activeCategory, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        DisposeRowImages();
        _thumbnailLoaded.Clear();
        _thumbnailLoading.Clear();
        _thumbnailRetryAfter.Clear();

        _grid.BeginBatchUpdate();
        try
        {
            _grid.CurrentCell = null;
            _grid.RowCount = 0;
            _grid.RowCount = _visibleItems.Count;
        }
        finally
        {
            _grid.EndBatchUpdate();
        }

        UpdateCategoryButtonStyles();
        UpdateSummary();
        ScheduleVisibleThumbnailLoad();
    }

    private void UpdateCategoryButtonStyles()
    {
        foreach (var pair in _categoryButtons)
        {
            var active = pair.Key.Equals(_activeCategory, StringComparison.OrdinalIgnoreCase);
            pair.Value.BackColor = active ? Color.FromArgb(111, 71, 255) : Color.FromArgb(28, 28, 28);
            pair.Value.FlatAppearance.BorderColor = active ? Color.FromArgb(133, 101, 255) : Color.FromArgb(62, 62, 62);
        }

        if (_activeCategory.Equals("Todos", StringComparison.OrdinalIgnoreCase))
        {
            _all.Text = "Selecionar todos";
            _none.Text = "Limpar seleção";
        }
        else
        {
            _all.Text = $"Selecionar {_activeCategory}";
            _none.Text = $"Limpar {_activeCategory}";
        }
    }

    private void ToggleRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _visibleItems.Count)
            return;

        var item = _visibleItems[rowIndex];
        var key = SelectionKey(item);
        if (!_selectedKeys.Remove(key))
            _selectedKeys.Add(key);

        _grid.InvalidateRow(rowIndex);
        UpdateSummary();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void UpdateRowVisual(DataGridViewRow row, bool selected)
    {
        var normal = selected ? Color.FromArgb(51, 40, 90) : Color.FromArgb(20, 20, 20);
        var focused = selected ? Color.FromArgb(65, 50, 115) : Color.FromArgb(34, 34, 34);
        row.DefaultCellStyle.BackColor = normal;
        row.DefaultCellStyle.SelectionBackColor = focused;
        row.DefaultCellStyle.ForeColor = Color.FromArgb(235, 235, 235);
        row.DefaultCellStyle.SelectionForeColor = Color.White;
    }

    private void ScheduleVisibleThumbnailLoad()
    {
        if (_visualWorkPaused || !Visible || _items.Count == 0 || _imageCts is null) return;
        _thumbnailTimer.Stop();
        _thumbnailTimer.Start();
    }

    private void QueueVisibleThumbnails(bool immediate = false)
    {
        if (_visualWorkPaused || _imageCts is null || _imageCts.IsCancellationRequested || _grid.Rows.Count == 0 || !Visible)
            return;

        int first;
        try { first = _grid.FirstDisplayedScrollingRowIndex; }
        catch { first = -1; }
        if (first < 0) first = 0;

        var displayed = Math.Max(8, _grid.DisplayedRowCount(true));
        var start = Math.Max(0, first - 4);
        var ahead = immediate ? 36 : 24;
        var end = Math.Min(_grid.Rows.Count - 1, first + displayed + ahead);

        TrimRowImages(start, end);

        var ct = _imageCts.Token;
        for (var i = start; i <= end; i++)
        {
            if (i < 0 || i >= _visibleItems.Count)
                continue;
            _ = LoadThumbnailForRowAsync(i, ct);
        }

        var loaded = _thumbnailLoaded.Count;
        if (_visibleItems.Count > 0)
            _loading.Text = loaded >= _visibleItems.Count
                ? "Miniaturas prontas"
                : $"Miniaturas: {loaded:N0}/{_visibleItems.Count:N0} (carregamento sob demanda)";
    }

    private async Task LoadThumbnailForRowAsync(int rowIndex, CancellationToken ct)
    {
        if (_visualWorkPaused || rowIndex < 0 || rowIndex >= _visibleItems.Count) return;

        var generation = _viewGeneration;
        var item = _visibleItems[rowIndex];
        var loadingKey = HashCode.Combine(generation, rowIndex);

        if (_thumbnailLoaded.ContainsKey(rowIndex)) return;
        if (_thumbnailRetryAfter.TryGetValue(rowIndex, out var retryAt) && retryAt > DateTime.UtcNow) return;
        if (!_thumbnailLoading.TryAdd(loadingKey, 0)) return;

        try
        {
            var candidates = ThumbnailCandidates(item);
            if (candidates.Count == 0)
                return;

            await _thumbnailGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Image? image = null;
                foreach (var candidate in candidates)
                {
                    ct.ThrowIfCancellationRequested();
                    image = await DownloadAndResizeAsync(candidate, 112, 70, ct).ConfigureAwait(false);
                    if (image is not null)
                        break;
                }

                if (image is null || ct.IsCancellationRequested)
                {
                    _thumbnailRetryAfter[rowIndex] = DateTime.UtcNow.AddSeconds(45);
                    return;
                }

                if (IsDisposed)
                {
                    image.Dispose();
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed ||
                        ct.IsCancellationRequested ||
                        generation != _viewGeneration ||
                        rowIndex >= _visibleItems.Count ||
                        !string.Equals(
                            SelectionKey(_visibleItems[rowIndex]),
                            SelectionKey(item),
                            StringComparison.Ordinal))
                    {
                        image.Dispose();
                        return;
                    }

                    if (!IsRowNearViewport(rowIndex, 72))
                    {
                        image.Dispose();
                        return;
                    }

                    if (_rowImages.TryGetValue(rowIndex, out var oldImage))
                    {
                        _grid.Rows[rowIndex].Cells[1].Value = null;
                        try { oldImage.Dispose(); } catch { }
                    }

                    _rowImages[rowIndex] = image;
                    _grid.InvalidateCell(1, rowIndex);
                    _thumbnailRetryAfter.TryRemove(rowIndex, out _);
                    _thumbnailLoaded.TryAdd(rowIndex, 0);

                    var loaded = _thumbnailLoaded.Count;
                    if (loaded == _visibleItems.Count || loaded % 8 == 0)
                    {
                        _loading.Text = loaded >= _visibleItems.Count
                            ? "Miniaturas prontas"
                            : $"Miniaturas: {loaded:N0}/{_visibleItems.Count:N0} (priorizando o que está na tela)";
                    }
                }));
            }
            finally
            {
                _thumbnailGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            _thumbnailLoading.TryRemove(loadingKey, out _);
        }
    }

    private static IReadOnlyList<string> ThumbnailCandidates(CollectionMediaItem item)
    {
        var candidates = new List<string>();
        var isYouTube = item.Url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                        item.Url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

        // Primeiro tenta a URL fornecida pelo extrator: ela costuma ser a melhor
        // opção disponível para Shorts e vídeos antigos. Se o formato não for
        // suportado pelo System.Drawing, os JPGs oficiais entram como fallback.
        if (!string.IsNullOrWhiteSpace(item.ThumbnailUrl))
            candidates.Add(item.ThumbnailUrl);

        if (isYouTube && !string.IsNullOrWhiteSpace(item.Id))
        {
            foreach (var suffix in new[]
                     {
                         "hqdefault.jpg",
                         "mqdefault.jpg",
                         "sddefault.jpg",
                         "maxresdefault.jpg",
                         "0.jpg"
                     })
            {
                var url = $"https://i.ytimg.com/vi/{item.Id}/{suffix}";
                if (!candidates.Contains(url, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(url);
            }
        }

        return candidates;
    }

    private async Task LoadAvatarAsync(CancellationToken ct)
    {
        if (_profile is null || string.IsNullOrWhiteSpace(_profile.AvatarUrl)) return;
        var image = await DownloadAndResizeAsync(_profile.AvatarUrl, 82, 82, ct).ConfigureAwait(false);
        if (image is null || ct.IsCancellationRequested || IsDisposed) return;

        if (!IsHandleCreated)
        {
            image.Dispose();
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || ct.IsCancellationRequested)
                {
                    image.Dispose();
                    return;
                }
                _loadedImages.Add(image);
                _avatar.Image = image;
            }));
        }
        catch (InvalidOperationException)
        {
            image.Dispose();
        }
    }

    private static async Task<Image?> DownloadAndResizeAsync(string url, int width, int height, CancellationToken ct)
    {
        try
        {
            var renderedKey = $"{url}|{width}x{height}";
            if (RenderedMemoryCache.TryGetValue(renderedKey, out var renderedMemory))
                return BitmapFromBytes(renderedMemory);

            var renderedPath = RenderedCachePath(url, width, height);
            try
            {
                if (File.Exists(renderedPath))
                {
                    try { File.SetLastAccessTimeUtc(renderedPath, DateTime.UtcNow); } catch { }
                    var cached = await File.ReadAllBytesAsync(renderedPath, ct).ConfigureAwait(false);
                    var cachedImage = BitmapFromBytes(cached);
                    if (cachedImage is not null)
                    {
                        AddMemoryEntry(RenderedMemoryCache, renderedKey, cached);
                        return cachedImage;
                    }

                    try { File.Delete(renderedPath); } catch { }
                    RenderedMemoryCache.TryRemove(renderedKey, out _);
                }
            }
            catch { }

            var bytes = await GetImageBytesAsync(url, ct).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0) return null;

            using var ms = new MemoryStream(bytes, writable: false);
            using var source = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            var bitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(28, 28, 28));
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.SmoothingMode = SmoothingMode.HighSpeed;

                var scale = Math.Min(width / (double)source.Width, height / (double)source.Height);
                var drawW = Math.Max(1, (int)(source.Width * scale));
                var drawH = Math.Max(1, (int)(source.Height * scale));
                var x = (width - drawW) / 2;
                var y = (height - drawH) / 2;
                g.DrawImage(source, x, y, drawW, drawH);
            }

            try
            {
                using var output = new MemoryStream();
                bitmap.Save(output, ImageFormat.Png);
                var renderedBytes = output.ToArray();
                AddMemoryEntry(RenderedMemoryCache, renderedKey, renderedBytes);
                Directory.CreateDirectory(RenderedCacheDirectory);
                await File.WriteAllBytesAsync(renderedPath, renderedBytes, ct).ConfigureAwait(false);
                ScheduleCacheTrim();
            }
            catch { }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static Image? BitmapFromBytes(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var source = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            return new Bitmap(source);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]?> GetImageBytesAsync(string url, CancellationToken ct)
    {
        if (FailedImageUntil.TryGetValue(url, out var failedUntil))
        {
            if (failedUntil > DateTime.UtcNow)
                return null;
            FailedImageUntil.TryRemove(url, out _);
        }

        if (MemoryCache.TryGetValue(url, out var memory))
        {
            if (IsValidImageBytes(memory))
                return memory;
            MemoryCache.TryRemove(url, out _);
        }

        var cachePath = CachePath(url);
        try
        {
            if (File.Exists(cachePath))
            {
                try { File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow); } catch { }
                var cached = await File.ReadAllBytesAsync(cachePath, ct).ConfigureAwait(false);
                if (IsValidImageBytes(cached))
                {
                    AddMemoryEntry(MemoryCache, url, cached);
                    return cached;
                }

                try { File.Delete(cachePath); } catch { }
            }
        }
        catch { }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/154.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            if (url.Contains("cdninstagram.com", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("fbcdn.net", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Referrer = new Uri("https://www.instagram.com/");
            }
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                MarkImageFailure(
                    url,
                    TimeSpan.FromMinutes(
                        response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden ? 10 : 1));
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length == 0 || !IsValidImageBytes(bytes))
            {
                MarkImageFailure(url, TimeSpan.FromMinutes(2));
                return null;
            }

            FailedImageUntil.TryRemove(url, out _);

            AddMemoryEntry(MemoryCache, url, bytes);
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                await File.WriteAllBytesAsync(cachePath, bytes, ct).ConfigureAwait(false);
                ScheduleCacheTrim();
            }
            catch { }
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    private static void MarkImageFailure(string url, TimeSpan retryDelay)
    {
        FailedImageUntil[url] = DateTime.UtcNow.Add(retryDelay);
        if (FailedImageUntil.Count <= 1024)
            return;

        var now = DateTime.UtcNow;
        foreach (var pair in FailedImageUntil)
        {
            if (pair.Value <= now)
                FailedImageUntil.TryRemove(pair.Key, out _);
        }

        if (FailedImageUntil.Count <= 1024)
            return;

        foreach (var key in FailedImageUntil.Keys.Take(FailedImageUntil.Count - 768))
            FailedImageUntil.TryRemove(key, out _);
    }

    private static bool IsValidImageBytes(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var image = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: true);
            return image.Width > 0 && image.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void AddMemoryEntry(ConcurrentDictionary<string, byte[]> cache, string key, byte[] bytes)
    {
        cache[key] = bytes;
        if (cache.Count <= MaxMemoryCacheEntries)
            return;

        var removeCount = Math.Max(1, cache.Count - (MaxMemoryCacheEntries * 3 / 4));
        foreach (var oldKey in cache.Keys.Take(removeCount))
            cache.TryRemove(oldKey, out _);
    }

    private static void ScheduleCacheTrim()
    {
        if (Interlocked.Increment(ref _cacheWrites) % 64 == 0)
            _ = Task.Run(AppCache.TrimThumbnailCache);
    }

    private static string CachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(CacheDirectory, hash + ".img");
    }

    private static string RenderedCachePath(string url, int width, int height)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{url}|{width}x{height}")));
        return Path.Combine(RenderedCacheDirectory, hash + ".png");
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 14,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(8)
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    private void SetAll(bool value)
    {
        var targetItems = _activeCategory.Equals("Todos", StringComparison.OrdinalIgnoreCase)
            ? _items
            : _visibleItems;

        foreach (var item in targetItems)
        {
            var key = SelectionKey(item);
            if (value)
                _selectedKeys.Add(key);
            else
                _selectedKeys.Remove(key);
        }

        _grid.Invalidate();
        UpdateSummary();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSummary()
    {
        var selected = _selectedKeys.Count;
        var visible = _visibleItems.Count;
        var selectedVisible = _visibleItems.Count(item => _selectedKeys.Contains(SelectionKey(item)));

        _summary.Text = _activeCategory.Equals("Todos", StringComparison.OrdinalIgnoreCase)
            ? $"Encontrados: {_items.Count:N0}   •   Selecionados: {selected:N0}"
            : $"{_activeCategory}: {visible:N0}   •   Selecionados nesta categoria: {selectedVisible:N0}   •   Total selecionado: {selected:N0}";
    }

    private void CancelImageLoading()
    {
        _thumbnailTimer.Stop();
        try { _imageCts?.Cancel(); } catch { }
        _imageCts?.Dispose();
        _imageCts = null;
    }

    private void DisposeImages()
    {
        _avatar.Image = null;
        foreach (var image in _loadedImages)
        {
            try { image.Dispose(); } catch { }
        }
        _loadedImages.Clear();
        DisposeRowImages();
    }

    private void DisposeRowImages()
    {
        foreach (var image in _rowImages.Values)
        {
            try { image.Dispose(); } catch { }
        }
        _rowImages.Clear();

        if (_grid.RowCount > 0)
            _grid.InvalidateColumn(1);
    }

    private bool IsRowNearViewport(int rowIndex, int extraRows)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count)
            return false;

        int first;
        try { first = _grid.FirstDisplayedScrollingRowIndex; }
        catch { first = -1; }
        if (first < 0) first = 0;

        var displayed = Math.Max(8, _grid.DisplayedRowCount(true));
        var min = Math.Max(0, first - extraRows);
        var max = Math.Min(_grid.Rows.Count - 1, first + displayed + extraRows);
        return rowIndex >= min && rowIndex <= max;
    }

    private void TrimRowImages(int visibleStart, int visibleEnd)
    {
        if (_rowImages.Count <= 160)
            return;

        var keepStart = Math.Max(0, visibleStart - 64);
        var keepEnd = Math.Min(_grid.Rows.Count - 1, visibleEnd + 64);
        var toRemove = _rowImages.Keys
            .Where(index => index < keepStart || index > keepEnd)
            .Take(Math.Max(1, _rowImages.Count - 128))
            .ToArray();

        foreach (var index in toRemove)
        {
            if (!_rowImages.Remove(index, out var image))
                continue;

            if (index >= 0 && index < _grid.RowCount)
                _grid.InvalidateCell(1, index);

            _thumbnailLoaded.TryRemove(index, out _);
            try { image.Dispose(); } catch { }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelImageLoading();
            DisposeImages();
            _thumbnailTimer.Dispose();
            _thumbnailGate.Dispose();
        }
        base.Dispose(disposing);
    }

    private static string SelectionKey(CollectionMediaItem item)
        => $"{item.Index}|{item.Id}|{item.Url}";

    private static string CompactText(string text, int maxLength)
    {
        var clean = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (clean.Length <= maxLength) return clean;
        return clean[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static string CompactDetails(CollectionMediaItem item)
    {
        // Duração vem primeiro para nunca desaparecer quando a janela/painel
        // de diagnóstico deixa a lista mais estreita.
        var primary = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Duration)) primary.Add($"Duração {item.Duration}");
        if (!string.IsNullOrWhiteSpace(item.Date)) primary.Add(item.Date);
        if (!string.IsNullOrWhiteSpace(item.Category)) primary.Add(item.Category);

        var firstLine = string.Join("  •  ", primary);
        var metrics = string.IsNullOrWhiteSpace(item.Details)
            ? string.Empty
            : CompactText(item.Details, 150);

        if (string.IsNullOrWhiteSpace(firstLine)) return metrics;
        if (string.IsNullOrWhiteSpace(metrics)) return firstLine;
        return firstLine + Environment.NewLine + metrics;
    }

    private static string FullDetails(CollectionMediaItem item)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Duration)) details.Add($"Duração {item.Duration}");
        if (!string.IsNullOrWhiteSpace(item.Date)) details.Add(item.Date);
        if (!string.IsNullOrWhiteSpace(item.Category)) details.Add(item.Category);
        if (!string.IsNullOrWhiteSpace(item.Details)) details.Add(item.Details);
        return string.Join(Environment.NewLine, details);
    }

    private static string FormatCount(long value)
    {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000d:0.#} bi";
        if (value >= 1_000_000) return $"{value / 1_000_000d:0.#} mi";
        if (value >= 1_000) return $"{value / 1_000d:0.#} mil";
        return value.ToString("N0");
    }
}
