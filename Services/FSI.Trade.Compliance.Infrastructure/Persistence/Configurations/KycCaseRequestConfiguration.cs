using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class KycCaseRequestConfiguration : IEntityTypeConfiguration<KycCaseRequest>
{
    public void Configure(EntityTypeBuilder<KycCaseRequest> b)
    {
        b.ToTable("KycCaseRequest", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)              .HasColumnName("Request_Id");
        b.Property(x => x.TenantId)        .HasColumnName("Tenant_Id");
        b.Property(x => x.CustomerId)      .HasColumnName("Customer_Id")     .HasMaxLength(100).IsRequired();
        b.Property(x => x.TransactionId)   .HasColumnName("Transaction_Id");
        b.Property(x => x.SubmittedBy)     .HasColumnName("Submitted_By")    .HasMaxLength(100).IsRequired();
        b.Property(x => x.SubmittedAt)     .HasColumnName("Submitted_At");

        b.Property(x => x.FccmRequestId)   .HasColumnName("Fccm_Request_Id") .HasMaxLength(100);
        b.Property(x => x.FccmCaseId)      .HasColumnName("Fccm_Case_Id")    .HasMaxLength(100);
        b.Property(x => x.RiskCategoryKey).HasColumnName("Risk_Category_Key").HasMaxLength(50);

        b.Property(x => x.Status)          .HasColumnName("Status")          .HasMaxLength(50).IsRequired();
        b.Property(x => x.LastPolledAt)    .HasColumnName("Last_Polled_At");
        b.Property(x => x.ErrorDetail)     .HasColumnName("Error_Detail")    .HasMaxLength(1000);
        b.Property(x => x.LastUpdatedAt)   .HasColumnName("Last_Updated_At");

        b.HasIndex(x => x.Status);
        b.HasIndex(x => new { x.CustomerId, x.SubmittedAt });
    }
}
