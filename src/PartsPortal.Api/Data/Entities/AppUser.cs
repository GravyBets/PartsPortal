namespace PartsPortal.Api.Data.Entities;

public sealed class AppUser
{
    public int Id { get; set; }
    public string EmployeeId { get; set; } = "";
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
}
