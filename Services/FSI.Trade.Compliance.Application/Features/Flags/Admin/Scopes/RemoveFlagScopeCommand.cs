using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;

/// <summary>
/// Permanently delete a scope row. Use this when the bank no longer
/// wants the flag to appear on a particular (product, tab) at all —
/// even hidden. For a "temporarily hide" semantic, prefer
/// <see cref="UpdateFlagScopeCommand"/> with <c>activeFlag = false</c>.
///
/// Maps to <c>DELETE /api/v1/Flag/Scope/{scopeId}</c>. Gated by
/// <c>Flags.Manage</c>.
///
/// Existing <c>TmX_Transaction_Flag</c> rows pointing at this flag are
/// NOT affected — historical transaction state stays intact.
/// </summary>
public record RemoveFlagScopeCommand(int FlagScopeId) : IRequest<Unit>;
