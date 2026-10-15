namespace PartsPortal.Api.Data.Entities;

public sealed class AppRole
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";

    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
}
