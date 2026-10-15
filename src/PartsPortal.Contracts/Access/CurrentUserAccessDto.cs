namespace PartsPortal.Contracts.Access;

public sealed record CurrentUserAccessDto(
    string EmployeeId,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    bool CanManageParts,
    bool CanManageUsers,
    bool BootstrapAvailable);
