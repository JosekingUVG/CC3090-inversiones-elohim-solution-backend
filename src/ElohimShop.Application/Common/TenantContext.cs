namespace ElohimShop.Application.Common;

public class TenantContext
{
    public string TenantId { get; init; } = default!;
    public string Slug { get; init; } = default!;
    public string Nombre { get; init; } = default!;
    public bool IsSuperAdminOverride { get; init; }
}
