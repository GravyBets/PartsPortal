namespace PartsPortal.Services;

public static class AppConfiguration
{
    public static Uri ApiBaseUri { get; } =
        new("http://10.130.206.135/partsportal/api/");

    public static TimeSpan ApiTimeout { get; } = TimeSpan.FromSeconds(4);
}
