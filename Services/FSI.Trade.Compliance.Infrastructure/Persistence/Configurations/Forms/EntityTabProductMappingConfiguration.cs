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

        b.Property(x => x.CreatedBy)       .HasColumnName("Created_By").HasMaxLength(100);
        b.Property(x => x.CreatedDate)     .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)   .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate) .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.ProductId, x.IsActive });
        b.HasIndex(x => x.TabId);
    }
}
