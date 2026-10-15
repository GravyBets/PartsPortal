namespace PartsPortal.Api.Data.Entities;

public sealed class CatalogTemplateLine
{
    public int Id { get; set; }

    public int TemplateId { get; set; }

    public CatalogTemplate Template { get; set; } = null!;

    public int PartId { get; set; }

    public CatalogPart Part { get; set; } = null!;

    public int SortOrder { get; set; }

    public int Quantity { get; set; }
}
