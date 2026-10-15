namespace PartsPortal.Api.Security;

public static class AppRoleCodes
{
    public const string Admin = "ADMIN";
    public const string PartsManager = "PARTS_MANAGER";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Admin,
            PartsManager
        };
}
