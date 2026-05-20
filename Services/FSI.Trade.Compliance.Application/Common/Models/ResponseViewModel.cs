namespace FSI.Trade.Compliance.Application.Common.Models;

/// <summary>
/// Single wire envelope for every API response. The body is in <c>data</c>;
/// status metadata is in <c>status</c>. No special top-level fields — tokens,
/// pagination, etc. live inside <c>data</c>.
/// </summary>
public class ResponseViewModel<T>
{
    public ResponseStatus status { get; set; } = new();
    public T?             data   { get; set; }

    public static ResponseViewModel<T> Ok(T? payload, string? message = "OK") =>
        new() { status = ResponseStatus.Ok(message), data = payload };

    public static ResponseViewModel<T> Fail(int httpCode, string message, string? description = null) =>
        new() { status = ResponseStatus.Error(httpCode, message, description) };
}
