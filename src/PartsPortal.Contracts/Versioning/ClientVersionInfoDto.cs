namespace PartsPortal.Contracts.Versioning;

public sealed record ClientVersionInfoDto(
    string LatestVersion,
    string MinimumSupportedVersion,
    string DownloadUrl,
    IReadOnlyList<string> ReleaseNotes);
