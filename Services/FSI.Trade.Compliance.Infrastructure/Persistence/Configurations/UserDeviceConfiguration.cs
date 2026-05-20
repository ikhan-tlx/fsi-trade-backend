using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> b)
    {
        // NOT "TmX_User_Device" — that's a pre-existing legacy junction table
        // with a different schema (int columns). Our auth-scoped device
        // registry lives in [dbo].[UserDevices].
        b.ToTable("UserDevices", "dbo");
        b.HasKey(d => d.DeviceId);

        b.Property(d => d.DeviceId)     .HasColumnName("Device_ID")     .IsRequired();
        b.Property(d => d.UserId)       .HasColumnName("User_ID")       .IsRequired();
        b.Property(d => d.Label)        .HasColumnName("Label");
        b.Property(d => d.UserAgent)    .HasColumnName("User_Agent");
        b.Property(d => d.FirstSeenAt)  .HasColumnName("First_Seen_At") .IsRequired();
        b.Property(d => d.LastSeenAt)   .HasColumnName("Last_Seen_At")  .IsRequired();
        b.Property(d => d.FirstSeenIp)  .HasColumnName("First_Seen_Ip");
        b.Property(d => d.LastSeenIp)   .HasColumnName("Last_Seen_Ip");
        b.Property(d => d.IsTrusted)    .HasColumnName("Is_Trusted")    .IsRequired();
        b.Property(d => d.RevokedAt)    .HasColumnName("Revoked_At");
        b.Property(d => d.RevokeReason) .HasColumnName("Revoke_Reason");

        b.Ignore(d => d.IsActive);
        b.HasIndex(d => d.UserId);
    }
}
