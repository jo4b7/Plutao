using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Plutao;

internal static class AppCache
{
    private sealed class PreviewCacheFile
    {
        public PreviewCacheFile() { }
        public Dictionary<string, PreviewCacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PreviewCacheEntry
    {
        public PreviewCacheEntry() { }
        public DateTime CachedAtUtc { get; set; }
        public MediaPreviewInfo? Preview { get; set; }
    }

    private sealed class DownloadCacheFile
    {
        public DownloadCacheFile() { }
        public Dictionary<string, DownloadCacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class DownloadCacheEntry
    {
        public DownloadCacheEntry() { }
        public DateTime CachedAtUtc { get; set; }
        public string Path { get; set; } = string.Empty;
    }

    private sealed class CollectionCacheFile
    {
        public CollectionCacheFile() { }
        public Dictionary<string, CollectionCacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CollectionCacheEntry
    {
        public CollectionCacheEntry() { }
        public DateTime CachedAtUtc { get; set; }
        public ProfileInfo? Profile { get; set; }
        public List<CollectionMediaItem> Items { get; set; } = new();
    }

    private static readonly object PreviewLock = new();
    private static readonly object DownloadLock = new();
    private static readonly object CollectionLock = new();
    private static PreviewCacheFile? _previewCache;
    private static DownloadCacheFile? _downloadCache;
    private static CollectionCacheFile? _collectionCache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string BaseDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Plutao",
        "cache");

    public static string ThumbnailDirectory { get; } = Path.Combine(BaseDirectory, "thumbs");
    public static string RenderedThumbnailDirectory { get; } = Path.Combine(ThumbnailDirectory, "rendered");
    public static string DataDirectory { get; } = Path.Combine(BaseDirectory, "data");

    private static string PreviewPath => Path.Combine(DataDirectory, "media-preview.json");
    private static string DownloadPath => Path.Combine(DataDirectory, "download-reuse.json");
    private static string CollectionPath => Path.Combine(DataDirectory, "collections.json");
    private const long MaxThumbnailBytes = 400L * 1024 * 1024;

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(ThumbnailDirectory);
            Directory.CreateDirectory(RenderedThumbnailDirectory);
            Directory.CreateDirectory(DataDirectory);
            MigrateLegacyThumbnailCache();
            _ = Task.Run(TrimThumbnailCache);
        }
        catch
        {
            // Cache nunca deve impedir o aplicativo de abrir.
        }
    }

    public static string NormalizeUrl(string url)
    {
        url = (url ?? string.Empty).Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.TrimEnd('/');

        if (host.Contains("youtu.be"))
        {
            var id = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(id))
                return $"https://www.youtube.com/watch?v={id}";
        }

        if (host.Contains("youtube.com"))
        {
            if (path.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                var query = ParseQuery(uri.Query);
                if (query.TryGetValue("v", out var id) && !string.IsNullOrWhiteSpace(id))
                    return $"https://www.youtube.com/watch?v={id}";
            }

            foreach (var prefix in new[] { "/shorts/", "/live/" })
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var id = path[prefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(id))
                        return $"https://www.youtube.com/watch?v={id}";
                }
            }
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
            Path = string.IsNullOrWhiteSpace(path) ? "/" : path
        };
        return builder.Uri.ToString().TrimEnd('/');
    }

    public static bool TryGetPreview(string url, TimeSpan maxAge, out MediaPreviewInfo? preview)
    {
        preview = null;
        var key = NormalizeUrl(url);
        if (string.IsNullOrWhiteSpace(key)) return false;

        lock (PreviewLock)
        {
            var cache = LoadPreviewCache();
            if (!cache.Entries.TryGetValue(key, out var entry) || entry.Preview is null)
                return false;
            if (DateTime.UtcNow - entry.CachedAtUtc > maxAge)
                return false;

            // A v0.5.3 passou a guardar as alturas realmente disponíveis.
            // Pré-visualizações antigas com resolução conhecida, mas sem a lista
            // de formatos, são atualizadas uma vez para evitar opções inválidas.
            if (entry.Preview.Height is > 0 &&
                (entry.Preview.AvailableHeights is null || entry.Preview.AvailableHeights.Length == 0))
                return false;

            preview = entry.Preview;
            return true;
        }
    }

    public static void StorePreview(string url, MediaPreviewInfo preview)
    {
        var key = NormalizeUrl(url);
        if (string.IsNullOrWhiteSpace(key)) return;

        lock (PreviewLock)
        {
            var cache = LoadPreviewCache();
            cache.Entries[key] = new PreviewCacheEntry
            {
                CachedAtUtc = DateTime.UtcNow,
                Preview = preview
            };
            PruneOldPreviewEntries(cache);
            SaveJsonAtomic(PreviewPath, cache);
        }
    }

    public static bool TryGetCollection(
        string url,
        string browserCookies,
        int limit,
        TimeSpan maxAge,
        out IReadOnlyList<CollectionMediaItem> items,
        out ProfileInfo? profile)
    {
        items = Array.Empty<CollectionMediaItem>();
        profile = null;
        var key = BuildCollectionKey(url, browserCookies, limit);

        lock (CollectionLock)
        {
            var cache = LoadCollectionCache();
            if (!cache.Entries.TryGetValue(key, out var entry))
                return false;
            if (DateTime.UtcNow - entry.CachedAtUtc > maxAge || entry.Items.Count == 0)
                return false;

            items = entry.Items.ToArray();
            profile = entry.Profile;
            return true;
        }
    }

    public static void StoreCollection(
        string url,
        string browserCookies,
        int limit,
        IReadOnlyList<CollectionMediaItem> items,
        ProfileInfo? profile)
    {
        if (items.Count == 0) return;
        var key = BuildCollectionKey(url, browserCookies, limit);

        lock (CollectionLock)
        {
            var cache = LoadCollectionCache();
            cache.Entries[key] = new CollectionCacheEntry
            {
                CachedAtUtc = DateTime.UtcNow,
                Profile = profile,
                Items = items.ToList()
            };

            foreach (var stale in cache.Entries
                         .Where(x => DateTime.UtcNow - x.Value.CachedAtUtc > TimeSpan.FromDays(14))
                         .Select(x => x.Key)
                         .ToArray())
            {
                cache.Entries.Remove(stale);
            }

            SaveJsonAtomic(CollectionPath, cache);
        }
    }

    private static string BuildCollectionKey(string url, string browserCookies, int limit)
        => $"collection-v054-instagram-meta1-tiktok-gallery1-youtube-tabs1|{NormalizeUrl(url)}|{browserCookies.Trim().ToLowerInvariant()}|{limit}";

    public static string BuildDownloadKey(string url, DownloadOptions options)
    {
        var raw = string.Join("|", new[]
        {
            "download-cache-v052-finalpath1",
            NormalizeUrl(url),
            options.Mode.ToString(),
            options.Quality,
            options.VideoContainer,
            options.AudioFormat,
            options.PreferCompatibleMp4.ToString(),
            options.EmbedMetadata.ToString(),
            options.SaveThumbnail.ToString(),
            options.OrganizeByCreator.ToString(),
            Path.GetFullPath(options.OutputDirectory).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public static bool TryGetReusableDownload(string key, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;

        lock (DownloadLock)
        {
            var cache = LoadDownloadCache();
            if (!cache.Entries.TryGetValue(key, out var entry) || string.IsNullOrWhiteSpace(entry.Path))
                return false;

            if (!File.Exists(entry.Path))
            {
                cache.Entries.Remove(key);
                SaveJsonAtomic(DownloadPath, cache);
                return false;
            }

            path = entry.Path;
            return true;
        }
    }

    public static void InvalidateReusableDownload(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        lock (DownloadLock)
        {
            var cache = LoadDownloadCache();
            if (cache.Entries.Remove(key))
                SaveJsonAtomic(DownloadPath, cache);
        }
    }

    public static void StoreReusableDownload(string key, string path)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        lock (DownloadLock)
        {
            var cache = LoadDownloadCache();
            cache.Entries[key] = new DownloadCacheEntry
            {
                CachedAtUtc = DateTime.UtcNow,
                Path = path
            };

            foreach (var stale in cache.Entries
                         .Where(x => DateTime.UtcNow - x.Value.CachedAtUtc > TimeSpan.FromDays(90) || !File.Exists(x.Value.Path))
                         .Select(x => x.Key)
                         .ToArray())
            {
                cache.Entries.Remove(stale);
            }

            SaveJsonAtomic(DownloadPath, cache);
        }
    }

    public static string CreateDuplicatePath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var candidate = Path.Combine(directory, $"{name} [{stamp}]{extension}");
        var counter = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{name} [{stamp}-{counter}]{extension}");
            counter++;
        }
        return candidate;
    }

    public static void TrimThumbnailCache()
    {
        try
        {
            if (!Directory.Exists(ThumbnailDirectory)) return;

            var files = Directory.EnumerateFiles(ThumbnailDirectory, "*", SearchOption.AllDirectories)
                .Select(path =>
                {
                    try
                    {
                        var info = new FileInfo(path);
                        return (Info: info, Length: info.Length, Stamp: info.LastAccessTimeUtc > info.LastWriteTimeUtc ? info.LastAccessTimeUtc : info.LastWriteTimeUtc);
                    }
                    catch
                    {
                        return (Info: (FileInfo?)null, Length: 0L, Stamp: DateTime.MaxValue);
                    }
                })
                .Where(x => x.Info is not null)
                .ToList();

            var total = files.Sum(x => x.Length);
            if (total <= MaxThumbnailBytes) return;

            var target = (long)(MaxThumbnailBytes * 0.85);
            foreach (var file in files.OrderBy(x => x.Stamp))
            {
                if (total <= target) break;
                try
                {
                    File.Delete(file.Info!.FullName);
                    total -= file.Length;
                }
                catch { }
            }
        }
        catch { }
    }

    private static PreviewCacheFile LoadPreviewCache()
    {
        if (_previewCache is not null) return _previewCache;
        _previewCache = LoadJson<PreviewCacheFile>(PreviewPath) ?? new PreviewCacheFile();
        _previewCache.Entries ??= new Dictionary<string, PreviewCacheEntry>(StringComparer.OrdinalIgnoreCase);
        return _previewCache;
    }

    private static DownloadCacheFile LoadDownloadCache()
    {
        if (_downloadCache is not null) return _downloadCache;
        _downloadCache = LoadJson<DownloadCacheFile>(DownloadPath) ?? new DownloadCacheFile();
        _downloadCache.Entries ??= new Dictionary<string, DownloadCacheEntry>(StringComparer.OrdinalIgnoreCase);
        return _downloadCache;
    }

    private static CollectionCacheFile LoadCollectionCache()
    {
        if (_collectionCache is not null) return _collectionCache;
        _collectionCache = LoadJson<CollectionCacheFile>(CollectionPath) ?? new CollectionCacheFile();
        _collectionCache.Entries ??= new Dictionary<string, CollectionCacheEntry>(StringComparer.OrdinalIgnoreCase);
        return _collectionCache;
    }

    private static T? LoadJson<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static void SaveJsonAtomic<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temp, path, true);
        }
        catch { }
    }

    private static void PruneOldPreviewEntries(PreviewCacheFile cache)
    {
        foreach (var key in cache.Entries
                     .Where(x => DateTime.UtcNow - x.Value.CachedAtUtc > TimeSpan.FromDays(30))
                     .Select(x => x.Key)
                     .ToArray())
        {
            cache.Entries.Remove(key);
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            result[key] = value;
        }
        return result;
    }

    private static void MigrateLegacyThumbnailCache()
    {
        try
        {
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Plutao",
                "thumb-cache");
            if (!Directory.Exists(legacy) || Directory.EnumerateFileSystemEntries(legacy).Take(1).Any() == false)
                return;

            Directory.CreateDirectory(ThumbnailDirectory);
            foreach (var file in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var relative = Path.GetRelativePath(legacy, file);
                    var destination = Path.Combine(ThumbnailDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (!File.Exists(destination))
                        File.Copy(file, destination, false);
                }
                catch { }
            }

            // Depois da migração, remove o cache antigo para não ocupar espaço em dobro.
            try { Directory.Delete(legacy, true); } catch { }
        }
        catch { }
    }
}
