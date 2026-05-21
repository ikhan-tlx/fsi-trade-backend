using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Get;

/// <summary>
/// Slice 8 Step 6 — single-flag detail with all its scopes denormalised
/// into the response (admin doesn't want a second roundtrip).
///
/// Maps to <c>GET /api/v1/Flag/{id}</c>. Gated by <c>Flags.View</c>.
/// </summary>
public record GetFlagByIdQuery(int FlagId) : IRequest<FlagCatalogueDetailDto>;

public class FlagCatalogueDetailDto
{
    public int       flagId             { get; set; }
    public string    flagCode           { get; set; } = "";
    public string    flagName           { get; set; } = "";
    public string    flagDescription    { get; set; } = "";
    public int       flagTypeLkpId      { get; set; }
    public int?      flagCategoryLkpId  { get; set; }
    public int?      severityLkpId      { get; set; }
    public decimal   defaultWeight      { get; set; }
    public bool      requiresEvidence   { get; set; }
    public string?   sourceSystem       { get; set; }
    public bool      activeFlag         { get; set; }
    public string    createdBy          { get; set; } = "";
    public DateTime  createdDate        { get; set; }
    public string?   lastUpdatedBy      { get; set; }
    public DateTime? lastUpdatedDate    { get; set; }

    public List<FlagScopeDto> scopes    { get; set; } = new();
}

public class FlagScopeDto
{
    public int      flagScopeId      { get; set; }
    public int      productId        { get; set; }
    public int?     tabId            { get; set; }
    public int      sortOrder        { get; set; }
    public bool     activeFlag       { get; set; }
    public string?  legacyFieldName  { get; set; }
}
