using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class PasswordChangeAuditConfiguration : IEntityTypeConfiguration<PasswordChangeAudit>
{
    public void Configure(EntityTypeBuilder<PasswordChangeAudit> b)
    {
        b.ToTable("Password_Change_Audit_Trail", "dbo");
        b.HasKey(a => a.AuditTrailId);

        b.Property(a => a.AuditTrailId)         .HasColumnName("Audit_Trail_ID")        .ValueGeneratedOnAdd();
        b.Property(a => a.UserId)               .HasColumnName("User_ID")               .IsRequired();
        b.Property(a => a.PasswordHash)         .HasColumnName("Password_Hash");
        b.Property(a => a.CreatedBy)            .HasColumnName("Created_By");
        b.Property(a => a.CreatedDate)          .HasColumnName("Created_Date")          .IsRequired();
        b.Property(a => a.SourceAuditTrailId)   .HasColumnName("Source_Audit_Trail_ID");
        b.Property(a => a.MigratedFromAspNetId) .HasColumnName("Migrated_From_AspNet_Id");
    }
}
