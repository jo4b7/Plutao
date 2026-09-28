using System.IO.Compression;
using System.Net.Http.Headers;

namespace Plutao;

public sealed class ToolManager
{
    private readonly HttpClient _http = new();
    public string ToolsDirectory { get; }
    public string YtDlpPath => Path.Combine(ToolsDirectory, "yt-dlp.exe");
    public string FfmpegPath => Path.Combine(ToolsDirectory, "ffmpeg.exe");
    public string FfprobePath => Path.Combine(ToolsDirectory, "ffprobe.exe");

    public ToolManager()
    {
        ToolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools");
        Directory.CreateDirectory(ToolsDirectory);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Plutao", "0.1"));
    }

    public async Task EnsureYtDlpAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (File.Exists(YtDlpPath)) return;
        await DownloadYtDlpAsync(log, ct);
    }

    public async Task DownloadYtDlpAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        const string url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        log?.Report("Baixando/atualizando yt-dlp...");
        var temp = YtDlpPath + ".download";
        await DownloadFileAsync(url, temp, ct);
        File.Move(temp, YtDlpPath, true);
        log?.Report("yt-dlp pronto.");
    }

    public async Task EnsureFfmpegAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (File.Exists(FfmpegPath) && File.Exists(FfprobePath)) return;
        await DownloadFfmpegAsync(log, ct);
    }

    public async Task DownloadFfmpegAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        const string url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
        log?.Report("Baixando FFmpeg...");

        var zipPath = Path.Combine(Path.GetTempPath(), $"plutao-ffmpeg-{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), $"plutao-ffmpeg-{Guid.NewGuid():N}");

        try
        {
            await DownloadFileAsync(url, zipPath, ct);
            log?.Report("Extraindo FFmpeg...");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var ffmpeg = Directory.EnumerateFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            var ffprobe = Directory.EnumerateFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (ffmpeg is null || ffprobe is null)
                throw new InvalidOperationException("FFmpeg não encontrado no pacote baixado.");

            File.Copy(ffmpeg, FfmpegPath, true);
            File.Copy(ffprobe, FfprobePath, true);
            log?.Report("FFmpeg pronto.");
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { }
        }
    }

    private async Task DownloadFileAsync(string url, string destination, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, ct);
    }
}
