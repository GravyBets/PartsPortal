using PartsPortal.Contracts.Catalog;
using PartsPortal.Contracts.Templates;
using PartsPortal.Models;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace PartsPortal.Services;

public sealed record TemplateLoadResult(
    IReadOnlyList<PartTemplate> Templates,
    string Source,
    string? Warning = null);

public sealed class PersonalTemplatesService
{
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly HttpClient _client;

    public PersonalTemplatesService()
    {
        _client = new HttpClient
        {
            BaseAddress = AppConfiguration.ApiBaseUri,
            Timeout = AppConfiguration.ApiTimeout
        };

        _client.DefaultRequestHeaders.Add(
            "X-PartsPortal-User",
            Environment.UserName);
    }

    public async Task<TemplateLoadResult> LoadBestAvailableAsync(
        string warehouseCode,
        CancellationToken cancellationToken = default)
    {
        Exception? apiError = null;

        try
        {
            var templates = await LoadFromApiAsync(
                warehouseCode,
                includeInactive: false,
                cancellationToken);

            SaveCache(warehouseCode, templates);

            return new TemplateLoadResult(
                templates,
                "API");
        }
        catch (Exception ex) when (
            ex is HttpRequestException ||
            ex is TaskCanceledException ||
            ex is JsonException ||
            ex is InvalidOperationException)
        {
            apiError = ex;
        }

        try
        {
            var cached = LoadFromCache(warehouseCode);

            if (cached.Count > 0)
            {
                return new TemplateLoadResult(
                    cached,
                    "Local cache",
                    "PartsPortal could not reach your personal templates. Using the last cached copy.");
            }
        }
        catch
        {
            // A bad cache should never crash the ordering screen.
        }

        var warning = apiError is null
            ? "Personal templates are unavailable and no local cached copy exists."
            : "Personal templates are unavailable and no local cached copy exists.\n\n" +
              apiError.Message;

        return new TemplateLoadResult(
            Array.Empty<PartTemplate>(),
            "Unavailable",
            warning);
    }

    public async Task<PartTemplate> SaveAsync(
        string warehouseCode,
        PartTemplate template,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        var warehouse = await ResolveWarehouseAsync(
            warehouseCode,
            cancellationToken);

        var request = new SaveTemplateRequest(
            template.Name.Trim(),
            warehouse.Id,
            template.Lines
                .Select((line, index) => new TemplateLineRequest(
                    line.Material,
                    line.Qty,
                    index))
                .ToList());

        var allTemplates = await LoadFromApiAsync(
            warehouseCode,
            includeInactive: true,
            cancellationToken);

        var existing = allTemplates.FirstOrDefault(x =>
            x.Id == template.Id && template.Id > 0);

        existing ??= allTemplates.FirstOrDefault(x =>
            string.Equals(
                x.Name,
                template.Name,
                StringComparison.OrdinalIgnoreCase));

        HttpResponseMessage response;

        if (existing is null)
        {
            response = await _client.PostAsJsonAsync(
                "templates",
                request,
                cancellationToken);
        }
        else
        {
            if (!existing.IsActive)
            {
                using var reactivateResponse = await _client.PostAsync(
                    $"templates/{existing.Id}/reactivate",
                    content: null,
                    cancellationToken);

                await EnsureSuccessAsync(
                    reactivateResponse,
                    cancellationToken);
            }

            response = await _client.PutAsJsonAsync(
                $"templates/{existing.Id}",
                request,
                cancellationToken);
        }

        using (response)
        {
            await EnsureSuccessAsync(response, cancellationToken);

            var savedDto = await response.Content.ReadFromJsonAsync<PartTemplateDto>(
                cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    "PartsPortal API returned an empty template response.");

            var saved = ToClientTemplate(savedDto);

            await RefreshCacheAsync(
                warehouseCode,
                cancellationToken);

            return saved;
        }
    }

    public async Task DeleteAsync(
        string warehouseCode,
        int templateId,
        CancellationToken cancellationToken = default)
    {
        if (templateId <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(templateId),
                "Template ID must be greater than zero.");

        using var response = await _client.DeleteAsync(
            $"templates/{templateId}",
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        await RefreshCacheAsync(
            warehouseCode,
            cancellationToken);
    }

    private async Task<List<PartTemplate>> LoadFromApiAsync(
        string warehouseCode,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var encodedWarehouse = Uri.EscapeDataString(
            warehouseCode.Trim());

        var path =
            $"templates?warehouse={encodedWarehouse}&includeInactive={includeInactive.ToString().ToLowerInvariant()}";

        using var response = await _client.GetAsync(
            path,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var dtos = await response.Content.ReadFromJsonAsync<List<PartTemplateDto>>(
            cancellationToken: cancellationToken)
            ?? new List<PartTemplateDto>();

        return dtos
            .Select(ToClientTemplate)
            .OrderBy(x => x.Name)
            .ToList();
    }

    private async Task<WarehouseDto> ResolveWarehouseAsync(
        string warehouseCode,
        CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(
            "warehouses",
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var warehouses = await response.Content.ReadFromJsonAsync<List<WarehouseDto>>(
            cancellationToken: cancellationToken)
            ?? new List<WarehouseDto>();

        var warehouse = warehouses.FirstOrDefault(x =>
            string.Equals(
                x.Code,
                warehouseCode,
                StringComparison.OrdinalIgnoreCase));

        return warehouse
            ?? throw new InvalidOperationException(
                $"Warehouse '{warehouseCode}' was not returned by the PartsPortal API.");
    }

    private async Task RefreshCacheAsync(
        string warehouseCode,
        CancellationToken cancellationToken)
    {
        var templates = await LoadFromApiAsync(
            warehouseCode,
            includeInactive: false,
            cancellationToken);

        SaveCache(warehouseCode, templates);
    }

    private static PartTemplate ToClientTemplate(
        PartTemplateDto dto)
        => new()
        {
            Id = dto.Id,
            Name = dto.Name,
            IsActive = dto.IsActive,
            WarehouseId = dto.Warehouse.Id,
            Lines = dto.Lines
                .OrderBy(x => x.SortOrder)
                .Select(x => new TemplateLine
                {
                    Material = x.MaterialNumber,
                    Description = x.Description,
                    Qty = x.Quantity
                })
                .ToList()
        };

    private static string CachePath(
        string warehouseCode)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "PartsPortal",
            "Cache");

        Directory.CreateDirectory(directory);

        var employeeId = SanitizeFileToken(Environment.UserName);
        var warehouse = SanitizeFileToken(warehouseCode);

        return Path.Combine(
            directory,
            $"templates_{employeeId}_{warehouse}.json");
    }

    private static void SaveCache(
        string warehouseCode,
        IReadOnlyList<PartTemplate> templates)
    {
        var path = CachePath(warehouseCode);
        var tempPath = path + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(
                templates,
                CacheJsonOptions);

            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Ignore cache cleanup failures.
            }
        }
    }

    private static List<PartTemplate> LoadFromCache(
        string warehouseCode)
    {
        var path = CachePath(warehouseCode);

        if (!File.Exists(path))
            return new List<PartTemplate>();

        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<List<PartTemplate>>(
                   json,
                   CacheJsonOptions)
               ?? new List<PartTemplate>();
    }

    private static string SanitizeFileToken(
        string value)
    {
        var result = value.Trim();

        foreach (var invalid in Path.GetInvalidFileNameChars())
            result = result.Replace(invalid, '_');

        return result.Replace(' ', '_').ToLowerInvariant();
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (string.IsNullOrWhiteSpace(message))
            message = $"{(int)response.StatusCode} {response.ReasonPhrase}";

        throw new InvalidOperationException(
            message.Trim('"'));
    }
}
