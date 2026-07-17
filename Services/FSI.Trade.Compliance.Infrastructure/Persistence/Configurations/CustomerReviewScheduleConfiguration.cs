using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class CustomerReviewScheduleConfiguration : IEntityTypeConfiguration<CustomerReviewSchedule>
{
    public void Configure(EntityTypeBuilder<CustomerReviewSchedule> b)
    {
        b.ToTable("TmX_Customer_Review_Schedule", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)                  .HasColumnName("Customer_Review_Schedule_Id");
        b.Property(x => x.TenantId)            .HasColumnName("Tenant_Id");
        b.Property(x => x.CustomerCode)        .HasColumnName("Customer_Code")         .HasMaxLength(50) .IsRequired();
        b.Property(x => x.CurrentRiskCategory) .HasColumnName("Current_Risk_Category") .HasMaxLength(20);
        b.Property(x => x.CurrentRiskScore)    .HasColumnName("Current_Risk_Score");
        b.Property(x => x.NextReviewDate)      .HasColumnName("Next_Review_Date");
        b.Property(x => x.Status)              .HasColumnName("Status")                .HasMaxLength(30) .IsRequired();
        b.Property(x => x.LastReviewedDate)    .HasColumnName("Last_Reviewed_Date");
        b.Property(x => x.LastKycTransactionId).HasColumnName("Last_Kyc_Transaction_Id");
        b.Property(x => x.LastRemark)          .HasColumnName("Last_Remark")           .HasMaxLength(1000);
        b.Property(x => x.CreatedBy)           .HasColumnName("Created_By")            .HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)         .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)       .HasColumnName("Last_Updated_By")       .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)     .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.TenantId, x.CustomerCode }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status, x.NextReviewDate });
    }
}
