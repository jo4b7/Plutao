namespace Plutao;

public sealed record ProfileInfo(
    string Platform,
    string Username,
    string DisplayName,
    string Biography,
    string AvatarUrl,
    long? Followers = null,
    long? Following = null,
    long? Posts = null,
    bool? IsVerified = null,
    bool? IsPrivate = null,
    string ExternalUrl = "")
{
    public int FoundVideos { get; init; }
}
