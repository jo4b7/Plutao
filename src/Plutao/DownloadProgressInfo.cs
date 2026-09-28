namespace Plutao;

public sealed record DownloadProgressInfo(
    int ItemIndex,
    int ItemCount,
    double Percent,
    string Speed,
    string Eta,
    string Title,
    string Stage)
{
    public double OverallPercent
        => ItemCount <= 0
            ? Percent
            : Math.Clamp(((ItemIndex - 1) + (Percent / 100.0)) / ItemCount * 100.0, 0, 100);
}
