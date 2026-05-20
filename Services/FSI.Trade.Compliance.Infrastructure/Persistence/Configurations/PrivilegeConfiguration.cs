using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class PrivilegeConfiguration : IEntityTypeConfiguration<Privilege>
{
    public void Configure(EntityTypeBuilder<Privilege> b)
    {
        b.ToTable("TmX_Privilege", "dbo");
        b.HasKey(p => p.Id);

        b.Property(p => p.Id)         .HasColumnName("Privilege_ID");
        b.Property(p => p.TenantId)   .HasColumnName("Tenant_ID");
        b.Property(p => p.Name)       .HasColumnName("Privilege_Name")       .HasMaxLength(100);
        b.Property(p => p.Description).HasColumnName("Privilege_Description").HasMaxLength(100);

        // Slice 2 read path filters by name; an index on the column accelerates
        // both the [RequiresPrivilege] resolver and the seed migration's IF NOT EXISTS guard.
        b.HasIndex(p => p.Name);
    }
}
