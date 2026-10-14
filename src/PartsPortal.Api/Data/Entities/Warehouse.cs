namespace PartsPortal.Api.Data.Entities;

public sealed class Warehouse
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Subtitle { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public ICollection<PartWarehouse> PartWarehouses { get; set; } = new List<PartWarehouse>();
}
