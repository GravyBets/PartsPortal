using PartsPortal.Contracts.Catalog;
using PartsPortal.Models;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace PartsPortal.Services;

public sealed class CatalogLoadResult
{
    public CatalogLoadResult(
        IReadOnlyList<Part> parts,
        string source,
        string? warning = null)
    {
        Parts = parts;
        Source = source;
        Warning = warning;
    }

    public IReadOnlyList<Part> Parts { get; }
    public string Source { get; }
    public string? Warning { get; }
}

public class PartsCatalogService
{
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private static string CachePath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "PartsPortal",
                "Cache");

            return Path.Combine(directory, "parts-catalog.json");
        }
    }

    public async Task<CatalogLoadResult> LoadBestAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        Exception? apiError = null;

        try
        {
            var parts = await LoadFromApiAsync(cancellationToken);
            SaveCache(parts);

            return new CatalogLoadResult(parts, "API");
        }
        catch (Exception ex)
        {
            apiError = ex;
        }

        try
        {
            var cached = LoadFromCache();

            if (cached.Count > 0)
            {
                return new CatalogLoadResult(
                    cached,
                    "Local cache",
                    "PartsPortal could not reach the central catalog. Using the last cached copy.");
            }
        }
        catch
        {
            // A bad cache should not crash the client.
        }

        var detail = apiError?.Message;

        var warning = string.IsNullOrWhiteSpace(detail)
            ? "The central parts catalog is unavailable and no local cached catalog is available."
            : "The central parts catalog is unavailable and no local cached catalog is available.\n\n" + detail;

        return new CatalogLoadResult(
            Array.Empty<Part>(),
            "Unavailable",
            warning);
    }

    public async Task<List<Part>> LoadFromApiAsync(
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient
        {
            BaseAddress = AppConfiguration.ApiBaseUri,
            Timeout = AppConfiguration.ApiTimeout
        };

        var catalog = await client.GetFromJsonAsync<List<PartDto>>(
            "parts",
            cancellationToken)
            ?? new List<PartDto>();

        var parts = new List<Part>();

        foreach (var item in catalog.Where(x => x.IsActive))
        {
            foreach (var warehouse in item.Warehouses.Where(x => x.IsActive))
            {
                parts.Add(new Part
                {
                    Material = item.MaterialNumber?.Trim() ?? "",
                    Description = item.Description?.Trim() ?? "",
                    Warehouse = warehouse.Name?.Trim() ?? ""
                });
            }
        }

        return parts
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Material) ||
                !string.IsNullOrWhiteSpace(x.Description))
            .OrderBy(x => x.Description)
            .ThenBy(x => x.Material)
            .ThenBy(x => x.Warehouse)
            .ToList();
    }

    public List<Part> LoadFromCache()
    {
        if (!File.Exists(CachePath))
            return new List<Part>();

        var json = File.ReadAllText(CachePath);

        return JsonSerializer.Deserialize<List<Part>>(
                   json,
                   CacheJsonOptions)
               ?? new List<Part>();
    }

    private static void SaveCache(IReadOnlyList<Part> parts)
    {
        var directory = Path.GetDirectoryName(CachePath)!;
        Directory.CreateDirectory(directory);

        var tempPath = CachePath + ".tmp";
        var json = JsonSerializer.Serialize(parts, CacheJsonOptions);

        File.WriteAllText(tempPath, json);
        File.Move(tempPath, CachePath, overwrite: true);
    }
}
