using FSI.Trade.Compliance.Domain.Entities.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Documents;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable("TmX_Document", "dbo");
        b.HasKey(x => x.DocumentId);

        b.Property(x => x.DocumentId)            .HasColumnName("Document_ID");
        b.Property(x => x.OriginalFileName)      .HasColumnName("Original_File_Name").HasMaxLength(255).IsRequired();
        b.Property(x => x.StoredFileName)        .HasColumnName("Stored_File_Name")  .HasMaxLength(255).IsRequired();
        b.Property(x => x.MimeType)              .HasColumnName("Mime_Type")         .HasMaxLength(100);
        b.Property(x => x.FileSizeBytes)         .HasColumnName("File_Size_Bytes");
        b.Property(x => x.Sha256Hash)            .HasColumnName("Sha256_Hash")       .HasMaxLength(64).IsFixedLength();
        b.Property(x => x.StorageProviderLkpId)  .HasColumnName("Storage_Provider_Lkp_ID");
        b.Property(x => x.StorageRelativePath)   .HasColumnName("Storage_Relative_Path").HasMaxLength(500).IsRequired();
        b.Property(x => x.TenantId)              .HasColumnName("Tenant_ID");
        b.Property(x => x.ActiveFlag)            .HasColumnName("Active_Flag");
        b.Property(x => x.UploadedBy)            .HasColumnName("Uploaded_By")       .HasMaxLength(100).IsRequired();
        b.Property(x => x.UploadedDate)          .HasColumnName("Uploaded_Date");
        b.Property(x => x.CreatedBy)             .HasColumnName("Created_By")        .HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)           .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)         .HasColumnName("Last_Updated_By")   .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)       .HasColumnName("Last_Updated_Date");
    }
}
