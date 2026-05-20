using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the minimal slice of <c>TmX_Product</c> we need in Slice 5 for the
/// workflow product-mapping endpoints. Slice 6 will expand the entity (and
/// add sibling tables — cycles, parameters, rate details, etc.) when full
/// Product CRUD lands. Unused columns intentionally NOT mapped — keeping the
/// surface narrow until the Product domain itself is in scope.
/// </summary>
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("TmX_Product", "dbo");
        b.HasKey(p => p.ProductId);

        b.Property(p => p.ProductId)          .HasColumnName("Product_ID");
        b.Property(p => p.ProductCode)        .HasColumnName("Product_Code").HasMaxLength(50);
        b.Property(p => p.ProductName)        .HasColumnName("Product_Name").HasMaxLength(100).IsRequired();
        b.Property(p => p.ProductDescription) .HasColumnName("Product_Description").HasMaxLength(100);
        b.Property(p => p.ProductTypeLkp)     .HasColumnName("Product_Type_Lkp");
        b.Property(p => p.WorkflowSchemeCode) .HasColumnName("Workflow_Scheme_Code").HasMaxLength(100);
        b.Property(p => p.CurrencyId)         .HasColumnName("Currency_ID");
        b.Property(p => p.ActiveFlag)         .HasColumnName("Active_Flag");
        b.Property(p => p.EffectiveStartDate) .HasColumnName("Effective_Start_Date");
        b.Property(p => p.EffectiveEndDate)   .HasColumnName("Effective_End_Date");
        b.Property(p => p.LastUpdatedBy)      .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(p => p.LastUpdatedDate)    .HasColumnName("Last_Updated_Date");

        // Workflow→Product fan-out is the primary read path here.
        b.HasIndex(p => p.WorkflowSchemeCode);
    }
}
