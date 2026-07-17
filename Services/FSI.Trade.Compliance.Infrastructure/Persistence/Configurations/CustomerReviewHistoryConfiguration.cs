using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class CustomerReviewHistoryConfiguration : IEntityTypeConfiguration<CustomerReviewHistory>
{
    public void Configure(EntityTypeBuilder<CustomerReviewHistory> b)
    {
        b.ToTable("TmX_Customer_Review_History", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)                  .HasColumnName("Customer_Review_History_Id");
        b.Property(x => x.TenantId)            .HasColumnName("Tenant_Id");
        b.Property(x => x.CustomerCode)        .HasColumnName("Customer_Code")        .HasMaxLength(50) .IsRequired();
        b.Property(x => x.DueDate)             .HasColumnName("Due_Date");
        b.Property(x => x.Outcome)             .HasColumnName("Outcome")              .HasMaxLength(30) .IsRequired();
        b.Property(x => x.SourceTransactionId) .HasColumnName("Source_Transaction_Id");
        b.Property(x => x.NewTransactionId)    .HasColumnName("New_Transaction_Id");
        b.Property(x => x.KycRequestId)        .HasColumnName("Kyc_Request_Id");
        b.Property(x => x.Remarks)             .HasColumnName("Remarks")              .HasMaxLength(1000);
        b.Property(x => x.CreatedBy)           .HasColumnName("Created_By")           .HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)         .HasColumnName("Created_Date");

        b.HasIndex(x => new { x.TenantId, x.CustomerCode, x.CreatedDate });
        b.HasIndex(x => new { x.Outcome, x.CreatedDate });
    }
}
