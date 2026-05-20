using FSI.Trade.Compliance.Domain.Entities.Flags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Flags;

public class FlagCatalogueLocaleConfiguration : IEntityTypeConfiguration<FlagCatalogueLocale>
{
    public void Configure(EntityTypeBuilder<FlagCatalogueLocale> b)
    {
        b.ToTable("TmX_Flag_Catalogue_Locale", "dbo");
        b.HasKey(x => x.FlagCatalogueLocaleId);

        b.Property(x => x.FlagCatalogueLocaleId).HasColumnName("Flag_Catalogue_Locale_ID");
        b.Property(x => x.FlagId)               .HasColumnName("Flag_ID");
        b.Property(x => x.LocaleId)             .HasColumnName("Locale_ID");
        b.Property(x => x.LocaleName)           .HasColumnName("Locale_Name")        .HasMaxLength(200);
        b.Property(x => x.LocaleDescription)    .HasColumnName("Locale_Description");
        b.Property(x => x.CreatedBy)            .HasColumnName("Created_By")         .HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)          .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)        .HasColumnName("Last_Updated_By")    .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)      .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.FlagId, x.LocaleId }).IsUnique();
    }
}
