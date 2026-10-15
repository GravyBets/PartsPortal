using PartsPortal.Contracts.Catalog;

namespace PartsPortal.Contracts.Templates;

public sealed record PartTemplateDto(
    int Id,
    string Name,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? UpdatedBy,
    WarehouseDto Warehouse,
    IReadOnlyList<TemplateLineDto> Lines);
