namespace Plutao;

public enum DownloadMode
{
    Video,
    Audio
}

public enum ExistingFileBehavior
{
    KeepExisting,
    Replace,
    KeepBoth
}

public sealed class DownloadOptions
{
    public DownloadMode Mode { get; set; } = DownloadMode.Video;
    public string Quality { get; set; } = "1080p";
    public string VideoContainer { get; set; } = "mp4";
    public string AudioFormat { get; set; } = "mp3";
    public string OutputDirectory { get; set; } = "";
    public string TemporaryDirectory { get; set; } = "";
    public string BrowserCookies { get; set; } = "Nenhum";
    public bool AllowPlaylists { get; set; }
    public bool EmbedMetadata { get; set; } = true;
    public bool SaveThumbnail { get; set; }
    public bool SaveInfoJson { get; set; }
    public bool UseArchive { get; set; }
    public bool OrganizeByCreator { get; set; }
    public bool PreferCompatibleMp4 { get; set; } = true;
    public ExistingFileBehavior ExistingFileBehavior { get; set; } = ExistingFileBehavior.KeepBoth;
}
