namespace FSI.Trade.Compliance.Application.Common.Exceptions;

/// <summary>
/// Login / refresh / token-validation failure. Maps to HTTP 400 with Code surfaced
/// as the response message so the FE can branch on values like "invalid_grant",
/// "account_locked", "password_expired".
/// </summary>
public class AuthenticationException : Exception
{
    public string Code { get; }
    public AuthenticationException(string code, string? description = null)
        : base(description ?? code) => Code = code;
}
