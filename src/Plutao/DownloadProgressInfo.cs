namespace Plutao;

public sealed record DownloadProgressInfo(
    int ItemIndex,
    int ItemCount,
    double Percent,
    string Speed,
    string Eta,
    string Title,
    string Stage,
    int CollectionIndex = 0,
    int CollectionCount = 0)
{
    public double OverallPercent
    {
        get
        {
            if (CollectionIndex > 0 && CollectionCount > 0)
                return Math.Clamp(((CollectionIndex - 1) + (Percent / 100.0)) / CollectionCount * 100.0, 0, 100);

            return ItemCount <= 0
                ? Percent
                : Math.Clamp(((ItemIndex - 1) + (Percent / 100.0)) / ItemCount * 100.0, 0, 100);
        }
    }
}
