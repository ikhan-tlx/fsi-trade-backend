using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class NotificationPrefConfiguration : IEntityTypeConfiguration<NotificationPref>
{
    public void Configure(EntityTypeBuilder<NotificationPref> b)
    {
        b.ToTable("TmX_Notification_Pref", "dbo");
        b.HasKey(x => x.NotificationPrefId);

        b.Property(x => x.NotificationPrefId).HasColumnName("Notification_Pref_ID");
        b.Property(x => x.UserId)            .HasColumnName("User_Id");
        b.Property(x => x.TypeCode)          .HasColumnName("Type_Code");
        b.Property(x => x.InAppEnabled)      .HasColumnName("In_App_Enabled");
        b.Property(x => x.CreatedDate)       .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedDate)   .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.UserId, x.TypeCode }).IsUnique();
    }
}
