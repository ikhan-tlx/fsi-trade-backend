using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using FSI.Trade.Compliance.Domain.Entities.Application;
using FSI.Trade.Compliance.Domain.Entities.Customer;
using FSI.Trade.Compliance.Domain.Entities.Documents;
using FSI.Trade.Compliance.Domain.Entities.Flags;
using FSI.Trade.Compliance.Domain.Entities.Forms;
using FSI.Trade.Compliance.Domain.Entities.Reports;
using FSI.Trade.Compliance.Domain.Entities.Transaction;
using FSI.Trade.Compliance.Domain.Entities.Workflow;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<ApplicationUser>      Users                => Set<ApplicationUser>();
    public DbSet<RefreshToken>         RefreshTokens        => Set<RefreshToken>();
    public DbSet<Role>                 Roles                  => Set<Role>();
    public DbSet<Privilege>            Privileges             => Set<Privilege>();
    public DbSet<RolePrivilegeMapping> RolePrivilegeMappings  => Set<RolePrivilegeMapping>();
    public DbSet<UserRoleMapping>      UserRoleMappings       => Set<UserRoleMapping>();
    public DbSet<PasswordChangeAudit>  PasswordChangeAudits   => Set<PasswordChangeAudit>();
    public DbSet<Lookup>               Lookups                => Set<Lookup>();
    public DbSet<AppConfiguration>     AppConfigurations      => Set<AppConfiguration>();
    public DbSet<KycCaseRequest>       KycCaseRequests        => Set<KycCaseRequest>();
    public DbSet<UserDevice>           UserDevices          => Set<UserDevice>();
    public DbSet<LoginAuditEntry>      LoginAudit           => Set<LoginAuditEntry>();
    public DbSet<Product>              Products             => Set<Product>();

    // Slice 6: Transaction grid + location/branch scoping.
    public DbSet<TransactionListView>      TransactionList           => Set<TransactionListView>();
    public DbSet<CompanyBranchUserMapping> CompanyBranchUserMappings => Set<CompanyBranchUserMapping>();
    public DbSet<CompanyBranch>            CompanyBranches           => Set<CompanyBranch>();
    public DbSet<Location>                 Locations                 => Set<Location>();

    // Slice 6 Step 2: Transaction detail (row + children).
    public DbSet<Domain.Entities.Transaction.Transaction> Transactions => Set<Domain.Entities.Transaction.Transaction>();
    public DbSet<TransactionDetail>        TransactionDetails       => Set<TransactionDetail>();
    public DbSet<BeneficiaryDetail>        BeneficiaryDetails       => Set<BeneficiaryDetail>();
    public DbSet<TransactionStakeholder>   TransactionStakeholders  => Set<TransactionStakeholder>();
    public DbSet<TransactionCustomerSnapshot> TransactionCustomerSnapshots => Set<TransactionCustomerSnapshot>();
    public DbSet<CustomerBankingDetail>    CustomerBankingDetails   => Set<CustomerBankingDetail>();
    public DbSet<ApplicationChecklist>     ApplicationChecklists    => Set<ApplicationChecklist>();
    public DbSet<ApplicationRemark>        ApplicationRemarks       => Set<ApplicationRemark>();
    public DbSet<ApplicationDeviationView> ApplicationDeviations    => Set<ApplicationDeviationView>();

    // Slice 6 Step 2: Dynamic-form (tabs + fields per product).
    public DbSet<Tab>                      Tabs                     => Set<Tab>();
    public DbSet<EntityTabProductMapping>  EntityTabProductMappings => Set<EntityTabProductMapping>();
    public DbSet<TenantFieldSetup>         TenantFieldSetups        => Set<TenantFieldSetup>();

    // OptimaJet-owned tables (read-only from our side).
    public DbSet<WorkflowProcessScheme>   WorkflowProcessSchemes   => Set<WorkflowProcessScheme>();
    public DbSet<WorkflowProcessInstance> WorkflowProcessInstances => Set<WorkflowProcessInstance>();
    public DbSet<WorkflowInbox>           WorkflowInboxes          => Set<WorkflowInbox>();

    // Slice 7: Reports.
    public DbSet<Template>                Templates                => Set<Template>();

    // Slice 8: Flag catalogue + generic document store.
    public DbSet<Document>                Documents                => Set<Document>();
    public DbSet<FlagCatalogue>           FlagCatalogues           => Set<FlagCatalogue>();
    public DbSet<FlagCatalogueLocale>     FlagCatalogueLocales     => Set<FlagCatalogueLocale>();
    public DbSet<FlagScope>               FlagScopes               => Set<FlagScope>();
    public DbSet<TransactionFlag>         TransactionFlags         => Set<TransactionFlag>();
    public DbSet<TransactionFlagHistory>  TransactionFlagHistories => Set<TransactionFlagHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
