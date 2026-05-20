using System.Text;
using Asp.Versioning;
using FSI.Trade.Compliance.API.Authentication;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application;
using FSI.Trade.Compliance.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Logging ----------------------------------------------------------
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// --- DI ---------------------------------------------------------------
builder.Services.AddControllers(opt =>
{
    opt.Filters.Add<ExceptionHandlingFilter>();
});
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddHttpContextAccessor();

// --- API Versioning ---------------------------------------------------
// Strategy:
//   - URL-segment is the canonical version reader: /api/v1/...
//   - Clients can ALSO supply X-API-Version or ?api-version=1.0 — useful for
//     header-driven negotiation later without breaking URL-bookmarked v1 calls.
//   - Default version 1.0 is assumed when none is provided (back-compat).
//   - Server reports api-supported-versions on every response so FE can detect
//     when v2 ships.
builder.Services
    .AddApiVersioning(o =>
    {
        o.DefaultApiVersion                   = new ApiVersion(1, 0);
        o.AssumeDefaultVersionWhenUnspecified = true;
        o.ReportApiVersions                   = true;
        o.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-API-Version"),
            new QueryStringApiVersionReader("api-version"));
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
    });

builder.Services.AddAuthorization();

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
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<CustomAuthorizationMiddleware>();   // whitelist + post-JWT gate
app.UseMiddleware<DeviceTrackingMiddleware>();        // X-Device-Id required on authenticated requests
app.UseAuthorization();

app.MapControllers();

app.Run();
