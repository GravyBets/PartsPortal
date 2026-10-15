namespace PartsPortal.Contracts.Access;

public sealed record UserAccessDto(
    int Id,
    string EmployeeId,
    string? DisplayName,
    bool IsActive,
    IReadOnlyList<string> Roles);
