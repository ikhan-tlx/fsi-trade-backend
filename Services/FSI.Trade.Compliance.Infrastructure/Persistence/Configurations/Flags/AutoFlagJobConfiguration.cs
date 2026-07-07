using FSI.Trade.Compliance.Domain.Entities.Flags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Flags;

public class AutoFlagJobConfiguration : IEntityTypeConfiguration<AutoFlagJob>
{
    public void Configure(EntityTypeBuilder<AutoFlagJob> b)
    {
        b.ToTable("TmX_AutoFlag_Job", "dbo");
        b.HasKey(x => x.JobId);

        b.Property(x => x.JobId)         .HasColumnName("Job_ID");
        b.Property(x => x.TransactionId) .HasColumnName("Transaction_ID");
        b.Property(x => x.CustomerCode)  .HasColumnName("Customer_Code")  .HasMaxLength(64);
        b.Property(x => x.TriggerType)   .HasColumnName("Trigger_Type")   .HasMaxLength(20).IsRequired();
        b.Property(x => x.Status)        .HasColumnName("Status")         .HasMaxLength(20).IsRequired();
        b.Property(x => x.Attempts)      .HasColumnName("Attempts");
        b.Property(x => x.Error)         .HasColumnName("Error");
        b.Property(x => x.EnqueuedDate)  .HasColumnName("Enqueued_Date");
        b.Property(x => x.ClaimedDate)   .HasColumnName("Claimed_Date");
        b.Property(x => x.ProcessedDate) .HasColumnName("Processed_Date");

        b.HasIndex(x => new { x.Status, x.EnqueuedDate });
    }
}
