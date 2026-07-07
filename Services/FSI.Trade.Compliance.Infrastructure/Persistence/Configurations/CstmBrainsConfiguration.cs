using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class CstmBrainsConfiguration : IEntityTypeConfiguration<CstmBrains>
{
    public void Configure(EntityTypeBuilder<CstmBrains> b)
    {
        b.ToTable("Cstm_Brains", "dbo");

        // The legacy table has no declared PK; Transaction_ID is the natural
        // one-row-per-transaction key. It is app-assigned, never store-generated.
        b.HasKey(x => x.TransactionId);
        b.Property(x => x.TransactionId).HasColumnName("Transaction_ID").ValueGeneratedNever();

        b.Property(x => x.CustomerNo)  .HasColumnName("Customer_No")  .HasMaxLength(1000);
        b.Property(x => x.FccmScore)   .HasColumnName("FCCM_Score")   .HasMaxLength(100);
        b.Property(x => x.BrainsScore) .HasColumnName("Brains_Score") .HasMaxLength(100);
        b.Property(x => x.FinalScore)  .HasColumnName("Final_Score")  .HasMaxLength(100);
        b.Property(x => x.Display)     .HasColumnName("Display")      .HasMaxLength(100);
    }
}
