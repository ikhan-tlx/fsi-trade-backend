namespace FSI.Trade.Compliance.Application.Common.Exceptions;

/// <summary>
/// A write/read the caller is authenticated for but not allowed to perform on
/// this specific resource (a business-rule authorization failure, distinct from
/// the coarse <c>[RequiresPrivilege]</c> privilege gate). Maps to HTTP 403 with
/// <c>data.code = &lt;Code&gt;</c> so the FE can branch on values like
/// "lookup_type_not_manageable", "lookup_value_not_editable".
/// </summary>
public class ForbiddenException : Exception
{
    public string Code { get; }
    public ForbiddenException(string code, string? description = null)
        : base(description ?? code) => Code = code;
}
