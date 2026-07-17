using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Documents;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Application.Contracts.Locations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Reports;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using FSI.Trade.Compliance.Domain.Entities;
using FSI.Trade.Compliance.Infrastructure.Documents;
using FSI.Trade.Compliance.Infrastructure.Identity;
using FSI.Trade.Compliance.Infrastructure.Integrations.Brains;
using FSI.Trade.Compliance.Infrastructure.Integrations.CustomerMaster;
using FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;
using FSI.Trade.Compliance.Infrastructure.Locations;
using FSI.Trade.Compliance.Infrastructure.Persistence;
using FSI.Trade.Compliance.Infrastructure.Persistence.Interceptors;
using FSI.Trade.Compliance.Infrastructure.Persistence.Repositories;
using FSI.Trade.Compliance.Infrastructure.Reports;
using FSI.Trade.Compliance.Infrastructure.Services;
using FSI.Trade.Compliance.Infrastructure.Workflow;
using FSI.Trade.Compliance.Infrastructure.Workflow.Actions;
using FSI.Trade.Compliance.Infrastructure.Workflow.Rules;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>           (config.GetSection(JwtOptions.SectionName));
        services.Configure<AuthOptions>          (config.GetSection(AuthOptions.SectionName));
        services.Configure<PasswordPolicyOptions>(config.GetSection(PasswordPolicyOptions.SectionName));
        services.Configure<TwoFactorOptions>     (config.GetSection(TwoFactorOptions.SectionName));
        services.Configure<IntegrationOptions>   (config.GetSection(IntegrationOptions.SectionName));
        services.Configure<PeriodicReviewOptions>(config.GetSection(PeriodicReviewOptions.SectionName));
        services.Configure<BrainsOptions>        (config.GetSection(BrainsOptions.SectionName));
        services.Configure<WorkflowOptions>      (config.GetSection(WorkflowOptions.SectionName));
        services.Configure<ReportOptions>        (config.GetSection(ReportOptions.SectionName));
        services.Configure<DocumentOptions>      (config.GetSection(DocumentOptions.SectionName));
        services.Configure<TradeRepositoryOptions>(config.GetSection(TradeRepositoryOptions.SectionName));
        services.Configure<CustomerInsightSnapshotOptions>(config.GetSection(CustomerInsightSnapshotOptions.SectionName));

        // EF Core (+ generic audit interceptor — writes TmX_Audit_Log on every save)
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<ApplicationDbContext>((sp, opt) =>
        {
            opt.UseSqlServer(config.GetConnectionString("DefaultConnection"),
                             sql => sql.EnableRetryOnFailure());
            opt.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // Explicit audit writer (reads / login / export / failures — events the
        // SaveChanges interceptor cannot see).
        services.AddScoped<FSI.Trade.Compliance.Application.Contracts.Audit.IAuditWriter,
                           FSI.Trade.Compliance.Infrastructure.Persistence.Auditing.AuditWriter>();

        // Auto-flag rule engine (P1: reference-list / field / static archetypes).
        services.AddScoped<FSI.Trade.Compliance.Application.Contracts.Flags.IAutoFlagEvaluator,
                           FSI.Trade.Compliance.Infrastructure.Flags.AutoFlagEvaluator>();

        // Phase 1 aggregate engine — metric handler registry (compute-on-read, no fact table).
        services.AddScoped<FSI.Trade.Compliance.Infrastructure.Flags.Metrics.IRuleDataProvider,
                           FSI.Trade.Compliance.Infrastructure.Flags.Metrics.RuleDataProvider>();
        services.AddScoped<FSI.Trade.Compliance.Infrastructure.Flags.Metrics.IRuleMetricHandler,
                           FSI.Trade.Compliance.Infrastructure.Flags.Metrics.AggregateMetricHandler>();
        services.AddScoped<FSI.Trade.Compliance.Infrastructure.Flags.Metrics.IRuleMetricHandler,
                           FSI.Trade.Compliance.Infrastructure.Flags.Metrics.RecencyMetricHandler>();
        services.AddScoped<FSI.Trade.Compliance.Infrastructure.Flags.Metrics.IRuleMetricHandler,
                           FSI.Trade.Compliance.Infrastructure.Flags.Metrics.VarianceMetricHandler>();
        services.AddScoped<FSI.Trade.Compliance.Infrastructure.Flags.Metrics.MetricHandlerRegistry>();

        // Slice 9 — FCCM staging lives on the same Oracle DB the rest
        // of FccmOracleReader uses (not a separate SQL Server). No
        // additional DbContext registration — all Oracle access flows
        // through FccmOracleReader via Oracle.ManagedDataAccess.Core.

        // Identity (Pattern A — IdentityCore + custom store, NO AspNet* tables)
        services.AddIdentityCore<ApplicationUser>(o =>
        {
            o.User.RequireUniqueEmail              = false;
            o.Password.RequireDigit                = false;
            o.Password.RequireLowercase            = false;
            o.Password.RequireUppercase            = false;
            o.Password.RequireNonAlphanumeric      = false;
            o.Password.RequiredLength              = 6;
            o.Lockout.MaxFailedAccessAttempts      = 5;
            o.Lockout.DefaultLockoutTimeSpan       = TimeSpan.FromMinutes(3);
            o.Lockout.AllowedForNewUsers           = true;
        })
        .AddUserStore<TmxUserStore>()
        .AddDefaultTokenProviders();

        services.Configure<PasswordHasherOptions>(o =>
            o.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3);

        // Tokens
        services.AddScoped<IJwtTokenService,            JwtTokenService>();
        services.AddScoped<IRefreshTokenStore,          RefreshTokenStore>();

        // Identity primitives (Application ports)
        services.AddScoped<IUserAuthenticationService,  UserAuthenticationService>();
        services.AddScoped<ITwoFactorVerifier,          TotpTwoFactorVerifier>();
        services.AddScoped<ITwoFactorSecretGenerator,   TotpSecretGenerator>();

        // Misc per-request
        services.AddScoped<ICurrentUserService,         CurrentUserService>();
        services.AddScoped<ICurrentDeviceService,       CurrentDeviceService>();

        // Persistence ports
        services.AddScoped<IRoleQueryService,           RoleQueryService>();
        services.AddScoped<IPasswordChangeAuditService, PasswordChangeAuditService>();
        services.AddScoped<IDeviceService,              DeviceService>();
        services.AddScoped<ILoginAuditService,          LoginAuditService>();
        services.AddScoped<IPrivilegeService,           PrivilegeService>();

        // ---------- Slice 4: Integrations ----------
        // Resilience policy (~3 attempts, 30s timeout, 5-failure breaker) per BACKLOG.
        // Applied as a typed-HttpClient handler stack so each upstream client gets
        // the same retry/breaker/timeout treatment without reimplementing.
        services.AddHttpClient<BrainsKycScreeningService>((sp, http) =>
                {
                    http.Timeout = sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.HttpTimeout;
                })
                .AddStandardResilienceHandler();

        services.AddHttpClient<CustomerMasterClient>((sp, http) =>
                {
                    http.Timeout = sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.HttpTimeout;
                })
                .AddStandardResilienceHandler();

        services.AddHttpClient<FccmHttpClient>((sp, http) =>
                {
                    http.Timeout = sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.HttpTimeout;
                })
                .AddStandardResilienceHandler();

        // Application contracts wired to Infrastructure adapters. Vendor names
        // (Brains, Fccm) appear ONLY here — never in Application or API.
        // For the typed HttpClients we must resolve via factory delegate, not
        // a second concrete-type registration — otherwise the resilience
        // pipeline configured by AddHttpClient<T> wouldn't apply when the
        // interface is resolved.
        // BRAINS KYC screening — Live calls the real servlet; Sandbox returns
        // canned responses so the UI isn't blocked while localhost BRAINS is
        // down. Selected by Integration:Brains:Mode (Program.cs blocks Sandbox
        // in Production). The typed HttpClient<BrainsKycScreeningService> above
        // stays registered either way (harmless when unused in Sandbox).
        var brainsMode = config.GetSection(BrainsOptions.SectionName)["Mode"];
        if (string.Equals(brainsMode, nameof(BrainsMode.Sandbox), StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IKycScreeningService, SandboxKycScreeningService>();
        else
            services.AddScoped<IKycScreeningService>(sp => sp.GetRequiredService<BrainsKycScreeningService>());

        // Slice 11 P0 §1 — ICustomerMasterService is now backed by
        // LocalCustomerMasterReader (reads TmX_Customer_Master snapshots)
        // instead of CustomerMasterClient (which proxied to BRAINS). KYC
        // lookups stay on BRAINS via IKycScreeningService above. The HTTP
        // client registration above is left in place so flipping the
        // adapter back is a single-line change if requirements shift.
        services.AddScoped<ICustomerMasterService, LocalCustomerMasterReader>();

        // FccmKycCaseService isn't a typed HttpClient itself (it COMPOSES
        // FccmHttpClient + Oracle reader + DbContext), so it can register as
        // a normal scoped service mapped to the interface.
        services.AddScoped<IKycCaseService,             FccmKycCaseService>();

        // Risk Assessment tab — privileged (Kyc.Operate) operational actions
        // (retry / re-poll / re-fetch). Reuses the poller's shared reconcile.
        services.AddScoped<IKycCaseOperationsService,   KycCaseOperationsService>();

        // Slice 9 — Trade-to-FCCM submission. Same composition pattern as
        // FccmKycCaseService: scoped, composes the shared FccmHttpClient
        // + FccmOracleReader + DbContext. Phase 1 ships the scaffold;
        // Phase 2 fills in the pipeline.
        services.AddScoped<IFccmTradeCaseService,       FccmTradeCaseService>();

        // Oracle reader stub — see file header for real-impl wiring.
        services.AddScoped<FccmOracleReader>();

        // KYC onboarding risk-parameter derivation (legacy SetRealtimeValues
        // parity): reads TmX_Transaction_Detail + TmX_Customer_Master UDF +
        // TmX_Lookup, and the Oracle FCC_OB_RISK_PRMS_CODE_SCRE_MAP max-score.
        services.AddScoped<KycOnboardingFieldResolver>();

        // Configurable onboarding payload mapping (externalized field mappings).
        // Provider is a singleton (loads + caches profile files); the mapper is
        // scoped (uses the request-scoped DbContext + Oracle reader). Active
        // profile chosen by Integration:Onboarding:MappingProfile.
        services.AddSingleton<Integrations.Fccm.Mapping.IOnboardingMappingProvider,
                              Integrations.Fccm.Mapping.OnboardingMappingProvider>();
        services.AddScoped<Integrations.Fccm.Mapping.OnboardingPayloadMapper>();

        // Periodic Customer Review — cadence policy + next-review-date calculator.
        services.AddSingleton<Application.Contracts.Kyc.IReviewPolicyProvider,     Kyc.Review.ReviewPolicyProvider>();
        services.AddSingleton<Application.Contracts.Kyc.INextReviewDateCalculator, Kyc.Review.NextReviewDateCalculator>();
        services.AddSingleton<Application.Contracts.Identity.IAmbientUserContext,   Services.AmbientUserContext>();
        services.AddScoped<Application.Contracts.Kyc.IKycTransactionCloner,        Kyc.Review.KycTransactionCloner>();
        services.AddScoped<Application.Contracts.Kyc.IKycReviewOrchestrator,       Kyc.Review.KycReviewOrchestrator>();
        services.AddScoped<Application.Contracts.Kyc.IPeriodicReviewRunner,        Kyc.Review.PeriodicReviewRunner>();
        services.AddHostedService<Kyc.Review.PeriodicReviewService>();

        // Customer Intelligence daily KPI snapshot — writes TmX_Customer_Insight_Daily
        // (one book-wide row/day) so the portfolio strip shows real "vs last week" deltas.
        services.AddScoped<Application.Contracts.Customer.ICustomerInsightSnapshotWriter, CustomerInsight.CustomerInsightSnapshotWriter>();
        services.AddHostedService<CustomerInsight.CustomerInsightSnapshotService>();

        // Diagnostic log archiver — zips rolled log files older than the
        // retention window into backlogs_<from>-<to>.zip (config-driven).
        services.AddHostedService<Diagnostics.LogArchiverService>();

        // Background poller — drives KycCaseRequest rows through the state machine.
        services.AddHostedService<FccmCaseIdPoller>();

        // Aggregate auto-flag queue worker — evaluates cross-transaction flags off the save path.
        services.AddHostedService<Flags.AutoFlagJobWorker>();

        // ---------- Slice 5: Workflow ----------
        // Action + rule providers consume Application contracts via DI.
        // Singleton lifetimes match the WorkflowRuntime singleton.
        services.AddSingleton<FsiWorkflowActionProvider>();
        services.AddSingleton<FsiWorkflowRuleProvider>();
        // Keeps dbo.WorkflowInbox in step with the engine on every transition
        // (legacy WorkflowInit.ProcessStatusChanged parity). Singleton: it only
        // holds an IServiceScopeFactory and is wired to the runtime singleton.
        services.AddSingleton<WorkflowInboxSynchronizer>();

        // Runtime factory — singleton; lazily constructs the WorkflowRuntime
        // on first access (so the host doesn't fail to start if a workflow
        // dep is misconfigured but you never need workflow during this run).
        services.AddSingleton<WorkflowRuntimeFactory>();

        // The Application-facing port. Scoped because it composes
        // IApplicationDbContext (which is Scoped). The factory under the hood
        // returns the same WorkflowRuntime singleton regardless.
        services.AddScoped<IWorkflowEngine, OptimaJetWorkflowEngine>();

        // ---------- Slice 6: Transaction grid + scoping ----------
        // Walks TmX_Location in-memory; scoped because it composes the DbContext.
        services.AddScoped<ILocationHierarchyService, LocationHierarchyService>();

        // Slice 6 Step 3: transaction number generator — direct sequence call to
        // dbo.TmX_Transaction_Sequence (retires legacy sp_GetNextTransactionSequenceNumber).
        services.AddScoped<ITransactionNumberGenerator, Persistence.Repositories.TransactionNumberGenerator>();

        // In-memory cache for product-scoped reads (tabs + form definition).
        // The DI helper is idempotent — safe to call even if AspNetCore already
        // registered it via AddRouting / etc.
        services.AddMemoryCache();

        // ---------- Slice 7: Reports ----------
        // Generic SP runner — used by the report stack to execute the SP
        // referenced by each report template. Validated against the
        // REPORT_TYPE lookup allowlist before this layer ever sees it.
        services.AddScoped<IStoredProcedureRunner, StoredProcedureRunner>();

        // HTML renderer (DotLiquid). Stateless, safe as singleton.
        services.AddSingleton<IReportHtmlRenderer, DotLiquidReportHtmlRenderer>();

        // PDF generator (PuppeteerSharp). MUST be singleton — keeps a
        // single Chromium instance alive and reuses it across requests.
        // Launching Chromium per-call would add ~700ms/request.
        services.AddSingleton<IReportPdfGenerator,  PuppeteerReportPdfGenerator>();

        // Excel exporter (ClosedXML). Stateless, safe as singleton.
        services.AddSingleton<IReportExcelExporter, ClosedXmlReportExcelExporter>();

        // ---------- Slice 8: Document storage ----------
        // Scoped because it composes IApplicationDbContext (also scoped)
        // for STORAGE_PROVIDER lookup resolution. The filesystem
        // operations themselves are stateless — could be singleton if we
        // moved the lookup resolution into a one-time bootstrap.
        services.AddScoped<IDocumentStorage, LocalDiskDocumentStorage>();

        return services;
    }
}
