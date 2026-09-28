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
    private static readonly Regex PercentRegex = new(@"(?<p>\d{1,3}(?:[\.,]\d+)?)%", RegexOptions.Compiled);
    private static readonly Regex AnsiRegex = new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);
    private static readonly string[] MediaExtensions =
    {
        ".mp4", ".mkv", ".webm", ".mp3", ".m4a", ".aac", ".flac", ".wav", ".opus"
    };

    public string? LastCompletedFilePath { get; private set; }

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
        await _tools.EnsureAllAsync(log, progress, ct);

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
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Informe o link da página/conta/canal/perfil.", nameof(url));

        progress.Report(new DownloadProgressInfo(0, 0, 0, "", "", "", "Preparando análise"));
        log.Report("[Análise] Preparando yt-dlp e Deno...");
        await _tools.EnsureYtDlpAsync(log, progress, ct);
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

            if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
            {
                var sequentialIndex = 0;
                foreach (var entry in entries.EnumerateArray())
                {
                    sequentialIndex++;
                    if (entry.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                        continue;

                    var index = ReadInt(entry, "playlist_index") ?? sequentialIndex;
                    var id = ReadString(entry, "id") ?? string.Empty;
                    var title = ReadString(entry, "title")
                                ?? ReadString(entry, "description")
                                ?? (!string.IsNullOrWhiteSpace(id) ? id : $"Mídia {index}");
                    var duration = ReadDouble(entry, "duration");
                    items.Add(new CollectionMediaItem(index, id, CleanDisplayTitle(title), FormatDuration(duration)));
                }
            }
            else
            {
                var id = ReadString(root, "id") ?? string.Empty;
                var title = ReadString(root, "title") ?? (!string.IsNullOrWhiteSpace(id) ? id : "Mídia 1");
                items.Add(new CollectionMediaItem(1, id, CleanDisplayTitle(title), FormatDuration(ReadDouble(root, "duration"))));
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("A plataforma respondeu, mas o Plutao não conseguiu interpretar a lista de vídeos.", ex);
        }

        progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", $"{items.Count} mídia(s) encontrada(s)"));
        log.Report($"[Análise] Encontrados {items.Count} item(ns)." );
        return items;
    }

    private static string? ReadString(JsonElement element, string property)
    {
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
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        return null;
    }

    private static double? ReadDouble(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return null;
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
        _process.OutputDataReceived += (_, e) => HandleLine(e.Data, itemIndex, itemCount, log, progress);
        _process.ErrorDataReceived += (_, e) => HandleLine(e.Data, itemIndex, itemCount, log, progress);

        log.Report($"URL: {url}");
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var reg = ct.Register(Stop);
        await _process.WaitForExitAsync(ct);
        await Task.Delay(80, CancellationToken.None);

        if (_process.ExitCode == 0)
            progress.Report(new DownloadProgressInfo(itemIndex, itemCount, 100, "", "", "", "Concluído"));

        return _process.ExitCode;
    }

    private void HandleLine(
        string? rawLine,
        int itemIndex,
        int itemCount,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress)
    {
        if (string.IsNullOrWhiteSpace(rawLine)) return;
        var line = AnsiRegex.Replace(rawLine, string.Empty).TrimEnd();

        if (line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
        {
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
            LastCompletedFilePath = candidate;
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
            "--js-runtimes", $"deno:{_tools.DenoPath}",
            "--progress-template", "download:PLUTAO_PROGRESS|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s|%(info.playlist_index)s|%(info.playlist_count)s|%(info.title)s",
            "-P", o.OutputDirectory,
            "-P", $"temp:{o.TemporaryDirectory}",
            "-o", OutputTemplate(o, uniqueSuffix)
        };

        if (!o.AllowPlaylists)
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
                // Instagram, TikTok e vários outros sites costumam fornecer um
                // MP4 progressivo (vídeo+áudio juntos) e nem sempre anunciam o
                // codec como 'avc1'. Priorize MP4 sem exigir o rótulo de codec.
                return $"b[ext=mp4]{limit}/" +
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
