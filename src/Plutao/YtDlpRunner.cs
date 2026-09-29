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
    public string? LastCompletedFilePath { get; private set; }
    public ProfileInfo? LastAnalyzedProfile { get; private set; }

    private sealed class RunTiming
    {
        public Stopwatch Total { get; } = Stopwatch.StartNew();
        public object SyncRoot { get; } = new();
        public bool DownloadStarted { get; set; }
        public TimeSpan AnalysisDuration { get; set; }
    }

    public YtDlpRunner(ToolManager tools) => _tools = tools;

    public void Stop()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(true);
        }
        catch { }
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

        // O extrator instagram:user do yt-dlp está quebrado nas versões atuais.
        // Para perfis do Instagram usamos gallery-dl para listar os posts/reels
        // e depois baixamos os itens escolhidos individualmente com yt-dlp.
        if (IsInstagramProfileUrl(url))
            return await AnalyzeInstagramProfileAsync(url, browserCookies, limit, log, progress, ct);

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

        _process = new Process { StartInfo = psi };
        progress.Report(new DownloadProgressInfo(0, 0, 15, "", "", "", "Analisando página/conta"));
        log.Report($"[Análise] Lendo itens de: {url}");

        _process.Start();
        using var reg = ct.Register(Stop);
        var stdoutTask = _process.StandardOutput.ReadToEndAsync();
        var stderrTask = _process.StandardError.ReadToEndAsync();
        await _process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        foreach (var line in stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            log.Report("[Análise] " + AnsiRegex.Replace(line, string.Empty));

        if (_process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
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

        progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", $"{items.Count} mídia(s) encontrada(s)"));
        log.Report($"[Análise] Encontrados {items.Count} item(ns).");
        return items;
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
            ExtractInstagramVideoItems(stdout, items, seen, limit);
        }

        if (items.Count == 0)
        {
            if (string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "O Instagram não liberou a lista de vídeos desse perfil sem uma sessão. " +
                    "Selecione Cookies: Edge, Chrome ou Firefox (com o Instagram logado) e tente ANALISAR CONTA/PÁGINA novamente.");
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

        LastAnalyzedProfile = (LastAnalyzedProfile ?? BuildInstagramFallbackProfile(profileBase)) with
        {
            FoundVideos = indexed.Length
        };

        progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", $"{indexed.Length} vídeo(s) encontrado(s)"));
        log.Report($"[Análise/Instagram] Encontrados {indexed.Length} vídeo(s) únicos.");
        return indexed;
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

        _process = new Process { StartInfo = psi };
        _process.Start();
        using var reg = ct.Register(Stop);
        var stdoutTask = _process.StandardOutput.ReadToEndAsync();
        var stderrTask = _process.StandardError.ReadToEndAsync();
        await _process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        foreach (var line in stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            log.Report("[Análise/Instagram] " + AnsiRegex.Replace(line, string.Empty));

        if (_process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            return string.Empty;

        return stdout;
    }

    private static void ExtractInstagramVideoItems(
        string stdout,
        List<CollectionMediaItem> items,
        HashSet<string> seen,
        int limit)
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
                var details = FormatInstagramMediaDetails(metadata);
                items.Add(new CollectionMediaItem(
                    items.Count + 1,
                    shortcode,
                    title,
                    "",
                    postUrl,
                    thumbnail,
                    date,
                    details));
            }
        }
        catch (JsonException)
        {
            // O stderr já é enviado ao log; um resultado inválido simplesmente
            // deixa este alvo sem itens e permite tentar o próximo (posts/reels).
        }
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
                    profileUrl);
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
        var username = ReadString(root, "uploader")
                       ?? ReadString(root, "channel")
                       ?? ReadString(root, "playlist_uploader")
                       ?? ReadString(root, "uploader_id")
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
            url)
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
        var direct = ReadString(element, "thumbnail");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("thumbnails", out var thumbnails) &&
            thumbnails.ValueKind == JsonValueKind.Array)
        {
            string result = string.Empty;
            foreach (var thumb in thumbnails.EnumerateArray())
            {
                if (thumb.ValueKind != JsonValueKind.Object) continue;
                var value = ReadString(thumb, "url");
                if (!string.IsNullOrWhiteSpace(value)) result = value;
            }
            return result;
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
        var width = ReadInt(element, "width");
        var height = ReadInt(element, "height");
        if (views.HasValue) details.Add($"{views.Value:N0} visualizações");
        if (likes.HasValue) details.Add($"{likes.Value:N0} curtidas");
        if (width.HasValue && height.HasValue) details.Add($"{width}×{height}");
        return string.Join(" • ", details);
    }

    private static string FormatInstagramMediaDetails(JsonElement metadata)
    {
        var details = new List<string>();
        var likes = ReadLong(metadata, "likes") ?? ReadLong(metadata, "like_count");
        var width = ReadInt(metadata, "width_original") ?? ReadInt(metadata, "width");
        var height = ReadInt(metadata, "height_original") ?? ReadInt(metadata, "height");
        if (likes.HasValue) details.Add($"{likes.Value:N0} curtidas");
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
        var timing = new RunTiming();
        var singleMedia = IsLikelySingleMediaUrl(url);
        if (singleMedia && options.AllowPlaylists)
            log.Report("[Detecção] Link de mídia individual: modo de página/playlist ignorado automaticamente.");

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
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var reg = ct.Register(Stop);
        await _process.WaitForExitAsync(ct);
        await Task.Delay(80, CancellationToken.None);

        var exitCode = _process.ExitCode;
        if (exitCode == 0 && ShouldGuaranteeCompatibleMp4(options))
        {
            var path = _currentCompletedFilePath ?? LastCompletedFilePath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    await EnsureCompatibleMp4Async(path, itemIndex, itemCount, log, progress, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log.Report("[Compatibilidade] ERRO ao garantir H.264/AAC: " + ex.Message);
                    return 2;
                }
            }
            else
            {
                log.Report("[Compatibilidade] Não foi possível localizar o arquivo final para validar o codec.");
            }
        }

        timing.Total.Stop();
        if (timing.DownloadStarted)
        {
            var afterAnalysis = timing.Total.Elapsed - timing.AnalysisDuration;
            if (afterAnalysis < TimeSpan.Zero) afterAnalysis = TimeSpan.Zero;
            log.Report($"[Tempo] Download/processamento: {FormatElapsed(afterAnalysis)} • Total: {FormatElapsed(timing.Total.Elapsed)}");
        }
        else
        {
            log.Report($"[Tempo] Total do item: {FormatElapsed(timing.Total.Elapsed)}");
        }

        if (exitCode == 0)
            progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 100, "", "", "", "Concluído"));

        return exitCode;
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

        if (line.StartsWith(FilePrefix, StringComparison.Ordinal))
        {
            var path = line[FilePrefix.Length..].Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(path) && IsMediaFile(path))
            {
                _currentCompletedFilePath = path;
                LastCompletedFilePath = path;
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

            progress.Report(new DownloadProgressInfo(
                itemIndex, itemCount, percent, speed, eta, title, "Baixando", collectionIndex, collectionCount));
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
            LastCompletedFilePath = candidate;
        }
    }

    private static bool IsMediaFile(string path)
    {
        var ext = Path.GetExtension(path);
        return MediaExtensions.Any(x => x.Equals(ext, StringComparison.OrdinalIgnoreCase));
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
            // TikTok muda com frequencia e costuma responder melhor com um UA de navegador atual.
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

        var limit = height.HasValue ? $"[height<={height.Value}]" : string.Empty;
        var isYouTube = url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                        url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

        if (container.Equals("mp4", StringComparison.OrdinalIgnoreCase))
        {
            if (preferCompatibleMp4 && isYouTube)
            {
                // No YouTube, AV1 em MP4 é comum. Mantemos uma seleção rígida
                // de H.264/AVC + AAC para máxima compatibilidade com Windows/TVs.
                return $"bv[vcodec^=avc1]{limit}+ba[acodec^=mp4a]/" +
                       $"b[vcodec^=avc1]{limit}/" +
                       "bv[vcodec^=avc1]+ba[acodec^=mp4a]/b[vcodec^=avc1]";
            }

            if (preferCompatibleMp4)
            {
                // Fora do YouTube, tenta H.264/AAC primeiro, mas mantém
                // fallbacks flexíveis porque algumas plataformas não rotulam
                // os codecs de forma consistente. Se o fallback vier em VP9/AV1,
                // o arquivo final é validado e convertido automaticamente.
                return $"bv[vcodec^=avc1]{limit}+ba[acodec^=mp4a]/" +
                       $"b[vcodec^=avc1]{limit}/" +
                       $"b[ext=mp4]{limit}/" +
                       $"bv*[ext=mp4]{limit}+ba/" +
                       $"b{limit}/" +
                       "b[ext=mp4]/b/bv*+ba";
            }

            return $"bv*{limit}[ext=mp4]+ba[ext=m4a]/b[ext=mp4]{limit}/bv*{limit}+ba/b{limit}/b";
        }

        if (container.Equals("webm", StringComparison.OrdinalIgnoreCase))
            return $"bv*{limit}[ext=webm]+ba[ext=webm]/b[ext=webm]{limit}/bv*{limit}+ba/b{limit}/b";

        return $"bv*{limit}+ba/b{limit}/b";
    }

    private static bool ShouldGuaranteeCompatibleMp4(DownloadOptions options)
        => options.Mode == DownloadMode.Video &&
           options.PreferCompatibleMp4 &&
           string.Equals(options.VideoContainer, "mp4", StringComparison.OrdinalIgnoreCase);

    private async Task EnsureCompatibleMp4Async(
        string path,
        int itemIndex,
        int itemCount,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        var videoCodec = await ProbeCodecAsync(path, "v:0", ct);
        var audioCodec = await ProbeCodecAsync(path, "a:0", ct);

        var videoCompatible = string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase);
        var audioCompatible = string.IsNullOrWhiteSpace(audioCodec) ||
                              string.Equals(audioCodec, "aac", StringComparison.OrdinalIgnoreCase);

        log.Report($"[Compatibilidade] Codec detectado: vídeo={EmptyAsUnknown(videoCodec)}, áudio={EmptyAsUnknown(audioCodec)}.");

        if (videoCompatible && audioCompatible)
        {
            log.Report("[Compatibilidade] MP4 já está em H.264/AAC. Nenhuma conversão necessária.");
            return;
        }

        progress.Report(new DownloadProgressInfo(
            itemIndex, itemCount, 99, "", "", Path.GetFileName(path), "Convertendo para H.264/AAC"));
        log.Report("[Compatibilidade] Convertendo arquivo para H.264/AAC para máxima compatibilidade...");

        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Pasta do arquivo final não encontrada.");
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(path)}.plutao-compatible-{Guid.NewGuid():N}.mp4");

        try
        {
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

            _process = new Process { StartInfo = psi };
            _process.Start();
            using var reg = ct.Register(Stop);
            var stdoutTask = _process.StandardOutput.ReadToEndAsync();
            var stderrTask = _process.StandardError.ReadToEndAsync();
            await _process.WaitForExitAsync(ct);
            _ = await stdoutTask;
            var stderr = await stderrTask;

            if (_process.ExitCode != 0 || !File.Exists(tempPath))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "FFmpeg falhou durante a conversão." : stderr.Trim());

            File.Move(tempPath, path, true);
            _currentCompletedFilePath = path;
            LastCompletedFilePath = path;
            log.Report("[Compatibilidade] Conversão concluída: H.264/AAC.");
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch { }
        }
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
                log.Report("[Dica Instagram] Perfis/contas frequentemente exigem uma sessao. Tente selecionar Cookies: Edge, Chrome ou Firefox usando uma conta que tenha acesso ao perfil.");
            else
                log.Report("[Dica Instagram] Se aparecer erro ao ler cookies, feche completamente o navegador selecionado e tente novamente.");
        }

        if (url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
        {
            log.Report("[Dica TikTok] Para contas/perfis, mantenha 'Baixar pagina/conta/canal/perfil completo' ativado. Se falhar, tente Cookies do navegador e Atualizar componentes.");
        }

        if (url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            log.Report("[Dica YouTube] O Plutao usa Deno automaticamente para os desafios JavaScript do YouTube. Se falhar, clique em 'Atualizar componentes' e tente novamente.");
        }
    }
}
