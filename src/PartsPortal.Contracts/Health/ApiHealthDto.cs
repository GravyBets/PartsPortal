namespace PartsPortal.Contracts.Health;

public sealed record ApiHealthDto(
    string Application,
    string Status,
    DateTimeOffset ServerTimeUtc,
    string DatabaseStatus);
