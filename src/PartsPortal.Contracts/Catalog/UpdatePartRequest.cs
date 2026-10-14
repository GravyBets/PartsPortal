namespace PartsPortal.Contracts.Catalog;

public sealed record UpdatePartRequest(
    string MaterialNumber,
    string Description,
    IReadOnlyList<int> WarehouseIds,
    string? ChangedBy);
