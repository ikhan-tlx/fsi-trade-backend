using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class RolePrivilegeMappingConfiguration : IEntityTypeConfiguration<RolePrivilegeMapping>
{
    public void Configure(EntityTypeBuilder<RolePrivilegeMapping> b)
    {
        b.ToTable("TmX_Role_Privilege_Mapping", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)                 .HasColumnName("Role_Privilege_Mapping_ID");
        b.Property(x => x.TenantId)           .HasColumnName("Tenant_ID");
        b.Property(x => x.RoleId)             .HasColumnName("Role_ID");
        b.Property(x => x.PrivilegeId)        .HasColumnName("Privilege_ID");
        b.Property(x => x.IsActive)           .HasColumnName("Active_Flag");
        b.Property(x => x.EffectiveStartDate) .HasColumnName("Effective_Start_Date");
        b.Property(x => x.EffectiveEndDate)   .HasColumnName("Effective_End_Date");
        b.Property(x => x.CreatedBy)          .HasColumnName("Created_By")        .HasMaxLength(100);
        b.Property(x => x.CreatedDate)        .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)      .HasColumnName("Last_Updated_By")   .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)    .HasColumnName("Last_Updated_Date");

        // Hot read paths.
        b.HasIndex(x => new { x.RoleId, x.PrivilegeId });
        b.HasIndex(x => x.PrivilegeId);
    }
}
