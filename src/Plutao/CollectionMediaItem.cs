namespace Plutao;

public sealed record CollectionMediaItem(
    int Index,
    string Id,
    string Title,
    string Duration,
    string Url = "",
    string ThumbnailUrl = "",
    string Date = "",
    string Details = "",
    string Category = "")
{
    public string DisplayText
    {
        get
        {
            var extra = new List<string>();
            if (!string.IsNullOrWhiteSpace(Category)) extra.Add(Category);
            if (!string.IsNullOrWhiteSpace(Duration)) extra.Add(Duration);
            if (!string.IsNullOrWhiteSpace(Date)) extra.Add(Date);
            if (!string.IsNullOrWhiteSpace(Details)) extra.Add(Details);
            return extra.Count == 0
                ? $"{Index:000}  —  {Title}"
                : $"{Index:000}  —  {Title}  [{string.Join(" • ", extra)}]";
        }
    }
}
