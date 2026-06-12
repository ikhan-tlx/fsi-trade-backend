using FSI.Trade.Compliance.Domain.Entities.Flags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Flags;

public class ReferenceListConfiguration : IEntityTypeConfiguration<ReferenceList>
{
    public void Configure(EntityTypeBuilder<ReferenceList> b)
    {
        b.ToTable("TmX_Reference_List", "dbo");
        b.HasKey(x => x.ReferenceListId);

        b.Property(x => x.ReferenceListId) .HasColumnName("Reference_List_ID");
        b.Property(x => x.ListCode)        .HasColumnName("List_Code")        .HasMaxLength(60).IsRequired();
        b.Property(x => x.ListName)        .HasColumnName("List_Name")        .HasMaxLength(200).IsRequired();
        b.Property(x => x.ActiveFlag)      .HasColumnName("Active_Flag");
        b.Property(x => x.CreatedBy)       .HasColumnName("Created_By")       .HasMaxLength(100);
        b.Property(x => x.CreatedDate)     .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)   .HasColumnName("Last_Updated_By")  .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate) .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => x.ListCode).IsUnique();
    }
}
