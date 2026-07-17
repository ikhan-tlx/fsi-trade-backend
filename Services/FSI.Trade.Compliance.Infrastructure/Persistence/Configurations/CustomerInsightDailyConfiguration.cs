using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class CustomerInsightDailyConfiguration : IEntityTypeConfiguration<CustomerInsightDaily>
{
    public void Configure(EntityTypeBuilder<CustomerInsightDaily> b)
    {
        b.ToTable("TmX_Customer_Insight_Daily", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)              .HasColumnName("Customer_Insight_Daily_Id");
        b.Property(x => x.SnapshotDate)    .HasColumnName("Snapshot_Date")   .HasColumnType("date").IsRequired();
        b.Property(x => x.TotalCustomers)  .HasColumnName("Total_Customers");
        b.Property(x => x.HighRisk)        .HasColumnName("High_Risk");
        b.Property(x => x.ReviewsOverdue)  .HasColumnName("Reviews_Overdue");
        b.Property(x => x.OpenCases)       .HasColumnName("Open_Cases");
        b.Property(x => x.FlagsRaised7d)   .HasColumnName("Flags_Raised_7d");
        b.Property(x => x.CreatedBy)       .HasColumnName("Created_By")      .HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)     .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedDate) .HasColumnName("Last_Updated_Date");

        // One row per calendar day (book-wide) — the upsert key.
        b.HasIndex(x => x.SnapshotDate).IsUnique();
    }
}
