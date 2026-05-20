using FSI.Trade.Compliance.Domain.Entities.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Forms;

public class TabConfiguration : IEntityTypeConfiguration<Tab>
{
    public void Configure(EntityTypeBuilder<Tab> b)
    {
        b.ToTable("TmX_Tab", "dbo");
        b.HasKey(x => x.TabId);

        b.Property(x => x.TabId)              .HasColumnName("Tab_Id");
        b.Property(x => x.TabName)            .HasColumnName("Tab_Name").HasMaxLength(100);
        b.Property(x => x.Description)        .HasColumnName("Description").HasMaxLength(500);
        b.Property(x => x.HiddenValue)        .HasColumnName("Hidden_Value").HasMaxLength(100);
        b.Property(x => x.TenantId)           .HasColumnName("Tenant_ID");
        b.Property(x => x.ActiveFlag)         .HasColumnName("Active_Flag");
        b.Property(x => x.LocaleLabel)        .HasColumnName("Locale_Label");
        b.Property(x => x.LocaleId)           .HasColumnName("Locale_ID");
        b.Property(x => x.EffectiveStartDate) .HasColumnName("Effective_Start_Date");
        b.Property(x => x.EffectiveEndDate)   .HasColumnName("Effective_End_Date");
        b.Property(x => x.CreatedBy)          .HasColumnName("Created_By").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)        .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)      .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)    .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => x.LocaleId);
    }
}
