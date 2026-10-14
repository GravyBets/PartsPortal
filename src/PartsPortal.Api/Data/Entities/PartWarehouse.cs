namespace PartsPortal.Api.Data.Entities;

public sealed class PartWarehouse
{
    public int PartId { get; set; }

    public CatalogPart Part { get; set; } = null!;

    public int WarehouseId { get; set; }

    public Warehouse Warehouse { get; set; } = null!;
}
