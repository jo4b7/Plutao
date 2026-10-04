using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Plutao;

public sealed class YtDlpRunner
{
    private readonly ToolManager _tools;
    private Process? _process;

    private const string ProgressPrefix = "PLUTAO_PROGRESS|";
    private const string FilePrefix = "PLUTAO_FILE|";
    private static readonly Regex PercentRegex = new(@"(?<p>\d{1,3}(?:[\.,]\d+)?)%", RegexOptions.Compiled);
    private static readonly Regex AnsiRegex = new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);
    private static readonly string[] MediaExtensions =
    {
        ".mp4", ".mkv", ".webm", ".mp3", ".m4a", ".aac", ".flac", ".wav", ".opus"
    };

    private string? _currentCompletedFilePath;
    private readonly ConcurrentQueue<string> _completedFilePaths = new();
    private volatile bool _currentRunHadErrors;
    private volatile bool _currentRunSkippedByArchive;
    public string? LastCompletedFilePath { get; private set; }
    public ProfileInfo? LastAnalyzedProfile { get; private set; }

    private sealed class RunTiming
    {
        public Stopwatch Total { get; } = Stopwatch.StartNew();
        public object SyncRoot { get; } = new();
        public bool DownloadStarted { get; set; }
        public TimeSpan AnalysisDuration { get; set; }
        public TimeSpan? FormatSelectionStartedAt { get; set; }
        public TimeSpan LastProgressReportAt { get; set; }
        public int LastCollectionIndex { get; set; }
    }

    public YtDlpRunner(ToolManager tools) => _tools = tools;

    public void Stop() => TryStopProcess(_process);

    private static void TryStopProcess(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
                process.Kill(true);
        }
        catch { }
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunCapturedProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken ct)
    {
        using var process = new Process { StartInfo = startInfo };
        _process = process;

        try
        {
            process.Start();
            using var reg = ct.Register(() => TryStopProcess(process));
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return (process.ExitCode, stdout, stderr);
        }
        finally
        {
            if (ReferenceEquals(_process, process))
                _process = null;

            if (ct.IsCancellationRequested)
            {
                TryStopProcess(process);
                try
                {
                    if (!process.HasExited)
                        process.WaitForExit(1500);
                }
                catch { }
            }
        }
    }

    public async Task<MediaPreviewInfo?> AnalyzeMediaAsync(
        string url,
        string browserCookies,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        url = url.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (AppCache.TryGetPreview(url, TimeSpan.FromMinutes(30), out var cached) && cached is not null)
        {
            log.Report("[Cache] Informações do vídeo reaproveitadas.");
            progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", cached.Title, "Informações prontas"));
            return cached;
        }

        progress.Report(new DownloadProgressInfo(0, 0, 5, "", "", "", "Conectando à plataforma"));
        await _tools.EnsureYtDlpAsync(log, progress, ct);
        if (IsYouTubeUrl(url))
            await _tools.EnsureDenoAsync(log, progress, ct);

        var psi = new ProcessStartInfo
        {
            FileName = _tools.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var args = new List<string>
        {
            "--dump-single-json",
            "--skip-download",
            "--no-playlist",
            "--cache-dir", _tools.CacheDirectory
        };

        if (IsYouTubeUrl(url))
        {
            args.Add("--js-runtimes");
            args.Add($"deno:{_tools.DenoPath}");
        }

        if (!string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--cookies-from-browser");
            args.Add(browserCookies.ToLowerInvariant());
        }

        if (url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--user-agent");
            args.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36");
        }

        args.Add("--");
        args.Add(url);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        progress.Report(new DownloadProgressInfo(0, 0, 20, "", "", "", "Obtendo informações"));
        var timer = Stopwatch.StartNew();
        var run = await RunCapturedProcessAsync(psi, ct);
        var stdout = run.Stdout;
        var stderr = run.Stderr;
        timer.Stop();

        if (run.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            var message = stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                ? "Não foi possível obter as informações desse link."
                : AnsiRegex.Replace(message, string.Empty));
        }

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var title = ReadString(root, "title") ?? ReadString(root, "fulltitle") ?? "Mídia";
        var creator = ReadString(root, "channel")
                      ?? ReadString(root, "uploader")
                      ?? ReadString(root, "creator")
                      ?? string.Empty;
        var canonicalUrl = ReadString(root, "webpage_url") ?? url;
        var mediaId = ReadString(root, "id") ?? string.Empty;
        var thumbnailUrl = ReadThumbnailUrl(root);
        if (string.IsNullOrWhiteSpace(thumbnailUrl) && IsYouTubeUrl(url) && !string.IsNullOrWhiteSpace(mediaId))
            thumbnailUrl = $"https://i.ytimg.com/vi/{mediaId}/hqdefault.jpg";
        var availableHeights = ReadAvailableVideoHeights(root);
        var maximumResolution = ReadMaximumVideoResolution(root);
        var preview = new MediaPreviewInfo(
            canonicalUrl,
            PlatformName(url),
            mediaId,
            CleanDisplayTitle(title),
            creator,
            FormatDuration(ReadDouble(root, "duration")),
            ReadMediaDate(root),
            thumbnailUrl,
            ReadLong(root, "view_count"),
            ReadLong(root, "like_count"),
            maximumResolution.Width,
            maximumResolution.Height)
        {
            AvailableHeights = availableHeights
        };

        AppCache.StorePreview(url, preview);
        log.Report($"[Tempo] Prévia/análise: {FormatElapsed(timer.Elapsed)}");
        progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", preview.Title, "Informações prontas"));
        return preview;
    }

    public async Task<int> DownloadAsync(
        IReadOnlyList<string> urls,
        DownloadOptions options,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        LastCompletedFilePath = null;
        _tools.TemporaryDirectory = options.TemporaryDirectory;
        Directory.CreateDirectory(options.OutputDirectory);
        Directory.CreateDirectory(options.TemporaryDirectory);

        progress.Report(new DownloadProgressInfo(0, urls.Count, 0, "", "", "", "Preparando componentes"));
        log.Report("Preparando componentes...");
        var componentsTimer = Stopwatch.StartNew();
        await _tools.EnsureDownloadDependenciesAsync(urls, log, progress, ct);
        componentsTimer.Stop();
        log.Report($"[Tempo] Componentes: {FormatElapsed(componentsTimer.Elapsed)}");

        var hadErrors = false;
        for (var i = 0; i < urls.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var itemIndex = i + 1;
            log.Report($"\r\n=== Item {itemIndex}/{urls.Count} ===");
            progress.Report(new DownloadProgressInfo(itemIndex, urls.Count, 0, "", "", "", "Analisando link"));

            var exitCode = await RunOneAsync(urls[i], options, itemIndex, urls.Count, log, progress, ct);
            if (exitCode != 0)
            {
                hadErrors = true;
                log.Report($"Falha no item {itemIndex} (código {exitCode}).");
                ReportFriendlyHint(urls[i], options, log);
            }
        }

        return hadErrors ? 1 : 0;
    }

    public async Task<IReadOnlyList<CollectionMediaItem>> AnalyzeCollectionAsync(
        string url,
        string browserCookies,
        int limit,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        url = url.Trim();
        LastAnalyzedProfile = null;
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Informe o link da página/conta/canal/perfil.", nameof(url));

        if (AppCache.TryGetCollection(
                url,
                browserCookies,
                limit,
                TimeSpan.FromMinutes(15),
                out var cachedItems,
                out var cachedProfile))
        {
            LastAnalyzedProfile = cachedProfile;
            log.Report($"[Cache] Perfil reaproveitado: {cachedItems.Count} itens, sem nova análise da plataforma.");
            progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", $"{cachedItems.Count} mídias em cache"));
            return cachedItems;
        }

        if (TryGetYouTubeChannelTarget(url, out var youtubeChannelBase, out var requestedYouTubeCategory))
        {
            var youtubeItems = await AnalyzeYouTubeChannelAsync(
                youtubeChannelBase,
                requestedYouTubeCategory,
                browserCookies,
                limit,
                log,
                progress,
                ct);
            AppCache.StoreCollection(url, browserCookies, limit, youtubeItems, LastAnalyzedProfile);
            return youtubeItems;
        }

        // O extrator instagram:user do yt-dlp está quebrado nas versões atuais.
        // Para perfis do Instagram usamos gallery-dl para listar os posts/reels
        // e depois baixamos os itens escolhidos individualmente com yt-dlp.
        if (IsInstagramProfileUrl(url))
        {
            var instagramItems = await AnalyzeInstagramProfileAsync(url, browserCookies, limit, log, progress, ct);
            AppCache.StoreCollection(url, browserCookies, limit, instagramItems, LastAnalyzedProfile);
            return instagramItems;
        }

        // O extrator de perfis do TikTok no yt-dlp pode falhar ao obter o
        // secUid/"secondary user ID", mesmo quando os vídeos individuais
        // funcionam. Para perfis usamos gallery-dl para descobrir os posts e
        // mantemos yt-dlp para o download dos links individuais selecionados.
        if (IsTikTokProfileUrl(url))
        {
            var tiktokItems = await AnalyzeTikTokProfileAsync(url, browserCookies, limit, log, progress, ct);
            AppCache.StoreCollection(url, browserCookies, limit, tiktokItems, LastAnalyzedProfile);
            return tiktokItems;
        }

        progress.Report(new DownloadProgressInfo(0, 0, 0, "", "", "", "Preparando análise"));
        log.Report("[Análise] Preparando componentes do analisador...");
        await _tools.EnsureYtDlpAsync(log, progress, ct);
        if (IsYouTubeUrl(url))
            await _tools.EnsureDenoAsync(log, progress, ct);

        var psi = new ProcessStartInfo
        {
            FileName = _tools.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var args = new List<string>
        {
            "--flat-playlist",
            "--dump-single-json",
            "--skip-download",
            "--ignore-errors",
            "--yes-playlist",
            "--cache-dir", _tools.CacheDirectory
        };

        if (IsYouTubeUrl(url))
        {
            args.Add("--js-runtimes");
            args.Add($"deno:{_tools.DenoPath}");
        }

        if (limit > 0)
        {
            args.Add("--playlist-end");
            args.Add(limit.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--cookies-from-browser");
            args.Add(browserCookies.ToLowerInvariant());
        }

        if (url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--user-agent");
            args.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36");
        }

        args.Add("--");
        args.Add(url);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        progress.Report(new DownloadProgressInfo(0, 0, 15, "", "", "", "Analisando página/conta"));
        log.Report($"[Análise] Lendo itens de: {url}");

        var run = await RunCapturedProcessAsync(psi, ct);
        var stdout = run.Stdout;
        var stderr = run.Stderr;

        foreach (var line in stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            log.Report("[Análise] " + AnsiRegex.Replace(line, string.Empty));

        if (run.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException("Não foi possível listar os vídeos dessa página/conta. Confira o log e, se necessário, tente usar Cookies do navegador.");

        if (string.IsNullOrWhiteSpace(stdout))
            return Array.Empty<CollectionMediaItem>();

        var items = new List<CollectionMediaItem>();
        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;

            // --ignore-errors pode fazer o yt-dlp devolver literalmente `null`.
            // A versão anterior tentava tratar esse valor como objeto e exibia
            // a exceção interna "target element has type Null".
            if (root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                log.Report("[Análise] A plataforma não devolveu uma lista utilizável.");
                return Array.Empty<CollectionMediaItem>();
            }

            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("A plataforma respondeu em um formato que o Plutao ainda não reconhece.");

            if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
            {
                var sequentialIndex = 0;
                foreach (var entry in entries.EnumerateArray())
                {
                    sequentialIndex++;
                    if (entry.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                        continue;

                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        var text = entry.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            items.Add(new CollectionMediaItem(sequentialIndex, text, CleanDisplayTitle(text), ""));
                        continue;
                    }

                    if (entry.ValueKind != JsonValueKind.Object)
                        continue;

                    var index = ReadInt(entry, "playlist_index") ?? sequentialIndex;
                    var id = ReadString(entry, "id") ?? string.Empty;
                    var title = ReadString(entry, "title")
                                ?? ReadString(entry, "description")
                                ?? (!string.IsNullOrWhiteSpace(id) ? id : $"Mídia {index}");
                    var duration = ReadDouble(entry, "duration");
                    var thumbnail = ReadThumbnailUrl(entry);
                    var date = ReadMediaDate(entry);
                    var details = FormatMediaDetails(entry);
                    var itemUrl = ReadString(entry, "webpage_url") ?? ReadString(entry, "url") ?? string.Empty;
                    items.Add(new CollectionMediaItem(index, id, CleanDisplayTitle(title), FormatDuration(duration), itemUrl, thumbnail, date, details));
                }
            }
            else
            {
                var id = ReadString(root, "id") ?? string.Empty;
                var title = ReadString(root, "title") ?? (!string.IsNullOrWhiteSpace(id) ? id : "Mídia 1");
                items.Add(new CollectionMediaItem(
                    1,
                    id,
                    CleanDisplayTitle(title),
                    FormatDuration(ReadDouble(root, "duration")),
                    ReadString(root, "webpage_url") ?? ReadString(root, "url") ?? string.Empty,
                    ReadThumbnailUrl(root),
                    ReadMediaDate(root),
                    FormatMediaDetails(root)));
            }

            LastAnalyzedProfile = BuildProfileInfoFromYtDlp(root, url, items.Count);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("A plataforma respondeu, mas o Plutao não conseguiu interpretar a lista de vídeos.", ex);
        }

        progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", $"{items.Count} mídias encontrada(s)"));
        log.Report($"[Análise] Encontrados {items.Count} itens.");
        AppCache.StoreCollection(url, browserCookies, limit, items, LastAnalyzedProfile);
        return items;
    }

    private static bool TryGetYouTubeChannelTarget(string url, out string channelBaseUrl, out string requestedCategory)
    {
        channelBaseUrl = string.Empty;
        requestedCategory = string.Empty;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            return false;

        int baseCount;
        if (segments[0].StartsWith("@", StringComparison.Ordinal))
        {
            baseCount = 1;
        }
        else if (segments.Length >= 2 &&
                 (segments[0].Equals("channel", StringComparison.OrdinalIgnoreCase) ||
                  segments[0].Equals("c", StringComparison.OrdinalIgnoreCase) ||
                  segments[0].Equals("user", StringComparison.OrdinalIgnoreCase)))
        {
            baseCount = 2;
        }
        else
        {
            return false;
        }

        if (segments.Length > baseCount)
        {
            var tab = segments[baseCount].ToLowerInvariant();
            requestedCategory = tab switch
            {
                "videos" => "Vídeos",
                "shorts" => "Shorts",
                "streams" or "live" => "Lives",
                "featured" => string.Empty,
                _ => "__unsupported__"
            };

            if (requestedCategory == "__unsupported__")
                return false;
        }

        var basePath = string.Join("/", segments.Take(baseCount));
        channelBaseUrl = $"https://www.youtube.com/{basePath}";
        return true;
    }

    private async Task<IReadOnlyList<CollectionMediaItem>> AnalyzeYouTubeChannelAsync(
        string channelBaseUrl,
        string requestedCategory,
        string browserCookies,
        int limit,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        progress.Report(new DownloadProgressInfo(0, 0, 0, "", "", "", "Preparando canal do YouTube"));
        log.Report("[Análise/YouTube] Canal detectado. O Plutao vai abrir as categorias automaticamente.");

        await _tools.EnsureYtDlpAsync(log, progress, ct);
        await _tools.EnsureDenoAsync(log, progress, ct);

        (string Category, string Suffix)[] tabs;
        if (string.IsNullOrWhiteSpace(requestedCategory))
        {
            tabs = new (string Category, string Suffix)[]
            {
                ("Vídeos", "videos"),
                ("Shorts", "shorts"),
                ("Lives", "streams")
            };
        }
        else
        {
            tabs = requestedCategory switch
            {
                "Shorts" => new (string Category, string Suffix)[] { ("Shorts", "shorts") },
                "Lives" => new (string Category, string Suffix)[] { ("Lives", "streams") },
                _ => new (string Category, string Suffix)[] { ("Vídeos", "videos") }
            };
        }

        var combined = new List<CollectionMediaItem>();
        ProfileInfo? profile = null;

        for (var tabIndex = 0; tabIndex < tabs.Length; tabIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var tab = tabs[tabIndex];
            var tabUrl = $"{channelBaseUrl}/{tab.Suffix}";
            var stagePercent = 10 + (int)Math.Round(tabIndex * 70d / Math.Max(1, tabs.Length));
            progress.Report(new DownloadProgressInfo(0, 0, stagePercent, "", "", "", $"Lendo {tab.Category}"));
            log.Report($"[Análise/YouTube] {tab.Category}: {tabUrl}");

            var remaining = limit > 0 ? Math.Max(0, limit - combined.Count) : 0;
            if (limit > 0 && remaining == 0)
                break;

            var result = await AnalyzeYouTubeChannelTabAsync(
                tabUrl,
                tab.Category,
                browserCookies,
                remaining,
                log,
                ct);

            if (profile is null && result.Profile is not null)
                profile = result.Profile;

            combined.AddRange(result.Items);
            if (limit > 0 && combined.Count > limit)
                combined.RemoveRange(limit, combined.Count - limit);
            log.Report($"[Análise/YouTube] {tab.Category}: {result.Items.Count} itens.");
            if (limit > 0 && combined.Count >= limit)
                break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<CollectionMediaItem>();
        foreach (var item in combined)
        {
            var key = !string.IsNullOrWhiteSpace(item.Id)
                ? "id:" + item.Id
                : !string.IsNullOrWhiteSpace(item.Url)
                    ? "url:" + AppCache.NormalizeUrl(item.Url)
                    : string.Empty;

            if (string.IsNullOrWhiteSpace(key) || seen.Add(key))
                unique.Add(item);
        }

        var finalItems = unique
            .Select((item, index) => item with { Index = index + 1 })
            .ToArray();

        var enrichedItems = await EnrichYouTubeMetadataAsync(
            finalItems,
            browserCookies,
            log,
            progress,
            ct);
        LastAnalyzedProfile = profile is null
            ? null
            : profile with
            {
                ExternalUrl = channelBaseUrl,
                FoundVideos = enrichedItems.Length
            };

        progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", $"{enrichedItems.Length} mídias encontradas"));
        log.Report($"[Análise/YouTube] Total: {enrichedItems.Length} mídias após remover duplicados e completar metadados.");
        return enrichedItems;
    }

    private async Task<(IReadOnlyList<CollectionMediaItem> Items, ProfileInfo? Profile)> AnalyzeYouTubeChannelTabAsync(
        string tabUrl,
        string category,
        string browserCookies,
        int limit,
        IProgress<string> log,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _tools.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var args = new List<string>
        {
            "--flat-playlist",
            "--dump-single-json",
            "--skip-download",
            "--ignore-errors",
            "--yes-playlist",
            "--cache-dir", _tools.CacheDirectory,
            "--js-runtimes", $"deno:{_tools.DenoPath}"
        };

        if (limit > 0)
        {
            args.Add("--playlist-end");
            args.Add(limit.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--cookies-from-browser");
            args.Add(browserCookies.ToLowerInvariant());
        }

        args.Add("--");
        args.Add(tabUrl);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var run = await RunCapturedProcessAsync(psi, ct);
        var stdout = run.Stdout;
        var stderr = run.Stderr;

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            foreach (var line in stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = AnsiRegex.Replace(line, string.Empty);

                // "Não possui aba de Shorts/Lives" é um estado normal do canal,
                // não um erro da análise. O resumo da categoria já mostrará 0 itens.
                var missingOptionalTab =
                    clean.Contains("does not have a shorts tab", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("does not have a streams tab", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("does not have a live tab", StringComparison.OrdinalIgnoreCase);
                if (missingOptionalTab)
                    continue;

                if (!clean.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
                    log.Report($"[Análise/YouTube/{category}] {clean}");
            }
        }

        if (string.IsNullOrWhiteSpace(stdout))
            return (Array.Empty<CollectionMediaItem>(), null);

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (Array.Empty<CollectionMediaItem>(), null);

            var items = new List<CollectionMediaItem>();
            if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                        continue;

                    index++;
                    var id = ReadString(entry, "id") ?? string.Empty;
                    var title = ReadString(entry, "title")
                                ?? ReadString(entry, "description")
                                ?? (!string.IsNullOrWhiteSpace(id) ? id : $"Mídia {index}");
                    var rawUrl = ReadString(entry, "webpage_url") ?? ReadString(entry, "url") ?? string.Empty;
                    var itemUrl = NormalizeYouTubeMediaUrl(rawUrl, id);
                    var thumbnailUrl = ReadThumbnailUrl(entry);
                    if (string.IsNullOrWhiteSpace(thumbnailUrl) && !string.IsNullOrWhiteSpace(id))
                        thumbnailUrl = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg";

                    items.Add(new CollectionMediaItem(
                        index,
                        id,
                        CleanDisplayTitle(title),
                        FormatDuration(ReadDouble(entry, "duration")),
                        itemUrl,
                        thumbnailUrl,
                        ReadMediaDate(entry),
                        FormatMediaDetails(entry),
                        category));
                }
            }

            var profile = BuildProfileInfoFromYtDlp(root, tabUrl, items.Count);
            return (items, profile);
        }
        catch (JsonException)
        {
            return (Array.Empty<CollectionMediaItem>(), null);
        }
    }

    private async Task<CollectionMediaItem[]> EnrichYouTubeMetadataAsync(
        IReadOnlyList<CollectionMediaItem> items,
        string browserCookies,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return Array.Empty<CollectionMediaItem>();

        var results = items.ToArray();
        var pending = items
            .Select((item, index) => (Item: item, Index: index))
            .Where(x => string.IsNullOrWhiteSpace(x.Item.Duration) ||
                        string.IsNullOrWhiteSpace(x.Item.ThumbnailUrl))
            .ToArray();

        if (pending.Length == 0)
            return results;

        log.Report($"[Análise/YouTube] Completando duração/miniatura de {pending.Length} mídias...");
        progress.Report(new DownloadProgressInfo(0, 0, 82, "", "", "", "Completando metadados do YouTube"));

        var completed = 0;
        var maxParallel = string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase) ? 6 : 3;
        using var gate = new SemaphoreSlim(maxParallel, maxParallel);

        var tasks = pending.Select(async entry =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                results[entry.Index] = await AnalyzeYouTubeItemMetadataAsync(
                    entry.Item,
                    browserCookies,
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                results[entry.Index] = entry.Item;
            }
            finally
            {
                gate.Release();
                var done = Interlocked.Increment(ref completed);
                var percent = 82 + (int)Math.Round(16d * done / pending.Length);
                progress.Report(new DownloadProgressInfo(
                    0,
                    0,
                    Math.Clamp(percent, 82, 98),
                    "",
                    "",
                    entry.Item.Title,
                    $"Metadados do YouTube: {done}/{pending.Length}"));
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private async Task<CollectionMediaItem> AnalyzeYouTubeItemMetadataAsync(
        CollectionMediaItem item,
        string browserCookies,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Url))
            return item;

        var psi = new ProcessStartInfo
        {
            FileName = _tools.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in new[]
                 {
                     "--dump-single-json", "--skip-download", "--no-playlist", "--no-warnings",
                     "--cache-dir", _tools.CacheDirectory,
                     "--js-runtimes", $"deno:{_tools.DenoPath}"
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        if (!string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add("--cookies-from-browser");
            psi.ArgumentList.Add(browserCookies.ToLowerInvariant());
        }

        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(item.Url);

        using var process = new Process { StartInfo = psi };
        process.Start();
        using var reg = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch { }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        _ = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return item;

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return item;

            var duration = FormatDuration(ReadDouble(root, "duration"));
            if (string.IsNullOrWhiteSpace(duration))
                duration = item.Duration;

            var thumbnail = ReadThumbnailUrl(root);
            if (string.IsNullOrWhiteSpace(thumbnail))
                thumbnail = item.ThumbnailUrl;
            if (string.IsNullOrWhiteSpace(thumbnail) && !string.IsNullOrWhiteSpace(item.Id))
                thumbnail = $"https://i.ytimg.com/vi/{item.Id}/hqdefault.jpg";

            var date = ReadMediaDate(root);
            if (string.IsNullOrWhiteSpace(date))
                date = item.Date;

            var details = FormatMediaDetails(root);
            if (string.IsNullOrWhiteSpace(details))
                details = item.Details;

            return item with
            {
                Duration = duration,
                ThumbnailUrl = thumbnail,
                Date = date,
                Details = details
            };
        }
        catch (JsonException)
        {
            return item;
        }
    }

    private static string NormalizeYouTubeMediaUrl(string rawUrl, string id)
    {
        rawUrl = (rawUrl ?? string.Empty).Trim();
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) &&
            (uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)))
            return rawUrl;

        if (!string.IsNullOrWhiteSpace(id))
            return $"https://www.youtube.com/watch?v={id}";

        return rawUrl;
    }

    private async Task<IReadOnlyList<CollectionMediaItem>> AnalyzeInstagramProfileAsync(
        string url,
        string browserCookies,
        int limit,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        progress.Report(new DownloadProgressInfo(0, 0, 0, "", "", "", "Preparando análise do Instagram"));
        log.Report("[Análise] Perfil do Instagram detectado.");
        log.Report("[Análise] Usando analisador alternativo de perfis (gallery-dl)...");
        await _tools.EnsureGalleryDlAsync(log, progress, ct);

        var profileBase = NormalizeInstagramProfileUrl(url);

        progress.Report(new DownloadProgressInfo(0, 0, 8, "", "", "", "Lendo informações do perfil"));
        log.Report("[Análise/Instagram] Lendo informações gerais do perfil...");
        try
        {
            var profileJson = await RunGalleryDlAnalysisAsync(profileBase + "info/", browserCookies, 0, log, ct);
            LastAnalyzedProfile = ParseInstagramProfileInfo(profileJson, profileBase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Report("[Análise/Instagram] Informações gerais indisponíveis; continuando com os vídeos.");
        }

        var targets = new[]
        {
            (Label: "Reels", Url: profileBase + "reels/"),
            (Label: "Posts", Url: profileBase + "posts/")
        };

        var items = new List<CollectionMediaItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
        {
            ct.ThrowIfCancellationRequested();
            if (limit > 0 && items.Count >= limit)
                break;

            var target = targets[targetIndex];
            var stagePercent = targetIndex == 0 ? 20 : 60;
            progress.Report(new DownloadProgressInfo(0, 0, stagePercent, "", "", "", $"Listando Instagram: {target.Label}"));
            log.Report($"[Análise/Instagram] Listando {target.Label.ToLowerInvariant()}...");

            var remaining = limit > 0 ? Math.Max(1, limit - items.Count) : 0;
            var stdout = await RunGalleryDlAnalysisAsync(target.Url, browserCookies, remaining, log, ct);
            ExtractInstagramVideoItems(stdout, items, seen, limit, target.Label);
        }

        if (items.Count == 0)
        {
            if (string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "O Instagram não liberou a lista de vídeos desse perfil sem uma sessão. " +
                    "Selecione Cookies: Edge, Chrome ou Firefox (com o Instagram logado) e clique em TENTAR NOVAMENTE.");
            }

            throw new InvalidOperationException(
                "O Instagram não devolveu vídeos para esse perfil. Confira se a conta está acessível no navegador escolhido, " +
                "clique em Atualizar componentes e tente novamente.");
        }

        // Reindexa depois da deduplicação de posts/reels.
        var indexed = items
            .Take(limit > 0 ? limit : int.MaxValue)
            .Select((item, index) => item with { Index = index + 1 })
            .ToArray();

        // O gallery-dl é excelente para descobrir os posts/reels do perfil,
        // mas o extrator atual do Instagram não expõe duração nem contagem
        // de visualizações no JSON de listagem. Para não deixar a grade
        // incompleta, consultamos somente os metadados dos links encontrados
        // com o yt-dlp (sem baixar mídia), em paralelo e com limite de carga.
        var enriched = await EnrichInstagramMetadataAsync(
            indexed,
            browserCookies,
            log,
            progress,
            ct);

        LastAnalyzedProfile = (LastAnalyzedProfile ?? BuildInstagramFallbackProfile(profileBase)) with
        {
            FoundVideos = enriched.Length
        };

        progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", $"{enriched.Length} vídeos encontrado(s)"));
        log.Report($"[Análise/Instagram] Encontrados {enriched.Length} vídeos únicos.");
        return enriched;
    }

    private async Task<string> RunGalleryDlAnalysisAsync(
        string url,
        string browserCookies,
        int limit,
        IProgress<string> log,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _tools.GalleryDlPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var args = new List<string>
        {
            "--config-ignore",
            "--no-colors",
            "-j"
        };

        if (limit > 0)
        {
            args.Add("-o");
            args.Add($"max-posts={limit.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--cookies-from-browser");
            args.Add(browserCookies.ToLowerInvariant());
        }

        args.Add(url);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var run = await RunCapturedProcessAsync(psi, ct);
        var stdout = run.Stdout;
        var stderr = run.Stderr;

        foreach (var line in stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            log.Report("[Análise/Instagram] " + AnsiRegex.Replace(line, string.Empty));

        if (run.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            return string.Empty;

        return stdout;
    }

    private static void ExtractInstagramVideoItems(
        string stdout,
        List<CollectionMediaItem> items,
        HashSet<string> seen,
        int limit,
        string category)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return;

        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (var message in document.RootElement.EnumerateArray())
            {
                if (limit > 0 && items.Count >= limit)
                    return;
                if (message.ValueKind != JsonValueKind.Array || message.GetArrayLength() < 3)
                    continue;

                var parts = message.EnumerateArray().ToArray();
                if (parts[0].ValueKind != JsonValueKind.Number || !parts[0].TryGetInt32(out var messageType) || messageType != 3)
                    continue;

                if (parts[1].ValueKind != JsonValueKind.String)
                    continue;
                var emittedUrl = parts[1].GetString() ?? string.Empty;

                var metadata = parts[^1];
                if (metadata.ValueKind != JsonValueKind.Object)
                    continue;

                var videoUrl = ReadString(metadata, "video_url");
                if (string.IsNullOrWhiteSpace(videoUrl))
                    continue;

                // Cada vídeo também gera uma mensagem para a miniatura. Mantemos
                // somente a mensagem do arquivo de vídeo/manifesto.
                if (!emittedUrl.StartsWith("ytdl:", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(emittedUrl, videoUrl, StringComparison.Ordinal))
                    continue;

                var shortcode = ReadString(metadata, "post_shortcode")
                                ?? ReadString(metadata, "shortcode")
                                ?? ReadString(metadata, "media_id")
                                ?? string.Empty;
                var postType = ReadString(metadata, "type") ?? "reel";
                var postUrl = ReadString(metadata, "post_url");
                if (string.IsNullOrWhiteSpace(postUrl) && !string.IsNullOrWhiteSpace(shortcode))
                {
                    var route = postType.Equals("reel", StringComparison.OrdinalIgnoreCase) ? "reel" : "p";
                    postUrl = $"https://www.instagram.com/{route}/{shortcode}/";
                }

                if (string.IsNullOrWhiteSpace(postUrl) || !seen.Add(postUrl))
                    continue;

                var description = ReadString(metadata, "description");
                var title = !string.IsNullOrWhiteSpace(description)
                    ? CleanDisplayTitle(description)
                    : !string.IsNullOrWhiteSpace(shortcode) ? $"Instagram {shortcode}" : "Vídeo do Instagram";

                var thumbnail = ReadString(metadata, "display_url") ?? string.Empty;
                var date = ReadMediaDate(metadata);
                var duration = ReadDouble(metadata, "video_duration") ?? ReadDouble(metadata, "duration");
                var details = FormatInstagramMediaDetails(metadata);
                items.Add(new CollectionMediaItem(
                    items.Count + 1,
                    shortcode,
                    title,
                    FormatDuration(duration),
                    postUrl,
                    thumbnail,
                    date,
                    details,
                    category));
            }
        }
        catch (JsonException)
        {
            // O stderr já é enviado ao log; um resultado inválido simplesmente
            // deixa este alvo sem itens e permite tentar o próximo (posts/reels).
        }
    }

    private async Task<CollectionMediaItem[]> EnrichInstagramMetadataAsync(
        IReadOnlyList<CollectionMediaItem> items,
        string browserCookies,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return Array.Empty<CollectionMediaItem>();

        log.Report("[Análise/Instagram] Completando duração e visualizações dos vídeos...");
        progress.Report(new DownloadProgressInfo(0, 0, 72, "", "", "", "Completando metadados do Instagram"));
        await _tools.EnsureYtDlpAsync(log, progress, ct);

        var results = new CollectionMediaItem[items.Count];
        var completed = 0;
        var successful = 0;
        var unavailable = 0;

        // Quando há cookies do navegador, menos processos simultâneos evitam
        // disputar o banco de cookies. Sem cookies, podemos ser um pouco mais agressivos.
        var maxParallel = string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase) ? 4 : 2;
        using var gate = new SemaphoreSlim(maxParallel, maxParallel);

        var tasks = items.Select(async (item, index) =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var (enriched, success) = await AnalyzeInstagramItemMetadataAsync(
                    item,
                    browserCookies,
                    ct).ConfigureAwait(false);

                results[index] = EnsureInstagramMetadataPlaceholders(enriched);
                if (success)
                    Interlocked.Increment(ref successful);
                else
                    Interlocked.Increment(ref unavailable);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                results[index] = EnsureInstagramMetadataPlaceholders(item);
                Interlocked.Increment(ref unavailable);
            }
            finally
            {
                gate.Release();
                var done = Interlocked.Increment(ref completed);
                var percent = 72 + (int)Math.Round(24d * done / items.Count);
                progress.Report(new DownloadProgressInfo(
                    0,
                    0,
                    Math.Clamp(percent, 72, 96),
                    "",
                    "",
                    item.Title,
                    $"Metadados do Instagram: {done}/{items.Count}"));
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        log.Report($"[Análise/Instagram] Metadados: {successful}/{items.Count} vídeos completados; {unavailable} com algum dado indisponível.");
        return results;
    }

    private async Task<(CollectionMediaItem Item, bool Success)> AnalyzeInstagramItemMetadataAsync(
        CollectionMediaItem item,
        string browserCookies,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Url))
            return (item, false);

        var psi = new ProcessStartInfo
        {
            FileName = _tools.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var args = new List<string>
        {
            "--dump-single-json",
            "--skip-download",
            "--no-playlist",
            "--no-warnings",
            "--cache-dir", _tools.CacheDirectory
        };

        if (!string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--cookies-from-browser");
            args.Add(browserCookies.ToLowerInvariant());
        }

        args.Add("--user-agent");
        args.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36");
        args.Add("--");
        args.Add(item.Url);

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();
        using var reg = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch { }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        _ = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return (item, false);

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (item, false);

            var metadata = root;
            if (root.TryGetProperty("entries", out var entries) &&
                entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                        continue;

                    metadata = entry;
                    if (ReadDouble(entry, "duration").HasValue ||
                        ReadLong(entry, "view_count").HasValue ||
                        ReadLong(entry, "like_count").HasValue)
                        break;
                }
            }

            var duration = FormatDuration(ReadDouble(metadata, "duration") ?? ReadDouble(root, "duration"));
            if (string.IsNullOrWhiteSpace(duration))
                duration = item.Duration;

            var views = ReadLong(metadata, "view_count")
                        ?? ReadLong(metadata, "video_view_count")
                        ?? ReadLong(root, "view_count");
            var likes = ReadLong(metadata, "like_count")
                        ?? ReadLong(root, "like_count");
            var width = ReadInt(metadata, "width") ?? ReadInt(root, "width");
            var height = ReadInt(metadata, "height") ?? ReadInt(root, "height");

            if ((!width.HasValue || !height.HasValue) && metadata.ValueKind == JsonValueKind.Object)
            {
                var max = ReadMaximumVideoResolution(metadata);
                width ??= max.Width;
                height ??= max.Height;
            }

            var thumbnail = ReadThumbnailUrl(metadata);
            if (string.IsNullOrWhiteSpace(thumbnail))
                thumbnail = ReadThumbnailUrl(root);
            if (string.IsNullOrWhiteSpace(thumbnail))
                thumbnail = item.ThumbnailUrl;

            var date = ReadMediaDate(metadata);
            if (string.IsNullOrWhiteSpace(date))
                date = ReadMediaDate(root);
            if (string.IsNullOrWhiteSpace(date))
                date = item.Date;

            var details = MergeInstagramMetadataDetails(item.Details, views, likes, width, height);

            return (item with
            {
                Duration = duration,
                ThumbnailUrl = thumbnail,
                Date = date,
                Details = details
            }, true);
        }
        catch (JsonException)
        {
            return (item, false);
        }
    }

    private static CollectionMediaItem EnsureInstagramMetadataPlaceholders(CollectionMediaItem item)
    {
        var duration = string.IsNullOrWhiteSpace(item.Duration) ? "—" : item.Duration;
        var details = item.Details ?? string.Empty;
        if (!details.Contains("visualiza", StringComparison.OrdinalIgnoreCase))
            details = string.IsNullOrWhiteSpace(details)
                ? "— visualizações"
                : $"— visualizações • {details}";

        return item with
        {
            Duration = duration,
            Details = details
        };
    }

    private static string MergeInstagramMetadataDetails(
        string existing,
        long? views,
        long? likes,
        int? width,
        int? height)
    {
        var parts = (existing ?? string.Empty)
            .Split('•', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var existingLikes = parts.FirstOrDefault(part =>
            part.Contains("curtida", StringComparison.OrdinalIgnoreCase));
        var existingSize = parts.FirstOrDefault(part =>
            Regex.IsMatch(part, @"^\s*\d+\s*[×x]\s*\d+\s*$", RegexOptions.IgnoreCase));
        var other = parts.Where(part =>
                !part.Contains("visualiza", StringComparison.OrdinalIgnoreCase) &&
                !part.Contains("curtida", StringComparison.OrdinalIgnoreCase) &&
                !Regex.IsMatch(part, @"^\s*\d+\s*[×x]\s*\d+\s*$", RegexOptions.IgnoreCase))
            .ToList();

        var details = new List<string>
        {
            views.HasValue ? $"{views.Value:N0} visualizações" : "— visualizações"
        };

        if (likes.HasValue)
            details.Add($"{likes.Value:N0} curtidas");
        else if (!string.IsNullOrWhiteSpace(existingLikes))
            details.Add(existingLikes);

        if (width.HasValue && height.HasValue && width > 0 && height > 0)
            details.Add($"{width}×{height}");
        else if (!string.IsNullOrWhiteSpace(existingSize))
            details.Add(existingSize);

        details.AddRange(other);
        return string.Join(" • ", details);
    }

    private async Task<IReadOnlyList<CollectionMediaItem>> AnalyzeTikTokProfileAsync(
        string url,
        string browserCookies,
        int limit,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        progress.Report(new DownloadProgressInfo(0, 0, 0, "", "", "", "Preparando análise do TikTok"));
        log.Report("[Análise] Perfil do TikTok detectado.");
        log.Report("[Análise/TikTok] Usando o analisador alternativo de perfis para evitar a falha de secondary user ID do yt-dlp...");
        await _tools.EnsureGalleryDlAsync(log, progress, ct);

        var profileBase = NormalizeTikTokProfileUrl(url);
        progress.Report(new DownloadProgressInfo(0, 0, 15, "", "", "", "Listando vídeos do TikTok"));

        var stdout = await RunTikTokGalleryDlAnalysisAsync(
            profileBase,
            browserCookies,
            limit,
            log,
            ct);

        var items = ExtractTikTokVideoItems(stdout, profileBase, limit, out var profile);
        if (items.Count == 0)
        {
            if (string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "O TikTok não liberou a lista de vídeos desse perfil nesta sessão. " +
                    "O Plutao já tentou o analisador alternativo; selecione Cookies: Edge, Chrome ou Firefox com o TikTok aberto/logado e tente novamente.");
            }

            throw new InvalidOperationException(
                "O TikTok não devolveu vídeos para esse perfil. Confira se o perfil abre normalmente no navegador selecionado, " +
                "clique em Atualizar componentes e clique em TENTAR NOVAMENTE.");
        }

        LastAnalyzedProfile = (profile ?? BuildTikTokFallbackProfile(profileBase)) with
        {
            FoundVideos = items.Count
        };

        progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", $"{items.Count} vídeos encontrado(s)"));
        log.Report($"[Análise/TikTok] Encontrados {items.Count} vídeos únicos.");
        return items;
    }

    private async Task<string> RunTikTokGalleryDlAnalysisAsync(
        string url,
        string browserCookies,
        int limit,
        IProgress<string> log,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _tools.GalleryDlPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var args = new List<string>
        {
            "--config-ignore",
            "--no-colors",
            "-j",
            "-o", "extractor.tiktok.user.include=posts",
            "-o", "extractor.tiktok.videos=ytdl",
            "-o", "extractor.tiktok.photos=false",
            "-o", "extractor.tiktok.audio=false",
            "-o", "extractor.tiktok.posts.order-posts=desc"
        };

        if (limit > 0)
        {
            args.Add("-o");
            args.Add($"extractor.tiktok.tiktok-range=1-{limit.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--cookies-from-browser");
            args.Add(browserCookies.ToLowerInvariant());
        }

        args.Add(url);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var run = await RunCapturedProcessAsync(psi, ct);
        var stdout = run.Stdout;
        var stderr = run.Stderr;

        foreach (var line in stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            log.Report("[Análise/TikTok] " + AnsiRegex.Replace(line, string.Empty));

        if (run.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            return string.Empty;

        return stdout;
    }

    private static IReadOnlyList<CollectionMediaItem> ExtractTikTokVideoItems(
        string stdout,
        string profileUrl,
        int limit,
        out ProfileInfo? profile)
    {
        profile = null;
        var items = new List<CollectionMediaItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(stdout))
            return items;

        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return items;

            foreach (var message in document.RootElement.EnumerateArray())
            {
                if (limit > 0 && items.Count >= limit)
                    break;
                if (message.ValueKind != JsonValueKind.Array || message.GetArrayLength() < 3)
                    continue;

                var parts = message.EnumerateArray().ToArray();
                if (parts[0].ValueKind != JsonValueKind.Number ||
                    !parts[0].TryGetInt32(out var messageType) ||
                    messageType != 3)
                    continue;

                var metadata = parts[^1];
                if (metadata.ValueKind != JsonValueKind.Object)
                    continue;

                var postType = ReadString(metadata, "post_type");
                if (string.Equals(postType, "image", StringComparison.OrdinalIgnoreCase))
                    continue;

                var id = ReadString(metadata, "id")
                         ?? ReadString(metadata, "media_id")
                         ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                    continue;

                var username = ReadString(metadata, "user")
                               ?? ReadNestedString(metadata, "author", "uniqueId")
                               ?? ExtractTikTokUsername(profileUrl);
                var postUrl = !string.IsNullOrWhiteSpace(username)
                    ? $"https://www.tiktok.com/@{username}/video/{id}"
                    : $"https://www.tiktok.com/@/video/{id}";

                if (parts[1].ValueKind == JsonValueKind.String)
                {
                    var emittedUrl = parts[1].GetString() ?? string.Empty;
                    if (emittedUrl.StartsWith("ytdl:", StringComparison.OrdinalIgnoreCase))
                    {
                        var candidate = emittedUrl[5..];
                        if (Uri.TryCreate(candidate, UriKind.Absolute, out _))
                            postUrl = candidate;
                    }
                }

                var description = ReadString(metadata, "desc")
                                  ?? ReadString(metadata, "title")
                                  ?? string.Empty;
                var title = !string.IsNullOrWhiteSpace(description)
                    ? CleanDisplayTitle(description)
                    : $"TikTok {id}";

                var duration = ReadNestedDouble(metadata, "video", "duration")
                               ?? ReadDouble(metadata, "duration");
                var thumbnail = ReadNestedString(metadata, "video", "originCover")
                                ?? ReadNestedString(metadata, "video", "cover")
                                ?? ReadNestedString(metadata, "video", "dynamicCover")
                                ?? ReadString(metadata, "thumbnail")
                                ?? string.Empty;
                var date = ReadTikTokMediaDate(metadata);
                var details = FormatTikTokMediaDetails(metadata);

                items.Add(new CollectionMediaItem(
                    items.Count + 1,
                    id,
                    title,
                    FormatDuration(duration),
                    postUrl,
                    thumbnail,
                    date,
                    details,
                    "Vídeos"));

                profile ??= BuildTikTokProfileFromMetadata(metadata, profileUrl);
            }
        }
        catch (JsonException)
        {
            return items;
        }

        return items;
    }

    private static string FormatTikTokMediaDetails(JsonElement metadata)
    {
        var details = new List<string>();
        var views = ReadNestedLong(metadata, "stats", "playCount")
                    ?? ReadLong(metadata, "play_count")
                    ?? ReadLong(metadata, "view_count");
        var likes = ReadNestedLong(metadata, "stats", "diggCount")
                    ?? ReadLong(metadata, "like_count");
        var comments = ReadNestedLong(metadata, "stats", "commentCount")
                       ?? ReadLong(metadata, "comment_count");
        var width = ReadNestedInt(metadata, "video", "width")
                    ?? ReadInt(metadata, "width");
        var height = ReadNestedInt(metadata, "video", "height")
                     ?? ReadInt(metadata, "height");

        if (views.HasValue) details.Add($"{views.Value:N0} visualizações");
        if (likes.HasValue) details.Add($"{likes.Value:N0} curtidas");
        if (comments.HasValue) details.Add($"{comments.Value:N0} comentários");
        if (width.HasValue && height.HasValue && width > 0 && height > 0)
            details.Add($"{width}×{height}");
        return string.Join(" • ", details);
    }

    private static string ReadTikTokMediaDate(JsonElement metadata)
    {
        var date = ReadMediaDate(metadata);
        if (!string.IsNullOrWhiteSpace(date))
            return date;

        var unix = ReadLong(metadata, "createTime");
        if (unix is > 1_000_000_000)
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(unix.Value).ToLocalTime().ToString("dd/MM/yyyy"); }
            catch { }
        }

        return string.Empty;
    }

    private static ProfileInfo? BuildTikTokProfileFromMetadata(JsonElement metadata, string profileUrl)
    {
        if (metadata.ValueKind != JsonValueKind.Object ||
            !metadata.TryGetProperty("author", out var author) ||
            author.ValueKind != JsonValueKind.Object)
            return null;

        var username = ReadString(author, "uniqueId")
                       ?? ReadString(metadata, "user")
                       ?? ExtractTikTokUsername(profileUrl);
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var displayName = ReadString(author, "nickname") ?? username;
        var biography = ReadString(author, "signature") ?? string.Empty;
        var avatar = ReadString(author, "avatarLarger")
                     ?? ReadString(author, "avatarMedium")
                     ?? ReadString(author, "avatarThumb")
                     ?? string.Empty;

        JsonElement authorStats = default;
        var hasStats = metadata.TryGetProperty("authorStats", out authorStats) &&
                       authorStats.ValueKind == JsonValueKind.Object;
        var followers = hasStats ? ReadLong(authorStats, "followerCount") : null;
        var following = hasStats ? ReadLong(authorStats, "followingCount") : null;
        var posts = hasStats ? ReadLong(authorStats, "videoCount") : null;

        return new ProfileInfo(
            "TikTok",
            username,
            displayName,
            biography,
            avatar,
            followers,
            following,
            posts,
            ReadBool(author, "verified"),
            ReadBool(author, "privateAccount"),
            profileUrl);
    }

    private static ProfileInfo BuildTikTokFallbackProfile(string profileUrl)
    {
        var username = ExtractTikTokUsername(profileUrl);
        return new ProfileInfo(
            "TikTok",
            username,
            username,
            string.Empty,
            string.Empty,
            ExternalUrl: profileUrl);
    }

    private static bool IsTikTokProfileUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (!uri.Host.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 && parts[0].StartsWith("@", StringComparison.Ordinal) && parts[0].Length > 1;
    }

    private static string NormalizeTikTokProfileUrl(string url)
    {
        var username = ExtractTikTokUsername(url);
        return string.IsNullOrWhiteSpace(username)
            ? url.TrimEnd('/')
            : $"https://www.tiktok.com/@{username}";
    }

    private static string ExtractTikTokUsername(string profileUrl)
    {
        if (!Uri.TryCreate(profileUrl, UriKind.Absolute, out var uri))
            return string.Empty;
        var segment = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return segment.StartsWith("@", StringComparison.Ordinal) ? segment[1..] : segment;
    }

    private static bool IsInstagramProfileUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (!uri.Host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("www.instagram.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1)
            return false;

        return !parts[0].Equals("p", StringComparison.OrdinalIgnoreCase) &&
               !parts[0].Equals("reel", StringComparison.OrdinalIgnoreCase) &&
               !parts[0].Equals("reels", StringComparison.OrdinalIgnoreCase) &&
               !parts[0].Equals("stories", StringComparison.OrdinalIgnoreCase) &&
               !parts[0].Equals("explore", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInstagramProfileUrl(string url)
    {
        var uri = new Uri(url);
        var username = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
        return $"https://www.instagram.com/{username}/";
    }

    private static int[] ReadAvailableVideoHeights(JsonElement root)
    {
        var heights = new HashSet<int>();

        var rootHeight = ReadInt(root, "height");
        if (rootHeight is > 0)
            heights.Add(rootHeight.Value);

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("formats", out var formats) &&
            formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var format in formats.EnumerateArray())
            {
                if (format.ValueKind != JsonValueKind.Object)
                    continue;

                var vcodec = ReadString(format, "vcodec");
                if (string.IsNullOrWhiteSpace(vcodec) ||
                    string.Equals(vcodec, "none", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(vcodec, "images", StringComparison.OrdinalIgnoreCase))
                    continue;

                var height = ReadInt(format, "height");
                if (height is > 0)
                    heights.Add(height.Value);
            }
        }

        return heights.OrderByDescending(x => x).ToArray();
    }

    private static (int? Width, int? Height) ReadMaximumVideoResolution(JsonElement root)
    {
        int? bestWidth = ReadInt(root, "width");
        int? bestHeight = ReadInt(root, "height");

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("formats", out var formats) ||
            formats.ValueKind != JsonValueKind.Array)
            return (bestWidth, bestHeight);

        foreach (var format in formats.EnumerateArray())
        {
            if (format.ValueKind != JsonValueKind.Object)
                continue;

            var vcodec = ReadString(format, "vcodec");
            if (string.IsNullOrWhiteSpace(vcodec) ||
                string.Equals(vcodec, "none", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(vcodec, "images", StringComparison.OrdinalIgnoreCase))
                continue;

            var width = ReadInt(format, "width");
            var height = ReadInt(format, "height");
            if (height is not > 0)
                continue;

            if (bestHeight is null ||
                height.Value > bestHeight.Value ||
                (height.Value == bestHeight.Value && (width ?? 0) > (bestWidth ?? 0)))
            {
                bestHeight = height;
                bestWidth = width;
            }
        }

        return (bestWidth, bestHeight);
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        return null;
    }

    private static double? ReadDouble(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    private static ProfileInfo? ParseInstagramProfileInfo(string stdout, string profileUrl)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return BuildInstagramFallbackProfile(profileUrl);

        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return BuildInstagramFallbackProfile(profileUrl);

            foreach (var message in document.RootElement.EnumerateArray())
            {
                if (message.ValueKind != JsonValueKind.Array)
                    continue;

                JsonElement? metadata = null;
                foreach (var part in message.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.Object)
                        metadata = part;
                }

                if (!metadata.HasValue)
                    continue;

                var user = metadata.Value;
                var username = ReadString(user, "username") ?? ExtractInstagramUsername(profileUrl);
                if (string.IsNullOrWhiteSpace(username))
                    continue;

                var displayName = ReadString(user, "full_name")
                                  ?? ReadString(user, "fullname")
                                  ?? username;
                var biography = ReadString(user, "biography") ?? string.Empty;
                var avatar = ReadString(user, "profile_pic_url_hd")
                             ?? ReadNestedString(user, "hd_profile_pic_url_info", "url")
                             ?? ReadLastArrayObjectString(user, "hd_profile_pic_versions", "url")
                             ?? ReadString(user, "profile_pic_url")
                             ?? string.Empty;

                var followers = ReadLong(user, "follower_count")
                                ?? ReadLong(user, "count_followed")
                                ?? ReadNestedLong(user, "edge_followed_by", "count");
                var following = ReadLong(user, "following_count")
                                ?? ReadLong(user, "count_follow")
                                ?? ReadNestedLong(user, "edge_follow", "count");
                var posts = ReadLong(user, "media_count")
                            ?? ReadLong(user, "count_media")
                            ?? ReadNestedLong(user, "edge_owner_to_timeline_media", "count");

                return new ProfileInfo(
                    "Instagram",
                    username,
                    displayName,
                    biography,
                    avatar,
                    followers,
                    following,
                    posts,
                    ReadBool(user, "is_verified"),
                    ReadBool(user, "is_private"),
                    profileUrl,
                    ReadProfileCreatedDate(user));
            }
        }
        catch (JsonException)
        {
        }

        return BuildInstagramFallbackProfile(profileUrl);
    }

    private static ProfileInfo BuildInstagramFallbackProfile(string profileUrl)
    {
        var username = ExtractInstagramUsername(profileUrl);
        return new ProfileInfo(
            "Instagram",
            username,
            username,
            string.Empty,
            string.Empty,
            ExternalUrl: profileUrl);
    }

    private static string ExtractInstagramUsername(string profileUrl)
    {
        if (!Uri.TryCreate(profileUrl, UriKind.Absolute, out var uri))
            return string.Empty;
        return uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    }

    private static ProfileInfo? BuildProfileInfoFromYtDlp(JsonElement root, string url, int foundVideos)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var platform = PlatformName(url);
        var username = ReadString(root, "uploader_id")
                       ?? ReadString(root, "channel_id")
                       ?? ReadString(root, "uploader")
                       ?? ReadString(root, "channel")
                       ?? ReadString(root, "playlist_uploader")
                       ?? string.Empty;
        var displayName = ReadString(root, "channel")
                          ?? ReadString(root, "uploader")
                          ?? ReadString(root, "playlist_title")
                          ?? ReadString(root, "title")
                          ?? username;
        var biography = ReadString(root, "description") ?? string.Empty;
        var avatar = ReadThumbnailUrl(root);
        var followers = ReadLong(root, "channel_follower_count") ?? ReadLong(root, "follower_count");
        var posts = ReadLong(root, "playlist_count");

        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(avatar))
            return null;

        return new ProfileInfo(
            platform,
            username,
            displayName,
            biography,
            avatar,
            followers,
            null,
            posts,
            null,
            null,
            url,
            ReadProfileCreatedDate(root))
        {
            FoundVideos = foundVideos
        };
    }

    private static string PlatformName(string url)
    {
        if (url.Contains("instagram.com", StringComparison.OrdinalIgnoreCase)) return "Instagram";
        if (url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase)) return "TikTok";
        if (url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)) return "YouTube";
        return "Perfil/Página";
    }

    private static string ReadThumbnailUrl(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("thumbnails", out var thumbnails) &&
            thumbnails.ValueKind == JsonValueKind.Array)
        {
            const int targetWidth = 480;
            string bestUrl = string.Empty;
            var bestScore = int.MaxValue;

            foreach (var thumb in thumbnails.EnumerateArray())
            {
                if (thumb.ValueKind != JsonValueKind.Object)
                    continue;

                var value = ReadString(thumb, "url");
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var width = ReadInt(thumb, "width");
                var score = width.HasValue && width.Value > 0
                    ? Math.Abs(width.Value - targetWidth)
                    : 100_000;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestUrl = value;
                }
            }

            if (!string.IsNullOrWhiteSpace(bestUrl))
                return bestUrl;
        }

        return ReadString(element, "thumbnail") ?? string.Empty;
    }

    private static string ReadProfileCreatedDate(JsonElement element)
    {
        // Só usa campos que explicitamente indicam criação/entrada da conta.
        // Não usa upload_date/timestamp genérico para não confundir a data de um vídeo
        // com a data em que o perfil foi criado.
        foreach (var key in new[]
                 {
                     "channel_created_at", "account_created_at", "profile_created_at",
                     "channel_creation_date", "account_creation_date", "profile_creation_date",
                     "joined_date", "join_date", "joined"
                 })
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unix) && unix > 1_000_000_000)
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd/MM/yyyy"); }
                catch { }
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (text.Length == 8 && DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var compact))
                    return compact.ToString("dd/MM/yyyy");
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
                    return parsed.ToString("dd/MM/yyyy");
            }
        }

        return string.Empty;
    }

    private static string ReadMediaDate(JsonElement element)
    {
        foreach (var key in new[] { "upload_date", "release_date", "post_date", "date", "timestamp" })
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unix) && unix > 1_000_000_000)
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd/MM/yyyy"); }
                catch { }
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (text.Length == 8 && DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var compact))
                    return compact.ToString("dd/MM/yyyy");
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
                    return parsed.ToString("dd/MM/yyyy");
            }
        }

        return string.Empty;
    }

    private static string FormatMediaDetails(JsonElement element)
    {
        var details = new List<string>();
        var views = ReadLong(element, "view_count");
        var likes = ReadLong(element, "like_count");
        var comments = ReadLong(element, "comment_count");
        var width = ReadInt(element, "width");
        var height = ReadInt(element, "height");
        if (views.HasValue) details.Add($"{views.Value:N0} visualizações");
        if (likes.HasValue) details.Add($"{likes.Value:N0} curtidas");
        if (comments.HasValue) details.Add($"{comments.Value:N0} comentários");
        if (width.HasValue && height.HasValue) details.Add($"{width}×{height}");
        return string.Join(" • ", details);
    }

    private static string FormatInstagramMediaDetails(JsonElement metadata)
    {
        var details = new List<string>();
        var views = ReadLong(metadata, "view_count")
                    ?? ReadLong(metadata, "video_view_count")
                    ?? ReadLong(metadata, "play_count")
                    ?? ReadLong(metadata, "views");
        var likes = ReadLong(metadata, "likes") ?? ReadLong(metadata, "like_count");
        var comments = ReadLong(metadata, "comments") ?? ReadLong(metadata, "comment_count");
        var width = ReadInt(metadata, "width_original") ?? ReadInt(metadata, "width");
        var height = ReadInt(metadata, "height_original") ?? ReadInt(metadata, "height");
        if (views.HasValue) details.Add($"{views.Value:N0} visualizações");
        if (likes.HasValue) details.Add($"{likes.Value:N0} curtidas");
        if (comments.HasValue) details.Add($"{comments.Value:N0} comentários");
        if (width.HasValue && height.HasValue && width > 0 && height > 0) details.Add($"{width}×{height}");
        return string.Join(" • ", details);
    }

    private static long? ReadLong(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    private static bool? ReadBool(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result)) return result;
        return null;
    }

    private static long? ReadNestedLong(JsonElement element, string parent, string child)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(parent, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return null;
        return ReadLong(nested, child);
    }

    private static int? ReadNestedInt(JsonElement element, string parent, string child)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(parent, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return null;
        return ReadInt(nested, child);
    }

    private static double? ReadNestedDouble(JsonElement element, string parent, string child)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(parent, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return null;
        return ReadDouble(nested, child);
    }

    private static string? ReadNestedString(JsonElement element, string parent, string child)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(parent, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return null;
        return ReadString(nested, child);
    }

    private static string? ReadLastArrayObjectString(JsonElement element, string property, string child)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return null;
        string? result = null;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var value = ReadString(item, child);
            if (!string.IsNullOrWhiteSpace(value)) result = value;
        }
        return result;
    }

    private static string CleanDisplayTitle(string title)
    {
        title = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return title.Length <= 140 ? title : title[..137] + "...";
    }

    private static string FormatDuration(double? seconds)
    {
        if (!seconds.HasValue || seconds.Value <= 0) return string.Empty;
        var span = TimeSpan.FromSeconds(seconds.Value);
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    private async Task<int> RunOneAsync(
        string url,
        DownloadOptions options,
        int itemIndex,
        int itemCount,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        // No modo "Manter os dois", um sufixo curto evita colisão mesmo ao
        // baixar novamente o mesmo link e a mesma qualidade.
        var uniqueSuffix = options.ExistingFileBehavior == ExistingFileBehavior.KeepBoth
            ? $" [{DateTime.Now:yyyyMMdd-HHmmssfff}]"
            : string.Empty;

        _currentCompletedFilePath = null;
        while (_completedFilePaths.TryDequeue(out _)) { }
        _currentRunHadErrors = false;
        _currentRunSkippedByArchive = false;
        var timing = new RunTiming();
        var singleMedia = IsLikelySingleMediaUrl(url);
        if (singleMedia && options.AllowPlaylists)
            log.Report("[Detecção] Link de mídia individual: modo de página/playlist ignorado automaticamente.");

        var reuseKey = singleMedia ? AppCache.BuildDownloadKey(url, options) : string.Empty;
        if (singleMedia && CanReuseCompletedFile(options) && AppCache.TryGetReusableDownload(reuseKey, out var reusablePath))
        {
            var cacheUsable = await IsReusableFileHealthyAsync(reusablePath, ct);
            if (!cacheUsable)
            {
                AppCache.InvalidateReusableDownload(reuseKey);
                log.Report("[Cache] Arquivo salvo está incompleto ou corrompido; será baixado novamente.");
            }

            // Nunca devolve silenciosamente um MP4 antigo em AV1/VP9/Opus.
            // Antes de reutilizar o arquivo, valida a mesma regra aplicada aos downloads novos.
            if (cacheUsable && ShouldGuaranteeCompatibleMp4(options))
            {
                try
                {
                    await EnsureCompatibleMp4Async(reusablePath, options, itemIndex, itemCount, log, progress, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    cacheUsable = false;
                    AppCache.InvalidateReusableDownload(reuseKey);
                    log.Report("[Cache] Arquivo salvo não passou na validação de codec; será baixado novamente. " + ex.Message);
                }
            }

            if (cacheUsable)
            {
                var finalPath = reusablePath;
                if (options.ExistingFileBehavior == ExistingFileBehavior.KeepBoth)
                {
                    finalPath = AppCache.CreateDuplicatePath(reusablePath);
                    File.Copy(reusablePath, finalPath, false);
                    log.Report("[Cache] Mesmo link e mesmas opções: nova cópia criada sem baixar novamente.");
                }
                else
                {
                    log.Report("[Cache] Mesmo link e mesmas opções: arquivo existente reaproveitado sem nova análise.");
                }

                _currentCompletedFilePath = finalPath;
                LastCompletedFilePath = finalPath;
                AppCache.StoreReusableDownload(reuseKey, finalPath);
                progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 100, "", "", Path.GetFileName(finalPath), "Concluído pelo cache"));
                timing.Total.Stop();
                log.Report($"[Arquivo] {finalPath}");
                log.Report($"[Tempo] Cache local: {FormatElapsed(timing.Total.Elapsed)} • Total: {FormatElapsed(timing.Total.Elapsed)}");
                return 0;
            }
        }

        var args = BuildArguments(url, options, uniqueSuffix);
        var psi = new ProcessStartInfo
        {
            FileName = _tools.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = options.OutputDirectory
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => HandleLine(e.Data, itemIndex, itemCount, log, progress, timing);
        _process.ErrorDataReceived += (_, e) => HandleLine(e.Data, itemIndex, itemCount, log, progress, timing);

        log.Report($"URL: {url}");
        progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 0, "", "", "", "Conectando à plataforma"));
        var processStartedUtc = DateTime.UtcNow;
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var reg = ct.Register(Stop);
        await _process.WaitForExitAsync(ct);
        // Garante que os handlers de OutputDataReceived/ErrorDataReceived
        // drenaram as últimas linhas (inclusive PLUTAO_FILE) antes de validar.
        _process.WaitForExit();

        var exitCode = _process.ExitCode;
        if (!singleMedia && exitCode == 0 && _currentRunHadErrors)
            exitCode = 1;
        var processEndedAt = timing.Total.Elapsed;
        var compatibilityElapsed = TimeSpan.Zero;

        string? completedPath = null;
        var capturedPaths = _completedFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (exitCode == 0)
        {
            completedPath = capturedPaths.LastOrDefault()
                            ?? ResolveCompletedMediaPath(options, url, processStartedUtc, uniqueSuffix);
            if (string.IsNullOrWhiteSpace(completedPath) || !File.Exists(completedPath))
            {
                if (options.UseArchive && _currentRunSkippedByArchive)
                {
                    timing.Total.Stop();
                    log.Report("[Histórico] Item já estava registrado; nenhum novo arquivo foi criado.");
                    progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 100, "", "", "", "Ignorado pelo histórico"));
                    return 0;
                }

                log.Report("[Arquivo] ERRO: o yt-dlp terminou, mas o Plutao não conseguiu localizar o arquivo final.");
                log.Report("[Arquivo] O download não será marcado como concluído sem validação do arquivo.");
                return 2;
            }

            _currentCompletedFilePath = completedPath;
            log.Report($"[Arquivo final] {completedPath}");
        }

        if (exitCode == 0 && ShouldGuaranteeCompatibleMp4(options))
        {
            try
            {
                var compatibilityTimer = Stopwatch.StartNew();
                var pathsToValidate = capturedPaths.Length > 0
                    ? capturedPaths
                    : new[] { completedPath! };

                foreach (var pathToValidate in pathsToValidate.Where(File.Exists))
                    await EnsureCompatibleMp4Async(pathToValidate, options, itemIndex, itemCount, log, progress, ct);

                compatibilityTimer.Stop();
                compatibilityElapsed = compatibilityTimer.Elapsed;
                _currentCompletedFilePath = completedPath;
                LastCompletedFilePath = completedPath;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastCompletedFilePath = null;
                log.Report("[Compatibilidade] ERRO ao validar/converter o MP4: " + ex.Message);
                return 2;
            }
        }
        else if (exitCode == 0)
        {
            LastCompletedFilePath = completedPath;
        }

        timing.Total.Stop();
        if (timing.DownloadStarted)
        {
            var downloadAndMerge = processEndedAt - timing.AnalysisDuration;
            if (downloadAndMerge < TimeSpan.Zero) downloadAndMerge = TimeSpan.Zero;
            if (timing.FormatSelectionStartedAt.HasValue && timing.FormatSelectionStartedAt.Value <= timing.AnalysisDuration)
            {
                var connectionAndExtraction = timing.FormatSelectionStartedAt.Value;
                var formatSelection = timing.AnalysisDuration - timing.FormatSelectionStartedAt.Value;
                log.Report($"[Tempo] Conexão/extração: {FormatElapsed(connectionAndExtraction)}");
                log.Report($"[Tempo] Seleção de formato: {FormatElapsed(formatSelection)}");
            }
            else
            {
                log.Report($"[Tempo] Análise/seleção: {FormatElapsed(timing.AnalysisDuration)}");
            }
            log.Report($"[Tempo] Download/merge: {FormatElapsed(downloadAndMerge)}");
            if (compatibilityElapsed > TimeSpan.Zero)
                log.Report($"[Tempo] Compatibilidade/codec: {FormatElapsed(compatibilityElapsed)}");
            log.Report($"[Tempo] Total do item: {FormatElapsed(timing.Total.Elapsed)}");
        }
        else
        {
            log.Report($"[Tempo] Total do item: {FormatElapsed(timing.Total.Elapsed)}");
        }

        if (exitCode == 0)
        {
            var finalCompletedPath = _currentCompletedFilePath ?? LastCompletedFilePath ?? completedPath;
            if (singleMedia && CanReuseCompletedFile(options) && !string.IsNullOrWhiteSpace(finalCompletedPath) && File.Exists(finalCompletedPath))
                AppCache.StoreReusableDownload(reuseKey, finalCompletedPath);
            progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 100, "", "", "", "Concluído"));
        }

        return exitCode;
    }

    private string? ResolveCompletedMediaPath(
        DownloadOptions options,
        string url,
        DateTime processStartedUtc,
        string uniqueSuffix)
    {
        var captured = _currentCompletedFilePath;
        if (!string.IsNullOrWhiteSpace(captured) && File.Exists(captured))
            return Path.GetFullPath(captured);

        try
        {
            if (!Directory.Exists(options.OutputDirectory))
                return null;

            var mediaId = ExtractLikelyMediaId(url);
            var threshold = processStartedUtc.AddSeconds(-5);

            var candidates = Directory.EnumerateFiles(
                    options.OutputDirectory,
                    "*",
                    options.OrganizeByCreator ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(IsMediaFile)
                .Select(path =>
                {
                    try
                    {
                        var info = new FileInfo(path);
                        var name = info.Name;
                        var suffixMatch = !string.IsNullOrWhiteSpace(uniqueSuffix) &&
                                          name.Contains(uniqueSuffix, StringComparison.OrdinalIgnoreCase);
                        var idMatch = !string.IsNullOrWhiteSpace(mediaId) &&
                                      name.Contains($"[{mediaId}]", StringComparison.OrdinalIgnoreCase);
                        var recent = info.LastWriteTimeUtc >= threshold;
                        var score = suffixMatch ? 3 : idMatch ? 2 : recent ? 1 : 0;
                        return new { Path = info.FullName, info.LastWriteTimeUtc, Score = score };
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(x => x is not null && x.Score > 0)
                .OrderByDescending(x => x!.Score)
                .ThenByDescending(x => x!.LastWriteTimeUtc)
                .FirstOrDefault();

            if (candidates is null)
                return null;

            logResolvedPath(candidates.Path);
            return candidates.Path;
        }
        catch
        {
            return null;
        }

        void logResolvedPath(string path)
        {
            _currentCompletedFilePath = path;
        }
    }

    private static string ExtractLikelyMediaId(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return string.Empty;

        var host = uri.Host.ToLowerInvariant();
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (host.Contains("youtu.be") && parts.Length > 0)
            return parts[0];

        if (host.Contains("youtube.com"))
        {
            if (uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=', 2);
                    if (kv.Length == 2 && kv[0].Equals("v", StringComparison.OrdinalIgnoreCase))
                        return Uri.UnescapeDataString(kv[1]);
                }
            }

            if (parts.Length >= 2 &&
                (parts[0].Equals("shorts", StringComparison.OrdinalIgnoreCase) ||
                 parts[0].Equals("live", StringComparison.OrdinalIgnoreCase)))
                return parts[1];
        }

        if (host.Contains("instagram.com") && parts.Length >= 2 &&
            (parts[0].Equals("p", StringComparison.OrdinalIgnoreCase) ||
             parts[0].Equals("reel", StringComparison.OrdinalIgnoreCase) ||
             parts[0].Equals("tv", StringComparison.OrdinalIgnoreCase)))
            return parts[1];

        for (var i = 0; i + 1 < parts.Length; i++)
        {
            if (parts[i].Equals("video", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Equals("status", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        }

        return string.Empty;
    }

    private void HandleLine(
        string? rawLine,
        int itemIndex,
        int itemCount,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        RunTiming timing)
    {
        if (string.IsNullOrWhiteSpace(rawLine)) return;
        var line = AnsiRegex.Replace(rawLine, string.Empty).TrimEnd();
        if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            _currentRunHadErrors = true;

        if (line.Contains("has already been recorded in the archive", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("has already been recorded in archive", StringComparison.OrdinalIgnoreCase))
        {
            _currentRunSkippedByArchive = true;
            progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 100, "", "", "", "Ignorado pelo histórico"));
        }

        if (line.StartsWith(FilePrefix, StringComparison.Ordinal))
        {
            var path = line[FilePrefix.Length..].Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(path) && IsMediaFile(path))
            {
                _currentCompletedFilePath = path;
                _completedFilePaths.Enqueue(path);
                log.Report($"[Arquivo] {path}");
            }
            return;
        }

        if (line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
        {
            MarkDownloadStarted(timing, log);
            var payload = line[ProgressPrefix.Length..];
            var parts = payload.Split('|', 6);
            var percent = ParsePercent(parts.ElementAtOrDefault(0));
            var speed = CleanMetric(parts.ElementAtOrDefault(1));
            var eta = CleanMetric(parts.ElementAtOrDefault(2));
            var collectionIndex = ParsePositiveInt(parts.ElementAtOrDefault(3));
            var collectionCount = ParsePositiveInt(parts.ElementAtOrDefault(4));
            var title = parts.ElementAtOrDefault(5)?.Trim() ?? string.Empty;

            var shouldReport = false;
            lock (timing.SyncRoot)
            {
                var now = timing.Total.Elapsed;
                shouldReport =
                    percent >= 100 ||
                    collectionIndex != timing.LastCollectionIndex ||
                    now - timing.LastProgressReportAt >= TimeSpan.FromMilliseconds(100);

                if (shouldReport)
                {
                    timing.LastProgressReportAt = now;
                    timing.LastCollectionIndex = collectionIndex;
                }
            }

            if (shouldReport)
            {
                progress.Report(new DownloadProgressInfo(
                    itemIndex, itemCount, percent, speed, eta, title, "Baixando", collectionIndex, collectionCount));
            }
            return;
        }

        if (line.StartsWith("[download] Destination:", StringComparison.OrdinalIgnoreCase))
        {
            MarkDownloadStarted(timing, log);
            progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 0, "", "", "", "Iniciando download"));
        }
        else if (line.Contains(" has already been downloaded", StringComparison.OrdinalIgnoreCase))
        {
            MarkDownloadStarted(timing, log);
        }
        else if (line.StartsWith("[info]", StringComparison.OrdinalIgnoreCase) &&
                 line.Contains("Downloading", StringComparison.OrdinalIgnoreCase) &&
                 line.Contains("format", StringComparison.OrdinalIgnoreCase))
        {
            lock (timing.SyncRoot)
            {
                timing.FormatSelectionStartedAt ??= timing.Total.Elapsed;
            }
            progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 0, "", "", "", "Selecionando formato"));
        }
        else if (line.Contains("Downloading webpage", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 0, "", "", "", "Obtendo informações"));
        }
        else if (line.Contains("player API JSON", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("m3u8 information", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("Downloading API JSON", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 0, "", "", "", "Lendo formatos disponíveis"));
        }
        else if (line.Contains("Extracting URL", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 0, "", "", "", "Conectando à plataforma"));
        }

        TryCaptureCompletedFile(line);
        log.Report(line);

        var match = PercentRegex.Match(line);
        if (match.Success && double.TryParse(
                match.Groups["p"].Value.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var fallbackPercent))
        {
            progress.Report(new DownloadProgressInfo(
                itemIndex,
                itemCount,
                Math.Clamp(fallbackPercent, 0, 100),
                "",
                "",
                "",
                "Baixando"));
        }
        else if (line.Contains("[ExtractAudio]", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 100, "", "", "", "Convertendo áudio"));
        }
        else if (line.Contains("[Merger]", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("[VideoRemuxer]", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 100, "", "", "", "Finalizando vídeo"));
        }
    }

    private void TryCaptureCompletedFile(string line)
    {
        string? candidate = null;

        const string movePrefix = "[MoveFiles] Moving file \"";
        if (line.StartsWith(movePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var marker = "\" to \"";
            var markerIndex = line.LastIndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0 && line.EndsWith('"'))
                candidate = line[(markerIndex + marker.Length)..^1];
        }
        else
        {
            const string already = " has already been downloaded";
            if (line.StartsWith("[download] ", StringComparison.OrdinalIgnoreCase) &&
                line.EndsWith(already, StringComparison.OrdinalIgnoreCase))
            {
                candidate = line["[download] ".Length..^already.Length].Trim().Trim('"');
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate) && IsMediaFile(candidate))
        {
            _currentCompletedFilePath = candidate;
            _completedFilePaths.Enqueue(candidate);
        }
    }

    private static bool IsMediaFile(string path)
    {
        var ext = Path.GetExtension(path);
        return MediaExtensions.Any(x => x.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanReuseCompletedFile(DownloadOptions options)
        => options.ExistingFileBehavior != ExistingFileBehavior.Replace &&
           !options.SaveThumbnail &&
           !options.SaveInfoJson &&
           !options.UseArchive &&
           string.IsNullOrWhiteSpace(options.SelectedPlaylistItems);

    private async Task<bool> IsReusableFileHealthyAsync(string path, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 1024)
                return false;

            var videoCodec = await ProbeCodecAsync(path, "v:0", ct);
            var audioCodec = await ProbeCodecAsync(path, "a:0", ct);
            return !string.IsNullOrWhiteSpace(videoCodec) || !string.IsNullOrWhiteSpace(audioCodec);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private IEnumerable<string> BuildArguments(string url, DownloadOptions o, string uniqueSuffix)
    {
        var args = new List<string>
        {
            "--newline",
            "--windows-filenames",
            "--continue",
            "--retries", "5",
            "--fragment-retries", "5",
            "--ffmpeg-location", _tools.ToolsDirectory,
            "--cache-dir", _tools.CacheDirectory,
            "--progress-template", "download:PLUTAO_PROGRESS|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s|%(info.playlist_index)s|%(info.playlist_count)s|%(info.title)s",
            "--print", "after_move:PLUTAO_FILE|%(filepath)s",
            "-P", o.OutputDirectory,
            "-P", $"temp:{o.TemporaryDirectory}",
            "-o", OutputTemplate(o, uniqueSuffix)
        };

        if (IsYouTubeUrl(url))
        {
            args.Add("--js-runtimes");
            args.Add($"deno:{_tools.DenoPath}");
        }

        var singleMedia = IsLikelySingleMediaUrl(url);
        if (singleMedia || !o.AllowPlaylists)
        {
            args.Add("--no-playlist");
        }
        else
        {
            // Permite que um unico link de perfil/canal/pagina se expanda
            // para todos os videos que o extrator da plataforma conseguir listar.
            args.Add("--yes-playlist");
            args.Add("--ignore-errors");

            if (!string.IsNullOrWhiteSpace(o.SelectedPlaylistItems))
            {
                args.Add("--playlist-items");
                args.Add(o.SelectedPlaylistItems);
            }
            else if (o.CollectionLimit > 0)
            {
                args.Add("--playlist-end");
                args.Add(o.CollectionLimit.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (o.EmbedMetadata)
            args.Add("--embed-metadata");

        if (o.SaveThumbnail)
        {
            args.Add("--write-thumbnail");
            args.Add("--convert-thumbnails");
            args.Add("jpg");
        }

        if (o.SaveInfoJson)
            args.Add("--write-info-json");

        if (o.UseArchive)
        {
            args.Add("--download-archive");
            args.Add(Path.Combine(o.OutputDirectory, "plutao-archive.txt"));
        }

        if (o.ExistingFileBehavior == ExistingFileBehavior.Replace)
            args.Add("--force-overwrites");

        if (!string.Equals(o.BrowserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--cookies-from-browser");
            args.Add(o.BrowserCookies.ToLowerInvariant());
        }

        if (url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
        {
            // TikTok muda com frequência e costuma responder melhor com um UA de navegador atual.
            args.Add("--user-agent");
            args.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36");
        }

        if (o.Mode == DownloadMode.Audio)
        {
            args.Add("-x");
            args.Add("--audio-format");
            args.Add(o.AudioFormat);
            args.Add("--audio-quality");
            args.Add("0");
        }
        else
        {
            args.Add("-f");
            args.Add(VideoSelector(url, o.Quality, o.VideoContainer, o.PreferCompatibleMp4));
            args.Add("--merge-output-format");
            args.Add(o.VideoContainer);

            if (!string.Equals(o.VideoContainer, "mkv", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--remux-video");
                args.Add(o.VideoContainer);
            }
        }

        args.Add("--");
        args.Add(url.Trim());
        return args;
    }

    public static bool IsLikelySingleMediaUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();

        if (host.Contains("youtu.be")) return true;
        if (host.Contains("youtube.com") &&
            (path.StartsWith("/watch") || path.StartsWith("/shorts/") || path.StartsWith("/live/")))
            return true;

        if (host.Contains("instagram.com") &&
            (path.StartsWith("/p/") || path.StartsWith("/reel/") || path.StartsWith("/tv/")))
            return true;

        if ((host.Contains("tiktok.com") && path.Contains("/video/")) ||
            host.Equals("vm.tiktok.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("vt.tiktok.com", StringComparison.OrdinalIgnoreCase))
            return true;

        if ((host.Contains("twitter.com") || host.Contains("x.com")) && path.Contains("/status/"))
            return true;

        if (host.Contains("facebook.com") && (path.Contains("/reel/") || path.Contains("/watch")))
            return true;

        return false;
    }

    private static bool IsYouTubeUrl(string url)
        => url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
           url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

    private static void MarkDownloadStarted(RunTiming timing, IProgress<string> log)
    {
        lock (timing.SyncRoot)
        {
            if (timing.DownloadStarted) return;
            timing.DownloadStarted = true;
            timing.AnalysisDuration = timing.Total.Elapsed;
            log.Report($"[Tempo] Preparação/análise do link: {FormatElapsed(timing.AnalysisDuration)}");
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalMinutes >= 1)
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        return $"{elapsed.TotalSeconds:0.0}s";
    }

    private static string OutputTemplate(DownloadOptions o, string uniqueSuffix)
    {
        var file = o.Mode == DownloadMode.Video
            ? $"%(title)s [%(id)s] [%(height)sp]{uniqueSuffix}.%(ext)s"
            : $"%(title)s [%(id)s]{uniqueSuffix}.%(ext)s";

        return o.OrganizeByCreator
            ? $"%(uploader)s/{file}"
            : file;
    }

    private static string VideoSelector(string url, string quality, string container, bool preferCompatibleMp4)
    {
        int? height = null;
        if (!quality.Equals("Melhor", StringComparison.OrdinalIgnoreCase))
        {
            var digits = new string(quality.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var parsed))
                height = parsed;
        }

        var exact = height.HasValue ? $"[height={height.Value}]" : string.Empty;
        var limit = height.HasValue ? $"[height<={height.Value}]" : string.Empty;
        var highResolution = height.HasValue && height.Value > 1080;

        if (container.Equals("mp4", StringComparison.OrdinalIgnoreCase))
        {
            if (height.HasValue && preferCompatibleMp4 && !highResolution)
            {
                // Primeiro tenta H.264 exatamente na resolução pedida. Se essa
                // combinação não existir, prefere QUALQUER codec na resolução
                // exata e converte depois. Assim 1080p nunca cai para 720p só
                // porque o AVC não estava disponível naquele vídeo.
                return $"bv[vcodec^=avc1]{exact}+ba[acodec^=mp4a]/" +
                       $"b[vcodec^=avc1]{exact}/" +
                       $"bv*{exact}+ba/b{exact}/" +
                       $"bv*{limit}+ba/b{limit}/b";
            }

            if (height.HasValue)
            {
                // 1440p/2160p: preserva a resolução pedida e aceita VP9/AV1
                // como fonte. A etapa final converte para HEVC se necessário.
                return $"bv*{exact}+ba/b{exact}/bv*{limit}+ba/b{limit}/b";
            }

            // "Melhor": baixa a melhor resolução real e decide H.264/HEVC
            // depois de medir a altura do arquivo final.
            return "bv*+ba/b";
        }

        if (container.Equals("webm", StringComparison.OrdinalIgnoreCase))
        {
            if (height.HasValue)
                return $"bv*[ext=webm]{exact}+ba[ext=webm]/b[ext=webm]{exact}/bv*[ext=webm]{limit}+ba[ext=webm]/b[ext=webm]{limit}";
            return "bv*[ext=webm]+ba[ext=webm]/b[ext=webm]";
        }

        if (height.HasValue)
            return $"bv*{exact}+ba/b{exact}/bv*{limit}+ba/b{limit}/b";
        return "bv*+ba/b";
    }

    private static bool ShouldGuaranteeCompatibleMp4(DownloadOptions options)
        => options.Mode == DownloadMode.Video &&
           string.Equals(options.VideoContainer, "mp4", StringComparison.OrdinalIgnoreCase);

    private async Task EnsureCompatibleMp4Async(
        string path,
        DownloadOptions options,
        int itemIndex,
        int itemCount,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        var videoCodec = await ProbeCodecAsync(path, "v:0", ct);
        var audioCodec = await ProbeCodecAsync(path, "a:0", ct);
        var (actualWidth, actualHeight) = await ProbeDimensionsAsync(path, ct);
        var requestedHeight = ParseQualityHeight(options.Quality);

        // Usa o menor lado como classe de resolução. Assim um Short 1080x1920
        // continua sendo tratado como 1080p, enquanto 2560x1440 vira 1440p.
        var actualResolution = actualWidth > 0 && actualHeight > 0
            ? Math.Min(actualWidth, actualHeight)
            : actualHeight;
        var effectiveHeight = actualResolution > 0 ? actualResolution : requestedHeight;
        var targetHevc = effectiveHeight > 1080;
        var targetCodecName = targetHevc ? "HEVC/H.265" : "H.264";
        var videoCompatible = targetHevc
            ? string.Equals(videoCodec, "hevc", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(videoCodec, "h265", StringComparison.OrdinalIgnoreCase)
            : string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase);
        var audioCompatible = string.IsNullOrWhiteSpace(audioCodec) ||
                              string.Equals(audioCodec, "aac", StringComparison.OrdinalIgnoreCase);

        var heightText = effectiveHeight > 0 ? $", altura={effectiveHeight}p" : string.Empty;
        log.Report($"[Compatibilidade] Codec detectado: vídeo={EmptyAsUnknown(videoCodec)}, áudio={EmptyAsUnknown(audioCodec)}{heightText}.");
        log.Report($"[Compatibilidade] Regra automática: {targetCodecName} + AAC.");

        if (videoCompatible && audioCompatible)
        {
            log.Report($"[Compatibilidade] MP4 já está em {targetCodecName}/AAC. Nenhuma conversão necessária.");
            return;
        }

        progress.Report(new DownloadProgressInfo(
            itemIndex, itemCount, 99, "", "", Path.GetFileName(path), $"Convertendo para {targetCodecName}/AAC"));
        log.Report($"[Compatibilidade] Convertendo arquivo para {targetCodecName}/AAC sem alterar a resolução...");

        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Pasta do arquivo final não encontrada.");
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(path)}.plutao-compatible-{Guid.NewGuid():N}.mp4");
        var backupPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.plutao-backup-{Guid.NewGuid():N}");

        async Task<(bool Success, string Error)> ConvertAsync(bool useAmdAmf)
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch { }

            var psi = new ProcessStartInfo
            {
                FileName = _tools.FfmpegPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-i", path, "-map", "0:v:0", "-map", "0:a?", "-map_metadata", "0" })
                psi.ArgumentList.Add(arg);

            if (videoCompatible)
            {
                psi.ArgumentList.Add("-c:v");
                psi.ArgumentList.Add("copy");
            }
            else if (targetHevc && useAmdAmf)
            {
                // RX 5000/6000/7000 e outras GPUs AMD compatíveis podem codificar
                // HEVC por hardware via AMF. CQP 20 preserva boa qualidade para
                // 1440p/4K e evita uma taxa fixa baixa demais para conteúdo complexo.
                psi.ArgumentList.Add("-c:v");
                psi.ArgumentList.Add("hevc_amf");
                psi.ArgumentList.Add("-quality");
                psi.ArgumentList.Add("quality");
                psi.ArgumentList.Add("-rc");
                psi.ArgumentList.Add("cqp");
                psi.ArgumentList.Add("-qp_i");
                psi.ArgumentList.Add("20");
                psi.ArgumentList.Add("-qp_p");
                psi.ArgumentList.Add("20");
                psi.ArgumentList.Add("-pix_fmt");
                psi.ArgumentList.Add("yuv420p");
                psi.ArgumentList.Add("-tag:v");
                psi.ArgumentList.Add("hvc1");
            }
            else if (targetHevc)
            {
                // Fallback universal: se AMF não estiver disponível, usa CPU.
                psi.ArgumentList.Add("-c:v");
                psi.ArgumentList.Add("libx265");
                psi.ArgumentList.Add("-preset");
                psi.ArgumentList.Add("medium");
                psi.ArgumentList.Add("-crf");
                psi.ArgumentList.Add("20");
                psi.ArgumentList.Add("-pix_fmt");
                psi.ArgumentList.Add("yuv420p");
                psi.ArgumentList.Add("-tag:v");
                psi.ArgumentList.Add("hvc1");
            }
            else
            {
                psi.ArgumentList.Add("-c:v");
                psi.ArgumentList.Add("libx264");
                psi.ArgumentList.Add("-preset");
                psi.ArgumentList.Add("medium");
                psi.ArgumentList.Add("-crf");
                psi.ArgumentList.Add("18");
                psi.ArgumentList.Add("-pix_fmt");
                psi.ArgumentList.Add("yuv420p");
            }

            if (audioCompatible)
            {
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("copy");
            }
            else
            {
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("aac");
                psi.ArgumentList.Add("-b:a");
                psi.ArgumentList.Add("192k");
            }

            psi.ArgumentList.Add("-movflags");
            psi.ArgumentList.Add("+faststart");
            psi.ArgumentList.Add(tempPath);

            var run = await RunCapturedProcessAsync(psi, ct);
            var success = run.ExitCode == 0 && File.Exists(tempPath) && new FileInfo(tempPath).Length > 0;
            return (success, run.Stderr.Trim());
        }

        try
        {
            var usedAmdAmf = false;
            (bool Success, string Error) result;

            if (targetHevc && !videoCompatible)
            {
                progress.Report(new DownloadProgressInfo(
                    itemIndex, itemCount, 99, "", "", Path.GetFileName(path), "Convertendo HEVC pela GPU"));
                log.Report("[Compatibilidade] Tentando conversão HEVC pela GPU AMD (AMF)...");
                result = await ConvertAsync(useAmdAmf: true);
                usedAmdAmf = result.Success;

                if (!result.Success)
                {
                    var shortError = ShortFfmpegError(result.Error);
                    log.Report(string.IsNullOrWhiteSpace(shortError)
                        ? "[Compatibilidade] AMF indisponível. Usando CPU automaticamente."
                        : $"[Compatibilidade] AMF indisponível ({shortError}). Usando CPU automaticamente.");
                    progress.Report(new DownloadProgressInfo(
                        itemIndex, itemCount, 99, "", "", Path.GetFileName(path), "Convertendo HEVC pela CPU"));
                    result = await ConvertAsync(useAmdAmf: false);
                }
            }
            else
            {
                result = await ConvertAsync(useAmdAmf: false);
            }

            if (!result.Success || !File.Exists(tempPath))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "FFmpeg falhou durante a conversão." : result.Error);

            var tempVideoCodec = await ProbeCodecAsync(tempPath, "v:0", ct);
            var tempAudioCodec = await ProbeCodecAsync(tempPath, "a:0", ct);
            var (tempWidth, tempHeight) = await ProbeDimensionsAsync(tempPath, ct);
            var tempResolution = tempWidth > 0 && tempHeight > 0 ? Math.Min(tempWidth, tempHeight) : tempHeight;
            var tempTargetHevc = (tempResolution > 0 ? tempResolution : effectiveHeight) > 1080;
            var tempVideoOk = tempTargetHevc
                ? string.Equals(tempVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(tempVideoCodec, "h265", StringComparison.OrdinalIgnoreCase)
                : string.Equals(tempVideoCodec, "h264", StringComparison.OrdinalIgnoreCase);
            var tempAudioOk = string.IsNullOrWhiteSpace(tempAudioCodec) ||
                              string.Equals(tempAudioCodec, "aac", StringComparison.OrdinalIgnoreCase);

            if (!tempVideoOk || !tempAudioOk)
                throw new InvalidOperationException(
                    $"Verificação do arquivo convertido falhou: vídeo={EmptyAsUnknown(tempVideoCodec)}, áudio={EmptyAsUnknown(tempAudioCodec)}.");

            // O temporário já passou no ffprobe. Ainda assim, preserva o
            // original até a validação do arquivo colocado no caminho final.
            var backupCreated = false;
            try
            {
                if (File.Exists(backupPath))
                    File.Delete(backupPath);

                File.Move(path, backupPath, false);
                backupCreated = true;
                File.Move(tempPath, path, false);

                var finalVideoCodec = await ProbeCodecAsync(path, "v:0", ct);
                var finalAudioCodec = await ProbeCodecAsync(path, "a:0", ct);
                var (finalWidth, finalHeight) = await ProbeDimensionsAsync(path, ct);
                var finalResolution = finalWidth > 0 && finalHeight > 0 ? Math.Min(finalWidth, finalHeight) : finalHeight;
                var finalTargetHevc = (finalResolution > 0 ? finalResolution : effectiveHeight) > 1080;
                var finalVideoOk = finalTargetHevc
                    ? string.Equals(finalVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(finalVideoCodec, "h265", StringComparison.OrdinalIgnoreCase)
                    : string.Equals(finalVideoCodec, "h264", StringComparison.OrdinalIgnoreCase);
                var finalAudioOk = string.IsNullOrWhiteSpace(finalAudioCodec) ||
                                   string.Equals(finalAudioCodec, "aac", StringComparison.OrdinalIgnoreCase);

                if (!finalVideoOk || !finalAudioOk)
                    throw new InvalidOperationException(
                        $"Verificação final falhou: vídeo={EmptyAsUnknown(finalVideoCodec)}, áudio={EmptyAsUnknown(finalAudioCodec)}.");

                _currentCompletedFilePath = path;
                log.Report($"[Compatibilidade] Verificação final: vídeo={EmptyAsUnknown(finalVideoCodec)}, áudio={EmptyAsUnknown(finalAudioCodec)}, resolução={(finalResolution > 0 ? finalResolution + "p" : "desconhecida")}.");

                if (backupCreated && File.Exists(backupPath))
                    File.Delete(backupPath);
                backupCreated = false;
            }
            catch
            {
                if (backupCreated && File.Exists(backupPath))
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                        File.Move(backupPath, path, true);
                        backupCreated = false;
                    }
                    catch (Exception restoreEx)
                    {
                        throw new InvalidOperationException(
                            $"A conversão falhou e o Plutao não conseguiu restaurar automaticamente o arquivo original. Backup preservado em: {backupPath}",
                            restoreEx);
                    }
                }

                throw;
            }

            if (targetHevc && !videoCompatible)
                log.Report(usedAmdAmf
                    ? "[Compatibilidade] HEVC codificado pela GPU AMD (AMF)."
                    : "[Compatibilidade] HEVC codificado pela CPU (fallback automático).");
            log.Report($"[Compatibilidade] Conversão concluída: {targetCodecName}/AAC.");
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                // Se ainda existir backup aqui, houve uma falha de restauração.
                // Não apaga: é a última cópia segura do arquivo original.
            }
            catch { }
        }
    }

    private static string ShortFfmpegError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return string.Empty;

        var line = error
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .LastOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? error.Trim();

        return line.Length <= 150 ? line : line[..147] + "...";
    }

    private static int ParseQualityHeight(string quality)
    {
        if (string.IsNullOrWhiteSpace(quality) || quality.Equals("Melhor", StringComparison.OrdinalIgnoreCase))
            return 0;
        var digits = new string(quality.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) ? height : 0;
    }

    private async Task<(int Width, int Height)> ProbeDimensionsAsync(string path, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _tools.FfprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0:s=x", path })
            psi.ArgumentList.Add(arg);

        using var probe = new Process { StartInfo = psi };
        probe.Start();
        var stdoutTask = probe.StandardOutput.ReadToEndAsync();
        var stderrTask = probe.StandardError.ReadToEndAsync();
        await probe.WaitForExitAsync(ct);
        var stdout = (await stdoutTask).Trim();
        _ = await stderrTask;

        if (probe.ExitCode != 0)
            return (0, 0);

        var first = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var parts = first?.Split('x', 2);
        return parts is { Length: 2 } &&
               int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
               int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            ? (width, height)
            : (0, 0);
    }

    private async Task<int> ProbeHeightAsync(string path, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _tools.FfprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=height", "-of", "default=noprint_wrappers=1:nokey=1", path })
            psi.ArgumentList.Add(arg);

        using var probe = new Process { StartInfo = psi };
        probe.Start();
        var stdoutTask = probe.StandardOutput.ReadToEndAsync();
        var stderrTask = probe.StandardError.ReadToEndAsync();
        await probe.WaitForExitAsync(ct);
        var stdout = (await stdoutTask).Trim();
        _ = await stderrTask;
        return probe.ExitCode == 0 && int.TryParse(stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var height)
            ? height
            : 0;
    }

    private async Task<string> ProbeCodecAsync(string path, string streamSelector, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _tools.FfprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in new[] { "-v", "error", "-select_streams", streamSelector, "-show_entries", "stream=codec_name", "-of", "default=noprint_wrappers=1:nokey=1", path })
            psi.ArgumentList.Add(arg);

        using var probe = new Process { StartInfo = psi };
        probe.Start();
        var stdoutTask = probe.StandardOutput.ReadToEndAsync();
        var stderrTask = probe.StandardError.ReadToEndAsync();
        await probe.WaitForExitAsync(ct);
        var stdout = (await stdoutTask).Trim();
        _ = await stderrTask;
        return probe.ExitCode == 0
            ? stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static string EmptyAsUnknown(string value)
        => string.IsNullOrWhiteSpace(value) ? "nenhum/desconhecido" : value;

    private static int ParsePositiveInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 0;
    }

    private static double ParsePercent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var match = PercentRegex.Match(text);
        if (!match.Success) return 0;

        return double.TryParse(
            match.Groups["p"].Value.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? Math.Clamp(value, 0, 100)
            : 0;
    }

    private static string CleanMetric(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Replace("N/A", "", StringComparison.OrdinalIgnoreCase).Trim();
    }

    private static void ReportFriendlyHint(string url, DownloadOptions options, IProgress<string> log)
    {
        if (url.Contains("instagram.com", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(options.BrowserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
                log.Report("[Dica Instagram] Perfis/contas frequentemente exigem uma sessão. Tente selecionar Cookies: Edge, Chrome ou Firefox usando uma conta que tenha acesso ao perfil.");
            else
                log.Report("[Dica Instagram] Se aparecer erro ao ler cookies, feche completamente o navegador selecionado e tente novamente.");
        }

        if (url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
        {
            log.Report("[Dica TikTok] Para contas/perfis, mantenha 'Baixar página/conta/canal/perfil completo' ativado. Se falhar, tente Cookies do navegador e Atualizar componentes.");
        }

        if (url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            log.Report("[Dica YouTube] O Plutao usa Deno automaticamente para os desafios JavaScript do YouTube. Se falhar, clique em 'Atualizar componentes' e tente novamente.");
        }
    }
}
