namespace PartsPortal.Contracts.Catalog;

public sealed record PartDto(
    int Id,
    string MaterialNumber,
    string Description,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? UpdatedBy,
    IReadOnlyList<WarehouseDto> Warehouses);
