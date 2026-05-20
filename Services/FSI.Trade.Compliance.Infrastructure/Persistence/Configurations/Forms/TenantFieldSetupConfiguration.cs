using FSI.Trade.Compliance.Domain.Entities.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Forms;

public class TenantFieldSetupConfiguration : IEntityTypeConfiguration<TenantFieldSetup>
{
    public void Configure(EntityTypeBuilder<TenantFieldSetup> b)
    {
        b.ToTable("TmX_Tenant_Field_Setup", "dbo");
        b.HasKey(x => x.TenantFieldSetupId);

        b.Property(x => x.TenantFieldSetupId)       .HasColumnName("Tenant_Field_Setup_Id");
        b.Property(x => x.TenantId)                 .HasColumnName("Tenant_Id");
        b.Property(x => x.ProductId)                .HasColumnName("Product_Id");
        b.Property(x => x.TabId)                    .HasColumnName("Tab_Id");
        b.Property(x => x.ParentTenantFieldSetupId) .HasColumnName("Parent_Tenant_Field_Setup_Id");

        b.Property(x => x.FieldName)                .HasColumnName("Field_Name").HasMaxLength(50);
        b.Property(x => x.FieldLabel)               .HasColumnName("Field_Label");
        b.Property(x => x.FieldTypeLkp)             .HasColumnName("Field_Type_Lkp");
        b.Property(x => x.FieldSequence)            .HasColumnName("Field_Sequence");
        b.Property(x => x.FieldTableName)           .HasColumnName("Field_Table_Name").HasMaxLength(100);
        b.Property(x => x.FieldLookupType)          .HasColumnName("Field_Lookup_Type").HasMaxLength(50);
        b.Property(x => x.FieldLength)              .HasColumnName("Field_Length").HasMaxLength(1000);
        b.Property(x => x.MinLength)                .HasColumnName("Min_Length").HasMaxLength(1000);

        b.Property(x => x.IsMandatory)              .HasColumnName("Is_Mandatory").HasMaxLength(999);
        b.Property(x => x.IsDisabled)               .HasColumnName("Is_Disabled").HasMaxLength(999);
        b.Property(x => x.Visibility)               .HasColumnName("Visibility").HasMaxLength(999);
        b.Property(x => x.Formula)                  .HasColumnName("Formula").HasMaxLength(1000);
        b.Property(x => x.AllowedState)             .HasColumnName("Allowed_State").HasMaxLength(200);
        b.Property(x => x.DefaultValue)             .HasColumnName("Default_Value").HasMaxLength(200);

        b.Property(x => x.LocaleFieldLabel)         .HasColumnName("Locale_Field_Label").HasMaxLength(200);
        b.Property(x => x.LocaleLabel)              .HasColumnName("Locale_Label");
        b.Property(x => x.LocaleId)                 .HasColumnName("Locale_ID");

        b.Property(x => x.CreatedBy)                .HasColumnName("Created_By").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)              .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)            .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)          .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.ProductId, x.TabId });
        b.HasIndex(x => x.LocaleId);
    }
}
