using System.IO.Compression;
using System.Net.Http.Headers;

namespace Plutao;

public sealed class ToolManager
{
    private readonly HttpClient _http = new();

    public string ToolsDirectory { get; }
    public string TemporaryDirectory { get; set; }
    public string YtDlpPath => Path.Combine(ToolsDirectory, "yt-dlp.exe");
    public string FfmpegPath => Path.Combine(ToolsDirectory, "ffmpeg.exe");
    public string FfprobePath => Path.Combine(ToolsDirectory, "ffprobe.exe");
    public string DenoPath => Path.Combine(ToolsDirectory, "deno.exe");

    public ToolManager()
    {
        ToolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools");
        TemporaryDirectory = ResolveDefaultTempDirectory();
        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(TemporaryDirectory);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Plutao", "0.2.1"));
    }

    public static string ResolveDefaultTempDirectory()
    {
        const string preferredRoot = @"C:\PROJETOS\Plutao-";
        if (Directory.Exists(preferredRoot))
            return Path.Combine(preferredRoot, "Teporarios");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Plutao",
            "Temporarios");
    }

    public async Task EnsureAllAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(TemporaryDirectory);
        await EnsureYtDlpAsync(log, ct);
        await EnsureFfmpegAsync(log, ct);
        await EnsureDenoAsync(log, ct);
    }

    public async Task UpdateAllAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(TemporaryDirectory);
        await DownloadYtDlpAsync(log, ct);
        await DownloadFfmpegAsync(log, ct);
        await DownloadDenoAsync(log, ct);
    }

    public async Task EnsureYtDlpAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (File.Exists(YtDlpPath)) return;
        await DownloadYtDlpAsync(log, ct);
    }

    public async Task DownloadYtDlpAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        const string url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        log?.Report("[Componentes] Baixando/atualizando yt-dlp...");
        var temp = Path.Combine(TemporaryDirectory, "yt-dlp.exe.download");
        await DownloadFileAsync(url, temp, ct);
        File.Move(temp, YtDlpPath, true);
        log?.Report("[Componentes] yt-dlp pronto.");
    }

    public async Task EnsureFfmpegAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (File.Exists(FfmpegPath) && File.Exists(FfprobePath)) return;
        await DownloadFfmpegAsync(log, ct);
    }

    public async Task DownloadFfmpegAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        const string url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
        log?.Report("[Componentes] Baixando FFmpeg...");

        Directory.CreateDirectory(TemporaryDirectory);
        var zipPath = Path.Combine(TemporaryDirectory, $"plutao-ffmpeg-{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(TemporaryDirectory, $"plutao-ffmpeg-{Guid.NewGuid():N}");

        try
        {
            await DownloadFileAsync(url, zipPath, ct);
            log?.Report("[Componentes] Extraindo FFmpeg...");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var ffmpeg = Directory.EnumerateFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            var ffprobe = Directory.EnumerateFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (ffmpeg is null || ffprobe is null)
                throw new InvalidOperationException("FFmpeg não encontrado no pacote baixado.");

            File.Copy(ffmpeg, FfmpegPath, true);
            File.Copy(ffprobe, FfprobePath, true);
            log?.Report("[Componentes] FFmpeg pronto.");
        }
        finally
        {
            TryDeleteFile(zipPath);
            TryDeleteDirectory(extractDir);
        }
    }

    public async Task EnsureDenoAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (File.Exists(DenoPath)) return;
        await DownloadDenoAsync(log, ct);
    }

    public async Task DownloadDenoAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        const string url = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";
        log?.Report("[Componentes] Baixando Deno (necessário para suporte completo ao YouTube)...");

        Directory.CreateDirectory(TemporaryDirectory);
        var zipPath = Path.Combine(TemporaryDirectory, $"plutao-deno-{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(TemporaryDirectory, $"plutao-deno-{Guid.NewGuid():N}");

        try
        {
            await DownloadFileAsync(url, zipPath, ct);
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            var deno = Directory.EnumerateFiles(extractDir, "deno.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (deno is null)
                throw new InvalidOperationException("Deno não encontrado no pacote baixado.");

            File.Copy(deno, DenoPath, true);
            log?.Report("[Componentes] Deno pronto.");
        }
        finally
        {
            TryDeleteFile(zipPath);
            TryDeleteDirectory(extractDir);
        }
    }

    private async Task DownloadFileAsync(string url, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, ct);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
