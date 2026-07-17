using FSI.Trade.Compliance.Domain.Entities.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Reference;

public class ReferenceTypeConfiguration : IEntityTypeConfiguration<ReferenceType>
{
    public void Configure(EntityTypeBuilder<ReferenceType> b)
    {
        b.ToTable("TmX_Reference_Type", "dbo");
        b.HasKey(x => x.ReferenceTypeId);

        b.Property(x => x.ReferenceTypeId).HasColumnName("Reference_Type_Id");
        b.Property(x => x.TypeCode)       .HasColumnName("Type_Code")   .HasMaxLength(80).IsRequired();
        b.Property(x => x.TypeName)       .HasColumnName("Type_Name")   .HasMaxLength(200).IsRequired();
        b.Property(x => x.Description)    .HasColumnName("Description")  .HasMaxLength(1000);
        b.Property(x => x.LookupType)     .HasColumnName("Lookup_Type")  .HasMaxLength(100);
        b.Property(x => x.UdfSchema)      .HasColumnName("Udf_Schema");
        b.Property(x => x.ActiveFlag)     .HasColumnName("Active_Flag");
        b.Property(x => x.Source)         .HasColumnName("Source")       .HasMaxLength(20);
        b.Property(x => x.CreatedBy)      .HasColumnName("Created_By")   .HasMaxLength(100);
        b.Property(x => x.CreatedDate)    .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)  .HasColumnName("Last_Updated_By")  .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate).HasColumnName("Last_Updated_Date");

        b.HasIndex(x => x.TypeCode).IsUnique();
    }
}
