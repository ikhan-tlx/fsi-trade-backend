using FSI.Trade.Compliance.Domain.Entities.Transaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Transaction;

public class BeneficiaryDetailConfiguration : IEntityTypeConfiguration<BeneficiaryDetail>
{
    public void Configure(EntityTypeBuilder<BeneficiaryDetail> b)
    {
        b.ToTable("TmX_Beneficiary_Detail", "dbo");
        b.HasKey(x => x.BeneficiaryDetailId);

        b.Property(x => x.BeneficiaryDetailId) .HasColumnName("Beneficiary_Detail_ID");
        b.Property(x => x.TransactionId)       .HasColumnName("Transaction_Id");
        b.Property(x => x.TenantId)            .HasColumnName("Tenant_Id");
        b.Property(x => x.UdfData)             .HasColumnName("UDF_Data");
        b.Property(x => x.CreatedBy)           .HasColumnName("Created_By").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)         .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)       .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)     .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => x.TransactionId);
    }
}
