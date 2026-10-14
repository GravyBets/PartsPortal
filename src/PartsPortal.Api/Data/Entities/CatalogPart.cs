namespace PartsPortal.Api.Data.Entities;

public sealed class CatalogPart
{
    public int Id { get; set; }

    public string MaterialNumber { get; set; } = "";

    public string Description { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }

    public ICollection<PartWarehouse> PartWarehouses { get; set; } = new List<PartWarehouse>();
}
