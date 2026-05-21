using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.SetActive;

/// <summary>
/// Toggle a flag's catalogue-level Active_Flag. Sets the flag globally
/// on or off across every product / tab it's scoped to. Existing
/// TmX_Transaction_Flag rows are preserved — only future read paths
/// hide the indicator when active = false.
///
/// Maps to:
///   <c>POST /api/v1/Flag/{id}/Activate</c>
///   <c>POST /api/v1/Flag/{id}/Deactivate</c>
/// Both gated by <c>Flags.Manage</c>.
/// </summary>
public record SetFlagActiveCommand(int FlagId, bool IsActive) : IRequest<Unit>;
