using FSI.Trade.Compliance.Domain.Entities.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Reference;

public class ScreeningGoodConfiguration : IEntityTypeConfiguration<ScreeningGood>
{
    public void Configure(EntityTypeBuilder<ScreeningGood> b)
    {
        // The legacy table has no primary key — map keyless, query-only.
        b.HasNoKey();
        b.ToTable("REPO_SCREENING_GOODS", "dbo");

        b.Property(x => x.GoodCode)        .HasColumnName("V_GOOD_CODE")        .HasMaxLength(50);
        b.Property(x => x.GoodName)        .HasColumnName("V_GOOD_NAME")        .HasMaxLength(3999);
        b.Property(x => x.GoodDescription) .HasColumnName("V_GOOD_DESCRIPTION") .HasMaxLength(500);
    }
}
