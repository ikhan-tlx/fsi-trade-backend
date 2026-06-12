using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="UserHoliday"/> onto <c>TmX_User_Holiday</c>. Slice 14.</summary>
public class UserHolidayConfiguration : IEntityTypeConfiguration<UserHoliday>
{
    public void Configure(EntityTypeBuilder<UserHoliday> b)
    {
        b.ToTable("TmX_User_Holiday", "dbo");
        b.HasKey(x => x.UserHolidayId);
        b.Property(x => x.UserHolidayId)    .HasColumnName("User_Holiday_ID");
        b.Property(x => x.UserId)           .HasColumnName("User_ID").HasMaxLength(50).IsRequired();
        b.Property(x => x.HolidayStartDate) .HasColumnName("Holiday_Start_Date");
        b.Property(x => x.HolidayEndDate)   .HasColumnName("Holiday_End_Date");
        b.Property(x => x.IsProcessed)      .HasColumnName("Is_Processed");
        b.Property(x => x.IsActive)         .HasColumnName("Is_Active");
        b.Property(x => x.CreatedBy)        .HasColumnName("Created_By").HasMaxLength(100);
        b.Property(x => x.CreatedDate)      .HasColumnName("Created_Date");
        b.HasIndex(x => x.UserId);
    }
}
