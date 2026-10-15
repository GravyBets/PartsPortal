namespace PartsPortal.Contracts.Access;

public sealed record SaveUserAccessRequest(
    string EmployeeId,
    string? DisplayName,
    bool IsActive,
    IReadOnlyList<string> Roles);
