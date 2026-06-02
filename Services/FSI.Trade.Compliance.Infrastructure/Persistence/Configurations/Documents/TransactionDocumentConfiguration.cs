using FSI.Trade.Compliance.Domain.Entities.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Documents;

public class TransactionDocumentConfiguration : IEntityTypeConfiguration<TransactionDocument>
{
    public void Configure(EntityTypeBuilder<TransactionDocument> b)
    {
        b.ToTable("TmX_Transaction_Document", "dbo");
        b.HasKey(x => x.TransactionDocumentId);

        b.Property(x => x.TransactionDocumentId).HasColumnName("Transaction_Document_ID");
        b.Property(x => x.TransactionId)        .HasColumnName("Transaction_ID");
        b.Property(x => x.DocumentId)           .HasColumnName("Document_ID");
        b.Property(x => x.TenantId)             .HasColumnName("Tenant_ID");
        b.Property(x => x.ActiveFlag)           .HasColumnName("Active_Flag");
        b.Property(x => x.CreatedBy)            .HasColumnName("Created_By").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)          .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)        .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)      .HasColumnName("Last_Updated_Date");

        // Matches UX_TmX_Transaction_Document_TxnDoc — one row per (txn, doc).
        b.HasIndex(x => new { x.TransactionId, x.DocumentId }).IsUnique();
        b.HasIndex(x => x.TransactionId);
    }
}
