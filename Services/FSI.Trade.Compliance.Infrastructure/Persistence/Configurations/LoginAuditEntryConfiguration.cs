using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class LoginAuditEntryConfiguration : IEntityTypeConfiguration<LoginAuditEntry>
{
    public void Configure(EntityTypeBuilder<LoginAuditEntry> b)
    {
        b.ToTable("TmX_Login_Audit", "dbo");
        b.HasKey(a => a.AuditId);

        b.Property(a => a.AuditId)         .HasColumnName("Audit_ID")        .ValueGeneratedOnAdd();
        b.Property(a => a.UserId)          .HasColumnName("User_ID");
        b.Property(a => a.UsernameAttempt) .HasColumnName("Username_Attempt");
        b.Property(a => a.DeviceId)        .HasColumnName("Device_ID");
        b.Property(a => a.IpAddress)       .HasColumnName("Ip_Address");
        b.Property(a => a.UserAgent)       .HasColumnName("User_Agent");
        b.Property(a => a.Action)          .HasColumnName("Action")          .IsRequired();
        b.Property(a => a.Result)          .HasColumnName("Result")          .IsRequired();
        b.Property(a => a.Detail)          .HasColumnName("Detail");
        b.Property(a => a.CreatedAt)       .HasColumnName("Created_At")      .IsRequired();
    }
}
