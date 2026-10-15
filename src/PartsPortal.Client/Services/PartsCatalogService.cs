using Microsoft.VisualBasic.FileIO;
using PartsPortal.Contracts.Catalog;
using PartsPortal.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace PartsPortal.Services;

public sealed class CatalogLoadResult
{
    public CatalogLoadResult(IReadOnlyList<Part> parts, string source, string? warning = null)
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
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PartsPortal",
                "Cache");

            return Path.Combine(directory, "parts-catalog.json");
        }
    }

    public async Task<CatalogLoadResult> LoadBestAvailableAsync(
        string? csvFallbackPath,
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
            // Continue to the legacy CSV fallback.
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(csvFallbackPath) &&
                File.Exists(csvFallbackPath))
            {
                var csvParts = LoadFromCsv(csvFallbackPath);

                if (csvParts.Count > 0)
                {
                    return new CatalogLoadResult(
                        csvParts,
                        "CSV fallback",
                        "PartsPortal could not reach the central catalog or local cache. Using the legacy CSV.");
                }
            }
        }
        catch
        {
            // Fall through to unavailable result.
        }

        var detail = apiError?.Message;
        var warning = string.IsNullOrWhiteSpace(detail)
            ? "The central parts catalog is unavailable and no local fallback could be loaded."
            : $"The central parts catalog is unavailable and no local fallback could be loaded.\n\n{detail}";

        return new CatalogLoadResult(Array.Empty<Part>(), "Unavailable", warning);
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
            cancellationToken) ?? new List<PartDto>();

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

        return JsonSerializer.Deserialize<List<Part>>(json, CacheJsonOptions)
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

    public List<Part> LoadFromCsv(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("CSV path is empty.");

        if (!File.Exists(path))
            throw new FileNotFoundException("CSV/TSV file not found.", path);

        var delimiter = DetectDelimiter(path);
        var parts = new List<Part>();

        using var parser = new TextFieldParser(path);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(delimiter);
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;

        string[]? header = ReadNonEmptyFields(parser);
        if (header == null)
            return parts;

        var headerCols = header.Select(h => (h ?? "").Trim()).ToArray();

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerCols.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(headerCols[i]) && !map.ContainsKey(headerCols[i]))
                map[headerCols[i]] = i;
        }

        if (!map.ContainsKey("Description") ||
            !map.ContainsKey("Material") ||
            !map.ContainsKey("Warehouse"))
        {
            var found = string.Join(", ", headerCols.Where(h => !string.IsNullOrWhiteSpace(h)));

            throw new Exception(
                "CSV headers must include: Description, Material, Warehouse.\n" +
                $"Found: {found}");
        }

        int descIdx = map["Description"];
        int matIdx = map["Material"];
        int whIdx = map["Warehouse"];

        while (!parser.EndOfData)
        {
            string[]? fields;

            try
            {
                fields = parser.ReadFields();
            }
            catch (MalformedLineException)
            {
                continue;
            }

            if (fields == null || fields.All(string.IsNullOrWhiteSpace))
                continue;

            string desc = Get(fields, descIdx);
            string mat = Get(fields, matIdx);
            string wh = Get(fields, whIdx);

            if (string.IsNullOrWhiteSpace(mat) && string.IsNullOrWhiteSpace(desc))
                continue;

            parts.Add(new Part
            {
                Description = desc,
                Material = mat,
                Warehouse = wh
            });
        }

        return parts;
    }

    private static string Get(string[] cols, int idx)
        => idx >= 0 && idx < cols.Length
            ? (cols[idx] ?? "").Trim()
            : "";

    private static string DetectDelimiter(string path)
    {
        using var reader = new StreamReader(path);

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            return line.Contains('\t')
                ? "\t"
                : ",";
        }

        return ",";
    }

    private static string[]? ReadNonEmptyFields(TextFieldParser parser)
    {
        while (!parser.EndOfData)
        {
            string[]? fields;

            try
            {
                fields = parser.ReadFields();
            }
            catch (MalformedLineException)
            {
                continue;
            }

            if (fields == null || fields.All(string.IsNullOrWhiteSpace))
                continue;

            return fields;
        }

        return null;
    }
}
