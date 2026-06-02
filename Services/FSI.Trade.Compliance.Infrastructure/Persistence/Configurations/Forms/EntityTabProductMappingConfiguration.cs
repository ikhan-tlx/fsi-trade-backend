using FSI.Trade.Compliance.Domain.Entities.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Forms;

public class EntityTabProductMappingConfiguration : IEntityTypeConfiguration<EntityTabProductMapping>
{
    public void Configure(EntityTypeBuilder<EntityTabProductMapping> b)
    {
        b.ToTable("TmX_Entity_Tab_Product_Mapping", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)              .HasColumnName("Entity_Tab_Product_Mapping_Id");
        b.Property(x => x.TenantId)        .HasColumnName("Tenant_Id");
        b.Property(x => x.EntityId)        .HasColumnName("Entity_Id");
        b.Property(x => x.ProductId)       .HasColumnName("Product_Id");
        b.Property(x => x.TabId)           .HasColumnName("Tab_Id");
        b.Property(x => x.ParentTabId)     .HasColumnName("Parent_Tab_Id");
        b.Property(x => x.IsActive)        .HasColumnName("Is_Active");
        b.Property(x => x.SortOrder)       .HasColumnName("Sort_Order");

        // Added by migration 20260522_001_DynamicFormEnhancements.
        // BIT NOT NULL DEFAULT 1. Surface in API responses but NEVER
        // filter on it — the FE owns the decision of whether to render
        // a tab in the rail.
        b.Property(x => x.IsVisible)       .HasColumnName("Is_Visible");

        b.Property(x => x.CreatedBy)       .HasColumnName("Created_By").HasMaxLength(100);
        b.Property(x => x.CreatedDate)     .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)   .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate) .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.ProductId, x.IsActive });
        b.HasIndex(x => x.TabId);
    }
}
