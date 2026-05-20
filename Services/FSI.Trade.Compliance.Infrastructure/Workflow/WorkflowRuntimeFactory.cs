using System.Reflection;
using System.Xml.Linq;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Infrastructure.Workflow.Actions;
using FSI.Trade.Compliance.Infrastructure.Workflow.Rules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptimaJet.Workflow.Core.Builder;
using OptimaJet.Workflow.Core.Bus;
using OptimaJet.Workflow.Core.Parser;
using OptimaJet.Workflow.Core.Runtime;
using OptimaJet.Workflow.DbPersistence;

namespace FSI.Trade.Compliance.Infrastructure.Workflow;

/// <summary>
/// Singleton bootstrap for the OptimaJet WorkflowRuntime. The runtime is
/// lazily constructed on first access and reused for the process lifetime.
///
/// Slice 5.6 PIN: this binds to OptimaJet 3.5.0 (.NET 8-compatible build of
/// the v3.x line). Same bootstrap shape as legacy
/// <c>tmx-finance-backend/TMX.Workflows/Runtime/WorkflowInit.cs</c> —
/// <c>WorkflowBuilder&lt;XElement&gt;</c> + <c>XmlWorkflowParser</c> +
/// <c>MSSQLProvider</c> — mirror-imaged so the legacy v3.x schemes load
/// without any XML rewrites and the expired
/// <c>techlogix2019:07.24.2020</c> license keeps soft-degrading.
///
/// LICENSE: <see cref="WorkflowOptions.LicenseKey"/> is registered at
/// bootstrap. v3.x logs a warning if the key is expired but the runtime
/// continues to function — this is the entire reason for Slice 5.6's
/// version pin. See <c>docs/OPTIMAJET_POC.md §License</c>.
/// </summary>
public class WorkflowRuntimeFactory
{
    private readonly Lazy<WorkflowRuntime> _runtime;

    public WorkflowRuntimeFactory(
        IServiceProvider                       sp,
        IOptions<WorkflowOptions>              opt,
        IConfiguration                         config,
        ILogger<WorkflowRuntimeFactory>        log)
    {
        _runtime = new Lazy<WorkflowRuntime>(() =>
        {
            var workflowOpt = opt.Value;

            if (string.IsNullOrWhiteSpace(workflowOpt.LicenseKey))
                log.LogWarning("Workflow:LicenseKey is not configured. The v3.x runtime will start but will run unlicensed.");
            else
                WorkflowRuntime.RegisterLicense(workflowOpt.LicenseKey);

            var connStr = config.GetConnectionString("DefaultConnection")
                          ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required for the workflow runtime.");

            // Action + rule providers come from DI so they compose
            // Application contracts (IRoleQueryService, IApplicationDbContext).
            var actions = sp.GetRequiredService<FsiWorkflowActionProvider>();
            var rules   = sp.GetRequiredService<FsiWorkflowRuleProvider>();

            // v3.5 bootstrap: explicit Builder<XElement> + XmlWorkflowParser
            // + MSSQLProvider chain (all-caps class name in v3.x), plus a
            // separate persistence provider on the runtime. Matches the
            // working sample app's WorkflowInit exactly.
            var schemePersistence = new MSSQLProvider(connStr);
            var builder = new WorkflowBuilder<XElement>(
                                schemePersistence,
                                new XmlWorkflowParser(),
                                schemePersistence)
                          .WithDefaultCache();

            var runtime = new WorkflowRuntime(workflowOpt.RuntimeId)
                .WithBuilder(builder)
                .WithActionProvider(actions)
                .WithRuleProvider(rules)
                .WithPersistenceProvider(new MSSQLProvider(connStr))
                .WithTimerManager(new TimerManager())
                .WithBus(new NullBus())
                .SwitchAutoUpdateSchemeBeforeGetAvailableCommandsOn()
                .RegisterAssemblyForCodeActions(Assembly.GetExecutingAssembly())
                .Start();

            log.LogInformation("OptimaJet WorkflowRuntime started (v3.5.0). RuntimeId={RuntimeId}.", workflowOpt.RuntimeId);
            return runtime;
        });
    }

    /// <summary>The (lazily-constructed) workflow runtime instance.</summary>
    public WorkflowRuntime Runtime => _runtime.Value;
}
