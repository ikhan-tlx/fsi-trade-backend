using FSI.Trade.Compliance.Domain.Entities.Flags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Flags;

public class TransactionFlagConfiguration : IEntityTypeConfiguration<TransactionFlag>
{
    public void Configure(EntityTypeBuilder<TransactionFlag> b)
    {
        b.ToTable("TmX_Transaction_Flag", "dbo");
        b.HasKey(x => x.TransactionFlagId);

        b.Property(x => x.TransactionFlagId)    .HasColumnName("Transaction_Flag_ID");
        b.Property(x => x.TransactionId)        .HasColumnName("Transaction_ID");
        b.Property(x => x.FlagId)               .HasColumnName("Flag_ID");
        b.Property(x => x.IsFlagged)            .HasColumnName("Is_Flagged");
        b.Property(x => x.EvidenceDocumentId)   .HasColumnName("Evidence_Document_ID");
        b.Property(x => x.AnalystNotes)         .HasColumnName("Analyst_Notes");
        b.Property(x => x.SetBy)                .HasColumnName("Set_By")            .HasMaxLength(100).IsRequired();
        b.Property(x => x.SetDate)              .HasColumnName("Set_Date");
        b.Property(x => x.CreatedBy)            .HasColumnName("Created_By")        .HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)          .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)        .HasColumnName("Last_Updated_By")   .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)      .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.TransactionId, x.FlagId }).IsUnique();
        b.HasIndex(x => new { x.FlagId, x.IsFlagged });
    }
}
