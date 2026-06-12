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

        // NOTE: Transaction_Flag_ID is the FK to TmX_Transaction_Flag
        // (FK_TmX_Transaction_Flag_History_TransactionFlag) but is intentionally
        // left UNMAPPED as an EF relationship. New-flag history is written only
        // after the parent flag row is flushed (see TransactionFlagDiffer), so
        // the FK value is always a real, existing key — no need for EF to manage
        // the relationship, and mapping it would pull the parent's soft-delete
        // query filter into this append-only audit table.
    }
}
