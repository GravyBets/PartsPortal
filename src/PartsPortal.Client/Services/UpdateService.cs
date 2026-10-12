using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using PartsPortal.Contracts.Versioning;
using PartsPortal.Models;

namespace PartsPortal.Services;

public sealed class UpdateService
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = AppConfiguration.ApiBaseUri,
        Timeout = AppConfiguration.ApiTimeout
    };

    public async Task<UpdateCheckResult?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await Http.GetFromJsonAsync<ClientVersionInfoDto>(
                "system/client-version",
                cancellationToken);

            if (info is null ||
                !TryParseVersion(info.LatestVersion, out var latest) ||
                !TryParseVersion(info.MinimumSupportedVersion, out var minimum))
            {
                return null;
            }

            var current = GetCurrentVersion();

            return new UpdateCheckResult(
                current,
                latest,
                minimum,
                info.DownloadUrl ?? string.Empty,
                info.ReleaseNotes ?? Array.Empty<string>());
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static Version GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null)
            return new Version(0, 0, 0);

        return new Version(
            Math.Max(0, version.Major),
            Math.Max(0, version.Minor),
            Math.Max(0, version.Build));
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var core = value.Trim().Split(new[] { ' ', '+', '-' }, StringSplitOptions.RemoveEmptyEntries)[0];
        if (!Version.TryParse(core, out var parsed) || parsed is null)
            return false;

        version = parsed;
        return true;
    }
}
