using FluentValidation.Results;

namespace FSI.Trade.Compliance.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException() : base("One or more validation failures occurred.")
        => Errors = new Dictionary<string, string[]>();

    public ValidationException(IEnumerable<ValidationFailure> failures) : this()
    {
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }

    /// <summary>
    /// Single-failure overload for hand-thrown validation errors (handlers
    /// that aren't backed by a FluentValidator). The <paramref name="code"/>
    /// becomes the Errors key so the FE can branch on it (e.g.
    /// "request_id_required"), mirroring <see cref="NotFoundException"/>'s
    /// code-first convention.
    /// </summary>
    public ValidationException(string code, string description) : base(description)
        => Errors = new Dictionary<string, string[]> { [code] = new[] { description } };
}
