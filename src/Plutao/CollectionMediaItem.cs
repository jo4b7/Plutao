namespace Plutao;

public sealed record CollectionMediaItem(int Index, string Id, string Title, string Duration, string Url = "")
{
    public string DisplayText
        => string.IsNullOrWhiteSpace(Duration)
            ? $"{Index:000}  —  {Title}"
            : $"{Index:000}  —  {Title}  [{Duration}]";
}
