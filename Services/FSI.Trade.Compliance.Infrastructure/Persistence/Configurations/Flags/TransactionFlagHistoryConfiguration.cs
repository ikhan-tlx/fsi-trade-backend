using FSI.Trade.Compliance.Domain.Entities.Flags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Flags;

public class TransactionFlagHistoryConfiguration : IEntityTypeConfiguration<TransactionFlagHistory>
{
    public void Configure(EntityTypeBuilder<TransactionFlagHistory> b)
    {
        b.ToTable("TmX_Transaction_Flag_History", "dbo");
        b.HasKey(x => x.TransactionFlagHistoryId);

        b.Property(x => x.TransactionFlagHistoryId)     .HasColumnName("Transaction_Flag_History_ID");
        b.Property(x => x.TransactionFlagId)            .HasColumnName("Transaction_Flag_ID");
        b.Property(x => x.TransactionId)                .HasColumnName("Transaction_ID");
        b.Property(x => x.FlagId)                       .HasColumnName("Flag_ID");
        b.Property(x => x.ChangeTypeLkpId)              .HasColumnName("Change_Type_Lkp_ID");
        b.Property(x => x.PreviousIsFlagged)            .HasColumnName("Previous_Is_Flagged");
        b.Property(x => x.NewIsFlagged)                 .HasColumnName("New_Is_Flagged");
        b.Property(x => x.PreviousNotes)                .HasColumnName("Previous_Notes");
        b.Property(x => x.NewNotes)                     .HasColumnName("New_Notes");
        b.Property(x => x.PreviousEvidenceDocumentId)   .HasColumnName("Previous_Evidence_Document_ID");
        b.Property(x => x.NewEvidenceDocumentId)        .HasColumnName("New_Evidence_Document_ID");
        b.Property(x => x.ChangedBy)                    .HasColumnName("Changed_By")  .HasMaxLength(100).IsRequired();
        b.Property(x => x.ChangedDate)                  .HasColumnName("Changed_Date");

        b.HasIndex(x => new { x.TransactionId, x.ChangedDate });
        b.HasIndex(x => new { x.FlagId, x.ChangedDate });
        b.HasIndex(x => new { x.ChangedBy, x.ChangedDate });
    }
}
