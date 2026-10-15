using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PartsPortal.Api.Data;
using PartsPortal.Api.Data.Entities;
using PartsPortal.Api.Security;
using PartsPortal.Contracts.Catalog;
using PartsPortal.Contracts.Templates;

namespace PartsPortal.Api.Controllers;

[ApiController]
[Route("api/templates")]
public sealed class TemplatesController : ControllerBase
{
    private readonly PartsPortalDbContext _db;
    private readonly AccessRepository _access;

    public TemplatesController(
        PartsPortalDbContext db,
        AccessRepository access)
    {
        _db = db;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PartTemplateDto>>> GetAll(
        [FromQuery] string? warehouse = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = TemplateQuery();

        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(warehouse))
        {
            var term = warehouse.Trim();

            query = query.Where(x =>
                x.Warehouse.Code == term ||
                x.Warehouse.Name == term);
        }

        var templates = await query
            .OrderBy(x => x.Warehouse.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(templates.Select(ToDto).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PartTemplateDto>> GetById(
        int id,
        CancellationToken cancellationToken = default)
    {
        var template = await TemplateQuery()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return template is null
            ? NotFound()
            : Ok(ToDto(template));
    }

    [HttpPost]
    public async Task<ActionResult<PartTemplateDto>> Create(
        SaveTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireTemplateManagerAsync(cancellationToken);

        if (actor is null)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Template management permission is required.");
        }

        var validation = ValidateRequest(request);
        if (validation is not null)
            return BadRequest(validation);

        var warehouse = await _db.Warehouses
            .SingleOrDefaultAsync(
                x => x.Id == request.WarehouseId && x.IsActive,
                cancellationToken);

        if (warehouse is null)
            return BadRequest("Selected warehouse does not exist or is inactive.");

        var name = request.Name.Trim();

        var duplicate = await _db.Templates
            .AnyAsync(
                x => x.WarehouseId == request.WarehouseId &&
                     x.Name == name,
                cancellationToken);

        if (duplicate)
        {
            return Conflict(
                $"A template named '{name}' already exists for {warehouse.Name}.");
        }

        var resolved = await ResolvePartsAsync(
            request.WarehouseId,
            request.Lines,
            cancellationToken);

        if (resolved.Error is not null)
            return BadRequest(resolved.Error);

        var now = DateTime.UtcNow;

        var template = new CatalogTemplate
        {
            Name = name,
            WarehouseId = warehouse.Id,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            UpdatedBy = actor
        };

        foreach (var line in resolved.Lines!)
        {
            template.Lines.Add(new CatalogTemplateLine
            {
                PartId = line.Part.Id,
                SortOrder = line.SortOrder,
                Quantity = line.Quantity
            });
        }

        _db.Templates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);

        var created = await TemplateQuery()
            .SingleAsync(x => x.Id == template.Id, cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = template.Id },
            ToDto(created));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PartTemplateDto>> Update(
        int id,
        SaveTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireTemplateManagerAsync(cancellationToken);

        if (actor is null)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Template management permission is required.");
        }

        var validation = ValidateRequest(request);
        if (validation is not null)
            return BadRequest(validation);

        var template = await _db.Templates
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (template is null)
            return NotFound();

        var warehouse = await _db.Warehouses
            .SingleOrDefaultAsync(
                x => x.Id == request.WarehouseId && x.IsActive,
                cancellationToken);

        if (warehouse is null)
            return BadRequest("Selected warehouse does not exist or is inactive.");

        var name = request.Name.Trim();

        var duplicate = await _db.Templates
            .AnyAsync(
                x => x.Id != id &&
                     x.WarehouseId == request.WarehouseId &&
                     x.Name == name,
                cancellationToken);

        if (duplicate)
        {
            return Conflict(
                $"A template named '{name}' already exists for {warehouse.Name}.");
        }

        var resolved = await ResolvePartsAsync(
            request.WarehouseId,
            request.Lines,
            cancellationToken);

        if (resolved.Error is not null)
            return BadRequest(resolved.Error);

        template.Name = name;
        template.WarehouseId = request.WarehouseId;
        template.UpdatedAtUtc = DateTime.UtcNow;
        template.UpdatedBy = actor;

        _db.TemplateLines.RemoveRange(template.Lines);
        template.Lines.Clear();

        foreach (var line in resolved.Lines!)
        {
            template.Lines.Add(new CatalogTemplateLine
            {
                TemplateId = template.Id,
                PartId = line.Part.Id,
                SortOrder = line.SortOrder,
                Quantity = line.Quantity
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var updated = await TemplateQuery()
            .SingleAsync(x => x.Id == id, cancellationToken);

        return Ok(ToDto(updated));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireTemplateManagerAsync(cancellationToken);

        if (actor is null)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Template management permission is required.");
        }

        var template = await _db.Templates
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (template is null)
            return NotFound();

        if (template.IsActive)
        {
            template.IsActive = false;
            template.UpdatedAtUtc = DateTime.UtcNow;
            template.UpdatedBy = actor;

            await _db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("{id:int}/reactivate")]
    public async Task<ActionResult<PartTemplateDto>> Reactivate(
        int id,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireTemplateManagerAsync(cancellationToken);

        if (actor is null)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Template management permission is required.");
        }

        var template = await _db.Templates
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (template is null)
            return NotFound();

        if (!template.IsActive)
        {
            template.IsActive = true;
            template.UpdatedAtUtc = DateTime.UtcNow;
            template.UpdatedBy = actor;

            await _db.SaveChangesAsync(cancellationToken);
        }

        var reactivated = await TemplateQuery()
            .SingleAsync(x => x.Id == id, cancellationToken);

        return Ok(ToDto(reactivated));
    }

    private async Task<string?> RequireTemplateManagerAsync(
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

    private IQueryable<CatalogTemplate> TemplateQuery()
        => _db.Templates
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Warehouse)
            .Include(x => x.Lines)
                .ThenInclude(x => x.Part);

    private async Task<(List<ResolvedLine>? Lines, string? Error)> ResolvePartsAsync(
        int warehouseId,
        IReadOnlyList<TemplateLineRequest> lines,
        CancellationToken cancellationToken)
    {
        var requestedMaterials = lines
            .Select(x => x.MaterialNumber.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedMaterials.Count != lines.Count)
            return (null, "A template cannot contain the same material more than once.");

        var parts = await _db.Parts
            .AsNoTracking()
            .Include(x => x.PartWarehouses)
            .Where(x =>
                x.IsActive &&
                requestedMaterials.Contains(x.MaterialNumber))
            .ToListAsync(cancellationToken);

        var byMaterial = parts.ToDictionary(
            x => x.MaterialNumber,
            StringComparer.OrdinalIgnoreCase);

        var resolved = new List<ResolvedLine>();

        foreach (var requestLine in lines.OrderBy(x => x.SortOrder))
        {
            var material = requestLine.MaterialNumber.Trim();

            if (!byMaterial.TryGetValue(material, out var part))
            {
                return (
                    null,
                    $"Material '{material}' does not exist or is inactive.");
            }

            if (!part.PartWarehouses.Any(x => x.WarehouseId == warehouseId))
            {
                return (
                    null,
                    $"Material '{material}' is not assigned to the selected warehouse.");
            }

            resolved.Add(new ResolvedLine(
                part,
                requestLine.Quantity,
                requestLine.SortOrder));
        }

        return (resolved, null);
    }

    private static string? ValidateRequest(SaveTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return "Template name is required.";

        if (request.Name.Trim().Length > 100)
            return "Template name cannot exceed 100 characters.";

        if (request.WarehouseId <= 0)
            return "A warehouse is required.";

        if (request.Lines is null || request.Lines.Count == 0)
            return "A template must contain at least one part.";

        if (request.Lines.Count > 500)
            return "A template cannot contain more than 500 lines.";

        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.MaterialNumber))
                return "Every template line requires a material number.";

            if (line.MaterialNumber.Trim().Length > 64)
                return "Template material numbers cannot exceed 64 characters.";

            if (line.Quantity <= 0)
                return "Template quantities must be greater than zero.";

            if (line.Quantity > 100000)
                return "Template quantities cannot exceed 100000.";

            if (line.SortOrder < 0)
                return "Template sort order cannot be negative.";
        }

        return null;
    }

    private static PartTemplateDto ToDto(CatalogTemplate template)
    {
        var warehouse = new WarehouseDto(
            template.Warehouse.Id,
            template.Warehouse.Code,
            template.Warehouse.Name,
            template.Warehouse.Subtitle,
            template.Warehouse.IsActive,
            template.Warehouse.SortOrder);

        var lines = template.Lines
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(x => new TemplateLineDto(
                x.Id,
                x.SortOrder,
                x.Part.MaterialNumber,
                x.Part.Description,
                x.Quantity))
            .ToList();

        return new PartTemplateDto(
            template.Id,
            template.Name,
            template.IsActive,
            template.CreatedAtUtc,
            template.UpdatedAtUtc,
            template.UpdatedBy,
            warehouse,
            lines);
    }

    private sealed record ResolvedLine(
        CatalogPart Part,
        int Quantity,
        int SortOrder);
}
