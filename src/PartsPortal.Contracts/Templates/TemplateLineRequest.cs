namespace PartsPortal.Contracts.Templates;

public sealed record TemplateLineRequest(
    string MaterialNumber,
    string Description,
    int Quantity,
    int SortOrder);
