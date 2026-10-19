using PartsPortal.Contracts.Access;
using System.Net.Http;
using System.Net.Http.Json;

namespace PartsPortal.Services;

public sealed class AccessApiService
{
    private readonly HttpClient _client;

    public AccessApiService()
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

    public async Task<CurrentUserAccessDto?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync(
            "access/me",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<CurrentUserAccessDto>(
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<UserAccessDto>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync(
            "access/users",
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<List<UserAccessDto>>(
                   cancellationToken: cancellationToken)
               ?? new List<UserAccessDto>();
    }

    public async Task DeleteUserAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        var id = Uri.EscapeDataString(employeeId);

        using var response = await _client.DeleteAsync(
            $"access/users/{id}",
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<UserAccessDto> SaveUserAsync(
        SaveUserAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var employeeId = Uri.EscapeDataString(request.EmployeeId);

        using var response = await _client.PutAsJsonAsync(
            $"access/users/{employeeId}",
            request,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<UserAccessDto>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   "PartsPortal API returned an empty user access response.");
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

        throw new InvalidOperationException(message.Trim('"'));
    }
}
