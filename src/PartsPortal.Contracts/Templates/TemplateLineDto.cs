namespace PartsPortal.Contracts.Templates;

public sealed record TemplateLineDto(
    int Id,
    int SortOrder,
    string MaterialNumber,
    string Description,
    int Quantity);
