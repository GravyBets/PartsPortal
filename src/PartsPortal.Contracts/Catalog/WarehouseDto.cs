namespace PartsPortal.Contracts.Catalog;

public sealed record WarehouseDto(
    int Id,
    string Code,
    string Name,
    string? Subtitle,
    bool IsActive,
    int SortOrder);
