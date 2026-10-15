namespace PartsPortal.Api.Data.Entities;

public sealed class CatalogTemplate
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int WarehouseId { get; set; }

    public Warehouse Warehouse { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }

    public ICollection<CatalogTemplateLine> Lines { get; set; }
        = new List<CatalogTemplateLine>();
}
