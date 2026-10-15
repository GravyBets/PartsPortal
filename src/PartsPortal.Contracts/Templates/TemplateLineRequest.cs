namespace PartsPortal.Contracts.Templates;

public sealed record TemplateLineRequest(
    string MaterialNumber,
    int Quantity,
    int SortOrder);
