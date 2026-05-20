using FSI.Trade.Compliance.Domain.Entities.Flags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Flags;

public class FlagScopeConfiguration : IEntityTypeConfiguration<FlagScope>
{
    public void Configure(EntityTypeBuilder<FlagScope> b)
    {
        b.ToTable("TmX_Flag_Scope", "dbo");
        b.HasKey(x => x.FlagScopeId);

        b.Property(x => x.FlagScopeId)          .HasColumnName("Flag_Scope_ID");
        b.Property(x => x.FlagId)               .HasColumnName("Flag_ID");
        b.Property(x => x.ProductId)            .HasColumnName("Product_ID");
        b.Property(x => x.TabId)                .HasColumnName("Tab_ID");
        b.Property(x => x.SortOrder)            .HasColumnName("Sort_Order");
        b.Property(x => x.ActiveFlag)           .HasColumnName("Active_Flag");
        b.Property(x => x.LegacyFieldName)      .HasColumnName("Legacy_Field_Name")  .HasMaxLength(50);
        b.Property(x => x.CreatedBy)            .HasColumnName("Created_By")         .HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)          .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)        .HasColumnName("Last_Updated_By")    .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)      .HasColumnName("Last_Updated_Date");

        // Filtered unique indexes intentionally NOT defined here — they
        // exist in SQL (2026_05_011) as separate indexes for NULL/non-NULL
        // Tab_ID. EF would generate them as combined which SQL Server
        // wouldn't allow with our nullable-column unique semantics.

        // Hot index for form-render: "all active flags for this (Product, Tab)".
        b.HasIndex(x => new { x.ProductId, x.TabId });
        b.HasIndex(x => x.LegacyFieldName);
    }
}
