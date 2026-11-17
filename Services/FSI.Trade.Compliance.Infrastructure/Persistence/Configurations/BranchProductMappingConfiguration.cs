using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="BranchProductMapping"/> onto <c>TmX_Branch_Products_Mapping</c>.
/// Read-only — consumed to scope the product LOV (user → branch → product), the same
/// entitlement chain the legacy app used for the new-transaction product step.
/// </summary>
public class BranchProductMappingConfiguration : IEntityTypeConfiguration<BranchProductMapping>
{
    public void Configure(EntityTypeBuilder<BranchProductMapping> b)
    {
        b.ToTable("TmX_Branch_Products_Mapping", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)             .HasColumnName("Branch_Product_Map_ID");
        b.Property(x => x.CompanyBranchId).HasColumnName("Company_Branch_ID");
        b.Property(x => x.ProductId)      .HasColumnName("Loan_Product_ID");
        b.Property(x => x.TenantId)       .HasColumnName("Tenant_ID");
        b.Property(x => x.ActiveFlag)     .HasColumnName("Active_Flag");

        // Hot read paths: products-for-branch and branch-for-product.
        b.HasIndex(x => x.CompanyBranchId);
        b.HasIndex(x => x.ProductId);
    }
}
