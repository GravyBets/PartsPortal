namespace PartsPortal.Services;

public static class AppConfiguration
{
    private const string DefaultApiBaseUrl =
        "http://10.130.206.135/partsportal/api/";

    public static Uri ApiBaseUri { get; } = ResolveApiBaseUri();

    public static TimeSpan ApiTimeout { get; } = TimeSpan.FromSeconds(4);

    private static Uri ResolveApiBaseUri()
    {
        var overrideUrl = Environment.GetEnvironmentVariable(
            "PARTSPORTAL_API_BASE_URL");

        var value = string.IsNullOrWhiteSpace(overrideUrl)
            ? DefaultApiBaseUrl
            : overrideUrl.Trim();

        if (!value.EndsWith("/", StringComparison.Ordinal))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }
}
