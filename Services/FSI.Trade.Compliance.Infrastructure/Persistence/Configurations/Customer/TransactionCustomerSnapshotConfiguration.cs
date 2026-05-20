using FSI.Trade.Compliance.Domain.Entities.Customer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Customer;

/// <summary>
/// Maps <see cref="TransactionCustomerSnapshot"/> onto the
/// <c>TmX_Customer_Master</c> table. The DB name stays as-is for back-compat
/// with everything that queries it; only the C# entity is renamed for
/// semantic accuracy.
/// </summary>
public class TransactionCustomerSnapshotConfiguration : IEntityTypeConfiguration<TransactionCustomerSnapshot>
{
    public void Configure(EntityTypeBuilder<TransactionCustomerSnapshot> b)
    {
        b.ToTable("TmX_Customer_Master", "dbo");
        b.HasKey(x => x.CustomerMasterId);

        b.Property(x => x.CustomerMasterId)          .HasColumnName("Customer_Master_Id");
        b.Property(x => x.TenantId)                  .HasColumnName("Tenant_Id");
        b.Property(x => x.TransactionId)             .HasColumnName("Transaction_Id");

        b.Property(x => x.CustomerCode)              .HasColumnName("Customer_Code").HasMaxLength(50);
        b.Property(x => x.CustomerTitle)             .HasColumnName("Customer_Title").HasMaxLength(50);
        b.Property(x => x.CustomerName)              .HasColumnName("Customer_Name").HasMaxLength(100);
        b.Property(x => x.CustomerTypeLkp)           .HasColumnName("Customer_Type_Lkp");
        b.Property(x => x.CustomerClassificationLkp) .HasColumnName("Customer_Classification_Lkp");
        b.Property(x => x.CustomerStatusLkp)         .HasColumnName("Customer_Status_Lkp");
        b.Property(x => x.NationalIdTypeLkp)         .HasColumnName("National_ID_Type_Lkp");
        b.Property(x => x.NationalIdentifierValue)   .HasColumnName("National_Identifier_Value").HasMaxLength(100);
        b.Property(x => x.CustomerSegmentLkp)        .HasColumnName("Customer_Segment_Lkp");
        b.Property(x => x.CustomerSubSegmentLkp)     .HasColumnName("Customer_Sub_Segment_Lkp");
        b.Property(x => x.EntityTypeLkp)             .HasColumnName("Entity_Type_Lkp");
        b.Property(x => x.FatcaClassLkp)             .HasColumnName("FATCA_Class_Lkp");
        b.Property(x => x.RelationshipCodeLkp)       .HasColumnName("Relationship_Code_Lkp");
        b.Property(x => x.LocationId)                .HasColumnName("Location_Id");
        b.Property(x => x.UdfData)                   .HasColumnName("udf_data");

        b.Property(x => x.CreatedBy)                 .HasColumnName("Created_By").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)               .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)             .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)           .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => x.TransactionId);
        b.HasIndex(x => x.CustomerCode);
    }
}
