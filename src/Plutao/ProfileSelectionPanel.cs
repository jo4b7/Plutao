using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Plutao;

public sealed class ProfileSelectionPanel : UserControl
{
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Plutao",
        "thumb-cache");

    private static readonly ConcurrentDictionary<string, byte[]> MemoryCache = new(StringComparer.Ordinal);
    private static readonly HttpClient Http = CreateHttpClient();

    private IReadOnlyList<CollectionMediaItem> _items = Array.Empty<CollectionMediaItem>();
    private ProfileInfo? _profile;
    private CancellationTokenSource? _imageCts;
    private readonly List<Image> _loadedImages = new();
    private bool _suppressSelectionEvents;

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
        Height = 430;
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

        _items = items;
        _profile = profile;
        _suppressSelectionEvents = true;
        _grid.Rows.Clear();

        PopulateProfile();
        PopulateGrid();
        _suppressSelectionEvents = false;
        UpdateSummary();
        Visible = true;

        _imageCts = new CancellationTokenSource();
        _ = LoadImagesAsync(_imageCts.Token);
    }

    public void ClearData()
    {
        CancelImageLoading();
        DisposeImages();
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
        Height = compact ? 330 : roomy ? 520 : 430;
        _grid.RowTemplate.Height = compact ? 76 : 88;
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
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.RowTemplate.Height = 88;

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
                Width = 190,
                MinimumWidth = 150,
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
            var title = $"{item.Index:000}  —  {item.Title}";
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.Duration)) details.Add(item.Duration);
            if (!string.IsNullOrWhiteSpace(item.Date)) details.Add(item.Date);
            if (!string.IsNullOrWhiteSpace(item.Details)) details.Add(item.Details);

            var rowIndex = _grid.Rows.Add(true, null, title, string.Join(Environment.NewLine, details));
            _grid.Rows[rowIndex].Tag = item;
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
    }

    private async Task LoadImagesAsync(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);

            var avatarTask = LoadAvatarAsync(ct);

            var rows = _grid.Rows.Cast<DataGridViewRow>()
                .Where(row => row.Tag is CollectionMediaItem item && !string.IsNullOrWhiteSpace(item.ThumbnailUrl))
                .ToArray();

            // Prioriza o que o usuário vê primeiro. O restante continua em segundo plano.
            var first = rows.Take(Math.Min(24, rows.Length)).ToArray();
            var rest = rows.Skip(first.Length).ToArray();

            _loading.Text = rows.Length > 0 ? "Carregando miniaturas..." : string.Empty;
            await LoadRowsAsync(first, 16, ct);
            if (!ct.IsCancellationRequested)
                await LoadRowsAsync(rest, 18, ct);

            await avatarTask;
            if (!ct.IsCancellationRequested && !IsDisposed)
                _loading.Text = rows.Length > 0 ? "Miniaturas prontas" : string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Miniaturas são opcionais e não podem impedir a seleção/download.
        }
    }

    private async Task LoadRowsAsync(DataGridViewRow[] rows, int maxConcurrency, CancellationToken ct)
    {
        if (rows.Length == 0) return;
        using var gate = new SemaphoreSlim(maxConcurrency);

        var tasks = rows.Select(async row =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (row.Tag is not CollectionMediaItem item || string.IsNullOrWhiteSpace(item.ThumbnailUrl))
                    return;

                var image = await DownloadAndResizeAsync(item.ThumbnailUrl, 112, 74, ct).ConfigureAwait(false);
                if (image is null || ct.IsCancellationRequested) return;

                if (IsDisposed)
                {
                    image.Dispose();
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || ct.IsCancellationRequested)
                    {
                        image.Dispose();
                        return;
                    }
                    _loadedImages.Add(image);
                    row.Cells[1].Value = image;
                }));
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
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
            var bytes = await GetImageBytesAsync(url, ct).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0) return null;

            using var ms = new MemoryStream(bytes, writable: false);
            using var source = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            var bitmap = new Bitmap(width, height);
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Color.FromArgb(28, 28, 28));
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighSpeed;

            var scale = Math.Min(width / (double)source.Width, height / (double)source.Height);
            var drawW = Math.Max(1, (int)(source.Width * scale));
            var drawH = Math.Max(1, (int)(source.Height * scale));
            var x = (width - drawW) / 2;
            var y = (height - drawH) / 2;
            g.DrawImage(source, x, y, drawW, drawH);
            return bitmap;
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
            if (File.Exists(cachePath) && File.GetLastWriteTimeUtc(cachePath) > DateTime.UtcNow.AddDays(-7))
            {
                var cached = await File.ReadAllBytesAsync(cachePath, ct).ConfigureAwait(false);
                MemoryCache.TryAdd(url, cached);
                return cached;
            }
        }
        catch
        {
        }

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
            }
            catch
            {
            }
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    private static string CachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(CacheDirectory, hash + ".img");
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 20,
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
        foreach (DataGridViewRow row in _grid.Rows)
            row.Cells[0].Value = value;
        _suppressSelectionEvents = false;
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
        try { _imageCts?.Cancel(); } catch { }
        _imageCts?.Dispose();
        _imageCts = null;
    }

    private void DisposeImages()
    {
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
        }
        base.Dispose(disposing);
    }

    private static string FormatCount(long value)
    {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000d:0.#} bi";
        if (value >= 1_000_000) return $"{value / 1_000_000d:0.#} mi";
        if (value >= 1_000) return $"{value / 1_000d:0.#} mil";
        return value.ToString("N0");
    }
}
