namespace PartsPortal.Api.Data.Entities;

public sealed class CatalogTemplateLine
{
    public int Id { get; set; }

    public int TemplateId { get; set; }

    public CatalogTemplate Template { get; set; } = null!;

    public int SortOrder { get; set; }

    public string MaterialNumber { get; set; } = "";

    public string Description { get; set; } = "";

    public int Quantity { get; set; }
}
