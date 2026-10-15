namespace PartsPortal.Api.Data.Entities;

public sealed class AppUserRole
{
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public int RoleId { get; set; }
    public AppRole Role { get; set; } = null!;
}
