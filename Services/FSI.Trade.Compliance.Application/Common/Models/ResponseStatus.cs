namespace FSI.Trade.Compliance.Application.Common.Models;

public class ResponseStatus
{
    public int     code        { get; set; }
    public string? message     { get; set; }
    public string? description { get; set; }

    public static ResponseStatus Ok(string? msg = "OK") =>
        new() { code = 200, message = msg };

    public static ResponseStatus Error(int httpCode, string msg, string? desc = null) =>
        new() { code = httpCode, message = msg, description = desc };
}
