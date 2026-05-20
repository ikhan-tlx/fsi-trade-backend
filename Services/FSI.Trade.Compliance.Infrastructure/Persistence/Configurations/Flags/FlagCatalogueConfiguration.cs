using FSI.Trade.Compliance.Domain.Entities.Flags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Flags;

public class FlagCatalogueConfiguration : IEntityTypeConfiguration<FlagCatalogue>
{
    public void Configure(EntityTypeBuilder<FlagCatalogue> b)
    {
        b.ToTable("TmX_Flag_Catalogue", "dbo");
        b.HasKey(x => x.FlagId);

        b.Property(x => x.FlagId)               .HasColumnName("Flag_ID");
        b.Property(x => x.FlagCode)             .HasColumnName("Flag_Code")            .HasMaxLength(100).IsRequired();
        b.Property(x => x.FlagName)             .HasColumnName("Flag_Name")            .HasMaxLength(200).IsRequired();
        b.Property(x => x.FlagDescription)      .HasColumnName("Flag_Description")     .IsRequired();
        b.Property(x => x.FlagTypeLkpId)        .HasColumnName("Flag_Type_Lkp_ID");
        b.Property(x => x.FlagCategoryLkpId)    .HasColumnName("Flag_Category_Lkp_ID");
        b.Property(x => x.SeverityLkpId)        .HasColumnName("Severity_Lkp_ID");
        b.Property(x => x.DefaultWeight)        .HasColumnName("Default_Weight")       .HasColumnType("decimal(8,2)");
        b.Property(x => x.RequiresEvidence)     .HasColumnName("Requires_Evidence");
        b.Property(x => x.SourceSystem)         .HasColumnName("Source_System")        .HasMaxLength(50);
        b.Property(x => x.ActiveFlag)           .HasColumnName("Active_Flag");
        b.Property(x => x.CreatedBy)            .HasColumnName("Created_By")           .HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)          .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)        .HasColumnName("Last_Updated_By")      .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)      .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => x.FlagCode).IsUnique();
    }
}
