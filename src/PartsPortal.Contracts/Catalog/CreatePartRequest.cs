namespace PartsPortal.Contracts.Catalog;

public sealed record CreatePartRequest(
    string MaterialNumber,
    string Description,
    IReadOnlyList<int> WarehouseIds,
    string? ChangedBy);
