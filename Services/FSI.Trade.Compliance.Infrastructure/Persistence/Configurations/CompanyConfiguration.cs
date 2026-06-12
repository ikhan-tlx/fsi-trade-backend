using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the minimal read-only slice of <c>TmX_Company</c> needed for the
/// Company LOV (Slice 14).
/// </summary>
public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.ToTable("TmX_Company", "dbo");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id)  .HasColumnName("Company_Id");
        b.Property(c => c.Code).HasColumnName("Company_Code").HasMaxLength(100);
        b.Property(c => c.Name).HasColumnName("Company_Name").HasMaxLength(200);
    }
}
