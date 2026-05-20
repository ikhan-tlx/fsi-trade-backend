using MediatR;
using FSI.Trade.Compliance.Application.Common.Models;

namespace FSI.Trade.Compliance.Application.Features.Auth.Login;

/// <summary>
/// Slice-1 login body. Slimmed down from the legacy OAuth shape:
///   - grant_type, client_id, client_secret, scope: removed (single FE client,
///     no client-credentials check).
///   - isEncrypted: kept; rejected when true (legacy AES path not ported).
///   - otp: present only when the user is enrolled in 2FA AND the global
///     TwoFactor:Enabled flag is true.
/// </summary>
public sealed class LoginCommand : IRequest<AuthResponse>
{
    public string  username    { get; init; } = "";
    public string  password    { get; init; } = "";
    public bool    isEncrypted { get; init; }
    public string? otp         { get; init; }
    public string? Ip          { get; init; }
}
