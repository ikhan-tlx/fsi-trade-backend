using FSI.Trade.Compliance.Domain.Entities.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Reference;

public class ReferenceValueTypeMapConfiguration : IEntityTypeConfiguration<ReferenceValueTypeMap>
{
    public void Configure(EntityTypeBuilder<ReferenceValueTypeMap> b)
    {
        b.ToTable("TmX_Reference_Value_Type_Map", "dbo");
        b.HasKey(x => x.MapId);

        b.Property(x => x.MapId)           .HasColumnName("Map_Id");
        b.Property(x => x.ReferenceValueId).HasColumnName("Reference_Value_Id");
        b.Property(x => x.ReferenceTypeId) .HasColumnName("Reference_Type_Id");
        b.Property(x => x.ActiveFlag)      .HasColumnName("Active_Flag");
        b.Property(x => x.CreatedBy)       .HasColumnName("Created_By").HasMaxLength(100);
        b.Property(x => x.CreatedDate)     .HasColumnName("Created_Date");

        b.HasIndex(x => new { x.ReferenceValueId, x.ReferenceTypeId }).IsUnique();
        b.HasIndex(x => new { x.ReferenceTypeId, x.ActiveFlag });
    }
}
