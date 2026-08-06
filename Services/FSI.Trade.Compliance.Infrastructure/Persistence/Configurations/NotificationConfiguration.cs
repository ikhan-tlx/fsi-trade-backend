using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("TmX_Notification", "dbo");
        b.HasKey(x => x.NotificationId);

        b.Property(x => x.NotificationId) .HasColumnName("Notification_ID");
        b.Property(x => x.TenantId)       .HasColumnName("Tenant_ID");
        b.Property(x => x.RecipientUser)  .HasColumnName("Recipient_User");
        b.Property(x => x.TypeCode)       .HasColumnName("Type_Code");
        b.Property(x => x.Severity)       .HasColumnName("Severity");
        b.Property(x => x.Title)          .HasColumnName("Title");
        b.Property(x => x.Body)           .HasColumnName("Body");
        b.Property(x => x.EntityType)     .HasColumnName("Entity_Type");
        b.Property(x => x.EntityId)       .HasColumnName("Entity_Id");
        b.Property(x => x.DeepLink)       .HasColumnName("Deep_Link");
        b.Property(x => x.IsRead)         .HasColumnName("Is_Read");
        b.Property(x => x.ReadDate)       .HasColumnName("Read_Date");
        b.Property(x => x.GroupKey)       .HasColumnName("Group_Key");
        b.Property(x => x.CreatedBy)      .HasColumnName("Created_By");
        b.Property(x => x.CreatedDate)    .HasColumnName("Created_Date");

        b.HasIndex(x => new { x.RecipientUser, x.IsRead, x.CreatedDate });
    }
}
