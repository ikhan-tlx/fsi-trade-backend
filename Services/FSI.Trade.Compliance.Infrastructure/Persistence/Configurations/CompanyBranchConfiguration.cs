using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class CompanyBranchConfiguration : IEntityTypeConfiguration<CompanyBranch>
{
    public void Configure(EntityTypeBuilder<CompanyBranch> b)
    {
        b.ToTable("TmX_Company_Branch", "dbo");
        b.HasKey(x => x.CompanyBranchId);

        b.Property(x => x.CompanyBranchId)    .HasColumnName("Company_Branch_Id");
        b.Property(x => x.TenantId)           .HasColumnName("Tenant_Id");
        b.Property(x => x.CompanyId)          .HasColumnName("Company_Id");
        b.Property(x => x.BranchCode)         .HasColumnName("Branch_Code").HasMaxLength(20).IsRequired();
        b.Property(x => x.BranchName)         .HasColumnName("Branch_Name").HasMaxLength(100);
        b.Property(x => x.BranchDescription)  .HasColumnName("Branch_Description").HasMaxLength(100);
        b.Property(x => x.LocationId)         .HasColumnName("Location_Id");
        b.Property(x => x.AddressId)          .HasColumnName("Address_Id");
        b.Property(x => x.ActiveFlag)         .HasColumnName("Active_Flag");
        b.Property(x => x.EffectiveStartDate) .HasColumnName("Effective_Start_Date");
        b.Property(x => x.EffectiveEndDate)   .HasColumnName("Effective_End_Date");
        b.Property(x => x.Status)             .HasColumnName("Status").HasMaxLength(50);

        b.Property(x => x.CreatedBy)          .HasColumnName("Created_By").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)        .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)      .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)    .HasColumnName("Last_Updated_Date");
    }
}
