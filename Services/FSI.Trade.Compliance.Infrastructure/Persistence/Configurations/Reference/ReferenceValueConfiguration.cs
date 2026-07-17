using FSI.Trade.Compliance.Domain.Entities.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Reference;

public class ReferenceValueConfiguration : IEntityTypeConfiguration<ReferenceValue>
{
    public void Configure(EntityTypeBuilder<ReferenceValue> b)
    {
        b.ToTable("TmX_Reference_Value", "dbo");
        b.HasKey(x => x.ReferenceValueId);

        b.Property(x => x.ReferenceValueId).HasColumnName("Reference_Value_Id");
        b.Property(x => x.Code)           .HasColumnName("Code")        .HasMaxLength(100).IsRequired();
        b.Property(x => x.Name)           .HasColumnName("Name")        .HasMaxLength(4000);
        b.Property(x => x.Description)    .HasColumnName("Description") .HasMaxLength(1000);
        b.Property(x => x.LookupType)     .HasColumnName("Lookup_Type") .HasMaxLength(100);
        b.Property(x => x.UdfData)        .HasColumnName("Udf_Data");
        b.Property(x => x.ActiveFlag)     .HasColumnName("Active_Flag");
        b.Property(x => x.CreatedBy)      .HasColumnName("Created_By")  .HasMaxLength(100);
        b.Property(x => x.CreatedDate)    .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)  .HasColumnName("Last_Updated_By")  .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate).HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.LookupType, x.Code });
    }
}
