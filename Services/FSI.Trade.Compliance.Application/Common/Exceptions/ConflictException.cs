namespace FSI.Trade.Compliance.Application.Common.Exceptions;

/// <summary>
/// A write that violates a business uniqueness or state invariant.
/// Maps to HTTP 409 with <c>data.code = &lt;Code&gt;</c> — e.g. "role_name_taken",
/// "role_already_active", "role_in_use".
/// </summary>
public class ConflictException : Exception
{
    public string Code { get; }
    public ConflictException(string code, string? description = null)
        : base(description ?? code) => Code = code;
}
