using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens", "dbo");
        b.HasKey(t => t.Id);

        b.Property(t => t.Id)            .HasColumnName("Id")            .IsRequired();
        b.Property(t => t.UserId)        .HasColumnName("User_ID")       .IsRequired();
        b.Property(t => t.DeviceId)      .HasColumnName("Device_ID");
        b.Property(t => t.IssuedAt)      .HasColumnName("Issued_At")     .IsRequired();
        b.Property(t => t.ExpiresAt)     .HasColumnName("Expires_At")    .IsRequired();
        b.Property(t => t.RevokedAt)     .HasColumnName("Revoked_At");
        b.Property(t => t.ReplacedBy)    .HasColumnName("Replaced_By");
        b.Property(t => t.RevokeReason)  .HasColumnName("Revoke_Reason");
        b.Property(t => t.CreatedByIp)   .HasColumnName("Created_By_Ip");
        b.Property(t => t.RevokedByIp)   .HasColumnName("Revoked_By_Ip");

        b.Ignore(t => t.IsActive);

        b.HasIndex(t => t.UserId);
        b.HasIndex(t => t.DeviceId);
    }
}
