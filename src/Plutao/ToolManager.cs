using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace Plutao;

public sealed class ToolManager
{
    private readonly HttpClient _http = new();

    public string ToolsDirectory { get; }
    public string CacheDirectory { get; }
    public string TemporaryDirectory { get; set; }
    public string YtDlpPath => Path.Combine(ToolsDirectory, "yt-dlp.exe");
    public string FfmpegPath => Path.Combine(ToolsDirectory, "ffmpeg.exe");
    public string FfprobePath => Path.Combine(ToolsDirectory, "ffprobe.exe");
    public string DenoPath => Path.Combine(ToolsDirectory, "deno.exe");
    public string GalleryDlPath => Path.Combine(ToolsDirectory, "gallery-dl.exe");

    public ToolManager()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Plutao");

        ToolsDirectory = Path.Combine(appDataRoot, "tools");
        CacheDirectory = Path.Combine(appDataRoot, "cache");
        TemporaryDirectory = ResolveDefaultTempDirectory();

        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(TemporaryDirectory);
        MigrateLegacyTools();

        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Plutao", "0.5.0"));

        // O HttpClient usa 100 s por padrão. O pacote do FFmpeg pode levar mais
        // que isso em conexões lentas e acabava sendo mostrado como "cancelado".
        // O cancelamento agora é controlado somente pelo botão PARAR/token.
        _http.Timeout = Timeout.InfiniteTimeSpan;
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


    public async Task EnsureDownloadDependenciesAsync(
        IReadOnlyList<string> urls,
        IProgress<string>? log = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(TemporaryDirectory);
        await EnsureYtDlpAsync(log, progress, ct);
        await EnsureFfmpegAsync(log, progress, ct);

        // Deno é necessário principalmente para o extrator moderno do YouTube.
        // Outras plataformas não precisam esperar por ele antes de iniciar.
        if (urls.Any(IsYouTubeUrl))
            await EnsureDenoAsync(log, progress, ct);
    }

    public async Task EnsureAllAsync(
        IProgress<string>? log = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(TemporaryDirectory);
        await EnsureYtDlpAsync(log, progress, ct);
        await EnsureFfmpegAsync(log, progress, ct);
        await EnsureDenoAsync(log, progress, ct);
    }

    public async Task UpdateAllAsync(
        IProgress<string>? log = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(TemporaryDirectory);
        await DownloadYtDlpAsync(log, progress, ct);
        await DownloadFfmpegAsync(log, progress, ct);
        await DownloadDenoAsync(log, progress, ct);
        if (File.Exists(GalleryDlPath))
            await DownloadGalleryDlAsync(log, progress, ct);
    }

    public async Task EnsureGalleryDlAsync(
        IProgress<string>? log = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        if (File.Exists(GalleryDlPath)) return;
        await DownloadGalleryDlAsync(log, progress, ct);
    }

    public async Task DownloadGalleryDlAsync(
        IProgress<string>? log = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        const string url = "https://github.com/gdl-org/builds/releases/latest/download/gallery-dl_windows.exe";
        log?.Report("[Componentes] Baixando analisador de perfis (gallery-dl)...");
        var temp = Path.Combine(TemporaryDirectory, "gallery-dl.exe.download");
        await DownloadFileAsync(url, temp, "Baixando analisador de perfis", progress, ct);
        File.Move(temp, GalleryDlPath, true);
        progress?.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", "Analisador de perfis pronto"));
        log?.Report("[Componentes] gallery-dl pronto.");
    }

    public async Task EnsureYtDlpAsync(
        IProgress<string>? log = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        if (File.Exists(YtDlpPath)) return;
        await DownloadYtDlpAsync(log, progress, ct);
    }

    public async Task DownloadYtDlpAsync(
        IProgress<string>? log = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        const string url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        log?.Report("[Componentes] Baixando/atualizando yt-dlp...");
        var temp = Path.Combine(TemporaryDirectory, "yt-dlp.exe.download");
        await DownloadFileAsync(url, temp, "Baixando yt-dlp", progress, ct);
        File.Move(temp, YtDlpPath, true);
        progress?.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", "yt-dlp pronto"));
        log?.Report("[Componentes] yt-dlp pronto.");
    }

    public async Task EnsureFfmpegAsync(
        IProgress<string>? log = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        if (File.Exists(FfmpegPath) && File.Exists(FfprobePath)) return;
        await DownloadFfmpegAsync(log, progress, ct);
    }

    public async Task DownloadFfmpegAsync(
        IProgress<string>? log = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        const string url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
        log?.Report("[Componentes] Baixando FFmpeg...");

        Directory.CreateDirectory(TemporaryDirectory);
        var zipPath = Path.Combine(TemporaryDirectory, $"plutao-ffmpeg-{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(TemporaryDirectory, $"plutao-ffmpeg-{Guid.NewGuid():N}");

        try
        {
            await DownloadFileAsync(url, zipPath, "Baixando FFmpeg", progress, ct);
            ct.ThrowIfCancellationRequested();

            log?.Report("[Componentes] Extraindo FFmpeg...");
            progress?.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", "Extraindo FFmpeg"));
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            ct.ThrowIfCancellationRequested();
            var ffmpeg = Directory.EnumerateFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            var ffprobe = Directory.EnumerateFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (ffmpeg is null || ffprobe is null)
                throw new InvalidOperationException("FFmpeg não encontrado no pacote baixado.");

            File.Copy(ffmpeg, FfmpegPath, true);
            File.Copy(ffprobe, FfprobePath, true);
            progress?.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", "FFmpeg pronto"));
            log?.Report("[Componentes] FFmpeg pronto.");
        }
        finally
        {
            TryDeleteFile(zipPath);
            TryDeleteDirectory(extractDir);
        }
    }

    public async Task EnsureDenoAsync(
        IProgress<string>? log = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        if (File.Exists(DenoPath)) return;
        await DownloadDenoAsync(log, progress, ct);
    }

    public async Task DownloadDenoAsync(
        IProgress<string>? log = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        const string url = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";
        log?.Report("[Componentes] Baixando Deno (necessário para suporte completo ao YouTube)...");

        Directory.CreateDirectory(TemporaryDirectory);
        var zipPath = Path.Combine(TemporaryDirectory, $"plutao-deno-{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(TemporaryDirectory, $"plutao-deno-{Guid.NewGuid():N}");

        try
        {
            await DownloadFileAsync(url, zipPath, "Baixando Deno", progress, ct);
            ct.ThrowIfCancellationRequested();

            progress?.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", "Extraindo Deno"));
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            var deno = Directory.EnumerateFiles(extractDir, "deno.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (deno is null)
                throw new InvalidOperationException("Deno não encontrado no pacote baixado.");

            File.Copy(deno, DenoPath, true);
            progress?.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", "Deno pronto"));
            log?.Report("[Componentes] Deno pronto.");
        }
        finally
        {
            TryDeleteFile(zipPath);
            TryDeleteDirectory(extractDir);
        }
    }


    private void MigrateLegacyTools()
    {
        // Versões anteriores guardavam os componentes ao lado do Plutao.exe.
        // Copiamos os que já existem para o cache permanente para evitar novo download.
        try
        {
            var legacyDirectory = Path.Combine(AppContext.BaseDirectory, "tools");
            if (!Directory.Exists(legacyDirectory) ||
                Path.GetFullPath(legacyDirectory).Equals(Path.GetFullPath(ToolsDirectory), StringComparison.OrdinalIgnoreCase))
                return;

            foreach (var fileName in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe", "gallery-dl.exe" })
            {
                var source = Path.Combine(legacyDirectory, fileName);
                var destination = Path.Combine(ToolsDirectory, fileName);
                if (File.Exists(source) && !File.Exists(destination))
                    File.Copy(source, destination, false);
            }
        }
        catch
        {
            // Migração é apenas uma otimização; falhas não impedem o aplicativo de abrir.
        }
    }

    private static bool IsYouTubeUrl(string url)
        => url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
           url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

    private async Task DownloadFileAsync(
        string url,
        string destination,
        string stage,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);

        var buffer = new byte[1024 * 128];
        long downloaded = 0;
        var sw = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0) break;

            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;

            if (sw.Elapsed - lastReport < TimeSpan.FromMilliseconds(180))
                continue;

            lastReport = sw.Elapsed;
            ReportTransferProgress(stage, downloaded, totalBytes, sw.Elapsed, progress);
        }

        ReportTransferProgress(stage, downloaded, totalBytes, sw.Elapsed, progress, forceComplete: true);
    }

    private static void ReportTransferProgress(
        string stage,
        long downloaded,
        long? totalBytes,
        TimeSpan elapsed,
        IProgress<DownloadProgressInfo>? progress,
        bool forceComplete = false)
    {
        if (progress is null) return;

        var percent = totalBytes is > 0
            ? Math.Clamp(downloaded * 100.0 / totalBytes.Value, 0, 100)
            : forceComplete ? 100 : 0;

        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        var bytesPerSecond = downloaded / seconds;
        var speed = FormatSpeed(bytesPerSecond);
        var eta = string.Empty;

        if (totalBytes is > 0 && bytesPerSecond > 0 && downloaded < totalBytes.Value)
        {
            var secondsLeft = (totalBytes.Value - downloaded) / bytesPerSecond;
            eta = FormatEta(secondsLeft);
        }

        var downloadedMiB = downloaded / 1024d / 1024d;
        var title = totalBytes is > 0
            ? $"{downloadedMiB:0.0}/{totalBytes.Value / 1024d / 1024d:0.0} MB"
            : $"{downloadedMiB:0.0} MB";

        progress.Report(new DownloadProgressInfo(0, 0, percent, speed, eta, title, stage));
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
            return $"{bytesPerSecond / 1024 / 1024:0.0} MB/s";
        if (bytesPerSecond >= 1024)
            return $"{bytesPerSecond / 1024:0.0} KB/s";
        return $"{bytesPerSecond:0} B/s";
    }

    private static string FormatEta(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            return string.Empty;

        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        return $"{t.Minutes:00}:{t.Seconds:00}";
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
