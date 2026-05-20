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

namespace FSI.Trade.Compliance.Application.Contracts.Persistence;

public interface IApplicationDbContext
{
    DbSet<ApplicationUser>      Users                { get; }
    DbSet<RefreshToken>         RefreshTokens        { get; }
    DbSet<Role>                 Roles                  { get; }
    DbSet<Privilege>            Privileges             { get; }
    DbSet<RolePrivilegeMapping> RolePrivilegeMappings  { get; }
    DbSet<UserRoleMapping>      UserRoleMappings       { get; }
    DbSet<PasswordChangeAudit>  PasswordChangeAudits   { get; }
    DbSet<Lookup>               Lookups                { get; }
    DbSet<AppConfiguration>     AppConfigurations      { get; }
    DbSet<KycCaseRequest>       KycCaseRequests        { get; }
    DbSet<UserDevice>           UserDevices          { get; }
    DbSet<LoginAuditEntry>      LoginAudit           { get; }

    // Product master — read-mostly; workflow product-mapping endpoint
    // mutates only the WorkflowSchemeCode column on the row.
    DbSet<Product>              Products             { get; }

    // Slice 6: Transaction grid + location/branch scoping.
    DbSet<TransactionListView>     TransactionList         { get; }
    DbSet<CompanyBranchUserMapping> CompanyBranchUserMappings { get; }
    DbSet<CompanyBranch>           CompanyBranches         { get; }
    DbSet<Location>                Locations               { get; }

    // Slice 6 Step 2: Transaction detail (the actual row + children).
    DbSet<Domain.Entities.Transaction.Transaction> Transactions { get; }
    DbSet<TransactionDetail>       TransactionDetails       { get; }
    DbSet<BeneficiaryDetail>       BeneficiaryDetails       { get; }
    DbSet<TransactionStakeholder>  TransactionStakeholders  { get; }
    DbSet<TransactionCustomerSnapshot> TransactionCustomerSnapshots { get; }
    DbSet<CustomerBankingDetail>   CustomerBankingDetails   { get; }
    DbSet<ApplicationChecklist>    ApplicationChecklists    { get; }
    DbSet<ApplicationRemark>       ApplicationRemarks       { get; }
    DbSet<ApplicationDeviationView> ApplicationDeviations   { get; }

    // Slice 6 Step 2: Dynamic-form (tabs + fields per product).
    DbSet<Tab>                     Tabs                     { get; }
    DbSet<EntityTabProductMapping> EntityTabProductMappings { get; }
    DbSet<TenantFieldSetup>        TenantFieldSetups        { get; }

    // OptimaJet-owned tables (read-only from our side).
    DbSet<WorkflowProcessScheme>   WorkflowProcessSchemes   { get; }
    DbSet<WorkflowProcessInstance> WorkflowProcessInstances { get; }
    DbSet<WorkflowInbox>           WorkflowInboxes          { get; }

    // Slice 7: Reports. Templates store the Liquid HTML for each report
    // type (joined to TmX_Lookup REPORT_TYPE rows via Template_Type_Lkp_ID).
    DbSet<Template>                Templates                { get; }

    // Slice 8: Flag catalogue + generic document store.
    DbSet<Document>                Documents                { get; }
    DbSet<FlagCatalogue>           FlagCatalogues           { get; }
    DbSet<FlagCatalogueLocale>     FlagCatalogueLocales     { get; }
    DbSet<FlagScope>               FlagScopes               { get; }
    DbSet<TransactionFlag>         TransactionFlags         { get; }
    DbSet<TransactionFlagHistory>  TransactionFlagHistories { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
