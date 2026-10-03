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
            var innerFraction = Math.Clamp(Percent / 100.0, 0, 1);

            if (CollectionIndex > 0 && CollectionCount > 0)
                innerFraction = Math.Clamp(((CollectionIndex - 1) + innerFraction) / CollectionCount, 0, 1);

            if (ItemIndex > 0 && ItemCount > 0)
                return Math.Clamp(((ItemIndex - 1) + innerFraction) / ItemCount * 100.0, 0, 100);

            return Math.Clamp(innerFraction * 100.0, 0, 100);
        }
    }
}
