using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class UserRoleMappingConfiguration : IEntityTypeConfiguration<UserRoleMapping>
{
    public void Configure(EntityTypeBuilder<UserRoleMapping> b)
    {
        b.ToTable("TmX_User_Role_Mapping", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)                .HasColumnName("User_Role_Mapping_Id");
        b.Property(x => x.TenantId)          .HasColumnName("Tenant_Id");
        b.Property(x => x.UserId)            .HasColumnName("User_Id").IsRequired().HasMaxLength(50);
        b.Property(x => x.RoleId)            .HasColumnName("Role_Id");
        b.Property(x => x.IsActive)          .HasColumnName("Active_Flag");
        b.Property(x => x.EffectiveStartDate).HasColumnName("Effective_Start_Date");
        b.Property(x => x.EffectiveEndDate)  .HasColumnName("Effective_End_Date");
        b.Property(x => x.CreatedBy)         .HasColumnName("Created_By")       .HasMaxLength(100);
        b.Property(x => x.CreatedDate)       .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)     .HasColumnName("Last_Updated_By")  .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)   .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => x.UserId);
        b.HasIndex(x => new { x.UserId, x.RoleId });
    }
}
