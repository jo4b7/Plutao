namespace Plutao;

public sealed record MediaPreviewInfo(
    string Url,
    string Platform,
    string Id,
    string Title,
    string Creator,
    string Duration,
    string PublishedDate,
    string ThumbnailUrl,
    long? Views = null,
    long? Likes = null,
    int? Width = null,
    int? Height = null)
{
    public string Resolution => Width.HasValue && Height.HasValue && Width > 0 && Height > 0
        ? $"{Width}×{Height}"
        : string.Empty;
}
