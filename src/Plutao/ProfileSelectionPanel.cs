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
    private static readonly HttpClient Http = CreateHttpClient();
    private static int _cacheWrites;

    private IReadOnlyList<CollectionMediaItem> _items = Array.Empty<CollectionMediaItem>();
    private ProfileInfo? _profile;
    private CancellationTokenSource? _imageCts;
    private readonly List<Image> _loadedImages = new();
    private readonly ConcurrentDictionary<int, byte> _thumbnailLoaded = new();
    private readonly ConcurrentDictionary<int, byte> _thumbnailLoading = new();
    private readonly SemaphoreSlim _thumbnailGate = new(12, 12);
    private readonly System.Windows.Forms.Timer _thumbnailTimer = new() { Interval = 70 };
    private bool _suppressSelectionEvents;

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

    private readonly Button _all = new() { Text = "Marcar todos", AutoSize = true };
    private readonly Button _none = new() { Text = "Desmarcar todos", AutoSize = true };
    private readonly Button _openProfile = new() { Text = "Abrir perfil", AutoSize = true };
    private readonly Button _hide = new() { Text = "Ocultar resultados", AutoSize = true };

    public event EventHandler? SelectionChanged;
    public event EventHandler? HideRequested;

    public bool HasData => _items.Count > 0;
    public int TotalItems => _items.Count;

    public IReadOnlyList<CollectionMediaItem> SelectedItems
        => _grid.Rows.Cast<DataGridViewRow>()
            .Where(row => Convert.ToBoolean(row.Cells[0].Value ?? false))
            .Select(row => (CollectionMediaItem)row.Tag!)
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

        _items = items;
        _profile = profile;
        _suppressSelectionEvents = true;

        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            PopulateProfile();
            PopulateGrid();
        }
        finally
        {
            _grid.ResumeLayout(false);
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
        _items = Array.Empty<CollectionMediaItem>();
        _profile = null;
        _grid.Rows.Clear();
        _summary.Text = string.Empty;
        _loading.Text = string.Empty;
        _avatar.Image = null;
        Visible = false;
    }

    public void ApplyResponsiveHeight(bool compact, bool roomy)
    {
        var area = Screen.FromControl(this).WorkingArea;
        var targetHeight = compact
            ? Math.Clamp((int)(area.Height * 0.45), 330, 430)
            : roomy
                ? Math.Clamp((int)(area.Height * 0.58), 600, 820)
                : Math.Clamp((int)(area.Height * 0.52), 460, 650);

        Height = targetHeight;

        var rowHeight = compact ? 70 : roomy ? 84 : 78;
        _grid.RowTemplate.Height = rowHeight;
        if (_grid.Rows.Count > 0 && _grid.Rows[0].Height != rowHeight)
        {
            _grid.SuspendLayout();
            try
            {
                foreach (DataGridViewRow row in _grid.Rows)
                    row.Height = rowHeight;
            }
            finally
            {
                _grid.ResumeLayout(false);
            }
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
            RowCount = 3,
            BackColor = Color.FromArgb(18, 18, 18)
        };
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

        ConfigureGrid();
        root.Controls.Add(_grid, 0, 2);

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
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 40, 90);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.RowTemplate.Height = 78;

        _grid.Columns.AddRange(
            new DataGridViewCheckBoxColumn
            {
                HeaderText = "✓",
                Width = 38,
                MinimumWidth = 38,
                SortMode = DataGridViewColumnSortMode.NotSortable
            },
            new DataGridViewImageColumn
            {
                HeaderText = "Miniatura",
                Width = 118,
                MinimumWidth = 96,
                ImageLayout = DataGridViewImageCellLayout.Zoom,
                SortMode = DataGridViewColumnSortMode.NotSortable
            },
            new DataGridViewTextBoxColumn
            {
                HeaderText = "Vídeo",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 280,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            },
            new DataGridViewTextBoxColumn
            {
                HeaderText = "Informações",
                Width = 240,
                MinimumWidth = 170,
                ReadOnly = true,
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

        _profileName.Text = string.IsNullOrWhiteSpace(_profile.DisplayName)
            ? (!string.IsNullOrWhiteSpace(_profile.Username) ? _profile.Username : _profile.Platform)
            : _profile.DisplayName;
        _profileHandle.Text = string.IsNullOrWhiteSpace(_profile.Username)
            ? _profile.Platform
            : $"@{_profile.Username}  •  {_profile.Platform}";

        var stats = new List<string>();
        if (_profile.Followers.HasValue && _profile.Followers.Value > 0) stats.Add($"{FormatCount(_profile.Followers.Value)} seguidores");
        if (_profile.Following.HasValue && _profile.Following.Value > 0) stats.Add($"{FormatCount(_profile.Following.Value)} seguindo");
        if (_profile.Posts.HasValue && _profile.Posts.Value > 0) stats.Add($"{FormatCount(_profile.Posts.Value)} publicações");
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
        if (_items.Count == 0) return;

        _grid.Rows.Add(_items.Count);
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var row = _grid.Rows[i];
            row.Tag = item;
            row.Cells[0].Value = true;
            row.Cells[1].Value = null;
            row.Cells[2].Value = $"{item.Index:000}  —  {CompactText(item.Title, 240)}";
            row.Cells[3].Value = CompactDetails(item);
        }
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

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.ColumnIndex != 0 || _suppressSelectionEvents) return;
            UpdateSummary();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        _grid.Scroll += (_, _) => ScheduleVisibleThumbnailLoad();
        _grid.Resize += (_, _) => ScheduleVisibleThumbnailLoad();
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
            if (_grid.Rows[e.RowIndex].Tag is not CollectionMediaItem item) return;

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

    private void ScheduleVisibleThumbnailLoad()
    {
        if (!Visible || _items.Count == 0 || _imageCts is null) return;
        _thumbnailTimer.Stop();
        _thumbnailTimer.Start();
    }

    private void QueueVisibleThumbnails(bool immediate = false)
    {
        if (_imageCts is null || _imageCts.IsCancellationRequested || _grid.Rows.Count == 0 || !Visible)
            return;

        int first;
        try { first = _grid.FirstDisplayedScrollingRowIndex; }
        catch { first = -1; }
        if (first < 0) first = 0;

        var displayed = Math.Max(8, _grid.DisplayedRowCount(true));
        var start = Math.Max(0, first - 4);
        var ahead = immediate ? 36 : 24;
        var end = Math.Min(_grid.Rows.Count - 1, first + displayed + ahead);

        var ct = _imageCts.Token;
        for (var i = start; i <= end; i++)
            _ = LoadThumbnailForRowAsync(i, ct);

        var loaded = _thumbnailLoaded.Count;
        if (_items.Count > 0)
            _loading.Text = loaded >= _items.Count
                ? "Miniaturas prontas"
                : $"Miniaturas: {loaded:N0}/{_items.Count:N0} (carregamento sob demanda)";
    }

    private async Task LoadThumbnailForRowAsync(int rowIndex, CancellationToken ct)
    {
        if (rowIndex < 0 || rowIndex >= _items.Count) return;
        if (_thumbnailLoaded.ContainsKey(rowIndex)) return;
        if (!_thumbnailLoading.TryAdd(rowIndex, 0)) return;

        try
        {
            var item = _items[rowIndex];
            if (string.IsNullOrWhiteSpace(item.ThumbnailUrl))
            {
                _thumbnailLoaded.TryAdd(rowIndex, 0);
                return;
            }

            await _thumbnailGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var image = await DownloadAndResizeAsync(item.ThumbnailUrl, 112, 70, ct).ConfigureAwait(false);
                if (image is null || ct.IsCancellationRequested) return;

                if (IsDisposed)
                {
                    image.Dispose();
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || ct.IsCancellationRequested || rowIndex >= _grid.Rows.Count)
                    {
                        image.Dispose();
                        return;
                    }

                    _loadedImages.Add(image);
                    _grid.Rows[rowIndex].Cells[1].Value = image;
                    _thumbnailLoaded.TryAdd(rowIndex, 0);

                    var loaded = _thumbnailLoaded.Count;
                    _loading.Text = loaded >= _items.Count
                        ? "Miniaturas prontas"
                        : $"Miniaturas: {loaded:N0}/{_items.Count:N0} (priorizando o que está na tela)";
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
            _thumbnailLoading.TryRemove(rowIndex, out _);
        }
    }

    private async Task LoadAvatarAsync(CancellationToken ct)
    {
        if (_profile is null || string.IsNullOrWhiteSpace(_profile.AvatarUrl)) return;
        var image = await DownloadAndResizeAsync(_profile.AvatarUrl, 82, 82, ct).ConfigureAwait(false);
        if (image is null || ct.IsCancellationRequested || IsDisposed) return;

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
                    RenderedMemoryCache.TryAdd(renderedKey, cached);
                    return BitmapFromBytes(cached);
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
                RenderedMemoryCache.TryAdd(renderedKey, renderedBytes);
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
        if (MemoryCache.TryGetValue(url, out var memory))
            return memory;

        var cachePath = CachePath(url);
        try
        {
            if (File.Exists(cachePath))
            {
                try { File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow); } catch { }
                var cached = await File.ReadAllBytesAsync(cachePath, ct).ConfigureAwait(false);
                MemoryCache.TryAdd(url, cached);
                return cached;
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
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length == 0) return null;

            MemoryCache.TryAdd(url, bytes);
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
        _suppressSelectionEvents = true;
        _grid.SuspendLayout();
        try
        {
            foreach (DataGridViewRow row in _grid.Rows)
                row.Cells[0].Value = value;
        }
        finally
        {
            _grid.ResumeLayout(false);
            _suppressSelectionEvents = false;
        }
        UpdateSummary();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSummary()
    {
        var selected = _grid.Rows.Cast<DataGridViewRow>()
            .Count(row => Convert.ToBoolean(row.Cells[0].Value ?? false));
        _summary.Text = $"Encontrados: {_items.Count:N0}   •   Selecionados: {selected:N0}";
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

    private static string CompactText(string text, int maxLength)
    {
        var clean = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (clean.Length <= maxLength) return clean;
        return clean[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static string CompactDetails(CollectionMediaItem item)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Date)) details.Add(item.Date);
        if (!string.IsNullOrWhiteSpace(item.Duration)) details.Add(item.Duration);
        if (!string.IsNullOrWhiteSpace(item.Details)) details.Add(CompactText(item.Details, 120));
        return string.Join("  •  ", details);
    }

    private static string FullDetails(CollectionMediaItem item)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Date)) details.Add(item.Date);
        if (!string.IsNullOrWhiteSpace(item.Duration)) details.Add(item.Duration);
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
