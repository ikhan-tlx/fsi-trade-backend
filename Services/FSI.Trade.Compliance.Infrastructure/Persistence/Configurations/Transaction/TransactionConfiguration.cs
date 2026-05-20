using FSI.Trade.Compliance.Domain.Entities.Transaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Transaction;

public class TransactionConfiguration : IEntityTypeConfiguration<Domain.Entities.Transaction.Transaction>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Transaction.Transaction> b)
    {
        b.ToTable("TmX_Transaction", "dbo");
        b.HasKey(x => x.TransactionId);

        b.Property(x => x.TransactionId)         .HasColumnName("Transaction_Id");
        b.Property(x => x.TenantId)              .HasColumnName("Tenant_Id");
        b.Property(x => x.CompanyBranchId)       .HasColumnName("Company_Branch_Id");
        b.Property(x => x.ProductId)             .HasColumnName("Product_Id");
        b.Property(x => x.ClientReferenceNumber) .HasColumnName("Client_Reference_Number").HasMaxLength(100);
        b.Property(x => x.UserId)                .HasColumnName("User_Id").HasMaxLength(50);
        b.Property(x => x.CurrencyId)            .HasColumnName("Currency_Id");
        b.Property(x => x.TransactionStatusLkp)  .HasColumnName("Transaction_Status_Lkp");
        b.Property(x => x.TransactionNumber)     .HasColumnName("Transaction_Number").HasMaxLength(100);
        b.Property(x => x.ProcessInstanceId)     .HasColumnName("Process_Instance_Id");
        b.Property(x => x.MobileId)              .HasColumnName("Mobile_ID");
        b.Property(x => x.IsWorkflowAttached)    .HasColumnName("Is_Workflow_Attached");
        b.Property(x => x.TransactionTypeLkp)    .HasColumnName("Transaction_Type_Lkp");
        b.Property(x => x.TransactionDate)       .HasColumnName("Transaction_Date");

        b.Property(x => x.CreatedBy)             .HasColumnName("Created_By").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)           .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)         .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)       .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => x.ProcessInstanceId);
        b.HasIndex(x => x.ProductId);
    }
}
