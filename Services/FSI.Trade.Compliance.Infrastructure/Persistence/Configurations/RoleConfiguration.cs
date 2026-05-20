using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("TmX_Role", "dbo");
        b.HasKey(r => r.Id);

        b.Property(r => r.Id)                 .HasColumnName("Role_ID");
        b.Property(r => r.TenantId)           .HasColumnName("Tenant_ID");
        b.Property(r => r.Name)               .HasColumnName("Role_Name")           .HasMaxLength(100).IsRequired();
        b.Property(r => r.Description)        .HasColumnName("Role_Description")    .HasMaxLength(200);
        b.Property(r => r.IsActive)           .HasColumnName("Active_Flag");
        b.Property(r => r.EffectiveStartDate) .HasColumnName("Effective_Start_Date");
        b.Property(r => r.EffectiveEndDate)   .HasColumnName("Effective_End_Date");
        b.Property(r => r.CreatedBy)          .HasColumnName("Created_By")          .HasMaxLength(100);
        b.Property(r => r.CreatedDate)        .HasColumnName("Created_Date");
        b.Property(r => r.LastUpdatedBy)      .HasColumnName("Last_Updated_By")     .HasMaxLength(100);
        b.Property(r => r.LastUpdatedDate)    .HasColumnName("Last_Updated_Date");

        b.HasIndex(r => r.Name);
    }
}
