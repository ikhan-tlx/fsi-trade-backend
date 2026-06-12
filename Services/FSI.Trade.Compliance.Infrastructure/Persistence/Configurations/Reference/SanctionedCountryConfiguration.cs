using FSI.Trade.Compliance.Domain.Entities.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Reference;

public class SanctionedCountryConfiguration : IEntityTypeConfiguration<SanctionedCountry>
{
    public void Configure(EntityTypeBuilder<SanctionedCountry> b)
    {
        b.ToTable("REPO_SANCTIONED_COUNTRIES", "dbo");
        b.HasKey(x => x.RecordId);

        b.Property(x => x.RecordId)        .HasColumnName("V_RECORD_ID");
        b.Property(x => x.CountryName)     .HasColumnName("V_COUNTRY_NAME")      .HasMaxLength(50);
        b.Property(x => x.IsoCountryCode)  .HasColumnName("V_ISO_COUNTRY_CODE")  .HasMaxLength(50);
        b.Property(x => x.Iso3CountryCode) .HasColumnName("V_ISO3_COUNTRY_CODE") .HasMaxLength(50);
        b.Property(x => x.CountrySynonyms) .HasColumnName("V_COUNTRY_SYNONYMS")  .HasMaxLength(50);
        b.Property(x => x.DataSource)      .HasColumnName("V_DATA_SOURCE")       .HasMaxLength(50);
        b.Property(x => x.RiskScore)       .HasColumnName("N_RISK_SCORE");
    }
}
