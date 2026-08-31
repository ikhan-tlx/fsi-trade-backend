using System.Text;
using Asp.Versioning;
using FSI.Trade.Compliance.API.Authentication;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.API.Routing;
using FSI.Trade.Compliance.Application;
using FSI.Trade.Compliance.Infrastructure;
using FSI.Trade.Compliance.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// --- DB-managed configuration bridge (Configuration Management, Phase 1) ---
// Layer DB settings ON TOP of appsettings so managed values override the file
// for the same key, and every existing IOptions/IOptionsMonitor<T> consumer
// reads the DB value with hot-reload (no restart). Secrets + logging levels are
// deliberately NOT bridged (see DbConfigurationProvider). Load() is fail-soft:
// if the table/DB is unavailable the app still boots on appsettings alone.
{
    var cfgConn = builder.Configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrWhiteSpace(cfgConn))
        builder.Configuration.AddDbConfiguration(cfgConn);
}

// --- Logging ----------------------------------------------------------
// Module-wise diagnostic files — the new-stack equivalent of the legacy
// custom-DLL + NLog dedicated logs. Driven by Integration:Diagnostics:
//
//   "Files": {
//     "Application.UserManagement": { "Enabled": true, "Level": "Information" },
//     "Application.Trade":          { "Enabled": true, "Level": "Information" },
//     "BackgroundService":          { "Enabled": true, "Level": "Information" }
//   }
//
//   • Application.<Module>-yyyyMMdd.log — end-to-end API request/response (body
//     in + body out + status + elapsed) written by ApiRequestLoggingMiddleware;
//     unhandled exceptions (full inner chain) land here too.
//   • BackgroundService-yyyyMMdd.log — hosted services (FccmCaseIdPoller, the
//     LogArchiver) plus the FCC_OB_RA poll detail.
//   Level: "Error"/"Fatal" = errors only … "Information" = normal … "Debug"/
//   "Verbose" = everything (payloads, FCCM responses, risk-param derivation).
//   Rolled files older than RetentionDays are zipped by LogArchiverService.
var diag   = builder.Configuration.GetSection("Integration:Diagnostics");
var logDir = diag.GetValue<string>("LogDirectory") ?? "Logs";
if (!Path.IsPathRooted(logDir))
    logDir = Path.Combine(builder.Environment.ContentRootPath, logDir);

// Verbose master switch for the WorkflowLog step trail. OFF by default:
// production logs business milestones/warnings/errors; ON adds Debug detail
// (payloads, variable values, decision paths). Requires the target file's
// Level to allow Debug for the detail to actually be written.
FSI.Trade.Compliance.Application.Common.Logging.WorkflowLog.EnableDetailedLogs =
    diag.GetValue("EnableDetailedLogs", false);

static LogEventLevel ParseLevel(string? s, LogEventLevel fallback)
    => Enum.TryParse<LogEventLevel>(s, ignoreCase: true, out var lvl) ? lvl : fallback;

// Dedicated file -> predicate over SourceContext deciding what it captures.
var fileDefs = new (string Key, Func<string, bool> Match)[]
{
    ("Application.UserManagement", sc => sc.Contains("Application.UserManagement")),
    ("Application.Trade",          sc => sc.Contains("Application.Trade")
                                       || (sc.Contains("Integrations.Fccm") && !sc.Contains("FccmCaseIdPoller"))),
    // Hosted/background services. Includes the periodic KYC review driver +
    // its workflow inbox synchronizer so the [PERIODIC-REVIEW ...] step trail
    // (create → drive → each command → halt diagnostic) is captured on UAT.
    ("BackgroundService",          sc => sc.Contains("FccmCaseIdPoller") || sc.Contains("LogArchiverService")
                                       || sc.Contains("Kyc.Review") || sc.Contains("WorkflowInboxSynchronizer")),
    // API/controller layer + ASP.NET Core host logs. Without this, controller and
    // framework logs reach only the Console sink, which is invisible under IIS.
    ("Api",                        sc => sc.Contains("FSI.Trade.Compliance.API")
                                       || sc.StartsWith("Microsoft.AspNetCore")),
};

var activeFiles = new List<(string Key, Func<string, bool> Match, LogEventLevel Level)>();
foreach (var f in fileDefs)
{
    var sec = diag.GetSection($"Files:{f.Key}");
    if (!sec.GetValue("Enabled", false)) continue;
    activeFiles.Add((f.Key, f.Match, ParseLevel(sec.GetValue<string>("Level"), LogEventLevel.Information)));
}

builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .WriteTo.Console();

    if (activeFiles.Count > 0)
    {
        // Let events down to the lowest enabled file level reach the loggers
        // the files care about (the API module loggers + the FCCM namespace).
        var lowest = activeFiles.Min(s => s.Level);
        lc.MinimumLevel.Override("Application", lowest);
        lc.MinimumLevel.Override("FSI.Trade.Compliance.Infrastructure.Integrations.Fccm", lowest);
        // Let the periodic KYC review driver + inbox synchronizer emit down to the
        // lowest enabled file level so their [PERIODIC-REVIEW ...] trail is written
        // (they log at Information, but this also allows Debug detail when enabled).
        lc.MinimumLevel.Override("FSI.Trade.Compliance.Infrastructure.Kyc.Review", lowest);
        lc.MinimumLevel.Override("FSI.Trade.Compliance.Infrastructure.Workflow.WorkflowInboxSynchronizer", lowest);

        foreach (var f in activeFiles)
        {
            var match = f.Match;
            var level = f.Level;
            var key   = f.Key;
            lc.WriteTo.Logger(sub => sub
                .Filter.ByIncludingOnly(e =>
                    e.Properties.TryGetValue("SourceContext", out var sc) &&
                    match(sc.ToString().Trim('"')))
                .WriteTo.File(
                    path:                     Path.Combine(logDir, key + "-.log"),
                    restrictedToMinimumLevel: level,
                    rollingInterval:          Serilog.RollingInterval.Day,
                    retainedFileCountLimit:   null,    // LogArchiverService owns retention/zip
                    shared:                   true,
                    outputTemplate:
                        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Properties:j}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}"));
        }
    }
});

// --- DI ---------------------------------------------------------------
// Configurable API route prefix. Defaults to "api" (routes stay /api/v1/...).
// When publishing under an IIS application named "api", set Api:RoutePrefix to ""
// (e.g. env var Api__RoutePrefix=) so the public URL is /api/v1/... and not the
// doubled /api/api/v1/... See ApiRoutePrefixConvention for the full rationale.
var apiRoutePrefix = builder.Configuration["Api:RoutePrefix"] ?? "api";
builder.Services.AddControllers(opt =>
{
    opt.Filters.Add<ExceptionHandlingFilter>();
    opt.Filters.Add<FSI.Trade.Compliance.API.Filters.AuditActionFilter>();
    opt.Conventions.Add(new ApiRoutePrefixConvention(apiRoutePrefix));
});
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddHttpContextAccessor();

// KYC screening is always LIVE (no in-process sandbox mode). It calls the
// provider at Integration:Brains:BaseUrl — a live provider in Production, or the
// standalone BRAINS-compatible test provider (kyc-mock-provider/) in dev/UAT.
// Screening is gated on/off by config (Onboarding.RequireExternalVerification).

// --- API Versioning ---------------------------------------------------
// Strategy:
//   - URL-segment is the SOLE, canonical version reader: /api/v1/..., /api/v2/...
//   - Default version 1.0 is assumed when none is provided (back-compat).
//   - Server reports api-supported-versions on every response so FE can detect
//     when v2 ships.
//
// NOTE (2026-06-01): the header (X-API-Version) and query (api-version) readers
// were REMOVED. With ApiVersionReader.Combine, if a client still sent a version
// hint that disagreed with the URL segment (e.g. a leftover `X-API-Version: 1.0`
// default header on a `/api/v2/...` request), Asp.Versioning treated it as an
// AMBIGUOUS API version and returned HTTP 400 — which broke every v2 call.
// The URL already carries the version, so the extra readers added no value and
// only created that failure mode. If header/query negotiation is needed later,
// re-add them ONLY after ensuring no client sends a version that contradicts the URL.
builder.Services
    .AddApiVersioning(o =>
    {
        o.DefaultApiVersion                   = new ApiVersion(1, 0);
        o.AssumeDefaultVersionWhenUnspecified = true;
        o.ReportApiVersions                   = true;
        o.ApiVersionReader                    = new UrlSegmentApiVersionReader();
    })
    .AddApiExplorer(o =>
    {
        // For Swagger group naming. Format "'v'V" → groups like "v1", "v2".
        o.GroupNameFormat            = "'v'VVV";
        o.SubstituteApiVersionInUrl  = true;
    });

// --- Authentication ---------------------------------------------------
var jwtIssuer   = builder.Configuration["Jwt:Issuer"]   ?? throw new InvalidOperationException("Jwt:Issuer not set");
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience not set");
var jwtKey      = builder.Configuration["Jwt:Key"]      ?? throw new InvalidOperationException("Jwt:Key not set");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.RequireHttpsMetadata = false;          // dev only; flip true for prod via config transform
        o.SaveToken            = true;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer            = true,
            ValidateAudience          = true,
            ValidateLifetime          = true,
            ValidateIssuerSigningKey  = true,
            ValidIssuer               = jwtIssuer,
            ValidAudience             = jwtAudience,
            IssuerSigningKey          = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew                 = TimeSpan.FromSeconds(30)
        };
        // WebSocket clients cannot send an Authorization header, so SignalR
        // passes the JWT as the ?access_token=... query string. Lift it into the
        // token pipeline ONLY for the hub path so normal HTTP auth is unchanged.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();

// --- Swagger ----------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "FSI.Trade.Compliance API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name        = "Authorization",
        Type        = SecuritySchemeType.Http,
        Scheme      = "bearer",
        BearerFormat = "JWT",
        In          = ParameterLocation.Header,
        Description = "Enter the JWT issued by /api/v1/User/Login (without the 'Bearer ' prefix)."
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// --- CORS (dev permissive; tighten via config in prod) ----------------
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader()
     .AllowAnyMethod()
     .SetIsOriginAllowed(_ => true)
     .AllowCredentials()));

var app = builder.Build();

// --- Pipeline ---------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseMiddleware<FSI.Trade.Compliance.API.Logging.ApiRequestLoggingMiddleware>();   // module-wise API request/response logs
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<FSI.Trade.Compliance.API.Logging.RequestContextLoggingMiddleware>();   // RequestId/CorrelationId/UserId scope on every downstream log line
app.UseMiddleware<CustomAuthorizationMiddleware>();   // whitelist + post-JWT gate
app.UseMiddleware<DeviceTrackingMiddleware>();        // X-Device-Id required on authenticated requests
app.UseAuthorization();

app.MapControllers();
app.MapHub<FSI.Trade.Compliance.Infrastructure.Realtime.NotificationHub>("/hubs/notifications");

app.Run();
