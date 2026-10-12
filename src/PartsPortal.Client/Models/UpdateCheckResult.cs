namespace PartsPortal.Models;

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    Version MinimumSupportedVersion,
    string DownloadUrl,
    IReadOnlyList<string> ReleaseNotes)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
    public bool IsUpdateRequired => CurrentVersion < MinimumSupportedVersion;
}
