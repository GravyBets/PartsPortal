using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PartsPortal.Api.Data;
using PartsPortal.Contracts.Catalog;

namespace PartsPortal.Api.Controllers;

[ApiController]
[Route("api/warehouses")]
public sealed class WarehousesController : ControllerBase
{
    private readonly PartsPortalDbContext _db;

    public WarehousesController(PartsPortalDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WarehouseDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WarehouseDto>>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Warehouses.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        var warehouses = await query
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new WarehouseDto(
                x.Id,
                x.Code,
                x.Name,
                x.Subtitle,
                x.IsActive,
                x.SortOrder))
            .ToListAsync(cancellationToken);

        return Ok(warehouses);
    }
}
