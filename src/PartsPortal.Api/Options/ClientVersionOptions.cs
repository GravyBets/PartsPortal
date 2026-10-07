namespace PartsPortal.Api.Options;

public sealed class ClientVersionOptions
{
    public const string SectionName = "ClientVersion";

    public string LatestVersion { get; set; } = "";
    public string MinimumSupportedVersion { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string[] ReleaseNotes { get; set; } = Array.Empty<string>();
}
