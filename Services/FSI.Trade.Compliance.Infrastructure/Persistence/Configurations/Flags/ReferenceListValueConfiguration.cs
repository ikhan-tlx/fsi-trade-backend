using FSI.Trade.Compliance.Domain.Entities.Flags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Flags;

public class ReferenceListValueConfiguration : IEntityTypeConfiguration<ReferenceListValue>
{
    public void Configure(EntityTypeBuilder<ReferenceListValue> b)
    {
        b.ToTable("TmX_Reference_List_Value", "dbo");
        b.HasKey(x => x.ReferenceListValueId);

        b.Property(x => x.ReferenceListValueId).HasColumnName("Reference_List_Value_ID");
        b.Property(x => x.ReferenceListId)     .HasColumnName("Reference_List_ID");
        b.Property(x => x.Value)               .HasColumnName("Value")       .HasMaxLength(200).IsRequired();
        b.Property(x => x.ValueType)           .HasColumnName("Value_Type")  .HasMaxLength(40);
        b.Property(x => x.ActiveFlag)          .HasColumnName("Active_Flag");
        b.Property(x => x.CreatedBy)           .HasColumnName("Created_By")  .HasMaxLength(100);
        b.Property(x => x.CreatedDate)         .HasColumnName("Created_Date");

        b.HasIndex(x => new { x.ReferenceListId, x.ActiveFlag });
    }
}
