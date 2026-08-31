using FSI.Trade.Compliance.API.Logging;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.API.Filters;

/// <summary>
/// Global exception → ResponseViewModel translator. Always returns the standard
/// envelope shape: { status, data }. On failure, <c>data</c> always carries
/// <c>success: 0</c> so the FE's <c>data.success === 0</c> check works
/// consistently for every error class — auth, validation, unauthorized, generic.
/// </summary>
public class ExceptionHandlingFilter : IExceptionFilter
{
    private readonly ILogger<ExceptionHandlingFilter> _log;
    private readonly ILoggerFactory                   _loggerFactory;
    public ExceptionHandlingFilter(ILogger<ExceptionHandlingFilter> log, ILoggerFactory loggerFactory)
    {
        _log           = log;
        _loggerFactory = loggerFactory;
    }

    public void OnException(ExceptionContext context)
    {
        ResponseViewModel<object> envelope;
        int                       httpStatus;

        switch (context.Exception)
        {
            case AuthenticationException ax:
                _log.LogInformation("Auth failure: {Code} {Msg}", ax.Code, ax.Message);
                httpStatus = 400;
                envelope   = new ResponseViewModel<object>
                {
                    status = ResponseStatus.Error(httpStatus, ax.Code, ax.Message),
                    data   = new { Success = 0, Code = ax.Code, Message = ax.Message }
                };
                break;

            case ValidationException vx:
                _log.LogInformation("Validation failure: {Errors}", string.Join("; ", vx.Errors.Keys));
                httpStatus = 400;
                envelope   = new ResponseViewModel<object>
                {
                    status = ResponseStatus.Error(httpStatus, "validation_failed", "One or more validation failures occurred."),
                    data   = new { Success = 0, Code = "validation_failed", Errors = vx.Errors }
                };
                break;

            case NotFoundException nx:
                _log.LogInformation("Not found: {Code} {Msg}", nx.Code, nx.Message);
                httpStatus = 404;
                envelope   = new ResponseViewModel<object>
                {
                    status = ResponseStatus.Error(httpStatus, nx.Code, nx.Message),
                    data   = new { Success = 0, Code = nx.Code, Message = nx.Message }
                };
                break;

            case ForbiddenException fx:
                _log.LogInformation("Forbidden: {Code} {Msg}", fx.Code, fx.Message);
                httpStatus = 403;
                envelope   = new ResponseViewModel<object>
                {
                    status = ResponseStatus.Error(httpStatus, fx.Code, fx.Message),
                    data   = new { Success = 0, Code = fx.Code, Message = fx.Message }
                };
                break;

            case ConflictException cx:
                _log.LogInformation("Conflict: {Code} {Msg}", cx.Code, cx.Message);
                httpStatus = 409;
                envelope   = new ResponseViewModel<object>
                {
                    status = ResponseStatus.Error(httpStatus, cx.Code, cx.Message),
                    data   = new { Success = 0, Code = cx.Code, Message = cx.Message }
                };
                break;

            case UnauthorizedAccessException:
                httpStatus = 401;
                envelope   = new ResponseViewModel<object>
                {
                    status = ResponseStatus.Error(httpStatus, "unauthorized"),
                    data   = new { Success = 0, Code = "unauthorized" }
                };
                break;

            // A storage-tier failure (missing/read-only folder, no write permission)
            // is a SERVER fault, not an auth challenge — 500 + full logging so the real
            // cause (e.g. Documents.BasePath not writable by the app-pool identity) is
            // visible, instead of a misleading 401 that pops the login modal.
            case DocumentStorageException dsx:
                var storeModule    = LogModules.FromPath(context.HttpContext.Request.Path.Value);
                var storeModuleLog = _loggerFactory.CreateLogger(LogModules.Category(storeModule));
                storeModuleLog.LogError(context.Exception,
                    "Document storage failure on {Method} {Path}",
                    context.HttpContext.Request.Method, context.HttpContext.Request.Path.Value);
                httpStatus = 500;
                envelope   = new ResponseViewModel<object>
                {
                    status = ResponseStatus.Error(httpStatus, dsx.Code, dsx.Message),
                    data   = new { Success = 0, Code = dsx.Code, Message = dsx.Message }
                };
                break;

            default:
                // Log the FULL exception (Serilog renders the inner-exception
                // chain via {Exception}) into the request's module file so it
                // sits alongside that call's request/response entry.
                var module    = LogModules.FromPath(context.HttpContext.Request.Path.Value);
                var moduleLog = _loggerFactory.CreateLogger(LogModules.Category(module));
                moduleLog.LogError(context.Exception,
                    "Unhandled exception on {Method} {Path}",
                    context.HttpContext.Request.Method, context.HttpContext.Request.Path.Value);
                httpStatus = 500;
                envelope   = new ResponseViewModel<object>
                {
                    status = ResponseStatus.Error(httpStatus, "internal_error", "An unexpected error occurred."),
                    data   = new { Success = 0, Code = "internal_error" }
                };
                break;
        }

        context.Result            = new ObjectResult(envelope) { StatusCode = httpStatus };
        context.ExceptionHandled  = true;
    }
}
