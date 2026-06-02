using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Delete;

/// <summary>
/// Slice 8.1 — SOFT delete a catalogue flag (sets Is_Deleted = 1 and
/// deactivates it). Built-in flags cannot be deleted — the handler returns
/// a 409 (<c>flag_is_builtin</c>); the admin UI hides the Delete action for
/// them. Existing <c>TmX_Transaction_Flag</c> rows are preserved so
/// historical / in-flight transaction state and audit stay intact.
///
/// Maps to <c>DELETE /api/v1/Flag/{id}</c>. Gated by <c>Flags.Manage</c>.
/// </summary>
public record DeleteFlagCommand(int FlagId) : IRequest<Unit>;
