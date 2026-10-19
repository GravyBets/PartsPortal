using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PartsPortal.Api.Data;
using PartsPortal.Api.Data.Entities;
using PartsPortal.Api.Security;
using PartsPortal.Contracts.Catalog;

namespace PartsPortal.Api.Controllers;

[ApiController]
[Route("api/parts")]
public sealed class PartsController : ControllerBase
{
    private readonly PartsPortalDbContext _db;
    private readonly AccessRepository _access;

    public PartsController(
        PartsPortalDbContext db,
        AccessRepository access)
    {
        _db = db;
        _access = access;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PartDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PartDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] string? warehouse = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = CatalogQuery();

        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            query = query.Where(x =>
                x.MaterialNumber.Contains(term) ||
                x.Description.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(warehouse))
        {
            var warehouseTerm = warehouse.Trim();

            query = query.Where(x =>
                x.PartWarehouses.Any(pw =>
                    pw.Warehouse.Code == warehouseTerm ||
                    pw.Warehouse.Name == warehouseTerm));
        }

        var parts = await query
            .OrderBy(x => x.Description)
            .ThenBy(x => x.MaterialNumber)
            .ToListAsync(cancellationToken);

        return Ok(parts.Select(ToDto).ToList());
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<PartDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PartDto>> GetById(
        int id,
        CancellationToken cancellationToken = default)
    {
        var part = await CatalogQuery()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return part is null
            ? NotFound()
            : Ok(ToDto(part));
    }

    [HttpPost]
    [ProducesResponseType<PartDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PartDto>> Create(
        CreatePartRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequirePartsManagerAsync(cancellationToken);

        if (actor is null)
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Parts management permission is required.");

        var validation = ValidateRequest(
            request.MaterialNumber,
            request.Description,
            request.WarehouseIds);

        if (validation is not null)
        {
            return BadRequest(validation);
        }

        var material = request.MaterialNumber.Trim();
        var description = request.Description.Trim();

        var duplicate = await _db.Parts
            .AnyAsync(x => x.MaterialNumber == material, cancellationToken);

        if (duplicate)
        {
            return Conflict($"Material number '{material}' already exists.");
        }

        var warehouseIds = request.WarehouseIds
            .Distinct()
            .ToArray();

        var warehouses = await _db.Warehouses
            .Where(x => warehouseIds.Contains(x.Id) && x.IsActive)
            .ToListAsync(cancellationToken);

        if (warehouses.Count != warehouseIds.Length)
        {
            return BadRequest("One or more selected warehouses do not exist or are inactive.");
        }

        var now = DateTime.UtcNow;

        var part = new CatalogPart
        {
            MaterialNumber = material,
            Description = description,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            UpdatedBy = actor
        };

        foreach (var warehouseItem in warehouses)
        {
            part.PartWarehouses.Add(new PartWarehouse
            {
                Part = part,
                Warehouse = warehouseItem
            });
        }

        _db.Parts.Add(part);
        await _db.SaveChangesAsync(cancellationToken);

        var created = await CatalogQuery()
            .SingleAsync(x => x.Id == part.Id, cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = created.Id },
            ToDto(created));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<PartDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PartDto>> Update(
        int id,
        UpdatePartRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequirePartsManagerAsync(cancellationToken);

        if (actor is null)
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Parts management permission is required.");

        var validation = ValidateRequest(
            request.MaterialNumber,
            request.Description,
            request.WarehouseIds);

        if (validation is not null)
        {
            return BadRequest(validation);
        }

        var part = await _db.Parts
            .Include(x => x.PartWarehouses)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (part is null)
        {
            return NotFound();
        }

        var material = request.MaterialNumber.Trim();
        var description = request.Description.Trim();

        var duplicate = await _db.Parts
            .AnyAsync(
                x => x.Id != id && x.MaterialNumber == material,
                cancellationToken);

        if (duplicate)
        {
            return Conflict($"Material number '{material}' already exists.");
        }

        var warehouseIds = request.WarehouseIds
            .Distinct()
            .ToArray();

        var warehouses = await _db.Warehouses
            .Where(x => warehouseIds.Contains(x.Id) && x.IsActive)
            .ToListAsync(cancellationToken);

        if (warehouses.Count != warehouseIds.Length)
        {
            return BadRequest("One or more selected warehouses do not exist or are inactive.");
        }

        part.MaterialNumber = material;
        part.Description = description;
        part.UpdatedAtUtc = DateTime.UtcNow;
        part.UpdatedBy = actor;

        var requestedWarehouseIds = warehouseIds.ToHashSet();

        foreach (var existing in part.PartWarehouses
                     .Where(x => !requestedWarehouseIds.Contains(x.WarehouseId))
                     .ToList())
        {
            part.PartWarehouses.Remove(existing);
        }

        var existingWarehouseIds = part.PartWarehouses
            .Select(x => x.WarehouseId)
            .ToHashSet();

        foreach (var warehouseItem in warehouses
                     .Where(x => !existingWarehouseIds.Contains(x.Id)))
        {
            part.PartWarehouses.Add(new PartWarehouse
            {
                PartId = part.Id,
                WarehouseId = warehouseItem.Id
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var updated = await CatalogQuery()
            .SingleAsync(x => x.Id == id, cancellationToken);

        return Ok(ToDto(updated));
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(
        int id,
        [FromQuery] string? changedBy = null,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequirePartsManagerAsync(cancellationToken);

        if (actor is null)
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Parts management permission is required.");

        var part = await _db.Parts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (part is null)
        {
            return NotFound();
        }

        if (part.IsActive)
        {
            part.IsActive = false;
            part.UpdatedAtUtc = DateTime.UtcNow;
            part.UpdatedBy = actor;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    [HttpDelete("{id:int}/permanent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PermanentlyDelete(
        int id,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequirePartsManagerAsync(cancellationToken);

        if (actor is null)
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Parts management permission is required.");

        var part = await _db.Parts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (part is null)
            return NotFound();

        // Include even inactive templates: a user may reactivate one later.
        var inSavedTemplate = await _db.TemplateLines
            .AnyAsync(x => x.PartId == id, cancellationToken);

        if (inSavedTemplate)
        {
            return Conflict(
                $"Part '{part.MaterialNumber}' is used in one or more saved templates. " +
                "Remove it from those templates first, or deactivate the part instead.");
        }

        // PartWarehouse records cascade on delete in the existing schema.
        // Other database references must remain protected.
        _db.Parts.Remove(part);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(
                "This part is still referenced by other records and cannot be " +
                "permanently deleted. Deactivate it instead.");
        }

        return NoContent();
    }

    [HttpPost("{id:int}/reactivate")]
    [ProducesResponseType<PartDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PartDto>> Reactivate(
        int id,
        [FromQuery] string? changedBy = null,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequirePartsManagerAsync(cancellationToken);

        if (actor is null)
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Parts management permission is required.");

        var part = await _db.Parts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (part is null)
        {
            return NotFound();
        }

        if (!part.IsActive)
        {
            part.IsActive = true;
            part.UpdatedAtUtc = DateTime.UtcNow;
            part.UpdatedBy = actor;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var reactivated = await CatalogQuery()
            .SingleAsync(x => x.Id == id, cancellationToken);

        return Ok(ToDto(reactivated));
    }

    private async Task<string?> RequirePartsManagerAsync(
        CancellationToken cancellationToken)
    {
        var employeeId =
            Request.Headers[AccessRepository.UserHeaderName]
                .FirstOrDefault()?
                .Trim();

        if (string.IsNullOrWhiteSpace(employeeId))
            return null;

        return await _access.CanManagePartsAsync(
            employeeId,
            cancellationToken)
            ? employeeId
            : null;
    }

    private IQueryable<CatalogPart> CatalogQuery()
        => _db.Parts
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.PartWarehouses)
                .ThenInclude(x => x.Warehouse);

    private static string? ValidateRequest(
        string? materialNumber,
        string? description,
        IReadOnlyList<int>? warehouseIds)
    {
        if (string.IsNullOrWhiteSpace(materialNumber))
        {
            return "Material number is required.";
        }

        if (materialNumber.Trim().Length > 64)
        {
            return "Material number cannot exceed 64 characters.";
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return "Description is required.";
        }

        if (description.Trim().Length > 255)
        {
            return "Description cannot exceed 255 characters.";
        }

        if (warehouseIds is null || warehouseIds.Count == 0)
        {
            return "At least one warehouse is required.";
        }

        return null;
    }

    private static string? CleanChangedBy(string? changedBy)
    {
        var value = changedBy?.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= 128
            ? value
            : value[..128];
    }

    private static PartDto ToDto(CatalogPart part)
    {
        var warehouses = part.PartWarehouses
            .Select(x => x.Warehouse)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new WarehouseDto(
                x.Id,
                x.Code,
                x.Name,
                x.Subtitle,
                x.IsActive,
                x.SortOrder))
            .ToList();

        return new PartDto(
            part.Id,
            part.MaterialNumber,
            part.Description,
            part.IsActive,
            part.CreatedAtUtc,
            part.UpdatedAtUtc,
            part.UpdatedBy,
            warehouses);
    }
}
