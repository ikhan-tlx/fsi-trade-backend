using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class CstmBrainsConfiguration : IEntityTypeConfiguration<CstmBrains>
{
    public void Configure(EntityTypeBuilder<CstmBrains> b)
    {
        b.ToTable("Cstm_Brains", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)            .HasColumnName("Id");
        b.Property(x => x.TransactionId) .HasColumnName("Transaction_ID");
        b.Property(x => x.CustomerNo)    .HasColumnName("Customer_No")  .HasMaxLength(100);
        b.Property(x => x.FccmScore)     .HasColumnName("FCCM_Score");
        b.Property(x => x.BrainsScore)   .HasColumnName("Brains_Score");
        b.Property(x => x.FinalScore)    .HasColumnName("Final_Score")  .HasMaxLength(20);
        b.Property(x => x.Display)       .HasColumnName("Display")      .HasMaxLength(60);
        b.Property(x => x.CreatedAt)     .HasColumnName("Created_At");
        b.Property(x => x.LastUpdatedAt) .HasColumnName("Last_Updated_At");

        b.HasIndex(x => x.TransactionId).IsUnique();
    }
}
