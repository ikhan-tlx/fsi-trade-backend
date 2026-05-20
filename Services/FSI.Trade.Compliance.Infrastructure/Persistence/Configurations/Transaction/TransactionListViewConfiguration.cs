using FSI.Trade.Compliance.Domain.Entities.Transaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Transaction;

/// <summary>
/// Maps <see cref="TransactionListView"/> onto the SQL view
/// <c>TmX_Transaction_VW</c>. <see cref="ToView"/> ensures EF Core treats
/// this as keyless / non-tracked from the start and never attempts to
/// generate migrations against it. Column names mirror the view definition
/// verbatim (CTE produces them with the underscore casing).
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> — search for
/// <c>CREATE VIEW [dbo].[TmX_Transaction_VW]</c>.
/// </summary>
public class TransactionListViewConfiguration : IEntityTypeConfiguration<TransactionListView>
{
    public void Configure(EntityTypeBuilder<TransactionListView> b)
    {
        b.ToView("TmX_Transaction_VW", "dbo");
        b.HasNoKey();

        b.Property(x => x.TransactionId)           .HasColumnName("Transaction_ID");
        b.Property(x => x.TenantId)                .HasColumnName("Tenant_Id");
        b.Property(x => x.CompanyBranchId)         .HasColumnName("Company_Branch_Id");
        b.Property(x => x.ProcessInstanceId)       .HasColumnName("Process_Instance_Id");
        b.Property(x => x.ClientReferenceNumber)   .HasColumnName("Client_Reference_Number");

        b.Property(x => x.CreatedBy)               .HasColumnName("Created_By");
        b.Property(x => x.CreatedDate)             .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)           .HasColumnName("Last_Updated_By");
        b.Property(x => x.LastUpdatedDate)         .HasColumnName("Last_Updated_Date");

        b.Property(x => x.IsWorkflowAttached)      .HasColumnName("Is_Workflow_Attached");
        b.Property(x => x.TransactionTypeLkp)      .HasColumnName("Transaction_Type_Lkp");
        b.Property(x => x.TransactionDate)         .HasColumnName("Transaction_Date");
        b.Property(x => x.TransactionType)         .HasColumnName("Transaction_Type");
        b.Property(x => x.TransactionNumber)       .HasColumnName("Transaction_Number");
        b.Property(x => x.TransactionStatusLkp)    .HasColumnName("Transaction_Status_Lkp");

        b.Property(x => x.CustomerCode)            .HasColumnName("Customer_Code");
        b.Property(x => x.NationalIdentifierValue) .HasColumnName("National_Identifier_Value");
        b.Property(x => x.CustomerName)            .HasColumnName("Customer_Name");

        b.Property(x => x.ProductId)               .HasColumnName("Product_Id");
        b.Property(x => x.ProductName)             .HasColumnName("Product_Name");
        b.Property(x => x.BranchName)              .HasColumnName("Branch_Name");

        b.Property(x => x.Creator)                 .HasColumnName("Creator");
        b.Property(x => x.CreatorName)             .HasColumnName("Creator_Name");
        b.Property(x => x.CreatorId)               .HasColumnName("Creator_Id");
        b.Property(x => x.CreatorLocationId)       .HasColumnName("Creator_Location_Id");

        b.Property(x => x.CurrentState)            .HasColumnName("Current_State");
        b.Property(x => x.InboxUserId)             .HasColumnName("Inbox_User_ID");
        b.Property(x => x.InboxUser)               .HasColumnName("Inbox_User");
        b.Property(x => x.InboxName)               .HasColumnName("Inbox_Name");
        b.Property(x => x.Status)                  .HasColumnName("Status");
    }
}
