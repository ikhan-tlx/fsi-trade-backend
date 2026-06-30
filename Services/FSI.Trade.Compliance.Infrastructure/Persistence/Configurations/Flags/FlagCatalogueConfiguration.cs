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
        b.Property(x => x.SeverityLkpId)        .HasColumnName("Severity_Lkp_ID");
        b.Property(x => x.DefaultWeight)        .HasColumnName("Default_Weight")       .HasColumnType("decimal(8,2)");
        b.Property(x => x.RequiresEvidence)     .HasColumnName("Requires_Evidence");
        b.Property(x => x.RaisePerMatch)        .HasColumnName("Raise_Per_Match");
        b.Property(x => x.SourceSystem)         .HasColumnName("Source_System")        .HasMaxLength(50);
        b.Property(x => x.ActiveFlag)           .HasColumnName("Active_Flag");
        b.Property(x => x.IsBuiltIn)            .HasColumnName("Is_Built_In");
        b.Property(x => x.IsDeleted)            .HasColumnName("Is_Deleted");
        b.Property(x => x.DeletedBy)            .HasColumnName("Deleted_By")           .HasMaxLength(100);
        b.Property(x => x.DeletedDate)          .HasColumnName("Deleted_Date");
        b.Property(x => x.CreatedBy)            .HasColumnName("Created_By")           .HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)          .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)        .HasColumnName("Last_Updated_By")      .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)      .HasColumnName("Last_Updated_Date");

        // Auto-flag maker-checker lifecycle (2026_06_059)
        b.Property(x => x.Status)               .HasColumnName("Status")               .HasMaxLength(20);
        b.Property(x => x.VersionNo)            .HasColumnName("Version_No");
        b.Property(x => x.SubmittedBy)          .HasColumnName("Submitted_By")         .HasMaxLength(100);
        b.Property(x => x.SubmittedDate)        .HasColumnName("Submitted_Date");
        b.Property(x => x.ApprovedBy)           .HasColumnName("Approved_By")          .HasMaxLength(100);
        b.Property(x => x.ApprovedDate)         .HasColumnName("Approved_Date");
        b.Property(x => x.ChangeReason)         .HasColumnName("Change_Reason")        .HasMaxLength(1000);

        b.HasIndex(x => x.FlagCode).IsUnique();
    }
}
