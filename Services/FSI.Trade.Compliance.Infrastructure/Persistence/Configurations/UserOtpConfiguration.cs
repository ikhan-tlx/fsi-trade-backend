using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class UserOtpConfiguration : IEntityTypeConfiguration<UserOtp>
{
    public void Configure(EntityTypeBuilder<UserOtp> b)
    {
        b.ToTable("TmX_User_Otp");
        b.HasKey(x => x.OtpId);
        b.Property(x => x.OtpId).HasColumnName("Otp_Id");
        b.Property(x => x.UserId).HasColumnName("User_Id");
        b.Property(x => x.Purpose).HasColumnName("Purpose");
        b.Property(x => x.OtpHash).HasColumnName("Otp_Hash");
        b.Property(x => x.ExpiresAt).HasColumnName("Expires_At");
        b.Property(x => x.Attempts).HasColumnName("Attempts");
        b.Property(x => x.MaxAttempts).HasColumnName("Max_Attempts");
        b.Property(x => x.SentAt).HasColumnName("Sent_At");
        b.Property(x => x.ConsumedAt).HasColumnName("Consumed_At");
        b.Property(x => x.CreatedDate).HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedDate).HasColumnName("Last_Updated_Date");
    }
}
