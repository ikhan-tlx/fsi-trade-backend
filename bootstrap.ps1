# Bootstrap FSI.Trade.Compliance.sln (.NET 8). Run once.
# Usage: cd "D:\ICBC - Latest\FSI.Trade.Compliance"  ;  powershell -File .\bootstrap.ps1
# Idempotent: safe to re-run after partial failures.

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot

# ---------- Solution ----------
if (-not (Test-Path 'FSI.Trade.Compliance.sln')) {
    dotnet new sln -n FSI.Trade.Compliance
}

# ---------- Class libs (Domain / Application / Infrastructure) ----------
foreach ($name in @(
    'FSI.Trade.Compliance.Domain',
    'FSI.Trade.Compliance.Application',
    'FSI.Trade.Compliance.Infrastructure'))
{
    if (-not (Test-Path "Services/$name/$name.csproj")) {
        dotnet new classlib -o "Services/$name" -f net8.0
        Remove-Item "Services/$name/Class1.cs" -ErrorAction SilentlyContinue
    }
}

# ---------- API project ----------
# We do NOT use `dotnet new webapi` because the template refuses to overwrite our
# pre-written Program.cs / appsettings*.json. The .csproj and launchSettings.json
# are checked into the repo. If either is missing, recreate them from the template
# elsewhere — but in normal flow they exist.
if (-not (Test-Path 'Services/FSI.Trade.Compliance.API/FSI.Trade.Compliance.API.csproj')) {
    Write-Host 'API .csproj missing. Re-pull from source control.' -ForegroundColor Red
    Pop-Location
    exit 1
}

# ---------- Add to solution (idempotent) ----------
dotnet sln add 'Services/FSI.Trade.Compliance.Domain/FSI.Trade.Compliance.Domain.csproj'
dotnet sln add 'Services/FSI.Trade.Compliance.Application/FSI.Trade.Compliance.Application.csproj'
dotnet sln add 'Services/FSI.Trade.Compliance.Infrastructure/FSI.Trade.Compliance.Infrastructure.csproj'
dotnet sln add 'Services/FSI.Trade.Compliance.API/FSI.Trade.Compliance.API.csproj'

# ---------- Project references ----------
dotnet add 'Services/FSI.Trade.Compliance.Application/FSI.Trade.Compliance.Application.csproj'       reference 'Services/FSI.Trade.Compliance.Domain/FSI.Trade.Compliance.Domain.csproj'
dotnet add 'Services/FSI.Trade.Compliance.Infrastructure/FSI.Trade.Compliance.Infrastructure.csproj' reference 'Services/FSI.Trade.Compliance.Application/FSI.Trade.Compliance.Application.csproj'
dotnet add 'Services/FSI.Trade.Compliance.Infrastructure/FSI.Trade.Compliance.Infrastructure.csproj' reference 'Services/FSI.Trade.Compliance.Domain/FSI.Trade.Compliance.Domain.csproj'
dotnet add 'Services/FSI.Trade.Compliance.API/FSI.Trade.Compliance.API.csproj'                       reference 'Services/FSI.Trade.Compliance.Application/FSI.Trade.Compliance.Application.csproj'
dotnet add 'Services/FSI.Trade.Compliance.API/FSI.Trade.Compliance.API.csproj'                       reference 'Services/FSI.Trade.Compliance.Infrastructure/FSI.Trade.Compliance.Infrastructure.csproj'

# ---------- Application packages ----------
dotnet add 'Services/FSI.Trade.Compliance.Application'    package MediatR                                              --version 12.4.1
dotnet add 'Services/FSI.Trade.Compliance.Application'    package FluentValidation                                    --version 11.10.0
dotnet add 'Services/FSI.Trade.Compliance.Application'    package FluentValidation.DependencyInjectionExtensions      --version 11.10.0
# IApplicationDbContext exposes DbSet<T> -> Application needs the EF Core types.
dotnet add 'Services/FSI.Trade.Compliance.Application'    package Microsoft.EntityFrameworkCore                       --version 8.0.10

# ---------- Infrastructure packages ----------
dotnet add 'Services/FSI.Trade.Compliance.Infrastructure' package Microsoft.EntityFrameworkCore                       --version 8.0.10
dotnet add 'Services/FSI.Trade.Compliance.Infrastructure' package Microsoft.EntityFrameworkCore.SqlServer             --version 8.0.10
dotnet add 'Services/FSI.Trade.Compliance.Infrastructure' package Microsoft.EntityFrameworkCore.Design                --version 8.0.10
dotnet add 'Services/FSI.Trade.Compliance.Infrastructure' package Microsoft.Extensions.Identity.Core                  --version 8.0.10
dotnet add 'Services/FSI.Trade.Compliance.Infrastructure' package Microsoft.Extensions.Configuration.Binder           --version 8.0.2
dotnet add 'Services/FSI.Trade.Compliance.Infrastructure' package Microsoft.Extensions.Options.ConfigurationExtensions --version 8.0.0
dotnet add 'Services/FSI.Trade.Compliance.Infrastructure' package Microsoft.AspNetCore.Http.Abstractions              --version 2.3.0
dotnet add 'Services/FSI.Trade.Compliance.Infrastructure' package System.IdentityModel.Tokens.Jwt                     --version 8.10.0
dotnet add 'Services/FSI.Trade.Compliance.Infrastructure' package Otp.NET                                             --version 1.4.0

# ---------- API packages ----------
# Already declared in the .csproj — but keep the explicit add as safety net.
dotnet add 'Services/FSI.Trade.Compliance.API' package Microsoft.AspNetCore.Authentication.JwtBearer                  --version 8.0.10
dotnet add 'Services/FSI.Trade.Compliance.API' package Swashbuckle.AspNetCore                                         --version 6.8.1
dotnet add 'Services/FSI.Trade.Compliance.API' package Serilog.AspNetCore                                             --version 8.0.3

# ---------- Build ----------
dotnet build

Pop-Location
