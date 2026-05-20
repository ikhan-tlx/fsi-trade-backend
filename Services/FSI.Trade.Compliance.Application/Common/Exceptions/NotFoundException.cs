namespace FSI.Trade.Compliance.Application.Common.Exceptions;

/// <summary>
/// A targeted resource (role, user, mapping, etc.) wasn't found by ID.
/// Maps to HTTP 404 with <c>data.code = &lt;Code&gt;</c> so the FE can branch
/// on values like "role_not_found", "user_not_found".
/// </summary>
public class NotFoundException : Exception
{
    public string Code { get; }
    public NotFoundException(string code, string? description = null)
        : base(description ?? code) => Code = code;
}
