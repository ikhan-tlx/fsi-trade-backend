using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class LookupTypeRegistrationConfiguration : IEntityTypeConfiguration<LookupTypeRegistration>
{
    public void Configure(EntityTypeBuilder<LookupTypeRegistration> b)
    {
        b.ToTable("TmX_Lookup_Type", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)          .HasColumnName("Lookup_Type_Id");
        b.Property(x => x.LookupType)  .HasColumnName("Lookup_Type").HasMaxLength(100).IsRequired();
        b.Property(x => x.IsManageable).HasColumnName("Is_Manageable");
        b.Property(x => x.IsSystem)    .HasColumnName("Is_System");
        b.Property(x => x.CreatedBy)   .HasColumnName("Created_By").HasMaxLength(200);
        b.Property(x => x.CreatedDate) .HasColumnName("Created_Date");

        b.HasIndex(x => x.LookupType).IsUnique();
    }
}
