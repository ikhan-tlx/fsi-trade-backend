using FSI.Trade.Compliance.Domain.Entities.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Application;

public class ApplicationChecklistConfiguration : IEntityTypeConfiguration<ApplicationChecklist>
{
    public void Configure(EntityTypeBuilder<ApplicationChecklist> b)
    {
        b.ToTable("TmX_Application_Checklist", "dbo");
        b.HasKey(x => x.ApplicationChecklistId);

        b.Property(x => x.ApplicationChecklistId) .HasColumnName("Application_Checklist_ID");
        b.Property(x => x.TransactionId)          .HasColumnName("Transaction_ID");
        b.Property(x => x.ModuleCode)             .HasColumnName("Module_Code").HasMaxLength(10).IsRequired();
        b.Property(x => x.TenantId)               .HasColumnName("Tenant_ID");
        b.Property(x => x.ActiveFlag)             .HasColumnName("Active_Flag");
        b.Property(x => x.ChecklistTypeLkp)       .HasColumnName("Checklist_Type_Lkp");
        b.Property(x => x.AttachmentUrl)          .HasColumnName("Attachment_URL").HasMaxLength(500);
        b.Property(x => x.ImageData)              .HasColumnName("Image_Data");
        b.Property(x => x.VerificationRequired)   .HasColumnName("Verification_Required");
        b.Property(x => x.VerificationOutcomeLkp) .HasColumnName("Verification_Outcome_Lkp");
        b.Property(x => x.LocationId)             .HasColumnName("Location_ID");
        b.Property(x => x.UserId)                 .HasColumnName("User_ID").HasMaxLength(50);
        b.Property(x => x.MobileId)               .HasColumnName("Mobile_ID");
        b.Property(x => x.TabId)                  .HasColumnName("Tab_ID");

        b.Property(x => x.CreatedBy)              .HasColumnName("Created_By").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)            .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)          .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)        .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.TransactionId, x.ModuleCode });
    }
}
