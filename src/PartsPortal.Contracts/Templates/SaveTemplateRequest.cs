namespace PartsPortal.Contracts.Templates;

public sealed record SaveTemplateRequest(
    string Name,
    int WarehouseId,
    IReadOnlyList<TemplateLineRequest> Lines);
