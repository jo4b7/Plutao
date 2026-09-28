using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Plutao;

public sealed class YtDlpRunner
{
    private readonly ToolManager _tools;
    private Process? _process;
    private static readonly Regex PercentRegex = new(@"(?<p>\d{1,3}(?:[\.,]\d+)?)%", RegexOptions.Compiled);

    public YtDlpRunner(ToolManager tools) => _tools = tools;

    public void Stop()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill(true);
        }
        catch { }
    }

    public async Task<int> DownloadAsync(
        IReadOnlyList<string> urls,
        DownloadOptions options,
        IProgress<string> log,
        IProgress<int> progress,
        CancellationToken ct)
    {
        await _tools.EnsureYtDlpAsync(log, ct);
        await _tools.EnsureFfmpegAsync(log, ct);

        Directory.CreateDirectory(options.OutputDirectory);

        int lastExit = 0;
        for (int i = 0; i < urls.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            log.Report($"\r\n=== Item {i + 1}/{urls.Count} ===");
            lastExit = await RunOneAsync(urls[i], options, log, progress, ct);
            if (lastExit != 0)
                log.Report($"Falha no item {i + 1} (código {lastExit}). Continuando a fila...");
        }

        return lastExit;
    }

    private async Task<int> RunOneAsync(string url, DownloadOptions options, IProgress<string> log, IProgress<int> progress, CancellationToken ct)
    {
        var args = BuildArguments(url, options);
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

        foreach (var arg in args) psi.ArgumentList.Add(arg);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => HandleLine(e.Data, log, progress);
        _process.ErrorDataReceived += (_, e) => HandleLine(e.Data, log, progress);

        log.Report($"URL: {url}");
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var reg = ct.Register(Stop);
        await _process.WaitForExitAsync(ct);
        progress.Report(_process.ExitCode == 0 ? 100 : 0);
        return _process.ExitCode;
    }

    private void HandleLine(string? line, IProgress<string> log, IProgress<int> progress)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        log.Report(line);

        var m = PercentRegex.Match(line);
        if (!m.Success) return;
        if (double.TryParse(m.Groups["p"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            progress.Report(Math.Clamp((int)Math.Round(value), 0, 100));
    }

    private IEnumerable<string> BuildArguments(string url, DownloadOptions o)
    {
        var args = new List<string>
        {
            "--newline",
            "--no-warnings",
            "--windows-filenames",
            "--ffmpeg-location", _tools.ToolsDirectory,
            "-P", o.OutputDirectory,
            "-o", "%(uploader)s/%(title)s [%(id)s].%(ext)s"
        };

        if (!o.AllowPlaylists) args.Add("--no-playlist");
        if (o.EmbedMetadata) args.Add("--embed-metadata");
        if (o.SaveThumbnail)
        {
            args.Add("--write-thumbnail");
            args.Add("--convert-thumbnails"); args.Add("jpg");
        }
        if (o.SaveInfoJson) args.Add("--write-info-json");
        if (o.UseArchive)
        {
            args.Add("--download-archive");
            args.Add(Path.Combine(o.OutputDirectory, "plutao-archive.txt"));
        }

        if (!string.Equals(o.BrowserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--cookies-from-browser");
            args.Add(o.BrowserCookies.ToLowerInvariant());
        }

        if (o.Mode == DownloadMode.Audio)
        {
            args.Add("-x");
            args.Add("--audio-format"); args.Add(o.AudioFormat);
            args.Add("--audio-quality"); args.Add("0");
        }
        else
        {
            args.Add("-f"); args.Add(VideoSelector(o.Quality));
            args.Add("--merge-output-format"); args.Add(o.VideoContainer);
            args.Add("--remux-video"); args.Add(o.VideoContainer);
        }

        args.Add("--");
        args.Add(url);
        return args;
    }

    private static string VideoSelector(string quality)
    {
        if (quality.Equals("Melhor", StringComparison.OrdinalIgnoreCase))
            return "bv*+ba/b";

        var h = new string(quality.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(h, out var height)
            ? $"bv*[height<={height}]+ba/b[height<={height}]"
            : "bv*+ba/b";
    }
}
