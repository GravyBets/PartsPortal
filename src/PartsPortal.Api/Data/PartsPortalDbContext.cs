using Microsoft.EntityFrameworkCore;

namespace PartsPortal.Api.Data;

public sealed class PartsPortalDbContext : DbContext
{
    public PartsPortalDbContext(DbContextOptions<PartsPortalDbContext> options)
        : base(options)
    {
    }
}
