using PartsPortal.Contracts.Catalog;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;

namespace PartsPortal.Services;

public sealed class PartsAdminService
{
    private readonly HttpClient _client;

    public PartsAdminService()
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

    public async Task<IReadOnlyList<PartDto>> GetPartsAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var path = includeInactive
            ? "parts?includeInactive=true"
            : "parts";

        return await _client.GetFromJsonAsync<List<PartDto>>(
                   path,
                   cancellationToken)
               ?? new List<PartDto>();
    }

    public async Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _client.GetFromJsonAsync<List<WarehouseDto>>(
                   "warehouses",
                   cancellationToken)
               ?? new List<WarehouseDto>();
    }

    public async Task<PartDto> CreatePartAsync(
        UpdatePartRequest request,
        CancellationToken cancellationToken = default)
    {
        var create = new CreatePartRequest(
            request.MaterialNumber,
            request.Description,
            request.WarehouseIds,
            request.ChangedBy);

        using var response = await _client.PostAsJsonAsync(
            "parts",
            create,
            cancellationToken);

        return await ReadPartResponseAsync(response, cancellationToken);
    }

    public async Task<PartDto> UpdatePartAsync(
        int id,
        UpdatePartRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await _client.PutAsJsonAsync(
            $"parts/{id}",
            request,
            cancellationToken);

        return await ReadPartResponseAsync(response, cancellationToken);
    }

    public async Task DeactivatePartAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var changedBy = Uri.EscapeDataString(Environment.UserName);

        using var response = await _client.DeleteAsync(
            $"parts/{id}?changedBy={changedBy}",
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeletePartPermanentlyAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        using var response = await _client.DeleteAsync(
            $"parts/{id}/permanent",
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<PartDto> ReactivatePartAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var changedBy = Uri.EscapeDataString(Environment.UserName);

        using var response = await _client.PostAsync(
            $"parts/{id}/reactivate?changedBy={changedBy}",
            content: null,
            cancellationToken);

        return await ReadPartResponseAsync(response, cancellationToken);
    }

    private static async Task<PartDto> ReadPartResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<PartDto>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The PartsPortal API returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(message))
            message = $"{(int)response.StatusCode} {response.ReasonPhrase}";

        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException(message.Trim('"'));

        throw new HttpRequestException(message.Trim('"'), null, response.StatusCode);
    }
}
